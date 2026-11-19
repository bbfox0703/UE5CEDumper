r"""[DYNOFF-LATCH-STALE] live regression check: with the epoch-keyed calibration latch, a fixture still walks unchanged.

    py tools/verify/latch_regression_live.py --pid 1234
    py tools/verify/latch_regression_live.py --pid 1234 --max-deltas 0 --class /Script/DumperTest.DumperTestActor

The failure the fix is for -- a property family published after a calibration, one pointer off -- needs a game whose
Genau derivation is wrong, and no fixture has one; this checks only that nothing else moved. On a scanned game (inject
mode, or a proxy after trigger_scan):
  - walk_class of the class names a struct type for every StructProperty field, which reads the calibrated slot,
  - the walk log of this run has at most --max-deltas "CorrectSubclassOffsets: delta=" lines (a fixture Genau derives
    right needs none; one per family move would mean the re-armed latch is moving it),
  - the offsets log has at least one ValidateAndFixOffsets summary.
The game, its pid and its log folder are arguments, never written here.
"""
import argparse
import pathlib
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import LOGROOT, PipeClient, process_image   # noqa: E402

results: list[tuple[str, bool, str]] = []


def check(name: str, ok: bool, detail: str = "") -> bool:
    results.append((name, ok, detail))
    print(f"  [{'PASS' if ok else 'FAIL'}] {name}" + (f" -- {detail}" if detail else ""))
    return ok


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--pid", type=int, required=True)
    ap.add_argument("--class", dest="cls", default="/Script/DumperTest.DumperTestActor")
    ap.add_argument("--max-deltas", type=int, default=0)
    ap.add_argument("--logdir", help="the game's folder under Logs\\ (default: its exe's stem)")
    a = ap.parse_args()
    logdir = LOGROOT / (a.logdir or pathlib.Path(process_image(a.pid)).stem)

    with PipeClient(pid=a.pid) as c:
        print("build", c.assert_build())
        c.ensure_scanned()
        obj = c.request("find_object", path=a.cls)
        if not obj.get("addr"):
            print(f"FAILED: {a.cls} not found: {obj}")
            return 2
        k = c.request("walk_class", addr=obj["addr"], timeout=120).get("class") or {}
    fields = k.get("fields") or []
    structs = [f for f in fields if f.get("type") == "StructProperty"]
    unnamed = [f.get("name") for f in structs if not f.get("struct_type")]
    print(f"{a.cls}: {len(fields)} fields, {len(structs)} StructProperty: "
          + ", ".join(f"{f.get('name')}={f.get('struct_type', '?')}" for f in structs[:12]))
    check("the class has StructProperty fields to read", bool(structs), f"{len(fields)} fields")
    check("every StructProperty field names its struct type", not unnamed, f"unnamed: {unnamed}")

    walk = (logdir / "walk-0.log").read_text(encoding="utf-8", errors="replace") if (logdir / "walk-0.log").is_file() else ""
    deltas = [ln for ln in walk.splitlines() if "CorrectSubclassOffsets: delta=" in ln]
    check(f"the walk log has at most {a.max_deltas} CorrectSubclassOffsets delta line(s)", len(deltas) <= a.max_deltas,
          "; ".join(deltas[:3]) or "none")
    offs = (logdir / "offsets-0.log").read_text(encoding="utf-8", errors="replace") if (logdir / "offsets-0.log").is_file() else ""
    runs = offs.count("ValidateAndFixOffsets: Starting dynamic offset detection")
    check("the offsets log has a ValidateAndFixOffsets run", runs >= 1, f"{runs} run(s)")

    failed = [n for n, ok, _ in results if not ok]
    print(f"\n{len(results) - len(failed)}/{len(results)} passed")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
