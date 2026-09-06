r"""Audit A3 — `UFunction::FunctionFlags`, WITHOUT the UE 4.21 title the row is gated on.

    py tools/verify/a3_funcflags_override.py            # any UE 4.08-4.21 host; OCTOPATH 4.18

THE ROW'S PROBLEM. `1a0a656f` replaced a ladder that read `>= 421 -> 0x98` with a measured table,
`DynOff::FunctionFlagsOffsetFor` (`Grimoire.h`): `4.08-4.21 = 0x88`, `4.22-4.24 = 0x98`,
`4.25+ = 0xB0`, `+8` under CasePreservingName. Exactly **one** producible version changes
behaviour: **4.21**. The register gates the row on Star Wars Jedi: Fallen Order, the corpus's only
4.21 title — which is a GHOST on both machines (folder, no executable) and is an EA-launcher title
needing the EA client to install.

⭐ WHY A 4.18 HOST IS A VALID SUBSTITUTE, and it is not a compromise:

  1. The table puts **4.08-4.21 in ONE band at 0x88**. A 4.18 binary's true FunctionFlags offset
     is therefore *the same* as a 4.21 binary's. For this test they are interchangeable.
  2. `FunctionFlagsOffsetFor` is **not latched**. Both readers (`Ubel.cpp:1432`,
     `Aura.cpp:6041`) recompute it from `g_cachedUEVersion` on every call — unlike the soft/lazy
     envelope, which IS latched and does NOT re-derive on override. So `set_ue_version_override`
     genuinely changes this offset, immediately, with no re-scan.

  Therefore: override a 4.18 host to **421** and you feed the reader *exactly the input that
  changed*. Post-fix the table says 0x88 and the reads stay sane; the pre-fix ladder said 0x98 and
  they would be garbage. Override to **422** and the table says 0x98, which is wrong for this
  binary — reads MUST degrade. That third leg is what makes this a test rather than a tautology:
  without it, "421 looks fine" is equally true of a reader that ignores the version entirely.

⚠ WHAT THIS DOES **NOT** PROVE. That a real retail 4.21 game's layout is 0x88. That comes from the
31 UVTD templates, offline, and no live run on any host can add to it. What this proves is the half
the templates cannot: that the table's value reaches the reader and yields sane parameter data at
version input 421. State it that way in the register; do not upgrade the claim.
"""
import pathlib
import sys

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from pipe_client import PipeClient  # noqa: E402

SAMPLE = 3000


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + "\n")
    sys.stdout.flush()


def survey(c, tag):
    """One pass over the function table; returns a sanity profile."""
    fl = c.request("list_all_functions", limit=SAMPLE, game_only=False, timeout=240).get("functions") or []
    n = len(fl)
    if not n:
        return {"tag": tag, "n": 0}
    zero_flags = sum(1 for f in fl if not f.get("function_flags"))
    # `2 (65413B)` is the register's own example of self-evident garbage. A UFunction with more
    # than 64 parameters or a parameter block over 4 KiB is not a thing UE produces.
    wild_parms = sum(1 for f in fl if (f.get("num_parms") or 0) > 64)
    wild_size = sum(1 for f in fl if (f.get("parms_size") or 0) > 4096)
    sane = sum(1 for f in fl
               if f.get("function_flags") and (f.get("num_parms") or 0) <= 64
               and (f.get("parms_size") or 0) <= 4096)
    return {"tag": tag, "n": n, "zero_flags": zero_flags, "wild_parms": wild_parms,
            "wild_size": wild_size, "sane": sane, "rate": 100.0 * sane / n,
            "sample": [(f.get("func_name"), f.get("num_parms"), f.get("parms_size"),
                        hex(f.get("function_flags") or 0)) for f in fl[:3]]}


def show(p):
    if not p.get("n"):
        say("   %-22s NO FUNCTIONS RETURNED" % p["tag"])
        return
    say("   %-22s n=%-5d sane=%-5d (%.1f%%)  zero_flags=%-5d wild_parms=%-5d wild_size=%d"
        % (p["tag"], p["n"], p["sane"], p["rate"], p["zero_flags"], p["wild_parms"], p["wild_size"]))
    for s in p["sample"]:
        say("        %-28s num_parms=%-6s parms_size=%-8s flags=%s" % s)


