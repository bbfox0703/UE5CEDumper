r"""Slice C of the ~166 unadjudicated sweep claims — the ONE question worth asking of a
discarded `ReadSafe`.

    py tools/verify/claims_readsafe_outparam.py            # rank the population
    py tools/verify/claims_readsafe_outparam.py --list     # every hit, with its line

WHY THIS EXISTS. The blind-spot sweep filed 209 claims across three rounds and skepticised 43;
the other ~166 were never adjudicated and were never written down as a list, so they cannot be
walked one by one. What CAN be walked is the POPULATION they were drawn from, and `todo.md`'s own
plan already splits it: of the qualified-call statements whose return is discarded, ~165 are
`Macht::ReadSafe`.

⛔ AND FOR THOSE, "the bool was discarded" IS NOT THE QUESTION. The discard is the documented
idiom — the identical `ReadPtrAt` body appears verbatim in `Edel.cpp`, `Solide.cpp`, `Solitar.cpp`
and `Hemmung.cpp`, and a blanket rule would open with ~165 waivers. `todo.md` says what to ask
instead, and this tool asks exactly that:

    does the UN-SET OUT-PARAM go on to feed a COUNT, or a PUBLISHED field?

Because that is the shape that has actually been confirmed here, four times over. On a faulted
read the out-param keeps its initialised value — usually 0 — and 0 then means something
affirmative downstream:
  * `Ubel.cpp` delegate readers: objIdx/serial stay 0, the weak pointer resolves null, and the
    element is published as "(unbound)" — an affirmative claim that nothing was ever bound;
  * `GetMapPairLayout`: the struct addr stays 0, the alignment is guessed, and every pair after
    the first is strided to a wrong address;
  * `WalkInstance`'s two inlined map copies: the same, and silently, until 2026-09-09;
  * `Fern`'s `delegate_pad`: set but never emitted, so a CE record pointed at address 0.

⚠ THIS IS A RANKER, NOT A VERDICT. It reports where the question is worth asking, ordered by how
close the out-param gets to something a user reads. Every hit still needs a human or an
adversarial agent to answer it, exactly as the four above did. A tool that called these defects
would be the "blanket rule" this file exists to avoid.
"""
from __future__ import annotations

import argparse
import collections
import glob
import io
import re
import sys

# `Macht::ReadSafe(addr, out)` / `ReadSafe<T>(addr, out)` / `ReadBytesSafe(addr, buf, n)` used as
# a bare statement -- the return not bound, tested or returned.
CALL = re.compile(r"^\s*(?:[A-Za-z_][A-Za-z0-9_]*::)*Read(?:Bytes)?Safe\s*(?:<[^>]*>)?\s*\(")
CONSUMED = re.compile(r"=|\breturn\b|\bif\s*\(|\bwhile\s*\(|&&|\|\||!|\?|\bEXPECT\b")

# The out-param is the LAST argument of the call.
LASTARG = re.compile(r",\s*([A-Za-z_][A-Za-z0-9_.\[\]>-]*)\s*\)\s*;\s*$")

# How close does that name then get to something a user reads? Highest first -- these are the
# tiers the four confirmed defects fell into.
TIERS = [
    (40, "PUBLISHED", re.compile(r"\bfv\.|\bresult\.|\bdata\[|\bitem\[|\bj\[|\bout\.|"
                                 r"\belem\.|\bce\.|\bsf\.")),
    (30, "COUNT/SIZE", re.compile(r"\bcount\b|\bnum\b|\bsize\b|\bstride\b|\blen\b|\bmax\b",
                                  re.I)),
    (20, "CONTROL-FLOW", re.compile(r"\bif\s*\(|\bwhile\s*\(|\bfor\s*\(|\?")),
    (10, "PASSED-ON", re.compile(r"\w+\s*\(")),
]
LOOKAHEAD = 14          # statements, not lines -- a faulted read's damage is local or it is none


def scan():
    hits = []
    for path in sorted(glob.glob("dll/src/*.cpp")):
        lines = io.open(path, encoding="utf-8", errors="replace").read().splitlines()
        for i, ln in enumerate(lines):
            if not CALL.match(ln):
                continue
            head = ln.split("(")[0]
            if CONSUMED.search(head):
                continue                       # the bool IS consumed -- not this population
            m = LASTARG.search(ln)
            if not m:
                continue
            out = m.group(1).lstrip("&")
            bare = re.sub(r"[.\[].*$", "", out)
            if not bare or len(bare) < 2:
                continue
            # Where does `bare` go next?
            score, tier, where = 0, "unused?", ""
            word = re.compile(r"\b%s\b" % re.escape(bare))
            for j in range(i + 1, min(i + 1 + LOOKAHEAD, len(lines))):
                nxt = lines[j]
                if not word.search(nxt):
                    continue
                for sc, name, rx in TIERS:
                    if rx.search(nxt) and sc > score:
                        score, tier, where = sc, name, "%s:%d" % (path, j + 1)
                if score >= 40:
                    break
            hits.append({"file": path, "line": i + 1, "out": out, "score": score,
                         "tier": tier, "where": where, "text": ln.strip()[:110]})
    return hits


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--list", action="store_true")
    ap.add_argument("--min-score", type=int, default=40)
    a = ap.parse_args()

    hits = scan()
    print("discarded Read*Safe statements with an identifiable out-param: %d\n" % len(hits))

    by_tier = collections.Counter(h["tier"] for h in hits)
    for sc, name, _ in TIERS + [(0, "unused?", None)]:
        if by_tier.get(name):
            print("  %-14s %3d" % (name, by_tier[name]))

    top = sorted([h for h in hits if h["score"] >= a.min_score],
                 key=lambda h: (-h["score"], h["file"], h["line"]))
    print("\n⭐ WORTH ASKING THE QUESTION OF (out-param reaches a published field): %d" % len(top))
    for h in (top if a.list else top[:20]):
        print("  %s:%d  out=%-22s -> %s" % (h["file"], h["line"], h["out"], h["where"]))
        print("      %s" % h["text"])
    if not a.list and len(top) > 20:
        print("  ... %d more (--list)" % (len(top) - 20))

    print("\n⚠ A hit is a QUESTION, not a verdict: does a faulted read leave this out-param at a "
          "value the code downstream treats as MEANINGFUL? Four such were confirmed in 2026-09; "
          "the same shape at a site whose caller re-checks, or whose 0 is impossible, is clean.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
