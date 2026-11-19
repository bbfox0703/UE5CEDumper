"""Read the five AOB-sweep ground-truth globals straight out of a PDB + its PE.

WHY THIS EXISTS. tools/ghidra/GROUND-TRUTH.md's "Deriving truth for a new game" recipe costs a
Ghidra headless run per binary, and a single mistyped VA silently corrupts every verdict in the
sweep downstream (it has happened once). For a binary that ships a PDB none of that is necessary:
the publics stream already holds four of the five addresses, and the fifth (GNames) is two
instructions away. This turns a ~10-minute headless run into a two-second script whose output can
be pasted straight into tools/ghidra/sweep.sh.

No third-party deps — a minimal MSF 7.00 reader, the same house style as pe_imports_exports.py.

WHAT IT EMITS, and the two non-obvious rules baked in:
  * GObjects is printed as the `base|base+0x10` PAIR sweep.sh wants. `?GUObjectArray@@` is the
    FUObjectArray BASE; `ObjObjects` sits at +0x10 up to UE 5.7 and patterns anchor on either, so
    both count as correct. DO NOT emit the pair on UE 5.8+ — 5.8 moved ObjObjects to +0x00, so
    base+0x10 is ObjObjects.NumChunks and the alias would score a hit on an int32 as correct.
    Pass --no-gobjects-alias for 5.8+.
  * GNames has NO usable symbol at ANY version (verified 4.23 through 5.8). It is recovered from
    `FNameDebugVisualizer::GetBlocks`, which is always `lea rax,[&Pool.Entries.Blocks]; ret` —
    so the RIP target minus 0x10 is NamePoolData. The script disassembles those 8 bytes itself
    rather than trusting the shape, and prints what it saw so the -0x10 stays checkable.

Usage:
    py pdb_globals.py <file.pdb> [<file.exe>]        # exe defaults to the pdb's sibling
    py pdb_globals.py <file.pdb> --grep FSparse      # dump every matching symbol instead
"""
import os
import struct
import sys

# Symbol record kinds we care about. Publics carry the globals in a monolithic UE build;
# S_GDATA32/S_LDATA32 show up too and are worth accepting — a value present in only one of
# them is exactly the case a publics-only reader would silently miss.
S_LDATA32 = 0x110C
S_GDATA32 = 0x110D
S_PUB32 = 0x110E

MSF_MAGIC = b"Microsoft C/C++ MSF 7.00\r\n\x1aDS\x00\x00\x00"

# Index of the section-header dump inside the DBI Optional Debug Header substream.
DBG_HDR_SECTION_HDR = 5


