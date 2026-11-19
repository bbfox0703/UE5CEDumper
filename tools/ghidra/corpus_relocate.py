#!/usr/bin/env python3
"""Is this set of folders enough to rebuild the whole corpus? Answered by HASH, not by path.

    py tools/ghidra/corpus_relocate.py
    py tools/ghidra/corpus_relocate.py --roots "D:/UE_Analyze_data" --out out/relocation.tsv

WHY THIS EXISTS. `corpus-manifest.json` records where each binary WAS when Ghidra imported it
(`binary_last_seen` comes from the `.rep`'s own `Executable Location`). Those paths rot — a row
ends up pointing at `D:\\tmp\\...`, `X:\\...` or a Steam library that has moved — and
`build_corpus_manifest.py` cannot help: it re-derives the same recorded path, so it can only
ever re-confirm "gone". `preflight.py` then reports those rows as BLOCKING when the file is in
fact sitting on the disk under a different name.

This walks the roots, hashes every PE once, and answers the only question that matters before a
corpus move, a disk cleanup, or a decision to delete a `.rep`:

    for each sweep row -- is its EXACT binary somewhere in these roots, and is there a PDB that
    provably belongs to it?

TWO THINGS IT REFUSES TO GUESS.

  * A MATCHING FILENAME IS NOT A MATCH. `corpus-manifest.json`'s `binary_md5` is the identity
    (it is never nulled, unlike `binary_sha256` which the generator clears on a drifted row --
    so check BOTH; checking only SHA-256 under-reports).
  * A PDB BESIDE THE EXE IS NOT ITS PDB. A PDB from a different build of the same game loads
    without complaint and yields addresses wrong by an unpredictable amount, which is the worst
    failure mode for ground truth because every value still looks plausible. Pairing is decided
    by the CodeView GUID + Age, reusing `tools/pe/pdb_match.py`.

Exit 0 = every row is recoverable from these roots (they are self-sufficient).
Exit 1 = at least one row is not. The report says which, and why.
"""
import argparse
import collections
import csv
import hashlib
import io
import json
import os
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, os.path.join(REPO, "tools", "pe"))
try:
    from pdb_match import pe_codeview, pdb_identity, guid_str
    HAVE_PDB_MATCH = True
except Exception:                                     # pragma: no cover - tool must still run
    HAVE_PDB_MATCH = False

DEFAULT_ROOTS = [r"D:\UE_Analyze_data"]
PE_EXT = (".exe", ".dll")


def sweep_tags(path):
    """(tag -> ghidra project) straight out of sweep.sh's ROWS table."""
    lines = io.open(path, encoding="utf-8", errors="replace").read().splitlines()
    a = next(i for i, l in enumerate(lines) if l.strip() == "ROWS=(")
    b = next(i for i in range(a + 1, len(lines)) if lines[i].strip() == ")")
    out = {}
    for l in lines[a + 1:b]:
        t = l.strip().strip('"')
        if t and not t.startswith("#") and "|" in t:
            p = t.split("|")
            out[p[0].strip('" ')] = p[1].strip('" ')
    return out


def index_roots(roots):
    """{md5|sha256 : path} for every PE under the roots. One read per file, both digests."""
    idx, n, total = {}, 0, 0
    for root in roots:
        if not os.path.isdir(root):
            print(f"  !! root not found, skipped: {root}")
            continue
        for dp, _, fn in os.walk(root):
            for f in fn:
                if not f.lower().endswith(PE_EXT):
                    continue
                p = os.path.join(dp, f)
                h, m = hashlib.sha256(), hashlib.md5()
                try:
                    with open(p, "rb") as fh:
                        while True:
                            c = fh.read(1 << 22)
                            if not c:
                                break
                            h.update(c)
                            m.update(c)
                except OSError:
                    continue
                idx.setdefault(h.hexdigest(), p)
                idx.setdefault(m.hexdigest(), p)
                n += 1
                total += os.path.getsize(p)
    return idx, n, total


