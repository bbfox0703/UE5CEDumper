#!/usr/bin/env python3
r"""[DEBUGCAM-FORCEON-CDO] A Debug Camera Force ON with no live CheatManager is refused (-17): pipe + mailbox, headless.

    py tools/verify/dbgcam_cdo_refusal.py                         # DumperTest Shipping: PRE, BLD, D1-D3, D5, D6, LOG
    py tools/verify/dbgcam_cdo_refusal.py --stall                 # ...plus D4: the same answers, game thread frozen
    py tools/verify/dbgcam_cdo_refusal.py --process DumperTest58  # another fixture (a prefix of its image name)
    py tools/verify/dbgcam_cdo_refusal.py --allow-live-toggle     # live CheatManager (Development): ON=1, OFF=0

Written for the row [DEBUGCAM-FORCEON-CDO] (docs/todo.md); the refusal ships from build 3682. With no live
CheatManager -- the usual Shipping state, since UE 5 compiles it out (UE_WITH_CHEAT_MANAGER) -- the Debug Camera state
read lands on Default__CheatManager, where ToggleDebugCamera does nothing. From 3682 that toggle is never sent: an ON
answers -17 at once, with no game-thread call and no ProcessEvent hook install. An OFF is never refused.

PREREQUISITES
  * DumperTest SHIPPING (UE 5.4, no CheatManager): `py tools/verify/launch_dumpertest.py shipping`, then
    `py tools/verify/inject.py --name DumperTest` (or a deployed proxy: assert_build refuses a stale one).
  * The UI must NOT be connected: it holds two of the DLL's three pipe instances (pipe_client.py).
  * No Cheat Engine: the mailbox is driven with ReadProcessMemory / WriteProcessMemory.
  * dist\build_number.txt names the DLL under test, and that build is 3682 or later.
  * One injected game at a time; kill the game when the row is done (handover section 4).

CHECKS -- one line each, "<ID> OK|BAD  <detail>"; the exit code is 0 only when every check that ran passed
  PRE   find_instances CheatManager, by exact name and by substring: both replies complete. A live row (one not
        named Default__*) means CONTROL mode, below.
  BLD   the DLL answering is 3682 or later. An older one SENDS the toggle to the class default object, which the
        house rule forbids, so the rig stops before any Force ON.
  D1    get_debug_camera_state == 0
  D2    set_debug_camera enable:true  -> state -17, no `nonstatic_on_template` key, round trip < 1.0 s
  D3    set_debug_camera enable:false -> state 0 (not -6: nothing was queued)
  D4    (--stall) the UE game thread suspended as l78_parentrel_arm.py `degrade` does it (the process main thread,
        an invoke timeout lowered with persist:false, a stall confirmed by EFFECT through get_diagnostics):
          D4-stall   game_thread reads unresponsive while the pipe still answers
          D4-on      Force ON -> -17 in < 1.0 s (a toggle sent would wait out the invoke timeout and answer -5)
          D4-off     Force OFF -> 0 (not -6, which would mean a toggle had been queued)
          D4-resume  the suspend count drained and game_thread reads responsive again
  D5    mailbox CMD_SET_DEBUG_CAMERA (7), cmdOutFlags and result poisoned before each command:
          D5-on      req=1 -> result -17, cmdOutFlags 0, errorMsg holds "no live CheatManager"
          D5-off     req=0 -> 0, cmdOutFlags 0
          D5-query   req=2 -> 0, cmdOutFlags 0
  D6    g_mailboxContract: the magic, current >= 8, min 1
  LOG   the DLL's logs (%LOCALAPPDATA%\UE5CEDumper\Logs\<exe stem>\), lines stamped since the rig started: the
        refusal line "no live CheatManager -- ToggleDebugCamera does nothing" appears, and no
        "ToggleDebugCamera ProcessEvent" line does. Best effort: a log that cannot be found prints "LOG --" and is
        not counted.

CONTROL MODE (a live CheatManager, e.g. `launch_dumpertest.py dev`): only D1 runs, and nothing is toggled -- a Force
ON there really runs the toggle -- unless --allow-live-toggle is given; then C-on (ON -> 1) and C-off (OFF -> 0), as
dbgcam_reenable.py measures them, and LOG. A control run does not exercise the refusal, and its RESULT line says so.

Never a CheatManager command other than set_debug_camera / get_debug_camera_state. The one other game-thread call is
D4's read-only Actor K2_GetActorLocation, sent only when no ProcessEvent hook is installed yet: without one
get_diagnostics cannot tell a frozen game thread from a healthy one. Every reply is saved verbatim under
out/DEBUGCAM-FORCEON-CDO/ (working-lessons 1.ad). The camera is left OFF when the rig sent any toggle.
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
import os
import pathlib
import re
import struct
import sys
import threading
import time

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.stdout.reconfigure(encoding="utf-8", errors="replace")
from pipe_client import PipeClient, PipeError  # noqa: E402
import invoke_template_live as ITL  # noqa: E402  (its mailbox round trip poisons cmdOutFlags and result first)
import mailbox_addr as MA  # noqa: E402
import mailbox_poke as MB  # noqa: E402
import suspend as S  # noqa: E402

TAG = "DEBUGCAM-FORCEON-CDO"
OUT = HERE.parents[1] / "out" / TAG

REFUSAL_FROM_BUILD = 3682
NO_LIVE_CHEATMANAGER = -17          # Mimic::DBGCAM_ERR_NO_LIVE_CHEATMANAGER
CMD_SET_DEBUG_CAMERA = 7            # Mimic::CMD_SET_DEBUG_CAMERA; instanceAddr carries the request
REQ_OFF, REQ_ON, REQ_QUERY = 0, 1, 2
CONTRACT_MAGIC = 0x43354555         # Mimic::MAILBOX_CONTRACT_MAGIC
CONTRACT_AT_LEAST, CONTRACT_MIN = 8, 1
ROUND_TRIP_LIMIT_S = 1.0
STALL_SETTLE_S = 1.5                # l78's wait: three times the DLL's 500 ms stall threshold
DUMPER_MODULES = ("ue5dumper.dll", "version.dll", "dxgi.dll", "dinput8.dll", "winmm.dll")
REFUSAL_LINE = "no live CheatManager -- ToggleDebugCamera does nothing"
TOGGLE_PE_LINE = "ToggleDebugCamera ProcessEvent"
LOG_TS = re.compile(r"^\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})\]")


def data(reply):
    return reply.get("data", reply)


def build_int(s) -> int:
    m = re.search(r"(\d+)\s*$", str(s))
    return int(m.group(1)) if m else -1


class Evidence:
    """Every reply the rig reads goes to disk as it is read: stdout is not evidence (working-lessons 1.ad)."""

    def __init__(self):
        self.stamp = dt.datetime.now().strftime("%Y%m%d-%H%M%S")
        self.seq = 0
        self.lock = threading.Lock()
        OUT.mkdir(parents=True, exist_ok=True)

    def save(self, label, obj):
        with self.lock:
            self.seq += 1
            path = OUT / f"{self.stamp}_{self.seq:03d}_{re.sub(r'[^A-Za-z0-9_.-]+', '_', label)}.json"
        rec = {"saved_at": dt.datetime.now().isoformat(timespec="milliseconds"), **obj}
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            json.dump(rec, f, ensure_ascii=False, indent=1)
            f.write("\n")
        return path


class RecordingClient(PipeClient):
    """A PipeClient that saves every reply and times every round trip, including the guards' own requests, so the
    build and scan evidence lands beside the checks'. The time excludes the save."""

    def __init__(self, ev: Evidence, **kw):
        super().__init__(**kw)
        self.ev = ev
        self.last_elapsed = 0.0

    def request(self, cmd: str, **params):
        t0 = time.perf_counter()
        try:
            reply = super().request(cmd, **params)
        except PipeError as e:
            self.ev.save(f"pipe_{cmd}_ERROR", {"request": {"cmd": cmd, **params}, "error": str(e),
                                               "elapsed_s": round(time.perf_counter() - t0, 4)})
            raise
        self.last_elapsed = time.perf_counter() - t0
        self.ev.save(f"pipe_{cmd}", {"request": {"cmd": cmd, **params}, "elapsed_s": round(self.last_elapsed, 4),
                                     "reply": reply})
        return reply


