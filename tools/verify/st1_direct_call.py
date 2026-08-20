r"""ST1 steps 1 + 2 (and PEHOOK step 8's invoke gate) over the pipe, with no UI.

    py tools/verify/st1_direct_call.py

THE CLAIM (`ST1`, build 3205). Our own direct calls must stop re-entering our own
ProcessEvent detour. The two predicates are unit-pinned; the ROUTING is not, and cannot be —
nothing offline can observe which address a live vtable holds, and no test target compiles
`Stark.cpp` or `Frieren.cpp`.

The row names the cheapest decisive evidence itself: **grep by FORMAT STRING**, never line
number — `via trampoline — not re-entering our hook` (the new path) versus the older
`(caller-asserted safe)` (the fail-open path, which step 5 wants on an overriding class).

WHAT THIS CHECKS
  gate  PEHOOK step 8 — `Add_IntInt(3, 4)` must return **7**. Everything below is meaningless
        against a host whose invoke does not work at all, so this runs first and stops on failure.
  ST1-1 the same call with `direct_call: true` must log `via trampoline`.
  ST1-2 `hook_fire_count` must NOT be advanced BY OUR OWN CALL.

⚠ THE TRAP IN STEP 2, and why this rig measures a rate rather than a difference.
  A running game fires ProcessEvent continuously — DumperTest at `t.MaxFPS 15` logs thousands of
  fires a minute — so `count_after > count_before` is guaranteed whether or not our call entered
  the detour, and a naive before/after difference PASSES a broken build. This samples the
  background rate over an idle window first, then compares the delta across the invoke against
  that rate. Our single call can only be detected if it adds a spike well outside the idle band.
"""
import json
import pathlib
import re
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient  # noqa: E402

LOG = pathlib.Path.home() / "AppData/Local/UE5CEDumper/Logs"


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + "\n")


def find_fn(c, cls, fn):
    # game_only defaults to TRUE and KismetMathLibrary is an ENGINE class, so the
    # obvious call silently returns 3,142 of 9,806 functions and Add_IntInt is not
    # among them -- which reads as "this host has no invoke target".
    r = c.request("list_all_functions", limit=20000, game_only=False)
    for f in (r.get("data", r).get("functions") or []):
        if f.get("func_name") == fn and (cls is None or f.get("class_name") == cls):
            return f
    return None


def fire_count(c):
    # get_diagnostics, NOT get_stats -- get_stats returns {ok:false,error} here and its
    # missing game_thread block silently reads as None, which made a first version of
    # this rig print PASS for step 2 having measured nothing at all.
    r = c.request("get_diagnostics")
    d = r.get("data", r)
    gt = d.get("game_thread") or {}
    return gt.get("hook_fire_count"), gt.get("hook_active"), d


def log_lines(proc):
    out = []
    d = LOG / proc
    for f in list(d.glob("pipe-0.log")) + list(d.glob("init-0.log")):
        try:
            out += [(f.name, l) for l in f.read_text(encoding="utf-8", errors="replace").splitlines()]
        except OSError:
            pass
    return out


def _game_thread_tid():
    """The UE game thread is the process MAIN thread (earliest created)."""
    import subprocess
    r = subprocess.run([sys.executable, str(pathlib.Path(__file__).with_name("suspend.py")),
                        "threads", "DumperTest"], capture_output=True, text=True, errors="replace")
    m = re.search(r"tid=(\d+)\s+cpu=.*main thread", r.stdout)
    return int(m.group(1)) if m else None


def _suspend(tid, on):
    import subprocess
    subprocess.run([sys.executable, str(pathlib.Path(__file__).with_name("suspend.py")),
                    "suspend-tid" if on else "resume-tid", "DumperTest", str(tid)],
                   capture_output=True, text=True, errors="replace")


