#!/usr/bin/env python3
"""Environment bootstrapper -- detect first, install second, verify last.

    py tools/bootstrap.py                 # --check, the default: installs NOTHING
    py tools/bootstrap.py --dry-run       # print the exact commands it WOULD run
    py tools/bootstrap.py --install       # actually install the missing pieces
    py tools/bootstrap.py --check --verify --tiers build,gates

The reasoning behind every row lives in docs/toolchain.md. This file is the mechanism.

WHY PYTHON AND NOT POWERSHELL
    An unsigned, create-heavy .ps1 is the exact shape that got six files quarantined by
    Bitdefender ATD on this machine -- four of them unrelated to what the script did, and
    path-blocked afterwards so even `git checkout` failed until reboot (working-lessons
    §3.8). Every automated tool in this repo is Python for that reason; the .ps1 that
    remains (build.ps1) is invoked by hand. bootstrap.cmd is a two-line launcher, not a
    script -- .cmd is not the hazard shape.

DESIGN RULES, all of which cost something to learn
    * stdlib ONLY. This runs before pip is guaranteed to have anything, and all the
      repo's gates are stdlib-only already.
    * Detect with a FUNCTIONAL PROBE of the tool, never with `winget list`. A package can
      be installed and not on PATH (VS's MinGit), and a tool can be on PATH with no winget
      record (portable copies, VS-bundled binaries).
    * NEVER parse winget's printed output -- it is localized (this machine answers in
      Chinese). Exit codes only.
    * A winget install writes the REGISTRY PATH; this process's environment does not
      update. Refresh from the registry after each install, or stop and say so. Reporting
      a false MISSING on a stale PATH is worse than reporting nothing.
"""

from __future__ import annotations

import sys

# FIRST executable line: the console here is cp950, and any ✅/⚠ in a print would raise
# UnicodeEncodeError before a single row reached the screen.
try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

import argparse
import ctypes
import glob
import json
import os
import pathlib
import re
import shutil
import subprocess
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[1]
VSCONFIG = ROOT / ".vsconfig"

# Exit codes -- see docs/toolchain.md §11.
EXIT_OK, EXIT_REQUIRED_MISSING, EXIT_VERIFY_FAILED, EXIT_OPTIONAL_MISSING, EXIT_PREFLIGHT = 0, 1, 2, 3, 10

TIERS = ("build", "gates", "re", "live", "contrib")
# The default is the DEVELOP set, not the minimum-to-compile set. The point of this
# script is that the maintainer no longer remembers what a working machine needs -- so
# the default has to be "everything I actually use", with `--tiers build` available for
# the narrower case (a CI box, a machine that only compiles).
DEFAULT_TIERS = ("build", "gates", "re")


# ---------------------------------------------------------------- small helpers

def run(cmd, timeout=60, shell=False):
    """Return (rc, stdout+stderr). Never raises for a missing binary."""
    try:
        p = subprocess.run(cmd, shell=shell, capture_output=True, text=True,
                           encoding="utf-8", errors="replace", timeout=timeout)
        return p.returncode, (p.stdout or "") + (p.stderr or "")
    except FileNotFoundError:
        return 127, ""
    except subprocess.TimeoutExpired:
        return 124, ""
    except OSError as e:                      # e.g. WinError 193 on a stub
        return 126, str(e)


def probe(cmd, pattern=None, timeout=60):
    """(ok, version_text). ok means the tool ran; pattern extracts a version if given.

    Note `java -version` writes to STDERR -- run() merges both streams for exactly that.
    """
    rc, out = run(cmd, timeout=timeout)
    if rc != 0:
        return False, ""
    if pattern is None:
        return True, out.strip().splitlines()[0] if out.strip() else "ok"
    m = re.search(pattern, out)
    return (True, m.group(0)) if m else (True, out.strip().splitlines()[0] if out.strip() else "ok")


def is_admin():
    try:
        return bool(ctypes.windll.shell32.IsUserAnAdmin())
    except Exception:
        return False


