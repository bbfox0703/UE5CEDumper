"""One-shot: snapshot the corpus's BUILD IDENTITY before build_corpus_manifest.py can null it.

Why this is not just a manifest re-run: the generator deliberately NULLS steam_buildid / size /
sha256 on any row whose on-disk bytes are no longer the corpus bytes (build_corpus_manifest.py's
BUILD IDENTITY block). That is right — it must never assert the wrong build — but it means the
buildid pointing at the SteamDB build a .rep was made from is DESTROYED the first time that game
patches.

Three identity sources, strongest first:
  1. sku.sis depot MANIFEST ids from X:\\SteamLibrary backup - EXACT, and they survive the game
     being uninstalled or even delisted (`steamcmd +download_depot <app> <depot> <manifest>`).
  2. steam_buildid - exact via SteamDB app -> Builds, but nulled on drift, hence this snapshot.
  3. file MTIME - the original Steam write time. It SURVIVES a copy (ctime does not), which is
     what makes install -> copy -> uninstall recoverable by date: a copied file's ctime is the
     copy's date while its mtime is still the pre-patch build's. Use file_modified, never
     file_created.

Output: tools/ghidra/corpus-provenance.tsv, a SNAPSHOT, never auto-regenerated.
"""
import json
import os
import re
import sys
import time

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
MAN = os.path.join(REPO, "tools", "ghidra", "corpus-manifest.json")
OUT = os.path.join(REPO, "tools", "ghidra", "corpus-provenance.tsv")
BACKUP_ROOT = r"X:\SteamLibrary backup"


def stamp(p):
    try:
        st = os.stat(p)
        return (time.strftime("%Y-%m-%d %H:%M", time.localtime(st.st_ctime)),
                time.strftime("%Y-%m-%d %H:%M", time.localtime(st.st_mtime)),
                str(st.st_size))
    except OSError:
        return "-", "-", "-"


def parse_sku(path):
    txt = open(path, encoding="utf-8", errors="replace").read()

    def block(key):
        i = txt.find(f'"{key}"')
        if i < 0:
            return ""
        j = txt.find("{", i)
        depth, k = 0, j
        while k < len(txt):
            if txt[k] == "{":
                depth += 1
            elif txt[k] == "}":
                depth -= 1
                if depth == 0:
                    return txt[j:k]
            k += 1
        return ""
    return (re.findall(r'"\d+"\s+"(\d+)"', block("apps")),
            dict(re.findall(r'"(\d+)"\s+"(\d+)"', block("manifests"))))


STEAM_LOG = r"C:\Program Files (x86)\Steam\logs\console_log.txt"


def scan_steam_log():
    """-> [(epoch, app, depot, manifest)] for every 'Depot download complete' line.

    This is the recovery route for the ARCHIVE rows, which have no appid/buildid of their own:
    Steam logs the app, depot and EXACT manifest of every depot it fetches, with a timestamp.
    An archived file's mtime falls inside the download that produced it, so mtime -> log entry
    -> manifest. Verify the guess afterwards with md5_at_import; do not trust the correlation
    alone. NOTE the log rotates, so this only ever sees recent downloads - snapshot early.
    """
    out = []
    try:
        txt = open(STEAM_LOG, encoding="utf-8", errors="replace").read()
    except OSError:
        return out
    pat = (r'\[([0-9]{4}-[0-9]{2}-[0-9]{2} [0-9:]{8})\] Depot download complete : '
           r'"[^"]*app_([0-9]+)[\\/]depot_([0-9]+)"\s*\(manifest ([0-9]+)\)')
    for ts, app, depot, man in re.findall(pat, txt):
        try:
            ep = time.mktime(time.strptime(ts, "%Y-%m-%d %H:%M:%S"))
        except ValueError:
            continue
        out.append((ep, app, depot, man))
    return sorted(out)


