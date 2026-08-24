r"""M1 / M2 / M3 — the See-through RESTORE SET must survive a hostile disable.

    py tools/verify/seethrough_restoreset.py

THE ROW. "Enable See-Through, then (a) toggle off during motion, (b) toggle off while the
game is paused/stalled, (c) yank the UI connection and (d) close the game -- in all four
every hidden actor must become visible again. A single actor left invisible is the
failure, and it is only visible on screen."

⭐ "ONLY VISIBLE ON SCREEN" IS OUT OF DATE, WHICH IS WHY THIS CAN RUN HEADLESS.
`seethrough_get_state` returns `hidden_actors` -- the ADDRESSES, not just the tally
(dll/src/Fern.cpp) -- so each actor's own `bHidden` bit can be read straight out of the
process with ReadProcessMemory. That is an independent witness: it is not the hider's
count, and it is not anything the DLL computed for us. On this build the field is
`AActor::bHidden`, offset +88, bit mask 0x80, resolved at runtime rather than hardcoded.

WHY THE ADDRESS SET IS CAPTURED BEFORE THE DISABLE. The worker re-picks occluders every
tick, so "hidden_actors is empty afterwards" is worthless -- an empty list is what you get
when the worker simply stopped choosing, whether or not it un-hid anything. The rig pins
the exact actors that were hidden at the moment of the disable and re-reads THOSE.

THE ARMS
  (a) disable DURING MOTION       -- the disable<->Tick race: a tick that re-populates
                                     hiddenActors after the disable took its snapshot
                                     leaves those actors hidden forever.
  (b) disable while the game thread is SUSPENDED -- the un-hide cannot run on a stalled
                                     game thread; the restore set must not be discarded.
  (c) YANK the connection          -- an abrupt socket close, not a clean teardown: the
                                     DLL disables See-through when it notices the drop.
  (d) close the game               -- ⛔ NOT RUNNABLE, and it is not a scheduling problem:
                                     after the process exits there is no memory to read,
                                     and the actors' visibility has no meaning. Recorded
                                     as structurally unobservable rather than skipped.

Each arm gets a NEGATIVE CONTROL first: with See-through still enabled and nothing
disabled, the same bits must stay SET. Without it, "the bit is clear" is equally well
explained by the hide never having happened.
"""
import ctypes
import ctypes.wintypes as w
import pathlib
import subprocess
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient  # noqa: E402

k32 = ctypes.WinDLL("kernel32", use_last_error=True)
k32.OpenProcess.restype = w.HANDLE
k32.ReadProcessMemory.argtypes = [w.HANDLE, w.LPCVOID, w.LPVOID,
                                  ctypes.c_size_t, ctypes.POINTER(ctypes.c_size_t)]
PROC = 0x0010 | 0x0400
PIERCE = 2


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + "\n")
    sys.stdout.flush()


def pid_of(name):
    out = subprocess.run(["tasklist", "/FO", "CSV", "/NH"],
                         capture_output=True, text=True, errors="replace").stdout
    for line in out.splitlines():
        p = [x.strip('"') for x in line.split('","')]
        if len(p) >= 2 and name.lower() in p[0].lower():
            return int(p[1])
    return None


def rb(h, addr):
    buf = (ctypes.c_ubyte * 1)()
    got = ctypes.c_size_t(0)
    if not k32.ReadProcessMemory(h, ctypes.c_void_p(addr), buf, 1, ctypes.byref(got)):
        return None
    return buf[0] if got.value == 1 else None


def hidden_bits(h, addrs, off, mask):
    """[(addr, is_hidden)] read from the process, not from the DLL's own report."""
    out = []
    for a in addrs:
        b = rb(h, a + off)
        out.append((a, None if b is None else bool(b & mask)))
    return out


