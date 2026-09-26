#!/usr/bin/env python3
"""preflight.py — run this BEFORE tools/ghidra/sweep.sh.

Answers one question: *if I start the sweep right now, what will actually run, and
what do I need to install first?*

It never runs Ghidra and never touches a project. Everything it reports is read
off disk:

  * sweep.sh                    — the TAG|PROJECT|GLOB|GS_TRUE table (THE source of truth)
  * $GHIDRA_PROJS/*.rep (machine-specific; default D:/Tools/GHIDRA_Projs, which is also
    where it currently lives — internal NVMe. Do NOT host it on USB: see
    docs/corpus-preservation.md) — which projects exist, whether they are locked,
                                  and which PROGRAM NAMES each one holds
                                  (from .rep/idata/**/*.prp, plain XML)
  * Steam's libraryfolders.vdf + steamapps/appmanifest_*.acf — what this machine
                                  has installed RIGHT NOW, resolved BY APP ID so a
                                  game that moved to another library/drive is still
                                  found at its new path
  * corpus-manifest.json        — the per-tag provenance produced by the inventory
                                  probe (optional; see SCHEMA below)

Two INDEPENDENT levels of "missing", because the actions differ:

  A. Ghidra project missing/locked → THE SWEEP FAILS ON THAT ROW. Nothing you can
     install fixes it quickly; you must re-import (which needs level B) or restore
     the .rep from backup. This is the only thing that gates.
  B. Binary (and PDB) missing → the sweep still runs fine; you just cannot
     RE-IMPORT that program if its .rep is ever lost. Advisory.

Exit codes (highest-precedence one wins; all sections are always printed):
    0  GO      — every selected row has a usable Ghidra project.
    1  ERROR   — preflight itself could not run (sweep.sh unreadable, bad args).
    2  NO-GO   — >=1 selected row has a missing/locked project; those rows will
                 fail. `--allow-partial` downgrades this to 0 after printing.
    3  DRIFT   — the manifest and sweep.sh disagree about the tag set, so the
                 provenance half of the report cannot be trusted.
                 `--allow-drift` downgrades to 0/2.
    4  GAPS    — only with `--strict`: binaries/PDBs missing, i.e. re-import is
                 impossible for some rows. Never gates by default.

Usage:
    py tools/ghidra/preflight.py                     # everything
    py tools/ghidra/preflight.py UE4.27 UE5.7        # same substring filter as sweep.sh
    py tools/ghidra/preflight.py --json              # machine-readable
    py tools/ghidra/preflight.py --emit-manifest-skeleton tools/ghidra/corpus-manifest.json

Gating a sweep — the decision stays with the maintainer, the tool just reports:

    py tools/ghidra/preflight.py "$@" || {
        read -rp "preflight says NO-GO. Sweep the healthy rows anyway? [y/N] " a
        [ "$a" = y ] || exit 1
    }
    bash tools/ghidra/sweep.sh "$@"

Same filter arguments go to both, so the check covers exactly the rows about to run.

Env overrides (same names sweep.sh uses):  GHIDRA_PROJS
Extra:  UE5_STEAM_ROOT, or --steam-root

--- SCHEMA: tools/ghidra/corpus-manifest.json -------------------------------------
{
  "schema": "ue5cedumper.corpus-manifest/2",
  "generated_utc": "2026-07-28T12:00:00Z",
  "generator": "inventory_probe.py",
  "entries": {
    "<SWEEP TAG, verbatim from sweep.sh>": {
      "ghidra_project":  "DropIn",            // cross-check against sweep.sh; null = don't check
      "source":          "steam" | "self-built" | "non-steam" | "archive" | "unknown",
      "steam_app_id":    "1030300" | null,    // PREFERRED key — survives a move
      "steam_installdir":"DQ7R" | null,       // steamapps/common/<installdir>; fallback key
      "binary_relpath":  "DQ7R/Binaries/Win64/DQ7R-Win64-Shipping.exe" | null,
      "binary_last_seen":"D:\\SteamLibrary\\..." | null,  // absolute; for non-Steam sources
      "needs_pdb":       true | false | null, // null = unknown, reported as unknown
      "pdb_relpath":     null,                // defaults to binary basename + .pdb

      // BUILD IDENTITY — all optional, all checked only when non-null. Without at
      // least one of these, "the app is installed" does NOT mean "the corpus binary
      // is present": three sweep rows (UE4.25-Everspace2, UE5.5-Everspace2,
      // UE5.5-Everspace2b) are three different BUILDS of ONE Steam app, and only the
      // newest is on disk. Unchecked, all three would report FOUND against the same
      // 5.5 install, which is a false pass on a corpus-integrity check.
      "steam_buildid":     "22195748" | null, // ACF buildid, free + exact
      "binary_size_bytes": 123456789 | null,  // one stat()
      "binary_sha256":     null,              // only compared under --verify-hash

      // Optional. false = the original binary is KNOWN to be unrecoverable (Steam
      // serves only the current build, the archive copy is gone, ...). Absence means
      // unknown, never "fine". This has to be a FIELD and not a sentence in `notes`:
      // UE5.5-Everspace2 is a measured live example — the file at its recorded path is
      // a newer build than the .rep was imported from, and with the fact living only in
      // prose the row reads as "present, nothing to verify" instead of "the .rep is now
      // the only copy of this corpus member".
      "binary_recoverable": true | false | null,

      "provenance":      "live-log" | "ghidra-metadata" | "maintainer" | "unknown",
      "notes":           "free text"
    }
  }
}
EVERY field is nullable and null MEANS UNKNOWN — it is reported as unknown, never
as missing. A guessed app id would send the maintainer to install the wrong game,
which is strictly worse than an honest gap.
----------------------------------------------------------------------------------

VDF/ACF parsing traps are ported from the audited C# implementation in
ui/UE5DumpUI/Services/VdfParser.cs + Services/ProxyDeployService.cs; see the
comments at each porting site. This is a deliberate second implementation, not a
shared one: that code is an Avalonia GUI service bound to the Core platform
interface and the AOT ruleset, and it is not callable from an offline CLI.
"""

from __future__ import annotations

import argparse
import fnmatch
import json
import os
import re
import sys
from datetime import datetime, timezone
from pathlib import Path

# ─────────────────────────────────────────────────────────────────────────────
# Magic values (one place, per the repo rule)
# ─────────────────────────────────────────────────────────────────────────────

DEFAULT_GHIDRA_PROJS = r"D:/Tools/GHIDRA_Projs"
DEFAULT_STEAM_PATH = r"C:\Program Files (x86)\Steam"
STEAM_REG_PATH = r"SOFTWARE\WOW6432Node\Valve\Steam"      # HKLM, InstallPath
STEAM_REG_PATH_HKCU = r"Software\Valve\Steam"             # HKCU, SteamPath
STEAM_LIBRARYFOLDERS_VDF = os.path.join("config", "libraryfolders.vdf")
STEAM_APPS = "steamapps"
STEAM_APPS_COMMON = os.path.join("steamapps", "common")

