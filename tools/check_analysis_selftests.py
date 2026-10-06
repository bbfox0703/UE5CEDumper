#!/usr/bin/env python3
"""Run the built-in self-tests of the scripts under scripts/analysis/.

    py tools/check_analysis_selftests.py          # exit 1 and print each failing script's output
    py tools/check_analysis_selftests.py --list   # which scripts were found and run

WHY. The analysis scripts (diff_dumps.py, walk_payload_audit.py, ...) are run by hand, against dumps a
user exports, so nothing ran their synthetic-fixture tests: a change could break what a patch diff
reports and every gate would stay green. Each script that offers `--self-test` is run with it here.

WHICH SCRIPTS. Every `scripts/analysis/*.py` whose source declares a `--self-test` argument. Found by
reading the files, not from a list, so a new script with a self-test is run without editing this gate.
Finding NONE is a failure: it means the folder or the flag was renamed, not that all is well.
"""
from __future__ import annotations

import pathlib
import subprocess
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ROOT = pathlib.Path(__file__).resolve().parents[1]
FOLDER = ROOT / "scripts" / "analysis"
FLAG = '"--self-test"'
TIMEOUT_S = 120


def scripts_with_selftest() -> list[pathlib.Path]:
    return sorted(p for p in FOLDER.glob("*.py") if FLAG in p.read_text(encoding="utf-8", errors="replace"))


def main() -> int:
    found = scripts_with_selftest()
    if not found:
        print(f"check_analysis_selftests FAILED: no script in {FOLDER.relative_to(ROOT)} declares {FLAG}")
        return 1
    if "--list" in sys.argv:
        for p in found:
            print("  " + p.relative_to(ROOT).as_posix())
    failed = []
    for p in found:
        try:
            r = subprocess.run([sys.executable, str(p), "--self-test"], cwd=ROOT, capture_output=True,
                               text=True, encoding="utf-8", errors="replace", timeout=TIMEOUT_S)
            ok, out = r.returncode == 0, (r.stdout + r.stderr).strip()
        except subprocess.TimeoutExpired:
            ok, out = False, f"timed out after {TIMEOUT_S} s"
        if not ok:
            failed.append((p, out))
    if failed:
        print(f"check_analysis_selftests FAILED: {len(failed)} of {len(found)} self-test(s)\n")
        for p, out in failed:
            print(f"--- {p.relative_to(ROOT).as_posix()} --self-test")
            print("\n".join("  " + line for line in out.splitlines()[-20:]))
        return 1
    print(f"check_analysis_selftests OK: {len(found)} script(s) passed their self-test")
    return 0


if __name__ == "__main__":
    sys.exit(main())
