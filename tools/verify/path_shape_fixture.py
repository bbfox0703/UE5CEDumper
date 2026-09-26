"""Live rig for [PATH-SHAPE-2026-09-25]: a DumperTest51 Shipping copy whose exe answers to several NAME SHAPES.

    py tools/verify/path_shape_fixture.py make  <DumperTest51 Shipping\\Windows dir> <work dir>
    py tools/verify/path_shape_fixture.py list  <work dir>
    py tools/verify/path_shape_fixture.py clean <work dir>

WHY. The rows fixed a non-ASCII exe name (module_name, the confirmed-proxy key, CE's view of the name incl. the DBCS
0x5C trail-byte cut), an apostrophe / '&' in it (trainer Lua, CE XML), and a stem ending in a space (the log folder).
No real game here has such a name (the audit walked both Steam libraries), so the fixture supplies them: ONE copy of
the packaged build, and the Shipping exe HARD-LINKED under each name in its own Binaries\\Win64 -- a packaged UE game
finds its content from its own folder, not its file name, and GetModuleFileName returns the link's name. Paths are
arguments (gates and rigs never reach for a machine path).
"""
import os
import pathlib
import shutil
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

EXE = "DumperTest51-Win64-Shipping.exe"
# name -> what it exercises
SHAPES = {
    "DumperTest51遊戲-Win64-Shipping.exe": "non-ASCII, held by cp950: CE knows the real name",
    "DumperTest51™-Win64-Shipping.exe": "non-ASCII, not in cp950: CE knows it as '?'",
    "功夫-Win64-Shipping.exe": "Big5 功 = A5 5C: ANSI Module32First cuts it -- CE knows '夫-Win64-Shipping.exe'",
    "Tony's&Jerry-Win64-Shipping.exe": "an apostrophe (trainer Lua) and '&' (CE XML)",
    "DumperTest51-Win64-Shipping .exe": "a stem ending in a space: the log folder",
}


def win64(work):
    return pathlib.Path(work) / "DumperTest51" / "Binaries" / "Win64"


def make(src, work):
    src, work = pathlib.Path(src), pathlib.Path(work)
    if not (src / "DumperTest51" / "Binaries" / "Win64" / EXE).exists():
        raise SystemExit(f"not a DumperTest51 Shipping\\Windows folder: {src}")
    if not work.exists():
        print(f"copying {src} -> {work} ...")
        shutil.copytree(src, work)
    b = win64(work)
    for name in SHAPES:
        link = b / name
        if not link.exists():
            os.link(b / EXE, link)
    list_(work)


def list_(work):
    b = win64(work)
    for name, why in SHAPES.items():
        p = b / name
        print(f"  {'ok ' if p.exists() else 'MISSING'}  {name}   -- {why}")


def clean(work):
    b = win64(work)
    for name in SHAPES:
        p = b / name
        if p.exists():
            p.unlink()
            print(f"  removed {name}")


if __name__ == "__main__":
    verb = sys.argv[1] if len(sys.argv) > 1 else ""
    if verb == "make" and len(sys.argv) == 4:
        make(sys.argv[2], sys.argv[3])
    elif verb == "list" and len(sys.argv) == 3:
        list_(sys.argv[2])
    elif verb == "clean" and len(sys.argv) == 3:
        clean(sys.argv[2])
    else:
        raise SystemExit(__doc__)
