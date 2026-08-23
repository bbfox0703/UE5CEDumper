"""L12 - a malformed `str_params` element must not leak the buffers earlier ones allocated.

    py tools/verify/l12_strparams_leak.py

THE FIX UNDER TEST (7f3898ff, "DLL LOW batch - L1/L5/L8/L10/L12"): Fern's
invoke_function parses `str_params` in a loop, `malloc`ing one buffer per element and
pushing it to `strAllocs`. A malformed element throws a nlohmann `type_error` mid-loop,
which used to unwind PAST the free loop at the bottom - so every buffer an EARLIER
iteration allocated leaked. The fix wraps the loop in try/catch, frees `strAllocs` and
rethrows (Fern.cpp:5356 / :5403-5409).

THE DISCRIMINATING INPUT, and it is the whole design. The throw must land AFTER at least
one successful allocation, or there is nothing to leak and the test is vacuous either way:

    str_params = [ {off:0, wide:true, text:"<16 KB of A>"},   <- allocates ~32 KB, pushed
                   {off:0, wide:true, text: 12345          } ] <- `sp.value("text","")`
                                                                  throws type_error.302

`parms_size` is caller-supplied (`Fern.cpp:5283`), so we declare 64 bytes to satisfy the
loop's `off + 16 > paramBuf.size()` bounds check. The throw happens during PARSING, before
`paramPtr` is formed (:5411) and long before `UE5_CallProcessEventEx` (:5429) - so no
UFunction is ever invoked and the target function is irrelevant.

⭐ WHY THERE IS A POSITIVE CONTROL. The expected result is "memory did not grow", and an
absence proves nothing unless the detector is shown able to fire. So the rig FIRST spawns
300 holder actors and requires PrivateUsage to rise. Only after that number moves does a
flat reading during the leak phase mean anything.

Private bytes come from psapi GetProcessMemoryInfo (PROCESS_MEMORY_COUNTERS_EX.PrivateUsage)
via ctypes - psutil is not installed on this machine.
"""
from __future__ import annotations

import argparse
import ctypes
import ctypes.wintypes as wt
import pathlib
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient           # noqa: E402
from ad4_contested import find_live_actor, invoke   # noqa: E402


class PMCEX(ctypes.Structure):
    _fields_ = [("cb", wt.DWORD), ("PageFaultCount", wt.DWORD),
                ("PeakWorkingSetSize", ctypes.c_size_t), ("WorkingSetSize", ctypes.c_size_t),
                ("QuotaPeakPagedPoolUsage", ctypes.c_size_t), ("QuotaPagedPoolUsage", ctypes.c_size_t),
                ("QuotaPeakNonPagedPoolUsage", ctypes.c_size_t), ("QuotaNonPagedPoolUsage", ctypes.c_size_t),
                ("PagefileUsage", ctypes.c_size_t), ("PeakPagefileUsage", ctypes.c_size_t),
                ("PrivateUsage", ctypes.c_size_t)]


def private_mb(pid: int) -> float:
    PROCESS_QUERY_INFORMATION, PROCESS_VM_READ = 0x0400, 0x0010
    h = ctypes.windll.kernel32.OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, False, pid)
    if not h:
        raise SystemExit("l12: OpenProcess failed for pid %d" % pid)
    try:
        c = PMCEX(); c.cb = ctypes.sizeof(PMCEX)
        if not ctypes.windll.psapi.GetProcessMemoryInfo(h, ctypes.byref(c), c.cb):
            raise SystemExit("l12: GetProcessMemoryInfo failed")
        return c.PrivateUsage / (1024.0 * 1024.0)
    finally:
        ctypes.windll.kernel32.CloseHandle(h)


