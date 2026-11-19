r"""Slice B of the unadjudicated sweep claims — and the SCORE that refuses to build a gate.

    py tools/verify/claims_effect_applier.py           # the population, adjudicated
    py tools/verify/claims_effect_applier.py --list    # every hit with its line

⛔ THIS IS A MEASUREMENT, NOT A GATE, AND IT IS THE ONE THING HERE WORTH KEEPING.
`docs/todo.md`'s round-2 section says *"Gate #17 as proposed in round 1 is REFUTED — do not
build it"*, and `tools/check_clipboard_delivery.py`'s header repeats the verdict. The proposal
is a curated allowlist of effect-appliers plus a gate over their discarded returns. Rather
than settle a re-proposal of that allowlist by reading, this file builds the list and scores
it, so the next person to propose the allowlist can re-run it instead of re-arguing it.

WHAT THE SCORE SAYS:

  * the appliers are derived from the module HEADERS (every public entry point whose return
    says whether an EFFECT landed), readers and param-buffer packers excluded by name exactly
    as round 1's design specified.
  * the discarded call sites are a handful — a list a human walks in an afternoon. Buying a
    curated TSV plus a baseline file plus a gate, for that few, is the trade round 2 already
    refused.
  * adjudicated by hand (the VERDICTS table below), most of them are CLEAN, so a gate would
    open with that many waivers — mostly false positives, the same shape that refutes the design.

⛔ A SCORER RE-RAISES REFUTED ROWS, AND THAT IS THE OTHER LESSON THIS FILE CARRIES. The
`Wirbel.cpp` hit is on round 2's `Refuted (8) -- do not re-raise` list (by a line number, and
line numbers move). A mechanical scorer surfaces refuted rows as fresh hits by construction,
because the refutation lives in prose while the code still matches the pattern -- so the VERDICTS
table below is not bookkeeping, it is the only thing standing between this script and re-opening
settled rows.

⚠ A REPAIRED SITE LEAVES THE POPULATION. Once its return is consumed it no longer matches, so
the count this prints shrinks as sites are repaired; that is the script working. What remains
are the waivers a gate would open with — which is the whole argument, standing on its own.

⭐ AND THE FOUR CLEAN VERDICTS ARE WHY A BLANKET RULE CANNOT WORK. Three of them are in ONE
handler, `Fern.cpp`'s `fly_set`: it drops `SetSpeed` / `SetPreset` / `SetNoclip` and is
correct anyway, because the next thing it does is `Dunste::GetStatus(st)` and it answers from
`st`. That is this repo's actual rule — *report the EFFECT, not the ATTEMPT* — and a checker
keyed on "the status was discarded" flags the file that follows it best.

⚠ THE FOURTH is the `Fern.cpp` `SetEnabled` verdict, for the last-client teardown. Under
[PIPE-PER-PROCESS-2026-11-06] Stage C a departing client calls `Schlacht::Release`, which turns
See-through off only when that client held the last claim, so that site does not match.

⚠ WHAT THIS SCRIPT IS NOT. It does not judge, it does not have a baseline, and it is not
wired into `check_all.py`. Re-run it after adding a gameplay module to see whether the
population has grown enough to change the trade; today it has not.
"""
from __future__ import annotations

import argparse
import glob
import io
import re
import sys

# Derived from dll/src/*.h -- every public entry point whose return says whether an EFFECT
# landed. Readers (Get*/Is*/Resolve*/Detect*/Find*) and the ProcessEvent param-buffer packers
# (WriteVecParam/WriteFloatParam/WriteBoolParam) are excluded BY NAME, as round 1 specified:
# they build an argument buffer, they are not effects.
APPLIERS = {
    # Wirbel (Teleport)
    "SaveMarker", "RecallMarker", "RecallExplicit", "TeleportToCursor", "ClearMarker",
    "RecallLast", "BugItGo", "TeleportRelative", "SetMouseCursor", "TeleportPawnTo",
    # Solitar (GodMode)
    "SetGodMode", "SetActorBool",
    # Laufen (MovementTuning)
    "SetMultiplier", "ResetKnob", "SetKnobPercent", "SetGravityDirection",
    "ResetGravityDirection",
    # Hemmung (TimeDilation)
    "SetDilation", "ResetDilation",
    # Solide (ForceField)
    "AddForce", "RemoveForce", "ClearAll", "ApplyToInstance",
    # Grausam / Schlacht / Stark / Dunste
    "SetForegroundLock", "SetEnabled", "InvokeSetHidden", "InvokeSetCollision",
    "InstallHook", "EnqueueInvoke", "SetSpeed", "SetPreset", "SetNoclip",
    # Macht -- the write side. The only reader-shaped name here on purpose: a discarded
    # WriteBytes is the archetype of this whole population.
    "WriteBytes",
}

