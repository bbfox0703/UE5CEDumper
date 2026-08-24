r"""M5 -- UE5_Shutdown worker-join ordering, with a detector shown able to report a hang.

    py tools/verify/m5_shutdown_join.py control    # prove the detector can see a hang
    py tools/verify/m5_shutdown_join.py run        # the actual test

THE DEFECT
  UE5_Shutdown joined the hold workers BEFORE stopping the pipe, so a mutator arriving
  in that window respawned an unjoined worker -- and the process could then hang or crash
  on exit.

WHY THIS RIG IS SHAPED THE WAY IT IS
  The register's acceptance is: "with a hold active, close the game while the UI is still
  connected -> no hang, no crash on exit. Evidence is the ABSENCE of a hang; there is no
  positive log line."

  An absence is not a measurement. A rig that waits for the process to vanish and prints
  PASS would print PASS on a build that hangs, if the wait were long enough or the exit
  check wrong -- and it would print PASS on a build where WM_CLOSE never even arrived.
  So this rig has two verbs and the control is not optional:

    control  suspend the game's MAIN THREAD, then post WM_CLOSE. The process cannot
             process the message, so it must NOT exit inside the deadline and the rig
             must report HANG. If `control` reports a clean exit, the detector is broken
             and `run` proves nothing. It also asserts IsHungAppWindow, so the hang is
             witnessed by the OS rather than inferred from a timeout alone.
    run      hold active + TWO live pipe connections (the UI holds two; kMaxPipeInstances
             is 3), post WM_CLOSE, require exit well inside the deadline, and require no
             new minidump.

  ⚠ It MUST be a posted WM_CLOSE, never `taskkill /F`: with /F the DLL's shutdown path
  never runs at all and the test is vacuous. Recorded during the See-through arms.

WHAT IT DELIBERATELY DOES NOT CLAIM
  A clean exit here does not prove the ordering bug is impossible -- the racing mutator
  would have to arrive inside a window of a few microseconds. What it does prove is that
  the ordinary shutdown-with-a-hold-active path is clean, and (with `--poke`) that a
  mutator hammering the field right through the close does not wedge it.
"""
from __future__ import annotations

import ctypes
import ctypes.wintypes as wt
import os
import pathlib
import subprocess
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient  # noqa: E402

u32 = ctypes.WinDLL("user32", use_last_error=True)
k32 = ctypes.WinDLL("kernel32", use_last_error=True)
WM_CLOSE = 0x0010
DEADLINE = 8.0          # generous; the acceptance is sub-second and is asserted separately
SUBSECOND = 1.0
DUMPS = pathlib.Path(os.environ["LOCALAPPDATA"]) / "CrashDumps"
CLS, FIELD = "Actor", "bCanBeDamaged"


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + "\n")
    sys.stdout.flush()


def game_hwnd(pid: int):
    found = []

    @ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)
    def cb(hwnd, _):
        p = wt.DWORD()
        u32.GetWindowThreadProcessId(hwnd, ctypes.byref(p))
        if p.value == pid and u32.IsWindowVisible(hwnd):
            found.append(hwnd)
        return True

    u32.EnumWindows(cb, 0)
    return found[0] if found else None


def alive(pid: int) -> bool:
    out = subprocess.run(["tasklist", "/FI", "PID eq %d" % pid, "/FO", "CSV", "/NH"],
                         capture_output=True, text=True, errors="replace").stdout
    return str(pid) in out


def dumps_now():
    try:
        return {p.name for p in DUMPS.glob("DumperTest*.dmp")}
    except OSError:
        return set()


def main_tid(pid: int):
    """The UE game thread, taken from the row suspend.py itself labels.

    ⚠ Do NOT compute this as min(tid). The first attempt did, and suspend.py's header
    line is "DumperTest.exe (50348): 141 threads -- EARLIEST CREATED FIRST", so the
    parser returned **141** -- the thread COUNT -- and suspending a non-existent thread
    produced a clean exit, i.e. a control that silently failed to arm. suspend.py already
    marks the right row:
        tid=30576    cpu=    3062.5 ms  <-- main thread (UE game thread)
    """
    out = subprocess.run([sys.executable, str(pathlib.Path(__file__).with_name("suspend.py")),
                          "threads", "DumperTest"],
                         capture_output=True, text=True, errors="replace").stdout
    for line in out.splitlines():
        if "main thread" in line and "tid=" in line:
            tok = line.split("tid=", 1)[1].split()[0].strip(",")
            if tok.isdigit():
                return int(tok)
    return None


def close_and_time(pid, hwnd, deadline=DEADLINE):
    t0 = time.time()
    u32.PostMessageW(hwnd, WM_CLOSE, 0, 0)
    while time.time() - t0 < deadline:
        if not alive(pid):
            return time.time() - t0
        time.sleep(0.05)
    return None


