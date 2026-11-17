r"""[PIPE-WALK-DURING-INIT] live check: what the pipe reads while an init runs is logged once per name, and refused never.

    py tools/verify/initlog_live.py --pid 1234
    py tools/verify/initlog_live.py --pid 1234 --logdir DumperTest-Win64-Shipping

Needs a game whose DLL came in as a PROXY and has NOT scanned yet. In inject mode the pipe opens only after UE5_Init
returns, so nothing can arrive during it; a proxy serves the pipe first and runs UE5_Init on a job thread when a client
sends trigger_scan, while the other connections keep being served -- the window the maintainer's decision
(2026-11-17: refuse nothing, log it) is about.

Lane A sends trigger_scan and polls scan_status; lane B, a second connection, sends the same few commands in a loop
until the scan ends, one of them a name no handler knows. Then the game's pipe-0.log, from where it stood before
trigger_scan, must hold:
  - at least one "arrived during an init or rescan apply" line (the window was reached; if not, the scan was too fast
    for this machine and the run proves nothing -- it says so and fails),
  - each command name at most once (once per name per run),
and every lane-B reply to a known command must be ok (refused never).

Pipe only: no UI, no CE. The game, its install path and its pid are arguments, never written here.
"""
import argparse
import pathlib
import re
import sys
import threading
import time

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import LOGROOT, PipeClient, process_image   # noqa: E402

LINE = re.compile(r"PipeServer: (\S+) arrived during an init or rescan apply")
KNOWN = ("get_pointers", "pipe_clients")   # both answer before, during and after a scan
UNKNOWN = "initlog_no_such_command"

results: list[tuple[str, bool, str]] = []


def check(name: str, ok: bool, detail: str = "") -> bool:
    results.append((name, ok, detail))
    print(f"  [{'PASS' if ok else 'FAIL'}] {name}" + (f" -- {detail}" if detail else ""))
    return ok


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--pid", type=int, required=True)
    ap.add_argument("--logdir", help="the game's folder under Logs\\ (default: its exe's stem)")
    ap.add_argument("--timeout", type=float, default=180.0)
    a = ap.parse_args()
    logdir = a.logdir or pathlib.Path(process_image(a.pid)).stem
    log = LOGROOT / logdir / "pipe-0.log"

    with PipeClient(pid=a.pid) as ca, PipeClient(pid=a.pid) as cb:
        build = ca.assert_build()
        print(f"build {build}, log {log}")
        p = ca.request("get_pointers")
        if p.get("gobjects_method") not in (None, "", "not_found"):
            print("FAILED: this game has already scanned -- the init window is gone. Restart it with the proxy.")
            return 2
        start = log.stat().st_size if log.is_file() else 0

        stop = threading.Event()
        replies: list[tuple[str, dict]] = []

        def lane_b():
            while not stop.is_set():
                for cmd in KNOWN + (UNKNOWN,):
                    try:
                        replies.append((cmd, cb.request(cmd)))
                    except Exception as e:   # a closed pipe is a failure the checks below report
                        replies.append((cmd, {"ok": False, "error": f"client: {e}"}))
                        return

        t = threading.Thread(target=lane_b)
        t.start()
        try:
            trig = ca.request("trigger_scan")
            check("trigger_scan starts the scan", trig.get("ok") is True and bool(trig.get("started")), str(trig))
            deadline = time.time() + a.timeout
            st = {}
            while time.time() < deadline:
                st = ca.request("scan_status")
                if not st.get("running"):
                    break
                time.sleep(0.2)
            check("the scan ends scanned", st.get("scanned") is True, f"phase={st.get('phase')}")
        finally:
            stop.set()
            t.join()

    time.sleep(0.5)   # the log line is written before the reply; give the file a moment anyway
    text = log.read_bytes()[start:].decode("utf-8", errors="replace") if log.is_file() else ""
    names = LINE.findall(text)
    print(f"lane B sent {len(replies)} commands; the log names {names}")
    check("the window was reached: at least one arrived-during-init line", bool(names),
          "none -- the scan ended before lane B's first command; this run proves nothing")
    dupes = sorted({n for n in names if names.count(n) > 1})
    check("each name at most once in the run", not dupes, f"repeated: {dupes}")
    # Not a check: lane B may not have reached the unknown name inside the window.
    print(f"  (info) the unknown name {'was' if UNKNOWN in names else 'was not'} logged as having arrived")
    refused = [(c, r.get("error")) for c, r in replies if c in KNOWN and r.get("ok") is not True]
    check("refused never: every known command's reply is ok", not refused, str(refused[:5]))

    failed = [n for n, ok, _ in results if not ok]
    print(f"\n{len(results) - len(failed)}/{len(results)} passed")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
