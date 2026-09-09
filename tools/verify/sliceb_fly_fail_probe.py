r"""`[SLICEB-FLY-2026-09-09]` — the probe that shows the FR_ERR_WRITE arm is NOT reachable.

    py tools/verify/sliceb_fly_fail_probe.py     # ⚠ addresses are per-run, see below

⛔ THIS IS A NULL RESULT, AND IT IS SHIPPED BECAUSE A NEGATIVE CLAIM NEEDS EVIDENCE TOO.
`sliceb_fly_arm.py` says the failure path "is not reachable from outside the process". That
is a strong claim to make about one's own unverified code, so it was MEASURED rather than
asserted. `Macht::WriteBytes` returns false only when `VirtualProtect` is refused, i.e. the
target page is MEM_FREE or MEM_RESERVE. There are exactly two ways to arrange that, and both
were tried:

  (a) DECOMMIT the page holding `cmc + modeOff`. ⛔ MEASURED DEAD, not guessed:
      `cmc = 0x2A3948CF010` and `modeOff = 513`, so `cmc + 0x10` and `cmc + modeOff` are on the
      SAME page. `ResolveCtx` reads `cmc + 0x10` (via `Ubel::GetClass`), so any page trick makes
      it return `FR_ERR_REFLECT` FIRST — a different arm, and a false green if mistaken for one.

  (b) POINT `modeOff` ITSELF at unmapped memory, by patching `FPROPERTY_OFFSET` on the
      MovementMode FField. `ResolveCtx` never reads the instance at `modeOff` — it resolves it
      by reflection — so it would still return `FR_OK` and the WRITE would be the first thing
      to fail. THIS PROBE. Measured 2026-09-09:

          patched to 4194304 (read-back 4194304)
          walk_instance sees offset=513 value='MOVE_Walking'      <- the CACHED layout
          fly_set enable -> state=1, active=true, current_mode=5  <- the write still landed
          RESTORED: offset reads 513  OK

⭐⭐ THE NULL RESULT IS THE POINT: it reproduces `[CLASSCACHE-FRONTED-2026-09-09]` from a
SECOND, unrelated experiment. `FindField` -> `WalkClass` is memoised in a 2048-entry LRU that
nothing invalidates, and `CharacterMovementComponent` was already in it, so a patched FField is
invisible to both `Dunste` and the walker. That note predicted this would block "that whole
family of layout experiments" and named only the two scalar delegate-refusal arms; it now also
blocks the Fly write arm and a live `ByteProperty` refusal arm. The `--force` / debug-only
`invalidate_class_cache` it proposes would unblock all four at once.

⚠ `FPROPERTY_OFFSET` IS 0x44 HERE, NOT `Grimoire.h`'s 0x4C. The header value is a
compile-time DEFAULT; Genau derives the real one per title (`get_offsets` -> 68). The first run
read 675 at `FField+0x4C` and the sanity guard below REFUSED to write — which is the only reason
a wild 4-byte write did not go into a live game object. Keep that guard.

⚠ ADDRESSES ARE PER-RUN. `FFIELD` is a heap address from one DumperTest session; re-derive it
with `search_properties` (`field_addr` for MovementMode on CharacterMovementComponent) before
re-running. The guard turns a stale address into a refusal rather than into corruption.

Restores in a `finally` with a verified read-back (`mutate_guard.py`'s rule).
"""
import ctypes
import ctypes.wintypes as wt
import json
import sys

sys.path.insert(0, r"D:\Github\UE5CEDumper\tools\verify")
from pipe_client import PipeClient          # noqa: E402
from sw1_worker_fault import pid_of         # noqa: E402

FFIELD = 0x2A390791C80          # MovementMode FField on CharacterMovementComponent
FPROPERTY_OFFSET = 0x44         # DynOff::FPROPERTY_OFFSET, taken from the LIVE
                                # get_offsets table (68). ⚠ Grimoire.h's 0x4C is the
                                # compile-time DEFAULT; Genau derives the real one per
                                # title, and the sanity guard below is what caught the
                                # difference before anything was written into the game.
BOGUS = 0x400000                # 4 MiB past the CMC -- certainly not committed


def main():
    pid = pid_of("DumperTest.exe")
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    k32.OpenProcess.restype = wt.HANDLE
    h = k32.OpenProcess(0x1F0FFF, False, pid)
    if not h:
        raise SystemExit("OpenProcess failed")
    addr = FFIELD + FPROPERTY_OFFSET
    buf = ctypes.create_string_buffer(4)
    n = ctypes.c_size_t(0)
    if not k32.ReadProcessMemory(h, ctypes.c_void_p(addr), buf, 4, ctypes.byref(n)):
        raise SystemExit("read failed")
    orig = buf.raw[:4]
    print("FPROPERTY_OFFSET at 0x%X reads %d" % (addr, int.from_bytes(orig, "little")))
    if int.from_bytes(orig, "little") != 513:
        raise SystemExit("*** that is not MovementMode's offset -- refusing to write")

    try:
        payload = BOGUS.to_bytes(4, "little")
        k32.WriteProcessMemory(h, ctypes.c_void_p(addr), payload, 4, ctypes.byref(n))
        k32.ReadProcessMemory(h, ctypes.c_void_p(addr), buf, 4, ctypes.byref(n))
        print("patched to %d (read-back %d)" % (BOGUS, int.from_bytes(buf.raw[:4], "little")))

        with PipeClient() as c:
            # \u2460 Does the WALKER see the patched offset, or the cached one?
            w = c.request("walk_instance", addr="0x2A3948CF010",
                          array_limit=1, preview_limit=0)
            for f in w.get("fields", []):
                if f.get("name") == "MovementMode":
                    print("walk_instance sees offset=%s value=%r"
                          % (f.get("offset"), f.get("value")))
            # \u2461 And does Dunste?
            st = c.request("fly_set", enable=True, speed=600.0, preset=0, noclip=False)
            print("fly_set enable ->", json.dumps(
                {k: st.get(k) for k in ("state", "active", "current_mode", "code")}))
            off = c.request("fly_set", enable=False)
            print("fly_set disable ->", json.dumps(
                {k: off.get(k) for k in ("state", "active", "current_mode", "code")}))
    finally:
        k32.WriteProcessMemory(h, ctypes.c_void_p(addr), orig, 4, ctypes.byref(n))
        k32.ReadProcessMemory(h, ctypes.c_void_p(addr), buf, 4, ctypes.byref(n))
        back = int.from_bytes(buf.raw[:4], "little")
        print("RESTORED: offset reads %d  %s" % (back, "OK" if back == 513 else "*** NOT RESTORED"))


main()
