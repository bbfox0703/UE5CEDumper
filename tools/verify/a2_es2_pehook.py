r"""Audit A2 — the ProcessEvent vtable-slot table, on a retail UE 5.6 build (EVERSPACE 2).

    py tools/verify/a2_es2_pehook.py

THE QUESTION. `1d647a08` replaced an unreachable `>= 550` band (under which every UE5 game
silently took `0x220`) with a measured per-version table, `DynOff::ProcessEventVTableSlotFor`
(`dll/src/Grimoire.h`). The table is deliberately NON-monotonic: `505 -> 0x278`, then
`506/507 -> 0x260`. EVERSPACE 2 was patched from UE 5.5.4 to 5.6.1 on 2026-09-01, so it crossed
that boundary. Its `0x278` was measured twice on the OLD binary (2026-05-11 and
`[PEHOOK-6-2026-08-20]`), which makes it a confirmed live witness for the 5.5 row. This run asks
what the 5.6 build does, and either corroborates the 506 row or falsifies it. Both are results.

WHY A RIG AND NOT A PLAY-TEST. Every observable here is DLL-side. The UI can show a plausible
`Connected — UE506` and a `HookActive: true` over a WRONG slot; what discriminates is that the
invoke returns a COMPUTED value (`Add_IntInt(3,4) == 7`) and that no fallback/validation-failure
line was written. A human looking at the UI cannot see either.

⚠ DETECTION IS LAZY. `RunPeDetection` is reached only from `EnsureProcessEventReady` and
`UE5_EnsureGameThreadHook`, so connect + scan + walk emits NO `DetectProcessEvent` line at all.
The invoke is not a nicety, it is what makes the row measurable.

⛔ THE GREP MUST FAIL LOUDLY, NOT RETURN ZERO. `lushfoil_pehook_batch.py:count()` swallows OSError
and returns 0. Build 3370 opened every live log with `_wfopen_s` (EXCLUSIVE), so every read raised
ERROR_SHARING_VIOLATION -- and every "this line must be ABSENT" check would have passed VACUOUSLY
while measuring nothing. Fixed in Sein.cpp for 3371; this rig refuses to guess regardless.
"""
import pathlib
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from pipe_client import PipeClient  # noqa: E402

# The log-folder name, i.e. the image name without .exe. Defaults to EVERSPACE 2 (the row's own
# host) but takes any host, because the table has ONE row per UE version and each row needs its own
# live witness -- 508 in particular had none until DumperTest58 existed.
PROC = sys.argv[1] if len(sys.argv) > 1 else "ES2-Win64-Shipping"
LOG = pathlib.Path.home() / "AppData/Local/UE5CEDumper/Logs" / PROC

# What DynOff::ProcessEventVTableSlotFor should answer, per UE version. Kept here rather than
# hardcoding 0x260 so the rig scores any host against the table's OWN claim for that host.
EXPECTED_SLOT = {500: 0x258, 501: 0x260, 502: 0x268, 503: 0x268, 504: 0x268,
                 505: 0x278, 506: 0x260, 507: 0x260, 508: 0x250}


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + "\n")
    sys.stdout.flush()


def lines(basename):
    """Every line of one live log. Raises -- never returns [] -- if it cannot be read."""
    p = LOG / (basename + "-0.log")
    if not p.is_file():
        raise SystemExit("a2: %s does not exist; wrong process folder or the DLL never loaded" % p)
    try:
        return p.read_text(encoding="utf-8", errors="replace").splitlines()
    except OSError as e:
        raise SystemExit(
            "a2: cannot read %s (%s). If this is errno 13/32 the DLL is holding the log "
            "EXCLUSIVELY -- that is the build-3370 _wfopen_s defect, and every absence check in "
            "this rig would otherwise have passed vacuously. Rebuild with the Sein.cpp fix." % (p, e))


def grep(basename, needle):
    return [l for l in lines(basename) if needle in l]


