r"""A12 steps 1, 4 (the no-change half), 5 and 6 -- group-mode container anchoring, over the pipe.

    py tools/verify/a12_group_anchor.py

THE CLAIM (`A12`, build 3261). A11's re-anchoring, but in GROUP mode, where the leaf address has
to survive three by-name hops. The rule and the anchor factories are unit-pinned (17 assertions,
two negative controls); the WIRING is not, because no target compiles `Aura.cpp`.

Grep by FORMAT STRING: `RefineGroup re-anchor:` (whole-pass summary), `container-moved=`
(per-candidate drop tally), `carries no ValueAnchor` (the stamp-dropped alarm).

WHAT IS COVERED HERE
  1   two slots whose values live inside the SAME container element -> the row's slot fields must
      carry an `[i]` index.
  4a  the TMAP half's no-change assertion: with NOTHING changed, a first Next Scan must NOT drop
      every row. A mass drop with no in-game change is the unit trap the step is named for.
  5   NON-REGRESSION: a plain (non-container) pair must survive an unchanged refine and produce
      NO `RefineGroup re-anchor` line at all.
  6   `carries no ValueAnchor` must be absent from the log.

NOT COVERED, and deliberately not skipped quietly: steps 2, 3 and the growth/removal half of 4
need the container to CHANGE SIZE in play. Nothing here does that unattended.

FIXTURE (read live, not assumed). `DumperTestActor.Map_IntToVec3f` is a
`TMap<int32, DumperTestVec3f>` whose value struct holds three floats:
    {1: X=6201 Y=6202 Z=6203}   {2: 6211 6212 6213}   {3: 6221 6222 6223}
so 6201 and 6202 are two values inside ONE element -- exactly what step 1 asks for, on the TMap
step 4 asks for. The plain control pair is `Health.BaseValue=100` / `FrozenInt=424242`,
ordinary struct members with no container in the path.
"""
import json
import pathlib
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient  # noqa: E402

# ⚠ offsets-0.log, NOT scan-0.log. All three markers this rig greps — `RefineGroup re-anchor:`,
# `container-moved=`, `carries no ValueAnchor` — are emitted from Aura.cpp, whose
# `#define LOG_CAT "OARR"` Sein maps to LF_Offsets. Reading scan-0.log made BOTH of this rig's
# absence assertions (step 5's "no re-anchor line" and step 6's "no ValueAnchor alarm") pass no
# matter what the code did. Same defect as its A11 sibling. [A12-LOGPATH-2026-08-21]
LOGDIR = pathlib.Path.home() / "AppData/Local/UE5CEDumper/Logs/DumperTest"
OFFSETS = LOGDIR / "offsets-0.log"
SCAN = OFFSETS          # the name the rest of the file uses


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + "\n")
    # Flush: a backgrounded rig's stdout is a FILE, which Python block-buffers --
    # a long run then shows an EMPTY output file and looks hung.
    sys.stdout.flush()


def since(mark, needle):
    out = []
    try:
        for l in SCAN.read_text(encoding="utf-8", errors="replace").splitlines():
            if l.startswith("[") and l[1:20] >= mark and needle in l:
                out.append(l)
    except OSError:
        pass
    return out


def rows(c, sid, limit=50):
    r = c.request("query_group_candidates", session_id=sid, offset=0, limit=limit)
    d = r.get("data", r)
    return d.get("candidates") or [], d


def assert_detector_can_see_oarr():
    """An absence is evidence only once the channel is shown to carry that category.

    Step 5 and step 6 are both ABSENCE claims. Grepping a file the marker cannot reach returns
    zero for "the code did not do it" and for "I am reading the wrong channel" alike, and no
    amount of evidence that the SCAN ran separates them. [A12-LOGPATH-2026-08-21]
    """
    if not OFFSETS.exists():
        say("DETECTOR CHECK FAILED: %s does not exist — an absence proves nothing." % OFFSETS)
        return False
    if "[OARR]" not in OFFSETS.read_text(encoding="utf-8", errors="replace"):
        say("DETECTOR CHECK FAILED: %s carries no [OARR] line, so it is not the channel Aura.cpp "
            "writes to. An absence proves nothing." % OFFSETS.name)
        return False
    say("detector OK: %s carries [OARR] traffic — an absent marker is a real absence"
        % OFFSETS.name)
    return True


