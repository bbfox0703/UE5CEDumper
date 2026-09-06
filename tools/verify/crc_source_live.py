"""Acceptance test for the CrashReportClient version source, on a MANUFACTURED case.

    py crc_source_live.py agree      # plant the UE_5.4 CRC beside a UE 5.4 fixture -> must AGREE
    py crc_source_live.py disagree   # plant the UE_5.7 CRC instead      -> must WARN and take 507
    py crc_source_live.py clean      # remove the planted file, restore the cache

⭐ WHY MANUFACTURE IT. Only 8 of 66 game folders on this machine ship a CrashReportClient, and
DumperTest -- the one fixture that can be launched in seconds, repeatedly, without Steam -- is not
one of them. working-lessons.md 1.aa: when no host has the case, MANUFACTURE it. Planting a known
Editor binary is strictly better than launching a real title here, because it lets the DISAGREEMENT
branch be tested at all: no installed game disagrees with our detector except DragonSword, whose
disagreement comes from the runtime ladder rather than from the resource.

⚠ THE DISAGREE RUN IS THE NEGATIVE CONTROL, and it is the half that actually proves something
(1.2). The agree run passes even if the new code never executes -- detection already returned 504
for this fixture. Only the disagree run can distinguish "CrashReportClient was read and won" from
"nothing happened".

⚠ Each run must start from a MISS in the hint cache, or FindAll skips detection entirely and the
test measures nothing. The fixture's record is removed before every launch and the whole cache is
backed up first.
"""
import argparse
import glob
import json
import os
import pathlib
import shutil
import subprocess
import sys
import time

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
HERE = pathlib.Path(__file__).resolve().parent
REPO = HERE.parent.parent

FIXTURE_ROOT = pathlib.Path(r"D:\UE_Analyze_data\for testing\DumperTest\Development\Windows")
PLANT_DIR = FIXTURE_ROOT / "Engine" / "Binaries" / "Win64"
PLANT = PLANT_DIR / "CrashReportClient.exe"
SOURCES = {
    "agree":    (r"C:\Program Files\Epic Games\UE_5.4\Engine\Binaries\Win64\CrashReportClient.exe", 504),
    "disagree": (r"C:\Program Files\Epic Games\UE_5.7\Engine\Binaries\Win64\CrashReportClient.exe", 507),
}
LOGDIR = pathlib.Path(os.path.expandvars(r"%LOCALAPPDATA%\UE5CEDumper\Logs\DumperTest"))
BACKUP = REPO / "out" / "cache-before-crc-live.json"


def cache_path():
    hits = glob.glob(os.path.expandvars(r"%LOCALAPPDATA%\UE5CEDumper\UE5CEDumper.*.json"))
    return pathlib.Path(hits[0]) if hits else None


def drop_fixture_record():
    """Force a cache MISS, or FindAll skips DetectVersion and the run measures nothing."""
    cp = cache_path()
    if not cp:
        return 0
    BACKUP.parent.mkdir(parents=True, exist_ok=True)
    if not BACKUP.exists():
        shutil.copy2(cp, BACKUP)
    d = json.loads(cp.read_text(encoding="utf-8"))
    drop = [k for k, v in d.get("games", {}).items()
            if isinstance(v, dict) and str(v.get("gameName", "")).lower() == "dumpertest.exe"]
    for k in drop:
        del d["games"][k]
    cp.write_text(json.dumps(d, indent=2) + "\n", encoding="utf-8", newline="\n")
    return len(drop)


def run(mode):
    src, want = SOURCES[mode]
    if not os.path.isfile(src):
        print("!! source CrashReportClient missing: %s" % src)
        return 1
    PLANT_DIR.mkdir(parents=True, exist_ok=True)
    shutil.copy2(src, PLANT)
    print("planted %s -> %s" % (os.path.basename(src), PLANT))
    print("dropped %d fixture cache record(s)" % drop_fixture_record())

    subprocess.run([sys.executable, str(HERE / "launch_dumpertest.py"), "dev"],
                   check=False, capture_output=True, text=True)
    pid = (REPO / "out" / "host.pid").read_text().strip()
    subprocess.run([sys.executable, str(HERE / "inject.py"), "--pid", pid],
                   check=False, capture_output=True, text=True)
    time.sleep(6)

    scan = (LOGDIR / "scan-0.log").read_text(encoding="utf-8", errors="replace")
    lines = [l for l in scan.splitlines()
             if "CrashReportClient" in l or "SOURCES DISAGREE" in l or "AGREE on" in l
             or "UE Version =" in l or "PE VERSIONINFO" in l]
    print("\n--- decisive log lines ---")
    for l in lines:
        print("  " + l.split("] ", 2)[-1][:150])

    ok = True
    if mode == "agree":
        ok &= any("AGREE on 504" in l for l in lines)
        ok &= any("UE Version = 504" in l for l in lines)
    else:
        ok &= any("SOURCES DISAGREE" in l for l in lines)
        ok &= any("UE Version = 507" in l for l in lines)
    print("\n%s: %s (wanted %d)" % (mode, "PASS" if ok else "FAIL", want))

    subprocess.run(["taskkill", "/PID", pid, "/F"], capture_output=True)
    return 0 if ok else 1


def clean():
    if PLANT.exists():
        PLANT.unlink()
        print("removed planted %s" % PLANT)
    try:
        PLANT_DIR.rmdir(); PLANT_DIR.parent.rmdir(); PLANT_DIR.parent.parent.rmdir()
        print("removed the empty Engine\\Binaries\\Win64 chain it needed")
    except OSError:
        pass
    if BACKUP.exists():
        cp = cache_path()
        if cp:
            shutil.copy2(BACKUP, cp)
            print("restored the hint cache from %s" % BACKUP)
        BACKUP.unlink()
    return 0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("mode", choices=["agree", "disagree", "clean"])
    a = ap.parse_args()
    return clean() if a.mode == "clean" else run(a.mode)


if __name__ == "__main__":
    raise SystemExit(main())
