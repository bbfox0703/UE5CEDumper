"""A4 half 2 -- make the 507 and 508 raise rungs actually FIRE, and read them off a live log.

    py tools/verify/a4_raise_ladder.py                 # dev58 (32-byte FUObjectItem)
    py tools/verify/a4_raise_ladder.py --flavour shipping58
    py tools/verify/a4_raise_ladder.py --restore-only  # if a previous run died mid-way

THE ROW. `UE5_Init` runs a raise-ONLY ladder every init: 503 (tagged FFieldVariant) -> 504
(CMC::GravityDirection) -> 507 (reordered FUObjectItem) -> 508 (virtual ~FFieldClass). The 507
and 508 rungs had never been observed firing, because they only run when the *cached* version is
BELOW them -- and on a genuine 5.8 binary detection already answers 508, so the ladder has nothing
to raise.

⭐ WHY NO REBUILD AND NO REPACKAGE. The input is data. Seed the hint cache with `ueVersion: 504`
for this game and the ladder has somewhere to climb from. This is legitimate rather than a lie the
code could detect: 504 is exactly what a stale cache from an older detection rev WOULD contain, and
audit A4 already established the ladder never writes back (it runs AFTER Flamme::SaveResults), so
the seed cannot be laundered into a persistent wrong answer.

THREE THINGS THAT WOULD SILENTLY MEASURE NOTHING, each guarded below:

  1. **The rev stamp must be current.** `Genau.cpp` honours a cached version only when
     `hints.versionDetectRev == kVersionDetectLogicRev`; otherwise it re-detects and re-stamps,
     the seed is discarded, detection answers 508, and the ladder has nothing to do -- a PASS-shaped
     nothing. The stamp is READ FROM `dll/src/Genau.h` at run time, never typed in: it was 5, then
     6, then 7 within two days.
  2. **The entry must NOT carry a user override.** `Genau.cpp`'s `hints.hasUserOverride` branch is
     checked FIRST and wins outright, skipping the cached-version branch entirely.
  3. **The DLL must be the one that has the rungs.** `inject.py` already refuses a stale
     `dist/UE5Dumper.dll` unless --allow-stale.

⚠ RESTORES THE CACHE BYTE-FOR-BYTE in a finally, and verifies the bytes came back. That file holds
45 games' scan hints; corrupting it would silently re-scan every one of them.
"""
import argparse
import json
import os
import pathlib
import re
import subprocess
import sys
import time

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

HERE = pathlib.Path(__file__).resolve().parent
REPO = HERE.parents[1]
CACHE = (pathlib.Path(os.environ["LOCALAPPDATA"]) / "UE5CEDumper"
         / ("UE5CEDumper.%s.json" % os.environ.get("COMPUTERNAME", "")))
GENAU_H = REPO / "dll" / "src" / "Genau.h"

FLAVOUR_EXE = {"dev58": "DumperTest58.exe", "shipping58": "DumperTest58-Win64-Shipping.exe"}
SEED_VERSION = 504


def current_rev():
    """kVersionDetectLogicRev, read from source. NEVER type this number in."""
    m = re.search(r"kVersionDetectLogicRev\s*=\s*(\d+)",
                  GENAU_H.read_text(encoding="utf-8", errors="replace"))
    if not m:
        raise SystemExit("!! could not read kVersionDetectLogicRev from %s" % GENAU_H)
    return int(m.group(1))


