#!/usr/bin/env python3
"""Does the build use MSVC when another toolchain is on PATH, in the environment or in a kept cache?
The acceptance test of the MSVC toolchain guards [MSVC-GUARD-2026-11-19].

    py tools/verify/msvc_guard_probe.py cmake-guard   --cxx <non-MSVC C++ compiler>
    py tools/verify/msvc_guard_probe.py inherited-env --prepend "<dir>;<dir>"
    py tools/verify/msvc_guard_probe.py stale-cache   --cxx <non-MSVC C++ compiler> [--cache-type STRING]
    py tools/verify/msvc_guard_probe.py cxx-env       --cxx <non-MSVC C++ compiler>
    py tools/verify/msvc_guard_probe.py platform-env  [--platform x86]
    py tools/verify/msvc_guard_probe.py inherited-stripped --path "<dir>;<dir>"
    py tools/verify/msvc_guard_probe.py aot-link      --prepend "<dir>;<dir>" [--direct]

The foreign toolchain is always an argument: CI has no second toolchain, and a gate must not reach outside the repo.
With MSYS2 and Git for Windows installed, `--cxx C:/msys64/mingw64/bin/g++.exe` and
`--prepend "C:/Program Files/Git/usr/bin;C:/msys64/mingw64/bin"` are the hostile inputs: both usr/bin folders hold a
coreutils `link.exe`, and mingw64/bin holds `c++.exe`, which CMake's Ninja generator takes from the first PATH folder
that has any compiler it knows.

cmake-guard    configure a scratch tree under out/ with that compiler, bypassing build.ps1.
               PASS: the configure fails with the project's own "builds with MSVC" message.
inherited-env  remove build_asan/, then run `build.ps1 -Target AsanConfigure` in an MSVC developer environment
               (vcvars64) whose PATH had the given folders put in front of it afterwards: the shell build.ps1 reuses
               without entering the DevShell again. PASS: exit 0, and the new cache names MSVC's cl.exe and link.exe.
stale-cache    plant build_asan/CMakeCache.txt naming that compiler, as a tree configured from another shell would,
               then run `build.ps1 -Target AsanConfigure`. PASS: exit 0, and the new cache names MSVC's cl.exe.
               --cache-type picks the entry's type: CMake writes FILEPATH for a compiler it found, STRING or
               UNINITIALIZED for one given with -D.
cxx-env        remove build_asan/, then run `build.ps1 -Target AsanConfigure` with CXX / CC naming that compiler, which
               CMake takes before any PATH search. PASS: exit 0, and the new cache names MSVC's cl.exe.
platform-env   run `build.ps1 -Target AsanConfigure` with Platform set, as every vcvarsall prompt sets it (x86 and arm64
               ones go through the fresh DevShell path). PASS: exit 0 and the log says Platform was cleared -- MSBuild
               would read it as a property and move the C# outputs to bin\\<platform>\\.
inherited-stripped  remove build_asan/, then run `build.ps1 -Target AsanConfigure` in a vcvars64 environment whose PATH
               was replaced by the given folders, as MSYS2's minimal PATH does: VSCMD_ARG_TGT_ARCH says x64, but no
               MSVC tool is on PATH. PASS: exit 0, and the new cache names MSVC's cl.exe.
aot-link       run `build.ps1 -Mode Publish` in the inherited environment above. PASS: exit 0, and dist/UE5DumpUI.exe
               was written by this run and is the Native AOT size, not the ~107 MB self-contained one.
               --direct instead runs the `dotnet publish` build.ps1 runs, into out/, without build.ps1: what
               ILCompiler's environmental `link` resolves to in that PATH.

Every build.ps1 run bumps build_number.txt, as every build does. Logs land in out/msvc_guard_probe/.
"""
from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
import time

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
BUILD_PS1 = os.path.join(ROOT, "build.ps1")
ASAN_DIR = os.path.join(ROOT, "build_asan")
OUT = os.path.join(ROOT, "out", "msvc_guard_probe")
UI_PROJ = os.path.join(ROOT, "ui", "UE5DumpUI", "UE5DumpUI.csproj")
DIST_EXE = os.path.join(ROOT, "dist", "UE5DumpUI.exe")
GUARD_TEXT = "builds with MSVC"
AOT_MAX_BYTES = 80 * 1024 * 1024   # the trimmed AOT exe is ~54 MB, the self-contained one ~107 MB

sys.stdout.reconfigure(encoding="utf-8", errors="replace")


def vcvars_path(given: str | None) -> str:
    if given:
        return given
    vswhere = os.path.join(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)"),
                           "Microsoft Visual Studio", "Installer", "vswhere.exe")
    out = subprocess.run([vswhere, "-latest", "-products", "*", "-prerelease", "-requires",
                          "Microsoft.VisualStudio.Component.VC.Tools.x86.x64", "-property", "installationPath"],
                         capture_output=True, text=True).stdout.strip().splitlines()
    if not out:
        sys.exit("no Visual Studio with the C++ x64 tools found; pass --vcvars")
    return os.path.join(out[0], "VC", "Auxiliary", "Build", "vcvars64.bat")