def correlate(mtime_str, log, window_min=90):
    """Closest download that COMPLETED at or after the file's write time."""
    if mtime_str == "-" or not log:
        return "-"
    try:
        t = time.mktime(time.strptime(mtime_str, "%Y-%m-%d %H:%M"))
    except ValueError:
        return "-"
    cands = [(ep - t, app, dep, man) for ep, app, dep, man in log
             if -120 <= (ep - t) <= window_min * 60]
    if not cands:
        return "-"
    cands.sort()
    best = cands[0]
    s = f"{best[1]}:{best[2]}:{best[3]}"
    if len(cands) > 1:
        s += f" (+{len(cands)-1} alt: " + ",".join(c[3] for c in cands[1:3]) + ")"
    return s


def scan_backups():
    """-> {appid: (manifests dict, sku path)}"""
    out = {}
    if not os.path.isdir(BACKUP_ROOT):
        return out
    for dirpath, _d, files in os.walk(BACKUP_ROOT):
        for f in files:
            if f.lower() == "sku.sis":
                p = os.path.join(dirpath, f)
                try:
                    apps, mans = parse_sku(p)
                except Exception:                            # noqa: BLE001
                    continue
                for a in apps:
                    out[a] = (mans, p)
    return out


# Manually resolved from SteamDB's depot manifest history, recorded here because nothing on this
# machine can derive them: the bytes are gone and the Steam log had rotated past them.
#   tag -> (app:depot:manifest, evidence)
KNOWN_MANIFESTS = {
    "UE5.5-Everspace2": (
        "1128920:1128921:4415922863161237626",
        "SteamDB: published 2025-05-16, the manifest live on the 05-17 snapshot date. CONFIRMED "
        "by sweep.sh's own independent note 'two manifests newer (2025-06-17 vs the 05-17 "
        "snapshot)': newer than this one are 2025-05-28 and 2025-06-17 = exactly two. The "
        "2025-05-12 alternative (5104604053195963718) would leave THREE and is thereby excluded."),
    "UE5.5-Everspace2b": (
        "1128920:1128921:735055807809773736",
        "SteamDB: published 2025-06-17, matching this row's recorded build date, and it is the "
        "manifest currently installed."),
}


