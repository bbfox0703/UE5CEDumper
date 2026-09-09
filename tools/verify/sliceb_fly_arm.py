r"""`[SLICEB-FLY-2026-09-09]` — the Fly enable/disable write gates, live.

    py tools/verify/sliceb_fly_arm.py clean       # the regression arm: on, off, and back
    py tools/verify/sliceb_fly_arm.py probe       # just read the state

THE FIX. `Dunste::SetEnabled` writes ONE byte — `UCharacterMovementComponent::MovementMode` —
and that write IS the effect; `active` / `baseCaptured` / `capturedPawn` are bookkeeping. Both
call sites dropped `Macht::WriteBytes`' result, so ENABLE returned 1 and logged "Fly: ENABLED"
over a pawn that never left its old mode, and DISABLE cleared `active` and logged
"Fly: DISABLED" over a pawn still in MOVE_Flying with nothing tracking it. Both now capture the
result and return `FR_ERR_WRITE` (-10).

⛔ WHAT THIS ARM IS, AND IT IS THE REGRESSION HALF ONLY — say so, because the fix added an EARLY
RETURN on a path that runs every time the user toggles Fly. The risk it carries is not the
failure case (which is rare); it is that the new gate fires SPURIOUSLY and Fly stops working at
all. This arm proves it does not: enable takes the pawn to MOVE_Flying and reports state 1,
disable restores the captured mode and reports state 0, and the mode observed on the wire agrees
with the state at every step.

⭐ THE THREE WITNESSES MUST AGREE, and they are computed by different code:
  * `state`        — the return of `Dunste::SetEnabled`, i.e. the value the fix decides;
  * `active`       — `s_state.active`, the bookkeeping the fix rolls back on failure;
  * `current_mode` — `MovementMode` READ BACK from the CMC, which is the effect itself
                     (5 == MOVE_Flying). This is the one that is not our bookkeeping.
A run where `state == 1` and `current_mode != 5` is exactly the defect, still present.

⚠ WHAT IT CANNOT DO. The failure arm needs `Macht::WriteBytes` to fail, i.e. `VirtualProtect` to
be refused on the CMC's page or the memcpy to fault — which needs the region freed between
`ResolveCtx` and the write. There is no way to manufacture that from outside without decommitting
a page of the game's live heap, which any other thread touching it turns into a crash rather than
into evidence. The failure path is therefore verified by reading, by `-Target DLL`, and by the C#
tests that pin what the UI does when the DLL reports it — NOT by this rig. Said here rather than
left for a reader to discover, because `Dunste.cpp` reaches no test target at all.

⚠ RESTORE. `clean` always ends with Fly OFF, and asserts the MovementMode came back to the value
it read BEFORE the run — the `mutate_guard.py` discipline. A run that leaves the pawn flying has
failed even if every intermediate assertion passed.
"""
from __future__ import annotations

import argparse
import pathlib
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient          # noqa: E402

MOVE_FLYING = 5
FR_OK, FR_ERR_WRITE = 0, -10


def show(tag, st):
    print("  %-22s state=%-4s active=%-5s mode=%-4s has_cmc=%s code=%s"
          % (tag, st.get("state"), st.get("active"), st.get("current_mode"),
             st.get("has_cmc"), st.get("code")))
    return st


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("action", choices=["clean", "probe"])
    a = ap.parse_args()

    fails = []

    def check(name, cond, got=""):
        print("  %-4s %s%s" % ("ok" if cond else "FAIL", name, ("   got: %s" % got) if got else ""))
        if not cond:
            fails.append(name)

    with PipeClient() as c:
        print("build:", c.assert_build())
        c.ensure_scanned()

        base = show("baseline", c.request("fly_get_state"))
        if not base.get("has_cmc"):
            print("\n*** no CharacterMovement on the current pawn -- the fixture is not in a "
                  "state this arm can measure. Relaunch DumperTest and re-inject.")
            return 2
        baseMode = base.get("current_mode")

        if a.action == "probe":
            return 0

        # ---- ENABLE -----------------------------------------------------------------
        on = show("after enable", c.request("fly_set", enable=True, speed=600.0,
                                            preset=0, noclip=False))
        check("enable: state is 1, NOT the new FR_ERR_WRITE", on.get("state") == 1,
              str(on.get("state")))
        check("enable: the bookkeeping was not rolled back", on.get("active") is True,
              str(on.get("active")))
        # ⭐ The witness that is not our bookkeeping.
        time.sleep(0.5)
        onRead = show("re-read while ON", c.request("fly_get_state"))
        check("enable ⭐: the CMC really holds MOVE_Flying (5)",
              onRead.get("current_mode") == MOVE_FLYING, str(onRead.get("current_mode")))
        check("enable ⭐: state and the live mode AGREE",
              (on.get("state") == 1) == (onRead.get("current_mode") == MOVE_FLYING),
              "state=%s mode=%s" % (on.get("state"), onRead.get("current_mode")))

        # ---- DISABLE ----------------------------------------------------------------
        off = show("after disable", c.request("fly_set", enable=False))
        check("disable: state is 0, NOT FR_ERR_WRITE", off.get("state") == 0,
              str(off.get("state")))
        check("disable: active cleared", off.get("active") is False, str(off.get("active")))
        time.sleep(0.5)
        offRead = show("re-read while OFF", c.request("fly_get_state"))
        check("disable ⭐: the CMC left MOVE_Flying", offRead.get("current_mode") != MOVE_FLYING,
              str(offRead.get("current_mode")))
        # ⛔ RESTORE, verified -- not "we sent a disable", but "the mode is back".
        check("disable ⭐⭐: the captured MovementMode was RESTORED to the baseline",
              offRead.get("current_mode") == baseMode,
              "baseline=%s now=%s" % (baseMode, offRead.get("current_mode")))

        # ---- A SECOND CYCLE, because a gate that fires on the SECOND call is the one a
        #      single toggle cannot see (the disable path sets baseCaptured=false).
        on2 = show("enable again", c.request("fly_set", enable=True, speed=600.0,
                                             preset=0, noclip=False))
        check("second cycle: enable still returns 1", on2.get("state") == 1,
              str(on2.get("state")))
        off2 = show("disable again", c.request("fly_set", enable=False))
        check("second cycle: disable still returns 0", off2.get("state") == 0,
              str(off2.get("state")))
        final = show("final", c.request("fly_get_state"))
        check("teardown: fly is OFF and the mode is the baseline",
              final.get("active") is False and final.get("current_mode") == baseMode,
              "active=%s mode=%s (baseline %s)"
              % (final.get("active"), final.get("current_mode"), baseMode))

    print("\n%d check(s), %d failure(s)" % (10, len(fails)))
    if fails:
        print("FAILED: " + ", ".join(fails))
        return 1
    print("PASS -- the enable/disable gates do NOT fire on a working write, across two full "
          "cycles, and the MovementMode read back from the CMC agrees with `state` every time. "
          "⚠ REGRESSION HALF ONLY: the FR_ERR_WRITE path is not reachable from outside the "
          "process (see this file's header).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