def refresh_path():
    """Rebuild os.environ['PATH'] from the registry (machine ; user).

    winget installers write the registry, not this process. Without this, everything
    installed during the run reads back as MISSING.
    """
    try:
        import winreg
    except ImportError:
        return False
    parts = []
    for hive, sub in ((winreg.HKEY_LOCAL_MACHINE,
                       r"SYSTEM\CurrentControlSet\Control\Session Manager\Environment"),
                      (winreg.HKEY_CURRENT_USER, r"Environment")):
        try:
            with winreg.OpenKey(hive, sub) as k:
                val, _ = winreg.QueryValueEx(k, "Path")
                if val:
                    parts.append(os.path.expandvars(val))
        except OSError:
            pass
    if not parts:
        return False
    os.environ["PATH"] = ";".join(parts)
    return True


def vswhere_exe():
    """build.ps1's own candidate order -- PATH FIRST, which is why a winget portable copy
    of vswhere can shadow the real one. Mirrored deliberately: the point of this probe is
    to see what build.ps1 will see, not to find the best vswhere."""
    found = shutil.which("vswhere")
    if found:
        return found
    for base in (os.environ.get("ProgramFiles(x86)"), os.environ.get("ProgramFiles"),
                 os.environ.get("LOCALAPPDATA")):
        if not base:
            continue
        for tail in (r"Microsoft Visual Studio\Installer\vswhere.exe",
                     r"Microsoft\VisualStudio\Installer\vswhere.exe"):
            p = pathlib.Path(base) / tail
            if p.is_file():
                return str(p)
    return None


def vswhere(*args):
    exe = vswhere_exe()
    if not exe:
        return ""
    rc, out = run([exe, *args])
    return out.strip() if rc == 0 else ""


def vsconfig_components():
    try:
        return json.loads(VSCONFIG.read_text(encoding="utf-8")).get("components", [])
    except Exception:
        return []


# ---------------------------------------------------------------- the tool table

class Tool:
    __slots__ = ("key", "name", "tier", "required", "detect", "install", "manual", "note")

    def __init__(self, key, name, tier, required, detect, install=None, manual=None, note=""):
        self.key, self.name, self.tier, self.required = key, name, tier, required
        self.detect, self.install, self.manual, self.note = detect, install, manual, note


def d_powershell51():
    ok, v = probe(["powershell", "-NoProfile", "-Command", "$PSVersionTable.PSVersion.Major"],
                  r"\d+")
    return (ok and v.strip().isdigit() and int(v.strip()) >= 5), (("5.1+ (major " + v.strip() + ")") if ok else "")


def d_winget():
    return probe(["winget", "--version"], r"v?[\d.]+")


def d_git():
    # `git version 2.55.0.windows.3` -- a bare [\d.]+ stops at the 'w' and renders "2.55.0.",
    # which reads like a truncated read rather than a version.
    return probe(["git", "--version"], r"\d+\.\d+\.\d+(?:\.\w+)*")


def d_submodules():
    rc, out = run(["git", "submodule", "status", "--recursive"], timeout=90)
    if rc != 0:
        return False, ""
    lines = [l for l in out.splitlines() if l.strip()]
    if not lines:
        return False, "no submodules reported"
    missing = [l for l in lines if l.lstrip().startswith("-")]
    return (not missing), f"{len(lines) - len(missing)}/{len(lines)} initialised"


def d_vswhere():
    p = vswhere_exe()
    return (p is not None), (p or "")


def d_vs_cpp():
    """Mirrors build.ps1's filter EXACTLY -- that is the whole point of this probe: to see
    what build.ps1 will see, not to find the best VS. All three call sites (build.ps1,
    tools/verify/build_dll.py, tools/verify/compile_sdk_header.py) drive one shared build/
    directory, so a filter that differs here would report a toolset the build then refuses
    to use."""
    filt = ("-latest", "-products", "*", "-prerelease")
    anywhere = vswhere(*filt, "-requires",
                       "Microsoft.VisualStudio.Component.VC.Tools.x86.x64",
                       "-property", "installationPath")
    if not anywhere:
        # "No VS at all" and "VS present, C++ toolset absent" are different problems, and
        # the second is a one-click fix in the installer.
        anyvs = vswhere(*filt, "-property", "installationPath")
        if anyvs:
            return False, ("VS at " + anyvs.splitlines()[0] + " has NO C++ toolset -- "
                           "add VC.Tools.x86.x64 (see .vsconfig)")
        return False, ""
    ver = vswhere(*filt, "-property", "installationVersion") or "?"
    warn = "" if ver.split(".")[0].isdigit() and int(ver.split(".")[0]) >= 18 else "  ⚠ not v18"
    return True, f"{ver} @ {anywhere.splitlines()[0]}{warn}"



