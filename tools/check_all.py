#!/usr/bin/env python3
"""Run every doc/source gate CI runs, in CI's order, from one command.

    py tools/check_all.py            # every pre-build gate
    py tools/check_all.py --quick    # skip the two that need a Ghidra-pattern re-extract
    py tools/check_all.py --list     # print the sequence and exit
    py tools/check_all.py --selftest # the result classifier's controls only (every run does them first)

WHY THIS EXISTS. The gates lived only as inline `pwsh` lines inside
`.github/workflows/ci.yml`, each with its own `throw` -- and a session that knows
about "the four doc checks" runs those, sees green, opens a PR and reddens CI on a
gate it has never heard of. Measured 2026-08-22: an entire day's work was committed
against 4 of the then-12.

⛔ DERIVE THE COUNT, never type it. This docstring said "the 12 pre-build gates" while
the list held 13, and `CONTRIBUTING.md` said "All 13 gates" for the same reason: a
number in prose does not move when someone appends a tuple. `--list` prints it, and the
run's own final line reports "N gate(s) run".

⚠ ADDING A GATE MEANS ADDING IT TO BOTH LISTS -- enforced since 2026-09-25 by the
`check_ci_gate_parity` gate, which also requires each CI line's exit check. This file
and `ci.yml` had silently drifted twice: `check_evidence_index` and
`check_inert_trimming` until 2026-09-06, then nine gates appended after that date
([CI-GATE-DRIFT-2026-09-25]). CI additionally runs `check_proxy_exports --artifacts`
post-build, which needs a build and is deliberately not here. Compare them with:
    py tools/check_ci_gate_parity.py --list

⚠ A GATE THAT CANNOT RUN HERE says so on its LAST line ("SKIPPED: ...", "SKIP: ...", or
"<gate>: SKIP -- ...") and exits 0; it is counted as skipped, not run (classify below).

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
import re
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

    # [VND583-16] the CRC oracle must MERGE, never overwrite: its rows outlive the Editors they came from
    ("crc_oracle_selftest",
     ["tools/verify/crc_authority_survey.py", "--selftest"],
     "the CRC oracle merge self-test failed", False),

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

    ("check_processevent_slots",
     ["tools/check_processevent_slots.py"],
     "Grimoire.h's ProcessEvent vtable slot table disagrees with the vendored "
     "RE-UE4SS templates it was transcribed from. It is DERIVED data -- run "
     "'py tools/check_processevent_slots.py --list'. ⚠ The runtime pattern scan is "
     "still primary and a per-BUILD difference is not a bug; this only pins the "
     "fallback table against its own source", False),

    ("check_property_family",
     ["tools/check_property_family.py"],
     "somebody assigned a DynOff sizeof(FProperty) family member directly instead of "
     "going through ApplyPropertyFamily. A split family does NOT crash -- struct reads "
     "stay correct while TArray element descriptors and every enum name read 8 bytes "
     "off -- which is why audit #5 G12 found it late and why its own fix missed a third "
     "writer. Run 'py tools/check_property_family.py --list'", False),

    ("check_ce_untick_placement",
     ["tools/check_ce_untick_placement.py"],
     "a CE Lua bail-out unticks its record with an IMMEDIATE 'memrec.Active = false' "
     "inside [ENABLE], which CE silently ignores -- setActive early-exits while "
     "autoassemble is still running, so the row ends up TICKED over a cheat that "
     "applied nothing. Use the deferred form (CeLuaHygiene.AppendDeferredUntick). "
     "Run 'py tools/check_ce_untick_placement.py --list' for every site and its "
     "verdict, or '--selftest' for the negative controls", False),

    ("check_badge_prime_symmetry",
     ["tools/check_badge_prime_symmetry.py"],
     "a gameplay card's badge is reset to Unknown on disconnect and primed by NOTHING on "
     "connect, so after a UI reconnect it reads 'never asked' over state the DLL is still "
     "holding -- a live Fly or Move Speed hold with no sign the game is modified. Add the "
     "read to TeleportViewModel.PrimeHeldBadgesAsync, quietly (no IsBusy, no StatusText). "
     "It counts Apply*State SYMBOLS, so the two time lanes are one entry. "
     "Run 'py tools/check_badge_prime_symmetry.py --list' or '--selftest'", False),

    ("check_ce_idlewait_scope",
     ["tools/check_ce_idlewait_scope.py"],
     "a CE mailbox emitter's bounded wait-for-IDLE is conditioned on the "
     "enable/disable discriminator, so only ONE of the two blocks gets it -- while the "
     "cmd store below is emitted for both. The unguarded block then writes operands, "
     "clears status and stores cmd while another command may still be in flight "
     "(the AA10 hazard). Emit the call unconditionally and pass "
     "'enable ? MailboxTimeout.UntickAndReturn : MailboxTimeout.SilentReturn'. "
     "It deliberately does NOT flag a guard that is not the enable discriminator "
     "(if (dll), if (verifyReturn)) -- that split is what keeps the check baseline-free. "
     "Run 'py tools/check_ce_idlewait_scope.py --list' or '--selftest'", False),

    ("check_clipboard_delivery",
     ["tools/check_clipboard_delivery.py"],
     "a DELIVERY clipboard copy (the payload is a generated CE script / memory-record "
     "XML) discards CopyToClipboardAsync's result, so the status can claim a paste-able "
     "script the clipboard never took -- and the user then pastes the PREVIOUS script "
     "into Cheat Engine and runs it. Route it through Helpers/ClipboardDelivery. "
     "Convenience copies (an address, a name) are deliberately NOT flagged: that split "
     "is what keeps this check baseline-free. "
     "Run 'py tools/check_clipboard_delivery.py --list' or '--selftest'", False),

    ("check_json_default_ignore",
     ["tools/check_json_default_ignore.py"],
     "a value-type property whose initializer differs from default(T) is serialized under "
     "WhenWritingDefault, so a user choosing the type-default (0 / false) has the key OMITTED "
     "and the next load silently re-runs the initializer -- [W1-QUOTA-UNLIMITED] deleted "
     "snapshots that way. Drop WhenWritingDefault from the context (the spec rule), or mark a "
     "property that can never take default(T) [JsonIgnore(Condition = JsonIgnoreCondition.Never)]. "
     "Run 'py tools/check_json_default_ignore.py --list' or '--selftest'", False),

    ("check_session_gate",
     ["tools/check_session_gate.py"],
     "a command hands a STORED snapshot's address (Live Walker / Locate / Copy) to the running game "
     "with nothing enabling it that reaches the game session (GameSessionId / _currentSessionId), so a "
     "previous launch's address is walked in this one. Gate it the way Snapshot Diff and SPC do "
     "([W1-PIVOT-SESSION]). Run 'py tools/check_session_gate.py --list' or '--selftest'", False),

    # [CI-GATE-DRIFT-2026-09-25] Enforces what the docstring above used to only ask for: every gate here also runs in
    # ci.yml, with the same arguments, and CI runs no pre-build gate this list lacks. Nine had drifted out again.
    ("check_ci_gate_parity",
     ["tools/check_ci_gate_parity.py"],
     "a gate runs in tools/check_all.py but not in .github/workflows/ci.yml, or the other way round -- add it to "
     "BOTH lists with the same arguments. Run 'py tools/check_ci_gate_parity.py --list'", False),

    # [PATH-SHAPE-2026-09-25] (skeptic T11) The Lua suites -- the only behavioural tests of the .CT and of the Lua the
    # UI emits -- on CE's own VM. SKIPS (exit 0, counted as skipped) where out/ce_lua53 is not built, e.g. CI: building it needs a local
    # Cheat Engine, which a gate must not reach for. Machine-bound suites are excluded (the script says which).
    ("check_lua_suites",
     ["tools/check_lua_suites.py"],
     "a scripts/tests/*.lua suite failed on Cheat Engine's own Lua VM -- the .CT or an emitted CE script regressed. "
     "Run 'py tools/check_lua_suites.py' (and --list for what runs and what is excluded)", False),

    # The local-LLM helper's pure logic -- above all the game guard, whose regression would leave a ~14 GB model
    # on the GPU while a commercial game runs. No network and no processes, so it runs identically in CI.
    ("ollama_local --selftest",
     ["tools/llm/ollama_local.py", "--selftest"],
     "tools/llm/ollama_local.py's controls failed -- the commercial-game / DumperTest classification, the model "
     "tag match, the chunker or the settings.local.json hook merge regressed. Run "
     "'py tools/llm/ollama_local.py --selftest' for the failing control", False),
]


def last_line(stdout: str) -> str:
    return ([ln for ln in (stdout or "").splitlines() if ln.strip()][-1:] or [""])[0]


def classify(name: str, returncode: int, stdout: str) -> str:
    """ok / skip / warn / fail for one gate's result."""
    if returncode == 0:
        # "SKIPPED: ..." / "SKIP: ...", or "<gate>: SKIP -- ..." (check_processevent_slots). Upper case only: a
        # summary's "skipped 0" or prose is not a skip.
        return "skip" if re.match(r"\s*(?:[\w.-]+:\s*)?SKIP(?:PED)?\b", last_line(stdout)) else "ok"
    return "warn" if name in ADVISORY else "fail"


