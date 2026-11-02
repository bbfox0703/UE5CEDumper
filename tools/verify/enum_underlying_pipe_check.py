"""Live pipe check for [USMAP-ENUM-SIGNEDNESS] (build 3674 on): walk_class's `enum_underlying`.

    py tools/verify/enum_underlying_pipe_check.py

Headless, through the pipe (the UI must NOT be connected). Walks every UClass and UScriptStruct in GObjects through
walk_class_batch and, for every EnumProperty field, checks that:

- it carries `enum_underlying` whenever it carries `enum_name` (the read is gated on the Enum slot validating);
- the underlying type is an integer property whose width equals the field's `size`.

and that no other field type carries the key. It prints how many fields each underlying type covers, and one example
of each, so a signed enum (Int8 / Int16 / Int / Int64) is visible when the engine has one.
"""
import pathlib
import sys
from collections import Counter

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient  # noqa: E402

WIDTH = {
    "ByteProperty": 1, "Int8Property": 1,
    "Int16Property": 2, "UInt16Property": 2,
    "IntProperty": 4, "UInt32Property": 4,
    "Int64Property": 8, "UInt64Property": 8,
}
STRUCT_CLASSES = {"Class", "ScriptStruct", "BlueprintGeneratedClass", "WidgetBlueprintGeneratedClass",
                  "AnimBlueprintGeneratedClass"}


def data(reply):
    return reply.get("data", reply)


def main() -> int:
    bad = 0
    with PipeClient(timeout=300) as pc:
        pc.connect()
        print("build:", pc.assert_build())
        pc.ensure_scanned()

        addrs, offset = [], 0
        while True:
            d = data(pc.request("get_object_list", offset=offset, limit=2000))
            for o in d.get("objects") or []:
                if o.get("class") in STRUCT_CLASSES:
                    addrs.append(o["addr"])
            scanned = d.get("scanned") or 0
            if scanned <= 0:
                break
            offset += scanned
            if offset >= (d.get("total") or 0):
                break
        print(f"   {len(addrs)} classes / structs to walk")

        types, examples = Counter(), {}
        enum_fields = missing = mismatch = stray = 0
        for i in range(0, len(addrs), 100):
            d = data(pc.request("walk_class_batch", addrs=addrs[i:i + 100]))
            for ci in d.get("classes") or []:
                for f in ci.get("fields") or []:
                    u = f.get("enum_underlying")
                    if f.get("type") != "EnumProperty":
                        if u is not None:
                            stray += 1
                            print(f"BAD: {ci.get('name')}.{f.get('name')} ({f.get('type')}) carries enum_underlying {u!r}")
                        continue
                    enum_fields += 1
                    if u is None:
                        if f.get("enum_name"):
                            missing += 1
                            if missing <= 5:
                                print(f"BAD: {ci.get('name')}.{f.get('name')} has enum {f.get('enum_name')!r} "
                                      f"but no enum_underlying")
                        continue
                    if WIDTH.get(u) != f.get("size"):
                        mismatch += 1
                        if mismatch <= 5:
                            print(f"BAD: {ci.get('name')}.{f.get('name')} enum_underlying {u!r} vs size {f.get('size')}")
                        continue
                    types[u] += 1
                    examples.setdefault(u, f"{ci.get('name')}.{f.get('name')} ({f.get('enum_name')})")

        print(f"   {enum_fields} EnumProperty fields (inherited ones counted once per class that carries them)")
        for t, n in types.most_common():
            print(f"   {t:16} {n:6}   e.g. {examples[t]}")
        print(f"   missing={missing} mismatch={mismatch} stray={stray}")
        bad = missing + mismatch + stray
        if enum_fields == 0 or not types:
            print("BAD: no EnumProperty field carried enum_underlying at all")
            bad += 1

    print("RESULT:", "PASS" if bad == 0 else f"FAIL ({bad})")
    return 0 if bad == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
