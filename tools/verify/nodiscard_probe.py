r"""Prove that a discarded [[nodiscard]] status fails the DLL build, with the tree's own flags.

    py tools/verify/nodiscard_probe.py

[CPP-HARDENING-2026-11-14] The DLL marks its status returns [[nodiscard]] (Macht::WriteBytes and the rest), and
dll/CMakeLists.txt turns the three related warnings into errors:

  C4834  a discarded [[nodiscard]] value;
  C4858  the same for a [[nodiscard("reason")]] value (recorded below, whichever number MSVC uses);
  C5240  an attribute in a position MSVC ignores -- `static [[nodiscard]] bool f()` compiles to NOTHING, so a
         misplaced attribute would silently protect nothing. It is level 4 and off by default.

Two things are checked, because each can fail on its own:

1. FLAGS. Every target that compiles DLL sources carries /we4834 /we4858 /we5240 in ninja's real compile commands
   (`ninja -t commands`). The dxgi and winmm proxies hand-list their flags instead of calling
   ue5_apply_common_msvc_flags, so an edit to the function alone misses them; that is why each target is listed.
2. BEHAVIOUR. Small translation units are compiled with the flags taken from the UE5DumperCommon compile command
   for Macht.cpp, the real Macht.h included, and each case must end as stated: a bare discarded call fails with
   C4834; `std::ignore =` compiles; a misplaced attribute fails with C5240; a reasoned nodiscard fails.

Needs a tree build.ps1 configured (it reads build/'s ninja commands; it never configures or builds). Exit 0 when
everything holds, 1 otherwise.
"""
import os
import pathlib
import shlex
import shutil
import subprocess
import sys
import tempfile

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools" / "verify"))
import build_dll  # noqa: E402  (msvc_env and the configured-tree check only)

ERROR_FLAGS = ("/we4834", "/we4858", "/we5240")
# Every target that compiles DLL sources. A test target that #includes production .cpp files is listed too: a
# discard in such a file would otherwise fail the DLL build and only warn in the test build.
TARGET_OBJECT_MARKERS = {
    "UE5DumperCommon": "UE5DumperCommon.dir",
    "UE5Dumper_ProxyDxgi": "UE5Dumper_ProxyDxgi.dir",
    "UE5Dumper_ProxyWinmm": "UE5Dumper_ProxyWinmm.dir",
    "dll_helpers_test": "dll_helpers_test.dir",
    "grausam_window_test": "grausam_window_test.dir",
    "sein_retention_test": "sein_retention_test.dir",
    "dll_core_test": "dll_core_test.dir",
}

CASES = [
    # (name, body, must_compile, required diagnostic or None)
    ("bare discard", "void t() { Macht::WriteBytes(0, nullptr, 0); }", False, "C4834"),
    ("std::ignore", "void t() { std::ignore = Macht::WriteBytes(0, nullptr, 0); }", True, None),
    ("misplaced attribute", "static [[nodiscard]] bool g() { return true; }\nvoid t() { (void)g(); }", False,
     "C5240"),
    ("reasoned nodiscard", '[[nodiscard("probe")]] bool h();\nvoid t() { h(); }', False, None),
]


def is_compile(cmd):
    """A cl.exe compile line. CMake's Ninja generator writes `-c <source>`, not `/c`."""
    return "cl.exe" in cmd.lower() and (" -c " in cmd or " /c " in cmd)


def ninja_commands(env):
    r = subprocess.run([build_dll._ninja(env), "-C", str(build_dll.BUILD), "-t", "commands"], env=env,
                       capture_output=True, text=True, errors="replace")
    if r.returncode != 0:
        raise SystemExit(f"nodiscard_probe: ninja -t commands failed: {r.stderr[:500]}")
    return r.stdout.splitlines()


def main():
    build_dll.require_configured()
    env = build_dll.msvc_env()
    cmds = ninja_commands(env)
    ok = True

    print("1. flags in ninja's compile commands")
    for target, marker in TARGET_OBJECT_MARKERS.items():
        lines = [c for c in cmds if marker in c and is_compile(c)]
        if not lines:
            print(f"   FAIL {target}: no compile command found (is the target configured?)")
            ok = False
            continue
        missing = sorted({f for c in lines for f in ERROR_FLAGS if f not in c.split()})
        if missing:
            print(f"   FAIL {target}: {len(lines)} command(s), missing {' '.join(missing)}")
            ok = False
        else:
            print(f"   ok   {target}: {len(lines)} command(s) carry {' '.join(ERROR_FLAGS)}")

    print("2. behaviour, with UE5DumperCommon's flags for Macht.cpp")
    base = [c for c in cmds if "UE5DumperCommon.dir" in c and c.rstrip().endswith("Macht.cpp") and is_compile(c)]
    if not base:
        print("   FAIL: no UE5DumperCommon compile command for Macht.cpp")
        return 1
    parts = shlex.split(base[0], posix=False)
    keep = []
    skip_next = False
    for p in parts:
        if skip_next:
            skip_next = False
            continue
        low = p.lower()
        if low.endswith("macht.cpp") or low.startswith(("/fo", "/fd", "/showincludes", "-fo", "-fd")):
            continue
        if low in ("/fo", "-fo"):
            skip_next = True
            continue
        if low == "-c":   # re-added with the probe's own source below
            continue
        keep.append(p)
    keep.append("-c")
    cl = shutil.which("cl", path=env.get("PATH") or env.get("Path"))
    if not cl:
        print("   FAIL: cl.exe not on the MSVC environment's PATH")
        return 1
    keep[0] = f'"{cl}"'
    src_dir = ROOT / "dll" / "src"
    with tempfile.TemporaryDirectory() as tmp:
        for name, body, must_compile, need in CASES:
            src = pathlib.Path(tmp) / "probe.cpp"
            src.write_text(f'#include "{(src_dir / "Macht.h").as_posix()}"\n#include <tuple>\n{body}\n',
                           encoding="utf-8")
            cmd = " ".join(keep + [f'/Fo"{pathlib.Path(tmp) / "probe.obj"}"', f'"{src}"'])
            r = subprocess.run(cmd, cwd=str(build_dll.BUILD), env=env, capture_output=True, shell=True)
            text = (r.stdout + r.stderr).decode("utf-8", "replace")
            codes = sorted({w for w in ("C4834", "C4858", "C5240") if w in text})
            compiled = r.returncode == 0
            good = compiled == must_compile and (need is None or need in codes)
            if not must_compile and compiled:
                good = False
            ok &= good
            print(f"   {'ok  ' if good else 'FAIL'} {name}: {'compiled' if compiled else 'failed'}"
                  f" (diagnostics: {', '.join(codes) or 'none'})")
            if not good:
                tail = [ln for ln in text.splitlines() if " error " in ln or " warning " in ln][:5]
                for ln in tail:
                    print("        " + ln.strip())
    print("nodiscard_probe:", "OK" if ok else "FAILED")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
