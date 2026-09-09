r"""SW3's other arm — the DLL is ALIVE and the mailbox is BUSY. The script must say so.

    py tools/verify/sw3_mailbox_busy.py arm      # freeze the target, make the mailbox busy
    <tick the pushed "Invoke: ..." record in Cheat Engine>
    py tools/verify/sw3_mailbox_busy.py disarm   # ALWAYS -- the game stays frozen until you do

THE ROW. `a55ef2b5` split two failures the generated invoke script used to conflate: a mailbox
that is genuinely BUSY, and a mailbox that cannot be READ at all. `[SW3-UNTICK-2026-09-09]` proved
the second live — the game was killed and the script said *"the contract symbol resolved to ...
but that memory could not be READ"* rather than blaming a busy mailbox. This rig is the other
side, and it is what makes the pair mean anything: **with the DLL ALIVE and the mailbox actually
busy, the script must say BUSY.** Two manufactured conditions, two different correct messages —
that is what "the script must not be guessing" comes down to.

⭐ THE MANUFACTURE. `CeLuaHygiene.AppendIdleWait` emits `while _idleCmd ~= 0 do` around
`readInteger(mb + OffCmd)` and gives up after `MailboxIdleWaitMs` (1500 ms). So a non-zero `cmd`
word IS a busy mailbox to every reader — it is the same field the DLL sets when a command is in
flight.

⛔ WHY THE PROCESS IS SUSPENDED, which is not incidental. The first attempt just wrote the word
and read back **0**: the DLL's own poller sees a command id it does not recognise and CLEARS it,
inside the same second. Spin-writing does not fix it either — the Lua loop exits on the FIRST
zero it happens to sample. Suspending every thread stops the poller while leaving memory
perfectly readable, which is all Cheat Engine needs (`ReadProcessMemory` works on a suspended
process).

⛔ AND THE WRITE THEREFORE CANNOT GO THROUGH `write_mem`: that command is served by the pipe
thread INSIDE the target, suspended along with everything else. `arm` uses
`WriteProcessMemory` from this process instead.

⚠ `cmd` IS THE TRIGGER WORD — the DLL polls it and executes what it finds. `0x7FFFFFFF` is
deliberately not a valid command id, so a poll that catches it rejects it rather than executing
something; `disarm` writes 0 back BEFORE resuming anyway.

⚠ THE GAME IS FROZEN BETWEEN `arm` AND `disarm`. Always run `disarm`.
"""
from __future__ import annotations

import argparse
import ctypes
import ctypes.wintypes as wt
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from sw1_worker_fault import modules, pid_of          # noqa: E402

SYMBOL = "g_invokeMailbox"
OFF_CMD = 0x00
BUSY = 0x7FFFFFFF
CANDIDATES = ["UE5Dumper.dll", "version.dll", "dxgi.dll", "winmm.dll", "dinput8.dll"]


def mailbox_addr(pid: int) -> int:
    """base + RVA, with the RVA read from the file ACTUALLY MAPPED in the target.

    Both halves matter and both were learned the hard way in `sw1_worker_fault.py`: a proxy is a
    different binary from `dist/UE5Dumper.dll` (taking the RVA from the wrong one crashed a
    game), and the system owns `dxgi`/`version`/`winmm`/`dinput8` too — so the module is chosen
    by which one actually EXPORTS the symbol, never by name."""
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    k32.LoadLibraryExW.restype = wt.HMODULE
    k32.GetProcAddress.restype = ctypes.c_void_p
    k32.GetProcAddress.argtypes = [wt.HMODULE, ctypes.c_char_p]
    for name, base, path in modules(pid):
        if name.lower() not in [c.lower() for c in CANDIDATES]:
            continue
        h = k32.LoadLibraryExW(path, None, 0x00000001)      # DONT_RESOLVE_DLL_REFERENCES
        if not h:
            continue
        a = k32.GetProcAddress(h, SYMBOL.encode())
        if a:
            print("module     : %s @ 0x%X" % (name, base))
            print("             %s" % path)
            print("             %s rva 0x%X" % (SYMBOL, a - h))
            return base + (a - h)
    raise SystemExit("no module in pid %d exports %s" % (pid, SYMBOL))


def _open(pid: int):
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    k32.OpenProcess.restype = wt.HANDLE
    h = k32.OpenProcess(0x1F0FFF, False, pid)               # PROCESS_ALL_ACCESS
    if not h:
        raise SystemExit("OpenProcess(%d) failed (%d)" % (pid, ctypes.get_last_error()))
    return k32, h


