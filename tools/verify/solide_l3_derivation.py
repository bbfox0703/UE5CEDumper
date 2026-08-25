"""Solide L3 — a class hold must follow DERIVATION, not a name substring.

    py tools/verify/solide_l3_derivation.py

WHY THIS HAS NEVER BEEN FALSIFIABLE HERE UNTIL NOW.
`Aura::FindInstancesDerivedFrom` is specified to hold a class *and every subclass of
it*. The obvious wrong implementation — matching class NAMES by substring — passes
every check you can build from a commercial game's class pool, because in practice
subclasses are usually named after their base. `[A6-DERIVE-2026-08-22]` got as close
as a real title allows: `StaticMeshActor` derives from `AActor` without being named
"Actor*" (the positive), and `ActorSequence` is a same-prefix stranger (the negative).
Good, but assembled from whatever a shipped game happened to contain.

The DumperTest spawner supplies the pair on purpose, and it is INVERTED so the two
rules cannot agree:

    class                       name contains        derives from
                                "DumperTestHolder"   ADumperTestHolder
    ADumperTestHolder                YES                  YES   (itself)
    ADumperTestDerivedHolder         NO                   YES   <- substring MISSES it
    ADumperTestHolderDecoy           YES                  NO    <- substring CATCHES it

  ==> A derivation walk holds {Holder, Derived} and skips Decoy.
      A substring match holds {Holder, Decoy} and skips Derived.
      There is no result that satisfies both, so this run cannot be passed by the
      wrong implementation. That is the whole point of the fixture.

⭐ The inversion is already visible before anything is forced: `find_instances`
matches by substring, so asking it for "DumperTestHolder" returns the DECOY and not
the DERIVED. This rig prints that first, as the proof that the two rules really do
disagree on this fixture rather than being distinguished only in theory.

TWO INDEPENDENT READS, because the hold's own tally is not evidence about the hold:
  (1) `force_field` -> `held`, the DLL's count.
  (2) every instance RE-WALKED and its HolderValue read back — the value is seeded
      DISTINCT per instance (1000 + index), so "restored the wrong base to all of
      them" is visible here and invisible to any count.
"""
from __future__ import annotations

import argparse
import json
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient           # noqa: E402
from ad4_contested import find_live_actor, invoke   # noqa: E402

BASE = "DumperTestHolder"
DERIVED = "DumperTestDerivedHolder"
DECOY = "DumperTestHolderDecoy"
FIELD = "HolderValue"
FORCED = 777.0


def i32(n):
    return int(n).to_bytes(4, "little", signed=True).hex()


def live_of(c, cls):
    """Instances whose class is EXACTLY cls (find_instances matches by substring)."""
    # exact_match=True: find_instances matches by SUBSTRING by default, which on this
    # fixture returns the DECOY under the base's name -- the very confusion under test.
    r = c.request("find_instances", class_name=cls, limit=5000, exact_match=True)
    c.check_complete(r)
    return [i for i in r.get("instances", [])
            if i.get("class") == cls and not i["name"].startswith("Default__")]


def read_field(c, addr, name=FIELD):
    w = c.request("walk_instance", addr=addr, array_limit=1)
    for f in w.get("fields", []):
        if f.get("name") == name:
            return f.get("value")
    return None


