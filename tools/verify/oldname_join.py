"""Register [PIPE-PER-PROCESS-2026-11-06] item 6: a 3707 copy whose first instance joins a name a 3695 copy made.

    py tools/verify/oldname_join.py --pid P [--old out/stageA/copy3695/UE5Dumper.dll] [--clients 3]

WHY A DEBUGGER. No shipped path makes a newer copy join a live name: every start probes first and defers or skips, so
the join needs the older copy to create the name in the instant between the newer copy's probe and its own
CreateNamedPipeW. A race between two remote threads started from python.exe would stage it, and the AV flags exactly
that (working-lessons.md 3.8). This holds the newer copy still instead: Cheat Engine's VEH debugger breaks its accept
thread ON ITS OWN CreateNamedPipeW call site (an address only that module's code reaches), the breakpoint's handler
starts the older copy's pipe server, waits, and lets the newer copy go on. Deterministic, no race; every remote thread
is Cheat Engine's (executeCodeEx), which is how the .CT drives the DLL anyway.

PRECONDITIONS (checked, not assumed):
  * game P runs the dist DLL (the newer copy) and serves its pipe;
  * the older copy is NOT loaded yet: this injects it, and its auto-start must settle READY beside the newer copy's
    server ("another copy ... already serves": idle, no claim watcher). It cannot write its logs while the newer copy
    holds the files (item 3), so this is read from its own mailbox, and the name must stay gone 3 s after the newer
    copy's stop;
  * ONE Cheat Engine runs with the AOBMaker plugin (its bridge is how the Lua runs). The rig opens P in it and starts
    its VEH debugger (debugProcess(2)) -- the Windows debugger would freeze every thread at the break, the older
    copy's start included.

STEPS: inject the older copy; stop the newer copy's server (UE5_StopPipeServer) and see the name gone; arm the
breakpoint and start the newer copy's server (UE5_StartPipeServer: its probe finds the name gone, its accept thread
breaks before creating); the handler calls the older copy's UE5_StartPipeServer, which creates the name with ITS limit
(3); then the newer copy creates its first instance on that name. Then --clients connections are held, so the newer
copy meets the limit.

PASS (exit 0): pipe-0.log, after the run's start, has the newer copy's first-instance WARN ("already had instances in
this process") AND "takes 3 clients at once, not this build's 6", and, with the clients held, an at-capacity line
quoting 3. The breakpoint is removed at the end; the game is left for the caller to kill.
"""
from __future__ import annotations

import argparse
import ctypes
import ctypes.wintypes as w
import os
import pathlib
import re
import sys
import time

import pefile

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parents[1]
sys.path.insert(0, str(HERE))
import ce_lua_eval                      # noqa: E402
from inject import inject, InjectError  # noqa: E402
from pipe_client import PipeClient      # noqa: E402

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
k32 = ctypes.WinDLL("kernel32", use_last_error=True)

MAILBOX_INITSTATE = 0x0C                          # Mimic.h: g_invokeMailbox.initState
INIT_READY, INIT_FAILED, INIT_SKIPPED = 2, 3, 4   # Mimic::InitState


def read_i32(pid: int, addr: int) -> int | None:
    k32.OpenProcess.restype = ctypes.c_void_p
    h = k32.OpenProcess(0x0010 | 0x1000, False, pid)   # VM_READ | QUERY_LIMITED_INFORMATION
    if not h:
        return None
    try:
        v, n = ctypes.c_int32(), ctypes.c_size_t()
        ok = k32.ReadProcessMemory(ctypes.c_void_p(h), ctypes.c_void_p(addr), ctypes.byref(v), 4, ctypes.byref(n))
        return v.value if ok and n.value == 4 else None
    finally:
        k32.CloseHandle(ctypes.c_void_p(h))


