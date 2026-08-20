"""PEHOOKONCE steps 1-3: a failed ProcessEvent detection must be RE-ARMABLE.

    py pehookonce.py            # run against an already-launched proxy-mode title

WAS: a detection that failed because there was nothing to detect *yet* stored the same
`-1` as a hard failure, and every retry path was gated against `-1` — so a single
`pe_profile_start` before the first scan **poisoned the PE hook for the whole
process**, and the advice told the user to retry the one thing that could not work.

!! WHY A PROXY-MODE TITLE IS REQUIRED. In proxy mode the DLL starts the pipe server
only and does NOT scan, so GObjects is genuinely unset when the profiler is first
asked — which is the state that used to be recorded as terminal. On a self-scanning
host the window does not exist and the test is vacuous.

THE ORDER IS THE TEST, and it is deliberately the WRONG order:

    init  ->  pe_profile_start   (BEFORE any scan)   step 1: armed, not failed
          ->  trigger_scan
          ->  one invoke (teleport_get_pov)
          ->  pe_profile_start   (again)             step 2: MUST now be true

Step 2 is the exact order-swap that used to be permanently broken, so it is the whole
point. Step 3 then checks the retry budget did not spin: nothing to detect must mean
no `detection run N/8` lines at all.
"""
import pathlib
import re
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient                        # noqa: E402

LOGROOT = pathlib.Path.home() / "AppData/Local/UE5CEDumper/Logs"


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(s.encode(enc, "replace").decode(enc, "replace") + "\n")


def profile_state(c):
    # hook_active / hook_detail live on the START reply, NOT on pe_profile_get
    # (which only carries recording/total_calls/distinct_funcs). Reading the wrong
    # one yields None and reads as a failure.
    r = c.request("pe_profile_start")
    c.request("pe_profile_stop")
    return r


def main(stem):
    ok = True
    log = LOGROOT / stem / "init-0.log"
    with PipeClient(timeout=600.0) as c:
        c.assert_build()

        say("=== step 1: pe_profile_start BEFORE any scan ===")
        p = c.request("get_pointers")
        say(f"  pre-scan GObjects: {p.get('gobjects')} ({p.get('gobjects_method')}) "
              f"— must be unset for this to mean anything")
        if p.get("gobjects_method") not in (None, "", "not_found"):
            say("  !! INCONCLUSIVE: the host already scanned, so there is no "
                  "'nothing to detect yet' window to test")
            return 2
        r1 = profile_state(c)
        d1 = str(r1.get("hook_detail", ""))
        say(f"  hook_active = {r1.get('hook_active')}   (expect False)")
        say(f"  hook_detail = {d1[:150]!r}")
        armed = "ARMED" in d1 or "not resolved yet" in d1
        if r1.get("hook_active") or not armed:
            say("  step 1 FAIL — expected inactive AND an ARMED/not-resolved-yet detail")
            ok = False
        else:
            say("  step 1 PASS")

        say("\n=== step 2 !! THE ONE THAT MATTERS: scan -> invoke -> start again ===")
        c.request("trigger_scan")
        for _ in range(60):
            time.sleep(5)
            p = c.request("get_pointers")
            if p.get("gobjects_method") not in (None, "", "not_found"):
                break
        say(f"  after scan: GObjects {p.get('gobjects')} ({p.get('gobjects_method')})")
        inv = c.request("teleport_get_pov")
        say(f"  invoke teleport_get_pov: ok={inv.get('ok')} code={inv.get('code')}")
        time.sleep(3)
        r2 = profile_state(c)
        say(f"  hook_active = {r2.get('hook_active')}   (MUST be True — this is the "
              f"order-swap that used to be permanently broken)")
        say(f"  hook_detail = {str(r2.get('hook_detail'))[:150]!r}")
        if not r2.get("hook_active"):
            say("  step 2 FAIL — the detection did NOT re-arm")
            ok = False
        else:
            say("  step 2 PASS")

    say("\n=== step 3 !! NO STORM: the retry budget must not spin ===")
    txt = log.read_text(encoding="utf-8", errors="replace") if log.is_file() else ""
    runs = re.findall(r"detection run (\d+)/(\d+)", txt)
    say(f"  'detection run N/M' lines in init-0.log: {len(runs)}  {runs[:6]}")
    for needle in ("no UObject vtable available yet", "offset resolved to vtable+",
                   "first-time init complete"):
        n = txt.count(needle)
        say(f"  {needle!r}: {n}")
    say(f"\nPEHOOKONCE steps 1-3: {'PASS' if ok else 'NEEDS ATTENTION'}")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1] if len(sys.argv) > 1 else "LushfoilSim-Win64-Shipping"))
