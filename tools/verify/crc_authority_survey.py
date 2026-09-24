"""Is `CrashReportClient.exe` usable as a UE-version source for the DLL? Measure, then decide.

    py crc_authority_survey.py --oracle          # harvest installed UE Editors -> the hash oracle
    py crc_authority_survey.py --survey          # every game folder: CRC vs game exe vs our cache
    py crc_authority_survey.py --oracle --survey

⭐ THE IDEA UNDER TEST (maintainer, 2026-09-06). `CrashReportClient.exe` is shipped BY THE ENGINE,
not built by the game team, so a developer has no reason to ship game version A beside a CRC from
version B. If it is present, read it; if not, skip. Worst case is absence, never a wrong answer.

⭐ AND THE CONSERVATIVE FORM, which is the interesting half: keep a corpus of KNOWN-OFFICIAL CRC
binaries (version + file hash) taken from installed UE Editors. A game's CRC counts as
authoritative only when its hash matches one we hold. Holding 4.18.3 but not 4.18.2 means a CRC
claiming 4.18.2 is *plausible but uncorroborated* -- a lower tier, not a rejection.

⚠ WHY THE ORACLE HALF IS TIME-SENSITIVE. The oracle can only be built from officially installed
Editors, and those get uninstalled to reclaim SSD space. The hashes must be harvested while the
Editors exist; afterwards the data is unobtainable without re-downloading tens of GB.

WHAT THIS MEASURES, and why each column decides something:
  * CRC ProductVersion  -- the proposal's signal. Note it carries a PATCH level (4.18.3), which is
    finer than our own version codes (418), so it can corroborate but not be compared naively.
  * game exe version    -- ⛔ NOT a signal. Frequently the GAME's version (OCTOPATH 1.0, DQ7R 1.1).
    Present here only to show how often it disagrees.
  * our cached ueVersion -- what Genau::DetectVersion actually produced for that binary.
  * hash in oracle?     -- whether the conservative rule would accept it.

Reads PE version info through tools/verify/pe_version_probe.py, which mirrors
`Genau::DetectVersionFromPEResource` -- so a divergence between this survey and the DLL is a real
divergence, not a porting artefact.
"""
import argparse
import glob
import hashlib
import json
import os
import pathlib
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
REPO = HERE.parent.parent

import pe_version_probe as PV  # noqa: E402

ORACLE_PATH = REPO / "tools" / "ue-crc-oracle.json"

EDITOR_ROOTS = [
    r"C:\Program Files\Epic Games",
    r"D:\Program Files\Epic Games",
    r"D:\Epic Games",
]
GAME_ROOTS = [
    r"D:\SteamLibrary\steamapps\common",
    r"C:\Program Files (x86)\Steam\steamapps\common",
    r"D:\UE_Analyze_data\For Testing",
    r"D:\Games",
]


def sha256(p):
    h = hashlib.sha256()
    with open(p, "rb") as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def probe(path):
    """(product, file, code) as Genau's PE-resource path would read it."""
    try:
        info = PV.read_version(str(path)) if hasattr(PV, "read_version") else None
    except Exception:
        info = None
    if info is None:
        info = {}
        for key in ("ProductVersion", "FileVersion"):
            try:
                info[key] = PV.query_string(str(path), key)
            except Exception:
                info[key] = None
        try:
            info["fixed"] = PV.fixed_info(str(path))
        except Exception:
            info["fixed"] = None
    return info


def fallback_probe(path):
    """Shell out to the probe if its internals are not importable in this shape."""
    import subprocess
    out = subprocess.run([sys.executable, str(HERE / "pe_version_probe.py"), str(path)],
                         capture_output=True, text=True, encoding="utf-8", errors="replace")
    prod = filev = code = ""
    for line in out.stdout.splitlines():
        if "prod=" in line:
            for tok in line.split():
                if tok.startswith("prod="):
                    prod = tok[5:]
                elif tok.startswith("file="):
                    filev = tok[5:]
            if "->" in line:
                code = line.rsplit("->", 1)[1].strip()
    return prod, filev, code


