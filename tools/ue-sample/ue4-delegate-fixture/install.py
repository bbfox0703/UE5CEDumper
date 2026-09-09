r"""Install (or remove) the portable delegate-layout fixture in a UE C++ project.

    py tools/ue-sample/ue4-delegate-fixture/install.py --project UE427_3rdPerson
    py tools/ue-sample/ue4-delegate-fixture/install.py --project UE427_3rdPerson --remove
    py tools/ue-sample/ue4-delegate-fixture/install.py --project UE427_3rdPerson --list

WHAT IT DOES. Copies `DelegatePadFixture.{h,cpp}` into the project's single Source module and
stamps their mtime to NOW.

⛔ THE MTIME STAMP IS NOT COSMETIC. `shutil.copy2` preserves the SOURCE file's mtime, and
UnrealHeaderTool decides whether to re-run by comparing each header against the module's
`Intermediate/Build/.../UHT/Timestamp` -- which any earlier build in the same session has already
pushed forward. A file copied in with an older mtime is invisible to UHT: it regenerates nothing,
UBT compiles against the PREVIOUS reflection data, and the build reports **BUILD SUCCESSFUL**.
Measured 2026-09-09 on DumperTest: two new UPROPERTYs and a UFUNCTION were packaged away, the game
booted normally, and the fields were simply absent from `walk_instance`. The only tell was a
`.generated.h` still carrying the previous day's timestamp. `repackage.py --sync-mirror` carries
the same stamp for the same reason.

⚠ WHAT IT DOES NOT DO. It does not place the actor in a level. `tools/verify/d4b_pad_survey.py`
needs only the CLASS -- a UCLASS in a game module is registered in GObjects at module load -- so
the survey works with the copy alone. `d4b_delegate_pad.py` reads BINDINGS and therefore needs a
live instance; `--spawn-from <ClassStem>` patches that class's `BeginPlay` to spawn one.

⚠ VERIFY, DO NOT ASSUME, that the fixture reached the package: after building, check that the
class is actually there rather than trusting the build's exit code --

    py tools/verify/d4b_pad_survey.py            # DelegatePadFixture must appear in the walk
"""
from __future__ import annotations

import argparse
import os
import shutil
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
PROJECTS = Path(r"D:\Unreal Projects")
FILES = ("DelegatePadFixture.h", "DelegatePadFixture.cpp")

SPAWN_MARK = "// [DELEGATE-PAD-FIXTURE]"


def module_dir(project: str) -> Path:
    src = PROJECTS / project / "Source"
    if not src.is_dir():
        raise SystemExit("%s has no Source/ -- it is a Blueprint-only project, and a UPROPERTY "
                         "fixture needs a C++ module. Convert it in the editor first, or pick a "
                         "project that already has one." % project)
    # A template project has exactly one runtime module directory beside the .Target.cs files.
    mods = [d for d in src.iterdir() if d.is_dir()]
    if len(mods) != 1:
        raise SystemExit("expected exactly one module under %s, found %s -- name it explicitly "
                         "in the code rather than guessing" % (src, [m.name for m in mods]))
    return mods[0]


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--project", required=True, help="folder name under %s" % PROJECTS)
    ap.add_argument("--remove", action="store_true", help="delete the fixture files again")
    ap.add_argument("--list", action="store_true", help="report what is installed, change nothing")
    ap.add_argument("--spawn-from", metavar="STEM",
                    help="patch <STEM>.cpp's BeginPlay to spawn the fixture, so a LIVE INSTANCE "
                         "exists for the binding-read rig (the survey does not need one)")
    a = ap.parse_args()

    mod = module_dir(a.project)
    print("module     : %s" % mod)

    if a.list:
        for n in FILES:
            p = mod / n
            print("  %-26s %s" % (n, "present (%d B)" % p.stat().st_size if p.is_file()
                                  else "ABSENT"))
        return 0

    if a.remove:
        gone = 0
        for n in FILES:
            p = mod / n
            if p.is_file():
                p.unlink()
                gone += 1
                print("  removed %s" % n)
        # ⚠ The generated reflection files are NOT removed -- UHT rewrites them from whatever
        # headers remain, and deleting them by hand is how a half-cleaned Intermediate tree
        # produces a link error nobody can place.
        print("removed %d file(s). Rebuild to drop the class from the module." % gone)
        return 0

    for n in FILES:
        dst = mod / n
        shutil.copy2(HERE / n, dst)
        os.utime(dst, None)          # ⛔ see the module docstring -- this is load-bearing
        print("  installed %s (%d B, mtime stamped to now)" % (n, dst.stat().st_size))

    if a.spawn_from:
        host = mod / (a.spawn_from + ".cpp")
        if not host.is_file():
            raise SystemExit("no such host file: %s" % host)
        text = host.read_text(encoding="utf-8", errors="replace")
        if SPAWN_MARK in text:
            print("  %s already spawns the fixture" % host.name)
            return 0
        raise SystemExit(
            "--spawn-from is not automated: BeginPlay's exact shape differs per template and a\n"
            "blind regex patch of someone else's project is the wrong trade. Add by hand to\n"
            "%s, inside BeginPlay:\n\n"
            "    %s\n"
            "    #include \"DelegatePadFixture.h\"   // at the top\n"
            "    GetWorld()->SpawnActor<ADelegatePadFixture>();\n\n"
            "The SURVEY does not need this -- only the binding-read rig does." % (host, SPAWN_MARK))

    print("\nInstalled. Next:\n"
          "  py tools/ue-sample/repackage.py --engine <ver> --project %s --compile-only\n"
          "  ...then package, run the game, inject, and:\n"
          "  py tools/verify/d4b_pad_survey.py --expect-pad 0     # UE4 has no access detector"
          % a.project)
    return 0


if __name__ == "__main__":
    sys.exit(main())
