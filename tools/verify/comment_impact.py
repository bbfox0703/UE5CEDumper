#!/usr/bin/env python3
"""Change-time aid: which comments ELSEWHERE name a symbol this diff touches?

    py tools/verify/comment_impact.py                 # working tree (staged + unstaged) vs HEAD
    py tools/verify/comment_impact.py --staged        # the index only -- run it before `git commit`
    py tools/verify/comment_impact.py --rev HEAD      # one commit (rev^..rev)
    py tools/verify/comment_impact.py --range A..B    # a span, e.g. a PR: origin/main..dev
    py tools/verify/comment_impact.py --docs          # also search docs/*.md (specs enumerate too)
    py tools/verify/comment_impact.py --max-hits 25   # a symbol named by more comments than this is "broad"
    py tools/verify/comment_impact.py --selftest

WHY ([COMMENT-INTEGRITY-2026-09-26], docs/comment-integrity-eval.md). The sampled stale-comment rate on build 3560
was 26%, and the commonest cause was ADDITIVE DRIFT: a commit adds a caller / field / pipe key / enum value, and the
comment that LISTS them -- in another file, written months earlier -- is never opened. No gate can know what a
comment should say; what a tool can do is put that comment in front of the author at the moment it goes stale.

Not a gate (exit 0 whatever it finds). It collects the distinctive identifiers on the diff's changed CODE lines (a new
call site counts: that is exactly the change that makes a caller list stale) plus each hunk's enclosing function,
then lists every comment line outside the diff that names one. A hit that also enumerates or claims uniqueness
("only", "both", "the one", a number word, a slash/comma list) is starred -- read those first. Symbols named by more
than --max-hits comments are reported as broad and not listed: at that spread the name is vocabulary, not a
reference.
"""
from __future__ import annotations

import os
import re
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import check_comment_refs as G  # noqa: E402  (tools/ -- reuses its comment extraction and file list)

ROOT = G.ROOT
CODE_EXT = (".cpp", ".h", ".hpp", ".cs", ".lua", ".py", ".ct")

# Words that pass the shape test but name nothing specific to this repo.
STOP = set("""
std string wstring vector size_t uint8_t uint16_t uint32_t uint64_t int8_t int16_t int32_t int64_t uintptr_t
nullptr return static const constexpr inline struct class public private protected override virtual
async await Task ValueTask bool void true false null this base using namespace typename template
LOG_INFO LOG_WARN LOG_ERROR LOG_DEBUG LOG_CAT TEXT DWORD HANDLE HMODULE BYTE WORD LPVOID
ToString Length Count Add Remove Contains TryGetValue GetValue SetValue Equals GetHashCode Dispose DisposeAsync
IsNullOrEmpty IsNullOrWhiteSpace StringComparison OrdinalIgnoreCase Ordinal
self None True False print format append
""".split())

IDENT = re.compile(r"\b[A-Za-z_][A-Za-z0-9_]{3,}\b")
QUOTED = re.compile(r"\"([a-z][a-z0-9]*(?:_[a-z0-9]+)+|[a-z]+[A-Z][A-Za-z0-9]+)\"")  # "pipe_key" / "jsonKey"
HUNK = re.compile(r"^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@ ?(.*)$")
CTX_FN = re.compile(r"([A-Za-z_][\w:]*)\s*\(")
RISKY = re.compile(r"(?i)\b(only|both|the one|the sole|sole|single|exactly|every|each of|three|four|five|six|seven|"
                   r"eight|nine|ten|\d+\s+(?:callers?|fields?|keys?|entries|values?|sites?|modules?|commands?|states?))\b")
LIST_SHAPE = re.compile(r"\b\w+\s*[/,]\s*\w+\s*[/,]\s*\w+")


