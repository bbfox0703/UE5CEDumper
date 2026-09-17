r"""D1 — a REFUSED collision restore must keep the record, so the restore can still land.

    py tools/verify/d1_collision_refusal.py     # DumperTest must be running + injected

⚠⚠ SINCE B31 ([W3-DUNSTE-QUEUED], 2026-09-12) ARM 1 NO LONGER MEASURES A REFUSAL. Its 100 ms dispatch times
out with -5, and -5 now means QUEUED: the restore stays queued and lands when the game thread drains, so
the record COMMITS -- "QUEUED (rc=-5 ...)", then "Fly: DISABLED", and no "keeping the record" line. Arm 1
asserts exactly that now. D1's responsive-but-REFUSED arm has no live trigger left: -8 cannot occur on the
pipe thread (it is not a background worker), and -5 was the only code a live hook returned. The refused arm
stays pinned by Test_Dunste_ShouldCommitCollision (dll_helpers_test). The queued restore drains with no Dunste
log line, so arm 1 has no log-observable self-heal; arm 2 keeps the record and self-heals exactly as before.

THE DEFECT (blind-spot sweep, fixed 2026-09-08, fc83923e).
`Dunste::InvokeSetCollision` returned "the setter was FOUND" as a bool and threw away the
dispatcher's `int32_t`, so all three call sites read a REFUSAL as "collision CHANGED".
`SetEnabled(false)` then cleared `collisionOff`/`collisionPawn` unconditionally -- leaving the
pawn non-colliding with **nothing tracking it**, which audit #4 B8 calls *"what made the pawn
fall through the world"*. The repair is `CollisionApply{Applied, Absent, Refused}` plus the
pure `ShouldCommitCollision`: **Absent** is permanent (retrying cannot conjure a setter, so it
commits) and **Refused** is transient (it must not).

⛔ WHY THIS ROW COULD NOT BE UNIT-TESTED, and what it took to reach live.
`todo.md` recorded it as *"every D1 call-site path needs a running game with the PE hook
down"*. The pure core is pinned by `Test_Dunste_ShouldCommitCollision`; the call sites were
reviewed, not executed. The obstacle is that the restore site reads

    const bool responsive = Stark::IsGameThreadResponsive();
    const bool restored   = responsive && ShouldCommitCollision(InvokeSetCollision(pawn, true));

so simply freezing the game leaves `responsive == false`, **short-circuits before
InvokeSetCollision is ever called**, and exercises B8's original path instead of D1's addition.
A rig that froze the game and watched the record survive would have proved nothing about this
fix -- it would have re-measured a 2026-07 repair and reported it as a 2026-09 one.

⭐ THE WINDOW THAT SEPARATES THEM. The two conditions are measured against *different* clocks:
`Stark::kStallThresholdMs` is **500** (time since the hook last fired) and `kMinInvokeTimeoutMs`
is **100** (how long one dispatch waits). Freeze the UE game thread and issue the restore inside
that gap and both hold at once -- the thread is still "responsive" because it fired a few ms
ago, while the dispatch times out at 100 ms and returns non-zero. That is D1's condition
exactly: **responsive, and refused anyway.**

The DLL names which half fired, and that is the acceptance:

    "Fly: DISABLED but the pawn's collision is still OFF (the dispatcher REFUSED the restore)
     - keeping the record and polling to restore it"          <- D1's arm
    "... (game thread unresponsive) ..."                      <- B8's pre-existing arm

⭐⭐ SO THE RIG RUNS BOTH ARMS AND REQUIRES THEM TO DIFFER. A short freeze must report the
dispatcher refusal; a long one must report the unresponsive thread. Running only the short arm
would leave "the discriminator actually discriminates" unmeasured -- the rig would pass just as
happily if it were matching any "keeping the record" line at all, on either path. Same reason
the D4b rig runs two build configurations rather than one.

⭐ AND THE BEHAVIOURAL PROOF, which no log line can fake: after the thread resumes,
`PendingRestoreLoop` restores collision **by itself**, with no further command. That can only
happen because the record was kept. Pre-fix there was nothing left to poll for and the pawn
stayed ghosted for the rest of the session.

⚠ Freezes ONE THREAD, not the process: a whole-process suspend stops Fern too, so the DLL never
picks the command up and the refusal under test never happens.
"""
from __future__ import annotations

