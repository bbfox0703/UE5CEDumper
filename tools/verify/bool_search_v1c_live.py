r"""Two register rows on DumperTest, over the pipe only: [BOOL-NATIVE-SEARCH] step 1 and V1c's "a Next Scan prunes".

    py tools/verify/bool_search_v1c_live.py --pid 1234

BOOL-NATIVE-SEARCH step 1: search_properties and search_properties_batch must mark DumperTestActor's native
bPlainBool with "bool_native": true and no bool_mask, and its packed bFlagA with a single-bit bool_mask and no
bool_native -- in the single reply and in both batch envelopes. It also runs the batch over the Interesting Properties
seed queries (game_only false) and names one bool_native row, the target the UI half (step 2b) needs.

V1c: a value scan over a TOptional<int32> (Opt_Int_Set, set to 24680 by the fixture) must find it, a refine to a value
it does not hold must prune it, and a refine to the value it holds must keep it at the same address (the control: a
refine that dropped every row would pass the prune alone).
The game, its pid and the seed list's source are arguments or the repo's own files, never written here.
"""
import argparse
import pathlib
import re
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from pipe_client import PipeClient   # noqa: E402

REPO = HERE.parent.parent
results: list[tuple[str, bool, str]] = []


def check(name: str, ok: bool, detail: str = "") -> bool:
    results.append((name, ok, detail))
    print(f"  [{'PASS' if ok else 'FAIL'}] {name}" + (f" -- {detail}" if detail else ""))
    return ok


def single_bit(mask) -> bool:
    return isinstance(mask, int) and mask > 0 and (mask & (mask - 1)) == 0


def seed_queries() -> list[str]:
    """PropertyScoringTable.SeedQueries, read from the source so the list cannot drift from the panel's."""
    src = (REPO / "ui/UE5DumpUI/Services/PropertyScoringTable.cs").read_text(encoding="utf-8")
    at = src.index("public static readonly string[] SeedQueries")
    body = src[src.index("{", at):src.index("};", at)]
    body = re.sub(r"//[^\n]*", "", body)
    return re.findall(r'"([^"]+)"', body)


def actor_row(rows, name):
    return [r for r in rows if r.get("prop_name") == name and r.get("class_name") == "DumperTestActor"]


def bool_rows(c) -> None:
    print("== [BOOL-NATIVE-SEARCH] step 1: search_properties / search_properties_batch ==")
    for name, native in (("bPlainBool", True), ("bFlagA", False)):
        rep = c.request("search_properties", query=name, limit=100)
        rows = actor_row(rep.get("data", rep).get("results") or [], name)
        if not check(f"search_properties finds DumperTestActor.{name}", len(rows) == 1, f"{len(rows)} row(s)"):
            continue
        r = rows[0]
        if native:
            check(f"{name}: bool_native true, no bool_mask", r.get("bool_native") is True and "bool_mask" not in r,
                  f"bool_native={r.get('bool_native')} bool_mask={r.get('bool_mask')}")
        else:
            check(f"{name}: single-bit bool_mask, no bool_native",
                  single_bit(r.get("bool_mask")) and "bool_native" not in r,
                  f"bool_mask={r.get('bool_mask')} bool_native={r.get('bool_native')}")

    rep = c.request("search_properties_batch", queries=["bPlainBool", "bFlagA"], game_only=True, limit=100)
    env = {e.get("query"): e for e in rep.get("data", rep).get("per_query") or []}
    for name, native in (("bPlainBool", True), ("bFlagA", False)):
        rows = actor_row((env.get(name) or {}).get("results") or [], name)
        if not check(f"batch envelope '{name}' carries the row", len(rows) == 1, f"{len(rows)} row(s)"):
            continue
        r = rows[0]
        ok = (r.get("bool_native") is True and "bool_mask" not in r) if native else \
             (single_bit(r.get("bool_mask")) and "bool_native" not in r)
        check(f"batch {name}: {'bool_native, no mask' if native else 'single-bit mask, no bool_native'}", ok,
              f"bool_native={r.get('bool_native')} bool_mask={r.get('bool_mask')}")

    seeds = seed_queries()
    rep = c.request("search_properties_batch", queries=seeds, game_only=False, limit=200)
    d = rep.get("data", rep)
    natives, masked, neither = [], 0, 0
    for e in d.get("per_query") or []:
        for r in e.get("results") or []:
            if r.get("prop_type") != "BoolProperty":
                continue
            if r.get("bool_native"):
                natives.append((e.get("query"), r))
            elif r.get("bool_mask"):
                masked += 1
            else:
                neither += 1
    check(f"the seed batch ({len(seeds)} queries, game_only false) has a bool_native row for step 2b",
          bool(natives), f"{len(natives)} native, {masked} masked, {neither} with neither")
    if natives:
        q, r = natives[0]
        print(f"  step 2b target: query '{q}' -> {r.get('class_name')}.{r.get('prop_name')} "
              f"(+0x{r.get('prop_offset', 0):X}, {r.get('class_path')})")
    masked_rows = [(e.get("query"), r) for e in d.get("per_query") or [] for r in e.get("results") or []
                   if r.get("prop_type") == "BoolProperty" and r.get("bool_mask") and not r.get("bool_native")]
    if masked_rows:
        q, r = masked_rows[0]
        print(f"  a packed row beside it: query '{q}' -> {r.get('class_name')}.{r.get('prop_name')} "
              f"mask 0x{r.get('bool_mask'):X}")


