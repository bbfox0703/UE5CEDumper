#!/usr/bin/env python3
"""Gate: what a code COMMENT points at must still exist -- and a comment may not point by line number.

    py tools/check_comment_refs.py            # gate mode: exit 1 on any finding
    py tools/check_comment_refs.py --list     # every finding, grouped by check
    py tools/check_comment_refs.py --selftest # the negative controls only (every run does them first)

WHY ([COMMENT-INTEGRITY-2026-09-26], docs/comment-integrity-eval.md). Comments are not compiled, so nothing tells
the author when what they describe moves. Measured on build 3560: 97 of 141 in-repo `File.cpp:NNN` references
in comments pointed at a line that had moved since the comment was written, "no test target compiles X.cpp"
was false in at least 9 places, and 18 references named a .md file that does not exist. Four checks, over
the comment text of every tracked code file under dll/ ui/ scripts/ tools/ (// /* */ /// -- #; NOT Python
docstrings, which are prose the scripts print):

  LINE        an in-repo `File.ext:NNN` (or a bare `:NNN` that continues one, or stands alone = same file).
              Cite a function, a constant, a table row or a [TAG] instead: they move with the code. A line in
              ANOTHER codebase (UE engine source, CE's Pascal, a vendored library) stays allowed -- it is fixed
              for the version named beside it.
  TESTTARGET  "no test target compiles X.cpp" (and its wordings) where X.cpp IS #included by a dll/tests
              target -- the same derivation check_derived_counts uses for CLAUDE.md's count.
  REFS        a `name.md` no tracked file carries; `name.md §N` / `working-lessons §N` naming a heading that
              does not exist; a finding `[TAG-WITH-HYPHENS]` that no doc under docs/ (archive included) records.
              A line that names another repository (AOBMaker, cheat-engine, RE-UE4SS, Dumper-7, UEPseudo,
              patternsleuth, CrimsonAtomtic) or points under out/ is out of scope for the .md check.
  CSDOC       `/// </summary>` directly followed by `/// <summary>`: two doc blocks stacked on one member, so
              the first documents something else (a type was inserted between a comment and its target).
"""
from __future__ import annotations

import glob
import os
import re
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CODE_DIRS = ("dll/", "ui/", "scripts/", "tools/")
CODE_EXT = (".cpp", ".h", ".hpp", ".cs", ".lua", ".py", ".CT")
OTHER_REPOS = re.compile(r"(?i)\b(AOBMaker|cheat-engine|RE-UE4SS|Dumper-?7|UEPseudo|patternsleuth|CrimsonAtomtic|"
                         r"discrete|UnrealEngine|Epic)\b")
HISTORY = re.compile(r"(?i)\b(until|before|used to|believ\w*)\b")
# The evaluation doc names the broken tags as examples; it must not count as their record.
TAG_CORPUS_EXCLUDE = ("docs/comment-integrity-eval.md",)

FILELINE = re.compile(r"\b([A-Za-z_][\w.-]*\.(?:cpp|h|hpp|cs|axaml|lua|py|CT|ps1|md))\s?:(\d{1,5})(?:\s*[-–]\s*\d{1,5})?\b")
ANYFILE = re.compile(r"\b([A-Za-z_][\w.-]*\.(?:cpp|h|hpp|cs|axaml|lua|py|CT|ps1|md|pas|inc|java|ini|txt))\b")
BARE = re.compile(r"(?:(?<=\()|(?<=\s)|(?<=,)|(?<=\[))(:\d{2,5})(?:\s*[-–]\s*\d{2,5})?\b")
TESTCLAIM = re.compile(r"(?i)(?:no|not by any|compiled by no|reach(?:es)? no)\s+(?:C\+\+\s+)?test(?:\s+(?:target|executable|binary|TU))?"
                       r"[^.;]{0,40}?\b([A-Z][A-Za-z]+)\.cpp|\b([A-Z][A-Za-z]+)\.cpp\b[^.;]{0,40}?"
                       r"(?:no test target|no test executable|compiled by no test|reaches no test|no test compiles)")
MDREF = re.compile(r"\b([\w.-]+\.md)\b(?:\s*§\s*([0-9]+(?:\.[0-9a-z]+)*))?")
WLREF = re.compile(r"working-lessons(?:\.md)?\s*§\s*([0-9]+(?:\.[0-9a-z]+)*)")
TAG = re.compile(r"\[([A-Z][A-Z0-9]*(?:-[A-Z0-9]+)+)\]")


def read(p):
    with open(p, "rb") as f:
        return f.read().decode("utf-8-sig", errors="replace").replace("\r\n", "\n")


