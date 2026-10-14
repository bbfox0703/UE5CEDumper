"""Build the synthetic marker exes B25 and [VER-410-GATE] need, and judge their scan logs.
No game, no PowerShell.

    py b25_marker_exes.py build      # compile all six into out/b25/ (E and F inside install trees)
    py b25_marker_exes.py check      # judge each exe's scan-0.log, once the DLL has scanned it
    py b25_marker_exes.py selftest   # the judges against canned logs, offline
    py b25_marker_exes.py clean

B25 has two opposite branches and one exe cannot exercise both:

  A  `b25a_subfloor.exe` -- carries a PE VERSIONINFO whose PRODUCTVERSION is
     4.5.0.0. `Genau::DetectVersionFromPEResource`'s `major == 4` branch turns that
     into 405, which is below `Grimoire::MIN_SUPPORTED_UE_VERSION` (411). The FIX
     under test is that this no longer short-circuits as tier 1: it must log
     "below the 411 floor -- NOT accepting that on its own", drop to tier 3
     (= low confidence), and let the scan RUN.
     PASS = that line present AND no "SKIPPING the scan".

  B  `b25b_ue3.exe` -- carries NO version resource at all (so the PE path returns 0
     and the terminal branch is reached structurally) plus two of the four
     UE3 markers `CountPreUE4Markers` looks for: the literals "UnrealEngine3" and
     "SeqAct_Interp". Threshold is 2 of 4.
     PASS = "PRE-UE4 engine POSITIVELY identified (2/4 markers, 2 needed)" AND
     "FindAll: PRE-UE4 engine (Unreal Engine 3) -- SKIPPING the scan".

  B is the direction that must still REFUSE. Testing only A would let a fix that
  disarmed the gate entirely pass, which is the whole reason the row names both.

[VER-410-GATE] adds a pair of its own, because A's lesson ("one fixed field must not
refuse") went one step too far: nothing below the floor could be confident, so the
first genuine 4.10 title met (IS Defense, 4.10.2) was scanned instead of refused.

  C  `b25c_corroborated.exe` -- FILEVERSION / PRODUCTVERSION 4,10,2,0 plus IS
     Defense's ProductVersion string, the engine's own build string
     `4.10.2-0+++depot+UE4-Releases+4.10`. The resources corroborate the 410, so it
     is tier 1 and the gate refuses it.
     PASS = "CORROBORATED by the exe's own engine build string", "UE Version = 410
     (tier=1", "older than the minimum supported 411 -- SKIPPING the scan", and no
     GObjects batch at all.

  D  `b25d_bare.exe` -- 4,10,3,0 plus a bare ProductVersion string `4.10.3`, a version
     a game team could have typed. Nothing corroborates it, so it is A's shape at 410.
     PASS = "below the 411 floor", "UE Version = 410 (tier=3", and no "SKIPPING the
     scan".

  C without D would pass a gate that refuses every 4.10 reading again, which is the
  B25 defect; D without C would pass today's broken build.

[VER-410-RIG-EF] adds the two shapes a CrashReportClient decides. Each exe is laid out
as an install -- out/b25/<stem>_root/B25Game/Binaries/Win64/<stem>.exe, with a copy of
C (fixed 4.10.2.0 + IS Defense's build string) at <stem>_root/Engine/Binaries/Win64/
CrashReportClient.exe, the first place Grimoire::CrashReportCandidates looks -- and is
started from THERE, not from out/b25/. A-D have no CrashReportClient above them, and
`build` checks both, through a port of that lookup.

  E  `b25e_crc_agrees.exe` -- game fixed fields 1.0.0.0 under IS Defense's
     ProductVersion string (dll/tests/res/game_buildstring_410.rc). The exe reads 410
     out of its own build string, which cannot corroborate itself; the
     CrashReportClient agreeing with it does, so it is refused.
     PASS = "is the engine's build string -> 410", the CrashReportClient in the layout
     read as 410, "AGREE on 410", "CORROBORATED by a CrashReportClient agreeing with
     the exe", "UE Version = 410 (tier=1", the SKIPPING line, and none of: the exe's
     own build string corroborating, "NOT accepting", a GObjects batch.

  F  `b25f_crc_alone.exe` -- a game version and nothing else
     (dll/tests/res/game_only.rc), so the exe reads nothing and the CrashReportClient's
     410 stands alone: one signal, scanned at tier 3. PASS = the CrashReportClient line,
     "CrashReportClient says UE 410, below the <floor> floor, and the game exe's own PE
     VERSIONINFO read nothing usable", "PE resource failed", the note under it naming
     the CrashReportClient, "UE Version = 410 (tier=3", and no "SKIPPING the scan",
     "CORROBORATED", "AGREE on" or "PE VERSIONINFO says UE 410" (its number in the
     exe's mouth, the wording before the [VER-410-GATE] review).

  E without F would pass a gate that lets one CrashReportClient refuse on its own; F
  without E would pass rev 8, which read nothing out of E's string.

RUNNING THEM is a live step this script does not take: start an exe, inject the DLL
(tools/verify/inject.py, or CE + the .CT's init), and trigger the scan -- injection
alone starts the pipe and scans nothing. Then `check`. The floor is read from
Grimoire.h, never typed. ⚠ A, D and F are SAVED to the hint cache after their scan, so a
second run of the same exe skips detection; `check` says so, and `build` mints a new PE
hash. B, C and E are refused before the save, so they re-detect on every run.

THE NEGATIVE CONTROL IS FREE AND ALREADY MEASURED. A stock `python.exe` reaches the
identical terminal branch and logs "pre-UE4 markers 0/4, below the 2 needed" -- same
code path, same host shape, markers absent. So B's 2/4 is a difference made by the
two literals and nothing else. Re-run it with tools/verify/inject.py on a plain
python sleeper if the control is ever wanted fresh.

Why compiled rather than a patched copy of a real exe: resource-patching a signed
system binary is indistinguishable from what malware does, and this machine runs
Bitdefender with ATD active. A 2 KB purpose-built exe states its own intent.

MSVC is driven through `cmd /c vcvars64.bat && set` -- cmd, not PowerShell, which is
blocked here.
"""
import os
import pathlib
import re
import struct
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
OUT = ROOT / "out" / "b25"
VCVARS = pathlib.Path(r"C:\Program Files\Microsoft Visual Studio\2022\Community"
                      r"\VC\Auxiliary\Build\vcvars64.bat")

