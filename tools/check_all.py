#!/usr/bin/env python3
"""Run every doc/source gate CI runs, in CI's order, from one command.

    py tools/check_all.py            # every pre-build gate
    py tools/check_all.py --quick    # skip the two that need a Ghidra-pattern re-extract
    py tools/check_all.py --list     # print the sequence and exit

WHY THIS EXISTS. The gates lived only as inline `pwsh` lines inside
`.github/workflows/ci.yml`, each with its own `throw` -- and a session that knows
about "the four doc checks" runs those, sees green, opens a PR and reddens CI on a
gate it has never heard of. Measured 2026-08-22: an entire day's work was committed
against 4 of the then-12.

⛔ DERIVE THE COUNT, never type it. This docstring said "the 12 pre-build gates" while
the list held 13, and `CONTRIBUTING.md` said "All 13 gates" for the same reason: a
number in prose does not move when someone appends a tuple. `--list` prints it, and the
run's own final line reports "N gate(s) run".

⚠ `check_all.py` and `ci.yml` HAVE DRIFTED and this file cannot see it: as of
2026-09-06 `check_evidence_index` and `check_inert_trimming` run here but are absent
from `ci.yml`. Adding a gate means adding it to BOTH lists.

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

# Gates whose failure is REPORTED but does not fail the run.
#
# The rule this encodes: a red build must mean "the software is wrong", never
# "someone forgot a tick". Every blocking gate here fails in the SAME commit as the
# change that broke it, so its fix is local and the noise is ~zero. An advisory gate
# is one that fails LATER, decoupled from the change -- which is how a team learns to
# read red as "probably just docs" and then misses a real one.
#
# check_audit_register was here, advisory, until 2026-09-03. RETIRED as a gate (the
# SCRIPT stays -- `--list` is still the documented way to derive the register count,
# and handover/todo/working-lessons all say "never hand-tally"). It existed to keep
# audit #5's open count honest WHILE a fix programme ran; that programme is spent
# (0 HIGH, 0 MED, 3 open of 297, all three open by decision). An advisory gate is the
# worst of both worlds -- it cannot fail the build, so it is a ::warning:: nobody
# reads, while still costing a run every time. Re-open an audit round => re-add it,
# BLOCKING this time.
ADVISORY = set()

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
     "the verification register", False),

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

    ("check_no_local_paths",
     ["tools/check_no_local_paths.py"],
     "a tracked file carries a concrete user home path. Use %LOCALAPPDATA% / "
     "%APPDATA% / %USERPROFILE%, or a placeholder", False),

    ("check_md_links",
     ["tools/check_md_links.py"],
     "a relative link in a tracked .md file does not resolve. Run "
     "'py tools/check_md_links.py --list'; --fix previews the provable rewrites. "
     "NOTE it checks the FILE, never the :LINE -- line drift is invisible to it", False),

    ("check_evidence_index",
     ["tools/check_evidence_index.py"],
     "docs/evidence/ drifted from the claims it serves -- an artifact whose claim tag no "
     "longer appears anywhere else under docs/ (an ORPHAN), or a directory missing from the "
     "index. Age is never the reason to delete evidence; orphanhood is", False),

    ("check_inert_trimming",
     ["tools/check_inert_trimming.py"],
     "a TextBlock asks for TextTrimming inside a horizontal StackPanel, where it can "
     "never fire, and no ancestor carries a ToolTip.Tip -- so the clipped tail is "
     "unreadable. This defect class has shipped four times", False),

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
    failed, skipped, advisory = [], [], []
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
        elif name in ADVISORY:
            advisory.append((name, why, r))
            print("  warn  %-28s %5.1fs  exit=%d  (advisory -- does not fail the run)"
                  % (name, dt, r.returncode))
        else:
            failed.append((name, why, r))
            print("  FAIL  %-28s %5.1fs  exit=%d" % (name, dt, r.returncode))

    print()
    print("%d gate(s) run, %d failed, %d advisory, %d skipped, %.1fs total"
          % (len(GATES) - len(skipped), len(failed), len(advisory), len(skipped),
             time.time() - t0))

    for name, why, r in advisory:
        print()
        print("-" * 78)
        print("ADVISORY (not a build failure): %s" % name)
        print("what drifted: %s" % why)
        out = ((r.stdout or "") + (r.stderr or "")).strip()
        print(out[-1500:] if out else "<no output>")

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
