"""V1a step 1 -- a container that reallocates must not leave a Next Scan on a stale address.

    py tools/verify/v1a_container_realloc.py

THE ROW, and why its wording needed re-deriving before it could be judged.
The row says the candidate must be "discarded (an SEH-safe read failure eliminates
it)" and must not report a wrong hit address. That was written before audit #5's A11
re-anchor landed. What the code does NOW is richer, and better:

    Radar.h  RefineContainerAnchor(...)
      2. if (elementIndex < 0 || numAtScan < 0 || !dataAtScan || !nowData) KeepAddress
      3. if (elementIndex >= nowNum)                                       Drop
      4. sparse && !slotAllocated                                          Drop
      5. if (nowNum < numAtScan)              // container SHRANK          Drop
      6. if (nowData != dataAtScan)           // buffer MOVED (growth)     Repoint
      7. otherwise                                                         KeepAddress

So a GROWTH realloc is expected to REPOINT the candidate to its new address, not drop
it -- which satisfies the row's real requirement ("no wrong address") more strongly
than dropping would. PASS is therefore: dropped, or re-pointed to the CORRECT new
address. FAIL is: kept at the OLD address.

  !! AND READING THAT ORDER PREDICTS A HOLE, which phase 3 exists to test rather than
     assert. TArray::Empty(0) sets Data to nullptr. That makes `!nowData` true, so the
     step-2 early-out fires and returns KeepAddress -- BEFORE step 3 and step 5, both
     of which would have said Drop. The guard's comment says it is for "missing the
     data to act on it", but nowData == 0 is not missing bookkeeping: it is positive
     evidence the buffer is gone. If that read is right, an emptied container leaves
     its candidates pointing at freed memory.

THE TWO WAYS THIS RIG COULD LIE TO ITSELF, both closed:

  * "The candidate vanished, so the anchor logic dropped it." NOT NECESSARILY -- it
    may simply have failed the VALUE test because freed memory no longer reads 7001.
    That is the allocator's luck, not our correctness, and the first run of this rig
    hit exactly that and had to report INCONCLUSIVE.
      The fix is to refine with a predicate that CANNOT eliminate anything:
    `Between INT32_MIN..INT32_MAX` matches every possible int32. Under it the byte
    comparison is a tautology, so the only things left that can remove a candidate
    are the container-anchor rule and a failed SEH read. A candidate that survives a
    permissive refine survives *because the anchor policy kept it*, and one that
    disappears was *dropped by policy*. No allocator behaviour is involved and
    nothing has to be assumed about what freed memory contains.
      Phase 2 still reads the stale address with ReadProcessMemory and prints it,
    because it is the difference between "the hole is theoretical" and "the hole is
    live right now on this machine".

  * "Everything got discarded, so discarding works." A refine that drops every
    candidate would pass a naive drop test. The positive control is built into the
    same scan: value 7001 exists in Arr_Churn[0] on BOTH the live actor and the CDO
    (Default__DumperTestActor), and nothing reallocates the CDO's array. It must
    survive every phase, at an unchanged address, or the refine is just a shredder.

Ground truth for the container header is read with ReadProcessMemory, NOT from the
DLL's own walk -- deriving the expected value from the observed one can only agree
with itself.
"""
from __future__ import annotations

import argparse
import ctypes
import ctypes.wintypes as w
import json
import pathlib
import struct
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient           # noqa: E402
from ad4_contested import find_live_actor, invoke   # noqa: E402

SCAN_VALUE = 7001          # Arr_Churn[0], seeded in the ctor so the CDO has it too
FIELD = "Arr_Churn"

k32 = ctypes.WinDLL("kernel32", use_last_error=True)
k32.OpenProcess.restype = w.HANDLE
k32.ReadProcessMemory.argtypes = [w.HANDLE, w.LPCVOID, w.LPVOID,
                                  ctypes.c_size_t, ctypes.POINTER(ctypes.c_size_t)]