# Sleep long enough to be injected and scanned, but never forever: an orphaned
# marker exe left running would be indistinguishable from a real host next session.
SRC_COMMON = r"""
#include <windows.h>
#include <stdio.h>
int main(int argc, char** argv) {
    (void)argv;
%s
    Sleep(3600000);
    return 0;
}
"""

# The two literals are exported DATA so the linker cannot fold or strip them, and
# referenced under a branch that never runs so the optimizer cannot constant-fold
# the whole thing away. They must survive into the MAPPED image -- CountPreUE4Markers
# scans base..base+SizeOfImage, not the file.
SRC_B_GLOBALS = r"""
__declspec(dllexport) const char* const g_marker0 = "UnrealEngine3";
__declspec(dllexport) const char* const g_marker1 = "SeqAct_Interp";
"""

RC_A = """1 VERSIONINFO
FILEVERSION 4,5,0,0
PRODUCTVERSION 4,5,0,0
FILEOS 0x4L
FILETYPE 0x1L
BEGIN
  BLOCK "StringFileInfo"
  BEGIN
    BLOCK "040904b0"
    BEGIN
      VALUE "FileDescription", "UE5CEDumper B25 branch-A marker (synthetic)"
      VALUE "FileVersion", "4.5.0.0"
      VALUE "ProductName", "B25 SubFloor Marker"
      VALUE "ProductVersion", "4.5.0.0"
    END
  END
  BLOCK "VarFileInfo"
  BEGIN
    VALUE "Translation", 0x409, 1200
  END
END
"""

# C copies IS Defense's resource as measured: fixed 4.10.2.0 and a ProductVersion string
# only, no FileVersion string, under the same 0409/1200 translation.
IS_DEFENSE_BUILD_STRING = "4.10.2-0+++depot+UE4-Releases+4.10"
# The resource script lives in dll/tests/res, where dll_core_test builds it into a resource-only DLL and
# judges it offline ([VER-410-GATE] review): one copy, so the live exe and the offline test carry the
# same bytes. Asserted to carry the build string, which `build` and `selftest` also use.
RES = ROOT / "dll" / "tests" / "res"
RC_C = (RES / "b25c_corroborated.rc").read_text(encoding="ascii")
if f'"ProductVersion", "{IS_DEFENSE_BUILD_STRING}"' not in RC_C:
    raise SystemExit("b25: FAILED -- dll/tests/res/b25c_corroborated.rc lost IS Defense's build string")

BARE_STRING = "4.10.3"
RC_D = (RES / "b25d_bare.rc").read_text(encoding="ascii")
if f'"ProductVersion", "{BARE_STRING}"' not in RC_D:
    raise SystemExit("b25: FAILED -- dll/tests/res/b25d_bare.rc lost its bare version string")

# [VER-410-RIG-EF] E and F carry dll_core_test's own fixtures too: E the game-versioned exe with IS Defense's build string
# (`verres_game_buildstring_410`, the exe of dll_core_test's install tree), F a game version and nothing else.
RC_E = (RES / "game_buildstring_410.rc").read_text(encoding="ascii")
if f'"ProductVersion", "{IS_DEFENSE_BUILD_STRING}"' not in RC_E or "PRODUCTVERSION 1,0,0,0" not in RC_E:
    raise SystemExit("b25: FAILED -- dll/tests/res/game_buildstring_410.rc lost its game fixed fields or the build string")
GAME_ONLY_STRING = "1.0.10897.0"
RC_F = (RES / "game_only.rc").read_text(encoding="ascii")
if f'"ProductVersion", "{GAME_ONLY_STRING}"' not in RC_F or "+UE" in RC_F:
    raise SystemExit("b25: FAILED -- dll/tests/res/game_only.rc is no longer a game version alone")

