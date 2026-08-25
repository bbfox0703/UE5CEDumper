r"""B29 step 3 — a UE game at a NON-ASCII path with a third-party wrapper loaded from it.

    py tools/verify/b29_nonascii_fixture.py create
    py tools/verify/b29_nonascii_fixture.py launch
    py tools/verify/b29_nonascii_fixture.py status
    py tools/verify/b29_nonascii_fixture.py clean

THE ROW (step 3): re-run step 2 *"再用一款路徑含非 ASCII 字元的遊戲"* and read the same message —
`CEPlugin: '%ls' is loaded but is not ours (path=%ls)` (`Methode.cpp:255`). The path must appear
intact rather than as `EVERSPACE? 2`. It was blocked as *"步驟 3（非 ASCII 路徑）仍缺樣本"* — no
sample. This builds the sample.

⛔ A JUNCTION DOES NOT WORK, AND THAT IS THE FIRST THING MEASURED HERE. `mklink /J` was the obvious
cheap route — it is what closed `AC17`/`VOLUMEROOT`, where a cross-volume junction stood in for a
real mount point. Here it is useless: launched through `D:\<CJK>\DumperTest`, the process reports

    QueryFullProcessImageNameW -> D:\UE_Analyze_data\For Testing\...\DumperTest-Win64-Shipping.exe

i.e. Windows resolves the reparse point and hands back the REAL path, so nothing non-ASCII ever
reaches the module list the row is about. ▶ The check needs a real directory, and a rig that used a
junction would report a confident PASS on an ASCII path.

⭐ SO IT IS A REAL DIRECTORY OF HARDLINKS. The package is 767 MB; copying it is slow and wasteful,
but a *directory* of hardlinks is a real directory whose *files* share the original's extents. 26
files, ~0 bytes, and `QueryFullProcessImageNameW` then returns the non-ASCII path — verified below,
which is the discriminator this rig exists to keep honest.

⚠ THE WRAPPER IS A REAL COPY, NOT A LINK. `dxgi.dll` is copied from SBDR rather than hardlinked, so
nothing this fixture does can reach the maintainer's own ReShade install.

⛔ WHAT THIS RIG DOES **NOT** DO, AND WHY. The message is emitted only from `OnInjectAndConnect`
(`Methode.cpp:291` / `:388`), the **CE plugin Type-5 menu callback**, and `Methode.cpp` is compiled
into `UE5Dumper.dll` itself. So reading it requires **registering our DLL as a Cheat Engine plugin** —
a persistent change to CE's configuration, and one that re-creates the very
`UE5Dumper.dll`-under-CE's-folder condition `[STALEDLL-2026-08-18]`(a) was closed by *deleting*, which
had blocked the `.CT DLL discovery` row until 2026-08-22. That is a maintainer decision, not a
side effect of a verification run. This rig stops at the fixture and says so.
"""
from __future__ import annotations

import ctypes
import ctypes.wintypes as wt
import hashlib
import os
import pathlib
import shutil
import subprocess
import sys
import time

ROOT = pathlib.Path("D:/") / "\u6e2c\u8a66"          # D:\測試
GAME = ROOT / "DumperTest"
SRC = pathlib.Path(r"D:\UE_Analyze_data\for testing\DumperTest\Shipping\Windows")
WRAPPER_SRC = pathlib.Path(
    r"D:\SteamLibrary\steamapps\common\SEED BATTLE DESTINY REMASTERED"
    r"\Game_SBDR\Binaries\Win64\dxgi.dll")
WIN64 = GAME / "DumperTest" / "Binaries" / "Win64"
EXE = WIN64 / "DumperTest-Win64-Shipping.exe"


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(s.encode(enc, "replace").decode(enc, "replace") + "\n")


def image_path(pid):
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    k32.OpenProcess.restype = ctypes.c_void_p
    h = k32.OpenProcess(0x1000, False, pid)
    if not h:
        return ""
    buf = ctypes.create_unicode_buffer(32768)
    n = ctypes.c_uint32(len(buf))
    ok = k32.QueryFullProcessImageNameW(ctypes.c_void_p(h), 0, buf, ctypes.byref(n))
    k32.CloseHandle(ctypes.c_void_p(h))
    return buf.value if ok else ""


