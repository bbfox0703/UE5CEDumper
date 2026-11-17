"""Pin the sizeof(FProperty) subclass-extension FAMILY: nobody assigns a member directly.

    py tools/check_property_family.py
    py tools/check_property_family.py --list

WHY THIS EXISTS. `DynOff::FSTRUCTPROP_STRUCT`, `FARRAYPROP_INNER`, `FBOOLPROP_FIELDSIZE`,
`FBYTEPROP_ENUM` and `FENUMPROP_ENUM` are FIVE NAMES FOR ONE MEASUREMENT -- the first
subclass field, i.e. sizeof(FProperty) -- with FENUMPROP_ENUM 8 bytes later because
FEnumProperty declares `FNumericProperty* UnderlyingProp` before its `UEnum*`. They must
move together, and `Grimoire.h` says so in as many words: "Never assign a member of this
family directly."

⚠ THE FAILURE IS SILENT AND HALF-CORRECT, WHICH IS WHY IT SURVIVES REVIEW. A split family
does not crash and does not look broken: struct reads stay right while TArray element
descriptors and every enum name read 8 bytes off. Audit #5 G12 found exactly that -- Genau's
Step 2.5 default block set only two of the five, so any run taking a "keeping defaults" exit
shipped a split family for the whole session, deterministically, first run, no concurrency
needed.

⛔ AND G12'S OWN FIX WAS INCOMPLETE, WHICH IS THE REAL REASON THIS IS A SCRIPT AND NOT A
COMMENT. It introduced `ApplyPropertyFamily`, routed the writers it knew about, and recorded
"Both writers now go through here so the two cannot drift again." There were THREE. The
third -- `Ubel.cpp` WalkInstance's StructProperty probe -- kept assigning
`DynOff::FSTRUCTPROP_STRUCT` directly until 2026-09-07, so the exact failure G12 existed to
prevent stayed reachable by the one path G12 had not counted. A prose invariant plus a
hand-counted writer list is what failed; counting them mechanically is the fix.

WHAT IT ALLOWS, and nothing else:
  * the DECLARATIONS in Grimoire.h (the defaults; `DynOff::RuntimeOffset` since
    [CPP-HARDENING-2026-11-14], `inline int` before),
  * the body of `ApplyPropertyFamily` itself, which is the one legitimate publisher,
  * ONE named exception: `DynOff::SetArrayInner` in Grimoire.h assigns FARRAYPROP_INNER on
    its own, for `Ubel.cpp`'s ArrayProperty probe, because UE5.3+ places `EArrayPropertyFlags`
    before `Inner`, so that member legitimately diverges from the shared base AFTER
    calibration. The probe re-probes per field per walk rather than latching, so a family
    write that resets it to the base is corrected on the next array the walker meets. Until
    2026-11-15 the probe assigned the member itself, outside any lock. The helper's CALLER is
    pinned the same way: only that probe, beside its "Correcting FARRAYPROP_INNER" marker.

Adding a new writer is fine -- route it through `ApplyPropertyFamily` and this gate is happy.
Adding a new EXCEPTION means editing the list below, which is the point: it makes the second
exception a decision somebody takes deliberately instead of a line that slips in.

THE FAMILY EPOCH'S WRITERS ([DYNOFF-LATCH-STALE]). `DynOff::g_propertyFamilyEpoch` keys every
family-keyed cache, and it moves in two places only: `ApplyPropertyFamily` on a move, and
`RetireFamilyEpoch` when an offset validation ends. The retire is seqlock-shaped -- it bumps
`g_offsetValidations` to odd, then the epoch, then the count to even -- and `WalkClassEx` relies on
that order: a count bumped once, before the epoch, let a walk read the new count with its plain walk
still keyed under the old epoch and file a mid-validation walk for the session (review
wf_09b53b67-caf). Reordering the three, or a third writer of the epoch that skips the count, is
exactly as silent as a split family, so both are checked here.
"""
import os
import re
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(REPO, "dll", "src")

FAMILY = ["FSTRUCTPROP_STRUCT", "FARRAYPROP_INNER", "FBOOLPROP_FIELDSIZE",
          "FBYTEPROP_ENUM", "FENUMPROP_ENUM"]