def modules(pid: int) -> list[tuple[str, int, int]]:
    """[(full path, base, size)] of the modules mapped in pid (Toolhelp32)."""
    class ME(ctypes.Structure):
        _fields_ = [("dwSize", w.DWORD), ("th32ModuleID", w.DWORD), ("th32ProcessID", w.DWORD),
                    ("GlblcntUsage", w.DWORD), ("ProccntUsage", w.DWORD), ("modBaseAddr", ctypes.c_void_p),
                    ("modBaseSize", w.DWORD), ("hModule", w.HMODULE), ("szModule", ctypes.c_wchar * 256),
                    ("szExePath", ctypes.c_wchar * 260)]
    k32.CreateToolhelp32Snapshot.restype = ctypes.c_void_p
    snap = k32.CreateToolhelp32Snapshot(0x18, pid)
    if not snap or snap == ctypes.c_void_p(-1).value:
        raise SystemExit(f"FAIL: CreateToolhelp32Snapshot({pid}) failed ({ctypes.get_last_error()})")
    out = []
    try:
        me = ME()
        me.dwSize = ctypes.sizeof(ME)
        ok = k32.Module32FirstW(ctypes.c_void_p(snap), ctypes.byref(me))
        while ok:
            out.append((me.szExePath, me.modBaseAddr or 0, me.modBaseSize))
            ok = k32.Module32NextW(ctypes.c_void_p(snap), ctypes.byref(me))
    finally:
        k32.CloseHandle(ctypes.c_void_p(snap))
    return out


def module_of(pid: int, dll: pathlib.Path) -> tuple[int, int] | None:
    want = os.path.normcase(str(dll.resolve()))
    for path, base, size in modules(pid):
        if os.path.normcase(path) == want:
            return base, size
    return None


def export_rva(pe: pefile.PE, name: str) -> int:
    for e in pe.DIRECTORY_ENTRY_EXPORT.symbols:
        if e.name and e.name.decode() == name:
            return e.address
    raise SystemExit(f"FAIL: no export {name}")


def call_site_rva(pe: pefile.PE, func: bytes) -> int:
    """The one `call qword ptr [rip+X]` whose X is func's IAT slot. More or fewer than one is a refusal."""
    iat = [imp.address - pe.OPTIONAL_HEADER.ImageBase
           for d in pe.DIRECTORY_ENTRY_IMPORT for imp in d.imports if imp.name == func]
    if len(iat) != 1:
        raise SystemExit(f"FAIL: {func.decode()} has {len(iat)} IAT slots, not 1")
    hits = []
    for s in pe.sections:
        if not s.Characteristics & 0x20000000:     # IMAGE_SCN_MEM_EXECUTE
            continue
        data = s.get_data()
        for m in re.finditer(rb"\xff\x15", data):
            off = m.start()
            disp = int.from_bytes(data[off + 2:off + 6], "little", signed=True)
            rva = s.VirtualAddress + off
            if rva + 6 + disp == iat[0]:
                hits.append(rva)
    if len(hits) != 1:
        raise SystemExit(f"FAIL: {len(hits)} call sites of {func.decode()}, not 1: {[hex(h) for h in hits]}")
    return hits[0]


def lua(code: str, timeout: float = 60.0) -> str:
    res = ce_lua_eval.run(code, timeout)
    if res is None:
        raise SystemExit("FAIL: Cheat Engine did not run the chunk")
    ok, body = res
    if not ok:
        raise SystemExit(f"FAIL: the chunk raised: {body}")
    return body


def name_state(pid: int) -> str:
    """'gone', 'served' or 'busy' for this game's pipe name, asked without connecting."""
    k32.WaitNamedPipeW.argtypes = [w.LPCWSTR, w.DWORD]
    if k32.WaitNamedPipeW(f"\\\\.\\pipe\\UE5DumpBfx_{pid}", 1):
        return "served"
    err = ctypes.get_last_error()
    return "gone" if err == 2 else ("busy" if err in (121, 231) else f"err {err}")


