r"""AC15 — an INDEPENDENT oracle for the Proxy Deploy Steam scan.

    py tools/verify/ac15_steam_oracle.py            # the game list
    py tools/verify/ac15_steam_oracle.py --verbose  # + every rejected candidate

⭐ WHY THIS EXISTS. `AC15` asks that removing a dead per-game VERSIONINFO load left the Steam
scan "finding the same games, with the same names and paths". Two earlier sessions recorded the
same honest limit: *no pre-fix baseline exists on this machine*, so re-running the scan shows only
that it still returns SOMETHING — never that the set is unchanged. Re-reading the same list from
the same code is not a second witness.

This is the second witness. It re-implements `ProxyDeployService`'s detector from the spec, in a
different language, with no shared code — so a game the C# scan silently drops shows up here as a
folder the oracle found and the UI never listed. Run it BEFORE reading the UI's answer; an oracle
computed afterwards can always be talked into agreeing.

⚠ It deliberately mirrors the C# rules INCLUDING their quirks, because the question is "does the
scan still do what it is specified to do", not "is the specification good":

  * shallowest primary depth wins, and the first depth that yields a row stops the search;
  * `Engine\` roots are a fallback used only when the primary tiers yielded nothing for that game;
  * `seenBinDirs` is global across every library and every game, not per-game;
  * one exe per Binaries\Win64, first match wins in directory order;
  * the stub list is exactly four names.

Source of every rule: ui/UE5DumpUI/Services/ProxyDeployService.cs
(`ScanGameFolder` / `CollectBinariesRoots` / `ScanBinariesDir` / `IsKnownStubExe`) and
ui/UE5DumpUI/Constants.cs (the five Steam constants). Read-only: it opens no exe and writes
nothing.
"""
import os
import re
import sys
import winreg

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

VERBOSE = "--verbose" in sys.argv

# ── Constants.cs:358-362 ────────────────────────────────────────────────
STEAM_REG_PATH = r"SOFTWARE\WOW6432Node\Valve\Steam"
STEAM_REG_KEY = "InstallPath"
STEAM_DEFAULT = r"C:\Program Files (x86)\Steam"
LIBRARYFOLDERS_VDF = r"config\libraryfolders.vdf"
STEAMAPPS_COMMON = r"steamapps\common"

# ── ProxyDeployService.cs:225 (MaxBinariesSearchDepth) and :232 ─────────
MAX_DEPTH = 3
SKIP_DIRS = {"binaries", "content", "saved", "intermediate",
             "config", "deriveddatacache", "plugins"}

# ── ProxyDeployService.cs:362-372 (IsKnownStubExe) ─────────────────────
STUB_EXES = {"crashreportclient.exe", "unrealeditor.exe",
             "ue4editor.exe", "unrealfrontend.exe"}


def steam_install_path():
    """GetSteamInstallPath — registry first, then the default path."""
    try:
        with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, STEAM_REG_PATH) as k:
            p = winreg.QueryValueEx(k, STEAM_REG_KEY)[0]
            if os.path.isdir(p):
                return p
    except OSError:
        pass
    return STEAM_DEFAULT if os.path.isdir(STEAM_DEFAULT) else None


def library_folders(steam_path):
    """The `path` entries of libraryfolders.vdf, plus the Steam root as fallback."""
    vdf = os.path.join(steam_path, LIBRARYFOLDERS_VDF)
    if not os.path.isfile(vdf):
        return [steam_path], "vdf missing -> Steam root only"
    txt = open(vdf, encoding="utf-8", errors="replace").read()
    paths = [m.group(1).replace("\\\\", "\\")
             for m in re.finditer(r'"path"\s+"([^"]+)"', txt)]
    paths = [p for p in paths if os.path.isdir(p)]
    if not paths:
        return [steam_path], "vdf parsed to 0 -> Steam root only"
    return paths, f"{len(paths)} library folder(s) from vdf"


def is_under_engine(d, game_dir):
    """IsUnderEngineFolder — ANY component between the game root and d, inclusive."""
    rel = os.path.relpath(d, game_dir)
    if rel == ".":
        return False
    return any(part.lower() == "engine" for part in rel.split(os.sep))