def distinctive(tok: str) -> bool:
    """CamelCase with 2+ humps, snake/SCREAMING with an underscore, or Ns::Name tails -- not plain English."""
    if tok in STOP or tok.isdigit():
        return False
    if "_" in tok.strip("_") and re.search(r"[A-Za-z]{2}", tok):
        return True
    humps = len(re.findall(r"[A-Z][a-z0-9]+", tok))
    return humps >= 2 or (humps == 1 and tok[0].islower() and any(c.isupper() for c in tok))


def code_part(line: str, ext: str) -> str:
    """The line minus its comment -- a symbol mentioned only in a changed COMMENT is not a changed symbol."""
    marks = {".cpp": "//", ".h": "//", ".hpp": "//", ".cs": "//", ".lua": "--", ".ct": "--", ".py": "#"}
    m = marks.get(ext)
    if m:
        i = line.find(m)
        if i >= 0 and line[:i].count('"') % 2 == 0:
            line = line[:i]
    s = line.strip()
    if s.startswith(("*", "/*")):
        return ""
    return line


def symbols_of(line: str, ext: str) -> set[str]:
    code = code_part(line, ext)
    out = {t for t in IDENT.findall(code) if distinctive(t)}
    out |= set(QUOTED.findall(code))
    return out


def parse_diff(text: str):
    """-> {path: {"symbols": set, "ranges": [(first,last) new-side lines]}} for code files only."""
    res, cur, ext = {}, None, ""
    for l in text.split("\n"):
        if l.startswith("+++ "):
            p = l[4:].strip()
            p = p[2:] if p.startswith("b/") else p
            ext = os.path.splitext(p)[1].lower()
            cur = res.setdefault(p, {"symbols": set(), "ranges": []}) if (p != "/dev/null" and ext in CODE_EXT) else None
            continue
        if cur is None or l.startswith("--- "):
            continue
        m = HUNK.match(l)
        if m:
            a, n = int(m.group(1)), int(m.group(2) or 1)
            cur["ranges"].append((a, a + max(n, 1) - 1))
            f = CTX_FN.search(m.group(3) or "")
            if f:
                name = f.group(1).split("::")[-1]
                if distinctive(name):
                    cur["symbols"].add(name)
            continue
        if l[:1] in "+-" and not l.startswith(("+++", "---")):
            cur["symbols"] |= symbols_of(l[1:], ext)
    return res


