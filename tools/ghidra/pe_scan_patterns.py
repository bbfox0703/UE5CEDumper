#!/usr/bin/env python3
"""pe_scan_patterns.py — scan_patterns.java without Ghidra. Byte-identical output, from the PE.

    GS_TSV=patterns.tsv GS_TRUE="GObjects=1423422b0|..." GS_TAG=UE4.10-Game GS_OUT=out \\
        py tools/ghidra/pe_scan_patterns.py <file.exe>

WHY. The sweep's only inputs are image base, block map and bytes (see `pe_memory.py`), all of
which a PE reader can supply. Dropping Ghidra from the sweep removes the JVM, the 12-15 min wall
time, the Ghidra install, and the GB-scale transient writes that `-readOnly` does NOT prevent.

THE BAR IS BYTE-IDENTICAL OUTPUT, so this is a transcription of the Java, not a reimplementation
of the idea, and the awkward parts are awkward on purpose:

  * SIGNED 64-BIT EVERYWHERE. Java longs wrap; Python ints do not. `direct` and the dereferenced
    qword are compared for equality against truth VAs, so reading either unsigned silently
    changes verdicts on any value with the top bit set.
  * JAVA ROUNDS %f HALF-UP, Python rounds half-to-even. Differs on midpoints only, but `%.1f` on
    hits-per-MB reaches them; `_jfmt` does it Java's way.
  * CRLF. Java's `println` uses the platform separator and the reference was produced on Windows.
  * INSERTION-ORDERED CONSENSUS. The Java uses a `LinkedHashMap` there, not a `HashMap`, because
    the iteration order decides ties in the consensus listing; both sides are ordered by first
    appearance. `HashMap` bucket order is deterministic but depends on the JDK's hash spreading,
    so its output would only be reproducible by emulating `java.util.HashMap` — and would shift
    under a JDK upgrade.

Scanning strategy differs from the Java and must not change results: the Java sweeps every byte
once against an anchor-bucket map (cheap in a JIT, hopeless in CPython), while this prefilters
each pattern on its longest fully-literal run using `bytes.find` (a C-speed search) and verifies
the mask at each candidate. A prefilter is a necessary condition of a match, so the match SET is
identical; only the visiting order differs, and nothing here depends on that.
"""
import io
import os
import struct
import sys
from decimal import Decimal, ROUND_HALF_UP

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pe_memory import PeMemory                                        # noqa: E402

HIT_DETAIL_CAP = 40000
MASK64 = (1 << 64) - 1


def s64(v):
    """Two's-complement wrap to a Java long."""
    v &= MASK64
    return v - (1 << 64) if v >> 63 else v


def u64(v):
    return v & MASK64


def _jfmt(value, digits):
    """Java's `String.format("%.Nf", d)` — HALF_UP on the exact double, not half-to-even."""
    q = Decimal(1).scaleb(-digits)
    return str(Decimal(value).quantize(q, rounding=ROUND_HALF_UP))


class Sig:
    __slots__ = ("id", "target", "resolve", "src", "note", "pat", "io", "opc", "tot", "adj",
                 "pri", "bytes", "mask", "anchor", "pat_len", "lit_bytes", "lit_nibbles",
                 "n_hits", "hits", "lit", "lit_at")

    def __init__(self):
        self.anchor = -1
        self.pat_len = 0
        self.lit_bytes = 0
        self.lit_nibbles = 0
        self.n_hits = 0
        self.hits = []


def parse(s):
    """Port of scan_patterns.java's parse(): per-byte AND mask, bytes pre-masked, anchor = the
    first FULLY literal byte. Returns False exactly where the Java does (no literal byte)."""
    toks = s.pat.strip().split()
    b, m = bytearray(len(toks)), bytearray(len(toks))
    s.pat_len = len(toks)
    for i, tok in enumerate(toks):
        if len(tok) != 2:
            return False
        hi, lo = tok[0], tok[1]
        mm = vv = 0
        if hi != "?":
            if hi not in "0123456789abcdefABCDEF":
                return False
            mm |= 0xF0
            vv |= int(hi, 16) << 4
        if lo != "?":
            if lo not in "0123456789abcdefABCDEF":
                return False
            mm |= 0x0F
            vv |= int(lo, 16)
        m[i] = mm
        b[i] = vv & mm
        if mm == 0xFF:
            s.lit_bytes += 1
        if hi != "?":
            s.lit_nibbles += 1
        if lo != "?":
            s.lit_nibbles += 1
    s.bytes, s.mask = bytes(b), bytes(m)
    for i in range(len(m)):
        if m[i] == 0xFF:
            s.anchor = i
            break
    if s.anchor < 0:
        return False
    # Prefilter: the LONGEST run of fully-literal bytes. Ties keep the earliest run, which is
    # arbitrary — the match set does not depend on it.
    best_start = best_len = cur_start = cur_len = 0
    for i in range(len(m)):
        if m[i] == 0xFF:
            if cur_len == 0:
                cur_start = i
            cur_len += 1
            if cur_len > best_len:
                best_len, best_start = cur_len, cur_start
        else:
            cur_len = 0
    s.lit = s.bytes[best_start:best_start + best_len]
    s.lit_at = best_start
    return True


