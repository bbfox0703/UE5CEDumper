"""[STARK-DRAIN-OFF-GAME-THREAD] Force the drain gate's worker-thread case: freeze ONLY the game thread, queue a Debug
Camera ON, let worker threads enter ProcessEvent while the request waits, then resume. With the gate, a worker entry
leaves the request queued (the DLL logs the first such skip) and the game thread runs it on resume.

    py tools/verify/stark_drain_suspend.py <game-thread-tid> [process name, default DumperTest58]

The game-thread id comes from `py tools/verify/suspend.py threads <process>` (the game thread is the process's main
thread, which it lists first by creation time);
the pipe is the only one up, or the one UE5DUMP_PID names. A Debug Camera left ON is turned OFF at the end.
"""
import pathlib
import subprocess
import sys
import threading
import time

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from pipe_client import PipeClient  # noqa: E402

SUSPEND = str(HERE / "suspend.py")


def suspend(verb, proc, tid):
    r = subprocess.run([sys.executable, SUSPEND, verb, proc, str(tid)], capture_output=True, text=True)
    out = (r.stdout + r.stderr).strip()
    print(f"{verb} {tid}: {out.splitlines()[-1] if out else r.returncode}")


def main() -> int:
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    tid = int(sys.argv[1])
    proc = sys.argv[2] if len(sys.argv) > 2 else "DumperTest58"
    with PipeClient() as pc:
        pc.connect()
        print("build:", pc.assert_build())
        print("state before:", pc.request("get_debug_camera_state").get("state"))
        result = {}

        def fire():
            result["set"] = pc.request("set_debug_camera", enable=True).get("state")

        suspend("suspend-tid", proc, tid)
        try:
            t = threading.Thread(target=fire)
            t.start()
            time.sleep(4.0)          # the request waits; worker threads may enter ProcessEvent meanwhile
        finally:
            suspend("resume-tid", proc, tid)
        t.join(timeout=15)
        print("set_debug_camera(1) while the game thread was frozen ->", result.get("set"))
        time.sleep(2.0)
    with PipeClient() as pc:
        pc.connect()
        st = pc.request("get_debug_camera_state").get("state")
        print("state after resume (the queued ON ran on the game thread):", st)
        if st == 1:
            print("forcing OFF:", pc.request("set_debug_camera", enable=False).get("state"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
