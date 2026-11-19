#!/usr/bin/env python3
"""The AOB sweep without Ghidra: same ROWS table, same patterns, same output files, from the PEs.

    py tools/ghidra/pe_sweep.py                              # all rows
    py tools/ghidra/pe_sweep.py UE4.10 UE5.8                 # substring filters, like sweep.sh
    py tools/ghidra/pe_sweep.py --out out/pe-sweep --map out/pe-corpus-map.tsv

WHY. `sweep.sh` spends 12-15 minutes and three JVMs re-reading a 181 GB Ghidra corpus to obtain
`image base + block map + bytes` — the only three things `scan_patterns.java` ever asks for. Those
come out of the game binary, which is what the 59.4 GB archive already holds.

THE ROWS TABLE IS NOT DUPLICATED HERE. It is parsed straight out of `sweep.sh`, so the two sweeps
can never disagree about which project carries which truth VAs. Everything else this needs — which
PROGRAMS a row covers, and where each one's binary is — comes from the verified map that
`check_pe_memory.py` writes, and every entry in that map was accepted only after its image base,
block layout and per-block MD5s matched Ghidra's.

WHAT IT WILL NOT DO. It will not scan a program that is not in the map. A row whose binary is
missing (or whose only candidate failed the hash check) is reported as UNMAPPED and skipped —
because a silently missing scan TSV becomes a missing ROW in `out/sweep/REPORT.md`, which reads as "that
pattern was never tested" rather than "that program did not run". That is the same failure
`sweep.sh`'s `_failures.tsv` exists to prevent.
"""
import argparse
import collections
import io
import os
import re
import subprocess
import sys
import time
from concurrent.futures import ProcessPoolExecutor

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import pe_scan_patterns                                               # noqa: E402

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SWEEP_SH = os.path.join(REPO, "tools", "ghidra", "sweep.sh")


def parse_rows(path):
    """(tag, project, glob, true) straight out of sweep.sh's ROWS table, with the @SF521@
    placeholder expanded from the same file's SF521_TRUE."""
    src = io.open(path, encoding="utf-8", errors="replace").read()
    lines = src.splitlines()
    a = next(i for i, l in enumerate(lines) if l.strip() == "ROWS=(")
    b = next(i for i in range(a + 1, len(lines)) if lines[i].strip() == ")")
    m = re.search(r'^SF521_TRUE="([^"]*)"', src, re.M)
    sf521 = m.group(1) if m else ""
    rows = []
    for l in lines[a + 1:b]:
        t = l.strip()
        if not t or t.startswith("#") or "|" not in t:
            continue
        p = t.strip('"').split("|")
        if len(p) < 4:
            continue
        tag, proj, glob, true = p[0], p[1], p[2], "|".join(p[3:])
        if true == "@SF521@":
            true = sf521
        rows.append((tag, proj, glob, true))
    return rows


def load_map(path):
    """tag -> [(program, image_base, pe_path)], from check_pe_memory.py."""
    out = collections.defaultdict(list)
    if not os.path.exists(path):
        return out
    with io.open(path, encoding="utf-8") as f:
        f.readline()
        for line in f:
            c = line.rstrip("\n").split("\t")
            if len(c) >= 4:
                out[c[0]].append((c[1], c[2], c[3]))
    return out


def _one(job):
    tag, prog, ib, path, out_dir, tsv, true = job
    t0 = time.time()
    try:
        stem = pe_scan_patterns.run(path, out_dir, tsv, true, tag, prog=prog,
                                    image_base_str=ib, quiet=True)
        return (tag, prog, "ok" if stem else "no-exec-bytes", time.time() - t0, "")
    except Exception as e:                                            # keep the sweep going
        return (tag, prog, "ERROR", time.time() - t0, "%s: %s" % (type(e).__name__, e))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("filters", nargs="*", help="tag substrings, like sweep.sh's positional args")
    ap.add_argument("--out", default=os.path.join(REPO, "out", "pe-sweep"))
    ap.add_argument("--map", default=os.path.join(REPO, "out", "pe-corpus-map.tsv"))
    ap.add_argument("--patterns", default=None, help="reuse an existing patterns.tsv")
    ap.add_argument("--jobs", type=int, default=None)
    args = ap.parse_args()

    os.makedirs(args.out, exist_ok=True)
    tsv = args.patterns or os.path.join(args.out, "patterns.tsv")
    if not args.patterns:
        rc = subprocess.call([sys.executable, os.path.join(REPO, "tools", "ghidra",
                                                           "extract_patterns.py"),
                              os.path.join(REPO, "dll", "src", "Himmel.h"), tsv])
        if rc != 0:
            print("extract_patterns.py failed")
            return 1

    rows = parse_rows(SWEEP_SH)
    pmap = load_map(args.map)
    if not pmap:
        print("no verified PE map at %s — run check_pe_memory.py --map %s first"
              % (args.map, args.map))
        return 1

    jobs, unmapped = [], []
    for tag, proj, glob, true in rows:
        if args.filters and not any(f in tag for f in args.filters):
            continue
        progs = pmap.get(tag)
        if not progs:
            unmapped.append((tag, proj))
            continue
        for prog, ib, path in progs:
            jobs.append((tag, prog, ib, path, args.out, tsv, true))

    # Each worker holds ONE WHOLE PE in memory (the largest in the corpus is ~700 MB), so the cap
    # here is RAM per worker, not core count — which is why it is not simply cpu_count().
    n = args.jobs or int(os.environ.get("PE_SWEEP_JOBS") or 0) or min((os.cpu_count() or 4) // 2, 8)
    print("== pe_sweep: %d program(s) over %d row(s), %d worker(s)"
          % (len(jobs), len({j[0] for j in jobs}), n))
    t0 = time.time()
    results = []
    with ProcessPoolExecutor(max_workers=n) as ex:
        for r in ex.map(_one, jobs):
            results.append(r)
            print("   %-9s %-28s %-46s %5.1fs %s" % (r[2], r[0], r[1], r[3], r[4]))

    bad = [r for r in results if r[2] == "ERROR"]
    print("\n== pe_sweep complete in %.1fs — %d ok, %d error, %d unmapped row(s)"
          % (time.time() - t0, sum(1 for r in results if r[2] == "ok"), len(bad), len(unmapped)))
    for tag, proj in unmapped:
        print("   UNMAPPED %-28s project=%s" % (tag, proj))
    for r in bad:
        print("   ERROR    %-28s %s  %s" % (r[0], r[1], r[4]))
    print("\n   Aggregate with:  py tools/ghidra/aggregate_sweep.py \"%s\"" % args.out)
    return 1 if (bad or unmapped) else 0


if __name__ == "__main__":
    sys.exit(main())