class Mem:
    """Independent witness: raw process reads, with the DLL out of the loop."""

    def __init__(self):
        pid = int((pathlib.Path(__file__).resolve().parents[2] / "out" / "host.pid").read_text())
        self.pid = pid
        self.h = k32.OpenProcess(0x0010 | 0x0400, False, pid)
        if not self.h:
            raise SystemExit("v1a: FAILED -- OpenProcess(%d): %d" % (pid, ctypes.get_last_error()))

    def read(self, addr, n):
        buf = (ctypes.c_ubyte * n)()
        got = ctypes.c_size_t()
        if not k32.ReadProcessMemory(self.h, ctypes.c_void_p(addr), buf, n, ctypes.byref(got)):
            return None
        return bytes(buf)

    def tarray(self, header_addr):
        """(Data, Count, Max) straight out of the TArray header."""
        b = self.read(header_addr, 16)
        if not b:
            return None
        return struct.unpack("<Qii", b)

    def int32(self, addr):
        b = self.read(addr, 4)
        return None if b is None else struct.unpack("<i", b)[0]


def arr_field(c, addr):
    w_ = c.request("walk_instance", addr=addr, array_limit=1)
    for f in w_.get("fields", []):
        if f.get("name") == FIELD:
            return f
    raise SystemExit("v1a: FAILED -- %s not found on %s" % (FIELD, addr))