class Verdicts:
    def __init__(self):
        self.rows = []

    def __call__(self, label, ok, detail) -> bool:
        ok = bool(ok)
        self.rows.append({"id": label, "ok": ok, "detail": detail})
        print(f"{label} {'OK' if ok else 'BAD'}  {detail}", flush=True)
        return ok

    @property
    def bad(self) -> int:
        return sum(not r["ok"] for r in self.rows)


def resolve_process(name, pipe_pid):
    """(pid, image) of the process whose DLL answers the pipe. The pipe's own pid decides between processes sharing the
    prefix, and a pipe served by a process the name does not match is refused: the mailbox read and the thread frozen
    must belong to the DLL the pipe checks ran against."""
    rows = [(pid, img) for pid, img in S.pids(name) if img.lower().startswith(name.lower())]
    if not rows:
        raise SystemExit(f"no process whose image starts with {name!r}")
    if pipe_pid is None:
        if len(rows) > 1:
            raise SystemExit(f"{name!r} matches {rows}, and the pipe names no pid to choose by")
        return rows[0]
    hit = [r for r in rows if r[0] == int(pipe_pid)]
    if not hit:
        raise SystemExit(f"the pipe is served by pid {pipe_pid}, which is none of {rows} -- another game is injected")
    return hit[0]


