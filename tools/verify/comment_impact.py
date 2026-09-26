#!/usr/bin/env python3
"""Change-time aid: which comments ELSEWHERE name a symbol this diff touches?

    py tools/verify/comment_impact.py                 # working tree (staged + unstaged) vs HEAD
    py tools/verify/comment_impact.py --staged        # the index only -- run it before `git commit`
    py tools/verify/comment_impact.py --rev HEAD      # one commit (rev^..rev)
    py tools/verify/comment_impact.py --range A..B    # a span, e.g. a PR: origin/main..dev
    py tools/verify/comment_impact.py --docs          # also search docs/*.md (specs enumerate too)
    py tools/verify/comment_impact.py --max-hits 25   # a symbol named by more blocks than this is "broad"
    py tools/verify/comment_impact.py --selftest

WHY ([COMMENT-INTEGRITY-2026-09-26], docs/comment-integrity-eval.md). The sampled stale-comment rate on build 3560
was 26%, and the commonest cause was ADDITIVE DRIFT: a commit adds a caller / field / pipe key / enum value, and the
comment that LISTS them -- in another file, written months earlier -- is never opened. No gate can know what a
comment should say; what a tool can do is put that comment in front of the author at the moment it goes stale.

Not a gate (exit 0 whatever it finds). It collects the distinctive identifiers on the diff's changed CODE lines (a new
call site counts: that is exactly the change that makes a caller list stale) plus each hunk's enclosing function,
then lists every comment BLOCK outside the diff that names one -- in its text, or on the declaration directly below
it, because a doc comment rarely repeats the name it documents (the caller list over FindInstancesByClass in Aura.h
names five modules and not the function). A hit that also enumerates or claims uniqueness ("only", "both", "the
one", "exactly", a number word, an a/b/c or comma list of identifiers) is starred -- read those first. "every" and
"each" are NOT starred: "every non-pipe caller" is the wording the comment-style rule asks for. Symbols named by more
than --max-hits blocks are reported as broad and not listed: at that spread the name is vocabulary, not a reference.
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
RISKY = re.compile(r"(?i)\b(only|both|the one|the sole|sole|exactly|two|three|four|five|six|seven|eight|nine|ten|"
                   r"\d+\s+(?:callers?|fields?|keys?|entries|values?|sites?|modules?|commands?|states?|knobs?))\b")
SLASH_LIST = re.compile(r"\b[A-Za-z_]\w+/[A-Za-z_]\w+/[A-Za-z_]\w+")
MARKS = {".cpp": "//", ".h": "//", ".hpp": "//", ".cs": "//", ".lua": "--", ".ct": "--", ".py": "#"}


def distinctive(tok: str) -> bool:
    """CamelCase with 2+ humps, snake/SCREAMING with an underscore, camelCase -- not plain English."""
    if tok in STOP or tok.isdigit():
        return False
    if "_" in tok.strip("_") and re.search(r"[A-Za-z]{2}", tok):
        return True
    humps = len(re.findall(r"[A-Z][a-z0-9]+", tok))
    return humps >= 2 or (humps == 1 and tok[0].islower() and any(c.isupper() for c in tok))


def code_part(line: str, ext: str) -> str:
    """The line minus its comment -- a symbol mentioned only in a changed COMMENT is not a changed symbol."""
    m = MARKS.get(ext)
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


def blocks_of(path, lines):
    """-> [(first, last, [(n, text)], subject_code)]. A block is a run of comment-only lines, and its SUBJECT is
    the next non-blank line's code -- the declaration it documents. A trailing comment is its own block whose
    subject is the code on its own line."""
    ext = os.path.splitext(path)[1].lower()
    out, run = [], []

    def close():
        if run:
            subj = ""
            for k in range(run[-1][0], min(run[-1][0] + 3, len(lines))):  # 0-based index of the next line(s)
                if lines[k].strip():
                    subj = code_part(lines[k], ext)
                    break
            out.append((run[0][0], run[-1][0], list(run), subj))
            run.clear()

    for n, c in G.comment_text(path, lines):
        own = code_part(lines[n - 1], ext).strip()
        if own and not own.startswith(("/*", "*")):
            close()
            out.append((n, n, [(n, c)], own))
            continue
        if run and n != run[-1][0] + 1:
            close()
        run.append((n, c))
    close()
    return out


def md_blocks(lines):
    out, run = [], []
    for n, l in enumerate(lines, 1):
        if l.strip():
            run.append((n, l))
        elif run:
            out.append((run[0][0], run[-1][0], list(run), ""))
            run = []
    if run:
        out.append((run[0][0], run[-1][0], list(run), ""))
    return out


def index_entry(p, a, b, body, subj):
    return (p, a, b, body, subj, set(IDENT.findall(" ".join(c for _, c in body) + " " + subj)))


def comment_index(with_docs: bool):
    """-> [(path, first, last, [(n, text)], subject_code, words)] over every tracked code file (+ docs/*.md)."""
    out = []
    for p in G.tracked():
        ext = os.path.splitext(p)[1].lower()
        code = p.startswith(G.CODE_DIRS) and ext in CODE_EXT
        doc = with_docs and p.startswith("docs/") and not p.startswith("docs/archive/") and ext == ".md"
        if not (code or doc):
            continue
        try:
            lines = G.read(os.path.join(ROOT, p)).split("\n")
        except OSError:
            continue
        for a, b, body, subj in (blocks_of(p, lines) if code else md_blocks(lines)):
            out.append(index_entry(p, a, b, body, subj))
    return out


def risky(c: str) -> bool:
    if RISKY.search(c) or SLASH_LIST.search(c):
        return True
    items = [t.strip(" `'\"()") for t in c.split(",")]
    return sum(1 for t in items
               if re.fullmatch(r"[A-Za-z_]\w*(?:::\w+)*", t) and distinctive(t.split("::")[-1])) >= 3


def find_hits(changed, index, max_hits):
    """-> ({symbol: [(path, n, shown line, starred)]}, {symbol: count}) -- one entry per BLOCK."""
    syms = set().union(*(v["symbols"] for v in changed.values())) if changed else set()
    inside = {p: v["ranges"] for p, v in changed.items()}
    hits = {s: [] for s in syms}
    for p, a, b, body, subj, words in index:
        found = syms & words
        if not found or any(a <= y and x <= b for x, y in inside.get(p, ())):
            continue
        star = next(((n, c) for n, c in body if risky(c)), None)
        for s in found:
            named = next(((n, c) for n, c in body if re.search(r"\b" + re.escape(s) + r"\b", c)), None)
            n, c = star or named or body[0]
            c = c.strip()
            if not named:
                c += f"   [documents: {subj.strip()[:70]}]"
            hits[s].append((p, n, c, star is not None))
    listed = {s: h for s, h in hits.items() if 0 < len(h) <= max_hits}
    broad = {s: len(h) for s, h in hits.items() if len(h) > max_hits}
    return listed, broad


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
    # The Aura.h shape: the caller list never names the function -- the declaration below it does.
    aura_h = ["// When false (the cheap internal", "// callers: Wirbel/Edel/Solitar, which want a bounded scan),",
              "// the loop keeps the early exit.", "SearchResultSet FindObjectsByName(const std::string& n);"]
    other = ["// unrelated FindObjectsByNameEx here", "int x = 0;"]
    laufen = [""] * 10 + ["    // FindObjectsByName inside the hunk itself", "    Aura::FindObjectsByName(n);"]
    idx = [index_entry(p, *blk) for p, L in (("dll/src/Aura.h", aura_h), ("dll/src/Other.cpp", other),
                                             ("dll/src/Laufen.cpp", laufen)) for blk in blocks_of(p, L)]
    listed, _ = find_hits(ch, idx, 25)
    got = [(p, n, st) for p, n, _, st in listed.get("FindObjectsByName", [])]
    if got != [("dll/src/Aura.h", 2, True)]: bad.append(f"block hit wrong (want the caller list via its declaration): {got}")
    if risky(" plain prose, with commas, about the walker"): bad.append("plain comma prose starred")
    if risky(" every non-pipe caller wants a bounded scan"): bad.append("the recommended 'every' wording starred")
    if not risky(" the callers are FindA, FindB, FindC today"): bad.append("an identifier comma list not starred")
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
    for s in sorted(listed, key=lambda k: (-sum(h[3] for h in listed[k]), len(listed[k]), k)):
        print(f"\n{s}  ({len(listed[s])} comment block(s))")
        for p, n, c, r in listed[s]:
            stars += r
            print(f"  {'*' if r else ' '} {p}:{n}  {c[:160]}")
    print(f"\n{len(changed)} code file(s), {nsym} changed symbol(s); {len(listed)} named by comments elsewhere "
          f"({stars} starred block(s): an enumeration or a uniqueness claim -- read those first).")
    if broad:
        print(f"Broad (named by > {max_hits} blocks, not listed): " +
              ", ".join(f"{k} ({v})" for k, v in sorted(broad.items(), key=lambda kv: -kv[1])[:20]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
