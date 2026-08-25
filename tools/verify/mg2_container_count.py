"""MG2 -- container header-count vs rendered-row-count, and the TSet element halves.

    py tools/verify/mg2_container_count.py

STEP 1 asks: for a container BELOW the array limit, does the header's count agree
with the number of rows actually rendered -- including after an element is removed?

THE VACUITY QUESTION COMES FIRST, because if the two numbers share a source the
check can only ever agree with itself. They do not, and it is checked in code
before it is checked in the game:

    Ubel.cpp:4410   fv.mapCount = sa.MaxIndex - sa.NumFreeIndices;   <- TSparseArray HEADER
    Ubel.cpp:4721   fv.setCount = sa.MaxIndex - sa.NumFreeIndices;
    Ubel.cpp:3682   WalkInstance(..., int32_t arrayLimit, ...)       <- the LIST is a capped walk

The count is read out of the container's own header; the element list is a separate
iteration bounded by arrayLimit. Independent.

  ==> AND IT IS PROVEN INDEPENDENT AT RUNTIME, not just argued. Phase 2 grows
      Map_Churn past the cap and requires the two numbers to DISAGREE. A check that
      can be made to fail on demand is worth something; one that has only ever been
      observed agreeing is not. This is the part a commercial game cannot give you --
      you cannot ask Elden Ring to add 300 map entries on command.

STEP 2 asks whether TSet<FName> / TSet<UObject*> elements still parse. Counting them
is not enough: a broken decode still produces N rows. So the FName set is checked
against its known seeded names, and the object set is RE-WALKED at the addresses it
reported, requiring each to be a real UObject of the expected class.

  !! The "open any UDataTable" half of step 2 is BLOCKED, not skipped, by
     [DTROWMAP-2026-08-23]: Ubel::ProbeRowMapOffset locates the wrong RowMap and can
     serve a NEIGHBOURING table's rows. Judging a DataTable render before that is
     fixed would be judging the wrong table's data.

THIS RIG MUTATES THE FIXTURE, and the next thing to run on the same process must know:
  * it removes one entry from Map_Churn and one from Set_Name (that is the test), so
    re-running it on one long-lived process keeps shrinking them -- Set_Name has only
    four seeds, and once empty MG2_RemoveOneSetEntry no-ops and [1b] correctly FAILS.
    Relaunch the sample rather than "fixing" that failure.
  * phase 2 leaves Map_Churn and Arr_Churn ~300 elements LARGER than it found them.
    That is deliberate (it is the independence control) and it is fine for V1a, which
    wants churn anyway -- but a later row that assumes the seeded 6 will be wrong.
"""
from __future__ import annotations

import argparse
import json
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient           # noqa: E402
from ad4_contested import find_live_actor, invoke   # noqa: E402

# Seeded by ADumperTestActor::BeginPlay -- DumperTestActor.cpp:136-143.
SEEDED_NAMES = {"Alpha", "Beta", "Gamma", "Delta"}
DEFAULT_LIMIT = 64          # Ubel.cpp:3682 default when the request omits array_limit


def field(c, addr, name, **kw):
    w = c.request("walk_instance", addr=addr, **kw)
    for f in w.get("fields", []):
        if f.get("name") == name:
            return f
    raise SystemExit("mg2: FAILED -- field %r not found on %s" % (name, addr))


def map_view(c, addr, **kw):
    f = field(c, addr, "Map_Churn", **kw)
    els = f.get("map_elements", [])
    return f.get("map_count"), len(els), [int(e["k"]) for e in els]


