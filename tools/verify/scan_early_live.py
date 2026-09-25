"""Live check for [SCAN-EARLY-TRIGGER-CONTAINED]: a trigger_scan sent the moment the pipe answers must not fault.

    py tools/verify/path_shape_live.py deploy <work dir>                  # dist\\proxy\\version.dll into the fixture
    py tools/verify/scan_early_live.py <work dir> [launches] [exe]        # default 5 launches, the plain exe name
    py tools/verify/path_shape_live.py undeploy <work dir>

WHY. On build 3555 a trigger_scan ~1 s after launch logged 'RunScan: UNCAUGHT non-standard exception — contained' in 4
of 5 launches: the multi-module AOB fallback read a DLL the booting engine had just freed (docs/todo.md). The fix pins
each module for its scan. This repeats the failing shape -- trigger as early as the pipe allows -- and checks, per
launch, that the scan thread reached 'RunScan: finished' and that NO log of this launch holds 'UNCAUGHT'. Whether the
early scan FINDS GObjects is reported but not judged: the engine may not have filled them yet (the row's other half).

One game at a time: each launch is killed (and confirmed gone) before the next. Paths are arguments.
"""
from __future__ import annotations

import os
import pathlib
import subprocess
import sys
import time

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient, PipeError          # noqa: E402
from path_shape_fixture import win64                   # noqa: E402
from path_shape_live import ARGS, DETACHED, alive, any_shipping, folder_name  # noqa: E402

SCAN_DONE_TIMEOUT_S = 180


def logs_of(exe: str) -> pathlib.Path:
    return pathlib.Path(os.environ["LOCALAPPDATA"]) / "UE5CEDumper" / "Logs" / folder_name(exe)


def this_launch_logs(folder: pathlib.Path, t0: float) -> list[pathlib.Path]:
    """The live (-0) logs this launch wrote. Each start archives the previous -0 files, so a -0 log with an mtime
    at or after the launch is this launch's."""
    return [p for p in folder.glob("*-0.log") if p.stat().st_mtime >= t0 - 2]


def read(p: pathlib.Path) -> str:
    try:
        return p.read_text(encoding="utf-8", errors="replace")
    except OSError:
        return ""


def one_launch(work: str, exe: str, n: int) -> tuple[bool, str]:
    path = win64(work) / exe
    folder = logs_of(exe)
    t0 = time.time()
    p = subprocess.Popen([str(path)] + ARGS, cwd=str(path.parent), creationflags=DETACHED)
    c = PipeClient(timeout=60)
    note = ""
    try:
        c.connect(retries=240, delay=0.25)
        c.assert_build()
        c.request("trigger_scan")
        sent = time.time() - t0
        deadline = time.time() + SCAN_DONE_TIMEOUT_S
        finished = False
        while time.time() < deadline:
            pipe_log = folder / "pipe-0.log"
            if pipe_log.exists() and pipe_log.stat().st_mtime >= t0 - 2 and "RunScan: finished" in read(pipe_log):
                finished = True
                break
            time.sleep(1)
        uncaught = [f"{q.name}: {ln.strip()[:160]}" for q in this_launch_logs(folder, t0)
                    for ln in read(q).splitlines() if "UNCAUGHT" in ln]
        try:
            method = c.request("get_pointers").get("gobjects_method")
        except PipeError as e:
            method = f"(get_pointers failed: {str(e)[:60]})"
        ok = finished and not uncaught
        note = (f"trigger_scan sent {sent:.1f}s after launch; RunScan finished={finished}; "
                f"gobjects_method={method}; UNCAUGHT lines={len(uncaught)}")
        for u in uncaught:
            note += f"\n      {u}"
    except PipeError as e:
        ok, note = False, f"pipe session: {e}"
    finally:
        c.close()
        subprocess.run(["taskkill", "/F", "/T", "/PID", str(p.pid)], capture_output=True)
        for _ in range(30):
            if not alive(p.pid):
                break
            time.sleep(1)
    print(f"  launch {n}: {'PASS' if ok else 'FAIL'}  {note}  (killed; alive afterwards: {alive(p.pid)})")
    return ok, note


def main(a: list[str]) -> int:
    if not a:
        raise SystemExit(__doc__)
    work = a[0]
    launches = int(a[1]) if len(a) > 1 else 5
    exe = a[2] if len(a) > 2 else "DumperTest51-Win64-Shipping.exe"
    live = any_shipping()
    if live:
        raise SystemExit(f"a fixture is already running -- one game at a time: {live}")
    if not (win64(work) / "version.dll").exists():
        raise SystemExit("no proxy in the fixture -- run path_shape_live.py deploy first")
    results = []
    for n in range(1, launches + 1):
        results.append(one_launch(work, exe, n)[0])
        time.sleep(3)
    print(f"\n{sum(results)}/{len(results)} launches clean")
    return 0 if all(results) else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
