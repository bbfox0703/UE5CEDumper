"""Register [PIPE-PER-PROCESS-2026-11-06] item 12: a Stop whose teardown waits on the game thread.

    py tools/verify/stop_teardown_live.py --pid P --game-name DumperTest-Win64-Shipping --ce-action untick|stop
        [--ce-desc "Inject DLL + Start Pipe Server"] [--ce-delay-ms 4000]

NEEDS: the UI connected to the game with See-through ON (its worker traces on the game thread every 100 ms), the DLL
started by the CE record --ce-desc, and Cheat Engine running with the AOBMaker plugin (`ce_lua_eval.py`).

WHAT IT DOES. It schedules the CE action on CE's main thread, --ce-delay-ms from now: `untick` unticks the record (its
[DISABLE] runs UE5_Shutdown), `stop` calls the export UE5_StopPipeServer through executeCodeEx (a Stop with no
UE5_Shutdown in front of it). Then it freezes the game thread through a handle opened beforehand, so the See-through
worker's next trace waits on it, and 0.3 s later posts WM_CLOSE to the UI's windows: the UI's teardown releases
See-through, which joins that worker. It waits for the UI to exit and for "PipeServer: Stopped", prints init-0.log and
pipe-0.log merged by their own stamps from the start, and resumes the game thread (always, in a finally).

Read the timeline for: the drain line ("satisfied|TIMEOUT, X left, Y still tearing down"), the WARN naming a teardown
still running, and how long "UE5_Shutdown: Cleaning up" (or the Stop) took to reach "Stopped". Exit 0 when Stopped was
seen and the game thread resumed; the verdict on the row is read from the timeline.
"""
from __future__ import annotations

import argparse
import ctypes
import ctypes.wintypes as w
import pathlib
import subprocess
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from pipe_client import LOGROOT   # noqa: E402

KEYS = ("SeeThrough", "UE5_Shutdown", "UE5_StopPipeServer", "Stop entry", "Stop conn drain", "still tearing down",
        "tearing down", "PipeServer: Stopped", "Client disconnected", "invoke timeout", "hook flag cleared",
        "straggler", "teardown")


def stamp() -> str:
    t = time.time()
    return time.strftime("%Y-%m-%d %H:%M:%S", time.localtime(t)) + ".%03d" % int((t % 1) * 1000)


def lines(folder: pathlib.Path, since: str) -> list[str]:
    out = []
    for name in ("init-0.log", "pipe-0.log"):
        try:
            text = (folder / name).read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue
        out += [l.rstrip() for l in text.splitlines() if l.startswith("[") and l[1:24] >= since]
    return sorted(out, key=lambda l: l[1:24])


def game_thread(name: str) -> int:
    out = subprocess.run([sys.executable, str(HERE / "suspend.py"), "threads", name], capture_output=True, text=True)
    mains = [l for l in out.stdout.splitlines() if "<-- main thread" in l]
    if len(mains) != 1:
        raise SystemExit(f"FAIL: --game-name {name!r} gives {len(mains)} main threads")
    return int(mains[0].split("tid=")[1].split()[0])


def ui_pids() -> list[int]:
    out = subprocess.run(["tasklist", "/FO", "CSV", "/NH"], capture_output=True, text=True, errors="replace").stdout
    pids = []
    for l in out.splitlines():
        parts = [p.strip('"') for p in l.split('","')]
        if parts and parts[0] == "UE5DumpUI.exe":
            pids.append(int(parts[1]))
    return pids


def windows_of(pids: list[int]) -> list[int]:
    u = ctypes.windll.user32
    found: list[int] = []

    @ctypes.WINFUNCTYPE(ctypes.c_bool, w.HWND, w.LPARAM)
    def cb(h, _l):
        p = w.DWORD()
        u.GetWindowThreadProcessId(h, ctypes.byref(p))
        if p.value in pids and u.IsWindowVisible(h) and u.GetWindowTextLengthW(h) > 0:
            found.append(h)
        return True

    u.EnumWindows(cb, 0)
    return found


