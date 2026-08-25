"""SE1: a log category that cannot open its file must reroute, not vanish.

    py se1_log_reroute.py

THE FIX (audit L4, build 3263). If a category's log file could not be opened, that
category was **dead for the whole process with nothing logged anywhere**, and its
buffered early lines were destroyed. A later grep for one of those lines then read as
"that code path never ran" — an absence caused by the logger, indistinguishable from
an absence caused by the code.

⚠ `Sein.cpp` is compiled by NO test target, so this has never executed.

HOW THE EXCLUSIVE HANDLE IS TAKEN. Python's `open()` uses the CRT, which shares the
file — the DLL would open it happily and nothing would be tested. This calls
`CreateFileW` directly with **`dwShareMode = 0`**, the same denial a viewer holding the
file produces, and verifies the handle is genuinely exclusive by attempting a second
open that must fail with ERROR_SHARING_VIOLATION (32). Without that self-check the run
could pass vacuously against a file nobody was actually locking.

TARGET: the `scan` category. `init` must stay writable -- it is where the rerouted
lines have to land, so locking it would destroy the evidence rather than produce it.
"""
import ctypes
import pathlib
import subprocess
import sys
import time
from ctypes import wintypes

k32 = ctypes.WinDLL("kernel32", use_last_error=True)
k32.CreateFileW.restype = wintypes.HANDLE
k32.CreateFileW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD,
                            wintypes.LPVOID, wintypes.DWORD, wintypes.DWORD,
                            wintypes.HANDLE]
k32.CloseHandle.argtypes = [wintypes.HANDLE]

GENERIC_READ = 0x80000000
OPEN_EXISTING = 3
INVALID = wintypes.HANDLE(-1).value
ERROR_SHARING_VIOLATION = 32

ROOT = pathlib.Path(__file__).resolve().parents[2]
LOGDIR = pathlib.Path.home() / "AppData/Local/UE5CEDumper/Logs/python"


def lock_exclusive(path):
    h = k32.CreateFileW(str(path), GENERIC_READ, 0, None, OPEN_EXISTING, 0, None)
    if h == INVALID:
        raise SystemExit(f"se1: FAILED -- could not open {path} exclusively "
                         f"(Win32 {ctypes.get_last_error()})")
    # Self-check: prove the lock actually denies a second opener.
    h2 = k32.CreateFileW(str(path), GENERIC_READ, 0, None, OPEN_EXISTING, 0, None)
    if h2 != INVALID:
        k32.CloseHandle(h2)
        k32.CloseHandle(h)
        raise SystemExit("se1: FAILED -- a second open SUCCEEDED, so the file is not "
                         "locked and this test would pass vacuously")
    if ctypes.get_last_error() != ERROR_SHARING_VIOLATION:
        print(f"  note: second open failed with Win32 {ctypes.get_last_error()}, "
              f"not the expected 32 (SHARING_VIOLATION)")
    return h


def main():
    scan = LOGDIR / "scan-0.log"
    init = LOGDIR / "init-0.log"
    if not scan.is_file():
        raise SystemExit(f"se1: FAILED -- {scan} does not exist yet; inject once first so "
                         f"the category files are present to lock")

    h = lock_exclusive(scan)
    print(f"holding an EXCLUSIVE handle on {scan.name} (second open denied, as required)")
    try:
        proc = subprocess.Popen([sys.executable, "-c", "import time;time.sleep(180)"],
                                creationflags=0x00000008 | 0x00000200)
        time.sleep(2)
        r = subprocess.run([sys.executable, str(ROOT / "tools/verify/inject.py"),
                            "--pid", str(proc.pid)],
                           capture_output=True, text=True, errors="replace")
        if r.returncode != 0:
            raise SystemExit(f"se1: FAILED -- inject: {r.stdout}{r.stderr}")
        print(f"  injected into pid {proc.pid}; letting the scan run ...")
        time.sleep(25)
        text = init.read_text(encoding="utf-8", errors="replace")
    finally:
        k32.CloseHandle(h)
        try:
            subprocess.run(["taskkill", "/F", "/PID", str(proc.pid)], capture_output=True)
        except Exception:
            pass

    ok = True
    print("\n--- the reroute announcement ---")
    ann = [l for l in text.splitlines() if "rerouted here" in l or "could not open" in l]
    for l in ann:
        print("  ", l.strip()[:160])
    if not ann:
        print("  *** ABSENT -- the category failed silently, which is the defect ***")
        ok = False

    print("\n--- the rerouted category's OWN lines must appear in init-0.log ---")
    # [SCAN] / [SCAN:...] tagged lines are what would otherwise have been lost.
    scan_lines = [l for l in text.splitlines() if "[SCAN" in l]
    print(f"  [SCAN*] lines found in init-0.log: {len(scan_lines)}")
    for l in scan_lines[:3]:
        print("  ", l.strip()[:150])
    if not scan_lines:
        print("  *** none -- the announcement alone is not the fix; the LINES must survive ***")
        ok = False

    print(f"\nSE1: {'PASS' if ok else 'FAIL'}")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
