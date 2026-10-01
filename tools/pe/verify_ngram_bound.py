#!/usr/bin/env python3
"""Re-measure the specificity index's own claims: is the bound really a proof on its sources?

    py tools/pe/verify_ngram_bound.py
    py tools/pe/verify_ngram_bound.py --tsv out/pe-sweep/patterns.tsv --roots D:/UE_Analyze_data

WHICH FILES ARE "ITS OWN SOURCES". An index built since 2026-10-01 does not name them
(`build_ngram_index.py` says why). It records the roots the builder walked and ONE digest of the
whole set, so this walks the same roots with the builder's own `find_sources` and compares
digests. Equal: these are the sources, and a violation means the index is broken (exit 1).
Different: the corpus changed since the build — a file added there was never indexed, so a
violation on it is expected and proves nothing; the run says so and exits 2 instead of 1. An older
index that lists its sources is still resolved by name and code size, as before.

WHY THIS EXISTS. `aob_specificity.py`'s docstring carries two measured claims — "0 violations /
1,017 pairs on the source binaries" and a worst-hits distribution per verdict. Both were measured
BY HAND once, and a hand-measured number in a docstring is a number that goes stale silently. This
re-derives them, so the next person can check instead of believe.

IT ALSO SETTLED A COUNTING BUG, in the index that listed its sources (before 2026-10-01). It
recorded **12 source entries but only 11 distinct binaries**: one build sat in two folders and the
builder globbed `**/*.exe`, so it was indexed twice. `aob_specificity.py` printed `len(sources)` =
12 while the docstring said 11 — the DOCSTRING was right. The duplicate did not corrupt the index:
`merge_max` takes the MAX bucket per key, so adding a binary twice is a no-op on the data. The
builder deduplicates by content now.

WHAT A VIOLATION IS. Every occurrence of the whole pattern must contain each of its literal
windows, so the frequency of the RAREST window upper-bounds the pattern's hit count in the code the
index was built from. A violation is `actual_hits > bound` on a source binary — which should be
impossible, and if it ever fires the index is broken rather than merely loose.

PAIRS ARE COUNTED EXPLICITLY, because the original 1,017 could not be reproduced from any obvious
product (151 patterns x 11 binaries = 1,661) and the definition behind it was never written down.
A pair here is (scoreable pattern, distinct source binary): scoreable = the pattern has a literal
run of at least 4 bytes AND the index can size at least one of its windows. That is stated rather
than inferred, so this number stays checkable even if it disagrees with the old one.
"""
import argparse
import collections
import io
import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(_HERE))
sys.path.insert(0, _HERE)
sys.path.insert(0, os.path.join(REPO, "tools", "ghidra"))

from aob_specificity import Index, DEFAULT_INDEX, score, verdict          # noqa: E402
from build_ngram_index import (find_sources, set_digest, exec_sections,  # noqa: E402
                               DEFAULT_EXCLUDE)
from pe_memory import PeMemory                                           # noqa: E402
import pe_scan_patterns as P                                             # noqa: E402


def distinct_sources(idx):
    """The index's sources with exact duplicates collapsed, in recorded order."""
    out, seen = [], set()
    for s in idx.meta.get("sources", []):
        k = (s.get("binary"), s.get("engine"), s.get("config"), s.get("exec_mb"))
        if k in seen:
            continue
        seen.add(k)
        out.append(s)
    return out


def index_exes(roots):
    by = collections.defaultdict(list)
    for root in roots:
        if not os.path.isdir(root):
            print("  !! root not found: %s" % root)
            continue
        for dp, _, fn in os.walk(root):
            for f in fn:
                if f.lower().endswith(".exe"):
                    by[f.lower()].append(os.path.join(dp, f))
    return by


def resolve(src, by_name):
    """Match a recorded source to a file. Name is ambiguous (three different StackOBot builds share
    one filename), so the discriminator is the EXEC SIZE the index recorded — recomputed here from
    the PE rather than trusted from the path."""
    best = None
    for p in by_name.get(src["binary"].lower(), []):
        try:
            mem = PeMemory(p)
        except Exception:
            continue
        # 1e6, NOT 2**20 — build_ngram_index.py `main` records `sum(len(b) for b in bufs) / 1e6`.
        # Using MiB here left all 11 sources unresolved and would have been easy to "fix" by
        # widening the tolerance, which silently matches the wrong StackOBot build (three of them
        # share a filename, 110.4 / 119.2 / 124.6 MiB apart). Match the producer's unit instead.
        mb = sum(b.size for b in mem.exec_blocks()) / 1e6
        d = abs(mb - float(src.get("exec_mb") or 0))
        if best is None or d < best[0]:
            best = (d, p, mb)
    if best is None or best[0] > 0.15:            # recorded to 1 decimal place
        return None, None
    return best[1], best[2]


def hits_for(path, sigs):
    """Actual hit count per pattern id on this binary, using the scanner that was verified
    byte-identical to Ghidra's (see tools/ghidra/compare_sweeps.py)."""
    mem = PeMemory(path)
    for s in sigs:
        s.n_hits = 0
        s.hits = []
    for blk in mem.exec_blocks():
        P.scan_block(sigs, mem.block_bytes(blk), blk.start, mem)
    return {s.id: s.n_hits for s in sigs}


def _hits_job(job):
    """One source in a worker process: the signatures are reloaded there (they carry scan state)."""
    path, tsv = job
    sigs, _skipped, _ = P.load_sigs(tsv)
    return path, hits_for(path, sigs)


