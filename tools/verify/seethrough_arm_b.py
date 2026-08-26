"""M1-M5 step 1 ARM (b) - See-through active while the GAME IS HUNG, then a graceful close.

    py tools/verify/seethrough_arm_b.py --tid <game thread id>

THE ARM. `WorkerLoop`'s catch exists for the "See-through then close the game"
`std::terminate` / `0xC0000409` crash. Arm (d) covered a graceful close on a HEALTHY game;
arm (b) is the same close on a game whose thread has STALLED - the harder case, because
See-through's ~10 Hz worker invokes on the game thread via Stark, so a stalled thread is
exactly when the worker is mid-flight and cannot complete.

The register classified this human-only ("needs a human ... stalling the game"). It is not:
`suspend.py suspend-tid` stalls the UE game thread deterministically, which is the same
technique the B8 and L8 rows ended up using.

WITNESS THE HANG BEFORE CONCLUDING ANYTHING. The row says so explicitly, and it matters: if
the thread never actually stalled, a clean close afterwards is just arm (d) run again. Two
independent witnesses are required to flip, and both must have been in the healthy state
first:

  * `IsHungAppWindow(hwnd)`         - the OS's own view, from user32
  * `get_pointers.game_thread_stalled` - the DLL's view (Stark's hook heartbeat)

⚠ `game_thread_stalled` is now ABSENT when no ProcessEvent hook is installed -- the DLL
withholds the key rather than defaulting it to False, so an unmeasured liveness can no longer
pose as a healthy one (STALLDEFAULT-2026-08-26). The rig forces the hook first; enabling
See-through does that anyway, but it is asserted rather than assumed.

⚠⚠ READ THE KEY WITH .get(), NEVER `[...]`. The second read sits BETWEEN `suspend-tid` and
`resume-tid` with no try/finally, so a KeyError there would leave the game thread SUSPENDED
and the process needing a kill.

⚠ A `taskkill /F` does NOT test this arm - recorded during arms (c)/(d): the DLL's shutdown
path never runs at all, so the arm is vacuous. It must be a posted WM_CLOSE.
"""
from __future__ import annotations

import argparse
import ctypes
import ctypes.wintypes as wt
import os
import pathlib
import subprocess
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from pipe_client import PipeClient  # noqa: E402

u32 = ctypes.WinDLL("user32", use_last_error=True)
k32 = ctypes.WinDLL("kernel32", use_last_error=True)
WM_CLOSE = 0x0010
LOG = pathlib.Path(os.environ["LOCALAPPDATA"]) / "UE5CEDumper" / "Logs" / "DumperTest"


def game_hwnd(pid: int):
    found = []

    @ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)
    def cb(hwnd, _):
        p = wt.DWORD()
        u32.GetWindowThreadProcessId(hwnd, ctypes.byref(p))
        if p.value == pid and u32.IsWindowVisible(hwnd):
            n = u32.GetWindowTextLengthW(hwnd)
            if n > 0:
                found.append(hwnd)
        return True

    u32.EnumWindows(cb, 0)
    return found[0] if found else None


def sizes():
    return {f: f.stat().st_size for f in LOG.glob("*-0.log")}


def since(mark):
    out = []
    for f in LOG.glob("*-0.log"):
        try:
            out.append(f.read_text(encoding="utf-8", errors="replace")[mark.get(f, 0):])
        except OSError:
            pass
    return chr(10).join(out)


