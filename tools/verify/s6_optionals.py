#!/usr/bin/env python3
r"""S6 of the FIX PASS LOW live plan: the three DumperTest58 (UE 5.8) TOptional rows, pipe only.

    py tools/verify/s6_optionals.py l82            [A2-TOPTIONAL-VALUESCAN]       value scan vs an intrusive UNSET FString
    py tools/verify/s6_optionals.py l84            [A2-TOPTIONAL-STRUCT-DESCENT]  Find Refs / Address Finder vs a RESET struct
    py tools/verify/s6_optionals.py l86 [--spawn N --edges K]   [A2-SENTINEL-OVERREAD]  FName scan vs page-edge optionals

UI CLOSED (the UI holds two of the three pipe slots). Run the SAME phase on the red (staged) DLL and on HEAD's DLL;
the phase prints what it measured and a one-word reading, and exits 0 either way unless the FIXTURE is wrong
(a missing actor, an optional that is not the shape the row needs) -- the red/green verdict is the caller's,
from the two outputs side by side.

l82: the live DumperTest58Actor's Opt_Str_Unset is an INTRUSIVE TOptional<FString> (size 16, ArrayMax sentinel
0xFFFFFFFF at +12) that was never assigned, and so is the CDO's Opt_Str_Set. An FString Exact "" scan over all
objects (game_only=false) must then match OTHER empty strings (so an absence is not vacuous) but neither of those
two. The SET control scans "Opt58StringPresent" and must hit the live actor's Opt_Str_Set only.

l84: Opt_Struct_Set is a TRAILING-FLAG TOptional<FDumperTest58OptInner> {Obj, Objs, Tag} seeded with Obj = Objs[0] =
the actor itself; its bIsSet byte sits after the 32-byte inner. The phase records Find Refs to the actor and
find_by_address on Objs' data buffer, then RESETS the optional by writing its flag byte 01 -> 00 (mutate_guard:
captured, witnessed, restored in `finally`, re-read), repeats both lookups, and restores. A reset optional must
lose both Opt_Struct_Set hits while every other hit stays. No destructor runs on a flag-only write, so writing 01
back returns the exact original state.

l86: UDumperTest58OptTail is a 64-byte UObject whose LAST 8 bytes are an intrusive TOptional<FName> set to
"Opt58TailName". `--spawn` calls ADumperTest58Actor::OptTail_Spawn(N, K), which keeps creating them until K end at
an unreadable page (measured in-game with VirtualQuery). The phase then RE-MEASURES every instance out of process
(the page after one can be committed later), scans FName Exact "Opt58TailName" over the class, and reports the
hits among the edge instances and among the rest. The pre-fix gate read a flat 16 bytes, so an edge instance's
read faulted and its SET optional was dropped; the fixed gate reads 4.
"""
import argparse
import ctypes
import os
import sys
import time
from ctypes import wintypes

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

ACTOR = "DumperTest58Actor"


def say(s=""):
    print(s, flush=True)


def actor_and_cdo(c):
    r = c.request("find_instances", class_name=ACTOR, exact_match=True, limit=100)
    c.check_complete(r)
    ins = r.get("instances") or []
    live = [i for i in ins if not i["name"].startswith("Default__")]
    cdo = [i for i in ins if i["name"].startswith("Default__")]
    if not live or not cdo:
        raise SystemExit("FIXTURE: want one live %s and its CDO, got %s" % (ACTOR, [i["name"] for i in ins]))
    return live[0], cdo[0]


def field(walk, name):
    return next((f for f in (walk.get("fields") or []) if f.get("name") == name), None)


def scan_all(c, data_type, value, game_only, want=None):
    """begin_value_scan + every candidate row (paged). Returns (begin reply, rows)."""
    r = c.request("begin_value_scan", data_type=data_type, scan_type="Exact", value=value,
                  game_only=game_only, max_results=1000000, deep=False)
    d = r.get("data", r)
    sid = d.get("session_id")
    rows, off = [], 0
    while sid:
        q = c.request("query_candidates", session_id=sid, offset=off, limit=5000,
                      **({"filter": want} if want else {}))
        qd = q.get("data", q)
        page = qd.get("candidates") or qd.get("results") or []
        rows += page
        off += len(page)
        if len(page) < 5000:
            break
    if sid:
        c.request("end_value_scan", session_id=sid)
    return d, rows


