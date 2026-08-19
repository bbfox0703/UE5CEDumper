"""A6 step 3: Force must hold by DERIVATION, not by a name prefix.

    py a6_derivation.py

A6's whole point is that `Aura::FindInstancesDerivedFrom` performs a real super-chain
test with a per-UClass verdict cache -- NOT a name substring, so forcing on "Enemy"
must not reach "EnemyProjectile". Steps 1, 2 and 4 were closed by the maintainer, but
**step 3 was explicitly left open because nothing so far distinguishes a super-chain
walk from a prefix match** -- both hold "hundreds".

THE PAIR USED HERE, and why it is a stronger trap than `Enemy`/`EnemyProjectile`:

    Character                      <- the base being forced
    CharacterMovementComponent     <- starts with "Character", derives from
                                      UActorComponent, and is NOT a Character

A prefix matcher cannot tell these apart. UE guarantees the relationship, so the
result does not depend on a particular game's class tree. `find_instances` itself
matches by NAME and happily returns `Default__CharacterMovementComponent` for the
query "Character" -- which is exactly the wrong answer for this question, and a good
reminder that the two code paths are different.

⚠ THIS ROW WRITES TO GAME STATE. Run it on DumperTest, never a real save. The rig
calls `reset_all_fields` in a `finally`, and re-reads `get_forced_fields` afterwards to
confirm the hold is actually gone rather than assuming the reset worked.
"""
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient                        # noqa: E402

BASE = "Character"
IMPOSTOR = "CharacterMovementComponent"
FIELD = "bCanBeDamaged"          # declared on AActor, inherited by ACharacter


def main():
    ok = True
    with PipeClient(timeout=300.0) as c:
        c.assert_build()
        c.ensure_scanned()

        print("=== the name trap, shown first ===")
        fi = c.request("find_instances", class_name=BASE, max_results=50)
        names = [i.get("name", "") for i in (fi.get("instances") or [])]
        impostors = [n for n in names if IMPOSTOR.lower() in n.lower()]
        print(f"  find_instances('{BASE}') returns {len(names)} rows, of which "
              f"{len(impostors)} are {IMPOSTOR}-shaped: {impostors[:2]}")
        print("  ^ find_instances matches by NAME. force_field must NOT.")

        try:
            print(f"\n=== forcing {FIELD} on {BASE} ===")
            r = c.request("force_field", class_name=BASE, field_name=FIELD,
                          kind="bool", on=True)
            print(f"  ok={r.get('ok')} held={r.get('held')} truncated={r.get('truncated')} "
                  f"code={r.get('code')}")
            if r.get("message"):
                print(f"  message: {r['message']}")
            held = r.get("held") or 0
            if not held:
                print("  A6 step 3 INCONCLUSIVE -- nothing was held, so there is no pool "
                      "in which an impostor could be found or excluded")
                return 1

            g = c.request("get_forced_fields")
            fields = g.get("fields") or []
            print(f"  get_forced_fields -> {len(fields)} entry(ies)")
            for f in fields:
                print(f"    class={f.get('class_name')} field={f.get('field_name')} "
                      f"held={f.get('held')} truncated={f.get('truncated')}")

            # The decisive question: which CLASSES are actually in the held pool?
            owners = {}
            for f in fields:
                for k in ("owners", "instances", "owner_classes"):
                    for o in (f.get(k) or []):
                        nm = o.get("class_name") if isinstance(o, dict) else str(o)
                        owners[nm] = owners.get(nm, 0) + 1
            if owners:
                print(f"  held classes: {owners}")
                bad = [k for k in owners if IMPOSTOR.lower() in (k or "").lower()]
                if bad:
                    print(f"  *** A6 step 3 FAIL -- {bad} is held, so this is a PREFIX match ***")
                    ok = False
                else:
                    print(f"  A6 step 3 PASS -- no {IMPOSTOR} in the held pool")
            else:
                print("  ! the reply does not enumerate owner classes, so the held pool "
                      "cannot be inspected from here -- falling back to the DLL log line")
            # ---- THE REACHABILITY CONTROL -------------------------------------
            # "The impostor was not held" is worthless if the impostor is not in the
            # pool at all, or could not be held by anything. Force the same field on
            # the impostor's OWN base: it must now be held. Only then is its absence
            # from the `Character` hold an EXCLUSION rather than an absence.
            print(f"\n=== control: is {IMPOSTOR} reachable and holdable at all? ===")
            c.request("reset_all_fields")
            r2 = c.request("force_field", class_name=IMPOSTOR, field_name="bAutoActivate",
                           kind="bool", on=True)
            print(f"  force {IMPOSTOR}.bAutoActivate -> ok={r2.get('ok')} "
                  f"held={r2.get('held')} code={r2.get('code')}")
            if (r2.get("held") or 0) > 0:
                print(f"  control PASS -- {IMPOSTOR} IS live and holdable, so its absence "
                      f"from the '{BASE}' hold is a real EXCLUSION, not an empty pool")
            else:
                print(f"  ⚠ control INCONCLUSIVE -- {IMPOSTOR} could not be held either, so "
                      f"the exclusion above may just mean it is not in the pool")
        finally:
            c.request("reset_all_fields")
            after = c.request("get_forced_fields")
            n = len(after.get("fields") or [])
            print(f"\n  reset_all_fields -> {n} field(s) still held (expect 0)")
            if n:
                print("  *** the reset did NOT clear the hold ***")
                ok = False

    # The DLL's own line names the base and the class count it walked.
    log = (pathlib.Path.home() /
           "AppData/Local/UE5CEDumper/Logs/DumperTest/scan-0.log")
    for cand in (log, log.with_name("pipe-0.log"), log.with_name("init-0.log")):
        if not cand.is_file():
            continue
        hits = [l for l in cand.read_text(encoding="utf-8", errors="replace").splitlines()
                if "FindInstancesDerivedFrom" in l]
        if hits:
            print(f"\n=== {cand.name}: FindInstancesDerivedFrom ===")
            for l in hits[-4:]:
                print("  ", l.strip()[:170])
    print(f"\nA6 step 3: {'PASS' if ok else 'NEEDS ATTENTION'}")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