import json
import os
import re
import subprocess
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
CLIENT = HERE / "pipe_client.py"
SUSPEND = HERE / "suspend.py"
# ⚠ Dunste's LOG_CAT is "FLY", which Sein.cpp:85 routes to LF_Walk -- walk-0.log, NOT the
# offsets log its OARR neighbours land in. Grepping the wrong file returns zero for both "the
# code did not do it" and "I am reading the wrong channel" (working-lessons 2.10).
LOG = Path(os.environ["LOCALAPPDATA"]) / "UE5CEDumper" / "Logs" / "DumperTest" / "walk-0.log"

APPLIED_OFF = "SetActorEnableCollision(0) applied"
APPLIED_ON = "SetActorEnableCollision(1) applied"
KEPT = "keeping the record and polling to restore it"
D1_CAUSE = "the dispatcher REFUSED the restore"
B8_CAUSE = "game thread unresponsive"
QUEUED = "QUEUED (rc=-5"   # [W3-DUNSTE-QUEUED] Dunste.cpp's line for a -5 timeout: queued, it will land


def call(cmd: str, args: dict | None = None) -> dict:
    r = subprocess.run([sys.executable, str(CLIENT), cmd, "--args", json.dumps(args or {})],
                       capture_output=True, text=True, encoding="utf-8", errors="replace",
                       cwd=str(ROOT))
    out = r.stdout or ""
    i = out.find("{")
    if i < 0:
        raise SystemExit("pipe_client gave no JSON for %s:\n%s\n%s" % (cmd, out, r.stderr))
    return json.loads(out[i:])


def log_lines() -> list[str]:
    if not LOG.exists():
        raise SystemExit("no log at %s -- is the DLL injected?" % LOG)
    return LOG.read_text(encoding="utf-8", errors="replace").splitlines()


def since(mark: int) -> list[str]:
    return log_lines()[mark:]


def msg(line: str) -> str:
    return line.split("] ")[-1]


def main_thread_id() -> int:
    r = subprocess.run([sys.executable, str(SUSPEND), "threads", "DumperTest"],
                       capture_output=True, text=True, encoding="utf-8", errors="replace")
    for line in (r.stdout or "").splitlines():
        if "main thread" in line:
            m = re.search(r"\b(\d{3,})\b", line)
            if m:
                return int(m.group(1))
    raise SystemExit("could not identify the UE game thread:\n%s\n%s" % (r.stdout, r.stderr))


def tid(verb: str, t: int) -> None:
    subprocess.run([sys.executable, str(SUSPEND), verb, "DumperTest", str(t)],
                   capture_output=True, text=True)


def wait_for(needle: str, mark: int, timeout_s: float) -> str | None:
    deadline = time.time() + timeout_s
    while time.time() < deadline:
        for ln in since(mark):
            if needle in ln:
                return ln
        time.sleep(0.25)
    return None


def arm(game_tid: int, hold_s: float, want: str, label: str) -> list[str]:
    """One enable / freeze / disable / resume cycle, asserting WHICH refusal arm fired.

    `hold_s` is the ONLY difference between the two arms: how long the game thread has
    ALREADY been frozen when the restore is issued, which is what pushes the elapsed time
    since the last hook fire past kStallThresholdMs.
    """
    fails: list[str] = []
    print("\n--- %s" % label)

    # 1. get the pawn genuinely ghosted, through the normal path.
    mark = len(log_lines())
    call("fly_set", {"enable": True, "noclip": True})
    hit = wait_for(APPLIED_OFF, mark, 20)
    print("  noclip on  : %s" % (msg(hit) if hit else "NOT APPLIED"))
    if not hit:
        return ["%s: collision was never disabled, so there is no record to keep -- this arm "
                "measured nothing. Is a pawn possessed?" % label]

    # 2. freeze, ask for the restore, resume.
    mark = len(log_lines())
    tid("suspend-tid", game_tid)
    t0 = time.time()
    try:
        # ⛔ THE SLEEP MUST COME BEFORE THE COMMAND, and the control is what proved it.
        # IsGameThreadResponsive() is evaluated the moment the restore is handled, so a
        # sleep placed AFTER the call changes nothing that is being measured: the first
        # draft held the freeze for 1459 ms and still landed on arm 1, because the verdict
        # had already been taken ~1 ms in. A control that cannot fail is not a control.
        if hold_s:
            time.sleep(hold_s)
        call("fly_set", {"enable": False})
    finally:
        frozen_ms = (time.time() - t0) * 1000.0
        tid("resume-tid", game_tid)
    print("  frozen for : %.0f ms   (invoke timeout 100, stall threshold 500)" % frozen_ms)

    kept = next((l for l in since(mark) if KEPT in l), None)
    print("  verdict    : %s" % (msg(kept) if kept else "NO 'keeping the record' LINE"))

    if not kept:
        fails.append("%s: no kept-record line at all. Either the restore succeeded (the window "
                     "was missed; frozen %.0f ms) or the record was DROPPED, which is the D1 "
                     "defect itself." % (label, frozen_ms))
    elif want not in kept:
        fails.append("%s: expected the cause to read %r, got %r (frozen %.0f ms). The two arms "
                     "differ only by that duration, so a mismatch means the freeze landed on "
                     "the wrong side of the 500 ms threshold." % (label, want, msg(kept),
                                                                 frozen_ms))

    # 3. ⭐ the behavioural proof. Nothing here issues another command: a later "applied" line
    #    can only come from PendingRestoreLoop, which exists only because the record survived.
    #    BOTH arms must self-heal -- keeping the record is the point of both.
    late = wait_for(APPLIED_ON, mark, 30)
    print("  self-heal  : %s" % (msg(late) if late else "NEVER RESTORED"))
    if not late:
        fails.append("%s: collision was never re-enabled after the thread resumed -- the pawn "
                     "is still ghosted with nothing tracking it, which is precisely the B8 "
                     "failure D1 keeps closed." % label)

    if call("fly_get_state").get("active"):
        fails.append("%s: Fly still reports active after being disabled" % label)
    return fails