def main():
    fails = []
    with PipeClient() as c:
        c.assert_build()
        c.ensure_scanned()
        proc = "DumperTest"

        say("== gate (PEHOOK step 8): Add_IntInt(3,4) must be 7 ==")
        f = find_fn(c, "KismetMathLibrary", "Add_IntInt") or find_fn(c, None, "Add_IntInt")
        if not f:
            say("   FAIL: Add_IntInt not found on this host -- gate cannot run")
            return 1
        say("   %s.%s @ %s (parms %sB)"
            % (f.get("class_name"), f.get("func_name"), f.get("func_addr"), f.get("parms_size")))
        # A=3, B=4, ReturnValue -- three int32 slots, little-endian.
        params = "03000000" + "04000000" + "00000000"
        r = c.request("invoke_function", class_name=f["class_name"], func_name="Add_IntInt",
                      parms_size=12, params_hex=params)
        d = r.get("data", r)
        say("   reply: %s" % json.dumps(d)[:240])
        out = d.get("params_hex") or d.get("result_hex") or ""
        val = None
        if isinstance(out, str) and len(out) >= 24:
            val = int.from_bytes(bytes.fromhex(out[16:24]), "little")
        say("   ReturnValue = %s   <-- must be 7" % val)
        if val != 7:
            fails.append("gate: Add_IntInt(3,4) returned %r, not 7" % val)
            say("   stopping: the rest is meaningless against a host whose invoke does not work")
            for x in fails:
                say("FAIL: %s" % x)
            return 1

        say("")
        say("== ST1 step 2: measure the BACKGROUND fire rate first (the trap) ==")
        n0, active, _ = fire_count(c)
        say("   hook_active=%s  hook_fire_count=%s" % (active, n0))
        if n0 is None:
            say("   FAIL: hook_fire_count unavailable -- step 2 cannot be measured, and a")
            say("         'no jump observed' here would be an artefact of the read, not a result.")
            fails.append("ST1-2: hook_fire_count unreadable")
            for x in fails:
                say("FAIL: %s" % x)
            return 1
        if not active:
            say("   FAIL: the PE hook is not active -- nothing can enter the detour, so step 2")
            say("         would pass vacuously. Force the hook with an invoke first.")
            fails.append("ST1-2: hook not active")
            for x in fails:
                say("FAIL: %s" % x)
            return 1
        time.sleep(5.0)
        n1, _, _ = fire_count(c)
        idle_rate = (n1 - n0) / 5.0 if (n0 is not None and n1 is not None) else None
        say("   after 5 s idle: %s   -> background rate = %.1f fires/s" % (n1, idle_rate or -1))

        say("")
        say("== ST1 step 1: invoke with direct_call=true ==")
        before = len(log_lines(proc))
        # The window must span EXACTLY what the counter spans. n2 and n3 are read by
        # get_diagnostics round trips that themselves take milliseconds, during which the
        # game keeps firing; timing only the invoke and comparing against n3-n2 charges
        # those round trips to our call and manufactures an excess. Bracket the whole thing.
        t0 = time.time()
        n2, _, _ = fire_count(c)
        r = c.request("invoke_function", class_name=f["class_name"], func_name="Add_IntInt",
                      parms_size=12, params_hex=params, direct_call=True)
        n3, _, _ = fire_count(c)
        dt = time.time() - t0
        say("   direct_call reply ok=%s  (window incl. both counter reads: %.3f s)" % (r.get("data", r).get("ok"), dt))

        new = [l for _, l in log_lines(proc)[before:]]
        tramp = [l for l in new if "via trampoline" in l]
        safe = [l for l in new if "caller-asserted safe" in l]
        say("   new log lines: %d" % len(new))
        say("   'via trampoline'         : %d   <-- ST1 step 1 wants >=1" % len(tramp))
        say("   '(caller-asserted safe)' : %d   (step 5's fail-open path; 0 expected here)" % len(safe))
        for l in tramp[:3]:
            say("      " + l.strip()[:150])
        if not tramp:
            fails.append("ST1-1: no 'via trampoline' line for a direct_call invoke")

        delta = (n3 - n2) if (n2 is not None and n3 is not None) else None
        expect = (idle_rate or 0) * dt
        say("")
        say("   fires across the invoke : %s over %.3f s" % (delta, dt))
        say("   expected from idle rate : %.1f" % expect)
        say("   excess attributable to our call: %.1f   <-- ST1 step 2 wants ~0, never +1 per call"
            % ((delta or 0) - expect))
        if delta is not None and delta > expect + 3:
            fails.append("ST1-2: %d fires across the invoke vs %.1f expected from the idle rate"
                         % (delta, expect))

    # ------------------------------------------------------------------
    # ST1 step 2, the NOISE-FREE form. At ~120 fires/s a single stray +1 hides
    # inside the background, so the rate comparison above can only exclude a
    # large excess. Suspending the UE game thread drops the background to ZERO
    # -- and a `direct_call` invoke does not need that thread, which is the whole
    # point of the flag -- so any movement at all is our call and nothing else.
    # ------------------------------------------------------------------
    say("")
    say("== ST1 step 2 (noise-free): background silenced by suspending the game thread ==")
    tid = _game_thread_tid()
    if tid is None:
        say("   SKIP: could not identify the UE game thread")
    else:
        say("   suspending tid %d" % tid)
        _suspend(tid, True)
        try:
            with PipeClient() as c2:
                a0, _, _ = fire_count(c2)
                time.sleep(2.0)
                a1, _, _ = fire_count(c2)
                say("   background over 2 s with the thread frozen: %s -> %s (delta %s)"
                    % (a0, a1, (a1 - a0) if None not in (a0, a1) else "?"))
                if a0 is None or a1 != a0:
                    say("   FAIL: background is not silent (%s -> %s); the probe is not noise-free"
                        % (a0, a1))
                    fails.append("ST1-2 noise-free: background not silenced")
                else:
                    r = c2.request("invoke_function", class_name=f["class_name"],
                                   func_name="Add_IntInt", parms_size=12,
                                   params_hex=params, direct_call=True)
                    ok = r.get("data", r).get("ok")
                    a2, _, _ = fire_count(c2)
                    say("   direct_call with the game thread frozen: ok=%s" % ok)
                    say("   hook_fire_count %s -> %s   delta = %s   <-- MUST be 0"
                        % (a1, a2, a2 - a1))
                    if not ok:
                        fails.append("ST1-2 noise-free: the direct_call itself failed, so the "
                                     "0-delta proves nothing")
                    elif a2 != a1:
                        fails.append("ST1-2 noise-free: our own call advanced hook_fire_count "
                                     "by %d" % (a2 - a1))
        finally:
            _suspend(tid, False)
            say("   resumed tid %d" % tid)

    say("")
    for x in fails:
        say("FAIL: %s" % x)
    if not fails:
        say("PASS")
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
