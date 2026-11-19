"""Is this .pdb ACTUALLY the one for this .exe, and does it carry what we need?

Why a dedicated check: a matching FILENAME proves nothing. A PDB from a different build of the
same game is named identically, loads without complaint, and yields addresses that are wrong by
an unpredictable amount — which is the worst possible failure for ground truth, because every
value looks plausible. Ghidra applies a mismatched PDB with only a warning.

Two independent questions, and BOTH must pass:

  1. PAIRING — the PE's CodeView debug directory carries {GUID, Age} identifying the exact PDB the
     linker produced. The PDB repeats them in its own info stream (stream 1). Equal GUID AND equal
     Age is the authoritative pairing test; the linker mints a fresh GUID per link, so this cannot
     be satisfied by a rebuild of the same source.
       - GUID differs -> different link entirely. Unusable, full stop.
       - GUID equal but AGE differs -> same link, but the PDB was written to again afterwards
         (incremental link / edit-and-continue). Usually still usable; flagged, not failed.

  2. CONTENT — pairing does not imply usefulness. A "stripped"/public PDB pairs perfectly and still
     has no symbols worth reading. So decode the publics stream and report how many of the five
     globals actually resolve, which is exactly what the corpus needs a PDB FOR.

Usage:
    py tools/pe/pdb_match.py <file.exe|file.pdb>          # partner is inferred by name
    py tools/pe/pdb_match.py <file.exe> <file.pdb>        # explicit pair
    py tools/pe/pdb_match.py --scan <directory>           # every exe/dll under it, recursively

Exit code 0 = every pair checked is USABLE, 1 = at least one is not.
"""
import os
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import pdb_globals as G                                    # noqa: E402

sys.stdout.reconfigure(encoding="utf-8", errors="replace")


def guid_str(b):
    d1, d2, d3 = struct.unpack_from("<IHH", b, 0)
    return (f"{d1:08X}-{d2:04X}-{d3:04X}-{b[8]:02X}{b[9]:02X}-"
            + "".join(f"{x:02X}" for x in b[10:16]))


def pe_build_stamp(path):
    """-> (timestamp, is_repro). AUTHORITATIVE test for whether TimeDateStamp is a real time.

    With `/Brepro` the linker overwrites the COFF TimeDateStamp with a CONTENT HASH and emits a
    debug-directory entry of type 16 (IMAGE_DEBUG_TYPE_REPRO). That entry is the flag — so never
    guess from plausibility. Measured on this corpus, plausibility is actively misleading:
    Hogwarts Legacy (4.27) reads `2025-11-12` and Elliot (5.4) reads `2026-07-15`; BOTH are hashes.
    Roughly a fifth of 32-bit hashes land in a 2000-2030 window.

    Rules of thumb, read off the corpus and NOT a version rule:
      * Epic's UBT enables /Brepro on **SHIPPING only**, from ~5.3. Development and DebugGame keep
        real link times at every version tested (4.10 -> 5.7).
      * UE 5.8 non-Shipping writes TimeDateStamp = 0 outright — a third state, neither time nor
        hash.
      * Third-party studios choose for themselves: Hogwarts is /Brepro at 4.27 while DQ7R, the same
        engine version, is not. So for a shipped game, TEST — do not infer from the UE version.
    """
    d = open(path, "rb").read()
    pe = struct.unpack_from("<I", d, 0x3C)[0]
    ts = struct.unpack_from("<I", d, pe + 8)[0]
    types = [t for t, _sz, _off in _debug_entries(d, pe)]
    return ts, (16 in types)