def relaunch():
    """Every arm gets a FRESH GAME, and that is not belt-and-braces.

    ⚠ Measured while building this rig: a fresh DumperTest hides an occluder within a
    second, but after arm (a)'s 6x600-unit move NOTHING is hideable any more -- the camera
    faces open space. So arms (b) and (c) reported "nothing was ever hidden" and failed for
    want of a SUBJECT, which reads exactly like a defect and is really a spent fixture.

    ⚠ And recalling a saved marker does NOT fix it: `teleport_save_marker` then
    `teleport_recall_marker` returns `code 0, tier 1` -- a clean success -- and the view is
    STILL not hideable. The marker restores where the pawn stands, not what the camera is
    looking at. Relaunching is the only state reset that is actually known to work.
    """
    root = pathlib.Path(__file__).resolve().parent
    subprocess.run(["taskkill", "/F", "/IM", "DumperTest.exe"], capture_output=True, text=True)
    time.sleep(2)
    subprocess.run([sys.executable, str(root / "launch_dumpertest.py"), "dev"],
                   capture_output=True, text=True)
    time.sleep(5)
    pid = pid_of("DumperTest")
    subprocess.run([sys.executable, str(root / "inject.py"), "--pid", str(pid)],
                   capture_output=True, text=True)
    time.sleep(2)
    h = k32.OpenProcess(PROC, False, pid)
    return pid, h


def arm(c, h, off, mask, name, disturb, fails):
    """Enable, wait for a hide, control, run `disturb`, then require every captured
    actor to be visible again."""
    say("")
    say("=== arm %s ===" % name)
    c.request("seethrough_set", enable=False)
    time.sleep(0.5)
    c.request("seethrough_set", enable=True, count=PIERCE)
    addrs = []
    for _ in range(20):
        time.sleep(0.4)
        st = c.request("seethrough_get_state")
        addrs = [int(x, 16) if isinstance(x, str) else int(x)
                 for x in (st.get("hidden_actors") or [])]
        if addrs:
            break
    if not addrs:
        fails.append("arm %s: nothing was ever hidden, so the arm has no subject" % name)
        say("  no hidden actor appeared -- arm skipped")
        return
    say("  hidden: %s" % ", ".join(hex(a) for a in addrs))

    pre = hidden_bits(h, addrs, off, mask)
    say("  positive control (bHidden read from the process): %s"
        % ", ".join("%s=%s" % (hex(a), v) for a, v in pre))
    if not all(v for _, v in pre):
        fails.append("arm %s: an actor the DLL reports as hidden does not have its own "
                     "bHidden bit set -- the witness disagrees with the report" % name)
        return

    # NEGATIVE CONTROL: leave it alone for as long as the disturbance will take. If the
    # bits clear on their own, nothing the arm does afterwards can be attributed to it.
    time.sleep(2.0)
    still = hidden_bits(h, addrs, off, mask)
    if not any(v for _, v in still):
        fails.append("arm %s: the bits cleared with NO disable at all -- the hide simply "
                     "lapsed, so this arm cannot attribute anything" % name)
        say("  negative control FAILED: bits cleared on their own")
        return
    say("  negative control: still hidden after 2 s of doing nothing -- good")

    disturb(c, addrs)

    time.sleep(1.5)
    post = hidden_bits(h, addrs, off, mask)
    say("  after: %s" % ", ".join("%s=%s" % (hex(a), v) for a, v in post))
    left = [hex(a) for a, v in post if v]
    if left:
        fails.append("arm %s: still invisible after the disable: %s" % (name, ", ".join(left)))
    else:
        say("  OK: every captured actor is visible again")


def resolve_field(c):
    row = [x for x in c.request("search_properties", query="bHidden", limit=10)
           .get("results") or [] if x.get("prop_name") == "bHidden"]
    if not row:
        raise SystemExit("restoreset: bHidden not found")
    return int(row[0]["prop_offset"]), int(row[0]["bool_mask"]), row[0]["defining_class_name"]


