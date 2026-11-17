r"""[VND584-03-FLOOR-HIGH-HEAP] live check: which FUObjectArray row a game's array was read by, and that it reads whole.

    py tools/verify/relaxed_layout_live.py --pid 1234 --expect-row "A/C (relaxed)"
    py tools/verify/relaxed_layout_live.py --pid 1234 --expect-row "A/C (relaxed)" --min-objects 100000

For a title that reaches Aura's relaxed tier (the Outer Worlds 2 reaches relaxed A/C: docs/test-games.md). After
`trigger_scan` on a proxy (or on an injected game, already scanned), it reads this run's DLL logs and the pipe:
  - the log names the expected row (`ObjectArray: Layout <row> detected`), and no relaxed row was refused as half of
    a chunk table pointer (`refused: Num=... is the high dword`),
  - get_object_count is at least --min-objects (a count read from a pointer's high dword is under 0x8000),
  - walk_world answers a world and its persistent level.
The game, its pid and its log folder are arguments, never written here.
"""
import argparse
import pathlib
import re
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
    ap.add_argument("--expect-row", required=True, help='as the log names it, e.g. "A/C (relaxed)"')
    ap.add_argument("--min-objects", type=int, default=0x8000)
    ap.add_argument("--logdir", help="the game's folder under Logs\\ (default: its exe's stem)")
    a = ap.parse_args()
    logdir = LOGROOT / (a.logdir or pathlib.Path(process_image(a.pid)).stem)

    with PipeClient(pid=a.pid) as c:
        print("build", c.assert_build())
        c.ensure_scanned(timeout=300)
        count = c.request("get_object_count").get("count") or c.request("get_pointers").get("object_count") or 0
        world = c.request("walk_world", timeout=120)

    text = ""
    for p in sorted(logdir.glob("*-0.log")):
        text += p.read_text(encoding="utf-8", errors="replace")
    rows = re.findall(r"ObjectArray: Layout (.+?) detected", text)
    refused = re.findall(r"ObjectArray: Layout .+? refused: Num=\d+ is the high dword[^\n]*", text)
    print(f"log rows: {rows}; refusals: {refused}")
    check(f"the array was read by {a.expect_row!r}", any(r.startswith(a.expect_row) for r in rows), str(rows))
    check("no relaxed row was refused as half of a chunk table pointer", not refused, "; ".join(refused[:2]))
    check(f"get_object_count is at least {a.min_objects}", int(count) >= a.min_objects, str(count))
    check("walk_world answers a world and its persistent level",
          bool(world.get("ok")) and bool(world.get("world_name")) and bool(world.get("level_addr")),
          f"ok={world.get('ok')} world={world.get('world_name')} level={world.get('level_name')} "
          f"actors={world.get('actor_count', world.get('actor_total'))} {world.get('error', '')}")

    failed = [n for n, ok, _ in results if not ok]
    print(f"\n{len(results) - len(failed)}/{len(results)} passed")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