def main_tid(pid) -> int:
    # The UE game thread is the process main thread (GuardedMain runs on the thread WinMain started on), so it is the
    # EARLIEST CREATED -- never the lowest tid, which comes from a recycled pool (suspend.py).
    rows = sorted(((S.thread_times(t) or (1 << 63, 0))[0], t) for t in S.thread_ids(pid))
    if not rows:
        raise SystemExit(f"no threads listed for pid {pid}")
    return rows[0][1]


def game_thread(pc) -> dict:
    return data(pc.request("get_diagnostics", limit=1)).get("game_thread") or {}


def pre_check(pc, v):
    """(complete, control). Only a COMPLETE reply can say that no CheatManager is live."""
    exact = pc.request("find_instances", class_name="CheatManager", exact_match=True, limit=500)
    loose = pc.request("find_instances", class_name="CheatManager", exact_match=False, limit=500)
    de, dl = data(exact), data(loose)
    complete = all(r.get("ok") and isinstance(d.get("instances"), list) and not d.get("truncated")
                   and not d.get("deadline_hit") for r, d in ((exact, de), (loose, dl)))
    rows = de.get("instances") or []
    for x in rows:
        print(f"    CheatManager row: {x.get('name')} #{x.get('index')} {x.get('addr')} outer={x.get('outer')}")
    live = [x for x in rows if not str(x.get("name", "")).startswith("Default__")]
    # exact_match compares class NAMES, while the DLL picks its CheatManager by derivation: a game's own subclass, or
    # an extension a live CheatManager owns, is invisible to it. Any live object whose class name holds
    # "CheatManager" therefore counts as live too, so no Force ON is sent where a toggle could really run.
    other = [x for x in (dl.get("instances") or [])
             if not str(x.get("name", "")).startswith("Default__") and x.get("class") != "CheatManager"]
    control = bool(live or other)
    names = ", ".join(f"{x.get('name')} ({x.get('class')})" for x in (live + other)[:6])
    v("PRE", complete,
      f"{len(rows)} CheatManager row(s), {len(live)} live; {len(other)} other live *CheatManager* object(s)"
      + (f" [{names}]" if names else "")
      + (f" -> {'CONTROL' if control else 'REFUSAL'} mode" if complete else " -- a reply is incomplete, no mode"))
    if complete and not control and not any(str(x.get("name", "")) == "Default__CheatManager" for x in rows):
        print("NOTE  no Default__CheatManager row: the DLL finds no CheatManager at all, which it refuses the same way")
    return complete, control