# (exe stem, C source, resource script or None, the fixed version its resource must carry)
BRANCHES = (
    ("b25a_subfloor", "b25a.c", "b25a.rc", (4, 5, 0, 0)),
    ("b25b_ue3", "b25b.c", None, None),
    ("b25c_corroborated", "b25c.c", "b25c.rc", (4, 10, 2, 0)),
    ("b25d_bare", "b25d.c", "b25d.rc", (4, 10, 3, 0)),
)

# [VER-410-RIG-EF] The two shapes a CrashReportClient decides, so each exe is laid out as an install: the exe at
# <root>/B25Game/Binaries/Win64/<stem>.exe and a 4.10.2 CrashReportClient -- a copy of b25c, whose resource is the one
# dll_core_test's install tree puts there -- at <root>/Engine/Binaries/Win64/CrashReportClient.exe, the first place
# Grimoire::CrashReportCandidates looks above that exe. Same columns as BRANCHES.
TREE_BRANCHES = (
    ("b25e_crc_agrees", "b25e.c", "b25e.rc", (1, 0, 0, 0)),
    ("b25f_crc_alone", "b25f.c", "b25f.rc", (1, 0, 10897, 0)),
)
CRC_SOURCE = "b25c_corroborated"   # the BRANCHES exe copied in as each tree's CrashReportClient


def tree_root(name):
    return OUT / f"{name}_root"


def exe_path(name):
    """Where the marker exe runs from: OUT itself for A-D, its install tree for E and F."""
    if any(name == t[0] for t in TREE_BRANCHES):
        return tree_root(name) / "B25Game" / "Binaries" / "Win64" / f"{name}.exe"
    return OUT / f"{name}.exe"


def tree_crc(name):
    return tree_root(name) / "Engine" / "Binaries" / "Win64" / "CrashReportClient.exe"


def crc_max_ancestors():
    """Grimoire::CRC_MAX_ANCESTORS, read from source: never typed here."""
    src = (ROOT / "dll" / "src" / "Grimoire.h").read_text(encoding="utf-8")
    m = re.search(r"constexpr\s+int\s+CRC_MAX_ANCESTORS\s*=\s*(\d+)\s*;", src)
    if not m:
        raise SystemExit("b25: FAILED -- CRC_MAX_ANCESTORS not found in Grimoire.h")
    return int(m.group(1))


def crash_report_candidates(exe):
    """Grimoire::CrashReportCandidates, ported: nearest ancestor first, Win64 before Win32, a drive root ends it."""
    out = []
    p = str(exe).replace("/", "\\")
    if "\\" not in p:
        return out
    d = p.rsplit("\\", 1)[0]
    for _ in range(crc_max_ancestors()):
        if len(d) <= 2:
            break
        out += [d + r"\Engine\Binaries\Win64\CrashReportClient.exe", d + r"\Engine\Binaries\Win32\CrashReportClient.exe"]
        if "\\" not in d:
            break
        d = d.rsplit("\\", 1)[0]
    return out


def crc_found(exe):
    """The CrashReportClient the DLL reads for this exe: the first candidate that exists, or None."""
    return next((c for c in crash_report_candidates(exe) if os.path.isfile(c)), None)


def msvc_env():
    """Environment after vcvars64.bat, captured through cmd (never PowerShell)."""
    if not VCVARS.is_file():
        raise SystemExit(f"b25: FAILED -- vcvars64.bat not found at {VCVARS}")
    # shell=True, NOT a ["cmd","/c",...] list: the list form re-quotes the argument
    # and cmd then sees \"C:\Program Files\...\" as a literal filename, failing with
    # "not recognized as an internal or external command" and an empty stdout.
    r = subprocess.run(f'call "{VCVARS}" >nul 2>&1 && set',
                       shell=True, capture_output=True, text=True, errors="replace")
    if r.returncode != 0:
        raise SystemExit(f"b25: FAILED -- vcvars64 returned {r.returncode}: {r.stderr[:400]}")
    env = {}
    for line in r.stdout.splitlines():
        if "=" in line:
            k, v = line.split("=", 1)
            env[k] = v
    if "INCLUDE" not in env or "LIB" not in env:
        raise SystemExit("b25: FAILED -- vcvars64 ran but INCLUDE/LIB are unset; "
                         "the compile would fail with a confusing C1083 instead")
    return env


def _tool(env, name):
    """Absolute path to an MSVC tool.

    Necessary, not defensive: on Windows CreateProcess resolves a bare program name
    against the PARENT process's PATH, ignoring the PATH inside `env`. Passing
    ["rc", ...] with a vcvars env therefore dies with a bare WinError 2 that looks
    like "MSVC is not installed" rather than "you looked in the wrong PATH".
    """
    import shutil
    p = shutil.which(name, path=env.get("PATH", ""))
    if not p:
        raise SystemExit(f"b25: FAILED -- {name}.exe not on the vcvars PATH")
    return p


