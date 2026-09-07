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
  * the `inline int` DECLARATIONS in Grimoire.h (the defaults),
  * the body of `ApplyPropertyFamily` itself, which is the one legitimate publisher,
  * ONE named exception: `Ubel.cpp`'s ArrayProperty probe assigns FARRAYPROP_INNER on its
    own, because UE5.3+ places `EArrayPropertyFlags` before `Inner`, so that member
    legitimately diverges from the shared base AFTER calibration. It re-probes per field per
    walk rather than latching, so a family write that resets it to the base is corrected on
    the next array the walker meets.

Adding a new writer is fine -- route it through `ApplyPropertyFamily` and this gate is happy.
Adding a new EXCEPTION means editing the list below, which is the point: it makes the second
exception a decision somebody takes deliberately instead of a line that slips in.
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

# The ONE sanctioned direct writer outside ApplyPropertyFamily. Keyed by (file, member) and
# required to carry its justification nearby, so deleting the reasoning breaks the gate too.
EXCEPTIONS = [
    {
        "file": "Ubel.cpp",
        "member": "FARRAYPROP_INNER",
        "why": "UE5.3+ puts EArrayPropertyFlags before Inner, so this member legitimately "
               "diverges from the shared base after calibration; the probe re-runs per field "
               "per walk rather than latching.",
        # A token that must appear within `window` lines of the assignment, so the exception
        # cannot silently move to an unrelated site that happens to be in the same file.
        "near": "Correcting FARRAYPROP_INNER",
        "window": 6,
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
    """`inline int FBYTEPROP_ENUM = 0x78;` in Grimoire.h — the default, not a writer."""
    return re.match(r"\s*inline\s+(constexpr\s+)?int\s+", line) is not None


def main():
    want_list = "--list" in sys.argv[1:]
    violations = []
    allowed = []
    scanned = 0

    for name in sorted(os.listdir(SRC)):
        if not name.endswith((".cpp", ".h")):
            continue
        path = os.path.join(SRC, name)
        lines = open(path, encoding="utf-8", errors="replace").read().split("\n")
        scanned += 1

        in_apply = False
        for i, line in enumerate(lines):
            code = strip_code(line)

            # Track the body of ApplyPropertyFamily — the legitimate publisher.
            if "inline void ApplyPropertyFamily" in code:
                in_apply = True
            elif in_apply and code.strip() == "}":
                in_apply = False

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
