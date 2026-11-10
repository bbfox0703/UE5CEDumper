#!/usr/bin/env python3
r"""[FINDINSTANCE-INTERNAL-SUBSTRING-FALLBACKS] The local-PlayerController and WorldSettings fallbacks, live, headless.

    py tools/verify/localpc_fallback_live.py all       [--process DumperTest] [--timeout-ms 200]
    py tools/verify/localpc_fallback_live.py baseline  # chain intact: record PC0 / WS0 and every module's reply
    py tools/verify/localpc_fallback_live.py break     # freeze the game thread, LocalPlayer.PlayerController = 0
    py tools/verify/localpc_fallback_live.py check     # re-read every module; then the WorldSettings sub-step
    py tools/verify/localpc_fallback_live.py restore   [--force-resume]

Written for the row [FINDINSTANCE-INTERNAL-SUBSTRING-FALLBACKS] (docs/todo.md). Seven DLL modules resolve the local
PlayerController through GWorld -> OwningGameInstance -> LocalPlayers[0] -> PlayerController and, only when that chain
gives none, fall back to Aura::ScanForLocalPlayerController (by derivation; the LocalPlayer-owned controller first).
Hemmung's world dilation resolves GWorld -> PersistentLevel -> WorldSettings and falls back to a derivation walk of
AWorldSettings. A healthy game never breaks either chain, so the rig breaks it in game memory where the engine cannot
see it: ONLY the game thread is suspended (the pipe and the mailbox keep answering), the pointer is written to 0, the
modules are read over the pipe, and the pointer is put back before the thread runs again. The verbs are separate steps
(the state lives in out/localpc_state.json) and each one can be run again.

PREREQUISITES
  * DumperTest SHIPPING: `py tools/verify/launch_dumpertest.py shipping`, then `py tools/verify/inject.py --name
    DumperTest`; in the level, the character possessed (not a menu). Another fixture: --process <image prefix>.
  * dist\build_number.txt names the DLL under test, built from the row's green commit or later (assert_build refuses
    any other DLL answering the pipe).
  * The UI must NOT be connected to a DLL from before [PIPE-PER-PROCESS-2026-11-06] Stage B (it holds two of that DLL's three pipe
    instances; 6 since). No Cheat Engine.
  * One injected game at a time; kill the game when the row is done (handover section 4).

WHAT IT WRITES: LocalPlayer.PlayerController and PersistentLevel.WorldSettings, each to 0, and nothing else in game
memory. Each old value is saved to the state file BEFORE its write. `restore` writes them back with WriteProcessMemory
(no pipe needed) BEFORE it resumes the thread, and only over the 0 this rig wrote -- so after a crash, a hang (kill it
and run `restore` from another shell) or a Ctrl+C, `restore` undoes everything. The invoke timeout is lowered with
persist:false while the thread is frozen (an invoke-backed read would otherwise wait out the 5 s default) and set back.
The engine's worker threads keep running while the game thread is frozen, so the WorldSettings window is held to one
get_time_state. One read-only K2_GetActorLocation is sent before the suspend when no ProcessEvent hook is installed yet:
without one get_diagnostics cannot tell a frozen game thread from a healthy one.

CHECKS -- one line each, "<ID> OK|BAD  <detail>"; the exit code is 0 only when every check that ran passed
  Module reads, numbered alike in each phase (B = chain intact, C = chain broken, R = after restore):
    1 teleport_get_pose   (Wirbel)  code 0, pawn_addr = PAWN0, rot_owner_addr (ControlRotation's owner) = PC0
    2 teleport_get_pov    (Wirbel)  code 0, hasPawn, cam_owner_addr = PC0.PlayerCameraManager
    3 get_current_target  (Edel)    player_controller = PC0, player_pawn = PAWN0
    4 get_movement_params (Laufen)  code 0, cmc_addr = PAWN0.CharacterMovement
    5 get_time_state      (Hemmung) code 0, dilation.global owner = WS0, dilation.pawn owner = PAWN0
    6 get_god_mode        (Solitar) B: state 0 or 1; C, R: the baseline's state
    7 find_stealth_meter  (Solide)  B: code 0; C, R: code 0 and the baseline's (owner, field) candidates
    8 fly_get_state       (Dunste)  B, R: code 0, cmc_addr = PAWN0.CharacterMovement. C: code -3 (no pawn): the polled
                                    status passes allowScan=false, so a broken chain must NOT fall back to a scan
  baseline  B0  the rig's own chain read resolves every link (offsets from walk_class up the super chain, bytes from
                read_mem) -- PC0, PAWN0, WS0 above are ITS values, never a module's
            B9  the DLL log since the reads: "Teleport: local PC <PC0> via GWorld chain" and the Edel line, and no
                instance-scan pick at all
  break     K1  the game thread's CPU time stands still while get_diagnostics answers with liveness "stalled"
            K2  LocalPlayer.PlayerController read PC0 before the write and reads 0 after it (ReadProcessMemory)
  check     C0  the pointer still reads 0 and the thread is still frozen (else C1..C8 would prove nothing)
            L1  "Teleport: local PC <PC0> via instance scan (Player set)" logged after the reads began
            L2  the same line from Edel
            L3  no other PC-resolution line since then: no remote / WITHOUT-a-Player / other-PC pick, no GWorld chain
            W1  PersistentLevel.WorldSettings read WS0 before the write and reads 0 after it
            W2  get_time_state with it broken: code 0, dilation.global resolved and owned by WS0
            W3  put back at once: it reads WS0
  restore   R0  both pointers read their baseline values
            R9  the game thread runs again: CPU time advances, liveness "responsive"
            R10 the invoke timeout is the baseline's, and the hint cache's persisted invokeTimeoutMs are unchanged
            R11 after R1..R8, with the game thread running, both pointers still read their baseline values
  An aborted phase prints "<P>X BAD aborted: ..." (P = B, K, C, R). A separate verb that leaves a break in place
  prints "STILL IN EFFECT (...)" before its RESULT line.

NOT PROVEN HERE: on DumperTest the substring scan this row replaced would have picked the same controller, so this shows
the derivation walk running live, picking the LocalPlayer-owned controller, and every module accepting it; the ranking
itself is pinned by dll_core_test LOCALPC. Dunste's scan runs only on the fly_set enable path, which writes the pawn's
movement mode, so only its gate (C8) is read. Laufen, Solitar, Solide and Hemmung log no pick: their replies are the
evidence. Every reply is saved verbatim under out/FINDINSTANCE-INTERNAL-SUBSTRING-FALLBACKS/ (working-lessons 1.ad).
"""
from __future__ import annotations

