#!/usr/bin/env python3
"""Gate: every relative link in a tracked Markdown file must resolve to a real path.

    py tools/check_md_links.py                      # gate mode: exit 1 on any unresolved link
    py tools/check_md_links.py --list               # every failure, grouped by cause
    py tools/check_md_links.py --list --include-archive
    py tools/check_md_links.py --fix                # DRY RUN: print what would change
    py tools/check_md_links.py --fix --apply        # actually rewrite

⭐ WHY A GATE AND NOT A ONE-OFF SWEEP. A broken relative link is invisible to the person who wrote
it: the author sees the target in the folder they are thinking about, and Markdown has no compiler.
They arrive in bulk whenever a file MOVES -- `docs/x.md` -> `docs/archive/x.md` leaves every `../foo`
in it one level too shallow -- and the move is exactly the moment nobody re-reads the body.

-----

⚠⚠ THE INSTRUMENT IS THE HARD PART. THREE VERSIONS OF IT WERE WRONG BEFORE THIS ONE (2026-09-06).

  * v1 reported **586** unresolved. It counted `Fern.cpp:5159` line references (a form CLAUDE.md
    mandates) and Lua patterns inside code fences as broken links.
  * v2 reported the right total but the wrong CAUSES: `classify` probed exactly ONE extra `../`, so
    32 links that are two levels too shallow were filed as "no such file". That produced a
    confident and wrong story about renamed modules -- `Methode.cpp`, `Heiter.cpp` and `Routine.h`
    all exist.
  * v3 shipped a `⚠ CASE` paragraph promising to catch `Readme.MD` -> `Readme.md`. It did not:
    `exists_cased` called `Path.resolve()` first, and **`resolve()` canonicalises case on Windows**,
    so the listdir comparison always compared a name against itself. `exists_cased(REPO/'README.MD')`
    returned True. The `case-mismatch-only` branch emitted zero rows for its whole life.

⛔ So: do not "simplify" any of the four exclusions below, and do not trust a count in a comment.
Re-derive with `--list`.

WHAT IS EXCLUDED, AND WHY EACH ONE IS NECESSARY:

  1. **A trailing `:123` / `:123-456` line reference** is stripped before the existence check.
     CLAUDE.md requires this citation form. ⚠ The consequence is stated once, here: this gate checks
     that the FILE exists and says nothing about whether the LINE is still the right one. It cannot
     -- and line drift is real: 16 of 28 such references in `docs/verification-register.md` were
     stale when this was written. Verified negative: all such targets end in a real source
     extension, none resolves to a directory, and no drive-letter target exists in the tree.
  2. **Fenced code blocks** (``` and ~~~), tracking the fence CHARACTER and LENGTH so a ``` inside a
     ~~~~ block does not close it -- and allowing a **blockquote prefix**, because `> ```lua` opens
     a fence just as well. That last clause is not hypothetical: without it the Lua pattern
     `^0[xX](%x+)$` inside a quoted fence parses as a link.
  3. **Inline code spans**, per line.
  4. **`docs/archive/**`**, by convention rather than by defect -- see the ARCHIVE section below.

KNOWN COVERAGE GAPS (documented rather than fixed, each for a reason):
  * Link text spanning a newline. Relaxing the regex to allow it lets link text swallow whole
    paragraphs; the one real instance is archived and excluded anyway.
  * Reference-style links (`[text][ref]` + a `[ref]: target` line). None in this tree today.
  * A bracket-pair inside prose that is not backticked reads as a link and cannot be excluded by
    any amount of code-stripping -- e.g. a C++ lambda written with typographic quotes.

⚠ CASE IS CHECKED, and it matters: `Path.exists()` is case-INSENSITIVE on Windows and
case-SENSITIVE on GitHub, so `Readme.MD` renders locally and 404s on the web. This walks the real
directory listing WITHOUT `resolve()`. (Every `.MD` in this repo was renamed to `.md` on
2026-09-06; there are zero case-mismatched links today, which is the point of holding it there.)

-----

ARCHIVE. `docs/archive/**` is excluded from the verdict, and its unresolved count is printed as an
informational line instead. This is not laziness: `docs/archive/README.md` documents the convention
("nothing was edited, only moved") and repairing the links would destroy the byte-identity that
makes an archived doc citable.

⚠ But the invariant is "unedited since the move", NOT "lives in docs/archive/", and exactly three
files there do not have it -- so they stay INSIDE the gate:
  * `README.md`                          -- authored in place (git `A`, no rename source)
  * `godmode-implementation-plan.md`     -- git R099; its whole archive diff is two link fixes
  * `audit-2026-08-16-med-rederivation.md` -- git R098; documented at `archive/README.md`
"""
import argparse
import collections
import os
import pathlib
import re
import subprocess
import sys
import urllib.parse

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
REPO = pathlib.Path(__file__).resolve().parent.parent

