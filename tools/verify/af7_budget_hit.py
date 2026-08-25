r"""AF7 — `walk_function_props` must report whether the native disassembler ran out of budget.

    py tools/verify/af7_budget_hit.py            # launch + inject DumperTest, then probe
    py tools/verify/af7_budget_hit.py --attach   # use the host already running

THE ROW. "Call `walk_function_props` on a NATIVE (non-Blueprint) UFunction and check the reply has
`budget_hit`. If it is true, the Props dialog's status turns amber and says 'hit its instruction
budget', and Interesting Functions' Uses column shows `⚠ partial`."

⚠ **"THE KEY EXISTS" IS ALMOST A TAUTOLOGY, AND THAT IS THE TRAP.** `Fern.cpp:4750` writes
`data["budget_hit"] = res.budgetHit;` unconditionally, so *every* reply carries it — including
replies from the **bytecode** path, where `budgetHit` is structurally always false because no
disassembler ran. Asserting the key on a Blueprint function therefore proves nothing at all. The
check only means something when `method == "disasm"`, i.e. Path 2 actually executed, so this rig
refuses to pass on anything else.

WHAT IT REPORTS, in the order the row cares about:
  1. the key is present on a reply whose `method` is `disasm`      — the row's stated PASS
  2. the distribution of `method` across the native functions probed — so a host where Path 2 never
     runs cannot look like a pass
  3. whether any function on this host trips the budget at all (`kInstrBudget = 8192`,
     `Denken.cpp:17`) — and if none does, it says so rather than implying the flag was exercised

⚠ **A `false` on every function is NOT evidence the flag works.** It is the value a hardwired
`false` would also produce. Finding a `true` needs a native function big enough to decode 8192
instructions; the row suggests grepping the DLL log for `AnalyzeNativeFunctionProps ... BUDGET`.
If nothing here trips it, that is an honest NOT_RUNNABLE for the second half, which the row itself
allows ("兩項都可能因為找不到樣本而測不了，那也是結論").
"""
import argparse
import collections
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
FUNC_NATIVE = 0x0000_0400
INSTR_BUDGET = 8192          # Denken.cpp:17 kInstrBudget
PROBE = 400                  # native functions to walk


def say(s=""):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + chr(10))
    sys.stdout.flush()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--attach", action="store_true")
    ap.add_argument("--probe", type=int, default=PROBE)
    a = ap.parse_args()

    if not a.attach:
        subprocess.run(["taskkill", "/F", "/IM", "DumperTest.exe"], capture_output=True)
        time.sleep(1.5)
        subprocess.Popen([EXE, "-windowed", "-ResX=1024", "-ResY=576", "-ExecCmds=t.MaxFPS 60"])
        time.sleep(22)
        r = subprocess.run([sys.executable, str(HERE / "inject.py"), "--name", "DumperTest"],
                           capture_output=True, text=True, encoding="utf-8", errors="replace")
        say((r.stdout or r.stderr or "").strip()[-110:])
        if r.returncode != 0:
            say("FAIL: could not inject")
            return 2
        time.sleep(2)

    with PipeClient() as c:
        c.assert_build()
        c.ensure_scanned()
        g = c.request("list_all_functions", limit=200000)
        rows = g.get("results") or g.get("functions") or []
        native = [r for r in rows if (r.get("function_flags") or 0) & FUNC_NATIVE]
        say("")
        say("%d function(s); %d carry FUNC_Native (0x400)" % (len(rows), len(native)))
        if not native:
            say("NOT_RUNNABLE: no native UFunction on this host")
            return 2

        # biggest first — a budget hit needs a big body, and ParmsSize is the only size
        # proxy available before walking
        native.sort(key=lambda r: -(r.get("parms_size") or 0))
        probe = native[:a.probe]
        say("walking %d of them with walk_function_props..." % len(probe))

        methods = collections.Counter()
        missing_key = []
        hits = []
        disasm_example = None
        for r in probe:
            addr = r.get("func_addr")
            if not addr:
                continue
            rep = c.request("walk_function_props", func_addr=addr)
            d = rep.get("data", rep)
            if "budget_hit" not in d:
                missing_key.append((r.get("class_name"), r.get("func_name"), d.get("method")))
                continue
            m = d.get("method") or "?"
            methods[m] += 1
            if m == "disasm" and disasm_example is None:
                disasm_example = (r, d)
            if d.get("budget_hit"):
                hits.append((r, d))

    say("")
    say("method distribution: %s" % dict(methods))
    say("replies MISSING the budget_hit key: %d" % len(missing_key))
    for cls, fn, m in missing_key[:5]:
        say("   %s::%s  (method=%s)" % (cls, fn, m))

    fails = []
    if missing_key:
        fails.append("%d reply/replies omitted `budget_hit` entirely" % len(missing_key))
    if methods.get("disasm", 0) == 0:
        say("")
        say("NOT_RUNNABLE: Path 2 (`disasm`) never ran on this host, so the key was only ever")
        say("              observed on a path that cannot set it. That is not the row's check.")
        return 2

    r0, d0 = disasm_example
    say("")
    say("a reply from the DISASM path — the only one where the flag can be non-trivial:")
    say("   %s::%s" % (r0.get("class_name"), r0.get("func_name")))
    say("   method=%s  budget_hit=%s  unmapped=%s  props=%d"
        % (d0.get("method"), d0.get("budget_hit"), d0.get("unmapped"),
           len(d0.get("props") or d0.get("refs") or [])))

    say("")
    if hits:
        say("⭐ %d function(s) TRIPPED the %d-instruction budget:" % (len(hits), INSTR_BUDGET))
        for r_, d_ in hits[:6]:
            say("   %s::%s  props=%d unmapped=%s"
                % (r_.get("class_name"), r_.get("func_name"),
                   len(d_.get("props") or d_.get("refs") or []), d_.get("unmapped")))
        say("   -> the UI half is now stageable: open Props on one of these and the status must")
        say("      be amber and say 'hit its instruction budget'.")
    else:
        say("ℹ️ NO function on this host trips the budget, so `budget_hit` was `false` everywhere.")
        say("   ⚠ That is NOT evidence the flag works — a hardwired false looks identical. The")
        say("   first half of the row (the key is present on a real disasm reply) is settled; the")
        say("   second half (a true, and the amber UI) needs a bigger native function than this")
        say("   host has. The row allows that outcome explicitly.")

    if fails:
        say("")
        say("AF7 step 3: FAIL")
        for f in fails:
            say("   - %s" % f)
        return 1
    say("")
    say("AF7 step 3: PASS on the stated check — `budget_hit` is present on every reply, including")
    say("            %d from the disasm path where it is meaningful." % methods["disasm"])
    return 0 if hits else 3      # 3 = passed the stated check, second half unexercised


if __name__ == "__main__":
    raise SystemExit(main())
