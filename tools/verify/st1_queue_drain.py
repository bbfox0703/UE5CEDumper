r"""ST1 steps 3 + 4 -- the queued request must NOT be drained by our own next call.

    py tools/verify/st1_queue_drain.py

THE DEFECT (pre-3205). An invoke that times out stays QUEUED on purpose, with its own owned
parameter copy, expecting a later drain. The drain's only gate was "is the queue non-empty",
and it ran inside `HookedProcessEvent` -- on whatever thread got there first. So the next call
we issued ourselves executed that abandoned, caller-judged-UNSAFE UFunction on the wrong thread.
Step 3 is the row's "THE ONE THAT MATTERS".

HOW THIS IS STAGED WITHOUT A HUMAN
  The row says "a paused/menu game". Suspending the UE game thread is the same condition and is
  scriptable: nothing can service the queue, so a game-thread invoke is guaranteed to time out and
  stay queued. It also silences the ~121 fires/s background, which is what makes the counter
  readable at all (see st1_direct_call.py).

  target for the QUEUED call : a UFunction that is NEITHER Static NOR Native, so it must go to the
                               game thread rather than the static-native fast path.
  target for the SECOND call : KismetMathLibrary.Add_IntInt -- Native|Static, the fast path the
                               row names.

WHAT WOULD A BROKEN BUILD LOOK LIKE
  The second call would drain the queued request through the detour, so `hook_fire_count` would
  move while the game thread is still frozen. With the fix it cannot move at all.

CONTROLS, all asserted rather than assumed:
  * the background really is silent while suspended (else a delta means nothing);
  * the first invoke really did TIME OUT (else there is nothing queued and step 3 is vacuous);
  * the second invoke really SUCCEEDED (else "no drain" just means "no call").
"""
import json
import pathlib
import re
import subprocess
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient  # noqa: E402

LOG = pathlib.Path.home() / "AppData/Local/UE5CEDumper/Logs" / "DumperTest"
HERE = pathlib.Path(__file__).resolve().parent


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + "\n")


def game_tid():
    r = subprocess.run([sys.executable, str(HERE / "suspend.py"), "threads", "DumperTest"],
                       capture_output=True, text=True, errors="replace")
    m = re.search(r"tid=(\d+)\s+cpu=.*main thread", r.stdout)
    return int(m.group(1)) if m else None


def suspend(tid, on):
    subprocess.run([sys.executable, str(HERE / "suspend.py"),
                    "suspend-tid" if on else "resume-tid", "DumperTest", str(tid)],
                   capture_output=True, text=True, errors="replace")


def fires(c):
    # The pipe replies are FLAT -- there is no "data" envelope. `.get("data", {})`
    # therefore yields an empty dict and every field reads as None, which looks
    # exactly like "the DLL did not report it".
    r = c.request("get_diagnostics")
    d = r.get("data", r)
    return (d.get("game_thread") or {}).get("hook_fire_count")


def loglen():
    """A TIMESTAMP watermark, not a line count.

    Counting lines across several *-0.log files and slicing the concatenation is
    wrong the moment more than one of them grows: new lines land in the middle of
    the list, not at the end, so the slice misses them. That is why a first run
    reported 0 'enqueued invoke' lines while the log plainly contained one.
    """
    return time.strftime("%Y-%m-%d %H:%M:%S")


def newlines(mark):
    out = []
    for f in sorted(LOG.glob("*-0.log")):
        try:
            for l in f.read_text(encoding="utf-8", errors="replace").splitlines():
                if l.startswith("[") and l[1:20] >= mark:
                    out.append(l)
        except OSError:
            pass
    return out


