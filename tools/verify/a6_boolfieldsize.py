r"""Audit A6 — `UBoolProperty::FieldSize` is now DERIVED, on a live UProperty-mode game.

    py tools/verify/a6_boolfieldsize.py "<LogFolderName>"
    py tools/verify/a6_boolfieldsize.py "DRAGON QUEST XI S"          # the positive host
    py tools/verify/a6_boolfieldsize.py Octopath_Traveler-Win64-Shipping   # negative control

THE QUESTION. `c0b4e709`: `DynOff::UBOOLPROP_FIELDSIZE` had ZERO writers repo-wide against nine
readers, so every UE4 <4.25 game kept the `0x70` default no matter what its `Offset_Internal`
actually probed to. It is now derived from the probe (`Genau.cpp` `ValidateAndFixOffsets`), with a
version-dependent delta -- `0x28` for 4.11-4.17, `0x2C` for 4.18+, `+8` more under
CasePreservingName.

WHY A SHIFTED HOST IS REQUIRED. On a stock layout the derived value equals the old default
(`0x44 + 0x2C == 0x70`), so a stock title is a NEGATIVE CONTROL and cannot show a behaviour change.
DQ XI S carries a whole-layout `+0x10` shift: `Offset_Internal` probes to `+0x54`, so the true
FieldSize is `0x80` -- and Ubel's probe spread is `{base, base-4, base+4, base+8, base-8}`, i.e.
`{0x68, 0x6C, 0x70, 0x74, 0x78}` around the old `0x70`. `0x80` is ABOVE that ceiling, so before this
fix NO probe could reach it: `boolFieldMask` stayed 0 and every native bitfield bool fell back to
`byteVal != 0`, which reports a bool as **true whenever any sibling in its byte is set**.

⛔ THE UI OBSERVABLE IS THE **LIVE WALKER**, NOT ClassStructPanel. The register row says
"ClassStructPanel | two sibling bitfield bools showing DIFFERENT values". ClassStructPanel renders
NO value column at all -- its DataGrid is Offset / Name / Type / Size / Address and nothing else
(`ClassStructPanel.axaml:108-125`), so no bool value and no mask can ever appear there. What exists
is `WalkInstance`'s value: `"%s (bit %d, mask 0x%02X)"` when the FieldSize probe landed
(`Ubel.cpp:5162-5170`), and a bare `"true"`/`"false"` when it did not. That suffix is a DIRECT
readout of whether the probe landed, so ONE field settles it -- the "two siblings with different
values" construction is both harder to stage and weaker as evidence.

⭐ THE STRONGEST FORM, and this rig looks for it: a native C++ bitfield packs siblings into one
byte, so a landed probe yields masks marching `0x01, 0x02, 0x04 ... 0x80` across bits 0-7 on ONE
class. A mask ladder like that is unreachable with `boolFieldMask == 0`.
"""
import pathlib
import re
import sys

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from pipe_client import PipeClient  # noqa: E402

MASKED = re.compile(r"\(bit (\d+), mask 0x([0-9A-Fa-f]{2})\)")


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + "\n")
    sys.stdout.flush()


def read_log(logdir, basename):
    p = logdir / (basename + "-0.log")
    if not p.is_file():
        raise SystemExit("a6: %s does not exist -- wrong log folder, or the DLL never loaded" % p)
    try:
        return p.read_text(encoding="utf-8", errors="replace").splitlines()
    except OSError as e:
        raise SystemExit("a6: cannot read %s (%s) -- see the build-3370 `_wfopen_s` defect" % (p, e))


