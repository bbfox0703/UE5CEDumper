#!/usr/bin/env python3
"""Run every doc/source gate CI runs, in CI's order, from one command.

    py tools/check_all.py            # the 12 pre-build gates
    py tools/check_all.py --quick    # skip the two that need a Ghidra-pattern re-extract
    py tools/check_all.py --list     # print the sequence and exit

WHY THIS EXISTS. The gates lived only as inline `pwsh` lines inside
`.github/workflows/ci.yml`, each with its own `throw`. There are **twelve** of them
before the build (plus a thirteenth over the built artifacts, which needs a build and
is therefore not run here) -- and a session that knows about "the four doc checks"
runs those, sees green, opens a PR and reddens CI on a gate it has never heard of.
Measured 2026-08-22: an entire day's work was committed against 4 of the 12.

⚠ ORDER MATTERS. `aob_specificity` reads the TSV that `extract_patterns --check`
writes, so it cannot run first. The sequence below is CI's, not alphabetical.

⚠ This deliberately does NOT build anything. `check_proxy_exports --artifacts`
inspects the BUILT proxy DLLs -- what the game's loader actually sees -- and is left
to CI (or to a local `build.ps1 -Target DLL` followed by
`py tools/check_proxy_exports.py --artifacts --list`).

The failure text under each gate is CI's own, copied so a local failure reads the
same as the one that will appear on the PR.
"""
from __future__ import annotations

import argparse
import os
import subprocess
import sys
import time

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TSV = os.path.join("out", "sweep", "patterns.tsv")

# (name, argv, why-it-failed text from ci.yml, needs_pattern_extract)
GATES = [
    ("extract_patterns --check",
     ["tools/ghidra/extract_patterns.py", "dll/src/Himmel.h", TSV, "--check"],
     "the Himmel.h pattern tables disagree with the counts CLAUDE.md/docs assert, "
     "or a table is unsorted / carries a dead constant", True),

    ("blocktest",
     ["tools/ghidra/blocktest.py"],
     "the AOB code-block library (tools/ghidra/blocks/blocks.json) no longer matches "
     "what the extractor produces", False),

    ("check_live_verification",
     ["tools/check_live_verification.py"],
     "a roadmap 'In-game verification pending' caveat is untagged or untracked in "
     "todo.md's register", False),

    ("aob_specificity --check",
     ["tools/pe/aob_specificity.py", "--tsv", TSV,
      "--baseline", "tools/pe/aob-specificity-baseline.tsv", "--check"],
     "an AOB pattern's n-gram specificity moved against the golden baseline", True),

    ("pe_scan_selftest",
     ["tools/ghidra/pe_scan_selftest.py"],
     "the PE scanner self-test failed", False),

    ("check_axaml_strings",
     ["tools/check_axaml_strings.py"],
     "an en.axaml key is referenced-but-undefined (a load-time crash) or "
     "defined-but-unreferenced (dead). Run 'py tools/check_axaml_strings.py --list'", False),

    ("check_mailbox_contract",
     ["tools/check_mailbox_contract.py"],
     "the CE Lua mailbox contract changed without a version bump, or "
     "CeMailboxLayout.ContractVersion disagrees with Mimic::MAILBOX_CONTRACT", False),

    ("check_derived_counts",
     ["tools/check_derived_counts.py"],
     "a doc states a count that disagrees with the tree, or a registered claim was "
     "reworded so the check silently stopped covering it", False),

    ("check_ue_sample_values",
     ["tools/check_ue_sample_values.py"],
     "tools/ue-sample/README.md and the DumperTest sources disagree. "
     "Run 'py tools/check_ue_sample_values.py --list'", False),

    ("check_audit_register",
     ["tools/check_audit_register.py"],
     "a finding docs/dev-log.md reports fixed has no tick on its row in the audit doc. "
     "Marking the grouped queue row is not enough", False),

    ("check_no_local_paths",
     ["tools/check_no_local_paths.py"],
     "a tracked file carries a concrete user home path. Use %LOCALAPPDATA% / "
     "%APPDATA% / %USERPROFILE%, or a placeholder", False),

    ("check_proxy_exports",
     ["tools/check_proxy_exports.py"],
     "a dll/src/Proxy*.def omits a real export or fails to pin its real ordinal. "
     "A .def line exports nothing on its own; it needs an implementation in the "
     "matching Lugner_*.cpp/.asm", False),
]


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--quick", action="store_true",
                    help="skip the two gates that re-extract the Ghidra pattern TSV")
    ap.add_argument("--list", action="store_true", help="print the sequence and exit")
    args = ap.parse_args()

    if args.list:
        for i, (name, argv, _, slow) in enumerate(GATES, 1):
            print("%2d. %-28s %s%s" % (i, name, "py " + " ".join(argv),
                                       "   [--quick skips]" if slow else ""))
        return 0

    os.makedirs(os.path.join(ROOT, "out", "sweep"), exist_ok=True)
    failed, skipped = [], []
    t0 = time.time()

    for name, argv, why, slow in GATES:
        if args.quick and slow:
            skipped.append(name)
            print("  SKIP  %s" % name)
            continue
        t = time.time()
        r = subprocess.run([sys.executable] + argv, cwd=ROOT,
                           capture_output=True, text=True,
                           encoding="utf-8", errors="replace")
        dt = time.time() - t
        tail = [ln for ln in (r.stdout or "").splitlines() if ln.strip()][-1:] or [""]
        if r.returncode == 0:
            print("  ok    %-28s %5.1fs  %s" % (name, dt, tail[0][:96]))
        else:
            failed.append((name, why, r))
            print("  FAIL  %-28s %5.1fs  exit=%d" % (name, dt, r.returncode))

    print()
    print("%d gate(s) run, %d failed, %d skipped, %.1fs total"
          % (len(GATES) - len(skipped), len(failed), len(skipped), time.time() - t0))

    for name, why, r in failed:
        print()
        print("=" * 78)
        print("FAILED: %s" % name)
        print("CI would say: %s" % why)
        print("-" * 78)
        out = ((r.stdout or "") + (r.stderr or "")).strip()
        print(out[-3000:] if out else "<no output>")

    if not failed:
        print()
        print("⚠ NOT covered here: check_proxy_exports --artifacts, which inspects the BUILT")
        print("  proxy DLLs (what the game's loader sees). Run it after a -Target DLL build:")
        print("      py tools/check_proxy_exports.py --artifacts --list")
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