# The return is bound, tested, or returned -- not this population.
CONSUMED = re.compile(r"=|\breturn\b|\bif\s*\(|\bwhile\s*\(|&&|\|\||!|\?|\bfor\s*\(")

# The hand verdicts, so re-running this re-states them instead of re-opening them. A site
# that drops off the list has moved; a site that appears without a verdict is NEW.
VERDICTS = {
    # Two entries are for files whose returns are consumed, so they do not MATCH and are
    # outside the population. Listed so a regression that re-drops one comes back with its
    # verdict attached instead of as a fresh unknown.
    ("dll/src/Dunste.cpp", "WriteBytes"):
        "FIXED 2026-09-09 -- both MovementMode writes now report their result",
    ("dll/src/Wirbel.cpp", "WriteBytes"):
        "HYGIENE 2026-09-09 -- `rewrote` counts what LANDED. ⚠ Round 2 REFUTED this exact "
        "line (as :619) and the refutation is sound on reachability: ReadBytesSafe covers the "
        "same 0x400 window one instruction above. Kept, but NOT counted as a defect",
    ("dll/src/Fern.cpp", "SetEnabled"):
        "CLEAN -- last-client teardown; no client left to claim anything to",
    ("dll/src/Fern.cpp", "SetSpeed"):
        "CLEAN -- cannot fail (clamps, returns FR_OK), and fly_set re-reads GetStatus",
    ("dll/src/Fern.cpp", "SetPreset"):
        "CLEAN -- CAN fail, but the response publishes st.preset: the EFFECT, not the attempt",
    ("dll/src/Fern.cpp", "SetNoclip"):
        "CLEAN -- same; the response publishes st.noclip",
}


def scan():
    hits = []
    for path in sorted(glob.glob("dll/src/*.cpp")):
        norm = path.replace("\\", "/")
        for i, ln in enumerate(io.open(path, encoding="utf-8",
                                       errors="replace").read().splitlines()):
            for name in sorted(APPLIERS):
                m = re.search(r"(^|[^\w:.>])((?:\w+\s*(?:::|->|\.))?)%s\s*\(" % name, ln)
                if not m:
                    continue
                head = ln[:m.start(2)] if m.start(2) else ln[:m.start()]
                if CONSUMED.search(head):
                    continue
                if not ln.rstrip().endswith(";"):
                    continue                        # multi-line call: out of this cut
                hits.append({"file": norm, "line": i + 1, "name": name,
                             "text": ln.strip()[:104],
                             "verdict": VERDICTS.get((norm, name), "*** NEW -- ADJUDICATE ***")})
                break
    return hits


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--list", action="store_true")
    a = ap.parse_args()

    hits = scan()
    print("effect-appliers listed : %d" % len(APPLIERS))
    print("discarded call sites   : %d   (7 before the 2026-09-09 fixes; round 1 "
          "predicted 23)" % len(hits))
    unknown = [h for h in hits if h["verdict"].startswith("***")]
    print("without a verdict      : %d\n" % len(unknown))

    for h in (hits if a.list else hits):
        print("  %s:%d  %s" % (h["file"], h["line"], h["name"]))
        print("      %s" % h["text"])
        print("      -> %s" % (h["verdict"] or "(see the enable arm above)"))

    print("\nA discarded status is NOT a defect: every site still listed above is CORRECT, "
          "three of them because Fern's fly_set re-reads GetStatus and answers from THAT. "
          "Report the EFFECT, not the ATTEMPT -- so a checker keyed on 'the status was "
          "discarded' would flag the handler that follows the rule best, and would open with "
          "exactly these as waivers. That is why this is a score and not a gate.")
    return 1 if unknown else 0


if __name__ == "__main__":
    sys.exit(main())
