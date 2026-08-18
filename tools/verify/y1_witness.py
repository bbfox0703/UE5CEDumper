"""Y1 witness: read the DropItemSpawner Owner field AND the mailbox paramsData.

Both reads FAIL LOUDLY. A reader that returns 0 for "read failed" and 0 for
"the field is zero" cannot distinguish the old Y1 bug (a 0 argument) from its own
failure -- that is exactly how a working store got recorded as "PERSISTS = False".
"""
import sys, ctypes, subprocess, struct
from ctypes import wintypes
k32 = ctypes.WinDLL("kernel32", use_last_error=True)
k32.OpenProcess.restype = ctypes.c_void_p
k32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
k32.ReadProcessMemory.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p,
                                  ctypes.c_size_t, ctypes.POINTER(ctypes.c_size_t)]

def pid_of(stem):
    out = subprocess.run(["tasklist","/fo","csv","/nh"], capture_output=True, text=True).stdout
    hits = [int([p.strip('"') for p in l.split('","')][1]) for l in out.splitlines()
            if l.lower().startswith('"' + stem.lower()[:20])]
    if len(hits) != 1: raise SystemExit(f"want exactly 1 {stem!r}, got {hits}")
    return hits[0]

PROC = "Elliot-Win64-Shipping"
h = k32.OpenProcess(0x0010 | 0x0400, False, pid_of(PROC))
if not h: raise SystemExit(f"OpenProcess failed err={ctypes.get_last_error()}")

def read(addr, n):
    buf = (ctypes.c_ubyte * n)(); got = ctypes.c_size_t(0)
    ok = k32.ReadProcessMemory(h, ctypes.c_void_p(addr), buf, n, ctypes.byref(got))
    if not ok or got.value != n:
        raise SystemExit(f"READ FAILED at {addr:#x} err={ctypes.get_last_error()} "
                         f"({got.value}/{n}) -- NOT a zero value")
    return bytes(buf)

INST = int(sys.argv[1], 16) if len(sys.argv) > 1 else 0x7FF4DE81F970
MB   = int(sys.argv[2], 16) if len(sys.argv) > 2 else 0x7FFEE924A5D0
own = struct.unpack("<Q", read(INST + 0xE0, 8))[0]
pd  = read(MB + 0x328, 16)
hdr = read(MB, 0x30)
print(f"Owner   (+0xE0)      = {own:#018x}")
print(f"params  (+0x328)[0]  = {struct.unpack_from('<Q', pd, 0)[0]:#018x}   <- ObjectProperty InOwner")
print(f"params  (+0x330)[8]  = {struct.unpack_from('<Q', pd, 8)[0]:#018x}   <- NameProperty NameLotteryID")
print(f"mailbox cmd={struct.unpack_from('<I',hdr,0)[0]} status={struct.unpack_from('<I',hdr,4)[0]} "
      f"result={struct.unpack_from('<i',hdr,8)[0]} inst={struct.unpack_from('<Q',hdr,0x10)[0]:#x} "
      f"func={struct.unpack_from('<Q',hdr,0x18)[0]:#x} "
      f"parmsSize={struct.unpack_from('<H',hdr,0x20)[0]} numParms={struct.unpack_from('<H',hdr,0x22)[0]}")
