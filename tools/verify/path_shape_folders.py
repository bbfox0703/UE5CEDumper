"""FOLDER shapes for [PATH-SHAPE-2026-09-25], made INSIDE the repo (out/pathshape/, gitignored) -- never ad hoc elsewhere.

    py tools/verify/path_shape_folders.py make                 # create every shape folder under out/pathshape/
    py tools/verify/path_shape_folders.py probe [--json FILE]  # measure each shape on this machine (read-only-ish)
    py tools/verify/path_shape_folders.py stage-dist <shape>   # copy dist\\ into out/pathshape/<shape>/UE5CEDumper/
    py tools/verify/path_shape_folders.py host <shape> <dir>   # MOVE a fixture folder (same volume) into the shape
    py tools/verify/path_shape_folders.py unhost <shape>       # move it back where `host` found it
    py tools/verify/path_shape_folders.py clean                # remove out/pathshape/ (refuses while a fixture is hosted)
    py tools/verify/path_shape_folders.py list                 # the shapes and their code points

WHY. The maintainer's rule (2026-09-25): a path shape used for a check is made by a committed script inside the repo,
not a folder someone created by hand somewhere else -- so it can be rebuilt on the other PC, and nobody has to guess
which code points a name held. Every name here is spelled with escapes: the source stays ASCII, and a Windows input
method cannot normalise it (it turned the maintainer's typed U+2126 OHM / U+212A KELVIN / U+212B ANGSTROM signs into
U+03A9 / U+004B / U+00C5 on disk -- measured -- so BOTH spellings are shapes).

WHAT `probe` SAYS, per shape (the ACP and the volume's 8.3 setting are this machine's):
  * the order WindowsSystemCodePage.AnsiPathBytes / Methode::NarrowForAnsiLoad take for <shape>\\UE5CEDumper\\UE5Dumper.dll
    -- ASCII as is, an ASCII 8.3 FOLDER alias with the DLL's own name, the exact ANSI narrowing, the alias narrowed
    exactly, else REFUSED (CE's injectDLL -> LoadLibraryA cannot open it; the scripts refuse and say why);
  * which characters the ACP holds exactly, and the best-fit view ANSI APIs (and so CE) show;
  * a file created, read and deleted inside through the wide API; the UTF-8 round trip;
  * the log folder an exe of that name would get (Sein::ProcessFolderName).

`host` / `unhost` move a fixture that lives outside the repo (a packaged game is far too big to copy) into a shape
for a live check and back; the fixture path is an ARGUMENT, and the move is refused across volumes. Not a gate.
"""
from __future__ import annotations

import ctypes
import json
import os
import pathlib
import shutil
import sys
from ctypes import wintypes

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ROOT = pathlib.Path(__file__).resolve().parents[2]
BASE = ROOT / "out" / "pathshape"
HOSTED = ".hosted-from.txt"

# name -> (folder name, what it exercises)
SHAPES = {
    "letterlike": ("\u2122 \u2123 \u2124 \u2125 \u03A9 \u2127 \u2128 \u2129 K \u00C5 \u212C \u212D \u212E \u212F "
                   "\u2130 \u2131 \u2132 \u2133 \u2134 \u2135",
                   "the maintainer's D:\\tmp folder, code point for code point as stored on disk"),
    "letterlike-signs": ("\u2122 \u2123 \u2124 \u2125 \u2126 \u2127 \u2128 \u2129 \u212A \u212B \u212C \u212D \u212E "
                         "\u212F \u2130 \u2131 \u2132 \u2133 \u2134 \u2135",
                         "the same with the real OHM / KELVIN / ANGSTROM signs: NTFS keeps them apart from "
                         "\u03A9 / K / \u00C5, so must we (no normalisation)"),
    "tm": ("EVERSPACE\u2122 2", "ES2's folder: \u2122 is not in cp950"),
    "big5": ("\u5DE5\u5177", "\u5DE5\u5177: cp950 holds it exactly (A4 75 A8 E3)"),
    "big5-5c": ("\u529F\u592B", "\u529F\u592B: \u529F is A5 5C in Big5 -- a trail byte that equals '\\'"),
    "kana": ("\u30C4\u30FC\u30EB", "katakana: not in cp950"),
    "spaces": ("DragonSword  Awakening", "two spaces in a row"),
    "apostrophe": ("No Man's Sky", "an apostrophe (Lua and CE XML literals)"),
    "ampersand": ("Tom&Jerry", "'&' (CE XML)"),
    "emoji": ("\U0001F600 smile", "a supplementary character: a UTF-16 surrogate pair"),
}

