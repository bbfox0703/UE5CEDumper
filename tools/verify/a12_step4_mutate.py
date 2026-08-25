r"""A12 step 4 — the TMap case: growth must repoint the leaves, removal must drop the candidate,
and neither may take the other's rows with it.

    py tools/verify/a12_step4_mutate.py     (DumperTest dev running + injected, no UI)

WHY THIS ROW EXISTS SEPARATELY. `TMap`/`TSet` are sparse: `MaxIndex` is not the element count and a
freed slot keeps its address. The row calls this "THE UNIT TRAP" — a `MaxCapacity`-vs-`MaxIndex`
mismatch drops every candidate on the very first refine, with nothing changed in the game.
Step 4a already showed that does not happen; this is the other half.

FIXTURE (read live, DumperTest `Map_IntToVec3f`): stride 24, key `int32` @0, value
`DumperTestVec3f` @+4 (three floats). Element 0 = {X=6201, Y=6202, Z=6203}, so the two scanned
slots are `Data+4` and `Data+8`. Header: Data / MaxIndex 3 / Cap 4 / inline bit word 0x7 /
secondary ptr 0 / NumBits 3.

⭐ THE GROWTH HALF ZEROES THE SOURCE, and that is the whole design. Copy the elements to a fresh
page, point the header there, and then **wipe the original bytes**. Without the wipe, a candidate
that was never repointed would still read 6201 at its stale address and SURVIVE — passing the test
by doing nothing. With it, survival is only possible if the leaves actually moved.

⚠ THE REMOVAL HALF NEEDS BOTH CONTROLS OR IT MEANS NOTHING:
  * before — clear the bit of an element the candidate does NOT point at; the row must SURVIVE, or
    the rule is "any sparse change kills everything" and the real result is unattributable.
  * after — restore the bit; the row must COME BACK, separating "my slot was freed" from "the
    container is now permanently unreadable".

⚠ Expect `container-moved=1`, not 2: the slot loop stops at the first emptied slot.
⚠ scan-0.log / `[SCAN:grp]` — explicit category, overriding Aura.cpp's `LOG_CAT "OARR"`.
"""
import ctypes
import pathlib
import re
import struct
import sys
import time
from ctypes import wintypes

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
sys.stdout.reconfigure(encoding="utf-8", errors="replace")

from mutate_guard import Mutation, assert_channel_carries, read_bytes, write_bytes  # noqa: E402
from pipe_client import PipeClient  # noqa: E402

LOGDIR = pathlib.Path.home() / "AppData/Local/UE5CEDumper/Logs/DumperTest"
SCANLOG = LOGDIR / "scan-0.log"
HOSTPID = pathlib.Path("out/host.pid")
A, B = "6201", "6202"


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + chr(10))
    sys.stdout.flush()


def alloc_in_target(pid, size):
    k = ctypes.WinDLL("kernel32", use_last_error=True)
    k.OpenProcess.restype = wintypes.HANDLE
    k.VirtualAllocEx.restype = ctypes.c_void_p
    k.VirtualAllocEx.argtypes = [wintypes.HANDLE, ctypes.c_void_p, ctypes.c_size_t,
                                 wintypes.DWORD, wintypes.DWORD]
    h = k.OpenProcess(0x0008 | 0x0020 | 0x0010 | 0x0400, False, pid)
    if not h:
        return None
    p = k.VirtualAllocEx(h, None, size, 0x3000, 0x04)
    k.CloseHandle(h)
    return int(p) if p else None


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


def scan(c):
    r = c.request("begin_group_scan", deep=True, game_only=True, max_results=50000,
                  values=[{"value": A, "data_type": "NumericNoByte"},
                          {"value": B, "data_type": "NumericNoByte"}])
    return r.get("data", r).get("session_id")


def refine(c, sid):
    c.request("refine_group_scan", session_id=sid,
              values=[{"value": A, "data_type": "NumericNoByte"},
                      {"value": B, "data_type": "NumericNoByte"}])


