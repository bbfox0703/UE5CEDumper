r"""A11 step 5 — appending into EXISTING SLACK must NOT drop the candidate.

    py tools/verify/a11_step5_slack.py     (DumperTest dev running + injected)

THE NON-REGRESSION THE ROW WARNS ABOUT. Steps 2-4 all end in "the candidate moved or died", so the
cheapest way to pass them is a rule that drops a container candidate whenever anything about the
header changes. That rule is wrong, and this is the case that catches it: `Arr_Int` is 5 of a
capacity-8 buffer, so `Add()` writes into slack and bumps `Num` with **Data unchanged**. Every
existing element is exactly where it was. A naive `{dataPtr,count}` comparison sees a changed count
and drops; the asymmetric rule must keep.

⚠ If this fails, it is a REGRESSION and not a missing feature — the row says so explicitly.

Emulates `Add()` into slack: element[5] = 60, `Num` 5 → 6, `Data` and `Max` untouched.
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
    return [x for x in (d.get("candidates") or d.get("results") or [])
            if "Arr_Int[" in (x.get("field_name") or "")]


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
        off, data = int(arr["offset"]), int(str(arr["array_data_addr"]), 16)
        esz = int(arr["array_elem_size"])
        header = inst + off
        hdr = read_bytes(c, header, 16)
        h_data, h_num, h_max = struct.unpack("<Qii", hdr)
        say("Arr_Int: Data=0x%X Num=%d Max=%d" % (h_data, h_num, h_max))
        if h_max <= h_num:
            say("BLOCKED: no slack (Num %d, Max %d) — an append would have to realloc, which is "
                "step 2's case, not step 5's" % (h_num, h_max))
            return 2
        say("slack available: %d element(s) — an append stays in place" % (h_max - h_num))

        r = c.request("begin_value_scan", data_type="Int32", scan_type="Exact",
                      value="30", deep=False, game_only=True, max_results=5000)
        sid = r.get("data", r).get("session_id")
        before = rows_for(c, sid)
        live_before = next((x for x in before
                            if int(str(x["instance_addr"]), 16) == inst), None)
        if not live_before:
            say("BLOCKED: no live candidate")
            c.request("end_value_scan", session_id=sid)
            return 2
        addr_before = int(str(live_before["addr"]), 16)
        say("live candidate: %s @ 0x%X" % (live_before.get("field_name"), addr_before))

        mark = time.strftime("%Y-%m-%d %H:%M:%S")
        time.sleep(1.1)

        say("")
        say("== Add() into slack: element[5]=60, Num 5 -> 6, Data and Max untouched ==")
        with Mutation(c, "Arr_Int slack element", data + 5 * esz, esz) as me:
            with Mutation(c, "Arr_Int Num", header + 8, 4,
                          expect_unchanged={"Data+Max": (header, 8)}) as mn:
                me.apply(struct.pack("<i", 60))
                mn.apply(struct.pack("<i", 6))
                mn.assert_others_unchanged()

                chk = read_bytes(c, header, 16)
                d2, n2, m2 = struct.unpack("<Qii", chk)
                say("   header now: Data=0x%X Num=%d Max=%d" % (d2, n2, m2))
                if d2 != data:
                    fails.append("Data moved — that is not an in-place append")
                if n2 != 6:
                    fails.append("Num is %d, expected 6" % n2)

                c.request("refine_value_scan", session_id=sid, scan_type="Exact", value="30")
                after = rows_for(c, sid)
                live_after = next((x for x in after
                                   if int(str(x["instance_addr"]), 16) == inst), None)
                say("")
                say("   rows after: %d" % len(after))
                for x in after:
                    say("      %-14s addr=%s inst=%s" % (x.get("field_name"), x.get("addr"),
                                                         x.get("instance_addr")))
                if not live_after:
                    fails.append("REGRESSION: the candidate was DROPPED by an in-place append. "
                                 "The naive {dataPtr,count} rule is back — nothing moved, and the "
                                 "row explicitly calls this a regression rather than a gap.")
                else:
                    got = int(str(live_after["addr"]), 16)
                    say("   survivor addr 0x%X (was 0x%X)  %s"
                        % (got, addr_before, "unchanged" if got == addr_before else "MOVED"))
                    if got != addr_before:
                        fails.append("the candidate survived but its address MOVED although the "
                                     "buffer did not — a spurious repoint")
                    else:
                        say("   OK: survived at the same address")

                lines = since(mark, "Refine re-anchor:")
                say("   re-anchor lines: %d" % len(lines))
                for l in lines:
                    say("      %s" % l.strip()[-125:])
                bad = [l for l in lines if "0 dropped" not in l]
                if bad:
                    fails.append("a re-anchor line reports drops on an in-place append: %s"
                                 % bad[-1].strip()[-125:])

        c.request("end_value_scan", session_id=sid)
        w2 = c.request("walk_instance", addr=live["addr"], array_limit=32)
        arr2 = next((f for f in (w2.get("fields") or []) if f.get("name") == "Arr_Int"), None)
        vals = [e["v"] for e in (arr2.get("elements") or [])]
        say("")
        say("fixture after restore: count=%s values=%s" % (arr2.get("count"), vals))
        if str(arr2.get("count")) != "5" or vals != ["10", "20", "30", "40", "50"]:
            fails.append("the Arr_Int fixture was NOT restored")

    say("")
    if fails:
        say("A11 step 5: FAIL")
        for f in fails:
            say("   - %s" % f)
        return 1
    say("A11 step 5: PASS — an in-place append keeps the candidate at its address, so the "
        "asymmetric rule is intact")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
