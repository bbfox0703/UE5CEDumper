r"""Can the Live Funcs **Period** column even TELL numeric sorting from string sorting?

    py tools/verify/livefuncs_period_fixture.py          (launches + injects DumperTest itself)
    py tools/verify/livefuncs_period_fixture.py --attach (use the host already running)

THE ROW. "Click the Live Funcs **Period** header. It must re-sort, and clicking again reverses.
**Period must sort NUMERICALLY** — a 16.7 ms row above a 1000 ms row — not by its displayed string."

THE PRECONDITION NOBODY CHECKS. A numeric sort and a string sort produce the SAME order for most
data. They diverge only on a pair where the lexicographic and numeric orders disagree, which for
these strings means a pair with **different integer-digit counts whose leading digits invert the
comparison** — "16.7" vs "1000": numerically 16.7 < 1000, lexicographically "1000" < "16.7" because
'0' < '6' at index 1. If the recorded window happens to yield only 12.1 / 14.3 / 16.7, both sorts
agree, the column looks correct under either implementation, and clicking it has measured nothing.

So this asks the profiler what it actually produces, and reports whether a DISCRIMINATING PAIR
exists. It does not click anything — it establishes whether clicking would be worth doing, and
names the two rows to look at.

⚠ THE ORDER MATTERS: init -> trigger_scan -> invoke -> profile. Starting the profiler first used
to poison the ProcessEvent hook for the life of the process, so this rig never leads with it.

⚠ The Period column is fed by Linie's CADENCE phase (Welford inter-arrival mean/cv), so a function
needs at least a couple of fires in the window to get a period at all. A short window yields mostly
blanks — this rig uses a long one and reports how many rows actually carry a number, because "only
three rows had a period" is itself the answer to whether the column can be tested here.
"""
import argparse
import json
import pathlib
import subprocess
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.stdout.reconfigure(encoding="utf-8", errors="replace")

from pipe_client import PipeClient  # noqa: E402

PY = sys.executable
WINDOW_S = 12.0


def say(s=""):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + chr(10))
    sys.stdout.flush()


def run(args, timeout=240):
    return subprocess.run([PY] + args, capture_output=True, text=True,
                          encoding="utf-8", errors="replace", timeout=timeout)


def fmt(ms):
    """Mirror the UI's own display shape closely enough to compare orderings."""
    return "%.1f ms" % ms


