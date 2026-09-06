"""Package a UE sample end-to-end from the command line. No Visual Studio, no editor GUI.

    py tools/ue-sample/repackage.py --engine 5.4 --project DumperTest --configs Development
    py tools/ue-sample/repackage.py --engine 5.4 --project DumperTest --configs Development,Shipping
    py tools/ue-sample/repackage.py --engine 5.4 --project DumperTest --compile-only

WHY THIS EXISTS. `README.md` step 6 still describes packaging through the editor --
*Platforms -> Windows -> Build Configuration -> Package Project* -- and that was the only step in
the whole recipe a human had to drive. It turned out that was not a policy, just an assumption
neither side had tested: `RunUAT BuildCookRun` does the same job headless. The maintainer's words,
2026-09-06: "Package Project 要我做不是規則, 是我不知道可自動".

⛔ THE VISUAL STUDIO IDE IS NOT IN THE LOOP AND MUST NOT BE PUT BACK IN IT.
`Build.bat` / `RunUAT.bat` invoke MSVC directly through UBT, which is shipped precompiled and
carries its own .NET. Building the GENERATED SOLUTION is the broken path -- it drags in the
engine's own `EpicGames.*` C# programs, which target net6.0 that this machine has no targeting
pack for, and VS 2026 additionally runs a one-way upgrade on the .sln first. See README.md:102.

⭐ MEASURED 2026-09-06, so the toolchain claim is not inherited from a version table:

    UE 5.4  DumperTestEditor    MSVC 14.38.33130 (inside the VS *18* install) + SDK 10.0.26100.0
                                9 actions, 20.51 s, exit 0
    UE 5.8  DumperTest58Editor  up to date, 4.81 s, exit 0
    UE 4.27 UE427_3rdPersonEditor  MSVC 14.51.36231 + SDK 10.0.26100.0, 11 actions, 37 s, exit 0

    UE 4.15 / 4.18   NO -- UBT probes HKLM\\...\\VisualStudio\\SxS\\VS7 for "12.0"/"14.0"/"15.0"
                     only (UEBuildWindows.cs:838) and BOTH the VS7 and VC7 keys are absent here.
                     Installing VS2017 creates "15.0" and would unlock them.
    UE 4.23          same, and additionally its older UBT writes into the engine install under
                     Program Files, which is not writable.

⚠ A FAILED OLD-ENGINE BUILD IS NOT AUTOMATICALLY A TOOLCHAIN ANSWER. 4.15 and 4.23 first died on
`System.UnauthorizedAccessException` and never reached compiler detection at all; 4.27 is *also*
non-writable there and built fine, because newer UBT writes user-local. Read the exception.

CONSEQUENCES OF A REPACKAGE -- stated up front because they are not obvious:
  * the `pe_hash` changes, which ORPHANS the per-game app-data folders keyed by it
    (`Snapshots\\`, `Bookmarks\\`, `TeleportCoords\\` under %LOCALAPPDATA%\\UE5CEDumper);
  * `package-identity.json` is invalidated until re-captured (this script does it for you);
  * the configs are packaged SEPARATELY -- a fixture added today reaches Development and NOT
    Shipping until Shipping is built too. That asymmetry has caused a wrong "the fixture is
    missing" conclusion before.

⛔ It refuses to archive into `Varies Version builds\\`: `inventory_builds.py` and `preflight.py`
treat that tree as the AOB corpus and CI asserts its row counts, so a new folder there drifts them.
"""
import argparse
import os
import re
import subprocess
import sys
import time

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))

ENGINE_ROOT = "C:\\Program Files\\Epic Games\\UE_%s"
PROJECTS_ROOT = "D:\\Unreal Projects"
DEFAULT_ARCHIVE = "D:\\UE_Analyze_data\\For Testing"

