r"""b636 — the static-native fast path really does bypass the game thread.

    py tools/verify/b636_latency.py

WHAT THE ROW ASKS, AND WHY THE LAST ATTEMPT WAS DISCARDED
  `Mimic` short-circuits a UFunction tagged Native+Static straight to
  `UE5_CallProcessEventDirect`, instead of queueing it on `GameThreadDispatch`. The point
  is that a pure helper (KismetMathLibrary etc.) must work on an IDLE or STALLED game,
  where the queue is never drained and every queued invoke times out.

  A latency number alone cannot show that. On 2026-08-23 the attempt was abandoned
  because `Abs(-3.5)` came back with a params buffer of `-3.5, 0, -3.5`: the return slot
  MIRRORED THE INPUT, so "it ran and wrote this" was indistinguishable from "it never
  executed" ([B636-NOACCIDENT-2026-08-23]). The number was discarded rather than
  published, which was the right call.

  Two things fix that here:

  1. The DLL now CLEARS the ReturnValue slot before dispatch (Mimic.cpp, build 3349), so
     a slot that still holds the pre-fill was never written.
  2. The fixture is chosen so no ambiguity survives: **Sqrt(16.0) = 4.0**. The result is
     neither zero (which a legitimately-zero return would confuse) nor equal to the input
     (which a mirror would confuse). The rig ALSO pre-fills the whole buffer with 0xAA, so
     "untouched" is visibly distinct from both.

THE 2x2 IS THE ACTUAL TEST
  Latency with a healthy game proves nothing: both routes are fast when the game thread is
  draining the queue. The discriminating experiment suspends the GAME THREAD and repeats:

                          thread running     thread SUSPENDED
    static-native (Sqrt)      fast               fast          <- the whole point
    stateful (queued)         fast               TIMES OUT     <- proves the split is real

  A fast path that secretly queued would time out in the top-right cell. Without the
  suspended column, both rows look identical and the row measures nothing.
"""
import pathlib
import struct
import subprocess
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from mailbox_poke import (Mem, pid_of, mailbox_addr, OFF_CMD, OFF_STATUS, OFF_RESULT,
                          OFF_INSTANCE, OFF_UFUNC, OFF_ERRORMSG, OFF_PARAMS,
                          STATUS_IDLE, STATUS_DONE, STATUS_PROCESSING, CMD_IDLE)  # noqa: E402
from pipe_client import PipeClient  # noqa: E402

CMD_INVOKE = 1
NAT, STA = 0x400, 0x2000
FILL = 0xAA


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + "\n")
    sys.stdout.flush()


def invoke(m, base, inst, func, params, timeout=6.0):
    """One mailbox CMD_INVOKE. Returns (result, params_out, seconds, err)."""
    if m.i32(base + OFF_STATUS) == STATUS_PROCESSING:
        raise SystemExit("b636: mailbox already PROCESSING -- a previous command is wedged")
    m.write(base + OFF_STATUS, struct.pack("<i", STATUS_IDLE))
    m.write(base + OFF_INSTANCE, struct.pack("<Q", inst))
    m.write(base + OFF_UFUNC, struct.pack("<Q", func))
    m.write(base + OFF_RESULT, struct.pack("<i", 0x7FFFFFFF))   # poison: 0 must be WRITTEN
    m.write(base + OFF_PARAMS, params)
    m.write(base + OFF_CMD, struct.pack("<i", CMD_INVOKE))      # trigger LAST
    t0 = time.time()
    while time.time() - t0 < timeout:
        if m.i32(base + OFF_STATUS) == STATUS_DONE:
            res = m.i32(base + OFF_RESULT)
            out = m.read(base + OFF_PARAMS, len(params))
            err = m.read(base + OFF_ERRORMSG, 256).split(b"\x00")[0].decode("utf-8", "replace")
            m.write(base + OFF_CMD, struct.pack("<i", CMD_IDLE))
            return res, out, time.time() - t0, err
        time.sleep(0.002)
    m.write(base + OFF_CMD, struct.pack("<i", CMD_IDLE))
    return None, None, time.time() - t0, "TIMEOUT"


