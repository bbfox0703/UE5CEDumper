"""Reconcile the SELF-BUILT reference binaries on disk against the sweep rows that use them.

Answers the one question no other tool does: **what did I build that the corpus is NOT testing?**
An unswept build is invisible — it cost the packaging time and the disk, and nothing exercises it,
so it neither proves anything nor shows up as missing. `preflight.py` cannot see this: it walks
`sweep.sh` outward to the files, so a binary no row mentions is outside its world entirely.

Source of truth is `corpus-manifest.json`'s `binary_last_seen` (+ `duplicate_copies`), which records
the exact path Ghidra imported.

⚠ Do NOT re-derive this by parsing project `.prp` files: that matches no row at all — not even one
imported the same day — because **`.prp` holds the program NAME only; the executable path lives
inside the packed `.db`**. `preflight.py`'s `project_programs` already documents exactly this
("The original `executablePath` is NOT here — it lives inside the packed .db"), and
`build_corpus_manifest.py` gets it the working way, by scanning the `.db` gbf files (`_scan_gbf`).
Note the failure mode: a wrong reader here returns *plausible emptiness*, not an error — every row
reads as "not swept", which looks like a finding. Cross-check any inventory that reports a suspicious
100% against a second source before believing it.

    py tools/ghidra/inventory_builds.py            # table + the unswept list
    py tools/ghidra/inventory_builds.py --md       # markdown, for docs/reference-builds.md

Feeds docs/reference-builds.md. Regenerate after adding any reference build.
"""
import glob
import json
import os
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ROOT = os.environ.get("REFBUILD_ROOT", r"D:\UE_Analyze_Data\Varies Version builds")
MAN = os.path.join(REPO, "tools", "ghidra", "corpus-manifest.json")

# A packaged UE build ships a small launcher stub named <Project>.exe (the engine's own
# BootstrapPackagedGame, renamed). It is NOT the game binary and carries EPIC's link date, so
# size-filter it out or the inventory reports engine builds as yours.
MIN_GAME_EXE = 5_000_000


def rows():
    ent = json.load(open(MAN, encoding="utf-8"))["entries"]
    by_path = {}
    for t, v in ent.items():
        if v.get("binary_last_seen"):
            by_path[os.path.normcase(v["binary_last_seen"])] = t
        for d in (v.get("duplicate_copies") or []):
            by_path.setdefault(os.path.normcase(d), t)

    out = []
    for ver in sorted(os.listdir(ROOT)):
        if not os.path.isdir(os.path.join(ROOT, ver)):
            continue
        for g in sorted(glob.glob(os.path.join(ROOT, ver, "**", "*.exe"), recursive=True)):
            if os.path.getsize(g) < MIN_GAME_EXE or f"{os.sep}Engine{os.sep}" in g:
                continue
            cfg = ("DebugGame" if "DebugGame" in g else
                   "Shipping" if "Shipping" in g else "Development")
            out.append((ver, cfg, by_path.get(os.path.normcase(g)), g))
    return out


def main():
    md = "--md" in sys.argv
    r = rows()
    swept = [x for x in r if x[2]]
    un = [x for x in r if not x[2]]

    if md:
        print("| engine | config | sweep tag | binary |")
        print("|---|---|---|---|")
        for ver, cfg, tag, g in r:
            print(f"| {ver} | {cfg} | {tag or '**— not swept —**'} | "
                  f"`{os.path.relpath(g, ROOT)}` |")
    else:
        print(f"{'engine':<8} {'config':<12} {'sweep tag':<30} binary")
        for ver, cfg, tag, g in r:
            print(f"  {ver:<6} {cfg:<12} {tag or '-- NOT SWEPT --':<30} "
                  f"{os.path.relpath(g, ROOT)}")

    print(f"\non disk: {len(r)}   swept: {len(swept)}   NOT swept: {len(un)}")
    if un:
        print("\nNOT swept — stored, but no sweep row exercises them:")
        for ver, cfg, _t, g in un:
            print(f"   {ver:<7} {cfg:<12} {os.path.relpath(g, ROOT)}")
        print("\nThat is not automatically a defect — see docs/reference-builds.md, which records "
              "WHY each unswept build exists. It is a defect only when the reason is 'nobody "
              "noticed'.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
