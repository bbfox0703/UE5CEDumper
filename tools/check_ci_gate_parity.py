#!/usr/bin/env python3
"""Every gate tools/check_all.py runs must ALSO run in .github/workflows/ci.yml, and every gate CI runs pre-build must
be in check_all -- the two lists are one list kept in two places.

    py tools/check_ci_gate_parity.py          # exit 1 and name each gate missing from the other list
    py tools/check_ci_gate_parity.py --list   # print both sequences side by side

WHY ([CI-GATE-DRIFT-2026-09-25]). check_all.py's own docstring says "ADDING A GATE MEANS ADDING IT TO BOTH LISTS, and
nothing enforces that" -- and the drift it describes (two gates absent from CI for months, closed 2026-09-06) had
recurred: NINE gates appended to check_all after that date never reached ci.yml, so a PR could break any of them and
redden nothing. This is the enforcement.

HOW. The gates are read from check_all.GATES (imported, not parsed). A gate "is in CI" when ci.yml runs its script
with the same arguments on an `& $py ...` line. The one post-build CI step (check_proxy_exports --artifacts, which
needs the built DLLs) is expected in CI only. Comments in ci.yml do not count.
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


def ci_invocations():
    runs = []
    for line in CI.read_text(encoding="utf-8").splitlines():
        s = line.strip()
        if s.startswith("#"):
            continue
        m = re.match(r"&\s*\$py\s+(tools/\S+\.py(?:\s+[^;|]*?)?)\s*$", s)
        if m:
            runs.append(re.sub(r"\s+", " ", m.group(1).replace("\\", "/")).strip())
    return runs


def normalise(cmd: str) -> str:
    # check_all passes the TSV as out\sweep\patterns.tsv (os.path.join); CI spells it out/sweep/patterns.tsv.
    return cmd.replace("\\", "/")


def main(argv):
    gates = check_all_gates()
    ci = [normalise(c) for c in ci_invocations()]
    if "--list" in argv:
        print("check_all:")
        for n, c in gates:
            print(f"  {n:28} {c}")
        print("ci.yml:")
        for c in ci:
            print(f"  {c}")
        return 0
    missing_in_ci = [(n, c) for n, c in gates if normalise(c) not in ci]
    missing_here = [c for c in ci if c not in {normalise(g) for _, g in gates} and c not in CI_ONLY]
    for n, c in missing_in_ci:
        print(f"  NOT IN CI     {n}: py {c}")
    for c in missing_here:
        print(f"  NOT IN check_all   py {c}")
    if missing_in_ci or missing_here:
        print(f"CHECK FAILED: {len(missing_in_ci)} gate(s) missing from ci.yml, {len(missing_here)} CI gate(s) "
              "missing from check_all.GATES. Add each to BOTH lists (same arguments).")
        return 1
    print(f"CHECK OK: all {len(gates)} check_all gates run in ci.yml, and CI runs no pre-build gate check_all lacks.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
