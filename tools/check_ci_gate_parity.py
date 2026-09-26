#!/usr/bin/env python3
"""Every gate tools/check_all.py runs must ALSO run in .github/workflows/ci.yml, and every gate CI runs pre-build must
be in check_all -- the two lists are one list kept in two places.

    py tools/check_ci_gate_parity.py          # exit 1 and name each gate missing from the other list
    py tools/check_ci_gate_parity.py --list   # print both sequences side by side
    py tools/check_ci_gate_parity.py --selftest   # the negative controls only (every run does them first)

WHY ([CI-GATE-DRIFT-2026-09-25]). check_all.py's own docstring says "ADDING A GATE MEANS ADDING IT TO BOTH LISTS, and
nothing enforces that" -- and the drift it describes (two gates absent from CI for months, closed 2026-09-06) had
recurred: NINE gates appended to check_all after that date never reached ci.yml, so a PR could break any of them and
redden nothing. This is the enforcement.

HOW. The gates are read from check_all.GATES (imported, not parsed). A gate "is in CI" when ci.yml runs its script
with the same arguments on an `& $py ...` line. The one post-build CI step (check_proxy_exports --artifacts, which
needs the built DLLs) is expected in CI only. Comments in ci.yml do not count.

EXIT CHECK (second review, PARITY-NO-EXITCHECK). Every such line must be followed AT ONCE (the next non-blank line)
by `if ($LASTEXITCODE -ne 0) { throw ... }`: PowerShell does not stop on a failing native command, so a gate line
without it runs, fails, and leaves the step green. The negative controls (_SELFTEST) run before every check.
"""
from __future__ import annotations

import importlib.util
import pathlib
import re
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ROOT = pathlib.Path(__file__).resolve().parents[1]
CI = ROOT / ".github" / "workflows" / "ci.yml"
CI_ONLY = {"tools/check_proxy_exports.py --artifacts --list"}   # post-build: needs the linked proxy DLLs


def check_all_gates():
    spec = importlib.util.spec_from_file_location("check_all", ROOT / "tools" / "check_all.py")
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    out = []
    for name, argv, _why, _slow in mod.GATES:
        out.append((name, " ".join(a.replace("\\", "/") for a in argv)))
    return out


EXIT_CHECK = re.compile(r"^if\s*\(\s*\$LASTEXITCODE\s+-ne\s+0\s*\)\s*\{\s*throw\b")


def ci_invocations(text: str):
    """-> [(command, checked)] for every `& $py tools/...` line of the CI text. `checked`: the very next non-blank
    line throws on a non-zero $LASTEXITCODE."""
    lines = text.splitlines()
    runs = []
    for i, line in enumerate(lines):
        s = line.strip()
        if s.startswith("#"):
            continue
        m = re.match(r"&\s*\$py\s+(tools/\S+\.py(?:\s+[^;|]*?)?)\s*$", s)
        if m:
            nxt = next((ln.strip() for ln in lines[i + 1:] if ln.strip()), "")
            runs.append((normalise(re.sub(r"\s+", " ", m.group(1)).strip()), bool(EXIT_CHECK.match(nxt))))
    return runs


def normalise(cmd: str) -> str:
    # check_all passes the TSV as out\sweep\patterns.tsv (os.path.join); CI spells it out/sweep/patterns.tsv.
    return cmd.replace("\\", "/")


def analyse(gates, ci_text: str):
    """-> (missing_in_ci, missing_here, unchecked). `gates` is [(name, command)] as check_all_gates returns it."""
    ci = ci_invocations(ci_text)
    cmds = [c for c, _ in ci]
    names = {normalise(g) for _, g in gates}
    missing_in_ci = [(n, c) for n, c in gates if normalise(c) not in cmds]
    missing_here = [c for c in cmds if c not in names and c not in CI_ONLY]
    unchecked = [c for c, checked in ci if not checked]
    return missing_in_ci, missing_here, unchecked


