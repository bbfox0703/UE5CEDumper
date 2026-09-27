r"""Whole-pool before/after diff of what `walk_class` reads through a property's subclass slot.

    py tools/verify/pool_walk_diff.py capture <label>            # against the DLL injected now
    py tools/verify/pool_walk_diff.py diff <labelA> <labelB>     # two captures of the same fixture

WHY THIS EXISTS
  A change to what a SHARED reader accepts -- the struct / class / enum a property's subclass slot
  points to -- can only regress types that are rare, and a spot check of a dozen core engine classes
  never meets them: a Blueprint struct, an interface class, a map keyed by a struct. [STRUCTPROBE-ANY-NAME]
  added a class-chain test to every slot reader; the eleven-class walk used for the earlier rows
  could not have shown a UserDefinedStruct it now rejected. This walks EVERY class `list_classes`
  returns (game_only=false) through `walk_class_batch` and keeps, per field, every piece of type
  metadata read through the slot, so two builds on the same fixture compare row by row.

HOW TO USE IT
  Capture with the old DLL, kill the game, relaunch, inject the new DLL, capture again, diff. A fresh
  inject each time, and NOTHING else first (no Live Walker): a walk before the capture can move the
  property family and hide the very defect a slot change can cause (working-lessons 1.ai).
  `changed` is the verdict -- over the rows BOTH captures hold, so the two must be keyed alike: a capture records
  its key format, and `diff` refuses (exit 2) two formats, or captures that share no row at all, rather than report
  a vacuous "0 changed". Rows present in only one capture are per-session differences in the
  loaded classes (the field LIST comes from the plain chain walk, not the slot), and should be
  explained, not ignored. Captures go to out/sdk-live/pool_<label>.json (gitignored).
  Measured 2026-09-28, builds 3594 -> 3595: six fixtures, ~445,000 fields, 0 changed.
"""
import collections
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

OUT_DIR = Path(__file__).resolve().parents[2] / "out" / "sdk-live"
KEYS = ("struct_type", "obj_class", "meta_class", "inner_type", "inner_struct_type", "inner_obj_class",
        "key_type", "key_struct_type", "key_obj_class", "value_type", "value_struct_type", "value_obj_class",
        "elem_type", "elem_struct_type", "elem_obj_class", "enum_name", "inner_enum", "elem_enum", "key_enum")
KEY_FORMAT = "class_path"   # how `capture` keys its rows; `diff` refuses to compare two formats
OBJFAM = {"ObjectProperty", "ClassProperty", "WeakObjectProperty", "SoftObjectProperty", "SoftClassProperty",
          "InterfaceProperty", "LazyObjectProperty"}


def out_path(label):
    return OUT_DIR / f"pool_{label}.json"


def count(tot, f):
    t = f.get("type")
    if t == "StructProperty":
        tot["struct"] += 1
        tot["struct_typed"] += bool(f.get("struct_type"))
    elif t in OBJFAM:
        tot["objfam"] += 1
        tot["objfam_classed"] += bool(f.get("obj_class"))
    it = f.get("inner_type")
    if it == "StructProperty":
        tot["inner_struct"] += 1
        tot["inner_struct_typed"] += bool(f.get("inner_struct_type"))
    elif it in OBJFAM:
        tot["inner_obj"] += 1
        tot["inner_obj_classed"] += bool(f.get("inner_obj_class"))
    for side in ("key", "value", "elem"):
        st = f.get(side + "_type")
        if st == "StructProperty":
            tot[side + "_struct"] += 1
            tot[side + "_struct_typed"] += bool(f.get(side + "_struct_type"))
        elif st in OBJFAM:
            tot[side + "_obj"] += 1
            tot[side + "_obj_classed"] += bool(f.get(side + "_obj_class"))


def capture(label):
    from pipe_client import PipeClient
    tot = collections.Counter()
    rows = {}
    with PipeClient(timeout=600.0) as c:
        off = c.request("get_offsets")
        print("build:", off.get("build_info"), "fproperty:", off.get("use_fproperty"))
        lc = c.request("list_classes", game_only=False, limit=50000)
        classes = lc.get("classes", [])
        if lc.get("truncated"):
            print("WARNING: list_classes is truncated -- the capture is a page, not the pool")
        print("classes:", len(classes))
        addrs = [x["class_addr"] for x in classes]
        # Rows are keyed by the class PATH, not its name: two class objects can share a name (a Blueprint class and
        # its re-instanced copy), and keyed by name the one walked last won -- a per-session difference that reads as
        # "fields only in one capture" (measured on DQ XI S, 2026-09-28).
        paths = [x.get("class_path") or x.get("class_name", "?") for x in classes]
        for i in range(0, len(addrs), 200):
            r = c.request("walk_class_batch", addrs=addrs[i:i + 200])
            for j, ci in enumerate(r.get("classes", [])):
                cname = paths[i + j] if i + j < len(paths) else ci.get("name", "?")
                for f in ci.get("fields", []):
                    rows[f"{cname}.{f.get('name')}@{f.get('offset')}"] = [f.get("type"),
                                                                          {k: f[k] for k in KEYS if f.get(k)}]
                    count(tot, f)
    print("totals:", dict(tot))
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    json.dump({"label": label, "key": KEY_FORMAT, "totals": dict(tot), "rows": rows},
              open(out_path(label), "w", encoding="utf-8"))
    print("written:", out_path(label))


def diff(la, lb):
    a = json.load(open(out_path(la), encoding="utf-8"))
    b = json.load(open(out_path(lb), encoding="utf-8"))
    # Captures before the key was recorded were keyed by class name.
    ka, kb = a.get("key", "class_name"), b.get("key", "class_name")
    if ka != kb:
        print(f"REFUSED: {la} is keyed by {ka}, {lb} by {kb} -- recapture one; a diff would compare nothing")
        return 2
    if not set(a["rows"]) & set(b["rows"]):
        print(f"REFUSED: {la} and {lb} share no row -- not two captures of one fixture")
        return 2
    print(la, a["totals"])
    print(lb, b["totals"])
    ra, rb = a["rows"], b["rows"]
    only_a = sorted(set(ra) - set(rb))
    only_b = sorted(set(rb) - set(ra))
    changed = sorted(k for k in set(ra) & set(rb) if ra[k] != rb[k])
    print(f"rows {len(ra)} vs {len(rb)}; only in {la} {len(only_a)}, only in {lb} {len(only_b)}, "
          f"changed {len(changed)}")
    for k in changed[:40]:
        print("  ", k, "\n      A:", ra[k], "\n      B:", rb[k])
    for k in only_a[:10]:
        print(f"   only {la}:", k)
    for k in only_b[:10]:
        print(f"   only {lb}:", k)
    return 1 if changed else 0


if __name__ == "__main__":
    if len(sys.argv) == 3 and sys.argv[1] == "capture":
        capture(sys.argv[2])
    elif len(sys.argv) == 4 and sys.argv[1] == "diff":
        sys.exit(diff(sys.argv[2], sys.argv[3]))
    else:
        print(__doc__)
        sys.exit(2)
