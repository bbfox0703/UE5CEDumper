"""A4 half 2 -- make the 507 and 508 raise rungs actually FIRE, and read them off a live log.

    py tools/verify/a4_raise_ladder.py                 # dev58 (32-byte FUObjectItem)
    py tools/verify/a4_raise_ladder.py --flavour shipping58
    py tools/verify/a4_raise_ladder.py --flavour shipping56   # a real 5.6 binary: 504 -> 505 -> 506, and no further
    py tools/verify/a4_raise_ladder.py --restore-only  # if a previous run died mid-way

THE ROW. `UE5_Init` runs a raise-ONLY ladder every init: 503 (tagged FFieldVariant) -> 504
(CMC::SetGravityDirection) -> 505 (UEnum::EnumPackage) -> 506 (UStruct::StructStateFlags) -> 507
(reordered FUObjectItem) -> 508 (virtual ~FFieldClass). The 507 and 508 rungs had never been
observed firing, because they only run when the *cached* version is BELOW them -- and on a genuine
5.8 binary detection already answers 508, so the ladder has nothing to raise.

From a 504 seed on a 5.8 binary the 506 rung fires first [VER-MARKERS-PAST-504]: 5.8 keeps 5.6's
StructStateFlags. The 505 rung stays silent there by design -- 5.7 replaced UEnum::Names with
FNameData, which has no EnumPackage slot behind it, so its probe logs that it skipped.

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

FLAVOUR_EXE = {"dev58": "DumperTest58.exe", "shipping58": "DumperTest58-Win64-Shipping.exe",
               "shipping56": "DumperTest56-Win64-Shipping.exe"}
# What each fixture must climb to from the 504 seed. A 5.8 binary keeps 5.6's StructStateFlags but has FNameData
# (the 505 probe skips) and then the 507 / 508 markers; a 5.6 binary has the 5.5 enum layout and the 5.6 struct
# one, and must STOP at 506 -- a 507 or 508 line there is a false raise, so it is checked as a must-not.
EXPECT = {"dev58": (("506", "507", "508"), ()), "shipping58": (("506", "507", "508"), ()),
          "shipping56": (("505", "506"), ("507", "508"))}
SEED_VERSION = 504


def current_rev():
    """kVersionDetectLogicRev, read from source. NEVER type this number in."""
    m = re.search(r"kVersionDetectLogicRev\s*=\s*(\d+)",
                  GENAU_H.read_text(encoding="utf-8", errors="replace"))
    if not m:
        raise SystemExit("!! could not read kVersionDetectLogicRev from %s" % GENAU_H)
    return int(m.group(1))


def pe_hash(exe_path):
    """The hint cache's key for an exe: PE TimeDateStamp then SizeOfImage, 16 upper-case hex digits.

    A fixture repackaged N times leaves N entries under one gameName (DumperTest58 Shipping had 8 on
    2026-10-14), so the name alone cannot say which entry the next launch will read; its own hash can.
    """
    b = pathlib.Path(exe_path).read_bytes()[:4096]
    e = int.from_bytes(b[0x3C:0x40], "little")
    if b[e:e + 4] != b"PE\0\0":
        raise SystemExit("!! %s is not a PE image" % exe_path)
    stamp = int.from_bytes(b[e + 8:e + 12], "little")
    size_of_image = int.from_bytes(b[e + 24 + 56:e + 24 + 60], "little")
    return "%08X%08X" % (stamp, size_of_image)


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
        if len(targets) > 1:
            # Several builds of the fixture left entries under one name: seed the one this exe's own PE hash keys.
            sys.path.insert(0, str(HERE))
            from launch_dumpertest import FLAVOURS
            own = pe_hash(FLAVOURS[a.flavour])
            print("entries : %d under %s; this exe's PE hash is %s" % (len(targets), exe, own))
            targets = [k for k in targets if k.upper() == own]
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
        want = {"seed": False, "505": False, "506": False, "507": False, "508": False, "complete": False}
        seen = 0
        for p in sorted(ld.glob("*-0.log")) if ld.is_dir() else []:
            for ln in p.read_text(encoding="utf-8", errors="replace").splitlines():
                m = re.match(r"\[(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d{3})\]", ln)
                if not m or m.group(1) < watermark:
                    continue
                seen += 1
                if not re.search(r"UE Version|raising floor|structural marker|Complete \(UE"
                                 r"|EnumPackage probe|StructStateFlags probe", ln):
                    continue
                print("   ", ln.strip()[:168])
                # The seed is witnessed by the first rung REPORTING it, which is stronger than
                # the "cached, rev=" line: it proves the value reached UE5_Init, not merely
                # that Genau read something. Since [VER-MARKERS-PAST-504] that first rung is
                # the 506 one, so the 507 line now reports 506.
                if "structural marker" in ln and "version=%d" % SEED_VERSION in ln:
                    want["seed"] = True
                if "raising to 505" in ln:
                    want["505"] = True
                if "raising to 506" in ln:
                    want["506"] = True
                if "raising floor to 507" in ln:
                    want["507"] = True
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
            must, must_not = EXPECT[a.flavour]
            labels = {"seed": "the seed reached UE5_Init (rung reports version=%d)" % SEED_VERSION,
                      "505": "505 rung fired (UEnum::EnumPackage)",
                      "506": "506 rung fired (UStruct::StructStateFlags)",
                      "507": "507 rung fired (FUObjectItem Object@+0x08)",
                      "508": "508 rung fired (FFieldClass::Name@+0x08)",
                      "complete": "scan completed"}
            for k in ("seed",) + tuple(must) + ("complete",):
                print("  %-5s %s" % ("OK" if want[k] else "MISS", labels[k]))
                if not want[k]:
                    fails.append(labels[k])
            for k in must_not:
                print("  %-5s %s (must NOT fire on this fixture)" % ("BAD" if want[k] else "OK", labels[k]))
                if want[k]:
                    fails.append("false raise: " + labels[k])
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