def main():
    fails, notes = [], []
    with PipeClient() as c:
        build = c.assert_build()
        say("build answering the pipe: %s" % build)

        p = c.ensure_scanned()
        ue = p.get("ue_version")
        say("UE version = %s   objects = %s   load_mode = %s"
            % (ue, p.get("object_count"), p.get("load_mode")))
        want = EXPECTED_SLOT.get(ue)
        if want is None:
            fails.append("A2: UE %r has no row in ProcessEventVTableSlotFor's UE5 band -- this host "
                         "cannot score the table" % ue)
        else:
            say("the table's own claim for UE %s: vtable+0x%X" % (ue, want))
        if (p.get("object_count") or 0) < 1000:
            fails.append("A2: object_count %r -- the engine did not boot; every number below is a corpse"
                         % p.get("object_count"))

        # --- scan-0.log: prove WHICH binary this is -------------------------------
        # ⚠ TWO WAYS the version legitimately arrives, and an earlier version of this rig knew only
        # one. `FindAll` SKIPS DetectVersion entirely when UE5CEDumper.{Machine}.json already holds
        # a verdict for this PE hash, so on any host scanned before, scan-0.log has ZERO
        # `DetectVersion:` lines and a `FindAll: UE Version = N (cached...)` line instead. Demanding
        # the first produced a false FAIL on DumperTest58.
        dv = grep("scan", "DetectVersion:") + grep("scan", "FindAll: UE Version =")
        say("\n[scan-0.log] version lines: %d" % len(dv))
        for l in dv[-3:]:
            say("   " + l.strip()[:170])
        if not any(str(ue) in l for l in dv):
            fails.append("A2: no scan-0.log line reporting UE %s (neither a DetectVersion result "
                         "nor a cached FindAll verdict) -- cannot confirm which binary this is" % ue)

        # --- the invoke: this is what makes detection run -------------------------
        fl = c.request("list_all_functions", limit=20000, game_only=False).get("functions") or []
        say("\nfunctions listed: %d" % len(fl))
        f = next((x for x in fl if x.get("func_name") == "Add_IntInt"), None)
        if not f:
            fails.append("A2: Add_IntInt not found on this host -- cannot produce the computed-value control")
        else:
            r = c.request("invoke_function", class_name=f["class_name"], func_name="Add_IntInt",
                          parms_size=12, params_hex="03000000" + "04000000" + "00000000")
            d = r.get("data", r)
            h = d.get("params_hex") or d.get("result_hex") or ""
            val = int.from_bytes(bytes.fromhex(h[16:24]), "little") if len(h) >= 24 else None
            say("Add_IntInt(3,4) = %s   <-- MUST be 7 (a wrong slot returns 0/unchanged)" % val)
            if val != 7:
                fails.append("A2: Add_IntInt returned %r, not 7 -- the installed slot does not dispatch" % val)

        dg = c.request("get_diagnostics")
        dg = dg.get("data", dg)
        gt = dg.get("game_thread") or {}
        say("hook_active = %s   hook_fire_count = %s"
            % (gt.get("hook_active"), gt.get("hook_fire_count")))
        if not gt.get("hook_active"):
            fails.append("A2: hook_active is false after scan+invoke")
        if not (gt.get("hook_fire_count") or 0) > 0:
            fails.append("A2: hook_fire_count is 0 -- the hook is installed but nothing dispatched through it")

    # --- init-0.log: the slot itself, and the two lines that must be ABSENT -------
    say("\n[init-0.log] the slot:")
    matched = grep("init", "DetectProcessEvent (pattern): match at vtable+")
    resolved = grep("init", "offset resolved to vtable+")
    for l in matched + resolved:
        say("   " + l.strip()[:170])
    if not matched:
        notes.append("no 'DetectProcessEvent (pattern): match' line -- detection may not have run")
    if not resolved:
        fails.append("A2: no 'offset resolved to vtable+' line -- nothing was ever INSTALLED")

    fallback = grep("init", "DetectProcessEvent (fallback)")
    validfail = grep("init", "VALIDATION FAILED")
    say("\n'DetectProcessEvent (fallback)' lines: %d   <-- MUST be 0 (else the table quoted itself)"
        % len(fallback))
    say("'VALIDATION FAILED' lines:              %d   <-- MUST be 0" % len(validfail))
    for l in fallback + validfail:
        say("   " + l.strip()[:170])
    if fallback:
        fails.append("A2: a fallback line is present -- the run must be DISCARDED, the pattern scan missed")
    if validfail:
        fails.append("A2: VALIDATION FAILED is present -- the installed hook did not behave")

    # ⛔ THE ABSENCE OF `VALIDATION FAILED` IS VACUOUS ON THE PATTERN PATH, WHICH IS THE PATH
    # THIS ROW EXPECTS. `Stark::ShouldActOnValidationFailure` returns `offsetFromVersionTable`
    # (`Stark.h:305-307`), so a PATTERN-derived offset that fires ZERO times takes the early
    # return at `Frieren.cpp:1951` and logs a line containing neither "VALIDATION FAILED" nor
    # "FAILED" -- the hook is deliberately KEPT. A mis-detected pattern slot therefore produces
    # exactly the log the naive grep set calls a pass. So the discriminator is the POSITIVE
    # line, not the absent one, and the zero-fire line is a HARD FAIL.
    # ⚠ THE VALIDATOR IS ARMED FOR 1500 ms AND THIS RIG USED TO RACE IT. `hook installed …
    # validator armed (1500ms)` is written at install; the verdict lands ~1.5 s later. On a host
    # where listing functions and invoking is quick, the log read happened FIRST and the rig
    # reported "no validation OK line" -- a false FAIL, and precisely the "report and reality
    # computed by different paths" shape this whole exercise is about. Wait for the verdict.
    deadline = time.time() + 15.0
    while time.time() < deadline:
        if grep("init", "GameThreadDispatch: validation OK") or grep("init", "fired 0 times in") \
                or grep("init", "VALIDATION FAILED"):
            break
        if not grep("init", "validator armed"):
            break            # the validator was never armed; waiting cannot help
        time.sleep(0.5)

    ok_line = grep("init", "GameThreadDispatch: validation OK")
    zero_fire = grep("init", "fired 0 times in")
    say("\n'GameThreadDispatch: validation OK' lines: %d   <-- MUST be >= 1 (the real discriminator)"
        % len(ok_line))
    for l in ok_line:
        say("   " + l.strip()[:170])
    say("'fired 0 times in' lines:                 %d   <-- MUST be 0 (pattern-path zero-fire)"
        % len(zero_fire))
    for l in zero_fire:
        say("   " + l.strip()[:170])
    if not ok_line:
        fails.append("A2: no 'validation OK' line -- absence of VALIDATION FAILED alone proves NOTHING "
                     "on the pattern path (Stark.h:305-307); the hook may be installed on a wrong slot")
    if zero_fire:
        fails.append("A2: the pattern-path zero-fire line is present -- the hook was KEPT but never "
                     "dispatched; treat as a hard fail, not as 'the game thread was idle'")

    # --- the verdict the row actually asks for ------------------------------------
    slots = set()
    for l in matched + resolved:
        i = l.find("vtable+0x")
        if i >= 0:
            slots.add("0x" + l[i + 9:i + 9 + 3].strip().split()[0].rstrip(":,.").upper())
    say("\n================ A2 RESULT (%s, UE %s) ================" % (PROC, ue))
    say("slot(s) observed: %s   table says: %s"
        % (", ".join(sorted(slots)) or "NONE", ("0x%X" % want) if want else "no row"))
    observed = {int(s, 16) for s in slots} if slots else set()
    if want and observed == {want}:
        say("=> the table's %s row is CORROBORATED by a live measurement on this host." % ue)
    elif want and observed:
        say("=> MISMATCH: measured %s, table says 0x%X. The %s row is wrong FOR THIS TITLE -- that "
            "is a RESULT, not a failure. Record it as a register note."
            % (", ".join(sorted(slots)), want, ue))
        say("   Do NOT collapse the table back into a '>=' ladder -- that is the bug A2 fixed, and "
            "the table is deliberately non-monotonic (505 -> 0x278, then 506/507 -> 0x260).")
        fails.append("A2: measured slot(s) %s != table 0x%X for UE %s"
                     % (", ".join(sorted(slots)), want, ue))
    else:
        say("=> inconclusive; see the lines above.")

    for n in notes:
        say("NOTE: " + n)
    if fails:
        say("\nFAIL (%d):" % len(fails))
        for f_ in fails:
            say("   - " + f_)
        return 1
    say("\nPASS -- every A2 observable captured. The load-bearing evidence is the POSITIVE set "
        "(validation OK, Add_IntInt==7, fire_count>0); the two absence checks are corroborating "
        "only, and on the pattern path the VALIDATION FAILED one is vacuous by construction.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
