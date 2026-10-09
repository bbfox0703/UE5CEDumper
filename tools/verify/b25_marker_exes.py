"""Build the synthetic marker exes B25 and [VER-410-GATE] need, and judge their scan logs.
No game, no PowerShell.

    py b25_marker_exes.py build      # compile all four into out/b25/
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

RUNNING THEM is a live step this script does not take: start an exe, inject the DLL
(tools/verify/inject.py, or CE + the .CT's init), and trigger the scan -- injection
alone starts the pipe and scans nothing. Then `check`. The floor is read from
Grimoire.h, never typed. ⚠ A and D are SAVED to the hint cache after their scan, so a
second run of the same exe skips detection; `check` says so, and `build` mints a new PE
hash. B and C are refused before the save, so they re-detect on every run.

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

# (exe stem, C source, resource script or None, the fixed version its resource must carry)
BRANCHES = (
    ("b25a_subfloor", "b25a.c", "b25a.rc", (4, 5, 0, 0)),
    ("b25b_ue3", "b25b.c", None, None),
    ("b25c_corroborated", "b25c.c", "b25c.rc", (4, 10, 2, 0)),
    ("b25d_bare", "b25d.c", "b25d.rc", (4, 10, 3, 0)),
)


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

    # A, C, D: with a version resource. B: deliberately WITHOUT one.
    for _, _, rcfile, _ in BRANCHES:
        if not rcfile:
            continue
        res = rcfile[:-3] + ".res"
        rc = subprocess.run([_tool(env, "rc"), "/nologo", "/fo", res, rcfile],
                            cwd=OUT, env=env, capture_output=True, text=True, errors="replace")
        if rc.returncode != 0:
            raise SystemExit(f"b25: FAILED -- rc.exe {rc.returncode} for {rcfile}: "
                             f"{rc.stdout}{rc.stderr}")

    for name, src, rcfile, _ in BRANCHES:
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


JUDGES = {"b25a_subfloor": judge_a, "b25b_ue3": judge_b,
          "b25c_corroborated": judge_c, "b25d_bare": judge_d}


def check():
    """0 only when every branch was judged and PASSed; 1 on any FAIL; 2 when none failed but a
    branch was not judged -- a check that judged nothing is not a pass."""
    floor = support_floor()
    print(f"support floor (read from Grimoire.h): {floor}")
    verdicts = []
    for name, _, _, _ in BRANCHES:
        log = LOGS / name / "scan-0.log"
        exe = OUT / f"{name}.exe"
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
    ]
    bad = 0
    for label, judge, text, want in cases:
        got, why = judge(text, floor)
        ok = got == want
        bad += not ok
        print(f"  {'ok  ' if ok else 'FAIL'} {label}: {got}" + ("" if ok else f" (want {want}) {why}"))
    print(f"selftest: {len(cases) - bad}/{len(cases)} passed")
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