def main():
    if len(sys.argv) < 2:
        raise SystemExit(__doc__)
    logdir = pathlib.Path.home() / "AppData/Local/UE5CEDumper/Logs" / sys.argv[1]
    fails, notes = [], []

    with PipeClient() as c:
        say("build answering the pipe: %s" % c.assert_build())
        p = c.ensure_scanned()
        ue = p.get("ue_version")
        say("UE version = %s   objects = %s   module = %s   load_mode = %s"
            % (ue, p.get("object_count"), p.get("module_name"), p.get("load_mode")))
        if (p.get("object_count") or 0) < 1000:
            fails.append("A6: object_count %r -- the engine did not boot" % p.get("object_count"))
        if (ue or 0) >= 425:
            notes.append("UE %s is FProperty-mode; A6 only governs UProperty-mode (<4.25) games." % ue)

        # ---- DLL side: the derivation itself -----------------------------------
        lines = read_log(logdir, "offsets")
        oi = [l for l in lines if "Offset_Internal at +0x" in l]
        fs = [l for l in lines if "UBoolProperty::FieldSize derived at +0x" in l]
        say("\n[offsets-0.log]")
        for l in oi[-2:] + fs[-2:]:
            say("   " + l.strip()[:170])
        if not fs:
            fails.append("A6: no 'UBoolProperty::FieldSize derived at' line -- the derivation "
                         "never ran (it is in the FProperty/UProperty arm of ValidateAndFixOffsets)")
        else:
            m = re.search(r"derived at \+0x([0-9A-Fa-f]+) \(Offset_Internal \+0x([0-9A-Fa-f]+), UE=(\d+)\)",
                          fs[-1])
            if not m:
                fails.append("A6: the derived line did not parse: %r" % fs[-1][:150])
            else:
                got, oint, uev = int(m.group(1), 16), int(m.group(2), 16), int(m.group(3))
                delta = 0x28 if uev < 418 else 0x2C
                want = oint + delta
                say("   derived 0x%02X   Offset_Internal 0x%02X   UE %d   delta 0x%02X   expected 0x%02X"
                    % (got, oint, uev, delta, want))
                if got != want:
                    fails.append("A6: derived 0x%02X but Offset_Internal 0x%02X + delta 0x%02X = 0x%02X"
                                 % (got, oint, delta, want))
                # The whole point of the row: is this host a NEGATIVE CONTROL or a real delta?
                SPREAD = {0x70, 0x70 - 4, 0x70 + 4, 0x70 + 8, 0x70 - 8}
                if got == 0x70:
                    say("   => STOCK LAYOUT. 0x%02X is byte-identical to the old default -- this host "
                        "is a NEGATIVE CONTROL and proves the fix changed nothing where it should not."
                        % got)
                elif got in SPREAD:
                    say("   => 0x%02X is INSIDE the old probe spread %s, so the pre-fix code already "
                        "landed on it. Correct, but NOT a behaviour-change witness."
                        % (got, sorted(hex(x) for x in SPREAD)))
                else:
                    say("   => ⭐ 0x%02X is OUTSIDE the old spread %s -- pre-fix NO probe could reach "
                        "it, so boolFieldMask was 0 and every bitfield bool fell back to byteVal!=0. "
                        "THIS host can show a real behaviour change."
                        % (got, sorted(hex(x) for x in SPREAD)))

        # ---- the read side: did the mask actually land? -------------------------
        r = c.request("search_properties", query="b", limit=20000, game_only=False, timeout=240)
        rows = [x for x in (r.get("results") or []) if x.get("prop_type") == "BoolProperty"]
        classes, seen = [], set()
        for x in rows:
            cn = x.get("class_name")
            if cn and cn not in seen:
                seen.add(cn)
                classes.append(cn)
        say("\nBoolProperty rows: %d across %d classes" % (len(rows), len(classes)))

        landed, bare, ladders = [], [], {}
        for cn in classes[:25]:
            fr = c.request("find_instances", class_name=cn, limit=6, exact_match=True, timeout=120)
            for inst in (fr.get("instances") or [])[:2]:
                w = c.request("walk_instance", addr=inst["addr"].replace("0x", ""), timeout=120)
                for f in (w.get("fields") or []):
                    if f.get("type") != "BoolProperty":
                        continue
                    v = str(f.get("value") or "")
                    m = MASKED.search(v)
                    if m:
                        landed.append((cn, f.get("name"), v))
                        ladders.setdefault((cn, f.get("offset")), set()).add(int(m.group(2), 16))
                    elif v in ("true", "false"):
                        bare.append((cn, f.get("name"), v))
            if len(landed) >= 12:
                break

        say("values with '(bit N, mask 0xNN)'  -> probe LANDED : %d" % len(landed))
        for x in landed[:8]:
            say("    %-30s %-28s %s" % x)
        say("bare true/false                   -> mask was 0   : %d" % len(bare))
        for x in bare[:4]:
            say("    %-30s %-28s %s" % x)

        best = max((len(v) for v in ladders.values()), default=0)
        say("\nlongest single-byte mask ladder on one class/offset: %d distinct mask(s)" % best)
        if not landed:
            fails.append("A6: NOT ONE BoolProperty reported a mask -- boolFieldMask is 0 everywhere, "
                         "so the derived FieldSize is not reaching the reader")
        elif best >= 4:
            say("   => a %d-wide mask ladder in one byte is unreachable with boolFieldMask == 0." % best)

    say("\n================ A6 RESULT ================")
    for n in notes:
        say("NOTE: " + n)
    if fails:
        say("FAIL (%d):" % len(fails))
        for f_ in fails:
            say("   - " + f_)
        return 1
    say("PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
