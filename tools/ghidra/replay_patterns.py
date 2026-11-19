"""Replay every Himmel.h pattern against a PE's executable sections and report, per target,
which VAs the RIP-relative resolutions converge on. Independent of Ghidra and of the PDB, so it
corroborates (or refutes) a pdb_globals.py derivation the way GROUND-TRUTH.md's "triple-derived"
rule wants.

Usage: py replay.py <patterns.tsv> <file.exe> [expected.json]
"""
import json
import os
import re
import struct
import sys
from collections import defaultdict


def sections(data):
    pe = struct.unpack_from("<I", data, 0x3C)[0]
    nsec = struct.unpack_from("<H", data, pe + 6)[0]
    optsz = struct.unpack_from("<H", data, pe + 20)[0]
    base = struct.unpack_from("<Q", data, pe + 24 + 24)[0]
    tbl = pe + 24 + optsz
    out = []
    for i in range(nsec):
        o = tbl + i * 40
        name = data[o:o + 8].rstrip(b"\0").decode("latin1")
        vsz, va, rsz, ptr = struct.unpack_from("<IIII", data, o + 8)
        chars = struct.unpack_from("<I", data, o + 36)[0]
        out.append((name, va, vsz, ptr, rsz, chars))
    return base, out


def compile_pat(pat):
    """-> compiled regex. Pure-Python masking is far too slow over a 120 MB .text; a regex with
    byte classes pushes the inner loop into C. Wrapped in a lookahead so OVERLAPPING matches are
    all reported, which a bare finditer would silently drop."""
    parts = []
    for t in pat.split():
        hi, lo = t[0], t[1]
        if hi != "?" and lo != "?":
            parts.append(re.escape(bytes([int(t, 16)])))
        elif hi == "?" and lo == "?":
            parts.append(b".")
        elif lo == "?":
            base = int(hi, 16) << 4
            parts.append(b"[" + re.escape(bytes([base])) + b"-" + re.escape(bytes([base + 15])) + b"]")
        else:
            lov = int(lo, 16)
            parts.append(b"[" + b"".join(re.escape(bytes([(h << 4) | lov])) for h in range(16)) + b"]")
    return re.compile(b"(?=(" + b"".join(parts) + b"))", re.DOTALL)


def scan(buf, cpat):
    return [m.start() for m in cpat.finditer(buf)]


def main():
    tsv, exe = sys.argv[1], sys.argv[2]
    expected = {}
    if len(sys.argv) > 3:
        expected = {k: int(v, 16) for k, v in json.load(open(sys.argv[3])).items()}
    data = open(exe, "rb").read()
    base, secs = sections(data)
    execs = [s for s in secs if s[5] & 0x20000000]
    lo = min(s[1] for s in secs) + base
    hi = max(s[1] + max(s[2], s[4]) for s in secs) + base

    rows = [l.rstrip("\n").split("\t") for l in open(tsv, encoding="utf-8")]
    hdr, rows = rows[0], rows[1:]
    ix = {k: i for i, k in enumerate(hdr)}

    votes = defaultdict(lambda: defaultdict(set))   # target -> va -> {pattern ids}
    only = os.environ.get("ONLY_TARGET")
    topn = int(os.environ.get("TOPN", "4"))
    for r in rows:
        if r[ix["resolve"]] not in ("RipBoth", "RipDirect", "RipDeref"):
            continue
        pid, tgt = r[ix["id"]], r[ix["target"]]
        if only and tgt != only:
            continue
        io, opc, tot = int(r[ix["io"]]), int(r[ix["opc"]]), int(r[ix["tot"]])
        adj = int(r[ix["adj"]])
        cpat = compile_pat(r[ix["pattern"]])
        for name, sva, vsz, ptr, rsz, _ in execs:
            buf = data[ptr:ptr + min(rsz, vsz)]
            for p in scan(buf, cpat):
                ia = base + sva + p + io
                fo = ptr + p + io + opc
                if fo + 4 > len(data):
                    continue
                disp = struct.unpack_from("<i", data, fo)[0]
                va = ia + tot + disp + adj
                if lo <= va < hi:
                    votes[tgt][va].add(pid)

    for tgt in ([only] if only else ("GObjects", "GNames", "GWorld", "SparseDelegates", "GEngine")):
        print(f"\n== {tgt}")
        ranked = sorted(votes[tgt].items(), key=lambda kv: -len(kv[1]))[:topn]
        exp = expected.get(tgt)
        for va, ids in ranked:
            mark = ""
            if exp is not None:
                if va == exp:
                    mark = "  <== MATCHES PDB"
                elif va == exp + 0x10:
                    mark = "  <== PDB + 0x10 (ObjObjects)"
            names = ", ".join(sorted(ids)[:8]) + ("..." if len(ids) > 8 else "")
            print(f"  {va:x}  n={len(ids):<3} [{names}]{mark}")


if __name__ == "__main__":
    # Guard so extract_blocks.py can reuse sections()/compile_pat()/scan() by import. Without
    # it the module would run main() at import and die on sys.argv.
    main()
