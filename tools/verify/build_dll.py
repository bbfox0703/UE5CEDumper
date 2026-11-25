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
at `#deps 0` is the broken state (`.rc.res` / `.asm.obj` legitimately have none) --
plus a third exception, an EMPTY translation unit; see `deps_health`. It runs after every
build because the failure mode is silent by construction -- the build succeeds, and only the
*staleness* of the objects is wrong.

Deliberately does NOT touch `dist/` or bump `build_number.txt`; both are build.ps1
behaviours that a verification-only build must not trigger.
"""
import argparse
import pathlib
import re
import shutil
import subprocess
import sys
import time

ROOT = pathlib.Path(__file__).resolve().parents[2]
BUILD = ROOT / "build"
VSWHERE = pathlib.Path(r"C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe")


def find_vcvars():
    """vcvars64.bat of the LATEST Visual Studio, the same one build.ps1 enters.

    ⚠ DO NOT HARDCODE A PATH HERE. This machine has TWO Visual Studio
    installations -- `2022\\Community` (MSVC 14.44.35207 only) and `18\\Community`
    (14.29, 14.38, 14.44 and **14.52.37025**) -- and `build.ps1` resolves the newest. A VS
    update moves the compiler out from under a configured tree: build\\'s cache still names
    the removed toolset directory, and build.ps1 must configure the tree again. Toolset
    numbers drift; read `build/CMakeCache.txt`'s CMAKE_CXX_COMPILER for the one a tree uses.

    Pointing this script at 2022's vcvars64 mixes toolsets inside ONE build
    directory. The compile stage succeeds, and the failure surfaces at LINK time
    as a couple of unresolved STL symbols in a file you never edited:

        Radar.cpp.obj : error LNK2019: unresolved external __std_rotate
        Radar.cpp.obj : error LNK2019: unresolved external __std_find_last_not_ch_pos_1

    Those are newer-toolset vectorized-algorithm helpers; no .lib under 14.44.35207
    defines either (dumpbin over every lib in its x64 dir shows it). It reads
    like "the STL is broken" or "my edit broke the build" -- it is neither. It
    means the objects and the libs came from different toolsets.
    """
    if VSWHERE.is_file():
        # -requires matches build.ps1's filter EXACTLY. Both drive the same build
        # directory, so two vswhere calls that disagree pick two toolsets -- and newer
        # objects linked against 14.44 libs is the unresolved-STL-symbol failure this
        # function's own docstring is about.
        r = subprocess.run([str(VSWHERE), "-latest", "-prerelease", "-products", "*",
                            "-requires", "Microsoft.VisualStudio.Component.VC.Tools.x86.x64",
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
        how = ("build.ps1 -Target AsanConfigure" if BUILD.name == "build_asan" else "build.ps1")
        raise SystemExit(
            f"build_dll: FAILED -- {BUILD} is not configured. This script deliberately will "
            f"NOT run cmake: configuring from the wrong console code page silently breaks "
            f"header dependency tracking (see the module docstring). Configure with {how}.")


# [CPP-HARDENING-2026-11-14] --run. Both harness summaries this tree prints; a run whose summary is missing, or says
# zero checks ran, is a FAILURE, never a pass (CLAUDE.md: a test run that executed zero tests is a failure).
SUMMARY_RES = (re.compile(r"Pass:\s+(\d+)\s+Fail:\s+(\d+)"), re.compile(r"(\d+) checks?, (\d+) failure"))
# Under ASan: a distinctive exit code (ASan's default 1 is also a one-failure harness exit) and the report text,
# which is searched for as well because a broad __except filter can swallow a report raised as an exception.
ASAN_EXIT = 86
ASAN_OPTIONS = f"exitcode={ASAN_EXIT}:print_summary=1"
ASAN_REPORT = "ERROR: AddressSanitizer"


def _summary(text):
    for rx in SUMMARY_RES:
        found = rx.findall(text)
        if found:
            n, fails = found[-1]
            return int(n), int(fails)
    return None


# The self-tests the ASan tree instruments (dll/CMakeLists.txt, ue5_apply_asan): what `--asan --run` builds and runs
# when no --targets is given. A run that reaches none of them is a failure, not a pass.
ASAN_TEST_TARGETS = ["utf8_helpers_test", "dll_helpers_test", "grausam_window_test", "sein_retention_test",
                     "methode_plugin_test", "dunste_fly_test", "mimic_mailbox_test", "stark_dispatch_test",
                     "schlacht_seethrough_test", "wirbel_teleport_test",
                     "edel_target_test", "dll_core_test"]


def run_tests(env, targets, asan):
    """Run each built *_test target and judge it. Returns the number of failures."""
    failures = 0
    tests = [t for t in targets if t.endswith("_test")]
    if not tests:
        # Without this, `--asan --run` with the default target (the DLL) runs nothing and exits 0.
        print("run: FAIL -- no *_test target among --targets, so zero tests ran")
        return 1
    for t in tests:
        exe = BUILD / "dll" / f"{t}.exe"
        if not exe.is_file():
            print(f"run: FAIL {t}: {exe} does not exist")
            failures += 1
            continue
        run_env = dict(env)
        if asan:
            deps = subprocess.run(["dumpbin", "/dependents", str(exe)], env=env, capture_output=True,
                                  text=True, errors="replace", shell=True).stdout
            if "clang_rt.asan" not in deps.lower():
                print(f"run: FAIL {t}: its import table names no clang_rt.asan runtime -- not instrumented")
                failures += 1
                continue
            run_env["ASAN_OPTIONS"] = ASAN_OPTIONS
            # The negative control first: a deliberate heap overflow must be reported, or nothing below is believed.
            neg_env = dict(run_env, UE5_ASAN_NEGATIVE_CONTROL="1")
            r = subprocess.run([str(exe)], env=neg_env, capture_output=True, cwd=str(exe.parent))
            out = (r.stdout + r.stderr).decode("utf-8", "replace")
            if r.returncode != ASAN_EXIT or "heap-buffer-overflow" not in out:
                print(f"run: FAIL {t}: the negative control was not reported (rc={r.returncode}) -- ASan is not "
                      f"live, so a clean run would mean nothing")
                failures += 1
                continue
            print(f"run: ok   {t}: negative control reported (rc={ASAN_EXIT}, heap-buffer-overflow)")
        t0 = time.monotonic()
        r = subprocess.run([str(exe)], env=run_env, capture_output=True, cwd=str(exe.parent))
        secs = time.monotonic() - t0
        out = (r.stdout + r.stderr).decode("utf-8", "replace")
        summ = _summary(out)
        problems = []
        if asan and ASAN_REPORT in out:
            first = next(ln for ln in out.splitlines() if ASAN_REPORT in ln)
            problems.append(f"ASan report: {first.strip()}")
        if asan and "asan: on" not in out:
            problems.append("no 'asan: on' banner")
        if r.returncode != 0:
            problems.append(f"exit code {r.returncode}")
        if summ is None:
            problems.append("no summary line")
        elif summ[0] == 0:
            problems.append("zero checks ran")
        elif summ[1] != 0:
            problems.append(f"{summ[1]} failure(s)")
        label = f"{summ[0]} checks" if summ else "no summary"
        if problems:
            print(f"run: FAIL {t}: {label}, {secs:.1f} s -- " + "; ".join(problems))
            failures += 1
        else:
            print(f"run: ok   {t}: {label}, 0 failures, {secs:.1f} s{' (ASan)' if asan else ''}")
    return failures


# An object emitted from a translation unit that preprocessed to NOTHING. On this tree an
# empty proxy object is ~530 bytes and the smallest real one ~11 KB — a 20x gap with nothing
# in between, because an empty MSVC COFF is just a handful of section headers plus the
# source-path debug record. 2 KB sits safely in the gap. [PROXYDEPS]
EMPTY_TU_MAX_BYTES = 2048


def deps_health(env, verbose=False):
    """Return (bad_objects, total_cxx_objects). Bad = a NON-EMPTY C++ object with #deps 0.

    ⚠ `#deps 0` alone is NOT the broken state ([PROXYDEPS-2026-08-19]). `.rc.res` and `.asm.obj`
    legitimately have none; there is a **third** exception, and it is the easy one to mistake for
    breakage: a translation unit that preprocessed to nothing. Every `Lugner*.cpp` is wrapped head-to-toe
    in `#ifdef UE5_PROXY_<FLAVOUR>_BUILD` with its `#include`s INSIDE the guard, and all four
    are compiled into all four proxy targets. In the three targets that do not define a given
    flavour the file is empty, `/showIncludes` prints nothing, and ninja records zero deps —
    correctly. Six objects, exactly the 4x4-minus-diagonal you would predict.

    So the discriminator is not the dep count but whether the object HAS ANY CONTENT. An empty
    object at #deps 0 is fine; one with real code at #deps 0 means the `msvc_deps_prefix`
    breakage CLAUDE.md warns about, and a `.h` edit will silently stop rebuilding it.
    """
    r = subprocess.run([_ninja(env), "-C", str(BUILD), "-t", "deps"],
                       capture_output=True, text=True, errors="replace", env=env)
    # `ninja -t deps` exits non-zero on a stale log; that is itself information.
    bad, empty, total = [], [], 0
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
        if n:
            continue
        # A missing object is NOT an empty one — it has simply never been built, and calling
        # that "fine" is how this check would go quiet on a tree it has not measured.
        p = BUILD / obj.replace("\\", "/")
        size = p.stat().st_size if p.exists() else None
        if size is not None and size <= EMPTY_TU_MAX_BYTES:
            empty.append((obj, size))
        else:
            bad.append(f"{obj} ({'not built' if size is None else f'{size} bytes'})")
    if verbose:
        print(f"  deps: {total} C++ objects, {len(bad)} with #deps 0 and real content, "
              f"{len(empty)} empty TUs (expected — see deps_health)")
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
    ap.add_argument("--targets", nargs="*", default=None,
                    help="ninja targets (default: UE5Dumper; with --asan --run, the five ASan self-tests)")
    ap.add_argument("--deps-check", action="store_true")
    ap.add_argument("--require-dep", metavar="HEADER",
                    help="hard-fail unless at least one built object records a dependency on "
                         "HEADER (e.g. Macht.h). This is the guard that matters when the point "
                         "of the build is a HEADER edit: a zero-dep object elsewhere in the tree "
                         "is irrelevant, but a missing dep on the header you just changed means "
                         "the build measured nothing.")
    ap.add_argument("--list-targets", action="store_true")
    ap.add_argument("--asan", action="store_true",
                    help="use build_asan\\ (configured by build.ps1 -Target AsanConfigure): the C++ self-tests "
                         "under AddressSanitizer. [CPP-HARDENING-2026-11-14]")
    ap.add_argument("--run", action="store_true",
                    help="after the build, run each *_test target and fail on a failure, a missing summary or "
                         "zero checks; with --asan also on an ASan report, and first prove ASan is live with a "
                         "deliberate heap overflow that must be reported")
    a = ap.parse_args(argv)

    global BUILD
    if a.asan:
        BUILD = ROOT / "build_asan"
    if a.targets is None:
        a.targets = list(ASAN_TEST_TARGETS) if (a.asan and a.run) else ["UE5Dumper"]
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
    # The tail above is cut at 4000 characters, so a build with many diagnostics loses some of them to it, with
    # no sign of the cut. Every compiler diagnostic line in the WHOLE output is listed here, so none is lost to
    # the cut: C-numbered ones from the compiler, and D-numbered ones from the cl driver ("Command line warning
    # D9025 : overriding ..."), which are how a rejected or overridden /we or /fsanitize flag shows.
    diag = sorted({ln.strip() for ln in (r.stdout or "").splitlines()
                   if re.search(r"\b(warning|error) [CD]\d{4}\b", ln)})
    if diag:
        print(f"compiler diagnostics in the whole output: {len(diag)} distinct line(s)")
        for ln in diag:
            print("   ", ln.encode(sys.stdout.encoding or "utf-8", "replace")
                          .decode(sys.stdout.encoding or "utf-8", "replace"))
    if r.returncode != 0:
        print(f"build_dll: FAILED -- ninja rc={r.returncode}", file=sys.stderr)
        return r.returncode

    bad, total = deps_health(env, verbose=True)
    if bad:
        # A hard FAIL, not a warning. The proxy objects at #deps 0 are EMPTY translation
        # units, not broken deps, and deps_health does not count them, so what remains is
        # real. A permanent warning is worse than either verdict: it is the state in which a
        # real breakage arrives and reads as the noise everyone already ignores.
        print(f"build_dll: FAILED -- {len(bad)} C++ object(s) with real content record NO "
              f"header deps. A .h edit will not rebuild these, so a green build measures "
              f"nothing (CLAUDE.md: msvc_deps_prefix). Re-configure via build.ps1:")
        for b in bad[:20]:
            print("   ", b)
        return 1
    print(f"deps OK: all {total} C++ objects carry header dependencies")

    if a.require_dep:
        n = objects_depending_on(env, a.require_dep)
        if not n:
            print(f"build_dll: FAILED -- NO built object records a dependency on "
                  f"{a.require_dep!r}. A build whose point is an edit to that header has "
                  f"therefore measured nothing.", file=sys.stderr)
            return 1
        print(f"dep guard OK: {len(n)} object(s) depend on {a.require_dep}")
    if a.run and run_tests(env, a.targets, a.asan):
        print("build_dll: FAILED -- a test run failed (above)", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