def l82(a):
    from pipe_client import PipeClient
    with PipeClient() as c:
        say("build %s" % c.assert_build())
        c.ensure_scanned()
        say("objects %s" % c.request("get_object_count").get("count"))
        live, cdo = actor_and_cdo(c)
        say("live %s @ %s   CDO @ %s" % (live["name"], live["addr"], cdo["addr"]))
        for who, addr in (("live", live["addr"]), ("CDO", cdo["addr"])):
            w = c.request("walk_instance", addr=addr, array_limit=0)
            for n in ("Opt_Str_Set", "Opt_Str_Unset"):
                f = field(w, n) or {}
                say("   %-4s %-14s type=%s size=%s value=%r hex=%s" % (who, n, f.get("type"), f.get("size"),
                                                                    f.get("value"), f.get("hex")))
        d, rows = scan_all(c, "FString", "", False)
        say("FString Exact \"\" (game_only=false): total=%s deadline_hit=%s complete=%s rows=%d"
            % (d.get("total") or d.get("count"), d.get("deadline_hit"), d.get("complete"), len(rows)))
        opt = [x for x in rows if "Opt_Str" in (x.get("field_name") or "")]
        for x in opt:
            say("   HIT %-18s on %s (%s) @ %s" % (x.get("field_name"), x.get("instance_name") or x.get("name"),
                                                 x.get("class_name"), x.get("instance_addr") or x.get("addr")))
        bad = [x for x in opt if (x.get("instance_addr") or "").lower() == live["addr"].lower()
               and x.get("field_name") == "Opt_Str_Unset"] + \
              [x for x in opt if (x.get("instance_addr") or "").lower() == cdo["addr"].lower()
               and x.get("field_name") == "Opt_Str_Set"]
        say("empty-string rows that are NOT Opt_Str_*: %d (the absence below is non-vacuous only if > 0)"
            % (len(rows) - len(opt)))
        say("READING l82 discriminator: %s" % ("UNSET OPTIONALS MATCHED \"\" (%d)" % len(bad) if bad
                                                 else "no unset optional matched \"\""))
        # The "" scan above is refused by design (an empty needle returns before scanning, and the UI refuses it at
        # First Scan), so the FString half cannot be driven. The FName half can: an UNSET intrusive TOptional<FName>
        # holds ComparisonIndex ~0u, which an UNGATED read resolves to "None" -- an ordinary, non-empty needle.
        d3, rows3 = scan_all(c, "FName", "None", False)
        opt3 = [x for x in rows3 if (x.get("field_name") or "").startswith("Opt_")]
        say("FName Exact \"None\" (game_only=false): %d row(s), %d on an Opt_* optional" % (len(rows3), len(opt3)))
        for x in opt3:
            say("   HIT %-16s on %s @ %s" % (x.get("field_name"), x.get("class_name"),
                                          x.get("instance_addr") or x.get("addr")))
        say("READING l82 FName discriminator: %s" % ("UNSET OPTIONALS MATCHED \"None\" (%d)" % len(opt3) if opt3
                                                     else "no unset optional matched \"None\""))
        d2, rows2 = scan_all(c, "FString", "Opt58StringPresent", True)
        say("SET control FString Exact \"Opt58StringPresent\" (game_only=true): %d row(s)" % len(rows2))
        for x in rows2:
            say("   %s.%s  type=%s  @ %s" % (x.get("class_name"), x.get("field_name"), x.get("field_type"),
                                           x.get("instance_addr") or x.get("addr")))
    return 0


def refs_and_container(c, target, data_addr):
    r = c.request("find_refs_to_uobject", addr=target, max_results=64)
    refs = r.get("references") or []
    scan = r.get("scan") or {}
    fb = c.request("find_by_address", addr=data_addr, scan_containers=True, container_depth=5,
                   container_elem_cap=256)
    cm = fb.get("container_matches") or fb.get("containers") or []
    return refs, scan, cm, fb.get("container_scan") or {}