def d_vs_components():
    """Every id in .vsconfig must be present in the SAME install build.ps1 will pick."""
    want = [c for c in vsconfig_components() if c.startswith("Microsoft.VisualStudio.Component.")]
    if not want:
        return False, ".vsconfig unreadable"
    missing = [c for c in want
               if not vswhere("-latest", "-products", "*", "-requires", c, "-property", "installationPath")]
    if missing:
        return False, "missing: " + ", ".join(s.rsplit(".", 1)[-1] for s in missing)
    return True, f"{len(want)}/{len(want)} present"


def d_dotnet():
    rc, out = run(["dotnet", "--list-sdks"], timeout=120)
    if rc != 0:
        return False, ""
    # NEVER test for a directory under C:\Program Files\dotnet\sdk -- empty leftover
    # version dirs survive an uninstall and would fool a path check.
    tens = [l.split()[0] for l in out.splitlines() if l.strip().startswith("10.")]
    return (bool(tens), ", ".join(tens))


def d_python():
    rc, out = run([sys.executable, "-c", "import sys;print('%d.%d.%d' % sys.version_info[:3])"])
    have_py = shutil.which("py") is not None
    ver = out.strip() if rc == 0 else ""
    if not have_py:
        return False, f"{ver} (⚠ the `py` launcher is NOT on PATH; every doc and CI line spells `py`)"
    return True, ver


def d_nuget_reachable():
    try:
        req = urllib.request.Request("https://api.nuget.org/v3/index.json", method="HEAD")
        with urllib.request.urlopen(req, timeout=10) as r:
            return (200 <= r.status < 400), f"HTTP {r.status}"
    except Exception as e:
        return False, type(e).__name__


def d_lua():
    return probe(["lua", "-v"], r"Lua [\d.]+")


def d_java21():
    ok, v = probe(["java", "-version"], r'"?(\d+)[.\d_]*"?')
    if not ok:
        return False, ""
    m = re.search(r'(\d+)', v)
    major = int(m.group(1)) if m else 0
    return (major >= 21), f"{v} (major {major})"


def d_ghidra():
    home = os.environ.get("GHIDRA_HOME") or os.environ.get("GHIDRA_INSTALL_DIR")
    cands = [home] if home else []
    cands += sorted(glob.glob(r"D:\Tools\ghidra_*_PUBLIC")) + sorted(glob.glob(r"C:\ghidra_*_PUBLIC"))
    for c in cands:
        if c and (pathlib.Path(c) / "support" / "analyzeHeadless.bat").is_file():
            props = pathlib.Path(c) / "Ghidra" / "application.properties"
            ver = ""
            if props.is_file():
                m = re.search(r"application\.version=(\S+)", props.read_text(encoding="utf-8", errors="replace"))
                ver = m.group(1) if m else ""
            env = "" if home else "  ⚠ GHIDRA_HOME is not set"
            return True, f"{ver or '?'} @ {c}{env}"
    return False, ""


def _pymod(mod):
    def f():
        rc, _ = run([sys.executable, "-c", f"import {mod}"])
        if rc != 0:
            return False, ""
        # importlib.metadata, not `mod.__version__`: capstone's attribute reports the
        # ENGINE api version (5.0.7), not the pip distribution (5.0.9).
        rc2, out = run([sys.executable, "-c",
                        f"import importlib.metadata as m;print(m.version('{mod}'))"])
        return True, (out.strip() if rc2 == 0 else "ok")
    return f


def d_windbg():
    # The Store/winget WinDbg registers an app-execution alias; the classic one lives in
    # the Windows Kits tree that the VS SDK component already installs.
    p = shutil.which("windbgx") or shutil.which("windbg")
    if p:
        return True, p
    for c in sorted(glob.glob(r"C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\windbg.exe")):
        return True, c
    return False, ""