import argparse
import collections
import datetime as dt
import json
import os
import pathlib
import re
import struct
import sys
import time
import traceback

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.stdout.reconfigure(encoding="utf-8", errors="replace")
from pipe_client import PipeError  # noqa: E402
import dbgcam_cdo_refusal as DCR  # noqa: E402  (RecordingClient, Verdicts, resolve_process, main_tid, game_thread)
import l78_parentrel_arm as L78  # noqa: E402  (the per-machine hint cache)
import mailbox_poke as MB  # noqa: E402  (Mem: ReadProcessMemory / WriteProcessMemory)
import suspend as S  # noqa: E402

TAG = "FINDINSTANCE-INTERNAL-SUBSTRING-FALLBACKS"
EVID = HERE.parents[1] / "out" / TAG
STATE = HERE.parents[1] / "out" / "localpc_state.json"
LOGS = pathlib.Path(os.environ.get("LOCALAPPDATA", "")) / "UE5CEDumper" / "Logs"

FLY_NO_PAWN = -3          # Dunste::FR_ERR_NO_PAWN
MIN_TIMEOUT_MS = 150      # l78: healthy invokes fail at the fixture's 15 FPS below ~150 ms
CPU_WINDOW_S = 1.5        # several scheduler ticks: a running game thread always accrues CPU time across it
LIVENESS_WAIT_S = 6.0
LOG_WAIT_S = 5.0          # a log line can reach the disk after the reply that caused it
PC_LINE = re.compile(r"\bPC 0x[0-9A-F]+ via (instance scan|GWorld chain)")


class RigError(RuntimeError):
    pass


def h(v) -> int:
    """An address from a reply or the state ("0x1A0..", "1A0..", "0x0", "", None) as an int; 0 when unparsable."""
    if isinstance(v, int):
        return v
    try:
        return int(str(v), 16) if v not in (None, "") else 0
    except ValueError:
        return 0


def hx(v) -> str:
    """The DLL's own spelling (Renge::AddrToStr, and its log lines): 0x + upper-case hex, so strings compare."""
    return "0x%X" % h(v)


def need(v: int, what: str) -> int:
    if not v:
        raise RigError(f"{what} is null")
    return v


def load_state() -> dict:
    return json.loads(STATE.read_text(encoding="utf-8")) if STATE.exists() else {}


def save_state(st: dict) -> None:
    # Replaced whole, never rewritten in place: this file is the only record of the values `restore` must put back,
    # and a rig killed mid-write must not leave half of it.
    STATE.parent.mkdir(parents=True, exist_ok=True)
    tmp = STATE.with_name(STATE.name + ".tmp")
    with open(tmp, "w", encoding="utf-8", newline="\n") as f:
        json.dump(st, f, indent=1)
        f.write("\n")
    os.replace(tmp, STATE)


def hint_timeouts() -> dict:
    """The per-machine hint cache's persisted invoke timeouts; persist:false must leave them as they were."""
    try:
        games = json.loads(L78.HINTS.read_bytes()).get("games", {})
    except (OSError, ValueError):
        return {}
    return {k: g["invokeTimeoutMs"] for k, g in games.items() if isinstance(g, dict) and g.get("invokeTimeoutMs")}


def liveness(gt: dict) -> str:
    return gt.get("liveness") or ("stalled" if gt.get("responsive") is False else "unknown")


