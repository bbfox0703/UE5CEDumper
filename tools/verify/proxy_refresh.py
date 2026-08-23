"""Report — and optionally refresh — the proxy DLLs deployed into game folders.

    py proxy_refresh.py report
    py proxy_refresh.py refresh "Lushfoil"      # substring of the game folder name

WHY THIS EXISTS. The maintainer's standing warning is that a game may already have an
OLD build of ours mapped. Measured 2026-08-19: **every** deployed proxy on this machine
was stale — six titles, all pre-3263:

    EVERSPACE 2 / EVERSPACE / Lushfoil / Manor Lords : version.dll  2,860,544
    OCTOPATH TRAVELER                                : winmm.dll    2,867,712
    Elliot                                           : dxgi.dll     2,855,936

A proxy auto-loads at game start and OWNS THE PIPE, so a later `inject.py` of the
current DLL is a no-op (`pipe already exists ... skipping auto-start`, and LoadLibraryW
just bumps a refcount). Everything then measured is the old binary.
`PipeClient.assert_build()` catches it — but only if you run it, and only after you
have already spent the launch.

WHAT `refresh` DOES, AND WHAT IT REFUSES TO DO. It copies `dist/proxy/<name>.dll` over
the deployed copy, **after** backing the old one up to `out/proxy-backups/` with its
size and SHA-256 recorded. It refuses if:
  * the game is running (replacing a mapped DLL silently does nothing useful);
  * the deployed file is already identical (nothing to do, and a needless write);
  * the backup cannot be written first.
It never deletes and never touches a name that is not one of our four proxies.
"""
import hashlib
import pathlib
import shutil
import subprocess
import sys
import time

ROOT = pathlib.Path(__file__).resolve().parents[2]
DIST = ROOT / "dist" / "proxy"
BACKUPS = ROOT / "out" / "proxy-backups"
LIBS = [pathlib.Path(r"C:\Program Files (x86)\Steam\steamapps\common"),
        pathlib.Path(r"D:\SteamLibrary\steamapps\common")]
OURS = {"dxgi.dll", "version.dll", "winmm.dll", "dinput8.dll"}


def _dist_build() -> str:
    """The build number actually sitting in dist/, read at call time."""
    try:
        return (pathlib.Path(__file__).resolve().parents[2]
                / "dist" / "build_number.txt").read_text().strip() or "?"
    except OSError:
        return "?"


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(s.encode(enc, "replace").decode(enc, "replace") + "\n")


def sha(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()


def dist_map():
    return {q.name.lower(): q for q in DIST.glob("*.dll") if q.name.lower() in OURS}


def deployed():
    """[(gamedir, exe, proxy_path)] for every game folder carrying one of our proxies."""
    out = []
    for lib in LIBS:
        if not lib.is_dir():
            continue
        for d in sorted(lib.iterdir()):
            if not d.is_dir():
                continue
            for exe in list(d.rglob("*-Win64-Shipping.exe"))[:1]:
                for q in exe.parent.glob("*.dll"):
                    if q.name.lower() in OURS:
                        out.append((d, exe, q))
    return out


def running(exe_name):
    o = subprocess.run(["tasklist", "/FI", f"IMAGENAME eq {exe_name}", "/FO", "CSV", "/NH"],
                       capture_output=True, text=True, errors="replace").stdout
    return exe_name.lower() in o.lower()


def report():
    dm = dist_map()
    say(f"dist/proxy: " + ", ".join(f"{k}={v.stat().st_size:,}" for k, v in sorted(dm.items())))
    rows = deployed()
    stale = 0
    for d, exe, q in rows:
        cur = dm.get(q.name.lower())
        if not cur:
            continue
        same = sha(q) == sha(cur)
        stale += 0 if same else 1
        say(f"  {d.name[:34]:<36} {q.name:<12} {q.stat().st_size:>10,}  "
            f"{'CURRENT' if same else '*** STALE ***'}   exe={exe.name}")
    say(f"\n{len(rows)} deployed proxy(ies), {stale} stale")
    return stale


def refresh(match):
    dm = dist_map()
    done = 0
    for d, exe, q in deployed():
        if match.lower() not in d.name.lower():
            continue
        cur = dm.get(q.name.lower())
        if not cur:
            continue
        if sha(q) == sha(cur):
            say(f"  {d.name}: {q.name} already current — nothing to do")
            continue
        if running(exe.name):
            say(f"  REFUSED: {exe.name} is RUNNING; replacing a mapped DLL achieves nothing. "
                f"Kill it first.")
            continue
        BACKUPS.mkdir(parents=True, exist_ok=True)
        stamp = time.strftime("%Y%m%d-%H%M%S")
        bak = BACKUPS / f"{d.name.replace(' ', '_')[:40]}.{q.name}.{stamp}.bak"
        shutil.copy2(q, bak)
        if not bak.is_file() or sha(bak) != sha(q):
            raise SystemExit(f"proxy_refresh: FAILED -- backup of {q} did not verify; "
                             f"refusing to overwrite the deployed file")
        say(f"  backed up {q.name} ({q.stat().st_size:,} B, sha {sha(q)[:12]}) -> {bak.name}")
        shutil.copy2(cur, q)
        if sha(q) != sha(cur):
            raise SystemExit(f"proxy_refresh: FAILED -- copy of {cur} to {q} did not verify")
        # DERIVED, not a literal. This line said "(dist 3263)" for every refresh
        # regardless of what it actually copied -- a verification tool quoting a build
        # number it never read, which is the one thing this repo's rules forbid. It
        # would have reported "3263" while deploying 3334.
        say(f"  refreshed {d.name} :: {q.name} -> {q.stat().st_size:,} B  (dist {_dist_build()})")
        done += 1
    say(f"\nrefreshed {done} file(s)")
    return 0


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else "report"
    if cmd == "report":
        report()
    elif cmd == "refresh":
        if len(sys.argv) < 3:
            raise SystemExit("proxy_refresh: refresh needs a game-folder substring")
        refresh(sys.argv[2])
    else:
        raise SystemExit(__doc__)