ARCHIVE = "docs/archive/"
# Files under docs/archive/ that were edited or authored there, so the byte-identity argument does
# not cover them and the gate must keep holding them at zero.
ARCHIVE_INCLUDED = {
    "docs/archive/README.md",
    "docs/archive/godmode-implementation-plan.md",
    "docs/archive/audit-2026-08-16-med-rederivation.md",
}

LINK = re.compile(r"\[(?P<text>[^\]\n]*)\]\(\s*(?P<target>[^)\s]+)(?:\s+\"[^\"]*\")?\s*\)")
# `> ```lua` opens a fence. Up to 3 spaces of indent per CommonMark, any depth of blockquote.
FENCE = re.compile(r"^(?:\s{0,3}(?:>\s{0,3})*)(`{3,}|~{3,})")
INLINE_CODE = re.compile(r"`[^`\n]*`")
SKIP_SCHEME = re.compile(r"^(?:https?|mailto|ftp|file|data|tel|#)", re.I)
LINE_SUFFIX = re.compile(r":\d+(?:-\d+)?$")

_listdir_cache = {}


def strip_code(text):
    """Blank out fenced blocks and inline spans, preserving line count and byte offsets."""
    out = []
    fence_char, fence_len = None, 0
    for line in text.split("\n"):
        m = FENCE.match(line)
        if fence_char is None:
            if m:
                fence_char, fence_len = m.group(1)[0], len(m.group(1))
                out.append(" " * len(line))
                continue
            out.append(INLINE_CODE.sub(lambda mm: " " * len(mm.group(0)), line))
        else:
            if m and m.group(1)[0] == fence_char and len(m.group(1)) >= fence_len:
                fence_char, fence_len = None, 0
            out.append(" " * len(line))
    return "\n".join(out)


def tracked_md():
    out = subprocess.run(["git", "-C", str(REPO), "ls-files", "*.md", "*.MD"],
                         capture_output=True, text=True, check=True).stdout
    return sorted(REPO / line for line in out.splitlines() if line.strip())


def exists_cased(path):
    """Case-SENSITIVE existence, and FAIL CLOSED outside the repo.

    ⛔ Never call `.resolve()` here -- on Windows it canonicalises case, which silently turns this
    whole function into `exists()`. `..` is collapsed lexically with normpath instead, which touches
    no filesystem. Failing closed outside the repo also keeps the verdict machine-independent: an
    escaping `../../x` otherwise resolves against whatever happens to sit above the checkout.
    """
    norm = pathlib.Path(os.path.normpath(str(path)))
    try:
        rel = norm.relative_to(REPO)
    except ValueError:
        return False
    cur = REPO
    for part in rel.parts:
        key = str(cur)
        names = _listdir_cache.get(key)
        if names is None:
            try:
                names = set(os.listdir(cur))
            except (NotADirectoryError, FileNotFoundError, PermissionError, OSError):
                names = set()
            _listdir_cache[key] = names
        if part not in names:
            return False
        cur = cur / part
    return True


def candidate_target(raw):
    """The filesystem path a link target refers to, or None if it is not a file link at all."""
    t = raw.strip()
    if t.startswith("<") and t.endswith(">"):
        t = t[1:-1]
    if not t or SKIP_SCHEME.match(t):
        return None
    t = t.split("#", 1)[0]
    if not t:
        return None
    t = urllib.parse.unquote(t)
    t = LINE_SUFFIX.sub("", t)
    return t or None


def classify(src, target):
    """Name the CAUSE. Probes every depth, because 'too shallow' is not always by exactly one."""
    depth = len(src.parent.relative_to(REPO).parts) + 1
    for k in range(1, depth + 1):
        cand = src.parent.joinpath(*([".."] * k), target)
        if exists_cased(cand):
            return ("needs-%d-more-dotdot" % k), cand
    return "no-such-file", None


def rel(p):
    return p.relative_to(REPO).as_posix()


