#!/usr/bin/env python3
r"""L81 `[A4-AB4-BETWEEN]`: NumericNoByte Between must reach UInt16 / Int16 fields whose range only
PARTLY fits the width -- single scan and group slot. The snapshot-group half is UI-only and is not here.

    py tools/verify/l81_between_unsigned.py

The fixture (DumperTest 5.4, ADumperTestActor) holds U16 = 54321 and I16 = -12345, which are neither
the row's "small value" nor "near 32767", so there are two arms:

  NO-WRITE  the ranges the defect ALSO breaks, on the fixture's own values:
              Between -5 .. 60000       -> U16 (54321): -5 has no unsigned encoding
              Between -20000 .. 70000   -> I16 (-12345): 70000 has no Int16 encoding
            control: Between 40000 .. 70000 must NOT return I16 (the predicate still applies)
  LITERAL   write U16 := 7 and I16 := 32760, then the row's own ranges -5..10 -> U16 and
            10..70000 -> I16, as single scans AND as group slot 0 (slot 1 = FrozenInt 424242 anchors
            the actor); restored and re-read afterwards.

Pre-fix, the Between bounds were encoded per width and a width that could not hold one bound was
dropped, so the field was never a candidate. Run with the UI closed (2 of 3 pipe slots).
"""
import os
import struct
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pipe_client import PipeClient                          # noqa: E402
from ad4_contested import find_live_actor                   # noqa: E402
from mutate_guard import read_bytes, write_bytes            # noqa: E402

fails = []


def say(s=""):
    print(s)
    sys.stdout.flush()


def scan(c, lo, hi, actor, want, label):
    r = c.request("begin_value_scan", data_type="NumericNoByte", scan_type="Between",
                  value=str(lo), value2=str(hi), game_only=True, max_results=50000)
    d = r.get("data", r)
    sid = d.get("session_id")
    total = d.get("count") if d.get("count") is not None else d.get("total")
    trunc = d.get("truncated") or d.get("deadline_hit")
    names = set()
    try:
        for f in (want,):
            q = c.request("query_candidates", session_id=sid, offset=0, limit=500, filter=f)
            qd = q.get("data", q)
            for x in (qd.get("candidates") or qd.get("results") or []):
                if str(x.get("instance_addr", "")).lower() == actor.lower():
                    names.add((x.get("field_name"), x.get("field_type") or x.get("type")))
    finally:
        if sid:
            c.request("end_value_scan", session_id=sid)
    hit = any(n == want for n, _t in names)
    say("   %-28s Between %6s .. %-6s -> %s candidates%s; %s on the actor: %s"
        % (label, lo, hi, total, " (TRUNCATED)" if trunc else "", want,
           sorted(names) if names else "absent"))
    if trunc:
        fails.append("%s: the scan was truncated -- an absence would be meaningless" % label)
    return hit


def group(c, lo, hi, actor, want, label):
    r = c.request("begin_group_scan", deep=False, game_only=True, max_results=50000, per_slot_cap=4096,
                  values=[{"data_type": "NumericNoByte", "scan_type": "Between", "value": str(lo), "value2": str(hi)},
                          {"data_type": "NumericNoByte", "scan_type": "Exact", "value": "424242"}])
    d = r.get("data", r)
    sid = d.get("session_id")
    capped = d.get("per_slot_cap_hit")
    leaves = []
    try:
        q = c.request("query_group_slot_leaves", session_id=sid, instance_addr=actor, slot_index=0, limit=5000)
        qd = q.get("data", q)
        leaves = [x.get("field_name") for x in (qd.get("leaves") or qd.get("results") or [])]
        capped = capped or qd.get("per_slot_cap_hit")
    finally:
        if sid:
            c.request("end_group_scan", session_id=sid)
    hit = want in leaves
    say("   %-28s slot0 Between %s .. %s + slot1 424242 -> matched=%s, per_slot_cap_hit=%s, slot-0 leaves on "
        "the actor: %d, %s among them: %s" % (label, lo, hi, d.get("total"), capped, len(leaves), want, hit))
    if capped:
        fails.append("%s: per_slot_cap_hit -- the leaf list is truncated" % label)
    return hit


