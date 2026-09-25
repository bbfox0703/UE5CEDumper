"""Live checks for [PATH-SHAPE-2026-09-25] on the PathShape fixture (tools/verify/path_shape_fixture.py), over the pipe.

    py tools/verify/path_shape_live.py deploy   <work dir>            # dist\\proxy\\version.dll -> Binaries\\Win64
    py tools/verify/path_shape_live.py run      <work dir> [index...] # each shape in turn: launch, check, KILL
    py tools/verify/path_shape_live.py undeploy <work dir>            # remove it (restores a backup if one was made)

WHAT `run` CHECKS, per exe-name shape (one game at a time; each is killed before the next starts):
  * the DLL answering is dist's build (pipe_client.assert_build -- a stale proxy serves a confident wrong answer);
  * `module_name` is the exe's REAL name, UTF-8 ([PATH-MODULE-NAME-UTF8]; it used to be '?' for every char >= 128);
  * `get_ce_pointer_info`'s `ce_base` names the module as CE does -- the ANSI Module32First view: narrowed to the
    system code page ('?' for a character it lacks) and cut after the LAST 0x5C byte (Big5 功 = A5 5C, so
    功夫-… is 夫-… to CE) ([PATH-CE-MODULE-VIEW] / MODVIEW-5C-TRAIL);
  * the log folder is Logs\\<Sein::ProcessFolderName(exe)> and its init-0.log was written by THIS launch
    ([PATH-SEIN-TRAILING-SPACE]: "… .exe" logs into "…", not the unopenable "… ").
The UI- and CE-side checks (trainer, XML, CE inject from non-ASCII folders) are driven separately.

Paths are arguments; nothing here reaches for a machine path. The expected CE name is computed with Python's codec for
the machine's ACP, which has no best-fit table: fine for these five shapes (none has a best-fit character).
"""
from __future__ import annotations

import ctypes
import json
import os
import pathlib
import shutil
import subprocess
import sys
import time

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient, PipeError  # noqa: E402
from path_shape_fixture import SHAPES, win64     # noqa: E402

ROOT = pathlib.Path(__file__).resolve().parents[2]
PROXY = ROOT / "dist" / "proxy" / "version.dll"
ARGS = ["-windowed", "-ResX=1280", "-ResY=720"]
BOOT_WAIT_S = 15   # seconds after the pipe answers, before the first trigger_scan
SCAN_TRIES = 3   # a stock 5.1 template: none of DumperTest's own switches
DETACHED = 0x00000008 | 0x00000200


def folder_name(exe: str) -> str:
    """Sein::ProcessFolderName."""
    stem = exe.rsplit(".", 1)[0] if "." in exe else exe
    stem = "".join("_" if c in '/\\:*?"<>|' else c for c in stem).rstrip(" .")
    return stem or "unknown"


def ce_name(exe: str) -> str:
    """ANSI Module32First's szModule, widened back: the ACP narrowing, cut after the last 0x5C byte."""
    acp = f"cp{ctypes.windll.kernel32.GetACP()}"
    b = exe.encode(acp, errors="replace")
    return b[b.rfind(b"\\") + 1:].decode(acp, errors="replace")


def tasklist(*filt):
    return subprocess.run(["tasklist", "/FO", "CSV", "/NH", *filt], capture_output=True, text=True,
                          encoding="mbcs", errors="replace").stdout or ""


def alive(pid):
    return f'"{pid}"' in tasklist("/FI", f"PID eq {pid}")


def any_shipping():
    """Every *-Win64-Shipping* process -- by a substring, because tasklist prints a name the console code page lacks
    as '?', so an exact match on 'DumperTest51™-…' would miss it."""
    return [ln for ln in tasklist().splitlines() if "-Win64-Shipping" in ln]


def deploy(work):
    dst = win64(work) / "version.dll"
    if dst.exists():
        bak = dst.with_name("version.dll.pathshape-bak")
        if not bak.exists():
            shutil.copy2(dst, bak)
            print(f"  backed up the existing version.dll -> {bak.name}")
    shutil.copy2(PROXY, dst)
    print(f"  deployed {PROXY} -> {dst}  (dist build {(ROOT / 'dist' / 'build_number.txt').read_text().strip()})")


def undeploy(work):
    dst = win64(work) / "version.dll"
    bak = dst.with_name("version.dll.pathshape-bak")
    if dst.exists():
        dst.unlink()
    if bak.exists():
        bak.rename(dst)
        print("  restored the backed-up version.dll")
    print("  undeployed")