def main(argv=None):
    # ⚠ Every print here carries box-drawing and warning glyphs, and this machine's console
    # is cp950: the run DIED mid-report with UnicodeEncodeError on the very line that
    # explains why a truncated hold is inconclusive. A rig that crashes while delivering
    # its own caveat is worse than one that never printed it.
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass

    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--base", type=int, default=300)
    ap.add_argument("--derived", type=int, default=50)
    ap.add_argument("--decoys", type=int, default=8)
    ap.add_argument("--sample", type=int, default=12, help="instances to re-walk per class")
    a = ap.parse_args(argv)
    fails = []

    with PipeClient() as c:
        c.assert_build()
        c.ensure_scanned()
        act = find_live_actor(c)
        addr = act["addr"]
        fn = {f["name"]: f for f in
              c.request("walk_functions", addr=act["class_addr"])["functions"]}

        # ---------- build the set ----------
        invoke(c, addr, "Spawn_Holders", parms_size=fn["Spawn_Holders"]["parms_size"],
               params_hex=i32(a.base) + "00")
        invoke(c, addr, "Spawn_Holders", parms_size=fn["Spawn_Holders"]["parms_size"],
               params_hex=i32(a.derived) + "01")
        invoke(c, addr, "Spawn_Decoys", parms_size=fn["Spawn_Decoys"]["parms_size"],
               params_hex=i32(a.decoys))

        base, derived, decoy = live_of(c, BASE), live_of(c, DERIVED), live_of(c, DECOY)
        print("spawned: %s=%d  %s=%d  %s=%d"
              % (BASE, len(base), DERIVED, len(derived), DECOY, len(decoy)))
        if not (base and derived and decoy):
            raise SystemExit("l3: FAILED -- the discriminating set is incomplete; "
                             "nothing below can distinguish anything")

        # ---------- the inversion, shown before the test ----------
        sub = c.request("find_instances", class_name=BASE, limit=5000)  # substring on purpose
        subclasses = sorted({i.get("class") for i in sub.get("instances", [])})
        print("\n[0] the two rules DISAGREE on this fixture (shown, not assumed)")
        print("    substring '%s' matches classes: %s" % (BASE, subclasses))
        inverted = (DECOY in subclasses) and (DERIVED not in subclasses)
        print("    -> catches the DECOY, misses the DERIVED: %s" % inverted)
        if not inverted:
            fails.append("0: the fixture is not inverted here, so the run proves nothing")

        # ---------- baseline values ----------
        def snapshot(rows):
            return {r["addr"]: read_field(c, r["addr"]) for r in rows[:a.sample]}
        b0, d0, k0 = snapshot(base), snapshot(derived), snapshot(decoy)
        distinct = len(set(b0.values()))
        print("\n[1] baseline: %d sampled base instances hold %d DISTINCT values "
              "(e.g. %s)" % (len(b0), distinct, sorted(b0.values())[:4]))
        if distinct < 2:
            fails.append("1: base instances are not distinct; a per-instance restore "
                         "defect would be invisible")

        try:
            # ---------- the hold ----------
            # `value` must be a JSON NUMBER. Sending the string "777.0" gets
            # `type must be number, but is string` from request.value("value", 0.0) --
            # and the protocol doc only shows the bool form, so the numeric form's
            # value type is not written down anywhere.
            r = c.request("force_field", class_name=BASE, field_name=FIELD,
                          kind="numeric", value=FORCED)
            print("\n[2] force_field %s.%s = %s -> held=%s resolved=%s truncated=%s"
                  % (BASE, FIELD, FORCED, r.get("held"), r.get("resolved"), r.get("truncated")))

            def held_frac(rows, label):
                got = {ad: read_field(c, ad) for ad in list(rows)[:a.sample]}
                n = sum(1 for v in got.values() if v is not None and abs(float(v) - FORCED) < 0.01)
                print("    %-26s %d/%d carry %s" % (label, n, len(got), FORCED))
                return n, len(got), got

            # ⭐ THE ARITHMETIC IS THE PROOF, when the walk was not truncated:
            #      derivation  -> held == base + derived
            #      substring   -> held == base + decoys
            # Two different numbers from the same pool, so `held` alone decides it.
            # Only meaningful with truncated=False -- under the cap, "the decoys were
            # not touched" could just mean the walk never reached them.
            held = r.get("held")
            if r.get("truncated"):
                print("    ⚠ truncated -- the cap fired, so 'decoys untouched' is NOT "
                      "decidable from this run. Re-run with fewer instances.")
                fails.append("2: run was truncated; the negative half is undecidable")
            else:
                want_deriv = len(base) + len(derived)
                want_subst = len(base) + len(decoy)
                print("    held=%s   derivation predicts %d   substring predicts %d"
                      % (held, want_deriv, want_subst))
                if held != want_deriv:
                    fails.append("2: held=%s, derivation predicts %d (substring would be %d)"
                                 % (held, want_deriv, want_subst))

            nb, tb, _ = held_frac(b0, BASE + " (must ALL be held)")
            nd, td, _ = held_frac(d0, DERIVED + " (must ALL be held)")
            nk, tk, kv = held_frac(k0, DECOY + " (must be UNTOUCHED)")

            ok_base = nb == tb
            ok_deriv = nd == td
            ok_decoy = nk == 0 and all(
                v is not None and abs(float(v) - float(k0[ad])) < 0.01 for ad, v in kv.items())
            if not ok_base:
                fails.append("2: only %d/%d base instances held" % (nb, tb))
            if not ok_deriv:
                fails.append("2: %d/%d DERIVED instances held -- the hold is not "
                             "following the super chain" % (nd, td))
            if not ok_decoy:
                fails.append("2: %d/%d DECOY instances were touched -- the hold is "
                             "matching the class NAME, not derivation" % (nk, tk))

            # ---------- restore ----------
            c.request("reset_field", class_name=BASE, field_name=FIELD)
            back = {ad: read_field(c, ad) for ad in list(b0)[:a.sample]}
            same = sum(1 for ad, v in back.items()
                       if v is not None and abs(float(v) - float(b0[ad])) < 0.01)
            print("\n[3] reset_field -> %d/%d base instances back at their OWN prior value"
                  % (same, len(back)))
            ok_restore = same == len(back)
            if not ok_restore:
                fails.append("3: only %d/%d restored to their own base -- a single shared "
                             "base restored to all is Solide L4" % (same, len(back)))
        finally:
            c.request("reset_all_fields")
            invoke(c, addr, "Spawn_DestroyHolders")

    print("\n" + "=" * 72)
    print("0 the two rules genuinely disagree on this fixture : %s" % ("PASS" if inverted else "FAIL"))
    print("1 base instances carry DISTINCT values             : %s" % ("PASS" if distinct >= 2 else "FAIL"))
    print("2a base held      2b DERIVED held (derivation)     : %s / %s"
          % ("PASS" if ok_base else "FAIL", "PASS" if ok_deriv else "FAIL"))
    print("2c DECOY untouched (not a name match)              : %s" % ("PASS" if ok_decoy else "FAIL"))
    print("3 each restored to its OWN base (L4)               : %s" % ("PASS" if ok_restore else "FAIL"))
    print("\nSolide L3 (+ the L4 restore half): %s" % ("PASS" if not fails else "FAIL"))
    for f in fails:
        print("   - %s" % f)
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