class Pdb:
    def __init__(self, path):
        self.data = open(path, "rb").read()
        if self.data[:32] != MSF_MAGIC:
            raise SystemExit(f"not an MSF 7.00 PDB: {path}")
        (self.block_size, _free, self.num_blocks,
         self.dir_bytes, _unk, self.block_map) = struct.unpack_from("<6I", self.data, 32)
        self.streams = self._read_directory()

    def _blocks(self, nums, size):
        out = bytearray()
        for b in nums:
            out += self.data[b * self.block_size:(b + 1) * self.block_size]
        return bytes(out[:size])

    def _read_directory(self):
        nblk = (self.dir_bytes + self.block_size - 1) // self.block_size
        off = self.block_map * self.block_size
        blocks = struct.unpack_from(f"<{nblk}I", self.data, off)
        d = self._blocks(blocks, self.dir_bytes)
        n = struct.unpack_from("<I", d, 0)[0]
        sizes = struct.unpack_from(f"<{n}I", d, 4)
        p = 4 + 4 * n
        streams = []
        for sz in sizes:
            # 0xFFFFFFFF marks a deleted stream — it has no block list at all.
            if sz == 0xFFFFFFFF:
                streams.append(b"")
                continue
            cnt = (sz + self.block_size - 1) // self.block_size
            blk = struct.unpack_from(f"<{cnt}I", d, p) if cnt else ()
            p += 4 * cnt
            streams.append(self._blocks(blk, sz))
        return streams

    def dbi(self):
        """-> (sym_record_stream_index, section_header_stream_index)"""
        s = self.streams[3]
        (_sig, _ver, _age, _gs, _bld, _ps, _dllver,
         sym_rec, _rbld, modinfo, seccontrib, secmap, srcinfo,
         typeserver, _mfc, dbghdr, ec, _flags, _mach, _pad) = struct.unpack_from(
            "<iIIHHHHHHiiiiiIiiHHI", s, 0)
        off = 64 + modinfo + seccontrib + secmap + srcinfo + typeserver + ec
        ids = struct.unpack_from(f"<{dbghdr // 2}H", s, off)
        return sym_rec, ids[DBG_HDR_SECTION_HDR]

    def sections(self, idx):
        """IMAGE_SECTION_HEADER[] -> list of VirtualAddress, 1-based by segment number."""
        s = self.streams[idx]
        return [struct.unpack_from("<I", s, o + 12)[0] for o in range(0, len(s), 40)]

    def symbols(self):
        """-> dict name -> rva, for every S_PUB32 / S_GDATA32 / S_LDATA32."""
        sym_rec, sec_idx = self.dbi()
        va = self.sections(sec_idx)
        s = self.streams[sym_rec]
        out = {}
        p = 0
        n = len(s)
        while p + 4 <= n:
            ln, kind = struct.unpack_from("<HH", s, p)
            if ln < 2:
                break
            body = s[p + 4:p + 2 + ln]
            if kind in (S_PUB32, S_GDATA32, S_LDATA32) and len(body) >= 10:
                _a, off, seg = struct.unpack_from("<IIH", body, 0)
                name = body[10:].split(b"\0", 1)[0].decode("latin1", "replace")
                if 1 <= seg <= len(va) and name and name not in out:
                    out[name] = va[seg - 1] + off
            p += 2 + ln
            p = (p + 3) & ~3   # records are 4-byte aligned
        return out


def image_base(exe):
    d = open(exe, "rb").read(0x400)
    pe = struct.unpack_from("<I", d, 0x3C)[0]
    return struct.unpack_from("<Q", d, pe + 24 + 24)[0]


def read_at_va(exe, va, size):
    """Read `size` bytes of the mapped image at a virtual address, via the section table."""
    data = open(exe, "rb").read()
    pe = struct.unpack_from("<I", data, 0x3C)[0]
    nsec = struct.unpack_from("<H", data, pe + 6)[0]
    optsz = struct.unpack_from("<H", data, pe + 20)[0]
    base = struct.unpack_from("<Q", data, pe + 24 + 24)[0]
    tbl = pe + 24 + optsz
    for i in range(nsec):
        o = tbl + i * 40
        vsz, sva, rsz, ptr = struct.unpack_from("<IIII", data, o + 8)
        if sva <= va - base < sva + max(vsz, rsz):
            fo = ptr + (va - base - sva)
            return data[fo:fo + size]
    return b""


def detect_ue58_plus(syms):
    """-> (is_5_8_plus | None, evidence). GROUND-TRUTH.md's one-second PDB test: 5.8 made
    ~FFieldClass() VIRTUAL, and MSVC mangles a virtual member with `U` where a non-virtual one
    gets `Q`. Used here only to decide the GObjects `base+0x10` alias, which 5.8 invalidates —
    getting that wrong scores a hit on ObjObjects.NumChunks (an int32) as "correct", and it is
    the one judgement call this tool would otherwise leave to the operator's memory."""
    if "??1FFieldClass@@UEAA@XZ" in syms or "??_7FFieldClass@@6B@" in syms:
        return True, "??1FFieldClass@@UEAA@XZ (U = virtual dtor)"
    if "??1FFieldClass@@QEAA@XZ" in syms:
        return False, "??1FFieldClass@@QEAA@XZ (Q = non-virtual dtor)"
    # No FFieldClass at ALL is itself a pre-5.8 answer, not an unknown: the type did not exist
    # before UE 4.25 (that is the FField/FProperty transition), and a large part of the corpus is
    # older than that. Reported so the reasoning stays visible rather than looking like a default.
    return False, "no FFieldClass symbol at all => pre-4.25, therefore pre-5.8"


