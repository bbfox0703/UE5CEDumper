#!/usr/bin/env python3
"""Does a code comment gain history? A per-file ratchet over the product code's comments.

    py tools/check_comment_history.py              # compare with the baseline; exit 1 on any change
    py tools/check_comment_history.py --update     # lower the baseline after history was cut (refuses on growth)
    py tools/check_comment_history.py --rebaseline # rewrite it regardless: ONLY when the counting rules change
    py tools/check_comment_history.py --list [PATH-SUBSTRING]   # print every counted comment line, exit 0

The rule ([COMMENT-NO-HISTORY-2026-11-18], working-lessons §8 rule 7): a comment says what the code does and why, as
it stands. History -- a fix narrative, a review id, a measurement, a bug's cause -- belongs in dev-log.md under a
[TAG]; a comment carries at most the tag and its date. The comments already in the code are grandfathered per file
and cut down by hand; this gate keeps the count from growing:

  * a file's count going UP fails -- a new history line; cut it to its [TAG] and date;
  * a file's count going DOWN also fails, and prints the --update command, so the baseline ratchets down and a later
    history line cannot hide in the slack an old one left.

Scope: SCOPES below -- the product and test code (C++ / C#, and the UI's XAML), the scripts (Lua, the .CT,
PowerShell, Python) and the tooling (Python). Counted: one per comment LINE (// or a /* */ block's line in C++ / C#;
the other languages through tools/comment_lang.py, a Python docstring included; string literals are skipped) holding
any HISTORY pattern below. The patterns are chosen for few false
hits on a NEW line, since a false hit fails the gate: "is used to", "measured at init" and a [TAG] with its date are
not history.
"""
from __future__ import annotations

import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import comment_lang  # noqa: E402  (the XAML / Lua / PowerShell / Python comment scanners)

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BASELINE = os.path.join(ROOT, "tools", "comment_history_baseline.json")
SCOPES = [(os.path.join(ROOT, "dll", "src"), (".h", ".cpp")),
          (os.path.join(ROOT, "ui", "UE5DumpUI"), (".cs", ".axaml")),
          # The tests' comments too: a test's doc says what it pins, and the history behind it is the dev-log's.
          (os.path.join(ROOT, "dll", "tests"), (".h", ".cpp")),
          (os.path.join(ROOT, "ui", "UE5DumpUI.Tests"), (".cs",)),
          (os.path.join(ROOT, "ui", "UE5DumpUI.HeadlessTests"), (".cs",)),
          # The emitted / shipped scripts and the repo's own tooling, through comment_lang's scanners.
          (os.path.join(ROOT, "scripts"), (".lua", ".ct", ".ps1", ".py")),
          (os.path.join(ROOT, "tools"), (".py",))]
SKIP_DIRS = {"bin", "obj", "TestResults", "__pycache__"}
# This file's own comments document the patterns with examples of the history they count.
SKIP_FILES = {"tools/check_comment_history.py"}
C_LIKE = (".h", ".cpp", ".cs")