def cpu_window(tid):
    """(CPU ms before, after) across CPU_WINDOW_S: equal for a frozen thread, rising for a running one."""
    def cpu():
        t = S.thread_times(tid)
        return None if t is None else t[1]
    a = cpu()
    time.sleep(CPU_WINDOW_S)
    return a, cpu()


class Evidence:
    """Every reply is saved as it is read, named by phase: stdout is not evidence (working-lessons 1.ad)."""

    def __init__(self):
        self.stamp = dt.datetime.now().strftime("%Y%m%d-%H%M%S")
        self.seq = 0
        self.phase = "init"

    def save(self, label, obj):
        self.seq += 1
        EVID.mkdir(parents=True, exist_ok=True)
        name = re.sub(r"[^A-Za-z0-9_.-]+", "_", f"{self.phase}_{label}")
        path = EVID / f"{self.stamp}_{self.seq:03d}_{name}.json"
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            json.dump({"saved_at": dt.datetime.now().isoformat(timespec="milliseconds"), **obj}, f,
                      ensure_ascii=False, indent=1)
            f.write("\n")
        return path


class Mark:
    """A moment as the DLL stamps its log lines (local time, milliseconds), and as an epoch for file mtimes."""

    def __init__(self):
        self.t = dt.datetime.now()
        self.text = self.t.strftime("%Y-%m-%d %H:%M:%S.") + f"{self.t.microsecond // 1000:03d}"


class Rig:
    def __init__(self, args):
        self.args = args
        self.ev = Evidence()
        self.v = DCR.Verdicts()
        self.pc = None
        self.pid = self.image = self.mem = None
        self.build = ""
        self.gworld = 0
        self.offs: dict = {}

    def phase(self, name):
        self.ev.phase = name
        print(f"== {name}", flush=True)

    # -- binding to one process ------------------------------------------------------------------------------------
    def bind(self, pid, image):
        # The thread frozen, the memory written and the DLL read must all be one process.
        if self.pid not in (None, pid):
            raise RigError(f"pid {pid} is not the process this run is bound to ({self.pid})")
        if self.pid is None:
            self.pid, self.image, self.mem = pid, image, MB.Mem(pid)
            print(f"process: {image} pid {pid}", flush=True)

    def bind_from_state(self, st):
        pid, image = st.get("pid"), st.get("image")
        if not pid or not image:
            raise RigError(f"{STATE} names no process")
        if not any(p == pid for p, _ in S.pids(image)):
            raise RigError(f"pid {pid} ({image}) from {STATE} is not running: its saved pointers mean nothing now")
        self.bind(pid, image)

    def open_pipe(self):
        if self.pc is not None:
            return
        pc = DCR.RecordingClient(self.ev, timeout=180).connect()
        try:
            self.build = pc.assert_build()
            print("build:", self.build, flush=True)
            ptrs = pc.ensure_scanned()
            try:
                pid, image = DCR.resolve_process(self.args.process, ptrs.get("pid"))
            except SystemExit as e:
                raise RigError(str(e)) from None
            self.bind(pid, image)
        except BaseException:
            pc.close()
            raise
        self.pc = pc
        self.gworld = h(ptrs.get("gworld"))

    def req(self, cmd, **kw) -> dict:
        return DCR.data(self.pc.request(cmd, **kw))

    # -- the rig's own chain read: reflection offsets from walk_class, bytes from read_mem ------------------------
    def read_ptr(self, at: int) -> int:
        r = self.req("read_mem", addr="%X" % at, size=8)
        if not r.get("bytes"):
            raise RigError(f"read_mem {at:#x}: {r.get('error')}")
        return struct.unpack("<Q", bytes.fromhex(r["bytes"]))[0]

    def class_of(self, obj: int) -> int:
        return need(h(self.req("get_object", addr="%X" % obj).get("class_addr")), f"the class of {obj:#x}")

    def field_off(self, cls: int, name: str) -> int:
        """Searched up the super chain: a field is listed on the class that declares it."""
        key = (cls, name)
        c, hops = cls, 0
        while key not in self.offs and c and hops < 64:   # bounded: a garbage super pointer must not loop forever
            hops += 1
            k = self.req("walk_class", addr="%X" % c).get("class") or {}
            f = next((f for f in k.get("fields") or [] if f.get("name") == name), None)
            if f is not None:
                self.offs[key] = int(f["offset"])
            c = h(k.get("super_addr"))
        if key not in self.offs:
            raise RigError(f"no field {name!r} on class {cls:#x} or any super of it")
        return self.offs[key]

    def slot(self, obj: int, name: str):
        """(where obj.name lives, the pointer stored there)."""
        at = obj + self.field_off(self.class_of(obj), name)
        return at, self.read_ptr(at)

    def read_lp(self):
        world = need(self.read_ptr(need(self.gworld, "get_pointers.gworld")), "*GWorld")
        gi = need(self.slot(world, "OwningGameInstance")[1], "World.OwningGameInstance")
        arr = gi + self.field_off(self.class_of(gi), "LocalPlayers")
        r = self.req("read_mem", addr="%X" % arr, size=16)
        if not r.get("bytes"):
            raise RigError(f"read_mem {arr:#x}: {r.get('error')}")
        data, num = struct.unpack_from("<Qi", bytes.fromhex(r["bytes"]))
        if num <= 0 or not data:
            raise RigError(f"GameInstance.LocalPlayers is empty (data {data:#x}, num {num})")
        return world, gi, need(self.read_ptr(data), "LocalPlayers[0]")

    def read_chain(self) -> dict:
        world, gi, lp = self.read_lp()
        lp_slot, pc0 = self.slot(lp, "PlayerController")
        need(pc0, "LocalPlayer.PlayerController")
        pawn0 = need(self.slot(pc0, "Pawn")[1], "PlayerController.Pawn")
        cam0 = need(self.slot(pc0, "PlayerCameraManager")[1], "PlayerController.PlayerCameraManager")
        cmc0 = need(self.slot(pawn0, "CharacterMovement")[1], "Pawn.CharacterMovement")
        level = need(self.slot(world, "PersistentLevel")[1], "World.PersistentLevel")
        ws_slot, ws0 = self.slot(level, "WorldSettings")
        need(ws0, "Level.WorldSettings")
        return {k: hx(v) for k, v in dict(world=world, gi=gi, lp=lp, lp_pc_slot=lp_slot, pc0=pc0, pawn0=pawn0,
                                          cam0=cam0, cmc0=cmc0, level=level, ws_slot=ws_slot, ws0=ws0).items()}

    # -- writes ----------------------------------------------------------------------------------------------------
    def pipe_write_ptr(self, at: int, value: int) -> int:
        r = self.pc.request("write_mem", addr="%X" % at, bytes=struct.pack("<Q", value).hex().upper())
        if not r.get("ok"):
            raise RigError(f"write_mem {at:#x}: {r.get('error')}")
        return self.mem.u64(at)   # read back with ReadProcessMemory: a witness that is not the DLL

    def game_thread(self) -> dict:
        return DCR.game_thread(self.pc)

    def wait_liveness(self, want: str) -> dict:
        deadline = time.time() + LIVENESS_WAIT_S
        while True:
            gt = self.game_thread()
            if liveness(gt) == want or time.time() >= deadline:
                return gt
            time.sleep(0.5)


