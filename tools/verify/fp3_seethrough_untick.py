r"""`[FP3]` — does See-through's [DISABLE] WAIT for an idle mailbox before it writes?

    py tools/verify/fp3_seethrough_untick.py arm      # manufacture a busy mailbox
    py tools/verify/fp3_seethrough_untick.py read     # what does cmd hold now?
    py tools/verify/fp3_seethrough_untick.py restore  # resume + clear, ALWAYS run this

⛔ THE ROW'S OWN VACUITY TRAP, and it is why this rig exists rather than a stopwatch.
`[R3-SEETHRU-2026-09-10]` fixed a [DISABLE] block that wrote the mailbox with no bounded
wait-for-IDLE. Unticking while the mailbox happens to be IDLE proves NOTHING: the wait
exits on its first read and the run is indistinguishable from having no wait at all. The
mailbox must be BUSY at the instant of the untick, and it must be busy DETERMINISTICALLY.

HOW THE BUSY STATE IS MANUFACTURED, and why not with a slow command. Mimic's poller clears
`cmd` back to CMD_IDLE as soon as its handler returns, so racing a real command is a
lottery. Instead the game process is SUSPENDED: the poller cannot run, so a `cmd` written
from outside stays non-zero for as long as we like. Reads and writes into a suspended
process work normally, and Cheat Engine drives the untick from its OWN process, so the Lua
under test runs at full speed against a mailbox that is frozen busy.

⭐ THE MEASUREMENT IS ONE FIELD, read back after the untick:
    cmd still == the sentinel  -> the wait HELD. It refused to write a busy mailbox.
    cmd == CMD_SEETHROUGH (14) -> it wrote straight over a command in flight. THE DEFECT.
Both outcomes are positive evidence; there is no "nothing happened" reading, which is
exactly what the row asked for.

⚠ RESTORE. `arm` suspends a real game. `restore` resumes it and puts `cmd` back to 0, and
is safe to run at any time -- run it even if the measurement failed, and especially then.
"""
from __future__ import annotations

import ctypes
import ctypes.wintypes as wt
import pathlib
import struct
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from mailbox_addr import find_export, modules, pid_of, Reader   # noqa: E402

CMD_OFF = 0x00          # CeMailboxLayout.OffCmd
STATUS_OFF = 0x04       # CeMailboxLayout.OffStatus
CMD_SEETHROUGH = 14     # what the DISABLE block stores if it does NOT wait
SENTINEL = 999          # ⭐ NOT a real command id: if the poller ever did run it would
                        #    reject this rather than perform something on the game.

k32 = ctypes.WinDLL('kernel32', use_last_error=True)
ntdll = ctypes.WinDLL('ntdll')
k32.OpenProcess.restype = ctypes.c_void_p
k32.OpenProcess.argtypes = [wt.DWORD, wt.BOOL, wt.DWORD]
k32.WriteProcessMemory.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p,
                                   ctypes.c_size_t, ctypes.POINTER(ctypes.c_size_t)]


def mailbox(pid: int) -> int:
    # ⚠ modules() yields (NAME, base) in that order -- reading it as (base, path) finds
    # nothing and would have made every later step measure a mailbox address of 0.
    r = Reader(pid)
    for name, base in modules(pid):
        if str(name).lower().endswith('ue5dumper.dll'):
            a = find_export(r, base, 'g_invokeMailbox')  # str, not bytes -- it compares decoded names
            if a:
                return a
    raise SystemExit('*** g_invokeMailbox not found -- is the DLL injected?')


def open_proc(pid: int):
    h = k32.OpenProcess(0x1F0FFF, False, pid)
    if not h:
        raise SystemExit('OpenProcess failed')
    return h


def read_u32(pid: int, addr: int) -> int:
    return struct.unpack('<I', Reader(pid).read(addr, 4))[0]


def write_u32(h, addr: int, val: int) -> None:
    n = ctypes.c_size_t(0)
    buf = struct.pack('<I', val)
    if not k32.WriteProcessMemory(h, ctypes.c_void_p(addr), buf, 4, ctypes.byref(n)):
        raise SystemExit('WriteProcessMemory failed at 0x%X' % addr)


def main() -> int:
    action = sys.argv[1] if len(sys.argv) > 1 else 'read'
    pid = pid_of('DumperTest-Win64-Shipping')
    mb = mailbox(pid)
    h = open_proc(pid)
    print('pid %d  mailbox 0x%X' % (pid, mb))

    if action == 'arm':
        cur = read_u32(pid, mb + CMD_OFF)
        print('cmd before = %d' % cur)
        if cur != 0:
            print('*** the mailbox is ALREADY busy -- refusing to arm over a real command')
            return 2
        # Order matters: write the sentinel FIRST, then freeze, so the poller never sees it.
        write_u32(h, mb + CMD_OFF, SENTINEL)
        ntdll.NtSuspendProcess(ctypes.c_void_p(h))
        back = read_u32(pid, mb + CMD_OFF)
        print('cmd after arm = %d   (process SUSPENDED, poller cannot clear it)' % back)
        if back != SENTINEL:
            print('*** the sentinel did not stick -- do NOT run the untick, restore first')
            return 2
        print('\nNow untick the See-through record in Cheat Engine, then run:')
        print('    py tools/verify/fp3_seethrough_untick.py read')
        return 0

    if action == 'read':
        cmd = read_u32(pid, mb + CMD_OFF)
        status = read_u32(pid, mb + STATUS_OFF)
        print('cmd = %d   status = %d' % (cmd, status))
        if cmd == SENTINEL:
            print('\n✅ THE WAIT HELD -- the DISABLE block refused to write a busy mailbox.')
        elif cmd == CMD_SEETHROUGH:
            print('\n⛔ DEFECT REPRODUCED -- the DISABLE block stored CMD_SEETHROUGH (14) '
                  'straight over a command in flight.')
        elif cmd == 0:
            print('\n⚠ cmd is IDLE. Either the process was resumed (the poller consumed the '
                  'sentinel) or nothing ran. This measurement is UNDECIDED, not a pass.')
        else:
            print('\n⚠ unexpected cmd %d -- UNDECIDED' % cmd)
        return 0

    if action == 'restore':
        ntdll.NtResumeProcess(ctypes.c_void_p(h))
        write_u32(h, mb + CMD_OFF, 0)
        write_u32(h, mb + STATUS_OFF, 0)
        print('resumed; cmd = %d status = %d'
              % (read_u32(pid, mb + CMD_OFF), read_u32(pid, mb + STATUS_OFF)))
        return 0

    print('unknown action: %s' % action)
    return 2


if __name__ == '__main__':
    sys.exit(main())