def set_cam(pc, enable):
    """(ok, state, seconds, key present). The key is the 3676-3681 warning a 3682+ DLL never sends."""
    r = pc.request("set_debug_camera", enable=enable)
    d = data(r)
    return bool(r.get("ok")), d.get("state"), pc.last_elapsed, ("nonstatic_on_template" in r
                                                                 or "nonstatic_on_template" in d)


def check_d1(pc, v):
    r = pc.request("get_debug_camera_state")
    st = data(r).get("state")
    v("D1", r.get("ok") and st == 0, f"get_debug_camera_state -> state={st}" + ("" if st == 0 else ", expected 0 (OFF)"))


def stall_checks(pc, a, pid, image, v) -> int:
    """D4. Returns how many Force ONs it sent."""
    gt = game_thread(pc)
    if gt.get("liveness") != "responsive":
        # The -17 path installs no hook, and without one liveness reads "unknown" -- which the DLL counts as
        # responsive, so a suspend could not be confirmed. One read-only invoke routed to the game thread installs it.
        pc.request("invoke_function", class_name="Actor", func_name="K2_GetActorLocation")
        time.sleep(1.0)
        gt = game_thread(pc)
    if gt.get("liveness") != "responsive":
        v("D4-stall", False, f"cannot confirm a stall: before it, liveness={gt.get('liveness')!r}, not 'responsive'")
        return 0
    prior_timeout = gt.get("invoke_timeout_ms")
    try:
        tid = main_tid(pid)
    except SystemExit as e:
        v("D4-stall", False, str(e))
        return 0
    print(f"D4: pid {pid} game thread tid {tid}; invoke timeout {prior_timeout} ms before the stall", flush=True)

    lock = threading.Lock()
    fired = threading.Event()

    def resume(why):
        # ResumeThread is the one call safe to over-call, and act_tid drains the count to zero, so the watchdog and
        # the finally block may both run it.
        with lock:
            print(f"D4: resuming tid {tid} ({why})", flush=True)
            return S.act_tid(image, tid, True)

    def watchdog_fire():
        fired.set()
        resume(f"watchdog: the stall outlived --stall-max {a.stall_max:g} s")

    # A pipe read blocks with no deadline of its own (PipeClient checks its timeout between reads), so a DLL thread
    # waiting on the frozen one would hang the rig with the game frozen. The watchdog bounds the stall regardless.
    watchdog = threading.Timer(a.stall_max, watchdog_fire)
    watchdog.daemon = True
    attempted = suspended = changed = False
    resume_rc = None
    sent_on = 0
    try:
        if a.stall_invoke_timeout_ms:
            # A DLL that did send the toggle would then answer -5 within this, not after the 5 s default: the stall
            # stays short, and the BAD line says why. persist:false keeps it out of the per-machine hint cache.
            r = pc.request("set_invoke_timeout", timeout_ms=a.stall_invoke_timeout_ms, persist=False)
            changed = bool(r.get("ok"))
            print(f"D4: set_invoke_timeout {a.stall_invoke_timeout_ms} (persist:false) -> "
                  f"{data(r).get('invoke_timeout_ms')} ok={r.get('ok')}", flush=True)
        attempted = True
        suspended = S.act_tid(image, tid, False) == 0
        if not suspended:
            v("D4-stall", False, f"SuspendThread on tid {tid} failed")
        else:
            watchdog.start()
            time.sleep(STALL_SETTLE_S)
            gt = game_thread(pc)
            stalled = gt.get("responsive") is False
            v("D4-stall", stalled,
              f"tid {tid} suspended; game_thread liveness={gt.get('liveness')!r} responsive={gt.get('responsive')!r} "
              f"hook_fire_count={gt.get('hook_fire_count')} (the pipe answered)"
              + ("" if stalled else " -- not stalled, so D4-on / D4-off would prove nothing"))
            if stalled:
                ok, st, t, key = set_cam(pc, True)
                sent_on += 1
                v("D4-on", ok and st == NO_LIVE_CHEATMANAGER and t < ROUND_TRIP_LIMIT_S,
                  f"set_debug_camera(true) while stalled -> state={st} in {t:.3f} s"
                  + (", nonstatic_on_template PRESENT" if key else ""))
                ok, st, t, _ = set_cam(pc, False)
                v("D4-off", ok and st == 0, f"set_debug_camera(false) while stalled -> state={st} in {t:.3f} s")
    finally:
        watchdog.cancel()
        if attempted:
            resume_rc = resume("finally")
            if resume_rc != 0:
                print(f"!! RESUME FAILED -- run: py tools/verify/suspend.py resume-tid {image} {tid}", flush=True)
        if changed:
            restore = prior_timeout if isinstance(prior_timeout, int) and prior_timeout > 0 else 0
            try:
                r = pc.request("set_invoke_timeout", timeout_ms=restore, persist=False)
                print(f"D4: invoke timeout restored -> {data(r).get('invoke_timeout_ms')} ms", flush=True)
            except PipeError as e:
                print(f"!! invoke timeout NOT restored ({e}): send set_invoke_timeout timeout_ms={restore} "
                      "persist=false", flush=True)
    if suspended:
        deadline = time.time() + 5.0
        gt = {}
        while time.time() < deadline:
            gt = game_thread(pc)
            if gt.get("liveness") == "responsive":
                break
            time.sleep(0.5)
        v("D4-resume", resume_rc == 0 and gt.get("liveness") == "responsive",
          f"resume rc={resume_rc}; game_thread liveness={gt.get('liveness')!r}"
          + (" (the watchdog fired first)" if fired.is_set() else ""))
        print(f"D4: after the resume the camera reads {data(pc.request('get_debug_camera_state')).get('state')}")
    return sent_on