def main():
    fails = []
    with PipeClient().connect() as c:
        say("DLL build %s" % c.assert_build())
        fns = c.request("list_all_functions", limit=20000, game_only=False)["functions"]

        sq = [f for f in fns if f["class_name"] == "KismetMathLibrary"
              and f["func_name"] == "Sqrt"]
        if not sq:
            raise SystemExit("b636: KismetMathLibrary::Sqrt not present")
        sq = sq[0]
        if (sq["function_flags"] & (NAT | STA)) != (NAT | STA):
            fails.append("Sqrt is not Native+Static, so it would not take the fast path -- "
                         "the fixture is wrong, not the feature")
        say("fast-path fixture : %s::%s  parms=%d size=%d flags=0x%X"
            % (sq["class_name"], sq["func_name"], sq["num_parms"], sq["parms_size"],
               sq["function_flags"]))

        # A stateful, ZERO-PARAM function on a live actor. Zero-param because the
        # parameterised invoke path returns -4 for every function, new and old alike --
        # a separate issue that would otherwise be mistaken for a routing failure.
        act = [x for x in c.request("find_instances", class_name="DumperTestActor",
                                    max_results=20).get("instances") or []
               if not x.get("name", "").startswith("Default__")]
        st = [f for f in fns if f["class_name"] == "DumperTestActor"
              and f["num_parms"] == 0
              and (f["function_flags"] & (NAT | STA)) != (NAT | STA)]
        if not act or not st:
            raise SystemExit("b636: need a live DumperTestActor and a zero-param stateful func")
        st = st[0]
        say("queued fixture    : DumperTestActor::%s (flags=0x%X)"
            % (st["func_name"], st["function_flags"]))

        cdo = [x for x in c.request("find_instances", class_name="KismetMathLibrary",
                                    max_results=5).get("instances") or []]
        if not cdo:
            raise SystemExit("b636: no KismetMathLibrary object to use as the invoke target")

        inst_sq = int(cdo[0]["addr"], 16)
        func_sq = int(sq["func_addr"], 16)
        inst_st = int(act[0]["addr"], 16)
        func_st = int(st["func_addr"], 16)

    pid = pid_of("DumperTest")
    m = Mem(pid)
    base = mailbox_addr("DumperTest")
    say("mailbox @ 0x%X" % base)

    # ---------------------------------------------------------------- the fix
    # Buffer pre-filled with 0xAA, input 16.0 at +0. If the return slot at +8 still
    # reads 0xAA... the clear never happened; if it reads 16.0 it mirrored the input.
    buf = bytearray([FILL] * 32)
    buf[0:8] = struct.pack("<d", 16.0)
    res, out, dt, err = invoke(m, base, inst_sq, func_sq, bytes(buf))
    say("")
    if out is None:
        fails.append("the fast-path invoke TIMED OUT with a healthy game thread")
        say("fast path: TIMEOUT")
    else:
        ret = struct.unpack("<d", out[8:16])[0]
        say("Sqrt(16.0) -> return slot = %r   result=%s  %.1f ms" % (ret, res, dt * 1000))
        say("  raw buffer: %s" % out[:24].hex())
        if out[8:16] == bytes([FILL] * 8):
            fails.append("the return slot still holds the 0xAA pre-fill -- it was never "
                         "cleared and never written")
        elif abs(ret - 16.0) < 1e-9:
            fails.append("the return slot MIRRORS THE INPUT (16.0) -- this is exactly the "
                         "ambiguity that made the 2026-08-23 measurement unpublishable")
        elif abs(ret - 4.0) > 1e-9:
            fails.append("Sqrt(16.0) returned %r, expected 4.0" % ret)
        else:
            say("  OK: 4.0 -- not zero, not the input, not the pre-fill. Unambiguous.")

    t_fast_running = dt
    res2, out2, t_queued_running, err2 = invoke(m, base, inst_st, func_st, bytes(32))
    say("queued  (thread running) : result=%s  %.1f ms%s"
        % (res2, t_queued_running * 1000, "" if out2 is not None else "  TIMEOUT"))

    # ------------------------------------------------------- the 2x2, suspended
    say("")
    say("suspending the GAME THREAD -- the fast path must survive it, the queue must not")
    sp = pathlib.Path(__file__).with_name("suspend.py")
    out_t = subprocess.run([sys.executable, str(sp), "threads", "DumperTest"],
                           capture_output=True, text=True, errors="replace").stdout
    tid = None
    for line in out_t.splitlines():
        if "main thread" in line and "tid=" in line:
            tid = line.split("tid=", 1)[1].split()[0].strip(",")
    if not tid:
        raise SystemExit("b636: could not identify the game thread")
    subprocess.run([sys.executable, str(sp), "suspend-tid", "DumperTest", tid],
                   capture_output=True, text=True)
    try:
        time.sleep(0.4)
        buf2 = bytearray([FILL] * 32)
        buf2[0:8] = struct.pack("<d", 16.0)
        r3, o3, t_fast_susp, _ = invoke(m, base, inst_sq, func_sq, bytes(buf2), timeout=6.0)
        if o3 is None:
            say("fast path (SUSPENDED)    : TIMEOUT after %.1f s" % t_fast_susp)
            fails.append("*** the static-native fast path TIMED OUT with the game thread "
                         "suspended -- it is not bypassing GameThreadDispatch at all, which "
                         "is the entire claim b636 makes")
        else:
            v = struct.unpack("<d", o3[8:16])[0]
            say("fast path (SUSPENDED)    : Sqrt(16.0)=%r  %.1f ms" % (v, t_fast_susp * 1000))
            if abs(v - 4.0) > 1e-9:
                fails.append("fast path returned %r while suspended, expected 4.0" % v)

        r4, o4, t_q_susp, _ = invoke(m, base, inst_st, func_st, bytes(32), timeout=6.0)
        if o4 is None:
            say("queued    (SUSPENDED)    : TIMEOUT after %.1f s  <- correct, and it is "
                "what makes the row above mean something" % t_q_susp)
        else:
            say("queued    (SUSPENDED)    : returned in %.1f ms" % (t_q_susp * 1000))
            fails.append("the QUEUED route completed with the game thread suspended -- then "
                         "the suspend did not bite, and the fast-path result proves nothing")
    finally:
        subprocess.run([sys.executable, str(sp), "resume-tid", "DumperTest", tid],
                       capture_output=True, text=True)
        say("(game thread resumed)")

    say("")
    if fails:
        say("FAIL (%d)" % len(fails))
        for f in fails:
            say("  - %s" % f)
        return 1
    say("PASS -- the static-native route returns a correct, unambiguous 4.0 even with the "
        "game thread suspended, while the queued route times out under the same condition")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