MANIFEST_SCHEMA = "ue5cedumper.corpus-manifest/2"
DEFAULT_MANIFEST = "corpus-manifest.json"   # relative to this script's directory

# ProxyDeployService.MaxBinariesSearchDepth / BinariesSearchSkipDirs — see the long
# comment in ScanGameFolder for why depth 3 and why SHALLOWEST DEPTH WINS.
MAX_BINARIES_SEARCH_DEPTH = 3
BINARIES_SEARCH_SKIP_DIRS = {
    "binaries", "content", "saved", "intermediate", "config",
    "deriveddatacache", "plugins",
}
# ProxyDeployService.IsKnownStubExe
STUB_EXES = {
    "crashreportclient.exe", "unrealeditor.exe", "ue4editor.exe", "unrealfrontend.exe",
}

EXIT_OK, EXIT_ERROR, EXIT_NOGO, EXIT_DRIFT, EXIT_GAPS = 0, 1, 2, 3, 4


# ─────────────────────────────────────────────────────────────────────────────
# Valve KeyValues (VDF / ACF)
# ─────────────────────────────────────────────────────────────────────────────

def vdf_tokenize(content: str, unescape: bool = True) -> list[str]:
    """Port of VdfParser.Tokenize — quoted strings, braces, // line comments,
    backslash escapes. NOTE the escape table: inside a VDF quoted string `\\\\`
    is one backslash, so `"D:\\\\SteamLibrary"` decodes to `D:\\SteamLibrary`.
    Skipping that step yields doubled separators that still *look* right in a
    printout but never match the filesystem."""
    tokens: list[str] = []
    i, n = 0, len(content)
    while i < n:
        c = content[i]
        if c.isspace():
            i += 1
            continue
        if c == "/" and i + 1 < n and content[i + 1] == "/":
            while i < n and content[i] != "\n":
                i += 1
            continue
        if c in "{}":
            tokens.append(c)
            i += 1
            continue
        if c == '"':
            i += 1
            buf: list[str] = []
            while i < n and content[i] != '"':
                if unescape and content[i] == "\\" and i + 1 < n:
                    nxt = content[i + 1]
                    buf.append({"\\": "\\", '"': '"', "n": "\n", "t": "\t"}.get(nxt, nxt))
                    i += 2
                else:
                    buf.append(content[i])
                    i += 1
            if i < n:
                i += 1
            tokens.append("".join(buf))
            continue
        start = i
        while i < n and not content[i].isspace() and content[i] not in '{}"':
            i += 1
        tokens.append(content[start:i])
    return tokens


def vdf_values_at_depth(tokens: list[str], key: str, depth_wanted: int) -> list[str]:
    """Port of VdfParser.ExtractPaths, generalised. Walks brace depth and returns
    every value whose key matches at exactly `depth_wanted`.

    libraryfolders.vdf is  "libraryfolders" { "0" { "path" "..." } } → depth 2.
    appmanifest_*.acf is   "AppState" { "appid" "..." }             → depth 1.

    Depth-scoping (rather than a global key scan) is what stops the nested
    "apps" { "<appid>" "<bytes>" } block inside each library entry from being
    mistaken for anything."""
    out: list[str] = []
    depth = 0
    i = 0
    while i < len(tokens):
        t = tokens[i]
        if t == "{":
            depth += 1
        elif t == "}":
            depth -= 1
        elif (depth == depth_wanted and t.lower() == key.lower()
              and i + 1 < len(tokens) and tokens[i + 1] not in "{}"):
            if tokens[i + 1].strip():
                out.append(tokens[i + 1])
            i += 1
        i += 1
    return out


def _pick_path_variant(decoded: str, raw: str) -> str:
    """Choose between the escape-decoded and the raw form of a VDF path value.
    Whichever one exists on disk wins. If neither exists — the "library is gone"
    case, which still has to be REPORTED accurately — prefer the form that still
    looks like a path, so a mangled `D:SteamLibrary` is not what the maintainer is
    told to go looking for."""
    if decoded == raw:
        return decoded
    if Path(decoded).is_dir():
        return decoded
    if Path(raw).is_dir():
        return raw
    has_sep = lambda s: ("\\" in s) or ("/" in s)  # noqa: E731
    return raw if (not has_sep(decoded) and has_sep(raw)) else decoded


def read_text(path: Path) -> str | None:
    for enc in ("utf-8", "utf-8-sig", "mbcs", "latin-1"):
        try:
            return path.read_text(encoding=enc)
        except (UnicodeDecodeError, LookupError):
            continue
        except OSError:
            return None
    return None


# ─────────────────────────────────────────────────────────────────────────────
# Steam
# ─────────────────────────────────────────────────────────────────────────────

class SteamIndex:
    """Everything this machine currently has installed, keyed BY APP ID.

    App-id keying is the whole point: the maintainer's note is that a re-install
    "will not necessarily be at the same path as before", so a recorded path is a
    hint at best. The app id is stable; the path is recomputed from today's VDF."""

    def __init__(self) -> None:
        self.steam_root: Path | None = None
        self.steam_root_source = "not found"
        self.libraries: list[Path] = []
        self.vanished_libraries: list[str] = []
        self.by_appid: dict[str, dict] = {}
        self.by_installdir: dict[str, dict] = {}   # lowercased installdir → app
        self.errors: list[str] = []

    @property
    def available(self) -> bool:
        return self.steam_root is not None and bool(self.libraries)


def find_steam_root(explicit: str | None) -> tuple[Path | None, str]:
    if explicit:
        p = Path(explicit)
        return (p if p.is_dir() else None), f"--steam-root {explicit}"
    env = os.environ.get("UE5_STEAM_ROOT")
    if env:
        p = Path(env)
        return (p if p.is_dir() else None), "UE5_STEAM_ROOT"
    # ProxyDeployService.GetSteamInstallPath: HKLM WOW6432Node first, then default path.
    # HKCU\Software\Valve\Steam\SteamPath is added here — it is the one that survives a
    # per-user Steam install where the HKLM key is absent.
    try:
        import winreg  # noqa: PLC0415  (Windows-only, optional)
        for hive, sub, val in (
            (winreg.HKEY_LOCAL_MACHINE, STEAM_REG_PATH, "InstallPath"),
            (winreg.HKEY_CURRENT_USER, STEAM_REG_PATH_HKCU, "SteamPath"),
        ):
            try:
                with winreg.OpenKey(hive, sub) as k:
                    v, _ = winreg.QueryValueEx(k, val)
                    p = Path(str(v))
                    if p.is_dir():
                        hv = "HKLM" if hive == winreg.HKEY_LOCAL_MACHINE else "HKCU"
                        return p, f"registry {hv}\\{sub}\\{val}"
            except OSError:
                continue
    except ImportError:
        pass
    p = Path(DEFAULT_STEAM_PATH)
    if p.is_dir():
        return p, "default path"
    return None, "not found"


