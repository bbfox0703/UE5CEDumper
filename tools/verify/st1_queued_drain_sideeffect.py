r"""ST1 steps 4 and 6 — the queued invoke must still DRAIN, and drain cleanly.

    py tools/verify/st1_queued_drain_sideeffect.py

WHY THIS RIG EXISTS: the obvious signals for both steps are undecidable.
  step 4  "the queued request runs on the game thread" — the completion line is written by the
          WAITING pipe thread, which has already timed out and gone, so an abandoned request that
          drains leaves **no log line at all**. A previous run recorded this honestly as *not
          decidable from that signal*.
  step 6  "no `SEH exception during queued PE call`, no 0xC0000409" — an absence. On its own it is
          equally satisfied by a drain that never happened, which is precisely the regression the
          step exists to catch (*"the `thread_local` guard did not suppress the legitimate drain"*).

⭐ THE FIX FOR BOTH: queue an invoke whose execution has an OBSERVABLE SIDE EFFECT, and read that
side effect out of process memory rather than out of a log.

  fixture   a live `StaticMeshActor`; `AActor::bHidden` is a packed bool at **+0x58 bit 7 (0x80)**,
            read directly with `ReadProcessMemory`.
  function  `Actor.SetActorHiddenInGame(bool)` — flags `0x04020402`: **Native but NOT Static**, and
            `Mimic::ShouldRouteDirectInvoke` requires *both*, so it is guaranteed to take the
            GameThreadDispatch path rather than the direct fast path. (Checked, not assumed: a
            Native|Static pick would have silently made the whole rig vacuous.)

SEQUENCE, with the control before the measurement:
  1  read the bit                                        -> must be 0, else there is nothing to see
  2  FREEZE the UE game thread
  3  invoke SetActorHiddenInGame(true)                    -> must return -5 (queued, not run)
  4  ⚠ CONTROL: re-read the bit while still frozen        -> must STILL be 0. This is what makes the
     flip in step 6 attributable to the drain rather than to the call having run immediately.
  5  RESUME the thread and let the game tick
  6  poll the bit                                         -> flips to 1  ==> the queued request
     really did drain and execute on the game thread (step 4), and the guard did not suppress it
  7  grep for `SEH exception during queued PE call` / `0xC0000409`  -> 0 (step 6)
  8  restore: invoke SetActorHiddenInGame(false) with the thread running, and confirm the bit
     returns to 0 — which also proves the bit is writable by this function at all, so a
     hypothetical "it never flips" result could not be blamed on the fixture.
"""
import pathlib
import re
import struct
import subprocess
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from pipe_client import PipeClient  # noqa: E402
from mailbox_poke import Mem, pid_of  # noqa: E402

LOG = pathlib.Path.home() / "AppData/Local/UE5CEDumper/Logs/DumperTest"
OFF_BHIDDEN, MASK_BHIDDEN = 0x58, 0x80
FUNC_NATIVE, FUNC_STATIC = 0x400, 0x2000
DRAIN_WAIT_S = 180


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + "\n")


