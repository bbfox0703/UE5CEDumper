#!/usr/bin/env python3
r"""Live check for [FINDINSTANCE-SUBSTRING-USERFACING] where only a live CheatManager can show it (build 3675 on).

    py tools/verify/launch_dumpertest.py dev58
    py tools/verify/inject.py --name DumperTest58
    py tools/verify/userpick_dbgcam_live.py              # 3 GCs, Debug Camera ON, the picks, one ToggleDebugCamera

Headless; the UI must NOT be connected. The user-facing class lookup ranks last an object the Debug Camera controller
(DCC) is, holds up its outer chain, or owns through AActor::Owner. A Development build gives the DCC its own
CheatManager (AddCheats), HUD and PlayerCameraManager when the camera goes ON.

  1. CollectGarbage x3 (so freed slots can put the DCC's objects BELOW the player's), then set_debug_camera ON.
  2. For CheatManager, PlayerController, HUD and PlayerCameraManager: every live object with its GObjects index and
     whether the DCC is it or owns it, and the mailbox FIND_INSTANCE pick (a lookup; nothing is invoked).
     PASS = the pick is not the DCC's while one that is not the DCC's is live. Where no DCC-owned object sits lower,
     the row is reported as non-discriminating: the old lowest-index pick would have been right too.
  3. invoke_function class_name="CheatManager" func_name="ToggleDebugCamera" while the camera is ON: instance_pick
     "live", the object is the player's, and on UE 5.8 the camera goes OFF (on 4.18-5.7 the player's toggle is a
     no-op there; the rig says which it saw).
  4. set_debug_camera OFF if the camera is still on.
"""
from __future__ import annotations

import argparse
import pathlib
import struct
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.stdout.reconfigure(encoding="utf-8", errors="replace")
from pipe_client import PipeClient  # noqa: E402
import mailbox_poke as MB  # noqa: E402

CMD_FIND_INSTANCE = 2
OFF_CLASSNAME = 0x028
CLASSES = ("CheatManager", "PlayerController", "HUD", "PlayerCameraManager")


def data(reply):
    return reply.get("data", reply)


def a2i(s) -> int:
    try:
        return int(str(s), 16)
    except ValueError:
        return 0