k32 = ctypes.WinDLL("kernel32", use_last_error=True)
k32.WideCharToMultiByte.argtypes = [wintypes.UINT, wintypes.DWORD, wintypes.LPCWSTR, ctypes.c_int, ctypes.c_char_p,
                                    ctypes.c_int, ctypes.c_char_p, ctypes.POINTER(wintypes.BOOL)]
k32.GetShortPathNameW.argtypes = [wintypes.LPCWSTR, wintypes.LPWSTR, wintypes.DWORD]
WC_NO_BEST_FIT_CHARS = 0x400
CP_UTF8 = 65001


def shape_dir(name: str) -> pathlib.Path:
    if name not in SHAPES:
        raise SystemExit(f"unknown shape {name!r}; one of: {', '.join(SHAPES)}")
    return BASE / SHAPES[name][0]


def narrow(text: str, flags: int, cp: int):
    """(bytes, usedDefault) -- the API's own answer, never a Python codec table."""
    if not text:
        return b"", False
    wlen = len(text.encode("utf-16-le")) // 2
    used = wintypes.BOOL(0)
    use_default = None if cp == CP_UTF8 else ctypes.byref(used)
    f = 0 if cp == CP_UTF8 else flags
    n = k32.WideCharToMultiByte(cp, f, text, wlen, None, 0, None, use_default)
    buf = ctypes.create_string_buffer(max(n, 1))
    used = wintypes.BOOL(0)
    use_default = None if cp == CP_UTF8 else ctypes.byref(used)
    k32.WideCharToMultiByte(cp, f, text, wlen, buf, n, None, use_default)
    return buf.raw[:n], bool(used.value)


def short_dir(path: str) -> str:
    buf = ctypes.create_unicode_buffer(1024)
    n = k32.GetShortPathNameW(path, buf, 1024)
    return buf.value if 0 < n < 1024 else ""


def ansi_path_verdict(dll: str, acp: int):
    """WindowsSystemCodePage.AnsiPathBytes' order, reported as (verdict, bytes-or-None)."""
    if all(ord(c) < 0x80 for c in dll):
        return "ASCII as is", dll.encode("ascii")
    folder, leaf = os.path.split(dll)
    alias_dir = short_dir(folder)
    alias = os.path.join(alias_dir, leaf) if alias_dir else ""
    have = bool(alias) and alias != dll
    if have and all(ord(c) < 0x80 for c in alias):
        return "ASCII 8.3 folder alias", alias.encode("ascii")
    exact, used = narrow(dll, WC_NO_BEST_FIT_CHARS, acp)
    if not used:
        return f"exact cp{acp} bytes", exact
    if have:
        exact2, used2 = narrow(alias, WC_NO_BEST_FIT_CHARS, acp)
        if not used2:
            return "the alias's exact bytes", exact2
    return "REFUSED (no ANSI form: the scripts refuse and say why)", None


def process_folder_name(exe: str) -> str:
    stem = exe.rsplit(".", 1)[0] if "." in exe else exe
    stem = "".join("_" if c in '/\\:*?"<>|' else c for c in stem).rstrip(" .")
    return stem or "unknown"


def make():
    for name in SHAPES:
        d = shape_dir(name)
        d.mkdir(parents=True, exist_ok=True)
        print(f"  ok  {name:17} {d}")


