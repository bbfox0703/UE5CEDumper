r"""A11 step 4 — for a TSet/TMap, the ALLOCATION BIT is the only witness that an element is gone.

    py tools/verify/a11_step4_sparsebit.py     (DumperTest dev running + injected, no UI)

WHY THE BIT IS THE ONLY WITNESS. A freed sparse slot is refilled at the identical address and the
old bytes are usually still there, so re-reading the candidate's address returns the same value it
always did. `{dataPtr,count}` does not move either. If the refine trusts either of those, a removed
entry reads as a live match forever — the silent-wrong-value case this row exists for.

LAYOUT, verified live rather than assumed (DumperTest `Set_Int`, read 2026-08-21):

    +0x00  Data ptr        0x…FB516370
    +0x08  Num (MaxIndex)  3
    +0x0C  Max (Capacity)  4
    +0x10  inline bit words[4]   word0 = 0x7   <- exactly the three live elements, 0b111
    +0x20  secondary ptr   0     <- MUST be 0, or the bits live on the heap and +0x10 is stale
    +0x28  NumBits 3 / MaxBits 128
    +0x30  FirstFreeIndex -1 / NumFreeIndices 0

The prediction "three elements at indices 0,1,2 ⇒ word0 == 0b111" was made before reading and
matched. That is what licenses poking a single bit here.

⚠ ONLY the live actor's bit is cleared. The CDO holds the same value in the same field reached by
the same code path, one bit different — it is the paired control, and if it disappears too the
result is a mass drop rather than a targeted one.
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
VALUE = "4242"
FIELD = "Set_Int"


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


def cands(c, sid, limit=400):
    r = c.request("query_candidates", session_id=sid, offset=0, limit=limit)
    d = r.get("data", r)
    return d.get("candidates") or d.get("results") or []


def rows_for(c, sid):
    return [x for x in cands(c, sid) if FIELD + "[" in (x.get("field_name") or "")]


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
        f = next((x for x in (w.get("fields") or []) if x.get("name") == FIELD), None)
        if not f:
            say("BLOCKED: %s not found" % FIELD)
            return 2
        header = inst + int(f["offset"])
        blob = read_bytes(c, header, 0x38)
        data, num, mx = struct.unpack("<Qii", blob[0:16])
        words = list(struct.unpack("<4I", blob[0x10:0x20]))
        sec = struct.unpack("<Q", blob[0x20:0x28])[0]
        numbits = struct.unpack("<i", blob[0x28:0x2C])[0]
        say("%s header 0x%X: Data=0x%X MaxIndex=%d Cap=%d word0=0x%X sec=0x%X NumBits=%d"
            % (FIELD, header, data, num, mx, words[0], sec, numbits))
        if sec != 0:
            say("BLOCKED: secondary bit pointer is non-zero — the bits are on the heap and the "
                "inline words at +0x10 are stale. Refusing to poke.")
            return 2

        # which element index holds VALUE, read live
        idx = next((e["i"] for e in (f.get("set_elements") or []) if str(e.get("k")) == VALUE),
                   None)
        if idx is None:
            say("BLOCKED: %s not present in %s" % (VALUE, FIELD))
            return 2
        if numbits <= idx:
            say("BLOCKED: NumBits %d <= index %d" % (numbits, idx))
            return 2
        say("target: %s is at element index %d" % (VALUE, idx))

        # ---- first scan -------------------------------------------------------
        r = c.request("begin_value_scan", data_type="Int32", scan_type="Exact",
                      value=VALUE, deep=False, game_only=True, max_results=5000)
        sid = r.get("data", r).get("session_id")
        before = rows_for(c, sid)
        say("")
        say("first scan: %d %s row(s)" % (len(before), FIELD))
        for x in before:
            say("   %-14s addr=%s inst=%s" % (x.get("field_name"), x.get("addr"),
                                              x.get("instance_addr")))
        live_row = next((x for x in before if int(str(x["instance_addr"]), 16) == inst), None)
        cdo_rows = [x for x in before if int(str(x["instance_addr"]), 16) != inst]
        if not live_row:
            say("BLOCKED: no candidate on the live actor")
            c.request("end_value_scan", session_id=sid)
            return 2
        if not cdo_rows:
            say("BLOCKED: no CDO row to use as the paired control — a drop could not be "
                "distinguished from a mass wipe")
            c.request("end_value_scan", session_id=sid)
            return 2
        live_cand_addr = int(str(live_row["addr"]), 16)

        # ---- NEGATIVE CONTROL FIRST: an unchanged refine must keep everything ---
        say("")
        say("== NEGATIVE CONTROL: refine with nothing changed ==")
        mark0 = time.strftime("%Y-%m-%d %H:%M:%S")
        time.sleep(1.1)
        c.request("refine_value_scan", session_id=sid, scan_type="Exact", value=VALUE)
        ctrl = rows_for(c, sid)
        say("   rows after an unchanged refine: %d (was %d)" % (len(ctrl), len(before)))
        if len(ctrl) != len(before):
            fails.append("an unchanged refine already lost rows — every later conclusion would "
                         "be about that, not about the bit")
        drops0 = [l for l in since(mark0, "Refine re-anchor:") if "0 dropped" not in l]
        if drops0:
            fails.append("an unchanged refine reported drops: %s" % drops0[-1].strip()[-120:])
        else:
            say("   OK: no drops reported on an unchanged refine")

        # ---- clear the allocation bit for THIS element, live actor only --------
        say("")
        say("== clearing allocation bit %d (live actor only) ==" % idx)
        word_off = header + 0x10 + 4 * (idx // 32)
        old_word = words[idx // 32]
        new_word = old_word & ~(1 << (idx % 32))
        say("   word at 0x%X: 0x%08X -> 0x%08X" % (word_off, old_word, new_word))

        mark = time.strftime("%Y-%m-%d %H:%M:%S")
        time.sleep(1.1)

        guards = {"Data/Num/Max": (header, 16), "NumBits/MaxBits": (header + 0x28, 8)}
        with Mutation(c, "%s alloc bits" % FIELD, word_off, 4, expect_unchanged=guards) as m:
            if not m.apply(struct.pack("<I", new_word)):
                fails.append("could not clear the allocation bit")
            else:
                if not m.assert_others_unchanged():
                    fails.append("the poke moved something other than the bit word")
                # the value must STILL be readable at the candidate address — that is the
                # whole point: only the bit says it is gone.
                stillv = read_bytes(c, live_cand_addr, 4)
                sv = struct.unpack("<i", stillv)[0] if stillv else None
                say("   candidate address still reads %s (must still be %s — the bit is the "
                    "only witness)" % (sv, VALUE))
                if str(sv) != VALUE:
                    fails.append("the candidate address no longer reads %s, so a drop could be "
                                 "explained by the value changing instead of by the bit" % VALUE)

                c.request("refine_value_scan", session_id=sid, scan_type="Exact", value=VALUE)
                after = rows_for(c, sid)
                say("")
                say("   rows after: %d" % len(after))
                for x in after:
                    say("      %-14s addr=%s inst=%s" % (x.get("field_name"), x.get("addr"),
                                                         x.get("instance_addr")))
                live_after = next((x for x in after
                                   if int(str(x["instance_addr"]), 16) == inst), None)
                if live_after:
                    fails.append("(a) the live candidate SURVIVED a cleared allocation bit — the "
                                 "removed entry still reads as a live match")
                else:
                    say("   (a) OK: the live candidate is gone")
                survivors = {int(str(x["instance_addr"]), 16) for x in after}
                missing = [x for x in cdo_rows
                           if int(str(x["instance_addr"]), 16) not in survivors]
                if missing:
                    fails.append("(b) the CDO candidate vanished too — mass drop, not a targeted "
                                 "one")
                else:
                    say("   (b) OK: the CDO candidate survived (paired control)")

                lines = since(mark, "Refine re-anchor:")
                say("   (c) re-anchor lines: %d" % len(lines))
                for l in lines:
                    say("        %s" % l.strip()[-120:])
                if not lines:
                    fails.append("(c) no re-anchor line at all — nothing reported the drop")
                else:
                    last = lines[-1]
                    if "1 dropped" not in last:
                        fails.append("(c) expected '1 dropped': %s" % last.strip()[-120:])
                    if "0 container element(s) repointed" not in last:
                        fails.append("(d) expected 0 repointed on a sparse drop: %s"
                                     % last.strip()[-120:])

                # second, independent detector: a FRESH scan must not find the live one
                say("")
                say("   second detector: a fresh scan with the bit still cleared")
                r2 = c.request("begin_value_scan", data_type="Int32", scan_type="Exact",
                               value=VALUE, deep=False, game_only=True, max_results=5000)
                sid2 = r2.get("data", r2).get("session_id")
                fresh = rows_for(c, sid2)
                fresh_live = [x for x in fresh if int(str(x["instance_addr"]), 16) == inst]
                fresh_cdo = [x for x in fresh if int(str(x["instance_addr"]), 16) != inst]
                say("      fresh scan: live rows=%d  cdo rows=%d" % (len(fresh_live),
                                                                     len(fresh_cdo)))
                if fresh_live:
                    fails.append("a FRESH scan still finds the live entry, so the first scanner "
                                 "path ignores the allocation bit even though refine honours it")
                if not fresh_cdo:
                    fails.append("a fresh scan finds no CDO row either — the fresh path is not "
                                 "working, so its silence about the live one means nothing")
                c.request("end_value_scan", session_id=sid2)

        c.request("end_value_scan", session_id=sid)
        now = read_bytes(c, header, 0x38)
        if now != blob:
            fails.append("the %s header was not fully restored" % FIELD)
        else:
            say("")
            say("%s header verified restored" % FIELD)

    say("")
    if fails:
        say("A11 step 4: FAIL")
        for f in fails:
            say("   - %s" % f)
        return 1
    say("A11 step 4: PASS — a cleared allocation bit drops the candidate, the CDO sibling "
        "survives, and the value is still readable at the dropped address")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
