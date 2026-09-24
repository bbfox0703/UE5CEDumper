#!/usr/bin/env python3
r"""Run ANOTHER UI build from the granted `dist\UE5DumpUI.exe` path for one red arm, then put `dist\` back byte-exact.

    py tools/verify/dist_swap.py backup              dist\ -> out\dist.swapbak\ + a sha256 manifest
    py tools/verify/dist_swap.py install <bindir>    copy every file of <bindir> over dist\ (backup must exist)
    py tools/verify/dist_swap.py restore             put every backed-up file back, delete extras, VERIFY the manifest

WHY. A red arm often needs the PRE-FIX UI (`git archive <fix>^` + `dotnet build` into out\). But the
computer-use grant is by exe PATH, so a UI started from out\ can be neither seen nor clicked -- and
scripting input into an ungranted app would bypass the grant. So the pre-fix build runs from dist\ for
that arm only. `dist\` is the AOT handover build, so it goes back byte-exact and is VERIFIED.

⛔ NOTHING MAY HOLD A FILE IN dist\ WHILE THIS RUNS. Measured 2026-09-22: a game still had
dist\UE5Dumper.dll LOADED (it had been injected from there), the old restore's rmtree deleted half of
dist\ and then died on that file. So `install` and `restore` refuse while a DumperTest* or UE5DumpUI
process exists, and `restore` copies file by file OVER dist\ (deleting only files the manifest does
not know) instead of removing the directory first.
"""
import hashlib
import json
import os
import shutil
import subprocess
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DIST = os.path.join(REPO, "dist")
BAK = os.path.join(REPO, "out", "dist.swapbak")
MAN = os.path.join(REPO, "out", "dist.swapbak.manifest.json")
HOLDERS = ("dumpertest", "ue5dumpui")


def manifest(root):
    m = {}
    for r, _d, fs in os.walk(root):
        for f in fs:
            p = os.path.join(r, f)
            m[os.path.relpath(p, root)] = hashlib.sha256(open(p, "rb").read()).hexdigest()
    return m


def holders():
    out = subprocess.run(["tasklist", "/FO", "CSV", "/NH"], capture_output=True, text=True,
                         errors="replace").stdout
    return sorted({l.split('","')[0].strip('"') for l in out.splitlines()
                   if any(l.strip('"').lower().startswith(h) for h in HOLDERS)})


def refuse_if_held(what):
    h = holders()
    if h:
        raise SystemExit("REFUSED (%s): %s is running and may hold a file in dist\\ -- close it first" % (what, h))


def main():
    cmd = sys.argv[1] if len(sys.argv) > 1 else ""
    if cmd == "backup":
        if os.path.exists(BAK):
            raise SystemExit("REFUSED: a backup already exists at %s -- restore it first" % BAK)
        shutil.copytree(DIST, BAK)
        m = manifest(DIST)
        json.dump(m, open(MAN, "w"), indent=0)
        print("backed up %d files; UE5DumpUI.exe sha %s" % (len(m), m.get("UE5DumpUI.exe", "?")[:8]))
    elif cmd == "install":
        if not os.path.exists(MAN):
            raise SystemExit("REFUSED: no backup manifest -- run backup first")
        refuse_if_held("install")
        src = sys.argv[2]
        n = 0
        for r, _d, fs in os.walk(src):
            for f in fs:
                s = os.path.join(r, f)
                t = os.path.join(DIST, os.path.relpath(s, src))
                os.makedirs(os.path.dirname(t), exist_ok=True)
                shutil.copy2(s, t)
                n += 1
        print("installed %d files from %s over dist" % (n, src))
    elif cmd == "restore":
        if not os.path.exists(MAN):
            raise SystemExit("REFUSED: no backup manifest")
        refuse_if_held("restore")
        want = json.load(open(MAN))
        for rel in want:
            t = os.path.join(DIST, rel)
            os.makedirs(os.path.dirname(t), exist_ok=True)
            shutil.copy2(os.path.join(BAK, rel), t)
        for r, _d, fs in os.walk(DIST, topdown=False):
            for f in fs:
                p = os.path.join(r, f)
                if os.path.relpath(p, DIST) not in want:
                    os.remove(p)
            if r != DIST and not os.listdir(r):
                os.rmdir(r)
        got = manifest(DIST)
        bad = sorted(k for k in set(want) | set(got) if want.get(k) != got.get(k))
        print("restored %d files; mismatches: %d; UE5DumpUI.exe sha %s; size %d" % (
            len(got), len(bad), got.get("UE5DumpUI.exe", "?")[:8],
            os.path.getsize(os.path.join(DIST, "UE5DumpUI.exe"))))
        if bad:
            print("MISMATCH:", bad[:10])
            return 1
        shutil.rmtree(BAK)
        os.remove(MAN)
    else:
        print(__doc__)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