def put_back(rig: Rig, st: dict, key: str) -> bool:
    """Write a saved pointer back -- only over the 0 this rig wrote, never over a value something else put there.
    WriteProcessMemory, not the pipe: it must work when the pipe or the DLL is what failed."""
    e = st["saved"][key]
    at, old = h(e["slot"]), h(e["old"])
    cur = rig.mem.u64(at)
    if cur == 0:
        rig.mem.write(at, struct.pack("<Q", old))
        cur = rig.mem.u64(at)
    ok = cur == old
    if ok:
        del st["saved"][key]
        save_state(st)
    print(f"    {key} @{hx(at)} reads {hx(cur)}, saved {hx(old)}"
          + ("" if ok else " -- NOT put back: it is neither 0 nor the saved value"), flush=True)
    return ok


# -- the module reads ----------------------------------------------------------------------------------------------
def module_reads(rig: Rig) -> dict:
    pose = rig.req("teleport_get_pose")
    pov = rig.req("teleport_get_pov")
    tgt = rig.req("get_current_target", max_candidates=8)
    mv = rig.req("get_movement_params")
    ts = rig.req("get_time_state")
    god = rig.req("get_god_mode")
    sm = rig.req("find_stealth_meter", max=8)
    fly = rig.req("fly_get_state")
    dil = ts.get("dilation") or {}
    return {
        "pose": {"code": pose.get("code"), "pawn": hx(pose.get("pawn_addr")), "ctrl": hx(pose.get("rot_owner_addr"))},
        "pov": {"code": pov.get("code"), "has_pawn": pov.get("hasPawn"), "cam": hx(pov.get("cam_owner_addr"))},
        "target": {"pc": hx(tgt.get("player_controller")), "pawn": hx(tgt.get("player_pawn"))},
        "move": {"code": mv.get("code"), "cmc": hx(mv.get("cmc_addr"))},
        "time": {"code": ts.get("code"), "world": hx((dil.get("global") or {}).get("owner_addr")),
                 "pawn": hx((dil.get("pawn") or {}).get("owner_addr"))},
        "god": {"state": god.get("state")},
        "stealth": {"code": sm.get("code"),
                    "cands": sorted([hx(c.get("owner_addr")), str(c.get("field_name"))]
                                    for c in sm.get("candidates") or [])},
        "fly": {"code": fly.get("code"), "cmc": hx(fly.get("cmc_addr"))},
    }


