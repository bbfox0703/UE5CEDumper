r"""Self-test for mutate_guard.py against a LIVE DumperTest — because a restore harness that has
never restored anything is a promise, not a mechanism.

    py tools/verify/mutate_guard_selftest.py     (DumperTest running + injected)

Deliberately targets a PLAIN SCALAR, never a container header: this is validating the harness, and
a harness bug on a `TArray` header leaves the process holding a pointer its destructor will free.

Four things are checked, and each is a way the harness could be quietly useless:
  1. capture + poke + read-back witness         — the write actually lands
  2. collateral guard NOTICES a real change     — the negative control for guard #3
  3. collateral guard STAYS QUIET when nothing else moved
  4. restore puts the original bytes back, verified by read-back
"""
import pathlib
import struct
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
sys.stdout.reconfigure(encoding="utf-8", errors="replace")

from mutate_guard import Mutation, read_bytes, write_bytes, assert_channel_carries  # noqa: E402
from pipe_client import PipeClient  # noqa: E402

LOGDIR = pathlib.Path.home() / "AppData/Local/UE5CEDumper/Logs/DumperTest"


def find_live_actor(c):
    r = c.request("find_instances", class_name="DumperTestActor", limit=50)
    rows = r.get("results", []) or r.get("instances", [])
    for row in rows:
        name = row.get("name") or row.get("object_name") or ""
        if "Default__" not in name:
            return row.get("address") or row.get("addr"), name
    return None, None


def field_offset(c, inst, want):
    r = c.request("walk_instance", addr=inst)
    for f in r.get("fields", []):
        if f.get("name") == want:
            return f.get("offset"), f.get("type"), f.get("size")
    return None, None, None


def main():
    fails = []
    with PipeClient() as c:
        c.assert_build()
        c.ensure_scanned()

        inst, name = find_live_actor(c)
        if not inst:
            print("BLOCKED: no live DumperTestActor")
            return 2
        print("live actor: %s @ %s" % (name, inst))

        off, ftype, fsize = field_offset(c, inst, "TickCount")
        if off is None:
            off, ftype, fsize = field_offset(c, inst, "Health")
        if off is None:
            print("BLOCKED: neither TickCount nor Health found on the actor")
            return 2
        base = int(str(inst), 16)
        target = base + int(off)
        print("target: +0x%X (%s, %s bytes) -> 0x%X" % (int(off), ftype, fsize, target))

        # A second, unrelated 4 bytes on the same object, used as the collateral guard.
        guard_addr = base + 0x10
        guard = {"unrelated +0x10": (guard_addr, 4)}

        # --- 1, 3, 4: poke, no collateral, restore -----------------------------
        print()
        print("== 1/3/4: poke, collateral-quiet, restore ==")
        original = read_bytes(c, target, 4)
        with Mutation(c, "scalar", target, 4, expect_unchanged=guard) as m:
            if not m.apply(struct.pack("<i", 0x5A5A5A5A)):
                fails.append("apply() did not witness the write")
            if not m.assert_others_unchanged():
                fails.append("collateral guard fired when nothing else was touched")
        after = read_bytes(c, target, 4)
        if after != original:
            fails.append("restore did not put the original bytes back (%s vs %s)"
                         % ((after or b"").hex(), (original or b"").hex()))
        else:
            print("restore verified independently of the harness: %s" % original.hex().upper())

        # --- 2: the collateral guard must be ABLE to fire -----------------------
        print()
        print("== 2: NEGATIVE CONTROL — the collateral guard must notice a real change ==")
        guard_orig = read_bytes(c, guard_addr, 4)
        noticed = None
        with Mutation(c, "scalar", target, 4, expect_unchanged=guard) as m2:
            m2.apply(struct.pack("<i", 0x11111111))
            write_bytes(c, guard_addr, struct.pack("<i", 0x22222222))   # collateral, on purpose
            noticed = m2.assert_others_unchanged()
        write_bytes(c, guard_addr, guard_orig)                          # put the guard back
        if noticed:
            fails.append("the collateral guard did NOT notice a deliberate change — it is inert")
        else:
            print("collateral guard fired as it should; guard bytes restored to %s"
                  % guard_orig.hex().upper())
        if read_bytes(c, guard_addr, 4) != guard_orig:
            fails.append("failed to restore the guard region after the negative control")

        # --- the channel assertion, both ways -----------------------------------
        print()
        print("== assert_channel_carries, both directions ==")
        if not assert_channel_carries(LOGDIR / "offsets-0.log", "[OARR]", "the re-anchor marker"):
            fails.append("offsets-0.log does not carry [OARR] — expected on a scanned host")
        if assert_channel_carries(LOGDIR / "scan-0.log", "[OARR]", "the re-anchor marker"):
            fails.append("scan-0.log reported [OARR] traffic — the check is not discriminating, "
                         "which is the exact failure it exists to catch")
        else:
            print("  (and it correctly REFUSES scan-0.log, the file the old rig used)")

    print()
    if fails:
        print("SELF-TEST FAILED:")
        for f in fails:
            print("   -", f)
        return 1
    print("SELF-TEST PASSED — capture, witness, collateral guard (both ways), restore, channel check")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
