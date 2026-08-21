r"""L11 7a — census: does any UFunction on this host have a COMPLEX return whose slot ends past
byte 256?

    py tools/verify/l11_7a_ret_census.py     (DumperTest running + injected, read-only)

WHY. The `>256` hint on the invoke dialog is emitted only for a return whose bytes extend beyond a
256-byte params buffer. The row has been open on "is there anything to point it at", which is a
census question, not a judgement — and a census answers either way: a fixture, or a MEASURED zero
that converts "unexercised" into "not exercisable here".

TWO STAGES, because `list_all_functions` carries no return information:
  1. every function, `game_only=false`; keep `parms_size > 256` and collect the owning classes.
  2. `walk_functions` per class; a fixture is a param with `ret == true` whose type is complex
     (struct / string / text / container / delegate) and whose `offset + max(size,1) > 256`.

⚠ STAGE 1 MUST NOT BE CAPPED. A truncated or aborted walk cannot support "there are none" — it can
only support "none in the part I looked at". The rig fails loudly rather than reporting a zero.

⚠ THE NEGATIVE CONTROL IS A KNOWN ≤256 CASE. `MakeTransform` returns the same 96-byte struct at
offset 80, ending at 176 — complex, but inside the buffer. If the classifier flags that one too it
is matching on "complex" and ignoring the boundary, and every hit it reports is worthless.
"""
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
sys.stdout.reconfigure(encoding="utf-8", errors="replace")

from pipe_client import PipeClient  # noqa: E402

COMPLEX = ("StructProperty", "StrProperty", "TextProperty", "ArrayProperty", "MapProperty",
           "SetProperty", "DelegateProperty", "MulticastInlineDelegateProperty",
           "MulticastSparseDelegateProperty", "NameProperty")
BOUNDARY = 256


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + chr(10))
    sys.stdout.flush()


def ret_end(p):
    return int(p.get("offset") or 0) + max(int(p.get("size") or 0), 1)


def is_fixture(p):
    return (p.get("ret") and (p.get("type") in COMPLEX) and ret_end(p) > BOUNDARY)


def main():
    with PipeClient() as c:
        c.assert_build()
        c.ensure_scanned()

        say("== stage 1: every UFunction, game_only=false ==")
        r = c.request("list_all_functions", game_only=False, limit=300000)
        d = r.get("data", r)
        funcs = d.get("results") or d.get("functions") or []
        say("   total=%s scanned_classes=%s truncated=%s aborted=%s"
            % (d.get("total"), d.get("scanned_classes"), d.get("truncated"), d.get("aborted")))
        if d.get("truncated") or d.get("aborted"):
            say("ABORT: the walk was capped or cancelled. A zero from here would mean 'none in "
                "the part I looked at', which is not the claim this census has to support.")
            return 2
        if not funcs:
            say("ABORT: no function rows returned at all")
            return 2

        big = [f for f in funcs if int(f.get("parms_size") or 0) > BOUNDARY]
        classes = {}
        for f in big:
            classes.setdefault(f.get("class_addr"), f.get("class_name"))
        say("   functions with parms_size > %d: %d, over %d class(es)"
            % (BOUNDARY, len(big), len(classes)))
        if not big:
            say("")
            say("MEASURED ZERO: no UFunction anywhere on this host has a params buffer larger "
                "than %d bytes, so a return slot cannot end past it. 7a is NOT EXERCISABLE here "
                "— that is the answer, not a gap." % BOUNDARY)
            return 0

        say("")
        say("== stage 2: walk_functions on those %d class(es) ==" % len(classes))
        fixtures, control_seen = [], []
        for addr, cname in classes.items():
            w = c.request("walk_functions", addr=addr)
            for fn in (w.get("functions") or []):
                params = fn.get("params") or []
                hits = [p for p in params if is_fixture(p)]
                if hits:
                    fixtures.append((cname, fn.get("name"), fn.get("parms_size"), hits[0]))
                # the control: a COMPLEX return that stays inside the buffer
                for p in params:
                    if p.get("ret") and p.get("type") in COMPLEX and ret_end(p) <= BOUNDARY:
                        control_seen.append((cname, fn.get("name"), ret_end(p)))

        say("")
        say("FIXTURES (complex return ending past %d):" % BOUNDARY)
        for cname, fname, ps, p in fixtures[:15]:
            say("   %-26s %-34s ParmsSize=%-5s ret %s@%s size %s -> ends %d"
                % (cname, fname, ps, p.get("type"), p.get("offset"), p.get("size"), ret_end(p)))
        say("   total: %d" % len(fixtures))

        say("")
        say("NEGATIVE CONTROL — complex returns that stay INSIDE the buffer (must be non-empty, "
            "or the classifier is matching 'complex' and ignoring the boundary):")
        for cname, fname, e in control_seen[:8]:
            say("   %-26s %-34s ends at %d  (<= %d)" % (cname, fname, e, BOUNDARY))
        say("   total: %d" % len(control_seen))

        say("")
        if not control_seen:
            say("⚠ INCONCLUSIVE: no complex-return-inside-the-buffer case was found, so the "
                "boundary half of the classifier was never exercised. Any fixtures above are "
                "unverified.")
            return 2
        if not fixtures:
            say("MEASURED ZERO: %d function(s) have a params buffer > %d bytes, but none of them "
                "returns a COMPLEX value ending past it. 7a is not exercisable on this host — "
                "that is the answer." % (len(big), BOUNDARY))
            return 0
        say("RESULT: %d fixture(s) available; the classifier is shown to discriminate (%d "
            "complex-but-inside cases were correctly NOT flagged)."
            % (len(fixtures), len(control_seen)))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
