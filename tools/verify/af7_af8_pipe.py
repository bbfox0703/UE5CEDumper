r"""L10 steps 7 and 8 (AF8 negative Force, AF7 walk_function_props budget_hit) over the pipe.

    py tools/verify/af7_af8_pipe.py

Both rows are category A in the plan — they are DLL-side facts, so the UI is not the
subject and driving them straight over `\\.\pipe\UE5DumpBfx` removes Avalonia as a
variable. Correspondingly this proves nothing about the panels' own bindings.

AF8 — a NEGATIVE value held in an Int8Property.
    ⚠ The two parameter names that make this silently do nothing: it is `kind`, not `mode`
    (and it DEFAULTS to "bool", so a wrong name is not an error -- it forces a bool), and
    `value` is read with `request.value("value", 0.0)`, so the STRING "-5" parses as 0.0.
    Both together give `ok:true, held:0, resolved:false, kind:"bool", value:0.0` -- a reply
    that looks like a failed fix rather than a malformed call.
    The defect shape is a signed byte round-tripping through an unsigned path: -5 stored
    and read back as 251. So the rig does not merely check "the force took"; it reads the
    value back and asserts it is **-5**, and it prints what it actually saw so a 251 is
    unmistakable.

AF7 — `budget_hit` must be PRESENT in the reply.
    The row asks whether the key exists, not whether it is true. A missing key and a
    `false` key are the same thing to a naive `if reply.get("budget_hit")`, and the whole
    point of the fix is that the caller can now tell "the walk completed" from "the walk
    stopped early". So presence is asserted separately from value.
"""
import json
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient  # noqa: E402


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(s.encode(enc, "replace").decode(enc, "replace") + "\n")


def main():
    fails = []
    with PipeClient() as c:
        c.assert_build()
        c.ensure_scanned()

        # ---------------------------------------------------------- AF8
        say("== AF8: hold a NEGATIVE value in an Int8Property ==")
        r = c.request("search_properties", types=["Int8Property"],
                      game_only=False, limit=50)
        rows = r.get("data", r).get("results") or []
        say("   Int8Property rows: %d  (%s)"
            % (len(rows), ", ".join(x.get("prop_name", "?") for x in rows)[:120]))
        # Prefer DumperTest's own signed fixture; it exists to be negative.
        hit = (next((x for x in rows if x.get("prop_name") == "I8_Neg"), None)
               or next((x for x in rows if x.get("prop_name") == "SignedInt8Variable"), None)
               or (rows[0] if rows else None))
        if not hit:
            fails.append("AF8: no Int8Property on this title -- INCONCLUSIVE, not a pass")
        else:
            say("   using %s.%s (offset %s)"
                % (hit["class_name"], hit["prop_name"], hit.get("prop_offset")))
            fr = c.request("force_field", class_name=hit["class_name"],
                           field_name=hit["prop_name"], kind="numeric", value=-5)
            fd = fr.get("data", fr)
            say("   force_field  -> ok=%s %s" % (fd.get("ok"), json.dumps(fd)[:260]))
            gf = c.request("get_forced_fields")
            d = gf.get("data", gf)
            say("   get_forced_fields -> %s" % json.dumps(d)[:420])
            held = d.get("fields") or d.get("forced") or d.get("results") or []
            if not held:
                fails.append("AF8: nothing held after force_field")
            for f in held:
                v = str(f.get("value", f.get("target_value", "")))
                say("      held: %s.%s = %s"
                    % (f.get("class_name"), f.get("field_name", f.get("prop_name")), v))
                if "251" in v:
                    fails.append("AF8: read back 251 -- the unsigned round-trip is back")
                elif "-5" not in v:
                    fails.append("AF8: expected -5, saw %r" % v)
            c.request("reset_all_fields")

        # ---------------------------------------------------------- AF7
        say("")
        say("== AF7: walk_function_props must carry budget_hit ==")
        fns = c.request("list_all_functions", limit=400)
        fl = fns.get("data", fns).get("functions") or []
        say("   functions listed: %d" % len(fl))
        probed = seen_key = seen_true = 0
        for fn in fl:
            addr = fn.get("func_addr")
            if not addr:
                continue
            wr = c.request("walk_function_props", func_addr=addr)
            wd = wr.get("data", wr)
            if not wd.get("ok", True):
                continue
            probed += 1
            if "budget_hit" in wd:
                seen_key += 1
                if wd["budget_hit"]:
                    seen_true += 1
                    say("   budget_hit TRUE on %s.%s"
                        % (fn.get("class_name"), fn.get("func_name")))
            if probed >= 80:
                break
        say("   probed %d functions: budget_hit KEY present on %d, true on %d"
            % (probed, seen_key, seen_true))
        if probed == 0:
            fails.append("AF7: probed 0 functions -- INCONCLUSIVE, not a pass")
        elif seen_key == 0:
            fails.append("AF7: budget_hit key absent from every reply")

    say("")
    for f in fails:
        say("FAIL: %s" % f)
    if not fails:
        say("PASS")
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
