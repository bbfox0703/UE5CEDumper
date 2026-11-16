"""Live check (build 3716+): who the DLL records for the debug camera when it is toggled by an invoke of its own
functions -- the UI's Console panel Run (pipe invoke_function) and a CE record (mailbox INVOKE_BY_NAME).

[MCP-WRITE-GRANTS-2026-11-14], the holder labels' review rounds 1-3. get_holds reports the camera's REAL state beside
the ledger's owner, so a toggle the ledger missed left the camera named for whoever held it before. Three clients,
none of them a real UI or CE:
  - this rig's own pipe client, a FOREGROUND client (the UE5DumpUI window's stand-in);
  - a BACKGROUND child process (an AI session's MCP server: "background": true on every request);
  - the CE mailbox, written with WriteProcessMemory (mailbox_poke.py's way; no Cheat Engine).

    py tools/verify/dbgcam_ledger_live.py [--pid N]

Needs a fixture where ToggleDebugCamera turns the camera both ways: DumperTest58 Development (UE 5.8). Below 5.8 the
player's CheatManager's OFF is a no-op the DLL's set_debug_camera finishes with a controller swap, which a raw invoke
does not do (Frieren.cpp [DBGCAM-58-REENABLE]); DumperTest Shipping has no live CheatManager at all.

Checks (each printed ok / FAIL; exit 0 only when all pass):
  1. the background client's invoke of ToggleDebugCamera is refused by name, pointing at write_debug_camera, and
     changes nothing (review 2, H: it names no hold, so the if_free check does not answer first);
  2. the foreground client's invoke turns the camera on, and it is that client's;
  3. its invoke again turns it off, and still names it (review 2, F: an invoke's toggle may be queued);
  4. the AI turns the camera on (set_debug_camera, if_free); the foreground client's invoke turns it off and on
     again: it is the foreground client's, and the AI's leaving does NOT turn it off (the failure fix (6) closed);
  5. the mailbox's INVOKE_BY_NAME of ToggleDebugCamera names CE, off and on (review 2, E);
  6. clean-up: the foreground client's set_debug_camera OFF leaves the camera off and nobody's.

    py tools/verify/dbgcam_ledger_live.py --busy-lock --suspend-name DumperTest58 [--pid N]

The verification register's "cards' holder labels" row 6, alone: with the game thread frozen (suspend.py, per
thread), a second foreground client's set_debug_camera ON holds the camera's lock through its own game-thread wait;
the mailbox's INVOKE_BY_NAME of ToggleDebugCamera, sent while it waits, must not wait for that lock (Mimic's
TryChangeHoldAsCe): it answers within ONE invoke timeout, not two, and the DLL's pipe log reads "ToggleDebugCamera
ran unrecorded -- another writer was changing the debug camera". Resumed, the queued toggles run; the clean-up turns
the camera off.
"""

from __future__ import annotations

import argparse
import os
import pathlib
import struct
import sys
import threading
import time

sys.path.insert(0, str(pathlib.Path(__file__).parent))
from holder_chips_live import Child  # noqa: E402
from mailbox_poke import (INIT_READY, OFF_CMD, OFF_ERRORMSG, OFF_INITSTATE, OFF_INSTANCE, OFF_PARAMS,  # noqa: E402
                          OFF_RESULT, OFF_STATUS, OFF_UFUNC, STATUS_DONE, STATUS_IDLE, STATUS_PROCESSING, Mem,
                          mailbox_addr)
from pipe_client import PipeClient  # noqa: E402

CMD_IDLE, CMD_INVOKE_BY_NAME = 0, 4
OFF_CLASSNAME, OFF_FUNCNAME = 0x028, 0x128   # Mimic.h MailboxData, #pragma pack(1)
CAMERA = "debug_camera"


class Checks:
    def __init__(self):
        self.failures: list[str] = []
        self.passed = 0

    def check(self, ok: bool, what: str, detail: object = "") -> bool:
        if ok:
            self.passed += 1
            print(f"  ok    {what}")
        else:
            self.failures.append(what)
            print(f"  FAIL  {what}: {detail}")
        return ok


def camera_hold(client) -> dict:
    r = client.request("get_holds")
    for h in r.get("holds", []):
        if h.get("hold") == CAMERA:
            return h
    return {}


def camera_state(client) -> int:
    return client.request("get_debug_camera_state").get("state", -1)


def wait_state(client, want: int, seconds: float = 3.0) -> int:
    end = time.time() + seconds
    state = camera_state(client)
    while state != want and time.time() < end:
        time.sleep(0.1)
        state = camera_state(client)
    return state


def toggle(client, **extra) -> dict:
    return client.request("invoke_function", class_name="CheatManager", func_name="ToggleDebugCamera",
                          parms_size=0, **extra)


