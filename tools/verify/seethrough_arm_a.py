"""M1-M5 step 1 ARM (a) - moving the view must RESTORE a previously-hidden actor.

    py tools/verify/seethrough_arm_a.py

THE ARM. See-through hides whatever occludes the camera->view-forward trace and must restore
each actor as soon as it stops occluding. Arm (a) is the "the player moves" case, and the
register classified it human-only: "needs a human moving the character".

⭐ IT IS NOT HUMAN-ONLY. `teleport_relative` is an ordinary pipe command and DumperTest has a
real player pawn (`ADumperTestCharacter : ACharacter`), so the movement can be driven over the
same connection that owns the See-through session - which matters, because the DLL disables
See-through when the pipe client disconnects, so a two-process arm would measure the disabled
state and call it a pass.

WHAT MAKES THE RESULT MEAN SOMETHING, in order:

  1. POSITIVE CONTROL - the actor must be confirmed hidden by its OWN `bHidden` bit, not by
     the hider's tally. `seethrough_get_state`'s `hidden_count` is the DLL's own bookkeeping;
     auditing the hide with it is letting the accused be the witness.
  2. NEGATIVE CONTROL - hold still for the same duration first and require the actor to STAY
     hidden. Without it, "it became visible after I moved" is equally consistent with the hide
     lapsing on its own, and the arm would pass on a feature that simply forgets.
  3. THE ARM - move, then require that same actor's `bHidden` to go FALSE.
  4. STILL ACTIVE - `active` must remain true at the end. If See-through switched itself off,
     the restore proves nothing about the move; it is just arm (c) again.

⚠ `walk_instance` renders a bit-field bool as `true (bit 7, mask 0x80)`, so a `== "true"`
test reads a HIDDEN actor as not hidden. Parse the leading token.
"""
from __future__ import annotations

import argparse
import pathlib
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient  # noqa: E402


def bhidden(c, addr):
    """True/False/None - the actor's OWN bHidden bit, parsed from the leading token."""
    w = c.request("walk_instance", addr=addr, array_limit=1)
    for f in w.get("fields", []):
        if f.get("name") == "bHidden":
            v = str(f.get("value", "")).strip().lower()
            return v.startswith("true")
    return None


def state(c):
    r = c.request("seethrough_get_state")
    return bool(r.get("active")), int(r.get("hidden_count", 0) or 0), \
        r.get("hidden_actors") or []


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--hold", type=float, default=6.0, help="seconds to hold still (control)")
    ap.add_argument("--distance", type=float, default=900.0)
    ap.add_argument("--steps", type=int, default=6)
    a = ap.parse_args(argv)
    fails = []

    with PipeClient() as c:
        c.assert_build()
        c.ensure_scanned()

        c.request("seethrough_set", enable=True, count=1)
        time.sleep(2.5)
        act, hc, addrs = state(c)
        print("[0] enabled: active=%s hidden_count=%d hidden_actors=%s" % (act, hc, addrs))
        if hc < 1 or not addrs:
            raise SystemExit("arm(a): nothing is being hidden from this pose -- every assertion "
                             "below would be vacuous. Re-run from a pose with an occluder.")
        target = addrs[0]

        # 1. POSITIVE CONTROL -- the actor's own bit, not the hider's tally
        h0 = bhidden(c, target)
        print("[1] positive control: %s bHidden=%s (must be True)" % (target, h0))
        if h0 is not True:
            fails.append("1: the DLL names %s as hidden but its own bHidden is %s -- detector (2) "
                         "cannot fire, so nothing below can be certified" % (target, h0))

        # 2. NEGATIVE CONTROL -- hold still; it must STAY hidden
        time.sleep(a.hold)
        h1 = bhidden(c, target)
        act1, hc1, _ = state(c)
        print("[2] negative control: after %.0fs holding still, bHidden=%s active=%s count=%d"
              % (a.hold, h1, act1, hc1))
        if h1 is not True:
            fails.append("2: the actor stopped being hidden WITHOUT any movement, so a restore "
                         "after moving would prove nothing about the movement")

        # 3. THE ARM -- move, and require that same actor to be restored
        print("[3] moving: %d x teleport_relative(distance=%.0f)" % (a.steps, a.distance))
        for i in range(a.steps):
            c.request("teleport_relative", distance=a.distance, horizontal=True)
            time.sleep(0.2)
        time.sleep(3.0)
        h2 = bhidden(c, target)
        act2, hc2, addrs2 = state(c)
        print("    after moving: %s bHidden=%s ; active=%s hidden_count=%d actors=%s"
              % (target, h2, act2, hc2, addrs2))
        if h2 is not False:
            fails.append("3: after moving away, %s is still bHidden=%s -- the actor was left "
                         "invisible, which is exactly what arm (a) is for" % (target, h2))

        # 4. the restore must be attributable to the MOVE, not to See-through switching off
        if not act2:
            fails.append("4: See-through is no longer active, so the restore is arm (c)/(d) "
                         "behaviour rather than the move-driven restore this arm tests")
        print("[4] still active at the end: %s (must be True)" % act2)

        c.request("seethrough_set", enable=False, count=1)

    print("")
    print("=" * 72)
    print("M1-M5 step 1 arm (a) -- move restores a hidden actor: %s"
          % ("PASS" if not fails else "FAIL"))
    for f in fails:
        print("   - %s" % f)
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