# (second review, LUAGATE-SKIP-HIDDEN) A gate that cannot run here says so and exits 0 (check_lua_suites without its
# CE host: "SKIPPED: ..."; check_ue_sample_values without its sample: "SKIP: ..."). Counting that as a pass made the
# summary say "25 gate(s) run, 0 skipped" on a machine where the Lua suites never ran.
_CLASSIFY_SELFTEST = [
    ("a pass", ("g", 0, "  ok  x\nCHECK OK: all fine\n"), "ok"),
    ("a failure", ("g", 1, "CHECK FAILED\n"), "fail"),
    ("a gate that could not run and said SKIPPED", ("g", 0, "SKIPPED: the host is not built here.\n"), "skip"),
    ("the SKIP: spelling too", ("g", 0, "SKIP: tools/ue-sample not present\n\n"), "skip"),
    ("a skip line that is not the LAST line is not a skip", ("g", 0, "SKIPPED: one part\nCHECK OK: the rest\n"), "ok"),
    ("a failure that printed SKIPPED is still a failure", ("g", 2, "SKIPPED: x\n"), "fail"),
    # (third review, CHECKALL-PESLOTS-SKIP-COUNTED-OK) check_processevent_slots names itself first -- and skips on
    # every fresh clone and on CI, where vendor/RE-UE4SS/ is gitignored.
    ("a gate that names itself before SKIP",
     ("g", 0, "check_processevent_slots: SKIP -- no vendored templates under vendor\\RE-UE4SS\n"), "skip"),
    ("a summary that counts skipped rows is not a skip", ("g", 0, "blocks 340   ok 340   FAIL 0   skipped 0\n"), "ok"),
    ("a lower-case 'skip' in prose is not a skip", ("g", 0, "check_x: skip list empty, all checked\n"), "ok"),
    # (fourth review, R4-CLASSIFY-NEGCTRL-CASE-ONLY) Upper case too: SKIP must START the line (after an optional
    # '<gate>:'), and be a whole word -- re.search, a dropped \b or a '.*' prefix would take these.
    ("an upper-case SKIPPED count later in a summary is not a skip", ("g", 0, "blocks 3   ok 3   SKIPPED 0\n"), "ok"),
    ("SKIPPING is not SKIP", ("g", 0, "check_x: SKIPPING nothing, all checked\n"), "ok"),
]


