#!/usr/bin/env python3
"""Fail the build when the vendored MinHook that dll/src/Lager.h models changes without re-verifying Lager.h.

WHY THIS EXISTS ([MH-PRERESERVE-PROXY])
  Lager.h is a PURE transcription of vendor/minhook/src/buffer.c's x64 trampoline-block search: its constants
  (MEMORY_BLOCK_SIZE, MAX_MEMORY_RANGE, MEMORY_SLOT_SIZE) and its three walks (FindPrevFreeRegion, FindNextFreeRegion,
  GetMemoryBlock) decide every "serves [lo, hi]" / "covers" verdict the DLL logs, and whether the held block lands in
  reach of ProcessEvent. vendor/minhook is a git submodule; a bump that changed any of those would leave Lager.h
  modelling a MinHook that no longer exists, and every dll_helpers_test still passes against Lager's own copy -- the
  live log line is then silently wrong. Lager.h's header says "A change to any of these in the vendored copy
  invalidates this model", but nothing checked it. This does.

  Stark.cpp already static_asserts Lager's MEM_*/PAGE_*/PE constants against the Windows SDK; that is the SDK side.
  This is the MinHook side, which no SDK pins.

WHAT IS CHECKED
  1. buffer.c's `#define MEMORY_BLOCK_SIZE` and `#define MAX_MEMORY_RANGE`, and buffer.h's x64 `MEMORY_SLOT_SIZE`,
     equal Lager.h's kMemoryBlockSize / kMaxMemoryRange / kMemorySlotSize.
  2. The comment-stripped, whitespace-normalised bodies of FindPrevFreeRegion, FindNextFreeRegion and GetMemoryBlock
     hash to GOLDEN. A change to any walk moves the hash.

WHEN IT FIRES
  Re-read the three walks and the constants against Lager.h, update Lager.h (and its dll_helpers_test cases) to match
  the new MinHook, then set GOLDEN below to the printed hash. Do NOT just update GOLDEN: the whole point is that the
  model moved.

Usage:
  py tools/check_lager_vendor_sync.py            # the gate
  py tools/check_lager_vendor_sync.py --list     # print the constants and the computed hash
  py tools/check_lager_vendor_sync.py --selftest # the negative controls only
"""
from __future__ import annotations

import argparse
import hashlib
import pathlib
import re
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ROOT = pathlib.Path(__file__).resolve().parents[1]
BUFFER_C = ROOT / "vendor" / "minhook" / "src" / "buffer.c"
BUFFER_H = ROOT / "vendor" / "minhook" / "src" / "buffer.h"
LAGER_H = ROOT / "dll" / "src" / "Lager.h"

# The three x64 walks Lager.h transcribes, by the signature that opens each.
WALKS = (
    "static LPVOID FindPrevFreeRegion(",
    "static LPVOID FindNextFreeRegion(",
    "static PMEMORY_BLOCK GetMemoryBlock(",
)

# sha256 over the comment-stripped, whitespace-normalised bodies of WALKS, in order. Update ONLY after re-verifying
# Lager.h against the new MinHook (see "WHEN IT FIRES").
GOLDEN = "35ddbd82648539e49de402510233d3e4bb6e12ac3a0da3d4f0a2a593e4f2b751"


def strip_comments(text: str) -> str:
    """Remove C block and line comments. Good enough for buffer.c (no comment-like text inside string literals)."""
    text = text.replace("\r\n", "\n")
    text = re.sub(r"/\*.*?\*/", " ", text, flags=re.DOTALL)
    text = re.sub(r"//[^\n]*", " ", text)
    return text


def extract_body(src_no_comments: str, signature: str) -> str:
    """The brace-matched body of the function opened by `signature`, whitespace-normalised to one line."""
    at = src_no_comments.find(signature)
    if at < 0:
        raise LookupError(signature)
    brace = src_no_comments.find("{", at)
    if brace < 0:
        raise LookupError(signature + " has no body")
    depth = 0
    i = brace
    while i < len(src_no_comments):
        c = src_no_comments[i]
        if c == "{":
            depth += 1
        elif c == "}":
            depth -= 1
            if depth == 0:
                body = src_no_comments[brace:i + 1]
                return re.sub(r"\s+", " ", body).strip()
        i += 1
    raise LookupError(signature + " has an unbalanced body")


