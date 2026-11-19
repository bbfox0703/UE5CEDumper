#!/usr/bin/env python3
"""Are two sweep output dirs byte-identical? The acceptance test for the Ghidra-free sweep.

    py tools/ghidra/compare_sweeps.py out/sweep-ref-ghidra2 out/pe-sweep
    py tools/ghidra/compare_sweeps.py A B --ignore-a Maelstrom       # known broken import

WHY BYTE-IDENTICAL AND NOT "EQUIVALENT". A sweep answers questions like "does the first hitting
pattern by priority resolve correctly on this build" — the kind of answer that stays plausible
when it is wrong. Grading a replacement on a summary comparison ("the same patterns are still
green") would pass a scanner that had silently lost half its hits on one program. Byte equality is
the only comparison that cannot be satisfied by a wrong answer that looks right.

The file SET is compared too, in both directions. A missing scan TSV becomes a missing row in
the report `aggregate_sweep.py` writes (`out/sweep/REPORT.md`), and a missing row reads as "that pattern was never tested" rather than "that program
did not run" — the same trap `sweep.sh`'s `_failures.tsv` exists to catch.
"""
import argparse
import io
import os
import sys


def listing(d):
    return {f for f in os.listdir(d)
            if (f.startswith("scan_") or f.startswith("consensus_"))
            and (f.endswith(".txt") or f.endswith(".tsv"))}


def first_diff(pa, pb, context=3):
    """-> a few lines around the first difference, for a report that is actionable without a
    separate diff run."""
    a = io.open(pa, encoding="utf-8", errors="replace").read().splitlines()
    b = io.open(pb, encoding="utf-8", errors="replace").read().splitlines()
    n = min(len(a), len(b))
    for i in range(n):
        if a[i] != b[i]:
            out = []
            for j in range(max(0, i - context), i):
                out.append("        %s" % a[j])
            out.append("      A> %s" % a[i])
            out.append("      B> %s" % b[i])
            return "line %d\n%s" % (i + 1, "\n".join(out))
    if len(a) != len(b):
        longer, which = (a, "A") if len(a) > len(b) else (b, "B")
        return "identical for %d lines, then %s has %d more (first: %r)" % (
            n, which, len(longer) - n, longer[n][:120])
    return "byte difference with no line difference (line endings or trailing bytes)"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("a")
    ap.add_argument("b")
    ap.add_argument("--ignore-a", nargs="*", default=[],
                    help="substrings of A-only filenames that are EXPECTED to be missing from B")
    ap.add_argument("-v", "--verbose", action="store_true")
    args = ap.parse_args()

    fa, fb = listing(args.a), listing(args.b)
    only_a = sorted(f for f in fa - fb
                    if not any(s in f for s in args.ignore_a))
    excused = sorted(f for f in fa - fb if any(s in f for s in args.ignore_a))
    only_b = sorted(fb - fa)
    both = sorted(fa & fb)

    same, diff = [], []
    for f in both:
        pa, pb = os.path.join(args.a, f), os.path.join(args.b, f)
        if open(pa, "rb").read() == open(pb, "rb").read():
            same.append(f)
        else:
            diff.append(f)

    print("A: %s  (%d files)" % (args.a, len(fa)))
    print("B: %s  (%d files)\n" % (args.b, len(fb)))
    print("   identical   %d" % len(same))
    print("   differing   %d" % len(diff))
    print("   only in A   %d%s" % (len(only_a), "  (+%d excused)" % len(excused) if excused else ""))
    print("   only in B   %d" % len(only_b))

    if excused:
        print("\n=== A-only, EXPECTED (--ignore-a) ===")
        for f in excused:
            print("   %s" % f)
    if only_a:
        print("\n=== only in A — B did not produce these ===")
        for f in only_a:
            print("   %s" % f)
    if only_b:
        print("\n=== only in B — A did not produce these ===")
        for f in only_b:
            print("   %s" % f)
    if diff:
        print("\n=== differing ===")
        for f in diff:
            print("   %s" % f)
            if args.verbose:
                print("      %s" % first_diff(os.path.join(args.a, f), os.path.join(args.b, f))
                      .replace("\n", "\n   "))
    ok = not (diff or only_a or only_b)
    print("\n   %s" % ("IDENTICAL: B reproduces A exactly." if ok
                       else "NOT IDENTICAL — see above."))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