def main():
    fails = []
    if not assert_channel_carries(SCANLOG, "[SCAN:grp]", "the group markers"):
        return 2
    pid = int(HOSTPID.read_text().strip()) if HOSTPID.exists() else None
    if not pid:
        say("BLOCKED: out/host.pid missing")
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
        f = next((x for x in (w.get("fields") or []) if x.get("name") == "Map_IntToVec3f"), None)
        if not f:
            say("BLOCKED: Map_IntToVec3f not found")
            return 2
        header = inst + int(f["offset"])
        stride = int(f["map_stride"])
        blob = read_bytes(c, header, 0x38)
        data, maxidx, cap = struct.unpack("<Qii", blob[0:16])
        word0 = struct.unpack("<I", blob[0x10:0x14])[0]
        sec = struct.unpack("<Q", blob[0x20:0x28])[0]
        say("Map_IntToVec3f: header=0x%X Data=0x%X MaxIndex=%d Cap=%d stride=%d word0=0x%X sec=0x%X"
            % (header, data, maxidx, cap, stride, word0, sec))
        if str(f.get("map_data_addr")).lower() != hex(data):
            fails.append("walk_instance's map_data_addr disagrees with the header")
        if sec != 0:
            say("BLOCKED: secondary bit pointer non-zero — inline bits at +0x10 are stale")
            return 2
        payload = read_bytes(c, data, maxidx * stride)

        # =================== GROWTH HALF =====================================
        say("")
        say("== GROWTH: copy the elements to a fresh page, point the header there, WIPE the "
            "original bytes ==")
        sid = scan(c)
        if not sid:
            say("BLOCKED: group scan returned no session")
            return 2
        rows = grows(c, sid)
        me_row = next((x for x in rows if int(str(x["instance_addr"]), 16) == inst), None)
        say("   rows=%d" % len(rows))
        for x in rows:
            say("      inst=%s addrs=%s" % (x.get("instance_addr"),
                                            [s.get("addr") for s in (x.get("slots") or [])]))
        if not me_row:
            say("BLOCKED: no candidate on the live actor")
            c.request("end_group_scan", session_id=sid)
            return 2
        before = sorted(int(str(s["addr"]), 16) for s in me_row["slots"])
        want = sorted([data + 4, data + 8])
        say("   slot addrs %s, arithmetically expected %s  %s"
            % ([hex(a) for a in before], [hex(a) for a in want],
               "OK" if before == want else "MISMATCH"))
        if before != want:
            fails.append("slot addresses are not Data+4 / Data+8")

        scratch = alloc_in_target(pid, 4096)
        if not scratch:
            say("BLOCKED: VirtualAllocEx failed")
            c.request("end_group_scan", session_id=sid)
            return 2
        write_bytes(c, scratch, payload)
        if read_bytes(c, scratch, len(payload)) != payload:
            say("BLOCKED: scratch copy not witnessed")
            c.request("end_group_scan", session_id=sid)
            return 2
        say("   copied %d bytes to scratch 0x%X" % (len(payload), scratch))

        mark = time.strftime("%Y-%m-%d %H:%M:%S")
        time.sleep(1.1)
        with Mutation(c, "map elements (source)", data, len(payload)) as msrc:
            with Mutation(c, "map Data ptr", header, 8,
                          expect_unchanged={"MaxIndex/Cap": (header + 8, 8),
                                            "bits": (header + 0x10, 0x10)}) as mptr:
                mptr.apply(struct.pack("<Q", scratch))
                msrc.apply(b"\x00" * len(payload))      # the wipe: a stale read now sees zeros
                mptr.assert_others_unchanged()
                say("   source wiped; a candidate that was NOT repointed can no longer match")

                refine(c, sid)
                after = grows(c, sid)
                mine = next((x for x in after
                             if int(str(x["instance_addr"]), 16) == inst), None)
                say("   rows after: %d" % len(after))
                if not mine:
                    fails.append("GROWTH: the candidate was dropped — its leaves were never "
                                 "repointed into the moved buffer")
                else:
                    got = sorted(int(str(s["addr"]), 16) for s in mine["slots"])
                    exp = sorted([scratch + 4, scratch + 8])
                    say("   slot addrs now %s, expected %s  %s"
                        % ([hex(a) for a in got], [hex(a) for a in exp],
                           "OK" if got == exp else "WRONG"))
                    if got != exp:
                        fails.append("GROWTH: leaves not repointed into the scratch buffer")
                ra = since(mark, "RefineGroup re-anchor")
                say("   re-anchor lines: %d" % len(ra))
                for l in ra:
                    say("      %s" % l.strip()[-135:])
                if not ra:
                    fails.append("GROWTH: no re-anchor line")
                else:
                    n = tally(ra[-1], "RefineGroup re-anchor: ")
                    if n is not None and n < 1:
                        fails.append("GROWTH: repointed=%d, expected >= 1" % n)
        c.request("end_group_scan", session_id=sid)
        if read_bytes(c, header, 0x38) != blob or read_bytes(c, data, len(payload)) != payload:
            fails.append("the map was not restored after the growth half")
        else:
            say("   restored (header + elements)")

        # =================== REMOVAL HALF ====================================
        say("")
        say("== REMOVAL: allocation bits, with a control on each side ==")
        sid2 = scan(c)
        rows2 = grows(c, sid2)
        mine2 = next((x for x in rows2 if int(str(x["instance_addr"]), 16) == inst), None)
        if not mine2:
            say("BLOCKED: no candidate for the removal half")
            c.request("end_group_scan", session_id=sid2)
            return 1
        # the candidate points at element 0 (Data+4 / Data+8)
        my_idx, other_idx = 0, 1
        wordaddr = header + 0x10

        say("")
        say("   -- control A: clear the bit of element %d, which the candidate does NOT use --"
            % other_idx)
        markA = time.strftime("%Y-%m-%d %H:%M:%S")
        time.sleep(1.1)
        with Mutation(c, "bit word", wordaddr, 4) as mb:
            mb.apply(struct.pack("<I", word0 & ~(1 << other_idx)))
            refine(c, sid2)
            got = grows(c, sid2)
            alive = any(int(str(x["instance_addr"]), 16) == inst for x in got)
            say("      row survives: %s  <-- must be True" % alive)
            if not alive:
                fails.append("REMOVAL control A: clearing an UNRELATED element's bit killed the "
                             "row — the rule is 'any sparse change kills everything' and the "
                             "real removal result would be unattributable")

        say("")
        say("   -- the step: clear the bit of element %d, which the candidate DOES use --"
            % my_idx)
        mark2 = time.strftime("%Y-%m-%d %H:%M:%S")
        time.sleep(1.1)
        with Mutation(c, "bit word", wordaddr, 4) as mb2:
            mb2.apply(struct.pack("<I", word0 & ~(1 << my_idx)))
            still = read_bytes(c, data + 4, 4)
            say("      the leaf address still reads %.0f (the bit is the only witness)"
                % struct.unpack("<f", still)[0])
            refine(c, sid2)
            got2 = grows(c, sid2)
            alive2 = any(int(str(x["instance_addr"]), 16) == inst for x in got2)
            say("      row survives: %s  <-- must be False" % alive2)
            if alive2:
                fails.append("REMOVAL: the candidate survived a freed slot")
            cl = since(mark2, "RefineGroup cand[")
            for l in cl:
                say("      %s" % l.strip()[-135:])
            cm = tally(cl[-1], "container-moved=") if cl else None
            pn = tally(cl[-1], "predicate-said-no=") if cl else None
            say("      container-moved=%s predicate-said-no=%s" % (cm, pn))
            if not cm:
                fails.append("REMOVAL: container-moved=%s — the drop was not attributed to the "
                             "container" % cm)
            if pn:
                fails.append("REMOVAL: predicate-said-no=%s — wrong mechanism" % pn)

        say("")
        say("   -- control B: the bit is back; the row must COME BACK --")
        sid3 = scan(c)
        back = any(int(str(x["instance_addr"]), 16) == inst for x in grows(c, sid3))
        say("      row present again: %s  <-- must be True" % back)
        if not back:
            fails.append("REMOVAL control B: the row did not come back after restoring the bit, "
                         "so the drop may have been 'the container is permanently unreadable' "
                         "rather than 'my slot was freed'")
        c.request("end_group_scan", session_id=sid3)
        c.request("end_group_scan", session_id=sid2)

        if read_bytes(c, header, 0x38) != blob:
            fails.append("the map header was NOT fully restored")
        else:
            say("")
            say("map header verified restored")

    say("")
    if fails:
        say("A12 step 4: FAIL")
        for f in fails:
            say("   - %s" % f)
        return 1
    say("A12 step 4: PASS — growth repoints the leaves (proved by wiping the source), a freed "
        "slot drops the row, an unrelated freed slot does not, and the row returns when restored")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
