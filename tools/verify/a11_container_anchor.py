r"""A11 steps 1 + 6 -- container candidates carry an element index; Direct ones never re-anchor.

    py tools/verify/a11_container_anchor.py

THE CLAIM (`A11`, build 3253). A Value Search candidate that lives INSIDE a container must be
re-anchored when the container grows, instead of being silently dropped or -- worse -- left
pointing at whatever now occupies the old address. The RULE is unit-pinned (15 assertions, two
negative controls); the WIRING is not, because no target compiles `Aura.cpp`.

WHICH STEPS THIS COVERS, AND WHICH IT CANNOT
  step 1  a scanned container value must come back with an `[i]` element index.            HERE
  step 6  a plain (non-container) field must NOT enter the new path -- no `Refine re-anchor`
          line at all.                                                                     HERE
  steps 2-5 all require the container to CHANGE SIZE in play (add/remove entries). Nothing on
          DumperTest grows a UPROPERTY container on its own, and there is no operator to play
          the game, so they stay open. Saying so is the point: a rig that quietly skipped them
          would leave the row looking checked.

The row names the evidence itself: grep by FORMAT STRING, `Refine re-anchor:`.

FIXTURES (DumperTest, read live before scanning rather than assumed):
  Arr_Int          TArray<int32>  = [10, 20, 30, 40, 50]   -> the container candidate
  I8_Neg / Health  plain scalars                            -> the Direct control
"""
import json
import pathlib
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient  # noqa: E402

# ⚠ offsets-0.log, NOT scan-0.log. `Refine re-anchor:` is emitted from Aura.cpp, whose
# `#define LOG_CAT "OARR"` Sein maps to LF_Offsets (Sein.cpp: { "OARR", 4, LF_Offsets }).
# The first version of this rig grepped scan-0.log, where the line can NEVER appear — which
# made step 6 ("expect NO re-anchor line") pass no matter what the code did. The old write-up
# even argued it was non-vacuous, on the grounds that the refine was shown to run over real
# candidates. That controlled for the wrong thing: it proved the STIMULUS happened, not that
# the DETECTOR could see anything. [A11-LOGPATH-2026-08-21]
LOGDIR = pathlib.Path.home() / "AppData/Local/UE5CEDumper/Logs/DumperTest"
OFFSETS = LOGDIR / "offsets-0.log"
SCAN = OFFSETS          # kept as the name the rest of the file uses


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + "\n")
    # Flush: a backgrounded rig's stdout is a FILE, which Python block-buffers --
    # a long run then shows an EMPTY output file and looks hung.
    sys.stdout.flush()


def assert_detector_can_see_oarr():
    """The channel must be shown to CARRY this category before an absence means anything.

    "0 matching lines" is the same output for "the code did not do it" and "I am reading the
    wrong file". Requiring at least one OARR-category line separates them.
    """
    if not OFFSETS.exists():
        say("DETECTOR CHECK FAILED: %s does not exist — an absence proves nothing." % OFFSETS)
        return False
    text = OFFSETS.read_text(encoding="utf-8", errors="replace")
    # Aura.cpp's own category tag, present on every line it writes.
    if "[OARR]" not in text:
        say("DETECTOR CHECK FAILED: %s carries no [OARR] line at all, so it is not the channel "
            "Aura.cpp writes to (or nothing has written yet). An absence proves nothing." % OFFSETS)
        return False
    say("detector OK: %s carries [OARR] traffic, so an absent marker is a real absence" % OFFSETS.name)
    return True


def scan_lines_since(mark):
    out = []
    try:
        for l in SCAN.read_text(encoding="utf-8", errors="replace").splitlines():
            if l.startswith("[") and l[1:20] >= mark:
                out.append(l)
    except OSError:
        pass
    return out


def candidates(c, sid, limit=200):
    r = c.request("query_candidates", session_id=sid, offset=0, limit=limit)
    d = r.get("data", r)
    return d.get("candidates") or d.get("results") or [], d