def undisclosed_sources(idx, roots, exclude):
    """-> ([(label, engine, path, exec_mb)], same_set) for an index that records roots + a digest.
    A file counts as a source exactly when the builder would have indexed it: at least 1 MB of
    executable code that its reader can parse."""
    used = []
    for s in find_sources(roots, exclude):
        try:
            mb = sum(len(b) for b in exec_sections(s["path"])) / 1e6
        except Exception:
            continue
        if mb >= 1:
            used.append((s, mb))
    same = set_digest([s for s, _mb in used]) == idx.meta.get("source_set_digest")
    return [("source %03d" % i, "-", s["path"], mb) for i, (s, mb) in enumerate(used, 1)], same


def pct(vals, q):
    if not vals:
        return 0
    v = sorted(vals)
    return v[min(len(v) - 1, int(round((len(v) - 1) * q)))]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--tsv", default=os.path.join(REPO, "out", "pe-sweep", "patterns.tsv"))
    ap.add_argument("--index", default=DEFAULT_INDEX)
    ap.add_argument("--roots", nargs="*", default=None,
                    help="default: the roots the index records (an older index: D:\\UE_Analyze_data)")
    ap.add_argument("--exclude", nargs="*", default=list(DEFAULT_EXCLUDE))
    ap.add_argument("--workers", type=int, default=1)
    args = ap.parse_args()

    if not os.path.exists(args.tsv):
        print("no patterns TSV at %s — generate one:\n"
              "   py tools/ghidra/extract_patterns.py dll/src/Himmel.h %s" % (args.tsv, args.tsv))
        return 2
    idx = Index(args.index)
    print("index    : %s" % os.path.basename(args.index))
    same_set = True
    if "source_files" in idx.meta:
        roots = args.roots or idx.meta.get("roots") or []
        targets, same_set = undisclosed_sources(idx, roots, args.exclude)
        print("sources  : %d recorded; %d found under %s — %s"
              % (idx.meta["source_files"], len(targets), ", ".join(roots),
                 "the SAME set (digest matches)" if same_set else
                 "NOT the set the index was built from (digest differs)"))
    else:
        srcs = distinct_sources(idx)
        n_entries = len(idx.meta.get("sources", []))
        print("sources  : %d distinct (%d entries recorded%s)"
              % (len(srcs), n_entries,
                 ", %d duplicate" % (n_entries - len(srcs)) if n_entries != len(srcs) else ""))
        by_name = index_exes(args.roots or [r"D:\UE_Analyze_data"])
        targets = []
        for s in srcs:
            path, mb = resolve(s, by_name)
            if not path:
                print("  !! UNRESOLVED %-40s eng=%s exec_mb=%s"
                      % (s["binary"], s["engine"], s["exec_mb"]))
                continue
            targets.append((s["binary"], s["engine"], path, mb))

    sigs, skipped, _ = P.load_sigs(args.tsv)
    print("patterns : %d scannable (%d symbol/unparsable skipped)\n" % (len(sigs), skipped))

    bounds, verds = {}, {}
    for s in sigs:
        b, n, win, longest, lit = score(idx, s.pat)
        bounds[s.id] = b
        verds[s.id] = verdict(b, longest, lit, idx.threshold - 1)[0]

    worst = collections.defaultdict(int)
    pairs = violations = 0
    viol_rows = []
    clear_pairs = clear_viol = 0
    print("scanning the index's own sources ...")
    info = {path: (label, eng, mb) for label, eng, path, mb in targets}
    jobs = [(path, args.tsv) for _label, _eng, path, _mb in targets]
    if args.workers > 1:
        from concurrent.futures import ProcessPoolExecutor
        results = ProcessPoolExecutor(max_workers=args.workers).map(_hits_job, jobs)
    else:
        results = ((path, hits_for(path, sigs)) for path, _tsv in jobs)
    for path, h in results:
        label, eng, mb = info[path]
        print("  %-40s eng=%-7s exec=%.1f MB" % (label[:40], eng, mb), flush=True)
        for pid, n in h.items():
            worst[pid] = max(worst[pid], n)
            b = bounds.get(pid)
            if b is None:
                continue
            pairs += 1
            is_clear = verds[pid] == "CLEAR"
            clear_pairs += is_clear
            if n > b:
                violations += 1
                clear_viol += is_clear
                viol_rows.append((pid, label, eng, n, b, verds[pid]))

    print("\n=== claim 1: the bound is a PROOF on the index's own sources ===")
    print("   %d violations / %d pairs   (pair = scoreable pattern x distinct source binary)"
          % (violations, pairs))
    print("   of which CLEAR-verdict:  %d / %d" % (clear_viol, clear_pairs))
    for r in viol_rows[:20]:
        print("   !! %-18s %-38s %s  hits=%d > bound=%d  [%s]"
              % (r[0], r[1][:38], r[2], r[3], r[4], r[5]))

    print("\n=== claim 2: worst hits per pattern across those sources, by verdict ===")
    groups = collections.defaultdict(list)
    for s in sigs:
        groups[verds[s.id]].append(worst[s.id])
    print("   %-12s %5s  %8s %8s %8s %8s" % ("verdict", "n", "median", "90th", "99th", "MAX"))
    for v in ("CLEAR", "UNPROVEN", "NO-ANCHOR", "UNSCOREABLE"):
        g = groups.get(v)
        if not g:
            continue
        print("   %-12s %5d  %8d %8d %8d %8d"
              % (v, len(g), pct(g, .5), pct(g, .9), pct(g, .99), max(g)))
    if violations and not same_set:
        print("\nThe corpus under the roots is NOT the set this index was built from, so the "
              "violations above may sit on files it never indexed. Not a verdict on the index: "
              "rebuild it, or point --roots at the corpus it was built from.")
        return 2
    return 1 if violations else 0


if __name__ == "__main__":
    sys.exit(main())