def main():
    with PipeClient() as c:
        say("build %s" % c.assert_build())
        c.ensure_scanned()
        act = find_live_actor(c)
        actor = act["addr"]
        w = c.request("walk_instance", addr=actor, array_limit=4)
        flds = {f.get("name"): f for f in (w.get("fields") or [])}
        off = {}
        for n in ("U16", "I16", "FrozenInt"):
            f = flds.get(n)
            if not f:
                say("FAIL: %s not in walk_instance" % n)
                return 2
            o = f.get("offset")
            off[n] = int(o, 16) if isinstance(o, str) else int(o)
        base = int(actor, 16)
        u16 = struct.unpack("<H", read_bytes(c, base + off["U16"], 2))[0]
        i16 = struct.unpack("<h", read_bytes(c, base + off["I16"], 2))[0]
        fz = struct.unpack("<i", read_bytes(c, base + off["FrozenInt"], 4))[0]
        say("actor %s: U16 @+0x%X = %d, I16 @+0x%X = %d, FrozenInt @+0x%X = %d"
            % (actor, off["U16"], u16, off["I16"], i16, off["FrozenInt"], fz))
        if (u16, i16, fz) != (54321, -12345, 424242):
            fails.append("fixture values are not the documented 54321 / -12345 / 424242")

        say("")
        say("== NO-WRITE arm (the fixture's own values) ==")
        if not scan(c, -5, 60000, actor, "U16", "U16 via -5..60000"):
            fails.append("no-write: U16 absent from Between -5..60000")
        if not scan(c, -20000, 70000, actor, "I16", "I16 via -20000..70000"):
            fails.append("no-write: I16 absent from Between -20000..70000")
        if scan(c, 40000, 70000, actor, "I16", "control: I16 via 40000..70000"):
            fails.append("control: I16 (-12345) returned by Between 40000..70000")

        say("")
        say("== LITERAL arm: U16 := 7, I16 := 32760 (restored below) ==")
        orig_u, orig_i = read_bytes(c, base + off["U16"], 2), read_bytes(c, base + off["I16"], 2)
        try:
            write_bytes(c, base + off["U16"], struct.pack("<H", 7))
            write_bytes(c, base + off["I16"], struct.pack("<h", 32760))
            nu = struct.unpack("<H", read_bytes(c, base + off["U16"], 2))[0]
            ni = struct.unpack("<h", read_bytes(c, base + off["I16"], 2))[0]
            say("   written: U16 = %d, I16 = %d" % (nu, ni))
            if (nu, ni) != (7, 32760):
                fails.append("literal: the write did not land")
            if not scan(c, -5, 10, actor, "U16", "U16 via -5..10"):
                fails.append("literal: U16 absent from Between -5..10")
            if not scan(c, 10, 70000, actor, "I16", "I16 via 10..70000"):
                fails.append("literal: I16 absent from Between 10..70000")
            if not group(c, -5, 10, actor, "U16", "GROUP U16 via -5..10"):
                fails.append("group: U16 absent from slot 0 (-5..10)")
            if not group(c, 10, 70000, actor, "I16", "GROUP I16 via 10..70000"):
                fails.append("group: I16 absent from slot 0 (10..70000)")
        finally:
            write_bytes(c, base + off["U16"], orig_u)
            write_bytes(c, base + off["I16"], orig_i)
            ru = struct.unpack("<H", read_bytes(c, base + off["U16"], 2))[0]
            ri = struct.unpack("<h", read_bytes(c, base + off["I16"], 2))[0]
            say("   restored: U16 = %d, I16 = %d" % (ru, ri))
            if (ru, ri) != (54321, -12345):
                fails.append("RESTORE FAILED")
    say("")
    for f in fails:
        say("FAIL: " + f)
    say("L81 pipe arms: %s" % ("PASS" if not fails else "FAIL"))
    return 0 if not fails else 1


if __name__ == "__main__":
    sys.exit(main())
