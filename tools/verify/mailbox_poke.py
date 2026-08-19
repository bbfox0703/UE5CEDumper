"""MB3: drive the CE mailbox from Python -- the ORDINARY dispatch path, no CE.

    py mailbox_poke.py DumperTest              # CMD_QUERY_PTR x2 (GWorld, GameEngine)
    py mailbox_poke.py DumperTest --repeat 20  # hammer it: one throw would end the loop

WHAT MB3 CHANGED (audit L12, build 3263), in `Mimic.cpp`, which **no test target
compiles** -- so none of it has ever executed:
  * the dispatch `switch` now runs inside `Routine::RunTickGuarded`, so one throwing
    handler loses that command instead of ending the mailbox for the session;
  * `CompoundOpGuard`'s destructor detects unwinding (`std::uncaught_exceptions()`
    against the count at entry) and publishes `-11` instead of the stale `result` --
    which for `HandleInvokeByName` was normally **0**, i.e. it reported SUCCESS for a
    command that threw.

⚠ THE RISK IS NOT THE THROW PATH. The row says so explicitly: the throw is hard to
trigger, but if the lambda refactor broke PLAIN dispatch then every CE command breaks
at once. So the check that matters is "do ordinary mailbox commands still work" --
which is what this does, and it needs no Cheat Engine at all (category A rather than
the row's category B).

WHY CMD_QUERY_PTR (13). It is READ-ONLY and THREAD-AGNOSTIC: it reads a cached global
and runs on the mailbox polling thread even while the game thread is idle. So it
exercises the refactored dispatch switch without changing a single byte of game state
and without depending on the ProcessEvent hook being alive.

PROTOCOL, in the order the contract requires: inputs first, `cmd` LAST as the trigger
(the DLL polls `cmd`), then poll `status` to DONE. The deadline is real wall-clock.
"""
import argparse
import ctypes
import struct
import subprocess
import sys
import time
from ctypes import wintypes

k32 = ctypes.WinDLL("kernel32", use_last_error=True)
k32.OpenProcess.restype = ctypes.c_void_p
k32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
k32.ReadProcessMemory.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p,
                                  ctypes.c_size_t, ctypes.POINTER(ctypes.c_size_t)]
k32.WriteProcessMemory.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p,
                                   ctypes.c_size_t, ctypes.POINTER(ctypes.c_size_t)]
k32.CloseHandle.argtypes = [ctypes.c_void_p]

PROCESS_VM_READ, PROCESS_VM_WRITE, PROCESS_VM_OPERATION, PROCESS_QUERY_INFORMATION = (
    0x0010, 0x0020, 0x0008, 0x0400)

# MailboxData offsets -- Mimic.h, #pragma pack(1)
OFF_CMD, OFF_STATUS, OFF_RESULT, OFF_INITSTATE = 0x000, 0x004, 0x008, 0x00C
OFF_INSTANCE, OFF_UFUNC = 0x010, 0x018
OFF_ERRORMSG = 0x228          # char[256]
OFF_PARAMS = 0x328            # uint8_t[1024] -- NOT 0x030; getting this wrong reads
                              # the tail of funcName and reports a silent all-zero output
CMD_IDLE, CMD_QUERY_PTR = 0, 13
STATUS_IDLE, STATUS_DONE, STATUS_PROCESSING = 0, 1, 0xFF
INIT_READY = 2
QUERY_OP_GWORLD, QUERY_OP_GAME_ENGINE = 0, 1


class Mem:
    def __init__(self, pid):
        self.h = k32.OpenProcess(PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_VM_OPERATION
                                 | PROCESS_QUERY_INFORMATION, False, pid)
        if not self.h:
            raise SystemExit(f"mailbox_poke: OpenProcess({pid}) failed "
                             f"err={ctypes.get_last_error()}")

    def read(self, addr, n):
        buf = (ctypes.c_ubyte * n)()
        got = ctypes.c_size_t(0)
        ok = k32.ReadProcessMemory(self.h, ctypes.c_void_p(addr), buf, n, ctypes.byref(got))
        # FAIL LOUDLY: a zero returned for "read failed" is indistinguishable from a
        # zero that IS the answer, and that has already produced a wrong verdict here.
        if not ok or got.value != n:
            raise SystemExit(f"mailbox_poke: READ FAILED at {addr:#x} "
                             f"err={ctypes.get_last_error()} got={got.value}/{n}")
        return bytes(buf)

    def write(self, addr, data):
        buf = (ctypes.c_ubyte * len(data)).from_buffer_copy(data)
        put = ctypes.c_size_t(0)
        ok = k32.WriteProcessMemory(self.h, ctypes.c_void_p(addr), buf, len(data),
                                    ctypes.byref(put))
        if not ok or put.value != len(data):
            raise SystemExit(f"mailbox_poke: WRITE FAILED at {addr:#x} "
                             f"err={ctypes.get_last_error()} put={put.value}/{len(data)}")

    def i32(self, addr):
        return struct.unpack("<i", self.read(addr, 4))[0]

    def u64(self, addr):
        return struct.unpack("<Q", self.read(addr, 8))[0]


def pid_of(stem):
    out = subprocess.run(["tasklist", "/fo", "csv", "/nh"],
                         capture_output=True, text=True, errors="replace").stdout
    hits = [(p[0], int(p[1])) for p in
            ([x.strip('"') for x in l.split('","')] for l in out.splitlines())
            if len(p) >= 2 and p[0].lower().startswith(stem.lower()[:24]) and p[1].isdigit()]
    if not hits:
        raise SystemExit(f"mailbox_poke: process {stem!r} not found")
    if len(hits) > 1:
        raise SystemExit(f"mailbox_poke: ambiguous {stem!r}: {hits}")
    return hits[1 - 1][1]


