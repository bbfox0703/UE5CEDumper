r"""Controlled single-variable rewrites of a USMAP, so a consumer can be A/B'd against itself.

    py tools/verify/usmap_rewrite.py identity  <in.usmap> <out.usmap>
    py tools/verify/usmap_rewrite.py degrade   <in.usmap> <out.usmap>
    py tools/verify/usmap_rewrite.py flipwidth <in.usmap> <out.usmap>
    py tools/verify/usmap_rewrite.py bytewidth <in.usmap> <out.usmap>
    py tools/verify/usmap_rewrite.py promote   <in.usmap> <out.usmap> --truth <truth.usmap>

⛔ NOT A GATE, and no path has a default -- see `usmap_reader.py`'s header for why.

WHY THIS EXISTS. Diffing OUR export against another dumper's cannot isolate one fix: the two
also disagree on schema indices (`[USMAP-INHERITED-DUPES]`), on struct membership, and on
compression. Rewriting ONE file and parsing the same assets under both copies removes every
other variable -- same name table, same struct set, same indices, same bloat -- so any
difference a consumer then shows is attributable to the single byte-level change made here.

⭐ `identity` IS THE GUARD, NOT A NO-OP. It runs the whole walk and re-emits, so its output
must be byte-identical to the input's decompressed payload. If it is not, the rewriter is
itself changing the file and every A/B below is worthless. Check it:

    py tools/verify/usmap_rewrite.py identity <in.usmap> <out.usmap>
    py tools/verify/usmap_reader.py <in.usmap> <out.usmap>     # censuses must match

THE MODES, and what each one is for

  degrade    every CONTAINER INNER that is `Enum[under=W][E]` becomes a bare ByteProperty --
             exactly the pre-`[A4-USMAP-CONTAINER-ENUM]` shape. This is load-bearing, not
             cosmetic: `PropertyArray.cpp` `CanBulkSerialize()` returns FALSE for an
             FByteProperty that has an Enum, so each element goes through `SerializeItem` and
             `PropertyByte.cpp` writes an FName -- while a genuine `TArray<uint8>` IS
             bulk-serialized as raw bytes. A consumer told "bare ByteProperty inner" therefore
             reads ONE BYTE where the package holds an FName, and everything after it in that
             object shifts.
  flipwidth  every container inner enum's declared UNDERLYING width becomes Int64Property;
             the type stays EnumProperty and the enum NAME is untouched. This is the CONTROL
             for `degrade`: `FEnumProperty::SerializeItem` writes an FName whenever
             `Enum != nullptr`, so the declared width should change nothing. Measured
             2026-09-16 on a commercial title: flipping all 535 inner widths changed 0 of
             3,003 exports, over the very same packages where `degrade` changed 3.
  bytewidth  every TOP-LEVEL `EnumProperty`'s declared underlying width becomes Byte, and
             nothing else moves -- not the type, not the enum name, not one container
             inner. That is exactly the pre-`[A4-USMAP-ENUM-UNDERLYING]` shape: the old
             writer emitted Byte for EVERY enum slot, a single bucket over all 3,704 of
             them, which no correct writer can produce. ⭐ It is the only mode aimed at
             L30; `degrade` and `flipwidth` both touch CONTAINER INNERS, which is L39's
             variable, and using one for the other's row measures the wrong thing.
             ⚠ A 1-byte enum is ALREADY `under=Byte`, so this mode is a no-op on it --
             which is the same fact that makes a `TEnumAsByte` witness useless for L30.
             If `descriptors changed` comes back 0, the mapping has no wide enum and the
             A/B below would be vacuous.
  promote    the inverse of `degrade`, driven by a second mapping: a bare ByteProperty inner
             is promoted to `[26][0][E]` when the truth mapping names that same
             `Struct.Prop` as an enum inner. Use it to ask whether a PRE-fix mapping actually
             misread a title's content. Only enum names already in the target's name table are
             used, so the table is never rewritten.

Input compression 0 or 3 (ZStandard) is accepted; output is always written uncompressed.
"""
import io
import os
import struct
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.stderr.reconfigure(encoding="utf-8", errors="replace")

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from usmap_reader import Reader, decode_header, load, T_ENUM, T_STRUCT, T_MAP, ONE_INNER  # noqa: E402

BYTE = 0
INT64 = 21


def truth_table(path):
    """'Struct.Prop' -> enum name, for every container whose inner names an enum."""
    u = load(path)
    out = {}
    for s in u.structs:
        for _idx, _dim, pname, ty in s.records:
            for slot, t in ty.walk():
                if slot is not None and t.is_enum and t.enum_name not in (None, "None"):
                    out["%s.%s" % (s.name, pname)] = t.enum_name
    return out


