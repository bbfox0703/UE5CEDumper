"""FREEZESCOPE step 8 - a PRE-CONTRACT-3 freeze script must still run, and stay EXACT-CLASS.

    py tools/verify/freezescope_step8.py spawn [--count 30]
    py tools/verify/freezescope_step8.py read
    py tools/verify/freezescope_step8.py verdict --phase arm|control [--value 9999]

THE ROW (step 8, the backward-compatibility control): *"tick an older saved `.CT` whose freeze
script predates contract 3 -> it still runs and still holds its exact-class pool. The flag defaults
off and the handler clears it, so an old script must be unaffected."*

⭐ THE BLOCKER WAS "no pre-contract-3 `.CT` exists on this machine". It is retired by locating the
contract where it actually lives. A `.CT` bakes NO contract at all (`grep -c CONTRACT
scripts/UE5CEDumper.CT` = 0) and neither does the generated freeze script - the version is carried
by the HELPER, as `local UE5_SCRIPT_CONTRACT = <n>`. And an old helper is in git:

    git show 04d40803^:scripts/ue5_freeze_helper.lua      -> v1.1, UE5_SCRIPT_CONTRACT = 2

a period artifact from commit 661c3925 (2026-08-16), which predates contract 3 (2c2a950c,
2026-08-19). So the fixture is real, not reconstructed - the objection the row records ("it would
have to be reconstructed, which tests the reconstruction") does not apply.

⭐ WHY `DumperTestHolder` AND NOT THE CLASS THE OTHER FREEZE ROWS USE. Exact-vs-derived is the whole
assertion, so the base needs a real subclass. `ADumperTestDerivedHolder : public ADumperTestHolder`
exists for exactly this, and `ADumperTestHolderDecoy` does NOT derive while its NAME contains the
base's - so a scope computed from a string test fails visibly. `HolderValue` is seeded **1000 + i**,
DISTINCT per instance, so an unfrozen instance cannot be mistaken for a frozen one.

THE TWO PHASES, and the second is what makes the first mean anything:

    arm      helper 1.1 (contract 2)  -> base held, DERIVED **untouched**, decoy untouched
    control  helper 1.5 (contract 3)  -> base held AND derived held

Without the control, "the derived pool was untouched" is equally consistent with the freeze never
having run at all - which is the other way step 8 could fail.
"""
from __future__ import annotations

import argparse
import pathlib
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from pipe_client import PipeClient                      # noqa: E402
from ad4_contested import find_live_actor, invoke        # noqa: E402

BASE = "DumperTestHolder"
DERIVED = "DumperTestDerivedHolder"
DECOY = "DumperTestHolderDecoy"
FIELD = "HolderValue"


def i32(n):
    return int(n).to_bytes(4, "little", signed=True).hex()


def live(c, cls):
    r = c.request("find_instances", class_name=cls, limit=500, exact_match=True)
    return [i for i in r.get("instances", [])
            if i.get("class") == cls and not i["name"].startswith("Default__")]


def values(c, cls, cap=30):
    out = []
    for row in live(c, cls)[:cap]:
        w = c.request("walk_instance", addr=row["addr"], array_limit=1)
        for f in w.get("fields", []):
            if f.get("name") == FIELD:
                out.append(float(f.get("value")))
                break
    return out


def spawn(count):
    with PipeClient() as c:
        c.assert_build()
        c.ensure_scanned()
        act = find_live_actor(c)
        fns = {f["name"]: f for f in
               c.request("walk_functions", addr=act["class_addr"])["functions"]}
        ps = fns["Spawn_Holders"]["parms_size"]
        invoke(c, act["addr"], "Spawn_Holders", parms_size=ps, params_hex=i32(count) + "00")
        time.sleep(1.2)
        invoke(c, act["addr"], "Spawn_Holders", parms_size=ps, params_hex=i32(count) + "01")
        time.sleep(1.5)
        # The decoy pool must be NON-EMPTY or its control is vacuous: "no decoy was held" is
        # trivially true of zero decoys, and the decoy is the half that catches a scope
        # computed from the class NAME rather than from derivation.
        dps = fns["Spawn_Decoys"]["parms_size"]
        invoke(c, act["addr"], "Spawn_Decoys", parms_size=dps, params_hex=i32(8))
        time.sleep(1.2)
        for cls in (BASE, DERIVED, DECOY):
            print("  %-26s live=%d" % (cls, len(live(c, cls))))
    return 0


def read():
    with PipeClient() as c:
        c.assert_build()
        c.ensure_scanned()
        for cls in (BASE, DERIVED, DECOY):
            v = values(c, cls)
            print("  %-26s n=%-3d %s" % (cls, len(v), sorted(set(v))[:6]))
    return 0


def verdict(phase, value):
    """Score a phase. `arm` and `control` differ ONLY in what the derived pool must do."""
    fails = []
    with PipeClient() as c:
        c.assert_build()
        c.ensure_scanned()
        b, d, k = values(c, BASE), values(c, DERIVED), values(c, DECOY)
        heldb = sum(1 for x in b if abs(x - value) < 0.01)
        heldd = sum(1 for x in d if abs(x - value) < 0.01)
        heldk = sum(1 for x in k if abs(x - value) < 0.01)
        print("[%s] %s: %d/%d at %s   %s: %d/%d at %s   %s: %d/%d at %s"
              % (phase, BASE, heldb, len(b), value, DERIVED, heldd, len(d), value,
                 DECOY, heldk, len(k), value))
        print("      base sample    %s" % sorted(set(b))[:6])
        print("      derived sample %s" % sorted(set(d))[:6])

        if not b:
            fails.append("no live %s -- run `spawn` first" % BASE)
        if not d:
            fails.append("no live %s -- the exact-vs-derived assertion would be vacuous" % DERIVED)

        # BOTH phases: the base pool must be held. In the arm this is what proves the
        # contract-2 script still RUNS against the contract-3 DLL at all.
        if b and heldb != len(b):
            fails.append("the BASE pool is not fully held (%d/%d) -- in the arm that means the old "
                         "script did not run, not that its scope was narrow" % (heldb, len(b)))

        if phase == "arm":
            if heldd:
                fails.append("%d/%d DERIVED instances were held by a contract-2 helper -- it must "
                             "not reach subclasses" % (heldd, len(d)))
        else:
            if d and heldd != len(d):
                fails.append("the CONTROL did not hold the derived pool (%d/%d), so the arm's empty "
                             "derived pool proves nothing about scope" % (heldd, len(d)))
        if heldk:
            fails.append("%d DECOY instances were held -- scope is matching on the NAME, not on "
                         "derivation" % heldk)

    print("")
    print("=" * 72)
    print("FREEZESCOPE step 8 [%s]: %s" % (phase, "PASS" if not fails else "FAIL"))
    for f in fails:
        print("   - %s" % f)
    return 1 if fails else 0


if __name__ == "__main__":
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("verb", choices=["spawn", "read", "verdict"])
    ap.add_argument("--count", type=int, default=30)
    ap.add_argument("--phase", choices=["arm", "control"], default="arm")
    ap.add_argument("--value", type=float, default=9999.0)
    a = ap.parse_args()
    if a.verb == "spawn":
        sys.exit(spawn(a.count))
    if a.verb == "read":
        sys.exit(read())
    sys.exit(verdict(a.phase, a.value))