def c_int(token: str) -> int:
    return int(token, 0)


def read_define(text: str, name: str) -> int:
    m = re.search(r"#define\s+%s\s+(0x[0-9A-Fa-f]+|\d+)" % re.escape(name), text)
    if not m:
        raise LookupError("#define " + name)
    return c_int(m.group(1))


def read_slot_size_x64(buffer_h_text: str) -> int:
    """buffer.h defines MEMORY_SLOT_SIZE 64 under the x64 #if, 32 under #else. Take the x64 arm."""
    text = buffer_h_text.replace("\r\n", "\n")
    m = re.search(r"#if\s+defined\(_M_X64\)[^\n]*\n\s*#define\s+MEMORY_SLOT_SIZE\s+(\d+)", text)
    if not m:
        raise LookupError("x64 MEMORY_SLOT_SIZE")
    return int(m.group(1))


def read_lager_constant(lager_text: str, name: str) -> int:
    m = re.search(r"constexpr\s+uint64_t\s+%s\s*=\s*(0x[0-9A-Fa-f]+|\d+)" % re.escape(name), lager_text)
    if not m:
        raise LookupError("Lager::" + name)
    return c_int(m.group(1))


def compute_hash(buffer_c_text: str) -> str:
    stripped = strip_comments(buffer_c_text)
    bodies = [extract_body(stripped, sig) for sig in WALKS]
    return hashlib.sha256("\n".join(bodies).encode("utf-8")).hexdigest()


def gather():
    """-> dict of the vendored facts and the Lager copies, raising LookupError on a surface that moved shape."""
    bc = BUFFER_C.read_text(encoding="utf-8", errors="replace")
    bh = BUFFER_H.read_text(encoding="utf-8", errors="replace")
    lg = LAGER_H.read_text(encoding="utf-8", errors="replace")
    return {
        "vendor_block": read_define(bc, "MEMORY_BLOCK_SIZE"),
        "vendor_range": read_define(bc, "MAX_MEMORY_RANGE"),
        "vendor_slot": read_slot_size_x64(bh),
        "lager_block": read_lager_constant(lg, "kMemoryBlockSize"),
        "lager_range": read_lager_constant(lg, "kMaxMemoryRange"),
        "lager_slot": read_lager_constant(lg, "kMemorySlotSize"),
        "hash": compute_hash(bc),
    }


# ---- selftest: the extractor and the comparisons, on synthetic strings (no files) --------------------------------

_SRC_A = (
    "static LPVOID FindPrevFreeRegion(void) { return a; }\n"
    "static LPVOID FindNextFreeRegion(void) { /* c */ return b; }\n"
    "static PMEMORY_BLOCK GetMemoryBlock(void) { if (x) { return y; } return z; }\n"
)
# Same code, only comments and spacing differ -> same hash.
_SRC_A2 = (
    "static LPVOID FindPrevFreeRegion(void)   {   return a;   }\n"
    "static LPVOID FindNextFreeRegion(void) { return b; }\n"
    "static PMEMORY_BLOCK GetMemoryBlock(void) {\n    if (x) { return y; }\n    return z;\n}\n"
)
# A real change to a walk -> different hash.
_SRC_B = (
    "static LPVOID FindPrevFreeRegion(void) { return a; }\n"
    "static LPVOID FindNextFreeRegion(void) { return b2; }\n"
    "static PMEMORY_BLOCK GetMemoryBlock(void) { if (x) { return y; } return z; }\n"
)