def rewrite(src, dst, mode, truth=None):
    data = io.open(src, "rb").read()
    magic, version, hasver, _comp, body = decode_header(data)

    p = Reader(body)
    out = []
    stats = {"inner_slots": 0, "changed": 0, "skipped_no_name": 0, "toplevel_enums": 0}

    n_names = p.u32()
    out.append(p.b[0:p.i])
    names = []
    for _ in range(n_names):
        s = p.i
        names.append(p.raw(p.u16()).decode("utf-8", "replace"))
        out.append(p.b[s:p.i])
    name_index = {n: i for i, n in enumerate(names)}

    s = p.i
    for _ in range(p.u32()):
        p.i32()
        for _ in range(p.u16()):
            p.i64(); p.i32()
    out.append(p.b[s:p.i])

    s = p.i
    n_structs = p.u32()
    out.append(p.b[s:p.i])

    def copy_type(want_enum, inner=False):
        start = p.i
        t = p.u8()
        if t == T_ENUM:
            under = p.u8()
            idx = p.i32()
            if not inner:
                stats["toplevel_enums"] += 1
                if mode == "bytewidth" and under != BYTE:
                    out.append(bytes([T_ENUM, BYTE]) + struct.pack("<i", idx))
                    stats["changed"] += 1
                    return
                out.append(p.b[start:p.i])
                return
            if mode == "degrade":
                out.append(bytes([BYTE]))
                stats["changed"] += 1
                return
            if mode == "flipwidth":
                out.append(bytes([T_ENUM, INT64]) + struct.pack("<i", idx))
                stats["changed"] += 1
                return
            out.append(p.b[start:p.i])
            return
        if t == T_STRUCT:
            p.i32()
            out.append(p.b[start:p.i])
            return
        if t in ONE_INNER:
            out.append(bytes([t]))
            stats["inner_slots"] += 1
            copy_type(want_enum, inner=True)
            return
        if t == T_MAP:
            out.append(bytes([t]))
            stats["inner_slots"] += 2
            copy_type(want_enum, inner=True)
            copy_type(want_enum, inner=True)
            return
        if t == BYTE and inner and mode == "promote" and want_enum:
            idx = name_index.get(want_enum)
            if idx is None:
                stats["skipped_no_name"] += 1
                out.append(bytes([BYTE]))
                return
            out.append(bytes([T_ENUM, BYTE]) + struct.pack("<i", idx))
            stats["changed"] += 1
            return
        out.append(bytes([t]))

    for _ in range(n_structs):
        s = p.i
        sname = names[p.i32()]
        p.i32(); p.u16()
        nrec = p.u16()
        out.append(p.b[s:p.i])
        for _ in range(nrec):
            s = p.i
            p.u16(); p.u8(); pname = names[p.i32()]
            out.append(p.b[s:p.i])
            copy_type(truth.get("%s.%s" % (sname, pname)) if truth else None)

    # A short read means the descriptor walk desynced; emitting anyway would produce a file
    # that looks plausible and parses to garbage.
    if p.i != len(body):
        raise SystemExit("payload not consumed: %d of %d bytes" % (p.i, len(body)))

    payload = b"".join(out)
    w = io.open(dst, "wb")
    w.write(struct.pack("<HB", magic, version))
    w.write(struct.pack("<i", hasver))
    w.write(struct.pack("<B", 0))                      # always uncompressed
    w.write(struct.pack("<II", len(payload), len(payload)))
    w.write(payload)
    w.close()

    print("%s  ->  %s   mode=%s" % (os.path.basename(src), os.path.basename(dst), mode))
    print("   container inner slots seen : %d" % stats["inner_slots"])
    print("   descriptors changed        : %d" % stats["changed"])
    if mode == "promote":
        print("   skipped, name not in table : %d" % stats["skipped_no_name"])
    print("   top-level enums %s: %d"
          % ("seen           " if mode == "bytewidth" else "untouched  ",
             stats["toplevel_enums"]))
    print("   payload %d -> %d bytes%s"
          % (len(body), len(payload),
             "   (identity: these MUST be equal)" if mode == "identity" else ""))
    if mode == "identity" and payload != body:
        raise SystemExit("⛔ identity re-emit is NOT byte-identical -- the rewriter is unsound")


def main():
    if len(sys.argv) < 4 or sys.argv[1] not in ("identity", "degrade", "flipwidth", "bytewidth", "promote"):
        raise SystemExit(__doc__)
    mode, src, dst = sys.argv[1], sys.argv[2], sys.argv[3]
    truth = None
    if mode == "promote":
        if "--truth" not in sys.argv:
            raise SystemExit("promote needs --truth <truth.usmap>")
        truth = truth_table(sys.argv[sys.argv.index("--truth") + 1])
        print("truth mapping names %d container-enum properties" % len(truth))
    rewrite(src, dst, mode, truth)


if __name__ == "__main__":
    main()
