#!/usr/bin/env python3
"""Does a view model gain a NEW hard-coded status string? A ratchet, not a sweep.

    py tools/check_vm_status_literals.py            # compare with the baseline; exit 1 on any change
    py tools/check_vm_status_literals.py --update   # rewrite the baseline from the tree
    py tools/check_vm_status_literals.py --list     # print every counted literal, exit 0

CLAUDE.md puts every UI string in en.axaml, but the view models set their status line
with C# literals. The maintainer's call ([VM-INLINE-STRINGS], 2026-09-26): NEW
view-model status strings go to en.axaml (Res.Get / Res.Format); the EXISTING ones stay,
because moving them means a large round of re-testing for little gain. So this gate
freezes the per-file count instead of demanding zero:

  * a file's count going UP fails -- a new literal; give it an en.axaml key instead;
  * a file's count going DOWN also fails, and prints the --update command -- so the
    baseline ratchets down, and a later literal cannot hide in the slack an old one left.

Counted: a string literal (plain, $, @ or $@) assigned to StatusText. An empty "" is a
clear, not text, and is not counted; a line that is a // comment is skipped. Other
properties (ErrorMessage, computed tooltips) are out of scope, as the decision was about
status strings.
"""
from __future__ import annotations

import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
VMS = os.path.join(ROOT, "ui", "UE5DumpUI", "ViewModels")
BASELINE = os.path.join(ROOT, "tools", "vm_status_literals_baseline.json")

LITERAL = re.compile(r'\bStatusText\s*=\s*(?:\$@|@\$|\$|@)?"(?!")')


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
                for i, line in enumerate(fh, 1):
                    if line.lstrip().startswith("//"):
                        continue
                    for _ in LITERAL.finditer(line):
                        found.setdefault(rel, []).append(f"{i}: {line.strip()[:120]}")
    return found


def main() -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(errors="backslashreplace")
    found = scan()
    counts = {k: len(v) for k, v in sorted(found.items())}
    total = sum(counts.values())

    if "--list" in sys.argv:
        for rel, rows in sorted(found.items()):
            for r in rows:
                print(f"  {rel}:{r}")
        print(f"{total} StatusText literal(s) in {len(counts)} view-model file(s)")
        return 0

    if "--update" in sys.argv:
        with open(BASELINE, "w", encoding="utf-8", newline="\n") as fh:
            json.dump(counts, fh, indent=2, sort_keys=True)
            fh.write("\n")
        print(f"baseline written: {total} literal(s) in {len(counts)} file(s)")
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

    rc = 0
    if grew:
        rc = 1
        print("FAIL: a view model gained a hard-coded StatusText string.")
        print("New status text goes to en.axaml and is read with Res.Get / Res.Format")
        print("([VM-INLINE-STRINGS]: the existing literals are grandfathered, new ones are not).")
        for rel, was, now in grew:
            print(f"    {rel}: {was} -> {now}")
            for r in found.get(rel, []):
                print(f"        {r}")
    if shrank:
        rc = 1
        print("FAIL: a view model has FEWER StatusText literals than the baseline -- good;")
        print("lower the baseline so the slack cannot be reused:")
        print("    py tools/check_vm_status_literals.py --update")
        for rel, was, now in shrank:
            print(f"    {rel}: {was} -> {now}")
    if rc == 0:
        print(f"CHECK OK: {total} grandfathered StatusText literal(s) in {len(counts)} view model(s), "
              "none added.")
    return rc


if __name__ == "__main__":
    raise SystemExit(main())