# An assignment to a family member: the name, then `=` that is not `==`, `!=`, `<=`, `>=`.
ASSIGN = re.compile(r"\b(" + "|".join(FAMILY) + r")\s*=(?!=)")
# A call of DynOff::SetArrayInner, the helper behind the one exception below.
SET_ARRAY_INNER_CALL = re.compile(r"\bSetArrayInner\s*\(")
# A write of the family epoch or of the retire count, in every form an atomic takes one: a member call (any fetch_*,
# store, exchange or compare_exchange_*), a free std::atomic_* call on its address, a prefix or postfix ++/--, or an
# assignment. Review wf_090d2608-d9f (R5-GATE-PREFIX-INC): the first pattern missed the prefix ++, the idiomatic one.
def atomic_write(name):
    member = r"\b" + name + r"\s*\.\s*(?:fetch_\w+|store|exchange|compare_exchange_\w+)\s*\("
    free = r"\batomic_(?:fetch_\w+|store\w*|exchange\w*|compare_exchange_\w+)\s*\(\s*&\s*(?:\w+::)*" + name + r"\b"
    prefix = r"(?:\+\+|--)\s*(?:\w+::)*" + name + r"\b"
    postfix_or_assign = r"\b" + name + r"\s*(?:\+\+|--|[-+|&^]?=(?!=))"
    return re.compile("|".join((member, free, prefix, postfix_or_assign)))


EPOCH_WRITE = atomic_write("g_propertyFamilyEpoch")
COUNT_WRITE = atomic_write("g_offsetValidations")

# The ONE sanctioned direct writer outside ApplyPropertyFamily. Keyed by (file, member) and
# required to carry its justification nearby, so deleting the reasoning breaks the gate too.
EXCEPTIONS = [
    {
        "file": "Grimoire.h",
        "member": "FARRAYPROP_INNER",
        "why": "UE5.3+ puts EArrayPropertyFlags before Inner, so this member legitimately "
               "diverges from the shared base after calibration; Ubel's probe re-runs per field "
               "per walk rather than latching, and writes through SetArrayInner, under the "
               "family write lock.",
        # A token that must appear within `window` lines of the assignment, so the exception
        # cannot silently move to an unrelated site that happens to be in the same file.
        "near": "inline void SetArrayInner",
        "window": 3,
    },
]


STRING_LIT = re.compile(r'"(?:[^"\\]|\\.)*"')


def strip_code(line):
    """Drop the comment tail AND every string literal.

    ⚠ Both are required, and the string half is not obvious: this codebase logs its own
    offsets, so `Sein::Info(..., "FSTRUCTPROP_STRUCT=0x%02X", ...)` and
    "FARRAYPROP_INNER=0x%X" contain a family name followed by `=` inside a FORMAT STRING.
    The first version of this gate matched those and reported two writers that do not
    exist (Fern.cpp:2624, Ubel.cpp:4420). A gate whose failures are diagnostics rather
    than defects gets switched off, so the literals go first.
    """
    return STRING_LIT.sub('""', line.split("//", 1)[0])


def is_declaration(line):
    """`inline RuntimeOffset FBYTEPROP_ENUM{0x78};` (or the older `inline int ... = 0x78;`) in Grimoire.h -- the
    default, not a writer."""
    return re.match(r"\s*inline\s+(constexpr\s+)?(int|RuntimeOffset)\s+", line) is not None