def d_sqlite():
    return probe(["sqlite3", "-version"], r"[\d.]+")


def d_rust_nightly():
    ok, v = probe(["cargo", "--version"], r"[\d.]+")
    if not ok:
        return False, ""
    rc, out = run(["rustup", "toolchain", "list"])
    nightly = rc == 0 and "nightly" in out
    return nightly, f"cargo {v}" + ("" if nightly else "  ⚠ no nightly toolchain (patternsleuth pins it)")


def d_cheatengine():
    p = pathlib.Path(os.environ.get("ProgramFiles", r"C:\Program Files")) / "Cheat Engine"
    exe = p / "cheatengine-x86_64-SSE4-AVX2.exe"
    return exe.is_file(), (str(p) if exe.is_file() else "")


def d_aobmaker():
    p = pathlib.Path(os.environ.get("ProgramFiles", r"C:\Program Files")) / "Cheat Engine" / "plugins" / "AOBMaker_CEPlugin.dll"
    return p.is_file(), (str(p) if p.is_file() else "")


def d_gh():
    return probe(["gh", "--version"], r"[\d.]+")


def d_pwsh7():
    p = shutil.which("pwsh")
    if not p:
        return False, ""
    ok, v = probe(["pwsh", "-NoProfile", "-Command", "$PSVersionTable.PSVersion.ToString()"], r"[\d.]+")
    return ok, v


def d_cloc():
    return probe(["cloc", "--version"], r"[\d.]+")


WG = "winget install --exact --source winget --accept-source-agreements --accept-package-agreements --disable-interactivity --id "

VS_INSTALL = (WG + "Microsoft.VisualStudio.Community "
              '--override "--quiet --wait --norestart --config \\"' + str(VSCONFIG) + '\\""')