def main():
    fails = []
    run_start = time.strftime("%Y-%m-%d %H:%M:%S")
    FUNC_NATIVE, FUNC_STATIC = 0x400, 0x2000
    tid = game_tid()
    if tid is None:
        say("FAIL: could not identify the UE game thread")
        return 1

    with PipeClient() as c:
        c.assert_build()
        c.ensure_scanned()
        _r = c.request("list_all_functions", limit=20000, game_only=False)
        fl = (_r.get("data", _r)).get("functions") or []
        say("functions listed: %d" % len(fl))
        queued_fn = next((f for f in fl
                          if f.get("num_parms", 0) == 0
                          and not (f.get("function_flags", 0) & FUNC_STATIC)
                          and not (f.get("function_flags", 0) & FUNC_NATIVE)), None)
        fast_fn = next((f for f in fl if f.get("func_name") == "Add_IntInt"), None)
        if not queued_fn or not fast_fn:
            say("FAIL: need one non-static/non-native fn and Add_IntInt; got %r / %r"
                % (bool(queued_fn), bool(fast_fn)))
            return 1
        say("queued-path target : %s.%s (flags 0x%X)"
            % (queued_fn["class_name"], queued_fn["func_name"], queued_fn["function_flags"]))
        say("fast-path target   : %s.%s (flags 0x%X)"
            % (fast_fn["class_name"], fast_fn["func_name"], fast_fn["function_flags"]))

        c.request("set_invoke_timeout", timeout_ms=1500)
        say("invoke timeout set to 1500 ms")

    say("")
    say("suspending the UE game thread (tid %d) -- the scriptable form of 'paused/menu'" % tid)
    suspend(tid, True)
    try:
        with PipeClient() as c:
            n0 = fires(c)
            time.sleep(2.0)
            n1 = fires(c)
            say("control: background while frozen  %s -> %s  (must be equal)" % (n0, n1))
            if n0 is None or n1 != n0:
                fails.append("background not silenced (%s -> %s)" % (n0, n1))
                raise SystemExit

            mark = loglen()
            say("")
            say("STEP 3a: fire the game-thread invoke -- it must TIME OUT and stay queued")
            t0 = time.time()
            r = c.request("invoke_function", class_name=queued_fn["class_name"],
                          func_name=queued_fn["func_name"], parms_size=0, params_hex="")
            d = r.get("data", r)
            say("   ok=%s result=%s message=%r  (%.2f s)"
                % (d.get("ok"), d.get("result"), str(d.get("message"))[:70], time.time() - t0))
            enq = [l for l in newlines(mark) if "enqueued invoke" in l]
            say("   'enqueued invoke' log lines: %d" % len(enq))
            timed_out = (d.get("result") not in (0, None)) or bool(enq)
            if not timed_out:
                fails.append("the first invoke did NOT time out -- nothing is queued, step 3 vacuous")
                raise SystemExit

            n2 = fires(c)
            mark2 = loglen()
            say("")
            say("STEP 3b: fire a DIRECT (trampoline) invoke -- it must NOT drain the queue")
            # direct_call=TRUE is the call that could drain. Measured 2026-08-20: with the
            # game thread frozen an ORDINARY static-native invoke also returns -5
            # ("game-thread dispatch timeout", game_thread_stalled=true) -- so it never
            # reaches ProcessEvent and never had the opportunity to drain anything, which
            # would make a 0-delta vacuous. A direct_call DOES reach PE on the calling
            # thread, which is exactly the route the pre-3205 drain took.
            r2 = c.request("invoke_function", class_name=fast_fn["class_name"],
                           func_name="Add_IntInt", parms_size=12,
                           params_hex="03000000" + "04000000" + "00000000",
                           direct_call=True)
            d2 = r2.get("data", r2)
            rv = None
            h = d2.get("result_hex") or ""
            if len(h) >= 24:
                rv = int.from_bytes(bytes.fromhex(h[16:24]), "little")
            say("   ok=%s  ReturnValue=%s   (must be 7, else 'no drain' proves nothing)"
                % (d2.get("ok"), rv))
            if rv != 7:
                fails.append("the static-native invoke did not succeed (rv=%r)" % rv)
            n3 = fires(c)
            say("   hook_fire_count %s -> %s   delta = %s   <-- MUST be 0" % (n2, n3, n3 - n2))
            if n3 != n2:
                fails.append("ST1-3: the queue WAS drained by our own call (+%d fires)" % (n3 - n2))
            drained = [l for l in newlines(mark2) if "queued" in l.lower()]
            for l in drained[:4]:
                say("      " + l.strip()[:140])
    except SystemExit:
        pass
    finally:
        suspend(tid, False)
        say("")
        say("STEP 4: resumed the game thread -- the queued request must now run")

    time.sleep(3.0)
    tail = [l for l in newlines(run_start) if "GameThreadDispatch" in l]
    say("   GameThreadDispatch lines from this run:")
    for l in tail[-8:]:
        say("      " + l.strip()[:150])
    done = [l for l in tail if "invoke completed" in l]
    say("   'invoke completed' after resume: %d" % len(done))
    if not done:
        say("   NOTE: the completion line is written by the WAITING pipe thread, which has")
        say("         already timed out and gone -- so an abandoned request draining leaves no")
        say("         log line. Step 4 is therefore NOT decidable from this signal.")

    say("")
    for x in fails:
        say("FAIL: %s" % x)
    if not fails:
        say("PASS")
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
