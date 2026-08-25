"""Build the two synthetic marker exes B25 needs. No game, no PowerShell.

    py b25_marker_exes.py build      # compile both into out/b25/
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

    # A: with the version resource. B: deliberately WITHOUT one.
    rc = subprocess.run([_tool(env, "rc"), "/nologo", "/fo", "b25a.res", "b25a.rc"],
                        cwd=OUT, env=env, capture_output=True, text=True, errors="replace")
    if rc.returncode != 0:
        raise SystemExit(f"b25: FAILED -- rc.exe {rc.returncode}: {rc.stdout}{rc.stderr}")

    for name, extra in (("b25a_subfloor", ["b25a.res"]), ("b25b_ue3", [])):
        src = "b25a.c" if name.startswith("b25a") else "b25b.c"
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


def _verify_artifacts():
    """Assert the exes actually carry what the two branches depend on.

    A compile that succeeds but drops the literals would make branch B look like a
    clean PASS of the refusal-does-not-fire kind -- the exact false negative this
    row exists to catch.
    """
    a = (OUT / "b25a_subfloor.exe").read_bytes()
    b = (OUT / "b25b_ue3.exe").read_bytes()
    checks = [
        ("A carries no UE3 marker", b"UnrealEngine3" not in a and b"SeqAct_" not in a),
        ("B carries 'UnrealEngine3'", b"UnrealEngine3" in b),
        ("B carries 'SeqAct_'", b"SeqAct_" in b),
        ("A carries a version resource", b"VS_VERSION_INFO" in a
         or b"V\x00S\x00_\x00V\x00E\x00R\x00S\x00I\x00O\x00N" in a),
        ("B carries NO version resource", b"VS_VERSION_INFO" not in b
         and b"V\x00S\x00_\x00V\x00E\x00R\x00S\x00I\x00O\x00N" not in b),
    ]
    bad = [n for n, ok in checks if not ok]
    for n, ok in checks:
        print(f"  {'ok  ' if ok else 'FAIL'} {n}")
    if bad:
        raise SystemExit("b25: FAILED -- artifact checks: " + "; ".join(bad))


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
    elif cmd == "clean":
        clean()
    else:
        raise SystemExit(__doc__)