HISTORY = [
    re.compile(r"\bwf_[0-9a-f]{8}-[0-9a-f]{3}\b"),                         # a review workflow's id
    re.compile(r"\breview (?:of build|round|R\d|wf_)", re.IGNORECASE),     # "review of build 3596", "review round 3"
    # "X used to Y": past behaviour. "is / are / be ... used to <verb>" is a purpose, not history.
    re.compile(r"(?<!\bis )(?<!\bare )(?<!\bbe )(?<!\bbeen )(?<!\bbeing )(?<!\bget )(?<!\bgets )(?<!\bgot )"
               r"(?<!\bwas )(?<!\bwere )\bused to\b", re.IGNORECASE),
    re.compile(r"\b[Mm]easured (?:on [A-Z]|20\d\d|in build)"),               # "Measured on DumperTest", "measured 2026-..."
    re.compile(r"\bbuilds? \d{4}\b", re.IGNORECASE),                       # "since build 3713", "builds 3728-3730"
    re.compile(r"\b(?:pre-fix|before the fix|after the fix|was fixed|the old (?:code|version))\b", re.IGNORECASE),
    # Pass 2: narrative shapes the first set missed, each measured for few false hits before it was added.
    re.compile(r"\b(?:first|second|third|fourth|fifth|sixth|seventh|eighth|ninth|tenth|eleventh|twelfth|"
               r"\d+(?:st|nd|rd|th)) review\b", re.IGNORECASE),                # "(tenth review, R10-02)"
    re.compile(r"\breview \d+ of\b|\breview follow-up\b|\breview finding\b", re.IGNORECASE),
    re.compile(r"(?:^\s*|[.;:(]\s*)Previously\b"),                           # a sentence that opens on the past
    re.compile(r"\b[Tt]he old (?:\w+ ){0,2}(?:was|were|did|said|read|used|collapsed|matched|took|had)\b"),
    re.compile(r"\b(?:PROMOTED|DEMOTED|promoted|demoted|moved) \d+\s*(?:->|→)\s*\d+"),   # a pattern's old priority
    re.compile(r"\b[Tt]he (?:first|original|previous) version (?:of|was|were|did|required|matched|had)\b"),
    re.compile(r"(?:^\s*|[.;]\s+)Until now\b|\buntil (?:this|the) fix\b"),
    re.compile(r"\bturned out\b", re.IGNORECASE),
    re.compile(r"\bcommit [0-9a-f]{7,10}\b", re.IGNORECASE),
    re.compile(r"\bmaintainer\b[^.\n]{0,60}\b20\d\d-\d\d-\d\d\b", re.IGNORECASE),   # a decision's provenance
    re.compile(r"\b(?:[Oo]bserved|[Ss]een|[Rr]eproduced|[Ff]ound|[Rr]eported) (?:on|in|by)\b[^.\n]{0,40}"
               r"\b20\d\d-\d\d-\d\d\b"),
    re.compile(r"\b(?:before|until|since) 202[6-9]-\d\d-\d\d\b"),             # this project's own dates
    # Pass 3: an audit's or a review's finding id, the reference a [TAG] replaces.
    re.compile(r"\baudit #\d+\b", re.IGNORECASE),                            # "(audit #5 A12)", "Audit #4 B34."
    re.compile(r"\b[Rr]eview [A-Z]{1,6}(?:-[A-Z0-9]{1,6})*-\d+[a-z]?\b"),     # "(review DWS-DLL-1)", "review UI-6"
    re.compile(r"\baudit (?:fix(?:es)?|finding) #\d+", re.IGNORECASE),        # "Audit fix #10:", "audit finding #2"
    re.compile(r"\baudit [A-Z]{1,3}\d+[a-z]?\b", re.IGNORECASE),             # "(audit U17)", "Audit PX1"
    re.compile(r"\b[Rr]eview [A-Z]{1,3}\d+[a-z]?\b"),                         # "(review F4)"
]


def comment_lines(text: str) -> list[tuple[int, str]]:
    """[(line number, comment text)] for one C++ / C# source: // tails and /* */ block lines, outside string literals.
    A scanner, not a parser: a quote opens a string, a backslash escapes inside one, a ' opens a char literal."""
    out: list[tuple[int, str]] = []
    in_block = False
    for no, line in enumerate(text.splitlines(), 1):
        i, n, quote, parts = 0, len(line), None, []
        if in_block:
            end = line.find("*/")
            if end < 0:
                out.append((no, line))
                continue
            parts.append(line[:end])
            in_block = False
            i = end + 2
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
                parts.append(line[i + 2:])
                break
            elif line.startswith("/*", i):
                end = line.find("*/", i + 2)
                if end < 0:
                    parts.append(line[i + 2:])
                    in_block = True
                    break
                parts.append(line[i + 2:end])
                i = end + 2
                continue
            i += 1
        if parts:
            out.append((no, " ".join(parts)))
    return out


