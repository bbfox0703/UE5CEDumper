r"""A11 step 3 — an element removed BEFORE the candidate's index must DROP it, not silently
re-read the neighbour.

    py tools/verify/a11_step3_shrink.py    (DumperTest dev running + injected; run LAST — it
                                            rewrites the Arr_Int fixture, and restores it)

THE SILENT-WRONG-VALUE CASE. `RemoveAt` on a `TArray` memmoves the tail down IN PLACE: the buffer
does not move, `Count` drops by one, and every element after the removed index now sits one slot
lower. A candidate anchored to a raw address therefore keeps reading cleanly — and returns its
NEIGHBOUR's value. Nothing about `{dataPtr}` changed, so a data-pointer check cannot see it; only
comparing `Count` can.

WHAT IS EMULATED. This reproduces `RemoveAt(0, 1, EAllowShrinking::No)`: the tail is memmoved down
and `Num` goes 5 → 4, with `Max` left at 8. UE's default `RemoveAt` ALLOWS shrinking and may
realloc, which is a different observable (that one is step 2's shape). Only the non-shrinking route
is exercised here and the write-up must say so.

⚠ THE PRECONDITION IS PART OF THE TEST. Before refining, the candidate's old address must still
read the scanned value — otherwise a drop is explained by the value changing rather than by the
count. The rig asserts it.

⚠ deep=False, deliberately, and it differs from the plan this came from. A deep descriptor carries
`ValueAnchor::Unknown`, so the container branch is never reached and the run would be vacuous —
the same reason step 2 uses it. deep=False was measured to return container rows here.
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


def rows_for(c, sid, limit=400):
    r = c.request("query_candidates", session_id=sid, offset=0, limit=limit)
    d = r.get("data", r)
    all_rows = d.get("candidates") or d.get("results") or []
    return [x for x in all_rows if "Arr_Int[" in (x.get("field_name") or "")]


def main():
    fails = []
    if not assert_channel_carries(OFFSETS, "[OARR]", "the re-anchor marker"):
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
        off = int(arr["offset"])
        data = int(str(arr["array_data_addr"]), 16)
        esz = int(arr["array_elem_size"])
        header = inst + off
        hdr = read_bytes(c, header, 16)
        h_data, h_num, h_max = struct.unpack("<Qii", hdr)
        say("Arr_Int: header=0x%X Data=0x%X Num=%d Max=%d elem=%dB"
            % (header, h_data, h_num, h_max, esz))
        if h_data != data or h_num != 5:
            say("BLOCKED: fixture is not the expected 5-element array")
            return 2
        orig_elems = read_bytes(c, data, 5 * esz)
        say("elements: %s" % list(struct.unpack("<5i", orig_elems)))

        with Mutation(c, "Arr_Int data", data, 5 * esz,
                      expect_unchanged={"header": (header, 16)}) as md:
            # ---- make index 3 also 30, so a shrink can be told from a re-read ----
            if not md.apply(struct.pack("<5i", 10, 20, 30, 30, 50)):
                fails.append("could not seed the two-match fixture")
                return 1
            md.assert_others_unchanged()

            r = c.request("begin_value_scan", data_type="Int32", scan_type="Exact",
                          value="30", deep=False, game_only=True, max_results=5000)
            sid = r.get("data", r).get("session_id")
            before = rows_for(c, sid)
            say("")
            say("scan for 30: %d Arr_Int row(s)" % len(before))
            for x in before:
                say("   %-14s addr=%s inst=%s" % (x.get("field_name"), x.get("addr"),
                                                  x.get("instance_addr")))
            live_rows = [x for x in before if int(str(x["instance_addr"]), 16) == inst]
            cdo_rows = [x for x in before if int(str(x["instance_addr"]), 16) != inst]
            # arithmetic cross-check rather than trusting the [i] label
            want = {data + 2 * esz, data + 3 * esz}
            got = {int(str(x["addr"]), 16) for x in live_rows}
            say("   live addrs %s, expected %s  %s"
                % (sorted(hex(a) for a in got), sorted(hex(a) for a in want),
                   "OK" if got == want else "MISMATCH"))
            if got != want:
                fails.append("the two live candidates are not at data+2 and data+3 — the fixture "
                             "is not what the rest of this rig assumes")
            if len(cdo_rows) != 1:
                fails.append("expected exactly one CDO row as the paired control, saw %d"
                             % len(cdo_rows))

            mark = time.strftime("%Y-%m-%d %H:%M:%S")
            time.sleep(1.1)

            # ---- emulate RemoveAt(0, 1, EAllowShrinking::No) --------------------
            say("")
            say("== RemoveAt(0) with no shrink: tail memmoved down, Num 5 -> 4, Max left at 8 ==")
            with Mutation(c, "Arr_Int Num", header + 8, 4) as mn:
                md.apply(struct.pack("<5i", 20, 30, 30, 50, 50))
                mn.apply(struct.pack("<i", 4))

                chk = read_bytes(c, header, 16)
                d2, n2, m2 = struct.unpack("<Qii", chk)
                say("   header now: Data=0x%X Num=%d Max=%d" % (d2, n2, m2))
                if d2 != data:
                    fails.append("Data moved — that is step 2's shape, not step 3's")
                if n2 != 4:
                    fails.append("Num is %d, expected 4" % n2)
                if m2 < 4:
                    fails.append("Max %d < Num — ReadTArray refuses this and the drop would come "
                                 "from an unreadable container, not from the shrink" % m2)
                # the precondition that makes this the SILENT case
                still = struct.unpack("<i", read_bytes(c, data + 2 * esz, 4))[0]
                say("   old candidate address data+2 still reads %d (must be 30)" % still)
                if still != 30:
                    fails.append("data+2 no longer reads 30, so a drop could be explained by the "
                                 "value rather than by the count — the test would be void")

                c.request("refine_value_scan", session_id=sid, scan_type="Exact", value="30")
                after = rows_for(c, sid)
                say("")
                say("   rows after: %d" % len(after))
                for x in after:
                    say("      %-14s addr=%s inst=%s" % (x.get("field_name"), x.get("addr"),
                                                         x.get("instance_addr")))
                live_after = [x for x in after if int(str(x["instance_addr"]), 16) == inst]
                cdo_after = [x for x in after if int(str(x["instance_addr"]), 16) != inst]

                if live_after:
                    fails.append("(a) %d live candidate(s) SURVIVED the shrink — they now read "
                                 "the neighbour's value, which is the silent-wrong-value case"
                                 % len(live_after))
                else:
                    say("   (a) OK: both live candidates dropped")
                if not cdo_after:
                    fails.append("(b) the CDO row vanished too — a blanket wipe, which would "
                                 "produce the same (a) for the wrong reason")
                else:
                    say("   (b) OK: the CDO row survived (paired control)")

                lines = since(mark, "Refine re-anchor:")
                say("   (c) re-anchor lines: %d" % len(lines))
                for l in lines:
                    say("        %s" % l.strip()[-125:])
                if not lines:
                    fails.append("(c) no re-anchor line — nothing reported the drops")
                else:
                    last = lines[-1]
                    if "0 dropped" in last:
                        fails.append("(c) the line reports 0 dropped: %s" % last.strip()[-125:])
                    if "0 container element(s) repointed" not in last:
                        fails.append("(d) expected 0 repointed on an in-place shrink: %s"
                                     % last.strip()[-125:])
                    else:
                        say("   (d) OK: 0 repointed, as an in-place shrink should report")

            c.request("end_value_scan", session_id=sid)

        # ---- both mutations restored; verify from the game's own view ----------
        w2 = c.request("walk_instance", addr=live["addr"], array_limit=32)
        arr2 = next((f for f in (w2.get("fields") or []) if f.get("name") == "Arr_Int"), None)
        vals = [e["v"] for e in (arr2.get("elements") or [])]
        say("")
        say("fixture after restore: count=%s values=%s" % (arr2.get("count"), vals))
        if str(arr2.get("count")) != "5" or vals != ["10", "20", "30", "40", "50"]:
            fails.append("the Arr_Int fixture was NOT restored to [10,20,30,40,50] × 5")

    say("")
    if fails:
        say("A11 step 3: FAIL")
        for f in fails:
            say("   - %s" % f)
        return 1
    say("A11 step 3: PASS — an in-place shrink drops the affected candidates while the untouched "
        "sibling survives (emulates RemoveAt with EAllowShrinking::No only)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