def collect_roots(d, game_dir, depth, primary, engine_roots):
    """CollectBinariesRoots — record every dir, descend while depth remains."""
    (engine_roots if is_under_engine(d, game_dir) else primary).append((d, depth))
    if depth >= MAX_DEPTH:
        return
    try:
        for e in os.scandir(d):
            if e.is_dir() and e.name.lower() not in SKIP_DIRS:
                collect_roots(e.path, game_dir, depth + 1, primary, engine_roots)
    except OSError:
        pass  # permission / reparse point — contributes nothing, as in the C#


def scan_binaries_dir(game_name, game_dir, root, games, seen, rejects):
    """ScanBinariesDir — returns True if it added a row."""
    bin_dir = os.path.join(root, "Binaries", "Win64")
    if not os.path.isdir(bin_dir):
        return False
    if bin_dir.lower() in seen:
        rejects.append((game_name, bin_dir, "duplicate BinariesDir"))
        return False
    seen.add(bin_dir.lower())

    try:
        exes = [e.path for e in os.scandir(bin_dir)
                if e.is_file() and e.name.lower().endswith(".exe")]
    except OSError as ex:
        rejects.append((game_name, bin_dir, f"enumerate failed: {ex}"))
        return False

    has_engine = (os.path.isdir(os.path.join(root, "Engine"))
                  or os.path.isdir(os.path.join(game_dir, "Engine")))

    # Pass 1: standard UE naming, or an Engine folder nearby.
    for exe in exes:
        name = os.path.basename(exe)
        if name.lower() in STUB_EXES:
            rejects.append((game_name, name, "known stub"))
            continue
        if "-win64-shipping" in name.lower() or has_engine:
            games.append((game_name, exe, bin_dir))
            return True
    # Pass 2: any non-stub exe at all.
    for exe in exes:
        name = os.path.basename(exe)
        if name.lower() in STUB_EXES:
            continue
        games.append((game_name, exe, bin_dir))
        return True
    if exes:
        rejects.append((game_name, bin_dir, "only stub exes"))
    return False


def scan_game_folder(game_dir, games, seen, rejects):
    """ScanGameFolder — shallowest primary depth wins, Engine only as fallback."""
    game_name = os.path.basename(game_dir)
    primary, engine_roots = [], []
    collect_roots(game_dir, game_dir, 0, primary, engine_roots)

    before = len(games)
    for tier in (primary, engine_roots):
        by_depth = {}
        for d, depth in tier:
            by_depth.setdefault(depth, []).append(d)
        for depth in sorted(by_depth):
            for root in by_depth[depth]:
                scan_binaries_dir(game_name, game_dir, root, games, seen, rejects)
            if len(games) != before:
                break
        if len(games) != before:
            break          # primary produced rows -> Engine tier never walked


def main():
    steam = steam_install_path()
    if not steam:
        print("Steam installation not found")
        return 1
    libs, how = library_folders(steam)
    print(f"Steam install : {steam}")
    print(f"Libraries     : {how}")
    for p in libs:
        print(f"                {p}")

    games, seen, rejects = [], set(), []
    folders = 0
    for lib in libs:
        common = os.path.join(lib, STEAMAPPS_COMMON)
        if not os.path.isdir(common):
            continue
        for e in sorted(os.scandir(common), key=lambda x: x.name):
            if e.is_dir():
                folders += 1
                scan_game_folder(e.path, games, seen, rejects)

    print(f"\nFolders under steamapps\\common : {folders}")
    print(f"ORACLE SAYS THE SCAN MUST FIND  : {len(games)} UE game(s)\n")
    for i, (name, exe, bin_dir) in enumerate(games, 1):
        print(f"{i:>3}. {name}")
        print(f"     exe  {exe}")
        print(f"     bin  {bin_dir}")

    if VERBOSE and rejects:
        print(f"\n--- {len(rejects)} rejected candidate(s) ---")
        for name, what, why in rejects:
            print(f"  {name}: {why} :: {what}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
