"""Build the C++ DLL through ninja from Python -- no PowerShell, no re-configure.

    py build_dll.py --targets UE5Dumper      # build, then check header-dep health
    py build_dll.py --deps-check             # health check only, build nothing
    py build_dll.py --list-targets

WHY THIS EXISTS. `build.ps1` is the sanctioned builder, but ad-hoc PowerShell is
blocked on this machine (Bitdefender ATD). Everything this script needs from
build.ps1 is the MSVC environment, which `cmd`+`vcvars64.bat` supplies just as well.

WHY IT ONLY BUILDS AND NEVER CONFIGURES. CLAUDE.md's loudest build warning is about
CONFIGURE, not build: cmake bakes the observed `/showIncludes` prefix into
`build/CMakeFiles/rules.ninja` as `msvc_deps_prefix`, and on this localized MSVC that
prefix is the zh-TW `注意: 包含檔案: `. Configure from a shell whose code page differs
and ninja matches nothing, so **a .h edit silently stops triggering a rebuild** and
header-pinned tests go green against objects that were never recompiled. This script
therefore refuses to configure -- it requires a tree build.ps1 already configured --
and pins the console to UTF-8 (`chcp 65001`) so the bytes cl.exe emits during THIS
build agree with the bytes cmake recorded during that configure.

The dep-health check is CLAUDE.md's own: `ninja -t deps`, where a C++ object sitting
at `#deps 0` is the broken state (`.rc.res` / `.asm.obj` legitimately have none).
It runs after every build because the failure mode is silent by construction -- the
build succeeds, and only the *staleness* of the objects is wrong.

Deliberately does NOT touch `dist/` or bump `build_number.txt`; both are build.ps1
behaviours that a verification-only build must not trigger.
"""
import argparse
import pathlib
import re
import shutil
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
BUILD = ROOT / "build"
VSWHERE = pathlib.Path(r"C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe")


def find_vcvars():
    """vcvars64.bat of the LATEST Visual Studio, the same one build.ps1 enters.

    ⚠ DO NOT HARDCODE A PATH HERE. This machine has TWO Visual Studio
    installations -- `2022\\Community` (MSVC 14.44.35207 only) and `18\\Community`
    (14.38, 14.44 and **14.51.36231**) -- and `build.ps1` resolves the newest, so
    every object already in `build/` was compiled by 14.51.

    Pointing this script at 2022's vcvars64 mixes toolsets inside ONE build
    directory. The compile stage succeeds, and the failure surfaces at LINK time
    as a couple of unresolved STL symbols in a file you never edited:

        Radar.cpp.obj : error LNK2019: unresolved external __std_rotate
        Radar.cpp.obj : error LNK2019: unresolved external __std_find_last_not_ch_pos_1

    Those are 14.51 vectorized-algorithm helpers; no .lib under 14.44.35207
    defines either (checked with dumpbin over every lib in its x64 dir). It reads
    like "the STL is broken" or "my edit broke the build" -- it is neither. It
    means the objects and the libs came from different toolsets.
    """
    if VSWHERE.is_file():
        r = subprocess.run([str(VSWHERE), "-latest", "-prerelease", "-products", "*",
                            "-property", "installationPath"],
                           capture_output=True, text=True, errors="replace")
        root = r.stdout.strip().splitlines()
        if root:
            cand = pathlib.Path(root[0]) / "VC/Auxiliary/Build/vcvars64.bat"
            if cand.is_file():
                return cand
    raise SystemExit("build_dll: FAILED -- could not locate vcvars64.bat via vswhere. "
                     "Do not fall back to a hardcoded path: picking the wrong VS mixes "
                     "toolsets in build/ and fails at link with unresolved STL symbols.")


def msvc_env():
    VCVARS = find_vcvars()
    # shell=True: the ["cmd","/c",...] list form re-quotes the path and vcvars dies
    # as an unrecognised command with empty stdout.
    r = subprocess.run(f'chcp 65001 >nul && call "{VCVARS}" >nul 2>&1 && set',
                       shell=True, capture_output=True, text=True, errors="replace")
    if r.returncode != 0:
        raise SystemExit(f"build_dll: FAILED -- vcvars64 rc={r.returncode}: {r.stderr[:400]}")
    env = dict(l.split("=", 1) for l in r.stdout.splitlines() if "=" in l)
    for k in ("INCLUDE", "LIB", "PATH"):
        if k not in env:
            raise SystemExit(f"build_dll: FAILED -- vcvars64 ran but {k} is unset")
    return env


def _ninja(env):
    p = shutil.which("ninja", path=env["PATH"]) or shutil.which("ninja")
    if not p:
        raise SystemExit("build_dll: FAILED -- ninja.exe not found on the vcvars PATH")
    return p


