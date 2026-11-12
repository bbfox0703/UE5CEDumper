"""Register [PIPE-PER-PROCESS-2026-11-06] item 2: a restart while this copy's own connections from before its stop
still hold their pipe instances.

    py tools/verify/straggler_restart.py --pid P --game-name DumperTest-Win64-Shipping
        [--ce-desc "Inject DLL + Start Pipe Server"] [--go-file out/straggler_go] [--ui-wait 120]

NEEDS: the DLL started by the CE record --ce-desc (the CT's inject record: its untick runs UE5_Shutdown, its re-tick
restarts the parked DLL), Cheat Engine running with the AOBMaker plugin (`ce_lua_eval.py`), and the DumperTest fixture
(`V1a_GrowContainers`, an unclamped UFUNCTION that runs long enough to be caught mid-run).

WHY A RUNNING CALL, NOT A QUEUED ONE. UE5_Shutdown answers every queued game-thread request -7 (Stark::Shutdown) before
Fern::Stop, so a queued invoke leaves no straggler. The drain pops a request just before running it, so a call the game
thread is EXECUTING when it is frozen is out of Stark::Shutdown's reach: its handler waits out the raised invoke
timeout, Stop's 5 s drain times out with it still registered, and the re-tick's probe finds every instance busy.

WHAT IT DOES:
  1. set_invoke_timeout 180000 (persist false), find the fixture actor, calibrate a V1a_GrowContainers count that runs
     about 2 s on the game thread.
  2. Three connections send that call at once; 1 s after the first is enqueued, the game thread is frozen
     (suspend.py suspend-tid), so one call is mid-run and two are queued.
  3. CE unticks the record (scheduled on CE's main thread); the rig waits for "PipeServer: Stopped" and reads the drain
     line: it must be TIMEOUT with 1 left, the straggler INSIDE invoke_function.
  4. At least 1 s after Stopped, CE re-ticks the record; the rig waits for init-0.log's "restarting the server beside
     them" and pipe-0.log's Started and shared-name WARN.
  5. It prints CONNECT THE UI NOW and waits (--ui-wait s) for --go-file to exist: connect the real UI meanwhile.
  6. It resumes the game thread (always, in a finally), reads the straggler's reply, asks get_pointers on it (an
     observation), restores the invoke timeout and shrinks the fixture's containers.
Exit 0 when every check passed.
"""
from __future__ import annotations

import argparse
import json
import pathlib
import subprocess
import sys
import threading
import time

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from pipe_client import PipeClient, LOGROOT   # noqa: E402
from ad4_contested import find_live_actor     # noqa: E402

FN = "V1a_GrowContainers"


def suspend(*args: str) -> tuple[int, str]:
    out = subprocess.run([sys.executable, str(HERE / "suspend.py"), *args], capture_output=True, text=True)
    return out.returncode, out.stdout.strip()


def game_thread(name: str) -> int:
    mains = [l for l in suspend("threads", name)[1].splitlines() if "<-- main thread" in l]
    if len(mains) != 1:
        raise SystemExit(f"FAIL: --game-name {name!r} gives {len(mains)} main threads")
    return int(mains[0].split("tid=")[1].split()[0])


def ce_schedule(desc: str, active: bool) -> str:
    """Tick or untick a CE record on CE's main thread, from a timer, so this call returns at once."""
    chunk = ("local t=createTimer(nil,false) t.Interval=50 t.OnTimer=function(x) x.destroy() "
             f"local r=getAddressList().getMemoryRecordByDescription([[{desc}]]) if r then r.Active={str(active).lower()} end "
             "end t.Enabled=true return 'scheduled'")
    out = subprocess.run([sys.executable, str(HERE / "ce_lua_eval.py"), chunk, "--timeout", "60"],
                         capture_output=True, text=True)
    return out.stdout.strip().splitlines()[-1] if out.stdout.strip() else out.stderr.strip()


def log_lines(folder: pathlib.Path, name: str, since: str) -> list[str]:
    p = folder / name
    try:
        text = p.read_text(encoding="utf-8", errors="replace")
    except OSError:
        return []
    return [l.rstrip() for l in text.splitlines() if l.startswith("[") and l[1:24] >= since]


