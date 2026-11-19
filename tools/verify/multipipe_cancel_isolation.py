"""Does ONE client dying mid-command cancel a DIFFERENT client's in-flight scan?

    py multipipe_cancel_isolation.py            # needs DumperTest injected, see below

THE ROW. multipipe-eval 9.6 item 5: "Disconnect only ONE lane (edge) -- the other keeps
working; a bulk scan isn't wrongly cancelled by an interactive disconnect (the 9.3.5
caveat)". Closing the whole GAME mid-command does not test it: BOTH lanes die together
and the router tears both down, so a whole-process death cannot show that one lane
surviving the other's drop keeps working, and it says nothing at all about the
cancellation clause.

THE DEFECT IT DETECTS. `Fern::MonitorLoop` raises the per-command cancel
(`Tot::RequestPerCommand()`) for a broken in-flight connection. If that cancel is one
process-wide flag OR-ed into every `Tot::Requested()`, it is global by construction, and
the only argument that it is safe is a TIMING one:

    only the bulk lane runs cancellable scans, and a fast light command finishes
    before a 200ms peek catches it in-flight, so in practice this only ever fires
    for a broken bulk scan.

This rig measures that argument instead of trusting it.

⚠ WHY THIS NEEDS A SLOW COMMAND, AND WHY THE FIXTURE NORMALLY CANNOT PROVIDE ONE.
MonitorLoop peeks every 200 ms and only inspects connections whose `inFlight` is set, so
reproducing the edge needs a command that occupies its connection for LONGER than 200 ms.
On DumperTest every candidate command finishes far below that (trigger_scan / rescan
return immediately: they are async, "started": true), so on this fixture the monitor can
essentially never catch a command in flight -- exactly the condition the timing argument
assumes.

THE LEVER. The sample's own `-DumperTestMaxFPS=N` makes a frame arbitrarily long, and
`invoke_function` dispatches to the GAME THREAD and blocks until a tick, so the doomed
client can be held in-flight for as long as you like.
⛔ RUN THE HOST AT `-DumperTestMaxFPS=1`. `launch_dumpertest.py` hardcodes 15 (67 ms a
frame), which is nowhere near enough, and **2 FPS is ALSO not enough**: the invoke then
takes ~0.5 s, over the 200 ms poll, yet the kill is seen by the doomed connection's OWN
read/write path first and logged as an ordinary `PipeServer: Client disconnected`, so the
monitor never peeks a broken pipe and the run is INCONCLUSIVE, with zero `client gone
mid-command` lines. One in-flight window has to span SEVERAL polls, not just one: at
1 FPS (~1 s per invoke, ~5 polls per window) it reproduces.
⚠ That near-miss is the reason this rig fails loudly on `observed == 0` instead of
reporting a pass: at 2 FPS it gives a clean, plausible, completely meaningless green.

THE VICTIM must run a command that actually polls the flag, or a leaked cancel is
invisible. `list_enums` does (`if ((i & 0xFFF) == 0 && Tot::Requested())` in Fern.cpp)
and returns ~720 KB, so a truncated reply is obvious by size alone.

READING THE RESULT. The victim's replies are sized before and after the kill:
  * every post-kill reply the same size as the pre-kill baseline -> cancel did NOT leak
  * any short or failed reply, with `client gone mid-command` in pipe-0.log at that
    moment -> a foreign client's death cancelled this client's scan. That is the defect.
⚠ `client gone mid-command` ABSENT from the log means the kill was never observed
in-flight and the run measured NOTHING -- it is not a pass. The rig says so rather than
reporting a green.

⭐ THE PARALLEL HALF NEEDS A REAL TITLE. `list_enums` walks inline on the connection's own
thread, so a fixture run only ever proves the thread that SERVES a command. Aura's heavy
scans run through ParallelGObjectsScan, whose workers are spawned std::threads inheriting
no thread_local. The fixture cannot reach that: none of its scans occupies a connection
for the 200 ms the monitor needs, and -DumperTestMaxFPS does not help because it
lengthens a GAME-THREAD dispatch and these scans never touch the game thread.

On a large title such as OCTOPATH TRAVELER (UE 4.18, ~400k objects) the host's own
commands are slow enough and no lever is needed. Victim `begin_value_scan`
(-> Radar/ScanForValue -> ParallelGObjectsScan), doomed `list_all_functions`
(comfortably over the poll). A build without the fix there gives short `ok: true`
replies; a run counts as a negative only when it logged `client gone mid-command`, so
the monitor genuinely fired -- not a missed trigger:

    py multipipe_cancel_isolation.py --process Octopath_Traveler-Win64-Shipping        --victim begin_value_scan        --victim-args '{"data_type":"Int32","scan_type":"Exact","value":"1",
                       "game_only":false,"max_results":50000}'        --doomed list_all_functions --doomed-args '{}'

⚠ Pick the victim for a STABLE reply size, not just a big one: value-scan replies on a
live game vary by a few bytes across runs, which is why "short" is a RATIO of the
baseline and not an equality test. A cancelled scan collapses by three orders of
magnitude; nothing else comes close to half.

⛔ THE SEVERITY IS NOT THE TRUNCATION, IT IS THAT THE TRUNCATED REPLIES SAY `ok: true`.
A caller gets a few dozen bytes where 720 KB was due and cannot tell it from a complete
answer. The window is bounded because `ReevaluatePerCommandCancel` clears the latch once
no raiser is live -- so the defect is silent partial data loss in a narrow window, not a
wedge.
"""
import json
import os
import re
import subprocess
import sys
import tempfile
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pipe_client import PipeClient  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

