"""How noisy will this AOB be? Answered offline, with no corpus and no Ghidra.

    py tools/pe/aob_specificity.py "48 8b 05 ?? ?? ?? ?? 48 85 c0 74 ??"
    py tools/pe/aob_specificity.py --tsv out/sweep/patterns.tsv        # score the whole DB

STDLIB ONLY, ON PURPOSE. Building the index needs numpy and the 200 GB corpus; querying it needs
neither, so this CAN run on a bare second machine and in CI. That portability is the entire point —
see docs/aob-block-library-eval.md §6.

✅ WIRED INTO CI AS A BLOCKING GATE (`6f594fa`, 2026-08-01). `.github/workflows/ci.yml` runs
`--tsv out/sweep/patterns.tsv --baseline tools/pe/aob-specificity-baseline.tsv --check` and throws on
a non-zero exit. **So `--update-baseline` is not a routine step.** The golden file exists to force a
human to notice when a pattern's upper bound RISES — i.e. when a pattern got noisier — and
regenerating it to make CI green discards exactly the signal the gate was built to deliver. Read the
diff first; if the rise is intended, say why in the commit.

(This paragraph used to say the opposite — "NOT ACTUALLY WIRED INTO CI, AND NOTHING ELSE READS IT
EITHER" — written three days before the gate landed and never updated. A maintainer reading it would
have concluded the baseline was scratch. Audit #4 R7.)

Still true, and the reason the claim was plausible: there are no callers in `dll/`, `ui/`, `scripts/`
or `build.ps1`. The invocation paths are CI and a human typing the command above. Step 5 of the
eval's build order ("gate authoring on it: a candidate clears the pre-filter before it earns a
sweep") is still unbuilt — the gate guards REGRESSIONS in shipped patterns, it does not yet gate new
ones.

WHAT THE NUMBER MEANS. Every occurrence of the whole pattern must contain each of its literal
windows, so the frequency of the RAREST window is an UPPER BOUND on the pattern's hit count *in the
code the index was built from*. Deliberately loose — wildcards constrain far more than literal runs
do — so read it as "at most this many", never as an estimate.

⚠ THE BOUND IS A PROOF ONLY ON THE INDEX'S OWN SOURCES, AND A PRIOR EVERYWHERE ELSE. Measured
2026-10-01, for the index built that day (126 binaries, 9,641 MB of code, every build config):

    on its own sources      0 violations / 14,490 pairs, CLEAR 0 / 4,032      (proof)
        `py tools/pe/verify_ngram_bound.py`; a pair is (scoreable pattern x source binary), 115 x 126.

    on programs it has NOT seen — `tools/pe/ngram_corpus_eval.py`, each developer's titles judged by
    an index built without them; a pair is (pattern, developer group), 153 patterns x 30 groups:

        index built from                                    exceeded        of which CLEAR
        11 self-built Shipping templates (until 2026-10-01)  20 / 3,105  0.64%   4 / 1,296  0.31%
        every self-built build config                        21 / 3,450  0.61%   1 / 1,320  0.08%
        + the other programs, one group held out              8 / 3,450  0.23%   1 /   968  0.10%

The failures were never a version problem — a 4.27-only index bounds a 5.4 binary with 0 violations
in 113. They are a CODE-COVERAGE problem: an index built from content-free engine templates has
seen engine code and no game code, while a shipped title adds 100+ MB of studio code. That is why
the index is now built from every UE program the build machine has, and why the rate fell: the old
index certified GNAM_UD2 at <= 15 where one game takes 932 hits, and GOBJ_AV2 at <= 15 where another
takes 510; both are bounded now. The rate does not reach zero, and it was still falling when the
corpus ran out: a program whose code resembles nothing indexed is bounded badly until it is a source.

⚠ The patterns were authored on, and swept against, most of those same programs, and only the
survivors are scored — so every out-of-sample figure above flatters a genuinely new game. On the
five groups no sweep ever covered the same comparison reads 5 -> 3 of 575.

WHAT IT CANNOT TELL YOU: whether the pattern hits the RIGHT address. `GNAM_XX_1` bounds at a clean
57 and is DECOY-ONLY. This is a PRE-FILTER, not an acceptance gate — `Himmel.h` rule 5 keeps meaning
the sweep. A quiet pattern that resolves to a decoy is still worthless.
"""
import gzip
import json
import os
import struct
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

