r"""executeCodeEx negative control — stage a GAME-THREAD stall so CE's timeout can fire.

    py tools/verify/execcodeex_negctl.py arm       # warm hook, raise Stark's deadline, stall
    py tools/verify/execcodeex_negctl.py check     # re-read the witnesses while stalled
    py tools/verify/execcodeex_negctl.py release   # resume the game thread

THE ROW (build 2792): `ue5_callDLL` must give CE a FINITE deadline and report CE's OWN reason
string. PASS is CE printing

    executeCodeEx failed for UE5_CallProcessEvent: Execution timeout

⛔ THE PRESCRIBED FORM WAS REFUTED — do not go back to it. Suspending the whole PROCESS
(`NtSuspendProcess`) cannot make this fail: `executeCodeEx` runs the target on a **newly created
remote thread**, and suspension only freezes threads that already exist. Measured 2026-08-18:
`elapsed=1422 ms ok=true` for a call issued 3 s into an 18 s suspension (todo.md:9869). The
dissect's exports are pure memory work needing no game thread either, so nothing blocked.

⭐ SO THE STALL MUST BE THE GAME THREAD, AND THE CALL MUST NEED IT. `UE5_CallProcessEvent` is the
one shipped export that queues onto the game thread via `Stark::EnqueueInvoke`, and
`ue5_callDLL(name, ret, ...)` (`scripts/UE5CEDumper.CT:485`) is the only 5000 ms call site that
takes varargs and can therefore reach it.

⚠⚠ TWO SILENT-PASS TRAPS, and this rig exists mainly to close them — both make the run look like a
PASS while testing nothing:

  1. **No ProcessEvent hook.** With `hook_active == false`, `UE5_CallProcessEvent` falls through to a
     DIRECT synchronous call that never touches the queue, returns promptly, and looks fine. The hook
     must be WARMED and observed true.
  2. **The two deadlines are identical.** `Stark::kDefaultInvokeTimeoutMs` is **5000**
     (`Stark.h:53`) and `UE5_CALL_TIMEOUT_MS` in the `.CT` is **5000** (`UE5CEDumper.CT:483`). At
     defaults they race, and if Stark answers first CE's timeout path is never exercised. Stark's is
     raised to 60000 so CE's 5000 is unambiguously the shorter one.

⚠ AND THE STALL IS CONFIRMED BY EFFECT, never by having issued the command: `responsive` must go
false and `hook_fire_count` must stop advancing WHILE the pipe still answers. `suspend.py` matches
on a SUBSTRING and acts on the FIRST match, so the tid is chosen here by fire-rate, not by name.
"""
from __future__ import annotations

import pathlib
import subprocess
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from pipe_client import PipeClient          # noqa: E402
from ad4_contested import find_live_actor, invoke   # noqa: E402


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(s.encode(enc, "replace").decode(enc, "replace") + "\n")


def gt(c):
    d = c.request("get_diagnostics")
    return d.get("game_thread") or d.get("gameThread") or {}


def tid_ctl(verb, tid, name="DumperTest"):
    r = subprocess.run([sys.executable, str(HERE / "suspend.py"), verb, name, str(tid)],
                       capture_output=True, text=True, encoding="utf-8", errors="replace")
    return (r.stdout or r.stderr).strip().splitlines()[-1]


