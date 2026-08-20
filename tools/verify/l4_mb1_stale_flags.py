r"""L4 / MB1 — the mailbox `functionFlags` field must NOT decide the invoke route.

    py tools/verify/l4_mb1_stale_flags.py

THE DEFECT (fixed 2026-08-19, no contract move). `CMD_INVOKE`'s documented inputs are
`instanceAddr` / `ufuncAddr` / `paramsData`. `functionFlags` at `+0x024` is a DLL-filled **output**,
so at invoke time it holds whatever the PREVIOUS command left there — `CMD_FIND_FUNCTION` leaves the
flags of the function *it* resolved, and `CMD_LIST_FUNCTIONS` / `CMD_LIST_INSTANCES` overwrite it
with a PAGE COUNT. The generated form's FIRE button re-issues `CMD_INVOKE` **without** re-running
`CMD_FIND_FUNCTION`, so a stale `Native|Static` from an earlier Kismet helper sent a *stateful actor*
UFunction down the direct off-game-thread path. The fix re-reads the real flags from the UFunction
that `ufuncAddr` names (`Ubel::ResolveFunctionInfo`) and routes on those.

WHY THE ROW'S UI RECIPE IS NOT WHAT THIS RUNS. The row prescribes two generated Invoke forms in
Cheat Engine and a FIRE-B-then-FIRE-A dance to make the staleness happen *by accident*. Writing the
stale value into `+0x024` directly is the same input with none of the ceremony, is exact (the poison
is a value chosen to be recognisable in the log), and needs no CE at all.

⭐ THE POINT OF FREEZING THE GAME THREAD: it turns the route into an OBSERVABLE, not just a log line.
A log message only proves the DLL *noticed*. With the UE game thread suspended:
    fast path  (direct)              -> still works, because it never needed that thread
    GameThreadDispatch (correct)     -> times out at -5, because nothing can service the queue
So a stale `Native|Static` on a stateful function would return **0**, and correct routing returns
**-5**. The regression would be visible even if the WARN were deleted.

THE THREE CELLS, and what each pair isolates:
  A  genuine Native|Static helper, correct flags   -> fast path, ReturnValue 7   (row 2's assertion:
     the re-read must not COST the fast path — a `ResolveFunctionInfo` that failed would silently
     degrade every pure helper into a queued call that times out at a menu)
  B  stateful function, flags POISONED to 0x2400   -> `is STALE` WARN + routed to dispatch (-5)
  C  stateful function, flags CORRECT              -> no WARN, also -5
  B vs A isolates the ROUTE. B vs C isolates the WARN. Neither alone would do.
"""
import argparse
import pathlib
import re
import struct
import subprocess
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from pipe_client import PipeClient  # noqa: E402
from mailbox_poke import (Mem, pid_of, mailbox_addr, OFF_CMD, OFF_STATUS, OFF_RESULT,
                          OFF_INITSTATE, OFF_INSTANCE, OFF_UFUNC, OFF_ERRORMSG,
                          OFF_PARAMS, STATUS_IDLE, STATUS_DONE, STATUS_PROCESSING,
                          CMD_IDLE, INIT_READY)  # noqa: E402

OFF_FUNCFLAGS = 0x024
CMD_INVOKE = 1
FUNC_NATIVE, FUNC_STATIC = 0x400, 0x2000
POISON_FLAGS = FUNC_NATIVE | FUNC_STATIC      # 0x2400 — the exact stale value the defect inherits
LOG = pathlib.Path.home() / "AppData/Local/UE5CEDumper/Logs/DumperTest"


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + "\n")


def all_matching(needle):
    """Every line containing `needle`, across the whole log set.

    ⚠ NOT a timestamp window. A `strftime("%Y-%m-%d %H:%M:%S")` watermark has ONE SECOND of
    resolution, and these three cells run milliseconds apart -- so cell A's
    `static-native fast path` line landed inside cell B's window and the rig reported a
    FALSE FAIL ("the poisoned flags took the FAST PATH") on a run whose own `result=-5`
    proved the call had been queued. Logs only grow within a run, so a before/after COUNT
    is exact at any timing. Same defect class as slicing several growing files by line
    offset, which bit `st1_queue_drain.py` earlier.
    """
    out = []
    for f in sorted(LOG.glob("*-0.log")):
        try:
            out += [l for l in f.read_text(encoding="utf-8", errors="replace").splitlines()
                    if needle in l]
        except OSError:
            pass
    return out


class Watch:
    """Counts of each needle at a point in time; `delta` returns only what appeared since."""

    NEEDLES = ("is STALE", "INVOKE -> static-native fast path")

    def __init__(self):
        self.base = {n: len(all_matching(n)) for n in self.NEEDLES}

    def delta(self, needle):
        return all_matching(needle)[self.base[needle]:]