TOOLS = [
    # ---- preflight (never installed here) ----
    Tool("ps51", "Windows PowerShell 5.1", "build", True, d_powershell51,
         manual="In-box on Windows; cannot be installed. build.cmd spawns it by design.",
         note="⛔ Do not repoint build.cmd at pwsh 7 -- DevShell.dll is .NET Framework."),
    Tool("winget", "winget (App Installer)", "build", True, d_winget,
         manual="Microsoft Store -> 'App Installer', or the Microsoft.DesktopAppInstaller "
                "msixbundle from github.com/microsoft/winget-cli/releases",
         note="There is no bootstrapping the bootstrapper."),

    # ---- Tier A: build ----
    Tool("git", "Git for Windows", "build", True, d_git, install=WG + "Git.Git",
         note="ADMIN. A build dep too: dll/CMakeLists.txt shells git at configure time."),
    Tool("submodules", "Submodules (minhook, zydis, zycore)", "build", True, d_submodules,
         install="git submodule update --init --recursive",
         note="vendor/nlohmann/json.hpp is committed -- no third submodule to miss."),
    Tool("vs", "Visual Studio 2026 + C++ toolset", "build", True, d_vs_cpp, install=VS_INSTALL,
         note="ADMIN. ⚠ winget id has NO YEAR: Microsoft.VisualStudio.Community."),
    Tool("vscomp", "VS components from .vsconfig", "build", True, d_vs_components,
         install='"%ProgramFiles(x86)%\\Microsoft Visual Studio\\Installer\\vs_installer.exe" '
                 'modify --installPath "<vswhere -latest -property installationPath>" '
                 '--config "' + str(VSCONFIG) + '" --quiet --norestart',
         note="⚠ These are RECOMMENDED deps of NativeDesktop, so --add the workload alone "
              "installs NO compiler, NO CMake, NO SDK. Supplies cmake+ninja -- do not "
              "also install Kitware.CMake / Ninja-build.Ninja."),
    Tool("vswhere", "vswhere", "build", True, d_vswhere,
         install=WG + "Microsoft.VisualStudio.Locator",
         note="Ships with the VS Installer. Repair-only fallback -- build.ps1 probes PATH "
              "FIRST, so a portable copy shadows the real one."),
    Tool("dotnet", ".NET SDK 10.0.x", "build", True, d_dotnet, install=WG + "Microsoft.DotNet.SDK.10",
         note="ADMIN. Or the VS component Microsoft.NetCore.Component.SDK -- pick ONE."),
    Tool("nuget", "nuget.org reachable", "build", True, d_nuget_reachable,
         manual="PublishAot restores Microsoft.DotNet.ILCompiler at publish time.",
         note="A private-feed-only machine fails with a RESTORE error, not a toolchain one."),

    # ---- Tier B: gates ----
    Tool("python", "Python 3 + the `py` launcher", "gates", True, d_python,
         install=WG + "Python.Python.3.12 && " + WG + "Python.Launcher",
         note="user scope, no admin. ⚠ Never detect a command named `python` -- on a fresh "
              "box that is the WindowsApps alias stub. All gates are STDLIB-ONLY: no pip step."),
    Tool("lua", "Lua 5.4 (CE-script rigs)", "gates", False, d_lua, install=WG + "DEVCOM.Lua",
         note="Runs scripts/tests/*.lua. Deliberately not a CI gate."),

    # ---- Tier C: offline RE ----
    Tool("java", "JDK 21 (Ghidra runtime)", "re", False, d_java21, install=WG + "Microsoft.OpenJDK.21",
         note="ADMIN. Must be a JDK, not a JRE -- Ghidra compiles .java scripts at runtime."),
    Tool("ghidra", "Ghidra 12.x PUBLIC", "re", False, d_ghidra,
         manual="Download from github.com/NationalSecurityAgency/ghidra/releases, unzip to a "
                "SPACE-FREE path, then: setx GHIDRA_HOME <path>",
         note="Not in winget. No admin (plain unzip). Do not hard-pin a version."),
    Tool("capstone", "capstone (pip)", "re", False, _pymod("capstone"),
         install=f'"{sys.executable}" -m pip install capstone',
         note="tools/pe/disasm_function.py"),
    Tool("pefile", "pefile (pip)", "re", False, _pymod("pefile"),
         install=f'"{sys.executable}" -m pip install pefile',
         note="tools/pe/disasm_function.py"),
    Tool("numpy", "numpy (pip)", "re", False, _pymod("numpy"),
         install=f'"{sys.executable}" -m pip install numpy',
         note="Rebuilding the AOB n-gram index (tools/pe/build_ngram_index.py). The index "
              "itself is git-tracked, so this is only needed to REGENERATE it."),
    Tool("windbg", "WinDbg", "re", False, d_windbg, install=WG + "Microsoft.WinDbg",
         note="Reading the crash dumps behind tools/pe/minidump_triage.py, and `gflags` "
              "page-heap runs (working-lessons §3.6). gflags ships in the Windows Kits "
              "Debuggers folder, not with WinDbg's Store package."),
    Tool("sqlite", "sqlite3 CLI", "re", False, d_sqlite, install=WG + "SQLite.SQLite",
         note="Opening %LOCALAPPDATA%\\UE5CEDumper\\Snapshots\\*.db by hand. NOTHING in "
              "the repo needs it -- the app reaches SQLite through NuGet and Python "
              "through its bundled stdlib module. Convenience only."),
    Tool("rust", "Rust + nightly (patternsleuth)", "re", False, d_rust_nightly,
         install=WG + "Rustlang.Rustup && rustup toolchain install nightly",
         note="user scope. patternsleuth pins channel=nightly in rust-toolchain.toml."),
    Tool("cloc", "cloc", "re", False, d_cloc, install=WG + "AlDanial.Cloc",
         note="⚠ casing: AlDanial.cloc is NOT found."),

    # ---- Tier D: live ----
    Tool("ce", "Cheat Engine 7.7+", "live", False, d_cheatengine,
         manual="github.com/cheat-engine/cheat-engine/releases (offer-free; cheatengine.org's "
                "installer carries bundled offers). Run elevated, KEEP the default path.",
         note="Not in winget. A rig hardcodes cheatengine-x86_64-SSE4-AVX2.exe there."),
    Tool("aobmaker", "AOBMaker CE plugin", "live", False, d_aobmaker,
         manual="Private sibling repo: gh auth login, clone, cmake --build, copy the DLL into "
                "%ProgramFiles%\\Cheat Engine\\plugins\\ (elevated).",
         note="Native C++ CMake target -- reuses Tier A's toolchain, adds nothing."),

    # ---- Tier E: contributing ----
    Tool("gh", "GitHub CLI", "contrib", False, d_gh, install=WG + "GitHub.cli",
         note="ADMIN, then `gh auth login`. No repo script invokes gh -- skip on a build-only box."),
    Tool("pwsh", "PowerShell 7", "contrib", False, d_pwsh7, install=WG + "Microsoft.PowerShell",
         note="msixbundle, NO admin. ⛔ Must not be wired into the build."),
]


