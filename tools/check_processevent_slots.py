"""Re-derive DynOff::ProcessEventVTableSlotFor from the vendored RE-UE4SS vtable templates.

    py tools/check_processevent_slots.py
    py tools/check_processevent_slots.py --list

WHY THIS EXISTS. `dll/src/Grimoire.h` carries a per-UE-version ProcessEvent vtable slot table,
added by audit A2 from `vendor/RE-UE4SS/assets/VTableLayoutTemplates/`. It was a hand-transcribed
table over a vendored source -- the exact shape CLAUDE.md says to derive and never hand-edit -- and
nothing checked it. Meanwhile the register spent effort planning how to obtain the **5.2** figure
(a Ghidra/pdb pass over the Satisfactory UE5.2.1 depot), when 5.2 was already in the table.

⭐ THE DERIVATION, and it reproduces ALL TEN entries exactly:

    slot = index_of("ProcessEvent") * 8

over the concatenated `[UObjectBase]` + `[UObjectBaseUtility]` + `[UObject]` sections, **with the
repeated `__vecDelDtor` removed after the first**.

⚠ THAT DEDUPE IS THE WHOLE TRICK, and without it every row is wrong by a constant 0x10. The
templates restate `__vecDelDtor` at the head of each class section, but a single-inheritance vtable
holds ONE destructor slot for the whole chain, not one per class. Concatenating three sections
naively double-counts it twice. The tell that this is the right explanation rather than a fudge:
the raw error is **exactly +0x10 on all ten versions**, 4.27 through 5.08 -- a constant, not a
drift. A correction that has to vary per row would be a fudge; one that is constant across ten
independent files is a missing rule.

⚠ This checks the TABLE, not the game. The pattern scan is still primary at runtime and the table
is the fallback; a per-BUILD difference is not a bug. What this gate prevents is the table silently
disagreeing with the vendored templates it was transcribed from -- e.g. when a new UE version is
added by hand, or a template is re-vendored.
"""
import argparse
import os
import re
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TEMPLATES = os.path.join(ROOT, "vendor", "RE-UE4SS", "assets", "VTableLayoutTemplates")
GRIMOIRE = os.path.join(ROOT, "dll", "src", "Grimoire.h")

# The vtable of UObject is the concatenation of its inheritance chain, in this order.
CHAIN = ("UObjectBase", "UObjectBaseUtility", "UObject")

# ⚠ Not a filter of convenience -- see the module docstring. One destructor slot per OBJECT.
DTOR = "__vecDelDtor"


def slot_from_template(path):
    """ProcessEvent's byte offset in UObject's vtable, or None if the file has no such entry."""
    names = []
    sect = None
    with open(path, encoding="utf-8", errors="replace") as fh:
        for ln in fh:
            s = ln.strip()
            if not s or s.startswith(";"):
                continue
            if s.startswith("["):
                sect = s[1:-1]
                continue
            if sect not in CHAIN:
                continue
            if s == DTOR and names:
                continue          # already have the one destructor slot
            names.append(s)
    return names.index("ProcessEvent") * 8 if "ProcessEvent" in names else None


def templates():
    """{version_code: slot} for every vendored template, e.g. {427: 0x220, 502: 0x268}."""
    out = {}
    if not os.path.isdir(TEMPLATES):
        return out
    for fn in sorted(os.listdir(TEMPLATES)):
        m = re.match(r"^VTableLayout_(\d+)_(\d+)(_CasePreserving)?_Template\.ini$", fn)
        if not m:
            continue
        if m.group(3):
            # 4.27 CasePreserving is a separate file whose slot is identical; the table says so
            # in a comment. Skipping it keeps one row per version code.
            continue
        code = int(m.group(1)) * 100 + int(m.group(2))
        s = slot_from_template(os.path.join(TEMPLATES, fn))
        if s is not None:
            out[code] = s
    return out


def table():
    """Parse the `case NNN: ... return 0xNNN;` block out of ProcessEventVTableSlotFor."""
    with open(GRIMOIRE, encoding="utf-8", errors="replace") as fh:
        src = fh.read()
    i = src.find("ProcessEventVTableSlotFor")
    if i < 0:
        raise SystemExit("!! ProcessEventVTableSlotFor not found in %s" % GRIMOIRE)
    body = src[i:src.find("\n}", i)]
    out = {}
    for line in body.splitlines():
        if "return" not in line or "case" not in line:
            continue
        m = re.search(r"return\s+(0x[0-9A-Fa-f]+)", line)
        if not m:
            continue
        val = int(m.group(1), 16)
        for c in re.findall(r"case\s+(\d+)\s*:", line):
            out[int(c)] = val
    return out


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--list", action="store_true", help="print every row, not just mismatches")
    a = ap.parse_args()

    tpl, tab = templates(), table()
    if not tpl:
        print("check_processevent_slots: SKIP -- no vendored templates under %s"
              % os.path.relpath(TEMPLATES, ROOT))
        return 0
    if not tab:
        print("!! parsed 0 rows out of ProcessEventVTableSlotFor -- the switch shape changed")
        return 1

    bad, checked, absent = [], 0, []
    for code in sorted(tpl):
        want = tpl[code]
        if code not in tab:
            absent.append((code, want))
            continue
        checked += 1
        ok = tab[code] == want
        if not ok:
            bad.append((code, tab[code], want))
        if a.list or not ok:
            print("  UE %d.%-2d  table 0x%03X   templates 0x%03X   %s"
                  % (code // 100, code % 100, tab[code], want, "ok" if ok else "*** MISMATCH ***"))

    for code, want in absent:
        print("  UE %d.%-2d  NOT IN TABLE -- the vendored template derives 0x%03X"
              % (code // 100, code % 100, want))

    if bad:
        print("\ncheck_processevent_slots: FAIL -- %d row(s) disagree with the vendored templates.\n"
              "Grimoire.h's table is a transcription of those files; derive it, do not hand-edit.\n"
              "⚠ If a template was legitimately re-vendored, update the table, not this check."
              % len(bad))
        return 1

    extra = " (%d version(s) have a template but no table row)" % len(absent) if absent else ""
    print("CHECK OK: %d ProcessEvent vtable slot(s) match the vendored RE-UE4SS templates%s"
          % (checked, extra))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