def scan_steam(explicit_root: str | None) -> SteamIndex:
    idx = SteamIndex()
    idx.steam_root, idx.steam_root_source = find_steam_root(explicit_root)
    if idx.steam_root is None:
        idx.errors.append("Steam not detected (no registry key, no default install).")
        return idx

    vdf = idx.steam_root / STEAM_LIBRARYFOLDERS_VDF
    roots: list[str] = []
    if vdf.is_file():
        content = read_text(vdf)
        if content is None:
            idx.errors.append(f"libraryfolders.vdf unreadable: {vdf}")
        else:
            roots = vdf_values_at_depth(vdf_tokenize(content), "path", 2)
            # Steam itself always writes doubled backslashes, so the unescape above is
            # correct for a real file. A HAND-EDITED or third-party-written VDF may use
            # single ones, and then `"D:\SteamLibrary"` decodes to `D:SteamLibrary` —
            # the tool would report a perfectly good library as GONE. Cheap fix: keep the
            # raw token too and prefer whichever one actually exists on disk. (The C#
            # VdfParser.Tokenize has the same blind spot; it has not bitten there because
            # it only ever reads Steam's own file.)
            raw = vdf_values_at_depth(vdf_tokenize(content, unescape=False), "path", 2)
            if len(raw) == len(roots):
                roots = [_pick_path_variant(dec, rw) for dec, rw in zip(roots, raw)]
    else:
        idx.errors.append(f"libraryfolders.vdf not found: {vdf}")

    if not roots:
        # ProxyDeployService does the same: fall back to the Steam dir itself as
        # the single library rather than reporting zero.
        roots = [str(idx.steam_root)]

    for r in roots:
        p = Path(r)
        if p.is_dir():
            idx.libraries.append(p)
        else:
            # A library root listed in the VDF that is gone = an unplugged/renamed
            # drive. Reported, not fatal: the other roots are still usable.
            idx.vanished_libraries.append(r)

    if not idx.libraries:
        idx.errors.append(
            "Steam is installed but NONE of its library roots exist on this machine — "
            "every Steam-sourced row will report UNKNOWN, not missing.")

    for lib in idx.libraries:
        apps_dir = lib / STEAM_APPS
        try:
            acfs = sorted(apps_dir.glob("appmanifest_*.acf"))
        except OSError as e:
            idx.errors.append(f"cannot list {apps_dir}: {e}")
            continue
        for acf in acfs:
            content = read_text(acf)
            if content is None:
                continue
            toks = vdf_tokenize(content)
            appid = (vdf_values_at_depth(toks, "appid", 1) or [""])[0]
            name = (vdf_values_at_depth(toks, "name", 1) or [""])[0]
            installdir = (vdf_values_at_depth(toks, "installdir", 1) or [""])[0]
            state = (vdf_values_at_depth(toks, "StateFlags", 1) or [""])[0]
            buildid = (vdf_values_at_depth(toks, "buildid", 1) or [""])[0]
            if not appid or not installdir:
                continue
            game_dir = lib / STEAM_APPS_COMMON / installdir
            rec = {
                "appid": appid,
                "name": name,
                "installdir": installdir,
                "library": str(lib),
                "game_dir": str(game_dir),
                "game_dir_exists": game_dir.is_dir(),
                "state_flags": state,
                "buildid": buildid,
                "acf": str(acf),
            }
            idx.by_appid[appid] = rec
            idx.by_installdir.setdefault(installdir.lower(), rec)
    return idx


# ─────────────────────────────────────────────────────────────────────────────
# Binary location inside a game folder
# ─────────────────────────────────────────────────────────────────────────────

def collect_binaries_roots(root: Path) -> list[tuple[Path, int, bool]]:
    """Port of ProxyDeployService.CollectBinariesRoots — bounded depth walk
    collecting dirs that may own a Binaries/Win64, tagged with depth and whether
    any path component is `Engine`."""
    out: list[tuple[Path, int, bool]] = []

    def walk(d: Path, depth: int) -> None:
        try:
            rel_parts = d.relative_to(root).parts
        except ValueError:
            rel_parts = ()
        under_engine = any(p.lower() == "engine" for p in rel_parts)
        out.append((d, depth, under_engine))
        if depth >= MAX_BINARIES_SEARCH_DEPTH:
            return
        try:
            for sub in d.iterdir():
                if sub.is_dir() and sub.name.lower() not in BINARIES_SEARCH_SKIP_DIRS:
                    walk(sub, depth + 1)
        except OSError:
            pass

    walk(root, 0)
    return out


def find_game_exe(game_dir: Path) -> Path | None:
    """Port of ScanGameFolder's ordering: primary roots before Engine roots, and
    within each tier SHALLOWEST DEPTH WINS.

    Both rules are load-bearing and both were bugs once (see the comment block in
    ProxyDeployService.ScanGameFolder): depth 1 alone misses NEKOPALIVE's
    <Game>/Package/<Sub>/Binaries/Win64, and an unordered depth-3 walk turns
    P3R's Artbook/ and Soundtrack/ into phantom hits — they are genuine UE apps,
    so only depth separates them from the real game."""
    if not game_dir.is_dir():
        return None
    roots = collect_binaries_roots(game_dir)
    for engine_tier in (False, True):
        tier = [(d, depth) for d, depth, ue in roots if ue == engine_tier]
        for depth in sorted({dp for _, dp in tier}):
            for d, dp in tier:
                if dp != depth:
                    continue
                bin_dir = d / "Binaries" / "Win64"
                if not bin_dir.is_dir():
                    continue
                try:
                    exes = sorted(bin_dir.glob("*.exe"))
                except OSError:
                    continue
                real = [e for e in exes if e.name.lower() not in STUB_EXES]
                shipping = [e for e in real if "-win64-shipping" in e.name.lower()]
                if shipping:
                    return shipping[0]
                if real:
                    return real[0]
    return None