def msvc_env(vcvars: str) -> dict[str, str]:
    """The environment vcvars64.bat leaves behind, keys upper-cased so PATH is a single key."""
    # One string, not a list: list2cmdline would escape the inner quotes, and cmd would then not find the .bat.
    raw = subprocess.run(f'cmd /d /s /c "call "{vcvars}" >nul && set"', capture_output=True).stdout
    env = {}
    for line in raw.decode("mbcs", errors="replace").splitlines():
        key, sep, value = line.partition("=")
        if sep and key:
            env[key.upper()] = value
    if env.get("VSCMD_ARG_TGT_ARCH") != "x64":
        sys.exit(f"vcvars64 did not produce an x64 environment: {vcvars}")
    return env


def plain_env() -> dict[str, str]:
    return {k.upper(): v for k, v in os.environ.items()}


def hostile(env: dict[str, str], prepend: str) -> dict[str, str]:
    env = dict(env)
    env["PATH"] = prepend.replace("/", "\\") + ";" + env["PATH"]
    return env


def run(cmd: list[str], env: dict[str, str], log_name: str) -> int:
    os.makedirs(OUT, exist_ok=True)
    log = os.path.join(OUT, log_name)
    with open(log, "wb") as fh:
        rc = subprocess.run(cmd, cwd=ROOT, env=env, stdout=fh, stderr=subprocess.STDOUT).returncode
    print(f"  {os.path.basename(cmd[0])} ... exit {rc}  (log: {os.path.relpath(log, ROOT)})")
    return rc


def build_ps1(args: list[str], env: dict[str, str], log_name: str) -> int:
    return run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", BUILD_PS1] + args, env, log_name)


def cache_entries(build_dir: str) -> dict[str, str]:
    path = os.path.join(build_dir, "CMakeCache.txt")
    entries = {}
    if os.path.exists(path):
        for line in open(path, encoding="utf-8", errors="replace"):
            key, sep, value = line.rstrip("\n").partition("=")
            if sep:
                entries[key.split(":", 1)[0]] = value
    return entries


def is_msvc_tool(path: str, leaf: str) -> bool:
    p = path.replace("\\", "/").lower()
    return "/vc/tools/msvc/" in p and p.endswith("/" + leaf)


def judge_cache(build_dir: str, want_linker: bool) -> bool:
    c = cache_entries(build_dir)
    checks = [("CMAKE_CXX_COMPILER", "cl.exe"), ("CMAKE_C_COMPILER", "cl.exe")]
    if want_linker:
        checks.append(("CMAKE_LINKER", "link.exe"))
    ok = True
    for key, leaf in checks:
        value = c.get(key, "<missing>")
        good = is_msvc_tool(value, leaf)
        ok &= good
        print(f"  {key:20} {'MSVC ' if good else 'WRONG'} {value}")
    return ok


def foreign_c_compiler(cxx: str) -> str:
    folder, leaf = os.path.split(cxx)
    for name in ("gcc.exe", "clang.exe", "cc.exe"):
        if os.path.exists(os.path.join(folder, name)):
            return os.path.join(folder, name)
    return cxx


def scenario_cmake_guard(a) -> bool:
    env = hostile(msvc_env(vcvars_path(a.vcvars)), os.path.dirname(a.cxx))
    tree = os.path.join(OUT, "cmake_guard_tree")
    shutil.rmtree(tree, ignore_errors=True)
    rc = run(["cmake", "-S", ROOT, "-B", tree, "-G", "Ninja", f"-DCMAKE_CXX_COMPILER={a.cxx}",
              f"-DCMAKE_C_COMPILER={foreign_c_compiler(a.cxx)}"], env, "cmake_guard.log")
    text = open(os.path.join(OUT, "cmake_guard.log"), encoding="utf-8", errors="replace").read()
    refused = rc != 0 and GUARD_TEXT in text
    print(f"  configure {'refused with the guard message' if refused else 'was NOT refused by the guard'}")
    return refused


def scenario_inherited_env(a) -> bool:
    env = hostile(msvc_env(vcvars_path(a.vcvars)), a.prepend)
    shutil.rmtree(ASAN_DIR, ignore_errors=True)
    rc = build_ps1(["-Target", "AsanConfigure"], env, "inherited_env.log")
    return judge_cache(ASAN_DIR, want_linker=True) and rc == 0