def probe(json_out: str | None):
    acp = k32.GetACP()
    print(f"ACP {acp}; base {BASE}")
    rows = []
    for name, (folder, why) in SHAPES.items():
        d = shape_dir(name)
        if not d.exists():
            raise SystemExit(f"{d} does not exist -- run `make` first")
        dll = str(d / "UE5CEDumper" / "UE5Dumper.dll")
        # the verdict needs the DLL's folder to exist (GetShortPathNameW of a missing folder fails)
        (d / "UE5CEDumper").mkdir(exist_ok=True)
        verdict, data = ansi_path_verdict(dll, acp)
        held = [c for c in folder if ord(c) >= 0x80 and not narrow(c, WC_NO_BEST_FIT_CHARS, acp)[1]]
        best_fit = narrow(folder, 0, acp)[0].decode(f"cp{acp}", "replace")
        f = d / "probe.txt"
        try:
            f.write_text("x", encoding="utf-8")
            wide_ok = f.read_text(encoding="utf-8") == "x"
            f.unlink()
        except OSError as e:
            wide_ok = f"FAILED: {e}"
        row = {
            "shape": name, "folder": folder, "why": why,
            "code_points": " ".join(f"U+{ord(c):04X}" for c in folder if c != " "),
            "ce_inject_path": verdict, "ansi_bytes": data.hex(" ") if data else None,
            "acp_holds_exactly": "".join(held), "best_fit_view": best_fit,
            "short_folder": short_dir(str(d)), "wide_api_file_ok": wide_ok,
            "utf8_round_trip": folder.encode("utf-8").decode("utf-8") == folder,
            "log_folder_for_exe": process_folder_name(folder + ".exe"),
        }
        rows.append(row)
        print(f"\n== {name}: {folder}\n   {why}")
        for k in ("code_points", "ce_inject_path", "ansi_bytes", "acp_holds_exactly", "best_fit_view", "short_folder",
                  "wide_api_file_ok", "utf8_round_trip", "log_folder_for_exe"):
            print(f"   {k:19} {row[k]}")
    if json_out:
        pathlib.Path(json_out).write_text(json.dumps(rows, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"\nwrote {json_out}")


def stage_dist(name: str):
    src = ROOT / "dist"
    dst = shape_dir(name) / "UE5CEDumper"
    if not (src / "UE5DumpUI.exe").exists():
        raise SystemExit(f"no {src / 'UE5DumpUI.exe'} -- publish first")
    if dst.exists():
        shutil.rmtree(dst)
    shutil.copytree(src, dst)
    print(f"  staged dist (build {(src / 'build_number.txt').read_text().strip()}) -> {dst}")


def host(name: str, fixture: str):
    d = shape_dir(name)
    d.mkdir(parents=True, exist_ok=True)
    src = pathlib.Path(fixture).resolve()
    if not src.is_dir():
        raise SystemExit(f"not a folder: {src}")
    if os.path.splitdrive(str(src))[0].lower() != os.path.splitdrive(str(d))[0].lower():
        raise SystemExit("refused: the fixture is on another volume -- a move there would be a full copy")
    marker = d / HOSTED
    if marker.exists():
        raise SystemExit(f"a fixture is already hosted here: {marker.read_text(encoding='utf-8')}")
    dst = d / src.name
    os.rename(src, dst)
    marker.write_text(str(src), encoding="utf-8")
    print(f"  moved {src}\n     -> {dst}")


def unhost(name: str):
    d = shape_dir(name)
    marker = d / HOSTED
    if not marker.exists():
        raise SystemExit(f"nothing hosted in {d}")
    origin = pathlib.Path(marker.read_text(encoding="utf-8"))
    here = d / origin.name
    if origin.exists():
        raise SystemExit(f"refused: {origin} exists again -- move it back by hand")
    os.rename(here, origin)
    marker.unlink()
    print(f"  moved back -> {origin}")


def clean():
    if not BASE.exists():
        return
    hosted = [str(p.parent) for p in BASE.rglob(HOSTED)]
    if hosted:
        raise SystemExit(f"refused: a fixture is hosted in {hosted} -- `unhost` it first")
    shutil.rmtree(BASE)
    print(f"  removed {BASE}")


def list_():
    for name, (folder, why) in SHAPES.items():
        print(f"  {name:17} {folder}\n  {'':17} {' '.join(f'U+{ord(c):04X}' for c in folder if c != ' ')}  -- {why}")


if __name__ == "__main__":
    a = sys.argv[1:]
    verb = a[0] if a else ""
    if verb == "make" and len(a) == 1:
        make()
    elif verb == "probe":
        probe(a[2] if len(a) == 3 and a[1] == "--json" else None)
    elif verb == "stage-dist" and len(a) == 2:
        stage_dist(a[1])
    elif verb == "host" and len(a) == 3:
        host(a[1], a[2])
    elif verb == "unhost" and len(a) == 2:
        unhost(a[1])
    elif verb == "clean" and len(a) == 1:
        clean()
    elif verb == "list" and len(a) == 1:
        list_()
    else:
        raise SystemExit(__doc__)
