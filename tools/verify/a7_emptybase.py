r"""Audit A7 — the empty-base struct fix, on the only fixture that can produce the shape.

    py tools/verify/a7_emptybase.py

THE QUESTION. UE reports an **empty** native `USTRUCT`'s `PropertiesSize` as **1** (a zero-size
struct is not addressable in C++). The SDK emitter splits own from inherited fields on
`Offset >= superPropsSize`, so with `superPropsSize == 1` a derived struct's **offset-0** field fell
BELOW the floor, was dropped, and the trailing-pad pass wrote padding in its place. `57251ef7`
lowers the floor via a new wire field, `own_props_start`, and emits the empty base empty so EBO
applies.

⛔ WHY THIS NEEDED A FIXTURE, against the row's own "rides along on any live session". Measured
2026-09-05: **zero** structs with `PropertiesSize == 1` in EVERSPACE 2's 3,808 loaded classes, and
none among DumperTest's reachable structs before this fixture existed. Two structural reasons, both
worth knowing before someone repeats the hunt:

  * `list_classes` returns **UClass** objects only — a `UScriptStruct` is not enumerable through it
    at all. Struct addresses have to be harvested from `walk_instance` **field rows**
    (`struct_class_addr`), which is what this rig does.
  * a whole-pool walk only ever sees what is **LOADED**, and the row's named vehicle
    (`FEmptyPayload`, an editor-adjacent AnimData type) is not loaded by a Shipping build.

So the fixture is `FDumperTestEmptyBase` + `FDumperTestBracketPayload`, reachable through
`ADumperTestActor::EmptyBasePayload`. See tools/ue-sample/README.md.

⚠ NAMES CARRY NO `F` PREFIX on the wire: a `UScriptStruct`'s FName is `DumperTestBracketPayload`.
Grepping for `FDumperTestBracketPayload` has zero hits, which reads as "the fixture is missing".
"""
import pathlib
import sys

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from pipe_client import PipeClient  # noqa: E402

ACTOR = "DumperTestActor"
FIELD = "EmptyBasePayload"
CHILD = "DumperTestBracketPayload"
BASE = "DumperTestEmptyBase"
VALUE = "A7EmptyBase"


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + "\n")
    sys.stdout.flush()


