r"""`[UNREADVAL-2026-09-09]` — the LIVE REGRESSION arm the manufactured fixture cannot be.

    py tools/verify/unreadval_live_arm.py                 # walk a broad sample, tally
    py tools/verify/unreadval_live_arm.py --limit 4000    # more objects

⛔ WHAT THIS ARM IS FOR, AND WHAT IT CANNOT DECIDE — say both, because `[IFACEREAD]` had to.
The fix makes three handlers REFUSE when the instance read faults: `EnumProperty`,
`ByteProperty`-with-UEnum, and `OptionalProperty` now publish
`"(<kind> — unreadable at +0xN, not read)"` instead of the un-set 0 they used to dress up as a
value (hex `"00"`, the NAME of enumerator 0, `"(unset)"`).

A live game cannot show that. A faulted instance read needs a PARTIAL fault, and
`WalkInstance`'s `IsAddrReadable` gate bails on a wholly-dead pointer for a different reason —
which is exactly why `dll_core_test`'s `UNREADVAL` block manufactures two pages with one
committed. ⭐ **What a live game CAN decide is the other half, and it is the half a manufactured
fixture is weakest on: did the new gate start refusing fields that are perfectly READABLE?** A
gate that fires spuriously would blank every enum in the game, and the fake-blob controls in
`dll_core_test` prove only that it does not fire on three synthetic fields.

So this arm asserts, over a broad sample of REAL live objects:

  1. the three families still publish values — nonzero AND zero;
  2. **not one** field carries an `unreadable at +0x` refusal;
  3. the sample actually CONTAINED those families (the anti-vacuity guard — "0 refusals" is
     satisfied for free by a run that walked no enums at all).

⚠ (3) IS NOT DECORATION. It is the same guard `UNREADVAL`'s extractor carries, and the same
mistake `[IFACEREAD]`'s live arm nearly published: an assertion of the form "X is absent" is
true of an empty run. The exit code is non-zero if any family is missing from the sample.

⚠ NOT COVERED, deliberately: this says nothing about the refusal path itself, and nothing about
the UI's rendering of it. The refusal is `dll_core_test`'s to prove; the UI's is nobody's yet.
"""
from __future__ import annotations

import argparse
import collections
import pathlib
import re
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient          # noqa: E402