def selftest(verbose: bool) -> bool:
    ok = True

    def check(what, cond):
        nonlocal ok
        ok &= cond
        if verbose or not cond:
            print("  %s  %s" % ("PASS" if cond else "FAIL", what))

    ha, ha2, hb = compute_hash(_SRC_A), compute_hash(_SRC_A2), compute_hash(_SRC_B)
    check("comments and spacing do not move the hash", ha == ha2)
    check("a changed walk moves the hash", ha != hb)
    check("brace matching takes the whole GetMemoryBlock body",
          "return z;" in extract_body(strip_comments(_SRC_A), WALKS[2]))
    check("MEMORY_BLOCK_SIZE reads from a #define", read_define("#define MEMORY_BLOCK_SIZE 0x1000\n", "MEMORY_BLOCK_SIZE") == 0x1000)
    check("the x64 slot size is the 64 arm, not the 32",
          read_slot_size_x64("#if defined(_M_X64)\n#define MEMORY_SLOT_SIZE 64\n#else\n#define MEMORY_SLOT_SIZE 32\n#endif\n") == 64)
    check("a missing define is a LookupError, not a silent pass",
          _raises(lambda: read_define("nothing here", "MEMORY_BLOCK_SIZE")))
    return ok


def _raises(fn) -> bool:
    try:
        fn()
        return False
    except LookupError:
        return True


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--list", action="store_true", help="print the constants and the computed hash")
    ap.add_argument("--selftest", action="store_true", help="run the negative controls and exit")
    args = ap.parse_args()

    if not selftest(args.selftest):
        print("check_lager_vendor_sync: its own controls failed (above)")
        return 1
    if args.selftest:
        print("selftest: PASS")
        return 0

    if not BUFFER_C.is_file() or not BUFFER_H.is_file():
        print("check_lager_vendor_sync: SKIP -- vendor/minhook not initialised (git submodule update --init vendor/minhook)")
        return 0

    try:
        g = gather()
    except LookupError as e:
        print("check_lager_vendor_sync: FAILED -- a vendored or Lager surface moved shape and could not be read: %s" % e)
        print("  Re-point this gate and re-verify Lager.h against vendor/minhook/src/buffer.c.")
        return 1

    if args.list:
        print("MEMORY_BLOCK_SIZE  vendor=0x%X  Lager=0x%X" % (g["vendor_block"], g["lager_block"]))
        print("MAX_MEMORY_RANGE   vendor=0x%X  Lager=0x%X" % (g["vendor_range"], g["lager_range"]))
        print("MEMORY_SLOT_SIZE   vendor=%d    Lager=%d" % (g["vendor_slot"], g["lager_slot"]))
        print("walks sha256       %s" % g["hash"])
        print("GOLDEN             %s" % GOLDEN)
        return 0

    problems = []
    if g["vendor_block"] != g["lager_block"]:
        problems.append("MEMORY_BLOCK_SIZE: vendor 0x%X != Lager::kMemoryBlockSize 0x%X" % (g["vendor_block"], g["lager_block"]))
    if g["vendor_range"] != g["lager_range"]:
        problems.append("MAX_MEMORY_RANGE: vendor 0x%X != Lager::kMaxMemoryRange 0x%X" % (g["vendor_range"], g["lager_range"]))
    if g["vendor_slot"] != g["lager_slot"]:
        problems.append("MEMORY_SLOT_SIZE: vendor %d != Lager::kMemorySlotSize %d" % (g["vendor_slot"], g["lager_slot"]))
    if g["hash"] != GOLDEN:
        problems.append("the FindPrev/FindNext/GetMemoryBlock walks changed: sha256 %s != GOLDEN %s" % (g["hash"], GOLDEN))

    if problems:
        print("check_lager_vendor_sync: FAILED -- vendor/minhook moved out of sync with dll/src/Lager.h:")
        for p in problems:
            print("  - " + p)
        print("  Re-verify Lager.h (and its dll_helpers_test cases) against the new MinHook, then update the")
        print("  constant / GOLDEN it names. Run 'py tools/check_lager_vendor_sync.py --list' for the current values.")
        return 1

    print("check_lager_vendor_sync: OK -- Lager.h matches vendor/minhook (block 0x%X, range 0x%X, slot %d, walks %s)"
          % (g["vendor_block"], g["vendor_range"], g["vendor_slot"], g["hash"][:12]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