def check_build_identity(entry: dict, binary: Path, app: dict | None,
                         verify_hash: bool) -> list[str]:
    """Compare whatever identity the manifest recorded against what is on disk.
    Returns a list of human-readable mismatches (empty = consistent, or nothing to
    check). Everything here is opt-in per field: a null records "not measured",
    and an unmeasured field must never manufacture a mismatch."""
    bad: list[str] = []

    want_build = entry.get("steam_buildid")
    if want_build and app is not None:
        got = app.get("buildid")
        if got and str(got) != str(want_build):
            bad.append(f"Steam buildid {got} on disk, corpus was imported from {want_build}")

    want_size = entry.get("binary_size_bytes")
    if want_size:
        try:
            got_size = binary.stat().st_size
        except OSError:
            got_size = None
        if got_size is not None and got_size != int(want_size):
            bad.append(f"binary is {got_size:,} bytes, corpus binary was {int(want_size):,}")

    # binary_md5 is what GHIDRA recorded at import, so — unlike buildid/size/sha256, which the
    # generator nulls on a drifted row to avoid asserting the replacement build is the corpus
    # build — it is present even when the file on disk is the WRONG build. It is therefore the
    # only field that can turn "a file exists here" into "this is not the binary we analysed".
    # Cheap enough to always check: md5 of one PE, no --verify-hash gate needed for correctness,
    # but it reads the whole file so it rides the same opt-in flag.
    want_md5 = entry.get("binary_md5")
    if want_md5 and verify_hash:
        import hashlib  # noqa: PLC0415
        h = hashlib.md5()
        try:
            with binary.open("rb") as f:
                for chunk in iter(lambda: f.read(1 << 20), b""):
                    h.update(chunk)
            if h.hexdigest().lower() != str(want_md5).lower():
                bad.append(f"md5 {h.hexdigest()[:16]}… != the md5 Ghidra recorded at import "
                           f"({str(want_md5)[:16]}…) — this file is NOT the corpus build")
        except OSError as e:
            bad.append(f"md5 not computable: {e}")

    want_hash = entry.get("binary_sha256")
    if want_hash and verify_hash:
        import hashlib  # noqa: PLC0415  (only on the slow, opt-in path)
        h = hashlib.sha256()
        try:
            with binary.open("rb") as f:
                for chunk in iter(lambda: f.read(1 << 20), b""):
                    h.update(chunk)
            if h.hexdigest().lower() != str(want_hash).lower():
                bad.append(f"sha256 {h.hexdigest()[:16]}… != recorded {str(want_hash)[:16]}…")
        except OSError as e:
            bad.append(f"sha256 not computable: {e}")
    return bad


def pdb_for(binary: Path, pdb_relpath: str | None, base: Path | None) -> Path | None:
    """Where a PDB would sit. UE puts it beside the binary with the same stem."""
    if pdb_relpath and base is not None:
        return base / pdb_relpath
    return binary.with_suffix(".pdb")


# ─────────────────────────────────────────────────────────────────────────────
# sweep.sh — THE source of truth for the corpus
# ─────────────────────────────────────────────────────────────────────────────

class SweepRow:
    __slots__ = ("tag", "project", "glob", "truth")

    def __init__(self, tag: str, project: str, glob: str, truth: str) -> None:
        self.tag, self.project, self.glob, self.truth = tag, project, glob, truth

    @property
    def is_noise_probe(self) -> bool:
        return not self.truth.strip()


def parse_sweep(path: Path) -> list[SweepRow]:
    """Read the ROWS=( "TAG|PROJECT|GLOB|GS_TRUE" ... ) array. Deliberately the
    ONLY corpus list this tool knows — a second hand-maintained list would drift
    silently, which is exactly the failure GROUND-TRUTH.md warns about."""
    text = read_text(path)
    if text is None:
        raise SystemExit(f"preflight: cannot read {path}")
    m = re.search(r"^ROWS=\(\s*$(.*?)^\)\s*$", text, re.S | re.M)
    if not m:
        raise SystemExit(f"preflight: no ROWS=( ... ) array found in {path}")
    rows: list[SweepRow] = []
    for line in m.group(1).splitlines():
        line = line.strip()
        if not line.startswith('"'):
            continue                       # comment or blank
        body = line.strip().strip('"')
        parts = body.split("|", 3)
        if len(parts) != 4:
            continue
        rows.append(SweepRow(*parts))
    return rows


def tag_matches(tag: str, filters: list[str]) -> bool:
    """sweep.sh match(): case-sensitive substring, empty filter list = match all."""
    return not filters or any(f in tag for f in filters)


# ─────────────────────────────────────────────────────────────────────────────
# Ghidra project state — read off disk, never via Ghidra
# ─────────────────────────────────────────────────────────────────────────────

PRP_NAME_RE = re.compile(r'NAME="NAME"\s+TYPE="string"\s+VALUE="([^"]*)"')


def project_programs(rep: Path) -> list[str]:
    """Program names inside a .rep, from .rep/idata/**/*.prp (plain XML).

    This is why preflight does not need Ghidra: the file names are metadata on
    disk. (The original `executablePath` is NOT here — it lives inside the packed
    .db, so recovering it does require a `-process -noanalysis -readOnly` run.
    That is the inventory probe's job, not this tool's.)"""
    names: list[str] = []
    idata = rep / "idata"
    if not idata.is_dir():
        return names
    try:
        for prp in idata.rglob("*.prp"):
            txt = read_text(prp)
            if not txt or 'VALUE="Program"' not in txt:
                continue
            m = PRP_NAME_RE.search(txt)
            if m:
                names.append(m.group(1))
    except OSError:
        pass
    return sorted(names)


def dir_size(p: Path) -> int:
    total = 0
    try:
        for root, _dirs, files in os.walk(p):
            for f in files:
                try:
                    total += os.path.getsize(os.path.join(root, f))
                except OSError:
                    pass
    except OSError:
        pass
    return total


def check_project(projs: Path, name: str, glob: str, want_sizes: bool) -> dict:
    rep, gpr = projs / f"{name}.rep", projs / f"{name}.gpr"
    lock = projs / f"{name}.lock"
    st: dict = {
        "project": name, "rep": str(rep), "rep_exists": rep.is_dir(),
        "gpr_exists": gpr.is_file(), "locked": lock.is_file(),
        "programs": [], "glob_matches": None, "bytes": None, "duplicate_imports": [],
    }
    if st["rep_exists"]:
        st["programs"] = project_programs(rep)
        if glob == "-":
            st["glob_matches"] = list(st["programs"])
        else:
            # Ghidra -process <glob>: wildcard match on the program name.
            st["glob_matches"] = [p for p in st["programs"] if fnmatch.fnmatch(p, glob)]
        if want_sizes:
            st["bytes"] = dir_size(rep)

    # Ghidra renames a second import of the same file to "<name>.0", and a `-process -`
    # row does visit both. MEASURED (out/sweep/log_UE4.27-Maelstrom_.txt): these are
    # MS-DOS/MZ-loader imports that map only the 1104-byte DOS stub — "scanning … CODE_0
    # size=1104" at image base 0000:0000 — and their scan TSVs are hits=0 on all 151
    # patterns. aggregate_sweep.py already drops them (`exec_mb <= 0` -> "broken import"),
    # which is why out/sweep/REPORT.md reads "programs scanned: 51" and not 55. So they do NOT
    # double-score truth, do NOT inflate any statistic and do NOT lengthen the run
    # meaningfully. What they DO cost is disk inside a kept project (Ghidra stores the
    # whole original file), and they trip a real output bug — see dup_note() below.
    base_names = set(st["programs"])
    st["duplicate_imports"] = sorted(
        p for p in (st["glob_matches"] or [])
        if re.match(r"^.*\.\d+$", p) and p.rsplit(".", 1)[0] in base_names)

    if not st["rep_exists"]:
        st["state"] = "MISSING"
    elif st["locked"]:
        st["state"] = "LOCKED"
    elif not st["gpr_exists"]:
        st["state"] = "NO_GPR"
    elif not st["programs"]:
        st["state"] = "EMPTY"
    elif not st["glob_matches"]:
        st["state"] = "GLOB_NO_MATCH"
    else:
        st["state"] = "OK"
    return st