def pipe_log(pid: int) -> pathlib.Path:
    import subprocess
    out = subprocess.run(["tasklist", "/FI", f"PID eq {pid}", "/FO", "CSV", "/NH"], capture_output=True, text=True).stdout
    exe = out.split(",")[0].strip('"')
    return pathlib.Path(os.environ["LOCALAPPDATA"]) / "UE5CEDumper" / "Logs" / pathlib.Path(exe).stem / "pipe-0.log"


def log_since(path: pathlib.Path, offset: int) -> str:
    with open(path, "rb") as f:
        f.seek(offset)
        return f.read().decode("utf-8", errors="replace")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--pid", type=int, required=True)
    ap.add_argument("--new", default=str(ROOT / "dist" / "UE5Dumper.dll"))
    ap.add_argument("--old", default=str(ROOT / "out" / "stageA" / "copy3695" / "UE5Dumper.dll"))
    ap.add_argument("--exe", default=str(ROOT / "dist" / "UE5DumpUI.exe"))
    ap.add_argument("--clients", type=int, default=3)
    a = ap.parse_args()
    new, old = pathlib.Path(a.new), pathlib.Path(a.old)
    pid = a.pid

    newmod = module_of(pid, new)
    if not newmod:
        raise SystemExit(f"FAIL: {new} is not loaded in {pid}")
    if module_of(pid, old):
        raise SystemExit(f"FAIL: {old} is already loaded in {pid}: this rig injects it itself, into a served name")
    if name_state(pid) != "served":
        raise SystemExit(f"FAIL: the newer copy does not serve the pipe ({name_state(pid)})")
    pe_new, pe_old = pefile.PE(str(new), fast_load=False), pefile.PE(str(old), fast_load=False)
    site = call_site_rva(pe_new, b"CreateNamedPipeW")
    stop_new = newmod[0] + export_rva(pe_new, "UE5_StopPipeServer")
    start_new = newmod[0] + export_rva(pe_new, "UE5_StartPipeServer")
    bp = newmod[0] + site
    print(f"newer copy at 0x{newmod[0]:X}; CreateNamedPipeW call site RVA 0x{site:X} -> breakpoint 0x{bp:X}", flush=True)

    log = pipe_log(pid)
    try:
        h = inject(pid, str(old), a.exe)
    except InjectError as e:
        raise SystemExit(f"FAIL: injecting the older copy: {e}")
    oldmod = module_of(pid, old)
    if not oldmod:
        raise SystemExit("FAIL: the older copy is not in the module list after its injection")
    start_old = oldmod[0] + export_rva(pe_old, "UE5_StartPipeServer")
    print(f"older copy at 0x{oldmod[0]:X} (HMODULE low32 0x{h:X})", flush=True)
    # The older copy cannot write its logs (the newer one holds the files: item 3), so its auto-start is read from its
    # own mailbox. READY after "another copy already serves" is the idle state; a watcher would leave it SKIPPED.
    init_at = oldmod[0] + export_rva(pe_old, "g_invokeMailbox") + MAILBOX_INITSTATE
    deadline = time.time() + 60
    state = None
    while time.time() < deadline:
        state = read_i32(pid, init_at)
        if state in (INIT_READY, INIT_FAILED, INIT_SKIPPED):
            break
        time.sleep(0.5)
    if state != INIT_READY:
        raise SystemExit(f"FAIL: the older copy's initState is {state}, not READY (2): it did not settle beside the "
                         "newer copy's server (4 = it defers and watches, and would race the staging below)")
    print("older copy: auto-start finished, READY beside the newer copy's server (idle)", flush=True)

    print("CE:", lua(f"openProcess({pid}); return getOpenedProcessID()"), flush=True)
    lua("debugProcess(2); return 'started'")
    deadline = time.time() + 30
    while time.time() < deadline:
        r = lua("return tostring(debug_isDebugging()) .. ' ' .. tostring(debug_getCurrentDebuggerInterface())")
        if r.startswith("true"):
            break
        time.sleep(1)
    else:
        raise SystemExit("FAIL: CE's VEH debugger did not attach")
    print(f"CE debugger: {r} (2 = VEH)", flush=True)

    r = lua(f"return tostring(executeCodeEx(0, 20000, 0x{stop_new:X}))", 40)
    time.sleep(0.5)
    print(f"newer copy UE5_StopPipeServer -> {r}; name {name_state(pid)}", flush=True)
    if name_state(pid) != "gone":
        raise SystemExit("FAIL: the name is still up after the newer copy's stop")
    time.sleep(3)    # a claim watcher in the older copy polls every 500 ms: it would have taken the name by now
    if name_state(pid) != "gone":
        raise SystemExit("FAIL: the name came back with nobody started: the older copy claimed it")
    print("name still gone 3 s later: the older copy is not watching", flush=True)

    off1 = log.stat().st_size
    lua(f"""
ZZ6 = {{state = "armed", later = 0, bp = 0x{bp:X}, start_old = 0x{start_old:X}}}
function zz6_onbp()
  if ZZ6.state ~= "armed" then ZZ6.later = ZZ6.later + 1; return 0 end
  ZZ6.state = "broken"
  ZZ6.rip = string.format("%X", RIP)
  local ok, r = pcall(executeCodeEx, 0, 5000, ZZ6.start_old)
  ZZ6.old_start = tostring(ok) .. ":" .. tostring(r)
  sleep(1000)
  ZZ6.state = "released"
  return 0
end
debug_setBreakpoint(ZZ6.bp, zz6_onbp)
return "armed"
""")
    r = lua(f"return tostring(executeCodeEx(0, 10000, 0x{start_new:X}))", 30)
    print(f"newer copy UE5_StartPipeServer -> {r}", flush=True)
    deadline = time.time() + 30
    state = ""
    while time.time() < deadline:
        state = lua("return ZZ6.state .. ' rip=' .. tostring(ZZ6.rip) .. ' old_start=' .. tostring(ZZ6.old_start)")
        if state.startswith("released"):
            break
        time.sleep(0.5)
    print(f"breakpoint: {state}", flush=True)
    time.sleep(1.5)

    clients = []
    for i in range(a.clients):
        try:
            c = PipeClient(pid=pid, timeout=10)
            c.connect()
            clients.append(c)
        except Exception as e:    # the limit is the point: a refused connect is a datum, not a crash
            print(f"client {i + 1}: {type(e).__name__}: {e}", flush=True)
    print(f"{len(clients)} of {a.clients} clients connected; name {name_state(pid)}", flush=True)
    time.sleep(3)
    text = log_since(log, off1)
    for c in clients:
        c.close()
    print("CE:", lua("debug_removeBreakpoint(ZZ6.bp); return 'removed; later hits ' .. ZZ6.later"), flush=True)

    shared = [ln for ln in text.splitlines() if "already had instances in this process" in ln]
    limit = [ln for ln in text.splitlines() if "takes 3 clients at once, not this build's 6" in ln]
    cap = [ln for ln in text.splitlines() if re.search(r"all 3 .*waiting for a free slot", ln)]
    print("\npipe-0.log since the newer copy's start:")
    for ln in text.splitlines():
        if "PipeServer" in ln or "UE5_StartPipeServer" in ln:
            print("  " + ln[:260])
    checks = [("the newer copy's first instance joined a name already in this process", bool(shared)),
              ("it logs the older copy's limit: takes 3, not 6", bool(limit)),
              ("the at-capacity line quotes 3", bool(cap)),
              ("the breakpoint held the newer copy while the older one started", state.startswith("released"))]
    print()
    for what, ok in checks:
        print(f"  {'PASS' if ok else 'FAIL'}  {what}")
    ok = all(x for _, x in checks)
    print("PASS" if ok else "FAIL")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
