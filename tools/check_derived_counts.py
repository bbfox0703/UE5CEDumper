#!/usr/bin/env python3
"""Assert that every DERIVED count stated in the docs matches the tree.

WHY THIS EXISTS
  CLAUDE.md already carries the rule — "Docs numbers are DERIVED — regenerate, never hand-edit" —
  and the docs broke it anyway, in the specific way a rule without a check always breaks: the
  number is right on the day it is typed and nobody re-derives it. A 2026-08-06 audit found the
  pipe-command count stated as `~87` in five places, `31` in a sixth and `~30` in a seventh, while
  `Renge.h` held **99**; the C ABI export count as 59 / 57 / 32 / ~30 against a real 59.

  None of those is dangerous on its own. They matter because a reader cannot tell a stale number
  from a live one, so ALL of them stop being trustworthy — including the correct ones.

WHY IT PRINTS THE SENTENCE
  `extract_patterns.py --check` is the model, and the audit worked out WHY it succeeded where the
  prose rule failed: it emits the paste-able sentence. The two docs that pasted its output are
  byte-exact today; the one that paraphrased it ("128+ AOB pattern database") went stale. So this
  prints exactly what to paste, per file, on failure.

WHY A REGISTRY INSTEAD OF A REGEX SWEEP
  A sweep for "<number> commands" cannot tell a live count from a DELTA or a historical snapshot,
  and the docs are full of both — godmode-spec's "+3 commands", teleport-spec's "+6 commands
  (~48 total)", an audit finding's "5 pipe commands" (Solide's own, not the protocol's). Every one
  of those is correct as written and must not be "fixed". So each claim is registered explicitly:
  file + a regex whose single group captures the number. Adding a doc that restates a derived count
  means adding it here — which is the point, not the friction: it makes restating one a deliberate act.

Usage:
  py tools/check_derived_counts.py            # repo root inferred from this file
  py tools/check_derived_counts.py --list     # show every derived value + who claims it
Exit 0 = clean, 1 = a doc disagrees with the tree.
"""
import os
import re
import sys


# ── deriving ────────────────────────────────────────────────────────────────

def count_matching(root, relpath, pattern):
    """Lines in one file matching a regex."""
    path = os.path.join(root, relpath)
    if not os.path.isfile(path):
        return None
    rx = re.compile(pattern)
    with open(path, encoding="utf-8", errors="replace") as f:
        return sum(1 for line in f if rx.search(line))


def count_in_section(root, relpath, h2_prefix, pattern):
    """Lines matching a regex INSIDE one '## ' section; the section ends at the next '## '.

    Scoping is the whole point. A whole-file scan of the verification register answers 11 open
    batches, not 9, because the CJK step sections below it are a different queue. Two
    self-enumeration commands that disagree is exactly how this count drifted to a stale
    43 / 36 / 40 / 30 in turn."""
    path = os.path.join(root, relpath)
    if not os.path.isfile(path):
        return None
    rx = re.compile(pattern)
    n, inside = 0, False
    with open(path, encoding="utf-8", errors="replace") as f:
        for line in f:
            if line.startswith("## "):
                if inside:
                    break
                inside = line.startswith(h2_prefix)
                continue
            if inside and rx.search(line):
                n += 1
    return n


def count_files(root, reldir, suffix, skip=("bin", "obj")):
    """Files under a directory tree, excluding build output."""
    base = os.path.join(root, reldir)
    if not os.path.isdir(base):
        return None
    n = 0
    for dirpath, dirnames, filenames in os.walk(base):
        dirnames[:] = [d for d in dirnames if d not in skip]
        n += sum(1 for f in filenames if f.endswith(suffix))
    return n


def count_dir(root, reldir, suffix):
    """Files directly in one directory (not recursive)."""
    base = os.path.join(root, reldir)
    if not os.path.isdir(base):
        return None
    return sum(1 for f in os.listdir(base) if f.endswith(suffix))


# ── the registry ────────────────────────────────────────────────────────────
# (key, human label, derive(root) -> int, paste-able sentence template, claims)
# A claim is (file, regex). The regex MUST have exactly one group, capturing the
# number as written in the doc.

