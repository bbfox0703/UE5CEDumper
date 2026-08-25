"""L1 - concurrent ON/OFF of GodMode must not orphan or prematurely join the re-assert worker.

    py tools/verify/l1_godmode_race.py

THE FIX UNDER TEST (7f3898ff): `Solitar::SetGodMode` mutated `s_wantGod` under `s_mutex`,
released it, then decided Start/StopWorker on the `on` PARAMETER outside any worker mutex.
A concurrent ON/OFF (pipe `set_god_mode` + CE-mailbox `CMD_PROTECT`) could therefore join a
worker that was still wanted, or leave an orphan running after the final state was OFF. The
fix holds `s_workerMutex` across the mutate AND the decision, and decides on the FINAL
`s_wantGod` rather than the argument (Solitar.cpp:383-400).

THE TWO FAILURE MODES, both checked BEHAVIOURALLY rather than from a status flag:

  * ORPHAN - final state OFF but a worker still running -> a poked bit gets restored
  * JOINED - final state ON but no worker running       -> a poked bit stays poked

`get_god_mode` reports intent, not whether the worker thread exists, so it cannot see either
failure. The discriminator is the write-on-drift behaviour itself: poke `bCanBeDamaged` to
the wrong value and see whether anything puts it back.

THE ANTI-VACUITY CHECK, and it is the point of the whole rig. A race test that never actually
races proves nothing. `SetGodMode` logs `GodMode: set <ON|OFF> -> rc=<n> (want=<0|1>)`, where
the first token is the ARGUMENT and `want` is the final `s_wantGod`. Those disagree exactly
when another thread interleaved between the store and the log - so counting the disagreements
MEASURES how much genuine interleaving the storm produced. Zero means the storm serialised
and the run must not be scored as a pass.

NOTE the parameter is `enable`, not `enabled` (Fern.cpp). An unknown key silently defaults to
false and the reply still says ok:true - that mistake cost a whole invalid L8 result earlier
in this session.
"""
from __future__ import annotations

import argparse
import os
import pathlib
import re
import sys
import threading
import time

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from pipe_client import PipeClient           # noqa: E402

LOGDIR = pathlib.Path(os.environ["LOCALAPPDATA"]) / "UE5CEDumper" / "Logs" / "DumperTest"
SET_RE = re.compile(r"GodMode: set (ON|OFF) -> rc=(-?\d+) \(want=(\d)\)")
APPLIED_RE = re.compile(r"applied \d+ protection bool.s. on pawn=0x([0-9A-Fa-f]+)")
BIT_RE = re.compile(r"@\+0x([0-9A-Fa-f]+) mask=0x([0-9A-Fa-f]+)")


def _sizes():
    """Per-file byte lengths of the CURRENT run only."""
    d = {}
    for f in LOGDIR.glob("*-0.log"):        # <cat>-0.log is always the live run
        try:
            d[f] = len(f.read_text(encoding="utf-8", errors="replace"))
        except OSError:
            pass
    return d


def log_since(mark):
    """Text appended to the live logs since a _sizes() snapshot.

    This used to concatenate sorted(LOGDIR.glob("*.log")) and slice by TOTAL length. Two
    things break that: the directory keeps every past session archive, and name-sorting
    puts the live walk-0.log BEFORE walk-2026..., so slicing by length cut inside an
    archive and returned a PREVIOUS process lines. That is how the first run of this rig
    read a dead pawn address and then reported the worker broken."""
    out = []
    for f in LOGDIR.glob("*-0.log"):
        try:
            out.append(f.read_text(encoding="utf-8", errors="replace")[mark.get(f, 0):])
        except OSError:
            pass
    return chr(10).join(out)


def log_text():
    return log_since({})


def set_lines(txt):
    return SET_RE.findall(txt)


def read_byte(c, addr):
    r = c.request("read_mem", addr=addr, size=1)
    h = r.get("hex") or r.get("bytes") or ""
    return int(h[:2], 16) if h else None


def write_byte(c, addr, val):
    return c.request("write_mem", addr=addr, bytes="%02x" % (val & 0xFF))