def build():
    OUT.mkdir(parents=True, exist_ok=True)
    env = msvc_env()

    (OUT / "b25a.c").write_text(SRC_COMMON % "    if (argc > 99) printf(\"a\\n\");",
                                encoding="ascii")
    (OUT / "b25a.rc").write_text(RC_A, encoding="ascii")
    (OUT / "b25b.c").write_text(
        SRC_B_GLOBALS + SRC_COMMON %
        "    if (argc > 99) printf(\"%s %s\\n\", g_marker0, g_marker1);",
        encoding="ascii")
    (OUT / "b25c.c").write_text(SRC_COMMON % "    if (argc > 99) printf(\"c\\n\");",
                                encoding="ascii")
    (OUT / "b25c.rc").write_text(RC_C, encoding="ascii")
    (OUT / "b25d.c").write_text(SRC_COMMON % "    if (argc > 99) printf(\"d\\n\");",
                                encoding="ascii")
    (OUT / "b25d.rc").write_text(RC_D, encoding="ascii")
    (OUT / "b25e.c").write_text(SRC_COMMON % "    if (argc > 99) printf(\"e\\n\");",
                                encoding="ascii")
    (OUT / "b25e.rc").write_text(RC_E, encoding="ascii")
    (OUT / "b25f.c").write_text(SRC_COMMON % "    if (argc > 99) printf(\"f\\n\");",
                                encoding="ascii")
    (OUT / "b25f.rc").write_text(RC_F, encoding="ascii")

    # A, C, D, E, F: with a version resource. B: deliberately WITHOUT one.
    for _, _, rcfile, _ in BRANCHES + TREE_BRANCHES:
        if not rcfile:
            continue
        res = rcfile[:-3] + ".res"
        rc = subprocess.run([_tool(env, "rc"), "/nologo", "/fo", res, rcfile],
                            cwd=OUT, env=env, capture_output=True, text=True, errors="replace")
        if rc.returncode != 0:
            raise SystemExit(f"b25: FAILED -- rc.exe {rc.returncode} for {rcfile}: "
                             f"{rc.stdout}{rc.stderr}")

    for name, src, rcfile, _ in BRANCHES + TREE_BRANCHES:
        extra = [rcfile[:-3] + ".res"] if rcfile else []
        cl = subprocess.run([_tool(env, "cl"), "/nologo", "/O2", "/W3", src,
                             f"/Fe:{name}.exe", f"/Fo:{name}.obj",
                             "/link", "/SUBSYSTEM:CONSOLE"] + extra,
                            cwd=OUT, env=env, capture_output=True, text=True, errors="replace")
        if cl.returncode != 0:
            raise SystemExit(f"b25: FAILED -- cl.exe {cl.returncode} for {src}: "
                             f"{cl.stdout}{cl.stderr}")
        exe = OUT / f"{name}.exe"
        if not exe.is_file():
            raise SystemExit(f"b25: FAILED -- cl reported success but {exe} is absent")
        print(f"built {exe}  ({exe.stat().st_size:,} bytes)")

    # [VER-410-RIG-EF] E and F move into their install trees, each beside its own CrashReportClient copy. A tree from an
    # earlier build goes first, so no stale exe or CrashReportClient survives into this one.
    import shutil
    for name, _, _, _ in TREE_BRANCHES:
        if tree_root(name).exists():
            shutil.rmtree(tree_root(name))
        exe_path(name).parent.mkdir(parents=True)
        tree_crc(name).parent.mkdir(parents=True)
        os.replace(OUT / f"{name}.exe", exe_path(name))
        shutil.copy2(OUT / f"{CRC_SOURCE}.exe", tree_crc(name))
        print(f"laid out {exe_path(name)}\n      with {tree_crc(name)}")

    _verify_artifacts()


def _fixed_version(data):
    """(file, product) versions from the VS_FIXEDFILEINFO in an exe's bytes, or None."""
    i = data.find(struct.pack("<I", 0xFEEF04BD))
    if i < 0 or i + 24 > len(data):
        return None
    _sig, _struc, fms, fls, pms, pls = struct.unpack_from("<6I", data, i)
    split = lambda ms, ls: (ms >> 16, ms & 0xFFFF, ls >> 16, ls & 0xFFFF)  # noqa: E731
    return split(fms, fls), split(pms, pls)


def _wide(s):
    return s.encode("utf-16-le")