def main():
    want_list = "--list" in sys.argv[1:]
    violations = []
    allowed = []
    scanned = 0
    retire_ops = []

    for name in sorted(os.listdir(SRC)):
        if not name.endswith((".cpp", ".h")):
            continue
        path = os.path.join(SRC, name)
        lines = open(path, encoding="utf-8", errors="replace").read().split("\n")
        scanned += 1

        in_apply = False
        in_retire = False
        for i, line in enumerate(lines):
            code = strip_code(line)

            # Track the body of ApplyPropertyFamily — the legitimate publisher.
            if "inline void ApplyPropertyFamily" in code:
                in_apply = True
            elif in_apply and code.strip() == "}":
                in_apply = False
            # ...and of RetireFamilyEpoch, the epoch's other writer, whose order is pinned below.
            if "inline void RetireFamilyEpoch" in code:
                in_retire = True
            elif in_retire and code.strip() == "}":
                in_retire = False

            if not is_declaration(code):
                if EPOCH_WRITE.search(code):
                    if in_retire:
                        retire_ops.append("epoch")
                    elif not in_apply:
                        violations.append(
                            (name, i + 1, "g_propertyFamilyEpoch",
                             "the family epoch moves only in ApplyPropertyFamily (a move) and RetireFamilyEpoch (the end "
                             "of an offset validation, paired with g_offsetValidations)"))
                if COUNT_WRITE.search(code):
                    if in_retire:
                        retire_ops.append("count")
                    else:
                        violations.append(
                            (name, i + 1, "g_offsetValidations",
                             "the retire count moves only in RetireFamilyEpoch, around the epoch"))

            # The helper is a writer too, so its caller is pinned as the old direct assignment was: only Ubel.cpp's
            # ArrayProperty probe, beside its marker (which sits in a log string, hence the raw lines). Without this
            # a second solo writer of Inner could call the helper and pass (review 2026-11-15).
            if SET_ARRAY_INNER_CALL.search(code) and "inline void SetArrayInner" not in code:
                lo, hi = max(0, i - 6), min(len(lines), i + 7)
                if name == "Ubel.cpp" and any("Correcting FARRAYPROP_INNER" in l for l in lines[lo:hi]):
                    allowed.append((name, i + 1, "FARRAYPROP_INNER", "SetArrayInner from the ArrayProperty probe"))
                else:
                    violations.append(
                        (name, i + 1, "FARRAYPROP_INNER",
                         "SetArrayInner outside Ubel.cpp's ArrayProperty probe -- the one member that may leave the "
                         "family has one writer; a new one is a decision, made by editing this gate"))

            m = ASSIGN.search(code)
            if not m:
                continue
            member = m.group(1)

            if is_declaration(code):
                continue
            if in_apply:
                allowed.append((name, i + 1, member, "ApplyPropertyFamily body"))
                continue

            exc = next((e for e in EXCEPTIONS
                        if e["file"] == name and e["member"] == member), None)
            if exc:
                lo = max(0, i - exc["window"])
                hi = min(len(lines), i + exc["window"] + 1)
                if any(exc["near"] in l for l in lines[lo:hi]):
                    allowed.append((name, i + 1, member, "documented exception"))
                    continue
                violations.append(
                    (name, i + 1, member,
                     "matches a documented exception for this file+member, but the marker "
                     "'%s' is not within %d lines -- the exception has moved or its "
                     "justification was deleted" % (exc["near"], exc["window"])))
                continue

            violations.append(
                (name, i + 1, member,
                 "direct assignment -- route it through DynOff::ApplyPropertyFamily("
                 "DynOff::PropertyFamilyAtBase(base)) so all five names move together"))

    if retire_ops != ["count", "epoch", "count"]:
        violations.append(
            ("Grimoire.h", 0, "RetireFamilyEpoch",
             "must bump g_offsetValidations, then g_propertyFamilyEpoch, then g_offsetValidations, once each -- found %s"
             % (retire_ops or "no writes")))

    if want_list or violations:
        print("family members: %s" % ", ".join(FAMILY))
        for f, ln, mem, why in allowed:
            print("  ok    %-14s:%-5d %-20s %s" % (f, ln, mem, why))
    for f, ln, mem, why in violations:
        print("  FAIL  %s:%d  %s -- %s" % (f, ln, mem, why))

    if violations:
        print("\ncheck_property_family: %d direct family assignment(s) -- a SPLIT FAMILY does "
              "not crash,\n  it makes TArray element descriptors and enum names read 8 bytes "
              "off while struct\n  reads stay correct (Grimoire.h, audit #5 G12)." % len(violations))
        return 1

    print("CHECK OK: %d source file(s), %d sanctioned family writer(s), 0 direct assignments"
          % (scanned, len(allowed)))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
