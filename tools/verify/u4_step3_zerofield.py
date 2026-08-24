r"""U4 step 3 -- a legitimately ZERO-FIELD UScriptStruct must still be MEMOIZED.

    py tools/verify/u4_step3_zerofield.py            # survey + run + negative control
    py tools/verify/u4_step3_zerofield.py survey     # just the survey

THE PREDICATE UNDER TEST
  ShouldPublishClassWalk (dll/src/Ubel.h:550) is
      propsSizeReadOk && IsSanePropertiesSize(propertiesSize)
  and its own comment (Ubel.h:541-545) says why it may NOT be tightened:
      * NEVER gate on Fields.empty() -- InjectIntrinsicStructFields exists precisely
        because an empty field list is a LEGITIMATE outcome (FDateTime/FTimespan and
        every UCLASS that declares no own UPROPERTYs).
      * NEVER gate on Name.empty() -- Serie::GetString returns "" for an unresolved
        obfuscated-fork tag key.
  So the live question is exactly: does a real-but-field-less UScriptStruct still get
  cached, while a non-UStruct address is still refused?

WHAT IS OBSERVED, AND WHY IT CANNOT BE FAKED
  A COLD walk logs two lines (dll/src/Ubel.cpp:904 and :980); a cache HIT logs neither,
  because WalkClass returns from the memo before reaching them. So "walked 4x, logged
  once" IS the memo, observed rather than asserted. The refusal line is Ubel.cpp:1007.

  Negative control, free and in the same run: an INSTANCE address is not a UStruct, so
  it must be refused every time -- 4 walks, 4 refusals, 4 cold walks, never memoized.
  Without it, "1 cold walk" could equally mean the walk silently failed.

  ⚠ THE SURVEY IS THE OTHER HALF OF THE TEST. `walk_class` returns its payload NESTED
  under "class"; reading `fields` off the TOP level returns [] for every object, which
  reports all 500 ScriptStructs as field-less and would pick a garbage fixture while
  looking like a rich survey. Measured while writing this: 500/500 "zero-field" via the
  top level vs 68/500 read correctly. Hence `props_size > 0` and a non-empty name are
  required of a candidate -- a struct with real storage and a resolved name cannot be a
  read failure masquerading as an empty struct.
"""
import os
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient  # noqa: E402

LOG = pathlib.Path(os.environ["LOCALAPPDATA"]) / "UE5CEDumper" / "Logs" / "DumperTest" / "walk-0.log"
WALKS = 4


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + "\n")
    sys.stdout.flush()


def survey(c, limit=6000):
    """Every ScriptStruct in the pool, split by whether it has reflected fields."""
    inst = c.request("find_instances", class_name="ScriptStruct",
                     max_results=limit, exact_match=True).get("instances") or []
    zero, withf = [], 0
    for x in inst:
        cl = c.request("walk_class", addr=x["addr"]).get("class") or {}
        f = cl.get("fields") or []
        if f:
            withf += 1
            continue
        # A candidate must be a REAL struct: named, and with storage. Both guard against
        # a failed read being counted as a legitimately empty struct.
        zero.append((cl.get("name") or "", x["addr"], cl.get("props_size")))
    good = [z for z in zero if z[0] and isinstance(z[2], int) and z[2] > 0]
    good.sort(key=lambda z: -z[2])
    return inst, withf, zero, good


def tail_since(mark):
    if not LOG.is_file():
        return ""
    with LOG.open("r", encoding="utf-8", errors="replace") as fh:
        fh.seek(mark)
        return fh.read()


def mark_now():
    # ⚠ Must be taken while the process is ALREADY running: <cat>-0.log rotates on every
    # process start, so a pre-launch offset points into the previous run's file.
    return LOG.stat().st_size if LOG.is_file() else 0


def count_lines(text, name, addr):
    a = int(addr, 16) if isinstance(addr, str) else int(addr)
    cold_hdr = sum(1 for ln in text.splitlines()
                   if ("WalkClass: %s (" % name) in ln)
    cold_fld = sum(1 for ln in text.splitlines()
                   if ln.startswith("") and ("WalkClass: %s " % name) in ln and "fields" in ln)
    refuse = sum(1 for ln in text.splitlines()
                 if "refusing to cache" in ln and ("%x" % a).lower() in ln.lower())
    return cold_hdr, cold_fld, refuse


