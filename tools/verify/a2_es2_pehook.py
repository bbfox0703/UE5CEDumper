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

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from pipe_client import PipeClient  # noqa: E402

PROC = "ES2-Win64-Shipping"
LOG = pathlib.Path.home() / "AppData/Local/UE5CEDumper/Logs" / PROC


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
        if ue != 506:
            fails.append("A2: UE version is %r, not 506 -- this is not the 5.6 build the row needs" % ue)
        if (p.get("object_count") or 0) < 1000:
            fails.append("A2: object_count %r -- the engine did not boot; every number below is a corpse"
                         % p.get("object_count"))

        # --- scan-0.log: prove WHICH binary this is -------------------------------
        dv = grep("scan", "DetectVersion:")
        say("\n[scan-0.log] DetectVersion lines: %d" % len(dv))
        for l in dv[-3:]:
            say("   " + l.strip()[:170])
        if not any("506" in l for l in dv):
            fails.append("A2: no DetectVersion line reporting 506 in scan-0.log")

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

    # --- the verdict the row actually asks for ------------------------------------
    slots = set()
    for l in matched + resolved:
        i = l.find("vtable+0x")
        if i >= 0:
            slots.add("0x" + l[i + 9:i + 9 + 3].strip().split()[0].rstrip(":,.").upper())
    say("\n================ A2 RESULT ================")
    say("slot(s) observed: %s" % (", ".join(sorted(slots)) or "NONE"))
    if "0x260" in slots:
        say("=> 0x260: the table's 506 row is CORROBORATED on a retail UE 5.6.1 licensee build.")
    elif "0x278" in slots:
        say("=> 0x278: the 506 row is WRONG FOR THIS TITLE. Record it as a register note.")
        say("   ⛔ Do NOT collapse the table back into a '>=' ladder -- that is the bug A2 fixed.")
    else:
        say("=> inconclusive; see the lines above.")

    for n in notes:
        say("NOTE: " + n)
    if fails:
        say("\nFAIL (%d):" % len(fails))
        for f_ in fails:
            say("   - " + f_)
        return 1
    say("\nPASS -- every A2 observable captured, both absence checks held.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
