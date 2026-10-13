"""Report — and optionally refresh — the proxy DLLs deployed into game folders.

    py proxy_refresh.py report
    py proxy_refresh.py refresh "Lushfoil"      # substring of the game folder name
    py proxy_refresh.py report --dist <dir>     # compare against another checkout's dist/proxy
    py proxy_refresh.py --selftest              # discovery, guards and refresh on temp trees only

WHY THIS EXISTS. The maintainer's standing warning is that a game may already have an
OLD build of ours mapped. Measured 2026-08-19: **every** deployed proxy on this machine
was stale — six titles, all pre-3263:

    EVERSPACE 2 / EVERSPACE / Lushfoil / Manor Lords : version.dll  2,860,544
    OCTOPATH TRAVELER                                : winmm.dll    2,867,712
    Elliot                                           : dxgi.dll     2,855,936

A proxy auto-loads at game start and OWNS THE PIPE, so a later `inject.py` of the
current DLL is a no-op (it finds its own process already serving the pipe and starts
nothing, and LoadLibraryW just bumps a refcount). Everything then measured is the old binary.
`PipeClient.assert_build()` catches it — but only if you run it, and only after you
have already spent the launch.

WHERE IT LOOKS. Every folder in the Steam libraries (or `--lib`) that holds one of OUR four
DLLs, whatever the exe beside it is called -- `deployed()` says why.

WHAT `refresh` DOES, AND WHAT IT REFUSES TO DO. It copies `dist/proxy/<name>.dll` over
the deployed copy, **after** backing the old one up to `out/proxy-backups/` with its
size and SHA-256 recorded. It refuses if:
  * an exe beside the proxy is running (replacing a mapped DLL silently does nothing useful);
  * the deployed file is already identical (nothing to do, and a needless write);
  * the backup cannot be written first.
It never deletes and never touches a name that is not one of our four proxies.
"""
import argparse
import contextlib
import hashlib
import io
import os
import pathlib
import shutil
import subprocess
import sys
import tempfile
import time
from typing import NamedTuple

# The ownership predicate, shared rather than reimplemented: PE VERSIONINFO ProductName ==
# "UE5CEDumper", which is exactly what Methode.cpp's IsOurModule reads. A refresh that
# decides by FILENAME will happily overwrite a third-party ReShade/SpecialK dxgi.dll.
# [PROXYREFRESHOWNER-2026-08-24]
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from b29_product_name import product_name   # noqa: E402

ROOT = pathlib.Path(__file__).resolve().parents[2]
DIST = ROOT / "dist" / "proxy"
BACKUPS = ROOT / "out" / "proxy-backups"
LIBS = [pathlib.Path(r"C:\Program Files (x86)\Steam\steamapps\common"),
        pathlib.Path(r"D:\SteamLibrary\steamapps\common")]
OURS = {"dxgi.dll", "version.dll", "winmm.dll", "dinput8.dll"}


class Deployed(NamedTuple):
    game: pathlib.Path    # the library's top-level game folder: what `refresh <substring>` matches
    exes: list            # the .exe files beside the proxy: the processes that would map it
    proxy: pathlib.Path


def _dist_build(dist=None) -> str:
    """The build number actually sitting in that dist/, read at call time."""
    try:
        return ((DIST if dist is None else pathlib.Path(dist)).parent
                / "build_number.txt").read_text().strip() or "?"
    except OSError:
        return "?"


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(s.encode(enc, "replace").decode(enc, "replace") + "\n")


