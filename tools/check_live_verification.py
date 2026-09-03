#!/usr/bin/env python3
"""Assert that every "In-game verification pending" caveat in docs/roadmap.md is actually
tracked in docs/verification-register.md.

WHY THIS EXISTS
  The repo had SIX spellings of "shipped but not proven on a real game" scattered across
  14 files, and the register (in todo.md until 2026-09-03, now its own file) was missing items roadmap.md had been
  carrying a caveat for since build 796. Nothing machine-checks docs/*.md, so a register
  is a live candidate to become the SEVENTH spelling rather than the first — a doc
  convention that nobody enforces survives about one commit.

  This does not try to police prose. It enforces one narrow, mechanical invariant:

    roadmap.md says "In-game verification pending"  ==>  it carries "(key: X)"
                                                    ==>  the verification register mentions X

  The caveat stays attached to the capability it qualifies (a reader of roadmap.md needs
  it there), and the STATUS lives in exactly one place.

Usage:
  py tools/check_live_verification.py            # repo root inferred from this file
  py tools/check_live_verification.py --root .   # explicit
Exit 0 = clean, 1 = a caveat is untracked / untagged / the register moved.
"""
import os
import re
import sys

CAVEAT = "In-game verification pending"
SECTION = "## Pending live-game verification"
KEY_RE = re.compile(r"\(key:\s*([A-Za-z0-9_.\-]+)\s*\)")


def read(path):
    with open(path, encoding="utf-8") as f:
        return f.read()


def register_body(todo_text):
    """The register section's text, or None if the heading is gone/renamed."""
    lines = todo_text.splitlines()
    start = next((i for i, l in enumerate(lines) if l.startswith(SECTION)), None)
    if start is None:
        return None
    end = next((i for i in range(start + 1, len(lines)) if lines[i].startswith("## ")), len(lines))
    return "\n".join(lines[start:end])


def main():
    root = "."
    if "--root" in sys.argv:
        root = sys.argv[sys.argv.index("--root") + 1]
    else:
        root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

    roadmap_path = os.path.join(root, "docs", "roadmap.md")
    todo_path = os.path.join(root, "docs", "verification-register.md")
    for p in (roadmap_path, todo_path):
        if not os.path.isfile(p):
            print("CHECK FAILED: missing %s" % p)
            return 1

    todo = read(todo_path)
    body = register_body(todo)
    if body is None:
        # A renamed heading must not silently disable the whole check — that is the
        # failure mode this script is guarding against in the first place.
        print("CHECK FAILED: docs/verification-register.md has no '%s' heading." % SECTION)
        print("  If you renamed the register, update SECTION in this script in the same commit.")
        return 1

    failures = []
    tracked = []
    for n, line in enumerate(read(roadmap_path).splitlines(), 1):
        if CAVEAT not in line:
            continue
        m = KEY_RE.search(line)
        if not m:
            failures.append(
                "roadmap.md:%d says '%s' but carries no '(key: X)' — add one and add a matching\n"
                "    entry to docs/verification-register.md, or drop the caveat if it has been verified." % (n, CAVEAT))
            continue
        key = m.group(1)
        if ("key: %s" % key) not in body and key not in body:
            failures.append(
                "roadmap.md:%d is tagged '(key: %s)' but todo.md's register never mentions '%s' —\n"
                "    the caveat is untracked, which is exactly how V1a and NumericAll went missing." % (n, key, key))
        else:
            tracked.append((key, n))

    if failures:
        print("CHECK FAILED — live-verification register is out of sync:\n")
        for f in failures:
            print("  * %s" % f)
        print("\nThe register is docs/todo.md '%s'." % SECTION)
        print("It is the SINGLE owner of 'shipped but unproven on a real game'; roadmap.md keeps the")
        print("caveat next to the capability, the register keeps the status and the acceptance test.")
        return 1

    print("CHECK OK: %d roadmap caveat(s) tracked in the register (%s)"
          % (len(tracked), ", ".join(k for k, _ in tracked) or "none"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
