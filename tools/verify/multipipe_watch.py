"""Multi-pipe Phase 1 item (6): watch events must keep arriving while the BULK lane is busy.

    py tools/verify/multipipe_watch.py            # DumperTest must be running + injected

THE ROW (todo.md, "Multi-pipe Phase 1 — residual verification: only the WATCH item is left"):
items 1-5 shipped and were verified in-game 2026-06-28; the lane-drop edge closed on Elliot
2026-07-23. **(6) watch-event delivery to the interactive lane** — "still pushes correctly while
the bulk lane is busy" — was left "verify opportunistically", which since 2026-06-28 has meant
not at all.

⭐ WHY THIS IS DONE WITH TWO PIPE CLIENTS AND NO UI. The UI's two lanes are just two
connections (`LaneRoutingPipeClient` opens an interactive "I" and a bulk "B"), and the DLL side is
per-connection by construction: `Fern::StartWatch` (Fern.cpp:6444) gives each watch its own thread
whose loop writes with `WriteLine(*ptr->owner, ...)` — `owner` being the connection that
registered it. So the question "does a busy bulk lane starve the interactive lane's watch pushes?"
is answered by two raw connections, and answered ON BOTH SIDES: we count what actually arrived,
rather than looking at a panel that could equally be showing a stale cache.

⚠ Fern::kMaxPipeInstances = 3 and the UI takes 2. **Do not run this with the UI connected.**

THE CONTROLS, because a push count on its own proves very little:
  * **`TickCount` is the mover** (1 Hz) and **`FrozenInt` is the still one** (424242, written once).
    Both are watched. If FrozenInt's bytes ever change, we are not reading what we think we are;
    if TickCount's never do, the sample is not ticking and every count below is meaningless.
  * **An IDLE window of the same length is measured first**, so "pushes kept coming" is a
    comparison and not an impression. The busy window has to match it, not merely be non-zero.
  * ⚠ The watch thread pushes EVERY interval regardless of whether the bytes changed (there is no
    change detection in that loop), so the count measures DELIVERY and the values measure the game.
    Those are two different assertions and this rig keeps them apart.

⚠⚠ THE ABSOLUTE PUSH COUNTS ARE HARVEST-LIMITED, AND ONLY THE RATIO IS MEANINGFUL. Events are
collected as a side effect of polling, so this rig samples at its own poll rate, not the watch's.
At the 0.2 s poll below that reads ~2.5 events/sec even though the watch is pushing 4/sec. That
gap looked like a defect ("asked for interval_ms=250, got ~1000 ms apart") until it was measured
properly: polling every 0.05 s instead returns **48 events in 12.0 s = 4.00/sec**, with
DLL-stamped inter-arrival gaps of **median 254 ms** (min 252, max 301) against the 250 requested.
The cadence is correct; the earlier number was the measuring instrument. ⭐ Because the poll
cadence is IDENTICAL in the idle and busy windows, the busy/idle ratio is unaffected by this —
which is exactly why the verdict is a ratio and not a rate.
"""
import argparse
import json
import pathlib
import struct
import sys
import threading
import time

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.stdout.reconfigure(encoding="utf-8", errors="replace")

from pipe_client import PipeClient          # noqa: E402
from ad4_contested import find_live_actor    # noqa: E402

WATCH_MS = 250


def drain(c, seconds):
    """Collect watch events for `seconds` by POLLING with a cheap request.

    ⛔ DO NOT "SIMPLIFY" THIS BACK TO A RAW `c._f.read()` LOOP. The first version did exactly
    that and HUNG the whole rig before the busy window ever started -- a blocking read on a
    handle that is also the request/response channel, with no deadline the OS respects. It
    produced a 0-byte output file and looked indistinguishable from "no events are being
    delivered", which is the very defect this rig exists to detect. A rig that cannot tell its
    own hang from its subject's failure is worse than no rig.

    `PipeClient.request()` harvests any interleaved event into `c.events` as a side effect and
    carries its own timeout, so polling something cheap is both safe and bounded. The poll
    traffic is identical in both windows, so it cancels out of the comparison.
    """
    start_idx = len(c.events)
    end = time.time() + seconds
    while time.time() < end:
        c.request("get_object_count")
        time.sleep(0.2)
    return [(None, e) for e in c.events[start_idx:]]


