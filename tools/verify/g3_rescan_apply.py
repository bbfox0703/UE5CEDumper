r"""G3 steps 3 + 4 — Extra Scan -> Apply, on the one title where they are reachable at all.

    py tools/verify/g3_rescan_apply.py

⛔⛔ THE FIXTURE CLAIM BELOW IS REFUTED — DO NOT CHASE SATISFACTORY (2026-08-23).
Its `GObjects=0x0` readings are a DEAD ENGINE, not an unresolved global. Four recorded
sessions, and the object count settles it:

    07:27  GObjects=0x0                 Objects=0        <- corpse
    07:34  GObjects=0x7FFCCA033620      Objects=137372   <- same title, booted
    17:30  GObjects=0x0                 Objects=0        <- corpse
    17:57  GObjects=0x7FFCC7CE3620      Objects=137425   <- same title, booted

Satisfactory's shipping exe CANNOT be launched directly (a modal "Failed to open descriptor
file ...uproject" appears behind the window); `steam.exe -applaunch 526870` boots it properly
and it then resolves everything. `apply_rescan: Applied GEngine` has NEVER appeared in any of
its logs. That is why [G3-VOID-2026-08-20] happened, and following the paragraph below would
reproduce it.

✅ THE ROW WAS CLOSED 2026-08-23 BY STAGING INSTEAD — see [G3-STAGE-2026-08-23] in todo.md:
a one-shot skip in `Genau::FindGEngineSlot` forces the first post-gate resolve to miss, which
leaves `g_cachedGEngine == 0` — exactly the precondition `apply_rescan` guards its GEngine
second pass on. DumperTest then drives the whole path.

--- original rationale, kept for the survey it records ---

WHY THIS HAS NEVER RUN. Both steps say so themselves: *"Needs a game where something is missing to
scan for (all 34 tested games resolve GWorld, so this may not be reachable)."* Surveying every log
folder on this machine settles it — **Satisfactory is the only host with an unresolved global**:

    FactoryGameSteam-Win64-Shipping   UE506, GObjects=0x0, GNames=0x7FFCCD6AD8C0, Objects=0
    (every other title: both pointers resolved, tens of thousands of objects)

So the Extra Scan -> Apply path, which only lights up when something is missing, is reachable here
and nowhere else.

WHAT EACH STEP ASSERTS
  step 3  after Extra Scan then Apply, `offsets-0.log` must contain **exactly one**
          `ValidateAndFixOffsets: Starting` — the gate's whole purpose is that Apply does not
          re-enter validation a second time.
  step 4  `apply_rescan: Applied GEngine=0x…` must still appear **when GEngine was previously
          unresolved**. The GEngine second pass was deliberately hoisted OUT of the gated block so
          that it keeps running; step 4 is the check that the hoist survived.

⚠ THE PRECONDITION IS ASSERTED, NOT ASSUMED. If this host turns out to resolve everything on the
first pass, Apply is not reachable and any "exactly one" count would be vacuous — the rig says so
instead of reporting a pass.

⚠ Counts are taken as before/after DELTAS over the whole log, never a timestamp window: this run's
events are milliseconds apart and process start rotates `-0.log` (working-lessons §1).
"""
import json
import pathlib
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from pipe_client import PipeClient  # noqa: E402

PROC = "FactoryGameSteam-Win64-Shipping"
LOG = pathlib.Path.home() / "AppData/Local/UE5CEDumper/Logs" / PROC


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + "\n")
    # Flush: a backgrounded rig's stdout is a FILE, which Python block-buffers --
    # a long run then shows an EMPTY output file and looks hung.
    sys.stdout.flush()


def all_lines(needle):
    out = []
    for f in sorted(LOG.glob("*-0.log")):
        try:
            out += [l for l in f.read_text(encoding="utf-8", errors="replace").splitlines()
                    if needle in l]
        except OSError:
            pass
    return out


