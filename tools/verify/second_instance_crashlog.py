r"""[LEAD-SECOND-UI-CRASHLOG] Launch the UI twice and check that the second launch leaves crash.log alone.

    py tools/verify/second_instance_crashlog.py <path to UE5DumpUI.exe>

Before the fix, a second launch found the single-instance mutex taken inside Avalonia's start-up, shut the lifetime
down, and the lifetime then threw "the Dispatcher shut down" into Program.Main's catch, which overwrote crash.log
(the first instance's real one included). Pass: the second process exits with 1, quickly, and crash.log's bytes and
modification time are what they were before it ran.

The first instance is started here and killed at the end; nothing else is touched. Refuses to run while another
UE5DumpUI is already up, since that one would be "the first instance" and would be killed.
"""
import os
import subprocess
import sys
import time
from pathlib import Path


def ui_running() -> bool:
    out = subprocess.run(["tasklist", "/FI", "IMAGENAME eq UE5DumpUI.exe", "/FO", "CSV", "/NH"],
                         capture_output=True, text=True).stdout
    return "UE5DumpUI.exe" in out


def crash_state(path: Path):
    if not path.exists():
        return None
    st = path.stat()
    return (st.st_mtime_ns, path.read_bytes())


def main(argv):
    if len(argv) != 1:
        print(__doc__)
        return 2
    exe = Path(argv[0])
    if not exe.is_file():
        print(f"no such exe: {exe}")
        return 2
    if ui_running():
        print("REFUSED: a UE5DumpUI is already running; close it first.")
        return 2

    crash = Path(os.environ["LOCALAPPDATA"]) / "UE5CEDumper" / "crash.log"
    before = crash_state(crash)
    print(f"crash.log before: {'absent' if before is None else f'{len(before[1])} bytes, mtime_ns {before[0]}'}")

    first = subprocess.Popen([str(exe)])
    try:
        # Long enough for the first instance to own the mutex and show its window.
        time.sleep(8)
        if first.poll() is not None:
            print(f"FAIL: the first instance exited early (code {first.returncode})")
            return 1
        t0 = time.monotonic()
        second = subprocess.run([str(exe)], timeout=60)
        took = time.monotonic() - t0
        print(f"second instance: exit code {second.returncode}, {took:.2f} s")
        after = crash_state(crash)
        print(f"crash.log after:  {'absent' if after is None else f'{len(after[1])} bytes, mtime_ns {after[0]}'}")
        ok = second.returncode == 1 and after == before
        print("PASS" if ok else "FAIL")
        return 0 if ok else 1
    finally:
        first.kill()
        first.wait(timeout=30)


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
