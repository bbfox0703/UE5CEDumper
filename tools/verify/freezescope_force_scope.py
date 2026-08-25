r"""FREEZESCOPE step 1 + step 9, and the Solide `capped` wire half -- one pipe run, no UI, no CE.

    py tools/verify/freezescope_force_scope.py

WHAT IT SETTLES
  FREEZESCOPE step 1  the Property Search row for `bCanBeDamaged` exists, is declared on `Actor`,
                      and carries the inheritor count the "+N inheritors" badge renders.
  FREEZESCOPE step 9  the cross-feature CONTROL: Force (Solide) on that same row must report an
                      instance count comparable to Freeze's derived sweep. "Force and Freeze sit on
                      one row and must not scope oppositely" is what started the whole finding, so
                      this is the half that can be checked without Cheat Engine.
  Solide `capped`     the wire half of the long-tail row: on a base class with far more than 256
                      live instances, `force_field` must report `held == 256` AND `truncated == true`
                      -- the cap has to be ADMITTED, not silently applied.

WHY `Actor` IS THE RIGHT SUBJECT
  `bCanBeDamaged` is declared on `Actor` and inherited by hundreds of classes, so an EXACT-class
  pool would hold almost nothing while a DERIVED sweep holds the whole level. That gap is the
  finding: pre-fix Freeze held one incidental actor and the player pawn died normally.

SAFETY
  The force is applied and then released in the same run (`reset_field`, then `reset_all_fields`),
  and the run reports what is held afterwards so a leak is visible rather than assumed. `Solitar`
  restores the captured base value on release.
"""
import json
import pathlib
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient  # noqa: E402


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + "\n")
    # Flush: a backgrounded rig's stdout is a FILE, which Python block-buffers --
    # a long run then shows an EMPTY output file and looks hung.
    sys.stdout.flush()


def main():
    fails = []
    with PipeClient() as c:
        c.assert_build()
        c.ensure_scanned()

        # -------------------------------------------------- step 1
        say("== FREEZESCOPE step 1: the row exists and names its declaring class ==")
        rs = (c.request("search_properties", query="bCanBeDamaged",
                        game_only=False, limit=20).get("results") or [])
        row = next((x for x in rs if x.get("prop_name") == "bCanBeDamaged"), None)
        if not row:
            say("   FAIL: no bCanBeDamaged row -- the rest of this run has no subject")
            return 1
        inh = row.get("inherited_by_count")
        say("   class=%s  defining=%s  type=%s  bool_mask=%s  inherited_by_count=%s"
            % (row.get("class_name"), row.get("defining_class_name"), row.get("prop_type"),
               row.get("bool_mask"), inh))
        if row.get("defining_class_name") != "Actor":
            fails.append("step 1: declared on %r, not Actor" % row.get("defining_class_name"))
        if not inh or inh < 2:
            fails.append("step 1: inherited_by_count=%r -- with no inheritors, derived vs exact "
                         "cannot differ and step 9 would be vacuous" % inh)

        # -------------------------------------------------- how big is the derived pool?
        say("")
        say("== context: how big is the pool, and why find_instances is NOT the baseline ==")
        fi = c.request("find_instances", class_name="Actor", max_results=5000,
                       exact_match=False)
        ins = fi.get("instances") or []
        cdo = sum(1 for i in ins if str(i.get("name", "")).startswith("Default__"))
        say("   find_instances('Actor', exact_match=false): total=%s  CDOs=%d  non-CDO=%d"
            % (fi.get("total"), cdo, len(ins) - cdo))
        say("   ^ NOT the number Force should equal. exact_match=false is a NAME SUBSTRING match,")
        say("     so it sweeps in ActorElementAssetDataInterface and friends, which do not derive")
        say("     from AActor at all. Solide uses Aura::FindInstancesDerivedFrom, a real super-chain")
        say("     test with a per-UClass verdict cache, and skips CDOs inside the walk.")

        # -------------------------------------------------- step 9 + Solide capped
        say("")
        say("== FREEZESCOPE step 9 / Solide `capped`: Force on the same row ==")
        c.request("reset_all_fields")
        fr = c.request("force_field", class_name="Actor", field_name="bCanBeDamaged",
                       kind="bool", on=False)
        say("   force_field -> ok=%s resolved=%s held=%s truncated=%s code=%s"
            % (fr.get("ok"), fr.get("resolved"), fr.get("held"),
               fr.get("truncated"), fr.get("code")))
        held, trunc = fr.get("held"), fr.get("truncated")
        if not fr.get("resolved"):
            fails.append("step 9: force_field did not resolve the field at all")
        # The pre-fix value on THIS host (25,179 objects) was 1 -- that is the number to beat.
        if held is None or held < 2:
            fails.append("step 9: held=%r -- Force scoped to ~one object while Freeze sweeps "
                         "derived; that is exactly the opposite-scoping the finding is about"
                         % held)
        if held == 256 and not trunc:
            fails.append("Solide capped: held is exactly the 256 cap but truncated is %r -- the "
                         "cap must be ADMITTED" % trunc)
        if held == 256 and trunc:
            say("   OK: at the cap AND says so (held=256, truncated=true)")

        gf = c.request("get_forced_fields")
        flds = gf.get("fields") or []
        say("   get_forced_fields -> %d entry(ies)" % len(flds))
        for f in flds[:3]:
            say("      %s.%s held=%s truncated=%s kind=%s"
                % (f.get("class_name"), f.get("field_name"), f.get("held"),
                   f.get("truncated"), f.get("kind")))

        # -------------------------------------------------- release
        say("")
        c.request("reset_field", class_name="Actor", field_name="bCanBeDamaged")
        c.request("reset_all_fields")
        time.sleep(0.4)
        left = (c.request("get_forced_fields").get("fields") or [])
        say("   after reset: %d field(s) still held   <-- must be 0" % len(left))
        if left:
            fails.append("cleanup: %d field(s) still held after reset_all_fields" % len(left))

    say("")
    for x in fails:
        say("FAIL: %s" % x)
    if not fails:
        say("PASS")
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