def module_checks(rig: Rig, p: str, now: dict, base: dict) -> None:
    """One verdict per module. The addresses wanted are the rig's own chain read; in C and R the replies taken with
    the baseline are wanted too, for what a chain read cannot give (god mode, the stealth candidates)."""
    pc0, pawn0, cam0, cmc0, ws0 = (base[k] for k in ("pc0", "pawn0", "cam0", "cmc0", "ws0"))
    ref = base.get("reads") if p != "B" else None
    x = now["pose"]
    rig.v(f"{p}1", x["code"] == 0 and x["pawn"] == pawn0 and x["ctrl"] == pc0,
          f"teleport_get_pose code={x['code']} pawn={x['pawn']} controller={x['ctrl']} (PAWN0 {pawn0}, PC0 {pc0})")
    x = now["pov"]
    rig.v(f"{p}2", x["code"] == 0 and x["has_pawn"] is True and x["cam"] == cam0,
          f"teleport_get_pov code={x['code']} hasPawn={x['has_pawn']} camera manager={x['cam']} (PC0's {cam0})")
    x = now["target"]
    rig.v(f"{p}3", x["pc"] == pc0 and x["pawn"] == pawn0,
          f"get_current_target player_controller={x['pc']} player_pawn={x['pawn']}")
    x = now["move"]
    rig.v(f"{p}4", x["code"] == 0 and x["cmc"] == cmc0, f"get_movement_params code={x['code']} cmc={x['cmc']} "
                                                       f"(PAWN0's {cmc0})")
    x = now["time"]
    rig.v(f"{p}5", x["code"] == 0 and x["world"] == ws0 and x["pawn"] == pawn0,
          f"get_time_state code={x['code']} global owner={x['world']} (WS0 {ws0}) pawn owner={x['pawn']}")
    x = now["god"]
    want = (0, 1) if ref is None else (ref["god"]["state"],)
    rig.v(f"{p}6", x["state"] in want and x["state"] in (0, 1),
          f"get_god_mode state={x['state']}" + ("" if ref is None else f" (baseline {ref['god']['state']})"))
    x = now["stealth"]
    same = ref is None or x["cands"] == ref["stealth"]["cands"]
    rig.v(f"{p}7", x["code"] == 0 and same,
          f"find_stealth_meter code={x['code']}, {len(x['cands'])} candidate(s)"
          + ("" if ref is None else f", {'the baseline' if same else 'NOT the baseline'}'s set"))
    x = now["fly"]
    if p == "C":
        rig.v("C8", x["code"] == FLY_NO_PAWN and x["cmc"] == "0x0",
              f"fly_get_state code={x['code']} cmc={x['cmc']} (want {FLY_NO_PAWN}: its polled status never scans)")
    else:
        rig.v(f"{p}8", x["code"] == 0 and x["cmc"] == cmc0, f"fly_get_state code={x['code']} cmc={x['cmc']}")


# -- the DLL log ---------------------------------------------------------------------------------------------------
def pc_lines(rig: Rig, mark: Mark) -> list:
    """Every local-PlayerController resolution line the DLL stamped at or after the mark, from any category file."""
    folder = LOGS / DCR.process_folder_name(rig.image)
    out = []
    if not folder.is_dir():
        return out
    since = mark.t.timestamp() - 2.0   # a file's mtime is coarser than its line stamps
    for p in sorted(folder.glob("*.log")):
        try:
            # The live <cat>-0.log is read whatever its mtime says: the DLL holds it open, and NTFS may report the
            # last-write time of a file still open for writing late, which would skip the very lines being waited for.
            # An archive is only ever written before its rename, so its mtime is honest.
            if not p.stem.endswith("-0") and p.stat().st_mtime < since:
                continue
            text = p.read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue
        for line in text.splitlines():
            m = DCR.LOG_TS.match(line)
            if m and m.group(1) >= mark.text and PC_LINE.search(line):
                out.append(line)
    return out


def wait_lines(rig: Rig, mark: Mark, want: list) -> list:
    deadline = time.time() + LOG_WAIT_S
    while True:
        lines = pc_lines(rig, mark)
        if all(any(w in l for l in lines) for w in want) or time.time() >= deadline:
            break
        time.sleep(0.5)
    rig.ev.save("dll_log_lines", {"folder": str(LOGS / DCR.process_folder_name(rig.image)), "since": mark.text,
                                  "wanted": want, "lines": lines})
    for text, n in collections.Counter(l[26:] if DCR.LOG_TS.match(l) else l for l in lines).items():
        print(f"    log x{n}: {text}")
    return lines


# -- the phases ----------------------------------------------------------------------------------------------------
def need_baseline(rig: Rig, st: dict) -> dict:
    base = st.get("baseline")
    if not base or st.get("pid") != rig.pid:
        raise RigError(f"{STATE} holds no baseline for pid {rig.pid} -- run `baseline` first")
    return base


def game_tid(pid) -> int:
    try:
        return DCR.main_tid(pid)
    except SystemExit as e:
        raise RigError(str(e)) from None


