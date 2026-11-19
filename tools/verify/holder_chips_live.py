"""Live check of the Teleport tab's holder chips: hold features as other programs while the UE5DumpUI
window is connected, so its cards can be looked at, then let go.

[MCP-WRITE-GRANTS-2026-11-14] §4.10. The window labels a gameplay card from get_holds every 2 s. This rig plays the
holders it names, with no AI client and no prompt:
  - a BACKGROUND client ("background": true on every request, as the MCP server sends) -- the window says "Held by an
    AI session (pid N)";
  - a FOREGROUND client of another process -- "Held by another program (pid N)".
They are child processes of this rig, because the DLL's owner is the client PROCESS: two connections of one process
are one owner, and neither may be the window's.

    py tools/verify/holder_chips_live.py [--pid N] [--seconds 60]

What it does, printing get_holds after each step (exit 0 when every step answered as expected):
  1. the background client turns god mode on and sets world time dilation 0.5 and walk speed x1.5 (all if_free);
  2. the foreground client sets jump x1.2 (if_free);
  3. both hold for --seconds: look at the Teleport tab now (God Mode, World time, Move Speed: an AI session; Super
     Jump: another program);
  4. the background client leaves, and the DLL releases what it held (the cards' chips go, their badges follow);
  5. the foreground client resets its jump and leaves.
A step the window took over in the meantime (its OFF or Reset) is reported, not failed: that is the chip's promise.

Needs a game running a DLL that answers get_holds ([PIPE-PER-PROCESS-2026-11-06]); DumperTest Shipping is the fixture.

    py tools/verify/holder_chips_live.py --experimental [--pid N] [--seconds 60]

The verification register's holder-labels row 3: the experimental cards. With the UI's experimental switch on, the
background client turns See-through on (seethrough_set) and takes Fly and Keep Foreground (if_free); seethrough_get_state
must name its claim ("you") and get_holds must name it for fly and foreground_lock. Look at the Teleport tab while they
hold: See-through reads "Held by another program or AI session" (its state names no pid), Fly and Keep Foreground "Held
by an AI session (pid N)". It then leaves, and the DLL releases all three.
"""

from __future__ import annotations

import argparse
import json
import os
import pathlib
import subprocess
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).parent))
from pipe_client import PipeClient  # noqa: E402

BACKGROUND_HOLDS = ("god_mode", "time:global", "movement:walk_speed")
FOREGROUND_HOLDS = ("movement:jump",)
EXPERIMENTAL_HOLDS = ("fly", "foreground_lock")


def run_child(pid: int | None, background: bool) -> int:
    """One client: reads commands on stdin (one JSON per line), answers on stdout, until EOF."""
    c = PipeClient(pid=pid, timeout=60)
    c.connect()
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        msg = json.loads(line)
        cmd = msg.pop("cmd")
        if background:
            msg["background"] = True
        reply = c.request(cmd, **msg)
        sys.stdout.write(json.dumps(reply) + "\n")
        sys.stdout.flush()
    c.close()
    return 0


class Child:
    def __init__(self, pid: int | None, background: bool):
        args = [sys.executable, __file__, "--child", "background" if background else "foreground"]
        if pid:
            args += ["--pid", str(pid)]
        self.proc = subprocess.Popen(args, stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True, bufsize=1)

    @property
    def pid(self) -> int:
        return self.proc.pid

    def request(self, cmd: str, **params) -> dict:
        self.proc.stdin.write(json.dumps({"cmd": cmd, **params}) + "\n")
        self.proc.stdin.flush()
        return json.loads(self.proc.stdout.readline())

    def leave(self) -> int | None:
        self.proc.stdin.close()
        try:
            return self.proc.wait(30)
        except subprocess.TimeoutExpired:
            self.proc.kill()
            return None


def holds_of(client) -> dict:
    r = client.request("get_holds")
    return {h["hold"]: h for h in r.get("holds", [])} if r.get("ok") else {}


def show(title: str, holds: dict, names) -> None:
    print(f"  {title}")
    for name in names:
        h = holds.get(name, {})
        who = h.get("holder", {})
        print(f"    {name:22} active={h.get('active')} pending={h.get('pending')} holder={who.get('kind')}"
              f" pid={who.get('pid')} background={who.get('background')}")


def see_through(client) -> dict:
    r = client.request("seethrough_get_state")
    return {k: r.get(k) for k in ("active", "claims", "you", "ce_claimed")}