PROJECT_BLOCKING = {"MISSING", "LOCKED", "NO_GPR", "EMPTY", "GLOB_NO_MATCH"}


# ─────────────────────────────────────────────────────────────────────────────
# Manifest
# ─────────────────────────────────────────────────────────────────────────────

def load_manifest(path: Path) -> tuple[dict, str | None]:
    if not path.is_file():
        return {}, f"manifest not found: {path}"
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as e:
        return {}, f"manifest unreadable ({e}): {path}"
    if data.get("schema") != MANIFEST_SCHEMA:
        return data, (f"manifest schema is {data.get('schema')!r}, "
                      f"expected {MANIFEST_SCHEMA!r} — reading it anyway")
    return data, None


def resolve_binary(entry: dict, steam: SteamIndex) -> dict:
    """Decide where this corpus member's ORIGINAL BINARY is today, and be explicit
    about which of the three answers it is: found / genuinely missing / cannot be
    determined. The third is not the second."""
    r: dict = {
        "state": "UNKNOWN", "binary": None, "base": None, "how": None,
        "steam": None, "detail": None,
    }
    src = (entry.get("source") or "unknown").lower()

    if src in ("self-built", "non-steam", "archive"):
        last = entry.get("binary_last_seen")
        if last and Path(last).is_file():
            r.update(state="FOUND", binary=last, base=None, how=f"{src}: recorded path",
                     detail="not a Steam title — verified at its recorded path")
        elif last:
            r.update(state="NOT_ON_STEAM", how=f"{src}: recorded path gone",
                     detail=f"was {last}")
        else:
            r.update(state="NOT_ON_STEAM", how=src,
                     detail="no recorded path — Steam cannot answer for this one")
        return r

    if src not in ("steam",):
        r["detail"] = "source unknown in manifest"
        return r

    appid = entry.get("steam_app_id")
    installdir = entry.get("steam_installdir")

    if not steam.available:
        r.update(state="UNKNOWN", detail="Steam not available — cannot verify")
        return r

    app = None
    if appid and appid in steam.by_appid:
        app, r["how"] = steam.by_appid[appid], f"app id {appid}"
    elif installdir and installdir.lower() in steam.by_installdir:
        app, r["how"] = steam.by_installdir[installdir.lower()], f"installdir {installdir}"

    if app is None:
        if not appid and not installdir:
            r.update(state="UNKNOWN", detail="manifest has neither app id nor installdir")
        else:
            r.update(state="NOT_INSTALLED",
                     detail=f"no appmanifest for {appid or installdir} in any library")
        return r

    r["steam"] = app
    if not app["game_dir_exists"]:
        r.update(state="STEAM_STALE",
                 detail=f"Steam lists it but {app['game_dir']} does not exist "
                        f"(StateFlags={app['state_flags']})")
        return r

    base = Path(app["game_dir"])
    r["base"] = str(base)
    rel = entry.get("binary_relpath")
    if rel:
        cand = base / rel
        if cand.is_file():
            r.update(state="FOUND", binary=str(cand))
        else:
            found = find_game_exe(base)
            if found:
                r.update(state="FOUND_MOVED", binary=str(found),
                         detail=f"manifest relpath {rel} not there; auto-located instead")
            else:
                r.update(state="BINARY_MISSING",
                         detail=f"install present but {rel} is not in it")
        return r

    found = find_game_exe(base)
    if found:
        r.update(state="FOUND", binary=str(found), detail="auto-located (no relpath in manifest)")
    else:
        r.update(state="BINARY_MISSING", detail="install present, no game exe found under it")
    return r


# ─────────────────────────────────────────────────────────────────────────────
# Report
# ─────────────────────────────────────────────────────────────────────────────

BAR = "=" * 78
SEP = "-" * 78


def human(n: int | None) -> str:
    if n is None:
        return "?"
    f = float(n)
    for unit in ("B", "KB", "MB", "GB", "TB"):
        if f < 1024 or unit == "TB":
            return f"{f:,.1f} {unit}"
        f /= 1024
    return f"{f:.1f} TB"


class _Parser(argparse.ArgumentParser):
    """argparse exits with 2 on a usage error — which is this tool's NO-GO code. A
    mistyped flag must not be indistinguishable from "the corpus is incomplete", so
    usage errors are re-mapped onto EXIT_ERROR."""

    def error(self, message: str):  # noqa: D102
        self.print_usage(sys.stderr)
        sys.stderr.write(f"{self.prog}: error: {message}\n")
        raise SystemExit(EXIT_ERROR)


