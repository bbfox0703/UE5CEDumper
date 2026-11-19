"""Score a PE for PRE-UE4 (Unreal Engine 3) markers — the offline twin of the DLL's counter.

    py tools/pe/pre_ue4_markers.py <exe> [<exe> ...]
    py tools/pe/pre_ue4_markers.py --corpus [<root>]

Mirrors `CountPreUE4Markers()` in dll/src/Genau.cpp. That C++ function decides whether the DLL
REFUSES to scan a game, so its false-positive rate has to be measurable without launching 30
games. There is no C++ unit-test project in this repo, so this script plus a live run is the
whole verification story — keep the two marker tables in step by hand.

THE FOUR MARKERS (2 of 4 refuses, matching Grimoire::PRE_UE4_SENTINEL_VERSION's gate):

    UnrealEngine3      ASCII or UTF-16LE   the engine's own name
    SeqAct_            ASCII or UTF-16LE   UE3 Kismet's native-registration table
    PhysXLoader64      ASCII               PhysX 2.8 loader, never used by UE4
    Epic copyright     PE VERSIONINFO      "Epic Games" AND a year <= 2013

`SeqAct_` is the strongest: it is the object model itself, not a version string a publisher can
strip. UE4 deleted Kismet in favour of Blueprints, so it measures 0 across every UE4/UE5 build.

The copyright year tracks the ENGINE SNAPSHOT, not the ship date — Gal*Gun: Double Peace shipped
2015 and still reads "Copyright 1998-2012 Epic Games, Inc." UE4.0 went public in March 2014, so
an Epic notice ending 2013 or earlier cannot be UE4/UE5. The inference is ONE-DIRECTIONAL: a
2014+ year proves nothing, since late UE3 games shipped well past 2014.

MEASURED, and what each number is for:

    30 reference builds, UE 4.10-5.8            all four markers 0  -> zero false positives
    35 installed UE games                       only the UE3 title scores
    Gal*Gun: Double Peace (UE3, x64)            4 of 4

    REJECTED "UE3"   3-85 hits in EVERY one of the 30 supported binaries. Using it would
                     refuse the whole corpus. Short UE tokens are known-noisy here — see
                     HasUEAnchorNearby's kAnchors.
    REJECTED ".nep"  nonstandard section names are not a discriminator: 27 of the 30 supported
                     binaries carry one (.uedbg, .lpp_pre, .msvcjmc, .detourc, ...).
    NEAR MISS        Manor Lords (UE 5.5, supported) ships "Copyright Epic Games, Inc. All
                     Rights Reserved." with NO year. This is why an explicit year is REQUIRED
                     rather than "no year or an old year" — that phrasing would refuse it.

--corpus walks the reference-build tree the way tools/ghidra/inventory_builds.py does and asserts
every binary scores below the threshold. It needs the ~200 GB corpus, so like blocktest.py it
cannot run in CI; run it by hand after touching the marker table. Set REFBUILD_ROOT to override.
"""
import os
import pathlib
import re
import sys

LAST_PRE_UE4_COPYRIGHT_YEAR = 2013
MARKER_COUNT = 4
MARKER_THRESHOLD = 2

DEFAULT_REFBUILD_ROOT = r"D:\UE_Analyze_Data\Varies Version builds"

# (label, needle, also_check_utf16)
STRING_MARKERS = [
    ("UnrealEngine3", b"UnrealEngine3", True),
    ("SeqAct_", b"SeqAct_", True),
    ("PhysXLoader64", b"PhysXLoader64", False),
]

RE_YEAR = re.compile(rb"(?:19|20)[0-9][0-9]")


def _widen(b):
    """b"abc" -> b"a\\x00b\\x00c\\x00" — a UTF-16LE literal of an ASCII string."""
    return bytes(x for c in b for x in (c, 0))