def phase_baseline(rig: Rig, st: dict) -> bool:
    rig.phase("baseline")
    rig.open_pipe()
    if st.get("saved") or st.get("tid") or st.get("timeout_set"):
        # A break recorded for ANOTHER process still running is as live as one on this process: clearing the state
        # here would lose the only record of the pointers and the thread `restore` must put back there.
        old, img = st.get("pid"), st.get("image") or ""
        if old == rig.pid or (img and any(p == old for p, _ in S.pids(img))):
            raise RigError(f"{STATE} records a break still in effect on pid {old} ({img}) -- run `restore` first")
    try:
        ch = rig.read_chain()
    except RigError as e:
        rig.v("B0", False, f"the chain did not resolve: {e}")
        return False
    rig.v("B0", True, "  ".join(f"{k}={ch[k]}" for k in ("world", "gi", "lp", "pc0", "pawn0", "cmc0", "cam0", "level",
                                                         "ws0")))
    gt = rig.game_thread()
    mark = Mark()
    reads = module_reads(rig)
    st.clear()
    st.update(pid=rig.pid, image=rig.image, saved={},
              baseline={**ch, "tid_main": game_tid(rig.pid), "invoke_timeout_ms": gt.get("invoke_timeout_ms"),
                        "hints": hint_timeouts(), "reads": reads, "taken": mark.text})
    save_state(st)
    module_checks(rig, "B", reads, st["baseline"])
    want = [f"Teleport: local PC {ch['pc0']} via GWorld chain", f"Edel: local PC {ch['pc0']} via GWorld chain"]
    lines = wait_lines(rig, mark, want)
    scans = [l for l in lines if "via instance scan" in l]
    rig.v("B9", all(any(w in l for l in lines) for w in want) and not scans,
          f"{sum(any(w in l for l in lines) for w in want)}/2 GWorld-chain lines naming PC0, {len(scans)} "
          f"instance-scan line(s) since {mark.text}")
    return True


def phase_break(rig: Rig, st: dict) -> bool:
    rig.phase("break")
    rig.open_pipe()
    base = need_baseline(rig, st)
    tid = base["tid_main"]
    if not st.get("tid"):
        gt = rig.game_thread()
        if liveness(gt) != "responsive":
            # No ProcessEvent hook yet, so a stall could not be told from health; a read-only invoke installs it.
            rig.req("invoke_function", class_name="Actor", func_name="K2_GetActorLocation")
            time.sleep(1.0)
            gt = rig.game_thread()
        if liveness(gt) != "responsive":
            rig.v("K1", False, f"before the suspend the game thread reads {liveness(gt)!r}, not 'responsive': a stall "
                               "could not be confirmed; nothing done")
            return False
    if not st.get("timeout_set"):
        st["timeout_set"] = True   # recorded first: `restore` resets it whether or not this reply comes back
        save_state(st)
        r = rig.req("set_invoke_timeout", timeout_ms=rig.args.timeout_ms, persist=False)
        print(f"    set_invoke_timeout {rig.args.timeout_ms} (persist:false) -> {r.get('invoke_timeout_ms')}", flush=True)
        if r.get("invoke_timeout_ms") != rig.args.timeout_ms:
            raise RigError(f"set_invoke_timeout answered {r}")
    if not st.get("tid"):
        st["tid"] = tid            # recorded first: a rig killed right after the suspend must leave `restore` the tid
        save_state(st)
        if S.act_tid(rig.image, tid, False) != 0:
            st.pop("tid")
            save_state(st)
            raise RigError(f"SuspendThread on tid {tid} failed")
    gt = rig.wait_liveness("stalled")
    a, b = cpu_window(tid)
    frozen = a is not None and a == b and liveness(gt) == "stalled"
    rig.v("K1", frozen, f"tid {tid}: CPU time {a} -> {b} ms over {CPU_WINDOW_S} s; get_diagnostics answered: "
                        f"liveness={liveness(gt)!r} hook_fire_count={gt.get('hook_fire_count')}")
    if not frozen:
        print("    nothing written; run `restore` (it resumes the thread and resets the invoke timeout)", flush=True)
        return False
    _, _, lp = rig.read_lp()
    at = lp + rig.field_off(rig.class_of(lp), "PlayerController")
    if hx(lp) != base["lp"] or hx(at) != base["lp_pc_slot"]:
        rig.v("K2", False, f"LocalPlayers[0] is {hx(lp)} (PlayerController @{hx(at)}), the baseline's was {base['lp']} "
                           f"(@{base['lp_pc_slot']}); nothing written -- `restore`, then `baseline` again")
        return False
    cur = rig.mem.u64(at)
    if cur == 0 and "lp_pc" in st.get("saved", {}):
        rig.v("K2", True, f"already broken: LocalPlayer.PlayerController @{hx(at)} reads 0x0 "
                          f"(saved {st['saved']['lp_pc']['old']})")
        return True
    if hx(cur) != base["pc0"]:
        rig.v("K2", False, f"LocalPlayer.PlayerController @{hx(at)} reads {hx(cur)}, not PC0 {base['pc0']}; "
                           "nothing written")
        return False
    st.setdefault("saved", {})["lp_pc"] = {"slot": hx(at), "old": hx(cur)}
    save_state(st)                 # BEFORE the write: the state must always be able to undo it
    after = rig.pipe_write_ptr(at, 0)
    rig.v("K2", after == 0, f"LocalPlayer.PlayerController @{hx(at)}: {hx(cur)} -> {hx(after)} (write_mem; read back "
                            "with ReadProcessMemory)")
    return after == 0