def mailbox_checks(pid, v, ev) -> int:
    """D5. Returns how many Force ONs it sent."""
    try:
        base = MB.mailbox_addr(str(pid))
        m = MB.Mem(pid)
        init = m.i32(base + MB.OFF_INITSTATE)
    except SystemExit as e:
        v("D5", False, f"mailbox not reachable: {e}")
        return 0
    if init != MB.INIT_READY:
        v("D5", False, f"mailbox {base:#x} initState={init}, not READY: its poll loop may not be running")
        return 0
    sent_on = 0
    for label, req in (("D5-on", REQ_ON), ("D5-off", REQ_OFF), ("D5-query", REQ_QUERY)):
        t0 = time.perf_counter()
        try:
            res, _, _, out, err = ITL.cmd(m, base, CMD_SET_DEBUG_CAMERA, inst=req, timeout=15.0)
        except SystemExit as e:
            ev.save(f"mailbox_set_debug_camera_req{req}_ERROR", {"mailbox": hex(base), "instanceAddr": req,
                                                                "error": str(e)})
            v(label, False, f"req={req}: {e}")
            return sent_on   # an unanswered command leaves the mailbox busy, so the next answer would not be its own
        el = time.perf_counter() - t0
        sent_on += req == REQ_ON
        ev.save(f"mailbox_set_debug_camera_req{req}", {
            "mailbox": hex(base), "cmd": CMD_SET_DEBUG_CAMERA, "instanceAddr": req, "result": res,
            "cmdOutFlags": out, "errorMsg": err, "elapsed_s": round(el, 4)})
        if req == REQ_ON:
            ok = res == NO_LIVE_CHEATMANAGER and out == 0 and "no live CheatManager" in err
        else:
            ok = res == 0 and out == 0
        v(label, ok, f"req={req} -> result={res} cmdOutFlags={out:#x} in {el:.3f} s"
          + (f" errorMsg={err!r}" if err else ""))
    return sent_on


