#!/usr/bin/env python3
"""Fail the build when the CE Lua <-> DLL mailbox CONTRACT changes without a version bump.

WHY THIS EXISTS
  The contract check shipped in build 2744 is only as good as someone remembering to
  bump `Mimic::MAILBOX_CONTRACT` when they move a field. Nothing enforces that, and a
  forgotten bump is WORSE than no versioning at all: every script then asserts it is
  compatible while writing to the wrong offsets, and the check that was supposed to
  catch it says "fine".

  So this hashes the contract surface and pins it to a golden value. Change the surface
  without bumping the version and the hash moves, CI goes red, and the message tells you
  which of the two numbers to touch.

WHAT IS IN THE HASH (matches the rule in dll/src/Mimic.h)
  1. Every `MailboxData` field: name, type and declared offset comment
  2. Every `Cmd` value
  3. Every per-command op enum value (TeleportOp / FlyOp / ProtectOp / QueryPtrOp /
     TimeOp / ForegroundOp)
  4. `Status` and `InitState` values
  It deliberately ignores comments, blank lines and prose, so documentation edits do not
  trip it — only the values a generated script actually depends on.

  It also checks the C# mirror agrees: `CeMailboxLayout.ContractVersion` must equal the
  DLL's `MAILBOX_CONTRACT`, because that constant is what gets baked into every emitted
  script. A mismatch there means scripts claim a contract the DLL does not implement.

WHAT ELSE MIRRORS THE LAYOUT (audit #5 AA36)
  `CeMailboxLayout.cs` is NOT the only hand-copy. The two standalone CE helpers
  (`scripts/ue5_invoke_helper.lua`, `scripts/ue5_freeze_helper.lua`), the shipped table
  `scripts/UE5CEDumper.CT`, and the two offline Lua test rigs each re-declare the
  offsets and enum values as their own literals. Until AA36 this file could not see any
  of them, so they could drift out of agreement with Mimic.h in total silence — and a
  gate with a hole in it is worse than a known-absent gate, because it reads as covered.

  So `check_lua_mirrors` now compares every one of those literals against Mimic.h:
    * offsets are COMPUTED from the packed struct (`#pragma pack(1)`, so an offset is
      just the sum of the preceding field sizes) rather than read from the `// 0xNNN:`
      comments — which also validates those comments, since the Lua copies were made
      from them;
    * `CMD_* / STATUS_* / INIT_* / LI_*` are compared against the enum member of the
      same name, and `ENTRY_SIZE_* / MAX_PAGES` against their `constexpr` originals;
    * each mirror's own `UE5_SCRIPT_CONTRACT` must fall inside [MIN, MAILBOX_CONTRACT],
      which is the very range its runtime `checkContract()` asserts against the DLL.

  The registry below is deliberately CLOSED: a layout-shaped constant in a mirrored file
  that is not registered here is a hard failure, not a skip. That is what stops the gap
  reopening — you cannot add a sixth hand-copy, or a new offset in an existing one,
  without this file being told what it mirrors.

WHEN IT FIRES, YOU HAVE EXACTLY TWO CHOICES
  * The change is ADDITIVE (a new Cmd, a new op at an unused number, a new knobId):
    bump MAILBOX_CONTRACT and CeMailboxLayout.ContractVersion. Leave the MIN alone —
    old scripts never referenced the new thing, so they stay valid.
  * The change BREAKS older scripts (a moved field, a renumbered op, changed semantics):
    bump MAILBOX_CONTRACT, CeMailboxLayout.ContractVersion **and** MAILBOX_CONTRACT_MIN.
  Then update GOLDEN below to the printed hash.

Usage:
  py tools/check_mailbox_contract.py            # repo root inferred from this file
  py tools/check_mailbox_contract.py --update   # print the new golden line
Exit 0 = clean, 1 = the contract moved without a bump (or the mirror disagrees).
"""
import hashlib
import os
import re
import sys

