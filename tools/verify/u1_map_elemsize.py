"""U1 - a degraded ElementSize on a TMap must REFUSE the element read, not wedge or lie.

    py tools/verify/u1_map_elemsize.py

THE ROW. `Ubel.cpp`'s map walker computes the pair stride from the key/value properties'
`ElementSize`. If that value is garbage the stride is garbage, and the walker must take its
degraded branch - `Sein::Warn("WALK:MapP", "Cannot read map elements for '%s': ...")`
(Ubel.cpp:4573) - rather than read past the buffer or hang. The branch has never been
exercised: the register records it as "🟡 PARTIAL ... the degraded branch itself is NOT
TESTED and must not be recorded as passing".

WHY A POKE AND NOT A STAGED BUILD. Nothing in the source needs changing - the input is data.
`mutate_guard.Mutation` captures the original bytes, restores them in a `finally` with a
read-back check, and additionally captures `expect_unchanged` regions so a stray write is
caught rather than assumed away.

⭐ FINDING THE ValueProp IS THE HARD PART, AND IT IS SEARCHED, NOT ASSUMED. `GetMapPairLayout`
probes `DynOff::FSTRUCTPROP_STRUCT + delta` over
`{0, 8, 4, 0xC, -4, -8, 0x10, -0x10}` for KeyProp, with ValueProp at `+8`. `get_offsets` does
not publish `FSTRUCTPROP_STRUCT`, so this rig scans an aligned window of the FMapProperty for
a POINTER PAIR whose ElementSizes match what the field's own name says they must be:

    Map_NameToInt : FName key = 8 bytes, int32 value = 4 bytes

Requiring BOTH sizes to match a value known from the type - rather than taking the first
plausible pointer - is what makes the identification a witness instead of a guess. The rig
refuses to run if it cannot find exactly one such pair.

⚠ `fproperty_elemsize` is read from `get_offsets` at RUNTIME (52 here), not from
`Grimoire.h`'s 0x3C default - the two disagree on this build.
"""
from __future__ import annotations

import argparse
import os
import pathlib
import struct
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from pipe_client import PipeClient          # noqa: E402
from mutate_guard import Mutation           # noqa: E402
from ad4_contested import find_live_actor    # noqa: E402

LOG = pathlib.Path(os.environ["LOCALAPPDATA"]) / "UE5CEDumper" / "Logs" / "DumperTest"
FIELD = "Map_NameToInt"
EXPECT_KEY_SIZE, EXPECT_VAL_SIZE = 8, 4      # FName -> int32, from the field's own name


def sizes():
    return {f: f.stat().st_size for f in LOG.glob("*-0.log")}


def since(mark):
    out = []
    for f in LOG.glob("*-0.log"):
        try:
            out.append(f.read_text(encoding="utf-8", errors="replace")[mark.get(f, 0):])
        except OSError:
            pass
    return chr(10).join(out)


def rd(c, addr, n):
    r = c.request("read_mem", addr=addr if isinstance(addr, str) else "0x%X" % addr, size=n)
    h = r.get("hex") or r.get("bytes") or ""
    return bytes.fromhex(h) if h else b""


def i32(c, addr):
    b = rd(c, addr, 4)
    return struct.unpack("<i", b)[0] if len(b) == 4 else None


def usermode(p):
    return 0x10000 < p < 0x7FFFFFFFFFFF


