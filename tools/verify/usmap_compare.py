r"""Keyed diffs over USMAP exports -- the measurements that closed L30 and L39.

    py tools/verify/usmap_compare.py census   <a.usmap> [<b.usmap> ...]
    py tools/verify/usmap_compare.py enums    <old.usmap> <new.usmap> [<reference.usmap>]
    py tools/verify/usmap_compare.py inners   <old.usmap> <new.usmap> [<reference.usmap>]
    py tools/verify/usmap_compare.py slots    <a.usmap> [<b.usmap> ...]
    py tools/verify/usmap_compare.py schema   <ours.usmap> <reference.usmap> [Struct ...]

⛔ NOT A GATE, and no path here has a default -- see the header of `usmap_reader.py` for why
(every input lives outside the repo, so a gate built on this would fail CI and rot).

WHAT EACH SUBCOMMAND SETTLES

  census   the anti-self-comparison guard, and the only honest source of a record count.
  enums    `[A4-USMAP-ENUM-UNDERLYING]`: every TOP-LEVEL enum property's underlying width,
           old vs new, plus how each compares with a reference writer. A single-bucket
           histogram (`{Byte: N}`) is the pre-fix signature -- no correct writer produces it.
  inners   `[A4-USMAP-CONTAINER-ENUM]`: every CONTAINER INNER, old vs new, split into the
           three things that must be told apart --
             A1  Enum[under=W][None]  -> a named enum      (the inner's name reached the file)
             A2  a bare ByteProperty  -> Enum[under=W][E]  (a TEnumAsByte inner)
             C   a bare ByteProperty in BOTH               (a real TArray<uint8>: THE CONTROL)
           ⭐ A2 and C are the discriminator/control pair. A fix that blanket-promoted every
           ByteProperty inner would show a large A2 and an empty C, and that is a FAILURE.
  slots    which container ARM each enum inner sits in (Array / Set / MapKey / MapValue /
           Optional). The four arms are separate wire keys (`inner_enum` / `elem_enum` /
           `key_enum` / `value_enum`, `Fern.cpp:2244-2247`), so a total hides which of them an
           artifact never exercised. Measured 2026-09-16, our post-fix DumperTest export had
           Array 22 / MapKey 18 / MapValue 1 / **Set 0 / Optional 0**.
  schema   `[USMAP-INHERITED-DUPES]`: per struct, the record count and the schema index of
           each property against a reference writer. A child that repeats its super's
           properties shows a record count equal to its own PLUS its chain's, and every own
           index shifted by exactly the chain's size.

⚠ EVERY COUNT IS OVER SHARED KEYS. Two exports of the "same" fixture are not a controlled
before/after -- measured on ours, 7,885 vs 7,689 structs and 209 struct names present only in
the older file. Whole-file deltas mix the fix with twenty days of unrelated walker changes;
only a key-by-key diff attributes anything.

⚠ "BYTE-IDENTICAL" IS THE WRONG WORD for two writers agreeing. A descriptor ends in a
name-table index and the tables are per-file, so the bytes can never match. What matches is
the DECODED type tree, which is what this compares.
"""
import collections
import os
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.stderr.reconfigure(encoding="utf-8", errors="replace")

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from usmap_reader import load, SHORT, T_ENUM  # noqa: E402


def _short(path):
    return path.replace("/", "\\").rsplit("\\", 1)[-1]


def _under(t):
    return SHORT.get(t.under, "t%d" % t.under)


# ⚠ WIDTH and SIGNEDNESS are different failures and must never be summed. Width decides how
# many bytes a consumer advances, so a width disagreement misaligns everything after it;
# signedness only decides how the value prints. `[A4-USMAP-ENUM-UNDERLYING]`'s own header
# records the signedness half as a KNOWN residual ("the width a consumer deserializes but not
# its signedness ... the exact fix is a DLL-side underlying-type key"), so reporting those as
# regressions is a false FAILURE -- the mirror image of a false pass, and just as costly.
_WIDTH_OF = {0: 1, 23: 1, 20: 2, 22: 2, 2: 4, 19: 4, 21: 8, 18: 8}


def _width(t):
    return _WIDTH_OF.get(t.under)


# --------------------------------------------------------------------- census
def cmd_census(paths):
    seen = collections.defaultdict(list)
    for p in paths:
        u = load(p)
        line = u.census()
        print("%-46s %s" % (_short(p)[:46], line))
        seen[line].append(p)
    for line, group in seen.items():
        if len(group) > 1:
            print("\n⚠ IDENTICAL CENSUS for %d inputs -- same file passed twice?" % len(group))


# ---------------------------------------------------------- top-level enums
def _toplevel_enums(u):
    out = {}
    for s in u.structs:
        for _idx, _dim, pname, ty in s.records:
            if ty.is_enum:
                out["%s.%s" % (s.name, pname)] = ty
    return out


