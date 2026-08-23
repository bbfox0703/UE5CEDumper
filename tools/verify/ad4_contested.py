"""AD4 step 4 -- record the God Mode badge's `ON (contested)` state actually occurring.

    py tools/verify/ad4_contested.py                 # full run (neg control -> positive -> recovery)
    py tools/verify/ad4_contested.py --seconds 12    # longer observation window per phase

WHAT THIS PROVES, AND WHAT IT DELIBERATELY DOES NOT.
The register row is explicit that the badge STRING is already a pure function with
tests, so the only open question is whether the underlying STATE ever genuinely
occurs. That state is the triple returned by `get_protect_state`:

    want=1, godmode=0, resolvable=true

i.e. "God Mode is wanted, the live flag says the pawn CAN be damaged, and we can
read it" -- the game is winning a race against Solitar's re-assert worker. The row
also warns to poll `get_protect_state` and NOT `get_god_mode`, which returns only
state/code and structurally cannot show this cell.

WHY A FIXTURE REPLACES "GO GET HIT IN COMBAT".
DumperTest's ADumperTestActor::Tick calls SetCanBeDamaged(true) on the player pawn
every frame while bContestDamage is set, so the contest is on demand instead of
dependent on someone finding a monster. Two numbers make it catchable rather than
lucky, and both are MEASURED here, not assumed:
  * Solitar re-asserts every Grimoire::PROTECT_REASSERT_MS = 300 ms (write-on-drift).
  * the sample's Tick rate, measured by AD4_GetContestWrites over a timed window.
So after each re-assert the flag is wrong again within one frame and STAYS wrong for
the rest of the 300 ms window: the contested state is the DOMINANT one here, not the
rare flicker real combat produces.

  ==> DO NOT assume 15 FPS here even though launch_dumpertest.py passes
      `-ExecCmds=t.MaxFPS 15`. On the SHIPPING flavour that cap does nothing:
      UE 5.4's Misc/Exec.h defines UE_ALLOW_EXEC_COMMANDS as
      UE_ALLOW_EXEC_COMMANDS_IN_SHIPPING when (UE_BUILD_SHIPPING && !WITH_EDITOR),
      and UnrealBuildTool sets that to 0 unless the Target.cs opts in
      (UEBuildTarget.cs:5147/5151). Measured on Shipping 2026-08-23: 59.8 Hz, which
      predicts a ~94% contested duty and 97.8% was observed. This rig therefore
      REPORTS the rate it measured rather than quoting a configured one -- a number
      without its conditions is not a measurement.

  ==> That asymmetry is the honest caveat on this evidence and it is printed with the
      result: this shows the state is REAL and the detector SEES it. It does not
      measure how often contention happens in ordinary play. A per-frame writer is a
      harsher contest than taking a hit every few seconds.

THREE INDEPENDENT WITNESSES, because one of them agreeing with itself proves nothing:
  1. the pipe poll        -- the DLL's own live read of the pawn's bCanBeDamaged bit
  2. AD4_GetContestWrites -- the GAME counting its own writes, from the other side
  3. the DLL walk log     -- Solitar's own "re-asserted protection flag(s) (drift #N)"
A flat counter (2) while (1) reports contested would mean the poll is lying; (1)
contested while (3) never logs drift would mean the worker never noticed.

THE NEGATIVE CONTROL RUNS FIRST, ON PURPOSE. Phase A holds God Mode with contention
OFF and must observe (1,1,true) and never (1,0,*). Without it, a phase-B run that
saw the contested triple could not distinguish "the detector discriminates" from
"the detector always says contested".
"""
from __future__ import annotations

import argparse
import json
import os
import pathlib
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient           # noqa: E402

ACTOR_CLASS = "DumperTestActor"
POLL_HZ = 40.0