def poke_and_watch(c, addr, mask, secs=4.0):
    """Flip the protection bit the WRONG way; report whether anything restored it."""
    cur = read_byte(c, addr)
    if cur is None:
        return None, "read_mem returned nothing"
    write_byte(c, addr, cur ^ mask)
    t0 = time.time()
    restored = False
    while time.time() - t0 < secs:
        time.sleep(0.4)
        now = read_byte(c, addr)
        if now is not None and (now & mask) == (cur & mask):
            restored = True
            break
    return restored, "cur=0x%02X poked=0x%02X restored=%s after %.1fs" % (
        cur, cur ^ mask, restored, time.time() - t0)


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--iters", type=int, default=1500, help="toggles PER LANE")
    a = ap.parse_args(argv)
    fails = []

    with PipeClient() as c:
        c.assert_build()
        c.ensure_scanned()

        # ---------- discover the pawn + protection bit from the DLL's own log ----------
        # Parse ONLY what THIS enable appends. The log directory accumulates every session,
        # so `findall(...)[-1]` over the whole corpus returns the PREVIOUS process's pawn --
        # a dead address, which is why the first run of this rig got "read_mem returned
        # nothing" four times and still reported a FAIL as if the worker were broken.
        c.request("set_god_mode", enable=False)
        time.sleep(0.8)
        mark = _sizes()
        c.request("set_god_mode", enable=True)
        time.sleep(1.5)
        txt = log_since(mark)
        pawn = APPLIED_RE.findall(txt)
        # The bit line ('<name>' @+0xNN mask=0xMM) is logged ONLY when the class is first
        # scanned, not on every toggle -- so it will not appear in this OFF->ON window.
        # Take it from the whole live log; the pawn address must still come from the fresh
        # window because it changes per process.
        bits = BIT_RE.findall(log_text())
        if not pawn or not bits:
            raise SystemExit("l1: could not find the pawn / protection bit in the DLL log")
        base = int(pawn[-1], 16)
        off, mask = int(bits[-1][0], 16), int(bits[-1][1], 16)
        bit_addr = "0x%X" % (base + off)
        print("[0] pawn 0x%X  bit @+0x%X mask=0x%02X -> %s" % (base, off, mask, bit_addr))

        # ---------- detectors, established BEFORE the storm ----------
        ok_on, det = poke_and_watch(c, bit_addr, mask)
        print("[1] worker ALIVE detector (god ON): restored=%s   %s" % (ok_on, det))
        if not ok_on:
            fails.append("1: with GodMode ON the poked bit was NOT restored, so the "
                         "worker-alive detector does not work and nothing below can be scored")
        c.request("set_god_mode", enable=False)
        time.sleep(1.5)
        ok_off, det = poke_and_watch(c, bit_addr, mask)
        print("[2] worker STOPPED detector (god OFF): restored=%s  %s" % (ok_off, det))
        if ok_off:
            fails.append("2: with GodMode OFF the poked bit was still restored -- a worker is "
                         "already orphaned before the storm even starts")

        before_mark = _sizes()

        # ---------- the storm ----------
        print("")
        print("[3] storm: 2 lanes x %d toggles" % a.iters)
        errs = [0, 0]

        # Each lane sends a CONSTANT value so the two are distinguishable in the log:
        # lane 0 only ever says ON, lane 1 only ever says OFF. The number of ON<->OFF
        # TRANSITIONS in the resulting line sequence then measures how much the two lanes
        # actually overlapped in time, and that metric is independent of the fix.
        def lane(idx, value):
            try:
                with PipeClient() as lc:
                    for _ in range(a.iters):
                        try:
                            lc.request("set_god_mode", enable=value)
                        except Exception:
                            errs[idx] += 1
            except Exception:
                errs[idx] += a.iters

        t0 = time.time()
        ts = [threading.Thread(target=lane, args=(0, True)),
              threading.Thread(target=lane, args=(1, False))]
        for t in ts:
            t.start()
        for t in ts:
            t.join()
        print("    %d toggles in %.1fs; lane errors %s" % (a.iters * 2, time.time() - t0, errs))

        # Did the two lanes ACTUALLY overlap in time?
        #
        # NOT by counting lines where the argument and `want` disagree. That was the first
        # version of this check and it is structurally always zero POST-FIX: the fix holds
        # s_workerMutex across the store AND the log line, so no other SetGodMode can be
        # between them. A metric the fix makes impossible cannot measure concurrency.
        #
        # Lane 0 only says ON and lane 1 only says OFF, so every ON<->OFF transition in the
        # line sequence is a point where the two lanes were both in flight. A serialised run
        # (lane 0 fully, then lane 1) yields exactly ONE transition.
        lines = set_lines(log_since(before_mark))
        seq = [l[0] for l in lines]
        transitions = sum(1 for i in range(1, len(seq)) if seq[i] != seq[i - 1])
        print("    %d GodMode-set lines logged; %d ON<->OFF transitions (lane overlap)"
              % (len(lines), transitions))
        if len(lines) < a.iters:
            fails.append("3: only %d set-lines logged for %d toggles -- most requests never "
                         "reached SetGodMode" % (len(lines), a.iters * 2))
        if transitions < a.iters // 4:
            fails.append("3: only %d ON/OFF transitions across %d lines -- the two lanes barely "
                         "overlapped, so this run does not exercise the race. Do not score it."
                         % (transitions, len(lines)))

        # ---------- settle ON: a worker MUST be running ----------
        c.request("set_god_mode", enable=True)
        time.sleep(2.0)
        ok, det = poke_and_watch(c, bit_addr, mask)
        print("")
        print("[4] settled ON  -> worker alive? %s   %s" % (ok, det))
        if not ok:
            fails.append("4: after settling ON the bit was NOT restored -- the worker was JOINED "
                         "while still wanted (one of L1's two failure modes)")

        # ---------- settle OFF: no worker may remain ----------
        c.request("set_god_mode", enable=False)
        time.sleep(2.0)
        ok, det = poke_and_watch(c, bit_addr, mask)
        print("[5] settled OFF -> worker orphaned? %s   %s" % (ok, det))
        # SECOND, INDEPENDENT detector: the worker announces its own lifecycle.
        wtxt = log_since(before_mark)
        started = wtxt.count("re-assert worker started")
        stopped = wtxt.count("re-assert worker stopped")
        print("    worker lifecycle over the whole run: started=%d stopped=%d (net=%d)"
              % (started, stopped, started - stopped))
        if started - stopped != 0:
            fails.append("5: worker started %d times and stopped %d -- net %d, so the final OFF "
                         "state left %d worker(s) running (orphan)"
                         % (started, stopped, started - stopped, started - stopped))
        if ok:
            fails.append("5: after settling OFF the bit was still restored -- an ORPHAN worker "
                         "survived (the other L1 failure mode)")

    print("")
    print("=" * 72)
    print("L1 GodMode worker start/stop race: %s" % ("PASS" if not fails else "FAIL"))
    for f in fails:
        print("   - %s" % f)
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