def find_editors():
    out = []
    for root in EDITOR_ROOTS:
        for d in sorted(glob.glob(os.path.join(root, "UE_*"))):
            crc = os.path.join(d, "Engine", "Binaries", "Win64", "CrashReportClient.exe")
            if os.path.isfile(crc):
                out.append((os.path.basename(d), crc))
    return out


def find_games():
    out = []
    for root in GAME_ROOTS:
        if not os.path.isdir(root):
            continue
        for entry in sorted(os.scandir(root), key=lambda e: e.name):
            if not entry.is_dir():
                continue
            crc = os.path.join(entry.path, "Engine", "Binaries", "Win64", "CrashReportClient.exe")
            out.append((entry.name, entry.path, crc if os.path.isfile(crc) else None))
    return out


def load_cache():
    hits = glob.glob(os.path.expandvars(r"%LOCALAPPDATA%\UE5CEDumper\UE5CEDumper.*.json"))
    if not hits:
        return {}
    d = json.load(open(hits[0], encoding="utf-8"))
    by_name = {}
    for v in d.get("games", {}).values():
        if isinstance(v, dict) and v.get("gameName"):
            by_name.setdefault(v["gameName"].lower(), v.get("ueVersion"))
    return by_name


def merge_oracle(existing, harvested):
    """[VND583-16] Merge freshly harvested entries INTO the stored oracle, keyed by sha256.

    Returns (merged, added, updated, kept). An entry whose Editor is no longer installed is KEPT:
    that is the whole value of the oracle -- its hashes are unobtainable once the Editor is gone.
    Overwriting (the old behaviour) silently dropped every such row; re-harvesting after the 5.8.2
    Editor was upgraded in place to 5.8.3 would have deleted the 5.8.2 CRC hash.
    """
    merged = dict(existing)
    added, updated = [], []
    for h, e in harvested.items():
        if h not in merged:
            added.append(h)
        elif merged[h] != e:
            updated.append(h)
        merged[h] = e
    kept = sorted(h for h in existing if h not in harvested)
    return merged, sorted(added), sorted(updated), kept


def selftest():
    """[VND583-16] The merge, on synthetic entries -- no Editor, no file outside the repo."""
    e = lambda h, ed, pv: {"editor": ed, "productVersion": pv, "sha256": h}
    existing = {"a": e("a", "UE_5.8", "5.8.2.0"), "b": e("b", "UE_4.27", "4.27.2.0")}
    harvested = {"c": e("c", "UE_5.8", "5.8.3.0"), "b": e("b", "UE_4.27", "4.27.2.0")}
    merged, added, updated, kept = merge_oracle(existing, harvested)
    checks = [
        ("the uninstalled 5.8.2 row is KEPT", "a" in merged and kept == ["a"]),
        ("the new 5.8.3 row is added", "c" in merged and added == ["c"]),
        ("an unchanged row is neither added nor updated", "b" in merged and updated == []),
        ("three rows in all", len(merged) == 3),
    ]
    renamed = {"b": e("b", "UE_4.27_moved", "4.27.2.0")}
    m2, a2, u2, k2 = merge_oracle(existing, renamed)
    checks.append(("a changed row under the same hash is UPDATED, not duplicated", u2 == ["b"] and len(m2) == 2))
    failed = [name for name, ok in checks if not ok]
    for name, ok in checks:
        print("  %s  %s" % ("ok  " if ok else "FAIL", name))
    print("crc_authority_survey selftest: %s" % ("FAILED" if failed else "OK"))
    return 1 if failed else 0