def log_dir(flavour):
    stem = FLAVOUR_EXE[flavour][:-4]
    return pathlib.Path(os.environ["LOCALAPPDATA"]) / "UE5CEDumper" / "Logs" / stem


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--flavour", default="dev58", choices=sorted(FLAVOUR_EXE))
    ap.add_argument("--restore-only", action="store_true",
                    help="put the cache back from the .a4bak snapshot and exit")
    ap.add_argument("--wait", type=int, default=30)
    a = ap.parse_args()

    bak = CACHE.with_suffix(".json.a4bak")

    if a.restore_only:
        if not bak.exists():
            print("no snapshot at %s -- nothing to restore" % bak)
            return 1
        CACHE.write_bytes(bak.read_bytes())
        print("restored %d bytes from %s" % (CACHE.stat().st_size, bak.name))
        bak.unlink()
        return 0

    if not CACHE.exists():
        raise SystemExit("!! no hint cache at %s" % CACHE)
    original = CACHE.read_bytes()
    rev = current_rev()
    exe = FLAVOUR_EXE[a.flavour]
    print("cache   : %s (%d bytes)" % (CACHE.name, len(original)))
    print("rev     : kVersionDetectLogicRev = %d  (read from Genau.h, not typed)" % rev)
    print("flavour : %s -> %s" % (a.flavour, exe))

    bak.write_bytes(original)          # survives a hard kill of this script
    fails = []
    try:
        doc = json.loads(original.decode("utf-8"))
        targets = [k for k, v in doc.get("games", {}).items()
                   if v.get("gameName") == exe]
        if len(targets) != 1:
            raise SystemExit("!! expected exactly 1 cache entry for %s, found %d -- refusing "
                             "to guess which to seed" % (exe, len(targets)))
        key = targets[0]
        rec = doc["games"][key]
        was = (rec.get("ueVersion"), rec.get("versionDetectRev"))

        # Guard 2: an override would win outright and the seed would never be read.
        for bad in ("ueVersionUserOverride", "userOverrideVersion", "isUserOverride"):
            if rec.get(bad):
                raise SystemExit("!! entry carries %s=%r -- Genau checks the override branch "
                                 "FIRST, so the seed would be ignored" % (bad, rec[bad]))

        rec["ueVersion"] = SEED_VERSION
        rec["versionDetectRev"] = rev
        rec["versionDetected"] = True
        CACHE.write_text(json.dumps(doc, indent=2), encoding="utf-8")
        print("seeded  : %s  ueVersion %s -> %d, versionDetectRev %s -> %d"
              % (key, was[0], SEED_VERSION, was[1], rev))

        # ---- launch ----------------------------------------------------------
        # ⛔⛔ DO NOT SLICE THESE LOGS BY A BYTE OFFSET TAKEN BEFORE THE LAUNCH. The first
        # version of this rig did exactly that and reported all four checks MISS on a run
        # whose log plainly contained all three lines -- because a new process ROTATES the
        # log, so the new file is SHORTER than the offset and everything got skipped. That
        # is a false FAIL on a genuine PASS, and CLAUDE.md lists this precise variant
        # ("a byte offset recorded before a process start that rotates the log").
        #
        # The watermark is a TIMESTAMP instead, parsed from each line's own `[YYYY-MM-DD
        # HH:MM:SS.mmm]` prefix, taken before the launch and compared with millisecond
        # precision. A one-second watermark would be the other documented variant of this
        # mistake; these lines land ~10 s after launch, and the compare is sub-second.
        ld = log_dir(a.flavour)
        watermark = time.strftime("%Y-%m-%d %H:%M:%S") + ".000"
        print("\nlog watermark: %s" % watermark)
        print("launching %s ..." % a.flavour)
        r = subprocess.run([sys.executable, str(HERE / "launch_dumpertest.py"), a.flavour,
                            "--wait", str(a.wait)],
                           capture_output=True, text=True, encoding="utf-8", errors="replace")
        print(r.stdout.strip()[-400:])
        if r.returncode != 0:
            print(r.stderr.strip()[-400:])
            raise SystemExit("!! launch failed -- nothing below measures anything")

        # ---- inject ----------------------------------------------------------
        print("\ninjecting ...")
        r = subprocess.run([sys.executable, str(HERE / "inject.py"), "--name", exe[:-4]],
                           capture_output=True, text=True, encoding="utf-8", errors="replace")
        print((r.stdout or "").strip()[-600:])
        if r.returncode != 0:
            print((r.stderr or "").strip()[-600:])
            fails.append("injection failed")
        time.sleep(12)

        # ---- read the ladder off the log ------------------------------------
        print("\n--- version lines written after the watermark ---")
        want = {"seed": False, "507": False, "508": False, "complete": False}
        seen = 0
        for p in sorted(ld.glob("*-0.log")) if ld.is_dir() else []:
            for ln in p.read_text(encoding="utf-8", errors="replace").splitlines():
                m = re.match(r"\[(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d{3})\]", ln)
                if not m or m.group(1) < watermark:
                    continue
                seen += 1
                if not re.search(r"UE Version|raising floor|structural marker|Complete \(UE", ln):
                    continue
                print("   ", ln.strip()[:168])
                # The seed is witnessed by the 507 rung REPORTING it, which is stronger than
                # the "cached, rev=" line: it proves the value reached UE5_Init, not merely
                # that Genau read something.
                if "raising floor to 507" in ln:
                    want["507"] = True
                    if "version=%d" % SEED_VERSION in ln:
                        want["seed"] = True
                if "raising floor to 508" in ln:
                    want["508"] = True
                if "Complete (UE" in ln:
                    want["complete"] = True

        # ⚠ A detector must be shown able to FIRE before its silence means anything.
        # Zero lines past the watermark means the WINDOW is wrong, not that the ladder
        # is broken -- report that as its own failure rather than four confident MISSes.
        if seen == 0:
            fails.append("read 0 log lines after the watermark -- the window is wrong, or the "
                         "game never logged; NOTHING below this measures the ladder")
        else:
            print("\n  (%d log lines in the window)" % seen)
            for k, label in (("seed", "the seed reached UE5_Init (rung reports version=%d)"
                                      % SEED_VERSION),
                             ("507", "507 rung fired (FUObjectItem Object@+0x08)"),
                             ("508", "508 rung fired (FFieldClass::Name@+0x08)"),
                             ("complete", "scan completed")):
                print("  %-5s %s" % ("OK" if want[k] else "MISS", label))
                if not want[k]:
                    fails.append(label)
    finally:
        # ---- kill the game, then put the cache back --------------------------
        subprocess.run(["taskkill", "/F", "/IM", FLAVOUR_EXE[a.flavour]],
                       capture_output=True, text=True)
        CACHE.write_bytes(original)
        back = CACHE.read_bytes()
        ok = back == original
        print("\ncache restored byte-for-byte: %s" % ("OK" if ok else "*** MISMATCH ***"))
        if ok and bak.exists():
            bak.unlink()

    print("\nA4 half 2: %s" % ("PASS" if not fails else "FAIL -- " + "; ".join(fails)))
    return 0 if not fails else 1


if __name__ == "__main__":
    raise SystemExit(main())
