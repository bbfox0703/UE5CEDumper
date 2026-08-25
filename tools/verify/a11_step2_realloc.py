r"""A11 step 2 — a container candidate must be RE-ANCHORED when the array's buffer moves.

    py tools/verify/a11_step2_realloc.py      (DumperTest dev running + injected, no UI)

THE CLAIM (build 3253). Before the fix, a growth realloc left every container element address stale
and the candidates were lost outright. After it, the refine notices `{dataPtr,count}` changed and
repoints them.

⚠ WHAT THIS DOES AND DOES NOT PROVE. Nothing on DumperTest grows a UPROPERTY container on its own,
so the buffer move is FORGED: a fresh page is allocated in the target, the elements are copied into
it, and the inline `TArray` header is rewritten to point there. That reproduces **what the scanner
observes** about a realloc — a new Data with a Count that grew — and it does NOT reproduce a
realloc: no allocator ran, the old buffer is still mapped, nothing was freed. The re-anchor rule
reads the header and compares, so this exercises it; anything depending on the old memory becoming
invalid is NOT covered and must not be claimed.

⚠ THE RESTORE IS LOAD-BEARING. A synthetic header left installed makes `TArray`'s destructor call
`FMemory::Free` on a pointer it does not own, and the crash arrives minutes later looking like the
game's fault. `mutate_guard.Mutation` restores in `finally` and verifies by read-back; if that ever
fails the host must be KILLED rather than allowed to exit cleanly.

⚠ `offsets-0.log`, not `scan-0.log` — `Refine re-anchor:` is `LOG_CAT "OARR"`, which Sein routes to
LF_Offsets. Reading the wrong file made this row's sibling step pass for a year. [A11-LOGPATH]
"""
import ctypes
import pathlib
import struct
import sys
import time
from ctypes import wintypes

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
sys.stdout.reconfigure(encoding="utf-8", errors="replace")

from mutate_guard import Mutation, assert_channel_carries, read_bytes  # noqa: E402
from pipe_client import PipeClient  # noqa: E402

LOGDIR = pathlib.Path.home() / "AppData/Local/UE5CEDumper/Logs/DumperTest"
OFFSETS = LOGDIR / "offsets-0.log"
HOSTPID = pathlib.Path("out/host.pid")

MEM_COMMIT_RESERVE = 0x3000
PAGE_RW = 0x04
PROCESS_VM_OP = 0x0008 | 0x0020 | 0x0010 | 0x0400   # OPERATION|WRITE|READ|QUERY_INFO


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
    h = k.OpenProcess(PROCESS_VM_OP, False, pid)
    if not h:
        return None, "OpenProcess failed err=%d" % ctypes.get_last_error()
    p = k.VirtualAllocEx(h, None, size, MEM_COMMIT_RESERVE, PAGE_RW)
    k.CloseHandle(h)
    if not p:
        return None, "VirtualAllocEx failed err=%d" % ctypes.get_last_error()
    return int(p), None


def since(mark, needle):
    try:
        return [l for l in OFFSETS.read_text(encoding="utf-8", errors="replace").splitlines()
                if l.startswith("[") and l[1:20] >= mark and needle in l]
    except OSError:
        return []


def cands(c, sid, limit=400):
    r = c.request("query_candidates", session_id=sid, offset=0, limit=limit)
    d = r.get("data", r)
    return d.get("candidates") or d.get("results") or []


def arrint_rows(rows):
    return [x for x in rows if "Arr_Int[" in (x.get("field_name") or "")]