# ---------------------------------------------------------------- verification

def verify_gates():
    print("\n── Stage 1: gates ──────────────────────────────────────────────")
    rc, out = run([sys.executable, str(ROOT / "tools" / "check_all.py")], timeout=900)
    tail = [l for l in out.splitlines() if "gate(s) run" in l]
    # Derive the count from the line itself. It was 4, then 12, then 13 -- a literal here
    # would be wrong by default.
    print("   " + (tail[-1].strip() if tail else f"(no summary line; rc={rc})"))
    return rc == 0


def verify_build(assume_yes):
    print("\n── Stage 2: AOT publish ────────────────────────────────────────")
    dist = ROOT / "dist" / "UE5DumpUI.exe"
    if dist.is_file() and not assume_yes:
        mb = dist.stat().st_size / (1024 * 1024)
        print(f"   SKIPPED -- dist\\UE5DumpUI.exe already exists ({mb:.1f} MB) and a publish "
              f"replaces it.\n   Pass --yes to rebuild it.")
        return None
    print("   running build.ps1 -Mode Publish -NoBumpBuildNumber (minutes)...")
    rc, out = run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
                   str(ROOT / "build.ps1"), "-Mode", "Publish", "-NoBumpBuildNumber"],
                  timeout=3600)
    if not dist.is_file():
        print(f"   FAILED (rc={rc}); dist\\UE5DumpUI.exe was not produced")
        for line in [l for l in out.splitlines() if "error" in l.lower()][-3:]:
            print("     " + line.strip())
        return False
    mb = dist.stat().st_size / (1024 * 1024)
    # ⚠ ~107 MB is the NON-TRIMMED build and is a FAILURE of this check, not a pass.
    trimmed = mb < 80
    print(f"   dist\\UE5DumpUI.exe = {mb:.1f} MB  ->  " +
          ("AOT-trimmed ✓" if trimmed else
           "⚠ NON-TRIMMED. Only -Mode Publish leaves a trimmed dist; -Target Test/UI overwrite it."))
    return rc == 0 and trimmed


# ---------------------------------------------------------------- main

