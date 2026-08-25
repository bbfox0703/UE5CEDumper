r"""PEHOOK step 5 -- THE FALSE-POSITIVE GUARD: a PATTERN-detected hook must survive an idle window.

    py tools/verify/pehook_step5_idle.py

THE ASYMMETRY THIS PROTECTS. A post-install validator soft-disables the ProcessEvent hook when it
counts **0 fires in 1500 ms** -- but only when the vtable offset came from the UE version TABLE
(a guess). A zero fire count also describes a perfectly correct hook on a game thread that is
paused, loading or minimised, so acting on every zero would disable a GOOD hook. When the offset
came from the PATTERN scan (which fingerprints ProcessEvent's own body) the verdict is REPORTED
and the hook is **KEPT**.

Steps 1b/2/3 already exercised the TABLE arm, by temporarily removing the SIB alternates so
DumperTest mis-detects. This is the other arm, and it needs the opposite staging: a host that
detects by PATTERN, whose game thread then goes quiet across the validator window.

HOW THE IDLE WINDOW IS STAGED WITHOUT A HUMAN
  The step says "background/pause the game so PE traffic stops". SUSPENDING THE UE GAME THREAD is
  the same condition and is scriptable -- and it is strictly stronger than backgrounding, which on
  this build still ticks (DumperTest logs ~120 ProcessEvent fires/s at `t.MaxFPS 15`). Frozen, the
  count is exactly 0, so "0 fires" is a fact rather than a hope.

WHY A FRESH PROCESS IS REQUIRED, and why the rig relaunches rather than reusing what is running
  The validator is armed ONCE, at hook install. A process whose hook is already installed and
  validated cannot re-enter the window, so reusing the running DumperTest would produce a vacuous
  pass. The order is therefore: launch -> inject -> scan -> **confirm the hook is NOT yet
  installed** -> freeze -> force the install -> wait out the window.

  Forcing the install with `pe_profile_start` (which calls `UE5_EnsureGameThreadHook`) rather than
  with an invoke is deliberate: hook installation is MinHook work on the calling thread and needs
  no game thread, whereas an invoke would block on the thread we just froze.

CONTROLS, none of them assumed:
  * the hook really was absent before the freeze (else the window was never entered);
  * the hook really was installed during it (else "no VALIDATION FAILED" means "nothing happened");
  * the fire count really is 0 across the window (else the guard was never under test);
  * and after the resume the host really does invoke -- `Add_IntInt(3,4) == 7` -- because "the hook
    is KEPT" is only worth anything if the kept hook still works.
"""
import json
import pathlib
import re
import subprocess
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from pipe_client import PipeClient  # noqa: E402

LOG = pathlib.Path.home() / "AppData/Local/UE5CEDumper/Logs/DumperTest"
PY = sys.executable

# Format strings, per the log-verification rule -- never line numbers.
WARN_KEPT = "came from the PATTERN scan"
WARN_KEPT2 = "The hook is KEPT"
ERR_FAILED = "VALIDATION FAILED"
INSTALLED = "hook installed at"


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + "\n")
    # Flush: a backgrounded rig's stdout is a FILE, which Python block-buffers --
    # a long run then shows an EMPTY output file and looks hung.
    sys.stdout.flush()


def run(args, timeout=180):
    return subprocess.run([PY] + args, capture_output=True, text=True,
                          errors="replace", timeout=timeout, cwd=str(HERE.parent.parent))


def logs_since(mark, needle):
    out = []
    for f in sorted(LOG.glob("*-0.log")):
        try:
            for l in f.read_text(encoding="utf-8", errors="replace").splitlines():
                if l.startswith("[") and l[1:20] >= mark and needle in l:
                    out.append(l)
        except OSError:
            pass
    return out


def game_tid():
    r = run([str(HERE / "suspend.py"), "threads", "DumperTest"])
    m = re.search(r"tid=(\d+)\s+cpu=.*main thread", r.stdout)
    return int(m.group(1)) if m else None


def freeze(tid, on):
    run([str(HERE / "suspend.py"), "suspend-tid" if on else "resume-tid",
         "DumperTest", str(tid)])


def diag(c):
    r = c.request("get_diagnostics")
    d = r.get("data", r)
    gt = d.get("game_thread") or {}
    return gt.get("hook_active"), gt.get("hook_fire_count")


