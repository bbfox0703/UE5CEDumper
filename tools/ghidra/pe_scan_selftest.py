#!/usr/bin/env python3
"""Self-test for the Ghidra-free sweep's two risky parts. Stdlib only, no corpus, ~1 second.

    py tools/ghidra/pe_scan_selftest.py

The full acceptance test is byte-identical output against a Ghidra sweep
(`compare_sweeps.py`), but that needs the 59.4 GB archive and 15 minutes. This covers the two
places where the port could silently diverge, on synthetic input, so CI can run it:

  1. THE PREFILTER. `scan_patterns.java` tests every pattern at every byte via an anchor-bucket
     map; `pe_scan_patterns.py` searches each pattern's longest literal run with `bytes.find` and
     verifies the mask at each hit. That is only equivalent because a prefilter hit is a necessary
     condition of a match — an assumption worth testing rather than asserting, especially for
     nibble wildcards and for patterns whose literal run is not at offset 0.
  2. THE BLOCK MODEL. `pe_memory.py` claims a section's initialized block is `SizeOfRawData` long,
     not `min(SizeOfRawData, VirtualSize)`, and that the virtual remainder becomes a separate
     uninitialized block.

EVERY CHECK HERE IS PAIRED WITH A NEGATIVE CONTROL — a deliberately broken variant that the same
assertion must REJECT. A test that has never been shown to fail is not evidence that the thing it
tests works; it is evidence that it ran.
"""
import os
import random
import struct
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pe_memory import PeMemory                                        # noqa: E402
import pe_scan_patterns as P                                          # noqa: E402

FAILS = []


def check(name, cond, detail=""):
    if cond:
        print("  ok   %s" % name)
    else:
        FAILS.append(name)
        print("  FAIL %s   %s" % (name, detail))


# ---------------------------------------------------------------------------------------------
# 1. prefilter vs brute force
# ---------------------------------------------------------------------------------------------
def brute_positions(buf, pat_bytes, pat_mask):
    """scan_patterns.java's matcher, transcribed with no prefilter."""
    out, n, plen = [], len(buf), len(pat_bytes)
    for start in range(n - plen + 1):
        for k in range(plen):
            m = pat_mask[k]
            if m and (buf[start + k] & m) != pat_bytes[k]:
                break
        else:
            out.append(start)
    return out


def prefilter_positions(buf, sig):
    out, blen, plen = [], len(buf), len(sig.bytes)
    pos = buf.find(sig.lit)
    while pos >= 0:
        start = pos - sig.lit_at
        if start >= 0 and start + plen <= blen:
            for k in range(plen):
                m = sig.mask[k]
                if m and (buf[start + k] & m) != sig.bytes[k]:
                    break
            else:
                out.append(start)
        pos = buf.find(sig.lit, pos + 1)
    return out


def make_sig(pat):
    s = P.Sig()
    s.pat = pat
    ok = P.parse(s)
    return s if ok else None


def random_pattern(rnd, length):
    """A mix of literal, full-wildcard and NIBBLE-wildcard tokens, guaranteed to contain at least
    one fully literal byte (`parse` rejects a pattern with no anchor, as the Java does)."""
    toks = []
    for _ in range(length):
        r = rnd.random()
        b = rnd.randrange(256)
        if r < 0.55:
            toks.append("%02x" % b)
        elif r < 0.7:
            toks.append("??")
        elif r < 0.85:
            toks.append("%x?" % (b >> 4))
        else:
            toks.append("?%x" % (b & 0xF))
    if not any("?" not in t for t in toks):
        toks[rnd.randrange(len(toks))] = "%02x" % rnd.randrange(256)
    return " ".join(toks)


def materialize(rnd, s):
    """A byte string that satisfies the pattern — the selftest PLANTS matches with it."""
    out = bytearray()
    for k in range(len(s.bytes)):
        m = s.mask[k]
        out.append((s.bytes[k] & m) | (rnd.randrange(256) & (~m & 0xFF)))
    return bytes(out)