def _verify_artifacts():
    """Assert the exes actually carry what their branches depend on.

    A compile that succeeds but drops the literals would make branch B look like a
    clean PASS of the refusal-does-not-fire kind -- the exact false negative this
    row exists to catch. C and D are told apart by one string, so each is checked to
    carry its own and not the other's, and every resource's FIXED version is read
    back rather than trusted from the .rc.
    """
    exe = {name: (OUT / f"{name}.exe").read_bytes() for name, _, _, _ in BRANCHES}
    a, b, c, d = (exe[n] for n, _, _, _ in BRANCHES)
    has_res = lambda x: (b"VS_VERSION_INFO" in x  # noqa: E731
                         or b"V\x00S\x00_\x00V\x00E\x00R\x00S\x00I\x00O\x00N" in x)
    no_ue3 = lambda x: b"UnrealEngine3" not in x and b"SeqAct_" not in x  # noqa: E731
    checks = [
        ("A carries no UE3 marker", no_ue3(a)),
        ("B carries 'UnrealEngine3'", b"UnrealEngine3" in b),
        ("B carries 'SeqAct_'", b"SeqAct_" in b),
        ("A carries a version resource", has_res(a)),
        ("B carries NO version resource", not has_res(b)),
        ("C and D carry no UE3 marker", no_ue3(c) and no_ue3(d)),
        ("C carries IS Defense's engine build string", _wide(IS_DEFENSE_BUILD_STRING) in c),
        ("D carries the bare string and no '+UE4'",
         _wide(BARE_STRING) in d and _wide("+UE4") not in d),
    ]
    for name, _, rcfile, fixed in BRANCHES:
        if rcfile:
            got = _fixed_version(exe[name])
            checks.append((f"{name} fixed file/product version is {fixed}",
                           got == (fixed, fixed)))
    # [VER-410-RIG-EF] E and F are told apart by their resources alone, and each is judged on the CrashReportClient the
    # DLL finds above it -- so that copy is checked where the ported lookup finds it, and A-D are checked to find none.
    e, f = (exe_path(n).read_bytes() for n, _, _, _ in TREE_BRANCHES)
    checks += [
        ("E and F carry no UE3 marker", no_ue3(e) and no_ue3(f)),
        ("E carries IS Defense's engine build string", _wide(IS_DEFENSE_BUILD_STRING) in e),
        ("F carries its game version string and no '+UE'", _wide(GAME_ONLY_STRING) in f and _wide("+UE") not in f),
    ]
    for name, _, _, fixed in TREE_BRANCHES:
        crc = tree_crc(name)
        checks.append((f"{name} fixed file/product version is {fixed}",
                       _fixed_version(exe_path(name).read_bytes()) == (fixed, fixed)))
        checks.append((f"{name}: the DLL's lookup finds its own CrashReportClient first",
                       crc_found(exe_path(name)) == str(crc).replace("/", "\\")))
        if crc.is_file():
            data = crc.read_bytes()
            checks.append((f"{name}: that CrashReportClient is fixed 4.10.2.0 with IS Defense's build string",
                           _fixed_version(data) == ((4, 10, 2, 0), (4, 10, 2, 0))
                           and _wide(IS_DEFENSE_BUILD_STRING) in data))
    for name, _, _, _ in BRANCHES:
        checks.append((f"{name}: no CrashReportClient above it", crc_found(exe_path(name)) is None))
    bad = [n for n, ok in checks if not ok]
    for n, ok in checks:
        print(f"  {'ok  ' if ok else 'FAIL'} {n}")
    if bad:
        raise SystemExit("b25: FAILED -- artifact checks: " + "; ".join(bad))


# ── judging the scan logs ─────────────────────────────────────────────────────

LOGS = pathlib.Path(os.environ.get("LOCALAPPDATA", "")) / "UE5CEDumper" / "Logs"


def support_floor():
    """Grimoire::MIN_SUPPORTED_UE_VERSION, read from source: never typed here."""
    src = (ROOT / "dll" / "src" / "Grimoire.h").read_text(encoding="utf-8")
    m = re.search(r"constexpr\s+uint32_t\s+MIN_SUPPORTED_UE_VERSION\s*=\s*(\d+)\s*;", src)
    if not m:
        raise SystemExit("b25: FAILED -- MIN_SUPPORTED_UE_VERSION not found in Grimoire.h")
    return int(m.group(1))


def _verdict(text, must, must_not):
    """PASS / FAIL with the reasons, or CACHED when detection never ran this time."""
    if "skipped DetectVersion" in text:
        return "CACHED", ["the hint cache answered, so detection did not run -- "
                          "`build` mints a new PE hash; then re-run"]
    missing = [f"missing: {s!r}" for s in must if s not in text]
    present = [f"present: {s!r}" for s in must_not if s in text]
    return ("FAIL" if missing or present else "PASS"), missing + present


def judge_a(text, floor):
    return _verdict(text,
                    [f"below the {floor} floor", "NOT accepting that on its own",
                     "UE Version = 405 (tier=3"],
                    ["SKIPPING the scan"])


def judge_b(text, floor):
    return _verdict(text,
                    ["PRE-UE4 engine POSITIVELY identified (2/4 markers, 2 needed)",
                     "FindAll: PRE-UE4 engine (Unreal Engine 3)", "SKIPPING the scan"],
                    [])


def judge_c(text, floor):
    return _verdict(text,
                    ["CORROBORATED by the exe's own engine build string",
                     "UE Version = 410 (tier=1",
                     f"older than the minimum supported {floor}", "SKIPPING the scan"],
                    ["NOT accepting that on its own", "[GObjects] Batch"])


