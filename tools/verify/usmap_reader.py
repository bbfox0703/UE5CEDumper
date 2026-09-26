r"""USMAP v4 reader -- the shared parser the other usmap rigs import, plus a census CLI.

    py tools/verify/usmap_reader.py <file.usmap> [<file.usmap> ...]

⛔ NOT A GATE, AND IT MUST NEVER BECOME ONE. Every input is passed on the command line and
nothing here has a default path, because the only files worth pointing it at live OUTSIDE the
repo: exports under the gitignored `out/`, a reference `.usmap` from another dumper, and a
game's cooked content. `tools/check_all.py` must stay able to run with none of that present --
a gate reaching a game install would fail CI today and rot the moment that game is patched or
uninstalled.

THE FORMAT, taken from this repo's own writer (`ui/UE5DumpUI/Services/UsmapExportService.cs`):

    header : uint16 magic 0x30C4 | uint8 version | int32 bHasVersionInfo |
             uint8 compression | uint32 compressedSize | uint32 decompressedSize
    payload: name table (uint32 count, then uint16 len + UTF-8 bytes)
             enums   (uint32 count; per enum: int32 nameIdx, uint16 count,
                      then int64 value + int32 nameIdx per member)
             structs (uint32 count; per struct: int32 nameIdx, int32 superIdx,
                      uint16 schemaSlots, uint16 recordCount, then per record:
                      uint16 schemaIdx, uint8 arrayDim, int32 nameIdx, type-descriptor)

Compression 0 (what our writer emits) and 3 (ZStandard, what Dumper-7's MappingGenerator
emits by default per its `Settings.h`) are both handled; 3 needs `pip install zstandard`.

⭐ THE CENSUS CLI IS AN ANTI-SELF-COMPARISON GUARD, not decoration. Two usmaps of the same
fixture differ only by a filename suffix and sit in the same directory, so passing the same
path twice to a diff yields a flawless zero-difference run. Printing
`magic/ver/comp/names/enums/structs/records` for every input makes that mistake visible:
identical census lines mean you compared one file with itself.

⚠ RECORDS ARE NOT DISTINCT KEYS, and the difference has already produced a wrong number in
`docs/todo.md` (86,011 vs the real 86,068). A reader that stores properties in a dict keyed
by (struct, property) silently collapses same-name records inside a struct AND whole structs
whose NAME collides -- `AnimBlueprintGeneratedConstantData` is declared twice in both our
export and Dumper-7's. `Usmap.record_count` counts RECORDS; `Usmap.by_key()` returns the
deduped view. Label whichever you quote.

⚠ THE TYPE WALK RECURSES. An enum inner can sit at any depth (`TArray<TArray<...>>` exists in
Dumper-7's output), and a reader that only looks one level down silently under-reports.
"""
import collections
import io
import struct
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.stderr.reconfigure(encoding="utf-8", errors="replace")

PT = {
    0: "ByteProperty", 1: "BoolProperty", 2: "IntProperty", 3: "FloatProperty",
    4: "ObjectProperty", 5: "NameProperty", 6: "DelegateProperty", 7: "DoubleProperty",
    8: "ArrayProperty", 9: "StructProperty", 10: "StrProperty", 11: "TextProperty",
    12: "InterfaceProperty", 13: "MulticastDelegateProperty", 14: "WeakObjectProperty",
    15: "LazyObjectProperty", 16: "AssetObjectProperty", 17: "SoftObjectProperty",
    18: "UInt64Property", 19: "UInt32Property", 20: "UInt16Property", 21: "Int64Property",
    22: "Int16Property", 23: "Int8Property", 24: "MapProperty", 25: "SetProperty",
    26: "EnumProperty", 27: "FieldPathProperty", 28: "OptionalProperty",
    29: "Utf8StrProperty", 30: "AnsiStrProperty",
}