def discriminating(a, b):
    """True when numeric and lexicographic order of these two DISAGREE.

    ⚠ A DISPLAY TIE IS NOT A DISAGREEMENT. 66.65 and 66.749 are different floats that both
    render "66.7 ms"; the naive `(a < b) != (sa < sb)` calls that pair discriminating, because the
    numbers order and the equal strings do not. It is the exact opposite: two rows that LOOK
    identical can never show you which comparer ran. The first version of this rig had that bug and
    reported "9 discriminating pairs, PASS" over six values that were all 66.7 ms.
    """
    sa, sb = fmt(a), fmt(b)
    if sa == sb or a == b:
        return False
    return (a < b) != (sa < sb)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--attach", action="store_true",
                    help="use the DumperTest already running instead of relaunching")
    a = ap.parse_args()

    if not a.attach:
        say("== staging a fresh DumperTest ==")
        subprocess.run(["taskkill", "/F", "/IM", "DumperTest.exe"],
                       capture_output=True, text=True)
        time.sleep(1.0)
        r = run([str(HERE / "launch_dumpertest.py"), "dev"])
        say((r.stdout or "").strip()[-300:] or (r.stderr or "").strip()[-300:])
        if r.returncode != 0:
            say("FAIL: could not launch DumperTest")
            return 2
        ri = run([str(HERE / "inject.py"), "--name", "DumperTest"], timeout=120)
        say((ri.stdout or "").strip()[-200:])
        if ri.returncode != 0:
            say("FAIL: could not inject")
            return 2
        time.sleep(1.5)

    with PipeClient() as c:
        c.assert_build()
        c.ensure_scanned()

        # invoke BEFORE profiling — never lead with the profiler
        say("")
        say("== priming the PE hook with a real invoke (never profile first) ==")
        # ⚠ the field is `func_name`; `function_name` is silently rejected with
        # {"ok": false, "error": "func_name is required"} and the prime never happens
        pr = c.request("invoke_function", func_name="Add_IntInt", args=[3, 4])
        say("   Add_IntInt(3,4) -> %s" % str(pr)[:200])
        if isinstance(pr, dict) and pr.get("ok") is False:
            say("   ⚠ the prime did NOT run: %s" % pr.get("error"))

        say("")
        say("== recording a %.0f s profile window ==" % WINDOW_S)
        c.request("pe_profile_start")
        time.sleep(WINDOW_S)
        c.request("pe_profile_stop")
        g = c.request("pe_profile_get", limit=5000)
        rows = g.get("functions") or g.get("results") or (g.get("data") or {}).get("functions") or []
        say("   %d function(s) recorded" % len(rows))

    if not rows:
        say("")
        say("NOT_RUNNABLE: the profiler recorded nothing, so the Period column would be empty and")
        say("clicking its header proves nothing.")
        return 2

    periods = []
    for r in rows:
        p = r.get("mean_period_ms")
        if p is None:
            p = (r.get("cadence") or {}).get("mean_period_ms")
        if p is not None and float(p) > 0:
            nm = "%s::%s" % (r.get("class_name") or "?", r.get("func_name") or "?")
            periods.append((float(p), nm, r.get("count")))
    periods.sort()

    say("")
    say("== rows carrying a numeric period: %d of %d ==" % (len(periods), len(rows)))
    for p, n, cnt in periods[:10]:
        say("   %-12s fires=%-6s %s" % (fmt(p), cnt, str(n)[:70]))
    if len(periods) > 10:
        say("   ... and %d more" % (len(periods) - 10))

    if len(periods) < 2:
        say("")
        say("NOT_RUNNABLE: fewer than two rows carry a period — nothing to order.")
        return 2

    shown = sorted(set(fmt(p) for p, _, _ in periods))
    say("")
    say("   distinct DISPLAYED values: %d  %s" % (len(shown), shown[:8]))
    if len(shown) < 2:
        say("")
        say("NOT_RUNNABLE: every row displays the same string (%s), so no click can distinguish"
            % shown[0])
        say("a numeric comparer from a string one. At t.MaxFPS 15 every frame-driven function")
        say("shares the 66.7 ms frame cadence -- the spread has to come from TIMER callbacks.")
        return 2

    pairs = [(x, y) for i, (x, nx, _) in enumerate(periods)
             for (y, ny, _) in periods[i + 1:]
             if discriminating(x, y)]
    say("")
    say("== DISCRIMINATING PAIRS (numeric order != string order) ==")
    if not pairs:
        say("   NONE.")
        say("")
        say("⛔ Every pair in this window orders the SAME under a numeric and a string sort, so")
        say("   clicking the header cannot distinguish a correct comparer from a broken one.")
        say("   Range here is %s .. %s. A discriminating pair needs different integer-digit"
            % (fmt(periods[0][0]), fmt(periods[-1][0])))
        say("   counts with inverting leading digits, e.g. 16.7 vs 1000.")
        say("   Re-run with a longer window, or drive an in-game action that fires a slow timer.")
        return 1
    say("   %d pair(s). The clearest:" % len(pairs))
    for x, y in pairs[:5]:
        say("      %-11s vs %-11s  numeric: %s first   string: %s first"
            % (fmt(x), fmt(y), fmt(min(x, y)), min(fmt(x), fmt(y))))
    say("")
    say("PASS: the column CAN be tested here. Sort ascending and check that %s appears above %s;"
        % (fmt(pairs[0][0]), fmt(pairs[0][1])))
    say("a string comparer puts them the other way round.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
