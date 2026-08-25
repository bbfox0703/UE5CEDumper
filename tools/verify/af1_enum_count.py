"""AF1 - a malformed UEnum NumValues must be REFUSED, not turned into a negative count.

    py tools/verify/af1_enum_count.py --target EInterpCurveMode --control ETextGender

THE DEFECT (audit #5 AF1), quoted from the fix's own comment in `Neu.h`:

    `static_cast<int32_t>(numNew) > maxCount` let the whole upper half of the uint32 range
    through: 0x80000000 casts to -2147483648, which is not greater than maxCount, so the
    guard passed and `out.count` below became NEGATIVE.

The fix keeps `numNew` UNSIGNED and widens both sides to int64, so 0x80000000 compares as
2147483648 and is rejected.

⚠ IT ONLY EXISTS ON THE UE5.6+ `FNameData` LAYOUT. The Legacy branch never had the bug -
its `num <= 0` test catches the wrapped value, while this branch only tested `== 0`. So
DumperTest (5.4, Legacy) can NEVER reach it. This rig needs a 5.6+ host; StackOBot 5.8
Shipping is the one on disk, and its log confirms the layout:
`UEnum::Names detected at UEnum+0x40 (UE5.6+ FNameData, verified with 'ENetRole', count=5)`.

⭐ WHY A FRESH PROCESS IS MANDATORY. `s_enumCache` in `Ubel.cpp` is a static map that is
NEVER cleared or erased (verified: no `.clear()` / `.erase()` anywhere). Once an enum has
resolved, the poke is unobservable - the cached entries are returned without re-reading
memory. So the sequence is: baseline in run 1, RESTART, poke before anything resolves, read
once in run 2.

⭐ THE PAIRED CONTROL. A refused enum reports `entries: []` - but an enum could be empty
anyway. So a second, UNTOUCHED enum is read in the SAME `list_enums` call and must still
return its baseline entry count. Target collapses, control does not: that pairing is what
separates "the guard fired" from "this run was broken".
"""
from __future__ import annotations

import argparse
import json
import pathlib
import struct
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient          # noqa: E402

UENUM_NAMES = 0x40          # from the DYNO:Enum log line; NOT published by get_offsets
NUMVALUES_OFF = 0x10        # FNameData57: +0x00 FName*, +0x08 int64*, +0x10 int32 NumValues
BOGUS = 0x80000000          # the exact value the pre-fix cast turned into -2147483648


def find_enum(c, name):
    r = c.request("find_instances", class_name="Enum", limit=4000, exact_match=True)
    for i in r.get("instances", []):
        if i.get("class") == "Enum" and i.get("name") == name:
            return int(i["addr"], 16)
    return None


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--target", default="EInterpCurveMode")
    ap.add_argument("--control", default="ETextGender")
    ap.add_argument("--baseline", default="out/af1_baseline.json")
    a = ap.parse_args(argv)
    fails = []

    base = json.load(open(a.baseline, encoding="utf-8"))
    want_t, want_c = base.get(a.target), base.get(a.control)
    print("[0] baseline (run 1, nothing poked): %s=%s entries, %s=%s entries"
          % (a.target, want_t, a.control, want_c))
    if not want_t or want_t < 3 or not want_c or want_c < 3:
        raise SystemExit("af1: pick a target/control that HAVE entries in the baseline")

    with PipeClient() as c:
        c.assert_build()
        c.ensure_scanned()
        ta, ca = find_enum(c, a.target), find_enum(c, a.control)
        if not ta or not ca:
            raise SystemExit("af1: could not locate both enums in this process")
        tgt = ta + UENUM_NAMES + NUMVALUES_OFF
        print("[1] this process: %s @0x%X  %s @0x%X   poking 0x%X"
              % (a.target, ta, a.control, ca, tgt))

        # read_mem replies carry `bytes`, not `hex` -- checking only `hex` made the read-back
        # silently empty, so the first run could not attribute the refusal to its own poke.
        def rd4(addr):
            rr = c.request("read_mem", addr="0x%X" % addr, size=4)
            return rr.get("bytes") or rr.get("hex") or ""
        before = rd4(tgt)
        n0 = struct.unpack("<I", bytes.fromhex(before))[0] if before else None
        print("    NumValues before: %s (0x%08X)" % (n0, n0 or 0))
        if n0 != want_t:
            fails.append("1: NumValues reads %s but the baseline says %s entries -- the offset "
                         "or the target is wrong, so the poke would land somewhere unknown"
                         % (n0, want_t))

        c.request("write_mem", addr="0x%X" % tgt, bytes=struct.pack("<I", BOGUS).hex())
        back = rd4(tgt)
        n1 = struct.unpack("<I", bytes.fromhex(back))[0] if back else None
        print("    NumValues after poke: 0x%08X (want 0x%08X)" % (n1 or 0, BOGUS))
        if n1 != BOGUS:
            fails.append("1: the poke did not land -- nothing below measures the guard")

        r = c.request("list_enums")
        got = {e["name"]: len(e.get("entries") or []) for e in r.get("enums", [])}
        gt, gc = got.get(a.target), got.get(a.control)
        print("[2] after the poke: %s=%s entries (want 0 -- REFUSED), %s=%s entries (want %s)"
              % (a.target, gt, a.control, gc, want_c))

        if gt != 0:
            fails.append("2: the poisoned enum still returned %s entries -- the guard did NOT "
                         "refuse a NumValues of 0x80000000" % gt)
        if gc != want_c:
            fails.append("2: the CONTROL enum returned %s entries, not its baseline %s -- this "
                         "run is broken generally, so the target's 0 proves nothing"
                         % (gc, want_c))

        # restore the bytes even though this process has now cached the refusal
        c.request("write_mem", addr="0x%X" % tgt, bytes=struct.pack("<I", n0 or want_t).hex())
        print("[3] restored NumValues to %s (the cache holds the refusal for THIS process; a "
              "fresh launch reads clean)" % (n0 or want_t))

    print("")
    print("=" * 72)
    print("AF1 malformed UEnum NumValues: %s" % ("PASS" if not fails else "FAIL"))
    for f in fails:
        print("   - %s" % f)
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