def _version_strings(blob):
    """Yield candidate VERSIONINFO string values, UTF-16LE, without parsing the resource tree.

    The DLL uses VerQueryValueW over every (lang, codepage) pair. Reproducing that faithfully
    means walking the RT_VERSION resource; for a copyright check a targeted UTF-16 search for
    the key name and the value that follows it is equivalent in practice and far shorter.
    """
    key = _widen(b"LegalCopyright")
    for m in re.finditer(re.escape(key), blob):
        # Value follows the key, WORD-aligned, NUL-terminated UTF-16LE.
        pos = m.end()
        while pos + 1 < len(blob) and blob[pos:pos + 2] == b"\x00\x00":
            pos += 2
        end = pos
        while end + 1 < len(blob) and blob[end:end + 2] != b"\x00\x00":
            end += 2
        if end > pos:
            yield blob[pos:end].decode("utf-16-le", errors="replace")


def epic_copyright_year(blob):
    """Newest year in an Epic Games LegalCopyright, or None when the marker does not apply."""
    for value in _version_strings(blob):
        if "epic games" not in value.lower():
            continue
        years = [int(y.group(0)) for y in RE_YEAR.finditer(value.encode("ascii", "replace"))]
        if not years:
            # An Epic notice with NO year proves nothing (Manor Lords, UE 5.5). Not a hit.
            return None
        return max(years)
    return None


def score(path):
    """Return (score, per_marker_dict). Reads the FILE, not a mapped image.

    The DLL scans the mapped module (Macht::GetModuleBase/GetModuleSize). For statically
    initialised string data the two agree; a file scan cannot see the ~2 MB of .data that only
    exists at runtime, which is irrelevant for string literals but worth knowing.
    """
    blob = pathlib.Path(path).read_bytes()
    hits = {}
    for label, needle, wide in STRING_MARKERS:
        n = blob.count(needle)
        w = blob.count(_widen(needle)) if wide else 0
        hits[label] = (n, w)

    year = epic_copyright_year(blob)
    hits["EpicCopyright"] = year

    total = sum(1 for label, _, _ in STRING_MARKERS
                if hits[label][0] or hits[label][1])
    if year is not None and year <= LAST_PRE_UE4_COPYRIGHT_YEAR:
        total += 1
    return total, hits


def fmt(path, total, hits):
    ue3, seq, phx = hits["UnrealEngine3"], hits["SeqAct_"], hits["PhysXLoader64"]
    year = hits["EpicCopyright"]
    verdict = "PRE-UE4 (REFUSED)" if total >= MARKER_THRESHOLD else "ok"
    return (f"{total}/{MARKER_COUNT} {verdict:18s} "
            f"UnrealEngine3={ue3[0]}/{ue3[1]} SeqAct_={seq[0]}/{seq[1]} "
            f"PhysXLoader64={phx[0]} EpicYear={year if year is not None else '-'}  "
            f"{os.path.basename(path)}")


def corpus(root):
    """Assert every reference build scores below the threshold. Returns a process exit code."""
    root = pathlib.Path(root)
    if not root.is_dir():
        print(f"corpus root not found: {root}")
        print("Set REFBUILD_ROOT or pass the path. See docs/reference-builds.md.")
        return 2

    # Same walk shape as tools/ghidra/inventory_builds.py: game binaries only, engine dirs out.
    exes = [p for p in root.rglob("*.exe")
            if p.stat().st_size >= 5 * 1024 * 1024
            and "Engine" not in p.parts
            and not p.name.startswith(("UE4PrereqSetup", "UEPrereqSetup", "CrashReportClient"))]
    if not exes:
        print(f"no candidate binaries under {root}")
        return 2

    failures = []
    for p in sorted(exes):
        total, hits = score(p)
        print(fmt(str(p), total, hits))
        if total >= MARKER_THRESHOLD:
            failures.append((p, total, hits))

    print()
    print(f"scanned {len(exes)} reference binaries")
    if failures:
        print(f"FAIL — {len(failures)} supported build(s) would be REFUSED as pre-UE4:")
        for p, total, hits in failures:
            print("   ", fmt(str(p), total, hits))
        return 1
    print(f"PASS — every build scored below the {MARKER_THRESHOLD}-marker threshold")
    return 0


def main(argv):
    if not argv:
        print(__doc__)
        return 1
    if argv[0] == "--corpus":
        root = argv[1] if len(argv) > 1 else os.environ.get("REFBUILD_ROOT", DEFAULT_REFBUILD_ROOT)
        return corpus(root)
    for path in argv:
        try:
            total, hits = score(path)
            print(fmt(path, total, hits))
        except OSError as exc:
            print(f"ERR  {path}  ({exc})")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