def candidates(c, sid):
    rep = c.request("query_candidates", session_id=sid, offset=0, limit=5000)
    d = rep.get("data", rep)
    return d.get("candidates") or d.get("results") or []


def opt_rows(rows):
    return [r for r in rows if (r.get("field_name") or "").startswith("Opt_Int_Set")]


def v1c(c) -> None:
    print("\n== V1c: a Next Scan prunes a TOptional row ==")
    inst = (c.request("find_instances", class_name="DumperTestActor", max_results=5).get("instances") or [])
    if not check("a live DumperTestActor", bool(inst), str(len(inst))):
        return
    w = c.request("walk_instance", addr=inst[0]["addr"])
    fields = w.get("data", w).get("fields") or []
    opt = [f for f in fields if f.get("name") == "Opt_Int_Set"]
    print(f"  Opt_Int_Set on the instance: {opt[0] if opt else 'absent'}"[:300])

    def scan(value):
        rep = c.request("begin_value_scan", data_type="Int32", scan_type="Exact", value=str(value),
                        game_only=True, max_results=5000)
        d = rep.get("data", rep)
        return d.get("session_id"), d

    sid, d = scan(24680)
    first = opt_rows(candidates(c, sid))
    check("the first scan for 24680 finds Opt_Int_Set", bool(first),
          f"{len(first)} row(s); {first[0].get('class_name')} addr={first[0].get('addr')}" if first else str(d)[:200])
    c.request("refine_value_scan", session_id=sid, scan_type="Exact", value="24681")
    after = opt_rows(candidates(c, sid))
    check("a refine to 24681 prunes it", first and not after, f"{len(after)} row(s) left")
    c.request("end_value_scan", session_id=sid)

    sid2, _ = scan(24680)
    first2 = opt_rows(candidates(c, sid2))
    c.request("refine_value_scan", session_id=sid2, scan_type="Exact", value="24680")
    kept = opt_rows(candidates(c, sid2))
    same = bool(first2) and bool(kept) and {r.get("addr") for r in kept} == {r.get("addr") for r in first2}
    check("control: a refine to 24680 keeps it at the same address", same,
          f"before {[r.get('addr') for r in first2]} after {[r.get('addr') for r in kept]}")
    c.request("end_value_scan", session_id=sid2)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--pid", type=int, required=True)
    a = ap.parse_args()
    with PipeClient(pid=a.pid) as c:
        print("build", c.assert_build())
        c.ensure_scanned()
        bool_rows(c)
        v1c(c)
    failed = [n for n, ok, _ in results if not ok]
    print(f"\n{len(results) - len(failed)}/{len(results)} passed")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