MAGIC = b"UEAOBNGX"
GZIP_MAGIC = bytes((0x1F, 0x8B))
_HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_INDEX = next((p for p in (os.path.join(_HERE, "aob-ngram-index.bin.gz"),
                                  os.path.join(_HERE, "aob-ngram-index.bin"))
                      if os.path.exists(p)), os.path.join(_HERE, "aob-ngram-index.bin.gz"))


class Index:
    def __init__(self, path):
        # Sniff gzip rather than trusting the extension, so a renamed or
        # uncompressed index still loads.
        with open(path, "rb") as f:
            head = f.read(2)
        opener = gzip.open if head == GZIP_MAGIC else open
        with opener(path, "rb") as f:
            self.buf = f.read()
        if self.buf[:8] != MAGIC:
            raise ValueError(f"{path}: not an AOB n-gram index")
        ver, ntab, metalen = struct.unpack_from("<HHI", self.buf, 8)
        off = 16
        heads = []
        for _ in range(ntab):
            n, _r, thr, cnt = struct.unpack_from("<BBII", self.buf, off)
            heads.append((n, thr, cnt))
            off += 10
        self.meta = json.loads(self.buf[off:off + metalen].decode("utf-8"))
        off += metalen
        if ver >= 2:
            # Format 2 stores each table COLUMN by column (build_ngram_index.write_index: the same
            # records gzip to about half that way). Turn the columns back into record rows, so
            # everything below, and every caller that reads `buf` + `tables`, sees format 1.
            rows = bytearray(self.buf[:off])
            src = off
            for n, _thr, cnt in heads:
                stride = n + 1
                tab = bytearray(cnt * stride)
                for j in range(stride):
                    tab[j::stride] = self.buf[src + j * cnt:src + (j + 1) * cnt]
                rows += tab
                src += cnt * stride
            self.buf = bytes(rows)
        self.tables = {}
        for n, thr, cnt in heads:
            self.tables[n] = (off, cnt, thr, n + 1)
            off += cnt * (n + 1)
        self.ns = sorted(self.tables)
        self.threshold = heads[0][1] if heads else 0

    def lookup(self, key):
        """-> upper bound on the count of this exact byte sequence."""
        n = len(key)
        t = self.tables.get(n)
        if t is None:
            return None
        base, cnt, thr, stride = t
        lo, hi = 0, cnt
        while lo < hi:                                    # records are sorted by big-endian key
            mid = (lo + hi) // 2
            p = base + mid * stride
            k = self.buf[p:p + n]
            if k < key:
                lo = mid + 1
            elif k > key:
                hi = mid
            else:
                return 1 << self.buf[p + n]               # bucket decodes to an UPPER bound
        return thr - 1                                    # absent => below threshold


def literal_runs(pat):
    """Runs of fully-literal bytes. A nibble wildcard (`4?`) is NOT literal and breaks the run."""
    runs, cur = [], []
    for tok in pat.replace(",", " ").split():
        t = tok.strip()
        if not t:
            continue
        if "?" in t or "*" in t:
            if cur:
                runs.append(bytes(cur))
                cur = []
            continue
        try:
            cur.append(int(t, 16))
        except ValueError:
            raise SystemExit(f"cannot parse token {t!r} — expected hex byte or wildcard")
    if cur:
        runs.append(bytes(cur))
    return runs


def score(idx, pat):
    """-> (bound, n_used, limiting_window, longest_run_len, literal_byte_count)."""
    runs = literal_runs(pat)
    lit = sum(len(r) for r in runs)
    longest = max((len(r) for r in runs), default=0)
    usable = [n for n in idx.ns if n <= longest]
    if not usable:
        return None, 0, None, longest, lit
    n = max(usable)
    best, win = None, None
    for r in runs:
        for i in range(len(r) - n + 1):
            w = r[i:i + n]
            f = idx.lookup(w)
            if f is not None and (best is None or f < best):
                best, win = f, w
    return best, n, win, longest, lit


