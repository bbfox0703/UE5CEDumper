"""Run the Lua test suites on Cheat Engine's OWN Lua VM, not a stock interpreter.

    py tools/verify/ce_lua53_host.py                      # build the host (if needed) and run scripts/tests/*.lua
    py tools/verify/ce_lua53_host.py --build-only         # build it if it is missing or stale; what build.ps1 calls
    py tools/verify/ce_lua53_host.py --probe              # print the VM's version/behaviour probe only
    py tools/verify/ce_lua53_host.py --ce-dir "E:/Tools/Cheat Engine"

WHY THIS EXISTS. scripts/tests/*.lua run under whatever `lua` is on the machine -- here a stock 5.4.6 -- while the
scripts they test run inside CE's Lua 5.3 (lua53-64.dll). The two differ in ways that can make a test PASS on 5.4
and the script FAIL in CE: 5.4-only syntax (<const>, <close>), 5.4-only functions (warn, coroutine.close), and
behaviours that changed. --probe measured one on 2026-09-25: string arithmetic yields a FLOAT on CE's VM ("10"+1 is
11.0, on 5.4 it is 11). The shipped DLL has NO 5.1/5.2 compat functions (bit32, math.pow, unpack, loadstring are
nil), although the source tree's Makefile says LUA_COMPAT_5_2. Running the same suites on CE's VM removes the
question instead of arguing it.

HOW. It compiles ce_lua53_host.c -- a small driver of our own that declares the Lua C API functions it uses -- and
links it against an import library generated from the INSTALLED lua53-64.dll's export table (dumpbin), then copies
that DLL beside it. So the interpreter that runs the tests is the exact binary CE loads. It needs a Cheat Engine
INSTALL and MSVC, and nothing else: until 2026-10-01 it compiled lua.c from a clone of CE's source tree, which a
second machine with CE installed did not have, and there the C# tests that use the host skipped.
MSVC comes from vcvars64.bat, the same way build_dll.py gets it: no PowerShell. Output goes to out/ce_lua53/
(gitignored scratch).

WHERE CHEAT ENGINE IS. --ce-dir, else the UE5CEDUMPER_CE_DIR environment variable: an explicit folder, so one that
does not hold lua53-64.dll is an ERROR, never a reason to quietly use another install. With neither given: the
InstallLocation its installer records (the Inno Setup uninstall key `Cheat Engine_is1`), else the installer's
default folder.

WHEN IT REBUILDS. The host is rebuilt when it is missing, when the installed DLL's bytes differ from the copy beside
it (a CE update), or when ce_lua53_host.c changed. A stamp file records the two hashes the last build was made from.
A rebuild deletes the old exe FIRST: the tests and the gate only ask whether the exe exists, so a build that died
half-way must leave no host rather than last month's.

--build-only EXIT CODES, for build.ps1: 0 the host is ready (built now or already current), 3 no Cheat Engine
install was found (nothing to build: the tests that need the host SKIP), 1 the build failed.

What it does NOT cover: the CE API itself (readInteger, createTimer, the mailbox the DLL answers). The suites stub
those; only a live run inside CE exercises them.
"""
import argparse
import hashlib
import os
import pathlib
import re
import shutil
import subprocess
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from build_dll import find_vcvars  # noqa: E402  (the same toolchain lookup build.ps1 uses)

# A status line names folders, and a folder can hold a character the console's code page cannot print. Without this
# a successful build died on its own success message when its output was captured, and exited 1.
sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.stderr.reconfigure(encoding="utf-8", errors="replace")

REPO = pathlib.Path(__file__).resolve().parents[2]
OUT = REPO / "out" / "ce_lua53"
HOST_C = pathlib.Path(__file__).resolve().with_suffix(".c")
DLL_NAME = "lua53-64.dll"
DEFAULT_CE_DIR = r"C:\Program Files\Cheat Engine"
NO_CE = 3


