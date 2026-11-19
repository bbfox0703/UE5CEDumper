r"""The deep Group Scan's per-object element budget (kDeepWalkMaxTotalElems) bites before the deadline.

    py tools/verify/a9_deep_budget_live.py --pid 1234

DumperTest's A9_BuildDeepContainers(Outer, Mid, Inner) fills Deep_Buckets -> Subs -> Leaves with the float
o*1e6 + m*1e3 + i, unique per path. The deep walk clamps each container at 256 elements, so (300,300,300) is
256 + 256^2 + 256^3 ~= 16.8M visits unclamped against a 50,000-element budget; (30,30,30) is 27,930, under it.

  control (30,30,30): deep=true finds the LAST block's pair (Deep_Buckets[29].Subs[29].Leaves[26]/[28]), so every leaf
                      is reached; deep=false does not (the pair only exists inside the container).
  budget (300,300,300): deep=true returns with deadline_hit false, within a small multiple of deep=false's time, and
                      finds an EARLY pair ([0].Subs[5].Leaves[10]/[12]); a LATE pair ([200].Subs[100].Leaves[48]/[64]) is
                      absent because the budget stopped the walk first.
Floats above 2^24 are spaced 2 apart and above 2^27 16 apart, so every value used here is exact.
The fixture is emptied again with (0,0,0) at the end. The game and its pid are arguments.
"""
import argparse
import pathlib
import struct
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient   # noqa: E402

results: list[tuple[str, bool, str]] = []


def check(name: str, ok: bool, detail: str = "") -> bool:
    results.append((name, ok, detail))
    print(f"  [{'PASS' if ok else 'FAIL'}] {name}" + (f" -- {detail}" if detail else ""))
    return ok


def build(c, addr, o, m, i):
    r = c.request("invoke_function", func_name="A9_BuildDeepContainers", instance_addr=addr,
                  parms_size=12, params_hex=struct.pack("<iii", o, m, i).hex())
    if not r.get("ok") or r.get("result", 0) != 0:
        raise SystemExit(f"a9: invoke A9_BuildDeepContainers({o},{m},{i}) failed: {str(r)[:300]}")


def group(c, deep, a, b):
    r = c.request("begin_group_scan", deep=deep, game_only=True, max_results=50000,
                  values=[{"value": str(a), "data_type": "NumericNoByte"},
                          {"value": str(b), "data_type": "NumericNoByte"}])
    d = r.get("data", r)
    sid = d.get("session_id")
    rows = []
    if sid:
        q = c.request("query_group_candidates", session_id=sid, offset=0, limit=200)
        rows = q.get("data", q).get("candidates") or []
        c.request("end_group_scan", session_id=sid)
    hit = [x for x in rows if x.get("class_name") == "DumperTestActor"
           and any("Deep_Buckets" in (s.get("field_name") or "") for s in x.get("slots") or [])]
    info = (f"deep={deep} {a}/{b}: {d.get('duration_ms')} ms, deadline_hit={d.get('deadline_hit')}, "
            f"scanned_objects={d.get('scanned_objects')}, rows={len(rows)}, Deep_Buckets rows={len(hit)}")
    print("   " + info)
    if hit:
        print("      " + str([s.get("field_name") for s in hit[0].get("slots") or []])[:200])
    return d, hit


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--pid", type=int, required=True)
    a = ap.parse_args()
    with PipeClient(pid=a.pid, timeout=240.0) as c:
        print("build", c.assert_build())
        c.ensure_scanned()
        r = c.request("find_instances", class_name="DumperTestActor", limit=100, exact_match=True)
        live = [i for i in r.get("instances", []) if not i["name"].startswith("Default__")]
        if not live:
            raise SystemExit("a9: no live DumperTestActor")
        addr = live[0]["addr"]
        try:
            print("== control (30,30,30): 27,930 visits, under the budget ==")
            build(c, addr, 30, 30, 30)
            d_on, hit_on = group(c, True, 29029026, 29029028)
            d_off, hit_off = group(c, False, 29029026, 29029028)
            check("deep=true reaches the last block's pair", bool(hit_on))
            check("deep=false does not (the pair lives only in the container)", not hit_off)

            print("== budget (300,300,300): ~16.8M visits unclamped, 50,000 budget ==")
            build(c, addr, 300, 300, 300)
            d_off, _ = group(c, False, 5010, 5012)
            d_on, hit_early = group(c, True, 5010, 5012)
            d_late, hit_late = group(c, True, 200100048, 200100064)
            t_off = max(int(d_off.get("duration_ms") or 0), 1)
            t_on = int(d_on.get("duration_ms") or 0)
            check("deep=true returns with no deadline hit", d_on.get("deadline_hit") is False
                  and d_late.get("deadline_hit") is False,
                  f"{d_on.get('deadline_hit')} / {d_late.get('deadline_hit')}")
            check("deep=true is within a small multiple of deep=false (x10)", t_on <= 10 * t_off + 200,
                  f"{t_on} ms against {t_off} ms")
            check("deep=true finds the early pair", bool(hit_early))
            check("the late pair is absent: the budget stopped the walk", not hit_late)
        finally:
            build(c, addr, 0, 0, 0)
            print("   fixture emptied (0,0,0)")
    failed = [n for n, ok, _ in results if not ok]
    print(f"\n{len(results) - len(failed)}/{len(results)} passed")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
