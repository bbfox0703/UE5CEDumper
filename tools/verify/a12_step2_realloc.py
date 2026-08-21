r"""A12 step 2 — in GROUP mode a realloc must repoint every matched LEAF, not just the candidate.

    py tools/verify/a12_step2_realloc.py      (DumperTest dev running + injected, no UI)

WHY GROUP MODE IS ITS OWN ROW. A single-value candidate has one address to fix. A group candidate
carries one address PER SLOT, each stamped with its own `ValueAnchor`, and all of them go stale
together when the buffer moves. A11's fix repointing the candidate is not evidence that the group
path repoints the leaves — different code, different stamps, and the "by-name hops" can drop a
stamp silently (which is what step 6's `carries no ValueAnchor` alarm exists for).

THE MUTATION. `Arr_Int` is 5 of a capacity-8 buffer, so the whole array can be slid **+4 bytes
inside its own allocation**: copy the five elements to `data+4` and rewrite the header to
`{data+4, 5, 7}`. Every leaf must then be found 4 bytes higher than before.

⭐ **+4 is the point.** A new page (A11 step 2's approach) would also pass if the code simply
re-derived the leaf from the container base — the addresses would look right for the wrong reason.
A 4-byte slide inside the same allocation means the old addresses are still perfectly readable and
still contain plausible data, so "every slot address moved by exactly +4" cannot be produced by a
stale read or by luck.

⚠ Count is deliberately left at 5. The re-anchor must fire on Data alone; bundling a count change
would let a count-only implementation pass.

⚠ `offsets-0.log`, not `scan-0.log` — every marker here is `LOG_CAT "OARR"`. [A12-LOGPATH]
"""
import pathlib
import struct
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
sys.stdout.reconfigure(encoding="utf-8", errors="replace")

from mutate_guard import Mutation, assert_channel_carries, read_bytes  # noqa: E402
from pipe_client import PipeClient  # noqa: E402

LOGDIR = pathlib.Path.home() / "AppData/Local/UE5CEDumper/Logs/DumperTest"
OFFSETS = LOGDIR / "offsets-0.log"
SLOT_A, SLOT_B = "20", "40"          # Arr_Int[1] and Arr_Int[3]


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + chr(10))
    sys.stdout.flush()


def since(mark, needle):
    try:
        return [l for l in OFFSETS.read_text(encoding="utf-8", errors="replace").splitlines()
                if l.startswith("[") and l[1:20] >= mark and needle in l]
    except OSError:
        return []


def grows(c, sid, limit=200):
    r = c.request("query_group_candidates", session_id=sid, offset=0, limit=limit)
    d = r.get("data", r)
    return d.get("candidates") or d.get("results") or []


def arr_rows(rows):
    out = []
    for x in rows:
        names = [s.get("field_name") or "" for s in (x.get("slots") or [])]
        if any("Arr_Int" in n for n in names):
            out.append(x)
    return out


def slot_addrs(row):
    return [int(str(s.get("addr")), 16) for s in (row.get("slots") or [])]


