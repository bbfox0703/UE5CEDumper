r"""AC15 — an INDEPENDENT oracle for the Proxy Deploy *generic drive* scan.

    py tools/verify/ac15_drive_oracle.py D
    py tools/verify/ac15_drive_oracle.py D --verbose

The sibling of `ac15_steam_oracle.py`, for the half that two earlier sessions left unrun: the
non-Steam drive walk. Same purpose — a second witness written from the C# spec in another
language, so a game the walk silently drops shows up as a disagreement rather than as nothing.

The walk rules it mirrors (ProxyDeployService.cs `WalkDrive` / `LooksLikeUeGameRoot`):

  * depth cap 6 from the drive root;
  * reparse points (junctions/symlinks) are not followed;
  * anything at-or-under a resolved Steam library root is excluded, and `steamapps` is
    additionally hard-skipped by name — so the Steam titles on this drive MUST be absent;
  * `$Recycle.Bin`, `System Volume Information`, `Windows`, `WinSxS`, `$SysReset`, `Recovery`,
    `node_modules`, `.git`, `WindowsApps` are hard-skipped by name;
  * PRUNE-ON-MATCH: the first directory that looks like a UE game root is handed to the same
    per-game detector the Steam scan uses, and the walk does NOT descend past it;
  * dedupe by Binaries\Win64 across the whole run.

⚠ The four "looks like a game root" tiers are checked in order and any one of them is enough:
canonical cooked tree, a `Content\Paks` with pak/utoc/ucas, a `*-Win64-Shipping.exe` one level
down, or a flattened one at the top. Getting the ORDER wrong changes which directory is pruned
at and therefore what name the row carries. Read-only.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ac15_steam_oracle import (            # noqa: E402  — the shared per-game detector
    scan_game_folder, steam_install_path, library_folders,
)

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

VERBOSE = "--verbose" in sys.argv
MAX_WALK_DEPTH = 6

HARD_SKIP = {"$recycle.bin", "system volume information", "windows", "winsxs",
             "$sysreset", "recovery", "node_modules", ".git", "windowsapps", "steamapps"}

FILE_ATTRIBUTE_REPARSE_POINT = 0x400


def normalize(p):
    try:
        return os.path.abspath(p).rstrip("\\/")
    except OSError:
        return ""


def excluded_by_steam(path, roots):
    n = normalize(path).lower()
    if not n:
        return False
    for r in roots:
        if n == r or n.startswith(r + "\\"):
            return True
    return False


def has_any_file(d, *patterns):
    import fnmatch
    try:
        names = [e.name for e in os.scandir(d) if e.is_file()]
    except OSError:
        return False
    return any(fnmatch.fnmatch(n.lower(), p.lower()) for n in names for p in patterns)


def subdirs(d):
    try:
        return [e.path for e in os.scandir(d) if e.is_dir()]
    except OSError:
        return []


def looks_like_ue_game_root(d):
    """LooksLikeUeGameRoot — four tiers, first hit wins."""
    # Tier 1 — canonical cooked tree.
    if os.path.isdir(os.path.join(d, "Engine", "Binaries", "Win64")):
        for sub in subdirs(d):
            if os.path.basename(sub).lower() == "engine":
                continue
            if os.path.isdir(os.path.join(sub, "Binaries", "Win64")):
                return True
    # Tier 2 — <Project>\Content\Paks\*.pak|*.utoc|*.ucas
    for sub in subdirs(d):
        if os.path.basename(sub).lower() == "engine":
            continue
        paks = os.path.join(sub, "Content", "Paks")
        if os.path.isdir(paks) and has_any_file(paks, "*.pak", "*.utoc", "*.ucas"):
            return True
    # Tier 3 — <Project>\Binaries\Win64\*-Win64-Shipping.exe
    for sub in subdirs(d):
        b = os.path.join(sub, "Binaries", "Win64")
        if os.path.isdir(b) and has_any_file(b, "*-win64-shipping.exe"):
            return True
    # Tier 4 — flattened top-level shipping exe.
    return has_any_file(d, "*-win64-shipping.exe")


def is_reparse(d):
    try:
        return bool(os.stat(d, follow_symlinks=False).st_file_attributes
                    & FILE_ATTRIBUTE_REPARSE_POINT)
    except OSError:
        return True     # unreadable -> the C# returns, so do we


def walk(d, depth, games, seen, steam_roots, rejects):
    if depth > MAX_WALK_DEPTH:
        return
    if is_reparse(d):
        return
    if excluded_by_steam(d, steam_roots):
        rejects.append((d, "under a Steam library root"))
        return
    if looks_like_ue_game_root(d):
        scan_game_folder(d, games, seen, rejects)
        return                                   # prune-on-match
    for child in subdirs(d):
        if os.path.basename(child).lower() in HARD_SKIP:
            continue
        walk(child, depth + 1, games, seen, steam_roots, rejects)


def main():
    letters = [a.rstrip(":\\").upper() for a in sys.argv[1:] if not a.startswith("--")]
    if not letters:
        print("usage: py tools/verify/ac15_drive_oracle.py D [E ...] [--verbose]")
        return 2

    steam = steam_install_path()
    libs, _ = library_folders(steam) if steam else ([], "")
    steam_roots = [normalize(p).lower() for p in libs if normalize(p)]
    print("Steam roots excluded:")
    for r in steam_roots:
        print(f"  {r}")

    games, seen, rejects = [], set(), []
    for L in letters:
        root = f"{L}:\\"
        if not os.path.isdir(root):
            print(f"  ! {root} not present")
            continue
        print(f"\nwalking {root} ...")
        walk(root, 0, games, seen, steam_roots, rejects)

    print(f"\nORACLE SAYS THE DRIVE SCAN MUST FIND : {len(games)} UE game(s)\n")
    for i, (name, exe, bin_dir) in enumerate(games, 1):
        print(f"{i:>3}. {name}")
        print(f"     bin  {bin_dir}")

    steam_hits = [g for g in games if "steamapps" in g[2].lower()]
    print(f"\nrows under a Steam library : {len(steam_hits)}   <- must be 0")
    if VERBOSE:
        print(f"\n--- {len(rejects)} rejection(s) ---")
        for what, why in rejects[:60]:
            print(f"  {why} :: {what}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