def selftest(verbose: bool) -> bool:
    ok_all = True
    for what, args_, want in _CLASSIFY_SELFTEST:
        got = classify(*args_)
        ok_all &= got == want
        if verbose or got != want:
            print("  %s  %s%s" % ("PASS" if got == want else "FAIL", what,
                                  "" if got == want else "   want %s, got %s" % (want, got)))
    return ok_all


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--quick", action="store_true",
                    help="skip the two gates that re-extract the Ghidra pattern TSV")
    ap.add_argument("--list", action="store_true", help="print the sequence and exit")
    ap.add_argument("--selftest", action="store_true", help="run the result classifier's controls and exit")
    args = ap.parse_args()

    if not selftest(args.selftest):
        print("check_all: its result classifier misses its own controls (above) -- the summary would lie.")
        return 1
    if args.selftest:
        print("selftest: PASS")
        return 0

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
        verdict = classify(name, r.returncode, r.stdout)
        if verdict == "ok":
            print("  ok    %-28s %5.1fs  %s" % (name, dt, last_line(r.stdout)[:96]))
        elif verdict == "skip":
            skipped.append(name)
            print("  SKIP  %-28s %5.1fs  %s" % (name, dt, last_line(r.stdout).strip()[:96]))
        elif verdict == "warn":
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
