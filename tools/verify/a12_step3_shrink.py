r"""A12 step 3 — an element removed BEFORE the matched one must DROP the group candidate, and the
per-reason tally must say it was the CONTAINER, not the predicate.

    py tools/verify/a12_step3_shrink.py     (DumperTest dev running + injected, no UI)

WHY THE TALLY IS THE ASSERTION. A shrink and a value change both make the row disappear, so "the
row is gone" is not evidence about which happened. `RefineGroup cand[...]` breaks the drop down by
reason — `container-moved=`, `predicate-said-no=`, `unreadable=` — and only the first is this row's
claim. Asserting the count without the reason would pass for the wrong mechanism.

⚠ THE FIXTURE IS MADE SELECTIVE ON PURPOSE, and it costs the CDO control. Rewriting the live array
to {60001..60005} leaves the CDO holding {10,20,30,40,50}, so exactly ONE candidate matches. Two
reasons: the per-candidate diagnostic line is gated on a small candidate count (`kDiagCandidates`),
and this row's discriminator is the reason breakdown rather than a surviving sibling. The
unchanged-refine control below is what replaces the sibling: it shows the row does not simply die
on any refine.

⚠ Max is deliberately LEFT AT 8 while Num goes 5 → 4. `Macht.h`'s `ReadTArray` refuses a container
whose Max < Num, and that refusal increments the SAME `container-moved` bucket — so shrinking Max
too would make the expected tally appear for an entirely different reason.

⚠ scan-0.log / `[SCAN:grp]`: these markers are `Sein::Info("SCAN:grp", ...)`, whose explicit
category overrides Aura.cpp's `LOG_CAT "OARR"`. [A12-LOGPATH-2026-08-21]
"""
import pathlib
import re
import struct
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
sys.stdout.reconfigure(encoding="utf-8", errors="replace")

from mutate_guard import Mutation, assert_channel_carries, read_bytes  # noqa: E402
from pipe_client import PipeClient  # noqa: E402

LOGDIR = pathlib.Path.home() / "AppData/Local/UE5CEDumper/Logs/DumperTest"
SCANLOG = LOGDIR / "scan-0.log"
A, B = "60002", "60003"


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + chr(10))
    sys.stdout.flush()


def since(mark, needle):
    try:
        return [l for l in SCANLOG.read_text(encoding="utf-8", errors="replace").splitlines()
                if l.startswith("[") and l[1:20] >= mark and needle in l]
    except OSError:
        return []


def grows(c, sid, limit=200):
    r = c.request("query_group_candidates", session_id=sid, offset=0, limit=limit)
    d = r.get("data", r)
    return d.get("candidates") or d.get("results") or []


def tally(line, key):
    m = re.search(re.escape(key) + r"(\d+)", line)
    return int(m.group(1)) if m else None


