r"""Reconcile docs/test-games.md's UE versions against every signal this machine can measure.

    py tools/verify/test_games_reconcile.py            # everything installed
    py tools/verify/test_games_reconcile.py --quiet    # disagreements only

⭐ WHY THIS IS A RIG AND NOT A CI GATE — the question was asked directly, and the answer is
measured rather than assumed. A gate must pass on every machine, and NO version signal is available
on every machine, because the games are not in the repo:

    signal                          coverage here            trustworthiness
    ------------------------------  -----------------------  --------------------------------
    `++UE4/5+Release-X.Y` build tag  2 of 19 exes (~10%)      AUTHORITATIVE when present
    game exe PE ProductVersion       every exe                OFTEN THE GAME VERSION, not the
                                                              engine — OCTOPATH 1.0, DQ7R 1.1,
                                                              Elliot 1.2, TQ2 63339.64744
    CrashReportClient.exe ProdVer    7 of 15 titles (~47%)    engine-shipped; 6/7 exact here
    our own detection cache          44 entries               our detector, so not independent —
                                                              but it is what the DLL will DO

So there is no gate. What there is: three partly-independent signals that, taken together, cover
most titles, and a doc that restates a version per title. Run this after a game patch, after a
detection change, or when a row looks wrong. A one-time cleanup rots; a rerunnable reconciliation
does not.

⚠ CrashReportClient reports the SHIPPED ENGINE BRANCH, which is not always what the DLL reports and
that is not a bug in either. DragonSword Awakening: CRC 5.3, DLL 504 — `docs/test-games.md` records
exactly that ("PE: 503 -> runtime-raised to 504 by the CMC::GravityDirection property marker"). A
disagreement is a question, never a verdict.

⚠ A licensee fork's LAYOUT is not a version signal. DQ XI S has a +0x10 shifted UObject layout and
`UField::Next=+0x38`, and reading that as "must be newer than stock 4.18" is exactly how its row
came to say UE4.22 for six weeks while `.rdata` carried `++UE4+Release-4.18`. Trust the tag, then
CrashReportClient, then the detector — never the shape of the layout.
"""
import argparse
import json
import os
import pathlib
import re
import struct
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

REPO = pathlib.Path(__file__).resolve().parents[2]
DOC = REPO / "docs" / "test-games.md"
CACHE_DIR = pathlib.Path(os.environ.get("LOCALAPPDATA", "")) / "UE5CEDumper"
LIBS = [pathlib.Path(r"C:\Program Files (x86)\Steam\steamapps\common"),
        pathlib.Path(r"D:\SteamLibrary\steamapps\common")]

TAG_RE = re.compile(rb"\+\+UE([45])\+Release-(\d+)\.(\d+)")
SIG = struct.pack("<I", 0xFEEF04BD)


def product_version(exe):
    try:
        d = exe.read_bytes()
    except OSError:
        return None
    i = d.find(SIG)
    if i < 0:
        return None
    _ms, _ls, pms, _pls = struct.unpack_from("<IIII", d, i + 8)
    return "%d.%d" % (pms >> 16, pms & 0xFFFF)


def build_tag(exe):
    """The compiled-in engine branch. Authoritative when present; absent ~90% of the time."""
    try:
        d = exe.read_bytes()
    except OSError:
        return None
    found = {"%s.%s" % (m.group(2).decode(), m.group(3).decode()) for m in TAG_RE.finditer(d)}
    return sorted(found)[0] if len(found) == 1 else (",".join(sorted(found)) if found else None)


def prereq_major(folder):
    """UE4 vs UE5, from the redistributable the packager shipped.

    ⭐ Only a MAJOR-version answer, and on that question it is the sharpest thing here: UE4 ships
    `UE4PrereqSetup_x64.exe`, UE5 dropped the digit and ships `UEPrereqSetup_x64.exe`. Measured
    across 16 installed titles 2026-09-06: present in 7, and 6 of 6 comparable ones correct, 0
    wrong. It is what settled DQ I&II HD-2D, whose row had claimed UE5.05 against three signals
    saying UE4 -- an error that CROSSES the major boundary, which is exactly what this catches
    and what a minor-version check never would.
    """
    for f in folder.rglob("UE*PrereqSetup*.exe"):
        return "UE4" if f.name.startswith("UE4Prereq") else "UE5"
    return None


def detection_cache():
    """gameName(no .exe) -> the version OUR DLL last detected. Not independent, but decisive
    about what the tool will actually do on this binary."""
    out = {}
    for f in sorted(CACHE_DIR.glob("UE5CEDumper.*.json")):
        try:
            d = json.loads(f.read_text(encoding="utf-8"))
        except (OSError, ValueError):
            continue
        for g in (d.get("games") or {}).values():
            name = str(g.get("gameName") or "").replace(".exe", "")
            ue = g.get("ueVersion")
            if name and ue:
                out.setdefault(name, set()).add(int(ue))
    return out