def scenario_stale_cache(a) -> bool:
    shutil.rmtree(ASAN_DIR, ignore_errors=True)
    os.makedirs(ASAN_DIR)
    with open(os.path.join(ASAN_DIR, "CMakeCache.txt"), "w", encoding="utf-8", newline="\n") as fh:
        fh.write("# planted by tools/verify/msvc_guard_probe.py stale-cache\n")
        fh.write(f"CMAKE_CXX_COMPILER:{a.cache_type}={a.cxx.replace(os.sep, '/')}\n")
        fh.write(f"CMAKE_C_COMPILER:{a.cache_type}={foreign_c_compiler(a.cxx).replace(os.sep, '/')}\n")
    rc = build_ps1(["-Target", "AsanConfigure"], plain_env(), f"stale_cache_{a.cache_type.lower()}.log")
    return judge_cache(ASAN_DIR, want_linker=False) and rc == 0


def scenario_platform_env(a) -> bool:
    env = plain_env()
    env["PLATFORM"] = a.platform
    rc = build_ps1(["-Target", "AsanConfigure"], env, "platform_env.log")
    text = open(os.path.join(OUT, "platform_env.log"), encoding="utf-8", errors="replace").read()
    cleared = f"Platform={a.platform}" in text and "cleared" in text
    print(f"  Platform={a.platform}: {'cleared' if cleared else 'NOT cleared'}")
    return cleared and rc == 0


def scenario_inherited_stripped(a) -> bool:
    env = msvc_env(vcvars_path(a.vcvars))
    env["PATH"] = a.path.replace("/", "\\")
    shutil.rmtree(ASAN_DIR, ignore_errors=True)
    rc = build_ps1(["-Target", "AsanConfigure"], env, "inherited_stripped.log")
    return judge_cache(ASAN_DIR, want_linker=True) and rc == 0


def scenario_cxx_env(a) -> bool:
    env = plain_env()
    env["CXX"] = a.cxx
    env["CC"] = foreign_c_compiler(a.cxx)
    shutil.rmtree(ASAN_DIR, ignore_errors=True)
    rc = build_ps1(["-Target", "AsanConfigure"], env, "cxx_env.log")
    return judge_cache(ASAN_DIR, want_linker=False) and rc == 0


def scenario_aot_link(a) -> bool:
    env = hostile(msvc_env(vcvars_path(a.vcvars)), a.prepend)
    if a.direct:
        rc = run(["dotnet", "publish", UI_PROJ, "-c", "Release", "-r", "win-x64", "-p:PublishAot=true",
                  "-p:IlcUseEnvironmentalTools=true", "-o", os.path.join(OUT, "aot_direct"), "--nologo"],
                 env, "aot_direct.log")
        return rc == 0
    start = time.time()
    rc = build_ps1(["-Mode", "Publish"], env, "aot_publish.log")
    fresh = os.path.exists(DIST_EXE) and os.path.getmtime(DIST_EXE) >= start
    size = os.path.getsize(DIST_EXE) if os.path.exists(DIST_EXE) else 0
    print(f"  dist/UE5DumpUI.exe: {'written by this run' if fresh else 'NOT written by this run'}, {size:,} bytes")
    return rc == 0 and fresh and 0 < size < AOT_MAX_BYTES


SCENARIOS = {"cmake-guard": scenario_cmake_guard, "inherited-env": scenario_inherited_env,
             "stale-cache": scenario_stale_cache, "cxx-env": scenario_cxx_env, "platform-env": scenario_platform_env,
             "inherited-stripped": scenario_inherited_stripped, "aot-link": scenario_aot_link}


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("scenario", choices=sorted(SCENARIOS))
    ap.add_argument("--cxx", help="a non-MSVC C++ compiler (cmake-guard, stale-cache, cxx-env)")
    ap.add_argument("--prepend", help="folders put in front of PATH, ';'-separated (inherited-env, aot-link)")
    ap.add_argument("--direct", action="store_true", help="aot-link: dotnet publish without build.ps1")
    ap.add_argument("--cache-type", default="FILEPATH", choices=["FILEPATH", "STRING", "UNINITIALIZED"],
                    help="stale-cache: the type of the planted compiler entries")
    ap.add_argument("--platform", default="x86", help="platform-env: the Platform value a vcvarsall prompt sets")
    ap.add_argument("--path", help="inherited-stripped: the whole PATH, ';'-separated")
    ap.add_argument("--vcvars", help="vcvars64.bat to use (default: the newest VS with the C++ x64 tools)")
    a = ap.parse_args()
    if a.scenario in ("cmake-guard", "stale-cache", "cxx-env") and not (a.cxx and os.path.exists(a.cxx)):
        ap.error(f"{a.scenario} needs --cxx naming an existing compiler")
    if a.scenario in ("inherited-env", "aot-link") and not a.prepend:
        ap.error(f"{a.scenario} needs --prepend")
    if a.scenario == "inherited-stripped" and not a.path:
        ap.error("inherited-stripped needs --path")
    print(f"== {a.scenario}")
    ok = SCENARIOS[a.scenario](a)
    print("PASS" if ok else "FAIL")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
