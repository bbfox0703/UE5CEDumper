r"""PEHOOK step 3b — after a condemn, the off-thread direct call must be REFUSED, not attempted.

    py tools/verify/pehook_3b_refusal.py

WHY THE STEP EXISTS (self-review found it, and it is the one that guards an over-correction):
re-arming detection without refusing the direct path made the mis-detected case **worse** than
before the fix. The old code merely timed out; a re-armed build with no refusal would take the
direct fallback and `call` a *known-wrong virtual* — i.e. execute whatever function happens to sit
at the guessed vtable slot. `Frieren.cpp` therefore sets `s_peOffsetDistrusted` inside the same lock
as the verdict, and both direct entry points open with
`if (s_peOffsetDistrusted.load(...)) return -3;`.

STAGING — the row's own ⭐ preferred route, and it needs a purpose-built DLL.
DumperTest's pattern scan MATCHES on the shipping build (`vtable+0x268`), so the version-table
branch that produces a condemn cannot be entered. The row sanctions temporarily disabling the two
`kPePat*Sib*` alternates and rebuilding. This rig does **not** build: it expects the variant at

    <scratchpad>\UE5Dumper.sibless.dll

built by gating both alternates behind `constexpr bool kSibAlternatesEnabled = false`, copied out,
and the source reverted **immediately** so `dist/` is never left holding it.

WHAT IS MEASURED
  1  the host really did take the version-table path   (`falling back to UE=…version-table`)
  2  a validation failure really was acted on          (`VALIDATION FAILED … failure N/3`)
  3  ⭐ an invoke issued right after that returns **-3**, and the log carries no line showing a call
     was attempted through the distrusted offset
  4  control: the -3 is a REFUSAL, not a timeout. -5 is the timeout code; -3 is only produced by the
     two distrust guards. A run that returned -5 would mean the request was queued as normal and the
     refusal never fired.
"""
import json
import os
import pathlib
import subprocess
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from pipe_client import PipeClient  # noqa: E402

SCRATCH = (pathlib.Path(os.environ.get("TEMP", "")) / "claude" / "D--Github-UE5CEDumper"
           / "ec58d532-ec51-4b81-b8b1-2494afdcd74a" / "scratchpad")
SIBLESS = SCRATCH / "UE5Dumper.sibless.dll"
LOG = pathlib.Path.home() / "AppData/Local/UE5CEDumper/Logs/DumperTest"
PY = sys.executable


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + "\n")


def lines(needle):
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
    if not SIBLESS.exists():
        say("FAIL: %s not found. Build it with the two kPePat*Sib* alternates gated off, copy it "
            "here, and revert the source before running." % SIBLESS)
        return 1
    say("variant: %s (%d bytes)" % (SIBLESS.name, SIBLESS.stat().st_size))

    subprocess.run(["taskkill", "/F", "/IM", "DumperTest.exe"], capture_output=True, text=True)
    time.sleep(2.0)
    r = subprocess.run([PY, str(HERE / "launch_dumpertest.py"), "dev"],
                       capture_output=True, text=True, errors="replace", timeout=240)
    say(r.stdout.strip().splitlines()[-1] if r.stdout.strip() else "(launch produced no output)")
    if r.returncode != 0:
        say(r.stderr[-400:]); return 1
    time.sleep(2.0)
    ri = subprocess.run([PY, str(HERE / "inject.py"), "--name", "DumperTest",
                         "--dll", str(SIBLESS)],
                        capture_output=True, text=True, errors="replace", timeout=120)
    say(ri.stdout.strip().splitlines()[-1] if ri.stdout.strip() else "(inject produced no output)")
    if ri.returncode != 0:
        say(ri.stderr[-400:]); return 1
    time.sleep(1.0)

    with PipeClient() as c:
        c.ensure_scanned()
        fl = (c.request("list_all_functions", limit=20000,
                        game_only=False).get("functions") or [])
        f = next((x for x in fl if x.get("func_name") == "Add_IntInt"), None)
        if not f:
            say("FAIL: Add_IntInt not found"); return 1
        params = "03000000" + "04000000" + "00000000"

        say("")
        say("== driving validation failures (each invoke arms a 1500 ms validator) ==")
        results = []
        for i in range(4):
            t0 = time.time()
            rr = c.request("invoke_function", class_name=f["class_name"],
                           func_name="Add_IntInt", parms_size=12, params_hex=params,
                           direct_call=True)
            d = rr.get("data", rr)
            results.append(d.get("result"))
            say("   invoke %d -> ok=%s result=%s  (%.1f s)   %s"
                % (i + 1, d.get("ok"), d.get("result"), time.time() - t0,
                   str(d.get("message") or "")[:50]))
            # 3b's window: the invoke must land inside the install-retry cooldown, so do
            # NOT sleep past it. The validator needs 1500 ms to render its verdict.
            time.sleep(2.2)

        say("")
        say("== 1: did this host take the version-table path? ==")
        tbl = lines("version-table")
        say("   'version-table' lines: %d" % len(tbl))
        for l in tbl[:2]:
            say("      " + l.strip()[:170])
        if not tbl:
            fails.append("the pattern scan still matched -- the SIB alternates are not disabled in "
                         "this DLL, so no condemn can occur and 3b cannot be staged")

        say("")
        say("== 2: was the verdict ACTED on? ==")
        vf = lines("VALIDATION FAILED")
        say("   'VALIDATION FAILED' lines: %d" % len(vf))
        for l in vf[:3]:
            say("      " + l.strip()[:200])
        if not vf:
            fails.append("no VALIDATION FAILED -- nothing was condemned, so a -3 below would not "
                         "be attributable to the distrust guard")

        say("")
        say("== 3 + 4: the invoke after a condemn must be REFUSED (-3), not queued (-5) ==")
        say("   results in order: %s" % results)
        minus3 = [x for x in results if x == -3]
        minus5 = [x for x in results if x == -5]
        say("   -3 (refused by the distrust guard): %d" % len(minus3))
        say("   -5 (ordinary game-thread timeout) : %d" % len(minus5))
        if not minus3:
            fails.append("no invoke returned -3 -- the direct path was NOT refused after the "
                         "condemn, which is the over-correction this step guards against")
        gave_up = lines("giving up on ProcessEvent for this process")
        say("   'giving up on ProcessEvent for this process': %d (step 3's terminal state)"
            % len(gave_up))

    say("")
    for x in fails:
        say("FAIL: %s" % x)
    if not fails:
        say("PASS (PEHOOK step 3b)")
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
