#!/usr/bin/env python3
"""Does a view model gain a NEW hard-coded status string? A ratchet, not a sweep.

    py tools/check_vm_status_literals.py            # compare with the baseline; exit 1 on any change
    py tools/check_vm_status_literals.py --update   # lower the baseline after literals moved out (refuses on growth)
    py tools/check_vm_status_literals.py --rebaseline  # rewrite it regardless: ONLY when the counting rules change
    py tools/check_vm_status_literals.py --list     # print every counted literal, exit 0

CLAUDE.md puts every UI string in en.axaml, but the view models set their status line
with C# literals. The maintainer's call ([VM-INLINE-STRINGS], 2026-09-26): NEW
view-model status strings go to en.axaml (Res.Get / Res.Format); the EXISTING ones stay,
because moving them means a large round of re-testing for little gain. So this gate
freezes the per-file count instead of demanding zero:

  * a file's count going UP fails -- a new literal; give it an en.axaml key instead;
  * a file's count going DOWN also fails, and prints the --update command -- so the
    baseline ratchets down, and a later literal cannot hide in the slack an old one left.

Counted: one per ASSIGNMENT to `StatusText` or to any property named `...Status` /
`...StatusText` (capitalised: a property, not a camelCase local), when its right-hand side
up to the `;` holds a non-empty string literal anywhere -- a plain value, a ternary or
switch arm, a concatenation, across lines. An empty "" is a clear, not text; // comment
lines are skipped. The first version matched only `StatusText = "` on one line, so a
ternary, a multi-line value or a sibling status property passed ([WIKI-REVIEW-GATES]).
Other properties (ErrorMessage, computed tooltips) are out of scope, as the decision was
about status strings.
"""
from __future__ import annotations

import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
VMS = os.path.join(ROOT, "ui", "UE5DumpUI", "ViewModels")
BASELINE = os.path.join(ROOT, "tools", "vm_status_literals_baseline.json")

# `X = ` or `X += ` where X is a capitalised ...Status / ...StatusText name; not `==`, `!=`, `=>`. `+=` counts: an
# appended clause is text the user reads as much as an assigned one ([I18N-READINESS-2026-11-04]; Live Funcs had seven).
ASSIGN = re.compile(r'\b((?:[A-Z]\w*)?Status(?:Text)?)\s*\+?=(?![=>])')
# A literal that is a COMPARISON operand or a PATTERN is logic, not text: `result == "ok" ? ... : ...`, `"x" => ...`,
# `is "x"`. It used to keep a line counted after its text had moved to en.axaml.
LOGIC_BEFORE = re.compile(r'(?:==|!=|\bis|\bcase)\s*$')
LOGIC_AFTER = re.compile(r'^\s*(?:==|!=|=>)')
# A resource key (Res.Get("str.X") / Res.Format("str.X", ...)) is the compliant form, not text.
RES_KEY = re.compile(r'str\.[\w.]+')
# A C# string token (plain, $, @, $@); group 1 is its content.
STRING = re.compile(r'(?:\$@|@\$|\$|@)?"((?:[^"\\\n]|\\.|"")*)"')
# The right-hand side up to the statement's `;` -- a `;` INSIDE a string does not end it.
RHS = re.compile(r'(?:(?:\$@|@\$|\$|@)?"(?:[^"\\\n]|\\.|"")*"|[^;"])*')


# Text holds a word: a run of two letters, check_axaml_strings' rule. " " joining two clauses, "…" or a glyph is not text.
WORDISH = re.compile(r"[A-Za-z]{2}")


def is_text(rhs: str, t: re.Match) -> bool:
    """A string token on a right-hand side that is display text: a word, not a resource key, not logic."""
    if not WORDISH.search(t.group(1)) or RES_KEY.fullmatch(t.group(1)):
        return False
    return not (LOGIC_BEFORE.search(rhs[:t.start()]) or LOGIC_AFTER.match(rhs[t.end():]))


def counted(text: str) -> list[str]:
    """[`line: text`, ...] for each counted assignment in one file's text (// comment lines already blanked)."""
    rows = []
    for m in ASSIGN.finditer(text):
        rhs = RHS.match(text, m.end()).group(0)
        if any(is_text(rhs, t) for t in STRING.finditer(rhs)):
            line_no = text.count("\n", 0, m.start()) + 1
            end = text.find("\n", m.start())
            rows.append(f"{line_no}: {text[m.start():end if end >= 0 else None].strip()[:120]}")
    return rows