# (contract_version, sha256 of the surface). Update BOTH together, never one.
#
# NOTE on version 2 (build 2926, audit #5 AA2/AA3): the version moved while the HASH
# did NOT, and that is correct rather than an oversight. The surface hash covers field
# names, types, declared offsets and enum VALUES — the LAYOUT. Version 2 changed a
# field's MEANING: CMD_LIST_INSTANCES now publishes the enumerated UClass* in
# `instanceAddr` and the ClassPrivate offset in `ufuncAddr`, both previously unused
# outputs for that command. No field moved, so nothing this script hashes could move.
#
# That is a real blind spot worth knowing about: a command that starts reading or
# writing a field it never touched before is a contract change this hash cannot see.
# The "bumped but unchanged" branch below is what surfaces it — it forces the bumper
# to come here and say why, which is the check actually doing its job.
#
# NOTE on version 3 (`[FREEZESCOPE-2026-08-18]`): this one moved the hash, and for two
# independent reasons — the struct grew and a new contract enum joined the surface.
#
#   1. `MailboxData` gained `cmdFlags` (IN) and `cmdOutFlags` (OUT) at its TAIL. The
#      tail is the only place it can grow without invalidating a saved .CT: every
#      pre-existing field keeps its offset, so an old script's reads and writes land
#      exactly where they always did. `sizeof` changed, which item 1 of Mimic.h's rule
#      says to bump for — hence the version move — but nothing OLD moved, which is why
#      MAILBOX_CONTRACT_MIN stays at 1.
#   2. `ListInstancesFlag` was added to CONTRACT_ENUMS above. Its two bits are written
#      and read as literals by scripts/ue5_freeze_helper.lua, which makes them contract
#      surface in exactly the sense the op enums already are; leaving them unhashed
#      would have re-created the gap this file exists to close.
#
# WHY the change is additive despite CMD_LIST_INSTANCES now having two wire formats:
# the 16-byte derived page is reachable ONLY when the caller sets LI_IN_DERIVED, the
# flag defaults to 0, and the handler CLEARS it after every use — so no contract-1/2
# script can set it, inherit it, or encounter the format it selects.
GOLDEN_VERSION = 3
GOLDEN_HASH = "b131d22dbef3e9bb5453d88afca22850ff7d54883ab99766a2c491aa0a5a6ac3"

MIMIC_H = os.path.join("dll", "src", "Mimic.h")
LAYOUT_CS = os.path.join("ui", "UE5DumpUI", "Services", "CeMailboxLayout.cs")

# Enums whose VALUES a generated script depends on.
CONTRACT_ENUMS = ("Cmd", "TeleportOp", "FlyOp", "ForegroundOp",
                  "QueryPtrOp", "TimeOp", "ProtectOp", "ListInstancesFlag",
                  "Status", "InitState")

# ---------------------------------------------------------------------------
# Hand-copied layout mirrors outside Mimic.h / CeMailboxLayout.cs (AA36)
# ---------------------------------------------------------------------------

# Every file that re-declares mailbox offsets or contract enum values as literals.
LUA_MIRRORS = (
    os.path.join("scripts", "ue5_invoke_helper.lua"),
    os.path.join("scripts", "ue5_freeze_helper.lua"),
    os.path.join("scripts", "UE5CEDumper.CT"),
    os.path.join("scripts", "tests", "freeze_helper_test.lua"),
    os.path.join("scripts", "tests", "invoke_helper_test.lua"),
)

# A constant in a mirrored file whose NAME matches one of these is layout surface and
# must be registered below. Anything else in those files is ordinary script state.
LUA_LAYOUT_PREFIXES = ("OFF_", "CMD_", "STATUS_", "INIT_", "LI_",
                       "ENTRY_SIZE_", "MAILBOX_", "MAX_PAGES")

