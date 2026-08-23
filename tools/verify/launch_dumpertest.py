"""Launch a DumperTest flavour with the house window/FPS settings, and wait for it.

    py launch_dumpertest.py dev            # Development  (UCheatManager live, full logging)
    py launch_dumpertest.py shipping       # Shipping     (the closest analogue to a real game)
    py launch_dumpertest.py debug          # DebugGame    (added 2026-08-23)
    py launch_dumpertest.py dev --idle     # ...with -DumperTestIdle (B8's deferred half ONLY)
    py launch_dumpertest.py dev --no-wait  # return as soon as the process exists

Prints the PID and writes it to out/host.pid so the injector and the killer agree
on one target.

WHY THE ARGS ARE HERE AND NOT IN EACH CALL SITE. The maintainer asked (2026-08-19)
that DumperTest run at **1280x720, FPS-capped to 15** so an all-night batch does not
load the machine -- this PC also drives the game under test, and a free-running UE
sample at native resolution competes with it. Hard-coding that in one launcher means
a later row cannot quietly launch at 4K/unbounded and skew a timing measurement.

TWO ARG TRAPS, both already paid for elsewhere in this repo:
  * `-DumperTestIdle` is **opt-in**, never a default. The session plan calls it out
    twice: B8's deferred half REQUIRES it, and the D2 sample-heartbeat row breaks if
    it is present. So it is a flag here, not a constant.
  * FPS is capped with `t.MaxFPS` via `-ExecCmds`, deliberately NOT with `-BENCHMARK
    -FPS=15`. The latter switches UE to a FIXED TIMESTEP, which silently changes what
    every timing- or tick-sensitive row is measuring.

!! AND THE THIRD, WHICH THAT SECOND BULLET WALKED STRAIGHT INTO (measured 2026-08-23):
   THE FPS CAP DOES NOTHING ON THE `shipping` FLAVOUR. UE 5.4's Misc/Exec.h:11-17
   defines UE_ALLOW_EXEC_COMMANDS as UE_ALLOW_EXEC_COMMANDS_IN_SHIPPING when
   (UE_BUILD_SHIPPING && !WITH_EDITOR), and UnrealBuildTool sets that to 0 unless the
   Target.cs opts in (UEBuildTarget.cs:5147/5151); GameEngine.cpp wraps its exec
   handling in the macro. So `-ExecCmds` is silently discarded in a Shipping package.
   MEASURED with AD4_GetContestWrites over a timed window: **59.8 Hz**, not 15. Every
   past all-night Shipping batch ran uncapped, and any Shipping measurement that
   ASSUMED 15 FPS was taken at ~60.

   The bullet above is a careful decision about WHICH mechanism to cap with, made
   without checking that the chosen mechanism runs at all in the flavour being
   launched. Keep the reasoning; check the gate. If a row needs a real cap, run it on
   `dev`/`debug` (where -ExecCmds works), or MEASURE the rate and report it -- which
   is what tools/verify/ad4_contested.py now does instead of quoting a configured one.
"""
import argparse
import os
import pathlib
import subprocess
import sys
import time

ROOT = pathlib.Path(r"D:\UE_Analyze_data\for testing\DumperTest")
FLAVOURS = {
    "dev": ROOT / "Development/Windows/DumperTest/Binaries/Win64/DumperTest.exe",
    "shipping": ROOT / "Shipping/Windows/DumperTest/Binaries/Win64/DumperTest-Win64-Shipping.exe",
    "debug": ROOT / "DebugGame/Windows/DumperTest/Binaries/Win64/DumperTest-Win64-DebugGame.exe",
}
# THREE flavours, and the exe NAME is not derivable from the folder: Development's binary is
# plain `DumperTest.exe` with no suffix, the other two carry `-Win64-<Flavour>`. A glob written
# as `DumperTest-Win64*.exe` silently finds two of three and reports the third as absent --
# which is how a "the Development package was never rebuilt" conclusion gets manufactured.
#
# WHICH ONE TO USE IS A REAL DECISION, not a default. UE_WITH_CHEAT_MANAGER is
# `(1 && !UE_BUILD_SHIPPING)`, so `dev`/`debug` have a live UCheatManager and `shipping` does not;
# Shipping also drops most logging (Build.h NO_LOGGING) and editor-only reflection metadata.
# A row whose claim could depend on any of that must say which flavour it was run on -- and the
# honest ones get run on more than one. See docs/todo.md, the DumperTest fixture section.
# 1280x720 windowed, 15 fps -- see the module docstring.
HOUSE_ARGS = ["-windowed", "-ResX=1280", "-ResY=720", "-ExecCmds=t.MaxFPS 15"]

DETACHED = 0x00000008 | 0x00000200  # DETACHED_PROCESS | CREATE_NEW_PROCESS_GROUP


def alive(pid):
    out = subprocess.run(["tasklist", "/FI", f"PID eq {pid}", "/FO", "CSV", "/NH"],
                         capture_output=True, text=True, errors="replace").stdout
    return str(pid) in out


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("flavour", choices=sorted(FLAVOURS))
    ap.add_argument("--idle", action="store_true",
                    help="add -DumperTestIdle (B8's deferred half; breaks the D2 heartbeat row)")
    ap.add_argument("--wait", type=int, default=25, help="seconds to let the sample come up")
    ap.add_argument("--no-wait", action="store_true")
    a = ap.parse_args(argv)

    exe = FLAVOURS[a.flavour]
    if not exe.is_file():
        print(f"launch_dumpertest.py: FAILED -- not found: {exe}", file=sys.stderr)
        return 1

    args = [str(exe)] + HOUSE_ARGS + (["-DumperTestIdle"] if a.idle else [])
    print("launching:", " ".join(args))
    if a.flavour == "shipping":
        # Say it at the point of use, not only in the docstring: an operator reading
        # the launch line sees `t.MaxFPS 15` and reasonably believes it took effect.
        print("  !! shipping: -ExecCmds is discarded in a Shipping package "
              "(UE_ALLOW_EXEC_COMMANDS_IN_SHIPPING=0), so t.MaxFPS 15 does NOT apply "
              "-- measured 59.8 Hz. Measure the rate; do not assume 15.")
    p = subprocess.Popen(args, cwd=str(exe.parent), creationflags=DETACHED)

    out = pathlib.Path(__file__).resolve().parents[2] / "out"
    out.mkdir(exist_ok=True)
    (out / "host.pid").write_text(str(p.pid))

    if not a.no_wait:
        time.sleep(a.wait)
    if not alive(p.pid):
        print(f"launch_dumpertest.py: FAILED -- pid {p.pid} died within {a.wait}s "
              f"(a dead host makes every downstream 'nothing found' meaningless)", file=sys.stderr)
        return 1
    print(f"pid {p.pid} alive; written to {out / 'host.pid'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