def load_sigs(tsv):
    """Returns (sigs, skipped). Skip rules are the Java's, including the SILENT skip of
    Symbol*/CallFollow — resolutions this offline model cannot reproduce, whose hits would be
    scored as meaningless decoys if included."""
    sigs, skipped, notes = [], 0, []
    with io.open(tsv, encoding="utf-8") as f:
        f.readline()                                                  # header
        for line in f:
            line = line.rstrip("\n")
            fl = line.split("\t")
            if len(fl) < 10:
                continue
            s = Sig()
            s.id, s.target, s.resolve = fl[0], fl[1], fl[2]
            s.io, s.opc, s.tot = int(fl[3]), int(fl[4]), int(fl[5])
            s.adj, s.pri = int(fl[6]), int(fl[7])
            s.src, s.pat = fl[8], fl[9]
            s.note = fl[10] if len(fl) > 10 else ""
            if s.resolve.startswith("Symbol") or s.resolve == "CallFollow":
                skipped += 1
                continue
            if not parse(s):
                skipped += 1
                notes.append("SKIP unparsable " + s.id)
                continue
            sigs.append(s)
    return sigs, skipped, notes


def parse_truth(spec, prog):
    """`GS_TRUE`, including the `programNameSubstring:` scoping that keeps a modular build's
    overlapping DLL address ranges from cross-crediting each other."""
    truth = {}
    if not spec or not spec.strip():
        return truth
    prog_lower = prog.lower()
    for kv0 in spec.split(","):
        kv = kv0.strip()
        if not kv:
            continue
        colon = kv.find(":")
        if colon > 0:
            want = kv[:colon].strip().lower()
            if want not in prog_lower:
                continue
            kv = kv[colon + 1:].strip()
        p = kv.split("=")
        if len(p) < 2:
            continue
        truth[p[0].strip()] = [s64(int(v.strip(), 16)) for v in p[1].strip().split("|")]
    return truth


def scan_block(sigs, buf, base, mem):
    """One executable block. Mirrors the Java inner loop, prefiltered."""
    blen = len(buf)
    for s in sigs:
        lit, lit_at, plen = s.lit, s.lit_at, len(s.bytes)
        sb, sm = s.bytes, s.mask
        pos = buf.find(lit)
        while pos >= 0:
            start = pos - lit_at
            if start >= 0 and start + plen <= blen:
                ok = True
                for k in range(plen):
                    mk = sm[k]
                    if mk and (buf[start + k] & mk) != sb[k]:
                        ok = False
                        break
                if ok:
                    match_va = base + start
                    ins_va = match_va + s.io
                    direct = deref = 0
                    off = ins_va + s.opc - base
                    if 0 <= off and off + 4 <= blen:
                        (rel,) = struct.unpack_from("<i", buf, off)
                        direct = s64(ins_va + s.tot + rel)
                        v = mem.get_long(direct)
                        if v is not None:
                            deref = v
                    s.n_hits += 1
                    if len(s.hits) < HIT_DETAIL_CAP:
                        s.hits.append((match_va, direct, deref))
            pos = buf.find(lit, pos + 1)


def is_data_addr(mem, va):
    if va < 0x10000:
        return False
    b = mem.get_block(va)
    return b is not None and not b.execute


def jhex_l(v):
    """Long.toHexString — lowercase, unsigned."""
    return format(u64(v), "x")


def jhex_u(v):
    """`%X` on a long — uppercase, unsigned."""
    return format(u64(v), "X")


def jmap(truth):
    """java.util.LinkedHashMap.toString() over Map<String,List<Long>> — decimal, not hex."""
    if not truth:
        return "{}"
    return "{" + ", ".join("%s=%s" % (k, jlist(v)) for k, v in truth.items()) + "}"


def jlist(vals):
    return "[" + ", ".join(str(v) for v in vals) + "]"


def jset(vals):
    return "[" + ", ".join(vals) + "]"


def sanitize(n):
    return "".join(c if (c.isascii() and (c.isalnum() or c in "._-")) else "_" for c in n)