def scan() -> dict[str, list[str]]:
    """{repo-relative path: [`line: text`, ...]} for every VM file with a counted literal."""
    found: dict[str, list[str]] = {}
    for base, dirs, files in os.walk(VMS):
        dirs[:] = [d for d in dirs if d not in {"obj", "bin"}]
        for name in sorted(files):
            if not name.endswith(".cs"):
                continue
            path = os.path.join(base, name)
            rel = os.path.relpath(path, ROOT).replace(os.sep, "/")
            with open(path, encoding="utf-8") as fh:
                # Blank // comment lines, keeping line numbers.
                text = "".join("\n" if ln.lstrip().startswith("//") else ln for ln in fh)
            rows = counted(text)
            if rows:
                found[rel] = rows
    return found


# The counting rules' controls, run before every scan: a rule that silently stopped counting is a gate that passes.
SELFTEST = [
    ('StatusText = "Done.";', 1),
    ('StatusText += " more";', 1),                                    # an appended clause is text
    ('StatusText += " " + Say("str.X", n);', 0),                       # a separator is not
    ('StatusText = "\u2713";', 0),                                     # nor a glyph
    ('ScanStatus = $"{n} found";', 1),
    ('StatusText = Res.Get("str.X.Done");', 0),                        # a key is the compliant form
    ('StatusText = "";', 0),                                          # a clear
    ('StatusText = r == "ok" ? Res.Get("str.A") : Res.Get("str.B");', 0),   # a comparison operand
    ('StatusText = r != "ok" ? Res.Get("str.A") : "Failed";', 1),       # ...beside real text
    ('StatusText = kind switch { "alpha" => Res.Get("str.A"), _ => Res.Get("str.B") };', 0),   # a pattern
    ('StatusText = kind is "alpha" ? Res.Get("str.A") : Res.Get("str.B");', 0),
    ('if (StatusText == "x") return;', 0),                            # not an assignment
]


def selftest() -> list[str]:
    return [f"{src!r}: counted {len(counted(src))}, expected {want}"
            for src, want in SELFTEST if len(counted(src)) != want]


def main() -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(errors="backslashreplace")
    bad = selftest()
    if bad:
        print("CHECK FAILED: the counting rules' own controls failed -- the gate cannot be trusted:")
        for b in bad:
            print("   ", b)
        return 1
    found = scan()
    counts = {k: len(v) for k, v in sorted(found.items())}
    total = sum(counts.values())

    if "--list" in sys.argv:
        for rel, rows in sorted(found.items()):
            for r in rows:
                print(f"  {rel}:{r}")
        print(f"{total} status literal(s) in {len(counts)} view-model file(s)")
        return 0

    with open(BASELINE, encoding="utf-8") as fh:
        baseline: dict[str, int] = json.load(fh)

    grew, shrank = [], []
    for rel in sorted(set(counts) | set(baseline)):
        now, was = counts.get(rel, 0), baseline.get(rel, 0)
        if now > was:
            grew.append((rel, was, now))
        elif now < was:
            shrank.append((rel, was, now))

    # --update lowers the baseline after literals moved out. It REFUSES while any file grew: rewriting the whole
    # baseline from the tree used to accept a new literal added elsewhere in the same run. --rebaseline rewrites it
    # regardless, and is only for a change to the COUNTING RULES themselves (say so in the commit).
    if "--update" in sys.argv or "--rebaseline" in sys.argv:
        if grew and "--rebaseline" not in sys.argv:
            print("REFUSED: --update only lowers the baseline, and these files GREW (a new literal belongs in en.axaml):")
            for rel, was, now in grew:
                print(f"    {rel}: {was} -> {now}")
            return 1
        with open(BASELINE, "w", encoding="utf-8", newline="\n") as fh:
            json.dump(counts, fh, indent=2, sort_keys=True)
            fh.write("\n")
        print(f"baseline written: {total} literal(s) in {len(counts)} file(s)"
              + (" (--rebaseline: the counting rules changed)" if "--rebaseline" in sys.argv else ""))
        return 0

    rc = 0
    if grew:
        rc = 1
        print("FAIL: a view model gained a hard-coded status string.")
        print("New status text goes to en.axaml and is read with Res.Get / Res.Format")
        print("([VM-INLINE-STRINGS]: the existing literals are grandfathered, new ones are not).")
        for rel, was, now in grew:
            print(f"    {rel}: {was} -> {now}")
            for r in found.get(rel, []):
                print(f"        {r}")
    if shrank:
        rc = 1
        print("FAIL: a view model has FEWER status literals than the baseline -- good;")
        print("lower the baseline so the slack cannot be reused:")
        print("    py tools/check_vm_status_literals.py --update")
        for rel, was, now in shrank:
            print(f"    {rel}: {was} -> {now}")
    if rc == 0:
        print(f"CHECK OK: {total} grandfathered status literal(s) in {len(counts)} view model(s), "
              "none added.")
    return rc


if __name__ == "__main__":
    raise SystemExit(main())