def arm_queued(game_tid: int, label: str) -> list[str]:
    """Arm 1 since B31: the restore's 100 ms dispatch times out (-5), which is QUEUED, not refused -- so the
    record commits, and no "keeping the record" line may appear. The queued restore then drains on the game
    thread with no Dunste log line, which is why this arm has no self-heal wait."""
    fails: list[str] = []
    print("\n--- %s" % label)

    mark = len(log_lines())
    call("fly_set", {"enable": True, "noclip": True})
    hit = wait_for(APPLIED_OFF, mark, 20)
    print("  noclip on  : %s" % (msg(hit) if hit else "NOT APPLIED"))
    if not hit:
        return ["%s: collision was never disabled, so this arm measured nothing. Is a pawn possessed?" % label]

    mark = len(log_lines())
    tid("suspend-tid", game_tid)
    t0 = time.time()
    try:
        call("fly_set", {"enable": False})
    finally:
        frozen_ms = (time.time() - t0) * 1000.0
        tid("resume-tid", game_tid)
    print("  frozen for : %.0f ms   (invoke timeout 100, stall threshold 500)" % frozen_ms)

    lines = since(mark)
    queued = next((l for l in lines if QUEUED in l), None)
    kept = next((l for l in lines if KEPT in l), None)
    print("  verdict    : %s" % (msg(queued) if queued else "NO 'QUEUED' LINE"))
    if not queued:
        fails.append("%s: no QUEUED line (frozen %.0f ms). Either the dispatch did not time out inside the "
                     "window, or -5 is being refused again, which is the [W3-DUNSTE-QUEUED] defect."
                     % (label, frozen_ms))
    if kept:
        fails.append("%s: the record was KEPT for a queued restore (%r). -5 must commit: the queued restore "
                     "will land." % (label, msg(kept)))
    if call("fly_get_state").get("active"):
        fails.append("%s: Fly still reports active after being disabled" % label)
    return fails


def main() -> int:
    st = call("status")
    print("objects    : %s" % st.get("object_count", st.get("count", "?")))

    # 100 ms is kMinInvokeTimeoutMs -- the shortest the DLL accepts, and the whole point: it
    # must expire well inside the 500 ms responsiveness window.
    r = call("set_invoke_timeout", {"timeout_ms": 100, "persist": False})
    print("timeout    : %s" % ("100 ms" if r.get("ok") else "FAILED %s" % r))

    game_tid = main_thread_id()
    print("game thread: %d" % game_tid)

    fails = arm_queued(game_tid, "ARM 1 (B31): freeze BELOW the stall threshold -- the -5 timeout is "
                                 "QUEUED, and commits")
    # ⭐ The negative control. Same sequence, one variable changed: hold the freeze past
    # kStallThresholdMs so IsGameThreadResponsive() goes false. If arm 2 reported the same
    # cause as arm 1, the rig would not be reading the cause at all.
    fails += arm(game_tid, 1.0, B8_CAUSE,
                 "ARM 2 (control): freeze ABOVE it -- must name the UNRESPONSIVE thread instead")

    call("set_invoke_timeout", {"timeout_ms": 0, "persist": False})

    print()
    if fails:
        print("D1: FAIL")
        for f in fails:
            print("  - %s" % f)
        return 1
    print("D1: PASS -- a -5 timeout (arm 1) is QUEUED and commits, and an unresponsive thread (arm 2) "
          "keeps the record and restores on its own once the game thread comes back.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
