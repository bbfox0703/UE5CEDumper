r"""SW1 — a scan worker that FAULTS must not let the run report a complete result.

    py tools/verify/sw1_worker_fault.py            # DumperTest must be running + injected

THE ROW (D2, `e6360903`). `ParallelGObjectsScan` wraps each worker in a `try` and, on a throw,
sets `workerFaulted` so `incomplete()` becomes true and every caller reports a PARTIAL result
rather than a complete one. Nothing on the 99-command pipe surface can make a worker throw, and
`dll_core_test` pins only the pure result struct — so the flag had never been set in a live
process, and the branch that consumes it had never run.

⭐ HOW IT IS REACHED. The DLL is built `/EHa` (`dll/CMakeLists.txt:155,186`), so the `catch (...)`
at `Aura.cpp:278` catches a hardware ACCESS VIOLATION as well as a C++ throw. `Aura::DecryptObjectPtr`
(`Aura.cpp:311-314`) is `if (!rawPtr || !s_decryptFunc) return rawPtr; return s_decryptFunc(rawPtr);`
— a raw indirect call that `GetByIndex` makes for EVERY object a worker touches — and
`UE5_SetObjectDecryption` (`Frieren.h:81`) is an exported C ABI setter for that pointer. Point it
at `0x1000` (inside Windows' permanently-reserved low 64 KiB, so the call faults on instruction
fetch), let the scan run into it, then set it back to NULL. Manufacture-and-restore, no DLL edit.

⛔ TRAP 1 — `deadline_hit` HAS THREE OTHER CAUSES. It is the same boolean for a real deadline
(`Aura.cpp:7414`), a cancel, and — silently — a `max_results` CAP HIT, which `Aura.cpp:8244-8247`
forces unconditionally once `candidates >= maxResults`. A rig that only asserts `deadline_hit ==
true` proves nothing. The control run below must come back with `deadline_hit == false` AND
`total < max_results`, or the run is void before it starts.

⛔ TRAP 2 — AN EMPTY RESULT IS NOT A PARTIAL ONE. Arming BEFORE the scan starts faults every
worker on its first object and yields `deadline_hit` over an EMPTY set, which cannot show that a
set is *partial*. The arm must land MID-SCAN. `parallel=false` (one inline chunk) makes the window
unambiguous: there is exactly one worker, its per-thread results accumulate into a vector the
unwind does not destroy, so everything found before the fault survives into the response.

⛔ TRAP 3 — THE WRONG LOG FILE. The register said "a SCAN-category log line". It is not: `Aura.cpp`
declares `LOG_CAT "OARR"` at line 8 and `Sein.cpp:82` routes OARR to `LF_Offsets`, so the worker
line lands in **offsets-*.log**. "SCAN" is a real category mapping to scan.log, so an operator
following the register greps a file that will never contain it.

⛔ TRAP 4 — THE WRONG MODULE. `call_export.py` defaults `--module UE5Dumper.dll`, but most games
here run our code through a deployed PROXY (`dxgi`/`version`/`winmm`/`dinput8`). This rig resolves
the module by asking which of the five actually carries the export in the target.

⚠ BLAST RADIUS. The armed pointer is global, so any other subsystem calling `GetByIndex` during
the window faults too. Most are guarded (`Fern::DispatchCommand`'s `catch(...)` at `Fern.cpp:6425`,
`Routine::RunThreadGuarded`, `Mimic.cpp:358`, `Stark.cpp:205`, `Schlacht.cpp:561`) and merely lose
a tick; the genuinely unguarded paths are the raw C ABI exports, i.e. a CE-Lua-driven session.
Keep the window short, and do not run this with gameplay features enabled.

ACCEPTANCE (all four, and the first two are the ones that make it a PARTIAL rather than an EMPTY
or a capped result):
  * `deadline_hit` flips false -> true between an otherwise-identical control and armed run;
  * `scanned_objects` is strictly LESS than the control's — the walk really did stop early;
  * `0 < total < control total` — some candidates were found and some were not;
  * `offsets-*.log` carries `ParallelGObjectsScan: worker ... faulted`, timestamped BETWEEN the
    `Custom decryption function SET` and `CLEARED (identity)` lines this rig itself writes.

=============================================================================================
WHAT THIS RIG ACTUALLY MEASURED, 2026-09-09, EVERSPACE 2 (1,137,983 objects, save loaded)
=============================================================================================

⭐ PROVEN — the row's stated property holds. With `--serial`, the armed access violation reached
the worker's `catch (...)`, `workerFaulted` was set, and `incomplete()` propagated:

    control :  total=16777  scanned_objects=1137983  duration_ms=2186  deadline_hit=False
    armed   :  total=0      scanned_objects=0        duration_ms=0     deadline_hit=True
    offsets-0.log: [ERROR] [OARR] ParallelGObjectsScan: worker tid=0 [0,1154906) faulted
                   — that index range was NOT walked; results are partial

That log line had never been emitted before in this repo's history, and `deadline_hit` flipped
false → true against an otherwise-identical control. **The run did not report a complete
result**, which is the property D2 is about.

⛔ NOT PROVEN, and measured to be otherwise: that the result is PARTIAL. It is EMPTY. `--serial`
means ONE chunk covering the whole array, so the unwind discards the entire walk — `total` and
`scanned_objects` both come back 0. The register warned that the all-workers variant "yields
deadline_hit over an EMPTY set"; this shows the SERIAL form does too, for the same structural
reason, even when the arm lands mid-scan. ⚠ Note the log line asserts "results are partial"
while the caller returns nothing at all. The rig FAILS on that arm rather than claiming a
partial result it did not see.

⛔ AND THE PARALLEL FORM COULD NOT BE MADE TO FAULT AT ALL. Armed across the ENTIRE run (0.0 s
in, 900 ms hold against a 458 ms scan) the response was byte-identical to the control and no
fault line appeared. The telling number is the ratio: across four armed runs in one process the
log holds **4 SET/CLEARED pairs and 1 fault line** — only the FIRST faulted. That is consistent
with the scan index being built once and reused (`Aura.h:1741` refers to "ScanForValue's index
builder"), so later runs never call `GetByIndex` and never reach `DecryptObjectPtr`.
⚠ CONSISTENT WITH, NOT PROVEN. Settling it needs one armed run per FRESH process, or a way to
invalidate that index. Do not record the cache as a fact on this evidence.
"""
from __future__ import annotations

