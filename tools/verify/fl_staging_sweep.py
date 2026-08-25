"""FL1 / FL2: the abandoned-staging-file sweep, with its own negative control.

    py fl_staging_sweep.py

THE FIX (audit L4, build 3263, `Flamme.cpp:139-168`). A failed hint-cache write used
to leak `<cache>.tmp.<pid>` forever. The cache now sweeps its OWN `"<name>.tmp."`
prefix -- never a folder wildcard -- and only files older than **1 hour**.

WHY THE AGE GUARD IS THE WHOLE POINT, AND WHY THIS RIG PLANTS TWO FILES. The UI writes
its own `<file>.tmp.<pid>` concurrently (`AobUsageService.cs:139`). A sweep without the
age guard would delete a *live* write in progress. So testing only "the stale one is
gone" would pass just as happily on a sweep that deletes EVERYTHING -- which is the
dangerous version of this code. This plants both:

    <cache>.tmp.99999   mtime 3 h ago   MUST be deleted
    <cache>.tmp.88888   mtime now       MUST survive

⚠ Neither planted name can collide with a real writer: the suffix is a PID, and 99999
/ 88888 are above Windows' practical PID range for a running process here -- checked
against the live process list before planting, and refused if either is in use.

FL1's other half is the production negative control: two scans back to back must
produce `HintCache: Saved results ... scan #N` and NO `staged write is incomplete`.
The refuse-on-failure gate must not refuse a legitimate write, and the unit test only
covers the predicate, not the write path.
"""
import os
import pathlib
import subprocess
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient                       # noqa: E402

APPDATA = pathlib.Path.home() / "AppData/Local/UE5CEDumper"
CACHE = APPDATA / "UE5CEDumper.MSI-NB.json"
STALE = APPDATA / (CACHE.name + ".tmp.99999")
FRESH = APPDATA / (CACHE.name + ".tmp.88888")
LOGROOT = APPDATA / "Logs"


def live_pids():
    out = subprocess.run(["tasklist", "/FO", "CSV", "/NH"],
                         capture_output=True, text=True, errors="replace").stdout
    pids = set()
    for line in out.splitlines():
        parts = [x.strip('"') for x in line.split('","')]
        if len(parts) >= 2 and parts[1].isdigit():
            pids.add(parts[1])
    return pids


def main(logdir_name):
    pids = live_pids()
    for p in ("99999", "88888"):
        if p in pids:
            raise SystemExit(f"fl: FAILED -- pid {p} is actually running; pick another suffix "
                             f"or the plant could collide with a real staging write")

    before_cache = CACHE.read_bytes()
    STALE.write_text('{"planted":"stale"}', encoding="utf-8")
    FRESH.write_text('{"planted":"fresh"}', encoding="utf-8")
    old = time.time() - 3 * 3600
    os.utime(STALE, (old, old))
    print(f"planted:\n  {STALE.name}  mtime {time.ctime(STALE.stat().st_mtime)}  (must be DELETED)")
    print(f"  {FRESH.name}  mtime {time.ctime(FRESH.stat().st_mtime)}  (must SURVIVE)")

    # ⚠ A FRESH PROCESS IS MANDATORY, and this is the trap that made the first run of
    # this rig report a false FAIL. SweepOrphanTemps holds
    # `static std::atomic<bool> s_swept` and so runs AT MOST ONCE PER PROCESS
    # (Flamme.cpp:136). In an already-injected game it has therefore already run --
    # before you planted anything -- and `trigger_scan` does not re-run it. Worse,
    # `trigger_scan` on an already-scanned process does not even re-save the hint
    # cache, so BOTH the sweep line and the "Saved results" line are legitimately
    # absent and the run reads as a total failure of a working fix.
    #
    # A python.exe sleeper is enough: every scan saves a hint entry, even one that
    # resolves nothing (`HintCache: Saved results for PE=... (python.exe, scan #1)`).
    root = pathlib.Path(__file__).resolve().parents[2]
    # Read the WHOLE fresh log, never a byte-offset slice of the old one: `scan-0.log`
    # is a SLOT NAME, not a file identity -- each process start archives the previous
    # run and begins a new one, so an offset taken before the launch indexes into a
    # different, shorter file and silently discards the very lines being looked for.
    log = LOGROOT / "python" / "scan-0.log"

    print("\nlaunching a FRESH host (the sweep is once-per-process) ...")
    proc = subprocess.Popen([sys.executable, "-c", "import time;time.sleep(300)"],
                            creationflags=0x00000008 | 0x00000200)
    time.sleep(2)
    r = subprocess.run([sys.executable, str(root / "tools/verify/inject.py"),
                        "--pid", str(proc.pid)],
                       capture_output=True, text=True, errors="replace")
    if r.returncode != 0:
        subprocess.run(["taskkill", "/F", "/PID", str(proc.pid)], capture_output=True)
        raise SystemExit(f"fl: FAILED -- inject: {r.stdout}{r.stderr}")
    print(f"  injected into pid {proc.pid}; waiting for the scan to save ...")
    deadline = time.time() + 120
    while time.time() < deadline:
        if log.is_file() and "HintCache: Saved results" in log.read_text(
                encoding="utf-8", errors="replace"):
            break
        time.sleep(2)
    time.sleep(2)
    subprocess.run(["taskkill", "/F", "/PID", str(proc.pid)], capture_output=True)

    new = log.read_text(encoding="utf-8", errors="replace") if log.is_file() else ""
    ok = True

    print("\n--- FL2: the sweep ---")
    stale_gone, fresh_alive = not STALE.exists(), FRESH.exists()
    print(f"  stale .tmp.99999 deleted : {stale_gone}   (expect True)")
    print(f"  fresh .tmp.88888 survived: {fresh_alive}   (expect True -- the age guard)")
    swept = [l for l in new.splitlines() if "abandoned staging file" in l]
    print(f"  log line: {swept[0].strip() if swept else '*** ABSENT ***'}")
    if not stale_gone or not fresh_alive or not swept:
        ok = False

    print("\n--- FL1: the production write must NOT be refused ---")
    saved = [l for l in new.splitlines() if "HintCache: Saved results" in l]
    refused = [l for l in new.splitlines() if "staged write is incomplete" in l]
    for l in saved[-2:]:
        print("  ", l.strip()[-110:])
    print(f"  'staged write is incomplete' lines: {len(refused)}  (expect 0)")
    if not saved or refused:
        ok = False

    print("\n--- the real cache must be intact ---")
    after = CACHE.read_bytes()
    import json
    try:
        n_before = len(json.loads(before_cache.decode("utf-8"))["games"])
        n_after = len(json.loads(after.decode("utf-8"))["games"])
        print(f"  parses: True   entries {n_before} -> {n_after}")
        if n_after < n_before:
            print("  *** entries LOST ***")
            ok = False
    except Exception as e:
        print(f"  *** cache no longer parses: {e} ***")
        ok = False

    FRESH.unlink(missing_ok=True)
    STALE.unlink(missing_ok=True)
    print(f"\nFL1/FL2: {'PASS' if ok else 'FAIL'}")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1] if len(sys.argv) > 1 else "DumperTest"))