def main():
    if not assert_detector_can_see_oarr():
        say("ABORT: the absence assertions in steps 5 and 6 would be vacuous.")
        return 2
    fails = []
    with PipeClient() as c:
        c.assert_build()
        c.ensure_scanned()

        inst = next((i["addr"] for i in
                     (c.request("find_instances", class_name="DumperTestActor",
                                max_results=5).get("instances") or [])
                     if i.get("name") == "DumperTestActor"), None)
        if not inst:
            say("FAIL: no live DumperTestActor")
            return 1
        w = c.request("walk_instance", addr=inst, array_limit=32)
        m = next((f for f in (w.get("fields") or []) if f.get("name") == "Map_IntToVec3f"), None)
        if not m or not m.get("map_elements"):
            say("FAIL: Map_IntToVec3f unreadable -- the fixture is gone, step 1 would be vacuous")
            return 1
        e0 = m["map_elements"][0]["v"]
        say("fixture Map_IntToVec3f[0] = %s   (%s, stride %s)"
            % (e0, m.get("map_value_struct_type"), m.get("map_stride")))

        # ------------------------------------------------ steps 1 + 4a
        say("")
        say("== A12 step 1: two slots inside ONE container element must carry [i] ==")
        mark = time.strftime("%Y-%m-%d %H:%M:%S"); time.sleep(1.1)
        r = c.request("begin_group_scan", deep=True, game_only=True, max_results=50000,
                      # Group slots accept only NumericNoByte / NumericAll -- "Float" is
                      # rejected outright ("group slot data_type must be ..."), which returns
                      # session=None and reads as "the fixture did not match".
                      values=[{"value": "6201", "data_type": "NumericNoByte"},
                              {"value": "6202", "data_type": "NumericNoByte"}])
        d = r.get("data", r)
        sid = d.get("session_id")
        say("   session=%s objects matched=%s truncated=%s"
            % (sid, d.get("total"), d.get("truncated")))
        cs, _ = rows(c, sid)
        say("   rows returned: %d" % len(cs))
        idx_rows = []
        for x in cs[:6]:
            slots = x.get("slots") or []
            names = [s.get("field_name") for s in slots]
            say("      %-22s %s" % (x.get("class_name"), names))
            if any("[" in (n or "") for n in names):
                idx_rows.append(x)
        if not cs:
            fails.append("A12-1: no group rows at all -- the fixture values did not match")
        elif not idx_rows:
            fails.append("A12-1: no slot field carries an [i] element index")
        else:
            say("   OK: %d row(s) carry an element index in their slot fields" % len(idx_rows))

        say("")
        say("== A12 step 4a (the unit trap): an UNCHANGED refine must not drop everything ==")
        before = len(cs)
        rr = c.request("refine_group_scan", session_id=sid,
                       values=[{"value": "6201", "data_type": "NumericNoByte"},
                               {"value": "6202", "data_type": "NumericNoByte"}])
        dd = rr.get("data", rr)
        say("   refine ok=%s surviving=%s (was %s)" % (dd.get("ok"), dd.get("total"), before))
        cs2, _ = rows(c, sid)
        say("   rows after refine: %d" % len(cs2))
        if before and not cs2:
            fails.append("A12-4a: EVERY row dropped on an unchanged Next Scan -- the mass-drop trap")

        # ------------------------------------------------ step 5
        say("")
        say("== A12 step 5 (non-regression): a PLAIN pair must not enter the re-anchor path ==")
        mark5 = time.strftime("%Y-%m-%d %H:%M:%S"); time.sleep(1.1)
        r5 = c.request("begin_group_scan", deep=False, game_only=True, max_results=50000,
                       values=[{"value": "100", "data_type": "NumericNoByte"},
                               {"value": "424242", "data_type": "NumericNoByte"}])
        d5 = r5.get("data", r5)
        sid5 = d5.get("session_id")
        say("   plain group scan session=%s matched=%s" % (sid5, d5.get("total")))
        c5, _ = rows(c, sid5)
        say("   rows: %d   sample slots: %s"
            % (len(c5), [s.get("field_name") for s in ((c5[0].get("slots") or []) if c5 else [])]))
        if not c5:
            fails.append("A12-5: the plain group scan matched nothing -- 'no re-anchor line' "
                         "would be vacuous")
        rr5 = c.request("refine_group_scan", session_id=sid5,
                        values=[{"value": "100", "data_type": "NumericNoByte"},
                                {"value": "424242", "data_type": "NumericNoByte"}])
        d55 = rr5.get("data", rr5)
        c55, _ = rows(c, sid5)
        say("   unchanged refine -> ok=%s surviving=%s rows=%d"
            % (d55.get("ok"), d55.get("total"), len(c55)))
        if c5 and not c55:
            fails.append("A12-5: the plain pair was dropped by an unchanged refine")
        time.sleep(0.6)
        ra = since(mark5, "RefineGroup re-anchor")
        say("   'RefineGroup re-anchor' since the plain scan: %d   <-- must be 0" % len(ra))
        for l in ra[:3]:
            say("      " + l.strip()[:150])
        if ra:
            fails.append("A12-5: a Direct group leaf entered the re-anchor path")

        # ------------------------------------------------ step 6
        say("")
        say("== A12 step 6: 'carries no ValueAnchor' must be absent ==")
        noanchor = since(mark, "carries no ValueAnchor")
        say("   occurrences since this run began: %d   <-- must be 0" % len(noanchor))
        for l in noanchor[:3]:
            say("      " + l.strip()[:150])
        if noanchor:
            fails.append("A12-6: 'carries no ValueAnchor' fired -- a by-name hop dropped the stamp")

        for s in (sid, sid5):
            if s:
                c.request("end_group_scan", session_id=s)

    say("")
    say("steps 2, 3 and the growth half of 4 NOT RUN: they need the container to change size "
        "in play.")
    say("")
    for x in fails:
        say("FAIL: %s" % x)
    if not fails:
        say("PASS (steps 1, 4a, 5, 6)")
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
