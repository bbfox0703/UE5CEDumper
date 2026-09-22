#!/usr/bin/env python3
r"""Build a STAGED UE5Dumper.dll from a reviewed source edit, then put the tree back byte-exact.

    py tools/verify/stage_dll.py tools/verify/staging/<name>.json
    py tools/verify/stage_dll.py tools/verify/staging/<name>.json --dry-run   # apply + diff + restore, no build

A spec is JSON:
    {"name": "l40-force-fallbacks",
     "why":  "one line: which row, which branch it forces",
     "subs": [{"file": "dll/src/Genau.cpp", "old": "...", "new": "...", "count": 1}, ...]}

WHY THIS EXISTS. Several LOW live rows can only be reached with a DLL that forces a branch the
installed titles never take (a fallback tier, an unmeasured-offsets verdict, a stalled first walk).
Each was being staged by hand, and the two ways that goes wrong are both silent:

  * the source is not restored byte-exact -- CLAUDE.md's NUL-byte incidents, or a CRLF rewrite that
    `git checkout` cannot be trusted to undo -- so the NEXT build ships the staging;
  * the staged DLL lands in `dist\`, which is the AOT handover build. `build.ps1 -Target DLL`
    writes there; `build_dll.py` does not, so this goes through `build_dll.py` only.

What it guarantees, in order: the files it edits have NO local changes before it starts (refuses
otherwise); every substitution matches EXACTLY `count` times; the originals are restored from
byte copies in a `finally`, and `git diff --quiet` on them must then pass; the staged DLL is copied
to `out\staged\<name>\UE5Dumper.dll` with a STAGED.txt naming the spec, HEAD and the sha256; and a
final clean build puts `build\` back on HEAD's sources. ⛔ A staged DLL carries the SAME build number
as HEAD (`build_dll.py` never bumps), so `pipe_client.assert_build()` cannot tell it apart --
identify it by the sha256 in STAGED.txt.
"""
import argparse
import hashlib
import json
import os
import shutil
import subprocess
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
BUILT = os.path.join(REPO, "build", "dll", "UE5Dumper.dll")


def sha(p):
    return hashlib.sha256(open(p, "rb").read()).hexdigest()


def git(*a):
    return subprocess.run(["git", "-C", REPO] + list(a), capture_output=True, text=True,
                          encoding="utf-8", errors="replace")


def build():
    r = subprocess.run([sys.executable, os.path.join(HERE, "build_dll.py"), "--targets", "UE5Dumper"],
                       capture_output=True, text=True, encoding="utf-8", errors="replace")
    tail = (r.stdout + r.stderr).strip().splitlines()[-6:]
    for ln in tail:
        print("   | " + ln[:160])
    return r.returncode


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("spec")
    ap.add_argument("--dry-run", action="store_true")
    a = ap.parse_args()
    spec = json.load(open(a.spec, encoding="utf-8"))
    name, subs = spec["name"], spec["subs"]
    files = sorted({s["file"] for s in subs})

    dirty = git("status", "--porcelain", "--", *files).stdout.strip()
    if dirty:
        print("REFUSED: the files to stage have local changes -- commit or restore them first:\n" + dirty)
        return 2
    head = git("rev-parse", "--short", "HEAD").stdout.strip()
    originals = {f: open(os.path.join(REPO, f), "rb").read() for f in files}
    print("staging %s at HEAD %s -- %s" % (name, head, spec.get("why", "")))

    rc = 1
    staged_sha = None
    try:
        for f in files:
            text = originals[f].decode("utf-8")
            # ⚠ A tracked file can be CRLF in the WORK TREE while its blob is LF: `git ls-files --eol`
            # listed 616 such files on 2026-09-22 (checked out before the eol=lf pin, never rewritten
            # since), and `git status` calls them clean. Specs are written LF, so speak the file's own
            # line ending rather than refusing -- the restore below is byte-exact either way.
            crlf = "\r\n" in text
            for s in (x for x in subs if x["file"] == f):
                old, new = s["old"], s["new"]
                if crlf and "\r\n" not in old:
                    old, new = old.replace("\n", "\r\n"), new.replace("\n", "\r\n")
                n = text.count(old)
                want = s.get("count", 1)
                if n != want:
                    raise SystemExit("REFUSED: %s: anchor matched %d time(s), spec says %d:\n  %r"
                                     % (f, n, want, s["old"][:100]))
                text = text.replace(old, new)
            data = text.encode("utf-8")
            if b"\x00" in data:
                raise SystemExit("REFUSED: a NUL byte would be written into " + f)
            open(os.path.join(REPO, f), "wb").write(data)
        print(git("diff", "--stat", "--", *files).stdout.rstrip())
        if a.dry_run:
            print("--dry-run: not building")
            rc = 0
        else:
            print("building the STAGED DLL ...")
            if build() != 0:
                print("BUILD FAILED -- nothing staged")
            else:
                out = os.path.join(REPO, "out", "staged", name)
                os.makedirs(out, exist_ok=True)
                dst = os.path.join(out, "UE5Dumper.dll")
                shutil.copy2(BUILT, dst)
                staged_sha = sha(dst)
                with open(os.path.join(out, "STAGED.txt"), "w", encoding="utf-8") as fh:
                    fh.write("name: %s\nwhy: %s\nhead: %s\nspec: %s\nsha256: %s\n"
                             % (name, spec.get("why", ""), head, os.path.relpath(a.spec, REPO), staged_sha))
                print("STAGED: %s  sha256 %s" % (dst, staged_sha[:16]))
                rc = 0
    finally:
        for f in files:
            open(os.path.join(REPO, f), "wb").write(originals[f])
        clean = git("diff", "--quiet", "--", *files).returncode == 0
        print("source restored byte-exact: %s" % ("yes (git diff clean)" if clean else "NO -- INVESTIGATE"))
        if not clean:
            rc = 3
    if staged_sha and rc == 0:
        print("rebuilding build\\ from HEAD's sources ...")
        if build() != 0:
            print("⚠ the post-stage rebuild FAILED -- build\\ may still hold the staged objects")
            rc = 4
        elif sha(BUILT) == staged_sha:
            print("⚠ build\\dll\\UE5Dumper.dll still equals the staged sha -- the rebuild did not happen")
            rc = 4
        else:
            print("build\\ is back on HEAD (UE5Dumper.dll sha %s)" % sha(BUILT)[:16])
    return rc


if __name__ == "__main__":
    sys.exit(main())