import argparse
import ctypes
import ctypes.wintypes as wt
import glob
import io
import os
import pathlib
import re
import sys
import threading
import time

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from pipe_client import PipeClient          # noqa: E402

DIST = HERE.parent.parent / "dist" / "UE5Dumper.dll"
EXPORT = "UE5_SetObjectDecryption"
CANDIDATE_MODULES = ["UE5Dumper.dll", "dxgi.dll", "version.dll", "winmm.dll", "dinput8.dll"]
BAD_PTR = 0x1000        # permanently reserved low 64 KiB -> AV on instruction fetch

# The scan shape, tuned on Titan Quest II (~460k objects, save loaded) 2026-09-09. It must
# satisfy three things at once and they pull against each other: the walk has to be LONG enough
# to arm inside (so not a narrow object set), the match count has to stay well UNDER max_results
# (a cap hit forces deadline_hit all on its own -- TRAP 1), and matches have to be SPREAD across
# the array so a mid-scan fault leaves a smaller but NON-EMPTY set (TRAP 2). Measured on TQ2:
#   Bigger -1e30  -> total 2,000,000 = the cap. Unusable: the control itself reports deadline_hit.
#   Exact  1      -> total 1,556,780. Under the cap but uncomfortably near it.
#   Bigger 1e6    -> total    17,912 over 460,829 objects in 1,724 ms, deadline_hit False.  <-- used
PARALLEL = True
SCAN_TYPE = "Bigger"
SCAN_VALUE = "1000000"

SET_LINE = "Custom decryption function SET"
CLEAR_LINE = "Custom decryption function CLEARED"
FAULT_LINE = "faulted"


