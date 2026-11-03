#!/usr/bin/env python3
r"""[FINDINSTANCE-INTERNAL-SUBSTRING-FALLBACKS] The local-PlayerController fallback with the Debug Camera ON, live.

    py tools/verify/localpc_dbgcam_live.py [--process DumperTest58] [--timeout-ms 200]

Written for the row [FINDINSTANCE-INTERNAL-SUBSTRING-FALLBACKS] (docs/todo.md): the one case where the derivation scan
answers differently from the substring scan it replaced. With the Debug Camera on, LocalPlayers[0].PlayerController
IS the debug camera controller (DCC) -- pawnless -- and UPlayer::SwitchController has taken the local player away from
the original PlayerController. The old scan found no PC with a Player and fell back to the original one "WITHOUT
Player check"; the new one takes the ACTIVE DCC (it holds the LocalPlayer, as the intact chain gives it), and every
caller but Wirbel's POV hops it back to the original PC. localpc_fallback_live.py cannot run here: its baseline needs a
pawn on PC0, which a DCC has not.

PREREQUISITES: a fixture with a live CheatManager (DumperTest58 Development: `launch_dumpertest.py dev58`, injected),
in the level, the UI NOT connected, no Cheat Engine, no break of localpc_fallback_live.py left in effect.

WHAT IT WRITES: the Debug Camera is turned ON (set_debug_camera) and OFF again at the end; LocalPlayer.PlayerController
is set to 0 with ONLY the game thread suspended, its old value saved first to out/localpc_state.json (the same state
file and put-back as localpc_fallback_live.py, so `py tools/verify/localpc_fallback_live.py restore` undoes a crash).

CHECKS -- one line each, "<ID> OK|BAD  <detail>"; exit 0 only when every check that ran passed
  D0  set_debug_camera ON answers 1; LocalPlayers[0].PlayerController's class derives from DebugCameraController and
      its OriginalControllerRef is the original PC, which has a pawn
  K1 / K2  the break, exactly as localpc_fallback_live.py's
  D1  the DLL logs "Teleport: local PC <DCC> via instance scan (Player set)" after the break
  D2  ... and "Teleport: hopped DebugCameraController <DCC> -> original PC <ORIG>"
  D3  teleport_get_pose code 0 on the original PC's pawn
  D4  teleport_get_pov code 0 with the DCC's PlayerCameraManager (the POV keeps the on-screen controller on purpose)
  D5  get_current_target names the original PC's pawn
  R0  LocalPlayer.PlayerController is the DCC again before the thread runs; R9 it runs; R10 the invoke timeout is reset
  D6  set_debug_camera OFF answers 0
Every reply is saved under out/FINDINSTANCE-INTERNAL-SUBSTRING-FALLBACKS/.
"""
from __future__ import annotations

import argparse
import sys
import time
import traceback

import localpc_fallback_live as L

S, DCR = L.S, L.DCR
DCC_CLASS = "DebugCameraController"


def class_chain_names(rig, obj: int) -> list:
    names, c = [], rig.class_of(obj)
    for _ in range(64):   # bounded: a garbage super pointer must not loop forever
        if not c:
            break
        k = rig.req("walk_class", addr="%X" % c).get("class") or {}
        names.append(k.get("name"))
        c = L.h(k.get("super_addr"))
    return names


def hop_lines(rig, mark) -> list:
    folder = L.LOGS / DCR.process_folder_name(rig.image)
    out = []
    for p in sorted(folder.glob("*-0.log")) if folder.is_dir() else []:
        for line in p.read_text(encoding="utf-8", errors="replace").splitlines():
            m = DCR.LOG_TS.match(line)
            if m and m.group(1) >= mark.text and "hopped DebugCameraController" in line:
                out.append(line)
    return out