SCRATCH = os.path.dirname(os.path.abspath(__file__))
VICTIM_CMD = "list_enums"          # polls Tot::Requested(); ~720 KB reply
DOOMED_FN = "Linie_Marker"         # cheap UFUNCTION; the COST is the game-thread wait


def parse_args(argv):
    """Fixture defaults, overridable for a REAL host.

    The fixture path needs the -DumperTestMaxFPS lever because nothing there occupies a
    connection for 200 ms. A big title needs no lever at all -- its own scans are slow --
    but it has no DumperTestActor, so the doomed client cannot invoke a UFUNCTION. Hence
    both ends are parameterised rather than the rig being forked.

        --process NAME     log folder under LOCALAPPDATA/UE5CEDumper/Logs (default DumperTest)
        --victim CMD       command the surviving client repeats (default list_enums)
        --victim-args JSON kwargs for it
        --doomed CMD       command the doomed client repeats instead of invoke_function
        --doomed-args JSON kwargs for it

    THE PARALLEL HALF. list_enums walks inline on the connection's own thread, so it can
    only ever prove the SERVING thread. To reach Aura::ParallelGObjectsScan -- the workers
    fixed in 257878a3 -- point --victim at find_refs_to_uobject / find_property_xrefs /
    scan_for_value / snapshot_chunk on a host big enough for ScanThreadCount to pick more
    than one thread (>= 8192 objects).
    """
    a = {"process": "DumperTest", "victim": VICTIM_CMD, "victim_args": {},
         "doomed": None, "doomed_args": {}}
    it = iter(argv)
    for tok in it:
        if tok == "--process":       a["process"] = next(it)
        elif tok == "--victim":      a["victim"] = next(it)
        elif tok == "--victim-args": a["victim_args"] = json.loads(next(it))
        elif tok == "--doomed":      a["doomed"] = next(it)
        elif tok == "--doomed-args": a["doomed_args"] = json.loads(next(it))
    return a


ARGS = parse_args(sys.argv[1:])
LOG = os.path.join(os.environ["LOCALAPPDATA"], "UE5CEDumper", "Logs",
                   ARGS["process"], "pipe-0.log")


