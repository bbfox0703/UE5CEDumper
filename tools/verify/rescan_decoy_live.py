r"""[RESCAN-DECOY-REINIT] live: Extra Scan replaces a published GObjects that reads no objects, and re-measures.

    py tools/verify/stage_dll.py tools/verify/staging/rescan-decoy-reinit.json
    py tools/verify/launch_dumpertest.py shipping
    py tools/verify/inject.py --name DumperTest --dll out/staged/rescan-decoy-reinit/UE5Dumper.dll
    py tools/verify/rescan_decoy_live.py --process DumperTest-Win64-Shipping --out out/rescan-decoy/live

Needs the STAGED DLL in a FRESH process: init publishes a decoy built from the real FUObjectArray and skips both
Count == 0 recoveries, which is the session Extra Scan's replace exists for and no shipped title is known to produce.
The precondition is asserted, not assumed: init must have logged the staging line and the pool must read no objects.

ASSERTS, in order
  before   get_pointers: object_count 0, GObjects = the staged decoy; get_offsets: probe ran, not validated.
  rescan   replacing, found_gobjects, at least one candidate.
  apply    gobjects_outcome "replaced"; GObjects = the real array init logged; object_count > 1000.
  after    get_offsets validated; offsets-*.log gained exactly one "ValidateAndFixOffsets: Starting"; the scan log
           names the replacement; a second apply_rescan answers "already_applied"; a second rescan finds nothing to do.
Counts are before/after deltas over the whole log, never a time window (working-lessons §1).
"""
import argparse
import os
import pathlib
import re
import sys
import time

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from pipe_client import PipeClient  # noqa: E402

LOGS = pathlib.Path(os.environ["LOCALAPPDATA"]) / "UE5CEDumper" / "Logs"


def count(logdir: pathlib.Path, glob: str, needle: str) -> int:
    n = 0
    for f in sorted(logdir.glob(glob)):
        try:
            n += f.read_text(encoding="utf-8", errors="replace").count(needle)
        except OSError:
            pass
    return n


def data(reply):
    return reply.get("data") or reply


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--process", required=True, help="image stem, e.g. DumperTest-Win64-Shipping")
    ap.add_argument("--out", required=True)
    ap.add_argument("--pid", type=int, default=None)
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    lines, failures = [], []

    def say(s=""):
        print(s, flush=True)
        lines.append(s)

    def check(name, ok, detail=""):
        say(f"  {'PASS' if ok else 'FAIL'}  {name}{('  -- ' + detail) if detail else ''}")
        if not ok:
            failures.append(name)

    logdir = LOGS / a.process
    init_text = "\n".join(f.read_text(encoding="utf-8", errors="replace") for f in sorted(logdir.glob("init-*.log")))
    staged = re.findall(r"STAGING: init publishes a decoy at (0x[0-9A-Fa-f]+) built from the real GObjects (0x[0-9A-Fa-f]+)",
                        init_text)
    say(f"# {time.strftime('%Y-%m-%d %H:%M:%S')} {a.process}")
    if not staged:
        raise SystemExit("precondition failed: init did not log the staging line -- inject the staged DLL into a fresh process")
    decoy, real = (int(x, 16) for x in staged[-1])
    say(f"staged decoy 0x{decoy:X}, real GObjects 0x{real:X}")
    probes_before = count(logdir, "offsets-*.log", "ValidateAndFixOffsets: Starting")
    replaced_before = count(logdir, "scan-*.log", "Rescan: GObjects ")

    with PipeClient(timeout=600, pid=a.pid) as c:
        p = data(c.request("get_pointers"))
        o = data(c.request("get_offsets"))
        say(f"before: gobjects {p.get('gobjects')} objects {p.get('object_count')} ue {p.get('ue_version')}; "
            f"offsets probe_ran {o.get('probe_ran')} validated {o.get('validated')} ({o.get('fallback_reason')})")
        check("precondition: the pool reads no objects", int(p.get("object_count") or 0) <= 0)
        check("precondition: GObjects is the staged decoy", int(str(p.get("gobjects")), 16) == decoy)
        check("precondition: the decoy's probe ran and measured nothing", bool(o.get("probe_ran")) and not o.get("validated"))

        r = data(c.request("rescan"))
        say(f"rescan: {r}")
        check("rescan looks for a replacement", bool(r.get("replacing")) and bool(r.get("scanning_gobjects")))
        t0 = time.time()
        while time.time() - t0 < 600:
            s = data(c.request("rescan_status"))
            if not s.get("running") and int(s.get("phase") or 0) >= 3:
                break
            time.sleep(1.5)
        say(f"rescan_status: found {s.get('found_gobjects')} first {s.get('gobjects_addr')} "
            f"candidates {s.get('gobjects_candidates')} cancelled {s.get('cancelled')} ({time.time() - t0:.1f} s)")
        check("the sweep found candidates", bool(s.get("found_gobjects")) and int(s.get("gobjects_candidates") or 0) >= 1)

        ap1 = data(c.request("apply_rescan"))
        say(f"apply_rescan: {ap1}")
        check("apply replaced the decoy", ap1.get("gobjects_outcome") == "replaced", str(ap1.get("gobjects_outcome")))
        check("...with the real array", int(str(ap1.get("gobjects")), 16) == real, f"{ap1.get('gobjects')} vs 0x{real:X}")
        check("...which reads objects", int(ap1.get("object_count") or 0) > 1000, str(ap1.get("object_count")))

        o2 = data(c.request("get_offsets"))
        p2 = data(c.request("get_pointers"))
        say(f"after: gobjects {p2.get('gobjects')} ({p2.get('gobjects_method')}) objects {p2.get('object_count')} "
            f"ue {p2.get('ue_version')}; offsets validated {o2.get('validated')} ({o2.get('fallback_reason')})")
        check("the offsets were measured over the real pool", bool(o2.get("validated")), str(o2.get("fallback_reason")))
        check("the probe ran once more, and only once",
              count(logdir, "offsets-*.log", "ValidateAndFixOffsets: Starting") == probes_before + 1)
        check("the scan log names the replacement", count(logdir, "scan-*.log", "Rescan: GObjects ") == replaced_before + 1)

        ap2 = data(c.request("apply_rescan"))
        check("a second apply finds the job consumed", ap2.get("gobjects_outcome") == "already_applied",
              str(ap2.get("gobjects_outcome")))
        r2 = data(c.request("rescan"))
        check("a second rescan has nothing to look for", not r2.get("scanning_gobjects") and not r2.get("replacing"),
              str(r2))

    say(f"RESULT: {'PASS' if not failures else 'FAIL'} ({len(failures)} failed)")
    pathlib.Path(a.out, "result.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
    return 0 if not failures else 1


if __name__ == "__main__":
    sys.exit(main())
