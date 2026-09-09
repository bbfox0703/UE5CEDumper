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


PCH_NOTE = ('// Added at INSTALL time, not stored in the repo copy: UE 4.15 and earlier\n'
            '// require every .cpp in a module to include the module PCH FIRST, and the\n'
            '// module name differs per project. Harmless on newer engines.\n')


def pch_mode(mod: Path):
    """(module-header name, is-IWYU, should-prepend).

    ⛔ TWO OPPOSITE RULES, AND `Build.cs` SAYS WHICH ONE APPLIES -- the engine version does not.
    Module-wide PCH (no `PCHUsage`, UE 4.15's template) wants the MODULE header first; IWYU
    (`UseExplicitOrSharedPCHs`, the 4.18/4.23/4.27 templates) wants the file's OWN header first.
    ⚠ Measured 2026-09-09: prepending unconditionally fixed 4.15 and BROKE 4.18.
    """
    pch = mod.name + ".h"
    build_cs = mod / (mod.name + ".Build.cs")
    iwyu = (build_cs.is_file()
            and "UseExplicitOrSharedPCHs" in build_cs.read_text(encoding="utf-8",
                                                                errors="replace"))
    return pch, iwyu, (mod / pch).is_file() and not iwyu


def wanted(name: str, pch: str, has_pch: bool) -> str:
    """Exactly what the installed file should contain -- one definition, so --verify cannot
    drift from what --install writes."""
    text = (HERE / name).read_text(encoding="utf-8")
    if name.endswith(".cpp") and has_pch:
        text = PCH_NOTE + ('#include "%s"\n' % pch) + text
    return text


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--project", required=True, help="folder name under %s" % PROJECTS)
    ap.add_argument("--remove", action="store_true", help="delete the fixture files again")
    ap.add_argument("--list", action="store_true", help="report what is installed, change nothing")
    ap.add_argument("--verify", action="store_true",
                    help="compare the INSTALLED copy against what this script would write now, "
                         "and exit non-zero on any difference")
    ap.add_argument("--spawn-from", metavar="STEM",
                    help="patch <STEM>.cpp's BeginPlay to spawn the fixture, so a LIVE INSTANCE "
                         "exists for the binding-read rig (the survey does not need one)")
    a = ap.parse_args()

    mod = module_dir(a.project)
    print("module     : %s" % mod)

    if a.verify:
        # ⚠ The installed copy is DERIVED, so this compares against the derivation rather than
        # against a stored second copy -- mirroring the derived form would be one more thing to
        # keep in step, which is the failure this repo keeps finding.
        pch, iwyu, has_pch = pch_mode(mod)
        bad = 0
        for n in FILES:
            p = mod / n
            if not p.is_file():
                print("  %-26s ABSENT" % n)
                bad += 1
                continue
            got = p.read_text(encoding="utf-8", errors="replace").replace("\r\n", "\n")
            want = wanted(n, pch, has_pch).replace("\r\n", "\n")
            print("  %-26s %s" % (n, "matches" if got == want else "*** DIFFERS ***"))
            bad += got != want
        print("\n%s" % ("in step with the repo copy" if not bad
                        else "%d file(s) differ -- re-run without --verify to reinstall" % bad))
        return 1 if bad else 0

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

    # ⛔ TWO OPPOSITE RULES, AND THE MODULE ITSELF SAYS WHICH ONE APPLIES.
    #   * module-wide PCH (no PCHUsage in Build.cs -- UE 4.15's template, and the default on old
    #     engines): every .cpp must include the MODULE header first, or UBT refuses outright --
    #     "All source files in module \"X\" must include the same precompiled header first."
    #   * IWYU (`PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs`, 4.16+ and every template from
    #     4.18 on): the file's OWN header must be first -- "Expected DelegatePadFixture.h to be
    #     first header included."
    # ⚠ Measured 2026-09-09: prepending unconditionally fixed 4.15 and BROKE 4.18. The engine
    # version is the wrong discriminator anyway -- 4.18 supports both and the template picks one.
    # Read Build.cs instead, which is where the answer actually lives.
    pch, iwyu, has_pch = pch_mode(mod)

    for n in FILES:
        dst = mod / n
        text = wanted(n, pch, has_pch)
        dst.write_text(text, encoding="utf-8", newline="\n")
        os.utime(dst, None)          # ⛔ see the module docstring -- this is load-bearing
        print("  installed %s (%d B, mtime stamped to now%s)"
              % (n, dst.stat().st_size,
                 ", module PCH prepended" if n.endswith(".cpp") and has_pch else ""))
    print("  PCH mode   : %s" % ("IWYU (own header first) -- no module PCH prepended" if iwyu
                                  else "module-wide PCH -- %s prepended" % pch if has_pch
                                  else "module-wide PCH, but no %s found ⚠" % pch))

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
