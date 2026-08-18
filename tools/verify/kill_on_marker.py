"""Kill a process the moment a log line proves the target command is IN FLIGHT.

    py kill_on_marker.py <logfile> "<marker>" <process-name> [--after-ms 700] [--timeout-s 120]

Exists for the B4-shaped rows, where the whole test is "the client dies while
ONE call is still running". Those cannot be staged by hand: the window here is a
3.3 s startup scan, a GUI click lands somewhere unknowable inside a couple of
seconds, and a fixed sleep either fires before the command starts or after it
finished -- both were observed on 2026-08-18 and both prove nothing.

Watching the DLL's own log removes the guess: the kill is triggered BY the
evidence that the command began, then delayed a little so it lands mid-flight
rather than on the boundary.

Only bytes appended AFTER this starts are considered, so a marker left by an
earlier run cannot fire it instantly -- that false start is the obvious way to
get a kill that looks correctly timed and is not.
"""
import os
import subprocess
import sys
import time


def main(argv):
    if len(argv) < 3:
        print(__doc__)
        return 2
    path, marker, proc = argv[0], argv[1], argv[2]
    after_ms = int(argv[argv.index("--after-ms") + 1]) if "--after-ms" in argv else 700
    timeout_s = int(argv[argv.index("--timeout-s") + 1]) if "--timeout-s" in argv else 120

    # Start from the CURRENT end of file: only new output counts.
    start = os.path.getsize(path) if os.path.exists(path) else 0
    print(f"watching {path} from byte {start} for {marker!r}")
    sys.stdout.flush()

    deadline = time.time() + timeout_s
    while time.time() < deadline:
        try:
            size = os.path.getsize(path)
            if size > start:
                with open(path, "r", encoding="utf-8", errors="replace") as fh:
                    fh.seek(start)
                    chunk = fh.read()
                if marker in chunk:
                    hit = time.strftime("%H:%M:%S") + f".{int(time.time() * 1000) % 1000:03d}"
                    print(f"MARKER SEEN at {hit} -- waiting {after_ms} ms, then killing {proc}")
                    sys.stdout.flush()
                    time.sleep(after_ms / 1000.0)
                    r = subprocess.run(["taskkill", "/F", "/IM", proc],
                                       capture_output=True, text=True, errors="replace")
                    killed = time.strftime("%H:%M:%S") + f".{int(time.time() * 1000) % 1000:03d}"
                    print(f"KILLED at {killed} rc={r.returncode} {r.stdout.strip()}")
                    return 0
        except OSError:
            pass
        time.sleep(0.02)

    print(f"TIMEOUT after {timeout_s}s -- marker never appeared, nothing killed")
    return 1


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.exit(main(sys.argv[1:]))