# Lua constant name -> the MailboxData field whose offset it copies.
# Two spellings of the same field are deliberate: the invoke helper calls
# `functionFlags` OFF_FLAGS, the freeze helper OFF_FUNC_FLAGS (it stores a page count
# there). Both mirror the same field, so both are checked against the same offset.
LUA_OFFSET_ALIASES = {
    "OFF_CMD": "cmd",
    "OFF_STATUS": "status",
    "OFF_RESULT": "result",
    "MAILBOX_INITSTATE_OFF": "initState",
    "OFF_INSTANCE": "instanceAddr",
    "OFF_UFUNC": "ufuncAddr",
    "OFF_PARMS_SZ": "parmsSize",
    "OFF_NUM_PARMS": "numParms",
    "OFF_FLAGS": "functionFlags",
    "OFF_FUNC_FLAGS": "functionFlags",
    "OFF_CLASS": "className",
    "OFF_FUNC": "funcName",
    "OFF_ERR": "errorMsg",
    "OFF_PARAMS": "paramsData",
    "OFF_CMD_FLAGS": "cmdFlags",
    "OFF_OUT_FLAGS": "cmdOutFlags",
}

# Lua constant name -> (enum, member) it copies. Same-named members only; the point is
# that the Lua literal must equal what Mimic.h declares under that exact name.
LUA_ENUM_ALIASES = {
    name: (enum, name)
    for enum, names in (
        ("Cmd", ("CMD_IDLE", "CMD_INVOKE", "CMD_FIND_INSTANCE", "CMD_FIND_FUNCTION",
                 "CMD_INVOKE_BY_NAME", "CMD_LIST_FUNCTIONS", "CMD_LIST_INSTANCES",
                 "CMD_SET_DEBUG_CAMERA", "CMD_TELEPORT", "CMD_PROTECT")),
        ("Status", ("STATUS_IDLE", "STATUS_DONE", "STATUS_PROCESSING")),
        ("InitState", ("INIT_IDLE", "INIT_RUNNING", "INIT_READY",
                       "INIT_FAILED", "INIT_SKIPPED")),
        ("ListInstancesFlag", ("LI_IN_DERIVED", "LI_OUT_TRUNCATED")),
    )
    for name in names
}

# Lua constant name -> the `constexpr` in Mimic.h it copies.
LUA_CONSTEXPR_ALIASES = {
    "ENTRY_SIZE_EXACT": "LIST_INSTANCES_ENTRY_EXACT",
    "ENTRY_SIZE_DERIVED": "LIST_INSTANCES_ENTRY_DERIVED",
    "MAX_PAGES": "LIST_INSTANCES_MAX_PAGES",
}

# Sizes of the scalar types MailboxData is built from. Absent type -> hard failure,
# because guessing a size would silently shift every offset after it.
C_TYPE_SIZES = {
    "int8_t": 1, "uint8_t": 1, "char": 1, "bool": 1,
    "int16_t": 2, "uint16_t": 2,
    "int32_t": 4, "uint32_t": 4, "float": 4,
    "int64_t": 8, "uint64_t": 8, "double": 8,
}


def read(path):
    with open(path, encoding="utf-8") as f:
        return f.read()


def strip_comments(text):
    """Drop // and /* */ so prose edits do not move the hash."""
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    return re.sub(r"//.*?$", "", text, flags=re.M)


def enum_members(text, name):
    """`NAME = VALUE` pairs from one enum, in declaration order."""
    m = re.search(r"\benum\s+" + name + r"\s*(?::\s*\w+)?\s*\{(.*?)\}", text, flags=re.S)
    if not m:
        return None
    out = []
    for line in m.group(1).split(","):
        line = line.strip()
        if not line:
            continue
        mm = re.match(r"(\w+)\s*=\s*([0-9xXa-fA-F]+)", line)
        if mm:
            out.append(f"{mm.group(1)}={int(mm.group(2), 0)}")
    return out


def struct_fields(text, name):
    """`type name[dim]` entries of one struct, in declaration order.

    Order IS the contract: these are packed, so a reordering moves every offset
    after it even though no single field's declaration changed.
    """
    m = re.search(r"\bstruct\s+" + name + r"\s*\{(.*?)\n\}", text, flags=re.S)
    if not m:
        return None
    out = []
    for line in m.group(1).split(";"):
        line = " ".join(line.split())
        if not line:
            continue
        mm = re.match(r"(?:volatile\s+)?([\w:]+)\s+(\w+)\s*(\[\s*\d+\s*\])?$", line)
        if mm:
            out.append(f"{mm.group(1)} {mm.group(2)}{mm.group(3) or ''}")
    return out