SHORT = {
    0: "Byte", 1: "Bool", 2: "Int", 3: "Float", 4: "Object", 5: "Name", 6: "Delegate",
    7: "Double", 8: "Array", 9: "Struct", 10: "Str", 11: "Text", 12: "Interface",
    13: "MCDelegate", 14: "WeakObj", 15: "LazyObj", 16: "AssetObj", 17: "SoftObj",
    18: "UInt64", 19: "UInt32", 20: "UInt16", 21: "Int64", 22: "Int16", 23: "Int8",
    24: "Map", 25: "Set", 26: "Enum", 27: "FieldPath", 28: "Optional",
    29: "Utf8Str", 30: "AnsiStr",
}

T_ENUM = 26
T_STRUCT = 9
T_MAP = 24
# Array / Set / Optional all carry exactly ONE inner descriptor.
ONE_INNER = (8, 25, 28)
# The slot a descriptor occupies inside its parent -- the four container arms are
# separate wire keys (inner_enum / elem_enum / key_enum / value_enum, Fern.cpp `Fern::DispatchCommand`),
# so a survey that lumps them together cannot see which arm is unexercised.
SLOT_OF = {8: "Inner", 25: "Elem", 28: "OptInner"}


class Reader:
    def __init__(self, b):
        self.b = b
        self.i = 0

    def u8(self):
        v = self.b[self.i]; self.i += 1; return v

    def u16(self):
        v = struct.unpack_from("<H", self.b, self.i)[0]; self.i += 2; return v

    def u32(self):
        v = struct.unpack_from("<I", self.b, self.i)[0]; self.i += 4; return v

    def i32(self):
        v = struct.unpack_from("<i", self.b, self.i)[0]; self.i += 4; return v

    def i64(self):
        v = struct.unpack_from("<q", self.b, self.i)[0]; self.i += 8; return v

    def raw(self, n):
        v = self.b[self.i:self.i + n]; self.i += n; return v


class Type:
    """One type descriptor. `inner` is a list of (slot, Type) for containers."""

    __slots__ = ("kind", "enum_name", "under", "struct_name", "inner")

    def __init__(self, kind, enum_name=None, under=None, struct_name=None, inner=None):
        self.kind = kind
        self.enum_name = enum_name
        self.under = under          # kind code of an Enum's underlying property
        self.struct_name = struct_name
        self.inner = inner or []

    @property
    def is_enum(self):
        return self.kind == T_ENUM

    def label(self):
        if self.kind == T_ENUM:
            return "Enum[under=%s][%s]" % (SHORT.get(self.under, "t%d" % self.under),
                                           self.enum_name)
        if self.kind == T_STRUCT:
            return "Struct[%s]" % self.struct_name
        if self.inner:
            return "%s<%s>" % (SHORT.get(self.kind, "t%d" % self.kind),
                               ",".join(t.label() for _, t in self.inner))
        return SHORT.get(self.kind, "t%d" % self.kind)

    def walk(self, slot=None):
        """Yield (slot, Type) for this node and every descendant. slot is None at top."""
        yield slot, self
        for s, t in self.inner:
            for pair in t.walk(s):
                yield pair


def parse_type(r, names):
    t = r.u8()
    if t == T_ENUM:
        under = r.u8()
        return Type(t, enum_name=names[r.i32()], under=under)
    if t == T_STRUCT:
        return Type(t, struct_name=names[r.i32()])
    if t in ONE_INNER:
        return Type(t, inner=[(SLOT_OF[t], parse_type(r, names))])
    if t == T_MAP:
        k = parse_type(r, names)
        v = parse_type(r, names)
        return Type(t, inner=[("MapKey", k), ("MapValue", v)])
    return Type(t)


class Struct:
    __slots__ = ("name", "super", "slots", "records")

    def __init__(self, name, super_name, slots, records):
        self.name = name
        self.super = super_name
        self.slots = slots
        self.records = records      # list of (schemaIdx, arrayDim, propName, Type)