def invoke(m, base, inst, ufunc, flags, params=b"", timeout=25.0):
    if m.i32(base + OFF_STATUS) == STATUS_PROCESSING:
        raise SystemExit("mailbox already PROCESSING — a previous command is wedged")
    m.write(base + OFF_STATUS, struct.pack("<i", STATUS_IDLE))
    m.write(base + OFF_INSTANCE, struct.pack("<Q", inst))
    m.write(base + OFF_UFUNC, struct.pack("<Q", ufunc))
    m.write(base + OFF_PARAMS, (params + b"\x00" * 64)[:64])
    m.write(base + OFF_FUNCFLAGS, struct.pack("<I", flags))     # the field under test
    m.write(base + OFF_ERRORMSG, bytes(8))                    # else the PREVIOUS cell's
                                                                #  message reads as this one's
    m.write(base + OFF_RESULT, struct.pack("<i", 0x7FFFFFFF))   # poison: 0 must be WRITTEN
    m.write(base + OFF_CMD, struct.pack("<i", CMD_INVOKE))      # trigger LAST
    t0 = time.time()
    while time.time() - t0 < timeout:
        if m.i32(base + OFF_STATUS) == STATUS_DONE:
            r = dict(result=m.i32(base + OFF_RESULT),
                     out=m.read(base + OFF_PARAMS, 32),
                     err=m.read(base + OFF_ERRORMSG, 256).split(b"\x00")[0]
                        .decode("utf-8", "replace"),
                     ms=(time.time() - t0) * 1000.0)
            m.write(base + OFF_CMD, struct.pack("<i", CMD_IDLE))
            return r
        time.sleep(0.004)
    raise SystemExit("TIMEOUT waiting for the mailbox (status=%#x)" % m.i32(base + OFF_STATUS))


def game_tid():
    r = subprocess.run([sys.executable, str(HERE / "suspend.py"), "threads", "DumperTest"],
                       capture_output=True, text=True, errors="replace")
    mm = re.search(r"tid=(\d+)\s+cpu=.*main thread", r.stdout)
    return int(mm.group(1)) if mm else None