def tag(cands, live_addr):
    """Split candidates into (live actor, CDO) by their instance address."""
    live = [x for x in cands if x.get("instance_addr") == live_addr]
    cdo = [x for x in cands if x.get("instance_addr") != live_addr]
    return live, cdo


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--grow", type=int, default=400)
    a = ap.parse_args(argv)
    mem = Mem()
    fails, notes = [], []

    with PipeClient() as c:
        c.assert_build()
        c.ensure_scanned()
        actor = find_live_actor(c)
        live_addr = actor["addr"]
        f = arr_field(c, live_addr)
        off = f["offset"]
        header = int(live_addr, 16) + off
        print("actor %s @%s   %s at +%d (header 0x%X)   pid %d"
              % (actor["name"], live_addr, FIELD, off, header, mem.pid))

        d0, n0, m0 = mem.tarray(header)
        print("ground truth (ReadProcessMemory): Data=0x%X Count=%d Max=%d\n" % (d0, n0, m0))

        # ---------- scan ----------
        r = c.request("begin_value_scan", data_type="Int32", scan_type="Exact",
                      value=str(SCAN_VALUE), game_only=True, max_results=50000)
        sid = r["session_id"]
        cands = r.get("candidates", [])
        live, cdo = tag(cands, live_addr)
        print("[scan] Int32 Exact %d -> %d candidate(s): %d live, %d CDO"
              % (SCAN_VALUE, len(cands), len(live), len(cdo)))
        for x in cands:
            print("       %s  %-16s inst=%s" % (x["addr"], x["field_name"], x["instance_addr"]))
        if not live:
            raise SystemExit("v1a: FAILED -- no candidate inside the LIVE actor's container; "
                             "nothing below tests a realloc")
        if not cdo:
            notes.append("no CDO candidate -- the built-in positive control is ABSENT, so a "
                         "'dropped' result cannot be distinguished from a shredder")
        live_addr0 = live[0]["addr"]
        cdo_addr0 = cdo[0]["addr"] if cdo else None

        try:
            # ---------- phase 1: GROWTH realloc -> expect Repoint ----------
            print("\n[1] growth realloc -- V1a_GrowContainers(%d)" % a.grow)
            invoke(c, live_addr, "V1a_GrowContainers", parms_size=4,
                   params_hex=a.grow.to_bytes(4, "little").hex())
            d1, n1, m1 = mem.tarray(header)
            print("    ground truth: Data 0x%X -> 0x%X   Count %d -> %d   %s"
                  % (d0, d1, n0, n1, "MOVED" if d1 != d0 else "DID NOT MOVE"))
            if d1 == d0:
                fails.append("1 precondition: the buffer did not move, so nothing was tested")
            r1 = c.request("refine_value_scan", session_id=sid,
                           scan_type="Exact", value=str(SCAN_VALUE))
            c1 = r1.get("candidates", [])
            l1, k1 = tag(c1, live_addr)
            print("    refine -> %d candidate(s)" % len(c1))
            for x in c1:
                print("       %s  inst=%s" % (x["addr"], x["instance_addr"]))
            expect_new = "0x%X" % d1              # element 0 sits at the buffer base
            got_new = l1[0]["addr"] if l1 else None
            if l1:
                same = int(got_new, 16) == d1
                stale = int(got_new, 16) == int(live_addr0, 16)
                print("    live candidate: %s   (old %s, correct new %s) -> %s"
                      % (got_new, live_addr0, expect_new,
                         "REPOINTED CORRECTLY" if same else
                         ("STALE -- reports the old address" if stale else "UNEXPECTED")))
                ok_1 = same
                if stale:
                    fails.append("1: live candidate kept the OLD address %s after the buffer "
                                 "moved to 0x%X" % (live_addr0, d1))
                elif not same:
                    fails.append("1: live candidate at %s, expected %s" % (got_new, expect_new))
            else:
                print("    live candidate: DROPPED (acceptable -- no wrong address reported)")
                ok_1 = True
                notes.append("phase 1 DROPPED rather than repointed; the row's letter is "
                             "satisfied but RefineContainerAnchor step 6 predicts Repoint")
            if cdo:
                surv = [x for x in k1 if x["addr"] == cdo_addr0]
                print("    POSITIVE CONTROL, the CDO's copy: %s"
                      % ("survived unchanged at %s" % cdo_addr0 if surv else "GONE -- refine is a shredder"))
                if not surv:
                    fails.append("1: the CDO candidate was dropped; the refine discards "
                                 "indiscriminately and phase-1 'correctness' means nothing")
                ok_1 = ok_1 and bool(surv)

            # ---------- phase 2: EMPTY -> the predicted hole ----------
            print("\n[2] empty the container -- V1a_ShrinkContainers() (TArray::Empty(0))")
            before_empty = l1[0]["addr"] if l1 else live_addr0
            invoke(c, live_addr, "V1a_ShrinkContainers")
            d2, n2, m2 = mem.tarray(header)
            print("    ground truth: Data=0x%X Count=%d Max=%d %s"
                  % (d2, n2, m2, "(buffer released)" if d2 == 0 else ""))
            stale_val = mem.int32(int(before_empty, 16))
            print("    the freed address %s now reads: %s"
                  % (before_empty,
                     "UNREADABLE" if stale_val is None else
                     "%d %s" % (stale_val, "<- STILL the scanned value"
                                if stale_val == SCAN_VALUE else "(changed)")))
            # PERMISSIVE refine: Between INT32_MIN..INT32_MAX matches every int32, so
            # the value test cannot remove anything. Whatever disappears was removed
            # by the container-anchor rule (or a failed read) -- which is precisely
            # the thing under test, and it makes the verdict independent of what the
            # allocator happened to leave in the freed block.
            r2 = c.request("refine_value_scan", session_id=sid, scan_type="Between",
                           value=str(-2147483648), value2=str(2147483647))
            c2 = r2.get("candidates", [])
            l2, k2 = tag(c2, live_addr)
            print("    refine (PERMISSIVE Between INT32_MIN..INT32_MAX) -> %d candidate(s)"
                  % len(c2))
            for x in c2:
                print("       %s  inst=%s" % (x["addr"], x["instance_addr"]))
            if l2:
                ok_2 = False
                fails.append("2: a candidate SURVIVED a PERMISSIVE refine in an emptied "
                             "container, at %s. The value test cannot have kept it, so "
                             "RefineContainerAnchor returned KeepAddress -- its `!nowData` "
                             "early-out firing before the two Drop rules. The address "
                             "points at freed memory." % l2[0]["addr"])
            else:
                ok_2 = True
                print("    -> dropped BY POLICY. Under a permissive predicate the value "
                      "test cannot eliminate a candidate, so the anchor rule did it.")
            if cdo:
                surv2 = [x for x in k2 if x["addr"] == cdo_addr0]
                print("    POSITIVE CONTROL, the CDO's copy: %s"
                      % ("survived" if surv2 else "GONE -- refine is a shredder"))
                if not surv2:
                    fails.append("2: the CDO candidate was dropped")
        finally:
            c.request("end_value_scan", session_id=sid)

    print("\n" + "=" * 72)
    print("1 growth realloc: no stale address (repoint or drop), CDO survives : %s"
          % ("PASS" if ok_1 else "FAIL"))
    print("2 emptied container: candidate not left on freed memory           : %s"
          % ({True: "PASS", False: "FAIL", None: "INCONCLUSIVE"}[ok_2]))
    for n in notes:
        print("   note: %s" % n)
    for f_ in fails:
        print("   FAIL: %s" % f_)
    print("\nV1a step 1: %s" % ("PASS" if not fails and ok_2 is True else
                                ("INCONCLUSIVE" if not fails else "FAIL")))
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
