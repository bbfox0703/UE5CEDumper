r"""Live check of Live Funcs' Hide per-frame: the DLL leaves the per-frame functions out BEFORE the fetch limit.

    py tools/verify/livefuncs_hide_per_frame.py --label <game> [--record-s 10] [--limit 64]

`[LIVEFUNCS-HIDE-PERFRAME]` `pe_profile_get` takes `skip_per_frame` and answers `per_frame_hidden`. On the game whose
DLL is serving the pipe (one game at a time, never while the UI holds the pipe), this records once, then fetches the
same table twice at a small limit -- plain, and with skip_per_frame -- and checks what only a running game can show:

  1. both fetches see the same table (distinct_funcs, total_calls); the plain one carries no per_frame_hidden;
  2. the DLL left something out (a game ticks every frame), and said how many;
  3. every row the skip fetch dropped, among those the plain fetch had, has a frame-band cadence (mean gap <= 40 ms,
     3+ gaps): the classifier took nothing slower;
  4. the skip fetch shows rows the plain fetch's limit had cut (the point: their rows went to the rest), and when the
     plain fetch was capped, its lowest count is above the skip fetch's lowest;
  5. a function slower than the frame band that the plain fetch showed is still shown.

The recording is stopped in a `finally`. Output: out/livefuncs-hide-per-frame/<label>.json and a summary.
Exit 0 when every check holds; 1 otherwise; 2 when the pipe or the game is not usable.
"""
from __future__ import annotations

import argparse
import json
import pathlib
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from pipe_client import PipeClient  # noqa: E402

OUT_DIR = HERE.parents[1] / "out" / "livefuncs-hide-per-frame"
FRAME_BAND_MS = 40.0
MIN_GAPS = 3


def say(s: str) -> None:
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + "\n")
    sys.stdout.flush()


def data_of(reply: dict) -> dict:
    return reply.get("data", reply)


def key(f: dict) -> str:
    return f"{f.get('class_name')}::{f.get('func_name')}"


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--label", required=True)
    ap.add_argument("--record-s", type=float, default=10.0)
    ap.add_argument("--limit", type=int, default=64)
    args = ap.parse_args()

    checks: list[tuple[str, bool, str]] = []
    out: dict = {"label": args.label, "record_s": args.record_s, "limit": args.limit}
    with PipeClient() as c:
        start = data_of(c.request("pe_profile_start"))
        if not start.get("hook_active", False):
            say("FAIL: the ProcessEvent hook is not active on this game (counts would stay 0)")
            c.request("pe_profile_stop")
            return 2
        try:
            time.sleep(args.record_s)
        finally:
            c.request("pe_profile_stop")
        plain = data_of(c.request("pe_profile_get", limit=args.limit))
        skip = data_of(c.request("pe_profile_get", limit=args.limit, skip_per_frame=True))
        full = data_of(c.request("pe_profile_get", limit=1 << 15))   # the whole table, for the cadence of every row

    out.update(plain=plain, skip=skip, full_distinct=full.get("distinct_funcs"))
    pf, sf, ff = plain.get("functions", []), skip.get("functions", []), full.get("functions", [])
    hidden = skip.get("per_frame_hidden")
    say(f"{args.label}: {plain.get('distinct_funcs')} distinct, {plain.get('total_calls')} calls in {args.record_s:.0f} s; "
        f"plain {len(pf)} rows, skip {len(sf)} rows, per_frame_hidden {hidden}")

    checks.append(("same table both times",
                   plain.get("distinct_funcs") == skip.get("distinct_funcs")
                   and plain.get("total_calls") == skip.get("total_calls"),
                   f"{plain.get('distinct_funcs')}/{plain.get('total_calls')} vs {skip.get('distinct_funcs')}/{skip.get('total_calls')}"))
    checks.append(("the plain fetch carries no per_frame_hidden", "per_frame_hidden" not in plain, ""))
    checks.append(("the DLL left per-frame functions out, and said how many", isinstance(hidden, int) and hidden > 0,
                   str(hidden)))

    dropped = [f for f in pf if key(f) not in {key(x) for x in sf}]
    slow_dropped = [key(f) for f in dropped
                    if not (f.get("gap_samples", 0) >= MIN_GAPS and f.get("mean_period_ms", 1e9) <= FRAME_BAND_MS)]
    checks.append((f"every row it dropped has a frame-band cadence ({len(dropped)} dropped)", not slow_dropped,
                   ", ".join(slow_dropped[:5])))

    # The limit's rows go to the rest: the skip fetch fills to the limit from what was not left out. (A game whose
    # whole table ticks every frame, an idle DumperTest at 60 fps, leaves nothing, and that is the right answer.)
    added = [f for f in sf if key(f) not in {key(x) for x in pf}]
    plain_capped = len(pf) >= args.limit and plain.get("distinct_funcs", 0) > len(pf)
    rest = plain.get("distinct_funcs", 0) - (hidden or 0)
    checks.append((f"the skip fetch fills the limit from the rest: {len(sf)} = min({args.limit}, {rest})",
                   len(sf) == min(args.limit, rest), "fewer: stale UFunction pointers dropped?" if len(sf) < min(args.limit, rest) else ""))
    if plain_capped and rest > 0:
        checks.append((f"the skip fetch shows rows the plain fetch's limit cut ({len(added)} new)", len(added) > 0, ""))
    if plain_capped and pf and sf:
        checks.append(("the plain fetch's lowest count is above the skip fetch's lowest",
                       min(f["count"] for f in pf) >= min(f["count"] for f in sf),
                       f"{min(f['count'] for f in pf)} vs {min(f['count'] for f in sf)}"))

    slow_kept = [key(f) for f in pf if f.get("mean_period_ms", 0) > FRAME_BAND_MS and key(f) not in {key(x) for x in sf}]
    checks.append(("no function slower than the frame band was dropped", not slow_kept, ", ".join(slow_kept[:5])))

    out["dropped"] = [key(f) for f in dropped]
    out["added"] = [key(f) for f in added]
    out["dropped_cadence"] = {key(f): [f.get("mean_period_ms"), f.get("gap_samples"), f.get("count")] for f in dropped}
    out["checks"] = [{"check": n, "ok": ok, "detail": d} for n, ok, d in checks]
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    path = OUT_DIR / f"{args.label}.json"
    path.write_text(json.dumps(out, indent=1, ensure_ascii=False), encoding="utf-8")

    for n, ok, d in checks:
        say(f"  {'PASS' if ok else 'FAIL'}  {n}" + (f"  [{d}]" if d else ""))
    say(f"  dropped (sample): {', '.join(out['dropped'][:8])}")
    say(f"  freed rows went to (sample): {', '.join(out['added'][:8])}")
    say(f"written {path}")
    return 0 if all(ok for _, ok, _ in checks) else 1


if __name__ == "__main__":
    sys.exit(main())
