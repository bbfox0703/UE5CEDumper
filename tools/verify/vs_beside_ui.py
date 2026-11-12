"""Register [PIPE-PER-PROCESS-2026-11-06] item 17: the UI's Value Search beside the MCP server's.

    py tools/verify/vs_beside_ui.py --exe dist/UE5DumpUI.exe --game-name DumperTest-Win64-Shipping [--pid N]
        [--seconds 60] [--args '{"data_type":"Float","value":"100","game_only":false,"deep":true,"page_size":1}']

One MCP session runs read_value_scan_begin / _end back to back for --seconds while the user clicks First Scan in the
UI's Value Search (a different data type, so the two sides' log lines stay apart). Afterwards it prints every
"ParallelGObjectsScan:" and "Radar: First Scan" line of the game's offsets-0.log stamped inside the loop, marked UI
(any data type but the AI's) or AI, and for each UI scan its thread count and whether an AI walk started inside it.
Exit 1 when an AI call failed or hit its deadline; whether the UI's scans overlapped is read from the printout.

WHY A LOOP. A scan of the 24.5k-object fixture takes well under a second, so a click during a single AI call would be
luck; back-to-back AI walks make the overlap near certain.
"""
from __future__ import annotations

import argparse
import json
import pathlib
import re
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from mcp_client import McpClient, payload   # noqa: E402
from pipe_client import LOGROOT             # noqa: E402

LINE = re.compile(r"ParallelGObjectsScan:|Radar: First Scan")


def stamp() -> str:
    t = time.time()
    return time.strftime("%Y-%m-%d %H:%M:%S", time.localtime(t)) + ".%03d" % int((t % 1) * 1000)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--exe", required=True)
    ap.add_argument("--game-name", required=True, help="the game's log folder (its image stem)")
    ap.add_argument("--pid", type=int)
    ap.add_argument("--seconds", type=float, default=60.0)
    ap.add_argument("--args", default='{"data_type":"Float","value":"100","game_only":false,"deep":true,'
                                      '"page_size":1}')
    a = ap.parse_args()
    begin = json.loads(a.args)
    if a.pid:
        begin["pid"] = a.pid
    ai_type = begin.get("data_type", "Float")

    c = McpClient(str(pathlib.Path(a.exe).resolve()))
    c.initialize()
    st, _ = payload(c.call("read_status", {"pid": a.pid} if a.pid else {}))
    print(f"build {st.get('build_number')}, scanned {st.get('scanned')}", flush=True)
    bad = 0
    ai_totals: set[int] = set()
    first, last = stamp(), None
    end = time.monotonic() + a.seconds
    n = 0
    while time.monotonic() < end:
        t0 = stamp()
        r = c.call("read_value_scan_begin", begin, 300)
        d, _ = payload(r)
        t1 = stamp()
        n += 1
        if r.get("isError") or not isinstance(d, dict) or d.get("deadline_hit"):
            bad += 1
            print(f"  FAIL  {t0} -> {t1}: {json.dumps(d)[:200]}", flush=True)
        else:
            ai_totals.add(int(d.get("total") or 0))
            print(f"  ai {n:3d}  {t0[11:]} -> {t1[11:]}  total {d.get('total')}  {d.get('duration_ms')} ms", flush=True)
        sid = d.get("session_id") if isinstance(d, dict) else None
        if sid is not None:
            c.call("read_value_scan_end", {"session_id": sid, **({"pid": a.pid} if a.pid else {})})
        last = t1
    c.close()

    try:
        text = (LOGROOT / a.game_name / "offsets-0.log").read_text(encoding="utf-8", errors="replace")
    except OSError as e:
        print(f"no offsets-0.log: {e}")
        return 1
    rows = [l.rstrip() for l in text.splitlines()
            if l.startswith("[") and first <= l[1:24] <= (last or first) and LINE.search(l)]
    print(f"\noffsets-0.log, {first[11:]} .. {(last or first)[11:]}:", flush=True)
    # A start line names its data type; a "complete" line does not, so it is told apart by its candidate count, which
    # for the AI's scans is the total each call returned.
    ui_open = None
    ai_inside = False
    for l in rows:
        start = re.search(r"Radar: First Scan dt=(\w+)", l)
        done = re.search(r"First Scan complete -- (\d+) candidates", l)
        if start:
            side = "AI" if start.group(1) == ai_type else "UI"
        elif done:
            side = "AI" if int(done.group(1)) in ai_totals else "UI"
        else:
            side = "  "
        if start and side == "UI":
            ui_open, ai_inside = l[1:24], False
        elif side == "AI" and ui_open:
            ai_inside = True
        if done and side == "UI" and ui_open:
            m = re.search(r"(\d+) thread\(s\)", l)
            print(f"  UI scan from {ui_open[11:]}: {done.group(1)} candidates, {m.group(1) if m else '?'} thread(s); "
                  f"an AI walk ran inside it: {ai_inside}", flush=True)
            ui_open = None
        print(f"  {side} {l[1:24][11:]} {l[l.find(']', 26) + 2:][:170]}", flush=True)
    print(f"\n{n} AI scans, {bad} failed or hit the deadline", flush=True)
    return 0 if bad == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