def count(needle):
    n = 0
    for f in sorted(LOG.glob("*-0.log")):
        try:
            n += sum(1 for l in f.read_text(encoding="utf-8", errors="replace").splitlines()
                     if needle in l)
        except OSError:
            pass
    return n


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
    fails = []
    m = Mem(pid_of("DumperTest"))

    with PipeClient() as c:
        c.assert_build()
        c.ensure_scanned()
        fl = (c.request("list_all_functions", limit=20000,
                        game_only=False).get("functions") or [])
        fn = next((f for f in fl if f.get("func_name") == "SetActorHiddenInGame"
                   and f.get("class_name") == "Actor"), None)
        # The subject MUST be an instance whose class DECLARES the function, not merely one
        # that inherits it: [INVOKEINHERIT-2026-08-20] -- `UE5_FindFunctionByName` never
        # climbs SuperStruct, so a `StaticMeshActor` returns "Function not found" for
        # `SetActorHiddenInGame` and the whole rig reports a false "the drain was suppressed".
        # That is exactly how that defect was found. `ChaosDebugDrawActor`'s class is
        # literally `Actor`, so it resolves.
        sm = [i for i in (c.request("find_instances", class_name="Actor",
                                    max_results=400,
                                    exact_match=False).get("instances") or [])
              if i.get("class") == "Actor"
              and not str(i.get("name", "")).startswith("Default__")]
        if not fn or not sm:
            say("FAIL: need Actor.SetActorHiddenInGame and a live instance whose class IS Actor")
            return 1
        flags = fn["function_flags"]
        inst = sm[0]["addr"]
        say("function : Actor.SetActorHiddenInGame  flags=0x%08X  parms=%s" % (flags, fn.get("parms_size")))
        direct = (flags & (FUNC_NATIVE | FUNC_STATIC)) == (FUNC_NATIVE | FUNC_STATIC)
        say("           Native=%s Static=%s  -> takes the %s"
            % (bool(flags & FUNC_NATIVE), bool(flags & FUNC_STATIC),
               "DIRECT fast path" if direct else "GameThreadDispatch queue"))
        if direct:
            say("FAIL: this function would bypass the queue entirely -- the rig would be vacuous")
            return 1
        say("instance : %s  (%s)" % (inst, sm[0].get("name")))

        addr = int(inst, 16) + OFF_BHIDDEN
        def bit():
            return (m.read(addr, 1)[0] & MASK_BHIDDEN) != 0
        say("bHidden  : +0x%02X bit 0x%02X = %s   (byte 0x%02X)"
            % (OFF_BHIDDEN, MASK_BHIDDEN, bit(), m.read(addr, 1)[0]))
        if bit():
            say("FAIL: bHidden is ALREADY true -- no transition to observe")
            return 1

        seh0, bug0 = count("SEH exception during queued PE call"), count("0xC0000409")
        tid = game_tid()
        if tid is None:
            say("FAIL: could not identify the UE game thread")
            return 1

        say("")
        say("== freezing tid %d, then queueing the invoke ==" % tid)
        freeze(tid, True)
        try:
            t0 = time.time()
            r = c.request("invoke_function", instance_addr=inst, class_name="Actor",
                          func_name="SetActorHiddenInGame", parms_size=1, params_hex="01")
            d = r.get("data", r)
            say("   invoke -> ok=%s result=%s stalled=%s  (%.1f s)"
                % (d.get("ok"), d.get("result"), d.get("game_thread_stalled"),
                   time.time() - t0))
            if d.get("result") != -5:
                fails.append("expected -5 (queued while frozen), got %r -- if it ran, there is "
                             "nothing queued and steps 4/6 are vacuous" % d.get("result"))
            say("")
            say("   ⚠ CONTROL: bHidden while STILL frozen = %s   <-- must be False" % bit())
            if bit():
                fails.append("the bit flipped while the game thread was frozen -- the call did "
                             "NOT go through the queue, so a later flip proves nothing")
        finally:
            freeze(tid, False)
            say("")
            say("== resumed; waiting up to %d s for the queued request to DRAIN ==" % DRAIN_WAIT_S)

        t0 = time.time()
        flipped_at = None
        while time.time() - t0 < DRAIN_WAIT_S:
            if bit():
                flipped_at = time.time() - t0
                break
            time.sleep(0.5)
        say("   bHidden after resume: %s   %s"
            % (bit(), ("flipped after %.1f s" % flipped_at) if flipped_at is not None
               else "NEVER flipped in %d s" % DRAIN_WAIT_S))
        if flipped_at is None:
            fails.append("ST1-4/6: the queued request never executed -- the drain WAS suppressed, "
                         "which is exactly the regression step 6 guards against")
        else:
            say("   ⇒ ST1 step 4: the queued request ran on the game thread, witnessed by the "
                "side effect rather than by an absent log line")

        seh1, bug1 = count("SEH exception during queued PE call"), count("0xC0000409")
        say("")
        say("   'SEH exception during queued PE call' : %d new   <-- step 6 wants 0" % (seh1 - seh0))
        say("   '0xC0000409'                          : %d new   <-- step 6 wants 0" % (bug1 - bug0))
        if seh1 != seh0:
            fails.append("ST1-6: %d new SEH exception(s) during the queued drain" % (seh1 - seh0))
        if bug1 != bug0:
            fails.append("ST1-6: %d new 0xC0000409 line(s)" % (bug1 - bug0))

        say("")
        say("== restoring bHidden (also proves the bit is writable by this function) ==")
        rr = c.request("invoke_function", instance_addr=inst, class_name="Actor",
                       func_name="SetActorHiddenInGame", parms_size=1, params_hex="00")
        dd = rr.get("data", rr)
        time.sleep(1.0)
        say("   invoke(false) -> result=%s ; bHidden now %s" % (dd.get("result"), bit()))
        if bit():
            fails.append("cleanup: bHidden is still true -- the actor was left hidden")

    say("")
    for x in fails:
        say("FAIL: %s" % x)
    if not fails:
        say("PASS (ST1 steps 4 and 6)")
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