def cmd_enums(paths):
    old, new = load(paths[0]), load(paths[1])
    ref = load(paths[2]) if len(paths) > 2 else None
    O, N = _toplevel_enums(old), _toplevel_enums(new)

    for label, table in (("OLD", O), ("NEW", N)):
        hist = collections.Counter(_under(t) for t in table.values())
        nameless = sum(1 for t in table.values() if t.enum_name == "None")
        print("%-4s %-40s enum records=%-6d underlying=%s  named 'None'=%d"
              % (label, _short(table and paths[0 if label == "OLD" else 1])[:40],
                 len(table), dict(hist), nameless))

    shared = sorted(set(O) & set(N))
    moved = [k for k in shared if _under(O[k]) != _under(N[k])]
    print("\nshared top-level enum records: %d   underlying CHANGED: %d"
          % (len(shared), len(moved)))
    for k in moved[:10]:
        print("   %-56s %s -> %s" % (k[:56], _under(O[k]), _under(N[k])))

    if ref is None:
        return
    R = _toplevel_enums(ref)
    for label, table in (("NEW", N), ("OLD", O)):
        both = [k for k in table if k in R]
        width = [k for k in both if _width(table[k]) != _width(R[k])]
        signed = [k for k in both
                  if _width(table[k]) == _width(R[k]) and table[k].under != R[k].under]
        print("\n%s vs reference: %d shared enum records, WIDTH disagreements %d, "
              "SIGNEDNESS-only %d"
              % (label, len(both), len(width), len(signed)))
        for k in sorted(width)[:8]:
            print("   WIDTH     %-46s ours=%-8s ref=%s"
                  % (k[:46], _under(table[k]), _under(R[k])))
        for k in sorted(signed)[:8]:
            print("   SIGNED    %-46s ours=%-8s ref=%s"
                  % (k[:46], _under(table[k]), _under(R[k])))


# ------------------------------------------------------------ container inners
def _inner_slots(u):
    """'Struct.Prop' -> tuple of (slot, Type) for every descendant of a container."""
    out = {}
    for s in u.structs:
        for _idx, _dim, pname, ty in s.records:
            inner = [(slot, t) for slot, t in ty.walk() if slot is not None]
            if inner:
                out["%s.%s" % (s.name, pname)] = inner
    return out


def _labels(slots):
    return tuple(t.label() for _s, t in slots)


def cmd_inners(paths):
    old, new = load(paths[0]), load(paths[1])
    ref = load(paths[2]) if len(paths) > 2 else None
    O, N = _inner_slots(old), _inner_slots(new)
    shared = sorted(set(O) & set(N))
    print("container properties: old=%d new=%d shared=%d" % (len(O), len(N), len(shared)))

    def named_enum(t):
        return t.is_enum and t.enum_name not in (None, "None")

    a1 = [k for k in shared
          if any(t.is_enum and t.enum_name == "None" for _s, t in O[k])
          and any(named_enum(t) for _s, t in N[k])]
    a2 = [k for k in shared
          if any(t.kind == 0 for _s, t in O[k]) and any(named_enum(t) for _s, t in N[k])
          and k not in set(a1)]
    ctrl = [k for k in shared if _labels(O[k]) == _labels(N[k])
            and any(t.kind == 0 for _s, t in O[k])]
    back = [k for k in shared
            if any(named_enum(t) for _s, t in O[k]) and any(t.kind == 0 for _s, t in N[k])]

    print("\nA1  inner enum was NAMELESS ([None]) -> carries its real enum : %d" % len(a1))
    for k in a1[:8]:
        print("   %-50s %s -> %s" % (k[:50], ",".join(_labels(O[k])), ",".join(_labels(N[k]))))
    print("\nA2  inner was a BARE Byte -> Enum[under=W][name]              : %d" % len(a2))
    for k in a2[:8]:
        print("   %-50s %s -> %s" % (k[:50], ",".join(_labels(O[k])), ",".join(_labels(N[k]))))
    print("\nCONTROL  bare Byte inner in BOTH (a real TArray<uint8>)       : %d" % len(ctrl))
    for k in ctrl[:6]:
        print("   %-50s %s" % (k[:50], ",".join(_labels(O[k]))))
    print("\n⛔ REGRESSED  named enum inner -> bare Byte                    : %d" % len(back))
    for k in back[:6]:
        print("   %-50s %s -> %s" % (k[:50], ",".join(_labels(O[k])), ",".join(_labels(N[k]))))

    if ref is None:
        return
    R = _inner_slots(ref)
    # ⚠ MISSING is the category that matters most on a PRE-fix file and the one a naive
    # enum-vs-enum comparison cannot see: the reference calls the inner an enum and we emit a
    # bare ByteProperty, so there is no enum on our side to compare names with. Counting only
    # NAME/WIDTH disagreements would report a pre-fix file as nearly clean.
    for label, table in (("NEW", N), ("OLD", O)):
        agree = name_bad = width_bad = missing = extra = 0
        detail = []
        for k in set(table) & set(R):
            for (sa, ta), (sb, tb) in zip(table[k], R[k]):
                if sa != sb:
                    continue
                if tb.is_enum and not ta.is_enum:
                    missing += 1
                    detail.append((k, ta.label(), tb.label(), "MISSING"))
                elif ta.is_enum and not tb.is_enum:
                    extra += 1
                    detail.append((k, ta.label(), tb.label(), "EXTRA"))
                elif not (ta.is_enum and tb.is_enum):
                    continue
                elif ta.enum_name != tb.enum_name:
                    name_bad += 1
                    detail.append((k, ta.label(), tb.label(), "NAME"))
                elif ta.under != tb.under:
                    width_bad += 1
                    detail.append((k, ta.label(), tb.label(), "WIDTH"))
                else:
                    agree += 1
        print("\n%s vs reference, container inner slots:" % label)
        print("     AGREE %d   NAME-bad %d   WIDTH-bad %d   MISSING (ref=enum, ours=not) %d"
              "   EXTRA (ours=enum, ref=not) %d"
              % (agree, name_bad, width_bad, missing, extra))
        for k, a, b, why in sorted(detail)[:10]:
            print("   %-8s %-40s ours=%-34s ref=%s" % (why, k[:40], a, b))