def tracked():
    out = subprocess.run(["git", "ls-files"], cwd=ROOT, capture_output=True, text=True, encoding="utf-8").stdout
    return [f for f in out.split("\n") if f]


def comment_text(path, lines):
    """Yield (lineno, comment text) -- line comments and block comments, per language."""
    ext = os.path.splitext(path)[1].lower()
    inblock = False
    for n, l in enumerate(lines, 1):
        if ext in (".cpp", ".h", ".hpp", ".cs"):
            if inblock:
                e = l.find("*/")
                yield n, (l if e < 0 else l[:e])
                inblock = e < 0
                continue
            i, j = l.find("//"), l.find("/*")
            if i >= 0 and (j < 0 or i < j) and l[:i].count('"') % 2 == 0:
                yield n, l[i + 2:]
            elif j >= 0 and l[:j].count('"') % 2 == 0:
                e = l.find("*/", j + 2)
                yield n, (l[j + 2:] if e < 0 else l[j + 2:e])
                inblock = e < 0
        elif ext in (".lua", ".ct"):
            i = l.find("--")
            if i >= 0 and l[:i].count("'") % 2 == 0 and l[:i].count('"') % 2 == 0:
                yield n, l[i + 2:]
        elif ext == ".py":
            i = l.find("#")
            if i >= 0 and l[:i].count("'") % 2 == 0 and l[:i].count('"') % 2 == 0 and not l[:i].rstrip().endswith(("[", "(")):
                yield n, l[i + 1:]


def test_compiled_cpps(root):
    """Stems of dll/src .cpp files some test target compiles: #included by a dll/tests .cpp (check_derived_counts'
    derivation) OR listed as a source of a *_test executable in dll/CMakeLists.txt (dll_helpers_test builds
    Radar.cpp and Denken.cpp that way, not by #include)."""
    stems = {m for f in glob.glob(os.path.join(root, "dll", "tests", "*.cpp"))
             for m in re.findall(r"\.\./src/([A-Za-z_]+)\.cpp", read(f))}
    cm = read(os.path.join(root, "dll", "CMakeLists.txt"))
    for body in re.findall(r"add_executable\(\s*\w+_test\b([^)]*)\)", cm):
        stems |= set(re.findall(r"src/([A-Za-z_]+)\.cpp", body))
    return stems


def headings(text):
    return set(re.findall(r"^#{1,6}\s+§?\s*([0-9]+(?:\.[0-9a-z]+)*)[.)\s]", text, re.M))


def scan_line(path, n, c, ctx):
    """Every finding on one comment line: (check, path, n, detail)."""
    out = []
    by_base, in_tests, md_by_base, doc_text, heads = ctx
    # --- LINE: in-repo file:line, plus bare :NNN that continues an in-repo ref or stands alone (same file)
    last_is_repo = None
    spans = []
    for m in FILELINE.finditer(c):
        base = m.group(1).lower()
        spans.append((m.start(), m.end(), base in by_base))
        if base in by_base and not base.endswith(".md"):
            out.append(("LINE", path, n, m.group(0)))
    for m in BARE.finditer(c):
        if any(s <= m.start() < e for s, e, _ in spans):
            continue
        prev = [(s, e, inrepo) for s, e, inrepo in spans if e <= m.start()]
        prior_file = [f for f in ANYFILE.finditer(c[:m.start()])]
        if prev:
            inrepo = prev[-1][2]
        elif prior_file:
            inrepo = prior_file[-1].group(1).lower() in by_base
        else:
            inrepo = True          # a bare :NNN with no file before it = this file
        if inrepo:
            out.append(("LINE", path, n, m.group(1)))
    # --- TESTTARGET (history -- "until now", "used to", a claim quoted as someone's belief -- is not a claim)
    history = bool(HISTORY.search(c))
    for m in TESTCLAIM.finditer(c):
        stem = m.group(1) or m.group(2)
        quoted = c[:m.start()].count('"') % 2 == 1
        if stem in in_tests and not history and not quoted:
            out.append(("TESTTARGET", path, n, f"{stem}.cpp IS compiled by a test target"))
    # --- REFS
    other_repo = (bool(OTHER_REPOS.search(c)) or "out/" in c or "out\\" in c
                  or "not in this repo" in c)
    for m in MDREF.finditer(c):
        name, sec = m.group(1), m.group(2)
        key = name.lower()
        if key not in md_by_base:
            if not other_repo:
                out.append(("REFS", path, n, f"{name}: no tracked file of that name"))
        elif sec and sec not in heads.get(md_by_base[key], set()):
            out.append(("REFS", path, n, f"{name} §{sec}: no such heading"))
    for m in WLREF.finditer(c):
        if m.group(1) not in heads.get("docs/working-lessons.md", set()):
            out.append(("REFS", path, n, f"working-lessons §{m.group(1)}: no such heading"))
    for m in TAG.finditer(c):
        t = m.group(1)
        if len(t) < 6 or not re.search(r"[A-Z]{2}", t):
            continue
        if t not in doc_text:
            out.append(("REFS", path, n, f"[{t}]: no doc under docs/ records it"))
    return out