def mailbox_addr(stem):
    r = subprocess.run([sys.executable,
                        str(__import__("pathlib").Path(__file__).with_name("mailbox_addr.py")),
                        stem], capture_output=True, text=True, errors="replace")
    for line in r.stdout.splitlines():
        if "g_invokeMailbox" in line:
            return int(line.split("=")[1].strip(), 16)
    raise SystemExit(f"mailbox_poke: could not resolve g_invokeMailbox:\n{r.stdout}{r.stderr}")


def poke(m, base, op, timeout=10.0, cmd=CMD_QUERY_PTR, value=0):
    """One mailbox round trip. Returns (result, paramsData[:32], elapsed, errorMsg).

    `op` goes in instanceAddr and `value` in ufuncAddr -- the convention every
    op-coded command in Mimic.h uses (CMD_FOREGROUND, CMD_TELEPORT, CMD_QUERY_PTR...).
    """
    st = m.i32(base + OFF_STATUS)
    if st == STATUS_PROCESSING:
        raise SystemExit("mailbox_poke: mailbox is already PROCESSING -- a previous command "
                         "is wedged; that is itself the MB3 failure mode, capture the logs")
    # Clear status FIRST. The DLL leaves it at DONE after the previous command, so a
    # poller that only waits for DONE returns instantly with the PREVIOUS result --
    # which is how this rig first reported a bogus failure on its second dispatch.
    m.write(base + OFF_STATUS, struct.pack("<i", STATUS_IDLE))
    m.write(base + OFF_INSTANCE, struct.pack("<Q", op))        # inputs...
    m.write(base + OFF_UFUNC, struct.pack("<Q", value))        # ...op's value arg
    m.write(base + OFF_RESULT, struct.pack("<i", 0x7FFFFFFF))  # poison: 0 must be WRITTEN
    m.write(base + OFF_PARAMS, b"\x00" * 32)                   # so an all-zero out is real
    m.write(base + OFF_CMD, struct.pack("<i", cmd))            # ...cmd LAST = the trigger

    # A REAL wall-clock deadline. Counting iterations against a millisecond constant is
    # how a "10 s" timeout became ~155 s elsewhere in this project.
    t0 = time.time()
    while time.time() - t0 < timeout:
        if m.i32(base + OFF_STATUS) == STATUS_DONE:
            res = m.i32(base + OFF_RESULT)
            params = m.read(base + OFF_PARAMS, 32)
            err = m.read(base + OFF_ERRORMSG, 256).split(b"\x00")[0].decode("utf-8", "replace")
            m.write(base + OFF_CMD, struct.pack("<i", CMD_IDLE))
            return res, params, time.time() - t0, err
        time.sleep(0.005)
    st = m.i32(base + OFF_STATUS)
    raise SystemExit(f"mailbox_poke: TIMEOUT after {timeout}s, status={st:#x}. "
                     f"status=0 means the DLL never picked the command up (stale mailbox "
                     f"address); status=0xFF means it took it and wedged -- which is exactly "
                     f"the regression MB3 is about.")


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("process")
    ap.add_argument("--repeat", type=int, default=1)
    ap.add_argument("--cmd", type=int, default=None,
                    help="raw Cmd number (e.g. 12 = CMD_FOREGROUND). Default: the "
                         "CMD_QUERY_PTR smoke test.")
    ap.add_argument("--op", type=int, default=0, help="op code -> instanceAddr")
    ap.add_argument("--value", type=int, default=0, help="value -> ufuncAddr")
    a = ap.parse_args()

    # An all-digits `process` is a PID. `pid_of` refuses to guess between same-named
    # processes, and these rigs run under python.exe, so the name "python" is always
    # ambiguous with the rig's own interpreter.
    pid = int(a.process) if a.process.isdigit() else pid_of(a.process)
    base = mailbox_addr(str(pid) if a.process.isdigit() else a.process)
    m = Mem(pid)
    init = m.i32(base + OFF_INITSTATE)
    print(f"pid {pid}  mailbox {base:#x}  initState={init} "
          f"({'READY' if init == INIT_READY else 'NOT READY'})")
    if init != INIT_READY:
        raise SystemExit("mailbox_poke: initState is not READY; the poll loop may not be "
                         "running yet and a timeout would be meaningless")

    if a.cmd is not None:
        res, params, dt, err = poke(m, base, a.op, cmd=a.cmd, value=a.value)
        a0, a1 = struct.unpack("<QQ", params[:16])
        print(f"  cmd={a.cmd} op={a.op} value={a.value} -> result={res}  {dt*1000:.1f} ms  "
              f"out0={a0:#x} out1={a1:#x}" + (f"  err={err!r}" if err else ""))
        return 0

    bad = 0
    for i in range(a.repeat):
        for name, op in (("GWORLD", QUERY_OP_GWORLD), ("GAME_ENGINE", QUERY_OP_GAME_ENGINE)):
            res, params, dt, err = poke(m, base, op)
            a0, a1 = struct.unpack("<QQ", params[:16])
            ok = (res == 0)
            bad += 0 if ok else 1
            if i == 0 or not ok:
                extra = f"  err={err!r}" if err else ""
                print(f"  {name:<12} result={res:<3} {dt*1000:6.1f} ms  "
                      f"out0={a0:#x} out1={a1:#x}  "
                      f"{'OK' if ok else '*** FAILED ***'}{extra}")
    if a.repeat > 1:
        print(f"  repeated {a.repeat}x2 = {a.repeat*2} dispatches, {bad} failure(s)")
    print("RESULT:", "PASS -- ordinary mailbox dispatch works" if bad == 0
          else f"FAIL -- {bad} dispatch(es) did not return 0")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