def do_oracle():
    eds = find_editors()
    print("=== ORACLE: CrashReportClient.exe shipped by each installed UE Editor ===")
    print("%-9s %-13s %-11s %10s  %s" % ("editor", "ProductVersion", "code", "bytes", "sha256[:16]"))
    print("-" * 78)
    entries = {}
    for name, crc in eds:
        prod, filev, code = fallback_probe(crc)
        h = sha256(crc)
        sz = os.path.getsize(crc)
        print("%-9s %-13s %-11s %10d  %s" % (name, prod or "-", code or "-", sz, h[:16]))
        entries[h] = {"editor": name, "productVersion": prod, "fileVersion": filev,
                      "code": code, "size": sz, "sha256": h}
    # [VND583-16] MERGE into the stored oracle -- never overwrite it (see merge_oracle).
    existing = {}
    if ORACLE_PATH.exists():
        existing = json.loads(ORACLE_PATH.read_text(encoding="utf-8")).get("entries", {})
    entries, added, updated, kept = merge_oracle(existing, entries)
    for h in kept:
        print("kept      %-9s %-13s (its Editor is not installed now)" % (entries[h].get("editor"),
                                                                        entries[h].get("productVersion")))
    print("merged: %d added, %d updated, %d kept from before" % (len(added), len(updated), len(kept)))
    payload = {
        "_comment": ("Known-official CrashReportClient.exe binaries, harvested from installed UE "
                     "Editors. A game's CRC whose sha256 appears here is CORROBORATED; one that "
                     "does not is plausible-but-uncorroborated, which is a lower tier, not a "
                     "rejection. Harvested because Editors get uninstalled to reclaim disk and the "
                     "hashes are then unobtainable without re-downloading tens of GB."),
        "keyedBy": "sha256",
        "entries": entries,
    }
    ORACLE_PATH.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8", newline="\n")
    print("\nwrote %s -- %d entr(ies)" % (ORACLE_PATH.relative_to(REPO).as_posix(), len(entries)))
    return entries


def do_survey(oracle):
    cache = load_cache()
    known = set(oracle) if oracle else set()
    print("\n=== SURVEY: every game folder under the known roots ===")
    print("%-34s %-11s %-11s %-7s %s" % ("game", "CRC prod", "CRC code", "cached", "oracle?"))
    print("-" * 84)
    n_with, n_agree, n_disagree, n_none = 0, 0, 0, 0
    for name, path, crc in find_games():
        exes = [p for p in glob.glob(os.path.join(path, "**", "*.exe"), recursive=True)][:1]
        cached = None
        for gname, ue in cache.items():
            if gname.split("-")[0].split(".")[0].lower() in name.lower().replace(" ", ""):
                cached = ue
                break
        if crc is None:
            if exes:
                n_none += 1
                print("%-34s %-11s %-11s %-7s %s" % (name[:34], "(no CRC)", "-",
                                                     cached if cached else "-", "-"))
            continue
        n_with += 1
        prod, filev, code = fallback_probe(crc)
        h = sha256(crc)
        inorc = "YES" if h in known else "no"
        flag = ""
        if cached and code and code.isdigit():
            if int(code) == cached:
                n_agree += 1
                flag = "  agree"
            else:
                n_disagree += 1
                flag = "  *** DISAGREES ***"
        print("%-34s %-11s %-11s %-7s %-7s%s" % (name[:34], prod or "-", code or "-",
                                                 cached if cached else "-", inorc, flag))
    print("\n%d folder(s) ship a CRC; %d agree with our cache, %d disagree; "
          "%d UE-looking folders ship NONE" % (n_with, n_agree, n_disagree, n_none))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--oracle", action="store_true")
    ap.add_argument("--survey", action="store_true")
    ap.add_argument("--selftest", action="store_true", help="check the oracle merge on synthetic data, then exit")
    a = ap.parse_args()
    if a.selftest:
        return selftest()
    if not (a.oracle or a.survey):
        a.oracle = a.survey = True
    oracle = {}
    if a.oracle:
        oracle = do_oracle()
    elif ORACLE_PATH.exists():
        oracle = json.loads(ORACLE_PATH.read_text(encoding="utf-8")).get("entries", {})
    if a.survey:
        do_survey(oracle)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
