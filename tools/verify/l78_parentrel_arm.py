#!/usr/bin/env python3
r"""L78 `[A2-CABI-TELEPORT-PARENTREL]`: arm and disarm a PARENT-RELATIVE pose on DumperTest, over the pipe.

    py tools/verify/l78_parentrel_arm.py find
    py tools/verify/l78_parentrel_arm.py attach  [--pawn 0x..] [--parent 0x..]
    py tools/verify/l78_parentrel_arm.py pose
    py tools/verify/l78_parentrel_arm.py degrade [--timeout-ms 200]
    py tools/verify/l78_parentrel_arm.py heal
    py tools/verify/l78_parentrel_arm.py marker  --slot N
    py tools/verify/l78_parentrel_arm.py detach  [--pawn 0x..]
    py tools/verify/l78_parentrel_arm.py restore

DumperTest has no vehicle, mount or moving platform, so both halves of Wirbel.cpp's condition are MANUFACTURED,
as L27 and L37 did (todo.md). GetPoseImpl reports a pose as parent-relative only when the pawn's root is attached
(`attached`, Wirbel.cpp:425) AND the world read `K2_GetActorLocation` failed (:459), after which it falls back to
RelativeLocation.
  * attach  -- `K2_AttachToActor` on the live BP_ThirdPersonCharacter_C onto a live StaticMeshActor, all three
               EAttachmentRule bytes KeepWorld (1), weld 0. The parameter layout is READ from `walk_functions` on
               /Script/Engine.Actor, never assumed. The ReturnValue byte must be 01. DumperTestActor has no root
               component and returns 00, so it is never the parent.
  * degrade -- `set_invoke_timeout {timeout_ms:200, persist:false}`, then suspend ONLY the UE game thread (the
               process main thread, the earliest created). Confirmed by EFFECT: get_diagnostics' game_thread must go
               unresponsive while the pipe still answers. A 100 ms timeout would make healthy invokes fail at this
               fixture's 15 FPS, so it is refused.
  * heal    -- resume that thread (drained to zero) and re-read diagnostics.
  * detach  -- `K2_DetachFromActor` with EDetachmentRule KeepWorld x3 (it returns void).
  * restore -- `set_invoke_timeout {timeout_ms:0}` (back to the default) and print the hint cache's sha256 and any
               invokeTimeoutMs it holds, so a persisted timeout cannot slip through.
State (pawn, parent, suspended tid) lives in out/l78_state.json, so the verbs can run as separate steps around CE.
Keep the UI CLOSED while this runs: it would take two of the three pipe slots. It never teleports.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import pathlib
import sys

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.stdout.reconfigure(encoding="utf-8", errors="replace")
from pipe_client import PipeClient  # noqa: E402
import suspend as S  # noqa: E402

STATE = HERE.parents[1] / "out" / "l78_state.json"
PROC = "DumperTest-Win64-Shipping"
HINTS = pathlib.Path(os.environ["LOCALAPPDATA"]) / "UE5CEDumper" / (
    "UE5CEDumper.%s.json" % os.environ.get("COMPUTERNAME", ""))


def load_state():
    return json.loads(STATE.read_text(encoding="utf-8")) if STATE.exists() else {}


def save_state(st):
    STATE.parent.mkdir(parents=True, exist_ok=True)
    STATE.write_text(json.dumps(st, indent=1), encoding="utf-8")


def live(c, cls):
    r = c.request("find_instances", class_name=cls, max_results=200)
    out = []
    for i in r.get("instances", []):
        name = i.get("name", "")
        if not name.startswith("Default__"):
            out.append((i.get("addr"), name))
    return out


def actor_funcs(c):
    cls = c.request("find_object", path="/Script/Engine.Actor")
    addr = cls.get("addr") or cls.get("address")
    if not addr:
        raise SystemExit("find_object /Script/Engine.Actor gave no address: %r" % cls)
    fs = c.request("walk_functions", addr=addr).get("functions", [])
    return {f["name"]: f for f in fs}


def layout(f):
    return ", ".join("%s@%d/%d%s" % (p["name"], p["offset"], p["size"], " ret" if p.get("ret") else "")
                     for p in f.get("params", []))


def build(f, values):
    buf = bytearray(f["parms_size"])
    for p in f.get("params", []):
        if p["name"] in values:
            v = values[p["name"]]
            buf[p["offset"]:p["offset"] + p["size"]] = v.to_bytes(p["size"], "little")
    return buf


def invoke(c, pawn, f, values):
    buf = build(f, values)
    r = c.request("invoke_function", instance_addr=pawn, func_name=f["name"],
                  parms_size=f["parms_size"], params_hex=buf.hex())
    print("%s -> result=%s message=%s" % (f["name"], r.get("result"), r.get("message")))
    return r


def pose(c):
    r = c.request("teleport_get_pose")
    keys = {k: r.get(k) for k in ("code", "source", "parent_relative", "x", "y", "z", "pitch", "yaw", "roll",
                                  "map", "pawn_addr") if k in r}
    print("teleport_get_pose:", json.dumps(keys))
    return r


def game_thread(c):
    gt = c.request("get_diagnostics").get("game_thread", {})
    print("game_thread: liveness=%s responsive=%s hook_active=%s hook_fire_count=%s" % (
        gt.get("liveness"), gt.get("responsive"), gt.get("hook_active"), gt.get("hook_fire_count")))
    return gt


def main_tid():
    pids = S.pids(PROC)
    if len(pids) != 1:
        raise SystemExit("expected one %s process, found %r" % (PROC, pids))
    pid = pids[0][0]
    rows = sorted(((S.thread_times(t) or (1 << 63, 0))[0], t) for t in S.thread_ids(pid))
    return pid, rows[0][1]


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("verb", choices=["find", "attach", "pose", "degrade", "heal", "marker", "detach", "restore"])
    ap.add_argument("--pawn")
    ap.add_argument("--parent")
    ap.add_argument("--slot", type=int)
    ap.add_argument("--timeout-ms", type=int, default=200)
    a = ap.parse_args()
    st = load_state()

    if a.verb == "heal":
        tid = st.get("tid")
        if not tid:
            raise SystemExit("no suspended tid in %s" % STATE)
        rc = S.act_tid(PROC, tid, True)
        if rc == 0:
            st.pop("tid", None)
            save_state(st)
        with PipeClient() as c:
            game_thread(c)
        return rc

    with PipeClient() as c:
        if a.verb == "find":
            pawns, meshes = live(c, "BP_ThirdPersonCharacter_C"), live(c, "StaticMeshActor")
            print("pawns:", pawns)
            print("StaticMeshActors (%d): %s" % (len(meshes), meshes[:6]))
            fs = actor_funcs(c)
            for n in ("K2_AttachToActor", "K2_DetachFromActor"):
                f = fs.get(n)
                print("%s: %s" % (n, "MISSING" if not f else "parms_size=%d  %s" % (f["parms_size"], layout(f))))
            if pawns and meshes:
                st.update(pawn=pawns[0][0], parent=meshes[0][0])
                save_state(st)
                print("state:", st)
            return 0

        if a.verb == "attach":
            pawn, parent = a.pawn or st.get("pawn"), a.parent or st.get("parent")
            f = actor_funcs(c)["K2_AttachToActor"]
            print("K2_AttachToActor layout:", layout(f))
            r = invoke(c, pawn, f, {"ParentActor": int(parent, 16), "SocketName": 0, "LocationRule": 1,
                                    "RotationRule": 1, "ScaleRule": 1, "bWeldSimulatedBodies": 0})
            ret = next((p for p in f["params"] if p.get("ret")), None)
            hx = bytes.fromhex(r.get("result_hex", ""))
            rv = hx[ret["offset"]] if ret and len(hx) > ret["offset"] else None
            print("result_hex=%s  ReturnValue=%s" % (r.get("result_hex"), rv))
            st.update(pawn=pawn, parent=parent, attached=(rv == 1))
            save_state(st)
            return 0 if rv == 1 else 1

        if a.verb == "pose":
            pose(c)
            return 0

        if a.verb == "degrade":
            if a.timeout_ms < 150:
                raise SystemExit("refusing timeout %d ms: healthy invokes fail at 15 FPS below ~150 ms" % a.timeout_ms)
            game_thread(c)
            print("set_invoke_timeout:", json.dumps(c.request("set_invoke_timeout", timeout_ms=a.timeout_ms,
                                                              persist=False)))
            pid, tid = main_tid()
            print("pid %d main thread tid %d" % (pid, tid))
            st["tid"] = tid
            save_state(st)
            rc = S.act_tid(PROC, tid, False)
            if rc != 0:
                st.pop("tid", None)
                save_state(st)
                return rc
            import time
            time.sleep(1.5)
            gt = game_thread(c)
            if gt.get("responsive") is not False:
                print("⚠ the game thread still reads responsive -- NOT degraded; run `heal`")
                return 1
            return 0

        if a.verb == "marker":
            print("teleport_save_marker:", json.dumps(c.request("teleport_save_marker", slot=a.slot)))
            return 0

        if a.verb == "detach":
            pawn = a.pawn or st.get("pawn")
            f = actor_funcs(c)["K2_DetachFromActor"]
            print("K2_DetachFromActor layout:", layout(f))
            invoke(c, pawn, f, {"LocationRule": 1, "RotationRule": 1, "ScaleRule": 1})
            st["attached"] = False
            save_state(st)
            return 0

        if a.verb == "restore":
            print("set_invoke_timeout 0:", json.dumps(c.request("set_invoke_timeout", timeout_ms=0)))
            raw = HINTS.read_bytes()
            print("hint cache %s sha256 %s" % (HINTS.name, hashlib.sha256(raw).hexdigest()[:8]))
            for k, g in json.loads(raw).get("games", {}).items():
                if g.get("invokeTimeoutMs"):
                    print("  persisted invokeTimeoutMs: %s = %s (%s)" % (k, g["invokeTimeoutMs"],
                                                                         g.get("invokeTimeoutMsAt")))
            return 0
    return 0


if __name__ == "__main__":
    sys.exit(main())