def first_addr(obj):
    """The first 0x… object address anywhere in a find_instances reply."""
    if isinstance(obj, dict):
        for k in ("addr", "address"):
            v = obj.get(k)
            if isinstance(v, str) and v.startswith("0x") and int(v, 16):
                return v
        for v in obj.values():
            a = first_addr(v)
            if a:
                return a
    elif isinstance(obj, list):
        for v in obj:
            a = first_addr(v)
            if a:
                return a
    return None


def field(reply, key):
    return reply.get(key, (reply.get("data") or {}).get(key))


def check_one(work, exe):
    results = []

    def rec(name, ok, got):
        results.append((name, ok, got))
        print(f"    {'PASS' if ok else 'FAIL'}  {name}   got: {got}")

    path = win64(work) / exe
    t0 = time.time()
    p = subprocess.Popen([str(path)] + ARGS, cwd=str(path.parent), creationflags=DETACHED)
    print(f"  launched pid {p.pid}: {exe}")
    c = PipeClient(timeout=180)
    try:
        c.connect(retries=90, delay=1.0)   # not `with`: __enter__ connects again, with only 10 retries
        c.assert_build()
        # Let the engine BOOT first (handover 3: a process that exists is not a game that booted). Measured
        # 2026-09-25: a trigger_scan ~1 s after launch hit 'RunScan: UNCAUGHT non-standard exception -- contained'
        # in 4 of 5 launches and never rescanned. Wait, then retrigger a scan that came back empty.
        time.sleep(BOOT_WAIT_S)
        ptr = None
        for attempt in range(1, SCAN_TRIES + 1):
            try:
                ptr = c.ensure_scanned(timeout=60)
                break
            except PipeError as e:
                print(f"    scan attempt {attempt} came back empty ({str(e)[:60]}...); retrying")
                time.sleep(10)
        if ptr is None:
            raise PipeError(f"no pointers after {SCAN_TRIES} scans")
        rec("module_name is the real name (UTF-8)", field(ptr, "module_name") == exe, field(ptr, "module_name"))
        inst = c.request("find_instances", class_name="World", max_results=4)
        addr = first_addr(inst)
        if not addr:
            rec("a World object to ask get_ce_pointer_info about", False, json.dumps(inst)[:200])
        else:
            info = c.request("get_ce_pointer_info", addr=addr, field_offset=0)
            ce_base = field(info, "ce_base") or ""
            want = f'"{ce_name(exe)}"+'
            rec(f"ce_base names the module as CE does ({ce_name(exe)})", ce_base.startswith(want), ce_base)
        log = pathlib.Path(os.environ["LOCALAPPDATA"]) / "UE5CEDumper" / "Logs" / folder_name(exe) / "init-0.log"
        fresh = log.exists() and log.stat().st_mtime >= t0 - 2
        rec(f"this launch logged into Logs\\{folder_name(exe)}\\init-0.log", fresh,
            f"{log} exists={log.exists()}" + (f" mtime-t0={log.stat().st_mtime - t0:.1f}s" if log.exists() else ""))
    except PipeError as e:
        rec("pipe session", False, str(e))
    finally:
        c.close()
        subprocess.run(["taskkill", "/F", "/T", "/PID", str(p.pid)], capture_output=True)
        for _ in range(30):
            if not alive(p.pid):
                break
            time.sleep(1)
        print(f"  killed pid {p.pid}; alive afterwards: {alive(p.pid)}")
    return results


def run(work, picks):
    names = list(SHAPES)
    chosen = [names[int(i)] for i in picks] if picks else names
    live = any_shipping()
    if live:
        raise SystemExit(f"a fixture is already running -- one game at a time: {live}")
    all_results = []
    for exe in chosen:
        all_results += [(exe, *r) for r in check_one(work, exe)]
        time.sleep(3)
    bad = [r for r in all_results if not r[2]]
    print(f"\n{len(all_results) - len(bad)}/{len(all_results)} checks passed over {len(chosen)} shape(s)")
    return 1 if bad else 0


if __name__ == "__main__":
    verb = sys.argv[1] if len(sys.argv) > 1 else ""
    if verb == "deploy" and len(sys.argv) == 3:
        deploy(sys.argv[2])
    elif verb == "undeploy" and len(sys.argv) == 3:
        undeploy(sys.argv[2])
    elif verb == "run" and len(sys.argv) >= 3:
        sys.exit(run(sys.argv[2], sys.argv[3:]))
    else:
        raise SystemExit(__doc__)