def log_marks():
    """Timestamped 'client gone mid-command' lines currently in pipe-0.log."""
    if not os.path.exists(LOG):
        return []
    out = []
    for ln in open(LOG, encoding="utf-8", errors="replace"):
        if "client gone mid-command" in ln:
            m = re.match(r"\[([\d\-: .]+)\]", ln)
            out.append(m.group(1) if m else ln[:32])
    return out


DOOMED_SRC = r'''
import sys, time
sys.path.insert(0, r"{here}")
from pipe_client import PipeClient
with PipeClient(timeout=300.0) as c:
    insts = c.request("find_instances", class_name="DumperTestActor", max_results=5).get("instances") or []
    live = next((i for i in insts if i.get("name") == "DumperTestActor"), None)
    if not live:
        print("DOOMED: no actor", flush=True); raise SystemExit(2)
    print("DOOMED: ready", flush=True)
    while True:
        try:
            c.request("invoke_function", instance_addr=live["addr"], func_name="{fn}")
        except Exception:
            time.sleep(0.05)
'''

# Real-host variant: no fixture actor, and no FPS lever needed -- a heavy command on a
# 400k-object title occupies its connection for well over the 200 ms monitor poll on its own.
DOOMED_CMD_SRC = r'''
import sys, time, json
sys.path.insert(0, r"{here}")
from pipe_client import PipeClient
with PipeClient(timeout=300.0) as c:
    kw = json.loads(r"""{kwargs}""")
    print("DOOMED: ready", flush=True)
    while True:
        try:
            c.request("{cmd}", **kw)
        except Exception:
            time.sleep(0.05)
'''