def world_settings_step(rig: Rig, st: dict, base: dict) -> None:
    at, ws0 = h(base["ws_slot"]), base["ws0"]
    rows = [x for x in rig.req("find_instances", class_name="WorldSettings", exact_match=False,
                               limit=50).get("instances") or []
            if not str(x.get("name", "")).startswith("Default__")]
    print("    live *WorldSettings* objects: "
          + (", ".join(f"{x.get('name')} ({x.get('class')}) {x.get('addr')}" for x in rows[:6]) or "none"), flush=True)
    cur = rig.mem.u64(at)
    if hx(cur) != ws0:
        rig.v("W1", False, f"PersistentLevel.WorldSettings @{hx(at)} reads {hx(cur)}, not WS0 {ws0}; nothing written")
        return
    st.setdefault("saved", {})["level_ws"] = {"slot": hx(at), "old": ws0}
    save_state(st)                 # BEFORE the write
    try:
        after = rig.pipe_write_ptr(at, 0)
        rig.v("W1", after == 0, f"PersistentLevel.WorldSettings @{hx(at)}: {ws0} -> {hx(after)}")
        if after == 0:
            ts = rig.req("get_time_state")
            g = (ts.get("dilation") or {}).get("global") or {}
            rig.v("W2", ts.get("code") == 0 and g.get("resolved") is True and hx(g.get("owner_addr")) == ws0,
                  f"get_time_state with it broken: code={ts.get('code')} resolved={g.get('resolved')} "
                  f"global owner={hx(g.get('owner_addr'))} (WS0 {ws0})")
    finally:
        # At once: the engine's worker threads run on while the game thread is frozen, and may read it.
        ok = put_back(rig, st, "level_ws")
        rig.v("W3", ok, f"PersistentLevel.WorldSettings @{hx(at)} reads {hx(rig.mem.u64(at))} after the put-back")


def phase_check(rig: Rig, st: dict) -> bool:
    rig.phase("check")
    rig.open_pipe()
    base = need_baseline(rig, st)
    tid = st.get("tid")
    at = h(base["lp_pc_slot"])
    cur = rig.mem.u64(at)
    a, b = cpu_window(tid) if tid else (None, None)
    ok = cur == 0 and "lp_pc" in st.get("saved", {}) and a is not None and a == b
    rig.v("C0", ok, f"LocalPlayer.PlayerController @{hx(at)} reads {hx(cur)}; game thread tid {tid} CPU time "
                    f"{a} -> {b} ms")
    if not ok:
        print("    the chain is not broken under a frozen game thread, so C1..C8 would prove nothing -- run `break`",
              flush=True)
        return False
    picked = f"local PC {base['pc0']} via instance scan (Player set)"
    mark = Mark()
    module_checks(rig, "C", module_reads(rig), base)
    want = [f"Teleport: {picked}", f"Edel: {picked}"]
    lines = wait_lines(rig, mark, want)
    for cid, w in (("L1", want[0]), ("L2", want[1])):
        n = sum(w in l for l in lines)
        rig.v(cid, n > 0, f"{n} x \"{w}\" since {mark.text}")
    other = [l for l in lines if picked not in l]
    rig.v("L3", not other, f"{len(other)} other PC-resolution line(s) since {mark.text}"
                           + (f"; first: {other[0][26:]}" if other else ""))
    world_settings_step(rig, st, base)
    return True