def main(argv: list[str]) -> int:
    ap = _Parser(
        prog="preflight.py", description="Pre-sweep corpus check for tools/ghidra/sweep.sh.")
    ap.add_argument("filters", nargs="*",
                    help="tag substrings, same semantics as sweep.sh's arguments")
    ap.add_argument("--sweep", default=None, help="path to sweep.sh")
    ap.add_argument("--manifest", default=None, help=f"path to {DEFAULT_MANIFEST}")
    ap.add_argument("--ghidra-projs", default=os.environ.get("GHIDRA_PROJS", DEFAULT_GHIDRA_PROJS))
    ap.add_argument("--steam-root", default=None)
    ap.add_argument("--sizes", action="store_true", help="measure .rep sizes on disk (slow)")
    ap.add_argument("--json", action="store_true", help="machine-readable output")
    ap.add_argument("--verbose", action="store_true", help="also list rows that are fine")
    ap.add_argument("--verify-hash", action="store_true",
                    help="also check binary_sha256 (reads every located binary — slow)")
    ap.add_argument("--strict", action="store_true",
                    help="binary/PDB/build-identity gaps also gate (exit 4)")
    ap.add_argument("--allow-partial", action="store_true",
                    help="do not gate on missing/locked projects")
    ap.add_argument("--allow-drift", action="store_true",
                    help="do not gate on manifest/sweep.sh drift")
    ap.add_argument("--emit-manifest-skeleton", metavar="PATH",
                    help="write an empty schema-conformant manifest keyed on the sweep tags")
    args = ap.parse_args(argv)

    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except (AttributeError, OSError):
        pass

    here = Path(__file__).resolve().parent
    sweep_path = Path(args.sweep) if args.sweep else here / "sweep.sh"
    manifest_path = Path(args.manifest) if args.manifest else here / DEFAULT_MANIFEST
    projs = Path(args.ghidra_projs)

    rows = parse_sweep(sweep_path)

    if args.emit_manifest_skeleton:
        skel = {
            "schema": MANIFEST_SCHEMA,
            "generated_utc": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
            "generator": f"preflight.py --emit-manifest-skeleton (from {sweep_path.name})",
            "entries": {
                r.tag: {
                    "ghidra_project": r.project,   # measured, from sweep.sh
                    "source": "unknown", "steam_app_id": None, "steam_installdir": None,
                    "binary_relpath": None, "binary_last_seen": None,
                    "needs_pdb": None, "pdb_relpath": None,
                    "steam_buildid": None, "binary_size_bytes": None, "binary_sha256": None,
                    "provenance": "unknown", "notes": "",
                } for r in rows
            },
        }
        out = Path(args.emit_manifest_skeleton)
        out.write_text(json.dumps(skel, indent=2) + "\n", encoding="utf-8")
        print(f"wrote skeleton for {len(rows)} tags -> {out}")
        return EXIT_OK

    manifest, manifest_note = load_manifest(manifest_path)
    entries: dict = manifest.get("entries", {}) if isinstance(manifest, dict) else {}
    have_manifest = bool(entries)

    selected = [r for r in rows if tag_matches(r.tag, args.filters)]
    steam = scan_steam(args.steam_root)

    # ── drift: the manifest must be CHECKABLE against sweep.sh, not parallel to it.
    #
    # Scoped to the SELECTED rows for gating. `preflight.py UE5.7` should not be
    # blocked by a manifest gap on a 4.x row it is not going to look at — but a
    # manifest tag that sweep.sh does not have at all is an inconsistency in the
    # manifest itself, so that one always counts.
    sweep_tags = {r.tag for r in rows}
    selected_tags = {r.tag for r in selected}
    drift_manifest_only = sorted(set(entries) - sweep_tags)
    drift_sweep_only = sorted(selected_tags - set(entries)) if have_manifest else []
    drift_uncovered_corpus = (len(sweep_tags - set(entries)) if have_manifest else 0)
    drift_project = []
    for r in selected:
        e = entries.get(r.tag)
        if e and e.get("ghidra_project") and e["ghidra_project"] != r.project:
            drift_project.append((r.tag, r.project, e["ghidra_project"]))
    has_drift = bool(drift_manifest_only or drift_sweep_only or drift_project)

    results = []
    for r in selected:
        proj = check_project(projs, r.project, r.glob, args.sizes)
        entry = entries.get(r.tag)
        if entry is None:
            binres = {"state": "UNKNOWN", "binary": None, "base": None,
                      "how": None, "steam": None,
                      "detail": "no manifest entry for this tag"}
            needs_pdb = None
        else:
            binres = resolve_binary(entry, steam)
            needs_pdb = entry.get("needs_pdb")

        # Build identity — only meaningful once a binary was actually located.
        identity, identity_problems = "unrecorded", []
        if entry and (entry.get("binary_recoverable") is False
                      or (entry.get("source") or "").lower() == "lost"):
            identity = "IRRECOVERABLE"
            identity_problems = [entry.get("notes")
                                 or "manifest marks the original binary as unrecoverable"]
        elif entry and binres["state"] in ("FOUND", "FOUND_MOVED"):
            has_any = any(entry.get(k) for k in
                          ("steam_buildid", "binary_size_bytes", "binary_sha256", "binary_md5"))
            if has_any:
                identity_problems = check_build_identity(
                    entry, Path(binres["binary"]), binres.get("steam"), args.verify_hash)
                identity = "MISMATCH" if identity_problems else "verified"

        pdb_state, pdb_path = "N/A", None
        if binres["state"] in ("FOUND", "FOUND_MOVED") and needs_pdb is not False:
            base = Path(binres["base"]) if binres["base"] else None
            rel = (entry or {}).get("pdb_relpath")
            p = pdb_for(Path(binres["binary"]), rel, base)
            pdb_path = str(p)
            if p.is_file():
                pdb_state = "FOUND"
            elif needs_pdb is True:
                pdb_state = "MISSING"
            else:
                pdb_state = "MISSING_UNKNOWN_IF_NEEDED"
        elif needs_pdb is True:
            pdb_state = "UNCHECKABLE"
        elif needs_pdb is None:
            # MEASURED GAP, not a pass. The inventory probe sets needs_pdb by stat()ing
            # next to the located binary, so a row it could not locate keeps null — and
            # null here used to fall through to "N/A" and vanish from the PDB section
            # entirely. That is exactly backwards: UE4.27-DropIn is a SYMBOLISED oracle
            # (sweep.sh lists it under "full PDB") that is not installed, so it is the
            # row most in need of a PDB, and it was the one being silently dropped.
            # "0 pdb gaps" must never mean "2 rows were never measured".
            pdb_state = "UNKNOWN_UNCHECKABLE"

        results.append({
            "tag": r.tag, "project": r.project, "glob": r.glob,
            "noise_probe": r.is_noise_probe, "_truth": r.truth,
            "ghidra": proj, "binary": binres,
            "needs_pdb": needs_pdb, "pdb_state": pdb_state, "pdb": pdb_path,
            "identity": identity, "identity_problems": identity_problems,
            "source": (entry or {}).get("source", "unknown"),
        })

    blocking = [x for x in results if x["ghidra"]["state"] in PROJECT_BLOCKING]
    ok_rows = [x for x in results if x["ghidra"]["state"] == "OK"]
    # Detected since the first version, but it used to be reachable only via --json —
    # i.e. invisible in the report a human actually reads, which for a pre-sweep check
    # is the same as not detected. It is the one finding here that skews the SWEEP
    # RESULT rather than the re-import story, so it goes directly under BLOCKING.
    dup_rows = [x for x in results if x["ghidra"].get("duplicate_imports")]

    # ── ORPHAN .rep ──────────────────────────────────────────────────────────
    # Projects on disk that NO sweep row references. Deliberately compared against
    # ALL sweep rows and not the selected ones — under `preflight.py UE5.7` every
    # 4.x project would otherwise look orphaned, which would be a catastrophic
    # thing to get wrong on a list the maintainer may use to decide what to DELETE.
    # Reported as "not referenced", never as "safe to delete": some are known
    # supersessions (sweep.sh says Meltopia_V2 supersedes Meltopia.rep), others may
    # still be somebody's working copy. The verdict stays the maintainer's.
    all_projects = {r.project for r in rows}
    orphans: list[tuple[str, int | None]] = []
    try:
        for rep in sorted(projs.glob("*.rep")):
            if rep.stem not in all_projects:
                orphans.append((rep.stem, dir_size(rep) if args.sizes else None))
    except OSError:
        pass
    to_install = [x for x in results
                  if x["binary"]["state"] in ("NOT_INSTALLED", "STEAM_STALE", "BINARY_MISSING")]
    pdb_missing = [x for x in results
                   if x["pdb_state"] in ("MISSING", "UNCHECKABLE", "UNKNOWN_UNCHECKABLE")]

    # ── SHARED ON-DISK BINARY ────────────────────────────────────────────────
    # Measured, and independent of the manifest's identity fields: if two SELECTED
    # rows resolve to the same file, they cannot both be the build their .rep was
    # imported from — the corpus deliberately holds same-game cross-build pairs
    # (UE5.5-Everspace2 / UE5.5-Everspace2b are one Steam app id, two compiles).
    # Steam only ever serves the CURRENT build, so at most one of the group is
    # reproducible and the rest exist only as .rep. Without this check each row
    # independently reports FOUND and the collision is invisible.
    by_path: dict[str, list] = {}
    for x in results:
        b = x["binary"]
        if b["state"] in ("FOUND", "FOUND_MOVED") and b["binary"]:
            by_path.setdefault(os.path.normcase(b["binary"]), []).append(x)
    collisions = [v for v in by_path.values() if len(v) > 1]
    # Not-obtainable-from-Steam is a property of the SOURCE, not of whether the file
    # happens to be present today. A self-built 4.23 sitting safely on X: still belongs
    # on this list: it is the one that cannot be re-downloaded if the drive dies, which
    # is precisely what the maintainer needs to see before choosing what to delete.
    not_steam = [x for x in results
                 if (x["source"] or "unknown").lower() in ("self-built", "non-steam", "archive")
                 or x["binary"]["state"] == "NOT_ON_STEAM"]
    unknown = [x for x in results if x["binary"]["state"] == "UNKNOWN"]
    wrong_build = [x for x in results if x["identity"] in ("MISMATCH", "IRRECOVERABLE")]
    unverified = [x for x in results
                  if x["identity"] == "unrecorded"
                  and x["binary"]["state"] in ("FOUND", "FOUND_MOVED")]

    if args.json:
        print(json.dumps({
            "sweep": str(sweep_path), "manifest": str(manifest_path),
            "manifest_note": manifest_note, "ghidra_projs": str(projs),
            "steam": {
                "root": str(steam.steam_root) if steam.steam_root else None,
                "root_source": steam.steam_root_source,
                "libraries": [str(p) for p in steam.libraries],
                "vanished_libraries": steam.vanished_libraries,
                "apps_indexed": len(steam.by_appid), "errors": steam.errors,
            },
            "drift": {"manifest_only": drift_manifest_only, "sweep_only": drift_sweep_only,
                      "project_mismatch": drift_project},
            "shared_binaries": [
                {"binary": g[0]["binary"]["binary"],
                 "tags": [x["tag"] for x in g],
                 "verified_tag": next((x["tag"] for x in g if x["identity"] == "verified"), None)}
                for g in collisions],
            "orphan_projects": [{"project": n, "bytes": b} for n, b in orphans],
            "rows": results,
        }, indent=2))
    else:
        print(BAR)
        print("UE5CEDumper — AOB sweep PREFLIGHT")
        print(BAR)
        print(f"sweep.sh      : {sweep_path}  ({len(rows)} rows, {len(selected)} selected)")
        if args.filters:
            print(f"filters       : {' '.join(args.filters)}")
        print(f"ghidra projs  : {projs}")
        manifest_flag = ""
        if not have_manifest:
            manifest_flag = ("   << UNREADABLE" if (manifest_note and "unreadable" in manifest_note)
                             else "   << ABSENT/EMPTY")
        print(f"manifest      : {manifest_path}{manifest_flag}")
        if manifest_note:
            print(f"                {manifest_note}")
        print(f"steam root    : {steam.steam_root or '(none)'}   [{steam.steam_root_source}]")
        for lib in steam.libraries:
            print(f"  library     : {lib}")
        for v in steam.vanished_libraries:
            print(f"  library GONE: {v}   << listed in libraryfolders.vdf, not on this machine")
        print(f"steam apps    : {len(steam.by_appid)} installed app manifests indexed")
        for e in steam.errors:
            print(f"  ! {e}")
        print()

        n = 1

        def section(title: str, body_rows: list, renderer) -> None:
            nonlocal n
            print(SEP)
            print(f"[{n}] {title}   ({len(body_rows)})")
            print(SEP)
            n += 1
            if not body_rows:
                print("  (none)")
            for x in body_rows:
                renderer(x)
            print()

        def r_block(x):
            g = x["ghidra"]
            why = {
                "MISSING": f".rep not present at {g['rep']}",
                "LOCKED": f"{x['project']}.lock present — Ghidra has it open (or a stale lock)",
                "NO_GPR": f"{x['project']}.gpr missing (Ghidra will not see the project)",
                "EMPTY": ".rep holds no Program files",
                "GLOB_NO_MATCH": (f"-process '{x['glob']}' matches none of "
                                  f"{g['programs'] or ['(no programs)']}"),
            }[g["state"]]
            print(f"  {x['tag']:<24} project={x['project']}")
            print(f"      {g['state']}: {why}")
            b = x["binary"]
            if b["state"] in ("FOUND", "FOUND_MOVED"):
                print(f"      RE-IMPORT possible — binary is here: {b['binary']}")
            elif b["state"] == "UNKNOWN":
                print("      re-import: UNKNOWN — no manifest data for this tag")
            else:
                print(f"      RE-IMPORT BLOCKED — {b['state']}: {b['detail']}")

        def r_install(x):
            b = x["binary"]
            app = b.get("steam") or {}
            print(f"  {x['tag']:<24} {b['state']}: {b['detail']}")
            e = entries.get(x["tag"], {})
            if e.get("steam_app_id"):
                print(f"      steam://install/{e['steam_app_id']}"
                      + (f"   ({app.get('name') or e.get('steam_installdir') or ''})"
                         if (app or e.get('steam_installdir')) else ""))
            else:
                print("      app id UNKNOWN in manifest — cannot name what to install")
            print(f"      sweep impact: NONE (project is {x['ghidra']['state']}); "
                  "needed only to re-import")

        def r_wrongbuild(x):
            head = ("marked IRRECOVERABLE in the manifest"
                    if x["identity"] == "IRRECOVERABLE"
                    else "installed, but NOT the build the corpus came from")
            print(f"  {x['tag']:<24} {head}")
            for p in x["identity_problems"]:
                print(f"      {p}")
            if x["binary"]["binary"]:
                print(f"      at: {x['binary']['binary']}")
            print("      the .rep is now the ONLY copy — re-importing from what is on disk "
                  "would NOT reproduce it")

        def r_pdb(x):
            print(f"  {x['tag']:<24} pdb {x['pdb_state']}")
            if x["pdb"]:
                print(f"      expected: {x['pdb']}")
            else:
                print(f"      cannot check — the binary was not located "
                      f"({x['binary']['state']}); fix that first, this is a consequence")
            if x["pdb_state"] == "UNKNOWN_UNCHECKABLE":
                print("      and needs_pdb is NULL in the manifest — whether this row even "
                      "HAS symbols is unmeasured, not 'no'")

        def r_collision(group):
            print(f"  {len(group)} rows resolve to ONE file:")
            print(f"      {group[0]['binary']['binary']}")
            for x in group:
                mark = {"verified": "  <- matches this row's recorded identity",
                        "MISMATCH": "  <- recorded identity does NOT match",
                        "IRRECOVERABLE": "  <- manifest marks it unrecoverable"}
                print(f"      {x['tag']:<24} identity={x['identity']}"
                      f"{mark.get(x['identity'], '')}")
            if not any(x["identity"] == "verified" for x in group):
                print("      NONE of them verifies against it — nothing here is known to be "
                      "the build any of these .rep files came from")
            else:
                print("      the others are NOT on disk: their .rep is the only copy left")

        def r_notsteam(x):
            b = x["binary"]
            here = b["state"] in ("FOUND", "FOUND_MOVED")
            mark = "present" if here else "GONE"
            print(f"  {x['tag']:<24} source={x['source']:<11} binary {mark}")
            print(f"      {b['binary'] or b['detail']}")
            if not here:
                print("      Steam cannot answer for this one — it must be REBUILT "
                      "(exact engine version) or restored from your own archive")

        def r_unknown(x):
            print(f"  {x['tag']:<24} {x['binary']['detail']}")

        def r_ok(x):
            g = x["ghidra"]
            size = f"  {human(g['bytes'])}" if g["bytes"] is not None else ""
            print(f"  {x['tag']:<24} project OK ({len(g['glob_matches'])} program(s)){size}"
                  f"   binary={x['binary']['state']}  pdb={x['pdb_state']}"
                  f"  id={x['identity']}")

        def r_dup(x):
            g = x["ghidra"]
            print(f"  {x['tag']:<24} project={x['project']}  glob={x['glob']}")
            for p in g["duplicate_imports"]:
                print(f"      {p}")
            print(f"      {len(g['glob_matches'])} programs match this row, "
                  f"{len(g['duplicate_imports'])} of them are stub re-imports")
            print("      NOT a scoring hazard: these map only the 1104-byte DOS stub "
                  "(base 0000:0000) and score hits=0 on every pattern; aggregate_sweep.py "
                  "already excludes them, which is why REPORT.md's program count is LOWER "
                  "than the raw import count (it also reports them as 'skipped N broken "
                  "imports'). Do not hardcode either number here - the corpus grows.")
            print("      side effect (real): scan_patterns.java builds its output name from "
                  "the image base, so '@0000:0000' is truncated by NTFS at the ':' — the .txt/"
                  ".tsv land in ALTERNATE DATA STREAMS of a 0-byte file in out/sweep/. "
                  "Harmless for a stub; silent data loss for any real segmented program")
            print("      optional cleanup: delete the .N copy in Ghidra to reclaim its "
                  "database (it stores the whole original file)")

        section("BLOCKING — the sweep will FAIL on these rows", blocking, r_block)
        section("STUB RE-IMPORTS (informational — no effect on sweep results)",
                dup_rows, r_dup)
        section("INSTALL BEFORE RE-IMPORT — Steam title not on this machine", to_install, r_install)
        section("WRONG BUILD / IRRECOVERABLE — on-disk copy would not reproduce the .rep",
                wrong_build, r_wrongbuild)
        section("SHARED ON-DISK BINARY — same-app rows that cannot all be reproducible",
                collisions, r_collision)
        section("PDB MISSING/UNMEASURED — symbols cannot be re-applied on re-import",
                pdb_missing, r_pdb)
        section("NOT OBTAINABLE FROM STEAM — self-built / archived (protect these first)",
                not_steam, r_notsteam)
        section("UNKNOWN — no measured provenance (fill in the manifest)", unknown, r_unknown)
        section("IDENTITY UNVERIFIED — a file is there, but nothing recorded to check it "
                "against", unverified,
                lambda x: print(f"  {x['tag']:<24} {x['binary']['binary']}\n"
                                f"      no buildid/size/sha in the manifest — 'present' here "
                                f"means 'a file exists at that path', nothing more"))
        def r_orphan(o):
            name, sz = o
            print(f"  {name:<40}{'  ' + human(sz) if sz is not None else ''}")

        section("ORPHAN PROJECTS — on disk, referenced by NO sweep row "
                "(disk-pressure candidates; verify before deleting)", orphans, r_orphan)
        if orphans and not args.sizes:
            print("  (re-run with --sizes to measure what reclaiming these would free)\n")

        if args.verbose:
            section("READY", ok_rows, r_ok)

        print(SEP)
        print("DRIFT — manifest vs sweep.sh")
        print(SEP)
        if not have_manifest:
            print("  manifest absent — provenance half of this report is UNAVAILABLE.")
            print("  Ghidra-project readiness above is still fully measured.")
        elif not has_drift:
            print(f"  none — manifest covers exactly the {len(sweep_tags)} sweep tags")
        else:
            # Capped: when a manifest covers only part of the corpus this list is the
            # same content as section [6] and burying the project-name mismatches under
            # 26 lines of it helps nobody.
            cap = 8
            if drift_sweep_only:
                print(f"  {len(drift_sweep_only)} SELECTED sweep tag(s) missing from the "
                      f"manifest (manifest is STALE / incomplete):")
                for t in drift_sweep_only[:cap]:
                    print(f"    - {t}")
                if len(drift_sweep_only) > cap:
                    print(f"    ... and {len(drift_sweep_only) - cap} more "
                          f"(see section [6], or --json)")
            for t in drift_manifest_only:
                print(f"  manifest has '{t}' — sweep.sh does not (row removed/renamed)")
            for tag, sw, mf in drift_project:
                print(f"  '{tag}': sweep.sh project={sw!r} but manifest says {mf!r}")
        if have_manifest and drift_uncovered_corpus:
            print(f"  (corpus-wide: {drift_uncovered_corpus} of {len(sweep_tags)} sweep tags "
                  f"have no manifest entry — informational unless selected)")
        print()

        print(BAR)
        print(f"  rows selected     : {len(results)}")
        print(f"  sweep-ready       : {len(ok_rows)}")
        print(f"  BLOCKING          : {len(blocking)}")
        print(f"  stub re-imports   : {len(dup_rows)} row(s) (informational, not a defect)")
        print(f"  install to reimport: {len(to_install)}")
        print(f"  wrong build       : {len(wrong_build)}")
        print(f"  pdb gaps          : {len(pdb_missing)}")
        print(f"  shared binaries   : {len(collisions)} group(s) "
              f"({sum(len(g) for g in collisions)} rows on "
              f"{len(collisions)} file(s))")
        print(f"  unverifiable      : {len(not_steam)} not-on-Steam, {len(unknown)} unknown, "
              f"{len(unverified)} located-but-no-recorded-identity")
        print(BAR)

    code = EXIT_OK
    # dup_rows is deliberately NOT here: stub re-imports are measured to have zero effect on
    # sweep results, so they must not fail --strict alongside genuine reconstructability gaps.
    if args.strict and (to_install or pdb_missing or wrong_build or collisions):
        code = EXIT_GAPS
    if has_drift and not args.allow_drift:
        code = EXIT_DRIFT
    if blocking and not args.allow_partial:
        code = EXIT_NOGO
    if not args.json:
        verdict = {EXIT_OK: "GO", EXIT_NOGO: "NO-GO", EXIT_DRIFT: "DRIFT",
                   EXIT_GAPS: "GAPS"}[code]
        print(f"verdict: {verdict}  (exit {code})")
        if code == EXIT_NOGO:
            print("  -> fix the BLOCKING rows, or re-run with --allow-partial to sweep "
                  "the rest anyway")
    return code


if __name__ == "__main__":
    try:
        sys.exit(main(sys.argv[1:]))
    except SystemExit:
        raise
    except KeyboardInterrupt:
        sys.exit(EXIT_ERROR)