def surface(root):
    """The canonical text whose hash is pinned. Deterministic and order-preserving."""
    text = strip_comments(read(os.path.join(root, MIMIC_H)))
    parts = []

    fields = struct_fields(text, "MailboxData")
    if fields is None:
        return None, "struct MailboxData not found in Mimic.h — did it move or get renamed?"
    parts.append("MailboxData:" + "|".join(fields))

    for name in CONTRACT_ENUMS:
        members = enum_members(text, name)
        if members is None:
            return None, f"enum {name} not found in Mimic.h — did it move or get renamed?"
        parts.append(f"{name}:" + "|".join(members))

    return "\n".join(parts), None


def const_int(text, pattern):
    m = re.search(pattern, text)
    return int(m.group(1), 0) if m else None


def field_offsets(fields):
    """Byte offset of every MailboxData field, COMPUTED not read.

    The struct is `#pragma pack(push, 1)`, so an offset is exactly the sum of the
    preceding field sizes. Computing means the `// 0xNNN:` comments are checked too
    rather than trusted — and those comments are what the Lua copies were made from.
    """
    offsets, off = {}, 0
    for f in fields:
        m = re.match(r"([\w:]+)\s+(\w+)(?:\[\s*(\d+)\s*\])?$", f)
        if not m:
            return None, 0, f"cannot parse MailboxData field {f!r}"
        ctype, name, dim = m.group(1), m.group(2), m.group(3)
        size = C_TYPE_SIZES.get(ctype)
        if size is None:
            return None, 0, (f"MailboxData field {name!r} has unknown type {ctype!r} — "
                             "add it to C_TYPE_SIZES, or every offset after it is wrong")
        offsets[name] = off
        off += size * (int(dim) if dim else 1)
    return offsets, off, None


def lua_int_consts(text):
    """`local A = 1` / `local A, B = 1, 2` integer declarations -> {name: (value, line)}.

    Non-integer right-hand sides are skipped on purpose: a mirrored file is ordinary
    script apart from these literals, and only literals can drift against a C header.
    Later declarations win, which matches Lua scoping closely enough for a lint.
    """
    out = {}
    for lineno, raw in enumerate(text.splitlines(), 1):
        m = re.match(r"\s*(?:local\s+)?([A-Za-z_][\w]*(?:\s*,\s*[A-Za-z_][\w]*)*)"
                     r"\s*=\s*([^=].*?)\s*(?:--.*)?$", raw)
        if not m:
            continue
        names = [n.strip() for n in m.group(1).split(",")]
        values = [v.strip() for v in m.group(2).split(",")]
        if len(names) != len(values):
            continue
        for name, value in zip(names, values):
            if re.fullmatch(r"0[xX][0-9a-fA-F]+|\d+", value):
                out[name] = (int(value, 0), lineno)
    return out