def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    g = ap.add_mutually_exclusive_group()
    g.add_argument("--check", action="store_true", help="detect and report only (DEFAULT)")
    g.add_argument("--install", action="store_true", help="install what is missing")
    g.add_argument("--dry-run", action="store_true", help="print the commands it would run")
    ap.add_argument("--tiers", default=",".join(DEFAULT_TIERS),
                    help=f"comma-separated, from {','.join(TIERS)} "
                         f"(default: {','.join(DEFAULT_TIERS)} — the DEVELOP set)")
    ap.add_argument("--all", action="store_true", help="every tier, including live + contrib")
    ap.add_argument("--verify", action="store_true", help="run the gates after checking")
    ap.add_argument("--build-verify", action="store_true", help="also run the AOT publish (minutes)")
    ap.add_argument("--yes", action="store_true", help="allow the publish to overwrite dist\\")
    ap.add_argument("--json", action="store_true", help="machine-readable report")
    a = ap.parse_args(argv)

    tiers = list(TIERS) if a.all else [t.strip() for t in a.tiers.split(",") if t.strip()]
    bad = [t for t in tiers if t not in TIERS]
    if bad:
        print(f"unknown tier(s): {', '.join(bad)}   (valid: {', '.join(TIERS)})")
        return EXIT_PREFLIGHT
    selected = [t for t in TOOLS if t.tier in tiers or t.key in ("ps51", "winget")]

    print(f"UE5CEDumper bootstrap — tiers: {', '.join(tiers)}"
          f"{'  [DRY RUN]' if a.dry_run else '  [INSTALL]' if a.install else '  [check only]'}")
    print(f"repo: {ROOT}")
    print(f"admin: {'yes' if is_admin() else 'NO'}      reasoning: docs/toolchain.md\n")

    # ---- pass 1: detect ----
    rows, missing_req, missing_opt, manual_only = [], [], [], []
    for t in selected:
        try:
            ok, ver = t.detect()
        except Exception as e:                    # a probe must never abort the report
            ok, ver = False, f"probe error: {type(e).__name__}"
        rows.append((t, ok, ver))
        if not ok:
            (missing_req if t.required else missing_opt).append(t)
            if t.install is None:
                manual_only.append(t)

    width = max(len(t.name) for t, _, _ in rows)
    print(f"  {'TOOL'.ljust(width)}  TIER      R/O  STATUS")
    print(f"  {'-' * width}  --------  ---  ------")
    for t, ok, ver in rows:
        mark = "OK " if ok else "-- "
        print(f"  {t.name.ljust(width)}  {t.tier.ljust(8)}  {'REQ' if t.required else 'opt'}  "
              f"{mark}{ver}")

    # ---- pass 2: what to do about it ----
    if missing_req or missing_opt:
        print("\n── Missing ─────────────────────────────────────────────────────")
        for t in missing_req + missing_opt:
            tag = "REQUIRED" if t.required else "optional"
            print(f"\n  [{tag}] {t.name}")
            if t.note:
                print(f"      {t.note}")
            if t.install:
                print(f"      install: {t.install}")
            if t.manual:
                print(f"      MANUAL:  {t.manual}")

    if a.dry_run:
        print("\n(dry run — nothing was installed)")

    installed_any = False
    if a.install and (missing_req or missing_opt):
        needs_admin = [t for t in missing_req if t.install and "ADMIN" in t.note]
        if needs_admin and not is_admin():
            print("\n⛔ PREFLIGHT: these need an elevated shell, and installing only the "
                  "user-scope half would leave a half-built environment:")
            for t in needs_admin:
                print(f"     {t.name}")
            print("   Re-run this from an elevated terminal.")
            return EXIT_PREFLIGHT
        for t in missing_req + missing_opt:
            if not t.install:
                continue
            print(f"\n>> installing {t.name}")
            rc, _ = run(t.install, timeout=3600, shell=True)
            print(f"   rc={rc}")
            installed_any = True
        if installed_any:
            print("\n>> refreshing PATH from the registry...")
            if refresh_path():
                print("   done — re-run with --check to confirm.")
            else:
                print("   ⚠ could not refresh. OPEN A NEW SHELL and re-run --check; this "
                      "process's PATH is stale and would report a false MISSING.")

    # ---- pass 3: verify ----
    gates_ok = build_ok = None
    if a.verify or a.build_verify:
        if missing_req:
            print("\n⛔ Skipping verification — a REQUIRED tool is missing. The failure would "
                  "surface as C1083 / LNK2019 / ILCompiler 9009 and read as 'the repo is "
                  "broken' rather than 'you are missing something'.")
        else:
            gates_ok = verify_gates()
            if a.build_verify:
                build_ok = verify_build(a.yes)

    if a.json:
        print("\n" + json.dumps({
            "tiers": tiers, "admin": is_admin(),
            "tools": [{"key": t.key, "name": t.name, "tier": t.tier,
                       "required": t.required, "present": ok, "version": ver}
                      for t, ok, ver in rows],
            "gates_ok": gates_ok, "build_ok": build_ok,
        }, indent=2))

    # ---- exit code ----
    if missing_req:
        print(f"\nRESULT: {len(missing_req)} REQUIRED tool(s) missing.")
        return EXIT_REQUIRED_MISSING
    if gates_ok is False or build_ok is False:
        print("\nRESULT: tools complete, but verification FAILED.")
        return EXIT_VERIFY_FAILED
    if missing_opt:
        print(f"\nRESULT: required set complete; {len(missing_opt)} optional tool(s) missing"
              f"{' (manual steps above)' if manual_only else ''}.")
        return EXIT_OPTIONAL_MISSING
    print("\nRESULT: everything in the selected tiers is present." +
          ("  Gates green." if gates_ok else ""))
    return EXIT_OK


if __name__ == "__main__":
    sys.exit(main())