def pid_of(name: str) -> int:
    import subprocess
    r = subprocess.run(["tasklist", "/FI", "IMAGENAME eq " + name, "/FO", "CSV", "/NH"],
                       capture_output=True, text=True, encoding="utf-8", errors="replace")
    for ln in (r.stdout or "").splitlines():
        m = re.match(r'^"([^"]+)","(\d+)"', ln.strip())
        if m and m.group(1).lower() == name.lower():
            return int(m.group(2))
    raise SystemExit("no process named %r" % name)


def true_path(hproc, hmod) -> str:
    """⛔ TOOLHELP32's szExePath LOSES NON-ANSI CHARACTERS, even from the W entry point.
    Measured 2026-09-09: EVERSPACE 2 installs to `...\\EVERSPACE™ 2\\...` and
    `MODULEENTRY32W.szExePath` came back as `EVERSPACE? 2` — a literal '?', not a console
    artefact (`os.path.isfile` on it is False), so the path could not be opened. `inject.py`
    carries the identical struct and never noticed, because it only ever compares NAMES.
    `GetModuleFileNameExW` returns the real path. This is the same class of damage
    `Heiter.cpp`'s own comment records for `GetModuleFileNameA`."""
    psapi = ctypes.WinDLL("psapi", use_last_error=True)
    # argtypes are load-bearing: without them ctypes marshals HMODULE as a signed C int and a
    # 64-bit module handle overflows ("int too long to convert").
    psapi.GetModuleFileNameExW.argtypes = [wt.HANDLE, wt.HMODULE, wt.LPWSTR, wt.DWORD]
    psapi.GetModuleFileNameExW.restype = wt.DWORD
    buf = ctypes.create_unicode_buffer(32768)
    n = psapi.GetModuleFileNameExW(hproc, hmod, buf, 32768)
    return buf.value if n else ""


def modules(pid: int):
    """[(name, base, path)] via Toolhelp32 for the enumeration, GetModuleFileNameExW for the
    path — see true_path for why the two cannot be the same call."""
    TH32CS_SNAPMODULE = 0x00000008 | 0x00000010

    class MODULEENTRY32W(ctypes.Structure):
        _fields_ = [("dwSize", wt.DWORD), ("th32ModuleID", wt.DWORD),
                    ("th32ProcessID", wt.DWORD), ("GlblcntUsage", wt.DWORD),
                    ("ProccntUsage", wt.DWORD), ("modBaseAddr", ctypes.POINTER(ctypes.c_byte)),
                    ("modBaseSize", wt.DWORD), ("hModule", wt.HMODULE),
                    ("szModule", ctypes.c_wchar * 256), ("szExePath", ctypes.c_wchar * 260)]
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    k32.CreateToolhelp32Snapshot.restype = wt.HANDLE
    snap = k32.CreateToolhelp32Snapshot(TH32CS_SNAPMODULE, pid)
    if snap == wt.HANDLE(-1).value:
        raise SystemExit("CreateToolhelp32Snapshot failed")
    k32.OpenProcess.restype = wt.HANDLE
    hproc = k32.OpenProcess(0x0410, False, pid)    # QUERY_INFORMATION | VM_READ
    out = []
    try:
        me = MODULEENTRY32W()
        me.dwSize = ctypes.sizeof(MODULEENTRY32W)
        ok = k32.Module32FirstW(snap, ctypes.byref(me))
        while ok:
            base = ctypes.cast(me.modBaseAddr, ctypes.c_void_p).value or 0
            p = true_path(hproc, me.hModule) if hproc else ""
            out.append((me.szModule, base, p or me.szExePath))
            ok = k32.Module32NextW(snap, ctypes.byref(me))
    finally:
        k32.CloseHandle(snap)
        if hproc:
            k32.CloseHandle(hproc)
    return out


