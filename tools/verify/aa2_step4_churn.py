"""AA2/AA3 step 4 — a CE freeze must re-acquire across object churn, and touch nothing else.

    py tools/verify/aa2_step4_churn.py

PRECONDITION, and it is on YOU: a CE freeze on `DumperTestHolder::HolderValue = 9999`
must already be TICKED (Property Search -> Freeze -> Create freeze script, then tick
the record in CE). This rig drives the churn and reads the result; it does not drive CE.

THE ROW: *"Now cause churn: kill/respawn the frozen actors, or cross a level-streaming
boundary, with the freeze still enabled. Success = the freeze re-acquires within one
rescan (~5 s) and nothing unrelated changes. Watch for any OTHER object's fields
changing — that is the old bug."*

WHY THIS IS THE HARD HALF. The defect being guarded is a freeze tick writing through a
cached pointer into a **recycled** block — an object of a *different class* that landed
in the freed slot. Steps 2 and 3 tested a quiescent pool, which cannot expose it. Churn
is the whole point, and no commercial game produces it on cue. `Spawn_DestroyHolders`
does `Destroy()` + `Empty()` + **`ForceGarbageCollection(true)`**, so the GObjects slots
are genuinely freed and available for reuse rather than merely unreferenced.

THE TWO THINGS THAT MAKE THE RESULT MEAN SOMETHING:

  * ⭐ **The new instances are seeded 1000+i, not 9999.** `Spawn_Holders` re-seeds from a
    fresh base after the destroy, so a post-churn read of 9999 can only come from the
    freeze re-acquiring. "It still says 9999" is not a reading that could have been
    true anyway.
  * ⭐ **The decoys are the "nothing unrelated changed" control, and they are adversarial.**
    `ADumperTestHolderDecoy` does NOT derive from the base, but its class NAME contains
    the base's AND it carries a field of the SAME name at the SAME offset. If a freeze
    tick ever wrote by name, or wrote into a recycled slot without re-checking
    `ClassPrivate`, the decoys are where it would land. They must stay at -1 throughout.

A wider "nothing else changed" sample is taken too: N unrelated live objects are walked
before and after and every field value compared.
"""
from __future__ import annotations

import argparse
import pathlib
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient           # noqa: E402
from ad4_contested import find_live_actor, invoke   # noqa: E402

BASE, DECOY = "DumperTestHolder", "DumperTestHolderDecoy"
FIELD = "HolderValue"
FROZEN = 9999.0


def i32(n):
    return int(n).to_bytes(4, "little", signed=True).hex()


def live_of(c, cls):
    r = c.request("find_instances", class_name=cls, limit=5000, exact_match=True)
    c.check_complete(r)
    return [i for i in r.get("instances", [])
            if i.get("class") == cls and not i["name"].startswith("Default__")]


def field_of(c, addr, name=FIELD):
    w = c.request("walk_instance", addr=addr, array_limit=1)
    for f in w.get("fields", []):
        if f.get("name") == name:
            return f.get("value")
    return None


def frozen_frac(c, rows, n):
    got = [field_of(c, r["addr"]) for r in rows[:n]]
    hit = sum(1 for v in got if v is not None and abs(float(v) - FROZEN) < 0.01)
    return hit, len(got), got[:4]


