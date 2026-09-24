#!/usr/bin/env python3
r"""S11 (L40 `[P1-GENAU-ABORT]` control, L52 `[A2-HEAP-ANCHOR-TEXT]`): one UNINTERRUPTED cold recovery scan, read from the logs.

    py tools/verify/s11_recovery_scan.py --dll out\staged\s11-force-recovery\UE5Dumper.dll --tag green
    py tools/verify/s11_recovery_scan.py --dll out\staged\l40-hoist-mutant\UE5Dumper.dll    --tag l40-red
    py tools/verify/s11_recovery_scan.py --dll out\staged\l52-heap-text-prefix\UE5Dumper.dll --tag l52-red

The staged DLLs carry genau_rip_recovery_ab.py's FORCE_GOBJ / FORCE_GNAM, so ScanForTarget's result is zeroed AFTER it
has run (its Pass-2 refusals included): GObjects falls to the data-scan tier (a HEAP FUObjectArray) and GNames to the
string-ref tier. One run:
  1. refuses unless the hint cache holds NO record for the DumperTest Development exe's PE key (a hint HIT skips every
     batch pass, so L52's Pass-2 refusals need a cold GNames scan), and copies the JSON aside;
  2. launches DumperTest DEVELOPMENT (launch_dumpertest.py dev) and injects --dll (inject.py) -- no UI, no CE, nothing
     touches the pipe, so the scan is uninterrupted, which is what L40's control needs;
  3. waits until init-0.log shows the init latch (`UE5_AutoStart: … initState=2`) or its refusal
     (`UE5_Init: scan was cancelled`), then kills the game;
  4. restores the hint JSON byte-exact (a staged run writes data_scan / string_ref hints for this PE) and prints
     every expected and MUST-BE-ABSENT line it found in scan-0.log / init-0.log, with line numbers.
It judges nothing; it prints the evidence. Name the DLL by the `Module identity … path:` line it prints: a staged DLL
keeps HEAD's build stamp.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import pathlib
import shutil
import struct
import subprocess
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parents[1]
sys.stdout.reconfigure(encoding="utf-8", errors="replace")
EXE = pathlib.Path(r"D:\UE_Analyze_data\for testing\DumperTest\Development\Windows\DumperTest\Binaries\Win64\DumperTest.exe")
APPDATA = pathlib.Path(os.environ["LOCALAPPDATA"]) / "UE5CEDumper"
HINTS = APPDATA / ("UE5CEDumper.%s.json" % os.environ.get("COMPUTERNAME", ""))
LOGS = APPDATA / "Logs" / "DumperTest"

EXPECT = [
    "Module identity",
    "FindGObjects: All patterns failed, trying data-section scan fallback",
    "DataScanGObjectsCandidates: Collecting",
    "Module anchor set",
    "FindGNames: All patterns failed, trying string-ref fallback",
    "FindGNames: String-ref failed, trying pointer scan fallback",
    "REFUSED",
    "UE5_Init: Complete (",
    "initState=2",
]
ABSENT = [
    "DataScanGObjectsCandidates: aborted (client gone / shutdown)",
    "FindGObjectsStaticStruct: aborted (client gone / shutdown)",
    "FindGNamesByPointerScan: aborted (client gone / shutdown)",
    "FindGNamesByStringRef: aborted (client gone / shutdown)",
    "UE5_Init: scan was cancelled",
    "FindAll: scan was CANCELLED",
    "multi-module fallback CANCELLED",
]


def pe_key(p):
    b = p.read_bytes()[:4096]
    pe = struct.unpack_from("<I", b, 0x3C)[0]
    return "%08X%08X" % (struct.unpack_from("<I", b, pe + 8)[0], struct.unpack_from("<I", b, pe + 24 + 56)[0])


def sha8(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()[:8]


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--dll", required=True)
    ap.add_argument("--tag", required=True)
    ap.add_argument("--timeout", type=float, default=600.0)
    a = ap.parse_args()
    dll = pathlib.Path(a.dll).resolve()
    key = pe_key(EXE)
    games = json.loads(HINTS.read_text(encoding="utf-8")).get("games", {})
    if key in games:
        print("REFUSED: the hint cache holds a record for %s (%s) -- the scan would not be cold" % (EXE.name, key))
        return 2
    bak = ROOT / "out" / ("s11_hints_%s.json" % a.tag)
    shutil.copy2(HINTS, bak)
    print("dll %s sha %s | PE key %s (no record) | hint cache sha %s backed up" % (dll, sha8(dll), key, sha8(HINTS)))
    t0 = time.time()
    try:
        subprocess.run([sys.executable, str(HERE / "launch_dumpertest.py"), "dev"], check=True)
        subprocess.run([sys.executable, str(HERE / "inject.py"), "--name", "DumperTest", "--dll", str(dll)], check=True)
        init = LOGS / "init-0.log"
        done = None
        while time.time() - t0 < a.timeout:
            time.sleep(2)
            txt = init.read_text(encoding="utf-8", errors="replace") if init.exists() else ""
            if str(dll) not in txt:
                continue          # still the previous run's log
            if "initState=2" in txt or "UE5_Init: scan was cancelled" in txt:
                done = time.time() - t0
                break
        print("init ended after %s s" % ("%.0f" % done if done else "TIMEOUT"))
        time.sleep(3)
    finally:
        subprocess.run(["taskkill", "/F", "/IM", "DumperTest.exe"], capture_output=True)
        time.sleep(3)
        shutil.copy2(bak, HINTS)
        print("hint cache restored: sha %s (backup %s)" % (sha8(HINTS), sha8(bak)))
    for name in ("init-0.log", "scan-0.log"):
        lines = (LOGS / name).read_text(encoding="utf-8", errors="replace").splitlines()
        print("---- %s (%d lines)" % (name, len(lines)))
        for i, ln in enumerate(lines, 1):
            if any(s in ln for s in EXPECT):
                print("  %5d  %s" % (i, ln[:260]))
        for s in ABSENT:
            hits = [i for i, ln in enumerate(lines, 1) if s in ln]
            print("  %s  %r%s" % ("PRESENT" if hits else "absent ", s, (" at " + ",".join(map(str, hits))) if hits else ""))
    return 0


if __name__ == "__main__":
    sys.exit(main())
