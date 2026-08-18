"""One-title sweep: version-detection evidence + CPN screening + offset banner host check.

    py sweep_title.py <LogFolderName>

Assumes the title is already running with the DLL serving the pipe. Pays for, in
one launch: G2 step 2 (the needle-only window, when a tier hit keeps
CountPreUE4Markers out of it), G8/G9 step 3 (a Tier 1 line naming a version),
U2's CPN sweep (case_preserving WITH probe_ran -- a verdict, not a sample), and
G1/X3 host screening (validated / any unmeasured key).

Prints the measured window rather than a verdict, because the register's rule is
that a duration without its image size and conditions is not a measurement.
"""
import json
import os
import pathlib
import re
import sys
from datetime import datetime

sys.path.insert(0, r"D:\Github\UE5CEDumper\tools\verify")
from pipe_client import PipeClient  # noqa: E402

LOGS = pathlib.Path(os.environ["LOCALAPPDATA"]) / "UE5CEDumper" / "Logs"

WANT = ("PE VERSIONINFO", "PE resource failed", "Tier 1 (", "Tier 2 ", "Tier 3 ",
        "pre-UE4 markers", "UE Version =", "DetectPublisher", "skipped DetectVersion")

TS = re.compile(r"^\[(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d+)\]")


def stamp(line):
    m = TS.match(line)
    return datetime.strptime(m.group(1), "%Y-%m-%d %H:%M:%S.%f") if m else None


def main(argv):
    folder = argv[0] if argv else None
    with PipeClient(timeout=600.0) as c:
        c.assert_build()
        c.ensure_scanned()
        p = c.request("get_pointers")
        o = c.request("get_offsets")

    print("=== pointers ===")
    for k in ("build_number", "module_name", "load_mode", "ue_version",
              "is_low_confidence", "object_count", "gobjects_pattern_id",
              "gnames_pattern_id", "gworld_pattern_id", "item_size"):
        if k in p:
            print(f"  {k} = {p[k]}")

    print("=== offsets: CPN + banner host ===")
    for k in sorted(o):
        if any(s in k.lower() for s in ("valid", "unmeasured", "partial",
                                        "probe", "case", "measur")):
            print(f"  {k} = {o[k]}")

    if not folder:
        return 0
    log = LOGS / folder / "scan-0.log"
    if not log.exists():
        print(f"\n(no {log})")
        return 0
    lines = [l for l in log.read_text(encoding="utf-8", errors="replace").splitlines()
             if any(w in l for w in WANT)]
    print(f"\n=== {log.name}: version lines ===")
    for l in lines:
        print("  " + l.strip())

    fall = next((l for l in lines if "PE resource failed" in l), None)
    if fall:
        t0 = stamp(fall)
        nxt = next((l for l in lines[lines.index(fall) + 1:] if stamp(l)), None)
        if nxt and t0:
            dt = (stamp(nxt) - t0).total_seconds()
            print(f"\n=== measured window ===")
            print(f"  fallback -> next SCAN:Ver = {dt:.3f} s")
            print(f"  terminated by: {nxt.strip()[:120]}")
            print(f"  CountPreUE4Markers inside window? "
                  f"{'YES (no tier hit)' if 'pre-UE4' in nxt else 'NO (tier hit)'}")
    else:
        print("\n(no memory-scan fallback -- this title short-circuits at the PE resource)")
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.exit(main(sys.argv[1:]))