# (second review, PARITY-NO-EXITCHECK) Negative controls. In PowerShell a failing native command does NOT stop the
# script: a gate line with no `if ($LASTEXITCODE -ne 0) { throw ... }` after it runs, fails, and leaves the step
# green. So "the gate is in CI" has to mean "it runs AND its exit code is checked".
_G = [("a", "tools/a.py"), ("b", "tools/b.py --x")]
_THROW = '  if ($LASTEXITCODE -ne 0) { throw "x FAILED" }'
_SELFTEST = [
    ("both lists agree and every line is checked",
     "\n".join(["  & $py tools/a.py", _THROW, "  & $py tools/b.py --x", _THROW]), ([], [], [])),
    ("a gate missing from CI",
     "\n".join(["  & $py tools/a.py", _THROW]), ([("b", "tools/b.py --x")], [], [])),
    ("a CI gate check_all lacks",
     "\n".join(["  & $py tools/a.py", _THROW, "  & $py tools/b.py --x", _THROW, "  & $py tools/c.py", _THROW]),
     ([], ["tools/c.py"], [])),
    ("a commented-out gate line does not count",
     "\n".join(["  & $py tools/a.py", _THROW, "  # & $py tools/b.py --x", _THROW]), ([("b", "tools/b.py --x")], [], [])),
    ("a gate line with NO exit check after it",
     "\n".join(["  & $py tools/a.py", "  & $py tools/b.py --x", _THROW]), ([], [], ["tools/a.py"])),
    ("the last line of the step, with no exit check",
     "\n".join(["  & $py tools/a.py", _THROW, "  & $py tools/b.py --x"]), ([], [], ["tools/b.py --x"])),
    ("an exit check that only warns does not stop the step",
     "\n".join(["  & $py tools/a.py", '  if ($LASTEXITCODE -ne 0) { Write-Warning "a" }', "  & $py tools/b.py --x",
                _THROW]), ([], [], ["tools/a.py"])),
    ("an exit check separated from its gate by a comment is not ITS check",
     "\n".join(["  & $py tools/a.py", "  # note", _THROW, "  & $py tools/b.py --x", _THROW]), ([], [], ["tools/a.py"])),
]


def selftest(verbose: bool) -> bool:
    ok_all = True
    for what, text, want in _SELFTEST:
        got = analyse(_G, text)
        ok = tuple(list(x) for x in got) == tuple(list(x) for x in want)
        ok_all &= ok
        if verbose or not ok:
            print(f"  {'PASS' if ok else 'FAIL'}  {what}" + ("" if ok else f"\n        want {want}\n        got  {got}"))
    return ok_all


def main(argv):
    if not selftest("--selftest" in argv):
        print("CHECK FAILED: the parity check misses its own negative controls (above), so a pass would mean nothing.")
        return 1
    if "--selftest" in argv:
        print("selftest: PASS")
        return 0
    gates = check_all_gates()
    ci_text = CI.read_text(encoding="utf-8")
    if "--list" in argv:
        print("check_all:")
        for n, c in gates:
            print(f"  {n:28} {c}")
        print("ci.yml:")
        for c, checked in ci_invocations(ci_text):
            print(f"  {c}" + ("" if checked else "   <- NO exit check"))
        return 0
    missing_in_ci, missing_here, unchecked = analyse(gates, ci_text)
    for n, c in missing_in_ci:
        print(f"  NOT IN CI     {n}: py {c}")
    for c in missing_here:
        print(f"  NOT IN check_all   py {c}")
    for c in unchecked:
        print(f"  NO EXIT CHECK      py {c}")
    if missing_in_ci or missing_here or unchecked:
        print(f"CHECK FAILED: {len(missing_in_ci)} gate(s) missing from ci.yml, {len(missing_here)} CI gate(s) "
              f"missing from check_all.GATES, {len(unchecked)} CI gate line(s) with no exit check. Add each gate to "
              "BOTH lists (same arguments), each CI line followed at once by "
              "`if ($LASTEXITCODE -ne 0) { throw \"...\" }`.")
        return 1
    print(f"CHECK OK: all {len(gates)} check_all gates run in ci.yml, each with its exit check, and CI runs no "
          "pre-build gate check_all lacks.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
