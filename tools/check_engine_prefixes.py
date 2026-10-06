#!/usr/bin/env python3
"""The three copies of the engine-package list must name the same modules.

    py tools/check_engine_prefixes.py             # exit 1 and say which copy differs, and how
    py tools/check_engine_prefixes.py --selftest  # the negative controls only (every run does them first)

WHY. Which `/Script/<Module>` paths count as the ENGINE (rather than the game's own C++) is decided
in three places that cannot share code:

  * `dll/src/Aura.h` `IsEnginePackage` -- the source: GameOnly filters in the DLL;
  * `ui/UE5DumpUI/Services/DumpAllService.cs` `EnginePathPrefixes` -- Dump All's GameOnly skip;
  * `scripts/analysis/diff_dumps.py` `ENGINE_PATH_PREFIXES` -- what a default patch diff leaves out.

A module added to one and not the others makes the DLL, Dump All and the diff disagree about the same
class, silently: each side still runs and reports. Before 2026-10-06 the diff used no list at all and
called every "/Script/" path engine, which dropped the game's own native classes from every default
diff (R4 of the PR 539 / 540 re-check).

WHAT IT CHECKS. The string literals inside each list, compared with Aura.h's as sets, plus duplicates
within a copy. Order is not checked: every copy tests each prefix in turn. The matching RULE (collapse
the leading slashes, then a prefix must be followed by the end, '/' or '.') is not parsed here; each
copy's tests pin it.
"""
from __future__ import annotations

import pathlib
import re
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ROOT = pathlib.Path(__file__).resolve().parents[1]

# (label, file, regex whose group 1 is the body of the list)
COPIES = (
    ("Aura.h IsEnginePackage", "dll/src/Aura.h",
     r"kEnginePrefixes\[\]\s*=\s*\{(.*?)\};"),
    ("DumpAllService.EnginePathPrefixes", "ui/UE5DumpUI/Services/DumpAllService.cs",
     r"EnginePathPrefixes\s*=\s*\{(.*?)\};"),
    ("diff_dumps.ENGINE_PATH_PREFIXES", "scripts/analysis/diff_dumps.py",
     r"ENGINE_PATH_PREFIXES\s*=\s*\((.*?)\)\n"),
)
LITERAL = re.compile(r'"(/Script/[^"]*)"')


def extract(text: str, pattern: str) -> list[str] | None:
    m = re.search(pattern, text, re.S)
    if not m:
        return None
    return LITERAL.findall(m.group(1))


def compare(lists: dict[str, list[str] | None]) -> list[str]:
    """Problems found, one line each; empty when every copy agrees with the first."""
    problems: list[str] = []
    labels = list(lists)
    for label in labels:
        if lists[label] is None:
            problems.append(f"{label}: the list was not found (renamed or reshaped?)")
        elif not lists[label]:
            problems.append(f"{label}: the list holds no \"/Script/...\" literal")
    if problems:
        return problems
    source_label = labels[0]
    source = set(lists[source_label])
    for label in labels:
        items = lists[label]
        dups = sorted({x for x in items if items.count(x) > 1})
        if dups:
            problems.append(f"{label}: listed twice: {', '.join(dups)}")
        if label == source_label:
            continue
        missing = sorted(source - set(items))
        extra = sorted(set(items) - source)
        if missing:
            problems.append(f"{label}: missing {', '.join(missing)} (in {source_label})")
        if extra:
            problems.append(f"{label}: has {', '.join(extra)}, which {source_label} does not")
    return problems


def selftest() -> list[str]:
    """Negative controls: the comparison must catch each kind of drift."""
    base = ["/Script/Engine", "/Script/UMG"]
    cases = (
        ("all equal", {"a": base, "b": list(reversed(base))}, False),
        ("one missing", {"a": base, "b": base[:1]}, True),
        ("one extra", {"a": base, "b": base + ["/Script/Game"]}, True),
        ("a duplicate", {"a": base, "b": base + base[:1]}, True),
        ("a list not found", {"a": base, "b": None}, True),
        ("an empty list", {"a": base, "b": []}, True),
    )
    failures = []
    for name, lists, should_fail in cases:
        if bool(compare(lists)) != should_fail:
            failures.append(f"selftest '{name}': expected {'a problem' if should_fail else 'none'}")
    # The extractors must read each copy's own syntax.
    shapes = (
        ('static const char* const kEnginePrefixes[] = {\n  "/Script/Engine", "/Script/UMG",\n};', COPIES[0][2]),
        ('private static readonly string[] EnginePathPrefixes =\n{\n  "/Script/Engine", "/Script/UMG",\n};',
         COPIES[1][2]),
        ('ENGINE_PATH_PREFIXES = (\n    "/Script/Engine", "/Script/UMG",\n)\n', COPIES[2][2]),
    )
    for text, pattern in shapes:
        if extract(text, pattern) != base:
            failures.append(f"selftest: the pattern {pattern!r} does not read its own shape")
    return failures


def main() -> int:
    failures = selftest()
    if failures:
        print("check_engine_prefixes FAILED its own selftest:")
        for f in failures:
            print("  " + f)
        return 2
    if "--selftest" in sys.argv:
        print("check_engine_prefixes selftest OK")
        return 0

    lists: dict[str, list[str] | None] = {}
    for label, rel, pattern in COPIES:
        path = ROOT / rel
        lists[label] = extract(path.read_text(encoding="utf-8"), pattern) if path.exists() else None
    problems = compare(lists)
    if problems:
        print("check_engine_prefixes FAILED: the engine-package lists disagree\n")
        for p in problems:
            print("  " + p)
        print("\nAura.h is the source. Make the other copies list the same modules, in the same commit.")
        return 1
    n = len(lists[COPIES[0][0]] or [])
    print(f"check_engine_prefixes OK: {len(COPIES)} copies, {n} engine modules each")
    return 0


if __name__ == "__main__":
    sys.exit(main())