def phase_restore(rig: Rig, st: dict) -> bool:
    rig.phase("restore")
    if not (st.get("baseline") or st.get("saved") or st.get("tid") or st.get("timeout_set")):
        print(f"    {STATE} holds nothing to restore", flush=True)
        return False
    rig.bind_from_state(st)
    base = st.get("baseline") or {}
    # Both pointers before the thread runs again; neither step may wait on the pipe.
    back = all([put_back(rig, st, k) for k in list(st.get("saved", {}))])
    if base:
        lp, ws = rig.mem.u64(h(base["lp_pc_slot"])), rig.mem.u64(h(base["ws_slot"]))
        rig.v("R0", back and hx(lp) == base["pc0"] and hx(ws) == base["ws0"],
              f"LocalPlayer.PlayerController={hx(lp)} (PC0 {base['pc0']}), PersistentLevel.WorldSettings={hx(ws)} "
              f"(WS0 {base['ws0']})")
    else:
        rig.v("R0", back, "no baseline in the state: only the saved pointers were checked")
    tid = st.get("tid")
    if tid:
        if back or rig.args.force_resume:
            if S.act_tid(rig.image, tid, True) == 0:   # drains the suspend count to zero
                st.pop("tid")
                save_state(st)
        else:
            print(f"    tid {tid} stays SUSPENDED: a pointer is not back. Look at it, then `restore --force-resume`.",
                  flush=True)
    rig.open_pipe()
    run_tid = tid or base.get("tid_main")
    gt = rig.wait_liveness("responsive")
    a, b = cpu_window(run_tid) if run_tid else (None, None)
    rig.v("R9", "tid" not in st and a is not None and b is not None and b > a and liveness(gt) == "responsive",
          f"tid {run_tid}: CPU time {a} -> {b} ms over {CPU_WINDOW_S} s; liveness={liveness(gt)!r}"
          + (" -- still suspended" if "tid" in st else ""))
    want = base.get("invoke_timeout_ms")
    if st.get("timeout_set"):
        rig.req("set_invoke_timeout", timeout_ms=0, persist=False)
        got = rig.game_thread().get("invoke_timeout_ms")
        if isinstance(want, int) and got != want and 100 <= want <= 600000:
            rig.req("set_invoke_timeout", timeout_ms=want, persist=False)
        st.pop("timeout_set")
        save_state(st)
    got, hints = rig.game_thread().get("invoke_timeout_ms"), hint_timeouts()
    rig.v("R10", want is not None and got == want and hints == base.get("hints", {}),
          f"invoke timeout {got} ms (baseline {want}); persisted invokeTimeoutMs {hints} "
          f"(baseline {base.get('hints', {})})")
    if base and "tid" not in st:
        module_checks(rig, "R", module_reads(rig), base)
        # R0 read the pointers while the thread was still frozen. A write_mem the DLL had taken but not yet run when
        # the rig was killed would land only after the resume, so the pointers are read again with the game running.
        lp, ws = rig.mem.u64(h(base["lp_pc_slot"])), rig.mem.u64(h(base["ws_slot"]))
        rig.v("R11", hx(lp) == base["pc0"] and hx(ws) == base["ws0"],
              f"with the game thread running: LocalPlayer.PlayerController={hx(lp)} (PC0 {base['pc0']}), "
              f"PersistentLevel.WorldSettings={hx(ws)} (WS0 {base['ws0']})")
    return True


PHASES = {"baseline": ("B", phase_baseline), "break": ("K", phase_break), "check": ("C", phase_check),
          "restore": ("R", phase_restore)}


def guarded(rig: Rig, verb: str, st: dict) -> bool:
    prefix, fn = PHASES[verb]
    try:
        return fn(rig, st)
    except (RigError, PipeError, SystemExit, OSError) as e:   # mailbox_poke / dbgcam helpers fail with SystemExit
        rig.v(f"{prefix}X", False, f"aborted: {e}")
        return False
    except Exception as e:   # noqa: BLE001
        # A bug of the rig's own (a reply shaped unlike the one expected) must still end as a BAD row and let `all`
        # reach its restore with the verdicts saved, not as a traceback that skips the RESULT line.
        rig.ev.save("traceback", {"phase": verb, "traceback": traceback.format_exc()})
        rig.v(f"{prefix}X", False, f"aborted: {type(e).__name__}: {e}")
        return False


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("verb", choices=("baseline", "break", "check", "restore", "all"))
    ap.add_argument("--process", default="DumperTest", help="a prefix of the game's image name (default DumperTest)")
    ap.add_argument("--timeout-ms", type=int, default=200,
                    help="invoke timeout while the game thread is frozen, persist:false (default 200)")
    ap.add_argument("--force-resume", action="store_true",
                    help="restore: resume the game thread even when a saved pointer could not be put back")
    a = ap.parse_args()
    if a.timeout_ms < MIN_TIMEOUT_MS:
        ap.error(f"--timeout-ms below {MIN_TIMEOUT_MS} fails healthy invokes at the fixture's 15 FPS (l78)")

    rig = Rig(a)
    st = load_state()
    try:
        if a.verb == "all":
            try:
                if guarded(rig, "baseline", st) and guarded(rig, "break", st):
                    guarded(rig, "check", st)
            finally:
                guarded(rig, "restore", st)
        else:
            guarded(rig, a.verb, st)
    finally:
        if rig.pc is not None:
            rig.pc.close()
        rig.ev.phase = a.verb
        rig.ev.save("verdicts", {"verb": a.verb, "build": rig.build, "image": rig.image, "pid": rig.pid,
                                 "rows": rig.v.rows, "state": st})
    # A separate verb leaves its break in place on purpose, a failed one included: say so, so a frozen game is never
    # mistaken for a hung one.
    left = [k for k, on in (("game thread SUSPENDED", st.get("tid")),
                            ("pointer(s) at 0: " + ", ".join(st.get("saved") or {}), st.get("saved")),
                            ("invoke timeout lowered", st.get("timeout_set"))) if on]
    if left:
        print(f"STILL IN EFFECT ({'; '.join(left)}) -- run: py tools/verify/localpc_fallback_live.py restore",
              flush=True)
    n, bad = len(rig.v.rows), rig.v.bad
    print(f"RESULT: {'PASS' if n and not bad else 'FAIL'} ({n} check(s), {bad} BAD) on build {rig.build or '?'}; "
          f"state: {STATE}")
    return 0 if n and not bad else 1


if __name__ == "__main__":
    sys.exit(main())