def tid_ctl(verb, tid):
    r = subprocess.run([sys.executable, str(HERE / "suspend.py"), verb, "DumperTest", str(tid)],
                       capture_output=True, text=True, encoding="utf-8", errors="replace")
    return (r.stdout or r.stderr).strip().splitlines()[-1]


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--tid", type=int, required=True)
    ap.add_argument("--hang", type=float, default=8.0, help="seconds to hold the stall")
    ap.add_argument("--exit-budget", type=float, default=20.0)
    a = ap.parse_args(argv)
    fails = []

    with PipeClient() as c:
        c.assert_build()
        c.ensure_scanned()
        pid = c.request("get_pointers")["pid"]
        hwnd = game_hwnd(pid)
        print("[0] pid=%d hwnd=0x%X" % (pid, hwnd or 0))
        if not hwnd:
            raise SystemExit("arm(b): no visible game window -- cannot post WM_CLOSE")

        c.request("pe_profile_start")
        c.request("seethrough_set", enable=True, count=1)
        time.sleep(2.5)
        st = c.request("seethrough_get_state")
        hidden = st.get("hidden_actors") or []
        print("[1] See-through active=%s hidden_count=%s actors=%s"
              % (st.get("active"), st.get("hidden_count"), hidden))
        if not hidden:
            raise SystemExit("arm(b): nothing hidden from this pose -- the arm would be vacuous")

        hung0 = bool(u32.IsHungAppWindow(hwnd))
        # Absent means the DLL is not MEASURING liveness (no PE hook), which makes the
        # whole arm vacuous -- and it must be caught HERE, before the suspend below.
        stalled0 = c.request("get_pointers").get("game_thread_stalled")
        print("[2] BEFORE the stall: IsHungAppWindow=%s  game_thread_stalled=%r (must be "
              "False, not None, or the witnesses cannot flip)" % (hung0, stalled0))
        if stalled0 is None:
            raise SystemExit("arm(b): the DLL is not measuring game-thread liveness (no PE "
                             "hook) -- refusing to suspend a thread whose stall nothing "
                             "would witness")
        if hung0 or stalled0:
            fails.append("2: the game already looks hung before the stall, so neither witness "
                         "can show the stall actually happened")

        mark = sizes()
        print("[3] stalling the game thread: %s" % tid_ctl("suspend-tid", a.tid))
        time.sleep(a.hang)
        hung1 = bool(u32.IsHungAppWindow(hwnd))
        # .get(), never [...]: this line is INSIDE the suspend window.
        stalled1 = c.request("get_pointers").get("game_thread_stalled")
        print("    WITNESSES: IsHungAppWindow=%s  game_thread_stalled=%r" % (hung1, stalled1))
        if not stalled1:
            fails.append("3: the DLL does not see the game thread as stalled -- the stall did "
                         "not take, so a clean close afterwards is just arm (d) again")
        if not hung1:
            print("    NOTE: the OS has not marked the window hung yet (it needs an unanswered "
                  "message); game_thread_stalled is the load-bearing witness here.")

        # the DLL must still answer while the game thread is stalled and See-through is active
        st2 = c.request("seethrough_get_state")
        print("[4] with the game hung, the DLL still answers: active=%s hidden_count=%s"
              % (st2.get("active"), st2.get("hidden_count")))

        print("[5] resuming: %s" % tid_ctl("resume-tid", a.tid))
        time.sleep(3.0)

    # ---------- the close, on a game that HAS been hung while See-through was active ----------
    print("[6] posting WM_CLOSE")
    u32.PostMessageW(hwnd, WM_CLOSE, 0, 0)
    t0 = time.time()
    gone = False
    while time.time() - t0 < a.exit_budget:
        time.sleep(0.5)
        h = k32.OpenProcess(0x1000, False, pid)
        if not h:
            gone = True
            break
        code = wt.DWORD()
        k32.GetExitCodeProcess(h, ctypes.byref(code))
        k32.CloseHandle(h)
        if code.value != 259:      # STILL_ACTIVE
            gone = True
            break
    dt = time.time() - t0
    print("    process exited=%s after %.1fs" % (gone, dt))
    if not gone:
        fails.append("6: the game did not exit within %.0fs of a graceful WM_CLOSE" % a.exit_budget)

    time.sleep(2.0)
    tail = since(mark)
    threw = tail.count("tick threw")
    print("[7] 'tick threw' in the logs since the stall: %d (must be 0)" % threw)
    if threw:
        fails.append("7: See-through's WorkerLoop caught a throw -- the crash this arm exists "
                     "for happened, it was merely swallowed")

    dumps = list(pathlib.Path(os.environ["LOCALAPPDATA"], "CrashDumps").glob("DumperTest*.dmp"))
    print("    DumperTest crash dumps: %d" % len(dumps))
    if dumps:
        fails.append("7: a DumperTest crash dump exists: %s" % [d.name for d in dumps][:3])

    print("")
    print("=" * 72)
    print("M1-M5 step 1 arm (b) -- hung game + graceful close: %s"
          % ("PASS" if not fails else "FAIL"))
    for f in fails:
        print("   - %s" % f)
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