def contract_check(pid, v, ev):
    """D6, read from the export table the way a CE script finds it, not through the pipe."""
    try:
        rd = MA.Reader(pid)
        addr, module = None, ""
        for path, mbase in MA.modules(pid):
            if mbase and path.lower().endswith(DUMPER_MODULES):
                try:
                    addr = MA.find_export(rd, mbase, "g_mailboxContract")
                except SystemExit:
                    addr = None
                if addr:
                    module = path
                    break
        if not addr:
            v("D6", False, "no injected dumper module exports g_mailboxContract")
            return
        magic, cur, lo = struct.unpack("<Iii", rd.read(addr, 12))
    except SystemExit as e:
        v("D6", False, f"g_mailboxContract not readable: {e}")
        return
    ev.save("mailbox_contract", {"module": module, "addr": hex(addr), "magic": magic, "current": cur, "minimum": lo})
    v("D6", magic == CONTRACT_MAGIC and cur >= CONTRACT_AT_LEAST and lo == CONTRACT_MIN,
      f"{pathlib.PureWindowsPath(module).name}: magic={magic:#x} current={cur} min={lo}")


def process_folder_name(image: str) -> str:
    """The DLL's log folder name (Sein::ProcessFolderName): the exe's stem, the nine characters a file name cannot
    hold made '_', trailing spaces and dots trimmed."""
    stem = image.rsplit(".", 1)[0] if "." in image else image
    stem = re.sub(r'[/\\:*?"<>|]', "_", stem).rstrip(" .")
    return stem or "unknown"