def run(root):
    files = tracked()
    by_base = {}
    for f in files:
        by_base.setdefault(os.path.basename(f).lower(), []).append(f)
    md_by_base = {os.path.basename(f).lower(): f for f in files if f.endswith(".md")}
    doc_text = "\n".join(read(os.path.join(root, f)) for f in files
                         if f.startswith("docs/") and f.endswith(".md") and f not in TAG_CORPUS_EXCLUDE)
    heads = {f: headings(read(os.path.join(root, f))) for f in files if f.endswith(".md")}
    ctx = (by_base, test_compiled_cpps(root), md_by_base, doc_text, heads)
    findings = []
    for f in files:
        if not (f.startswith(CODE_DIRS) and f.endswith(CODE_EXT)) or f == "tools/check_comment_refs.py":
            continue
        lines = read(os.path.join(root, f)).split("\n")
        for n, c in comment_text(f, lines):
            findings += scan_line(f, n, c, ctx)
        if f.endswith(".cs"):
            prev = None
            for n, l in enumerate(lines, 1):
                s = l.strip()
                if not s:
                    continue
                if prev == "/// </summary>" and s == "/// <summary>":
                    findings.append(("CSDOC", f, n, "a second <summary> block right after one: the first documents another member"))
                prev = s
    return findings


def selftest():
    ctx = ({"genau.cpp": ["dll/src/Genau.cpp"], "working-lessons.md": ["docs/working-lessons.md"]},
           {"Aura"}, {"working-lessons.md": "docs/working-lessons.md"}, "[REAL-TAG-2026-01-01]",
           {"docs/working-lessons.md": {"3", "3.wc"}})
    cases = [
        ("Genau.cpp:788 is where it happens", "LINE", True),
        ("see the jump at :528 below", "LINE", True),
        ("CEFuncProc.pas:1346 / :1360 in CE 7.5", "LINE", False),
        ("UnrealNames.cpp@5.4 line 2017", "LINE", False),
        ("no test target compiles Aura.cpp, so", "TESTTARGET", True),
        ("no test target compiles Stark.cpp, so", "TESTTARGET", False),
        ("Until now no test target compiled Aura.cpp", "TESTTARGET", False),
        ('believing "Aura.cpp is in no test target" -- wrong', "TESTTARGET", False),
        ("discrete's docs: Scan-Internals.md §16", "REFS", False),
        ("report.md, not in this repo", "REFS", False),
        ("see aot-pitfalls.md for the list", "REFS", True),
        ("see working-lessons §3.wc", "REFS", False),
        ("see working-lessons §9.zz", "REFS", True),
        ("closed by [REAL-TAG-2026-01-01]", "REFS", False),
        ("closed by [GHOST-TAG-2026-01-01]", "REFS", True),
        ("AOBMaker's API-CEPlugin.md says so", "REFS", False),
    ]
    bad = 0
    for text, check, want in cases:
        got = any(k == check for k, *_ in scan_line("dll/src/X.cpp", 1, text, ctx))
        if got != want:
            bad += 1
            print(f"SELFTEST FAILED: {check} on {text!r}: expected {want}, got {got}")
    return bad


def main():
    if selftest():
        print("check_comment_refs: its own negative controls fail -- the verdict below would lie.")
        return 2
    if "--selftest" in sys.argv:
        print("selftest OK")
        return 0
    findings = run(ROOT)
    by = {}
    for k, f, n, d in findings:
        by.setdefault(k, []).append((f, n, d))
    if "--list" in sys.argv or findings:
        for k in ("LINE", "TESTTARGET", "REFS", "CSDOC"):
            v = by.get(k, [])
            print(f"== {k}: {len(v)}")
            for f, n, d in (v if "--list" in sys.argv else v[:15]):
                print(f"   {f}:{n}  {d}")
    if findings:
        print(f"CHECK FAILED: {len(findings)} comment reference(s) point at nothing, or by line number. "
              "See docs/comment-integrity-eval.md; cite a function / constant / [TAG] instead of a line.")
        return 1
    print("CHECK OK: no in-repo line numbers, no false test-target claims, every .md / § / [TAG] resolves")
    return 0


if __name__ == "__main__":
    sys.exit(main())