def main():
    fails = []
    if not assert_channel_carries(SCANLOG, "[SCAN:grp]", "the group drop tally"):
        return 2

    with PipeClient() as c:
        c.assert_build()
        c.ensure_scanned()
        insts = c.request("find_instances", class_name="DumperTestActor",
                          max_results=10).get("instances") or []
        live = next((i for i in insts if i.get("name") == "DumperTestActor"), None)
        if not live:
            say("BLOCKED: no live DumperTestActor")
            return 2
        inst = int(str(live["addr"]), 16)
        w = c.request("walk_instance", addr=live["addr"], array_limit=32)
        arr = next((f for f in (w.get("fields") or []) if f.get("name") == "Arr_Int"), None)
        off, data = int(arr["offset"]), int(str(arr["array_data_addr"]), 16)
        esz = int(arr["array_elem_size"])
        header = inst + off
        hdr = read_bytes(c, header, 16)
        h_data, h_num, h_max = struct.unpack("<Qii", hdr)
        say("Arr_Int: Data=0x%X Num=%d Max=%d" % (h_data, h_num, h_max))
        if h_data != data or h_num != 5 or h_max < 5:
            say("BLOCKED: unexpected header")
            return 2
        elems = read_bytes(c, data, 5 * esz)

        with Mutation(c, "Arr_Int elements", data, 5 * esz) as me:
            # selective fixture: only the LIVE actor can match
            if not me.apply(struct.pack("<5i", 60001, 60002, 60003, 60004, 60005)):
                say("BLOCKED: could not install the selective fixture")
                return 2

            say("")
            say("== group scan: %s + %s (indices 1 and 2 of the live array only) ==" % (A, B))
            r = c.request("begin_group_scan", deep=True, game_only=True, max_results=50000,
                          values=[{"value": A, "data_type": "NumericNoByte"},
                                  {"value": B, "data_type": "NumericNoByte"}])
            d = r.get("data", r)
            sid = d.get("session_id")
            if not sid:
                say("BLOCKED: session_id null — %s" % str(d)[:180])
                return 2
            rows = grows(c, sid)
            say("   rows: %d" % len(rows))
            for x in rows:
                say("      inst=%s slots=%s addrs=%s"
                    % (x.get("instance_addr"),
                       [s.get("field_name") for s in (x.get("slots") or [])],
                       [s.get("addr") for s in (x.get("slots") or [])]))
            if len(rows) != 1:
                fails.append("expected exactly one candidate (the selective fixture); saw %d — "
                             "the per-candidate diagnostic is gated on a small count"
                             % len(rows))
            if not rows:
                c.request("end_group_scan", session_id=sid)
                say("BLOCKED: nothing matched")
                return 2
            names = [s.get("field_name") or "" for s in (rows[0].get("slots") or [])]
            idxs = [int(m.group(1)) for m in (re.search(r"\[(\d+)\]", n) for n in names) if m]
            say("   slot indices: %s" % idxs)
            if any(i > 2 for i in idxs):
                fails.append("a slot index is > 2, so the later drop would come from 'slot gone' "
                             "rather than 'container shrank' — the wrong branch")

            # ---- NEGATIVE CONTROL: unchanged refine keeps it -------------------
            say("")
            say("== NEGATIVE CONTROL: refine unchanged ==")
            mark0 = time.strftime("%Y-%m-%d %H:%M:%S")
            time.sleep(1.1)
            c.request("refine_group_scan", session_id=sid,
                      values=[{"value": A, "data_type": "NumericNoByte"},
                              {"value": B, "data_type": "NumericNoByte"}])
            ctrl = grows(c, sid)
            say("   rows: %d (was 1)" % len(ctrl))
            if not ctrl:
                fails.append("the row died on an UNCHANGED refine — every later conclusion would "
                             "be about that")
            cand0 = since(mark0, "RefineGroup cand[")
            if cand0:
                cm = tally(cand0[-1], "container-moved=")
                say("   container-moved on an unchanged refine: %s  <-- must be 0" % cm)
                if cm:
                    fails.append("an unchanged refine already reports container-moved=%s" % cm)
            else:
                fails.append("no per-candidate diagnostic line on the control refine — the tally "
                             "assertions below would have nothing to read")
            ra0 = since(mark0, "RefineGroup re-anchor")
            if ra0:
                fails.append("an unchanged refine emitted a re-anchor line")

            # ---- emulate RemoveAt(0), no shrink --------------------------------
            say("")
            say("== RemoveAt(0): tail down, Num 5 -> 4, Max LEFT at 8 ==")
            mark = time.strftime("%Y-%m-%d %H:%M:%S")
            time.sleep(1.1)
            with Mutation(c, "Arr_Int Num", header + 8, 4,
                          expect_unchanged={"Data": (header, 8)}) as mn:
                me.apply(struct.pack("<5i", 60002, 60003, 60004, 60005, 60005))
                mn.apply(struct.pack("<i", 4))
                mn.assert_others_unchanged()
                d2, n2, m2 = struct.unpack("<Qii", read_bytes(c, header, 16))
                say("   header: Data=0x%X Num=%d Max=%d" % (d2, n2, m2))
                if d2 != data:
                    fails.append("Data moved — that is step 2's shape")
                if n2 != 4 or m2 != 8:
                    fails.append("expected Num=4 Max=8, got Num=%d Max=%d" % (n2, m2))

                c.request("refine_group_scan", session_id=sid,
                          values=[{"value": A, "data_type": "NumericNoByte"},
                                  {"value": B, "data_type": "NumericNoByte"}])
                after = grows(c, sid)
                say("")
                say("   rows after: %d" % len(after))
                if after:
                    fails.append("(a) the candidate SURVIVED an in-place shrink — its leaves now "
                                 "read the neighbours' values")
                else:
                    say("   (a) OK: the candidate is gone")

                cl = since(mark, "RefineGroup cand[")
                say("   (b/c) per-candidate diagnostic lines: %d" % len(cl))
                for l in cl:
                    say("        %s" % l.strip()[-140:])
                if not cl:
                    fails.append("(b) no per-candidate line, so the REASON for the drop is "
                                 "unknown — 'gone' alone is not this row's claim")
                else:
                    last = cl[-1]
                    cm = tally(last, "container-moved=")
                    pn = tally(last, "predicate-said-no=")
                    ur = tally(last, "unreadable=")
                    say("   (b) container-moved=%s  (c) predicate-said-no=%s unreadable=%s"
                        % (cm, pn, ur))
                    if not cm:
                        fails.append("(b) container-moved is %s — the drop was not attributed to "
                                     "the container" % cm)
                    if pn:
                        fails.append("(c) predicate-said-no=%s — the leaves were rejected on "
                                     "value, which is a different mechanism" % pn)
                    if ur:
                        fails.append("(c) unreadable=%s — the container could not be read at all, "
                                     "which is also not this row's claim" % ur)

                ra = since(mark, "RefineGroup re-anchor")
                say("   (d) whole-pass re-anchor lines: %d" % len(ra))
                for l in ra:
                    say("        %s" % l.strip()[-140:])
                if ra:
                    last = ra[-1]
                    if "0 container element(s) repointed" not in last:
                        fails.append("(d) expected 0 repointed on an in-place shrink: %s"
                                     % last.strip()[-130:])
                    if "0 dropped" in last:
                        fails.append("(d) the whole-pass line reports 0 dropped")

            c.request("end_group_scan", session_id=sid)

        if read_bytes(c, data, 5 * esz) != elems or read_bytes(c, header, 16) != hdr:
            fails.append("the Arr_Int fixture was NOT restored")
        else:
            say("")
            say("fixture verified restored")

    say("")
    if fails:
        say("A12 step 3: FAIL")
        for f in fails:
            say("   - %s" % f)
        return 1
    say("A12 step 3: PASS — the candidate dropped, and the tally attributes it to the container "
        "rather than to the predicate")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
