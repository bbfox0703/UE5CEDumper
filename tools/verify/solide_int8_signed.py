r"""Solide — forcing an `Int8Property` must round-trip a NEGATIVE value, and must REFUSE one that
does not fit.

    py tools/verify/solide_int8_signed.py     (DumperTest running + injected, no UI)

THE DEFECT. `int8` was read back through an unsigned path, so a held **-5** was reported as **251**.
The re-assert worker compares what it reads against what it wants, so every tick saw a mismatch,
rewrote the same byte, and the UI showed permanent drift on a field that was in fact correct.

TWO STEPS, and the second is the one that makes the first mean something:
  1. force a negative value and read it back — must be the negative, not its unsigned image.
  2. force **200**, which does not fit in an int8 — must be REFUSED, not silently wrapped to -56.

⚠ Step 2 is not a bonus. If the range check is missing, step 1 still passes: -5 round-trips fine
through a signed path that happens to have no bounds. Only the out-of-range case separates "reads
back correctly" from "reads back correctly AND knows what an int8 is".

⚠ RE-VERIFICATION, and why: the maintainer marked both of these ✅ on 2026-08-21 at 10:08, on a
copy of the checklist held outside the repo (`Y:\`). The marks never reached git and the rows read
as untested. Rather than transcribe a tick with no evidence behind it, this re-runs them and
records what it actually saw. [ZHTW-MARKS-2026-08-21]
"""
import pathlib
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
sys.stdout.reconfigure(encoding="utf-8", errors="replace")

from pipe_client import PipeClient  # noqa: E402


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + chr(10))
    sys.stdout.flush()


def forced(c):
    r = c.request("get_forced_fields")
    d = r.get("data", r)
    return d.get("fields") or d.get("forced") or d.get("results") or []


def main():
    fails, notes = [], []
    with PipeClient() as c:
        c.assert_build()
        c.ensure_scanned()

        gw = str((c.request("get_pointers").get("data", {}) or {}).get("gworld") or "0")
        say("gworld = %s" % gw)

        # ---- find an Int8Property that actually has live instances ----------
        sp = c.request("search_properties", query="", types=["Int8Property"],
                       game_only=False, limit=50000)
        rows = sp.get("results", [])
        say("Int8Property rows on this host: %d" % len(rows))
        if not rows:
            say("NOT_RUNNABLE: no Int8Property anywhere — the row's own ⚠ says this is a valid "
                "outcome, not a failure")
            return 2

        target = None
        for r in rows[:40]:
            cls, fld = r.get("class_name"), r.get("prop_name")
            fi = c.request("find_instances", class_name=cls, max_results=3)
            if fi.get("instances"):
                target = (cls, fld, r.get("defining_class_name"))
                break
        if not target:
            say("NOT_RUNNABLE: %d Int8Property fields exist but none of their classes has a live "
                "instance, so nothing can be held" % len(rows))
            for r in rows[:6]:
                say("   candidate: %s::%s" % (r.get("class_name"), r.get("prop_name")))
            return 2
        cls, fld, decl = target
        say("fixture: %s::%s  (declared on %s)" % (cls, fld, decl))

        c.request("reset_all_fields")

        # ---- step 1: a negative value must round-trip ------------------------
        say("")
        say("== step 1: Force -5 and read it back ==")
        r1 = c.request("force_field", class_name=cls, field_name=fld, kind="number", value=-5)
        d1 = r1.get("data", r1)
        say("   force_field: code=%s held=%s resolved=%s"
            % (d1.get("code"), d1.get("held"), d1.get("resolved")))
        if d1.get("code") not in (0, None) or not d1.get("held"):
            say("NOT_RUNNABLE: the force did not take (code=%s held=%s), so there is nothing to "
                "read back" % (d1.get("code"), d1.get("held")))
            c.request("reset_all_fields")
            return 2
        time.sleep(1.0)
        got = forced(c)
        for j in got:
            say("   get_forced_fields: %s" % str(j)[:220])
        vals = [j.get("value") for j in got]
        if not vals:
            fails.append("step 1: the job is held but get_forced_fields lists nothing")
        else:
            v = vals[0]
            say("   value read back: %s   (must be -5; 251 is the pre-fix unsigned image)" % v)
            if str(v).startswith("251"):
                fails.append("step 1: read back 251 — the unsigned path is back")
            elif str(v) not in ("-5", "-5.0", "-5.0000"):
                fails.append("step 1: read back %r, expected -5" % v)
            else:
                say("   step 1 OK")

        c.request("reset_all_fields")
        time.sleep(0.5)

        # ---- step 2: out of range must be REFUSED ----------------------------
        say("")
        say("== step 2: Force 200, which does not fit in an int8 ==")
        r2 = c.request("force_field", class_name=cls, field_name=fld, kind="number", value=200)
        d2 = r2.get("data", r2)
        say("   force_field: code=%s held=%s resolved=%s"
            % (d2.get("code"), d2.get("held"), d2.get("resolved")))
        after = forced(c)
        say("   get_forced_fields: %d job(s)" % len(after))
        if d2.get("code") in (0, None) and d2.get("held"):
            wrote = [j.get("value") for j in after]
            fails.append("step 2: 200 was ACCEPTED (held=%s, value=%s). If it wrapped to -56 that "
                         "is the silent-corruption case; either way the range check is missing."
                         % (d2.get("held"), wrote))
        else:
            say("   step 2 OK: refused (code=%s)" % d2.get("code"))

        c.request("reset_all_fields")

    say("")
    for n in notes:
        say("NOTE: %s" % n)
    if fails:
        say("Solide int8: FAIL")
        for f in fails:
            say("   - %s" % f)
        return 1
    say("Solide int8: PASS — a negative round-trips as itself, and an out-of-range value is "
        "refused rather than wrapped")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