def show_refs(tag, refs, scan, cm, cs):
    say("   [%s] Find Refs: %d hit(s), scanned %s/%s deadline_hit=%s" % (tag, len(refs), scan.get("objects_scanned"),
                                                                    scan.get("objects_total"), scan.get("deadline_hit")))
    for x in refs:
        say("      %-26s owner=%s (%s) elem=%s" % (x.get("field_name"), x.get("owner_name"), x.get("owner_addr"),
                                                  x.get("element_index")))
    say("   [%s] Address Finder containers: %d match(es), deep_scan=%s deadline_hit=%s" %
        (tag, len(cm), cs.get("deep_scan"), cs.get("deadline_hit")))
    for x in cm:
        say("      %-26s owner=%s (%s) elem=%s" % (x.get("field_name"), x.get("owner_name"), x.get("owner_addr"),
                                                  x.get("element_index")))


def l84(a):
    import mutate_guard as MG
    from pipe_client import PipeClient
    with PipeClient() as c:
        say("build %s" % c.assert_build())
        c.ensure_scanned()
        live, _ = actor_and_cdo(c)
        A = int(live["addr"], 16)
        w = c.request("walk_instance", addr=live["addr"], array_limit=4)
        fs, fu = field(w, "Opt_Struct_Set"), field(w, "Opt_Struct_Unset")
        if not fs or not fu:
            raise SystemExit("FIXTURE: Opt_Struct_Set / Opt_Struct_Unset not walked")
        off = lambda f: int(f["offset"], 16) if isinstance(f["offset"], str) else int(f["offset"])
        O, S, OU = off(fs), int(fs.get("size") or 0), off(fu)
        say("live %s @ 0x%X: Opt_Struct_Set +0x%X size %d, Opt_Struct_Unset +0x%X" % (live["name"], A, O, S, OU))
        if S != 40:
            raise SystemExit("FIXTURE: Opt_Struct_Set size %d, want 40 (32-byte inner + flag, aligned 8)" % S)
        flag, flag_u = A + O + 32, A + OU + 32
        fb0, fu0 = MG.read_bytes(c, flag, 1), MG.read_bytes(c, flag_u, 1)
        hdr = MG.read_bytes(c, A + O + 8, 16)
        D = int.from_bytes(hdr[0:8], "little")
        num = int.from_bytes(hdr[8:12], "little")
        d0 = int.from_bytes(MG.read_bytes(c, D, 8), "little") if D else 0
        say("flag bytes: Set=%s Unset=%s   Objs data=0x%X num=%d, Objs[0]=0x%X (== actor: %s)"
            % (fb0.hex(), fu0.hex(), D, num, d0, d0 == A))
        if fb0 != b"\x01" or fu0 != b"\x00" or num != 1 or d0 != A:
            raise SystemExit("FIXTURE: not the seeded shape the row needs")
        base_val = MG.read_bytes(c, A + O, 32)
        show_refs("SET", *refs_and_container(c, live["addr"], "0x%X" % D))
        with MG.Mutation(c, "L84 Opt_Struct_Set bIsSet", flag, 1,
                         expect_unchanged={"inner 32 bytes": (A + O, 32)}) as m:
            if not m.apply(b"\x00"):
                raise SystemExit("FAIL: the flag write was not witnessed -- nothing below would mean anything")
            m.assert_others_unchanged()
            say("RESET: flag -> %s; inner bytes identical: %s" % (MG.read_bytes(c, flag, 1).hex(),
                                                                MG.read_bytes(c, A + O, 32) == base_val))
            show_refs("RESET", *refs_and_container(c, live["addr"], "0x%X" % D))
        say("RESTORE: flag -> %s" % MG.read_bytes(c, flag, 1).hex())
        show_refs("RESTORED", *refs_and_container(c, live["addr"], "0x%X" % D))
    return 0


PAGE = 0x1000
MEM_COMMIT, PAGE_NOACCESS, PAGE_GUARD = 0x1000, 0x01, 0x100


class MBI(ctypes.Structure):
    _fields_ = [("BaseAddress", ctypes.c_void_p), ("AllocationBase", ctypes.c_void_p),
                ("AllocationProtect", wintypes.DWORD), ("PartitionId", wintypes.WORD),
                ("RegionSize", ctypes.c_size_t), ("State", wintypes.DWORD), ("Protect", wintypes.DWORD),
                ("Type", wintypes.DWORD)]