def main():
    fails, notes = [], []
    if not assert_channel_carries(OFFSETS, "[OARR]", "the re-anchor marker"):
        return 2
    pid = int(HOSTPID.read_text().strip()) if HOSTPID.exists() else None
    if not pid:
        say("BLOCKED: out/host.pid missing — launch via tools/verify/launch_dumpertest.py")
        return 2

    with PipeClient() as c:
        c.assert_build()
        c.ensure_scanned()

        # ---- fixture, read live -------------------------------------------------
        insts = c.request("find_instances", class_name="DumperTestActor",
                          max_results=10).get("instances") or []
        live = next((i for i in insts if i.get("name") == "DumperTestActor"), None)
        if not live:
            say("BLOCKED: no live DumperTestActor")
            return 2
        inst = int(str(live["addr"]), 16)
        w = c.request("walk_instance", addr=live["addr"], array_limit=32)
        arr = next((f for f in (w.get("fields") or []) if f.get("name") == "Arr_Int"), None)
        if not arr:
            say("BLOCKED: Arr_Int not found")
            return 2
        off = int(arr["offset"])
        data = int(str(arr.get("array_data_addr")), 16)
        count = int(arr.get("count"))
        esz = int(arr.get("array_elem_size"))
        header = inst + off
        say("Arr_Int: offset=0x%X data=0x%X count=%d elem=%dB header=0x%X"
            % (off, data, count, esz, header))

        hdr = read_bytes(c, header, 16)
        if hdr is None or len(hdr) != 16:
            say("BLOCKED: cannot read the 16-byte header")
            return 2
        h_data, h_num, h_max = struct.unpack("<Qii", hdr)
        say("header bytes: Data=0x%X Num=%d Max=%d" % (h_data, h_num, h_max))
        if h_data != data or h_num != count:
            say("BLOCKED: header disagrees with walk_instance (Data 0x%X vs 0x%X, Num %d vs %d) — "
                "refusing to write against an unverified layout" % (h_data, data, h_num, count))
            return 2

        # ---- first scan ----------------------------------------------------------
        # deep=False on purpose: a deep descriptor carries ValueAnchor::Unknown, and the
        # re-anchor branch would never be reached — the test would be vacuous.
        say("")
        say("== first scan (deep=False) ==")
        r = c.request("begin_value_scan", data_type="Int32", scan_type="Exact",
                      value="30", deep=False, game_only=True, max_results=5000)
        d = r.get("data", r)
        sid = d.get("session_id")
        rows = cands(c, sid)
        hits = arrint_rows(rows)
        say("   session=%s total=%s  Arr_Int rows=%d" % (sid, d.get("count") or d.get("total"),
                                                         len(hits)))
        for x in hits:
            say("      %-16s addr=%s inst=%s" % (x.get("field_name"), x.get("addr"),
                                                 x.get("instance_addr")))
        if not hits:
            say("BLOCKED: deep=False returns no container rows at all, so the re-anchor branch is "
                "unreachable from here. Stopping BEFORE touching memory.")
            c.request("end_value_scan", session_id=sid)
            return 2

        live_row = next((x for x in hits if int(str(x.get("instance_addr")), 16) == inst), None)
        cdo_rows = [x for x in hits if int(str(x.get("instance_addr")), 16) != inst]
        if not live_row:
            say("BLOCKED: no candidate on the LIVE actor")
            c.request("end_value_scan", session_id=sid)
            return 2
        live_addr = int(str(live_row["addr"]), 16)
        want = data + 2 * esz
        say("   live candidate addr=0x%X, expected data+2*elem=0x%X  %s"
            % (live_addr, want, "OK" if live_addr == want else "MISMATCH"))
        if live_addr != want:
            fails.append("the live candidate is not at data+2*elem — the fixture is not what the "
                         "rest of this rig assumes")
        cdo_before = {int(str(x["instance_addr"]), 16): int(str(x["addr"]), 16) for x in cdo_rows}
        say("   CDO rows: %d" % len(cdo_rows))

        # ---- forge the realloc ---------------------------------------------------
        say("")
        say("== forged buffer move (7 elements at a fresh page) ==")
        buf2, err = alloc_in_target(pid, 4096)
        if not buf2:
            say("BLOCKED: %s" % err)
            c.request("end_value_scan", session_id=sid)
            return 2
        say("   allocated 0x%X in pid %d" % (buf2, pid))
        payload = struct.pack("<7i", 10, 20, 30, 40, 50, 60, 70)
        from mutate_guard import write_bytes
        if not write_bytes(c, buf2, payload):
            say("BLOCKED: could not seed the new buffer")
            c.request("end_value_scan", session_id=sid)
            return 2
        back = read_bytes(c, buf2, 28)
        if back != payload:
            say("BLOCKED: new buffer did not take the payload")
            c.request("end_value_scan", session_id=sid)
            return 2
        say("   new buffer seeded and witnessed")

        mark = time.strftime("%Y-%m-%d %H:%M:%S")
        time.sleep(1.1)

        ok_all = False
        with Mutation(c, "Arr_Int header", header, 16) as m:
            if not m.apply(struct.pack("<Qii", buf2, 7, 16)):
                fails.append("could not install the forged header")
            else:
                say("")
                say("== refine ==")
                rr = c.request("refine_value_scan", session_id=sid,
                               scan_type="Exact", value="30")
                dd = rr.get("data", rr)
                say("   refine ok=%s remaining=%s" % (rr.get("ok"), dd.get("count")
                                                      or dd.get("remaining")))
                rows2 = cands(c, sid)
                hits2 = arrint_rows(rows2)
                for x in hits2:
                    say("      %-16s addr=%s inst=%s" % (x.get("field_name"), x.get("addr"),
                                                         x.get("instance_addr")))

                live2 = next((x for x in hits2
                              if int(str(x.get("instance_addr")), 16) == inst), None)
                # (a) the live row survives
                if not live2:
                    fails.append("(a) the live container candidate was DROPPED after the buffer "
                                 "moved — this is the pre-3253 behaviour")
                else:
                    # (b) it points into the NEW buffer at the same element index
                    got = int(str(live2["addr"]), 16)
                    exp = buf2 + 2 * esz
                    say("   (b) live addr 0x%X, expected new buf+2*elem 0x%X  %s"
                        % (got, exp, "OK" if got == exp else "WRONG"))
                    if got != exp:
                        fails.append("(b) the survivor was not repointed into the new buffer "
                                     "(0x%X != 0x%X)" % (got, exp))
                # (c) the CDO row — untouched header — must survive AT ITS ORIGINAL ADDRESS.
                #     This is the internal negative control: a blanket repoint would move it too.
                for ia, addr_before in cdo_before.items():
                    now = next((x for x in hits2
                                if int(str(x.get("instance_addr")), 16) == ia), None)
                    if not now:
                        fails.append("(c) the CDO candidate at inst 0x%X vanished — a blanket "
                                     "drop, not a targeted re-anchor" % ia)
                    elif int(str(now["addr"]), 16) != addr_before:
                        fails.append("(c) the CDO candidate MOVED (0x%X -> %s) although its "
                                     "header never changed — the branch fired blanket"
                                     % (addr_before, now["addr"]))
                say("   (c) CDO rows unchanged: %s"
                    % ("yes" if not [f for f in fails if f.startswith("(c)")] else "NO"))

                # (d) exactly one re-anchor line, and it must name ONE element
                lines = since(mark, "Refine re-anchor:")
                say("   (d) 'Refine re-anchor:' lines since the mark: %d" % len(lines))
                for l in lines:
                    say("        %s" % l.strip()[-120:])
                if len(lines) != 1:
                    fails.append("(d) expected exactly one re-anchor line, saw %d" % len(lines))
                elif "1 container element(s) repointed" not in lines[0]:
                    fails.append("(d) the re-anchor line does not report exactly 1 repointed "
                                 "element — a larger number means the branch fired blanket: %s"
                                 % lines[0].strip()[-140:])
                ok_all = not fails
            m.assert_others_unchanged()

        # header restored by the context manager (verified by read-back)
        c.request("end_value_scan", session_id=sid)

        # final independent restore check
        hdr_now = read_bytes(c, header, 16)
        if hdr_now != hdr:
            fails.append("HEADER NOT RESTORED — kill the host, do not let it exit cleanly")
        else:
            say("")
            say("header verified restored: %s" % hdr.hex().upper())

    say("")
    if fails:
        say("A11 step 2: FAIL")
        for f in fails:
            say("   - %s" % f)
        return 1
    say("A11 step 2: PASS (forged buffer move; see the header comment for what that does and "
        "does not cover)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