def freeze(tid, on):
    subprocess.run([sys.executable, str(HERE / "suspend.py"),
                    "suspend-tid" if on else "resume-tid", "DumperTest", str(tid)],
                   capture_output=True, text=True, errors="replace")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("process", nargs="?", default="DumperTest")
    a = ap.parse_args()
    fails = []
    pid = pid_of(a.process)
    base = mailbox_addr(a.process)
    m = Mem(pid)
    if m.i32(base + OFF_INITSTATE) != INIT_READY:
        raise SystemExit("mailbox initState is not READY")
    say("pid %d  mailbox %#x" % (pid, base))

    with PipeClient() as c:
        c.assert_build()
        c.ensure_scanned()
        rr = c.request("list_all_functions", limit=20000, game_only=False)
        fl = (rr.get("data", rr)).get("functions") or []
        helper = next((f for f in fl if f.get("func_name") == "Add_IntInt"), None)
        # A STATEFUL target: neither Native nor Static, 0 params, and belonging to a
        # class that actually has a live instance -- an invoke against a class with no
        # instance would fail for the wrong reason.
        # ⚠ `find_instances` without `exact_match` is a NAME SUBSTRING match, so asking for
        # "Actor" returns ActorSequence, ActorElement*, ActorPartitionSubsystem... The first
        # run of this rig took the first hit and invoked `Actor.UserConstructionScript`
        # against a `UActorSequence`. The ROUTE measurement survived that (the decision is
        # made from ufuncAddr's flags alone, before any call, and both cells timed out
        # without executing) -- but a wrong-type call left QUEUED can drain later and run an
        # AActor method against a non-actor. Filter on the reported `class`, not the name.
        live = {}
        for cls in ("Actor", "DumperTestActor", "Pawn"):
            ins = [i for i in (c.request("find_instances", class_name=cls, max_results=200)
                               .get("instances") or [])
                   if i.get("class") == cls
                   and not str(i.get("name", "")).startswith("Default__")]
            if ins:
                live[cls] = ins[0]["addr"]
        stateful = next((f for f in fl
                         if f.get("num_parms", 0) == 0
                         and not (f.get("function_flags", 0) & (FUNC_NATIVE | FUNC_STATIC))
                         and f.get("class_name") in live), None)
    if not helper or not stateful:
        say("FAIL: need Add_IntInt and a 0-param stateful function on a class with a live "
            "instance; got %r / %r" % (bool(helper), bool(stateful)))
        return 1

    hfn = int(helper["func_addr"], 16)
    sfn = int(stateful["func_addr"], 16)
    sinst = int(live[stateful["class_name"]], 16)
    hinst = int(live.get("Actor") or list(live.values())[0], 16)
    say("")
    say("fast-path target : %s.%s  flags=0x%08X  @%#x"
        % (helper["class_name"], helper["func_name"], helper["function_flags"], hfn))
    say("stateful target  : %s.%s  flags=0x%08X  @%#x  on instance %#x"
        % (stateful["class_name"], stateful["func_name"], stateful["function_flags"],
           sfn, sinst))
    say("poison to plant  : 0x%08X (Native|Static)" % POISON_FLAGS)

    tid = game_tid()
    if tid is None:
        say("FAIL: could not identify the UE game thread")
        return 1
    say("")
    say("freezing the UE game thread (tid %d) -- this is what makes the ROUTE observable" % tid)
    freeze(tid, True)
    try:
        # ---------------------------------------------------------------- A
        w = Watch()
        say("")
        say("== A (row 2): a genuine Native|Static helper must STILL take the fast path ==")
        ra = invoke(m, base, hinst, hfn, helper["function_flags"],
                    struct.pack("<iii", 3, 4, 0))
        rv = struct.unpack_from("<i", ra["out"], 8)[0]
        say("   result=%d  ReturnValue=%d  %.0f ms  %s"
            % (ra["result"], rv, ra["ms"], ra["err"] or ""))
        fast = w.delta("INVOKE -> static-native fast path")
        stale = w.delta("is STALE")
        say("   'INVOKE -> static-native fast path' : %d   <-- must be >=1" % len(fast))
        say("   'is STALE'                          : %d   <-- must be 0 (flags were correct)"
            % len(stale))
        if not fast:
            fails.append("A: the helper did NOT take the fast path -- the re-read has COST it, "
                         "which would degrade every pure helper into a queued call")
        if ra["result"] == -5:
            fails.append("A: the helper timed out (-5) with the game thread frozen -- it was "
                         "queued, not called directly")
        if rv != 7:
            fails.append("A: Add_IntInt(3,4) returned %d, not 7" % rv)
        if stale:
            fails.append("A: an 'is STALE' WARN fired on correct flags")

        # ---------------------------------------------------------------- B
        w = Watch()
        say("")
        say("== B: the SAME field poisoned to Native|Static on a STATEFUL function ==")
        rb = invoke(m, base, sinst, sfn, POISON_FLAGS)
        say("   result=%d  %.0f ms  %s" % (rb["result"], rb["ms"], rb["err"] or ""))
        stale = w.delta("is STALE")
        fast = w.delta("INVOKE -> static-native fast path")
        say("   'is STALE' WARN : %d" % len(stale))
        for l in stale[:2]:
            say("      " + l.strip()[:200])
        say("   fast-path lines : %d   <-- MUST be 0; the poison must not decide the route"
            % len(fast))
        if not stale:
            fails.append("B: no 'is STALE' WARN -- the mailbox field was not compared against "
                         "the re-read flags")
        else:
            txt = stale[-1]
            if ("0x%08X" % POISON_FLAGS) not in txt:
                fails.append("B: the WARN does not name the stale value 0x%08X" % POISON_FLAGS)
            if ("0x%08X" % stateful["function_flags"]) not in txt:
                fails.append("B: the WARN does not name the re-read value 0x%08X"
                             % stateful["function_flags"])
            if stateful["func_name"] not in txt:
                fails.append("B: the WARN does not name the function")
        if fast:
            fails.append("B: the poisoned flags took the FAST PATH -- this is the defect, a "
                         "stateful UFunction ran off the game thread")
        if rb["result"] != -5:
            fails.append("B: expected -5 (queued, thread frozen) but got %d; if it is 0 the call "
                         "went direct" % rb["result"])
        else:
            say("   OK: result -5 = it really was queued for the game thread, not called direct")

        # ---------------------------------------------------------------- C
        w = Watch()
        say("")
        say("== C (control): the same function with CORRECT flags -- warn must vanish, route not ==")
        rc = invoke(m, base, sinst, sfn, stateful["function_flags"])
        stale = w.delta("is STALE")
        fast = w.delta("INVOKE -> static-native fast path")
        say("   result=%d   'is STALE': %d (must be 0)   fast-path: %d (must be 0)"
            % (rc["result"], len(stale), len(fast)))
        if stale:
            fails.append("C: the WARN fired even with correct flags -- it is not keyed to the "
                         "mismatch, so B's WARN proved nothing")
        if rc["result"] != -5:
            fails.append("C: expected -5 like B, got %d -- the -5 in B may not be about routing"
                         % rc["result"])
        else:
            say("   OK: same -5 as B, so B's -5 is the ROUTE and not the poison")
    finally:
        freeze(tid, False)
        say("")
        say("resumed the game thread")

    say("")
    for x in fails:
        say("FAIL: %s" % x)
    if not fails:
        say("PASS (L4/MB1 rows 1 and 2)")
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