def sha(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()


def is_ours(p, owner=product_name):
    return (owner(str(p)) or "").lower() == "ue5cedumper"


def dist_map(dist=None):
    src = DIST if dist is None else pathlib.Path(dist)
    return {q.name.lower(): q for q in src.glob("*.dll") if q.name.lower() in OURS}


def deployed(libs=None, owner=product_name):
    """[Deployed(game, exes, proxy)] for every proxy-named DLL in a folder that holds one of OURS.

    The folder is found the way the loader finds the proxy: Windows looks for a DLL in the directory of the exe
    that loads it first, so a deployment is a folder holding one of our four DLLs, whatever the exe beside it is
    called. This used to find a game only through a `*-Win64-Shipping.exe`, so the proxies beside `Extinction.exe`,
    `Nekopara.exe` or `DRAGON QUEST XI S.exe` were neither reported nor refreshed -- Extinction ran build 3645 after
    this tool said every stale proxy was refreshed. [PROXYREFRESH-NONSHIPPING-EXE]

    "Ours" is the ownership predicate, so a folder holding only third-party wrappers is no deployment. Measured
    2026-10-13 on the maintainer's two libraries: 25 files under our four names, 11 of them someone else's (ReShade,
    DXVK, mod loaders) and none beside a Shipping exe, so a search by name alone would have listed all eleven. A
    foreign file BESIDE one of ours is still yielded, so report and refresh can name it and leave it alone."""
    out = []
    for lib in (LIBS if libs is None else libs):
        if not lib.is_dir():
            continue
        for d in sorted(lib.iterdir()):
            if not d.is_dir():
                continue
            for folder, dirs, files in os.walk(d):
                dirs.sort()
                here = pathlib.Path(folder)
                named = [here / f for f in sorted(files) if f.lower() in OURS]
                if not any(is_ours(q, owner) for q in named):
                    continue
                exes = [here / f for f in sorted(files) if f.lower().endswith(".exe")]
                out.extend(Deployed(d, exes, q) for q in named)
    return out


def _tasklist(exe_name):
    return subprocess.run(["tasklist", "/FI", f"IMAGENAME eq {exe_name}", "/FO", "CSV", "/NH"],
                          capture_output=True, text=True, errors="replace").stdout


def running(exe_name, tasklist=_tasklist):
    """Any process with this image name? tasklist's filter matches inside tasklist, in UTF-16, so a CSV row at all is
    the answer. Searching its OUTPUT for the name cannot be: that comes back in the console code page, where a name
    the page cannot hold is replacement characters (any non-ASCII name under `-X utf8`), and the no-match notice is
    localized, so neither the name nor an "INFO:" prefix is there to find."""
    return any(line.startswith('"') for line in tasklist(exe_name).splitlines())


def busy(exes, is_running=running):
    """The exes beside a proxy that are running now: any one of them may have it mapped."""
    return [e.name for e in exes if is_running(e.name)]


def _exe_list(exes):
    return ", ".join(e.name for e in exes) or "(none beside it)"


def report(libs=None, dist=None, owner=product_name):
    dm = dist_map(dist)
    say(f"dist/proxy: " + ", ".join(f"{k}={v.stat().st_size:,}" for k, v in sorted(dm.items())))
    rows = deployed(libs, owner)
    stale = foreign = 0
    for d, exes, q in rows:
        cur = dm.get(q.name.lower())
        if not cur:
            continue
        # Ownership BEFORE freshness, and for the same reason `refresh()` gates on it
        # ([PROXYREFRESHOWNER-2026-08-24]): `deployed()` yields every file under one of our four
        # names in a folder of ours, and a third-party wrapper can legitimately own one of them. This line used to be missing here,
        # so OCTOPATH's real ReShade `dxgi.dll` was reported as "*** STALE ***" -- a word that
        # means "run refresh on it", i.e. the report ADVISED the destruction the refresh gate
        # exists to prevent. A foreign DLL is not out of date; it is not ours to date.
        if not is_ours(q, owner):
            foreign += 1
            say(f"  {d.name[:34]:<36} {q.name:<12} {q.stat().st_size:>10,}  "
                f"FOREIGN (not ours — left alone)   exe={_exe_list(exes)}")
            continue
        same = sha(q) == sha(cur)
        stale += 0 if same else 1
        say(f"  {d.name[:34]:<36} {q.name:<12} {q.stat().st_size:>10,}  "
            f"{'CURRENT' if same else '*** STALE ***'}   exe={_exe_list(exes)}")
    tail = f", {foreign} foreign (skipped)" if foreign else ""
    say(f"\n{len(rows)} deployed proxy(ies), {stale} stale{tail}")
    return stale


def _backup_path(backups, game, proxy, stamp):
    """A name no earlier backup holds. One game can now carry our proxy in more than one folder, and the two would
    share a game-name + DLL-name + second key: the second copy would overwrite the first one's backup AFTER the
    first was refreshed, so the old bytes would be gone."""
    base = f"{game.name.replace(' ', '_')[:40]}.{proxy.name}.{stamp}"
    bak, n = backups / f"{base}.bak", 1
    while bak.exists():
        bak, n = backups / f"{base}.{n}.bak", n + 1
    return bak


def refresh(match, libs=None, dist=None, backups=None, owner=product_name, is_running=running):
    dm = dist_map(dist)
    backups = BACKUPS if backups is None else pathlib.Path(backups)
    stamp = time.strftime("%Y%m%d-%H%M%S")
    done = 0
    for d, exes, q in deployed(libs, owner):
        if match.lower() not in d.name.lower():
            continue
        cur = dm.get(q.name.lower())
        if not cur:
            continue
        if sha(q) == sha(cur):
            say(f"  {d.name}: {q.name} already current — nothing to do")
            continue
        # ⛔ OWNERSHIP GATE — [PROXYREFRESHOWNER-2026-08-24]. Everything above this line
        # decides by FILENAME for this file (its folder qualified through a SIBLING of ours), and a
        # game folder can legitimately hold a third-party DLL under one of our four names: ReShade ships `dxgi.dll`, and so do SpecialK and
        # Ultimate ASI Loader. Without this check the "refresh" DESTROYS it — measured on
        # 2026-08-24, when this rig overwrote OCTOPATH TRAVELER's real ReShade `dxgi.dll`
        # (5,255,448 B, ProductName "ReShade") with our 2,893,824 B proxy. It was
        # recoverable only because of the backup two lines below, which is luck, not design.
        #
        # This is the same predicate `Methode.cpp`'s `IsOurModule` uses and the same helper
        # `inject.py` now imports, so the three cannot drift.
        if not is_ours(q, owner):
            say(f"  ⛔ SKIPPED {d.name}: {q.name} is NOT ours "
                f"(ProductName={owner(str(q))!r}) — a third-party wrapper, left untouched")
            continue
        up = busy(exes, is_running)
        if up:
            say(f"  REFUSED: {', '.join(up)} is RUNNING; replacing a mapped DLL achieves nothing. "
                f"Kill it first.")
            continue
        backups.mkdir(parents=True, exist_ok=True)
        bak = _backup_path(backups, d, q, stamp)
        shutil.copy2(q, bak)
        if not bak.is_file() or sha(bak) != sha(q):
            raise SystemExit(f"proxy_refresh: FAILED -- backup of {q} did not verify; "
                             f"refusing to overwrite the deployed file")
        say(f"  backed up {q} ({q.stat().st_size:,} B, sha {sha(q)[:12]}) -> {bak.name}")
        shutil.copy2(cur, q)
        if sha(q) != sha(cur):
            raise SystemExit(f"proxy_refresh: FAILED -- copy of {cur} to {q} did not verify")
        # DERIVED, not a literal. This line said "(dist 3263)" for every refresh
        # regardless of what it actually copied -- a verification tool quoting a build
        # number it never read, which is the one thing this repo's rules forbid. It
        # would have reported "3263" while deploying 3334.
        say(f"  refreshed {d.name} :: {q.relative_to(d)} -> {q.stat().st_size:,} B  (dist {_dist_build(dist)})")
        done += 1
    say(f"\nrefreshed {done} file(s)")
    return 0


# ── selftest ─────────────────────────────────────────────────────────────────────────────────

def _owner_by_bytes(path):
    """The selftest's ownership: a fake DLL's bytes name its ProductName. A real VERSIONINFO needs a real PE;
    b29_product_name.py checks the reader itself."""
    data = pathlib.Path(path).read_bytes()
    return "UE5CEDumper" if data.startswith(b"OURS") else (data.decode("ascii", "replace") or None)


def selftest():
    """[PROXYREFRESH-NONSHIPPING-EXE] Discovery, the guards and refresh on temp trees only: no Steam library, no
    dist/ and no backup folder outside this run is read or written, and the only process looked at is this one."""
    checks = []

    def check(name, ok):
        checks.append((name, bool(ok)))

    # tasklist filters by name inside itself, in UTF-16; its OUTPUT comes back in the console code page, so a
    # name that page cannot hold arrives as replacement characters, and the no-match notice is localized.
    row = '"\ufffd\ufffd\ufffd  \ufffd.exe","4242","Console","1","10,000 K"\n'
    check("a running non-ASCII exe reads as running though tasklist cannot print its name",
          running("ネコぱら  ライブ.exe", tasklist=lambda n: row))
    check("the English no-match notice reads as not running",
          not running("Extinction.exe", tasklist=lambda n:
                      "INFO: No tasks are running which match the specified criteria.\n"))
    check("a localized, mis-decoded no-match notice reads as not running",
          not running("Extinction.exe", tasklist=lambda n: "\ufffd\ufffdT: \ufffdS\ufffd\ufffd\ufffd\n"))
    check("no output at all reads as not running", not running("Extinction.exe", tasklist=lambda n: ""))
    check("the real tasklist finds this very process by its image name",
          running(pathlib.Path(sys.executable).name))
    check("the real tasklist finds no multi-space non-ASCII exe that does not exist",
          not running("no such game  ネコぱら.exe"))

    with tempfile.TemporaryDirectory(prefix="proxy_refresh_selftest_") as tmp:
        tmp = pathlib.Path(tmp)
        lib1 = tmp / "Steam Lib" / "steamapps" / "common"
        lib2 = tmp / "ライブラリ" / "steamapps" / "common"
        dist = tmp / "dist" / "proxy"
        backups = tmp / "backups"

        def put(p, data):
            p.parent.mkdir(parents=True, exist_ok=True)
            p.write_bytes(data)
            return p

        for n in sorted(OURS):
            put(dist / n, b"OURS new " + n.encode())
        (dist.parent / "build_number.txt").write_text("9999")

        old = {}

        def game(lib, name, folder, exes, dlls):
            for e in exes:
                put(lib / name / folder / e, b"MZ")
            for dll, data in dlls.items():
                old[(name, f"{folder}/{dll}")] = put(lib / name / folder / dll, data)

        game(lib1, "Shipping Game", "G/Binaries/Win64", ["G-Win64-Shipping.exe"], {"version.dll": b"OURS old ship"})
        game(lib1, "Extinction", "Blink/Binaries/Win64", ["Extinction.exe"], {"version.dll": b"OURS old ext"})
        game(lib1, "DRAGON QUEST XI S", "Game/Binaries/Win64", ["DRAGON QUEST XI S.exe", "PostCrashDump.exe"],
             {"dxgi.dll": b"OURS old dq"})
        game(lib2, "ネコぱら  ライブ", "Package/Binaries/Win64", ["ネコぱら  ライブ.exe"], {"winmm.dll": b"OURS old neko"})
        game(lib1, "Octo", "Octo/Binaries/Win64", ["Octo-Win64-Shipping.exe"],
             {"winmm.dll": b"OURS old octo", "dxgi.dll": b"ReShade"})
        game(lib1, "Fresh", "Bin", ["Fresh.exe"], {"dinput8.dll": (dist / "dinput8.dll").read_bytes()})
        game(lib1, "Busy Game", "Bin", ["Busy Game.exe", "Anti-Cheat Helper.exe"], {"version.dll": b"OURS old busy"})
        game(lib1, "Twin", "A/Win64", ["Twin.exe"], {"version.dll": b"OURS old twin A"})
        game(lib1, "Twin", "B/Win64", ["TwinB.exe"], {"version.dll": b"OURS old twin B"})
        game(lib1, "Caps", "Bin", ["Caps.exe"], {"VERSION.dll": b"OURS old caps"})
        game(lib1, "SEED BATTLE", "Bin", ["SEED BATTLE.exe"], {"dxgi.dll": b"ReShade"})
        game(lib1, "Only ReShade", "OR/Binaries/Win64", ["OR-Win64-Shipping.exe"], {"dxgi.dll": b"ReShade"})
        game(lib1, "Plain", "Bin", ["Plain.exe"], {"steam_api64.dll": b"OURS but not a proxy name"})
        libs = [lib1, lib2]

        rows = deployed(libs, owner=_owner_by_bytes)
        found = {(r.game.name, r.proxy.relative_to(r.game).as_posix()) for r in rows}
        for (name, rel), why in [
            (("Shipping Game", "G/Binaries/Win64/version.dll"), "beside a *-Win64-Shipping.exe (the control)"),
            (("Extinction", "Blink/Binaries/Win64/version.dll"), "beside Extinction.exe"),
            (("DRAGON QUEST XI S", "Game/Binaries/Win64/dxgi.dll"), "beside a multi-space exe name"),
            (("ネコぱら  ライブ", "Package/Binaries/Win64/winmm.dll"), "beside a non-ASCII exe in a non-ASCII library"),
            (("Twin", "A/Win64/version.dll"), "in one of two folders of one game"),
            (("Twin", "B/Win64/version.dll"), "in the other folder of that game"),
            (("Caps", "Bin/VERSION.dll"), "under an upper-case name"),
            (("Octo", "Octo/Binaries/Win64/dxgi.dll"), "a foreign wrapper BESIDE one of ours (so it is named, then left)"),
        ]:
            check(f"found: {name} :: {rel} -- {why}", (name, rel) in found)
        for name in ("SEED BATTLE", "Only ReShade"):
            check(f"not a deployment: {name} holds only a foreign wrapper", all(g != name for g, _ in found))
        check("never listed: a DLL that is not one of the four proxy names", all(g != "Plain" for g, _ in found))
        dq = [r for r in rows if r.game.name == "DRAGON QUEST XI S"]
        check("every exe beside the proxy is carried, not only the first",
              dq and sorted(e.name for e in dq[0].exes) == ["DRAGON QUEST XI S.exe", "PostCrashDump.exe"])

        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            stale = report(libs, dist, owner=_owner_by_bytes)
        text = out.getvalue()
        check("report counts nine stale proxies of ours", stale == 9)
        check("report names the Extinction proxy STALE with its exe",
              any("Extinction" in ln and "*** STALE ***" in ln and "exe=Extinction.exe" in ln
                  for ln in text.splitlines()))
        check("report calls the wrapper beside ours FOREIGN", any("Octo" in ln and "FOREIGN" in ln
                                                                  for ln in text.splitlines()))
        check("report's total is every listed file, one foreign",
              "11 deployed proxy(ies), 9 stale, 1 foreign (skipped)" in text)

        # Nothing to compare against must never read as "0 stale": a worktree has no dist/, and a mistyped --dist or
        # --lib is one keystroke away, and the operator then skips the refresh the title needed.
        def refusal(fn):
            said_here = io.StringIO()
            try:
                with contextlib.redirect_stdout(said_here):
                    fn()
            except SystemExit as e:
                return str(e), said_here.getvalue()
            return None, said_here.getvalue()

        nowhere = tmp / "no dist here" / "proxy"
        why, said_there = refusal(lambda: report(libs, nowhere, owner=_owner_by_bytes))
        check("report against a dist that holds none of our DLLs stops, naming it, and prints no total",
              why and str(nowhere) in why and "stale" not in said_there)
        before = {k: p.read_bytes() for k, p in old.items()}
        why, _ = refusal(lambda: refresh("", libs, nowhere, backups, owner=_owner_by_bytes,
                                         is_running=lambda n: False))
        check("refresh from a dist that holds none of our DLLs stops and writes nothing",
              why and str(nowhere) in why and all(p.read_bytes() == before[k] for k, p in old.items())
              and not backups.exists())
        missing_lib = tmp / "Steam Libary" / "steamapps" / "common"
        why, said_there = refusal(lambda: report([lib1, missing_lib], dist, owner=_owner_by_bytes))
        check("a library named explicitly that is not a folder stops the report, naming it",
              why and str(missing_lib) in why and "stale" not in said_there)

        partial = tmp / "partial" / "proxy"
        put(partial / "version.dll", b"OURS new version.dll")
        (partial.parent / "build_number.txt").write_text("9998")
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            stale = report(libs, partial, owner=_owner_by_bytes)
        text_partial = out.getvalue()
        check("a proxy with no counterpart in dist is reported UNCOMPARED, not skipped",
              any("DRAGON QUEST XI S" in ln and "UNCOMPARED" in ln for ln in text_partial.splitlines()))
        check("report's total counts the uncompared proxies apart from the stale ones",
              stale == 6 and "11 deployed proxy(ies), 6 stale, 1 foreign (skipped), 4 UNCOMPARED" in text_partial)
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            refresh("DRAGON", libs, partial, backups, owner=_owner_by_bytes, is_running=lambda n: False)
        check("refresh says it did not compare a proxy dist has no counterpart for, and leaves it",
              any("DRAGON QUEST XI S" in ln and "no dxgi.dll" in ln for ln in out.getvalue().splitlines())
              and old[("DRAGON QUEST XI S", "Game/Binaries/Win64/dxgi.dll")].read_bytes() == b"OURS old dq")

        def is_running(name):
            return name == "Busy Game.exe"

        said = io.StringIO()
        with contextlib.redirect_stdout(said):
            refresh("extinc", libs, dist, backups, owner=_owner_by_bytes, is_running=is_running)
        check("a scoped refresh writes only the game it names",
              old[("Extinction", "Blink/Binaries/Win64/version.dll")].read_bytes() == b"OURS new version.dll"
              and old[("DRAGON QUEST XI S", "Game/Binaries/Win64/dxgi.dll")].read_bytes() == b"OURS old dq")

        with contextlib.redirect_stdout(said):
            refresh("", libs, dist, backups, owner=_owner_by_bytes, is_running=is_running)
        # The build a refresh prints is the build of the dist it COPIED, read from that dist: a number from any other
        # dist/ (this checkout's, or "?" in a worktree with none) is the "(dist 3263)" lie the line exists to stop.
        done = [ln for ln in said.getvalue().splitlines() if ln.startswith("  refreshed ")]
        check("every refresh line names the build of the dist it copied, one line per refreshed file",
              len(done) == 8 and all(ln.endswith("(dist 9999)") for ln in done))
        head = text.splitlines()[0] if text else ""
        check("report's dist header lists the dist it was given",
              head.startswith("dist/proxy") and all(f"{n}=" in head for n in sorted(OURS)))
        refreshed = [k for k in old if k[0] in ("Shipping Game", "Extinction", "DRAGON QUEST XI S", "ネコぱら  ライブ",
                                                 "Twin", "Caps") or k == ("Octo", "Octo/Binaries/Win64/winmm.dll")]
        check("every stale proxy of ours is refreshed to dist's bytes, whatever its exe is called",
              all(old[k].read_bytes() == (dist / old[k].name.lower()).read_bytes() for k in refreshed))
        check("a running exe -- not the first beside the proxy -- refuses its refresh",
              old[("Busy Game", "Bin/version.dll")].read_bytes() == b"OURS old busy")
        check("the foreign wrapper beside ours is left untouched",
              old[("Octo", "Octo/Binaries/Win64/dxgi.dll")].read_bytes() == b"ReShade")
        check("a current proxy is not rewritten",
              old[("Fresh", "Bin/dinput8.dll")].read_bytes() == (dist / "dinput8.dll").read_bytes())
        saved = sorted(p.read_bytes() for p in backups.glob("*.bak")) if backups.is_dir() else []
        want = sorted([b"OURS old ship", b"OURS old ext", b"OURS old dq", b"OURS old neko", b"OURS old octo",
                       b"OURS old twin A", b"OURS old twin B", b"OURS old caps"])
        check("every refreshed file's old bytes sit in their own backup -- two in one game and one second included",
              saved == want)
        check("nothing running was backed up", not any("Busy" in p.name for p in backups.glob("*.bak")))

    for name, ok in checks:
        print(f"  {'ok  ' if ok else 'FAIL'}  {name}")
    bad = sum(not ok for _, ok in checks)
    print(f"proxy_refresh selftest: {len(checks) - bad}/{len(checks)} controls passed")
    return 1 if bad else 0


if __name__ == "__main__":
    ap = argparse.ArgumentParser(description="Report or refresh the proxy DLLs deployed into game folders.")
    ap.add_argument("cmd", nargs="?", default="report", choices=["report", "refresh"])
    ap.add_argument("match", nargs="?", help="refresh: a substring of the game folder name")
    ap.add_argument("--dist", type=pathlib.Path, help="the dist/proxy folder to compare with (default: this checkout's)")
    ap.add_argument("--lib", type=pathlib.Path, action="append",
                    help="a steamapps/common folder to search (repeatable; default: the two Steam libraries)")
    ap.add_argument("--selftest", action="store_true", help="run the controls on temp trees, then exit")
    a = ap.parse_args()
    if a.selftest:
        sys.exit(selftest())
    if a.cmd == "report":
        report(a.lib, a.dist)
    else:
        if not a.match:
            raise SystemExit("proxy_refresh: refresh needs a game-folder substring")
        refresh(a.match, a.lib, a.dist)