def main():
    print("=" * 74)
    print("multipipe cancel isolation -- does a foreign client's death cancel MY scan?")
    print("=" * 74)

    with PipeClient(timeout=300.0) as victim:
        # ⛔ --no-build-assert is for ONE purpose: measuring the BEFORE state against a
        # deliberately old DLL already loaded in a host (e.g. a proxy deployed in a game
        # folder weeks ago). Never use it to "get past" a build mismatch on a run whose
        # result you intend to report as current -- that is exactly how a rig ends up
        # measuring yesterday's binary and reporting it as today's.
        if "--no-build-assert" in sys.argv[1:]:
            print("!! --no-build-assert: NOT checking the loaded build. Only valid for a "
                  "deliberate BEFORE measurement.")
        else:
            victim.assert_build()
        victim.ensure_scanned()

        # Baseline: what a healthy reply weighs, with nobody else connected.
        base = []
        for _ in range(5):
            r = victim.request(ARGS["victim"], **ARGS["victim_args"])
            base.append(len(json.dumps(r)))
        baseline = max(base)
        print(f"baseline {ARGS['victim']}: {baseline} bytes (5 samples, spread "
              f"{max(base) - min(base)})")
        if baseline < 1000:
            print("!! baseline reply is tiny -- wrong command or a dead host. ABORTING.")
            return 2

        # Start the doomed client and wait until it is genuinely looping invokes.
        # ⛔ NOT next to this script: a generated .py under tools/verify/ gets picked up
        # by the repo gates and committed by accident. Temp dir, cleaned up below.
        tmpdir = tempfile.mkdtemp(prefix="mp_cancel_")
        src = os.path.join(tmpdir, "_doomed_client.py")
        with open(src, "w", encoding="utf-8") as fh:
            if ARGS["doomed"]:
                fh.write(DOOMED_CMD_SRC.format(here=SCRATCH, cmd=ARGS["doomed"],
                                               kwargs=json.dumps(ARGS["doomed_args"])))
            else:
                fh.write(DOOMED_SRC.format(here=SCRATCH, fn=DOOMED_FN))
        doomed = subprocess.Popen([sys.executable, src], stdout=subprocess.PIPE,
                                  stderr=subprocess.STDOUT, text=True,
                                  encoding="utf-8", errors="replace")
        ready = False
        t0 = time.time()
        while time.time() - t0 < 60:
            line = doomed.stdout.readline()
            if not line:
                break
            print("   " + line.rstrip())
            if "ready" in line:
                ready = True
                break
        if not ready:
            print("!! doomed client never became ready. ABORTING.")
            doomed.kill()
            return 2

        marks_before = len(log_marks())
        time.sleep(1.5)          # let it get well inside an invoke

        # Kill it hard, mid-invoke, then hammer the victim while the monitor reacts.
        print(f"killing doomed client pid {doomed.pid} mid-invoke...")
        subprocess.run(["taskkill", "/F", "/PID", str(doomed.pid)],
                       capture_output=True)

        after = []
        t0 = time.time()
        while time.time() - t0 < 8.0:
            try:
                r = victim.request(ARGS["victim"], **ARGS["victim_args"])
                after.append((round(time.time() - t0, 2), len(json.dumps(r)), bool(r.get("ok")),
                              bool(r.get("truncated"))))
            except Exception as exc:                       # noqa: BLE001
                after.append((round(time.time() - t0, 2), -1, False, False))
                print(f"   victim request FAILED: {str(exc)[:90]}")

        marks = log_marks()
        observed = len(marks) - marks_before

        # ⚠ RATIO, not "< baseline". On a live game a victim command's reply wobbles by a
        # few bytes run to run, and an exact-equality test would call that truncation. A
        # CANCELLED scan does not come back 1% short -- it collapses by orders of magnitude
        # (tens of bytes against a ~720 KB baseline). Half the baseline separates the two cleanly
        # and leaves no room for variance to masquerade as a defect.
        short = [a for a in after if a[1] < baseline * 0.5 or not a[2]]
        print()
        print(f"victim replies after the kill : {len(after)}")
        print(f"  short / failed              : {len(short)}")
        print(f"'client gone mid-command' new : {observed}")
        if short:
            first_bad = short[0][0]
            last_bad = short[-1][0]
            sizes = sorted({a[1] for a in short})
            oks = sum(1 for a in short if a[2])
            print(f"  blast radius                : {first_bad:.2f}s -> {last_bad:.2f}s "
                  f"({last_bad - first_bad:.2f}s of degraded replies)")
            print(f"  truncated reply sizes       : {sizes[:4]} vs baseline {baseline}")
            flagged = sum(1 for a in short if len(a) > 3 and a[3])
            print(f"  ⚠ of those, reported ok=true: {oks} / {len(short)}")
            print(f"  ⭐ of those, carried truncated:true: {flagged} / {len(short)}")
            if oks and not flagged:
                print("  ⚠ A TRUNCATED REPLY THAT SAYS ok=true AND CARRIES NO FLAG IS SILENT")
                print("    DATA LOSS -- the caller cannot tell it from a complete answer.")
            elif flagged == len(short):
                print("  ✅ every short reply is FLAGGED -- the loss is detectable by the")
                print("     caller (UI: AllFunctionsResult.IsPartial / ClassListResult.Truncated).")
            recovered = [a for a in after if a[0] > last_bad]
            print(f"  full-size replies after last bad: {len(recovered)}"
                  f"{' (recovered)' if recovered else ' (NEVER recovered in-window)'}")

        print()
        if observed == 0:
            print("INCONCLUSIVE -- the kill was never observed in-flight, so nothing was")
            print("  measured. Check the host really is at -DumperTestMaxFPS=2. NOT a pass.")
            return 3
        if short:
            print("*** DEFECT REPRODUCED *** a foreign client dying mid-command cancelled")
            print("  this connection's scan. Fern.cpp MonitorLoop trips the GLOBAL")
            print("  Tot::RequestPerCommand() for ANY broken in-flight connection.")
            return 1
        print("PASS -- the monitor DID observe the foreign death (see count above) and")
        print("  this connection's scans were unaffected across all samples.")
        return 0


if __name__ == "__main__":
    raise SystemExit(main())