def tally(events, label_by_addr):
    """-> {label: [(t, int_value), ...]} for watch events only.

    ⚠ THE PAYLOAD IS AT TOP LEVEL, NOT UNDER "data". `Renge::MakeEvent` sets `event` and then
    `ApplyPayload` (Renge.h:308) copies each payload key straight onto the object, so a watch
    event is {"event":"watch","addr":...,"bytes":...,"timestamp":...}. A first draft of this
    read `o["data"]["addr"]`, matched nothing, and would have reported zero pushes in BOTH
    windows -- which looks exactly like the defect it is meant to detect. Read the envelope
    before parsing it.
    """
    per = {v: [] for v in label_by_addr.values()}
    want = {k.lower(): v for k, v in label_by_addr.items()}
    for t, o in events:
        if o.get("event") != "watch":
            continue
        lab = want.get(str(o.get("addr", "")).lower())
        if lab is None:
            continue
        try:
            b = bytes.fromhex(o.get("bytes") or "")
            val = struct.unpack("<i", b[:4])[0] if len(b) >= 4 else None
        except ValueError:
            val = None
        per[lab].append((o.get("timestamp"), val))
    return per


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--window", type=int, default=20, help="seconds per window")
    a = ap.parse_args()
    fails = []

    inter = PipeClient().connect()
    bulk = PipeClient().connect()
    try:
        inter.assert_build()
        act = find_live_actor(inter)
        base = int(act["addr"], 16)
        r = inter.request("walk_instance", addr=act["addr"])
        fields = {f.get("name"): f for f in r.get("fields", [])}
        for n in ("TickCount", "FrozenInt"):
            if n not in fields:
                raise SystemExit("!! %s not in the walk -- wrong fixture?" % n)
        addrs = {n: base + fields[n]["offset"] for n in ("TickCount", "FrozenInt")}
        label = {("0x%X" % v): k for k, v in addrs.items()}
        print("actor %s @ 0x%X" % (act.get("name"), base))
        for n, v in addrs.items():
            print("  watch %-10s @ 0x%X  (initial %s)" % (n, v, fields[n].get("value")))

        for n, v in addrs.items():
            inter.request("watch", addr="0x%X" % v, size=4, interval_ms=WATCH_MS)

        # ---------- window 1: IDLE ----------
        print("\n[1] idle window, %ds ..." % a.window)
        idle = tally(drain(inter, a.window), label)

        # ---------- window 2: BULK LANE SATURATED ----------
        print("[2] busy window, %ds -- bulk lane hammering list_all_functions ..." % a.window)
        stop = threading.Event()
        calls = [0]

        def hammer():
            while not stop.is_set():
                try:
                    bulk.request("list_all_functions", game_only=True, limit=100000)
                    calls[0] += 1
                except Exception:
                    break

        th = threading.Thread(target=hammer, daemon=True)
        th.start()
        busy = tally(drain(inter, a.window), label)
        stop.set()
        th.join(timeout=90)
        print("    bulk lane completed %d list_all_functions call(s) in the window" % calls[0])

        for n, v in addrs.items():
            try:
                inter.request("unwatch", addr="0x%X" % v)
            except Exception:
                pass

        # ---------- verdict ----------
        print("\n%-12s %-18s %-18s" % ("watch", "idle window", "busy window"))
        print("-" * 52)
        for n in ("TickCount", "FrozenInt"):
            print("%-12s %-18s %-18s" % (n, "%d pushes" % len(idle[n]), "%d pushes" % len(busy[n])))

        if calls[0] == 0:
            fails.append("the bulk lane never completed a call -- the busy window was not busy, "
                         "so nothing below measures contention")

        i_n, b_n = len(idle["TickCount"]), len(busy["TickCount"])
        if i_n == 0:
            fails.append("no watch pushes even when IDLE -- the watch never armed; the busy "
                         "comparison is meaningless")
        else:
            ratio = b_n / float(i_n)
            print("\n  delivery ratio busy/idle: %.2f  (%d vs %d)" % (ratio, b_n, i_n))
            if ratio < 0.5:
                fails.append("watch delivery COLLAPSED while the bulk lane was busy "
                             "(%.2f of idle) -- that is the defect item (6) asks about" % ratio)

        # values: the mover must move, the frozen one must not
        tv = [v for _, v in busy["TickCount"] if v is not None]
        fv = [v for _, v in busy["FrozenInt"] if v is not None]
        print("  TickCount over the busy window: %s ... %s (%d samples)"
              % (tv[:3], tv[-3:], len(tv)))
        print("  FrozenInt distinct values      : %s" % sorted(set(fv))[:5])
        if tv and tv != sorted(tv):
            fails.append("TickCount pushes were NOT monotonically increasing -- events are being "
                         "reordered or stale bytes are being pushed")
        if tv and len(set(tv)) < 2:
            fails.append("TickCount never changed across the busy window -- the game is not "
                         "ticking, so delivery cannot be judged")
        if len(set(fv)) > 1:
            fails.append("FrozenInt CHANGED (%s) -- the watch is not reading the address we think"
                         % sorted(set(fv))[:5])
    finally:
        inter.close()
        bulk.close()

    print("\nmultipipe watch (6): %s" % ("PASS" if not fails else "FAIL"))
    for f in fails:
        print("  - %s" % f)
    return 0 if not fails else 1


if __name__ == "__main__":
    raise SystemExit(main())