def main():
    fails = []
    say("== staging: a FRESH DumperTest, because the validator arms only at install ==")
    subprocess.run(["taskkill", "/F", "/IM", "DumperTest.exe"],
                   capture_output=True, text=True, errors="replace")
    time.sleep(2.0)
    r = run([str(HERE / "launch_dumpertest.py"), "dev"], timeout=240)
    say(r.stdout.strip()[-400:])
    if r.returncode != 0:
        say(r.stderr[-600:])
        say("FAIL: could not launch DumperTest")
        return 1
    time.sleep(2.0)
    ri = run([str(HERE / "inject.py"), "--name", "DumperTest"], timeout=120)
    say(ri.stdout.strip()[-300:])
    if ri.returncode != 0:
        say(ri.stderr[-600:])
        say("FAIL: injection failed")
        return 1

    mark = time.strftime("%Y-%m-%d %H:%M:%S")
    time.sleep(1.1)

    with PipeClient() as c:
        c.assert_build()
        c.ensure_scanned()
        act, fires = diag(c)
        say("")
        say("after scan, BEFORE any invoke: hook_active=%s fire_count=%s" % (act, fires))
        if act:
            fails.append("staging: the hook was ALREADY installed before the freeze -- the "
                         "validator window cannot be re-entered and any pass here is vacuous")
            say("FAIL: " + fails[-1])
            return 1

    tid = game_tid()
    if tid is None:
        say("FAIL: could not identify the UE game thread")
        return 1
    say("freezing the UE game thread (tid %d) -- 'PE traffic stops', stronger than backgrounding"
        % tid)
    freeze(tid, True)
    installed_line = None
    try:
        with PipeClient() as c:
            say("")
            say("== forcing the hook install while the thread is frozen ==")
            pr = c.request("pe_profile_start")
            say("   pe_profile_start -> %s" % json.dumps(pr.get("data", pr))[:200])
            time.sleep(0.5)
            inst = logs_since(mark, INSTALLED)
            installed_line = inst[-1] if inst else None
            say("   '%s' lines: %d" % (INSTALLED, len(inst)))
            for l in inst[-2:]:
                say("      " + l.strip()[:160])
            if not inst:
                fails.append("the hook never installed, so the validator never armed and "
                             "'no VALIDATION FAILED' below would mean nothing")

            act, f0 = diag(c)
            say("   hook_active=%s fire_count=%s   (frozen: this must not move)" % (act, f0))
            say("")
            say("== waiting out the 1500 ms validator window with the thread frozen ==")
            time.sleep(3.5)
            act2, f1 = diag(c)
            say("   after the window: hook_active=%s fire_count=%s" % (act2, f1))
            if f0 is not None and f1 is not None and f1 != f0:
                fails.append("the fire count moved (%s -> %s) while the thread was frozen, so "
                             "the validator did not see 0 and the guard was never under test"
                             % (f0, f1))
            else:
                say("   OK: 0 fires across the window -- the guard really was exercised")

            warn = logs_since(mark, WARN_KEPT)
            kept = logs_since(mark, WARN_KEPT2)
            bad = logs_since(mark, ERR_FAILED)
            say("")
            say("   '%s' : %d   <-- step 5 wants >=1" % (WARN_KEPT, len(warn)))
            for l in warn[:2]:
                say("      " + l.strip()[:230])
            say("   '%s'      : %d" % (WARN_KEPT2, len(kept)))
            say("   '%s'         : %d   <-- MUST be 0" % (ERR_FAILED, len(bad)))
            for l in bad[:2]:
                say("      " + l.strip()[:200])
            if not warn:
                fails.append("no PATTERN-scan WARN -- either the validator did not run or it "
                             "took the version-table branch")
            if not kept:
                fails.append("the WARN did not say the hook is KEPT")
            if bad:
                fails.append("VALIDATION FAILED fired on a PATTERN-detected offset -- this is "
                             "exactly the false positive the asymmetry exists to prevent")
            if warn and act2 is False:
                fails.append("the hook was soft-disabled anyway (hook_active=False) despite the "
                             "WARN saying it is KEPT -- report and reality disagree")
            elif warn and act2:
                say("   OK: hook_active is still True -- reported, not acted on")
    finally:
        freeze(tid, False)
        say("")
        say("resumed the game thread")

    say("")
    say("== the kept hook must still WORK once the game ticks again ==")
    time.sleep(2.5)
    with PipeClient() as c:
        rr = c.request("list_all_functions", limit=20000, game_only=False)
        fl = (rr.get("data", rr)).get("functions") or []
        f = next((x for x in fl if x.get("func_name") == "Add_IntInt"), None)
        if not f:
            fails.append("Add_IntInt not found -- cannot show the kept hook still works")
        else:
            iv = c.request("invoke_function", class_name=f["class_name"], func_name="Add_IntInt",
                           parms_size=12, params_hex="03000000" + "04000000" + "00000000")
            d = iv.get("data", iv)
            h = d.get("params_hex") or d.get("result_hex") or ""
            val = int.from_bytes(bytes.fromhex(h[16:24]), "little") if len(h) >= 24 else None
            say("   Add_IntInt(3,4) = %s   <-- must be 7" % val)
            if val != 7:
                fails.append("the KEPT hook does not work after the resume (got %r) -- keeping a "
                             "broken hook is not better than disabling it" % val)
        act3, f3 = diag(c)
        say("   hook_active=%s fire_count=%s (ticking again)" % (act3, f3))

    say("")
    for x in fails:
        say("FAIL: %s" % x)
    if not fails:
        say("PASS (PEHOOK step 5)")
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