def history_rows(text: str, rel: str = "x.cs") -> list[str]:
    """[`line: comment`] for each comment line holding a history pattern; `rel` picks the language."""
    lines = comment_lines(text) if rel.lower().endswith(C_LIKE) else comment_lang.comments(rel, text)
    return [f"{no}: {c.strip()[:140]}" for no, c in lines if any(p.search(c) for p in HISTORY)]


def scan() -> dict[str, list[str]]:
    found: dict[str, list[str]] = {}
    for base_dir, exts in SCOPES:
        for base, dirs, files in os.walk(base_dir):
            dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
            for name in sorted(files):
                if not name.lower().endswith(exts):
                    continue
                path = os.path.join(base, name)
                rel = os.path.relpath(path, ROOT).replace(os.sep, "/")
                if rel in SKIP_FILES:
                    continue
                with open(path, encoding="utf-8-sig", errors="replace") as fh:
                    rows = history_rows(fh.read(), rel)
                if rows:
                    found[rel] = rows
    return found


# The counting rules' controls, run before every scan: a rule that silently stopped counting is a gate that passes.
SELFTEST = [
    ("// Review wf_1b9e937b-a0d DLL-1: the guard missed it", 1),
    ("// [TRACE-DROPPED-ENTRY] 2026-11-18", 0),                         # the allowed form: a tag and its date
    ("// the offset is used to read the name", 0),                     # a purpose
    ("// it used to answer the old walk", 1),                          # past behaviour
    ("// Measured on DumperTest (UE 5.4): the hit is a component", 1),
    ("// the offset is measured at init", 0),                          # a mechanism
    ("// since build 3713 the DLL refuses it", 1),
    ("// a give-up session never measures USTRUCT_SUPER", 0),
    ('Log("it used to fail"); // writes the line', 0),                 # history inside a string is not a comment
    ("/* the review of build 3596 found it */", 1),
    ("/* a block\n   that used to say more */", 1),                    # a block comment's later line
    ("x = 'a'; // char literal, then a plain comment", 0),
    ('s = "a // b"; // no history here', 0),                           # // inside a string opens no comment
    ("// (tenth review, R10-02) Test seam: called after the pin", 1),
    ("// [W2-MARKER-PARENTREL] ALWAYS sent (review 5 of 7490c24e)", 1),
    ("// Previously each entry point did an unsynchronized check", 1),
    ("// the previously selected row stays selected", 0),              # an adverb of the present
    ("// The old rule was `if (firstConn) reset;`", 1),
    ("// leaving the field blank while the old value is still shown", 0),
    ("// PROMOTED 210 -> 101. The most successful GWorld pattern", 1),
    ("// The first version required ByteMask 0xFF", 1),
    ("// a latch taken under the PREVIOUS version outranks the new one", 0),   # a UE version, not ours
    ("// Until now this branch did not exist", 1),
    ("// The maintainer's decision (2026-10-06): HTML only", 1),
    ("// Observed on Elliot 2026-08-07: closing the game", 1),
    ("// found in the storage map, and UE erases that entry", 0),
    ("// set unconditionally until 2026-09-09", 1),
    ("// on by default since 2022-03-30 (WeakObjectPtr.h @5.1.0-release)", 0),  # an engine fact, its own date
    ("// NO ULevel::Actors lookup (audit #5 F8).", 1),
    ("// `anchor` has NO default (Audit #4 B21): the callers", 1),
    ("// the address held another function (review DWS-DLL-1)", 1),
    ("// [A3-B30-STALE-FLAG] cleared before the untick is deferred", 0),  # a tag is the allowed reference
    ("// the audit trail keeps #3 of the entries", 0),
    ("// open the Review UI dialog", 0),                                # a name, not a finding id
    ("// Audit fix #13: re-enable after soft disable", 1),
    ("// shared with the reflected-struct preview (audit U17).", 1),
    ("// wraps to about 1.8e15 ms (review F4).", 1),
    ("// the audit log records every write", 0),                        # a feature, not a finding
]