class Usmap:
    def __init__(self, path, magic, version, has_version_info, compression,
                 names, enums, structs):
        self.path = path
        self.magic = magic
        self.version = version
        self.has_version_info = has_version_info
        self.compression = compression
        self.names = names
        self.enums = enums
        self.structs = structs      # list, in file order; names CAN repeat

    @property
    def record_count(self):
        return sum(len(s.records) for s in self.structs)

    def by_name(self):
        """name -> Struct. ⚠ collapses duplicate struct names; use for lookup only."""
        return {s.name: s for s in self.structs}

    def by_key(self):
        """'Struct.Prop' -> (Type, schemaIdx). ⚠ a DEDUPED view, not a record count.

        Iterates the struct LIST, so both copies of a duplicated struct name contribute.
        A reader that first builds {name: Struct} instead keeps only one copy, and on these
        artifacts that silently moves the shared-key count by 52: both files declare
        `AnimBlueprintGeneratedConstantData` twice, and in the reference export the second
        copy carries 0 records while ours carries 52 in both. Use `collapsed_keys()` to see
        how many records a quoted key count hides.
        """
        out = {}
        for s in self.structs:
            for idx, _dim, pname, ty in s.records:
                out["%s.%s" % (s.name, pname)] = (ty, idx)
        return out

    def collapsed_keys(self):
        """How many records `by_key` hides, and which struct names are declared twice."""
        seen = set()
        collapsed = 0
        names = collections.Counter(s.name for s in self.structs)
        for s in self.structs:
            for _idx, _dim, pname, _ty in s.records:
                k = "%s.%s" % (s.name, pname)
                if k in seen:
                    collapsed += 1
                seen.add(k)
        return collapsed, sorted(n for n, c in names.items() if c > 1)

    def census(self):
        return ("magic=0x%04X ver=%d comp=%d  names=%d enums=%d structs=%d records=%d"
                % (self.magic, self.version, self.compression, len(self.names),
                   len(self.enums), len(self.structs), self.record_count))


def decode_header(data):
    """-> (magic, version, hasVersionInfo, compression, decompressed payload)."""
    h = Reader(data)
    magic = h.u16()
    version = h.u8()
    has_version_info = h.i32()
    comp = h.u8()
    csize = h.u32()
    dsize = h.u32()
    body = h.raw(csize)
    if comp == 3:
        import zstandard
        body = zstandard.ZstdDecompressor().decompress(body, max_output_size=dsize * 2)
    elif comp != 0:
        raise SystemExit("unhandled compression method %d" % comp)
    return magic, version, has_version_info, comp, body


def load(path):
    data = io.open(path, "rb").read()
    magic, version, hasver, comp, body = decode_header(data)
    p = Reader(body)

    names = [p.raw(p.u16()).decode("utf-8", "replace") for _ in range(p.u32())]

    enums = []
    for _ in range(p.u32()):
        ename = names[p.i32()]
        members = []
        for _ in range(p.u16()):
            value = p.i64()
            members.append((value, names[p.i32()]))
        enums.append((ename, members))

    structs = []
    for _ in range(p.u32()):
        sname = names[p.i32()]
        sup = p.i32()
        slots = p.u16()
        nrec = p.u16()
        records = []
        for _ in range(nrec):
            idx = p.u16()
            dim = p.u8()
            pname = names[p.i32()]
            records.append((idx, dim, pname, parse_type(p, names)))
        structs.append(Struct(sname, names[sup] if sup >= 0 else None, slots, records))

    # A trailing byte means the descriptor walk desynced, which is exactly the failure a
    # container tag with no payload would cause. Never report numbers from a short read.
    if p.i != len(body):
        raise SystemExit("payload not consumed: %d of %d bytes in %s"
                         % (p.i, len(body), path))
    return Usmap(path, magic, version, hasver, comp, names, enums, structs)


def _short(path):
    return path.replace("/", "\\").rsplit("\\", 1)[-1]


def main():
    if len(sys.argv) < 2:
        raise SystemExit(__doc__)
    seen = {}
    for path in sys.argv[1:]:
        u = load(path)
        line = u.census()
        print("%-46s %s" % (_short(path)[:46], line))
        seen.setdefault(line, []).append(path)
    for line, paths in seen.items():
        if len(paths) > 1:
            print("\n⚠ IDENTICAL CENSUS for %d inputs -- are these the same file?" % len(paths))
            for q in paths:
                print("     %s" % q)


if __name__ == "__main__":
    main()
