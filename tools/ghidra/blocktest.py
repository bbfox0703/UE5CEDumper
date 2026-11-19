"""Pattern regression test over the shape blocks. Milliseconds, no Ghidra, no corpus, stdlib only.

THE GAP THIS FILLS. Between `extract_patterns.py` (which only checks for dead constants) and the
full sweep (which needs ~200 GB of Ghidra projects) nothing else tests a pattern, so without this an
edit to a `Himmel.h` pattern is unverifiable on a machine without the corpus. This runs anywhere,
including CI.

WHAT IT ASSERTS, and it is the strong property: for each recorded site, the pattern that found it
must still match AND still RESOLVE to the same address. "The bytes still match" would pass even if
an edit broke the displacement arithmetic and sent a resolve out of bounds.

    py tools/ghidra/blocktest.py                       # -> exit 0 / 1
    py tools/ghidra/blocktest.py --tsv <patterns.tsv>  # against a specific extraction
    py tools/ghidra/blocktest.py -v                    # list every check

WHAT IT DOES NOT DO. It cannot tell you a pattern is quiet (that is `tools/pe/aob_specificity.py`)
and it cannot tell you what the runtime would land on first — the priority walk needs whole images,
so `Himmel.h` rule 5 keeps meaning the sweep. A pattern can pass every block here and still take
thousands of hits on a real game.
"""
import argparse
import json
import os
import re
import struct
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
BLOCKS = os.path.join(REPO, "tools", "ghidra", "blocks", "blocks.json")
DEFAULT_TSV = os.path.join(REPO, "out", "sweep", "patterns.tsv")


def compile_pat(pat):
    """Byte pattern -> regex, honouring nibble wildcards (`4?`, `?d`) like scan_patterns.java."""
    parts = []
    for t in pat.split():
        hi, lo = t[0], t[1]
        if hi != "?" and lo != "?":
            parts.append(re.escape(bytes([int(t, 16)])))
        elif hi == "?" and lo == "?":
            parts.append(b".")
        elif lo == "?":
            b = int(hi, 16) << 4
            parts.append(b"[" + re.escape(bytes([b])) + b"-" + re.escape(bytes([b + 15])) + b"]")
        else:
            v = int(lo, 16)
            parts.append(b"[" + b"".join(re.escape(bytes([(h << 4) | v])) for h in range(16)) + b"]")
    return re.compile(b"(?=(" + b"".join(parts) + b"))", re.DOTALL)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--blocks", default=BLOCKS)
    ap.add_argument("--tsv", default=DEFAULT_TSV)
    ap.add_argument("-v", "--verbose", action="store_true")
    a = ap.parse_args()

    if not os.path.exists(a.blocks):
        print(f"no blocks at {a.blocks} — run tools/ghidra/extract_blocks.py")
        return 2
    if not os.path.exists(a.tsv):
        print(f"no patterns.tsv at {a.tsv} — run tools/ghidra/extract_patterns.py "
              f"dll/src/Himmel.h {a.tsv}")
        return 2

    doc = json.load(open(a.blocks, encoding="utf-8"))
    blocks = doc["blocks"]
    rows = [l.rstrip("\n").split("\t") for l in open(a.tsv, encoding="utf-8")]
    hdr, rest = rows[0], rows[1:]
    pats = {r[hdr.index("id")]: dict(zip(hdr, r)) for r in rest}

    ok = fail = miss = 0
    failures = []
    for b in blocks:
        raw = bytes.fromhex(b["bytes"].replace(" ", ""))
        wva = int(b["window_va"], 16)
        want_va = int(b["resolves_to"], 16)
        pid = b["found_by"]
        r = pats.get(pid)
        if r is None:
            # The pattern was renamed or removed. That is a legitimate change, not a test failure —
            # but it must be visible, or a deleted pattern silently stops being covered.
            miss += 1
            if a.verbose:
                print(f"  SKIP  {b['id']:<44} pattern {pid} no longer in Himmel.h")
            continue
        cpat = compile_pat(r["pattern"])
        io_, opc, tot, adj = (int(r["io"]), int(r["opc"]), int(r["tot"]), int(r["adj"]))
        hit = False
        for m in cpat.finditer(raw):
            p = m.start()
            fo = p + io_ + opc
            if fo + 4 > len(raw):
                continue
            disp = struct.unpack_from("<i", raw, fo)[0]
            if wva + p + io_ + tot + disp + adj == want_va:
                hit = True
                break
        if hit:
            ok += 1
            if a.verbose:
                print(f"  ok    {b['id']:<44} {pid} -> {b['resolves_to']}")
        else:
            fail += 1
            failures.append((b, pid))

    print(f"\nblocks {len(blocks)}   ok {ok}   FAIL {fail}   skipped {miss}")
    if failures:
        print("\nFAILURES — the pattern no longer resolves to the recorded address:")
        for b, pid in failures:
            print(f"  {b['id']:<44} {pid:<16} expected {b['resolves_to']} "
                  f"({b['target']}/{b['cls']}, {b['source_binary']})")
        print("\nA `true`-class failure means a real regression. A `decoy`-class failure means the "
              "pattern stopped matching a known decoy, which is usually an IMPROVEMENT — confirm, "
              "then re-extract the blocks.")
    return 1 if fail else 0


if __name__ == "__main__":
    sys.exit(main())
