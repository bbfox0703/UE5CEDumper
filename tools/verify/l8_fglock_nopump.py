"""L8 - enabling the foreground lock must not hang the pipe thread on a non-pumping window.

    py tools/verify/l8_fglock_nopump.py --tid <game thread id>

THE FIX UNDER TEST (7f3898ff, "DLL LOW batch"): Grausam's `SubclassEnumProc` called
`GetWindowTextW` **under `g_mutex`** into a buffer it never used. `GetWindowTextW` on a
same-process window is a SYNCHRONOUS `WM_GETTEXT`, so if that window's owning thread is not
pumping messages it blocks - and it blocks while holding `g_mutex`, i.e. it hangs the pipe /
mailbox thread. A paused or backgrounded game is exactly the state this feature targets.
The dead call was deleted (Grausam.cpp:175 now carries only the explanatory comment).

OFFLINE HALF, already settled and stronger than reading source: `GetWindowTextW` does not
appear in `dist/UE5Dumper.dll`'s IMPORT TABLE at all, while `SetWindowLongPtrW` - the
subclassing call two lines away in the same function - does. So the detector fires and the
absence is real.

LIVE HALF, this rig. Freeze ONLY the UE game thread (`suspend-tid`, not a whole-process
suspend, which would stop Fern too and make the pipe unable to answer at all), then toggle
the foreground lock OFF and back ON so `SubclassEnumProc` re-runs over a window whose thread
is not pumping. `set_foreground_lock` is documented thread-agnostic (it runs on the polling
thread), so it must still answer promptly.

⭐ THE FREEZE IS PROVEN, NOT ASSUMED. `get_pointers` reports `game_thread_stalled`, and the
rig requires it to flip False -> True before it will score the latency. Without that, "the
call returned fast" is equally consistent with "the thread was never actually frozen", which
is the vacuous version of this test.
"""
from __future__ import annotations

import argparse
import os
import pathlib
import subprocess
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from pipe_client import PipeClient           # noqa: E402


def tid_ctl(verb, tid):
    return subprocess.run([sys.executable, str(HERE / "suspend.py"), verb, "DumperTest", str(tid)],
                          capture_output=True, text=True, encoding="utf-8", errors="replace")


def timed(c, cmd, **kw):
    t = time.time()
    try:
        r = c.request(cmd, **kw)
        return time.time() - t, r
    except Exception as e:
        return time.time() - t, {"ok": False, "error": repr(e)}


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--tid", type=int, required=True, help="the UE game thread id")
    ap.add_argument("--budget", type=float, default=3.0,
                    help="seconds; a lock toggle slower than this counts as a hang")
    a = ap.parse_args(argv)
    fails = []

    with PipeClient() as c:
        c.assert_build(); c.ensure_scanned()
        logdir = pathlib.Path(os.environ["LOCALAPPDATA"]) / "UE5CEDumper" / "Logs" / "DumperTest"

        def subclass_lines():
            n = 0
            for f in logdir.glob("*.log"):
                try:
                    n += sum(1 for ln in f.read_text(encoding="utf-8", errors="replace").splitlines()
                             if "Subclassed window" in ln)
                except OSError:
                    pass
            return n

        st0 = c.request("get_foreground_lock").get("state")
        print("[0] fresh process, lock state = %s (must be 0 -- this must be the FIRST enable)" % st0)
        if st0 != 0:
            fails.append("0: the lock is already on, so SubclassEnumProc has already run and its "
                         "body will be SKIPPED on the next enable (Grausam.cpp:167 returns early "
                         "on an already-subclassed window). Relaunch the game.")
        before_lines = subclass_lines()

        # ⭐ FORCE THE ProcessEvent HOOK FIRST, or the stall detector is inert. With no hook
        # the DLL cannot measure liveness at all and now WITHHOLDS `game_thread_stalled`
        # (STALLDEFAULT-2026-08-26) -- it used to default it to False, which is why the first
        # run of this rig froze the correct thread (highest-CPU, 4906 ms vs 15 ms) and still
        # saw False. pe_profile_start calls UE5_EnsureGameThreadHook.
        c.request("pe_profile_start")
        time.sleep(1.5)
        live = c.request("get_pointers").get("game_thread_stalled")
        print("    hook forced; game_thread_stalled = %r (must be False -- detector armed AND "
              "thread healthy; None means not armed)" % live)
        # `is not False` covers both failure modes in one test: True (already stalled) and
        # None (never armed). The second used to be indistinguishable from success.
        if live is not False:
            fails.append("0: the stall detector is not armed (live=%r) or the host is already "
                         "stalled -- the freeze below would prove nothing" % live)

        print("")
        print("[1] freezing tid %d BEFORE the first enable (game thread only)" % a.tid)
        out = tid_ctl("suspend-tid", a.tid)
        print("    " + (out.stdout or out.stderr).strip().splitlines()[-1][:100])
        time.sleep(2.0)
        stalled1 = c.request("get_pointers").get("game_thread_stalled")
        print("    game_thread_stalled = %s   (must be True)" % stalled1)
        if not stalled1:
            fails.append("1: game_thread_stalled never became True -- the thread was NOT frozen, "
                         "so a fast reply below would prove nothing")

        dt_on, r = timed(c, "set_foreground_lock", enable=True)
        dt_get, _ = timed(c, "get_foreground_lock")
        print("")
        print("[2] FIRST enable with the thread frozen: %.3fs  (state=%s), get %.3fs"
              % (dt_on, r.get("state"), dt_get))
        if r.get("state") != 1:
            fails.append("2: enable returned state=%s not 1 -- the lock did not engage, so this "
                         "is a no-op measurement" % r.get("state"))
        worst = max(dt_on, dt_get)
        if worst > a.budget:
            fails.append("2: slowest call took %.2fs (> %.1fs) with the window's thread not "
                         "pumping -- the pipe thread is blocking on the window (the L8 hang)"
                         % (worst, a.budget))

        after_lines = subclass_lines()
        print("    'Subclassed window' log lines: %d -> %d (+%d)"
              % (before_lines, after_lines, after_lines - before_lines))
        if after_lines <= before_lines:
            fails.append("2: no window was subclassed, so SubclassEnumProc's BODY never ran and "
                         "the deleted GetWindowTextW call site was never reached -- vacuous")

        print("")
        print("[3] resuming tid %d" % a.tid)
        c.request("set_foreground_lock", enable=False)
        out = tid_ctl("resume-tid", a.tid)
        print("    " + (out.stdout or out.stderr).strip().splitlines()[-1][:100])
        time.sleep(2.0)
        stalled2 = c.request("get_pointers").get("game_thread_stalled")
        print("    game_thread_stalled = %s   (must be back to False)" % stalled2)
        if stalled2:
            fails.append("3: the game thread did not resume -- the host is left damaged")

    print("\n" + "=" * 72)
    print("L8 foreground-lock vs non-pumping window: %s" % ("PASS" if not fails else "FAIL"))
    for f in fails:
        print("   - %s" % f)
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