def in_archive(src):
    r = rel(src)
    return r.startswith(ARCHIVE) and r not in ARCHIVE_INCLUDED


def scan():
    """Return (files, links_checked, rows). Rows carry src, line, raw, target, cause, fixed."""
    rows = []
    files = tracked_md()
    total = 0
    for src in files:
        try:
            raw_text = src.read_text(encoding="utf-8")
        except (UnicodeDecodeError, OSError):
            continue
        stripped = strip_code(raw_text)
        for m in LINK.finditer(stripped):
            target = candidate_target(m.group("target"))
            if target is None:
                continue
            total += 1
            if exists_cased(src.parent / target):
                continue
            cause, fixed = classify(src, target)
            rows.append({"src": src,
                         "line": stripped.count("\n", 0, m.start()) + 1,
                         "span": m.span("target"),
                         "raw": m.group("target").strip(),
                         "target": target, "cause": cause, "fixed": fixed})
    return files, total, rows


def do_fix(rows, apply_changes):
    """Rewrite by MATCH OFFSET, back-to-front, never by textual replace.

    A `text.replace("](old)", ...)` would also hit occurrences inside fenced blocks, which detection
    deliberately ignored -- so the fixer would edit things the scanner never looked at.
    """
    fixable = [r for r in rows if r["fixed"] is not None and not in_archive(r["src"])]
    by_file = collections.defaultdict(list)
    for r in fixable:
        by_file[r["src"]].append(r)

    changed = 0
    for src, items in sorted(by_file.items()):
        with open(src, encoding="utf-8", newline="") as fh:   # newline="" -> endings round-trip
            text = fh.read()
        edits = []
        for r in sorted(items, key=lambda x: -x["span"][0]):
            s, e = r["span"]
            if text[s:e] != r["raw"]:
                print("  !! offset mismatch %s:%d -- skipped" % (rel(src), r["line"]))
                continue
            newpath = os.path.relpath(r["fixed"], src.parent).replace(os.sep, "/")
            newraw = r["raw"].replace(r["target"], newpath, 1)
            edits.append((s, e, r["raw"], newraw, r["line"]))
        for s, e, old, new, line in edits:
            print("  %s:%d  %s  ->  %s" % (rel(src), line, old, new))
            text = text[:s] + new + text[e:]
        if edits and apply_changes:
            with open(src, "w", encoding="utf-8", newline="") as fh:
                fh.write(text)
        changed += len(edits)

    skipped = len([r for r in rows if r["fixed"] is not None]) - len(fixable)
    print("\n%s %d link(s) across %d file(s); %d archived link(s) left alone by convention"
          % ("rewrote" if apply_changes else "WOULD rewrite (dry run; pass --apply)",
             changed, len(by_file), skipped))
    return 0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--list", action="store_true", help="print every failure, grouped by cause")
    ap.add_argument("--include-archive", action="store_true",
                    help="also report docs/archive/, which is excluded by convention")
    ap.add_argument("--fix", action="store_true", help="rewrite provable failures (dry run)")
    ap.add_argument("--apply", action="store_true", help="with --fix: actually write")
    a = ap.parse_args()

    files, total, rows = scan()
    if a.fix:
        return do_fix(rows, a.apply)

    live = [r for r in rows if not in_archive(r["src"])]
    archived = [r for r in rows if in_archive(r["src"])]
    shown = rows if a.include_archive else live

    if a.list:
        by_cause = collections.defaultdict(list)
        for r in shown:
            by_cause[r["cause"]].append(r)
        for cause in sorted(by_cause, key=lambda c: -len(by_cause[c])):
            print("\n=== %s  (%d) ===" % (cause, len(by_cause[cause])))
            for r in by_cause[cause]:
                print("  %s:%d: %s" % (rel(r["src"]), r["line"], r["raw"]))
        print()

    if live:
        print("CHECK FAILED: %d of %d relative link(s) do not resolve" % (len(live), total))
        for c, n in collections.Counter(r["cause"] for r in live).most_common():
            print("    %-28s %d" % (c, n))
        if not a.list:
            print("  --list to see them, --fix to preview the provable rewrites")
        return 1

    print("check_md_links: OK -- %d relative link(s) across %d tracked .md file(s) all resolve"
          % (total, len(files)))
    if archived:
        print("  (%d unresolved link(s) under docs/archive/ are excluded by convention --"
              " see docs/archive/README.md; --list --include-archive to see them)" % len(archived))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