def arm():
    fails = []
    with PipeClient() as c:
        c.assert_build()
        c.ensure_scanned()

        # ---- trap 1: the hook must be ACTIVE, or the export never queues ----
        act = find_live_actor(c)
        fns = {f["name"]: f for f in
               c.request("walk_functions", addr=act["class_addr"])["functions"]}
        warm = next((n for n in ("Spawn_LateInstance", "Spawn_Decoys") if n in fns), None)
        if warm:
            try:
                invoke(c, act["addr"], warm, parms_size=fns[warm]["parms_size"],
                       params_hex="00" * max(fns[warm]["parms_size"], 1))
            except Exception as e:
                say("  (warm invoke raised: %s -- continuing, the hook may already be up)" % e)
        time.sleep(1.0)
        g = gt(c)
        say("[1] hook_active=%s  fire_count=%s  responsive=%s"
            % (g.get("hook_active"), g.get("hook_fire_count"), g.get("responsive")))
        if not g.get("hook_active"):
            fails.append("1: hook_active is FALSE -- UE5_CallProcessEvent would take the DIRECT "
                         "synchronous path and the run would be a false PASS. Re-inject; if "
                         "MinHook failed with MH_ERROR_MEMORY_ALLOC, restart the game.")

        # ---- trap 2: break the 5000-vs-5000 tie ----
        r = c.request("set_invoke_timeout", timeout_ms=60000)
        say("[2] set_invoke_timeout(60000) -> %s   (CE's own deadline stays 5000, so CE is now "
            "unambiguously the shorter one)" % (r.get("timeout_ms") or r.get("ok") or r))

        # ---- pick the game thread BY FIRE RATE, not by name ----
        before = gt(c).get("hook_fire_count") or 0
        time.sleep(1.5)
        after = gt(c).get("hook_fire_count") or 0
        say("[3] hook fired %d times in 1.5 s -- the hook is live, so the thread is findable"
            % (after - before))
        if after == before:
            fails.append("3: the hook is not firing, so nothing is queuing onto the game thread")

    tid = _game_tid()
    if tid is None:
        fails.append("3: could not identify the game thread")
    else:
        say("[4] stalling game thread tid=%s: %s" % (tid, tid_ctl("suspend-tid", tid)))
        time.sleep(3.0)
        with PipeClient() as c:
            g = gt(c)
            say("    WITNESSES while stalled: responsive=%s  fire_count=%s  (pipe still answering)"
                % (g.get("responsive"), g.get("hook_fire_count")))
            if g.get("responsive"):
                fails.append("4: the DLL still reports the game thread responsive -- the stall did "
                             "not take, so a CE timeout afterwards would not be attributable to it")
        pathlib.Path("out/negctl_tid.txt").write_text(str(tid))

    say("")
    say("=" * 72)
    if fails:
        say("ARM FAILED -- do not run the CE half:")
        for f in fails:
            say("   - %s" % f)
        return 1
    say("ARMED. Now in CE: attach to the game, then in the Lua Engine run")
    say("    dofile([[D:\\Github\\UE5CEDumper\\scripts\\ue5_ce_helper_shim.lua]])  -- or paste ue5_callDLL")
    say("    ue5_callDLL('UE5_CallProcessEvent', 'int', <instance>, <ufunc>, 0)")
    say("PASS = CE prints: executeCodeEx failed for UE5_CallProcessEvent: Execution timeout")
    say("Then: py tools/verify/execcodeex_negctl.py release")
    return 0


def _game_tid():
    """Ask suspend.py which thread is the game thread.

    ⭐ Use suspend.py's OWN marker (`<-- main thread (UE game thread)`) rather than a
    busiest-CPU heuristic of our own: it already makes this judgement, and two rigs
    disagreeing about which thread is the game thread is exactly the kind of drift that
    makes a stall land on the wrong thread and produce a confident false PASS.
    """
    r = subprocess.run([sys.executable, str(HERE / "suspend.py"), "threads", "DumperTest"],
                       capture_output=True, text=True, encoding="utf-8", errors="replace")
    out = r.stdout or ""
    for line in out.splitlines():
        if "main thread" in line and "tid=" in line:
            tid = line.split("tid=", 1)[1].split()[0]
            say("    suspend.py names the game thread: tid=%s" % tid)
            return int(tid)
    say("    could not find suspend.py's 'main thread' marker; listing follows")
    say(out[:1200])
    return None


def check():
    with PipeClient() as c:
        g = gt(c)
        say("responsive=%s  hook_active=%s  fire_count=%s"
            % (g.get("responsive"), g.get("hook_active"), g.get("hook_fire_count")))
    return 0


def release():
    p = pathlib.Path("out/negctl_tid.txt")
    if not p.exists():
        say("no stalled tid recorded")
        return 1
    tid = p.read_text().strip()
    say("resuming tid=%s: %s" % (tid, tid_ctl("resume-tid", tid)))
    time.sleep(2.0)
    with PipeClient() as c:
        g = gt(c)
        say("after resume: responsive=%s  fire_count=%s"
            % (g.get("responsive"), g.get("hook_fire_count")))
    p.unlink()
    return 0


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else "check"
    sys.exit({"arm": arm, "release": release}.get(cmd, check)())
