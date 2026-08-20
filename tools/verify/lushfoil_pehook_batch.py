r"""PEHOOKONCE step 3 (literal pre-scan form) + PEHOOK step 4 — one Lushfoil launch.

    py tools/verify/lushfoil_pehook_batch.py

BOTH are genuinely open, and each is a *different* leftover from an earlier run:

  PEHOOKONCE 3  recorded as "partially covered, and stated honestly": the no-storm grep was taken
                AFTER the scan, where exactly one `detection run N/8` is correct and expected. The
                row asks for it after step 1 and **before any scan**, where the answer must be
                **zero** — nothing to detect must not spend a retry. That literal form is run here.
  PEHOOK 4      the non-regression check that the pattern path is untouched on a known-good title:
                `Add_IntInt(3,4) = 7`, the hook stays installed, and **no** `VALIDATION FAILED`.
                PEHOOKONCE 4's evidence recorded `hook_active: true` and `vtable+0x260` but neither
                the invoke result nor the absence of the failure line, so it does not cover this.

ORDER MATTERS AND IS THE TEST. The pre-scan window exists only before the first `trigger_scan`, so
PEHOOKONCE 3 must run first; PEHOOK 4 then needs the scan. One launch, in that order, no restart.

⚠ This deliberately performs the profiler-before-scan sequence that USED to poison the PE hook for
the whole process. That is the point of PEHOOKONCE — and PEHOOK 4, run afterwards in the same
process, is what proves the poisoning no longer happens.

⚠ Proxy mode is required and is asserted, not assumed: GObjects must read unresolved before the
scan, or the "nothing to detect yet" window never existed and step 3 is vacuous.
"""
import json
import pathlib
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from pipe_client import PipeClient  # noqa: E402

PROC = "LushfoilSim-Win64-Shipping"
LOG = pathlib.Path.home() / "AppData/Local/UE5CEDumper/Logs" / PROC
IDLE_S = 60


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + "\n")


def count(needle):
    n = 0
    for f in sorted(LOG.glob("*-0.log")):
        try:
            n += sum(1 for l in f.read_text(encoding="utf-8", errors="replace").splitlines()
                     if needle in l)
        except OSError:
            pass
    return n


def grep(needle, tail=3):
    out = []
    for f in sorted(LOG.glob("*-0.log")):
        try:
            out += [l for l in f.read_text(encoding="utf-8", errors="replace").splitlines()
                    if needle in l]
        except OSError:
            pass
    return out[-tail:]


def main():
    fails = []
    with PipeClient() as c:
        c.assert_build()

        say("== precondition: proxy mode, GObjects NOT yet resolved ==")
        g = c.request("get_pointers")
        g = g.get("data", g)
        gobj = str(g.get("gobjects", ""))
        say("   gobjects=%r  objects=%s" % (gobj, g.get("object_count")))
        if gobj and gobj not in ("", "0x0", "not_found", "None"):
            fails.append("GObjects is ALREADY resolved (%s) -- something scanned before this run, "
                         "so the pre-scan window does not exist and step 3 would be vacuous" % gobj)
            for x in fails:
                say("FAIL: " + x)
            return 1
        say("   OK: the 'nothing to detect yet' window genuinely exists")

        # ---------------------------------------------------------- PEHOOKONCE 3
        say("")
        say("== PEHOOKONCE step 3 (literal): profiler BEFORE any scan, then 60 s of 10 Hz traffic ==")
        d0_run = count("detection run")
        d0_novt = count("no UObject vtable available yet")
        say("   baseline: 'detection run'=%d  'no UObject vtable available yet'=%d" % (d0_run, d0_novt))

        pr = c.request("pe_profile_start")
        pd = pr.get("data", pr)
        say("   pe_profile_start (pre-scan) -> hook_active=%s" % pd.get("hook_active"))
        say("   detail: %s" % str(pd.get("hook_detail"))[:150])

        say("   driving a 10 Hz pipe feature for %d s ..." % IDLE_S)
        t0 = time.time()
        polls = 0
        while time.time() - t0 < IDLE_S:
            c.request("get_diagnostics")
            polls += 1
            time.sleep(0.1)
        say("   %d polls in %.0f s" % (polls, time.time() - t0))

        d1_run = count("detection run")
        d1_novt = count("no UObject vtable available yet")
        say("")
        say("   NEW 'detection run N/8' lines            : %d   <-- must be 0" % (d1_run - d0_run))
        say("   NEW 'no UObject vtable available yet'    : %d   <-- must be <= 1" % (d1_novt - d0_novt))
        for l in grep("no UObject vtable available yet", 2):
            say("      " + l.strip()[:160])
        if d1_run != d0_run:
            fails.append("PEHOOKONCE-3: %d detection run(s) were spent with nothing to detect -- "
                         "that is the retry storm the step forbids" % (d1_run - d0_run))
        if d1_novt - d0_novt > 1:
            fails.append("PEHOOKONCE-3: %d 'no UObject vtable available yet' lines -- the step "
                         "allows at most one" % (d1_novt - d0_novt))

        # ---------------------------------------------------------- PEHOOK 4
        say("")
        say("== PEHOOK step 4 (non-regression): scan, then the pattern path must be untouched ==")
        vf0 = count("VALIDATION FAILED")
        t0 = time.time()
        st = c.ensure_scanned(timeout=600)
        say("   scanned in %.0f s (objects=%s)" % (time.time() - t0,
                                                   st.get("object_count") or st.get("objects")))
        for l in grep("offset resolved to vtable+", 2):
            say("      " + l.strip()[:170])

        fl = (c.request("list_all_functions", limit=20000,
                        game_only=False).get("functions") or [])
        f = next((x for x in fl if x.get("func_name") == "Add_IntInt"), None)
        if not f:
            fails.append("PEHOOK-4: Add_IntInt not found on this host")
        else:
            r = c.request("invoke_function", class_name=f["class_name"], func_name="Add_IntInt",
                          parms_size=12, params_hex="03000000" + "04000000" + "00000000")
            d = r.get("data", r)
            h = d.get("params_hex") or d.get("result_hex") or ""
            val = int.from_bytes(bytes.fromhex(h[16:24]), "little") if len(h) >= 24 else None
            say("   Add_IntInt(3,4) = %s   <-- must be 7" % val)
            if val != 7:
                fails.append("PEHOOK-4: Add_IntInt returned %r, not 7" % val)

        dg = c.request("get_diagnostics")
        dg = dg.get("data", dg)
        gt = dg.get("game_thread") or {}
        say("   hook_active=%s  fire_count=%s" % (gt.get("hook_active"), gt.get("hook_fire_count")))
        if not gt.get("hook_active"):
            fails.append("PEHOOK-4: the hook is not installed after a normal scan+invoke")
        vf1 = count("VALIDATION FAILED")
        say("   NEW 'VALIDATION FAILED' lines: %d   <-- must be 0" % (vf1 - vf0))
        for l in grep("VALIDATION FAILED", 2):
            say("      " + l.strip()[:170])
        if vf1 != vf0:
            fails.append("PEHOOK-4: %d VALIDATION FAILED on a pattern-detected title -- the "
                         "pattern path is NOT untouched" % (vf1 - vf0))

    say("")
    for x in fails:
        say("FAIL: %s" % x)
    if not fails:
        say("PASS (PEHOOKONCE step 3 literal + PEHOOK step 4)")
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