def find_instance(m, base, name, timeout=60.0):
    if m.i32(base + MB.OFF_STATUS) == MB.STATUS_PROCESSING:
        raise SystemExit("mailbox is PROCESSING before the command -- a previous one is wedged")
    m.write(base + MB.OFF_STATUS, struct.pack("<i", MB.STATUS_IDLE))
    raw = name.encode("utf-8")[:255]
    m.write(base + OFF_CLASSNAME, raw + b"\x00" * (256 - len(raw)))
    m.write(base + MB.OFF_INSTANCE, struct.pack("<Q", 0xDEADBEEF))
    m.write(base + MB.OFF_RESULT, struct.pack("<i", 0x7FFFFFFF))
    m.write(base + MB.OFF_CMD, struct.pack("<i", CMD_FIND_INSTANCE))
    t0 = time.time()
    while time.time() - t0 < timeout:
        if m.i32(base + MB.OFF_STATUS) == MB.STATUS_DONE:
            res, inst = m.i32(base + MB.OFF_RESULT), m.u64(base + MB.OFF_INSTANCE)
            m.write(base + MB.OFF_CMD, struct.pack("<i", MB.CMD_IDLE))
            return res, inst
        time.sleep(0.005)
    raise SystemExit(f"FIND_INSTANCE {name!r}: TIMEOUT")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--gc", type=int, default=3)
    ap.add_argument("--process", default="DumperTest58")
    a = ap.parse_args()
    pid = MB.pid_of(a.process)
    base = MB.mailbox_addr(str(pid))
    m = MB.Mem(pid)
    bad = 0

    with PipeClient(timeout=180) as pc:
        pc.connect()
        print("build:", pc.assert_build())
        pc.ensure_scanned()

        def dcc_set():
            rows = data(pc.request("find_instances", class_name="DebugCameraController", exact_match=False,
                                   limit=50)).get("instances") or []
            return {a2i(r["addr"]) for r in rows if not str(r.get("name", "")).startswith("Default__")}

        def owner_of(addr):
            w = data(pc.request("walk_instance", addr=addr, array_limit=0, preview_limit=0))
            for f in w.get("fields", []):
                if f.get("name") == "Owner":
                    return a2i(f.get("ptr", "0"))
            return 0

        chains = {}

        def chain_of(cls_addr):
            """The class's super-chain names (walk_class), cached per class."""
            if cls_addr not in chains:
                names, c = [], cls_addr
                for _ in range(24):
                    if not a2i(c):
                        break
                    k = data(pc.request("walk_class", addr=c)).get("class") or {}
                    if not k.get("name"):
                        break
                    names.append(k["name"])
                    c = k.get("super_addr", "")
                chains[cls_addr] = names
            return chains[cls_addr]

        def dcc_owned(row, dccs):
            addr = a2i(row["addr"])
            if addr in dccs:
                return "is the DCC"
            o = a2i(row.get("outer", "0"))
            for _ in range(16):   # the outer chain
                if not o:
                    break
                if o in dccs:
                    return "outer chain"
                o = a2i(data(pc.request("get_object", addr=hex(o))).get("outer_addr", "0"))
            ow = owner_of(row["addr"])
            for _ in range(16):   # the AActor::Owner chain
                if not ow:
                    break
                if ow in dccs:
                    return "owner chain"
                ow = owner_of(hex(ow))
            return ""

        for i in range(a.gc):
            r = data(pc.request("invoke_function", class_name="KismetSystemLibrary", func_name="CollectGarbage"))
            print(f"CollectGarbage #{i + 1}: result={r.get('result')}")
            time.sleep(3.0)
        print("set_debug_camera(1) ->", data(pc.request("set_debug_camera", enable=True)).get("state"))
        time.sleep(1.5)
        dccs = dcc_set()
        print(f"DCCs: {[hex(d) for d in dccs]}")

        for cls in CLASSES:
            # The pipe lists by class NAME; keep only the rows whose class chain really contains `cls`. A subclass
            # whose name lacks the word is missed here -- the listing is the report, not the check.
            rows = data(pc.request("find_instances", class_name=cls, exact_match=False, limit=500)).get("instances") or []
            live = [r for r in rows if not str(r.get("name", "")).startswith("Default__")
                    and cls in chain_of(r.get("class_addr", ""))]
            info = []
            for r in live:
                info.append((r.get("index", -1), r["name"], r.get("class", ""), a2i(r["addr"]), dcc_owned(r, dccs)))
            info.sort()
            res, pick = find_instance(m, base, cls)
            mine = [x for x in info if not x[4]]
            theirs = [x for x in info if x[4]]
            pick_row = next((x for x in info if x[3] == pick), None)
            ok = res == 0 and pick_row is not None and (not mine or not pick_row[4])
            lower = bool(mine and theirs and theirs[0][0] < mine[0][0])
            bad += not ok
            print(f"\n{cls}: {'OK ' if ok else 'BAD'}  pick -> {pick_row[1] if pick_row else hex(pick)}"
                  f"{' (' + pick_row[4] + ')' if pick_row and pick_row[4] else ''}   "
                  f"{'DISCRIMINATING: a DCC-owned one sits lower' if lower else 'non-discriminating'}")
            for x in info:
                print(f"     #{x[0]:<7} {x[1]:<40} {x[2]:<32} {x[4] or 'player'}")

        state_before = data(pc.request("get_debug_camera_state")).get("state")
        r = pc.request("invoke_function", class_name="CheatManager", func_name="ToggleDebugCamera")
        d = data(r)
        time.sleep(1.5)
        state_after = data(pc.request("get_debug_camera_state")).get("state")
        cm = next((row for row in (data(pc.request("find_instances", class_name="CheatManager", exact_match=True,
                                                    limit=50)).get("instances") or [])
                   if a2i(row["addr"]) == a2i(d.get("instance_addr", "0"))), None)
        owned = dcc_owned(cm, dccs) if cm else "?"
        ok = r.get("ok") and d.get("instance_pick") == "live" and not owned
        bad += not ok
        print(f"\nToggleDebugCamera by class name: {'OK ' if ok else 'BAD'}  on {d.get('instance_name')} "
              f"pick={d.get('instance_pick')!r} ({owned or 'player'})  camera {state_before} -> {state_after}"
              f"{'  (turned OFF: the UE 5.8 player toggle)' if state_before == 1 and state_after == 0 else ''}")

        if data(pc.request("get_debug_camera_state")).get("state") == 1:
            print("camera left ON -- forcing OFF:", data(pc.request("set_debug_camera", enable=False)).get("state"))

    print("RESULT:", "PASS" if bad == 0 else f"FAIL ({bad})")
    return 0 if bad == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
