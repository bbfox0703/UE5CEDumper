"""Hold a file open with NO sharing (dwShareMode = 0) for a while -- the live rig for [PROXY-PRODUCTNAME-UNREADABLE].

    py tools/verify/hold_exclusive.py <file> [seconds]      # default 300; Ctrl+C or kill ends it early

WHY. FileVersionInfo returns the same null ProductName for a file with no version resource, one an ACL denies, and one
held with FileShare.None (measured). The row made the last two a third state, UNREADABLE: never written or deleted,
and said so. Python's open() shares the file, so nothing would be tested; this calls CreateFileW directly, and proves
the hold by a second open that must fail with ERROR_SHARING_VIOLATION (32) -- without that self-check a run could pass
against a file nobody was holding (the se1_log_reroute.py pattern).
"""
import ctypes
import sys
import time
from ctypes import wintypes

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

k32 = ctypes.WinDLL("kernel32", use_last_error=True)
k32.CreateFileW.restype = wintypes.HANDLE
k32.CreateFileW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, wintypes.LPVOID,
                            wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE]
k32.CloseHandle.argtypes = [wintypes.HANDLE]

GENERIC_READ = 0x80000000
OPEN_EXISTING = 3
INVALID = wintypes.HANDLE(-1).value
ERROR_SHARING_VIOLATION = 32


def main(argv):
    if not argv:
        raise SystemExit(__doc__)
    path, secs = argv[0], float(argv[1]) if len(argv) > 1 else 300.0
    h = k32.CreateFileW(path, GENERIC_READ, 0, None, OPEN_EXISTING, 0, None)
    if h == INVALID:
        raise SystemExit(f"FAILED: could not open {path} exclusively (Win32 {ctypes.get_last_error()})")
    h2 = k32.CreateFileW(path, GENERIC_READ, 0x7, None, OPEN_EXISTING, 0, None)   # share all: must still fail
    err = ctypes.get_last_error()
    if h2 != INVALID:
        k32.CloseHandle(h2)
        k32.CloseHandle(h)
        raise SystemExit("FAILED: a second open succeeded -- the file is not held exclusively")
    if err != ERROR_SHARING_VIOLATION:
        k32.CloseHandle(h)
        raise SystemExit(f"FAILED: the second open failed with Win32 {err}, not a sharing violation")
    print(f"HOLDING {path} exclusively for {secs:.0f}s (self-check: a second open -> sharing violation)", flush=True)
    try:
        time.sleep(secs)
    except KeyboardInterrupt:
        pass
    finally:
        k32.CloseHandle(h)
        print("released", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
