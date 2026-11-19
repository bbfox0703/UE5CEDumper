#!/usr/bin/env python3
"""Minimal standalone PE parser: dump imports (by-name + by-ordinal) and exports.

No third-party deps. A tool for diagnosing proxy-DLL export/import mismatches.
Usage:
    pe_imports_exports.py imports <file.exe> [--dll dxgi]
    pe_imports_exports.py exports <file.dll>
"""
import struct
import sys


def rva_to_off(sections, rva):
    for va, vsz, raw, rsz in sections:
        if va <= rva < va + max(vsz, rsz):
            return raw + (rva - va)
    return None


def read_cstr(data, off):
    end = data.find(b"\x00", off)
    return data[off:end].decode("latin1", "replace")


def parse(path):
    with open(path, "rb") as f:
        data = f.read()
    if data[:2] != b"MZ":
        raise SystemExit(f"{path}: not MZ")
    pe_off = struct.unpack_from("<I", data, 0x3C)[0]
    if data[pe_off:pe_off + 4] != b"PE\x00\x00":
        raise SystemExit(f"{path}: no PE sig")
    coff = pe_off + 4
    machine, nsec = struct.unpack_from("<HH", data, coff)
    opt_off = coff + 20
    magic = struct.unpack_from("<H", data, opt_off)[0]
    pe32plus = (magic == 0x20B)
    # data directories start
    if pe32plus:
        nrva_off = opt_off + 108
        dd_off = opt_off + 112
    else:
        nrva_off = opt_off + 92
        dd_off = opt_off + 96
    nrva = struct.unpack_from("<I", data, nrva_off)[0]
    dirs = []
    for i in range(nrva):
        va, sz = struct.unpack_from("<II", data, dd_off + i * 8)
        dirs.append((va, sz))
    sec_off = opt_off + struct.unpack_from("<H", data, coff + 16)[0]
    sections = []
    for i in range(nsec):
        base = sec_off + i * 40
        vsz, va, rsz, raw = struct.unpack_from("<IIII", data, base + 8)
        sections.append((va, vsz, raw, rsz))
    return data, sections, dirs, pe32plus, machine


def dump_imports(path, only_dll=None):
    data, sections, dirs, pe32plus, machine = parse(path)
    print(f"== IMPORTS {path}  (machine=0x{machine:04X}, {'PE32+' if pe32plus else 'PE32'}) ==")
    imp_va, imp_sz = dirs[1]
    if imp_va == 0:
        print("  (no import directory)")
        return
    off = rva_to_off(sections, imp_va)
    idx = 0
    while True:
        base = off + idx * 20
        oft, tstamp, fwd, name_rva, ft = struct.unpack_from("<IIIII", data, base)
        if name_rva == 0 and oft == 0 and ft == 0:
            break
        dll = read_cstr(data, rva_to_off(sections, name_rva)).lower()
        idx += 1
        if only_dll and only_dll.lower() not in dll:
            continue
        print(f"  [{dll}]")
        thunk_rva = oft if oft else ft
        toff = rva_to_off(sections, thunk_rva)
        ord_flag = 0x8000000000000000 if pe32plus else 0x80000000
        entsz = 8 if pe32plus else 4
        k = 0
        while True:
            ent = struct.unpack_from("<Q" if pe32plus else "<I", data, toff + k * entsz)[0]
            if ent == 0:
                break
            if ent & ord_flag:
                print(f"      ORDINAL  @{ent & 0xFFFF}")
            else:
                hint_off = rva_to_off(sections, ent & 0x7FFFFFFF)
                hint = struct.unpack_from("<H", data, hint_off)[0]
                nm = read_cstr(data, hint_off + 2)
                print(f"      {nm}  (hint {hint})")
            k += 1


def dump_exports(path):
    data, sections, dirs, pe32plus, machine = parse(path)
    print(f"== EXPORTS {path} ==")
    exp_va, exp_sz = dirs[0]
    if exp_va == 0:
        print("  (no export directory)")
        return
    off = rva_to_off(sections, exp_va)
    (flags, stamp, vmaj, vmin, name_rva, ord_base, naddr, nnames,
     addr_rva, names_rva, ords_rva) = struct.unpack_from("<IIHHIIIIIII", data, off)
    dll = read_cstr(data, rva_to_off(sections, name_rva))
    print(f"  name={dll} ordinal_base={ord_base} n_funcs={naddr} n_names={nnames}")
    addr_off = rva_to_off(sections, addr_rva)
    names_off = rva_to_off(sections, names_rva)
    ords_off = rva_to_off(sections, ords_rva)
    # map index -> name
    idx_to_name = {}
    for i in range(nnames):
        nrva = struct.unpack_from("<I", data, names_off + i * 4)[0]
        nm = read_cstr(data, rva_to_off(sections, nrva))
        oi = struct.unpack_from("<H", data, ords_off + i * 2)[0]
        idx_to_name[oi] = nm
    rows = []
    for i in range(naddr):
        fva = struct.unpack_from("<I", data, addr_off + i * 4)[0]
        if fva == 0:
            continue
        ordinal = ord_base + i
        nm = idx_to_name.get(i, "<noname>")
        # forwarder?
        fwd = ""
        if exp_va <= fva < exp_va + exp_sz:
            fwd = " -> " + read_cstr(data, rva_to_off(sections, fva))
        rows.append((ordinal, nm, fva, fwd))
    for ordinal, nm, fva, fwd in sorted(rows):
        print(f"  @{ordinal:<4} {nm:<40} RVA=0x{fva:06X}{fwd}")


if __name__ == "__main__":
    if len(sys.argv) < 3:
        print(__doc__)
        sys.exit(1)
    mode = sys.argv[1]
    path = sys.argv[2]
    if mode == "imports":
        dll = None
        if "--dll" in sys.argv:
            dll = sys.argv[sys.argv.index("--dll") + 1]
        dump_imports(path, dll)
    elif mode == "exports":
        dump_exports(path)
    else:
        print("mode must be imports|exports")