def main():
    fails, notes = [], []
    with PipeClient() as c:
        say("build answering the pipe: %s" % c.assert_build())
        p = c.ensure_scanned()
        native = p.get("ue_version")
        say("native UE %s   objects %s   module %s\n"
            % (native, p.get("object_count"), p.get("module_name")))
        if not (408 <= (native or 0) <= 421):
            raise SystemExit("a3: host reports UE %r; this substitute needs a UE 4.08-4.21 host, "
                             "because that is the band that shares 4.21's 0x88 offset" % native)

        say("A/B over set_ue_version_override (the offset is recomputed per call, never latched):")
        base = survey(c, "native %d" % native)
        show(base)

        c.request("set_ue_version_override", version=421, timeout=60)
        at421 = survey(c, "override 421")
        show(at421)

        c.request("set_ue_version_override", version=422, timeout=60)
        at422 = survey(c, "override 422")
        show(at422)

        # ⛔ IT TAKES BOTH CALLS, IN THIS ORDER, AND NEITHER ONE ALONE IS ENOUGH.
        #   * setting the native version restores the IN-PROCESS reads, but writes
        #     `ueVersionUserOverride` into UE5CEDumper.{Machine}.json -- and `Genau.cpp:4946` checks
        #     that field BEFORE the rev-stamped detection cache and is NOT invalidated by a
        #     `kVersionDetectLogicRev` bump. The title would then take the override branch on every
        #     future launch, never re-detect, and be surfaced as `bVersionDetected=true,
        #     bLowConfidence=false` -- the code's own words are "user is the source of truth". So a
        #     "restore" leaves a permanent, confident-looking assertion behind.
        #   * clearing with 0 removes that record (`Flamme.cpp:33-34` erases both keys) but
        #     DELIBERATELY does not touch `g_cachedUEVersion` ("would require re-init",
        #     Fern.cpp's own comment) -- so on its own it leaves the process reading at 422 and the
        #     final survey below would report a degraded rate that is the rig's own doing.
        # Measured 2026-09-06: an earlier run of this rig set the native version back and left
        # exactly that residue on OCTOPATH, which is how the two-call requirement was found.
        c.request("set_ue_version_override", version=native, timeout=60)   # fix the process
        c.request("set_ue_version_override", version=0, timeout=60)        # erase the record
        back = survey(c, "restored + cleared")
        show(back)

    say("\n================ A3 (substitute host) RESULT ================")
    # 421 is THE version the fix changed. On this binary the true offset is 0x88, and the fixed
    # table returns 0x88 for 421 -- so the reads must stay as sane as native.
    if at421.get("rate", 0) < base.get("rate", 0) - 5.0:
        fails.append("A3: overriding to 421 degraded sanity %.1f%% -> %.1f%% -- the table is NOT "
                     "returning 0x88 for 421, which is the entire fix"
                     % (base.get("rate", 0), at421.get("rate", 0)))
    else:
        say("421: sanity held (%.1f%% -> %.1f%%) -- the table returns the 4.08-4.21 offset for the "
            "one version the fix changed." % (base.get("rate", 0), at421.get("rate", 0)))

    # 422 must DEGRADE. If it does not, the reader is not following g_cachedUEVersion at all and
    # the 421 leg above proves nothing.
    if at422.get("rate", 100) >= base.get("rate", 0) - 5.0:
        fails.append("A3: overriding to 422 did NOT degrade (%.1f%% vs %.1f%%). The reader is not "
                     "following the version, so the 421 leg is vacuous -- this is the control that "
                     "matters" % (at422.get("rate", 0), base.get("rate", 0)))
    else:
        say("422: sanity DROPPED (%.1f%% -> %.1f%%) -- 0x98 is wrong for this binary, so the reader "
            "demonstrably follows the version. This is what makes the 421 leg meaningful."
            % (base.get("rate", 0), at422.get("rate", 0)))

    if back.get("rate", 0) < base.get("rate", 0) - 5.0:
        fails.append("A3: the restore+clear did not bring sanity back (%.1f%%) -- the process is "
                     "still reading at the overridden version. Relaunch before trusting ANY later "
                     "row on this host, and check UE5CEDumper.{Machine}.json for a leftover "
                     "ueVersionUserOverride" % back.get("rate", 0))

    say("\n⚠ SCOPE: this proves the table's value REACHES THE READER and yields sane data at "
        "version input 421. It does NOT prove a retail 4.21 binary's layout is 0x88 -- that is the "
        "UVTD templates' job, offline, and no live host can add to it.")
    for n in notes:
        say("NOTE: " + n)
    if fails:
        say("\nFAIL (%d):" % len(fails))
        for f_ in fails:
            say("   - " + f_)
        return 1
    say("\nPASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
