r"""`[TG1-CLEARALL-RESULT-2026-09-09]` — make "Clear all markers" REALLY fail, live in CE.

    py tools/verify/tg1_clearall_result_gate.py probe                # resolve + dump the mailbox
    py tools/verify/tg1_clearall_result_gate.py spin --seconds 25    # arm, then tick the row in CE
    py tools/verify/tg1_clearall_result_gate.py restore              # courtesy teardown + read-back

THE ROW. `GenerateClearAll` claimed success from the ATTEMPT: it wrote CMD_TELEPORT, waited on
`AppendMailboxWait`, and reported "all markers cleared". ⛔ **`STATUS_DONE` IS NOT A SUCCESS
SIGNAL** — `Mimic.cpp`'s `SetError` writes the negative code to `result` and THEN sets
`status = STATUS_DONE`, and `AppendMailboxWait` polls `OffStatus` **only**. So a REJECTED command
satisfied that wait exactly like a successful one: the row auto-closed the Lua Engine window (this
repo's documented clean-success-ONLY signal) and unticked silently. The fix re-reads `OffResult`
and gates on it. This rig is the live proof, because a unit test can only pin the emitted TEXT.

⭐ THE MANUFACTURE, and why it is this one. The emitted loop writes the slot itself
(`writeQword(mb + 0x18, slot)`) and the DLL reads it back at `Mimic.cpp:1049` **after** it has
seen `cmd != 0`. `Wirbel::ClearMarker` opens with
`if (slot < 0 || slot >= Grimoire::TELEPORT_SLOTS) return TP_ERR_EMPTY_MARKER;` — a real, SHIPPED
rejection path (-6). So overwriting that one input word between the Lua's write and the DLL's read
makes the actual owner of the op reject it for its own reason. Nothing is faked: not the result
word, not the status, not the error string.

⛔ WHY NOT the alternatives, each rejected for a stated reason:
  * **Writing `OffResult` directly** — exercises only the Lua branch, and makes the two sides
    CONTRADICT (the DLL log would say rc=0 while CE says -6). Fails the both-sides bar by
    construction.
  * **Zeroing `g_cachedGNames`** so `EnsureInitialized` fails (-10) — works, but the variable is
    not exported (the address must be RIP-decoded out of `UE5_GetGNamesAddr`), it disables EVERY
    mailbox command process-wide while armed, and it lands on exactly the vacuous arm the
    generator's own comment concedes ("whenever it fires there are no markers to clear").
  * **Editing the loop to `for slot = 0, 3`** — deterministic and reaches the same -6, but then
    the verified text is NOT the shipped text. Kept as the fallback if the race proves unwinnable.
  * **Suspending threads, as `sw3_mailbox_busy.py` does** — sw3 suspends because it needs `cmd` to
    STAY non-zero against the poller that clears it. Here the poller must RUN to read our slot;
    suspending would produce a timeout, which is the wrong dialog.

⚠ IT IS A RACE, AND IT DOES NOT NEED TO BE WON EVERY TIME. The window between the Lua's slot
write and the DLL's read is sub-millisecond; a tight `WriteProcessMemory` loop lands many writes
inside it. The loop runs three times and the gate `break`s on the FIRST non-zero, so losing one
iteration still leaves two. If all three lose, just tick again — the write is non-destructive.

⛔ AND IT MUST NOT GO THROUGH THE PIPE'S `write_mem`: a JSON round-trip is ~0.2 ms, the same order
as the window itself. This is the same conclusion `sw3` reached, for a different reason — nothing
is suspended here, so the pipe stays fully alive; it is simply far too slow.

⚠ NOTHING PERSISTENT IS ARMED. `+0x18` is a per-command INPUT word: every subsequent command
rewrites it, and `StopThread` memsets the whole struct. `Wirbel`'s bounds check runs BEFORE
`s_markers[slot]` is indexed, so slot 99 is refused, never dereferenced. Killing this rig
mid-spin leaves nothing behind. `restore` exists as courtesy and as a read-back witness, in the
`mutate_guard.py` discipline: a write returning ok is not the same as the memory holding it.
"""
from __future__ import annotations

import argparse
import ctypes
import ctypes.wintypes as wt
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from sw1_worker_fault import modules, pid_of          # noqa: E402
from sw3_mailbox_busy import mailbox_addr             # noqa: E402  -- resolved by EXPORT, not name

