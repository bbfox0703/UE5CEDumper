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
   `-ExecCmds` IS SILENTLY DISCARDED ON THE `shipping` FLAVOUR. UE 5.4's Misc/Exec.h:11-17
   defines UE_ALLOW_EXEC_COMMANDS as UE_ALLOW_EXEC_COMMANDS_IN_SHIPPING when
   (UE_BUILD_SHIPPING && !WITH_EDITOR), and UnrealBuildTool sets that to 0 unless the
   Target.cs opts in (UEBuildTarget.cs:5147/5151); GameEngine.cpp wraps its exec handling
   in the macro. So `-ExecCmds=t.MaxFPS 15` never reached the CVar, and a timed window
   with AD4_GetContestWrites measured **59.8 Hz**.

   ⚠ BUT "so Shipping ran UNCAPPED" -- said here earlier the same day -- WAS WRONG, and
   the 59.8 is the tell. DumperTest caps ITSELF: DumperTestSubsystem.cpp ApplyMaxFPS
   defaults t.MaxFPS to 60 and applies it from C++ with ECVF_SetByCode, which the Shipping
   console restriction does not touch (only ProcessUserConsoleInput refuses cheat cvars).
   Past Shipping batches ran at the sample's own 60, not unbounded.

   The sample exposes `-DumperTestMaxFPS=N` for exactly this, so the cap is now requested
   through the switch that actually works in every flavour. Two lessons, and the second is
   the one that keeps costing: (1) a careful choice of MECHANISM is worthless if the
   mechanism is gated out of the build you launch; (2) when a measurement contradicts a
   configured value, the next question is "what else sets this?", not "so it is unset".
"""
import argparse
import os
import pathlib
import subprocess
import sys
import time

ROOT = pathlib.Path(r"D:\UE_Analyze_data\for testing\DumperTest")
ROOT58 = pathlib.Path(r"D:\UE_Analyze_data\for testing\DumperTest58")
FLAVOURS = {
    "dev": ROOT / "Development/Windows/DumperTest/Binaries/Win64/DumperTest.exe",
    "shipping": ROOT / "Shipping/Windows/DumperTest/Binaries/Win64/DumperTest-Win64-Shipping.exe",
    "debug": ROOT / "DebugGame/Windows/DumperTest/Binaries/Win64/DumperTest-Win64-DebugGame.exe",
    # ⚠⚠ THE 5.8 FIXTURE IS A DIFFERENT PROJECT, NOT ANOTHER FLAVOUR OF THIS ONE.
    # DumperTest58 is the STOCK UE 5.8 Third Person template (with the Combat /
    # Platforming / SideScrolling variants). It does NOT contain the property zoo --
    # no ADumperTestActor, no Spawn_*, no mutators, no heartbeat HUD. Measured
    # 2026-09-06: `DumperTestActor` gets zero utf-16le hits in either exe.
    #
    # So it answers ENGINE-LAYOUT questions only (FUObjectItem reorder, FFieldClass
    # vfptr, version detection, envelope sizes) -- which is exactly what audits A1,
    # A2 and A4 used it for. Do not point a property-shape row at it and read a
    # missing field as a defect.
    "dev58": ROOT58 / "Development/Windows/DumperTest58/Binaries/Win64/DumperTest58.exe",
    "shipping58": ROOT58 / "Shipping/Windows/DumperTest58/Binaries/Win64/DumperTest58-Win64-Shipping.exe",
}
# The 5.8 fixture takes NO sample-specific switch, because none of them exist in it.
IS_58 = {"dev58", "shipping58"}
# THREE flavours, and the exe NAME is not derivable from the folder: Development's binary is
# plain `DumperTest.exe` with no suffix, the other two carry `-Win64-<Flavour>`. A glob written
# as `DumperTest-Win64*.exe` silently finds two of three and reports the third as absent --
# which is how a "the Development package was never rebuilt" conclusion gets manufactured.
#
# WHICH ONE TO USE IS A REAL DECISION, not a default. UE_WITH_CHEAT_MANAGER is
# `(1 && !UE_BUILD_SHIPPING)`, so `dev`/`debug` have a live UCheatManager and `shipping` does not;
# Shipping also drops most logging (Build.h NO_LOGGING) and editor-only reflection metadata.
# A row whose claim could depend on any of that must say which flavour it was run on -- and the
# honest ones get run on more than one. See docs/verification-register.md, the DumperTest fixture rows.
# 1280x720 windowed, 15 fps -- see the module docstring.
# -DumperTestMaxFPS is the sample's OWN switch, applied from C++ with ECVF_SetByCode
# (DumperTestSubsystem.cpp ApplyMaxFPS), so unlike -ExecCmds it survives a Shipping
# package. -ExecCmds is kept alongside it only as a belt for any future sample that
# lacks the switch; on this one it is inert in Shipping and redundant elsewhere.
HOUSE_ARGS = ["-windowed", "-ResX=1280", "-ResY=720",
              "-DumperTestMaxFPS=15", "-ExecCmds=t.MaxFPS 15"]

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

    if a.flavour in IS_58:
        # ⚠ Strip every DumperTest-specific switch. `-DumperTestMaxFPS` and
        # `-DumperTestIdle` are the SAMPLE's own C++ (DumperTestSubsystem.cpp); the stock
        # 5.8 template has neither, so passing them is inert at best and misleading in a
        # launch line that claims a 15 fps cap it never applied. `-ExecCmds` is kept: 5.8
        # Development honours it, which is the only cap available here.
        house = ["-windowed", "-ResX=1280", "-ResY=720", "-ExecCmds=t.MaxFPS 15"]
        if a.idle:
            print("launch_dumpertest.py: FAILED -- --idle is a DumperTest-only switch; the "
                  "stock 5.8 template has no -DumperTestIdle handler", file=sys.stderr)
            return 1
    else:
        house = HOUSE_ARGS

    args = [str(exe)] + house + (["-DumperTestIdle"] if (a.idle and a.flavour not in IS_58) else [])
    print("launching:", " ".join(args))
    if a.flavour == "shipping58":
        print("  note: Shipping discards -ExecCmds, and the 5.8 template has no self-cap "
               "of its own (that is DumperTest's ApplyMaxFPS, which does not exist here). "
               "This one runs UNCAPPED -- measure the rate for any timing-sensitive row.")
    if a.flavour == "shipping":
        # Say it at the point of use: an operator reading the launch line sees BOTH
        # switches and should know which of the two is doing the work here.
        print("  note: shipping discards -ExecCmds "
              "(UE_ALLOW_EXEC_COMMANDS_IN_SHIPPING=0); the cap comes from "
              "-DumperTestMaxFPS, applied from C++. Still MEASURE the rate for any "
              "row whose result depends on it.")
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