def verdict(bound, longest, lit, floor):
    """THE GUARANTEE IS ONE-DIRECTIONAL: this can CERTIFY a pattern quiet, and cannot CONDEMN one.

    Worst hits per pattern over 153 patterns x the index's 126 source binaries
    (2026-10-01, `tools/pe/verify_ngram_bound.py`):

        CLEAR (bound <= floor)   n=32  median 1   90th    1   99th     3   MAX      3
        UNPROVEN (bound > floor) n=83  median 2   90th  619   99th  6069   MAX  26942
        NO-ANCHOR                n=38  median 2   90th 5329   99th 19501   MAX  19501

    NO-ANCHOR is its own row for a reason: an earlier measurement lumped every non-CLEAR pattern
    together, and its alarming maximum belonged to a pattern with no 4-byte literal run at all, not
    to one the index had scored and failed to certify.

    So CLEAR is tight and trustworthy. Above the floor the bound is far too loose to mean anything:
    the buckets an earlier version called NOISY and VERY NOISY had MEDIANS of 0 and 2 — quieter than
    the bucket it called OK — because wildcards constrain a pattern enormously more than its literal
    windows do. That 5-level scale would have told you to reject GOBJ_ES53_1 (bound 2048, actually
    47 hits and the single best pattern in the corpus, priority 100, correct on 37 oracles).

    Hence two outcomes plus a structural one, and no invented gradations. Use it to pick which
    candidates to sweep FIRST, never to throw one away."""
    if longest < 4:
        return ("NO-ANCHOR", "no literal run reaches 4 bytes, so nothing can be established from "
                             "bytes alone — the pattern leans entirely on the validator and belongs "
                             "in a last-resort priority band. NOT a rejection: SPARSE_ES2_1 and "
                             "SPARSE_X1/X2 look like this, and the X pair is the ONLY thing "
                             "reaching sparse delegates on some non-Shipping builds.")
    if bound is None:
        return ("UNSCOREABLE", "no window the index can size. NOT the same as 'rare' — unknown.")
    if bound <= floor:
        return ("CLEAR", f"quiet in every program the index was built from — under {floor + 1} "
                         "occurrences of its rarest window in each (proven there: 0 violations / "
                         "14,490 pairs). On a program it has not seen it is a STRONG PRIOR, not a "
                         "proof: with each developer's titles held out in turn, 1 of 968 CLEAR "
                         "pairs was exceeded.")
    return ("UNPROVEN", f"cannot certify (rarest window bounds at {bound}). This is NOT evidence "
                        "the pattern is noisy — the bound is very loose, and most patterns landing "
                        "here measure in single digits. Sweep it to find out.")


def report(idx, pat, label=None):
    bound, n, win, longest, lit = score(idx, pat)
    v, why = verdict(bound, longest, lit, idx.threshold - 1)
    print(f"\n  {label or pat}")
    if label:
        print(f"    pattern         {pat}")
    print(f"    literal bytes   {lit}   longest run {longest}")
    if win is not None:
        print(f"    limiting window {win.hex(' ')}   (n={n})")
    print(f"    upper bound     {'—' if bound is None else f'<= {bound}'} matches")
    print(f"    VERDICT         {v} — {why}")
    return v