def edge_of(h, k, field_addr):
    """True when a 16-byte read from field_addr would cross into a page that is not readable."""
    nxt = (field_addr // PAGE + 1) * PAGE
    if field_addr + 16 <= nxt:
        return False
    m = MBI()
    if not k.VirtualQueryEx(h, ctypes.c_void_p(nxt), ctypes.byref(m), ctypes.sizeof(m)):
        return True
    return m.State != MEM_COMMIT or bool(m.Protect & (PAGE_NOACCESS | PAGE_GUARD)) or m.Protect == 0


def l86(a):
    import mailbox_poke as MP
    from pipe_client import PipeClient
    from ad4_contested import invoke
    k = ctypes.WinDLL("kernel32", use_last_error=True)
    k.OpenProcess.restype = wintypes.HANDLE
    k.VirtualQueryEx.argtypes = [wintypes.HANDLE, ctypes.c_void_p, ctypes.POINTER(MBI), ctypes.c_size_t]
    k.VirtualQueryEx.restype = ctypes.c_size_t
    pid = MP.pid_of(a.process)
    with PipeClient() as c:
        say("build %s" % c.assert_build())
        c.ensure_scanned()
        live, _ = actor_and_cdo(c)
        if a.spawn:
            fns = {f["name"]: f for f in c.request("walk_functions", addr=live["class_addr"])["functions"]}
            ps = fns["OptTail_Spawn"]["parms_size"]
            hexs = a.spawn.to_bytes(4, "little").hex() + a.edges.to_bytes(4, "little").hex() + "00" * (ps - 8)
            t = time.time()
            invoke(c, live["addr"], "OptTail_Spawn", parms_size=ps, params_hex=hexs)
            say("OptTail_Spawn(%d, %d) in %.1fs" % (a.spawn, a.edges, time.time() - t))
            c.request("trigger_scan")
            time.sleep(3)
            c.ensure_scanned()
        w = c.request("walk_instance", addr=live["addr"], array_limit=0)
        for n in ("OptTail_Total", "OptTail_Edges", "OptTail_ObjectSize", "OptTail_FieldOffset"):
            say("   %s = %s" % (n, (field(w, n) or {}).get("value")))
        size = int((field(w, "OptTail_ObjectSize") or {}).get("value") or 0)
        foff = int((field(w, "OptTail_FieldOffset") or {}).get("value") or 0)
        if not size or foff + 8 != size:
            raise SystemExit("FIXTURE: OptTail layout size %d offset %d -- the optional is not the last 8 bytes"
                             % (size, foff))
        r = c.request("find_instances", class_name="DumperTest58OptTail", exact_match=True, limit=500000)
        ins = [i for i in (r.get("instances") or []) if not i["name"].startswith("Default__")]
        say("live UDumperTest58OptTail instances: %d" % len(ins))
        h = k.OpenProcess(0x0400, False, pid)
        try:
            edge = {i["addr"].lower() for i in ins if edge_of(h, k, int(i["addr"], 16) + foff)}
        finally:
            k.CloseHandle(h)
        say("re-measured now: %d instance(s) whose Opt_Tail +16 crosses into an unreadable page" % len(edge))
        d, rows = scan_all(c, "FName", "Opt58TailName", False)
        hits = {(x.get("instance_addr") or x.get("addr") or "").lower() for x in rows
                if x.get("field_name") == "Opt_Tail"}
        others = {i["addr"].lower() for i in ins} - edge
        say("FName Exact \"Opt58TailName\": %d row(s), complete=%s deadline_hit=%s" %
            (len(rows), d.get("complete"), d.get("deadline_hit")))
        say("   edge instances hit: %d of %d" % (len(edge & hits), len(edge)))
        say("   other instances hit: %d of %d" % (len(others & hits), len(others)))
        say("READING l86: %s" % ("EDGE INSTANCES DROPPED (%d)" % len(edge - hits) if edge - hits
                                 else ("every edge instance is a hit" if edge else "NO EDGE INSTANCES -- not discriminating")))
    return 0


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("phase", choices=("l82", "l84", "l86"))
    ap.add_argument("--process", default="DumperTest58-Win64-Shipping")
    ap.add_argument("--spawn", type=int, default=0, help="l86: OptTail_Spawn MaxObjects (0 = spawn nothing)")
    ap.add_argument("--edges", type=int, default=8, help="l86: OptTail_Spawn WantEdges")
    a = ap.parse_args()
    return {"l82": l82, "l84": l84, "l86": l86}[a.phase](a)


if __name__ == "__main__":
    sys.exit(main())
