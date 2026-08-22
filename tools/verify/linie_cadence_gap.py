r"""Linie cadence: does the reported Period agree with the fire count in the same row?

    py tools/verify/linie_cadence_gap.py            # launch DumperTest at 60 FPS, inject, measure
    py tools/verify/linie_cadence_gap.py --attach   # use the host already running
    py tools/verify/linie_cadence_gap.py --fps 15

WHAT IT COMPARES. `pe_profile_get` returns, per UFunction, both `count` (fires in the window) and
`mean_period_ms` (Welford mean of the inter-arrival gaps). Over a window of known length those two
are the same fact stated twice, so they must agree:

    implied period = window_ms / (count - 1)      vs      reported mean_period_ms

A ratio near 1.00 means the row is self-consistent. Anything else means the Period column and the
fire count are computed by different paths and disagree — and the user reading the row to find a
cooldown timer believes the Period.

⭐ THE CONTROL IS FREE AND IT IS THE POINT. DumperTest's six recorded functions split into four that
fire ONCE per frame and two (`CameraModifier::BlueprintModify*`) that fire TWICE. If a discrepancy
showed up on all six, the cause would be the window measurement or the clock, and the theory below
would be refuted. It shows up on exactly the two, which no clock error can produce.

WHAT IT FOUND (2026-08-22, dist build 3309, DumperTest, 10.00 s window):

    t.MaxFPS 60                                 count   gaps   reported   implied   ratio
    CameraModifier::BlueprintModifyCamera        1202    602      16.61      8.33   1.99x
    CameraModifier::BlueprintModifyPostProcess   1202    602      16.61      8.33   1.99x
    ABP_Manny_C::BlueprintUpdateAnimation         600    599      16.67     16.70   1.00x
    AnimInstance::BlueprintThreadSafeUpdate...    600    599      16.67     16.70   1.00x
    ABP_Manny_C::EvaluateGraphExposedInputs...    600    599      16.67     16.70   1.00x
    AnimInstance::BlueprintPostEvaluateAnim...    600    599      16.67     16.70   1.00x

`gap_samples` is the tell. It must be `count - 1`; for the two doubled functions it is `count / 2`.

THE CAUSE. `Linie::RecordCall` guards the Welford update on `nowMs > s.lastMs`, and `nowMs` is a
`steady_clock` reading truncated to MILLISECONDS (`Stark.cpp:103-107`). Two fires inside the same
millisecond therefore compare EQUAL, the gap is dropped, and `s.lastMs` is left pointing at the
first of the pair — so the next gap spans both fires and reads as a full frame.

⚠ THE GUARD IS DELIBERATE, AND THAT IS WHY THIS IS EASY TO MISREAD. It is the prescribed fix for
audit #3 finding L5 (`docs/audit-2026-07-14-findings.md:343`), which is about REORDERED timestamps:
`nowMs` is stamped before `RecordCall` takes the lock, so multi-thread PE can deliver two fires out
of order, and an unsigned `nowMs - s.lastMs` would underflow to a ~1.8e19 gap that poisons the mean
for the rest of the window. That hazard is real. But a reorder is `nowMs < s.lastMs`; `nowMs ==
s.lastMs` is not a reorder, it is two fires in one millisecond, which is ordinary. `>=` excludes the
underflow just as completely and keeps the sample.

SECOND-ORDER, and worse than the wrong number: dropping the ~0 ms gaps MANUFACTURES the regularity
the "Timer" badge keys on. The surviving gaps are all exactly one frame apart, so cv collapses to
~0.01 and a twice-per-frame render callback scores as a textbook periodic timer — the precise
distinction Linie's cadence phase exists to draw.

⚠ NOT THE SAME BUG, do not merge them: at `t.MaxFPS 15` ALL SIX functions are flagged periodic,
because the classifier's "out of the per-frame band" test is `meanPeriodMs > 40.0` and a 15 FPS
frame is 66.7 ms. That is a threshold assuming a normal frame rate, and it fires with or without
the gap drop. Measure at 60 FPS to keep the two apart.
"""
import argparse
import pathlib
import subprocess
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.stdout.reconfigure(encoding="utf-8", errors="replace")

from pipe_client import PipeClient  # noqa: E402

EXE = (r"D:\UE_Analyze_data\for testing\DumperTest\Development\Windows"
       r"\DumperTest\Binaries\Win64\DumperTest.exe")
WINDOW_S = 10.0
TOLERANCE = 0.15          # a ratio inside 1 +- this is "consistent"


def say(s=""):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + chr(10))
    sys.stdout.flush()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--attach", action="store_true")
    ap.add_argument("--fps", type=int, default=60,
                    help="frame cap; 60 keeps this separate from the >40ms classifier threshold")
    ap.add_argument("--window", type=float, default=WINDOW_S)
    a = ap.parse_args()

    if not a.attach:
        subprocess.run(["taskkill", "/F", "/IM", "DumperTest.exe"], capture_output=True)
        time.sleep(1.5)
        p = subprocess.Popen([EXE, "-windowed", "-ResX=1280", "-ResY=720",
                              "-ExecCmds=t.MaxFPS %d" % a.fps])
        say("launched pid %s at t.MaxFPS %d" % (p.pid, a.fps))
        time.sleep(22)
        r = subprocess.run([sys.executable, str(HERE / "inject.py"), "--name", "DumperTest"],
                           capture_output=True, text=True, encoding="utf-8", errors="replace")
        say((r.stdout or r.stderr or "").strip()[-160:])
        if r.returncode != 0:
            say("FAIL: could not inject")
            return 2
        time.sleep(2)

    with PipeClient() as c:
        c.assert_build()
        c.ensure_scanned()
        c.request("pe_profile_start")
        t0 = time.time()
        time.sleep(a.window)
        el = time.time() - t0
        c.request("pe_profile_stop")
        g = c.request("pe_profile_get", limit=200)

    rows = g.get("functions") or []
    if len(rows) < 2:
        say("NOT_RUNNABLE: only %d function(s) recorded" % len(rows))
        return 2

    say("")
    say("window %.2f s   ·   %d function(s)" % (el, len(rows)))
    say("%-50s %6s %6s %9s %9s %8s" % ("func", "count", "gaps", "reported", "implied", "ratio"))
    say("-" * 97)
    bad, good = [], []
    for r in sorted(rows, key=lambda x: -(x.get("count") or 0)):
        n = r.get("count") or 0
        gaps = r.get("gap_samples") or 0
        rep = float(r.get("mean_period_ms") or 0.0)
        if n < 2 or rep <= 0:
            continue
        imp = el * 1000.0 / (n - 1)
        ratio = rep / imp
        name = ("%s::%s" % (r.get("class_name"), r.get("func_name")))[:50]
        say("%-50s %6d %6d %9.2f %9.2f %7.2fx" % (name, n, gaps, rep, imp, ratio))
        (bad if abs(ratio - 1.0) > TOLERANCE else good).append((name, n, gaps, ratio))

    say("")
    if not bad:
        say("PASS: every row's Period agrees with its own fire count (within %.0f%%)."
            % (TOLERANCE * 100))
        say("      gap_samples == count-1 throughout, so no inter-arrival sample was dropped.")
        return 0

    say("FAIL: %d row(s) report a Period that contradicts their own fire count." % len(bad))
    for name, n, gaps, ratio in bad:
        say("   %-50s ratio %.2fx   gaps %d, expected %d" % (name, ratio, gaps, n - 1))
    if good:
        say("")
        say("   ⭐ %d row(s) ARE consistent in the same window, which is the control: a clock or"
            % len(good))
        say("     window error would move all of them together. It did not.")
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