def find_live_actor(c):
    r = c.request("find_instances", class_name=ACTOR_CLASS, max_results=100)
    c.check_complete(r)
    live = [i for i in r.get("instances", []) if not i["name"].startswith("Default__")]
    if not live:
        raise SystemExit("ad4: FAILED -- no live %s (a CDO-only result means the level "
                         "never started; every reading below would be a coherent zero)"
                         % ACTOR_CLASS)
    return live[0]


def invoke(c, addr, fn, parms_size=0, params_hex=""):
    r = c.request("invoke_function", func_name=fn, instance_addr=addr,
                  parms_size=parms_size, params_hex=params_hex)
    if not r.get("ok") or r.get("result", 0) != 0:
        raise SystemExit("ad4: FAILED -- invoke %s -> %s" % (fn, json.dumps(r)[:300]))
    return r


def contest_writes(c, addr):
    r = invoke(c, addr, "AD4_GetContestWrites", parms_size=4, params_hex="00000000")
    hexs = r.get("result_hex", "")
    if len(hexs) < 8:
        raise SystemExit("ad4: FAILED -- AD4_GetContestWrites gave no result_hex: %r" % hexs)
    return int.from_bytes(bytes.fromhex(hexs[:8]), "little", signed=True)


def set_contention(c, addr, on):
    invoke(c, addr, "AD4_SetDamageContention", parms_size=1,
           params_hex="01" if on else "00")


def poll(c, seconds):
    """Sample get_protect_state as fast as POLL_HZ allows. Returns the sample list."""
    out, deadline, period = [], time.monotonic() + seconds, 1.0 / POLL_HZ
    while time.monotonic() < deadline:
        t0 = time.monotonic()
        r = c.request("get_protect_state")
        out.append((r.get("want"), r.get("godmode"), bool(r.get("resolvable"))))
        rest = period - (time.monotonic() - t0)
        if rest > 0:
            time.sleep(rest)
    return out


def tally(samples):
    t = {}
    for s in samples:
        t[s] = t.get(s, 0) + 1
    return dict(sorted(t.items(), key=lambda kv: -kv[1]))


def show(label, samples):
    n = len(samples) or 1
    print("  %s: %d samples" % (label, len(samples)))
    for trip, cnt in tally(samples).items():
        print("      (want=%s, godmode=%s, resolvable=%s)  x%-5d  %5.1f%%"
              % (trip[0], trip[1], trip[2], cnt, 100.0 * cnt / n))