def require_configured():
    if not (BUILD / "build.ninja").is_file():
        raise SystemExit(
            f"build_dll: FAILED -- {BUILD} is not configured. This script deliberately will "
            f"NOT run cmake: configuring from the wrong console code page silently breaks "
            f"header dependency tracking (see the module docstring). Configure with build.ps1.")


def deps_health(env, verbose=False):
    """Return (bad_objects, total_cxx_objects). Bad = a C++ object with #deps 0."""
    r = subprocess.run([_ninja(env), "-C", str(BUILD), "-t", "deps"],
                       capture_output=True, text=True, errors="replace", env=env)
    # `ninja -t deps` exits non-zero on a stale log; that is itself information.
    bad, total = [], 0
    for line in r.stdout.splitlines():
        m = re.match(r"^(\S+):\s+#deps\s+(\d+)", line)
        if not m:
            continue
        obj, n = m.group(1), int(m.group(2))
        if obj.endswith((".rc.res", ".asm.obj")):
            continue          # legitimately have no header deps
        if not obj.endswith(".obj"):
            continue
        total += 1
        if n == 0:
            bad.append(obj)
    if verbose:
        print(f"  deps: {total} C++ objects, {len(bad)} with #deps 0")
    return bad, total


def objects_depending_on(env, header):
    """Objects whose recorded dep list mentions `header` (basename match)."""
    r = subprocess.run([_ninja(env), "-C", str(BUILD), "-t", "deps"],
                       capture_output=True, text=True, errors="replace", env=env)
    cur, hits = None, []
    for line in r.stdout.splitlines():
        m = re.match(r"^(\S+):\s+#deps\s+\d+", line)
        if m:
            cur = m.group(1)
            continue
        if cur and header in line:
            hits.append(cur)
            cur = None
    return hits


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--targets", nargs="*", default=["UE5Dumper"])
    ap.add_argument("--deps-check", action="store_true")
    ap.add_argument("--require-dep", metavar="HEADER",
                    help="hard-fail unless at least one built object records a dependency on "
                         "HEADER (e.g. Macht.h). This is the guard that matters when the point "
                         "of the build is a HEADER edit: a zero-dep object elsewhere in the tree "
                         "is irrelevant, but a missing dep on the header you just changed means "
                         "the build measured nothing.")
    ap.add_argument("--list-targets", action="store_true")
    a = ap.parse_args(argv)

    require_configured()
    env = msvc_env()

    if a.list_targets:
        r = subprocess.run([_ninja(env), "-C", str(BUILD), "-t", "targets"],
                           capture_output=True, text=True, errors="replace", env=env)
        print(r.stdout[:8000])
        return 0

    if a.deps_check:
        bad, total = deps_health(env, verbose=True)
        if bad:
            print("BAD (a .h edit will not rebuild these):")
            for b in bad[:20]:
                print("   ", b)
            return 1
        print(f"deps OK: all {total} C++ objects carry header dependencies")
        return 0

    cmd = [_ninja(env), "-C", str(BUILD)] + list(a.targets)
    print("build:", " ".join(cmd[-3:]))
    r = subprocess.run(cmd, env=env, text=True, errors="replace",
                       capture_output=True)
    tail = (r.stdout or "")[-4000:] + (r.stderr or "")[-4000:]
    # The console here is cp950 and MSVC emits localized diagnostics; printing the raw
    # text raises UnicodeEncodeError and HIDES the compile error behind a traceback.
    print(tail.encode(sys.stdout.encoding or "utf-8", "replace")
              .decode(sys.stdout.encoding or "utf-8", "replace"))
    if r.returncode != 0:
        print(f"build_dll: FAILED -- ninja rc={r.returncode}", file=sys.stderr)
        return r.returncode

    bad, total = deps_health(env, verbose=True)
    if bad:
        # WARN, not fail. Six proxy `Lugner*.cpp` objects sit at #deps 0 in this tree and
        # predate any of this work; hard-failing on them would block every legitimate build.
        # Spun off as its own task. What must never be silent is the header YOU edited.
        print(f"WARNING: {len(bad)} C++ object(s) record no header deps (pre-existing; "
              f"proxy targets). A .h edit will not rebuild these:")
        for b in bad[:20]:
            print("   ", b)
    else:
        print(f"deps OK: all {total} C++ objects carry header dependencies")

    if a.require_dep:
        n = objects_depending_on(env, a.require_dep)
        if not n:
            print(f"build_dll: FAILED -- NO built object records a dependency on "
                  f"{a.require_dep!r}. A build whose point is an edit to that header has "
                  f"therefore measured nothing.", file=sys.stderr)
            return 1
        print(f"dep guard OK: {len(n)} object(s) depend on {a.require_dep}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