def set_view(c, addr, **kw):
    f = field(c, addr, "Set_Name", **kw)
    els = f.get("set_elements", [])
    return f.get("set_count"), len(els), [e["k"] for e in els]


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.parse_args(argv)
    fails = []

    with PipeClient() as c:
        c.assert_build()
        c.ensure_scanned()
        a = find_live_actor(c)
        addr = a["addr"]
        print("actor: %s @%s\n" % (a["name"], addr))

        # ---------- 1a. TMap under the cap: agree, then agree after a removal ----------
        print("[1a] TMap<int32,int32> Map_Churn -- under the cap, remove one")
        c0, n0, k0 = map_view(c, addr)
        print("     before: header count=%d  rows=%d  keys=%s" % (c0, n0, k0))
        if c0 >= DEFAULT_LIMIT:
            fails.append("1a precondition: Map_Churn is not below the cap (%d)" % c0)
        invoke(c, addr, "MG2_RemoveOneMapEntry")
        c1, n1, k1 = map_view(c, addr)
        print("     after : header count=%d  rows=%d  keys=%s" % (c1, n1, k1))
        expect_keys = sorted(k0)[1:]            # the mutator drops the LOWEST key
        ok_1a = (c0 == n0 and c1 == n1 and c1 == c0 - 1 and sorted(k1) == expect_keys)
        print("     header==rows both times, both dropped by exactly 1, and the "
              "REMAINING KEYS are the old set minus the lowest: %s" % ok_1a)
        if not ok_1a:
            fails.append("1a: counts %d/%d -> %d/%d, keys %s (expected %s)"
                         % (c0, n0, c1, n1, sorted(k1), expect_keys))

        # ---------- 1b. TSet under the cap ----------
        print("\n[1b] TSet<FName> Set_Name -- under the cap, remove one")
        s0, m0, e0 = set_view(c, addr)
        print("     before: header count=%d  rows=%d  elements=%s" % (s0, m0, e0))
        invoke(c, addr, "MG2_RemoveOneSetEntry")
        s1, m1, e1 = set_view(c, addr)
        print("     after : header count=%d  rows=%d  elements=%s" % (s1, m1, e1))
        gone = set(e0) - set(e1)
        ok_1b = (s0 == m0 and s1 == m1 and s1 == s0 - 1 and len(gone) == 1
                 and set(e1) < set(e0))
        print("     header==rows both times, dropped by 1, exactly one element "
              "removed (%s): %s" % (sorted(gone), ok_1b))
        if not ok_1b:
            fails.append("1b: %d/%d -> %d/%d, removed %s" % (s0, m0, s1, m1, sorted(gone)))

        # ---------- 2. THE INDEPENDENCE CONTROL: make the two numbers disagree ----------
        print("\n[2] independence control -- grow past the cap so header != rows")
        invoke(c, addr, "V1a_GrowContainers", parms_size=4,
               params_hex=(300).to_bytes(4, "little").hex())
        c2, n2, _ = map_view(c, addr, array_limit=DEFAULT_LIMIT)
        print("     array_limit=%d : header count=%d  rows=%d  -> %s"
              % (DEFAULT_LIMIT, c2, n2, "DISAGREE (correct)" if c2 != n2 else "AGREE (WRONG)"))
        c3, n3, _ = map_view(c, addr, array_limit=1024)
        print("     array_limit=1024: header count=%d  rows=%d  -> %s"
              % (c3, n3, "AGREE (correct)" if c3 == n3 else "DISAGREE (WRONG)"))
        ok_2 = (c2 > n2 == DEFAULT_LIMIT) and (c3 == n3 == c2)
        print("     the count is NOT derived from the row list, and the cap is what "
              "bounds the list: %s" % ok_2)
        if not ok_2:
            fails.append("2: capped %d/%d, uncapped %d/%d" % (c2, n2, c3, n3))

        # ---------- 3. step 2 -- TSet elements actually parse ----------
        print("\n[3] TSet elements parse (not merely count)")
        _, _, names = set_view(c, addr)
        name_ok = set(names) < SEEDED_NAMES and all(n in SEEDED_NAMES for n in names)
        print("     TSet<FName>   : %s -- all from the seeded set %s: %s"
              % (sorted(names), sorted(SEEDED_NAMES), name_ok))

        fo = field(c, addr, "Set_Object")
        els = fo.get("set_elements", [])
        print("     TSet<UObject*>: %d elements" % len(els))
        obj_ok = bool(els)
        for e in els:
            # Re-walk the address the set reported. A decode that merely LOOKS right
            # cannot survive being dereferenced as an object of the claimed class.
            w = c.request("walk_instance", addr=e["ka"])
            pv = next((f.get("value") for f in w.get("fields", [])
                       if f.get("name") == "PayloadValue"), None)
            good = w.get("ok") and w.get("class") == e.get("kc") and pv is not None
            print("       %s  set says class=%-18s re-walk says class=%-18s "
                  "PayloadValue=%-6s  %s"
                  % (e["ka"], e.get("kc"), w.get("class"), pv, "OK" if good else "MISMATCH"))
            obj_ok = obj_ok and good
        ok_3 = name_ok and obj_ok
        if not ok_3:
            fails.append("3: FName ok=%s, object ok=%s" % (name_ok, obj_ok))

    print("\n" + "=" * 72)
    print("1a TMap  header==rows, survives a removal   : %s" % ("PASS" if ok_1a else "FAIL"))
    print("1b TSet  header==rows, survives a removal   : %s" % ("PASS" if ok_1b else "FAIL"))
    print("2  the two numbers CAN disagree (not vacuous): %s" % ("PASS" if ok_2 else "FAIL"))
    print("3  TSet<FName> / TSet<UObject*> parse        : %s" % ("PASS" if ok_3 else "FAIL"))
    print("\nMG2 step 1 + the TSet half of step 2: %s" % ("PASS" if not fails else "FAIL"))
    for f in fails:
        print("   - %s" % f)
    print("\nSTILL BLOCKED: step 2's 'open any UDataTable' half, by "
          "[DTROWMAP-2026-08-23] --")
    print("the drill-down can serve a NEIGHBOURING table's rows, so a render check "
          "there would")
    print("be judging the wrong table's data.")
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
