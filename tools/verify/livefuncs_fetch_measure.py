r"""How long `pe_profile_get` takes, and how big its reply is, at the fetch limits Live Funcs may offer.

    py tools/verify/livefuncs_fetch_measure.py --label <game> [--record-s 60] [--limits 300,512,8192,32768]

`[EXTPR-539-540-2026-10-02]` L1 raises the fetch limit from a fixed 300 to a slider whose max is
2^15 = 32768. `pe_profile_get` runs on the UI's INTERACTIVE pipe lane, and its reply is one JSON
line, so while it is being built and sent, Live Walker and every other interactive command wait
behind it. The plan says to measure this on a game that fires many functions before deciding
whether the command has to move to the bulk lane -- and not to move it on a guess.

What it does, on the game whose DLL is serving the pipe (one game at a time):

  1. checks the DLL is the build in dist\ and that the game has been scanned;
  2. times a cheap command (`get_pointers`) as the lane's normal latency;
  3. records for --record-s seconds, then fetches once at the largest limit WHILE still recording
     (Refresh during a recording does that);
  4. stops the recording and fetches at every limit --repeat times;
  5. times `get_pointers` again straight after the largest fetch.

Each fetch reports: wall time on this client (send to last byte of the reply), the reply's size on
the wire, how many functions came back, and the recording's distinct-function and call totals.
A game that fired fewer distinct functions than a limit sends only what it has, so the largest
limit measures this game's whole table, which is the number that matters.

The recording is stopped in a `finally`, so a failed run does not leave the hook counting.
Output: a JSON file under out/livefuncs-fetch-measure/ and a summary on stdout.
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
import pathlib
import statistics
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from pipe_client import PipeClient  # noqa: E402

OUT_DIR = HERE.parents[1] / "out" / "livefuncs-fetch-measure"


def say(s: str) -> None:
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + "\n")
    sys.stdout.flush()


def timed(c: PipeClient, cmd: str, **params) -> tuple[dict, float, int]:
    t0 = time.perf_counter()
    reply = c.request(cmd, **params)
    ms = (time.perf_counter() - t0) * 1000.0
    return reply, ms, c.last_reply_bytes


def fetch(c: PipeClient, limit: int) -> dict:
    reply, ms, nbytes = timed(c, "pe_profile_get", limit=limit)
    data = reply.get("data", reply)
    funcs = data.get("functions") or []
    return {
        "limit": limit,
        "ms": round(ms, 1),
        "reply_bytes": nbytes,
        "returned": len(funcs),
        "distinct_funcs": data.get("distinct_funcs"),
        "total_calls": data.get("total_calls"),
        "recording": data.get("recording"),
        "truncated": bool(data.get("truncated")),
        "error": reply.get("error"),
    }


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--label", required=True, help="the game, for the output file name and the record")
    ap.add_argument("--record-s", type=float, default=60.0)
    ap.add_argument("--limits", default="300,512,8192,32768")
    ap.add_argument("--repeat", type=int, default=3)
    ap.add_argument("--timeout", type=float, default=300.0, help="per-request timeout, seconds")
    args = ap.parse_args()
    limits = sorted({int(x) for x in args.limits.split(",") if x.strip()})
    top = limits[-1]

    rec: dict = {"label": args.label, "started": dt.datetime.now().isoformat(timespec="seconds"),
                 "record_s": args.record_s, "limits": limits, "repeat": args.repeat}
    c = PipeClient(timeout=args.timeout).connect()
    started = False
    try:
        rec["build"] = c.assert_build()
        c.ensure_scanned()
        base = [timed(c, "get_pointers")[1] for _ in range(5)]
        rec["get_pointers_ms_before"] = [round(x, 1) for x in base]

        start = c.request("pe_profile_start")
        started = True
        sdata = start.get("data", start)
        rec["hook_active"] = sdata.get("hook_active")
        say(f"recording {args.record_s:.0f} s (hook_active={rec['hook_active']}) ...")
        if not rec["hook_active"]:
            say("hook is not active: the counts would stay 0 -- stopping")
            return 2
        time.sleep(args.record_s)

        rec["peek_while_recording"] = fetch(c, top)
        c.request("pe_profile_stop")
        started = False

        rows = []
        for limit in limits:
            for _ in range(args.repeat):
                rows.append(fetch(c, limit))
        rec["fetches"] = rows
        rec["fetch_before_latency_probe"] = fetch(c, top)
        rec["get_pointers_ms_after_top"] = round(timed(c, "get_pointers")[1], 1)
    finally:
        if started:
            try:
                c.request("pe_profile_stop")
            except Exception as e:  # report, but do not mask the original failure
                say(f"pe_profile_stop in finally failed: {e}")
        c.close()

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    stamp = dt.datetime.now().strftime("%Y%m%d-%H%M%S")
    out = OUT_DIR / f"{args.label}-{stamp}.json"
    out.write_text(json.dumps(rec, indent=2, ensure_ascii=False), encoding="utf-8")

    p = rec["peek_while_recording"]
    say(f"\nbuild {rec['build']}  distinct={p['distinct_funcs']}  total_calls={p['total_calls']}")
    say(f"get_pointers before: median {statistics.median(rec['get_pointers_ms_before']):.1f} ms;"
        f" right after a {top} fetch: {rec['get_pointers_ms_after_top']:.1f} ms")
    say(f"peek while recording, limit {top}: {p['ms']:.0f} ms, {p['reply_bytes']:,} B, {p['returned']} rows")
    say("limit    returned   reply bytes    ms (each run)")
    for limit in limits:
        runs = [r for r in rec["fetches"] if r["limit"] == limit]
        say(f"{limit:>6} {runs[0]['returned']:>10} {runs[0]['reply_bytes']:>13,}    "
            + ", ".join(f"{r['ms']:.0f}" for r in runs))
    say(f"\nwritten: {out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