def test_prefilter():
    print("\n== prefilter vs brute force ==")
    rnd = random.Random(0xC0FFEE)
    # MATCHES ARE PLANTED, not hoped for. Over random bytes a 3-byte literal essentially never
    # hits, so an unseeded run compares "both found nothing" a few hundred times and would pass
    # with the prefilter deleted. The small alphabet on top of that produces the near-misses and
    # the OVERLAPPING matches (a planted copy can start inside another) that make the comparison
    # worth running.
    cases = agree = total_hits = zero_hit = 0
    for _ in range(400):
        buf = bytearray(rnd.choice(b"\x00\x01\x0f\x48\x8b\x05\x4c\xff\x90") for _ in range(3000))
        s = make_sig(random_pattern(rnd, rnd.randrange(3, 9)))
        if s is None:
            continue
        for _ in range(rnd.randrange(1, 5)):
            at = rnd.randrange(0, len(buf) - 16)
            lit = materialize(rnd, s)
            buf[at:at + len(lit)] = lit
        buf = bytes(buf)
        cases += 1
        a = brute_positions(buf, s.bytes, s.mask)
        b = prefilter_positions(buf, s)
        total_hits += len(a)
        zero_hit += (len(a) == 0)
        if a == b:
            agree += 1
        else:
            check("prefilter agrees on %r" % s.pat, False, "brute=%r prefilter=%r" % (a[:5], b[:5]))
    check("prefilter == brute force on %d random patterns (%d hits found)" % (cases, total_hits),
          agree == cases and total_hits > 0,
          "agree=%d/%d hits=%d" % (agree, cases, total_hits))
    check("every case actually had a match to find (planting worked)", zero_hit == 0,
          "%d of %d cases found nothing — that many comparisons proved nothing"
          % (zero_hit, cases))

    # NEGATIVE CONTROL: break the prefilter (search a literal the pattern does not contain) and
    # the same comparison must now FAIL. Without this, "agree" could mean the comparison is blind.
    rnd = random.Random(0xC0FFEE)
    caught = 0
    for _ in range(400):
        buf = bytearray(rnd.choice(b"\x00\x01\x0f\x48\x8b\x05\x4c\xff\x90") for _ in range(3000))
        s = make_sig(random_pattern(rnd, rnd.randrange(3, 9)))
        if s is None:
            continue
        for _ in range(rnd.randrange(1, 5)):
            at = rnd.randrange(0, len(buf) - 16)
            lit = materialize(rnd, s)
            buf[at:at + len(lit)] = lit
        buf = bytes(buf)
        if not brute_positions(buf, s.bytes, s.mask):
            continue
        broken = make_sig(s.pat)
        broken.lit = bytes([b ^ 0xFF for b in s.lit])                 # a literal that cannot match
        if prefilter_positions(buf, broken) != brute_positions(buf, s.bytes, s.mask):
            caught += 1
    check("negative control: a broken prefilter is detected", caught > 0,
          "the comparison never fired — it may not be able to see a difference")


# ---------------------------------------------------------------------------------------------
# 2. block model
# ---------------------------------------------------------------------------------------------
def build_pe(sections, image_base=0x140000000, size_of_headers=0x400):
    """A minimal PE32+ good enough for pe_memory.py. sections = [(name, rva, vsize, raw, chars)]"""
    e_lfanew = 0x80
    opt_size = 240
    hdr = bytearray(size_of_headers)
    hdr[0:2] = b"MZ"
    struct.pack_into("<I", hdr, 0x3C, e_lfanew)
    struct.pack_into("<4sHHIIIHH", hdr, e_lfanew, b"PE\0\0", 0x8664, len(sections), 0, 0, 0,
                     opt_size, 0x22)
    opt = e_lfanew + 24
    struct.pack_into("<H", hdr, opt, 0x20B)                           # PE32+
    struct.pack_into("<Q", hdr, opt + 24, image_base)
    struct.pack_into("<I", hdr, opt + 60, size_of_headers)
    tbl = opt + opt_size
    body = bytearray()
    ptr = size_of_headers
    for i, (name, rva, vsz, raw, chars) in enumerate(sections):
        o = tbl + i * 40
        hdr[o:o + 8] = name.encode("latin1").ljust(8, b"\0")
        struct.pack_into("<IIII", hdr, o + 8, vsz, rva, len(raw), ptr if raw else 0)
        struct.pack_into("<I", hdr, o + 36, chars)
        body += raw
        ptr += len(raw)
    return bytes(hdr) + bytes(body)