# The five files package-identity.json hashes. Kept in sync with capture_package_identity.SOURCES
# deliberately by duplication rather than import, so a rename there fails loudly here.
MIRROR_SOURCES = ["DumperTestActor.h", "DumperTestActor.cpp", "DumperTestTypes.h",
                  "DumperTestSubsystem.h", "DumperTestSubsystem.cpp"]

FORBIDDEN_ARCHIVE = "varies version builds"

# Lines worth echoing from a UBT/UAT run. Everything else is thousands of lines of noise, and
# printing all of it is how a real error gets scrolled past.
KEEP = re.compile(
    r"Using Visual Studio|toolchain|Windows SDK|must be installed|Building \d+ action|"
    r"Total execution|ERROR|error [A-Z]+\d{2,}|Target is up to date|Unable to find|"
    r"BUILD SUCCESSFUL|BUILD FAILED|AutomationTool exiting|ExitCode|Cook failed|"
    r"UnauthorizedAccess|Exception|WARNING: .*deprecat", re.I)


def run(argv, label, timeout):
    """Run one batch step, echo only the interesting lines, return (rc, seconds)."""
    print("  $ %s" % " ".join('"%s"' % a if " " in a else a for a in argv[:3]))
    t0 = time.time()
    try:
        r = subprocess.run(argv, capture_output=True, text=True, encoding="utf-8",
                           errors="replace", timeout=timeout)
    except subprocess.TimeoutExpired:
        print("  !! %s TIMED OUT after %d s" % (label, timeout))
        return 124, timeout
    dt = time.time() - t0
    shown = 0
    for ln in ((r.stdout or "") + "\n" + (r.stderr or "")).splitlines():
        s = ln.strip()
        if s and KEEP.search(s):
            print("     %s" % s[:170])
            shown += 1
            if shown > 40:
                print("     ... (further matching lines suppressed)")
                break
    print("  -- %s exit=%d  %.1f s" % (label, r.returncode, dt))
    return r.returncode, dt


def mirror_state(project):
    """Is the in-repo mirror identical to the real project's source?

    Only DumperTest has a mirror in this repo; anything else returns None rather than
    pretending to have checked.
    """
    if project != "DumperTest":
        return None
    import hashlib

    def h(d):
        x = hashlib.sha256()
        for n in MIRROR_SOURCES:
            p = os.path.join(d, n)
            if not os.path.exists(p):
                return None
            x.update(n.encode("utf-8"))
            with open(p, "rb") as fh:
                x.update(fh.read().replace(b"\r\n", b"\n"))
        return x.hexdigest()

    a = h(os.path.join(HERE, "DumperTest", "Source", "DumperTest"))
    b = h(os.path.join(PROJECTS_ROOT, "DumperTest", "Source", "DumperTest"))
    return (a, b, a is not None and a == b)


UBT_CFG = os.path.join(os.environ.get("APPDATA", ""), "Unreal Engine", "UnrealBuildTool",
                       "BuildConfiguration.xml")

PIN_XML = ('<?xml version="1.0" encoding="utf-8" ?>\n'
           '<Configuration xmlns="https://www.unrealengine.com/BuildConfiguration">\n'
           '  <WindowsPlatform>\n'
           '    <CompilerVersion>%s</CompilerVersion>\n'
           '  </WindowsPlatform>\n'
           '</Configuration>\n')


