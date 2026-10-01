"""Build the AOB specificity index: a byte n-gram FREQUENCY TABLE, shipping no code.

WHAT THIS IS. Slide a fixed-width window over the executable sections and count how often each
distinct byte sequence occurs. The result is a flat {sequence: count} table — a word-frequency list
for machine code. There is no tree, no hierarchy, no relation between entries: `48 8B 05 A1` and
`8B 05 A1 B2` are unrelated keys even though they overlap in the source.

WHY IT CAN BE COMMITTED. Three properties of the FORMAT, each one something the file guarantees:
  1. **Only sequences occurring >= `--threshold` times in a single binary are kept.** This is NOT
     merely a size optimisation. A COMPLETE overlapping n-gram table IS assemblable — de Bruijn /
     Eulerian path, exactly how DNA sequencing reconstructs a genome. Never lower it to 1 "for
     accuracy" — that is the property, not a knob.
  2. **The count is a log2 bucket of the MAX across every source**, not a sum and not exact. The
     stored multiset is therefore not the n-gram spectrum of ANY byte stream, so there is no
     assembly target to recover, and the multiplicities an assembly needs are not there.
  3. **No positions, no order, and no attribution**: a key does not say which source it came from,
     or how many.
Thresholding is a BARRIER rather than a proof — a sequence a program repeats often enough does
survive, and long runs of a source can be covered by surviving keys. What that amounts to is
measured in docs/aob-block-library-eval.md §8; quote those numbers, not "nothing can be
reconstructed".

WHAT IT ANSWERS. Given a candidate AOB, the frequency of its rarest literal window is a hard UPPER
BOUND on how many times the whole pattern can match in any SOURCE binary: every occurrence of the
pattern must contain that window.

WHAT IT CANNOT ANSWER. Whether a pattern hits the RIGHT address. `GNAM_XX_1` bounds at a clean 57
and is DECOY-ONLY. This is a pre-filter; `Himmel.h` rule 5 keeps meaning the sweep.

SOURCES. Every UE program under `--roots`, all build configs: a game's exe, and for a modular build
its `*-Win64-Shipping.dll` modules. An index only knows the code it was built from — one built from
content-free engine templates has seen no game code and bounds a real game badly (eval §8) — so
build it from as many different programs as the machine has. Adding a source only ever LOOSENS a
bound (the union takes the max), never breaks it.

THE FILE DOES NOT SAY WHICH PROGRAMS IT WAS BUILT FROM, on purpose: the set is whatever the builder
had on disk that day, it will differ on the next rebuild and on anyone else's machine, and it is
not part of what the index claims. It records the roots, the number of files, their total code
size, and one digest of the whole set (so `verify_ngram_bound.py` can tell whether the corpus under
those roots is still the one the index was built from). A rebuild is a NEW index: regenerate
`aob-specificity-baseline.tsv` with it, in the same commit.

    py tools/pe/build_ngram_index.py --roots D:/UE_Analyze_data
    py tools/pe/build_ngram_index.py --roots D:/corpus E:/more --exclude AOBMaker --workers 4

Needs numpy, and runs only on a machine that has the binaries. The QUERY tool (aob_specificity.py)
is stdlib-only by design so it works anywhere.
"""
import argparse
import gzip
import hashlib
import json
import os
import struct
import sys
import time

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

MAGIC = b"UEAOBNGX"
FORMAT_VERSION = 2                # 2 = tables stored column-major (write_index); 1 = record rows
DEFAULT_NS = (4, 5, 6)
DEFAULT_ROOT = r"D:\UE_Analyze_data"
# Folders under a root that hold something other than UE programs (a sibling project's corpus).
DEFAULT_EXCLUDE = ("AOBMaker", "_aobmaker_work")
MIN_GAME_EXE = 5_000_000          # below this it is the launcher stub, not the game
MIN_MODULE_DLL = 3_000_000        # a modular build's modules; smaller ones carry no engine code