def export_rva(dll_path: str) -> int:
    """⛔ THE RVA MUST COME FROM THE EXACT FILE THAT IS MAPPED IN THE TARGET, and this rig
    learned that the expensive way on 2026-09-09: it took the RVA from dist/UE5Dumper.dll and
    added it to the base of the deployed **proxy** dxgi.dll. A proxy is a DIFFERENT binary --
    same exports, different layout -- so the remote thread started at a wrong address inside
    dxgi and Titan Quest II died with "EXCEPTION_ACCESS_VIOLATION 0x00007ffe0b0ece10 /
    dxgi_7ffe0b000000 / kernel32 / ntdll" -- a crash-reporter stack that names the remote thread
    we started, at base + the RVA of a DIFFERENT file. Toolhelp32 hands us szExePath, so we load
    THAT file and cannot be wrong."""
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    k32.LoadLibraryExW.restype = wt.HMODULE
    h = k32.LoadLibraryExW(dll_path, None, 0x00000001)       # DONT_RESOLVE_DLL_REFERENCES
    if not h:
        raise SystemExit("could not load %s locally" % dll_path)
    k32.GetProcAddress.restype = ctypes.c_void_p
    k32.GetProcAddress.argtypes = [wt.HMODULE, ctypes.c_char_p]
    a = k32.GetProcAddress(h, EXPORT.encode())
    if not a:
        raise SystemExit("%s is not exported by the file actually mapped in the target (%s)"
                         % (EXPORT, dll_path))
    return a - h


def call_setter(pid: int, base: int, rva: int, param: int) -> None:
    """⭐ THE ONE THING call_export.py CANNOT DO: pass an argument. lpParameter lands in RCX,
    which IS the first x64 argument of `void UE5_SetObjectDecryption(uintptr_t(*)(uintptr_t))`.
    call_export.py hard-codes it NULL — which happens to be exactly the DISARM call, so
    `py tools/verify/call_export.py UE5_SetObjectDecryption --process <game>` is a valid panic
    button if this rig ever dies with the pointer still armed."""
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    k32.OpenProcess.restype = wt.HANDLE
    h = k32.OpenProcess(0x1F0FFF, False, pid)                # PROCESS_ALL_ACCESS
    if not h:
        raise SystemExit("OpenProcess(%d) failed" % pid)
    try:
        k32.CreateRemoteThread.restype = wt.HANDLE
        k32.CreateRemoteThread.argtypes = [wt.HANDLE, ctypes.c_void_p, ctypes.c_size_t,
                                           ctypes.c_void_p, ctypes.c_void_p, wt.DWORD,
                                           ctypes.POINTER(wt.DWORD)]
        th = k32.CreateRemoteThread(h, None, 0, ctypes.c_void_p(base + rva),
                                    ctypes.c_void_p(param), 0, None)
        if not th:
            raise SystemExit("CreateRemoteThread failed (%d)" % ctypes.get_last_error())
        k32.WaitForSingleObject(th, 5000)
        k32.CloseHandle(th)
    finally:
        k32.CloseHandle(h)


def log_lines(proc: str, pattern: str) -> list[tuple[str, str]]:
    d = pathlib.Path(os.environ["LOCALAPPDATA"]) / "UE5CEDumper" / "Logs" / proc
    out = []
    for f in glob.glob(str(d / "offsets*.log")):
        for ln in io.open(f, encoding="utf-8", errors="replace"):
            if pattern in ln:
                m = re.match(r"^\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d+)", ln)
                out.append((m.group(1) if m else "", ln.rstrip("\n")))
    return sorted(out)


