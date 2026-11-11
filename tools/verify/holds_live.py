"""Stage D2-1 live check: the DLL records who holds each gameplay hold, refuses an if_free change to another's, and ends
a background client's holds when it leaves.

[PIPE-PER-PROCESS-2026-11-06] Two pipe clients on one game, both from this process's view separate owners: a
FOREGROUND one standing in for the UE5DumpUI window (its requests carry no "background") and a BACKGROUND one standing
in for the MCP server ("background": true on every request). They are two processes -- the background client runs as a
child (`--child`) -- because the DLL's owner is the client PROCESS: two connections of one process are one owner.

    py tools/verify/holds_live.py [--pid N]

Checks, in order (each printed ok / FAIL; exit 0 only when all pass):
  1. get_holds answers, and lists the fixed holds;
  2. the foreground client turns god mode on: get_holds names it (kind client, its pid, not background);
  3. the background client's if_free god-mode OFF is refused with code -20 and that holder -- and god mode stays on;
  4. the background client sets world time dilation (if_free): it is its own ("you"), marked background;
  5. the foreground client's if_free change to that lane is refused in turn (the rule is symmetric);
  6. pipe_clients marks the background client's connection;
  7. the background client leaves: its time lane is released (inactive, nobody's), the foreground client's god mode
     is untouched;
  8. clean-up: the foreground client turns god mode off, and nothing is left held.

Needs a game running a DLL of build 3704 or later (get_holds); DumperTest Shipping is the fixture.
"""

from __future__ import annotations

import argparse
import json
import pathlib
import subprocess
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).parent))
from pipe_client import PipeClient  # noqa: E402

HELD_BY_OTHER = -20


class Checks:
    def __init__(self):
        self.failures: list[str] = []
        self.passed = 0

    def check(self, ok: bool, what: str, detail: object = ""):
        if ok:
            self.passed += 1
            print(f"  ok    {what}")
        else:
            self.failures.append(what)
            print(f"  FAIL  {what}: {detail}")
        return ok


def holds_by_name(client: PipeClient) -> dict:
    r = client.request("get_holds")
    return {h["hold"]: h for h in r.get("holds", [])} if r.get("ok") else {}


def run_child(pid: int | None) -> int:
    """The background client: reads commands on stdin (one JSON per line), answers on stdout, until EOF."""
    c = PipeClient(pid=pid, timeout=60)
    c.connect()
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        msg = json.loads(line)
        cmd = msg.pop("cmd")
        reply = c.request(cmd, background=True, **msg)
        sys.stdout.write(json.dumps(reply) + "\n")
        sys.stdout.flush()
    c.close()
    return 0


class Child:
    def __init__(self, pid: int | None):
        args = [sys.executable, __file__, "--child"] + (["--pid", str(pid)] if pid else [])
        self.proc = subprocess.Popen(args, stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True, bufsize=1)

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


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--pid", type=int)
    ap.add_argument("--child", action="store_true", help=argparse.SUPPRESS)
    a = ap.parse_args()
    if a.child:
        return run_child(a.pid)

    ck = Checks()
    ui = PipeClient(pid=a.pid, timeout=60)
    ui.connect()
    ptrs = ui.request("get_pointers")
    game = ptrs.get("pid")
    print(f"game pid {game}, DLL build {ptrs.get('build_number')}; this (foreground) client pid {__import__('os').getpid()}")

    holds = holds_by_name(ui)
    ck.check(bool(holds) and {"god_mode", "fly", "time:global", "movement:jump"} <= set(holds),
             f"get_holds lists the fixed holds ({len(holds)})", holds.keys())
    if not holds:
        print("  (get_holds missing: the game's DLL predates build 3704)")
        return 1

    # 2. The foreground client turns god mode on.
    on = ui.request("set_god_mode", enable=True)
    holds = holds_by_name(ui)
    god = holds.get("god_mode", {})
    ck.check(god.get("active") and god["holder"].get("kind") == "client" and god["holder"].get("you")
             and not god["holder"].get("background"),
             "the foreground client's god mode is its own, not background", (on, god))

    bg = Child(game)
    try:
        # 3. The background client may not end it.
        refused = bg.request("set_god_mode", enable=False, if_free=True)
        ck.check(refused.get("code") == HELD_BY_OTHER and refused.get("hold") == "god_mode"
                 and refused.get("holder", {}).get("kind") == "client" and not refused["holder"].get("you"),
                 "the background client's if_free god-mode OFF is refused, naming the holder", refused)
        ck.check(holds_by_name(ui).get("god_mode", {}).get("active"), "and god mode stays on")

        # 4. The background client takes a free hold.
        took = bg.request("set_time_dilation", target="global", value=0.5, if_free=True)
        mine = {h["hold"]: h for h in bg.request("get_holds").get("holds", [])}.get("time:global", {})
        ck.check(took.get("code") == 0 and mine.get("active") and mine["holder"].get("you")
                 and mine["holder"].get("background"),
                 "the background client's world time dilation is its own, marked background", (took, mine))

        # 5. The rule is symmetric: the foreground client's if_free change is refused too.
        back = ui.request("reset_time_dilation", target="global", if_free=True)
        ck.check(back.get("code") == HELD_BY_OTHER and back.get("holder", {}).get("background"),
                 "the foreground client's if_free reset of it is refused, holder marked background", back)

        # 6. pipe_clients marks the background connection.
        clients = ui.request("pipe_clients").get("clients", [])
        ck.check(any(c.get("background") for c in clients) and any(not c.get("background") for c in clients),
                 "pipe_clients marks the background connection and not the foreground one",
                 [(c.get("pid"), c.get("background")) for c in clients])
    finally:
        code = bg.leave()
    ck.check(code == 0, "the background client left", code)

    # 7. Its hold is released; the foreground client's is not. The release runs on the DLL's teardown of that
    # connection, a moment after the process exits.
    released = None
    for _ in range(50):
        holds = holds_by_name(ui)
        released = holds.get("time:global", {})
        if not released.get("active"):
            break
        time.sleep(0.1)
    ck.check(not released.get("active") and released.get("holder", {}).get("kind") == "none",
             "the background client's time lane is released when it leaves", released)
    ck.check(holds.get("god_mode", {}).get("active") and holds["god_mode"]["holder"].get("you"),
             "the foreground client's god mode is untouched", holds.get("god_mode"))

    # 8. Clean-up.
    ui.request("set_god_mode", enable=False)
    holds = holds_by_name(ui)
    still = [h for h, v in holds.items() if v.get("active")]
    ck.check(not still, "nothing is left held", still)
    ui.close()

    print(f"\n{ck.passed} passed, {len(ck.failures)} failed")
    return 0 if not ck.failures else 1


if __name__ == "__main__":
    sys.exit(main())