def find(syms, *prefixes):
    """First symbol whose name starts with any prefix. Prefix, not equality: the sparse-delegate
    mangling embeds its whole template argument list and changes between engine versions."""
    for p in prefixes:
        for k in sorted(syms):
            if k.startswith(p):
                return k, syms[k]
    return None, None


def main():
    pdb_path = sys.argv[1]
    rest = sys.argv[2:]
    grep = None
    alias = None          # None = auto-detect from the PDB; the flags force it either way
    exe = None
    i = 0
    while i < len(rest):
        if rest[i] == "--grep":
            grep = rest[i + 1]
            i += 2
        elif rest[i] == "--no-gobjects-alias":
            alias = False
            i += 1
        elif rest[i] == "--gobjects-alias":
            alias = True
            i += 1
        else:
            exe = rest[i]
            i += 1
    if exe is None:
        stem = os.path.splitext(pdb_path)[0]
        exe = stem + ".exe"

    pdb = Pdb(pdb_path)
    syms = pdb.symbols()
    print(f"# pdb   : {pdb_path}")
    print(f"# exe   : {exe}")
    print(f"# symbols decoded: {len(syms)}")

    if grep:
        for k in sorted(syms):
            if grep.lower() in k.lower():
                print(f"  {syms[k]:#010x}  {k}")
        return

    base = image_base(exe)
    print(f"# imagebase: {base:#x}")

    # Printed output stays 7-bit ASCII on purpose: a Windows console under a legacy code page
    # (cp950 here) raises UnicodeEncodeError mid-print, which turns a working derivation into a
    # traceback. Costs nothing to avoid.
    ue58, why = detect_ue58_plus(syms)
    if alias is None:
        alias = not ue58
        print(f"# engine era: {'UE 5.8+' if ue58 else 'pre-5.8'} - {why} -> GObjects alias "
              f"{'OFF' if ue58 else 'ON'}")
    else:
        print(f"# GObjects alias FORCED {'ON' if alias else 'OFF'} (detector said: {why})")

    out = {}

    def emit(label, name, rva, note=""):
        if rva is None:
            print(f"  {label:<16} NOT FOUND")
            return None
        va = base + rva
        out[label] = va
        print(f"  {label:<16} {va:x}   {note or name[:78]}")
        return va

    n, r = find(syms, "?GUObjectArray@@3VFUObjectArray@@A")
    gobj = emit("GObjects", n, r)
    if gobj is None:
        # Pre-4.11 the array is a FUNCTION-LOCAL STATIC, so it has no public symbol at all and
        # "NOT FOUND" is correct rather than a decoder bug. Point at the recovery route instead of
        # leaving a dead end: on UE 4.10.4 the guarded init inside GetUObjectArray
        # does `lea rbx,[rip+X]`, passes rbx as `this` to the ctor and returns rbx.
        gua = find(syms, "?GetUObjectArray@@YAAEAVFUObjectArray@@XZ")[1]
        if gua is not None:
            print(f"  {'':<16} ^ pre-4.11 function-local static (no public symbol). Disassemble "
                  f"GetUObjectArray @{base + gua:x}: take the `lea` feeding "
                  f"??0FUObjectArray@@QEAA@XZ.")
            dbg = find(syms, "?GetObjectArrayForDebugVisualizers@FUObjectArray@@"
                             "CAPEAPEAPEAVUObjectBase@@XZ")[1]
            if dbg is not None:
                print(f"  {'':<16}   corroborate with GetObjectArrayForDebugVisualizers "
                      f"@{base + dbg:x} == GetUObjectArray() + 0x10 (this MEASURES the alias).")
            print(f"  {'':<16}   NOTE 4.10 has NO FUObjectItem (TStaticIndirectArrayThreadSafeRead,"
                  f" bare UObjectBase*) - finding it does not make it readable.")
    n, r = find(syms, "?GWorld@@3VUWorldProxy@@A")
    emit("GWorld", n, r)
    n, r = find(syms, "?GEngine@@3PEAVUEngine@@EA")
    emit("GEngine", n, r)
    n, r = find(syms, "?SparseDelegates@FSparseDelegateStorage@@")
    emit("SparseDelegates", n, r, note=(n[:60] + "...") if n else "")

    # GNames: no symbol at any version, so it is always recovered from a function.
    #
    # PRE-4.23 comes FIRST because `FNameDebugVisualizer` does not exist yet — there is no
    # FNamePool at all, GNames is the `TNameEntryArray*` that `FName::GetNames` lazily allocates.
    # Its prologue is `sub rsp,0x28; mov rax,[rip+Names]; test rax,rax; ...`, so the load sits at
    # +4 and needs NO -0x10 (that adjustment is an FNamePool/Blocks artifact and applying it here
    # would silently land 16 bytes low). The UE4.15-Flying Shipping row in sweep.sh is the check:
    # this must reproduce its recorded GNames=142c92508 exactly.
    n, r = find(syms, "?GetNames@FName@@")
    if r is not None:
        gn = base + r
        code = read_at_va(exe, gn, 24)
        hit = next((o for o in range(12) if code[o:o + 3] == b"\x48\x8b\x05"), None)
        if hit is not None:
            disp = struct.unpack_from("<i", code, hit + 3)[0]
            out["GNames"] = gn + hit + 7 + disp
            print(f"  GNames           {out['GNames']:x}   pre-4.23 TNameEntryArray via "
                  f"FName::GetNames @{gn:x}, load at +{hit} (no -0x10)")
        else:
            print(f"  GNames           UNRESOLVED — FName::GetNames @{gn:x} has no "
                  f"`48 8B 05` in its first 12 bytes: {code[:12].hex(' ')}")
        n, r = None, None
    else:
        n, r = find(syms, "?GetBlocks@FNameDebugVisualizer@@")
    if n is None and r is None and "GNames" in out:
        pass
    elif r is None:
        print("  GNames           NOT FOUND (neither FName::GetNames nor "
              "FNameDebugVisualizer::GetBlocks)")
    else:
        gb = base + r
        code = read_at_va(exe, gb, 8)
        if len(code) == 8 and code[:3] == b"\x48\x8d\x05" and code[7] == 0xC3:
            disp = struct.unpack_from("<i", code, 3)[0]
            blocks = gb + 7 + disp
            out["GNames"] = blocks - 0x10
            print(f"  GNames           {blocks - 0x10:x}   "
                  f"GetBlocks @{gb:x} = `{code.hex(' ')}` -> Blocks {blocks:x}, minus 0x10")
        else:
            print(f"  GNames           UNRESOLVED — GetBlocks @{gb:x} is "
                  f"`{code.hex(' ')}`, not `48 8d 05 <disp32> c3`")

    print("\n# sweep.sh GS_TRUE:")
    parts = []
    for k in ("GObjects", "GNames", "GWorld", "SparseDelegates", "GEngine"):
        if k not in out:
            continue
        if k == "GObjects" and alias:
            parts.append(f"GObjects={gobj:x}|{gobj + 0x10:x}")
        else:
            parts.append(f"{k}={out[k]:x}")
    print('GS_TRUE="' + ",".join(parts) + '"')


# Guarded so this file is IMPORTABLE, not just runnable: `Pdb`, `image_base` and `read_at_va`
# are useful on their own (e.g. resolving an arbitrary symbol and reading its bytes out of the
# EXE). Without the guard, `import pdb_globals` executed main() and died on argv.
if __name__ == "__main__":
    main()