def wait_line(folder, name, since, needle, seconds) -> str | None:
    end = time.monotonic() + seconds
    while time.monotonic() < end:
        hit = [l for l in log_lines(folder, name, since) if needle in l]
        if hit:
            return hit[-1]
        time.sleep(0.25)
    return None


def stamp() -> str:
    t = time.time()
    return time.strftime("%Y-%m-%d %H:%M:%S", time.localtime(t)) + ".%03d" % int((t % 1) * 1000)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--pid", type=int, required=True)
    ap.add_argument("--game-name", required=True, help="the game's image stem, for suspend.py")
    ap.add_argument("--ce-desc", default="Inject DLL + Start Pipe Server")
    ap.add_argument("--go-file", default=str(HERE.parent.parent / "out" / "straggler_go"))
    ap.add_argument("--ui-wait", type=float, default=120.0)
    a = ap.parse_args()
    folder = LOGROOT / a.game_name
    go = pathlib.Path(a.go_file)
    go.unlink(missing_ok=True)
    fails: list[str] = []

    def check(ok: bool, what: str, detail: object = "") -> bool:
        print(("  ok    " if ok else "  FAIL  ") + what + ("" if ok or detail == "" else f": {detail}"), flush=True)
        if not ok:
            fails.append(what)
        return ok

    c0 = PipeClient(pid=a.pid, timeout=300)
    c0.connect()
    print(f"build {c0.assert_build()}", flush=True)
    c0.request("set_invoke_timeout", timeout_ms=180000, persist=False)
    act = find_live_actor(c0)
    fns = {f["name"]: f for f in c0.request("walk_functions", addr=act["class_addr"])["functions"]}
    ps = fns[FN]["parms_size"]

    def params(count: int) -> str:
        return count.to_bytes(4, "little").hex() + "00" * (ps - 4)

    count, took = 200000, 0.0
    for _ in range(5):
        t0 = time.monotonic()
        r = c0.request("invoke_function", func_name=FN, instance_addr=act["addr"], parms_size=ps,
                       params_hex=params(count))
        took = time.monotonic() - t0
        print(f"calibrate {FN}({count}) -> result {r.get('result')} in {took:.2f} s", flush=True)
        if took >= 1.5 or count >= 8000000:
            break
        count = min(8000000, int(count * max(2.0, 2.0 / max(took, 0.05))))
    c0.close()
    tid = game_thread(a.game_name)
    print(f"actor {act['addr']}; {FN}({count}) about {took:.1f} s; game thread tid {tid}", flush=True)

    clients = [PipeClient(pid=a.pid, timeout=300) for _ in range(3)]
    for c in clients:
        c.connect()
    replies: list[object] = [None, None, None]

    def send(i: int):
        try:
            replies[i] = clients[i].request("invoke_function", func_name=FN, instance_addr=act["addr"],
                                            parms_size=ps, params_hex=params(count))
        except Exception as e:  # noqa: BLE001 -- recorded, judged below
            replies[i] = f"error: {e}"

    since = stamp()
    frozen = False
    threads = [threading.Thread(target=send, args=(i,), daemon=True) for i in range(3)]
    try:
        for t in threads:
            t.start()
        first = wait_line(folder, "pipe-0.log", since, "GameThreadDispatch: enqueued invoke", 10)
        t_first = time.monotonic()
        end = time.monotonic() + 10
        while time.monotonic() < end and sum("GameThreadDispatch: enqueued invoke" in l
                                             for l in log_lines(folder, "pipe-0.log", since)) < 3:
            time.sleep(0.05)
        time.sleep(max(0.0, 1.0 - (time.monotonic() - t_first)))
        rc, out = suspend("suspend-tid", a.game_name, str(tid))
        frozen = rc == 0 and f"tid {tid}: SUSPENDED" in out
        if not check(frozen and first is not None, "the game thread frozen 1 s into the first call", out):
            return 1
        done = sum("GameThreadDispatch: invoke completed" in l for l in log_lines(folder, "pipe-0.log", since))
        print(f"        calls completed before the freeze: {done}", flush=True)

        print("UNTICK:", ce_schedule(a.ce_desc, False), flush=True)
        stopped = wait_line(folder, "pipe-0.log", since, "PipeServer: Stopped", 30)
        drain = wait_line(folder, "pipe-0.log", since, "Stop conn drain", 1)
        strag = [l for l in log_lines(folder, "pipe-0.log", since) if "straggler:" in l]
        check(stopped is not None, "the untick stopped the server", stopped)
        print("        " + (drain or "<no drain line>"), flush=True)
        for l in strag:
            print("        " + l, flush=True)
        check(bool(drain) and "TIMEOUT" in drain and "1 left" in drain, "the drain timed out with 1 connection left",
              drain)
        check(any("INSIDE a command" in l and "invoke_function" in l for l in strag),
              "the straggler is inside invoke_function", strag)
        if stopped is None:
            return 1

        time.sleep(1.2)
        since2 = stamp()
        print("RE-TICK:", ce_schedule(a.ce_desc, True), flush=True)
        restart = wait_line(folder, "init-0.log", since2, "restarting the server beside them", 30)
        started = wait_line(folder, "pipe-0.log", since2, "PipeServer: Started on", 10)
        shared = wait_line(folder, "pipe-0.log", since2, "already had instances in this process", 10)
        refused = [l for l in log_lines(folder, "pipe-0.log", since2) + log_lines(folder, "init-0.log", since2)
                   if "Start refused" in l or "deferring" in l]
        check(restart is not None, "init-0.log: restarting the server beside them", restart)
        check(started is not None, "pipe-0.log: Started", started)
        check(shared is not None, "pipe-0.log: the shared-name WARN", shared)
        check(not refused, "no Start refused / deferring line", refused)
        for l in (restart, started, shared):
            if l:
                print("        " + l, flush=True)

        print(f"CONNECT THE UI NOW (waiting up to {a.ui_wait:.0f} s for {go})", flush=True)
        end = time.monotonic() + a.ui_wait
        while time.monotonic() < end and not go.exists():
            time.sleep(0.5)
        print("        go file seen" if go.exists() else "        no go file: carrying on", flush=True)
    finally:
        if frozen:
            rc, out = suspend("resume-tid", a.game_name, str(tid))
            print("RESUME:", out, flush=True)
            if rc != 0:
                print(f"!! GAME THREAD LEFT SUSPENDED: py tools/verify/suspend.py resume-tid {a.game_name} {tid}",
                      flush=True)

    for t in threads:
        t.join(timeout=200)
    for i, r in enumerate(replies):
        print(f"        c{i + 1} reply: {json.dumps(r)[:200] if not isinstance(r, str) else r}", flush=True)
    check(any(isinstance(r, dict) and r.get("result") == 0 for r in replies), "the straggler's call finished (0)",
          replies)
    for i, r in enumerate(replies):
        if isinstance(r, dict) and r.get("result") == 0:
            try:
                p = clients[i].request("get_pointers")
                print(f"        observation: get_pointers on the straggler answered build {p.get('build_number')}",
                      flush=True)
            except Exception as e:  # noqa: BLE001 -- an observation only
                print(f"        observation: get_pointers on the straggler failed: {e}", flush=True)
    for c in clients:
        try:
            c.close()
        except Exception:  # noqa: BLE001
            pass
    try:
        c9 = PipeClient(pid=a.pid, timeout=120)
        c9.connect()
        c9.request("set_invoke_timeout", timeout_ms=0, persist=False)
        c9.request("invoke_function", func_name="V1a_ShrinkContainers", instance_addr=act["addr"], parms_size=0,
                   params_hex="")
        c9.close()
        print("        restored: invoke timeout cleared, containers shrunk", flush=True)
    except Exception as e:  # noqa: BLE001
        print(f"        restore failed: {e}", flush=True)
    print(f"\n{'PASS' if not fails else 'FAIL'}: {len(fails)} failed", flush=True)
    return 0 if not fails else 1


if __name__ == "__main__":
    sys.exit(main())