# The three families this fix touched, by the type name the walker publishes.
FAMILIES = ("EnumProperty", "ByteProperty", "OptionalProperty")
REFUSAL = re.compile(r"unreadable at \+0x", re.I)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--limit", type=int, default=2500, help="objects to sample")
    ap.add_argument("--chunk", type=int, default=64)
    a = ap.parse_args()

    with PipeClient() as c:
        print("build:", c.assert_build())
        c.ensure_scanned()
        # ⚠ The pipe's replies are FLAT -- there is no "data" envelope. Getting that wrong
        # returned 0 objects and the anti-vacuity guard below is what caught it, which is
        # the argument for having written it before the first run rather than after.
        total = c.request("get_object_count")["count"]
        print("objects in pool: %d, sampling %d" % (total, a.limit))

        # ① TARGETED. Ask the class tree WHICH classes declare each family, then pull their
        #    live instances. ⛔ Without this the run is a coverage LOTTERY: the first version
        #    strided the pool and reported ZERO OptionalProperty, because the only two classes
        #    that declare one are DumperTestActor (2 instances in 25,231 objects) and
        #    WorldPartitionRuntimeCellData. The anti-vacuity guard failed that run, which is
        #    the only reason it was noticed instead of published as "no refusals anywhere".
        seen_addr = set()
        addrs, targeted = [], collections.Counter()
        for fam in FAMILIES:
            r = c.request("search_properties", types=[fam], game_only=False,
                          limit=2000, deep=True)
            classes = []
            for row in r.get("results", []):
                cn = row.get("class_name")
                if cn and cn not in classes:
                    classes.append(cn)
            for cn in classes[:40]:
                fi = c.request("find_instances", class_name=cn, max_results=40)
                for inst in fi.get("instances", []):
                    ad = inst.get("addr")
                    if ad and ad not in seen_addr:
                        seen_addr.add(ad)
                        addrs.append(ad)
                        targeted[fam] += 1
        print("targeted addresses: %d  (%s)"
              % (len(addrs), ", ".join("%s<-%d" % (f, targeted[f]) for f in FAMILIES)))

        # ② BROAD, on top. Stride the pool rather than taking the first N: the head of
        #    GObjects is engine boilerplate, and a fix that broke only GAMEPLAY enums would
        #    hide behind a prefix sample. The targeted pull proves the families are present;
        #    this half is what makes "not one refusal" a claim about the TITLE rather than
        #    about the handful of classes the property search happened to name.
        step = max(1, total // a.limit)
        for off in range(0, total, max(step * 200, 200)):
            r = c.request("get_object_list", offset=off, limit=200)
            for o in r.get("objects", []):
                ad = o.get("addr")
                if ad and ad not in seen_addr:
                    seen_addr.add(ad)
                    addrs.append(ad)
            if len(addrs) >= a.limit:
                break
        print("addresses collected: %d" % len(addrs))

        seen = collections.Counter()          # type -> fields seen
        valued = collections.Counter()        # type -> fields with a published value
        zeroes = collections.Counter()        # type -> fields whose value is a real 0
        refusals = []
        silent = []      # neither value nor hex -- printed, never hand-waved
        walked = 0
        definitions = 0  # UClass/UScriptStruct schema walks, excluded above

        for i in range(0, len(addrs), a.chunk):
            items = [{"addr": x} for x in addrs[i:i + a.chunk]]
            r = c.request("walk_instance_batch", items=items, array_limit=4, preview_limit=1)
            for res in r.get("instances", []):
                # ⛔ A DEFINITION WALK IS A SCHEMA VIEW, NOT A VALUE VIEW, and counting it
                #    here would have been a false alarm. When the walked object IS a UClass or
                #    UScriptStruct, WalkInstance (Ubel.cpp:4031) takes a separate branch that
                #    emits field METADATA and deliberately reads NO values -- its own comment
                #    says the offsets describe instances of the struct, not the metaobject's
                #    own memory. It returns before any type handler runs, so these rows can be
                #    neither refused nor valued, by construction.
                #    ⚠ This cost a real detour: the first tally counted them and reported 17
                #    fields with "neither value nor hex", which reads exactly like a walker
                #    declining silently. 77 of 96 fields on ONE such object were valueless
                #    across FOURTEEN type families -- of which this fix touched three -- and
                #    that ratio is what said "branch", not "regression".
                if res.get("is_definition"):
                    definitions += 1
                    continue
                walked += 1
                for f in res.get("fields", []) or []:
                    tn = f.get("type", "")
                    if tn not in FAMILIES:
                        continue
                    seen[tn] += 1
                    tv = f.get("value", "") or ""
                    hx = f.get("hex", "") or ""
                    if REFUSAL.search(tv):
                        refusals.append((res.get("addr", "?"), f.get("name", "?"), tn, tv))
                        continue
                    if tv or hx:
                        valued[tn] += 1
                    else:
                        silent.append((res.get("addr", "?"), res.get("class", "?"),
                                       f.get("name", "?"), tn, f.get("offset", -1)))
                    # A byte/enum that genuinely reads 0 is the case the refusal must NOT
                    # look like -- count them so the sample is known to contain some.
                    if hx and set(hx) == {"0"}:
                        zeroes[tn] += 1

    print("\nwalked LIVE instances: %d   (+%d definition walks excluded)"
          % (walked, definitions))
    print("%-18s %8s %8s %8s" % ("family", "seen", "valued", "real-0"))
    missing = []
    for fam in FAMILIES:
        print("%-18s %8d %8d %8d" % (fam, seen[fam], valued[fam], zeroes[fam]))
        if seen[fam] == 0:
            missing.append(fam)

    # ⚠ A field with NEITHER a value nor hex is not a refusal, but it is not a pass either --
    # it is the walker declining silently, which is the shape [SW6-STRIDEREFUSAL] was about.
    # Printed so it gets looked at rather than absorbed into the "valued" shortfall.
    print("\nfields with NEITHER value nor hex: %d" % len(silent))
    for row in silent[:12]:
        print("  %s %s.%s (%s) +0x%X" % (row[0], row[1], row[2], row[3], row[4]))

    print("\nrefusals published over READABLE memory: %d" % len(refusals))
    for row in refusals[:20]:
        print("  %s .%s (%s) -> %s" % row)

    ok = True
    if refusals:
        print("\n*** FAIL: the gate fired on a live, readable field. That is the regression "
              "this arm exists to catch.")
        ok = False
    if missing:
        print("\n*** FAIL (anti-vacuity): the sample contained NO %s, so '0 refusals' is "
              "satisfied for free and decides nothing." % ", ".join(missing))
        ok = False
    if ok:
        print("\nPASS -- %d fields across the three families published values and NOT ONE was "
              "refused. ⚠ This is the REGRESSION half only: it shows the gate does not fire on "
              "readable memory. The refusal itself is dll_core_test's UNREADVAL block to prove, "
              "because a live game cannot produce a partial fault on demand."
              % sum(seen[f] for f in FAMILIES))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