# ⚠ The doc's first cell is a HUMAN title, not the folder or exe name, so matching is an explicit
# map rather than a heuristic. Fuzzy matching here produced false "disagreements" on every title
# whose name is a substring of another's (EVERSPACE inside EVERSPACE 2, Satisfactory's two rows).
# An unmatched folder prints "(no row)" instead of being silently skipped -- extend the map.
DOC_ALIAS = {
    "OCTOPATH TRAVELER": "OctoPath Traveler",
    "DRAGON QUEST XI S": "DQ XI S",
    "DRAGON QUEST I and II HD-2D Remake": "DQ I&II HD-2D Remake",
    "EVERSPACE™ 2": "EverSpace 2",
    "Lushfoil Photography Sim": "Lushfoil Photography Sim",
    "Manor Lords": "Manor Lords",
    "Solarpunk": "Solarpunk",
    "Titan Quest II": "Titan Quest II",
    "The Artisan of Glimmith": "Artisan of Glimmith",
    "DragonSword  Awakening": "DragonSword Awakening",
    "Avowed": "Avowed",
    "P3R": "P3R",
    "The Adventures of Elliot_The Millennium Tales": "Elliot",
}
DOC_VER_RE = re.compile(r"\*{0,2}UE\s?(\d)\.(\d{1,2})")


def doc_version(doc_lines, folder):
    """The UE version the doc states for this title, or None. Matches the FIRST table cell only."""
    want = DOC_ALIAS.get(folder)
    if not want:
        return None
    for line in doc_lines:
        if not line.startswith("|"):
            continue
        cells = [c.strip() for c in line.split("|")]
        if len(cells) < 3 or want.lower() not in cells[1].lower():
            continue
        m = DOC_VER_RE.search(cells[2])
        if m:
            return "%s.%s" % (m.group(1), m.group(2).lstrip("0") or "0")
    return None


def code(ver):
    """'5.6' -> 506, '4.18' -> 418."""
    if not ver or "," in ver:
        return None
    try:
        maj, minor = ver.split(".")[:2]
        return int(maj) * 100 + int(minor)
    except ValueError:
        return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--quiet", action="store_true", help="print only titles with a disagreement")
    a = ap.parse_args()

    cache = detection_cache()
    doc_lines = DOC.read_text(encoding="utf-8", errors="replace").splitlines() if DOC.is_file() else []
    titles = []
    for lib in LIBS:
        if not lib.is_dir():
            continue
        for d in sorted(lib.iterdir()):
            if not d.is_dir():
                continue
            exes = [e for e in d.rglob("*.exe")
                    if e.name.endswith("-Win64-Shipping.exe") and e.stat().st_size > 5_000_000]
            if not exes:
                exes = [e for e in d.rglob("*.exe")
                        if e.stat().st_size > 20_000_000 and "CrashReport" not in e.name]
            if not exes:
                continue
            crc = next(iter(sorted(d.rglob("CrashReportClient.exe"))), None)
            titles.append((d.name, exes[0], crc, prereq_major(d)))

    print("%-30s %-8s %-8s %-6s %-8s %-7s %s"
          % ("title", "buildtag", "CrashRep", "prereq", "detected", "doc", "verdict"))
    print("-" * 104)
    disagreements = 0
    for name, exe, crc, pre in titles:
        tag = build_tag(exe)
        crcv = product_version(crc) if crc else None
        stem = exe.name.replace(".exe", "")
        det = sorted(cache.get(stem, set()))
        docv = doc_version(doc_lines, name)
        measured = {c for c in (code(tag), code(crcv)) if c is not None} | set(det)
        codes = measured | ({code(docv)} - {None} if docv else set())
        # A disagreement is only interesting between DIFFERENT signals, and only when the DLL's
        # own answer is one of them -- two archived binaries of one title legitimately differ.
        bad = len(measured) > 1 and len(det) <= 1
        if bad or (docv and measured and code(docv) not in measured):
            disagreements += 1
        docBad = bool(docv and measured and code(docv) not in measured)
        if a.quiet and not (bad or docBad):
            continue
        # A MAJOR-version conflict outranks everything: it cannot be a runtime raise or a
        # patch, only a wrong row or a wrong detection.
        majors = {c // 100 for c in measured} | ({int(pre[2])} if pre else set())
        if docv:
            majors.add(code(docv) // 100)
        note = ""
        if len(majors) > 1:
            note = "*** UE4/UE5 CONFLICT ***"
            docBad = True
        elif docv and measured and code(docv) not in measured:
            note = "*** DOC DISAGREES WITH EVERY MEASURED SIGNAL ***"
        elif bad:
            note = "*** DISAGREE ***"
        elif len(codes) == 1:
            note = "ok"
        print("%-30s %-8s %-8s %-6s %-8s %-7s %s"
              % (name[:29], tag or "-", crcv or "-", pre or "-",
                 ",".join(str(x) for x in det) or "-", docv or "(no row)", note))

    print("\n%d title(s) scanned, %d disagreement(s)." % (len(titles), disagreements))
    print("⚠ A disagreement is a QUESTION, not a verdict: CrashReportClient reports the shipped "
          "engine branch, while the DLL may runtime-RAISE it (DragonSword: CRC 5.3, DLL 504, and "
          "the doc says so). Read docs/test-games.md's row before changing anything, and never "
          "infer a version from a fork's LAYOUT -- that is how DQ XI S's row said 4.22 for six "
          "weeks against a `++UE4+Release-4.18` tag in its own .rdata.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