def find_ce_dir(explicit=None):
    """The folder that holds lua53-64.dll, or None. See WHERE CHEAT ENGINE IS."""
    for label, value in (("--ce-dir", explicit), ("UE5CEDUMPER_CE_DIR", os.environ.get("UE5CEDUMPER_CE_DIR"))):
        if value:
            d = pathlib.Path(value.strip().strip('"'))
            if not (d / DLL_NAME).is_file():
                raise SystemExit(f"ce_lua53_host: {label} names {d}, which holds no {DLL_NAME}")
            return d
    cands = []
    try:
        import winreg
        for hive in (winreg.HKEY_LOCAL_MACHINE, winreg.HKEY_CURRENT_USER):
            for view in (winreg.KEY_WOW64_64KEY, winreg.KEY_WOW64_32KEY):
                try:
                    with winreg.OpenKey(hive, r"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"
                                              r"\Cheat Engine_is1", 0, winreg.KEY_READ | view) as k:
                        cands.append(winreg.QueryValueEx(k, "InstallLocation")[0])
                except OSError:
                    pass
    except ImportError:                                   # not Windows: only an explicit folder can work
        pass
    cands.append(DEFAULT_CE_DIR)
    for c in cands:
        if c and (pathlib.Path(c) / DLL_NAME).is_file():
            return pathlib.Path(c)
    return None


def vc(cmd, cwd):
    """Run one command inside the MSVC environment; return (rc, output)."""
    # shell=True, as build_dll.py does: the ["cmd", "/c", ...] list form re-quotes the vcvars path and cmd dies.
    # The commands name their files RELATIVE to cwd, so no repo path goes through cmd (which would expand a
    # %NAME% in it). That only works if vcvars leaves the directory alone, and it changes to VSCMD_START_DIR
    # when a user has set that variable: drop it for this child.
    env = {k: v for k, v in os.environ.items() if k.upper() != "VSCMD_START_DIR"}
    r = subprocess.run(f'chcp 65001 >nul && call "{find_vcvars()}" >nul 2>&1 && {cmd}', shell=True, cwd=cwd,
                       env=env, capture_output=True, text=True, encoding="utf-8", errors="replace")
    return r.returncode, (r.stdout or "") + (r.stderr or "")


def needed_exports():
    """The API functions the host declares, read from its own prototypes so the two cannot drift."""
    names = re.findall(r"^API\s[^;(]*?\b(\w+)\s*\(", HOST_C.read_text(encoding="utf-8"), re.M)
    if len(names) < 10:
        raise SystemExit(f"ce_lua53_host: only {len(names)} API prototypes parsed out of {HOST_C.name}")
    return names


def build(ce_dir: pathlib.Path):
    """-> (exe, built): the host, and whether this call compiled it."""
    dll = ce_dir / DLL_NAME
    if not HOST_C.is_file():
        raise SystemExit(f"ce_lua53_host: missing {HOST_C}")
    OUT.mkdir(parents=True, exist_ok=True)
    exe, local_dll, stamp = OUT / "lua53ce.exe", OUT / DLL_NAME, OUT / "host.stamp"
    want = "dll %s\nsrc %s\n" % (hashlib.sha256(dll.read_bytes()).hexdigest(),
                                hashlib.sha256(HOST_C.read_bytes()).hexdigest())
    if exe.exists() and local_dll.exists() and stamp.exists() and stamp.read_text() == want \
            and local_dll.read_bytes() == dll.read_bytes():
        return exe, False
    stamp.unlink(missing_ok=True)                        # a build that dies half-way must not look current,
    exe.unlink(missing_ok=True)                          # ...and must not leave the old host for the tests to run
    shutil.copy2(dll, local_dll)
    shutil.copy2(HOST_C, OUT / HOST_C.name)

    # The import library comes from the DLL CE ships, not from any source tree's .def: they can differ.
    rc, out = vc(f"dumpbin /nologo /exports {DLL_NAME}", OUT)
    if rc != 0:
        raise SystemExit("ce_lua53_host: dumpbin failed\n" + out)
    names = re.findall(r"^\s+\d+\s+[0-9A-F]+\s+[0-9A-F]{8}\s+(\S+)", out, re.M)
    missing = [n for n in needed_exports() if n not in names]
    if missing:
        raise SystemExit(f"ce_lua53_host: {dll} exports {len(names)} names but not {', '.join(missing)} -- "
                         "not the Lua 5.3 DLL this host is written for")
    (OUT / "lua53-64.def").write_text("LIBRARY lua53-64.dll\nEXPORTS\n" + "".join(f"  {n}\n" for n in names))
    rc, out = vc("lib /nologo /machine:x64 /def:lua53-64.def /out:lua53-64.lib", OUT)
    if rc != 0:
        raise SystemExit("ce_lua53_host: lib failed\n" + out)
    rc, out = vc(f"cl /nologo /O2 /W3 /WX {HOST_C.name} /Fe:lua53ce.exe /link lua53-64.lib", OUT)
    if rc != 0 or not exe.exists():
        raise SystemExit("ce_lua53_host: cl failed\n" + out)
    stamp.write_text(want)
    return exe, True