def main():
    fails = []
    if not assert_channel_carries(OFFSETS, "[OARR]", "the group re-anchor marker"):
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
        say("Arr_Int: header=0x%X Data=0x%X Num=%d Max=%d elem=%dB"
            % (header, h_data, h_num, h_max, esz))
        if h_data != data or h_num != 5:
            say("BLOCKED: fixture is not the expected 5-element array")
            return 2
        if h_max < 8:
            say("BLOCKED: Max=%d < 8 — sliding the array +4 would overrun the allocation" % h_max)
            return 2
        elems = read_bytes(c, data, 5 * esz)
        say("elements: %s" % list(struct.unpack("<5i", elems)))

        # ---- group scan, two slots in the SAME container -----------------------
        say("")
        say("== group scan: slots %s and %s (both inside Arr_Int) ==" % (SLOT_A, SLOT_B))
        r = c.request("begin_group_scan", deep=True, game_only=True, max_results=50000,
                      values=[{"value": SLOT_A, "data_type": "NumericNoByte"},
                              {"value": SLOT_B, "data_type": "NumericNoByte"}])
        d = r.get("data", r)
        sid = d.get("session_id")
        if not sid:
            say("BLOCKED: session_id is null — a group slot rejects Int32/Float; it must be "
                "NumericNoByte / NumericAll. Response: %s" % str(d)[:200])
            return 2
        rows = arr_rows(grows(c, sid))
        say("   session=%s   Arr_Int rows=%d" % (sid, len(rows)))
        for x in rows:
            say("      inst=%s slots=%s addrs=%s"
                % (x.get("instance_addr"),
                   [s.get("field_name") for s in (x.get("slots") or [])],
                   [s.get("addr") for s in (x.get("slots") or [])]))
        live_row = next((x for x in rows if int(str(x["instance_addr"]), 16) == inst), None)
        cdo_rows = [x for x in rows if int(str(x["instance_addr"]), 16) != inst]
        if not live_row:
            say("BLOCKED: no group candidate on the live actor")
            c.request("end_group_scan", session_id=sid)
            return 2
        if not cdo_rows:
            say("BLOCKED: no CDO row — there would be no paired control")
            c.request("end_group_scan", session_id=sid)
            return 2
        live_before = sorted(slot_addrs(live_row))
        want = sorted([data + 1 * esz, data + 3 * esz])
        say("   live slot addrs %s, expected %s  %s"
            % ([hex(a) for a in live_before], [hex(a) for a in want],
               "OK" if live_before == want else "MISMATCH"))
        if live_before != want:
            fails.append("the live slot addresses are not at data+1elem / data+3elem")
        cdo_before = {int(str(x["instance_addr"]), 16): sorted(slot_addrs(x)) for x in cdo_rows}

        # ---- NEGATIVE CONTROL: an unchanged refine keeps everything ------------
        say("")
        say("== NEGATIVE CONTROL: refine with the same two values, nothing changed ==")
        mark0 = time.strftime("%Y-%m-%d %H:%M:%S")
        time.sleep(1.1)
        c.request("refine_group_scan", session_id=sid,
                  values=[{"value": SLOT_A, "data_type": "NumericNoByte"},
                          {"value": SLOT_B, "data_type": "NumericNoByte"}])
        ctrl = arr_rows(grows(c, sid))
        say("   rows: %d (was %d)" % (len(ctrl), len(rows)))
        if len(ctrl) != len(rows):
            fails.append("an unchanged refine already lost rows — later conclusions would be "
                         "about that, not about the slide")
        ra0 = since(mark0, "RefineGroup re-anchor")
        say("   re-anchor lines on an unchanged refine: %d  <-- must be 0" % len(ra0))
        if ra0:
            fails.append("an unchanged refine emitted a re-anchor line: %s"
                         % ra0[-1].strip()[-120:])

        # ---- slide the array +4 inside its own allocation ----------------------
        say("")
        say("== slide the whole array +4 bytes inside its own allocation ==")
        mark = time.strftime("%Y-%m-%d %H:%M:%S")
        time.sleep(1.1)
        with Mutation(c, "Arr_Int buffer(+4 region)", data, 6 * esz) as mb:
            with Mutation(c, "Arr_Int header", header, 16) as mh:
                # elements copied up one slot; slot 0 keeps its old value (harmless filler)
                slid = struct.pack("<i", 10) + elems
                if not mb.apply(slid):
                    fails.append("could not slide the elements")
                elif not mh.apply(struct.pack("<Qii", data + esz, 5, 7)):
                    fails.append("could not install the slid header")
                else:
                    chk = read_bytes(c, header, 16)
                    d2, n2, m2 = struct.unpack("<Qii", chk)
                    say("   header now: Data=0x%X (was 0x%X, +%d) Num=%d Max=%d"
                        % (d2, data, d2 - data, n2, m2))
                    if n2 != 5:
                        fails.append("Num changed — the re-anchor must fire on Data alone")

                    c.request("refine_group_scan", session_id=sid,
                              values=[{"value": SLOT_A, "data_type": "NumericNoByte"},
                                      {"value": SLOT_B, "data_type": "NumericNoByte"}])
                    after = arr_rows(grows(c, sid))
                    say("")
                    say("   rows after: %d" % len(after))
                    for x in after:
                        say("      inst=%s addrs=%s" % (x.get("instance_addr"),
                                                        [s.get("addr") for s in (x.get("slots") or [])]))
                    live_after = next((x for x in after
                                       if int(str(x["instance_addr"]), 16) == inst), None)
                    if not live_after:
                        fails.append("(a) the live group candidate was DROPPED after the buffer "
                                     "moved — the pre-3261 behaviour")
                    else:
                        got = sorted(slot_addrs(live_after))
                        exp = sorted(a + esz for a in live_before)
                        say("   (b) live slot addrs %s, expected each +%d -> %s  %s"
                            % ([hex(a) for a in got], esz, [hex(a) for a in exp],
                               "OK" if got == exp else "WRONG"))
                        if got != exp:
                            fails.append("(b) the leaves were not repointed by exactly +%d: %s "
                                         "vs %s" % (esz, [hex(a) for a in got],
                                                    [hex(a) for a in exp]))
                    for ia, before_addrs in cdo_before.items():
                        now = next((x for x in after
                                    if int(str(x["instance_addr"]), 16) == ia), None)
                        if not now:
                            fails.append("(c) the CDO group row vanished — a blanket drop")
                        elif sorted(slot_addrs(now)) != before_addrs:
                            fails.append("(c) the CDO slot addresses MOVED although its header "
                                         "never changed — the branch fired blanket")
                    if not [f for f in fails if f.startswith("(c)")]:
                        say("   (c) OK: CDO row present with unchanged slot addresses")

                    lines = since(mark, "RefineGroup re-anchor")
                    say("   (d) RefineGroup re-anchor lines: %d" % len(lines))
                    for l in lines:
                        say("        %s" % l.strip()[-130:])
                    if len(lines) != 1:
                        fails.append("(d) expected exactly one re-anchor line, saw %d" % len(lines))
                    elif "repointed" not in lines[0]:
                        fails.append("(d) the line does not report a repoint")

                    va = since(mark, "carries no ValueAnchor")
                    say("   (e) 'carries no ValueAnchor' since the mark: %d  <-- must be 0"
                        % len(va))
                    if va:
                        fails.append("(e) a leaf lost its ValueAnchor stamp: %s"
                                     % va[-1].strip()[-130:])
                mh.assert_others_unchanged()

        c.request("end_group_scan", session_id=sid)
        if read_bytes(c, header, 16) != hdr or read_bytes(c, data, 5 * esz) != elems:
            fails.append("the Arr_Int fixture was NOT fully restored")
        else:
            say("")
            say("fixture verified restored (header and elements)")

    say("")
    if fails:
        say("A12 step 2: FAIL")
        for f in fails:
            say("   - %s" % f)
        return 1
    say("A12 step 2: PASS — every matched leaf repointed by exactly the slide distance, the CDO "
        "row untouched, one re-anchor line, no lost anchors")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