class Out:
    """PrintWriter with Java's platform line separator (the reference was made on Windows)."""

    def __init__(self, path, newline="\r\n"):
        self.f = io.open(path, "w", encoding="utf-8", newline="")
        self.nl = newline

    def println(self, s=""):
        self.f.write(s + self.nl)

    def close(self):
        self.f.close()


def run(exe, out_dir, tsv, true_spec, tag, prog=None, image_base_str=None, quiet=False):
    mem = PeMemory(exe)
    if prog is None:
        prog = os.path.basename(exe)
    if not tag:
        tag = prog
    truth = parse_truth(true_spec, prog)
    sigs, skipped, notes = load_sigs(tsv)
    for n in notes:
        if not quiet:
            print(n)

    exec_bytes = 0
    for blk in mem.exec_blocks():
        if blk.size > 0x7FFFFFFF - 8:
            continue
        buf = mem.block_bytes(blk)
        exec_bytes += blk.size
        if not quiet:
            print("scanning %s %s size=%d" % (prog, blk.name, blk.size))
        scan_block(sigs, buf, blk.start, mem)
    exec_mb = exec_bytes / (1024.0 * 1024.0)

    if exec_bytes == 0:
        if not quiet:
            print("SKIP %s — no executable bytes (broken import)" % prog)
        return None

    if image_base_str is None:
        image_base_str = format(mem.image_base, "08x")
    stem = "%s__%s@%s" % (sanitize(tag), sanitize(prog), sanitize(image_base_str))
    base_path = os.path.join(out_dir, "scan_" + stem)
    w = Out(base_path + ".txt")
    t2 = Out(base_path + ".tsv")
    cw = Out(os.path.join(out_dir, "consensus_" + stem + ".txt"))

    w.println("# program %s  tag=%s" % (prog, tag))
    w.println("# image base %s  exec bytes %d (%s MB)" % (image_base_str, exec_bytes,
                                                          _jfmt(exec_mb, 2)))
    w.println("# scan of %d byte patterns (%d symbol/unparsable skipped)" % (len(sigs), skipped))
    w.println("# truth: " + jmap(truth))
    w.println("# verdict: OK = at least one hit resolves (direct/deref, +adj or raw) to the true VA")
    w.println()
    t2.println("tag\tprog\texec_mb\tid\ttarget\tsrc\tpri\tpat_bytes\tlit_bytes\tlit_nibbles"
               "\thits\thits_per_mb\tok\tdecoy\tdistinct_targets\twasted\tverdict\tselected")
    cw.println("# CONSENSUS for %s — VA -> the DISTINCT patterns that resolve there." % prog)
    cw.println("# Candidate = (direct + adjustment), restricted to non-executable blocks.")
    cw.println("# On a symbol-less binary the top row is the de-facto truth: >=3 independent")
    cw.println("# patterns agreeing was validated on Everspace (matched the PDB exactly).")
    cw.println()

    selected_for = set()
    by_target = {}
    for s in sigs:
        by_target.setdefault(s.target, []).append(s)

    for tgt, lst in by_target.items():
        exp = truth.get(tgt)
        w.println("################ TARGET %s  true=%s" % (tgt, "?" if exp is None else jlist(exp)))
        lst = sorted(lst, key=lambda a: a.pri)
        consensus = {}

        for s in lst:
            n_hits = s.n_hits
            n_correct = n_decoy = 0
            first_correct = first_decoy = -1
            ok_sites, decoy_targets, distinct = [], [], []
            decoy_seen, distinct_seen = set(), set()
            for hidx, h in enumerate(s.hits):
                cands = (h[1], s64(h[1] + s.adj), h[2], s64(h[2] + s.adj))
                primary = s64(h[1] + s.adj)
                if is_data_addr(mem, primary):
                    if primary not in distinct_seen:
                        distinct_seen.add(primary)
                        distinct.append(primary)
                    consensus.setdefault(primary, set()).add(s.id)
                this_ok = False
                for c in cands:
                    if c == 0:
                        continue
                    if exp is not None and c in exp:
                        this_ok = True
                if this_ok:
                    n_correct += 1
                    if first_correct < 0:
                        first_correct = hidx
                    if len(ok_sites) < 6:
                        ok_sites.append(jhex_l(h[0]))
                else:
                    n_decoy += 1
                    if first_decoy < 0:
                        first_decoy = hidx
                    if len(decoy_targets) < 10:
                        d = "@%s->%s" % (jhex_u(h[0]), jhex_u(h[1]))
                        if d not in decoy_seen:
                            decoy_seen.add(d)
                            decoy_targets.append(d)

            have_truth = exp is not None
            if have_truth:
                have_truth = any(tv is not None and tv > 0x10000 for tv in exp)
            if n_hits == 0:
                verdict = "MISS      "
            elif not have_truth:
                verdict = "NO-TRUTH  "
            elif n_correct == 0:
                verdict = "DECOY-ONLY"
            elif n_decoy == 0:
                verdict = "UNIQUE-OK "
            else:
                verdict = "OK-FIRST  " if first_correct < first_decoy else "OK-BEHIND "
            if not have_truth:
                n_correct = n_decoy = 0
            trunc = ("  [detail capped at %d]" % len(s.hits)) if s.n_hits > len(s.hits) else ""
            w.println("%-18s pri=%-4d lit=%-3d hits=%-6d ok=%-5d decoy=%-5d %s src=%s%s"
                      % (s.id, s.pri, s.lit_bytes, n_hits, n_correct, n_decoy, verdict, s.src,
                         trunc))
            if ok_sites:
                w.println("        true@ " + jset(ok_sites))
            if decoy_targets:
                w.println("        decoy " + jset(decoy_targets)
                          + ("  ...(%d total)" % n_decoy if n_decoy > 10 else ""))

            is_selected = False
            if have_truth and n_hits > 0 and tgt not in selected_for:
                selected_for.add(tgt)
                is_selected = True
                if n_correct == 0:
                    how = ("  => *** WOULD RESOLVE WRONG unless the validator rejects all %d "
                           "hits ***" % n_decoy)
                elif n_decoy == 0:
                    how = "  => CORRECT (all hits)"
                elif first_correct < first_decoy:
                    how = "  => CORRECT first (%d decoy(s) scan later, never reached)" % n_decoy
                else:
                    how = ("  => AT RISK: %d decoy(s) scan BEFORE the first correct match — "
                           "the validator has to reject every one" % first_correct)
                w.println("  >>> SELECTED (first hitting pattern by priority): " + s.id + how)

            wasted = 0 if n_hits == 0 else (n_hits if not have_truth
                                            else (n_hits if first_correct < 0 else first_correct))
            t2.println("%s\t%s\t%s\t%s\t%s\t%s\t%d\t%d\t%d\t%d\t%d\t%s\t%d\t%d\t%d\t%d\t%s\t%s"
                       % (tag, prog, _jfmt(exec_mb, 2), s.id, s.target, s.src, s.pri, s.pat_len,
                          s.lit_bytes, s.lit_nibbles, n_hits,
                          _jfmt(n_hits / exec_mb if exec_mb > 0 else 0.0, 1),
                          n_correct, n_decoy, len(distinct), wasted, verdict.strip(),
                          "SELECTED" if is_selected else ""))
        w.println()

        ce = sorted(consensus.items(), key=lambda kv: -len(kv[1]))
        cw.println("======== TARGET %s  true=%s" % (tgt, "?" if exp is None else jlist(exp)))
        shown = 0
        for k, v in ce:
            if len(v) < 2 and shown >= 6:
                break
            mark = "  <== TRUE" if (exp is not None and k in exp) else ""
            cw.println("  %-12s  n=%-3d  %s%s" % (jhex_u(k), len(v), jset(sorted(v)), mark))
            shown += 1
            if shown >= 60:
                cw.println("  ...(capped)")
                break

        cw.println("  -- priority walk (first hitting patterns, in scan order) --")
        walked = 0
        for s in lst:
            if s.n_hits == 0:
                continue
            vas = []
            seen = set()
            for h in s.hits:
                primary = s64(h[1] + s.adj)
                if is_data_addr(mem, primary) and primary not in seen:
                    seen.add(primary)
                    vas.append(primary)
                if len(vas) >= 4:
                    break
            sb = "".join(" %s(n=%d)" % (jhex_u(v), len(consensus.get(v, ()))) for v in vas)
            cw.println("   pri=%-4d %-18s hits=%-6d ->%s%s"
                       % (s.pri, s.id, s.n_hits, sb,
                          " ..." if len(vas) >= 4 else (" <none in .data>" if not vas else "")))
            walked += 1
            if walked >= 10:
                cw.println("   ...(walk capped)")
                break
        cw.println()

    w.close()
    t2.close()
    cw.close()
    if not quiet:
        print("pe_scan_patterns DONE -> %s.txt / .tsv / consensus" % base_path)
    return stem


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    exe = sys.argv[1]
    out_dir = os.environ.get("GS_OUT") or "."
    tsv = os.environ.get("GS_TSV")
    if not tsv:
        print("GS_TSV is required")
        return 2
    os.makedirs(out_dir, exist_ok=True)
    run(exe, out_dir, tsv, os.environ.get("GS_TRUE"), os.environ.get("GS_TAG"),
        prog=os.environ.get("GS_PROG") or None,
        image_base_str=os.environ.get("GS_IMAGEBASE") or None)
    return 0


if __name__ == "__main__":
    sys.exit(main())