def run_experimental(pid: int | None, seconds: float) -> int:
    """Row 3: the background client holds the three experimental features while the window is looked at."""
    ok = True
    probe = PipeClient(pid=pid, timeout=60)
    probe.connect()
    ptrs = probe.request("get_pointers")
    game = ptrs.get("pid")
    probe.close()
    print(f"game pid {game}, DLL build {ptrs.get('build_number')}; rig pid {os.getpid()}")
    ai = Child(game, background=True)
    print(f"  AI-session stand-in pid {ai.pid}")
    try:
        took = {
            "see-through": ai.request("seethrough_set", enable=True),
            "fly": ai.request("fly_set", enable=True, if_free=True),
            "foreground_lock": ai.request("set_foreground_lock", enable=True, if_free=True),
        }
        for name, reply in took.items():
            print(f"  took {name}: {reply}")
            if not reply.get("ok") or (reply.get("code") or 0) < 0:
                ok = False
                print(f"  FAIL  {name}: {reply}")
        st = see_through(ai)
        print(f"  seethrough_get_state: {st}")
        if not (st.get("active") and st.get("you") and (st.get("claims") or 0) >= 1):
            ok = False
            print("  FAIL  See-through is not on with the background client's claim")
        holds = holds_of(ai)
        show("taken:", holds, EXPERIMENTAL_HOLDS)
        for name in EXPERIMENTAL_HOLDS:
            h = holds.get(name, {})
            if not (h.get("active") and h.get("holder", {}).get("you") and h["holder"].get("background")):
                ok = False
                print(f"  FAIL  {name} is not the background client's")
        print(f"  holding for {seconds:.0f} s: look at the Teleport tab now", flush=True)
        time.sleep(seconds)
        show("before letting go:", holds_of(ai), EXPERIMENTAL_HOLDS)
        print(f"  seethrough_get_state: {see_through(ai)}")
    finally:
        code = ai.leave()
        print(f"  the background client left (exit {code})")
    probe = PipeClient(pid=game, timeout=60)
    probe.connect()
    try:
        holds, st = {}, {}
        for _ in range(50):
            holds, st = holds_of(probe), see_through(probe)
            if not any(holds.get(n, {}).get("active") and holds[n].get("holder", {}).get("pid") == ai.pid
                       for n in EXPERIMENTAL_HOLDS) and not st.get("active"):
                break
            time.sleep(0.1)
        show("after the background client left:", holds, EXPERIMENTAL_HOLDS)
        print(f"  seethrough_get_state: {st}")
        for name in EXPERIMENTAL_HOLDS:
            h = holds.get(name, {})
            if h.get("active") and h.get("holder", {}).get("pid") == ai.pid:
                ok = False
                print(f"  FAIL  {name} still held by the background client after it left")
        if st.get("active") and not st.get("ce_claimed") and (st.get("claims") or 0) == 0:
            ok = False
            print("  FAIL  See-through stayed on with no claim left")
    finally:
        probe.close()
    print("PASS" if ok else "FAIL")
    return 0 if ok else 1


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--pid", type=int)
    ap.add_argument("--seconds", type=float, default=60.0, help="how long both hold (default 60)")
    ap.add_argument("--child", choices=("background", "foreground"), help=argparse.SUPPRESS)
    ap.add_argument("--experimental", action="store_true",
                    help="hold See-through, Fly and Keep Foreground instead (register row 3)")
    a = ap.parse_args()
    if a.child:
        return run_child(a.pid, a.child == "background")
    if a.experimental:
        return run_experimental(a.pid, a.seconds)

    ok = True
    probe = PipeClient(pid=a.pid, timeout=60)
    probe.connect()
    ptrs = probe.request("get_pointers")
    game = ptrs.get("pid")
    probe.close()   # the window needs the pipe's instances more than this rig does
    print(f"game pid {game}, DLL build {ptrs.get('build_number')}; rig pid {os.getpid()}")
    names = BACKGROUND_HOLDS + FOREGROUND_HOLDS

    ai = Child(game, background=True)
    other = Child(game, background=False)
    print(f"  AI-session stand-in pid {ai.pid}; other-program stand-in pid {other.pid}")
    try:
        took = {
            "god_mode": ai.request("set_god_mode", enable=True, if_free=True),
            "time:global": ai.request("set_time_dilation", target="global", value=0.5, if_free=True),
            "movement:walk_speed": ai.request("set_movement_multiplier", knob="walk_speed", multiplier=1.5,
                                              if_free=True),
            "movement:jump": other.request("set_movement_multiplier", knob="jump", multiplier=1.2, if_free=True),
        }
        for name, reply in took.items():
            if not reply.get("ok") or reply.get("code", 0) < 0:
                ok = False
                print(f"  FAIL  {name}: {reply}")
        holds = holds_of(ai)
        show("taken:", holds, names)
        for name in BACKGROUND_HOLDS:
            h = holds.get(name, {})
            if not (h.get("active") and h.get("holder", {}).get("you") and h["holder"].get("background")):
                ok = False
                print(f"  FAIL  {name} is not the background client's")
        h = holds_of(other).get("movement:jump", {})
        if not (h.get("active") and h.get("holder", {}).get("you") and not h["holder"].get("background")):
            ok = False
            print("  FAIL  movement:jump is not the foreground client's")

        print(f"  holding for {a.seconds:.0f} s: look at the Teleport tab now", flush=True)
        time.sleep(a.seconds)
        show("before letting go:", holds_of(ai), names)
    finally:
        code = ai.leave()
        print(f"  the background client left (exit {code})")

    # Its holds are released on the DLL's teardown of that connection, a moment after the process exits.
    holds = {}
    for _ in range(50):
        holds = holds_of(other)
        if not any(holds.get(n, {}).get("active") and holds[n].get("holder", {}).get("pid") == ai.pid
                   for n in BACKGROUND_HOLDS):
            break
        time.sleep(0.1)
    show("after the background client left:", holds, names)
    for name in BACKGROUND_HOLDS:
        h = holds.get(name, {})
        if h.get("active") and h.get("holder", {}).get("pid") == ai.pid:
            ok = False
            print(f"  FAIL  {name} still held by the background client after it left")
        elif h.get("active"):
            print(f"  note  {name} is held by someone else now (taken over): {h.get('holder')}")

    jump = holds.get("movement:jump", {})
    if jump.get("holder", {}).get("you") is False and jump.get("active"):
        print(f"  note  movement:jump was taken over: {jump.get('holder')}")
    else:
        reset = other.request("reset_movement", knob="jump", if_free=True)
        print(f"  the foreground client reset its jump: {reset}")
    print(f"  the foreground client left (exit {other.leave()})")
    print("PASS" if ok else "FAIL")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