def check_lua_mirrors(root, mimic, offsets, cur, lo):
    """Every hand-copied mailbox literal outside Mimic.h must agree with Mimic.h.

    Returns (problems, checked_count). See the AA36 note in the module docstring for
    why the registry is closed rather than best-effort.
    """
    problems, checked = [], 0

    enums = {}
    for name in CONTRACT_ENUMS:
        members = enum_members(mimic, name)
        if members is not None:
            enums[name] = dict(
                (m.split("=", 1)[0], int(m.split("=", 1)[1])) for m in members)

    for rel in LUA_MIRRORS:
        path = os.path.join(root, rel)
        if not os.path.exists(path):
            problems.append(f"{rel}: mirrored file is missing — it moved or was deleted, "
                            "so this gate silently stopped covering it. Update LUA_MIRRORS.")
            continue
        consts = lua_int_consts(read(path))

        for name, (value, lineno) in sorted(consts.items()):
            if name in LUA_OFFSET_ALIASES:
                field = LUA_OFFSET_ALIASES[name]
                want = offsets.get(field)
                if want is None:
                    problems.append(
                        f"{rel}:{lineno}: {name} mirrors MailboxData.{field}, which no "
                        "longer exists. The field was renamed or removed — fix the script "
                        "and LUA_OFFSET_ALIASES together.")
                elif value != want:
                    problems.append(
                        f"{rel}:{lineno}: {name} = 0x{value:03X} but MailboxData.{field} "
                        f"is at 0x{want:03X}. This script reads/writes the wrong bytes.")
                checked += 1

            elif name in LUA_ENUM_ALIASES:
                enum, member = LUA_ENUM_ALIASES[name]
                want = enums.get(enum, {}).get(member)
                if want is None:
                    problems.append(
                        f"{rel}:{lineno}: {name} mirrors {enum}::{member}, which is not "
                        f"in Mimic.h any more.")
                elif value != want:
                    problems.append(
                        f"{rel}:{lineno}: {name} = {value} but {enum}::{member} = {want}.")
                checked += 1

            elif name in LUA_CONSTEXPR_ALIASES:
                cname = LUA_CONSTEXPR_ALIASES[name]
                want = const_int(mimic, r"\b" + cname + r"\s*=\s*(\d+)")
                if want is None:
                    problems.append(
                        f"{rel}:{lineno}: {name} mirrors {cname}, which is not in Mimic.h.")
                elif value != want:
                    problems.append(
                        f"{rel}:{lineno}: {name} = {value} but {cname} = {want}.")
                checked += 1

            elif name.startswith(LUA_LAYOUT_PREFIXES):
                problems.append(
                    f"{rel}:{lineno}: {name} = {value} looks like mailbox layout surface "
                    "but is not registered in check_mailbox_contract.py. Add it to "
                    "LUA_OFFSET_ALIASES / LUA_ENUM_ALIASES / LUA_CONSTEXPR_ALIASES so it "
                    "is checked, or rename it if it is not layout.")

        # Each mirror asserts this range against the DLL at runtime; if the constant
        # baked here falls outside it, the script refuses itself on every machine.
        script_v = consts.get("UE5_SCRIPT_CONTRACT")
        if script_v is not None:
            v = script_v[0]
            checked += 1
            if not (lo <= v <= cur):
                problems.append(
                    f"{rel}:{script_v[1]}: UE5_SCRIPT_CONTRACT = {v} is outside the DLL's "
                    f"accepted range [{lo}, {cur}] — checkContract() would reject this "
                    "script against the very DLL it ships with.")

    return problems, checked


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    root = os.path.dirname(here)

    body, err = surface(root)
    if err:
        print(f"CHECK FAILED: {err}")
        return 1
    digest = hashlib.sha256(body.encode("utf-8")).hexdigest()

    mimic = read(os.path.join(root, MIMIC_H))
    cur = const_int(mimic, r"MAILBOX_CONTRACT\s*=\s*(\d+)")
    lo = const_int(mimic, r"MAILBOX_CONTRACT_MIN\s*=\s*(\d+)")
    cs = const_int(read(os.path.join(root, LAYOUT_CS)), r"ContractVersion\s*=\s*(\d+)")

    if "--update" in sys.argv:
        print(f'GOLDEN_VERSION = {cur}')
        print(f'GOLDEN_HASH = "{digest}"')
        return 0

    if cur is None or lo is None:
        print("CHECK FAILED: MAILBOX_CONTRACT / MAILBOX_CONTRACT_MIN not found in Mimic.h")
        return 1
    if lo > cur:
        print(f"CHECK FAILED: MAILBOX_CONTRACT_MIN ({lo}) > MAILBOX_CONTRACT ({cur}) "
              "— the accepted range is empty, so every script would be rejected.")
        return 1

    # The C# constant is what gets BAKED into every emitted script. If it disagrees with
    # the DLL, scripts claim a contract the DLL does not implement — the check would then
    # pass on a lie, which is worse than not checking.
    if cs != cur:
        print(f"CHECK FAILED: CeMailboxLayout.ContractVersion = {cs} but "
              f"Mimic::MAILBOX_CONTRACT = {cur}.\n"
              "  Generated scripts bake the C# value, so these MUST match.")
        return 1

    if digest != GOLDEN_HASH:
        bumped = cur != GOLDEN_VERSION
        print("CHECK FAILED: the mailbox contract surface changed.")
        print(f"  golden : version {GOLDEN_VERSION}  {GOLDEN_HASH}")
        print(f"  actual : version {cur}  {digest}")
        if not bumped:
            print("\n  MAILBOX_CONTRACT was NOT bumped. Every script generated before this\n"
                  "  change still claims to be compatible, and would write to the offsets\n"
                  "  you just moved. Decide which kind of change this is:\n"
                  "    additive (new Cmd / new op at an unused number / new knobId)\n"
                  "      -> bump MAILBOX_CONTRACT + CeMailboxLayout.ContractVersion only\n"
                  "    breaking (moved field / renumbered op / changed meaning)\n"
                  "      -> ALSO bump MAILBOX_CONTRACT_MIN\n"
                  "  then re-run with --update and paste the new golden pair.")
        else:
            print(f"\n  MAILBOX_CONTRACT was bumped to {cur} — good. Now re-run with\n"
                  "  --update and paste the new golden pair below.")
            print(f"  Reminder: MAILBOX_CONTRACT_MIN is {lo}. Bump it too ONLY if this\n"
                  "  change invalidates scripts older than it.")
        return 1

    if cur != GOLDEN_VERSION:
        print(f"CHECK FAILED: MAILBOX_CONTRACT is {cur} but the surface is unchanged "
              f"(golden {GOLDEN_VERSION}).\n"
              "  A bump with no contract change makes every older script look stale for\n"
              "  no reason. Revert the bump, or update GOLDEN_VERSION if it was deliberate.")
        return 1

    # ---- the hand-copied Lua/CT mirrors (AA36) -----------------------------
    # Comments are stripped for anything PARSED (an enum's `0xFF` would otherwise be
    # read out of the prose beside it) but kept for the declared-offset audit below,
    # which is entirely about whether those comments are still true.
    bare = strip_comments(mimic)
    offsets, total, err = field_offsets(struct_fields(bare, "MailboxData"))
    if err:
        print(f"CHECK FAILED: {err}")
        return 1

    # The `// 0xNNN:` comments are how a human copies an offset into a script, so a
    # stale one is a live trap even though nothing compiles it.
    declared = dict(re.findall(r"\b(\w+)\s*(?:\[\s*\d+\s*\])?\s*;\s*//\s*0x([0-9A-Fa-f]+):",
                               mimic))
    for name, hexoff in declared.items():
        want = offsets.get(name)
        if want is not None and int(hexoff, 16) != want:
            print(f"CHECK FAILED: MailboxData.{name} is documented at 0x{hexoff} but the "
                  f"packed layout puts it at 0x{want:03X}.\n"
                  "  Scripts are hand-copied from these comments, so a stale one becomes "
                  "a wrong offset in CE Lua.")
            return 1

    problems, checked = check_lua_mirrors(root, bare, offsets, cur, lo)
    if problems:
        print("CHECK FAILED: a hand-copied mailbox layout disagrees with Mimic.h.")
        for p in problems:
            print(f"  {p}")
        print("\n  These files are not compiled against anything, so nothing but this\n"
              "  check can notice. Fix the literal, or the registry at the top of\n"
              "  tools/check_mailbox_contract.py if the field genuinely moved.")
        return 1

    print(f"CHECK OK: mailbox contract v{cur} (min {lo}), surface hash matches, "
          f"C# mirror agrees; {checked} hand-copied literals across "
          f"{len(LUA_MIRRORS)} Lua/CT mirrors match the {total}-byte packed layout.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