def main():
    fails = []
    with PipeClient() as c:
        c.assert_build()
        say("== precondition: something must be MISSING, or Apply is unreachable ==")
        try:
            st = c.ensure_scanned(timeout=600)
        except Exception as e:
            say("   ensure_scanned raised: %s" % str(e)[:160])
            st = {}
        g = c.request("get_pointers")
        g = g.get("data", g)
        say("   ue=%s  gobjects=%s  gnames=%s  gworld=%s  gengine=%s  objects=%s"
            % (g.get("ue_version"), g.get("gobjects"), g.get("gnames"),
               g.get("gworld"), g.get("gengine"), g.get("object_count")))
        missing = [k for k in ("gobjects", "gnames", "gworld", "gengine")
                   if str(g.get(k, "")) in ("", "0x0", "0", "None", "not_found")]
        say("   unresolved: %s" % (missing or "nothing"))
        if not missing:
            say("")
            say("NOT RUN: every global resolved on the first pass, so Extra Scan -> Apply is not "
                "reachable here and an 'exactly one' count would be vacuous.")
            return 1

        base_start = len(all_lines("ValidateAndFixOffsets: Starting"))
        base_appl = len(all_lines("apply_rescan: Applied"))
        say("   baseline: 'ValidateAndFixOffsets: Starting'=%d  'apply_rescan: Applied'=%d"
            % (base_start, base_appl))

        # ---------------------------------------------------------------- rescan
        say("")
        say("== Extra Scan (rescan) ==")
        r = c.request("rescan")
        say("   rescan -> %s" % json.dumps(r.get("data", r))[:200])
        t0 = time.time()
        last = None
        while time.time() - t0 < 600:
            s2 = c.request("rescan_status")
            d2 = s2.get("data", s2)
            state = d2.get("state") or d2.get("status")
            if state != last:
                say("   rescan_status: %s" % json.dumps(d2)[:200])
                last = state
            if d2.get("done") or str(state).lower() in ("done", "complete", "completed", "finished",
                                                        "idle", "failed", "error"):
                break
            time.sleep(2.0)
        say("   rescan finished after %.0f s" % (time.time() - t0))

        # ---------------------------------------------------------------- apply
        say("")
        say("== Apply ==")
        ap = c.request("apply_rescan")
        say("   apply_rescan -> %s" % json.dumps(ap.get("data", ap))[:260])
        time.sleep(1.5)

        # ---------------------------------------------------------------- step 3
        say("")
        say("== step 3: the gate — EXACTLY ONE 'ValidateAndFixOffsets: Starting' ==")
        got = all_lines("ValidateAndFixOffsets: Starting")
        new = len(got) - base_start
        say("   total=%d   new this run=%d   <-- must be exactly 1" % (len(got), new))
        for l in got[-3:]:
            say("      " + l.strip()[:160])
        if new != 1:
            fails.append("G3-3: %d new 'ValidateAndFixOffsets: Starting' line(s) — the gate is "
                         "meant to admit exactly one" % new)

        # ---------------------------------------------------------------- step 4
        say("")
        say("== step 4: GEngine must still resolve after an Apply ==")
        gap = all_lines("apply_rescan: Applied")
        say("   'apply_rescan: Applied…' lines: %d (was %d)" % (len(gap), base_appl))
        for l in gap[-3:]:
            say("      " + l.strip()[:180])
        geng = [l for l in gap if "GEngine=" in l]
        say("   of those, naming GEngine=: %d" % len(geng))
        g2 = c.request("get_pointers")
        g2 = g2.get("data", g2)
        say("   after apply: gobjects=%s gnames=%s gworld=%s gengine=%s objects=%s"
            % (g2.get("gobjects"), g2.get("gnames"), g2.get("gworld"),
               g2.get("gengine"), g2.get("object_count")))
        if "gengine" in missing and not geng:
            fails.append("G3-4: GEngine was unresolved and no 'apply_rescan: Applied GEngine=' "
                         "line appeared — the hoisted second pass did not run")

    say("")
    for x in fails:
        say("FAIL: %s" % x)
    if not fails:
        say("PASS (G3 steps 3 and 4)")
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