def mailbox_toggle(m: Mem, base: int, timeout: float = 15.0) -> tuple[int, str]:
    """INVOKE_BY_NAME CheatManager::ToggleDebugCamera, inputs first and cmd last, as a CE record does."""
    if m.i32(base + OFF_STATUS) == STATUS_PROCESSING:
        raise SystemExit("dbgcam_ledger_live: the mailbox is PROCESSING another command")
    m.write(base + OFF_STATUS, struct.pack("<i", STATUS_IDLE))
    m.write(base + OFF_INSTANCE, struct.pack("<Q", 0))   # find the instance by class name
    m.write(base + OFF_UFUNC, struct.pack("<Q", 0))
    m.write(base + OFF_CLASSNAME, b"CheatManager".ljust(256, b"\x00"))
    m.write(base + OFF_FUNCNAME, b"ToggleDebugCamera".ljust(256, b"\x00"))
    m.write(base + OFF_PARAMS, b"\x00" * 64)
    m.write(base + OFF_RESULT, struct.pack("<i", 0x7FFFFFFF))
    m.write(base + OFF_CMD, struct.pack("<i", CMD_INVOKE_BY_NAME))
    t0 = time.time()
    while time.time() - t0 < timeout:
        if m.i32(base + OFF_STATUS) == STATUS_DONE:
            res = m.i32(base + OFF_RESULT)
            err = m.read(base + OFF_ERRORMSG, 256).split(b"\x00")[0].decode("utf-8", "replace")
            m.write(base + OFF_CMD, struct.pack("<i", CMD_IDLE))
            return res, err
        time.sleep(0.01)
    raise SystemExit(f"dbgcam_ledger_live: mailbox TIMEOUT after {timeout}s, status={m.i32(base + OFF_STATUS):#x}")


