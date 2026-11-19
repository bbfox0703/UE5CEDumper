"""Which installed-LOOKING game folders are ACTUALLY installed, and which are empty leftovers?

    py tools/verify/fixture_census.py

⭐ WHY THIS EXISTS. A folder under `steamapps/common` is NOT evidence that a game is playable:
a leftover folder can hold no executable at all (one directory containing one empty directory),
and such a game can still be cited in `docs/` as an available fixture, or named in
`docs/todo.md` as the requirement a verification row is waiting for.

A register plan built from `ls steamapps/common` therefore sends the operator to boot a game that
does not exist — which reads as a launcher problem, not as a missing fixture, and costs a session.
This turns "is X installed" into a measurement instead of a directory listing.

The discriminator is an executable somewhere in the tree, cross-checked against the appmanifest's
`StateFlags` (4 = fully installed). Size and file count are printed because a 3-file / 0-byte tree
is the signature of a leftover, and because "how long will this take to boot" is the next question
the operator has.
"""
import os
import pathlib
import re
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

LIBS = [
    pathlib.Path(r"C:\Program Files (x86)\Steam\steamapps"),
    pathlib.Path(r"D:\SteamLibrary\steamapps"),
]

# appid -> (name, stateflags, sizeondisk) from the manifests
manifests = {}
for lib in LIBS:
    if not lib.is_dir():
        continue
    for m in lib.glob("appmanifest_*.acf"):
        try:
            txt = m.read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue
        def field(k):
            mo = re.search(r'"%s"\s+"([^"]*)"' % k, txt)
            return mo.group(1) if mo else ""
        manifests[field("installdir").lower()] = (
            m.stem.replace("appmanifest_", ""), field("name"),
            field("StateFlags"), field("SizeOnDisk"))

rows = []
for lib in LIBS:
    common = lib / "common"
    if not common.is_dir():
        continue
    for d in sorted(common.iterdir()):
        if not d.is_dir():
            continue
        exes, total, files = [], 0, 0
        for root, _dirs, fnames in os.walk(d):
            for f in fnames:
                files += 1
                p = os.path.join(root, f)
                try:
                    total += os.path.getsize(p)
                except OSError:
                    pass
                if f.lower().endswith(".exe"):
                    exes.append(os.path.relpath(p, d))
            if files > 40000:      # big installs: stop counting, we already know
                break
        appid, name, state, _size = manifests.get(d.name.lower(), ("?", "", "", ""))
        rows.append((d.name, len(exes), total, files, appid, state,
                     exes[0] if exes else ""))

print("%-46s %4s %10s %7s %9s %5s  %s"
      % ("title", "exes", "GB", "files", "appid", "state", "first exe"))
print("-" * 140)
ghosts = []
for name, nexe, total, files, appid, state, first in rows:
    gb = total / 2**30
    flag = ""
    if nexe == 0:
        flag = "   <<< NO EXE — not actually installed"
        ghosts.append(name)
    print("%-46s %4d %10.2f %7d %9s %5s  %s%s"
          % (name[:46], nexe, gb, files, appid, state, first[:44], flag))

print()
print("GHOSTS (folder exists, nothing to run): %d" % len(ghosts))
for g in ghosts:
    print("   ", g)
