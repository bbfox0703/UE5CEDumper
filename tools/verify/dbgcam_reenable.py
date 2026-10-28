"""[DBGCAM-58-REENABLE] Debug Camera Force ON -> OFF -> ON on a live game, under the condition that broke it.

    py tools/verify/dbgcam_reenable.py              # 3 GCs, then ON/OFF/ON/OFF through set_debug_camera
    py tools/verify/dbgcam_reenable.py --gc 0       # no GC first (the ordering usually stays harmless)
    py tools/verify/dbgcam_reenable.py --cycles 10  # more ON/OFF pairs after the first

Headless, through the pipe: the UI must NOT be connected (pipe_client's own rule). Needs a game with a live
UCheatManager -- a Development build (DumperTest58 Development: `launch_dumpertest.py dev58`, then `inject.py`).
Ends with the camera OFF.

WHAT IT MEASURES. The first ON spawns a DebugCameraController, and ADebugCameraController::AddCheats forces a
CheatManager onto it in non-Shipping builds, so two CheatManagers are live afterwards. GObjects hands out freed slots
before new ones, so a GC between the player's CheatManager being created and the first ON lets the DCC's take a LOWER
index. Before the fix, the Debug Camera picked the lowest-index CheatManager, landed on the DCC's, and on UE 5.8 a
ToggleDebugCamera there never enables the camera (FDebugCameraManager::EnableDebugCamera returns at once -- the DCC
has no Player): the second ON stayed 0. The GC is requested through UKismetSystemLibrary::CollectGarbage, which
takes no parameters. The script prints both CheatManagers' indices after each step -- the DCC's below the player's
is the condition -- and says whether the second ON worked.

Measured 2026-10-29 on DumperTest58 Development: DCC's CheatManager 34955 against the player's 35796; build 3662
gave ON/OFF/ON = 1, 0, 0, build 3663 gave 1, 0, 1. A -1 (SEH -4 in the DLL log, with the game's own log showing an
`IsInGameThread` assertion) is the separate [STARK-DRAIN-OFF-GAME-THREAD] lead, not this bug.
"""
import argparse
import pathlib
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient  # noqa: E402


def data(reply):
    return reply.get("data", reply)


def cheat_manager_indices(pc):
    """(player-owned indices, DCC-owned indices) of the live CheatManagers."""
    rows = data(pc.request("find_instances", class_name="CheatManager", exact_match=True, limit=50))["instances"]
    dccs = {x["addr"] for x in data(pc.request("find_instances", class_name="DebugCameraController",
                                               exact_match=True, limit=50))["instances"]}
    live = [x for x in rows if not x["name"].startswith("Default__")]
    return ([x["index"] for x in live if x["outer"] not in dccs], [x["index"] for x in live if x["outer"] in dccs])


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--gc", type=int, default=3, help="CollectGarbage rounds before the first ON")
    ap.add_argument("--wait", type=float, default=3.0, help="seconds after each GC request")
    ap.add_argument("--cycles", type=int, default=0, help="extra ON/OFF pairs after the first two")
    a = ap.parse_args()
    with PipeClient() as pc:
        pc.connect()
        print("build:", pc.assert_build())
        pc.ensure_scanned()
        print("CheatManagers (player, DCC-owned):", cheat_manager_indices(pc))
        for i in range(a.gc):
            r = data(pc.request("invoke_function", class_name="KismetSystemLibrary", func_name="CollectGarbage",
                                parms_size=0))
            print(f"CollectGarbage #{i + 1}: {r.get('message')} result={r.get('result')}")
            time.sleep(a.wait)
        states = []
        for want in [1, 0, 1, 0] + [1, 0] * a.cycles:
            st = data(pc.request("set_debug_camera", enable=bool(want))).get("state")
            time.sleep(1.0)
            states.append((want, st))
            print(f"set_debug_camera({want}) -> {st}   CheatManagers:", cheat_manager_indices(pc))
        player, dcc = cheat_manager_indices(pc)
        lower = bool(player and dcc and min(dcc) < min(player))
        print(f"\ncondition (the DCC's CheatManager below the player's): {'MET' if lower else 'not met'}")
        print("second ON worked" if states[2][1] == 1 else f"second ON FAILED (state {states[2][1]})")
        if data(pc.request("get_debug_camera_state")).get("state") == 1:
            print("camera left ON -- forcing OFF:", data(pc.request("set_debug_camera", enable=False)).get("state"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