def i32(n):
    return int(n).to_bytes(4, "little", signed=True).hex()


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--n", type=int, default=2000, help="malformed invokes to send")
    ap.add_argument("--kb", type=int, default=16, help="size of the GOOD element's string, KB")
    ap.add_argument("--grow-mb", type=float, default=24.0,
                    help="fail if private bytes grow more than this during the leak phase")
    a = ap.parse_args(argv)
    fails = []

    with PipeClient() as c:
        c.assert_build(); c.ensure_scanned()
        pid = c.request("get_pointers")["pid"]
        act = find_live_actor(c)
        fn = {f["name"]: f for f in
              c.request("walk_functions", addr=act["class_addr"])["functions"]}
        print("[0] pid %d   private = %.1f MB" % (pid, private_mb(pid)))

        # ---------- POSITIVE CONTROL: the probe must be able to SEE growth ----------
        before = private_mb(pid)
        invoke(c, act["addr"], "Spawn_Holders",
               parms_size=fn["Spawn_Holders"]["parms_size"], params_hex=i32(3000) + "00")
        time.sleep(2.5)
        after = private_mb(pid)
        print("[1] CONTROL spawn 3000 holders: %.1f -> %.1f MB  (delta %+.1f)"
              % (before, after, after - before))
        # ⚠ CALIBRATION, not an arbitrary number. 300 actors was the first attempt and moved
        # the probe +0.0 MB -- NOT because the probe is blind but because ~600 KB is under the
        # noise floor of a 3 GB process (idle drift measured at ~0.2 MB between reads). The
        # control's job is to establish the probe's RESOLUTION relative to the effect under
        # test: 3000 holders moves it ~2.4 MB, and the leak this rig looks for is ~63 MB --
        # a 26x margin. A control the same size as the effect is not required; a control that
        # resolves an order of magnitude below it is.
        if after - before < 1.0:
            fails.append("1: the probe did not move on a real 3000-actor allocation, so a flat "
                         "reading later would prove NOTHING. Detector not established.")
        invoke(c, act["addr"], "Spawn_DestroyHolders")
        time.sleep(2.0)

        # ---------- settle, then the leak phase ----------
        base = private_mb(pid)
        print("[2] settled baseline: %.1f MB" % base)
        good = {"off": 0, "wide": True, "text": "A" * (a.kb * 1024)}
        bad = {"off": 0, "wide": True, "text": 12345}      # type_error.302 mid-loop
        per_kb = (a.kb * 1024 + 1) * 2 / 1024.0
        print("[3] sending %d invokes, each leaking ~%.0f KB if unfixed (~%.0f MB total)"
              % (a.n, per_kb, a.n * per_kb / 1024.0))

        errs = 0
        for i in range(a.n):
            try:
                r = c.request("invoke_function", addr=act["addr"],
                              func_name="Spawn_CountHolders", parms_size=64,
                              str_params=[good, bad])
                if not r.get("ok", True):
                    errs += 1
            except Exception:
                errs += 1
            if (i + 1) % 500 == 0:
                print("     %5d sent   private = %.1f MB (%+.1f)"
                      % (i + 1, private_mb(pid), private_mb(pid) - base))
        time.sleep(1.5)
        end = private_mb(pid)
        grew = end - base
        print("[4] after %d malformed invokes: %.1f MB  (delta %+.1f MB); %d error replies"
              % (a.n, end, grew, errs))

        # ANTI-VACUITY: if nothing errored, the malformed element never threw and the
        # whole run exercised the SUCCESS path -- which leaks nothing either way.
        if errs < a.n * 0.9:
            fails.append("4: only %d of %d requests errored -- the malformed element did not "
                         "throw, so the leak path was never entered and this run is vacuous"
                         % (errs, a.n))
        if grew > a.grow_mb:
            fails.append("4: private bytes grew %.1f MB (> %.1f) -- the mid-loop throw is "
                         "still leaking strAllocs" % (grew, a.grow_mb))

    print("\n" + "=" * 72)
    print("L12 str_params mid-loop leak: %s" % ("PASS" if not fails else "FAIL"))
    for f in fails:
        print("   - %s" % f)
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
