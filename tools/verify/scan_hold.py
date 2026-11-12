"""Register [PIPE-PER-PROCESS-2026-11-06] item 10: the UI's Start Scan against a scan another client started.

    py tools/verify/scan_hold.py --pid P --log-folder DumperTest51-Win64-Shipping [--wait-ui 120]

NEEDS: a game whose DLL has NOT scanned yet (a proxy-loaded game defers its scan until asked), and the UI connected to
it, showing Start Scan. This rig starts the scan from its own pipe client, finds the thread the scan job runs on and
freezes it, so the scan stays "running" for as long as it takes to click Start Scan in the UI. Then it resumes the
thread (always, in a finally) and reads the game's pipe-0.log.

WHY FREEZE. A DumperTest scan takes about 1.8 s; a click that misses it makes the UI run a fresh scan of its own, which
passes for the wrong reason and costs a relaunch.

PASS (exit 0): exactly one "trigger_scan: Starting async engine scan" and one "RunScan: started" / "RunScan:
finished" since the rig started, and the UI's own trigger_scan received while the scan ran (the DLL answered it
in_progress; the UI then polls scan_status and shows the progress).
"""
from __future__ import annotations

import argparse
import ctypes
import pathlib
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from pipe_client import PipeClient, LOGROOT   # noqa: E402
from suspend import thread_ids, thread_times  # noqa: E402

k32 = ctypes.WinDLL("kernel32", use_last_error=True)
k32.OpenThread.restype = ctypes.c_void_p
k32.SuspendThread.restype = ctypes.c_uint32
k32.ResumeThread.restype = ctypes.c_uint32


def stamp() -> str:
    t = time.time()
    return time.strftime("%Y-%m-%d %H:%M:%S", time.localtime(t)) + ".%03d" % int((t % 1) * 1000)


def log_since(folder: pathlib.Path, since: str) -> list[str]:
    try:
        text = (folder / "pipe-0.log").read_text(encoding="utf-8", errors="replace")
    except OSError:
        return []
    return [l.rstrip() for l in text.splitlines() if l.startswith("[") and l[1:24] >= since]


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--pid", type=int, required=True)
    ap.add_argument("--log-folder", required=True, help="the game's folder under Logs (its image stem)")
    ap.add_argument("--wait-ui", type=float, default=120.0)
    a = ap.parse_args()
    folder = LOGROOT / a.log_folder

    c = PipeClient(pid=a.pid, timeout=60)
    c.connect()
    print(f"build {c.assert_build()}", flush=True)
    # scan_status' own flag: get_pointers' gobjects_method reads "not_found" before a scan, which is not empty.
    if c.request("scan_status").get("scanned"):
        print("FAIL: this game is already scanned -- start a fresh one (a proxy-loaded game scans only when asked)")
        return 1
    since = stamp()
    before = set(thread_ids(a.pid))
    r = c.request("trigger_scan")
    if not r.get("started"):
        print(f"FAIL: trigger_scan did not start a scan: {r}")
        return 1
    held = None
    try:
        for attempt in range(20):
            # The scan job is the newest thread since trigger_scan: newest creation time first.
            new = [t for t in thread_ids(a.pid) if t not in before]
            new.sort(key=lambda t: (thread_times(t) or (0, 0.0))[0], reverse=True)
            if not new:
                time.sleep(0.05)
                continue
            tid = new[0]
            h = k32.OpenThread(0x0002, False, tid)   # THREAD_SUSPEND_RESUME
            if not h:
                time.sleep(0.05)
                continue
            h = ctypes.c_void_p(h)
            if k32.SuspendThread(h) == 0xFFFFFFFF:
                k32.CloseHandle(h)
                continue
            held = (tid, h)
            probe = PipeClient(pid=a.pid, timeout=3)
            try:
                probe.connect(retries=2, delay=0.2)
                s = probe.request("scan_status")
            finally:
                probe.close()
            if s.get("running"):
                print(f"HELD: scan thread tid {tid} frozen (try {attempt + 1}); phase {s.get('phase')}, "
                      f"{s.get('status_text')!r}", flush=True)
                break
            k32.ResumeThread(h)
            k32.CloseHandle(h)
            held = None
            before.add(tid)
            time.sleep(0.05)
        if held is None:
            print("FAIL: could not hold the scan running (it finished, or its thread was not found)")
            return 1

        print(f"CLICK START SCAN IN THE UI NOW (waiting up to {a.wait_ui:.0f} s for its trigger_scan)", flush=True)
        end = time.monotonic() + a.wait_ui
        seen = False
        while time.monotonic() < end and not seen:
            seen = any("Received:" in l and '"cmd":"trigger_scan"' in l for l in log_since(folder, since))
            time.sleep(0.2)
        print("        the UI's trigger_scan arrived" if seen else "        no trigger_scan from the UI", flush=True)
        time.sleep(3.0)
    finally:
        if held:
            prev = k32.ResumeThread(held[1])
            while prev not in (0, 1, 0xFFFFFFFF):
                prev = k32.ResumeThread(held[1])
            k32.CloseHandle(held[1])
            print(f"RESUME: tid {held[0]}", flush=True)
    end = time.monotonic() + 60
    while time.monotonic() < end and not any("RunScan: finished" in l for l in log_since(folder, since)):
        time.sleep(0.25)
    c.close()
    lines = log_since(folder, since)
    starts = sum("trigger_scan: Starting async engine scan" in l for l in lines)
    runs = sum("RunScan: started" in l for l in lines)
    fins = sum("RunScan: finished" in l for l in lines)
    ui = [l for l in lines if "Received:" in l and '"cmd":"trigger_scan"' in l]
    fin_at = next((l[1:24] for l in lines if "RunScan: finished" in l), None)
    ui_in_time = bool(ui) and fin_at is not None and ui[0][1:24] < fin_at
    for l in lines:
        if "trigger_scan" in l or "RunScan" in l:
            print("  " + l[:200], flush=True)
    ok = starts == 1 and runs == 1 and fins == 1 and ui_in_time
    print(f"\nstarts {starts}, RunScan started {runs} / finished {fins}, the UI's trigger_scan before the finish: "
          f"{ui_in_time}\n{'PASS' if ok else 'FAIL'}", flush=True)
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
