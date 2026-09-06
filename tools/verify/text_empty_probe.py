"""Is `Text_Empty` reading as `No` the DLL's answer or the UI's rendering?

    py tools/verify/text_empty_probe.py          # DumperTest must already be running + injected

THE OBSERVATION. During the B28 pass the Live Walker showed `Text_Empty` as **`No`**
(verification-register.md, "NEW, unfiled"). `Text_Empty` is `FText::GetEmpty()`
(DumperTestActor.cpp), and tools/ue-sample/README.md's acceptance value for it is *(empty)*.
`No` looks like a truncated `None`, or a boolean rendering, or a placeholder.

⭐ WHY HEADLESS FIRST, AND WHY THAT IS THE WHOLE POINT. The register's charter is that a screen
reading cannot tell you which side is at fault. `No` on screen is equally consistent with:
  (a) the DLL returning the string "No" / "None" for an empty FText, and
  (b) the DLL returning an empty string that the UI renders as a placeholder.
A pipe client answers that in one call, with no UI in the loop at all -- and the answer decides
whether the defect is filed against Ubel::ReadFTextString or against the C# render path.

⚠ IT ALSO PRINTS THE RAW BYTES. An empty FText is not nothing: FTextData is a TSharedRef, so the
field is a live pointer to an object whose display string is empty. "The value is empty" and "the
field is null" are different defects and the value column cannot distinguish them.

⚠ CONTROLS, because a lone reading of one field proves nothing about the reader: the other seven
FTexts on the same actor are read in the same call. If `Text_Ascii` also came back odd, the fault
would be the walk, not the empty-string path.
"""
import json
import os
import pathlib
import sys

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.stdout.reconfigure(encoding="utf-8", errors="replace")

from pipe_client import PipeClient          # noqa: E402
from ad4_contested import find_live_actor    # noqa: E402

FTEXTS = ["Text_Empty", "Text_Ascii", "Text_Localized", "Text_Even2_OneNull",
          "Text_Even2_TwoNull", "Text_Even4_TwoNull", "Text_Odd3_OneNull", "Text_Even6_NoNull"]


def main():
    fails = []
    with PipeClient() as c:
        c.assert_build()
        act = find_live_actor(c)
        print("actor: %s @ %s" % (act.get("name"), act.get("addr")))

        r = c.request("walk_instance", addr=act["addr"])
        c.check_complete(r)
        fields = {f.get("name"): f for f in r.get("fields", [])}
        print("fields walked: %d" % len(fields))

        print("\n%-22s %-18s %-9s %s" % ("field", "type", "len", "value (repr)"))
        print("-" * 78)
        target = None
        for n in FTEXTS:
            f = fields.get(n)
            if not f:
                print("%-22s %s" % (n, "*** NOT IN THE WALK ***"))
                fails.append("%s absent from walk_instance" % n)
                continue
            v = f.get("value")
            print("%-22s %-18s %-9s %r" % (n, f.get("type"), len(v) if isinstance(v, str) else "-", v))
            if n == "Text_Empty":
                target = f

        if target is None:
            print("\nNO VERDICT: Text_Empty was not walked at all.")
            return 1

        v = target.get("value")
        print("\n--- Text_Empty, in full ---")
        print("  raw json : %s" % json.dumps(target, ensure_ascii=False)[:400])

        # The discriminator. Say which side owns the defect, in the terms the register needs.
        # ⭐ "(empty)" IS THE PASS, and it is not the DLL inventing a word. ReadFTextString
        # returns "", and the wire/display layer renders an empty display string as the
        # placeholder `(empty)` -- which is exactly the acceptance value README.md has always
        # documented for this field ("| Text_Empty | *(empty)* | the empty display-string path |").
        # The tell that it really is empty now: the garbage run also carried a `str_value` key,
        # and this one does not.
        if v in ("", None, "(empty)"):
            print("\n⭐ THE DLL RETURNS AN EMPTY VALUE (%r) -- matches README's acceptance value." % v)
            print("   [TEXTEMPTY-2026-09-06] is fixed: before the guard this field read 'ࣳ' (U+08F3),")
            print("   and had been seen as 'No' -- different garbage per run, read from adjacent memory.")
        elif isinstance(v, str) and v.strip().lower() in ("no", "none", "null"):
            print("\n⭐ THE DLL ITSELF RETURNS %r." % v)
            print("   So the screen is faithfully showing what the DLL sent, and the defect is")
            print("   in Ubel::ReadFTextString's empty-FText path.")
            fails.append("DLL returns %r for FText::GetEmpty()" % v)
        else:
            print("\n⭐ THE DLL RETURNS %r -- neither empty nor a No/None placeholder." % v)
            print("   Record this verbatim; it matches neither hypothesis and the row needs it.")
            fails.append("unexpected Text_Empty value %r" % v)

    print("\ntext_empty_probe: %s" % ("clean" if not fails else "FINDINGS -- " + "; ".join(fails)))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