def exec_sections(path):
    d = open(path, "rb").read()
    pe = struct.unpack_from("<I", d, 0x3C)[0]
    nsec = struct.unpack_from("<H", d, pe + 6)[0]
    optsz = struct.unpack_from("<H", d, pe + 20)[0]
    out = []
    for i in range(nsec):
        o = pe + 24 + optsz + i * 40
        vsz, _va, rsz, ptr = struct.unpack_from("<IIII", d, o + 8)
        if struct.unpack_from("<I", d, o + 36)[0] & 0x20000000:
            out.append(d[ptr:ptr + min(rsz, vsz)])
    return out


def bucket_of(counts, np):
    """count -> 1-byte bucket, decoded later as (1 << bucket).

    MUST decode to an UPPER bound or the whole guarantee breaks: bit_length() gives the smallest b
    with count <= 2**b, so 2**b >= count always. At most 2x loose, never wrong. Reporting the
    bucket's LOWER edge would under-report and make the bound unsound."""
    b = np.zeros(counts.shape, dtype=np.uint8)
    v = counts.astype(np.uint64)
    cur = np.ones(counts.shape, dtype=np.uint64)
    for i in range(1, 64):
        cur = cur << np.uint64(1)
        b[(v > (cur >> np.uint64(1))) & (v <= cur)] = i
    b[v > cur] = 63
    return b


def count_ngrams(bufs, n, threshold, np):
    """-> (keys uint64 sorted, buckets uint8) for sequences occurring >= threshold."""
    keys_all = []
    for buf in bufs:
        if len(buf) <= n:
            continue
        a = np.frombuffer(buf, dtype=np.uint8)
        L = len(a) - n + 1
        v = np.zeros(L, dtype=np.uint64)
        for i in range(n):
            v = (v << np.uint64(8)) | a[i:i + L].astype(np.uint64)
        keys_all.append(v)
    if not keys_all:
        return np.empty(0, dtype=np.uint64), np.empty(0, dtype=np.uint8)
    v = np.concatenate(keys_all) if len(keys_all) > 1 else keys_all[0]
    del keys_all
    uniq, cnt = np.unique(v, return_counts=True)
    del v
    keep = cnt >= threshold
    return uniq[keep], bucket_of(cnt[keep], np)


def merge_max(a_keys, a_buck, b_keys, b_buck, np):
    """Union taking the MAX bucket per key — sound for 'any source in the union'."""
    if a_keys.size == 0:
        return b_keys, b_buck
    if b_keys.size == 0:
        return a_keys, a_buck
    k = np.concatenate([a_keys, b_keys])
    v = np.concatenate([a_buck, b_buck])
    order = np.argsort(k, kind="stable")
    k, v = k[order], v[order]
    # np.maximum.reduceat over runs of equal keys
    first = np.empty(k.shape, dtype=bool)
    first[0] = True
    np.not_equal(k[1:], k[:-1], out=first[1:])
    starts = np.flatnonzero(first)
    return k[starts], np.maximum.reduceat(v, starts)


def sha256_of(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 22), b""):
            h.update(chunk)
    return h.hexdigest()


def config_of(path):
    """Shipping-vs-not for `--shipping-only`, from the path: a packaged tree names its config in a
    folder or in the file name, and a retail exe with neither is a shipped build."""
    low = path.replace("\\", "/").lower()
    if "debuggame" in low:
        return "DebugGame"
    if "/development/" in low:
        return "Development"
    return "Shipping"


