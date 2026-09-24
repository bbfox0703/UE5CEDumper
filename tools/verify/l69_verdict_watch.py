#!/usr/bin/env python3
r"""L69 step 3 `[W5-OFFSETS-UNMEASURED]`: watch the offsets verdict ACROSS a CE Disable / Enable, from outside CE.

    py tools/verify/l69_verdict_watch.py [--process DumperTest-Win64-Shipping] [--seconds 90]

CE's [ENABLE] of the table's `init` record blocks until READY, so CE itself cannot see the verdict while the
re-scan runs. This rig can: it polls CMD_OFFSETS_VERDICT (16) through the mailbox (mailbox_poke's ordinary
dispatch path; the command is init-exempt, and the poller thread is restarted before UE5_Init) every 50 ms, and
prints one line each time (initState, result, reason) CHANGES, with a timestamp. Start it after READY, then untick
and re-tick the hidden child `Inject DLL + Start Pipe Server` in CE -- on a record that INJECTED the DLL itself: one
that found it already loaded and serving does not own it, and its untick tears nothing down (the B30 guard).
Measured 2026-09-23 on a fixed DLL: 1/'' (READY) -> `no answer` after the Disable (it STOPS the mailbox poller, so
the verdict is readable only through the C ABI, which says 0/'probe-not-run') -> 0/'probe-not-run' once the poller
restarts at the re-Enable -> 1/'' as soon as the re-scan's offset probe publishes, which is while initState is still
1, about 225 ms BEFORE READY (the deferred &GEngine scan runs after it) -> 1/'' at READY. A DLL whose Disable does
not reset the verdict answers 1/'' at every poll, the re-scan's pre-probe polls included: a stale TRUE. `no answer`
is not treated as a failure. It never writes anything but the mailbox's own command fields.
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