def main():
    fails = []
    with PipeClient().connect() as c:
        say("DLL build %s" % c.assert_build())

        inst, withf, zero, good = survey(c)
        say("")
        say("SURVEY: %d ScriptStruct object(s) -- %d with fields, %d with ZERO fields, "
            "%d usable candidates (named + props_size>0)"
            % (len(inst), withf, len(zero), len(good)))
        if not good:
            say("")
            say("BLOCKED: no natural zero-field UScriptStruct in this pool. U4 step 3 then "
                "needs the drafted fixture (USTRUCT FDumperTestEmpty) and a UE 5.4 repackage.")
            return 2
        for nm, a, ps in good[:5]:
            say("   candidate  %-38s %s  props_size=%s" % (nm[:38], a, ps))
        name, addr, ps = good[0]
        say("")
        say("chosen : %s @ %s (props_size=%d, 0 reflected fields)" % (name, addr, ps))

        # ---------------------------------------------------------- the memo
        # Walk it once first so the "cold" walk we measure is OURS, not a leftover from
        # the survey above -- the survey already walked every candidate.
        mark = mark_now()
        for _ in range(WALKS):
            c.request("walk_class", addr=addr)
        txt = tail_since(mark)
        hdr, fld, ref = count_lines(txt, name, addr)
        say("")
        say("MEMO   : walked %dx after the survey -> cold-header lines=%d  "
            "'N fields' lines=%d  refusals=%d" % (WALKS, hdr, fld, ref))
        if hdr or fld:
            fails.append("the struct was re-walked %d/%d times after already being walked "
                         "-- it is NOT memoized" % (max(hdr, fld), WALKS))
        else:
            say("         OK: zero cold walks -- every one of the %d calls hit the memo"
                % WALKS)
        if ref:
            fails.append("'refusing to cache' fired %d time(s) for a legitimate "
                         "zero-field UScriptStruct -- the publish gate is too tight, which "
                         "is exactly what U4 forbids" % ref)
        else:
            say("         OK: no 'refusing to cache' for a legitimate zero-field struct")

        # ------------------------------------------------- negative control
        # An INSTANCE address is not a UStruct. It must be refused EVERY time, and must
        # therefore be re-walked cold every time. Without this, "0 cold walks" above
        # could equally mean the walk silently did nothing.
        say("")
        say("negative control -- an INSTANCE address (not a UStruct):")
        acts = c.request("find_instances", class_name="DumperTestActor",
                         max_results=5).get("instances") or []
        if not acts:
            fails.append("no DumperTestActor instance -- the negative control could not run, "
                         "so the positive result above is unwitnessed")
        else:
            iaddr = acts[0]["addr"]
            mark2 = mark_now()
            for _ in range(WALKS):
                c.request("walk_class", addr=iaddr)
            t2 = tail_since(mark2)
            ia = int(iaddr, 16)
            ref2 = sum(1 for ln in t2.splitlines()
                       if "refusing to cache" in ln and ("%x" % ia).lower() in ln.lower())
            say("   instance %s walked %dx -> refusals=%d" % (iaddr, WALKS, ref2))
            if ref2 != WALKS:
                fails.append("negative control: expected %d refusals for an instance "
                             "address, saw %d -- the detector cannot distinguish a cached "
                             "walk from a refused one, so the memo result means nothing"
                             % (WALKS, ref2))
            else:
                say("   OK: refused every time and never memoized -- the detector works")

    say("")
    if fails:
        say("FAIL (%d)" % len(fails))
        for f in fails:
            say("  - %s" % f)
        return 1
    say("PASS -- a legitimately zero-field UScriptStruct IS memoized, while a non-UStruct "
        "address is refused every time")
    return 0


if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "survey":
        with PipeClient().connect() as c:
            inst, withf, zero, good = survey(c)
            say("%d ScriptStructs: %d with fields, %d zero-field, %d usable"
                % (len(inst), withf, len(zero), len(good)))
            for nm, a, ps in good[:20]:
                say("   %-40s %s  props_size=%s" % (nm[:40], a, ps))
        raise SystemExit(0)
    raise SystemExit(main())
