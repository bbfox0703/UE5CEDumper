"""Call a UE5Dumper.dll C-ABI export inside the injected game, from Python.

    py tools/verify/call_export.py UE5_Shutdown --process DumperTest

WHY THIS EXISTS. `UE5_Shutdown` has no pipe command and no mailbox op - it is reached only
from CE (the plugin's Disable, or a CE-Lua `callFunction`). Two register rows need it and
neither is runnable without it:

  * L10's teardown half - `UE5_Shutdown` must call `Grausam::SetForegroundLock(false)`
  * M5 - worker-join ordering inside `UE5_Shutdown`

⚠ AND CLOSING THE GAME DOES NOT CALL IT. That is recorded in the register (B8's block) and
is why both rows kept not getting run. The claim that it has NEVER run is stale, though:
6 log files in the corpus contain `UE5_Shutdown: Cleaning up...`, so the path is real and
reachable - just not from a window close.

HOW. The RVA is resolved by loading our own DLL into THIS process with
`DONT_RESOLVE_DLL_REFERENCES` (so no DllMain, no side effects) and subtracting the local
base from `GetProcAddress`. The same RVA is then added to the module base found in the
TARGET process, and `CreateRemoteThread` is pointed at it - the same mechanism inject.py
uses for `LoadLibraryW`, which is why an export taking no arguments is safe here.

The elapsed time is reported because M5's acceptance is a DEADLINE ("no hang"), and a hang
is not a test result: the wait is bounded and a timeout is reported as such rather than
blocking forever.
"""
from __future__ import annotations

import argparse
import ctypes
import ctypes.wintypes as wt
import sys
import time

k32 = ctypes.WinDLL("kernel32", use_last_error=True)

TH32CS_SNAPMODULE = 0x08
TH32CS_SNAPMODULE32 = 0x10
PROCESS_ALL = 0x1F0FFF
DONT_RESOLVE_DLL_REFERENCES = 0x0001


class MODULEENTRY32W(ctypes.Structure):
    _fields_ = [("dwSize", wt.DWORD), ("th32ModuleID", wt.DWORD), ("th32ProcessID", wt.DWORD),
                ("GlblcntUsage", wt.DWORD), ("ProccntUsage", wt.DWORD),
                ("modBaseAddr", ctypes.POINTER(ctypes.c_byte)), ("modBaseSize", wt.DWORD),
                ("hModule", wt.HMODULE), ("szModule", wt.WCHAR * 256), ("szExePath", wt.WCHAR * 260)]


def pid_of(name: str) -> int:
    import subprocess
    out = subprocess.run(["tasklist", "/FI", "IMAGENAME eq %s.exe" % name, "/FO", "CSV", "/NH"],
                         capture_output=True, text=True).stdout
    for line in out.splitlines():
        parts = [p.strip('"') for p in line.split('","')]
        if len(parts) > 1 and parts[0].lower().startswith(name.lower()):
            return int(parts[1])
    raise SystemExit("call_export: no process named %s" % name)


def module_base(pid: int, modname: str) -> int:
    snap = k32.CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, pid)
    if snap == -1:
        raise SystemExit("call_export: CreateToolhelp32Snapshot failed")
    me = MODULEENTRY32W(); me.dwSize = ctypes.sizeof(MODULEENTRY32W)
    ok = k32.Module32FirstW(snap, ctypes.byref(me))
    try:
        while ok:
            if me.szModule.lower() == modname.lower():
                return ctypes.cast(me.modBaseAddr, ctypes.c_void_p).value
            ok = k32.Module32NextW(snap, ctypes.byref(me))
    finally:
        k32.CloseHandle(snap)
    raise SystemExit("call_export: %s is not loaded in pid %d -- inject first" % (modname, pid))


def export_rva(dll_path: str, export: str) -> int:
    """RVA via a side-effect-free local load. DONT_RESOLVE_DLL_REFERENCES skips DllMain."""
    k32.LoadLibraryExW.restype = wt.HMODULE
    h = k32.LoadLibraryExW(dll_path, None, DONT_RESOLVE_DLL_REFERENCES)
    if not h:
        raise SystemExit("call_export: local LoadLibraryEx failed (%d)" % ctypes.get_last_error())
    k32.GetProcAddress.restype = ctypes.c_void_p
    k32.GetProcAddress.argtypes = [wt.HMODULE, ctypes.c_char_p]
    addr = k32.GetProcAddress(h, export.encode())
    if not addr:
        raise SystemExit("call_export: %s is not an export of %s" % (export, dll_path))
    return addr - h


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("export")
    ap.add_argument("--process", default="DumperTest")
    ap.add_argument("--dll", default="dist/UE5Dumper.dll")
    ap.add_argument("--module", default="UE5Dumper.dll")
    ap.add_argument("--timeout", type=float, default=20.0)
    a = ap.parse_args(argv)

    pid = pid_of(a.process)
    base = module_base(pid, a.module)
    rva = export_rva(str(__import__("pathlib").Path(a.dll).resolve()), a.export)
    target = base + rva
    print("pid=%d  %s base=0x%X  %s rva=0x%X  -> 0x%X"
          % (pid, a.module, base, a.export, rva, target))

    h = k32.OpenProcess(PROCESS_ALL, False, pid)
    if not h:
        raise SystemExit("call_export: OpenProcess failed (%d)" % ctypes.get_last_error())
    k32.CreateRemoteThread.restype = wt.HANDLE
    k32.CreateRemoteThread.argtypes = [wt.HANDLE, ctypes.c_void_p, ctypes.c_size_t,
                                       ctypes.c_void_p, ctypes.c_void_p, wt.DWORD,
                                       ctypes.POINTER(wt.DWORD)]
    t0 = time.time()
    th = k32.CreateRemoteThread(h, None, 0, ctypes.c_void_p(target), None, 0, None)
    if not th:
        raise SystemExit("call_export: CreateRemoteThread failed (%d)" % ctypes.get_last_error())
    rc = k32.WaitForSingleObject(th, int(a.timeout * 1000))
    dt = time.time() - t0
    code = wt.DWORD(0)
    k32.GetExitCodeThread(th, ctypes.byref(code))
    k32.CloseHandle(th); k32.CloseHandle(h)

    if rc == 0x102:
        print("TIMEOUT after %.3fs -- the export did not return (this IS the hang)" % dt)
        return 2
    print("returned in %.3fs (thread exit code %d)" % (dt, code.value))
    return 0


if __name__ == "__main__":
    sys.exit(main())