def modules(pid, names):
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)

    class ME(ctypes.Structure):
        _fields_ = [("dwSize", ctypes.c_uint32), ("th32ModuleID", ctypes.c_uint32),
                    ("th32ProcessID", ctypes.c_uint32), ("GlblcntUsage", ctypes.c_uint32),
                    ("ProccntUsage", ctypes.c_uint32),
                    ("modBaseAddr", ctypes.POINTER(ctypes.c_byte)),
                    ("modBaseSize", ctypes.c_uint32), ("hModule", ctypes.c_void_p),
                    ("szModule", ctypes.c_wchar * 256), ("szExePath", ctypes.c_wchar * 260)]

    k32.CreateToolhelp32Snapshot.restype = ctypes.c_void_p
    snap = k32.CreateToolhelp32Snapshot(0x8 | 0x10, pid)
    out, e = [], ME()
    e.dwSize = ctypes.sizeof(e)
    ok = k32.Module32FirstW(ctypes.c_void_p(snap), ctypes.byref(e))
    while ok:
        if e.szModule.lower() in names:
            out.append((e.szModule, e.szExePath))
        ok = k32.Module32NextW(ctypes.c_void_p(snap), ctypes.byref(e))
    k32.CloseHandle(ctypes.c_void_p(snap))
    return out


def create():
    if not SRC.is_dir():
        say("MISSING source package %s" % SRC)
        return 1
    if not WRAPPER_SRC.is_file():
        say("MISSING third-party wrapper %s" % WRAPPER_SRC)
        return 1
    if GAME.exists():
        say("REFUSING: %s already exists -- run clean first" % GAME)
        return 1
    ROOT.mkdir(exist_ok=True)
    n_link = n_copy = 0
    for dirpath, _dirs, files in os.walk(SRC):
        rel = pathlib.Path(dirpath).relative_to(SRC)
        (GAME / rel).mkdir(parents=True, exist_ok=True)
        for fn in files:
            s, d = pathlib.Path(dirpath) / fn, GAME / rel / fn
            try:
                os.link(s, d)
                n_link += 1
            except OSError:
                shutil.copy2(s, d)
                n_copy += 1
    say("staged %s  (%d hardlinked, %d copied)" % (GAME, n_link, n_copy))
    dst = WIN64 / "dxgi.dll"
    shutil.copy2(WRAPPER_SRC, dst)      # a REAL copy -- never link the maintainer's ReShade
    say("third-party wrapper: %s" % dst)
    say("  %d bytes  sha256 %s" % (dst.stat().st_size,
                                   hashlib.sha256(dst.read_bytes()).hexdigest()[:32]))
    return 0


def launch():
    if not EXE.is_file():
        say("MISSING %s -- run create first" % EXE)
        return 1
    p = subprocess.Popen([str(EXE), "-windowed", "-ResX=1280", "-ResY=720",
                          "-DumperTestMaxFPS=15"])
    say("pid %d -- waiting for the module list to settle" % p.pid)
    time.sleep(14)
    ip = image_path(p.pid)
    say("image path : %s" % ip)
    nonascii = any(ord(c) > 127 for c in ip)
    say("NON-ASCII PRESERVED: %s" % nonascii)
    if not nonascii:
        say("⛔ the path came back ASCII -- this is the junction failure mode; the fixture is "
            "NOT usable for B29 step 3 in this state")
    for m, path in modules(p.pid, {"dxgi.dll", "version.dll", "dinput8.dll", "winmm.dll"}):
        marker = "  <-- THIRD-PARTY, from the game folder" if str(GAME) in path else ""
        say("  %-14s %s%s" % (m, path, marker))
    say("")
    say("NEXT (maintainer decision -- see this file's header): register UE5Dumper.dll as a CE")
    say("plugin, attach CE to pid %d, click 'UE5CEDumper: Inject && Connect', then grep the log" % p.pid)
    say("for: CEPlugin: 'dxgi.dll' is loaded but is not ours (path=...)")
    pathlib.Path("out").mkdir(exist_ok=True)
    pathlib.Path("out/host.pid").write_text(str(p.pid))
    return 0


def status():
    say("fixture present : %s" % GAME.is_dir())
    if GAME.is_dir():
        say("  exe     : %s" % EXE.is_file())
        w = WIN64 / "dxgi.dll"
        say("  wrapper : %s%s" % (w.is_file(),
                                  ("  (%d bytes)" % w.stat().st_size) if w.is_file() else ""))
        real = sum(1 for p in GAME.rglob("*") if p.is_file() and os.stat(p).st_nlink == 1)
        say("  files that are NOT hardlinks (i.e. real disk cost): %d" % real)
    return 0


def clean():
    if not GAME.exists():
        say("nothing to clean")
        return 0
    assert GAME.parent == ROOT and any(ord(c) > 127 for c in ROOT.name), "refusing %s" % GAME
    shutil.rmtree(GAME)
    say("removed %s" % GAME)
    try:
        ROOT.rmdir()
        say("removed %s (was empty)" % ROOT)
    except OSError:
        say("kept %s (not empty)" % ROOT)
    return 0


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else "status"
    sys.exit({"create": create, "launch": launch, "clean": clean}.get(cmd, status)())