# ---------------------------------------------------------------- slot survey
def cmd_slots(paths):
    for p in paths:
        u = load(p)
        allslots = collections.Counter()
        enumslots = collections.Counter()
        for s in u.structs:
            for _idx, _dim, _pname, ty in s.records:
                for slot, t in ty.walk():
                    if slot is None:
                        continue
                    allslots[slot] += 1
                    if t.is_enum:
                        enumslots[slot] += 1
        print("%-42s records=%-7d" % (_short(p)[:42], u.record_count))
        print("     all container slots : %s" % dict(allslots))
        print("     ENUM inner slots    : %s" % dict(enumslots))


# --------------------------------------------------------- schema index / order
def cmd_schema(paths):
    ours, ref = load(paths[0]), load(paths[1])
    wanted = paths[2:]
    O, R = ours.by_name(), ref.by_name()
    ok = ours.by_key()
    rk = ref.by_key()

    shared = set(ok) & set(rk)
    same = sum(1 for k in shared if ok[k][1] == rk[k][1])
    print("properties in both: %d   schema index SAME %d   DIFFERENT %d"
          % (len(shared), same, len(shared) - same))

    # ⚠ The DENOMINATOR is the fragile half of this measurement, so say what it hides. Both
    # of our artifacts declare AnimBlueprintGeneratedConstantData twice; in the reference
    # export the second copy has 0 records and in ours it has 52, so a reader that keyed
    # structs by NAME and kept the last copy reports 52 FEWER shared keys (27,491 instead of
    # 27,543). The DIFFERENT count is unaffected either way -- quote that one.
    for label, u in (("ours", ours), ("ref ", ref)):
        collapsed, dups = u.collapsed_keys()
        if collapsed or dups:
            print("   %s: %d record(s) hidden by duplicate keys; struct names declared twice: %s"
                  % (label, collapsed, ", ".join(dups) or "none"))

    per = collections.Counter()
    for k in shared:
        if ok[k][1] != rk[k][1]:
            per[k.split(".", 1)[0]] += 1
    print("classes affected: %d   worst: %s" % (len(per), per.most_common(6)))

    for name in wanted:
        a, b = O.get(name), R.get(name)
        if a is None or b is None:
            print("\n%-34s missing from %s" % (name, "ours" if a is None else "reference"))
            continue
        print("\n%-34s ours: super=%-22s records=%-4d first=%s@%d"
              % (name, a.super, len(a.records),
                 a.records[0][2] if a.records else "-",
                 a.records[0][0] if a.records else -1))
        print("%-34s ref : super=%-22s records=%-4d first=%s@%d"
              % ("", b.super, len(b.records),
                 b.records[0][2] if b.records else "-",
                 b.records[0][0] if b.records else -1))


CMDS = {"census": cmd_census, "enums": cmd_enums, "inners": cmd_inners,
        "slots": cmd_slots, "schema": cmd_schema}


def main():
    if len(sys.argv) < 3 or sys.argv[1] not in CMDS:
        raise SystemExit(__doc__)
    CMDS[sys.argv[1]](sys.argv[2:])


if __name__ == "__main__":
    main()