def poke(pid: int, addr: int, v: int) -> None:
    k32, h = _open(pid)
    try:
        buf = v.to_bytes(4, "little")
        n = ctypes.c_size_t(0)
        if not k32.WriteProcessMemory(h, ctypes.c_void_p(addr), buf, 4, ctypes.byref(n)) \
                or n.value != 4:
            raise SystemExit("WriteProcessMemory failed at 0x%X (%d)"
                             % (addr, ctypes.get_last_error()))
    finally:
        k32.CloseHandle(h)


def peek(pid: int, addr: int) -> int:
    k32, h = _open(pid)
    try:
        buf = (ctypes.c_ubyte * 4)()
        n = ctypes.c_size_t(0)
        if not k32.ReadProcessMemory(h, ctypes.c_void_p(addr), buf, 4, ctypes.byref(n)):
            raise SystemExit("ReadProcessMemory failed at 0x%X" % addr)
        return int.from_bytes(bytes(buf), "little")
    finally:
        k32.CloseHandle(h)


def _threads(pid: int):
    class THREADENTRY32(ctypes.Structure):
        _fields_ = [("dwSize", wt.DWORD), ("cntUsage", wt.DWORD),
                    ("th32ThreadID", wt.DWORD), ("th32OwnerProcessID", wt.DWORD),
                    ("tpBasePri", ctypes.c_long), ("tpDeltaPri", ctypes.c_long),
                    ("dwFlags", wt.DWORD)]
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    k32.CreateToolhelp32Snapshot.restype = wt.HANDLE
    snap = k32.CreateToolhelp32Snapshot(0x00000004, 0)      # TH32CS_SNAPTHREAD
    out = []
    try:
        te = THREADENTRY32()
        te.dwSize = ctypes.sizeof(THREADENTRY32)
        ok = k32.Thread32First(snap, ctypes.byref(te))
        while ok:
            if te.th32OwnerProcessID == pid:
                out.append(te.th32ThreadID)
            ok = k32.Thread32Next(snap, ctypes.byref(te))
    finally:
        k32.CloseHandle(snap)
    return out


def freeze(pid: int, resume: bool) -> int:
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    k32.OpenThread.restype = wt.HANDLE
    n = 0
    for tid in _threads(pid):
        h = k32.OpenThread(0x0002, False, tid)              # THREAD_SUSPEND_RESUME
        if not h:
            continue
        try:
            if (k32.ResumeThread(h) if resume else k32.SuspendThread(h)) != 0xFFFFFFFF:
                n += 1
        finally:
            k32.CloseHandle(h)
    return n


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("action", choices=["arm", "disarm", "show"])
    ap.add_argument("--image", default="DumperTest.exe")
    a = ap.parse_args()

    pid = pid_of(a.image)
    mb = mailbox_addr(pid)
    cmd_addr = mb + OFF_CMD
    print("pid        : %d" % pid)
    print("mailbox    : 0x%X   cmd word @ 0x%X" % (mb, cmd_addr))
    now = peek(pid, cmd_addr)
    print("cmd now    : 0x%X %s" % (now, "(IDLE)" if now == 0 else "(non-zero)"))

    if a.action == "show":
        return 0

    if a.action == "arm":
        if now != 0:
            raise SystemExit("cmd is already 0x%X -- the mailbox is not idle, so arming would "
                             "not be the manufacture it claims to be" % now)
        n = freeze(pid, resume=False)
        print("suspended  : %d thread(s)" % n)
        poke(pid, cmd_addr, BUSY)
        back = peek(pid, cmd_addr)
        print("armed      : cmd = 0x%X %s" % (back, "OK" if back == BUSY else "MISMATCH"))
        if back != BUSY:
            freeze(pid, resume=True)
            raise SystemExit("the write did not stick even with the process suspended")
        print("\nNOW: tick the pushed \"Invoke: ...\" record in Cheat Engine.")
        print("EXPECT: a BUSY message, NOT 'could not be READ', and the record unticks.")
        print("THEN: py tools/verify/sw3_mailbox_busy.py disarm   <-- the game is FROZEN "
              "until you run it")
        return 0

    poke(pid, cmd_addr, 0)
    back = peek(pid, cmd_addr)
    n = freeze(pid, resume=True)
    print("disarmed   : cmd = 0x%X %s ; resumed %d thread(s)"
          % (back, "(IDLE, restored)" if back == 0 else "MISMATCH", n))
    return 0 if back == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