def _debug_entries(d, pe):
    """-> [(type, size, file_offset_of_payload)] from the PE debug directory."""
    magic = struct.unpack_from("<H", d, pe + 24)[0]
    dd = pe + 24 + (112 if magic == 0x20B else 96)
    rva, size = struct.unpack_from("<II", d, dd + 6 * 8)
    if not rva:
        return []
    nsec = struct.unpack_from("<H", d, pe + 6)[0]
    optsz = struct.unpack_from("<H", d, pe + 20)[0]
    secs = []
    for i in range(nsec):
        o = pe + 24 + optsz + i * 40
        vsz, va, rsz, ptr = struct.unpack_from("<IIII", d, o + 8)
        secs.append((va, max(vsz, rsz), ptr))
    base = next((p + (rva - v) for v, s, p in secs if v <= rva < v + s), None)
    if base is None:
        return []
    out = []
    for i in range(size // 28):
        e = base + i * 28
        # IMAGE_DEBUG_DIRECTORY: Type@12, SizeOfData@16, AddressOfRawData@20, PointerToRawData@24.
        out.append((struct.unpack_from("<I", d, e + 12)[0],
                    struct.unpack_from("<I", d, e + 16)[0],
                    struct.unpack_from("<I", d, e + 24)[0]))
    return out


def pe_codeview(path):
    """-> (guid_bytes, age, pdb_path_recorded_by_the_linker) or None."""
    d = open(path, "rb").read()
    pe = struct.unpack_from("<I", d, 0x3C)[0]
    magic = struct.unpack_from("<H", d, pe + 24)[0]
    # PE32+ (0x20B) puts the data directories 16 bytes further in than PE32 (0x10B).
    dd = pe + 24 + (112 if magic == 0x20B else 96)
    rva, size = struct.unpack_from("<II", d, dd + 6 * 8)          # entry 6 = DEBUG
    if not rva:
        return None
    nsec = struct.unpack_from("<H", d, pe + 6)[0]
    optsz = struct.unpack_from("<H", d, pe + 20)[0]
    secs = []
    for i in range(nsec):
        o = pe + 24 + optsz + i * 40
        vsz, va, rsz, ptr = struct.unpack_from("<IIII", d, o + 8)
        secs.append((va, max(vsz, rsz), ptr))

    def off(r):
        for va, sz, ptr in secs:
            if va <= r < va + sz:
                return ptr + (r - va)
        return None

    base = off(rva)
    if base is None:
        return None
    for i in range(size // 28):
        e = base + i * 28
        typ = struct.unpack_from("<I", d, e + 12)[0]
        # IMAGE_DEBUG_DIRECTORY: ... Type@12, SizeOfData@16, AddressOfRawData@20,
        # PointerToRawData@24. Take @24 — @20 is an RVA and using it as a file offset silently
        # lands in the wrong place, which reads as "no CodeView record" rather than as an error.
        cvsz = struct.unpack_from("<I", d, e + 16)[0]
        cvoff = struct.unpack_from("<I", d, e + 24)[0]
        if typ != 2:                                              # IMAGE_DEBUG_TYPE_CODEVIEW
            continue
        if d[cvoff:cvoff + 4] != b"RSDS":
            continue
        gu = d[cvoff + 4:cvoff + 20]
        age = struct.unpack_from("<I", d, cvoff + 20)[0]
        p = d[cvoff + 24:cvoff + cvsz].split(b"\0")[0].decode("latin1", "replace")
        return gu, age, p
    return None


def pdb_identity(path):
    """-> (guid_bytes, age) from the PDB info stream (stream 1)."""
    p = G.Pdb(path)
    s = p.streams[1]
    # PDBStream70: u32 Version, u32 Signature, u32 Age, GUID[16]
    age = struct.unpack_from("<I", s, 8)[0]
    return s[12:28], age


TARGETS = ("GObjects", "GNames", "GWorld", "SparseDelegates", "GEngine")
# Enough to say "this PDB has real symbol content", without re-implementing pdb_globals' full
# per-version recipe set (GNames in particular needs FNameDebugVisualizer/GetNames fallbacks).
PROBES = {
    "GObjects": "?GUObjectArray@@3VFUObjectArray@@A",
    "GWorld": "?GWorld@@3VUWorldProxy@@A",
    "GEngine": "?GEngine@@3PEAVUEngine@@EA",
    "SparseDelegates": "?SparseDelegates@FSparseDelegateStorage@@0V?$TMap@",
    "GNames": "?GetNames@FName@@",                                # pre-4.23 only; see note below
}


def check(exe, pdb):
    print(f"\n== {os.path.basename(exe)}")
    print(f"   exe {exe}")
    print(f"   pdb {pdb}")
    cv = pe_codeview(exe)
    if not cv:
        print("   PAIRING  ?? no CodeView record in the PE — cannot verify. Treat as UNUSABLE.")
        return False
    g_pe, age_pe, recorded = cv
    try:
        g_db, age_db = pdb_identity(pdb)
    except Exception as e:                                        # noqa: BLE001
        print(f"   PAIRING  ** cannot read PDB info stream: {e}")
        return False

    ok = g_pe == g_db
    print(f"   PE  GUID {guid_str(g_pe)}  age {age_pe}")
    print(f"   PDB GUID {guid_str(g_db)}  age {age_db}")
    print(f"   linker recorded: {recorded}")
    ts, repro = pe_build_stamp(exe)
    if repro:
        print(f"   BUILD TIME  ** /Brepro (IMAGE_DEBUG_TYPE_REPRO) — TimeDateStamp 0x{ts:08X} is a "
              "CONTENT HASH, not a date. Do not read it as one even if it looks plausible.")
    elif ts == 0:
        print("   BUILD TIME  zeroed (0x00000000) — no link time recorded.")
    else:
        import time as _t
        print(f"   BUILD TIME  {_t.strftime('%Y-%m-%d %H:%M', _t.gmtime(ts))} UTC "
              f"(0x{ts:08X}) — real link time, no /Brepro flag.")
    if not ok:
        print("   PAIRING  ** MISMATCH — different link. This PDB's addresses do NOT describe "
              "this binary. UNUSABLE.")
        return False
    if age_pe != age_db:
        print(f"   PAIRING  ~~ GUID matches, AGE differs ({age_pe} vs {age_db}) — same link, PDB "
              "written again after. Usually fine; verify a known symbol before trusting it.")
    else:
        print("   PAIRING  OK — GUID and age both match.")

    try:
        syms = G.Pdb(pdb).symbols()
    except Exception as e:                                        # noqa: BLE001
        print(f"   CONTENT  ** publics stream unreadable: {e}")
        return False
    found = [t for t, pat in PROBES.items() if any(pat in k for k in syms)]
    print(f"   CONTENT  {len(syms)} public symbols; direct hits: "
          f"{', '.join(found) if found else 'NONE'}")
    if not syms:
        print("   CONTENT  ** no public symbols — pairs correctly but carries nothing. UNUSABLE.")
        return False
    # Absences here are frequently CORRECT, not defects, so do not fail on them:
    #   GNames  - no ?GetNames@FName@@ from 4.23 on; pdb_globals falls back to
    #             FNameDebugVisualizer::GetBlocks minus 0x10.
    #   GObjects- no public symbol pre-4.11 (function-local static; see pdb_globals' hint).
    #   Sparse  - genuinely absent before 4.23.
    print("   VERDICT  USABLE — now run `py tools/pe/pdb_globals.py <pdb>` for the actual truth,")
    print("            and corroborate with tools/ghidra/replay_patterns.py (rule 4).")
    return True


def partner(p):
    stem = os.path.splitext(p)[0]
    if p.lower().endswith(".pdb"):
        for ext in (".exe", ".dll"):
            if os.path.exists(stem + ext):
                return stem + ext, p
        return None
    return p, stem + ".pdb"


def main():
    args = sys.argv[1:]
    pairs = []
    if args and args[0] == "--scan":
        for dp, _d, fs in os.walk(args[1]):
            for f in fs:
                if f.lower().endswith((".exe", ".dll")):
                    exe = os.path.join(dp, f)
                    pdb = os.path.splitext(exe)[0] + ".pdb"
                    if os.path.exists(pdb):
                        pairs.append((exe, pdb))
    elif len(args) == 2:
        pairs = [(args[0], args[1])]
    elif len(args) == 1:
        pr = partner(args[0])
        if not pr:
            print("no partner file next to it; pass both paths explicitly")
            return 1
        pairs = [pr]
    else:
        print(__doc__)
        return 1

    if not pairs:
        print("no exe/dll with a same-named .pdb beside it")
        return 1
    bad = 0
    for exe, pdb in sorted(pairs):
        if not os.path.exists(pdb):
            print(f"\n== {os.path.basename(exe)}\n   pdb MISSING: {pdb}")
            bad += 1
            continue
        if not check(exe, pdb):
            bad += 1
    print(f"\n{len(pairs) - bad}/{len(pairs)} usable")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