def make_scan(max_results: int, deadline_ms: int) -> dict:
    # `Bigger -1e30` matches essentially every float, which is deliberate: the candidates are
    # then spread across the WHOLE object array, so a mid-scan fault yields a strictly smaller
    # but still NON-EMPTY set. A rare-value Exact scan would walk just as far but could easily
    # return 0 either way, and "0 vs 0" cannot distinguish partial from empty.
    return dict(data_type="Float", scan_type=SCAN_TYPE, value=SCAN_VALUE,
                game_only=False, parallel=PARALLEL, page_size=0,
                max_results=max_results, deadline_ms=deadline_ms)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--image", default="DumperTest.exe")
    ap.add_argument("--arm-at", type=float, default=0.40,
                    help="fraction of the control duration at which to arm")
    ap.add_argument("--hold-ms", type=int, default=600)
    ap.add_argument("--max-results", type=int, default=50000,
                    help="must stay ABOVE the control total or a cap hit forces deadline_hit")
    ap.add_argument("--deadline-ms", type=int, default=600000)
    ap.add_argument("--serial", action="store_true",
                    help="parallel=false: ONE chunk, so a fault unwinds the whole walk and "
                         "yields an EMPTY result. Kept because measuring that is what proves "
                         "the parallel form is the one that shows PARTIAL.")
    ap.add_argument("--min-duration-ms", type=int, default=1200,
                    help="refuse if the control is faster than this -- there would be no "
                         "reliable MID-SCAN window and an arm before the first object gives "
                         "an EMPTY result, which cannot show a set is PARTIAL")
    a = ap.parse_args()
    global PARALLEL
    PARALLEL = not a.serial
    SCAN = make_scan(a.max_results, a.deadline_ms)

    pid = pid_of(a.image)
    proc = a.image[:-4]
    # ⛔ PICK BY EXPORT, NOT BY NAME. Every D3D title loads the SYSTEM C:\WINDOWS\SYSTEM32\
    # dxgi.dll, so a name-ordered search finds that one first and would then add our RVA to
    # Microsoft's module. Measured on EVERSPACE 2, 2026-09-09 -- the same "the system owns that
    # filename too" trap that bit the SW8 process probe. The only sound test is whether the file
    # actually mapped at that base exports the function.
    # ⛔ AND DO NOT KEY BY MODULE NAME EITHER. A proxy FORWARDS to the system library of the
    # same name, so both are mapped at once and a name-keyed dict silently keeps whichever the
    # enumeration saw last. Measured on EVERSPACE 2, 2026-09-09: our proxy at
    # ...\ES2\Binaries\Win64\VERSION.dll was displaced by C:\WINDOWS\system32\version.dll and
    # the rig concluded no module exported the function at all. Keep EVERY match and test each.
    cands = [(n, b, p) for n, b, p in modules(pid)
             if n.lower() in [c.lower() for c in CANDIDATE_MODULES]]
    mod = base = path = rva = None
    tried = []
    for n, b, p in cands:
        try:
            rva = export_rva(p)
        except SystemExit as e:
            tried.append("%s (%s): %s" % (n, p, e))
            continue
        mod, base, path = n, b, p
        break
    if mod is None:
        raise SystemExit("no module in %s exports %s.\n  %s"
                         % (a.image, EXPORT, "\n  ".join(tried) or "none of %r is loaded"
                            % CANDIDATE_MODULES))
    print("target     : %s pid %d" % (a.image, pid))
    print("module     : %s @ 0x%X" % (mod, base))
    print("             %s" % path)
    print("             %s rva 0x%X (read from THAT file, not from dist/)" % (EXPORT, rva))

    fails: list[str] = []

    # ---- CONTROL -------------------------------------------------------------------
    with PipeClient().connect() as c:
        t = time.time()
        ctl = c.request("begin_value_scan", **SCAN)
        wall = time.time() - t
    if not ctl.get("ok", True):
        raise SystemExit("control scan failed: %s" % ctl)
    c_total, c_scanned = ctl.get("total"), ctl.get("scanned_objects")
    c_dur, c_dl = ctl.get("duration_ms"), ctl.get("deadline_hit")
    print("control    : total=%s scanned_objects=%s duration_ms=%s deadline_hit=%s (wall %.1fs)"
          % (c_total, c_scanned, c_dur, c_dl, wall))

    # ⛔ TRAP 1 -- design the confounds OUT rather than explain them away.
    if c_dl:
        raise SystemExit("the CONTROL already reports deadline_hit=true, so the armed run's "
                         "flag would prove nothing. Raise deadline_ms or narrow the scan.")
    if c_total is None or c_total >= a.max_results:
        raise SystemExit("the control returned total=%s against max_results=%d -- a cap hit "
                         "FORCES deadline_hit (Aura.cpp:8244) regardless of any fault. Narrow "
                         "the scan, or raise --max-results." % (c_total, a.max_results))
    if not c_scanned:
        raise SystemExit("the control scanned 0 objects -- nothing to be partial about")
    if c_dur is None or c_dur < a.min_duration_ms:
        raise SystemExit("the control ran in %sms; there is no reliable MID-SCAN window to arm "
                         "in, and arming before the first object yields an EMPTY set, which "
                         "cannot show a set is PARTIAL. Widen the scan." % c_dur)

    # ---- ARMED ---------------------------------------------------------------------
    box: dict = {}

    def run_scan():
        try:
            with PipeClient().connect() as c2:
                box["r"] = c2.request("begin_value_scan", **SCAN)
        except Exception as e:            # noqa: BLE001
            box["err"] = repr(e)

    th = threading.Thread(target=run_scan, daemon=True)
    # ⛔ NO 0.5s FLOOR. It was one, and on a 457 ms parallel scan that pushed the arm past the
    # END of the run: the armed pass came back byte-identical to the control and the rig
    # correctly reported that nothing had been measured.
    delay = max(0.02, (c_dur / 1000.0) * a.arm_at)
    print("armed run  : arming %.1fs in, holding %dms" % (delay, a.hold_ms))
    th.start()
    time.sleep(delay)
    try:
        call_setter(pid, base, rva, BAD_PTR)
        time.sleep(a.hold_ms / 1000.0)
    finally:
        call_setter(pid, base, rva, 0)          # DISARM, always
    th.join(timeout=600)

    if "err" in box:
        raise SystemExit("the armed scan died on the wire: %s" % box["err"])
    arm = box.get("r") or {}
    a_total, a_scanned = arm.get("total"), arm.get("scanned_objects")
    a_dl = arm.get("deadline_hit")
    print("armed      : total=%s scanned_objects=%s duration_ms=%s deadline_hit=%s"
          % (a_total, a_scanned, arm.get("duration_ms"), a_dl))

    if not a_dl:
        fails.append("deadline_hit stayed FALSE -- the fault never reached the worker's "
                     "catch(...), or the arm missed the scan window entirely")
    if a_scanned is None or c_scanned is None or a_scanned >= c_scanned:
        fails.append("scanned_objects did not drop (%s vs control %s) -- the walk did not stop "
                     "early, so whatever set deadline_hit was not a faulted worker"
                     % (a_scanned, c_scanned))
    if a_total is None or a_total <= 0:
        fails.append("the armed run returned total=%s -- an EMPTY result, not a PARTIAL one. "
                     "That is the outcome the register warned the all-workers variant gives, "
                     "and it cannot show a set is partial." % a_total)
    elif a_total >= c_total:
        fails.append("the armed run found %s candidates vs the control's %s -- not partial"
                     % (a_total, c_total))

    # ---- the log side, scoped to the window this rig itself created -----------------
    sets = log_lines(proc, SET_LINE)
    clears = log_lines(proc, CLEAR_LINE)
    faults = log_lines(proc, FAULT_LINE)
    print("log        : %d SET, %d CLEARED, %d fault line(s) in offsets*.log"
          % (len(sets), len(clears), len(faults)))
    if not sets or not clears:
        fails.append("the SET/CLEARED markers are missing from offsets*.log, so no window "
                     "exists to scope the fault line to")
    else:
        t0, t1 = sets[-1][0], clears[-1][0]
        inside = [ln for ts, ln in faults if t0 <= ts <= t1]
        for ln in inside[:2]:
            print("  %s" % ln.strip()[:170])
        if not inside:
            fails.append("no worker-faulted line in offsets*.log between %s and %s. ⛔ Note the "
                         "register said SCAN category; Aura.cpp's LOG_CAT is OARR, which "
                         "Sein.cpp routes to offsets*.log -- this rig already greps the right "
                         "file, so an absence here is real." % (t0, t1))

    print()
    if fails:
        print("SW1: FAIL")
        for f in fails:
            print("  - %s" % f)
        return 1
    print("SW1: PASS -- a worker that took an access violation mid-scan set workerFaulted, and "
          "the run reported %s candidates over %s objects instead of the control's %s over %s, "
          "with deadline_hit true. A PARTIAL result, not a complete one and not an empty one; "
          "the decryption pointer is restored." % (a_total, a_scanned, c_total, c_scanned))
    return 0


if __name__ == "__main__":
    sys.exit(main())
