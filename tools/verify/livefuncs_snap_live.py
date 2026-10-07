r"""Live check of Live Funcs step 2 -- parameter snapshots and following functions by name -- through the pipe.

    py tools/verify/livefuncs_snap_live.py --fixture-check
    py tools/verify/livefuncs_snap_live.py --label <game> [--record-s 10]

`[LIVEFUNCS-STEP2]` Runs against the game whose DLL serves the pipe (one game at a time, never while the UI holds
the pipe). Its subject is the DumperTest58 fixture's snapshot probes (tools/ue-sample/README.md, "DumperTest58"):
SnapNest_Outer calls SnapProbe_Call inside its scope and the timer calls it again on its own, every argument a fixed
function of the round, so a decoded snapshot is checked from its Round alone.

--fixture-check only asks whether the package under test carries the probes: a plain recording of a few seconds,
then every probe must be in pe_profile_get with calls. It is how a stale package is told from a broken DLL, and the
red of the fixture's own item.

Exit 0 when every check holds; 1 otherwise; 2 when the pipe or the game is not usable. Output:
out/livefuncs-snap/<label>.json and a summary.
"""
from __future__ import annotations

import argparse
import json
import pathlib
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from pipe_client import PipeClient, PipeError  # noqa: E402

OUT_DIR = HERE.parents[1] / "out" / "livefuncs-snap"
FIXTURE_CLASS = "DumperTest58Actor"
# The probes a plain recording must see called. SnapLate_Call is not here: it runs only when the rig calls
# SnapLate_Begin.
PROBES = ("SnapNest_Outer", "SnapProbe_Call", "SnapProbe_RetOnly", "SnapProbe_ConstRefOnly", "SnapProbe_PerFrame")


def say(s: str) -> None:
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + "\n")
    sys.stdout.flush()


def data_of(reply: dict) -> dict:
    return reply.get("data", reply)


def ok_of(reply: dict) -> bool:
    return bool(reply.get("ok", True)) and "error" not in reply


class Checks:
    def __init__(self) -> None:
        self.items: list[tuple[str, bool, str]] = []

    def __call__(self, name: str, cond: bool, got: str = "") -> bool:
        self.items.append((name, bool(cond), got))
        say(f"  {'ok  ' if cond else 'FAIL'}  {name}" + (f"   ({got})" if got else ""))
        return bool(cond)

    @property
    def failed(self) -> int:
        return sum(1 for _, ok, _ in self.items if not ok)


def plain_table(c: PipeClient, seconds: float) -> dict:
    """A recording without the trace, then the whole table."""
    start = c.request("pe_profile_start")
    if not ok_of(start):
        raise PipeError(f"pe_profile_start refused: {start}")
    try:
        time.sleep(seconds)
    finally:
        c.request("pe_profile_stop")
    return data_of(c.request("pe_profile_get", limit=32768, include_unloaded=True))


def fixture_rows(table: dict) -> dict[str, dict]:
    """The fixture class's rows by function name (the busiest when a name repeats)."""
    rows: dict[str, dict] = {}
    for f in table.get("functions", []):
        if f.get("class_name") != FIXTURE_CLASS:
            continue
        name = f.get("func_name", "")
        if name not in rows or f.get("count", 0) > rows[name].get("count", 0):
            rows[name] = f
    return rows


def fixture_check(c: PipeClient, check: Checks, seconds: float) -> dict:
    table = plain_table(c, seconds)
    rows = fixture_rows(table)
    check("the table recorded calls", table.get("total_calls", 0) > 0, f"{table.get('total_calls', 0):,} calls")
    for p in PROBES:
        n = rows.get(p, {}).get("count", 0)
        check(f"{FIXTURE_CLASS}::{p} was called", n > 0, f"{n} calls" if n else "absent: a package without the probes")
    return {"total_calls": table.get("total_calls", 0), "window_ms": table.get("window_ms", 0),
            "probes": {p: rows.get(p) for p in PROBES}}


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--label", default="dumpertest58")
    ap.add_argument("--fixture-check", action="store_true", help="only check that the package carries the probes")
    ap.add_argument("--plain-s", type=float, default=3.0)
    args = ap.parse_args()

    check = Checks()
    out: dict = {"label": args.label}
    try:
        c = PipeClient().connect()
    except PipeError as e:
        say(f"pipe not usable: {e}")
        return 2
    try:
        say("fixture:")
        out["fixture"] = fixture_check(c, check, args.plain_s)
        if out["fixture"]["total_calls"] == 0:
            say("no calls recorded: is the game running, scanned, and the hook up?")
            return 2
    finally:
        try:
            c.request("pe_profile_stop")
            c.request("pe_trace_release")
        except PipeError:
            pass
        c.close()

    out["checks"] = [{"name": n, "ok": ok, "got": g} for n, ok, g in check.items]
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    path = OUT_DIR / f"{args.label}{'-fixture' if args.fixture_check else ''}.json"
    path.write_text(json.dumps(out, indent=1, default=str), encoding="utf-8")
    say(f"\n{len(check.items) - check.failed}/{len(check.items)} checks hold; written to {path}")
    return 1 if check.failed else 0


if __name__ == "__main__":
    sys.exit(main())