def test_block_model():
    print("\n== block model ==")
    EXEC = 0x60000020
    DATA = 0xC0000040
    text = bytes(range(256)) * 8                                      # 2048 raw
    packed = b"\x90" * 1024                                           # exec, raw 1024 < vsize 3000
    rdata = b"\xAA" * 1024
    pe = build_pe([
        (".text", 0x1000, 2000, text, EXEC),                          # raw 2048 > vsize 2000
        (".packed", 0x2000, 3000, packed, EXEC),                      # vsize 3000 > raw 1024
        (".data", 0x3000, 4096, rdata, DATA),                         # vsize 4096 > raw 1024
        (".bss", 0x4000, 8192, b"", DATA),                            # no raw data at all
    ])
    with tempfile.NamedTemporaryFile(suffix=".exe", delete=False) as f:
        f.write(pe)
        path = f.name
    try:
        m = PeMemory(path)
        names = [(b.name, b.start - m.image_base, b.size, b.execute, b.initialized)
                 for b in m.blocks]
        check("image base", m.image_base == 0x140000000, hex(m.image_base))
        check("one block per section, sized max(raw, virtual); a raw-less section is uninitialized",
              names == [("Headers", 0, 0x400, False, True),
                        (".text", 0x1000, 2048, True, True),
                        (".packed", 0x2000, 3000, True, True),
                        (".data", 0x3000, 4096, False, True),
                        (".bss", 0x4000, 8192, False, False)], names)
        check("exec block with raw > virtual takes the RAW size",
              m.exec_blocks()[0].size == 2048, m.exec_blocks()[0].size)
        check("exec block with virtual > raw takes the VIRTUAL size (the DQ7R case)",
              m.exec_blocks()[1].size == 3000, m.exec_blocks()[1].size)
        check("exec block bytes", m.block_bytes(m.exec_blocks()[0]) == text)
        check("a short-raw block is zero-padded to its virtual size",
              m.block_bytes(m.exec_blocks()[1]) == packed + bytes(3000 - 1024))
        check("a raw-less block reads as zeros", m.block_bytes(m.blocks[4]) == bytes(8192))
        base = m.image_base
        (want,) = struct.unpack_from("<q", text, 16)
        check("get_long in an initialized block", m.get_long(base + 0x1000 + 16) == want)
        check("get_long inside the zero padding is 0, not None",
              m.get_long(base + 0x2000 + 2000) == 0)
        check("get_long straddling raw and padding takes the raw bytes then zeros",
              m.get_long(base + 0x2000 + 1020) == 0x90909090)
        check("get_long in an uninitialized block is None", m.get_long(base + 0x4000) is None)
        check("get_long outside memory is None", m.get_long(base + 0x900000) is None)
        check("get_long on a negative address is None", m.get_long(-8) is None)
        check("get_long past the end of a block is None", m.get_long(base + 0x1000 + 2044) is None)
        check("get_block classifies exec vs data",
              m.get_block(base + 0x1000).execute and not m.get_block(base + 0x3000).execute)
        # NEGATIVE CONTROLS. Both of the rules this replaced fit SOME of the corpus, which is why
        # they have to be shown wrong here rather than argued against.
        check("negative control: min(raw,virtual) would give a different size",
              min(2048, 2000) != m.exec_blocks()[0].size)
        check("negative control: raw alone would give a different size on the packed section",
              1024 != m.exec_blocks()[1].size)
    finally:
        os.unlink(path)


def test_hit_cap():
    """`HIT_DETAIL_CAP` keeps the per-hit detail list bounded while the COUNT keeps rising. No
    corpus program reaches 40 000 hits for one pattern, so the acceptance sweep never exercises
    this — it is transcribed from the Java and tested here or nowhere."""
    print("\n== hit detail cap ==")
    EXEC = 0x60000020
    n = P.HIT_DETAIL_CAP + 500
    text = b"\x48\x8b\x05\x00\x00\x00\x00" * n                        # one match per repetition
    pe = build_pe([(".text", 0x1000, len(text), text, EXEC)])
    with tempfile.NamedTemporaryFile(suffix=".exe", delete=False) as f:
        f.write(pe)
        path = f.name
    try:
        m = PeMemory(path)
        s = make_sig("48 8B 05 ?? ?? ?? ??")
        s.io = s.opc = 0
        s.tot = 7
        s.adj = 0
        P.scan_block([s], m.block_bytes(m.exec_blocks()[0]), m.exec_blocks()[0].start, m)
        check("count is uncapped", s.n_hits == n, "%d vs %d" % (s.n_hits, n))
        check("detail list is capped", len(s.hits) == P.HIT_DETAIL_CAP, len(s.hits))
        check("negative control: the count and the detail list really do differ here",
              s.n_hits != len(s.hits))
    finally:
        os.unlink(path)


def test_java_formatting():
    print("\n== Java-compatible formatting ==")
    check("%.2f rounds HALF_UP like Java", P._jfmt(0.125, 2) == "0.13", P._jfmt(0.125, 2))
    check("negative control: Python's round() would give 0.12", round(0.125, 2) == 0.12)
    check("%.1f", P._jfmt(2.25, 1) == "2.3", P._jfmt(2.25, 1))
    check("signed 64-bit wrap", P.s64(0xFFFFFFFFFFFFFFFF) == -1)
    check("unsigned hex of a negative long", P.jhex_u(-1) == "FFFFFFFFFFFFFFFF")
    check("Long.toHexString of a negative long", P.jhex_l(-1) == "ffffffffffffffff")
    check("LinkedHashMap.toString is DECIMAL",
          P.jmap({"GObjects": [5417441264, 5417441280]})
          == "{GObjects=[5417441264, 5417441280]}")
    check("empty map", P.jmap({}) == "{}")


def main():
    print("pe_scan_selftest")
    test_prefilter()
    test_block_model()
    test_hit_cap()
    test_java_formatting()
    print("\n%d check(s) failed" % len(FAILS) if FAILS else "\nall checks passed")
    return 1 if FAILS else 0


if __name__ == "__main__":
    sys.exit(main())