def main():
    fails = []
    root = pathlib.Path(__file__).resolve().parent

    # ---- arm (a): disable DURING MOTION -------------------------------
    pid, h = relaunch()
    with PipeClient().connect() as c:
        off, mask, cls = resolve_field(c)
        say("DLL build %s   pid %d   witness %s.bHidden +%d mask 0x%02X"
            % (c.assert_build(), pid, cls, off, mask))

        def during_motion(cc, _addrs):
            say("  moving, then disabling mid-motion ...")
            for i in range(6):
                cc.request("teleport_relative", distance=600.0)
                if i == 2:
                    cc.request("seethrough_set", enable=False)
                    say("    <- seethrough_set(False) issued mid-loop")
                time.sleep(0.15)
        arm(c, h, off, mask, "(a) disable during motion", during_motion, fails)

    # ---- arm (b): disable while the game thread is SUSPENDED ----------
    pid, h = relaunch()
    with PipeClient().connect() as c:
        def while_stalled(cc, _addrs):
            out = subprocess.run([sys.executable, str(root / "suspend.py"),
                                  "threads", "DumperTest"],
                                 capture_output=True, text=True, errors="replace").stdout
            tid = None
            for line in out.splitlines():
                if "main thread" in line and "tid=" in line:
                    tid = line.split("tid=", 1)[1].split()[0].strip(",")
            if not tid:
                fails.append("arm (b): could not identify the game thread")
                return
            subprocess.run([sys.executable, str(root / "suspend.py"),
                            "suspend-tid", "DumperTest", tid], capture_output=True, text=True)
            say("    game thread %s SUSPENDED; disabling now" % tid)
            try:
                cc.request("seethrough_set", enable=False)
            finally:
                time.sleep(0.8)
                subprocess.run([sys.executable, str(root / "suspend.py"),
                                "resume-tid", "DumperTest", tid],
                               capture_output=True, text=True)
                say("    game thread resumed")
        arm(c, h, off, mask, "(b) disable while the game thread is stalled",
            while_stalled, fails)

    # ---- arm (c): YANK the connection -------------------------------------
    pid, h = relaunch()
    say("")
    say("=== arm (c) yank the connection ===")
    c2 = PipeClient().connect()
    c2.request("seethrough_set", enable=False)
    time.sleep(0.4)
    c2.request("seethrough_set", enable=True, count=PIERCE)
    addrs = []
    for _ in range(20):
        time.sleep(0.4)
        st = c2.request("seethrough_get_state")
        addrs = [int(x, 16) if isinstance(x, str) else int(x)
                 for x in (st.get("hidden_actors") or [])]
        if addrs:
            break
    if not addrs:
        fails.append("arm (c): nothing was hidden, so the arm has no subject")
    else:
        say("  hidden: %s" % ", ".join(hex(a) for a in addrs))
        pre = hidden_bits(h, addrs, off, mask)
        if not all(v for _, v in pre):
            fails.append("arm (c): positive control failed -- not actually hidden")
        else:
            say("  positive control: hidden per their own bHidden bit")
            time.sleep(2.0)
            if not any(v for _, v in hidden_bits(h, addrs, off, mask)):
                fails.append("arm (c): the bits cleared with the connection still OPEN -- "
                             "the hide lapsed on its own, so the yank proves nothing")
            else:
                say("  negative control: still hidden after 2 s with the socket open")
                # ABRUPT close, not __exit__. The clean teardown is the path that already
                # works; the interesting one is the monitor noticing a dropped socket.
                try:
                    c2._f.close()
                except Exception:
                    c2.close()
                say("  connection YANKED (abrupt handle close)")
                time.sleep(3.5)
                post = hidden_bits(h, addrs, off, mask)
                say("  after: %s" % ", ".join("%s=%s" % (hex(a), v) for a, v in post))
                left = [hex(a) for a, v in post if v]
                if left:
                    fails.append("arm (c): still invisible after the connection dropped: %s"
                                 % ", ".join(left))
                else:
                    say("  OK: the disconnect restored every captured actor")

    say("")
    say("=== arm (d) close the game ===")
    say("  ⛔ NOT RUNNABLE, and not for want of scheduling: once the process exits there is")
    say("  no memory to read and 'is this actor visible' has no referent. Any rig that")
    say("  claimed a pass here would be asserting something unobservable.")

    subprocess.run(["taskkill", "/F", "/IM", "DumperTest.exe"], capture_output=True, text=True)
    say("")
    if fails:
        say("FAIL (%d)" % len(fails))
        for f in fails:
            say("  - %s" % f)
        return 1
    say("PASS -- arms (a), (b) and (c) all restored every captured actor, each against a "
        "negative control that showed the hide does not lapse on its own")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