def main():
    e = json.load(open(MAN, encoding="utf-8"))["entries"]
    backups = scan_backups()
    steamlog = scan_steam_log()
    rows = []
    for tag, v in sorted(e.items()):
        src = v.get("source") or "-"
        app = v.get("steam_app_id") or ""
        paths = [p for p in [v.get("binary_last_seen")] + list(v.get("duplicate_copies") or [])
                 if p and os.path.exists(p)]
        # Prefer a copy whose size matches the manifest, not merely the first that exists —
        # a row's binary_last_seen can be a Steam path the game has since PATCHED while a D:
        # backup is still the corpus build. Stamping the former pairs a corpus-build buildid
        # with a new-build timestamp in one row.
        want = v.get("binary_size_bytes")
        best = next((p for p in paths if os.path.getsize(p) == want), None) if want else None
        if best is None:
            best = paths[0] if paths else None
        c, m, sz = stamp(best) if best else ("-", "-", "-")

        logman = correlate(m, steamlog)
        mans, sku = backups.get(app, ({}, None))
        man_s = "; ".join(f"{d}:{x}" for d, x in mans.items()) if mans else "-"
        drifted = not want

        known = KNOWN_MANIFESTS.get(tag)
        if known:
            man_s = known[0]
            how = "STEAMDB-MANIFEST"
            why = known[1]
        elif mans:
            how = "STEAM-BACKUP-MANIFEST"
            why = "exact depot manifest on hand; date irrelevant"
        elif src == "self-built":
            how, why = "REBUILD", "build time"
        elif src == "steam" and app and v.get("steam_buildid"):
            how, why = "STEAMDB-BUILDID", "exact buildid on hand; date is corroboration"
        elif src == "steam" and app:
            how = "STEAMDB-BY-MTIME"
            why = ("mtime dates the WRONG build (on-disk copy drifted)" if drifted
                   else "mtime = original Steam write time; usable")
        elif logman != "-":
            how = "STEAMLOG-MANIFEST"
            why = "mtime falls inside a logged depot download - manifest is a strong CANDIDATE"
        else:
            how, why = "NONE-HASH-ONLY", "local re-save time - maps to NO manifest"

        rows.append([tag, src, how, app or "-", v.get("steam_buildid") or "-", man_s, logman,
                     str(want or "-"), v.get("binary_sha256") or "-", v.get("binary_md5") or "-",
                     c, m, sz, why, best or "-", sku or "-"])

    hdr = ["tag", "source", "reproduce_via", "steam_app_id", "steam_buildid",
           "depot_manifests", "steamlog_candidate", "manifest_size", "sha256", "md5_at_import",
           "file_created", "file_modified", "file_size_now", "date_meaning",
           "path_snapshotted", "sku_sis"]
    L = [
        "# corpus-provenance.tsv - SNAPSHOT taken 2026-07-29. NOT auto-generated; do NOT overwrite",
        "# it with build_corpus_manifest.py output.",
        "#",
        "# WHY: build_corpus_manifest.py NULLS steam_buildid/size/sha256 on any row whose on-disk",
        "# bytes stopped being the corpus bytes (by design - it must not assert the wrong build).",
        "# So the pointer to the build a .rep was made from is lost the first time the game",
        "# patches. Palworld drifted on 2026-07-29. This file preserves it. Re-snapshot BEFORE a",
        "# game updates, not after.",
        "#",
        "# reproduce_via, strongest first:",
        "#   STEAM-BACKUP-MANIFEST  a sku.sis backup holds the EXACT depot manifest ids. Recover",
        "#                          with: steamcmd +download_depot <app> <depot> <manifest>.",
        "#                          Survives uninstall AND delisting. Best possible case.",
        "#   STEAMDB-BUILDID        exact: SteamDB app -> Builds -> this buildid -> depot manifest.",
        "#   STEAMDB-MANIFEST       manifest id resolved by hand off SteamDB's depot history and",
        "#                          cross-checked against an independent record in the repo. The",
        "#                          reasoning is kept in date_meaning - do not drop it, it is the",
        "#                          only thing that makes the id auditable.",
        "#   STEAMDB-BY-MTIME       appid known, no buildid/backup: narrow by file_modified on",
        "#                          SteamDB, then confirm with md5_at_import.",
        "#   STEAMLOG-MANIFEST      no appid of its own, BUT Steam's console_log.txt logged a depot",
        "#                          download whose window contains the file's mtime. That names the",
        "#                          app, depot and exact manifest. A STRONG CANDIDATE, not a proof -",
        "#                          download it and confirm against md5_at_import. This is what",
        "#                          rescues the D:/tmp/Game archive rows, which have no identity",
        "#                          of their own. The Steam log ROTATES: snapshot it early.",
        "#   REBUILD                self-built - recompile from the installed engine.",
        "#   NONE-HASH-ONLY         archive copies re-saved locally; they came from an old manifest",
        "#                          and map back to none. md5/sha256 is all there is.",
        "#",
        "# USE file_modified, NOT file_created. A copy RESETS ctime but PRESERVES mtime, so for an",
        "# install -> copy -> uninstall workflow the mtime is still the original Steam write time.",
        "# Measured here: Hogwarts ctime 2026-07-29 / mtime 2025-12-04; Octopath mtime 2025-05-12;",
        "# Palworld mtime 2026-07-15 = the pre-patch corpus build. An earlier pass of this file",
        "# read ctime and wrongly concluded the dates were destroyed by the corpus move.",
        "#",
        "# md5_at_import is Ghidra's own import-time record. It is the ONLY identity that survives",
        "# the file being deleted or replaced, so it is the final arbiter in every case.",
    ]
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(L) + "\n")
        f.write("\t".join(hdr) + "\n")
        for r in rows:
            f.write("\t".join(r) + "\n")

    from collections import Counter
    c = Counter(r[2] for r in rows)
    print(f"wrote {OUT}  ({len(rows)} entries)  sku.sis backups seen: {len(backups)}")
    for k, n in sorted(c.items()):
        print(f"  {k:<24} {n}")


main()