CHECKS = [
    dict(
        key="open_verification_batches",
        label="open batch headings in the verification register",
        derive=lambda r: count_in_section(
            r, "docs/verification-register.md",
            "## Pending live-game verification", r"^### .*⬜"),
        derive_cmd=("awk '/^## Pending live-game verification/,0' docs/verification-register.md "
                    "| awk '/^## /&&!/^## Pending live-game/{exit}1' | grep '^### ' | grep -c ⬜"),
        # The register ASKED for this. It said "expect 40 and 0 -- a machine check, since this is
        # the second time the invariant drifted", and then drifted again: the real value is 9.
        # A number whose own prose tells you to re-derive it is a number nothing derives.
        claims=[
            ("docs/todo.md",                  r"\*\*(\d+) open batches\*\* needing a running game"),
            ("docs/verification-register.md", r"and expect\s+`(\d+)` and"),
        ],
    ),
    dict(
        key="pipe_commands",
        label="pipe commands",
        derive=lambda r: count_matching(r, "dll/src/Renge.h", r"constexpr const char\* CMD"),
        derive_cmd="grep -c 'constexpr const char* CMD' dll/src/Renge.h",
        claims=[
            ("CLAUDE.md",                 r"the live count is \*\*(\d+)\*\*"),
            ("CLAUDE.md",                 r"Named Pipe JSON IPC protocol \((\d+) commands"),
            ("docs/architecture.md",      r"JSON dispatch \((\d+) commands\)"),
            ("docs/architecture.md",      r"Named Pipe JSON IPC protocol \((\d+) commands\)"),
            ("docs/dll-spec.md",          r"For the JSON protocol \((\d+) commands"),
            ("docs/naming-convention.md", r"Named Pipe JSON IPC \((\d+) commands\)"),
            # The SOURCE header. It is not a doc, and that is exactly why it drifted to
            # "~30" against a real 99 and stayed there: the registry covered every file
            # that derives FROM this one and not this one. (audit #5 AD7/AD22)
            ("dll/src/Fern.h",            r"Named Pipe JSON IPC server \((\d+) commands\)"),
        ],
    ),
    dict(
        key="c_abi_exports",
        label="C ABI exports",
        # Matches the DECLARATION, not the bare word. A loose `dllexport` counted any line
        # mentioning it, so a comment in Frieren.h describing this very check incremented the
        # count it describes (found while wiring AD7's claim). Same number on a clean tree,
        # immune to prose.
        derive=lambda r: count_matching(r, "dll/src/Frieren.h", r"__declspec\(dllexport\)"),
        derive_cmd="grep -c '__declspec(dllexport)' dll/src/Frieren.h",
        claims=[
            ("CLAUDE.md",                 r"loadLibrary / callFunction \((\d+) C ABI exports\)"),
            ("CLAUDE.md",                 r"C ABI exports \(\*\*(\d+)\*\* — derive it"),
            ("docs/architecture.md",      r"ExportAPI — (\d+) C ABI exports"),
            ("docs/naming-convention.md", r"ExportAPI: (\d+) C ABI exports"),
            # The header the count is DERIVED FROM — same blind spot as Fern.h above; it
            # claimed "~30" while being the very file the grep counts. (audit #5 AD7)
            ("dll/src/Frieren.h",         r"ExportAPI: (\d+) C ABI exports"),
        ],
    ),
    dict(
        key="test_files",
        label="C# test files",
        derive=lambda r: count_files(r, "ui/UE5DumpUI.Tests", ".cs"),
        derive_cmd="count *.cs under ui/UE5DumpUI.Tests (excluding bin/ obj/)",
        claims=[
            ("CLAUDE.md",            r"\*\*(\d+)\*\* test files"),
            ("docs/architecture.md", r"xUnit test project \((\d+) \.cs test files"),
        ],
    ),
    dict(
        key="dll_cpp",
        label="DLL .cpp files",
        derive=lambda r: count_dir(r, "dll/src", ".cpp"),
        derive_cmd="ls dll/src/*.cpp | wc -l",
        claims=[
            ("CLAUDE.md", r"\*\*(\d+) \.cpp \+ \d+ \.h\*\* DLL files"),
        ],
    ),
    dict(
        key="dll_h",
        label="DLL .h files",
        derive=lambda r: count_dir(r, "dll/src", ".h"),
        derive_cmd="ls dll/src/*.h | wc -l",
        claims=[
            ("CLAUDE.md", r"\*\*\d+ \.cpp \+ (\d+) \.h\*\* DLL files"),
        ],
    ),
]


def read(path):
    with open(path, encoding="utf-8", errors="replace") as f:
        return f.read()


def main():
    # The registry's regexes quote doc text VERBATIM, em-dashes included, and this
    # runs on CI and on a cp950 console. Echoing an unmatched pattern back would
    # then raise UnicodeEncodeError and the CRASH would replace the finding it was
    # reporting. Found by actually walking the failure path, not by reading it.
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass

    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    listing = "--list" in sys.argv[1:]
    failures = []
    missing = []

    for chk in CHECKS:
        truth = chk["derive"](root)
        if truth is None:
            missing.append(f"{chk['key']}: cannot derive — the source moved ({chk['derive_cmd']})")
            continue
        if listing:
            print(f"{chk['label']:22} = {truth}   ({chk['derive_cmd']})")

        for relpath, pattern in chk["claims"]:
            full = os.path.join(root, relpath)
            if not os.path.isfile(full):
                missing.append(f"{chk['key']}: {relpath} not found")
                continue
            m = re.search(pattern, read(full))
            if not m:
                # A claim that stops matching is itself a finding: either the prose was
                # reworded (so the check silently stopped covering it) or the number was
                # removed. Both need a human, and a silently-uncovered claim is exactly
                # the state this tool exists to prevent.
                missing.append(f"{chk['key']}: {relpath} no longer matches /{pattern}/ "
                               f"— reworded? update the registry or restore the sentence")
                continue
            stated = int(m.group(1))
            if listing:
                mark = "ok " if stated == truth else "STALE"
                print(f"    {mark} {relpath}: {stated}")
            elif stated != truth:
                failures.append((chk, relpath, stated, truth))

    if listing:
        return 0

    if failures or missing:
        print("CHECK FAILED: a doc states a derived count that disagrees with the tree.\n")
        for chk, relpath, stated, truth in failures:
            print(f"  {relpath}")
            print(f"    says {stated} {chk['label']}, tree has {truth}")
            print(f"    derive: {chk['derive_cmd']}")
            print(f"    paste:  {truth}\n")
        for msg in missing:
            print(f"  [!] {msg}")
        if failures:
            print("These are DERIVED numbers. Fix the doc from the tree — never the other way,\n"
                  "and never by guessing the delta.")
        return 1

    total = sum(len(c["claims"]) for c in CHECKS)
    print(f"CHECK OK: {total} derived-count claim(s) across "
          f"{len({f for c in CHECKS for f, _ in c['claims']})} file(s) match the tree.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