def log_check(image, t_start, sent_on, control, v, ev):
    folder = pathlib.Path(os.environ.get("LOCALAPPDATA", "")) / "UE5CEDumper" / "Logs" / process_folder_name(image)
    if not folder.is_dir():
        print(f"LOG --  no log folder {folder} (not counted)")
        return
    since = t_start.timestamp() - 2.0   # a file's mtime is coarser than its line stamps
    files = sorted(p for p in folder.glob("*.log") if p.stat().st_mtime >= since)
    refusals, toggles, read = [], [], []
    for p in files:
        try:
            text = p.read_text(encoding="utf-8", errors="replace")
        except OSError as e:
            print(f"LOG --  {p.name} unreadable: {e}")
            continue
        read.append(p.name)
        for line in text.splitlines():
            m = LOG_TS.match(line)
            if not m:
                continue
            try:
                ts = dt.datetime.strptime(m.group(1), "%Y-%m-%d %H:%M:%S.%f")
            except ValueError:
                continue
            if ts < t_start:
                continue
            if REFUSAL_LINE in line:
                refusals.append(f"{p.name}: {line}")
            if TOGGLE_PE_LINE in line:
                toggles.append(f"{p.name}: {line}")
    ev.save("dll_log_lines", {"folder": str(folder), "since": t_start.isoformat(timespec="milliseconds"),
                              "files_read": read, "refusal_lines": refusals, "toggle_processevent_lines": toggles})
    if not read:
        print(f"LOG --  nothing in {folder} written since the rig started (not counted)")
        return
    v("LOG", not toggles and (control or refusals),
      f"{folder.name}: {len(refusals)} refusal line(s) for {sent_on} Force ON(s) sent, {len(toggles)} "
      f"'{TOGGLE_PE_LINE}' line(s) since {t_start:%H:%M:%S} ({', '.join(read)})")
    for line in toggles[:5]:
        print(f"    {line}")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--process", default="DumperTest", help="a prefix of the game's image name (default DumperTest)")
    ap.add_argument("--stall", action="store_true", help="also run D4 with the UE game thread suspended")
    ap.add_argument("--stall-invoke-timeout-ms", type=int, default=200,
                    help="invoke timeout during D4, persist:false, restored after; 0 leaves it alone (default 200)")
    ap.add_argument("--stall-max", type=float, default=20.0,
                    help="seconds after which a watchdog resumes the game thread whatever the rig is doing")
    ap.add_argument("--allow-live-toggle", action="store_true",
                    help="CONTROL mode only: send ON then OFF to the live CheatManager (expects 1, then 0)")
    ap.add_argument("--timeout", type=float, default=60.0, help="pipe reply timeout, seconds")
    a = ap.parse_args()
    if a.stall_invoke_timeout_ms and a.stall_invoke_timeout_ms < 150:
        ap.error("--stall-invoke-timeout-ms below 150 fails healthy invokes at the fixture's 15 FPS (l78)")

    t_start = dt.datetime.now()
    ev = Evidence()
    v = Verdicts()
    mode, build, proc = "undecided", "", None
    sent_on, toggled = 0, False
    try:
        with RecordingClient(ev, timeout=a.timeout) as pc:
            build = pc.assert_build()
            print("build:", build, flush=True)
            ptrs = pc.ensure_scanned()
            try:
                proc = resolve_process(a.process, ptrs.get("pid"))
            except SystemExit as e:
                v("PRE", False, f"process: {e}")
            if proc:
                pid, image = proc
                print(f"process: {image} pid {pid}; pipe module {ptrs.get('module_name')!r}", flush=True)
                complete, control = pre_check(pc, v)
                if complete and control:
                    mode = "CONTROL"
                    print("CONTROL mode -- a CheatManager is live, so a Force ON would really run the toggle: "
                          "only D1" + (", C-on, C-off" if a.allow_live_toggle else ""), flush=True)
                    check_d1(pc, v)
                    if a.allow_live_toggle:
                        toggled = True
                        for label, want, expect in (("C-on", True, 1), ("C-off", False, 0)):
                            ok, st, t, key = set_cam(pc, want)
                            v(label, ok and st == expect and not key,
                              f"set_debug_camera({str(want).lower()}) -> state={st} in {t:.3f} s"
                              + (", nonstatic_on_template PRESENT" if key else ""))
                            time.sleep(1.0)   # dbgcam_reenable.py's pacing between toggles
                    else:
                        print("NOTE  no Force ON sent; --allow-live-toggle runs the ON=1 / OFF=0 control")
                    if a.stall:
                        print("NOTE  --stall ignored in CONTROL mode")
                elif complete:
                    mode = "REFUSAL"
                    n = build_int(build)
                    if v("BLD", n >= REFUSAL_FROM_BUILD,
                         f"build {n} " + (f">= {REFUSAL_FROM_BUILD}: carries the refusal" if n >= REFUSAL_FROM_BUILD
                                          else f"< {REFUSAL_FROM_BUILD}: would SEND the toggle to the class default "
                                               "object -- no Force ON sent")):
                        toggled = True
                        check_d1(pc, v)
                        ok, st, t, key = set_cam(pc, True)
                        sent_on += 1
                        v("D2", ok and st == NO_LIVE_CHEATMANAGER and not key and t < ROUND_TRIP_LIMIT_S,
                          f"set_debug_camera(true) -> state={st} in {t:.3f} s, nonstatic_on_template "
                          f"{'PRESENT' if key else 'absent'}")
                        ok, st, t, _ = set_cam(pc, False)
                        v("D3", ok and st == 0, f"set_debug_camera(false) -> state={st} in {t:.3f} s")
                        if a.stall:
                            sent_on += stall_checks(pc, a, pid, image, v)
                        sent_on += mailbox_checks(pid, v, ev)
                        contract_check(pid, v, ev)
                if toggled:
                    st = data(pc.request("get_debug_camera_state")).get("state")
                    if st == 1:
                        print("camera left ON -- forcing OFF:", data(pc.request("set_debug_camera", enable=False))
                              .get("state"))
    except PipeError as e:
        v("PIPE", False, str(e))
    if proc and toggled:
        log_check(proc[1], t_start, sent_on, mode == "CONTROL", v, ev)

    total = len(v.rows)
    ev.save("summary", {"tag": TAG, "build": build, "mode": mode, "process": proc, "args": vars(a),
                        "started": t_start.isoformat(timespec="milliseconds"), "verdicts": v.rows})
    print(f"evidence: {OUT}\\{ev.stamp}_*.json")
    if total == 0:
        print("RESULT: FAIL -- no check ran")
        return 1
    if v.bad:
        print(f"RESULT: FAIL ({v.bad} of {total}) on build {build or '?'}")
        return 1
    if mode == "CONTROL":
        print(f"RESULT: CONTROL PASS ({total} checks) on build {build} -- the -17 refusal was NOT exercised "
              f"(a CheatManager is live); this run does not close [{TAG}]")
        return 0
    print(f"RESULT: PASS ({total} checks) on build {build}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