def busy_lock(ui, game: int, name: str, ck: Checks) -> None:
    """Row 6: a CE invoke of the camera's function behind a pipe writer that holds the camera's lock."""
    from mcp_client import _camera_force_off, _fresh_stamp, _FrozenGameThread, _log_line  # noqa: E402

    t = int(ui.request("get_pointers").get("invoke_timeout_ms") or 5000) / 1000.0
    base = mailbox_addr(str(game))
    m = Mem(game)
    if not ck.check(m.i32(base + OFF_INITSTATE) == INIT_READY, f"the mailbox is ready; invoke timeout {t:.0f}s"):
        return
    writer = PipeClient(pid=game, timeout=t * 4 + 30)   # the pipe writer: a second foreground connection
    writer.connect()
    answer: dict = {}
    since = _fresh_stamp()
    try:
        with _FrozenGameThread(name) as frozen:
            print(f"        {frozen.suspend_line}")
            th = threading.Thread(target=lambda: answer.update(r=writer.request("set_debug_camera", enable=True)),
                                  daemon=True)
            th.start()
            time.sleep(min(1.0, t / 4))   # inside its game-thread wait now, holding the camera's lock
            ck.check(th.is_alive(), "the pipe writer's set_debug_camera ON is waiting on the frozen game thread")
            t0 = time.time()
            res, err = mailbox_toggle(m, base, timeout=t * 3 + 10)
            took = time.time() - t0
            ck.check(took < t * 1.5,
                     f"the mailbox's INVOKE_BY_NAME answered in {took:.1f}s: one invoke timeout ({t:.0f}s), not two",
                     (res, err))
            th.join(t * 3 + 10)
            ck.check("r" in answer, "the pipe writer's set_debug_camera answered", answer)
        print(f"        resumed; the writer answered {answer.get('r')}; the mailbox answered {res}, {err!r}")
        line = _log_line("pipe", r"Mailbox: ToggleDebugCamera ran unrecorded -- another writer was changing the debug "
                                 r"camera", since)
        ck.check(line is not None,
                 "the DLL's pipe log: ToggleDebugCamera ran unrecorded -- another writer was changing the debug camera",
                 line)
    finally:
        writer.close()
        time.sleep(2.0)   # the queued toggles run once the game thread is back
        off = _camera_force_off(ui)
        ck.check(off == 0, "clean-up: the camera is off", off)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--pid", type=int)
    ap.add_argument("--busy-lock", action="store_true", help="run the busy-lock check (register row 6) alone")
    ap.add_argument("--suspend-name", help="with --busy-lock: the game's image stem, for suspend.py (DumperTest58)")
    a = ap.parse_args()
    if a.busy_lock and not a.suspend_name:
        ap.error("--busy-lock needs --suspend-name")

    ck = Checks()
    ui = PipeClient(pid=a.pid, timeout=60)
    ui.connect()
    ptrs = ui.request("get_pointers")
    game = ptrs.get("pid")
    me = os.getpid()
    print(f"game pid {game}, DLL build {ptrs.get('build_number')}, objects {ptrs.get('object_count')}; "
          f"foreground (window stand-in) pid {me}")
    if (ptrs.get("build_number") or 0) < 3716:
        print("  the game's DLL predates build 3716")
        return 1
    state = camera_state(ui)
    if state == 1:
        ui.request("set_debug_camera", enable=False)
        state = wait_state(ui, 0)
    if not ck.check(state == 0, "the camera reads OFF to start", state):
        print("  (state -1: no live CheatManager -- use DumperTest58 Development)")
        return 1
    if a.busy_lock:
        busy_lock(ui, game, a.suspend_name, ck)
        ui.close()
        print(f"{ck.passed} passed, {len(ck.failures)} failed")
        print("PASS" if not ck.failures else "FAIL")
        return 0 if not ck.failures else 1

    ai = Child(game, background=True)
    print(f"  AI-session stand-in pid {ai.pid}")
    try:
        # 1. The AI's invoke is refused by name, before the hold check could answer.
        r = toggle(ai, if_free=True)
        ck.check(not r.get("ok") and "write_debug_camera" in (r.get("error") or "") and r.get("code") != -20,
                 "the AI's invoke of ToggleDebugCamera is refused by name, pointing at write_debug_camera", r)
        ck.check(camera_state(ui) == 0, "and the camera is untouched")

        # 2. The window's invoke turns it on: its own.
        r = toggle(ui)
        on = wait_state(ui, 1)
        h = camera_hold(ui)
        ck.check(on == 1 and h.get("active") and h.get("holder", {}).get("you"),
                 "the window's Console-style invoke turns the camera on, and it is the window's", (r, on, h))

        # 3. Off again by the same invoke: still named (an off hold reads as free).
        r = toggle(ui)
        off = wait_state(ui, 0)
        h = camera_hold(ui)
        ck.check(off == 0 and not h.get("active") and h.get("holder", {}).get("you"),
                 "its invoke turns it off, and still names the window", (r, off, h))

        # 4. The AI holds it; the window's invokes take it over; the AI's leaving leaves it on.
        r = ai.request("set_debug_camera", enable=True, if_free=True)
        on = wait_state(ui, 1)
        h = camera_hold(ui)
        ck.check(on == 1 and h.get("holder", {}).get("background") and h["holder"].get("pid") == ai.pid,
                 "the AI's write_debug_camera turns it on, and it is the AI's", (r, on, h))
        toggle(ui)
        off = wait_state(ui, 0)
        toggle(ui)
        on = wait_state(ui, 1)
        h = camera_hold(ui)
        ck.check(off == 0 and on == 1 and h.get("holder", {}).get("you"),
                 "the window's invoke off and on again: the camera is the window's now", (off, on, h))
    finally:
        code = ai.leave()
    ck.check(code == 0, "the AI session left", code)
    time.sleep(2.0)   # the DLL releases a leaving background client's holds on its connection's teardown
    h = camera_hold(ui)
    ck.check(camera_state(ui) == 1 and h.get("holder", {}).get("you"),
             "the AI's leaving did not turn the window's camera off", h)

    # 5. A CE record's INVOKE_BY_NAME names CE, off and on.
    base = mailbox_addr(str(game))
    m = Mem(game)
    if ck.check(m.i32(base + OFF_INITSTATE) == INIT_READY, "the mailbox is ready"):
        res, err = mailbox_toggle(m, base)
        off = wait_state(ui, 0)
        h = camera_hold(ui)
        ck.check(res == 0 and off == 0 and h.get("holder", {}).get("kind") == "ce",
                 "the mailbox's INVOKE_BY_NAME turns it off, and names CE", (res, err, off, h))
        res, err = mailbox_toggle(m, base)
        on = wait_state(ui, 1)
        h = camera_hold(ui)
        ck.check(res == 0 and on == 1 and h.get("active") and h.get("holder", {}).get("kind") == "ce",
                 "and on again, CE's", (res, err, on, h))

    # 6. Clean-up.
    ui.request("set_debug_camera", enable=False)
    off = wait_state(ui, 0)
    h = camera_hold(ui)
    ck.check(off == 0 and not h.get("active") and h.get("holder", {}).get("kind") == "none",
             "clean-up: the camera is off and nobody's", (off, h))
    ui.close()

    print(f"{ck.passed} passed, {len(ck.failures)} failed")
    print("PASS" if not ck.failures else "FAIL")
    return 0 if not ck.failures else 1


if __name__ == "__main__":
    sys.exit(main())
