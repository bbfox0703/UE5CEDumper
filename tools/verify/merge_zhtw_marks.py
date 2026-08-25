r"""Recover ✅ / ❌ marks made on a COPY of the 繁中 checklist that never went through git.

    py tools/verify/merge_zhtw_marks.py <annotated-copy>        # report only
    py tools/verify/merge_zhtw_marks.py <annotated-copy> --diff # show the surrounding rows

WHY THIS EXISTS. On 2026-08-21 the maintainer worked through the checklist on `Y:\` and marked ten
rows — seven ✅, two ❌ and one free-text note. `Y:\` is not the repo. The `docs/` copy was edited
from the git version a few hours later, which had never seen those marks, so **six of the seven
passes and the note were silently lost**; only the two ❌ survived, because they had been turned
into defect entries (`GRIDRECYCLE`, `LWREFRESH`) and lived on elsewhere.

Nothing was overwritten and nothing was reverted. The two files were simply never connected. That
is worth stating plainly, because "my results got written back" sounds like a git accident and
looking for one wastes the time that should go into recovering the marks.

⭐ THE ASYMMETRY IS THE LESSON. A ❌ survives being lost, because it becomes a bug with a life of
its own. A ✅ evaporates without trace — the row simply stays open and someone re-runs it. So the
marks most worth rescuing are exactly the ones nobody notices are missing.

WHAT THIS DOES. Matches rows by their text (ignoring any leading mark), and reports every row whose
mark differs between the copy and the repo. It does NOT write: a ✅ recovered this way is a
maintainer's claim with no evidence attached, and the register's own rule is that a result without
its conditions is not a measurement. Record it as reported-by-the-maintainer, or re-run it — but
decide that per row, deliberately.
"""
import pathlib
import re
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

REPO = pathlib.Path("docs/pending-verification_zh-TW.md")
MARKS = "✅❌🟡⚠⛔"


def rows(text):
    """{normalised row text: (mark, raw line, line number)} for every table row."""
    out = {}
    for i, line in enumerate(text.replace("\r\n", "\n").split("\n"), 1):
        s = line.strip()
        if not s:
            continue
        m = re.match(r"^([%s]*)\s*(\|.*)$" % MARKS, s)
        if not m:
            continue
        mark, body = m.group(1), m.group(2)
        # the row's own text is the key; a leading mark must not change identity
        key = re.sub(r"\s+", " ", body)
        out[key] = (mark, s, i)
    return out


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    src = pathlib.Path(sys.argv[1])
    want_diff = "--diff" in sys.argv
    if not src.exists():
        print("no such file: %s" % src)
        return 2
    if not REPO.exists():
        print("not in the repo root — %s not found" % REPO)
        return 2

    a = rows(src.read_text(encoding="utf-8", errors="replace"))
    b = rows(REPO.read_text(encoding="utf-8", errors="replace"))

    gained, lost, gone = [], [], []
    for key, (mark, raw, ln) in a.items():
        if not mark:
            continue
        if key not in b:
            gone.append((mark, raw, ln))
        elif not b[key][0]:
            lost.append((mark, raw, ln, b[key][2]))
    for key, (mark, raw, ln) in b.items():
        if mark and key in a and not a[key][0]:
            gained.append((mark, raw, ln))

    print("annotated copy : %s  (%d marked row(s))" % (src, sum(1 for m, _, _ in a.values() if m)))
    print("repo           : %s  (%d marked row(s))" % (REPO, sum(1 for m, _, _ in b.values() if m)))
    print()

    if lost:
        print("⚠ MARKED IN THE COPY, UNMARKED IN THE REPO — these are the ones that were lost:")
        for mark, raw, ln, rln in lost:
            print("   %s  copy:%-4d repo:%-4d  %s" % (mark, ln, rln, raw[:120]))
        print()
    if gone:
        print("ℹ MARKED IN THE COPY, ROW NO LONGER IN THE REPO (closed and deleted since — fine):")
        for mark, raw, ln in gone:
            print("   %s  copy:%-4d  %s" % (mark, ln, raw[:120]))
        print()
    if gained:
        print("ℹ MARKED IN THE REPO ONLY (verified after the copy was taken):")
        for mark, raw, ln in gained:
            print("   %s  repo:%-4d  %s" % (mark, ln, raw[:120]))
        print()

    # free-text notes the copy carries and the repo does not
    at = set(l.strip() for l in src.read_text(encoding="utf-8", errors="replace").split("\n"))
    bt = set(l.strip() for l in REPO.read_text(encoding="utf-8", errors="replace").split("\n"))
    notes = [l for l in at - bt if l.startswith(">") and "|" not in l and len(l) < 200]
    if notes:
        print("⚠ FREE-TEXT NOTES only in the copy — these vanish even more quietly than a mark:")
        for n in notes:
            print("   %s" % n[:150])
        print()

    if not (lost or gone or notes):
        print("nothing to recover — every mark in the copy is reflected in the repo.")
        return 0
    print("This tool does NOT write. A ✅ recovered here is a claim with no evidence attached;")
    print("record it as reported-by-the-maintainer, or re-run it — per row, deliberately.")
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
