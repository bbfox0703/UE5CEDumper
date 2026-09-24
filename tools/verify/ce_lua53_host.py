"""Run the Lua test suites on Cheat Engine's OWN Lua VM, not a stock interpreter.

    py tools/verify/ce_lua53_host.py                      # build the host (if needed) and run scripts/tests/*.lua
    py tools/verify/ce_lua53_host.py --probe              # print the VM's version/behaviour probe only
    py tools/verify/ce_lua53_host.py --ce-dir "C:/Program Files/Cheat Engine" --ce-src "D:/Github/cheat-engine"

WHY THIS EXISTS. scripts/tests/*.lua run under whatever `lua` is on the machine -- here a stock 5.4.6 -- while the
scripts they test run inside CE's Lua 5.3 (lua53-64.dll, built with LUA_COMPAT_5_2). The two differ in ways that can
make a test PASS on 5.4 and the script FAIL in CE: 5.4-only syntax (<const>, <close>), 5.4-only functions (warn,
coroutine.close), and behaviours that changed (the integer for-loop, the math.random generator). The reverse
direction -- 5.3-with-compat functions 5.4 dropped (bit32, math.pow, unpack, loadstring) -- fails LOUDLY on 5.4, so
it cannot hide a defect. Running the same suites on CE's VM removes the question instead of arguing it.

HOW. It builds a tiny host -- the stock lua.c from CE's own Lua source tree -- and links it against an import
library generated from the INSTALLED lua53-64.dll's export table (dumpbin), then copies that DLL beside it. So the
interpreter that runs the tests is the exact binary CE loads; only the ~600-line REPL/driver around it is compiled
here, and it uses the public API only. MSVC comes from vcvars64.bat, the same way build_dll.py gets it: no
PowerShell. Output goes to out/ce_lua53/ (gitignored scratch).

What it does NOT cover: the CE API itself (readInteger, createTimer, the mailbox the DLL answers). The suites stub
those; only a live run inside CE exercises them.
"""
import argparse
import pathlib
import re
import shutil
import subprocess
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from build_dll import find_vcvars  # noqa: E402  (the same toolchain lookup build.ps1 uses)

REPO = pathlib.Path(__file__).resolve().parents[2]
OUT = REPO / "out" / "ce_lua53"


def vc(cmd, cwd):
    """Run one command inside the MSVC environment; return (rc, output)."""
    full = f'call "{find_vcvars()}" >nul && {cmd}'
    r = subprocess.run(["cmd", "/d", "/s", "/c", full], cwd=cwd, capture_output=True, text=True,
                       encoding="utf-8", errors="replace")
    return r.returncode, (r.stdout or "") + (r.stderr or "")


def build(ce_dir: pathlib.Path, ce_src: pathlib.Path) -> pathlib.Path:
    dll = ce_dir / "lua53-64.dll"
    src = ce_src / "Cheat Engine" / "lua53" / "lua53" / "src"
    for p in (dll, src / "lua.c", src / "lua.h"):
        if not p.exists():
            raise SystemExit(f"ce_lua53_host: missing {p}")
    OUT.mkdir(parents=True, exist_ok=True)
    exe = OUT / "lua53ce.exe"
    local_dll = OUT / "lua53-64.dll"
    if exe.exists() and local_dll.exists() and local_dll.read_bytes() == dll.read_bytes():
        return exe
    shutil.copy2(dll, local_dll)

    # The import library comes from the DLL CE ships, not from the source tree's .def: they can differ.
    rc, out = vc(f'dumpbin /nologo /exports "{local_dll}"', OUT)
    if rc != 0:
        raise SystemExit("ce_lua53_host: dumpbin failed\n" + out)
    names = re.findall(r"^\s+\d+\s+[0-9A-F]+\s+[0-9A-F]{8}\s+(\S+)", out, re.M)
    if not any(n == "lua_pcallk" for n in names):
        raise SystemExit(f"ce_lua53_host: {len(names)} exports parsed, lua_pcallk not among them -- not a Lua 5.3 DLL?")
    (OUT / "lua53-64.def").write_text("LIBRARY lua53-64.dll\nEXPORTS\n" + "".join(f"  {n}\n" for n in names))
    rc, out = vc("lib /nologo /machine:x64 /def:lua53-64.def /out:lua53-64.lib", OUT)
    if rc != 0:
        raise SystemExit("ce_lua53_host: lib failed\n" + out)
    rc, out = vc(f'cl /nologo /O2 /W0 /DLUA_BUILD_AS_DLL /I"{src}" "{src / "lua.c"}" /Fe:lua53ce.exe '
                 f'/link lua53-64.lib', OUT)
    if rc != 0 or not exe.exists():
        raise SystemExit("ce_lua53_host: cl failed\n" + out)
    return exe


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
    ap.add_argument("--ce-dir", default=r"C:\Program Files\Cheat Engine")
    ap.add_argument("--ce-src", default=r"D:\Github\cheat-engine")
    ap.add_argument("--probe", action="store_true", help="only print the VM probe")
    ap.add_argument("files", nargs="*", help="test files (default: scripts/tests/*.lua)")
    a = ap.parse_args()
    exe = build(pathlib.Path(a.ce_dir), pathlib.Path(a.ce_src))
    probe = OUT / "probe.lua"
    probe.write_text(PROBE)
    print(f"host: {exe}  (VM = {OUT / 'lua53-64.dll'}, copied from {a.ce_dir})")
    print(subprocess.run([str(exe), str(probe)], capture_output=True, text=True).stdout.rstrip())
    if a.probe:
        return 0
    files = [pathlib.Path(f) for f in a.files] or sorted((REPO / "scripts" / "tests").glob("*.lua"))
    fails = run(exe, files)
    print(f"ce_lua53_host: {len(files) - fails}/{len(files)} suites passed on CE's Lua VM")
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
