"""Hold a game's pipe instances, and let a control file release them (register [PIPE-PER-PROCESS-2026-11-06] item 3).

    py tools/verify/pipe_hold.py --pid P --count 6 --control out/row3/hold.ctl [--seconds 600]

Opens --count connections to the game's pipe and prints each one's own connection seq (pipe_clients, the entry marked
self). Then it polls --control every 0.5 s for one word:
    release1    close the last connection (one instance comes free)
    probe       open one more connection, print its seq and the highest seq listed, close it
    releaseall  close every connection and exit
The connections always close on exit. WHY: a second copy of the DLL in the process defers while every instance is
busy; the probe's seq being the holders' highest + 1 proves that copy's watcher made no connection meanwhile (the
DLL numbers every accept).
"""
from __future__ import annotations

import argparse
import pathlib
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from pipe_client import PipeClient   # noqa: E402


def own_seq(c: PipeClient) -> tuple[int | None, int]:
    clients = c.request("pipe_clients").get("clients", [])
    mine = next((x.get("seq") for x in clients if x.get("self")), None)
    top = max((x.get("seq") or 0 for x in clients), default=0)
    return mine, top


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--pid", type=int, required=True)
    ap.add_argument("--count", type=int, default=6)
    ap.add_argument("--control", required=True)
    ap.add_argument("--seconds", type=float, default=600.0)
    a = ap.parse_args()
    ctl = pathlib.Path(a.control)
    ctl.parent.mkdir(parents=True, exist_ok=True)
    ctl.write_text("", encoding="utf-8")
    held: list[PipeClient] = []
    try:
        for i in range(a.count):
            c = PipeClient(pid=a.pid, timeout=10)
            c.connect(retries=3, delay=0.5)
            held.append(c)
            print(f"holder {i + 1}: seq {own_seq(c)[0]}", flush=True)
        print(f"HOLDING {len(held)} connections; waiting on {ctl}", flush=True)
        end = time.monotonic() + a.seconds
        done: set[str] = set()
        while time.monotonic() < end:
            word = ctl.read_text(encoding="utf-8").strip()
            if word == "release1" and "release1" not in done and held:
                held.pop().close()
                done.add("release1")
                print("released one connection", flush=True)
            elif word == "probe" and "probe" not in done:
                p = PipeClient(pid=a.pid, timeout=10)
                p.connect(retries=3, delay=0.5)
                mine, top = own_seq(p)
                held_top = max((own_seq(c)[0] or 0) for c in held) if held else 0
                print(f"probe: its seq {mine}, highest listed {top}, the holders' highest {held_top}", flush=True)
                p.close()
                done.add("probe")
            elif word == "releaseall":
                break
            time.sleep(0.5)
    finally:
        for c in held:
            try:
                c.close()
            except Exception:  # noqa: BLE001
                pass
        print("released all", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