SELFTEST_OTHER = [   # the same rules over the other languages' comments
    ("a.lua", "x = 1 -- it used to answer the old walk", 1),
    ("a.lua", "x = 'it used to answer' -- the current walk", 0),         # history inside a string
    ("a.axaml", "<Grid><!-- since build 3713 the panel refuses it --></Grid>", 1),
    ("a.py", '"""Review wf_1b9e937b-a0d DLL-1: the guard missed it."""\nx = 1\n', 1),
    ("a.py", "x = 'measured on DumperTest'  # reads the name\n", 0),
    ("a.ps1", "$x = 1 # (audit #5 F8)", 1),
]


def selftest() -> list[str]:
    bad = [f"{src!r}: counted {len(history_rows(src))}, expected {want}"
           for src, want in SELFTEST if len(history_rows(src)) != want]
    bad += [f"{rel} {src!r}: counted {len(history_rows(src, rel))}, expected {want}"
            for rel, src, want in SELFTEST_OTHER if len(history_rows(src, rel)) != want]
    return bad + comment_lang.selftest()


def main() -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="backslashreplace")
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
        args = [a for a in sys.argv[1:] if not a.startswith("--")]
        for rel, rows in sorted(found.items()):
            if args and not any(a in rel for a in args):
                continue
            for r in rows:
                print(f"  {rel}:{r}")
        print(f"{total} history line(s) in {len(counts)} file(s)")
        return 0

    baseline: dict[str, int] = {}
    if os.path.exists(BASELINE):
        with open(BASELINE, encoding="utf-8") as fh:
            baseline = json.load(fh)
    elif "--rebaseline" not in sys.argv:
        print(f"CHECK FAILED: no baseline at {os.path.relpath(BASELINE, ROOT)} -- create it with --rebaseline")
        return 1

    grew, shrank = [], []
    for rel in sorted(set(counts) | set(baseline)):
        now, was = counts.get(rel, 0), baseline.get(rel, 0)
        if now > was:
            grew.append((rel, was, now))
        elif now < was:
            shrank.append((rel, was, now))

    if "--update" in sys.argv or "--rebaseline" in sys.argv:
        if grew and "--rebaseline" not in sys.argv:
            print("REFUSED: --update only lowers the baseline, and these files GREW (cut the new history to its [TAG]):")
            for rel, was, now in grew:
                print(f"    {rel}: {was} -> {now}")
            return 1
        with open(BASELINE, "w", encoding="utf-8", newline="\n") as fh:
            json.dump(counts, fh, indent=2, sort_keys=True)
            fh.write("\n")
        print(f"baseline written: {total} history line(s) in {len(counts)} file(s)"
              + (" (--rebaseline: the counting rules changed)" if "--rebaseline" in sys.argv else ""))
        return 0

    rc = 0
    if grew:
        rc = 1
        print("FAIL: a comment gained history ([COMMENT-NO-HISTORY-2026-11-18], working-lessons §8 rule 7).")
        print("A comment says what the code does; the history goes to dev-log.md under a [TAG], and the comment keeps")
        print("at most the tag and its date. The lines counted in each file that grew:")
        for rel, was, now in grew:
            print(f"    {rel}: {was} -> {now}")
            for r in found.get(rel, []):
                print(f"        {r}")
    if shrank:
        rc = 1
        print("FAIL: a file has FEWER history lines than the baseline -- good; lower the baseline so the slack cannot")
        print("be reused:")
        print("    py tools/check_comment_history.py --update")
        for rel, was, now in shrank:
            print(f"    {rel}: {was} -> {now}")
    if rc == 0:
        print(f"CHECK OK: {total} grandfathered history line(s) in {len(counts)} file(s), none added.")
    return rc


if __name__ == "__main__":
    raise SystemExit(main())
