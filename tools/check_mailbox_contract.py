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
GOLDEN_VERSION = 2
GOLDEN_HASH = "7021c884dde831a77009d4a8b968f6c2e8382941c4100f87dda223fe92b5b63b"

MIMIC_H = os.path.join("dll", "src", "Mimic.h")
LAYOUT_CS = os.path.join("ui", "UE5DumpUI", "Services", "CeMailboxLayout.cs")

# Enums whose VALUES a generated script depends on.
CONTRACT_ENUMS = ("Cmd", "TeleportOp", "FlyOp", "ForegroundOp",
                  "QueryPtrOp", "TimeOp", "ProtectOp", "Status", "InitState")


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

    print(f"CHECK OK: mailbox contract v{cur} (min {lo}), surface hash matches, "
          "C# mirror agrees.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