def main():
    if not assert_detector_can_see_oarr():
        say("ABORT: the absence assertions below would be vacuous.")
        return 2
    fails = []
    with PipeClient() as c:
        c.assert_build()
        c.ensure_scanned()

        # --- confirm the fixture really is a container with the value we will scan ---
        inst = None
        for i in (c.request("find_instances", class_name="DumperTestActor",
                            max_results=5).get("instances") or []):
            if i.get("name") == "DumperTestActor":
                inst = i["addr"]
        if not inst:
            say("FAIL: no live DumperTestActor (only the CDO) -- fixture unavailable")
            return 1
        w = c.request("walk_instance", addr=inst, array_limit=32)
        arr = next((f for f in (w.get("fields") or []) if f.get("name") == "Arr_Int"), None)
        if not arr or not arr.get("elements"):
            say("FAIL: Arr_Int not readable -- step 1 would be vacuous")
            return 1
        vals = [e["v"] for e in arr["elements"]]
        say("fixture Arr_Int = %s  (count=%s, elem %sB)" % (vals, arr.get("count"),
                                                            arr.get("array_elem_size")))
        target = arr["elements"][2]["v"]          # the middle element, index 2
        say("scanning for %s -- expected at Arr_Int[2]" % target)

        # ---------------------------------------------------------- step 1
        say("")
        say("== A11 step 1: the candidate must carry an [i] element index ==")
        r = c.request("begin_value_scan", data_type="Int32", scan_type="Exact",
                      value=target, deep=True, game_only=True, max_results=5000)
        d = r.get("data", r)
        sid = d.get("session_id")
        say("   session=%s candidates=%s" % (sid, d.get("count") or d.get("total")))
        cands, _ = candidates(c, sid)
        say("   returned %d candidate row(s)" % len(cands))
        # The element index lives in `field_name` ("Arr_Int[2]"), not in a `path` /
        # `class_field` key -- neither of those exists on a candidate row.
        hit = [x for x in cands if "Arr_Int[" in (x.get("field_name") or "")]
        for x in hit[:4]:
            say("      %-22s %-14s addr=%s inst=%s"
                % (x.get("field_name"), x.get("class_name"), x.get("addr"),
                   x.get("instance_addr")))
        if not hit:
            sample = [x.get("field_name") for x in cands[:6]]
            say("      sample field_names: %s" % sample)
            fails.append("A11-1: no candidate path containing 'Arr_Int[' -- the element index is "
                         "absent, or the scan did not reach the container")
        else:
            say("   OK: %d candidate(s) carry the element index" % len(hit))

        # ---------------------------------------------------------- step 6
        say("")
        say("== A11 step 6: a Direct (non-container) candidate must NOT re-anchor ==")
        mark = time.strftime("%Y-%m-%d %H:%M:%S")
        time.sleep(1.1)
        r2 = c.request("begin_value_scan", data_type="Int32", scan_type="Exact",
                       value="100", deep=False, game_only=True, max_results=5000)
        d2 = r2.get("data", r2)
        sid2 = d2.get("session_id")
        say("   plain scan (deep OFF) session=%s candidates=%s"
            % (sid2, d2.get("count") or d2.get("total")))
        c2, _ = candidates(c, sid2, limit=20)
        say("   sample field_name: %s" % (c2[0].get("field_name") if c2 else "-"))
        if not c2:
            fails.append("A11-6: the Direct scan produced no candidates -- nothing to refine, "
                         "so 'no re-anchor line' would be vacuous")
        rr = c.request("refine_value_scan", session_id=sid2, scan_type="Exact", value="100")
        dd = rr.get("data", rr)
        say("   refine -> ok=%s remaining=%s" % (dd.get("ok"), dd.get("total") if dd.get("total") is not None else dd.get("count")))
        time.sleep(0.6)
        anchor = [l for l in scan_lines_since(mark) if "Refine re-anchor" in l]
        say("   'Refine re-anchor:' lines since the Direct scan began: %d   <-- must be 0"
            % len(anchor))
        for l in anchor[:3]:
            say("      " + l.strip()[:150])
        if anchor:
            fails.append("A11-6: a Direct candidate entered the re-anchor path")

        for s in (sid, sid2):
            if s:
                c.request("end_value_scan", session_id=s)

    say("")
    say("steps 2-5 NOT RUN: they need the container to change size in play "
        "(add/remove entries) and nothing here does that unattended.")
    say("")
    for x in fails:
        say("FAIL: %s" % x)
    if not fails:
        say("PASS (steps 1 and 6)")
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