def map_field(c, addr):
    for f in c.request("walk_instance", addr=addr, array_limit=64).get("fields", []):
        if f.get("name") == FIELD:
            return f
    return None


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--window", type=lambda s: int(s, 0), default=0x90)
    a = ap.parse_args(argv)
    fails = []

    with PipeClient() as c:
        c.assert_build()
        c.ensure_scanned()
        elem_off = c.request("get_offsets").get("fproperty_elemsize")
        print("[0] runtime fproperty_elemsize = %s" % elem_off)

        act = find_live_actor(c)
        hits = c.request("search_properties", query=FIELD, limit=10).get("results", [])
        hits = [h for h in hits if h.get("class_name") == "DumperTestActor"]
        if not hits:
            raise SystemExit("u1: %s not found by search_properties" % FIELD)
        fa = int(hits[0]["field_addr"], 16)
        print("    %s FMapProperty @ 0x%X" % (FIELD, fa))

        # ---------- WITNESS the ValueProp instead of assuming an offset ----------
        blob = rd(c, fa, a.window)
        cands = []
        for off in range(0, len(blob) - 16, 8):
            kp, vp = struct.unpack_from("<QQ", blob, off)
            if not (usermode(kp) and usermode(vp)):
                continue
            ks, vs = i32(c, kp + elem_off), i32(c, vp + elem_off)
            if ks == EXPECT_KEY_SIZE and vs == EXPECT_VAL_SIZE:
                cands.append((off, kp, vp))
        print("[1] pointer pairs whose ElementSizes are (%d, %d): %d"
              % (EXPECT_KEY_SIZE, EXPECT_VAL_SIZE, len(cands)))
        for off, kp, vp in cands:
            print("      +0x%02X  KeyProp=0x%X  ValueProp=0x%X" % (off, kp, vp))
        if len(cands) != 1:
            raise SystemExit("u1: need exactly ONE witnessed (key,value) pair; got %d. Refusing "
                             "to poke a guessed address." % len(cands))
        _, keyProp, valueProp = cands[0]
        tgt = valueProp + elem_off

        # ---------- baseline: the map must READ before we break it ----------
        f0 = map_field(c, act["addr"])
        print("[2] before: map_count=%s elements=%d" % (f0.get("map_count"), len(f0.get("map_elements") or [])))
        if not f0 or (f0.get("map_count") or 0) < 1 or not f0.get("map_elements"):
            fails.append("2: the map does not read cleanly to begin with, so a later failure "
                         "would not be attributable to the poke")

        # ---------- the mutation ----------
        mark = sizes()
        with Mutation(c, "%s ValueProp ElementSize" % FIELD, tgt, 4,
                      expect_unchanged={"KeyProp ElementSize": (keyProp + elem_off, 4)}):
            c.request("write_mem", addr="0x%X" % tgt, bytes=struct.pack("<i", 0x40000200).hex())
            # ⭐ READ IT BACK. "the walker still worked" has two very different causes: the
            # walker ignored a garbage size, or the WRITE NEVER LANDED. Only a read-back
            # separates them, and without it this rig would report a defect either way.
            back = i32(c, tgt)
            print("    poke read-back: 0x%08X (want 0x40000200)" % (back & 0xFFFFFFFF))
            if (back & 0xFFFFFFFF) != 0x40000200:
                fails.append("3: the poke did not land (read-back 0x%08X) -- nothing below "
                             "measures the walker" % (back & 0xFFFFFFFF))
            time.sleep(0.3)
            f1 = map_field(c, act["addr"])
            got = since(mark)
            warned = ("Cannot read map elements for '%s'" % FIELD) in got
            print("[3] with a bogus ElementSize: map_count=%s elements=%d  oracle_warn=%s"
                  % (f1.get("map_count") if f1 else None,
                     len(f1.get("map_elements") or []) if f1 else -1, warned))
            for ln in got.splitlines():
                if "Cannot read map elements" in ln:
                    print("      LOG:", ln.strip()[:150])
            if not warned:
                fails.append("3: the degraded branch did NOT fire -- no 'Cannot read map "
                             "elements' warning, so either the walker accepted the garbage "
                             "stride or it failed somewhere else entirely")
            if f1 and f1.get("map_elements"):
                fails.append("3: elements were still returned with a garbage ElementSize -- the "
                             "walker read them anyway, which is the defect U1 is about")

        # ---------- restored: it must WORK again (proves no wedge, and the poke was the cause) ----------
        time.sleep(0.3)
        f2 = map_field(c, act["addr"])
        print("[4] after restore: map_count=%s elements=%d"
              % (f2.get("map_count") if f2 else None, len(f2.get("map_elements") or []) if f2 else -1))
        if not f2 or not f2.get("map_elements"):
            fails.append("4: the map still does not read after the restore -- the walker wedged, "
                         "or the restore did not take")

    print("")
    print("=" * 72)
    print("U1 degraded map ElementSize: %s" % ("PASS" if not fails else "FAIL"))
    for f in fails:
        print("   - %s" % f)
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
