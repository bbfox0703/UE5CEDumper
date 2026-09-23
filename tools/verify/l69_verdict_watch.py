#!/usr/bin/env python3
r"""L69 step 3 `[W5-OFFSETS-UNMEASURED]`: watch the offsets verdict ACROSS a CE Disable / Enable, from outside CE.

    py tools/verify/l69_verdict_watch.py [--process DumperTest-Win64-Shipping] [--seconds 90]

CE's [ENABLE] of the table's `init` record blocks until READY, so CE itself cannot see the verdict while the
re-scan runs. This rig can: it polls CMD_OFFSETS_VERDICT (16) through the mailbox (mailbox_poke's ordinary
dispatch path; the command is init-exempt, and the poller thread is restarted before UE5_Init) every 50 ms, and
prints one line each time (initState, result, reason) CHANGES, with a timestamp. Start it, then untick and re-tick
`init` in CE. A fixed DLL goes 1/'' (READY) -> 0/'probe-not-run' after the Disable -> stays 0/'probe-not-run'
through the scan -> 1/'' at READY. A DLL whose Disable does not reset the verdict keeps 1/'' throughout, i.e. a
stale TRUE after a Disable. A mailbox that stops answering (the Disable parks the poller) is printed as
`no answer`, not treated as a failure: that is the parked state between Disable and Enable. It never writes
anything but the mailbox's own command fields.
"""
import argparse
import os
import struct
import sys
import time

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import mailbox_poke as MP   # noqa: E402

CMD_OFFSETS_VERDICT = 16


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--process", default="DumperTest-Win64-Shipping")
    ap.add_argument("--seconds", type=float, default=90.0)
    a = ap.parse_args()
    pid = MP.pid_of(a.process)
    base = MP.mailbox_addr(a.process)
    m = MP.Mem(pid)
    print("pid %d g_invokeMailbox 0x%X" % (pid, base), flush=True)
    last, t0 = None, time.time()
    while time.time() - t0 < a.seconds:
        init = m.i32(base + MP.OFF_INITSTATE)
        try:
            if m.i32(base + MP.OFF_STATUS) == MP.STATUS_PROCESSING:
                state = (init, "busy", "")
            else:
                res, params, _, _ = MP.poke(m, base, 0, timeout=0.5, cmd=CMD_OFFSETS_VERDICT)
                state = (init, res, params.split(b"\x00")[0].decode("utf-8", "replace"))
        except SystemExit:
            state = (init, "no answer", "")
        if state != last:
            print("%s  initState=%s result=%s reason=[%s]" % (time.strftime("%H:%M:%S") + ".%03d" % (
                int(time.time() * 1000) % 1000), state[0], state[1], state[2]), flush=True)
            last = state
        time.sleep(0.05)
    return 0


if __name__ == "__main__":
    sys.exit(main())