def log_drift_lines():
    """Witness 3: Solitar's own drift warnings, from the DLL's walk log."""
    root = pathlib.Path(os.environ["LOCALAPPDATA"]) / "UE5CEDumper" / "Logs"
    hits = []
    for d in sorted(root.glob("DumperTest*")):
        f = d / "walk-0.log"
        if not f.is_file():
            continue
        try:
            for line in f.read_text(encoding="utf-8", errors="replace").splitlines():
                if "re-asserted protection flag" in line:
                    hits.append((d.name, line.strip()))
        except OSError as e:                      # a live writer's handle, not fatal
            print("      (could not read %s: %s)" % (f, e))
    return hits


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--seconds", type=float, default=8.0,
                    help="observation window per phase (default 8)")
    a = ap.parse_args(argv)

    with PipeClient() as c:
        c.assert_build()
        c.ensure_scanned()
        actor = find_live_actor(c)
        addr = actor["addr"]
        print("actor      : %s @%s (index %s)" % (actor["name"], addr, actor["index"]))

        base = c.request("get_protect_state")
        print("baseline   : %s" % json.dumps(base, sort_keys=True))
        if not base.get("resolvable"):
            raise SystemExit("ad4: FAILED -- pawn not resolvable; nothing below can mean "
                             "anything (resolvable=false is the third element of the very "
                             "triple under test)")

        drift_before = len(log_drift_lines())
        set_contention(c, addr, False)
        c.request("set_god_mode", enable=True)
        time.sleep(1.0)                                  # let the worker take the flag

        # ---------- A. NEGATIVE CONTROL: hold with NO contention ----------
        print("\n[A] negative control -- God Mode held, contention OFF")
        w0 = contest_writes(c, addr)
        a_samples = poll(c, a.seconds)
        w1 = contest_writes(c, addr)
        show("A", a_samples)
        print("      AD4_GetContestWrites: %d -> %d (must not move)" % (w0, w1))
        a_contested = [s for s in a_samples if s[0] == 1 and s[1] == 0]
        a_ok = (not a_contested) and w1 == w0 and all(s == (1, 1, True) for s in a_samples)

        # ---------- B. POSITIVE: the game fights back ----------
        print("\n[B] positive -- contention ON (game re-writes bCanBeDamaged every Tick)")
        set_contention(c, addr, True)
        time.sleep(0.5)
        t2 = time.monotonic()
        w2 = contest_writes(c, addr)
        b_samples = poll(c, a.seconds)
        w3 = contest_writes(c, addr)
        t3 = time.monotonic()
        show("B", b_samples)
        print("      AD4_GetContestWrites: %d -> %d (+%d, must be > 0)" % (w2, w3, w3 - w2))
        # The tick rate is MEASURED, not quoted -- see the -ExecCmds note in the
        # module docstring. It is also what makes the duty cycle below a prediction
        # the observation can disagree with, rather than a restatement of it.
        hz = (w3 - w2) / max(t3 - t2, 1e-6)
        duty = max(0.0, (300.0 - 1000.0 / hz) / 300.0) if hz > 0 else 0.0
        obs = 100.0 * len(([s for s in b_samples if s == (1, 0, True)])) / max(len(b_samples), 1)
        print("      measured Tick rate  : %.1f Hz over %.2f s" % (hz, t3 - t2))
        print("      contested duty      : predicted %.1f%% from %.1f Hz vs 300 ms "
              "re-assert; observed %.1f%%" % (100.0 * duty, hz, obs))
        b_contested = [s for s in b_samples if s == (1, 0, True)]
        b_ok = bool(b_contested) and (w3 - w2) > 0

        # ---------- C. RECOVERY: contention off again ----------
        print("\n[C] recovery -- contention OFF again")
        set_contention(c, addr, False)
        time.sleep(1.0)                                  # >= 3 re-assert periods
        c_samples = poll(c, a.seconds)
        show("C", c_samples)
        c_contested = [s for s in c_samples if s[0] == 1 and s[1] == 0]
        c_ok = not c_contested

        # ---------- D. WITNESS 3: the DLL's own drift log ----------
        print("\n[D] witness 3 -- Solitar's drift warnings in walk-0.log")
        lines = log_drift_lines()
        print("      drift lines: %d before, %d after" % (drift_before, len(lines)))
        for _, ln in lines[-3:]:
            print("      | %s" % ln[-150:])
        d_ok = len(lines) > drift_before

        c.request("set_god_mode", enable=False)
        set_contention(c, addr, False)

    print("\n" + "=" * 72)
    print("A negative control (never contested, counter flat) : %s" % ("PASS" if a_ok else "FAIL"))
    print("B contested triple (1,0,true) observed             : %s  (%d/%d samples)"
          % ("PASS" if b_ok else "FAIL", len(b_contested), len(b_samples)))
    print("C recovers when contention stops                   : %s" % ("PASS" if c_ok else "FAIL"))
    print("D DLL logged the drift independently               : %s" % ("PASS" if d_ok else "FAIL"))
    ok = a_ok and b_ok and c_ok and d_ok
    print("\nAD4 step 4: %s" % ("PASS" if ok else "FAIL"))
    print("\nCAVEAT recorded with the result: the contest here is per-frame against a")
    print("300 ms re-assert (see the measured Hz in [B]), so the contested state is the")
    print("DOMINANT one. This shows the state is REAL and the detector SEES it -- NOT")
    print("how often contention arises in ordinary play, where a hit lands every few")
    print("seconds and the worker usually wins.")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