def main():
    fails, notes = [], []
    with PipeClient() as c:
        say("build answering the pipe: %s" % c.assert_build())
        p = c.ensure_scanned()
        say("UE %s   objects %s   module %s" % (p.get("ue_version"), p.get("object_count"),
                                                p.get("module_name")))
        if (p.get("object_count") or 0) < 1000:
            raise SystemExit("a7: the engine did not boot; nothing below would mean anything")

        # ---- reach the struct through a live instance's field row -----------------
        fr = c.request("find_instances", class_name=ACTOR, limit=20, exact_match=True, timeout=180)
        insts = fr.get("instances") or []
        say("\n%s instances: %d" % (ACTOR, len(insts)))
        if not insts:
            raise SystemExit("a7: no %s instance -- wrong package, or the actor never spawned" % ACTOR)

        row = None
        for inst in insts:
            w = c.request("walk_instance", addr=inst["addr"].replace("0x", ""), timeout=180)
            for f in (w.get("fields") or []):
                if f.get("name") == FIELD:
                    row = f
                    break
            if row:
                say("found %s on %s @ %s" % (FIELD, inst.get("name"), inst.get("addr")))
                break
        if not row:
            raise SystemExit("a7: no %s field on any %s -- the package predates the fixture. "
                             "Re-cook Development and re-run capture_package_identity --check."
                             % (FIELD, ACTOR))

        say("   type=%s  struct_type=%s  offset=%s  size=%s  value=%r"
            % (row.get("type"), row.get("struct_type"), row.get("offset"), row.get("size"),
               row.get("value")))
        if row.get("struct_type") != CHILD:
            fails.append("A7: field struct_type is %r, expected %r" % (row.get("struct_type"), CHILD))

        # ---- the DERIVED struct ---------------------------------------------------
        addr = str(row.get("struct_class_addr") or "").replace("0x", "")
        if not addr:
            raise SystemExit("a7: the field row carries no struct_class_addr; cannot reach the struct")
        k = c.request("walk_class", addr=addr, timeout=120).get("class") or {}
        fields = k.get("fields") or []
        say("\n--- derived: %s ---" % k.get("name"))
        say("   super_name       = %r" % k.get("super_name"))
        say("   super_props_size = %r      <-- MUST be 1 (UE reports an empty USTRUCT as 1)"
            % k.get("super_props_size"))
        say("   own_props_start  = %r      <-- MUST be 0 (the field the old floor DROPPED)"
            % k.get("own_props_start"))
        say("   props_size       = %r" % k.get("props_size"))
        for f in fields[:4]:
            say("   field: %-16s offset=%-4s size=%-4s type=%s"
                % (f.get("name"), f.get("offset"), f.get("size"), f.get("type")))
        if k.get("super_name") != BASE:
            fails.append("A7: derived super is %r, expected %r" % (k.get("super_name"), BASE))
        if k.get("super_props_size") != 1:
            fails.append("A7: super_props_size is %r, expected 1 -- without it the bug cannot occur "
                         "and this fixture proves nothing" % k.get("super_props_size"))
        if k.get("own_props_start") != 0:
            fails.append("A7: own_props_start is %r, expected 0 -- this IS the fix"
                         % k.get("own_props_start"))
        if not fields or fields[0].get("offset") != 0:
            fails.append("A7: first field is %r at offset %r, expected one at 0"
                         % (fields[0].get("name") if fields else None,
                            fields[0].get("offset") if fields else None))

        # ---- the EMPTY BASE itself ------------------------------------------------
        saddr = str(k.get("super_addr") or "").replace("0x", "")
        if not saddr:
            notes.append("derived struct carries no super_addr; base not checked directly")
        else:
            b = c.request("walk_class", addr=saddr, timeout=120).get("class") or {}
            bf = b.get("fields") or []
            say("\n--- base: %s ---" % b.get("name"))
            say("   props_size       = %r      <-- MUST be 1" % b.get("props_size"))
            say("   own_props_start  = %r     <-- MUST be -1 (no own properties)"
                % b.get("own_props_start"))
            say("   fields           = %d      <-- MUST be 0" % len(bf))
            if b.get("props_size") != 1:
                fails.append("A7: base props_size is %r, expected 1" % b.get("props_size"))
            if b.get("own_props_start") != -1:
                fails.append("A7: base own_props_start is %r, expected -1" % b.get("own_props_start"))
            if bf:
                fails.append("A7: the base reports %d field(s); it must be empty" % len(bf))

        # ---- the live value, i.e. the field really is readable at offset 0 --------
        # ⚠ NOT `row["value"]`. `EmptyBasePayload` is a StructProperty and a struct row carries no
        # scalar value -- `Fern.cpp:1442` only sends `value` when `typedValue` is non-empty, which
        # for a struct it never is. The string lives INSIDE the struct, so walk the struct instance
        # itself: walk_instance takes `addr` = struct_data_addr with `class_addr` = struct_class_addr
        # exactly for this. (This rig checked the struct row first and scored a correct fix as a
        # FAIL -- the same shape of mistake as expecting the lazy envelope to equal the soft one.)
        daddr = str(row.get("struct_data_addr") or "").replace("0x", "")
        v = ""
        if not daddr:
            notes.append("field row carries no struct_data_addr; live value not read")
        else:
            si = c.request("walk_instance", addr=daddr, class_addr=addr, timeout=120)
            sfields = si.get("fields") or []
            say("\n--- live struct instance @ 0x%s ---" % daddr)
            for f in sfields[:4]:
                say("   %-16s offset=%-4s type=%-14s value=%r"
                    % (f.get("name"), f.get("offset"), f.get("type"), f.get("value")))
            d = next((f for f in sfields if f.get("name") == "Description"), None)
            if d is None:
                fails.append("A7: the struct instance reports no 'Description' field -- the offset-0 "
                             "field is missing at read time, which is the pad regression")
            else:
                v = str(d.get("value") or "")
                if d.get("offset") != 0:
                    fails.append("A7: Description reads at offset %r, expected 0" % d.get("offset"))
        say("\noffset-0 field value: %r   <-- MUST contain %r" % (v, VALUE))
        if VALUE not in v:
            fails.append("A7: the offset-0 field does not read %r (got %r) -- it is being read at "
                         "the wrong address, which is what the pad regression looks like" % (VALUE, v))

    say("\n================ A7 RESULT ================")
    for n in notes:
        say("NOTE: " + n)
    if fails:
        say("FAIL (%d):" % len(fails))
        for f_ in fails:
            say("   - " + f_)
        return 1
    say("PASS -- the empty base reports PropertiesSize 1 with no fields, the derived struct starts "
        "its own chain at offset 0, and the offset-0 field reads its real value.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
