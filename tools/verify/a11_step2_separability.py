r"""A11 step 2, control (ii) — the re-anchor and the predicate must be SEPARABLE.

    py tools/verify/a11_step2_separability.py     (run after a11_step2_realloc.py)

Step 2's main run shows a candidate SURVIVING a buffer move. On its own that is compatible with a
weaker, wrong implementation: "if the container moved, keep the candidate". This asks the opposite
question — move the buffer AND make the value stop matching, in the same refine.

  * If the row survives, the refine is not applying the predicate to re-anchored candidates, which
    is worse than dropping them: the user is shown a stale match.
  * If the row drops AND a `Refine re-anchor: 1 ... repointed` line is still emitted, the two are
    independent: the element was repointed, then judged on its merits and rejected.

⚠ Both changes must land in ONE refine. Doing them in two passes proves nothing, because the first
refine already re-stamps the anchor and the second sees no container change at all — there would be
no re-anchor line to observe, and the drop would be unattributable.

Same restore contract as the main rig: `mutate_guard.Mutation`, verified by read-back.
"""
import ctypes
import pathlib
import struct
import sys
import time
from ctypes import wintypes

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
sys.stdout.reconfigure(encoding="utf-8", errors="replace")

from mutate_guard import Mutation, assert_channel_carries, read_bytes, write_bytes  # noqa: E402
from pipe_client import PipeClient  # noqa: E402

LOGDIR = pathlib.Path.home() / "AppData/Local/UE5CEDumper/Logs/DumperTest"
OFFSETS = LOGDIR / "offsets-0.log"
HOSTPID = pathlib.Path("out/host.pid")


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
        return [l for l in OFFSETS.read_text(encoding="utf-8", errors="replace").splitlines()
                if l.startswith("[") and l[1:20] >= mark and needle in l]
    except OSError:
        return []


def cands(c, sid, limit=400):
    r = c.request("query_candidates", session_id=sid, offset=0, limit=limit)
    d = r.get("data", r)
    return d.get("candidates") or d.get("results") or []


def main():
    fails = []
    if not assert_channel_carries(OFFSETS, "[OARR]", "the re-anchor marker"):
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
        arr = next((f for f in (w.get("fields") or []) if f.get("name") == "Arr_Int"), None)
        off, data = int(arr["offset"]), int(str(arr["array_data_addr"]), 16)
        esz = int(arr["array_elem_size"])
        header = inst + off
        hdr = read_bytes(c, header, 16)
        say("header 0x%X = %s" % (header, hdr.hex().upper()))

        r = c.request("begin_value_scan", data_type="Int32", scan_type="Exact",
                      value="30", deep=False, game_only=True, max_results=5000)
        sid = r.get("data", r).get("session_id")
        before = [x for x in cands(c, sid) if "Arr_Int[" in (x.get("field_name") or "")]
        live_before = next((x for x in before
                            if int(str(x.get("instance_addr")), 16) == inst), None)
        cdo_before = [x for x in before if int(str(x.get("instance_addr")), 16) != inst]
        if not live_before:
            say("BLOCKED: no live candidate to begin with")
            c.request("end_value_scan", session_id=sid)
            return 2
        say("live candidate before: %s @ %s" % (live_before.get("field_name"),
                                                live_before.get("addr")))

        buf2 = alloc_in_target(pid, 4096)
        if not buf2:
            say("BLOCKED: VirtualAllocEx failed")
            c.request("end_value_scan", session_id=sid)
            return 2
        # element 2 is deliberately 31, NOT 30 — the value stops matching in the same move.
        write_bytes(c, buf2, struct.pack("<7i", 10, 20, 31, 40, 50, 60, 70))
        say("new buffer 0x%X seeded with element[2] = 31 (was 30)" % buf2)

        mark = time.strftime("%Y-%m-%d %H:%M:%S")
        time.sleep(1.1)

        with Mutation(c, "Arr_Int header", header, 16) as m:
            if not m.apply(struct.pack("<Qii", buf2, 7, 16)):
                fails.append("could not install the forged header")
            else:
                c.request("refine_value_scan", session_id=sid, scan_type="Exact", value="30")
                after = [x for x in cands(c, sid) if "Arr_Int[" in (x.get("field_name") or "")]
                live_after = next((x for x in after
                                   if int(str(x.get("instance_addr")), 16) == inst), None)
                say("")
                say("after one refine that moved the buffer AND changed the value:")
                for x in after:
                    say("   %-16s addr=%s inst=%s" % (x.get("field_name"), x.get("addr"),
                                                      x.get("instance_addr")))

                if live_after:
                    fails.append("the live row SURVIVED although its value is now 31 — the refine "
                                 "is not applying the predicate to re-anchored candidates, which "
                                 "shows the user a stale match")
                else:
                    say("   OK: the live row is gone (predicate rejected it)")

                if len(after) != len(cdo_before):
                    fails.append("the CDO rows did not all survive (%d before, %d after) — a "
                                 "blanket drop would produce the same 'gone' as above"
                                 % (len(cdo_before), len(after)))
                else:
                    say("   OK: the untouched CDO row(s) survived — so the drop was targeted")

                lines = since(mark, "Refine re-anchor:")
                say("   re-anchor lines: %d" % len(lines))
                for l in lines:
                    say("      %s" % l.strip()[-120:])
                if not lines:
                    fails.append("NO re-anchor line — the element was never repointed, so the "
                                 "drop could simply be the stale address failing to read, which "
                                 "is the pre-fix behaviour wearing the right answer's clothes")
                elif "repointed" not in lines[-1]:
                    fails.append("the re-anchor line does not report a repoint: %s"
                                 % lines[-1].strip()[-120:])
            m.assert_others_unchanged()

        c.request("end_value_scan", session_id=sid)
        if read_bytes(c, header, 16) != hdr:
            fails.append("HEADER NOT RESTORED — kill the host")

    say("")
    if fails:
        say("A11 step 2 control (ii): FAIL")
        for f in fails:
            say("   - %s" % f)
        return 1
    say("A11 step 2 control (ii): PASS — the element was repointed AND then rejected on its "
        "merits, so re-anchor and predicate are independent")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