def find_sources(roots, exclude=DEFAULT_EXCLUDE, shipping_only=False):
    """Every distinct UE program file under `roots` -> [{path, name, size, sha}], sorted by sha.

    A root may also be a single file. Deduplicated by CONTENT: an archive and an install often hold
    the same build, and three copies of one binary are one source."""
    ex = tuple(e.lower() for e in exclude)
    cands = []
    for root in roots:
        if os.path.isfile(root):
            cands.append(root)
            continue
        for dp, dn, fn in os.walk(root):
            dn[:] = [d for d in dn if d.lower() not in ex]
            in_engine = "/engine/" in dp.replace("\\", "/").lower() + "/"
            for f in fn:
                fl = f.lower()
                p = os.path.join(dp, f)
                if fl.endswith(".exe"):
                    # Engine/ holds the packaged tools (CrashReportClient, ...), never the game exe
                    if in_engine or "crashreport" in fl or os.path.getsize(p) < MIN_GAME_EXE:
                        continue
                elif fl.endswith(".dll"):
                    # ...but a MODULAR build keeps its engine modules there, named like the game's.
                    # EOSSDK carries the same suffix and is a prebuilt SDK, not UE code.
                    if "-win64-shipping" not in fl or fl.startswith("eossdk") \
                            or os.path.getsize(p) < MIN_MODULE_DLL:
                        continue
                else:
                    continue
                if shipping_only and config_of(p) != "Shipping":
                    continue
                cands.append(p)
    seen, out = set(), []
    for p in cands:
        sha = sha256_of(p)
        if sha in seen:
            continue
        seen.add(sha)
        out.append({"path": p, "name": os.path.basename(p), "size": os.path.getsize(p),
                    "sha": sha})
    out.sort(key=lambda s: s["sha"])
    return out


def set_digest(sources):
    """One digest of the whole source set. It identifies no member: it only answers "is the corpus
    under these roots still the one this index was built from"."""
    return hashlib.sha256("\n".join(sorted(s["sha"] for s in sources)).encode()).hexdigest()


def write_index(path, meta, ns, threshold, tables):
    """`tables`: {n: (sorted uint64 keys, uint8 buckets)}.

    FORMAT 2 stores each table COLUMN by column — every record's first key byte, then every second
    byte, ... then every bucket — instead of record by record. The keys are sorted, so the leading
    columns are long runs and gzip takes them almost for free: the same 107.0 MB of records is
    26.5 MB this way against 51.4 MB as rows (measured 2026-10-01, 126 sources). The reader turns
    the columns back into rows in memory, so lookups are unchanged.

    Key bytes are big-endian so lexicographic byte order == numeric order, which lets the query
    tool binary-search the records with no unpacking. gzip, because it is stdlib and the query
    tool must stay dependency-free. mtime=0: the same tables give the same bytes, so a rebuild
    that changed nothing does not rewrite a multi-megabyte blob in git."""
    blob = json.dumps(meta, ensure_ascii=False, indent=1).encode("utf-8")
    raw = open(path, "wb")
    f = gzip.GzipFile(fileobj=raw, mode="wb", mtime=0) if path.endswith(".gz") else raw
    try:
        f.write(MAGIC)
        f.write(struct.pack("<HHI", FORMAT_VERSION, len(ns), len(blob)))
        for n in ns:
            f.write(struct.pack("<BBII", n, 0, threshold, int(tables[n][0].size)))
        f.write(blob)
        for n in ns:
            k, b = tables[n]
            kb = k.astype(">u8").tobytes()
            for j in range(n):
                f.write(kb[8 - n + j::8])
            f.write(b.tobytes())
    finally:
        if f is not raw:
            f.close()
        raw.close()


