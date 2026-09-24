#!/usr/bin/env python3
r"""L56 `[A4-CDOSCOPE-ANCESTOR]` + `[A4-CDOSCOPE-NESTED-PREVIEW]`: Property Search previews, pipe half.

    py tools/verify/l56_cdoscope.py

  ANCESTOR  search_properties "EyeHeight" (game_only off) returns Pawn.BaseEyeHeight AND
            Character.CrouchedEyeHeight. The Pawn row's preview must end " (subclass instance)", never
            "(CDO default)". ⚠ The query must match a field on a NEARER class too: with Pawn the only
            preview class, the pre-fix walk credited Pawn anyway, so `BaseEyeHeight` alone cannot fail.
            Then Force the Pawn field to the value it already holds: `held` is the instance count the
            row's "Freeze reports the same instances" compares against. Released and verified empty.
  NESTED    search_properties "Num" (game_only, deep): direct DumperTestActor InvokeGate_*Num rows carry a
            `preview`; the nested `Arr_StrRows[].Num` row (is_nested) carries NONE. Pre-fix it previewed
            the int at instance+0x10 -- the low dword of UObject::ClassPrivate, a large garbage number.

Run with the UI closed (2 of 3 pipe slots).
"""
import os
import re
import sys
import time

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pipe_client import PipeClient   # noqa: E402

fails = []


def rows_of(r):
    d = r.get("data", r)
    return d.get("results") or d.get("properties") or d.get("matches") or []


def main():
    with PipeClient() as c:
        print("build %s" % c.assert_build())
        c.ensure_scanned()

        print("\n== ANCESTOR: search_properties 'EyeHeight' (game_only off) ==")
        rs = rows_of(c.request("search_properties", query="EyeHeight", game_only=False, limit=200))
        for x in rs:
            if x.get("class_name") in ("Pawn", "Character"):
                print("   %-10s %-20s preview=%r" % (x.get("class_name"), x.get("prop_name"), x.get("preview")))
        pawn = next((x for x in rs if x.get("class_name") == "Pawn" and x.get("prop_name") == "BaseEyeHeight"), None)
        char = next((x for x in rs if x.get("class_name") == "Character"
                     and x.get("prop_name") == "CrouchedEyeHeight"), None)
        if not pawn or not char:
            fails.append("ancestor: the Pawn and/or Character row is missing -- the check would be vacuous")
        else:
            pv, cv = str(pawn.get("preview") or ""), str(char.get("preview") or "")
            if not pv.endswith(" (subclass instance)") or "(CDO default)" in pv:
                fails.append("ancestor: Pawn preview %r is not a subclass-instance reading" % pv)
            if not cv.endswith(" (subclass instance)"):
                fails.append("ancestor control: Character preview %r" % cv)
            m = re.match(r"\s*(-?[0-9.]+)", pv)
            if m:
                c.request("reset_all_fields")
                fr = c.request("force_field", class_name="Pawn", field_name="BaseEyeHeight",
                               kind="numeric", value=float(m.group(1)))   # a JSON NUMBER: a string is refused (type_error.302)
                print("   force_field Pawn.BaseEyeHeight = %s (the value it already holds) -> resolved=%s "
                      "held=%s truncated=%s" % (m.group(1), fr.get("resolved"), fr.get("held"), fr.get("truncated")))
                if not fr.get("resolved") or not fr.get("held"):
                    fails.append("ancestor: force_field held nothing")
                c.request("reset_field", class_name="Pawn", field_name="BaseEyeHeight")
                c.request("reset_all_fields")
                time.sleep(0.4)
                left = c.request("get_forced_fields").get("fields") or []
                print("   after reset: %d field(s) still held (must be 0)" % len(left))
                if left:
                    fails.append("cleanup: fields still held")

        # ⭐ THE ANCESTOR DISCRIMINATOR ON THIS FIXTURE (found 2026-09-22 with the pre-fix DLL). `EyeHeight` cannot
        # fail here: a live SpectatorPawn derives from Pawn but not Character, so even the pre-fix walk credited Pawn.
        # `Jump` matches Character (bPressedJump, JumpKeyHoldTime, ...) AND the template's nearer
        # DumperTestCharacter.JumpAction, and the only live Character is the BP subclass of DumperTestCharacter --
        # so pre-fix every Character row read "(CDO default)" (measured), and post-fix none may.
        print("\n== ANCESTOR, discriminating: search_properties 'Jump' (game_only off) ==")
        rs = rows_of(c.request("search_properties", query="Jump", game_only=False, limit=300))
        chars = [x for x in rs if x.get("class_name") == "Character"]
        nearer = [x for x in rs if x.get("class_name") == "DumperTestCharacter"]
        for x in chars[:4] + nearer[:1]:
            print("   %-20s %-26s preview=%r" % (x.get("class_name"), x.get("prop_name"), x.get("preview")))
        if not chars or not nearer:
            fails.append("ancestor (Jump): the Character or DumperTestCharacter rows are missing -- vacuous")
        cdo = [x.get("prop_name") for x in chars if "(CDO default)" in str(x.get("preview") or "")]
        sub = [x.get("prop_name") for x in chars if str(x.get("preview") or "").endswith(" (subclass instance)")]
        print("   Character rows: %d, (subclass instance) %d, (CDO default) %d" % (len(chars), len(sub), len(cdo)))
        if cdo:
            fails.append("ancestor (Jump): %d Character row(s) read (CDO default): %s" % (len(cdo), cdo[:4]))

        print("\n== NESTED: search_properties 'Num' (game_only, deep) ==")
        rs = rows_of(c.request("search_properties", query="Num", game_only=True, deep=True, limit=200))
        mine = [x for x in rs if x.get("class_name") == "DumperTestActor"]
        for x in mine:
            if "InvokeGate" in (x.get("prop_name") or "") or "Arr_StrRows" in (x.get("prop_name") or ""):
                print("   %-34s nested=%-5s has_preview=%-5s preview=%r"
                      % (x.get("prop_name"), x.get("is_nested"), "preview" in x, x.get("preview")))
        direct = [x for x in mine if (x.get("prop_name") or "").startswith("InvokeGate_") and not x.get("is_nested")]
        nested = [x for x in mine if (x.get("prop_name") or "") == "Arr_StrRows[].Num"]
        if not direct or not nested:
            fails.append("nested: the direct InvokeGate_*Num rows or the Arr_StrRows[].Num row is missing")
        if any("preview" not in x for x in direct):
            fails.append("nested control: a direct row has no preview")
        if any(("preview" in x and x.get("preview") not in (None, "")) for x in nested):
            fails.append("nested: the nested row carries a preview %r" % nested[0].get("preview"))
        if nested and not nested[0].get("is_nested"):
            fails.append("nested: Arr_StrRows[].Num is not flagged is_nested")
    print("")
    for f in fails:
        print("FAIL: " + f)
    print("L56 pipe half: %s" % ("PASS" if not fails else "FAIL"))
    return 0 if not fails else 1


if __name__ == "__main__":
    sys.exit(main())