def judge_d(text, floor):
    return _verdict(text,
                    [f"below the {floor} floor", "NOT accepting that on its own",
                     "UE Version = 410 (tier=3"],
                    ["SKIPPING the scan", "CORROBORATED"])


# [VER-410-RIG-EF] The CrashReportClient line both trees must show: the copy in the install layout, read as 410.
CRC_IN_LAYOUT = r"\Engine\Binaries\Win64\CrashReportClient.exe' -> 410"


def clauses_e(floor):
    """E: the exe's 4.10 reading comes out of its build string, which cannot corroborate itself; the CrashReportClient
    agreeing with it does, so it is refused before any scan. -> (must, must_not)"""
    return (["is the engine's build string -> 410", CRC_IN_LAYOUT,
             "CrashReportClient and the game exe AGREE on 410",
             "CORROBORATED by a CrashReportClient agreeing with the exe",
             "UE Version = 410 (tier=1",
             f"older than the minimum supported {floor}", "SKIPPING the scan"],
            ["CORROBORATED by the exe's own engine build string", "NOT accepting that on its own",
             "SOURCES DISAGREE", "[GObjects] Batch"])


def clauses_f(floor):
    """F: the exe reads nothing, so the CrashReportClient's 410 stands alone -- said as the CrashReportClient's, never
    put in the exe's mouth -- and the scan runs. -> (must, must_not)"""
    return ([CRC_IN_LAYOUT,
             f"CrashReportClient says UE 410, below the {floor} floor, and the game exe's own PE VERSIONINFO "
             "read nothing usable", "NOT accepting that on its own",
             "PE resource failed",
             f"the game exe's resource read nothing usable; CrashReportClient says UE 410, below the {floor} "
             "floor", "UE Version = 410 (tier=3"],
            ["SKIPPING the scan", "CORROBORATED", "AGREE on", "PE VERSIONINFO says UE 410"])


def judge_e(text, floor):
    return _verdict(text, *clauses_e(floor))


def judge_f(text, floor):
    return _verdict(text, *clauses_f(floor))


JUDGES = {"b25a_subfloor": judge_a, "b25b_ue3": judge_b,
          "b25c_corroborated": judge_c, "b25d_bare": judge_d,
          "b25e_crc_agrees": judge_e, "b25f_crc_alone": judge_f}


def check():
    """0 only when every branch was judged and PASSed; 1 on any FAIL; 2 when none failed but a
    branch was not judged -- a check that judged nothing is not a pass."""
    floor = support_floor()
    print(f"support floor (read from Grimoire.h): {floor}")
    verdicts = []
    for name, _, _, _ in BRANCHES + TREE_BRANCHES:
        log = LOGS / name / "scan-0.log"
        exe = exe_path(name)
        if not log.is_file():
            print(f"  NOT RUN  {name}: no {log}")
            verdicts.append("NOT RUN")
            continue
        if exe.is_file() and log.stat().st_mtime < exe.stat().st_mtime:
            print(f"  STALE    {name}: {log} predates this build of the exe")
            verdicts.append("STALE")
            continue
        text = log.read_text(encoding="utf-8", errors="replace")
        head = next((l.strip() for l in text.splitlines() if "Logger started" in l), "?")
        verdict, why = JUDGES[name](text, floor)
        verdicts.append(verdict)
        print(f"  {verdict:<8} {name}: {len(text.splitlines()):,} log lines; {head[-90:]}")
        for w in why:
            print(f"           {w}")
    if "FAIL" in verdicts:
        return 1
    return 0 if all(v == "PASS" for v in verdicts) else 2


