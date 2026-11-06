"""Group zdiff TSV rows into transitions: kind, pin mnemonic -> master mnemonic,
and (for OPS) the per-operand fields that changed. Prints counts and one sample
per group. Usage: py -I summarize.py <tsv>... [--dk-only]"""
import collections
import re
import sys


def ops_delta(ra, rb):
    pa, pb = ra.split(" | "), rb.split(" | ")
    if len(pa) != len(pb):
        return f"opcount {len(pa)-1}->{len(pb)-1}"
    out = []
    for i, (x, y) in enumerate(zip(pa[1:], pb[1:])):
        if x != y:
            # Keep only the token(s) that differ, e.g. a2 -> a3 (actions) or s32 -> s64 (size).
            tx, ty = x.split(), y.split()
            d = [f"{u}->{v}" for u, v in zip(tx, ty) if u != v]
            out.append(f"op{i}:" + ",".join(re.sub(r"0x[0-9a-f]+", "#", t) for t in d))
    return ";".join(out)


def main():
    files = [a for a in sys.argv[1:] if not a.startswith("--")]
    dk_only = "--dk-only" in sys.argv
    groups = collections.Counter()
    sample = {}
    total = 0
    for fn in files:
        with open(fn, encoding="utf-8", errors="replace") as f:
            for line in f:
                c = line.rstrip("\n").split("\t")
                if len(c) < 11:
                    continue
                mode, rva, hexb, dk, kind, ra, rb, da, db = c[:9]
                if dk_only and dk != "1":
                    continue
                total += 1
                ma = ra.split(" ")[1] if not ra.startswith("FAIL") else "FAIL"
                mb = rb.split(" ")[1] if not rb.startswith("FAIL") else "FAIL"
                if kind == "OPS":
                    key = (kind, ma, ops_delta(ra, rb), dk)
                elif kind == "LEN":
                    key = (kind, f"{ma}->{mb}", f"len {ra.split(' ')[0]}->{rb.split(' ')[0]}", dk)
                else:
                    key = (kind, f"{ma}->{mb}", "", dk)
                groups[key] += 1
                sample.setdefault(key, (fn, rva, hexb, ra, rb, da, db))
    print(f"rows={total} groups={len(groups)}")
    for key, n in groups.most_common(60):
        fn, rva, hexb, ra, rb, da, db = sample[key]
        print(f"{n:9d}  {key[0]:8s} {key[1]:28s} {key[2]:40s} dk={key[3]}")
        print(f"           e.g. rva {rva} bytes {hexb}\n             pin: {ra[:150]}\n             mas: {rb[:150]}")
        if key[3] == "1":
            print(f"             dk pin: {da[:120]}\n             dk mas: {db[:120]}")


if __name__ == "__main__":
    main()