PROBE = r"""
print('_VERSION', _VERSION)
print('math.type(1)', math.type and math.type(1))
print('tostring(3.0)', tostring(3.0))
print('bit32', type(bit32), 'math.pow', type(math.pow), 'unpack', type(unpack), 'loadstring', type(loadstring))
print('warn', type(warn), 'coroutine.close', type(coroutine and coroutine.close))
print('"10"+1', "10"+1, '3//2', 3//2, '7/2', 7/2)
"""


def run(exe: pathlib.Path, files):
    fails = 0
    for f in files:
        r = subprocess.run([str(exe), str(f)], cwd=REPO, capture_output=True, text=True, encoding="utf-8",
                           errors="replace")
        tail = [ln for ln in (r.stdout + r.stderr).splitlines() if ln.strip()][-1:] or ["(no output)"]
        status = "ok  " if r.returncode == 0 else "FAIL"
        fails += r.returncode != 0
        print(f"  {status} {f.name:34} rc={r.returncode}  {tail[0][:110]}")
    return fails


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--ce-dir", default=None, help="the Cheat Engine install folder (default: found, see the header)")
    ap.add_argument("--build-only", action="store_true", help="build the host if needed, run nothing")
    ap.add_argument("--probe", action="store_true", help="only print the VM probe")
    ap.add_argument("files", nargs="*", help="test files (default: scripts/tests/*.lua)")
    a = ap.parse_args()
    try:
        ce_dir = find_ce_dir(a.ce_dir)
        if ce_dir is None:
            print(f"ce_lua53_host: no Cheat Engine install found ({DLL_NAME} is not in the installer's registry "
                  f"entry or in {DEFAULT_CE_DIR}; --ce-dir or UE5CEDUMPER_CE_DIR names another folder)")
            return NO_CE
        exe, built = build(ce_dir)
    except (SystemExit, OSError) as e:
        if not a.build_only:
            raise
        # build.ps1 reads the exit code and shows this text. On STDOUT: Windows PowerShell turns a native
        # command's stderr into an error record, which under ErrorActionPreference Stop ends the build script
        # at the call instead of letting it report the failure and go on to the tests.
        print(e if isinstance(e, SystemExit) else f"ce_lua53_host: {type(e).__name__}: {e}")
        return 1
    if a.build_only:
        print(f"ce_lua53_host: {'built' if built else 'up to date'}: {exe}  (VM = {DLL_NAME} from {ce_dir})")
        return 0
    probe = OUT / "probe.lua"
    probe.write_text(PROBE)
    print(f"host: {exe}  (VM = {OUT / DLL_NAME}, copied from {ce_dir})")
    print(subprocess.run([str(exe), str(probe)], capture_output=True, text=True).stdout.rstrip())
    if a.probe:
        return 0
    files = [pathlib.Path(f) for f in a.files] or sorted((REPO / "scripts" / "tests").glob("*.lua"))
    fails = run(exe, files)
    print(f"ce_lua53_host: {len(files) - fails}/{len(files)} suites passed on CE's Lua VM")
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