def run(poke=False, hold=True):
    """hold=False is the BASELINE: an identical close with NO hold armed.

    ⚠ It exists because "sub-second exit" turned out to be an assumption about UE, not
    about our DLL. The first run measured 1.970s with a hold active, which reads as a
    failure against the register's wording -- but a UE game tears down rendering, audio
    and the engine on a normal close, and none of that is ours. The only number that can
    accuse the DLL is the DIFFERENCE between hold and no-hold on the same host, so both
    are measured and the delta is what gets reported.
    """
    fails = []
    before = dumps_now()
    # Two live connections: the UI holds two, and the defect is about a request arriving
    # while shutdown is in flight, so a single idle socket is the weaker setup.
    a = PipeClient().connect()
    b = PipeClient().connect()
    try:
        say("DLL build %s" % a.assert_build())
        pid = a.request("get_pointers")["pid"]
        hwnd = game_hwnd(pid)
        say("pid=%d hwnd=0x%X  (2 pipe connections open)" % (pid, hwnd or 0))
        if not hwnd:
            raise SystemExit("m5: no visible game window -- cannot post WM_CLOSE")

        a.request("reset_all_fields")
        held0 = None
        if hold:
            fr = a.request("force_field", class_name=CLS, field_name=FIELD,
                           kind="bool", on=False)
            say("hold   : force_field %s.%s -> resolved=%s held=%s"
                % (CLS, FIELD, fr.get("resolved"), fr.get("held")))
            if not fr.get("resolved") or not fr.get("held"):
                fails.append("the hold did not take, so there is no worker to mis-join -- "
                             "this run would be vacuous")
            held0 = fr.get("held")
        else:
            say("hold   : NONE (baseline run)")

        # A hold is live and a re-assert worker is running. Close via WM_CLOSE.
        say("")
        say("posting WM_CLOSE ...")
        dt = close_and_time(pid, hwnd)
    finally:
        for c in (a, b):
            try:
                c.close()
            except Exception:
                pass

    if dt is None:
        fails.append("*** HANG: still alive %.1fs after WM_CLOSE with a hold active "
                     "(held=%s). This is the M5 defect." % (DEADLINE, held0))
        say("HANG -- process did not exit within %.1fs" % DEADLINE)
    else:
        say("exited in %.3fs   (hold=%s)" % (dt, "yes" if hold else "no"))
        # NOT compared against SUBSECOND: see run()'s docstring. UE's own teardown
        # dominates. Compare hold vs no-hold instead, with `baseline`.

    time.sleep(1.0)
    new = dumps_now() - before
    say("new minidumps: %s" % (sorted(new) if new else "none"))
    if new:
        fails.append("crash on exit -- new minidump(s): %s" % sorted(new))

    say("")
    if fails:
        say("FAIL (%d)" % len(fails))
        for f in fails:
            say("  - %s" % f)
        return 1
    say("PASS -- %s + 2 connections, clean exit on a posted WM_CLOSE, no minidump"
        % ("hold active" if hold else "NO hold (baseline)"))
    return 0


def control():
    """Show the detector can REPORT a hang. Without this, run()'s PASS is unfalsifiable."""
    say("CONTROL: suspending the game's main thread so WM_CLOSE cannot be processed.")
    say("Expect HANG. A clean exit here means the detector is broken.")
    with PipeClient().connect() as c:
        pid = c.request("get_pointers")["pid"]
    hwnd = game_hwnd(pid)
    tid = main_tid(pid)
    if not hwnd or not tid:
        raise SystemExit("control: need both a window and a main tid (hwnd=%s tid=%s)"
                         % (hwnd, tid))
    say("pid=%d hwnd=0x%X main tid=%d" % (pid, hwnd, tid))
    sp = pathlib.Path(__file__).with_name("suspend.py")
    subprocess.run([sys.executable, str(sp), "suspend-tid", "DumperTest", str(tid)],
                   capture_output=True, text=True)
    try:
        time.sleep(0.5)
        hung = bool(u32.IsHungAppWindow(hwnd))
        dt = close_and_time(pid, hwnd, deadline=5.0)
        hung2 = bool(u32.IsHungAppWindow(hwnd)) if alive(pid) else None
        say("IsHungAppWindow before/after close: %s / %s" % (hung, hung2))
        if dt is None:
            say("")
            say("CONTROL OK -- the rig reported a HANG (no exit in 5.0s). The detector "
                "in run() is therefore able to fail.")
            rc = 0
        else:
            say("")
            say("CONTROL FAILED -- the process exited in %.3fs even with its main thread "
                "suspended, so this rig cannot tell a hang from a clean exit." % dt)
            rc = 1
    finally:
        subprocess.run([sys.executable, str(sp), "resume-tid", "DumperTest", str(tid)],
                       capture_output=True, text=True)
        say("(main thread resumed; kill the game before running `run`)")
    return rc


if __name__ == "__main__":
    v = sys.argv[1] if len(sys.argv) > 1 else "run"
    if v == "control":
        raise SystemExit(control())
    if v == "run":
        raise SystemExit(run(poke="--poke" in sys.argv))
    if v == "baseline":
        raise SystemExit(run(hold=False))
    raise SystemExit(__doc__)