def _tables_for(job):
    path, ns, threshold = job
    import numpy as np
    try:
        bufs = exec_sections(path)
    except Exception:
        return path, 0.0, None
    mb = sum(len(b) for b in bufs) / 1e6
    if mb < 1:
        return path, mb, None
    return path, mb, {n: count_ngrams(bufs, n, threshold, np) for n in ns}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("-o", "--out", default=os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                                        "aob-ngram-index.bin.gz"))
    ap.add_argument("--roots", nargs="+", default=[DEFAULT_ROOT],
                    help="folders (or single files) holding UE programs")
    ap.add_argument("--exclude", nargs="*", default=list(DEFAULT_EXCLUDE),
                    help="folder names to skip under the roots")
    ap.add_argument("--threshold", type=int, default=16)
    ap.add_argument("--ns", default=",".join(str(x) for x in DEFAULT_NS))
    ap.add_argument("--shipping-only", action="store_true",
                    help="skip Development/DebugGame builds (tighter bounds, and an index that "
                         "has never seen non-Shipping codegen)")
    ap.add_argument("--workers", type=int, default=1)
    ap.add_argument("--limit", type=int, default=0, help="first N sources only (for testing)")
    a = ap.parse_args()

    try:
        import numpy as np
    except ImportError:
        print("numpy required to BUILD the index (the query tool needs nothing).")
        return 1
    if a.threshold < 2:
        print("refusing threshold < 2 — thresholding is what makes the artifact "
              "non-reconstructive, not a tuning knob. See this file's header.")
        return 1

    ns = tuple(int(x) for x in a.ns.split(","))
    if any(n < 3 or n > 8 for n in ns):
        print("n must be 3..8 (packed into a uint64)")
        return 1

    srcs = find_sources(a.roots, a.exclude, a.shipping_only)
    if a.limit:
        srcs = srcs[:a.limit]
    if not srcs:
        print(f"no sources under {a.roots}")
        return 1
    print(f"sources: {len(srcs)} files  (threshold {a.threshold}, n={ns}, "
          f"{'Shipping only' if a.shipping_only else 'all configs'})")

    tables = {n: (np.empty(0, dtype=np.uint64), np.empty(0, dtype=np.uint8)) for n in ns}
    used, total_mb = [], 0.0
    t0 = time.time()
    jobs = [(s["path"], ns, a.threshold) for s in srcs]
    if a.workers > 1:
        from concurrent.futures import ProcessPoolExecutor
        pool = ProcessPoolExecutor(max_workers=a.workers)
        results = pool.map(_tables_for, jobs)
    else:
        results = map(_tables_for, jobs)
    by_path = {s["path"]: s for s in srcs}
    for i, (path, mb, tabs) in enumerate(results, 1):
        if tabs is None:
            print(f"  [{i}/{len(srcs)}] skipped (no code this reader handles): "
                  f"{os.path.basename(path)}")
            continue
        for n in ns:
            tables[n] = merge_max(*tables[n], *tabs[n], np)
        used.append(by_path[path])
        total_mb += mb
        print(f"  [{i}/{len(srcs)}] {mb:7.1f} MB   union "
              + "  ".join(f"n={n} {tables[n][0].size:,}" for n in ns), flush=True)

    meta = {
        "format": "UE5CEDumper AOB n-gram specificity index",
        "format_version": FORMAT_VERSION,
        "threshold": a.threshold,
        "ns": list(ns),
        "roots": [os.path.abspath(r) for r in a.roots],
        "configs": "Shipping only" if a.shipping_only else "all",
        "source_files": len(used),
        "source_exec_mb": round(total_mb, 1),
        "source_set_digest": set_digest(used),
        "sources_are_not_listed":
            "on purpose: the set is whatever was under the roots at build time and is not part of "
            "what the index claims; see build_ngram_index.py",
        "contains": "byte-sequence FREQUENCIES ONLY — no code, no addresses, no symbols",
        "non_reconstructive_because":
            "only sequences occurring >= threshold in one source are kept, and the stored count is "
            "a log2 bucket of the MAX across sources: not the n-gram spectrum of any byte stream",
        "bound_semantics":
            "stored bucket b decodes to (1<<b), an UPPER bound on the true count in any source; a "
            "key absent from a table means its count is < threshold, so the bound is (threshold-1)",
    }
    write_index(a.out, meta, ns, a.threshold, tables)

    sz = os.path.getsize(a.out)
    print(f"\nwrote {a.out}  {sz/2**20:.1f} MB   {len(used)} files, {total_mb:,.0f} MB of code, "
          f"in {time.time()-t0:.0f}s")
    for n in ns:
        print(f"   n={n}: {tables[n][0].size:,} records")
    return 0


if __name__ == "__main__":
    sys.exit(main())