class CompilerPin(object):
    """Temporarily pin the MSVC toolset, then put the config back exactly as it was.

    ⚠ THIS SETTING IS GLOBAL TO EVERY INSTALLED ENGINE. `WindowsPlatform.CompilerVersion` is
    [XmlConfigFile] only -- 4.23 exposes `-2015`/`-2017`/`-2019` on the command line but NOT the
    specific version -- and the only writable config location is under %APPDATA%. The per-engine
    location (`UE_<ver>\\Engine\\Saved\\UnrealBuildTool\\`) sits under Program Files and is not
    writable here. Leaving a pin behind would force 5.4 / 5.8 onto the wrong toolset too, so the
    restore is a finally, and it is verified by comparing bytes rather than assumed.
    """

    def __init__(self, version):
        self.version = version
        self.original = None
        self.existed = False

    def __enter__(self):
        if not self.version:
            return self
        if os.path.exists(UBT_CFG):
            self.existed = True
            with open(UBT_CFG, "rb") as fh:
                self.original = fh.read()
            body = self.original.decode("utf-8", "replace")
            if "CompilerVersion" in body:
                raise SystemExit(
                    "!! %s already pins a CompilerVersion. Refusing to overwrite it blind --\n"
                    "   inspect it and decide by hand." % UBT_CFG)
        os.makedirs(os.path.dirname(UBT_CFG), exist_ok=True)
        with open(UBT_CFG, "w", encoding="utf-8") as fh:
            fh.write(PIN_XML % self.version)
        print("[pin] MSVC pinned GLOBALLY to %s for the duration of this run" % self.version)
        return self

    def __exit__(self, *exc):
        if not self.version:
            return False
        if self.existed:
            with open(UBT_CFG, "wb") as fh:
                fh.write(self.original)
            with open(UBT_CFG, "rb") as fh:
                ok = fh.read() == self.original
            print("[pin] original BuildConfiguration.xml restored: %s"
                  % ("byte-for-byte OK" if ok else "*** MISMATCH -- CHECK BY HAND ***"))
        else:
            if os.path.exists(UBT_CFG):
                os.remove(UBT_CFG)
            print("[pin] temporary BuildConfiguration.xml removed")
        return False


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--engine", required=True, help='engine version, e.g. "5.4" or "5.8"')
    ap.add_argument("--project", required=True, help='project name, e.g. "DumperTest"')
    ap.add_argument("--configs", default="Development",
                    help="comma-separated: Development,Shipping,DebugGame")
    ap.add_argument("--archive", default=None,
                    help="archive root; default %s\\<project>" % DEFAULT_ARCHIVE)
    ap.add_argument("--compile-only", action="store_true",
                    help="stop after Build.bat -- the UHT/compile legality gate, ~20 s, no cook")
    ap.add_argument("--sync-mirror", action="store_true",
                    help="copy the in-repo mirror OVER the real project's source first")
    ap.add_argument("--pin-compiler", default=None, metavar="VER",
                    help="pin the MSVC toolset, e.g. 14.29.30133 -- REQUIRED for UE 4.23, which "
                         "otherwise picks 14.51 and dies on error C4865 (/Zc:enumTypes). The knob "
                         "is GLOBAL to every engine, so it is snapshotted and restored.")
    ap.add_argument("--timeout", type=int, default=5400, help="per-step timeout in seconds")
    args = ap.parse_args()

    eng = ENGINE_ROOT % args.engine
    proj_dir = os.path.join(PROJECTS_ROOT, args.project)
    uproject = os.path.join(proj_dir, args.project + ".uproject")
    build_bat = os.path.join(eng, "Engine", "Build", "BatchFiles", "Build.bat")
    runuat = os.path.join(eng, "Engine", "Build", "BatchFiles", "RunUAT.bat")
    archive = args.archive or os.path.join(DEFAULT_ARCHIVE, args.project)

    for label, p in (("engine", eng), ("project", proj_dir), ("uproject", uproject),
                     ("Build.bat", build_bat), ("RunUAT.bat", runuat)):
        if not os.path.exists(p):
            print("!! %s not found: %s" % (label, p))
            return 2

    if FORBIDDEN_ARCHIVE in archive.lower():
        print("!! refusing to archive into 'Varies Version builds' -- that tree is the AOB corpus\n"
              "   and CI asserts its row counts (inventory_builds.py / preflight.py).")
        return 2

    print("=" * 78)
    print("UE %s   %s   configs=%s" % (args.engine, args.project, args.configs))
    print("archive: %s" % archive)
    print("=" * 78)

    # ---- mirror check -------------------------------------------------------------
    st = mirror_state(args.project)
    if st is None:
        print("\n[mirror] no in-repo mirror for this project -- nothing to compare")
    else:
        repo_h, real_h, same = st
        print("\n[mirror] repo %s" % (repo_h[:16] if repo_h else "MISSING"))
        print("         real %s   %s" % (real_h[:16] if real_h else "MISSING",
                                         "IN SYNC" if same else "*** DRIFT ***"))
        if args.sync_mirror and not same:
            src = os.path.join(HERE, "DumperTest", "Source", "DumperTest")
            dst = os.path.join(PROJECTS_ROOT, "DumperTest", "Source", "DumperTest")
            import shutil
            for n in MIRROR_SOURCES:
                shutil.copy2(os.path.join(src, n), os.path.join(dst, n))
            print("         copied %d files repo -> real project" % len(MIRROR_SOURCES))
        elif not same:
            print("         ⚠ packaging would build the REAL project's source, not the mirror's.\n"
                  "           Pass --sync-mirror if the repo copy is the one you edited.")

    configs = [c.strip() for c in args.configs.split(",") if c.strip()]

    with CompilerPin(args.pin_compiler):
        # ---- step 1: the compile / UHT legality gate ------------------------------
        print("\n[1/3] Build.bat %sEditor Win64 Development" % args.project)
        rc, _ = run([build_bat, args.project + "Editor", "Win64", "Development",
                     "-Project=" + uproject, "-WaitMutex",
                     "-Log=" + os.path.join(proj_dir, "Saved", "Logs", "ubt-repackage.log")],
                    "Build.bat", args.timeout)
        if rc != 0:
            print("\n!! the editor module did not build -- nothing was cooked.\n"
                  "   ⚠ UnauthorizedAccessException on the ENGINE folder is a PERMISSION problem,\n"
                  "     not a missing compiler -- it fires before compiler detection and hides the\n"
                  "     real answer. ⚠ `error C4865` on /Zc:enumTypes means the toolset is too new\n"
                  "     for this engine: retry with --pin-compiler (UE 4.23 needs 14.29.30133).")
            return 1
        if args.compile_only:
            print("\n--compile-only: stopping here. Nothing was cooked, no package was touched.")
            return 0

        # ---- step 2: cook + stage + pak + archive, once per config ----------------
        for i, cfg in enumerate(configs, 1):
            print("\n[2/3] BuildCookRun %s  (%d of %d)" % (cfg, i, len(configs)))
            rc, dt = run([runuat, "BuildCookRun",
                          "-project=" + uproject,
                          "-noP4", "-nop4", "-utf8output",
                          "-platform=Win64",
                          "-clientconfig=" + cfg,
                          "-cook", "-build", "-stage", "-pak", "-prereqs",
                          "-archive", "-archivedirectory=" + os.path.join(archive, cfg),
                          ], "BuildCookRun " + cfg, args.timeout)
            if rc != 0:
                print("\n!! %s failed. ⚠ Check for `ensureMsgf` on a CHEAT cvar set from an ini --\n"
                      "   that is 22 errors and ExitCode=25 and it is the only distinct error in a\n"
                      "   198 KB log. README.md 'The cook-breaking ini' has the whole story." % cfg)
                return 1

    # ---- step 3: re-capture the identity record ----------------------------------
    print("\n[3/3] capture_package_identity.py")
    cap = os.path.join(HERE, "capture_package_identity.py")
    rc, _ = run([sys.executable, cap, archive, "--project", args.project], "capture", 900)

    print("\n" + "=" * 78)
    print("DONE. ⚠ The pe_hash changed, so the per-game app-data folders keyed by it are now")
    print("orphaned: %LOCALAPPDATA%\\UE5CEDumper\\{Snapshots,Bookmarks,TeleportCoords}\\")
    if len(configs) == 1:
        print("⚠ Only %s was packaged. The other configs still carry the OLD source." % configs[0])
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