def find_pdb_for(exe_path, roots):
    """A PDB whose CodeView GUID+Age match this EXE. Sibling first, then a wider walk.

    Returns (path, status) where status is PAIRED / MISMATCH-ONLY / ABSENT / UNCHECKED.
    """
    if not HAVE_PDB_MATCH:
        return None, "UNCHECKED"
    try:
        cv = pe_codeview(exe_path)
    except Exception:
        cv = None
    if not cv:
        return None, "ABSENT"          # PE carries no CodeView record -> nothing to pair to
    want = (guid_str(cv[0]), cv[1])

    def ok(p):
        try:
            pi = pdb_identity(p)
        except Exception:
            return False
        return bool(pi) and (guid_str(pi[0]), pi[1]) == want

    stem = os.path.splitext(exe_path)[0]
    sib = stem + ".pdb"
    if os.path.exists(sib):
        if ok(sib):
            return sib, "PAIRED"
        near_miss = sib
    else:
        near_miss = None
    base = os.path.basename(stem).lower() + ".pdb"
    for root in roots:
        if not os.path.isdir(root):
            continue
        for dp, _, fn in os.walk(root):
            for f in fn:
                if f.lower() != base:
                    continue
                p = os.path.join(dp, f)
                if ok(p):
                    return p, "PAIRED"
                near_miss = near_miss or p
    return near_miss, ("MISMATCH-ONLY" if near_miss else "ABSENT")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--roots", nargs="*", default=None,
                    help="folders to search (default: %s)" % ", ".join(DEFAULT_ROOTS))
    ap.add_argument("--manifest", default=os.path.join(REPO, "tools", "ghidra", "corpus-manifest.json"))
    ap.add_argument("--sweep", default=os.path.join(REPO, "tools", "ghidra", "sweep.sh"))
    ap.add_argument("--out", default=None, help="write a relocation TSV here")
    ap.add_argument("--no-pdb", action="store_true", help="skip the GUID+Age pairing check")
    args = ap.parse_args()
    roots = args.roots or DEFAULT_ROOTS

    ent = json.load(io.open(args.manifest, encoding="utf-8"))["entries"]
    tags = sweep_tags(args.sweep)

    print("roots:")
    for r in roots:
        print(f"   {'OK ' if os.path.isdir(r) else '!! '} {r}")
    print("\nindexing (one read per PE, md5 + sha256) ...")
    idx, nfiles, nbytes = index_roots(roots)
    print(f"   {nfiles} PE files, {nbytes / 2**30:.1f} GB\n")

    rows, tally = [], collections.Counter()
    for tag in sorted(tags):
        e = ent.get(tag, {})
        md5 = (e.get("binary_md5") or "").lower()
        sha = (e.get("binary_sha256") or "").lower()
        found = idx.get(md5) or idx.get(sha)
        needs_pdb = bool(e.get("needs_pdb"))
        if not found:
            tally["EXE-MISSING"] += 1
            rows.append((tag, "EXE-MISSING", "", "", e.get("binary_last_seen") or ""))
            continue
        if not needs_pdb or args.no_pdb:
            tally["OK (no pdb needed)" if not needs_pdb else "OK (pdb unchecked)"] += 1
            rows.append((tag, "OK", found, "-", ""))
            continue
        pdb, st = find_pdb_for(found, roots)
        tally["OK" if st == "PAIRED" else f"PDB-{st}"] += 1
        rows.append((tag, "OK" if st == "PAIRED" else f"PDB-{st}", found, pdb or "", ""))

    print("=== per-row ===")
    for tag, st, exe, pdb, old in rows:
        mark = "  " if st.startswith("OK") else "!!"
        print(f" {mark} {tag:<26} {st:<20} {os.path.basename(exe) if exe else ''}")
        if st == "EXE-MISSING" and old:
            print(f"        recorded at (gone): {old}")
        elif st == "PDB-MISMATCH-ONLY":
            print(f"        a same-named PDB exists but its GUID/Age do NOT match: {pdb}")

    print("\n=== summary ===")
    for k, v in tally.most_common():
        print(f"   {k:<26} {v}")
    bad = sum(v for k, v in tally.items() if not k.startswith("OK"))
    total = sum(tally.values())
    print(f"\n   {total - bad}/{total} rows recoverable from these roots")
    if args.out:
        os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
        with io.open(args.out, "w", encoding="utf-8", newline="\n") as f:
            w = csv.writer(f, delimiter="\t")
            w.writerow(["tag", "status", "exe_found_at", "pdb_paired_at", "recorded_path_if_gone"])
            w.writerows(rows)
        print(f"   -> {args.out}")
    if bad:
        print("\n   NOT self-sufficient: the rows marked !! cannot be rebuilt from these roots alone.")
        return 1
    print("\n   SELF-SUFFICIENT: every sweep row's exact binary (and a GUID+Age-paired PDB where")
    print("   one is needed) is present in these roots. Copying them carries the whole corpus.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