def fingerprint(c, rows):
    """{addr: {field: value}} for a set of unrelated objects."""
    out = {}
    for r in rows:
        w = c.request("walk_instance", addr=r["addr"], array_limit=1)
        out[r["addr"]] = {f.get("name"): f.get("value") for f in w.get("fields", [])}
    return out


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--count", type=int, default=100)
    ap.add_argument("--sample", type=int, default=12)
    ap.add_argument("--unrelated", type=int, default=10)
    ap.add_argument("--settle", type=float, default=6.0, help="the row allows ~5 s for one rescan")
    a = ap.parse_args(argv)
    fails = []

    with PipeClient() as c:
        c.assert_build()
        c.ensure_scanned()
        act = find_live_actor(c)
        addr = act["addr"]
        fn = {f["name"]: f for f in
              c.request("walk_functions", addr=act["class_addr"])["functions"]}

        # ---------- 0. the freeze must ALREADY be holding, or nothing below tests churn ----
        base0, decoy0 = live_of(c, BASE), live_of(c, DECOY)
        hit, tot, sample = frozen_frac(c, base0, a.sample)
        print("[0] before churn: %d live %s, %d live %s" % (len(base0), BASE, len(decoy0), DECOY))
        print("    %d/%d sampled hold %s   e.g. %s" % (hit, tot, FROZEN, sample))
        if hit != tot or tot == 0:
            raise SystemExit("aa2: FAILED PRECONDITION -- the freeze is not holding before the "
                             "churn, so a post-churn reading would measure nothing. Tick the CE "
                             "record first.")
        d0 = {r["addr"]: field_of(c, r["addr"]) for r in decoy0}
        print("    decoys: %s" % sorted(set(d0.values())))

        # a wider control set: unrelated live objects, fingerprinted field-by-field
        others = [i for i in c.request("find_instances", class_name="Actor",
                                       limit=400).get("instances", [])
                  if i.get("class") not in (BASE, DECOY)
                  and not i["name"].startswith("Default__")][:a.unrelated]
        f0 = fingerprint(c, others)
        print("    fingerprinted %d unrelated objects (%d fields total)"
              % (len(f0), sum(len(v) for v in f0.values())))

        # ---------- 1. CHURN ----------
        print("\n[1] churn: destroy all %d + force GC, then spawn %d fresh" % (len(base0), a.count))
        invoke(c, addr, "Spawn_DestroyHolders")
        gone = live_of(c, BASE)
        print("    after destroy+GC: %d live %s" % (len(gone), BASE))
        # ⚠ Spawn_DestroyHolders empties SpawnedHolders, and Spawn_Decoys adds the
        # decoys to that SAME array -- so the churn destroys the control too. The first
        # run of this rig scored "decoys: 0 checked, 0 changed" as a PASS, which is a
        # vacuous control: an empty set cannot fail. Re-spawn them BEFORE the holders so
        # they are live for the whole re-acquisition window, and refuse to score if the
        # set is empty (below).
        invoke(c, addr, "Spawn_Decoys", parms_size=fn["Spawn_Decoys"]["parms_size"],
               params_hex=i32(len(decoy0) or 8))
        invoke(c, addr, "Spawn_Holders", parms_size=fn["Spawn_Holders"]["parms_size"],
               params_hex=i32(a.count) + "00")
        d_mid = {r["addr"]: field_of(c, r["addr"]) for r in live_of(c, DECOY)}
        print("    re-spawned %d decoys for the control, all at %s"
              % (len(d_mid), sorted(set(d_mid.values()))))

        # ---------- 2. does the freeze RE-ACQUIRE? ----------
        print("\n[2] re-acquire within ~%.0fs (fresh instances are seeded 1000+i, NOT %s)"
              % (a.settle, FROZEN))
        t0 = time.time()
        ok_reacq = False
        while time.time() - t0 < a.settle + 4:
            time.sleep(1.0)
            rows = live_of(c, BASE)
            if not rows:
                continue
            hit, tot, sample = frozen_frac(c, rows, a.sample)
            print("    t=+%4.1fs  live=%-4d  %2d/%d hold %s   e.g. %s"
                  % (time.time() - t0, len(rows), hit, tot, FROZEN, sample))
            if hit == tot and tot > 0:
                ok_reacq = (time.time() - t0) <= a.settle
                print("    -> re-acquired after %.1fs %s"
                      % (time.time() - t0, "(within budget)" if ok_reacq else "(TOO SLOW)"))
                break
        if not ok_reacq:
            fails.append("2: the freeze did not re-acquire every sampled instance within %.0fs"
                         % a.settle)

        # ---------- 3. nothing unrelated changed ----------
        print("\n[3] nothing unrelated changed")
        d1 = {r["addr"]: field_of(c, r["addr"]) for r in live_of(c, DECOY)}
        # ANTI-VACUITY: an empty control set cannot fail, so refuse to score it.
        if not d1:
            fails.append("3: NO decoys alive -- the control set is empty and 'nothing "
                         "changed' would be vacuous")
        moved = {k: (d_mid.get(k), v) for k, v in d1.items() if k in d_mid and d_mid[k] != v}
        wrong = {k: v for k, v in d1.items()
                 if v is not None and abs(float(v) - FROZEN) < 0.01}
        print("    decoys: %d checked, %d changed, %d wrongly holding %s  %s"
              % (len(d1), len(moved), len(wrong), FROZEN, moved or ""))
        if wrong:
            fails.append("3: %d DECOY(s) hold %s -- the freeze reached a class that does not "
                         "derive from the frozen one" % (len(wrong), FROZEN))
        if moved:
            fails.append("3: %d DECOY value(s) changed -- a write reached a class that does not "
                         "derive from the frozen one: %s" % (len(moved), moved))

        f1 = fingerprint(c, others)
        diffs = []
        for ad, before in f0.items():
            after = f1.get(ad, {})
            for k, v in before.items():
                if k in after and after[k] != v:
                    diffs.append((ad, k, v, after[k]))
        print("    unrelated objects: %d fields compared, %d changed"
              % (sum(len(v) for v in f0.values()), len(diffs)))
        for d in diffs[:6]:
            print("       %s %s: %s -> %s" % d)
        # a live game mutates its own state; only flag it, do not fail on it blindly
        if diffs:
            print("    NOTE: some drift is normal on a live actor (timers, transforms). "
                  "What matters is whether any of it is %s." % FROZEN)
            bad = [d for d in diffs if str(d[3]).startswith("9999")]
            if bad:
                fails.append("3: an unrelated field became %s -- the freeze wrote outside its "
                             "class: %s" % (FROZEN, bad[:3]))
        ok_unrelated = not [f for f in fails if f.startswith("3:")]

    print("\n" + "=" * 72)
    print("2 freeze re-acquires after destroy+GC+respawn : %s" % ("PASS" if ok_reacq else "FAIL"))
    print("3 nothing unrelated changed (decoys + sample) : %s" % ("PASS" if ok_unrelated else "FAIL"))
    print("\nAA2/AA3 step 4: %s" % ("PASS" if not fails else "FAIL"))
    for f in fails:
        print("   - %s" % f)
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