OFF_CMD = 0x00
OFF_STATUS = 0x04
OFF_RESULT = 0x08
OFF_INSTANCE = 0x10          # op   (CLEAR_MARKER == 6)
OFF_UFUNC = 0x18             # slot -- THE WORD WE CLOBBER
OFF_ERRMSG = 0x228           # errorMsg[] -- the third witness

BAD_SLOT = 99                # >= Grimoire::TELEPORT_SLOTS, so ClearMarker returns -6


def _open(pid: int):
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    k32.OpenProcess.restype = wt.HANDLE
    h = k32.OpenProcess(0x1F0FFF, False, pid)               # PROCESS_ALL_ACCESS
    if not h:
        raise SystemExit("OpenProcess(%d) failed (%d)" % (pid, ctypes.get_last_error()))
    return k32, h


def _read(k32, h, addr: int, n: int) -> bytes:
    buf = ctypes.create_string_buffer(n)
    got = ctypes.c_size_t(0)
    if not k32.ReadProcessMemory(h, ctypes.c_void_p(addr), buf, n, ctypes.byref(got)):
        raise SystemExit("ReadProcessMemory(0x%X) failed (%d)" % (addr, ctypes.get_last_error()))
    return buf.raw[:got.value]


def _write(k32, h, addr: int, data: bytes) -> None:
    n = ctypes.c_size_t(0)
    if not k32.WriteProcessMemory(h, ctypes.c_void_p(addr), data, len(data), ctypes.byref(n)):
        raise SystemExit("WriteProcessMemory(0x%X) failed (%d)" % (addr, ctypes.get_last_error()))


def dump(k32, h, mb: int) -> None:
    b = _read(k32, h, mb, 0x20)
    i32 = lambda o: int.from_bytes(b[o:o + 4], "little", signed=True)      # noqa: E731
    u64 = lambda o: int.from_bytes(b[o:o + 8], "little")                   # noqa: E731
    print("  cmd=%d  status=%d  result=%d  op=%d  slot=%d"
          % (i32(OFF_CMD), i32(OFF_STATUS), i32(OFF_RESULT),
             u64(OFF_INSTANCE), u64(OFF_UFUNC)))
    msg = _read(k32, h, mb + OFF_ERRMSG, 128).split(b"\x00")[0]
    if msg:
        print("  errorMsg='%s'" % msg.decode("utf-8", "replace"))


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("action", choices=["probe", "spin", "restore"])
    ap.add_argument("--process", default="DumperTest.exe")
    ap.add_argument("--seconds", type=float, default=25.0)
    a = ap.parse_args()

    pid = pid_of(a.process)
    if not pid:
        raise SystemExit("no process named %s" % a.process)
    print("process    : %s pid %d" % (a.process, pid))
    mb = mailbox_addr(pid)
    print("g_invokeMailbox at 0x%X" % mb)

    k32, h = _open(pid)

    if a.action == "probe":
        dump(k32, h, mb)
        return 0

    if a.action == "restore":
        _write(k32, h, mb + OFF_UFUNC, (0).to_bytes(8, "little"))
        back = int.from_bytes(_read(k32, h, mb + OFF_UFUNC, 8), "little")
        # ⛔ READ IT BACK. "the write returned ok" is a different claim from "the memory
        # holds it" -- mutate_guard.py's rule, and it costs one call.
        print("slot word read-back: %d  %s" % (back, "OK" if back == 0 else "*** NOT RESTORED ***"))
        dump(k32, h, mb)
        return 0 if back == 0 else 1

    # spin
    payload = BAD_SLOT.to_bytes(8, "little")
    addr = ctypes.c_void_p(mb + OFF_UFUNC)
    n = ctypes.c_size_t(0)
    deadline = time.time() + a.seconds
    print("SPINNING slot:=%d for %.0fs -- tick 'Teleport: Clear all markers' in CE NOW."
          % (BAD_SLOT, a.seconds))
    print("Expect ONE dialog: [Teleport] slot 0 was NOT cleared (code -6), and the Lua")
    print("Engine window STAYS OPEN (the close is unreachable once hadError is set).")
    writes = 0
    while time.time() < deadline:
        k32.WriteProcessMemory(h, addr, payload, 8, ctypes.byref(n))
        writes += 1
    print("done: %d writes (%.0f/s)" % (writes, writes / a.seconds))
    dump(k32, h, mb)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