def main():
    args = [a for a in sys.argv[1:]]
    idxpath = DEFAULT_INDEX
    if "--index" in args:
        i = args.index("--index")
        idxpath = args[i + 1]
        del args[i:i + 2]
    if not os.path.exists(idxpath):
        print(f"index not found: {idxpath}\nBuild it with tools/pe/build_ngram_index.py "
              f"(needs numpy + the corpus).")
        return 2
    idx = Index(idxpath)
    if "source_files" in idx.meta:
        # An index built since 2026-10-01 does not list its sources (build_ngram_index.py says
        # why): it records how many, how much code, and where the builder looked.
        print(f"index: {os.path.basename(idxpath)}  threshold {idx.threshold}  n={idx.ns}  "
              f"{idx.meta['source_files']} source binaries, "
              f"{idx.meta.get('source_exec_mb', 0):,.0f} MB of code")
    src = idx.meta.get("sources", [])
    # An index from before 2026-10-01 lists its sources, and listed one twice: the builder globbed
    # `**/*.exe` and the same build sat in two folders. DISTINCT, not len(src), is the number of
    # binaries. The data is unaffected: merge_max() takes the MAX bucket per key, so a repeat is a
    # no-op. (The builder deduplicates by content now.)
    distinct = {(s.get("binary"), s.get("engine"), s.get("config"), s.get("exec_mb")) for s in src}
    dupes = len(src) - len(distinct)
    if "source_files" not in idx.meta:
        print(f"index: {os.path.basename(idxpath)}  threshold {idx.threshold}  n={idx.ns}  "
              f"{len(distinct)} source binaries"
              + (f" ({len(src)} entries, {dupes} duplicate)" if dupes else ""))

    if "--tsv" in args:
        tsv = args[args.index("--tsv") + 1]
        baseline = None
        if "--baseline" in args:
            baseline = args[args.index("--baseline") + 1]
        do_update = "--update-baseline" in args
        do_check = "--check" in args

        rows = [l.rstrip("\n").split("\t") for l in open(tsv, encoding="utf-8")]
        hdr, rows = rows[0], rows[1:]
        ix = {k: i for i, k in enumerate(hdr)}
        tally = {}
        computed = {}
        print(f"\n{'pattern':<18} {'tgt':<16} {'bound':>10}  verdict")
        for r in sorted(rows, key=lambda r: r[ix["id"]]):
            b, _n, _w, lg, lt = score(idx, r[ix["pattern"]])
            v, _ = verdict(b, lg, lt, idx.threshold - 1)
            tally[v] = tally.get(v, 0) + 1
            pid = r[ix["id"]]
            computed[pid] = (r[ix["target"]], "-" if b is None else str(b), v)
            print(f"  {pid:<16} {r[ix['target']]:<16} "
                  f"{('—' if b is None else b):>10}  {v}")
        print("\n  " + "   ".join(f"{k}={n}" for k, n in sorted(tally.items())))

        if not (do_update or do_check):
            return 0
        if not baseline:
            print("\n--check / --update-baseline need --baseline <path>")
            return 2

        if do_update:
            with open(baseline, "w", encoding="utf-8", newline="\n") as f:
                f.write("# AOB specificity baseline — regenerate with:\n"
                        "#   py tools/pe/aob_specificity.py --tsv out/sweep/patterns.tsv \\\n"
                        "#      --baseline tools/pe/aob-specificity-baseline.tsv --update-baseline\n"
                        "# A DIFF HERE IS THE POINT. It means a pattern's noise profile changed, or a\n"
                        "# new pattern landed without anyone looking at its specificity. Both deserve a\n"
                        "# human. Never regenerate to make CI green without reading the diff first —\n"
                        "# `bound` rising is a pattern getting noisier.\n"
                        "# NOTE: `verdict` CLEAR means \"this window is ABSENT from the index\", i.e.\n"
                        "# never observed — NOT \"measured to be rare\". See aob-block-library-eval.md §7.\n"
                        "id\ttarget\tbound\tverdict\n")
                for pid in sorted(computed):
                    t, b, v = computed[pid]
                    f.write(f"{pid}\t{t}\t{b}\t{v}\n")
            print(f"\nbaseline written: {baseline}  ({len(computed)} patterns)")
            return 0

        # --check: golden-file compare. Any difference fails; the message says how to refresh.
        if not os.path.exists(baseline):
            print(f"\nCHECK FAILED: baseline not found: {baseline}")
            return 1
        recorded = {}
        with open(baseline, encoding="utf-8") as f:
            for line in f:
                if line.startswith("#") or line.startswith("id\t") or not line.strip():
                    continue
                p = line.rstrip("\n").split("\t")
                if len(p) >= 4:
                    recorded[p[0]] = (p[1], p[2], p[3])
        problems = []
        for pid in sorted(set(computed) | set(recorded)):
            c, r = computed.get(pid), recorded.get(pid)
            if r is None:
                problems.append(f"NEW pattern {pid} ({c[0]}) scores bound={c[1]} {c[2]} — record it")
            elif c is None:
                problems.append(f"GONE from Himmel.h: {pid} — stale baseline row")
            elif c[1:] != r[1:]:
                worse = ""
                try:
                    if c[1] != "-" and r[1] != "-" and int(c[1]) > int(r[1]):
                        worse = "  <-- BOUND ROSE: the pattern got noisier"
                except ValueError:
                    pass
                if r[2] == "CLEAR" and c[2] != "CLEAR":
                    worse += "  <-- lost CLEAR"
                problems.append(f"CHANGED {pid}: bound {r[1]}->{c[1]}  verdict {r[2]}->{c[2]}{worse}")
        if problems:
            print("\nCHECK FAILED — specificity differs from the recorded baseline:\n")
            for p in problems:
                print("  *", p)
            print("\nThis is the authoring gate (aob-block-library-eval.md build order, step 5).")
            print("Read the diff, then if it is intended:")
            print("  py tools/pe/aob_specificity.py --tsv out/sweep/patterns.tsv \\")
            print(f"     --baseline {baseline} --update-baseline")
            print("\nA rising `bound` means the pattern matches more of the indexed corpus.")
            print("A verdict moving CLEAR->UNPROVEN can ALSO mean the index improved — CLEAR only")
            print("ever means \"never seen\" (eval §7), so check which changed before assuming fault.")
            return 1
        print(f"\nCHECK OK: {len(computed)} patterns match the recorded specificity baseline")
        return 0

    if not args:
        print(__doc__)
        return 1
    for pat in args:
        report(idx, pat)
    return 0


if __name__ == "__main__":
    sys.exit(main())
