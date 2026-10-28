"""[STARK-DRAIN-OFF-GAME-THREAD] Queued invokes on a live game: did every one run on the game thread?

    py tools/verify/stark_game_thread_drain.py DumperTest58                 # 50 Debug Camera ON/OFF pairs
    py tools/verify/stark_game_thread_drain.py DumperTest58 --pairs 100
    py tools/verify/stark_game_thread_drain.py DumperTest-Win64-Shipping --gc 20   # no CheatManager: GC invokes
    py tools/verify/stark_game_thread_drain.py DumperTest58 --game-log "<game>\\Saved\\Logs\\DumperTest58.log"

Headless, through the pipe (the UI must NOT be connected). The first argument is the log folder's name under
%LOCALAPPDATA%\\UE5CEDumper\\Logs -- the game's process name -- so the DLL's own lines for this run can be read back.

WHAT IT MEASURES. Stark drains the invoke queue inside the hooked ProcessEvent; engine worker threads call
ProcessEvent too, and before the fix a drain there ran queued work off the game thread -- measured on DumperTest58
Development: a ToggleDebugCamera failed BodyInstance.cpp's IsInGameThread check, ~1 in 50 toggles, which the DLL
logged as "SEH exception during queued PE call". With --pairs (a Development build with a live CheatManager) the run is
that same workload: Debug Camera ON/OFF through set_debug_camera. With --gc (any build) it is UKismetSystemLibrary::
CollectGarbage, a parameterless static, as many times. Then it reads the DLL's log lines written since it started and
reports: the game thread picked and how ("named GameThread by the engine" or "the main thread"), whether the install
validator kept it or gave the gate up (a WARN naming "the main-thread guess"), how many worker-thread entries left
queued work, and every "SEH exception during queued PE call".
--game-log adds the engine's own `IsInGameThread` assertions from that file. Ends with the camera OFF.
"""
import argparse
import datetime
import os
import pathlib
import re
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient  # noqa: E402


def data(reply):
    return reply.get("data", reply)


def log_lines_since(folder, since):
    """DLL log lines stamped at or after `since` across the folder's current (-0) logs."""
    stamp = re.compile(r"^\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})")
    out = []
    for f in sorted(folder.glob("*-0.log")):
        for line in f.read_text(encoding="utf-8", errors="replace").splitlines():
            m = stamp.match(line)
            if m and datetime.datetime.strptime(m.group(1), "%Y-%m-%d %H:%M:%S") >= since:
                out.append(line)
    return out


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("process", help="the game's log folder name, e.g. DumperTest58")
    ap.add_argument("--pairs", type=int, default=50, help="Debug Camera ON/OFF pairs (needs a live CheatManager)")
    ap.add_argument("--gc", type=int, default=0, help="CollectGarbage invokes instead of Debug Camera toggles")
    ap.add_argument("--game-log", default=None, help="the game's own log, to count IsInGameThread assertions")
    a = ap.parse_args()
    folder = pathlib.Path(os.environ["LOCALAPPDATA"]) / "UE5CEDumper" / "Logs" / a.process
    since = datetime.datetime.now().replace(microsecond=0) - datetime.timedelta(seconds=1)

    wrong = 0
    with PipeClient() as pc:
        pc.connect()
        print("build:", pc.assert_build())
        pc.ensure_scanned()
        if a.gc:
            for i in range(a.gc):
                r = data(pc.request("invoke_function", class_name="KismetSystemLibrary", func_name="CollectGarbage",
                                    parms_size=0))
                wrong += r.get("result") != 0
                time.sleep(0.3)
            print(f"CollectGarbage x{a.gc}: {a.gc - wrong} returned 0")
        else:
            for i in range(a.pairs):
                for want in (1, 0):
                    st = data(pc.request("set_debug_camera", enable=bool(want))).get("state")
                    wrong += st != want
                    time.sleep(0.4)
                if (i + 1) % 10 == 0:
                    print(f"  {i + 1} pairs, {wrong} answer(s) not as asked so far")
            if data(pc.request("get_debug_camera_state")).get("state") == 1:
                pc.request("set_debug_camera", enable=False)
    time.sleep(2.0)   # let the DLL flush

    lines = log_lines_since(folder, since)
    pick = [l for l in lines if "GameThreadDispatch: game thread" in l
            or "GameThreadDispatch: the main-thread guess" in l]
    skips = [l for l in lines if "left" in l and "queued request(s) for the game thread" in l]
    seh = [l for l in lines if "SEH exception during queued PE call" in l]
    print("\n-- DLL log since the run started --")
    for l in pick + skips[:1]:
        print("  ", l[:220])
    print(f"SEH during a queued call: {len(seh)}")
    for l in seh:
        print("  ", l[:160])
    if a.game_log:
        text = pathlib.Path(a.game_log).read_text(encoding="utf-8", errors="replace")
        asserts = [l for l in text.splitlines() if "IsInGameThread" in l and "Assertion failed" in l]
        print(f"engine IsInGameThread assertions in {a.game_log} (whole file): {len(asserts)}")
        for l in asserts[-3:]:
            print("  ", l[:200])
    total = a.gc or 2 * a.pairs
    print(f"\n{total - wrong} of {total} invokes answered as asked")
    return 0 if wrong == 0 and not seh else 1


if __name__ == "__main__":
    sys.exit(main())
