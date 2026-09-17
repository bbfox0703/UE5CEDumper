#!/usr/bin/env python3
r"""Build the vendored **Dumper-7** so its `.usmap` can be re-taken from a given package.

    py tools/verify/build_dumper7.py --build-dir <dir>
    py tools/verify/build_dumper7.py --build-dir <dir> --clean

⛔ NOT A GATE, and it never will be. It compiles a THIRD-PARTY dumper whose only use is to
be injected into a running game, and CI has neither a game nor a reason to build it. Every
path is an argument with no default, for `usmap_reader.py`'s stated reason.

WHY THIS EXISTS. `[A4-USMAP-ENUM-UNDERLYING]` (L30) is a cross-check against the CANONICAL
writer: our mapping is only interesting if an independent one agrees with it. The 2026-09-16
run built Dumper-7 by hand and left nothing behind, so the next re-take started from zero --
and a re-take is not optional:

⛔⛔ **BOTH MAPPINGS MUST COME FROM THE SAME PACKAGED BUILD.** A `.usmap` is reflection data,
so two exports taken from two different packages differ by the PACKAGE, and a diff over them
measures the repackage rather than the writer. Anything that forces a repackage -- adding a
UPROPERTY, authoring the usmap probe -- invalidates every previously captured mapping,
whoever wrote it.

⛔ DUMPER-7 CALLS INTO THE GAME. `main.cpp` ProcessEvents `KismetSystemLibrary::GetGameName`
and `GetEngineVersion` before it dumps. Inject it into the DISPOSABLE FIXTURE ONLY, never
into a commercial title.

⚠ IT WRITES OUTSIDE THE REPO BY DESIGN: `Settings::SDKGenerationPath` is `C:/Dumper-7`
(Dumper/Settings.h:45) and `GlobalConfigPath` is `C:/Dumper-7/Dumper-7.ini`. Nothing here
changes that -- patching a vendored third party's defaults is how a comparison stops being a
comparison against the real tool. Clean that folder up afterwards; the 2026-09-16 run did.

⚠ ITS OUTPUT IS ZSTANDARD-COMPRESSED (compression byte 3) where ours is 0. `usmap_reader.py`
handles both (`pip install zstandard`); a reader that assumes 0 reports an empty file rather
than an error.
"""
import argparse
import os
import shutil
import subprocess
import sys

# ⚠ MSVC speaks the console codepage, and this box is cp950: one undecodable byte in a
# compiler diagnostic crashed the whole script with UnicodeEncodeError AFTER a successful
# build, which reads exactly like a build failure. Measured 2026-09-17.
try:
    sys.stdout.reconfigure(errors="replace")
    sys.stderr.reconfigure(errors="replace")
except Exception:
    pass

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
SRC = os.path.join(REPO, "vendor", "Dumper-7")

sys.path.insert(0, HERE)
from build_dll import msvc_env, _ninja                 # noqa: E402  the vcvars64 shell dance


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--build-dir", required=True,
                    help="where to configure and build; use a scratch dir, not the repo")
    ap.add_argument("--clean", action="store_true", help="delete the build dir first")
    args = ap.parse_args()

    if not os.path.isfile(os.path.join(SRC, "CMakeLists.txt")):
        raise SystemExit("build_dumper7: %s has no CMakeLists.txt -- run "
                         "`git submodule update --init --recursive`" % SRC)

    build = os.path.abspath(args.build_dir)
    if args.clean and os.path.isdir(build):
        shutil.rmtree(build)
    os.makedirs(build, exist_ok=True)

    env = msvc_env()
    ninja = _ninja(env)
    cmake = shutil.which("cmake", path=env["PATH"]) or shutil.which("cmake")
    if not cmake:
        raise SystemExit("build_dumper7: cmake.exe not found on the vcvars PATH")

    # ⚠ Ninja, NOT the bundled vs2022 preset: that one is a Visual Studio generator and
    # drags the IDE's MSBuild in, which README.md:102 rules out for this machine.
    cfg = [cmake, "-S", SRC, "-B", build, "-G", "Ninja",
           "-DCMAKE_BUILD_TYPE=Release",
           "-DCMAKE_MAKE_PROGRAM=" + ninja]
    print("$ cmake -S %s -B %s -G Ninja" % (SRC, build))
    r = subprocess.run(cfg, env=env, capture_output=True, text=True, errors="replace")
    if r.returncode != 0:
        print(r.stdout[-3000:])
        print(r.stderr[-3000:])
        raise SystemExit("build_dumper7: configure FAILED rc=%d" % r.returncode)

    print("$ cmake --build %s" % build)
    r = subprocess.run([cmake, "--build", build], env=env,
                       capture_output=True, text=True, errors="replace")
    print(r.stdout[-1500:])
    if r.returncode != 0:
        print(r.stderr[-3000:])
        raise SystemExit("build_dumper7: build FAILED rc=%d" % r.returncode)

    dlls = []
    for root, _dirs, files in os.walk(build):
        for f in files:
            if f.lower().endswith(".dll"):
                dlls.append(os.path.join(root, f))
    if not dlls:
        raise SystemExit("build_dumper7: the build reported success but produced no .dll")

    for d in dlls:
        print("  %s  (%d bytes)" % (d, os.path.getsize(d)))
    print("\nNext: launch the fixture, then")
    print("    py tools/verify/inject.py --name DumperTest --dll %s" % dlls[0])
    print("and collect the .usmap from C:/Dumper-7/<GameName>/Mappings/ (Settings.h:45).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