def ce_schedule(action: str, desc: str, delay_ms: int) -> str:
    if action == "untick":
        body = f"local r=getAddressList().getMemoryRecordByDescription([[{desc}]]) if r then r.Active=false end"
    else:
        body = "print(executeCodeEx(0, 30000, getAddress('UE5_StopPipeServer')))"
    chunk = (f"local t=createTimer(nil,false) t.Interval={delay_ms} t.OnTimer=function(x) x.destroy() {body} end "
             "t.Enabled=true return 'scheduled'")
    out = subprocess.run([sys.executable, str(HERE / "ce_lua_eval.py"), chunk, "--timeout", "60"],
                         capture_output=True, text=True)
    return out.stdout.strip().splitlines()[-1] if out.stdout.strip() else out.stderr.strip()


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--pid", type=int, required=True)
    ap.add_argument("--game-name", required=True)
    ap.add_argument("--ce-action", choices=["untick", "stop"], required=True)
    ap.add_argument("--ce-desc", default="Inject DLL + Start Pipe Server")
    ap.add_argument("--ce-delay-ms", type=int, default=4000)
    ap.add_argument("--wait", type=float, default=40.0)
    a = ap.parse_args()
    folder = LOGROOT / a.game_name
    ui = [p for p in ui_pids() if windows_of([p])]   # a windowless one is an MCP server, not the UI
    wins = windows_of(ui)
    if not wins:
        raise SystemExit("FAIL: no UE5DumpUI window to close (connect the UI and turn See-through on first)")
    tid = game_thread(a.game_name)
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    k32.OpenThread.restype = ctypes.c_void_p
    k32.SuspendThread.restype = ctypes.c_uint32
    k32.ResumeThread.restype = ctypes.c_uint32
    h = k32.OpenThread(0x0002, False, tid)
    if not h:
        raise SystemExit(f"FAIL: OpenThread({tid}) -> Win32 {ctypes.get_last_error()}")
    h = ctypes.c_void_p(h)

    since = stamp()
    frozen = False
    stopped = None
    try:
        print(f"CE {a.ce_action} in {a.ce_delay_ms} ms:", ce_schedule(a.ce_action, a.ce_desc, a.ce_delay_ms),
              flush=True)
        t0 = time.monotonic()
        frozen = k32.SuspendThread(h) != 0xFFFFFFFF
        print(f"{time.monotonic() - t0:6.2f} s  game thread tid {tid} frozen: {frozen}", flush=True)
        time.sleep(0.3)
        for hw in windows_of(ui):
            ctypes.windll.user32.PostMessageW(hw, 0x0010, 0, 0)
        print(f"{time.monotonic() - t0:6.2f} s  WM_CLOSE posted to UE5DumpUI {ui}", flush=True)
        end = time.monotonic() + a.wait
        gone = None
        while time.monotonic() < end:
            if gone is None and not any(p in ui_pids() for p in ui):
                gone = time.monotonic() - t0
                print(f"{gone:6.2f} s  the UI exited", flush=True)
            if any("PipeServer: Stopped" in l for l in lines(folder, since)):
                stopped = time.monotonic() - t0
                print(f"{stopped:6.2f} s  PipeServer: Stopped seen", flush=True)
                break
            time.sleep(0.2)
    finally:
        if frozen:
            prev = k32.ResumeThread(h)
            while prev not in (0, 1, 0xFFFFFFFF):
                prev = k32.ResumeThread(h)
            print(f"RESUME: previous suspend count {prev}", flush=True)
        k32.CloseHandle(h)
    time.sleep(1.0)
    print("\nTIMELINE (init-0.log + pipe-0.log):", flush=True)
    for l in lines(folder, since):
        if any(k in l for k in KEYS):
            print("  " + l[:220], flush=True)
    print(f"\n{'Stopped seen' if stopped is not None else 'NO Stopped within the wait'}", flush=True)
    return 0 if stopped is not None else 1


if __name__ == "__main__":
    sys.exit(main())
