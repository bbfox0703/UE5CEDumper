#!/usr/bin/env python3
"""Did an edit touch only comments? Compares each file's code -- comments removed -- with a git revision.

    py tools/verify/comment_only_diff.py                   # every modified .h / .cpp / .cs under dll/src and ui/UE5DumpUI
    py tools/verify/comment_only_diff.py FILE [FILE ...]   # just these
    py tools/verify/comment_only_diff.py --base REF ...    # against REF instead of HEAD

For a comment clean-up ([COMMENT-NO-HISTORY-2026-11-18]): the code of a file -- every line with its // tail and /* */
parts removed, blank lines dropped, whitespace runs collapsed -- must be identical before and after. A changed string
literal, identifier, token or preprocessor line is reported with the first line where the code differs, and the exit
code is 1. Exit 0 only when every file compared is comment-only.
"""
from __future__ import annotations

import argparse
import os
import re
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SCOPES = ("dll/src/", "ui/UE5DumpUI/")
EXTS = (".h", ".cpp", ".cs")


def code_lines(text: str) -> list[str]:
    """The code of a C++ / C# source, comments removed: one entry per non-blank line, whitespace runs collapsed.
    A scanner, not a parser: a quote opens a string, a backslash escapes inside one, a ' opens a char literal."""
    out: list[str] = []
    in_block = False
    for line in text.splitlines():
        i, n, quote, code = 0, len(line), None, []
        if in_block:
            end = line.find("*/")
            if end < 0:
                continue
            in_block = False
            i = end + 2
        start = i
        while i < n:
            c = line[i]
            if quote:
                if c == "\\":
                    i += 2
                    continue
                if c == quote:
                    quote = None
            elif c in "\"'":
                quote = c
            elif line.startswith("//", i):
                break
            elif line.startswith("/*", i):
                code.append(line[start:i])
                end = line.find("*/", i + 2)
                if end < 0:
                    in_block = True
                    start = n
                    break
                i = end + 2
                start = i
                continue
            i += 1
        code.append(line[start:i] if start <= i else "")
        joined = re.sub(r"\s+", " ", "".join(code)).strip()
        if joined:
            out.append(joined)
    return out


def git(*args: str) -> str:
    return subprocess.run(["git", *args], cwd=ROOT, capture_output=True, text=True, encoding="utf-8",
                          errors="replace").stdout


def changed_files(base: str) -> list[str]:
    names = git("diff", "--name-only", base, "--").splitlines()
    return [n for n in names if n.startswith(SCOPES) and n.endswith(EXTS)]


def main() -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="backslashreplace")
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("files", nargs="*")
    ap.add_argument("--base", default="HEAD")
    a = ap.parse_args()
    files = [f.replace("\\", "/") for f in a.files] or changed_files(a.base)
    if not files:
        print("no changed source file to compare")
        return 0
    bad = 0
    for rel in files:
        path = os.path.join(ROOT, rel)
        old = subprocess.run(["git", "show", f"{a.base}:{rel}"], cwd=ROOT, capture_output=True).stdout
        if not os.path.exists(path):
            print(f"FAIL {rel}: deleted")
            bad += 1
            continue
        with open(path, "rb") as fh:
            new = fh.read()
        before = code_lines(old.decode("utf-8-sig", errors="replace"))
        after = code_lines(new.decode("utf-8-sig", errors="replace"))
        if before == after:
            print(f"ok   {rel}: comments only")
            continue
        bad += 1
        k = next((i for i, (x, y) in enumerate(zip(before, after)) if x != y), min(len(before), len(after)))
        print(f"FAIL {rel}: the code differs at code line {k + 1}")
        print(f"       before: {before[k] if k < len(before) else '(end of file)'}"[:240])
        print(f"       after:  {after[k] if k < len(after) else '(end of file)'}"[:240])
    print(f"{len(files) - bad} of {len(files)} file(s) comment-only")
    return 1 if bad else 0


if __name__ == "__main__":
    raise SystemExit(main())