def selftest():
    """Every judge against a canned log of each outcome, so a judge that cannot fail is caught."""
    floor = support_floor()
    stamp = "[2026-10-08 22:11:21.669] [WARN] [SCAN:Ver] "
    below = (f"{stamp}DetectVersion: PE VERSIONINFO says UE %d, below the {floor} floor — NOT "
             "accepting that on its own (it would refuse the whole scan).\n")
    a_ok = below % 405 + "FindAll: UE Version = 405 (tier=3, detected=yes)\n[GObjects] Batch 1/7\n"
    b_ok = ("DetectVersion: PRE-UE4 engine POSITIVELY identified (2/4 markers, 2 needed)\n"
            "FindAll: PRE-UE4 engine (Unreal Engine 3) — SKIPPING the scan.\n")
    c_ok = (f"{stamp}DetectVersion: PE VERSIONINFO says UE 410, below the {floor} floor, "
            f"CORROBORATED by the exe's own engine build string '{IS_DEFENSE_BUILD_STRING}'\n"
            "FindAll: UE Version = 410 (tier=1, detected=yes, lowConfidence=no)\n"
            f"FindAll: UE 410 is older than the minimum supported {floor} — SKIPPING the scan.\n")
    d_ok = below % 410 + "FindAll: UE Version = 410 (tier=3, detected=yes)\n[GObjects] Batch 1/7\n"
    rev7 = below % 410 + "FindAll: UE Version = 410 (tier=3, detected=yes)\n"  # IS Defense's rev-7 run
    cached = "FindAll: UE Version = 410 (cached, rev=8) — skipped DetectVersion\n"
    # [VER-410-RIG-EF] E and F, in the DLL's own wording (Genau's resource half).
    crc = (f"{stamp}DetectVersion: CrashReportClient at 'D:\\rig\\out\\b25\\b25x_root\\Engine\\Binaries\\Win64\\"
           "CrashReportClient.exe' -> 410\n")
    e_read = (f"{stamp}DetectVersion: VERSIONINFO string 'ProductVersion' = '{IS_DEFENSE_BUILD_STRING}' is the "
              "engine's build string -> 410\n")
    e_ok = (e_read + crc + "DetectVersion: CrashReportClient and the game exe AGREE on 410\n"
            f"DetectVersion: PE VERSIONINFO says UE 410, below the {floor} floor, CORROBORATED by a CrashReportClient "
            "agreeing with the exe — accepting it at tier 1, so the scan will be refused as too old.\n"
            "FindAll: UE Version = 410 (tier=1, detected=yes, lowConfidence=no)\n"
            f"FindAll: UE 410 is older than the minimum supported {floor} — SKIPPING the scan.\n")
    f_ok = (crc + f"{stamp}DetectVersion: CrashReportClient says UE 410, below the {floor} floor, and the game exe's own "
            "PE VERSIONINFO read nothing usable — NOT accepting that on its own (it would refuse the whole scan). "
            "Corroborating against the memory string scan.\n"
            "DetectVersion: PE resource failed, falling back to memory string scan\n"
            "DetectVersion: (the game exe's resource read nothing usable; CrashReportClient says UE 410, below the "
            f"{floor} floor, which alone does not corroborate a reading below it — the memory scan decides)\n"
            "FindAll: UE Version = 410 (tier=3, detected=yes)\n[GObjects] Batch 1/7\n")
    # E before rev 9: the version-first string read nothing, so E's log was F's.
    e_rev8 = f_ok
    # F before the review: the CrashReportClient's 410 logged as the exe's own reading.
    f_mouth = f_ok.replace("CrashReportClient says UE 410, below the "
                           f"{floor} floor, and the game exe's own PE VERSIONINFO read nothing usable",
                           f"PE VERSIONINFO says UE 410, below the {floor} floor")
    cases = [
        ("A passes its own log", judge_a, a_ok, "PASS"),
        ("A fails on a refusal", judge_a, a_ok + "SKIPPING the scan\n", "FAIL"),
        ("B passes its own log", judge_b, b_ok, "PASS"),
        ("B fails when nothing is refused", judge_b, a_ok, "FAIL"),
        ("C passes its own log", judge_c, c_ok, "PASS"),
        ("C fails on IS Defense's rev-7 run (scanned, not refused)", judge_c, rev7, "FAIL"),
        ("C fails if a GObjects batch ran", judge_c, c_ok + "[GObjects] Batch 1/7\n", "FAIL"),
        ("C fails on D's log", judge_c, d_ok, "FAIL"),
        ("D passes its own log", judge_d, d_ok, "PASS"),
        ("D fails on C's log (a bare string must not refuse)", judge_d, c_ok, "FAIL"),
        ("D fails when corroborated but still scanned", judge_d, d_ok + "CORROBORATED\n", "FAIL"),
        ("a cached run is CACHED, never PASS", judge_d, cached + d_ok, "CACHED"),
        ("an empty log fails every judge", judge_c, "", "FAIL"),
        ("E passes its own log", judge_e, e_ok, "PASS"),
        ("E fails on its rev-8 run (the string read nothing, scanned)", judge_e, e_rev8, "FAIL"),
        ("E fails when the exe's own build string corroborated itself", judge_e,
         e_ok.replace("by a CrashReportClient agreeing with the exe",
                      f"by the exe's own engine build string '{IS_DEFENSE_BUILD_STRING}'"), "FAIL"),
        # ...and with the CrashReportClient's line still there, only the must-not can see it.
        ("E fails when the exe's own build string corroborated it as well", judge_e,
         e_ok + f"{stamp}DetectVersion: CORROBORATED by the exe's own engine build string "
         f"'{IS_DEFENSE_BUILD_STRING}'\n", "FAIL"),
        ("E fails if a GObjects batch ran", judge_e, e_ok + "[GObjects] Batch 1/7\n", "FAIL"),
        ("E fails if a NOT-accepting line came too", judge_e, e_ok + f"{stamp}DetectVersion: PE VERSIONINFO says UE "
         f"410, below the {floor} floor — NOT accepting that on its own (it would refuse the whole scan).\n", "FAIL"),
        ("E fails if the exe and the CrashReportClient disagreed", judge_e, e_ok + f"{stamp}DetectVersion: SOURCES "
         "DISAGREE — CrashReportClient says 410, the game exe's own VERSIONINFO says 427.\n", "FAIL"),
        ("E fails without the CrashReportClient its install layout puts above it", judge_e,
         e_ok.replace(crc, ""), "FAIL"),
        ("E fails on C's log (the exe corroborating itself, no CrashReportClient)", judge_e, c_ok, "FAIL"),
        ("F passes its own log", judge_f, f_ok, "PASS"),
        ("F fails on E's log (a CrashReportClient alone must not refuse)", judge_f, e_ok, "FAIL"),
        ("F fails when the CrashReportClient's 410 is put in the exe's mouth", judge_f, f_mouth, "FAIL"),
        ("F fails when the exe's mouth line comes beside the right one", judge_f,
         f_ok + f"{stamp}DetectVersion: PE VERSIONINFO says UE 410, below the {floor} floor — NOT accepting that on its "
         "own (it would refuse the whole scan).\n", "FAIL"),
        ("F fails on a refusal", judge_f, f_ok + f"FindAll: UE 410 is older than the minimum supported {floor} — "
         "SKIPPING the scan.\n", "FAIL"),
        ("F fails on D's log (the exe's own reading, no CrashReportClient)", judge_f, d_ok, "FAIL"),
        ("F fails if anything was CORROBORATED", judge_f,
         f_ok + "DetectVersion: CORROBORATED by a CrashReportClient agreeing with the exe\n", "FAIL"),
        ("F fails if the exe agreed with the CrashReportClient", judge_f,
         f_ok + "DetectVersion: CrashReportClient and the game exe AGREE on 410\n", "FAIL"),
        ("a cached F is CACHED, never PASS", judge_f, cached + f_ok, "CACHED"),
        ("an empty log fails E", judge_e, "", "FAIL"),
        ("an empty log fails F", judge_f, "", "FAIL"),
    ]
    # [VER-410-RIG-EF] Every clause of E's and F's judges carries weight: the PASS log with the lines holding one `must`
    # taken out fails, and so does the PASS log with one `must_not` added.
    for tag, clauses, judge, ok_log in (("E", clauses_e, judge_e, e_ok), ("F", clauses_f, judge_f, f_ok)):
        must, must_not = clauses(floor)
        for s in must:
            cut = "".join(l for l in ok_log.splitlines(True) if s not in l)
            cases.append((f"{tag} fails without {s[:60]!r}", judge, cut, "FAIL"))
        for s in must_not:
            cases.append((f"{tag} fails with {s[:60]!r} added", judge, ok_log + s + "\n", "FAIL"))
    bad = 0
    for label, judge, text, want in cases:
        got, why = judge(text, floor)
        ok = got == want
        bad += not ok
        print(f"  {'ok  ' if ok else 'FAIL'} {label}: {got}" + ("" if ok else f" (want {want}) {why}"))
    # [VER-410-RIG-EF] The CrashReportClient lookup's port, against Test_CrashReportCandidates' own cases: `build`
    # checks each tree's copy is the first one it finds, which is only as good as the port.
    p3r = r"D:\SteamLibrary\steamapps\common\P3R\P3R\Binaries\Win64\P3R.exe"
    c = crash_report_candidates(p3r)
    lookups = [
        ("the real P3R path is produced",
         r"D:\SteamLibrary\steamapps\common\P3R\Engine\Binaries\Win64\CrashReportClient.exe" in c),
        ("nearest ancestor first -- the exe's own dir leads",
         bool(c) and c[0].startswith(r"D:\SteamLibrary\steamapps\common\P3R\P3R\Binaries\Win64\Engine")),
        ("Win64 before Win32 at each level", len(c) % 2 == 0 and all(
            c[i].endswith(r"\Win64\CrashReportClient.exe") and c[i + 1].endswith(r"\Win32\CrashReportClient.exe")
            for i in range(0, len(c), 2))),
        ("at most CRC_MAX_ANCESTORS levels are probed", len(c) <= 2 * crc_max_ancestors()),
        ("forward slashes are a separator too", bool(crash_report_candidates("D:/Games/X/Binaries/Win64/X.exe"))),
        ("a bare filename yields nothing", crash_report_candidates("Game.exe") == []),
        ("a drive root stops the walk", crash_report_candidates(r"D:\Game.exe") == []),
        ("E's layout: its CrashReportClient is three levels above the exe's folder, the fourth Win64 candidate",
         crash_report_candidates(exe_path("b25e_crc_agrees"))[6] == str(tree_crc("b25e_crc_agrees"))),
    ]
    for label, ok in lookups:
        bad += not ok
        print(f"  {'ok  ' if ok else 'FAIL'} lookup: {label}")
    total = len(cases) + len(lookups)
    print(f"selftest: {total - bad}/{total} passed")
    return 1 if bad else 0


def clean():
    import shutil
    if OUT.exists():
        shutil.rmtree(OUT)
        print(f"removed {OUT}")
    else:
        print(f"nothing to remove at {OUT}")


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else "build"
    if cmd == "build":
        build()
    elif cmd == "check":
        sys.exit(check())
    elif cmd == "selftest":
        sys.exit(selftest())
    elif cmd == "clean":
        clean()
    else:
        raise SystemExit(__doc__)