def run(rig, st) -> None:
    rig.phase("dbgcam")
    rig.open_pipe()
    if st.get("saved") or st.get("tid") or st.get("timeout_set"):
        raise L.RigError(f"{L.STATE} records a break still in effect -- run localpc_fallback_live.py restore first")
    on = rig.req("set_debug_camera", enable=True).get("state")
    time.sleep(1.0)
    world, gi, lp = rig.read_lp()
    lp_slot, dcc = rig.slot(lp, "PlayerController")
    chain = class_chain_names(rig, L.need(dcc, "LocalPlayer.PlayerController"))
    orig = rig.slot(dcc, "OriginalControllerRef")[1] if DCC_CLASS in chain else 0
    opawn = rig.slot(orig, "Pawn")[1] if orig else 0
    dcam = rig.slot(dcc, "PlayerCameraManager")[1]
    rig.v("D0", on == 1 and DCC_CLASS in chain and orig and opawn,
          f"set_debug_camera ON -> {on}; LocalPlayer.PlayerController={L.hx(dcc)} classes={chain[:4]} "
          f"OriginalControllerRef={L.hx(orig)} its Pawn={L.hx(opawn)} DCC camera manager={L.hx(dcam)}")
    if not (on == 1 and orig and opawn):
        return
    gt = rig.game_thread()
    st.clear()
    st.update(pid=rig.pid, image=rig.image, saved={},
              baseline={"lp": L.hx(lp), "lp_pc_slot": L.hx(lp_slot), "pc0": L.hx(dcc), "tid_main": L.game_tid(rig.pid),
                        "invoke_timeout_ms": gt.get("invoke_timeout_ms"), "hints": L.hint_timeouts()})
    L.save_state(st)
    if not L.phase_break(rig, st):
        return
    rig.phase("dbgcam-check")
    mark = L.Mark()
    pose = rig.req("teleport_get_pose")
    pov = rig.req("teleport_get_pov")
    tgt = rig.req("get_current_target")
    want = f"Teleport: local PC {L.hx(dcc)} via instance scan (Player set)"
    lines = L.wait_lines(rig, mark, [want])
    rig.v("D1", any(want in l for l in lines), f"{sum(want in l for l in lines)} x {want!r}")
    hop = f"hopped DebugCameraController {L.hx(dcc)} -> original PC {L.hx(orig)}"
    deadline = time.time() + L.LOG_WAIT_S
    hops = hop_lines(rig, mark)
    while not any(hop in l for l in hops) and time.time() < deadline:
        time.sleep(0.5)
        hops = hop_lines(rig, mark)
    rig.ev.save("hop_lines", {"since": mark.text, "wanted": hop, "lines": hops})
    rig.v("D2", any(hop in l for l in hops), f"{sum(hop in l for l in hops)} x 'Teleport: {hop}'")
    rig.v("D3", pose.get("code") == 0 and L.hx(pose.get("pawn_addr")) == L.hx(opawn),
          f"teleport_get_pose code={pose.get('code')} pawn={pose.get('pawn_addr')} (the original PC's {L.hx(opawn)})")
    rig.v("D4", pov.get("code") == 0 and L.hx(pov.get("cam_owner_addr")) == L.hx(dcam),
          f"teleport_get_pov code={pov.get('code')} camera manager={pov.get('cam_owner_addr')} (the DCC's {L.hx(dcam)})")
    rig.v("D5", L.hx(tgt.get("player_pawn")) == L.hx(opawn),
          f"get_current_target player_controller={tgt.get('player_controller')} player_pawn={tgt.get('player_pawn')}")


def restore(rig, st) -> None:
    """The pointer first, then the thread, then the timeout -- localpc_fallback_live.py's order, without its checks of a
    camera-off baseline. Nothing here waits on the pipe before the resume."""
    rig.phase("dbgcam-restore")
    base = st.get("baseline") or {}
    back = all([L.put_back(rig, st, k) for k in list(st.get("saved", {}))])
    if base:
        cur = rig.mem.u64(L.h(base["lp_pc_slot"]))
        rig.v("R0", back and L.hx(cur) == base["pc0"], f"LocalPlayer.PlayerController={L.hx(cur)} (the DCC {base['pc0']})")
    tid = st.get("tid")
    if tid:
        if back and S.act_tid(rig.image, tid, True) == 0:   # drains the suspend count to zero
            st.pop("tid")
            L.save_state(st)
        elif not back:
            print(f"!! tid {tid} stays SUSPENDED: the pointer is not back -- run "
                  "py tools/verify/localpc_fallback_live.py restore --force-resume after looking at it", flush=True)
            return
    if st.get("timeout_set"):
        rig.req("set_invoke_timeout", timeout_ms=0, persist=False)
        want = base.get("invoke_timeout_ms")
        if isinstance(want, int) and rig.game_thread().get("invoke_timeout_ms") != want and 100 <= want <= 600000:
            rig.req("set_invoke_timeout", timeout_ms=want, persist=False)
        st.pop("timeout_set")
        L.save_state(st)
    if tid:
        gt = rig.wait_liveness("responsive")
        rig.v("R9", L.liveness(gt) == "responsive", f"tid {tid} resumed; liveness={L.liveness(gt)!r}")
    if base:
        got = rig.game_thread().get("invoke_timeout_ms")
        rig.v("R10", got == base.get("invoke_timeout_ms") and L.hint_timeouts() == base.get("hints", {}),
              f"invoke timeout {got} ms (before {base.get('invoke_timeout_ms')})")
    off = rig.req("set_debug_camera", enable=False).get("state")
    rig.v("D6", off == 0, f"set_debug_camera OFF -> {off}")
    st.clear()
    L.save_state(st)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--process", default="DumperTest58")
    ap.add_argument("--timeout-ms", type=int, default=200)
    a = ap.parse_args()
    a.force_resume = False
    rig = L.Rig(a)
    st = L.load_state()
    try:
        try:
            run(rig, st)
        finally:
            restore(rig, st)
    except Exception as e:   # noqa: BLE001
        rig.ev.save("traceback", {"traceback": traceback.format_exc()})
        rig.v("X", False, f"aborted: {type(e).__name__}: {e}")
    bad = rig.v.bad
    print(f"RESULT: {'PASS' if not bad and rig.v.rows else 'FAIL'} ({len(rig.v.rows)} check(s), {bad} BAD) "
          f"on build {rig.build or '?'}")
    return 0 if rig.v.rows and not bad else 1


if __name__ == "__main__":
    sys.exit(main())