def git_diff(args: list[str]) -> str:
    base = ["git", "diff", "-U0", "--no-color", "--no-ext-diff"]
    if "--staged" in args:
        cmd = base + ["--cached"]
    elif "--rev" in args:
        r = args[args.index("--rev") + 1]
        cmd = base + [f"{r}^", r]
    elif "--range" in args:
        cmd = base + [args[args.index("--range") + 1]]
    else:
        cmd = base + ["HEAD"]
    return subprocess.run(cmd, cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace").stdout


def comment_index(with_docs: bool):
    """-> list of (path, lineno, comment text) over every tracked code file (+ docs/*.md lines)."""
    out = []
    for p in G.tracked():
        ext = os.path.splitext(p)[1].lower()
        if p.startswith(G.CODE_DIRS) and ext in CODE_EXT:
            try:
                lines = G.read(os.path.join(ROOT, p)).split("\n")
            except OSError:
                continue
            out.extend((p, n, c) for n, c in G.comment_text(p, lines))
        elif with_docs and p.startswith("docs/") and not p.startswith("docs/archive/") and ext == ".md":
            try:
                lines = G.read(os.path.join(ROOT, p)).split("\n")
            except OSError:
                continue
            out.extend((p, n, l) for n, l in enumerate(lines, 1))
    return out


def find_hits(changed, index, max_hits):
    syms = set().union(*(v["symbols"] for v in changed.values())) if changed else set()
    inside = {p: v["ranges"] for p, v in changed.items()}
    pats = {s: re.compile(r"(?<![\w])" + re.escape(s) + r"(?![\w])") for s in syms}
    hits = {s: [] for s in syms}
    for p, n, c in index:
        if any(a <= n <= b for a, b in inside.get(p, ())):
            continue
        for s, rx in pats.items():
            if rx.search(c):
                hits[s].append((p, n, c.strip()))
    listed = {s: h for s, h in hits.items() if 0 < len(h) <= max_hits}
    broad = {s: len(h) for s, h in hits.items() if len(h) > max_hits}
    return listed, broad


def risky(c: str) -> bool:
    return bool(RISKY.search(c) or LIST_SHAPE.search(c))


def selftest() -> int:
    diff = "\n".join([
        "diff --git a/dll/src/Laufen.cpp b/dll/src/Laufen.cpp",
        "--- a/dll/src/Laufen.cpp",
        "+++ b/dll/src/Laufen.cpp",
        "@@ -10,0 +11,2 @@ bool ApplyKnob(int k)",
        "+    auto hits = Aura::FindObjectsByName(name, excludeSet, false);  // FindObjectsByName comment-only",
        "+    // SomeCommentOnlySymbol must not count",
        "diff --git a/docs/x.md b/docs/x.md",
        "+++ b/docs/x.md",
        "@@ -1 +1 @@",
        "+ FindObjectsByName in a doc is not code",
    ])
    ch = parse_diff(diff)
    bad = []
    s = ch.get("dll/src/Laufen.cpp", {}).get("symbols", set())
    if "FindObjectsByName" not in s: bad.append("call site symbol not collected")
    if "ApplyKnob" not in s: bad.append("hunk-context function not collected")
    if "excludeSet" not in s: bad.append("camelCase local not collected")
    if "SomeCommentOnlySymbol" in s: bad.append("a comment-only symbol was collected")
    if "docs/x.md" in ch: bad.append("a non-code file was parsed")
    if distinctive("return") or distinctive("value") or not distinctive("bool_native"): bad.append("distinctive() shape")
    idx = [("dll/src/Aura.h", 202, " the cheap internal callers: Wirbel/Edel/Solitar/Mimic/Frieren use FindObjectsByName"),
           ("dll/src/Laufen.cpp", 11, " FindObjectsByName inside the hunk itself"),
           ("dll/src/Other.cpp", 5, " unrelated FindObjectsByNameEx")]
    listed, _ = find_hits(ch, idx, 25)
    got = [(p, n) for p, n, _ in listed.get("FindObjectsByName", [])]
    if got != [("dll/src/Aura.h", 202)]: bad.append(f"hits wrong: {got}")
    if not risky(idx[0][2]): bad.append("a slash list was not starred")
    if risky(" plain prose about the walker"): bad.append("plain prose starred")
    for b in bad:
        print("SELFTEST FAIL:", b)
    return 1 if bad else 0


def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    args = sys.argv[1:]
    if selftest():
        print("comment_impact: its own controls fail -- the list below would lie.")
        return 2
    if "--selftest" in args:
        print("selftest OK")
        return 0
    max_hits = int(args[args.index("--max-hits") + 1]) if "--max-hits" in args else 25
    changed = parse_diff(git_diff(args))
    if not changed:
        print("comment_impact: no code file changed in that diff.")
        return 0
    listed, broad = find_hits(changed, comment_index("--docs" in args), max_hits)
    nsym = sum(len(v["symbols"]) for v in changed.values())
    stars = 0
    for s in sorted(listed, key=lambda k: (-sum(risky(c) for *_, c in listed[k]), len(listed[k]), k)):
        print(f"\n{s}  ({len(listed[s])} comment line(s))")
        for p, n, c in listed[s]:
            r = risky(c)
            stars += r
            print(f"  {'*' if r else ' '} {p}:{n}  {c[:150]}")
    print(f"\n{len(changed)} code file(s), {nsym} changed symbol(s); {len(listed)} named by comments elsewhere "
          f"({stars} starred line(s): an enumeration or a uniqueness claim -- read those first).")
    if broad:
        print(f"Broad (named by > {max_hits} comments, not listed): " +
              ", ".join(f"{k} ({v})" for k, v in sorted(broad.items(), key=lambda kv: -kv[1])[:20]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
