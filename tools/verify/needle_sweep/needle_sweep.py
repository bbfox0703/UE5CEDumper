#!/usr/bin/env python3
"""Does a change to the version-needle table move what any exe reads? The acceptance sweep for a new needle.

    py tools/verify/needle_sweep/needle_sweep.py <root or exe>... [--base REV] [--must-differ FILE...] [--vcvars PATH]

A needle at the head of kVersionNeedles wins Tiers 2/3 by table order, so a new one can take over a title whose
memory merely carries the string ("5.9." inside a path or a plugin's version). The unit tests prove the scan matches
its naive reference, not that a real exe gains no stray hit; only the exes can say that.

It compiles needle_sweep_run.cpp twice -- against REV's dll/src/VersionNeedleScan.h (default HEAD) and against the
working tree's -- runs both over every UE-sized exe under each root (linker_survey.py's filter), and compares Tier 1,
the branch-name tier and Tiers 2/3 per file. PASS (exit 0): no exe reads differently, and every --must-differ file
does. Every difference is listed; for a needle no shipped engine has yet, any difference is a stray hit. A
--must-differ file carrying the new needle is the positive control: a sweep that cannot see the change it is checking
proves nothing. The roots are arguments: the corpus and the
Steam libraries are on this machine, not in the repo. Binaries and logs land in out/needle_sweep/.
"""
from __future__ import annotations

import argparse
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
OUT = os.path.join(ROOT, "out", "needle_sweep")
sys.path.insert(0, os.path.dirname(HERE))
import linker_survey  # noqa: E402  (its UE-sized exe filter)
import msvc_guard_probe  # noqa: E402  (vcvars64's environment)

sys.stdout.reconfigure(encoding="utf-8", errors="replace")


def build(label: str, header_dir: str, env: dict[str, str]) -> str:
    exe = os.path.join(OUT, f"needle_sweep_{label}.exe")
    obj_dir = os.path.join(OUT, f"obj_{label}") + os.sep
    os.makedirs(obj_dir, exist_ok=True)
    cmd = ["cl", "/nologo", "/std:c++20", "/O2", "/EHsc", "/utf-8", f"/I{header_dir}", f"/Fo{obj_dir}", f"/Fe{exe}",
           os.path.join(HERE, "needle_sweep_run.cpp")]
    r = subprocess.run(cmd, cwd=OUT, env=env, capture_output=True, text=True, errors="replace")
    if r.returncode != 0:
        sys.exit(f"build {label} failed:\n{r.stdout}{r.stderr}")
    return exe


def run(exe: str, files: list[str]) -> dict[str, str]:
    r = subprocess.run([exe], input="\n".join(files) + "\n", capture_output=True, text=True, encoding="utf-8",
                       errors="replace")
    rows = {}
    for line in r.stdout.splitlines():
        path, _, rest = line.partition("\t")
        rows[path] = rest
    return rows


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("paths", nargs="+", help="folders to search for UE exes, or exe files")
    ap.add_argument("--base", default="HEAD", help="the revision whose VersionNeedleScan.h is the old table")
    ap.add_argument("--must-differ", nargs="*", default=[], help="files that must read differently (controls)")
    ap.add_argument("--vcvars", help="vcvars64.bat to use (default: the newest VS with the C++ x64 tools)")
    a = ap.parse_args()

    os.makedirs(os.path.join(OUT, "base"), exist_ok=True)
    old = subprocess.run(["git", "show", f"{a.base}:dll/src/VersionNeedleScan.h"], cwd=ROOT, capture_output=True)
    if old.returncode != 0:
        sys.exit(f"git show {a.base}:dll/src/VersionNeedleScan.h failed")
    with open(os.path.join(OUT, "base", "VersionNeedleScan.h"), "wb") as fh:
        fh.write(old.stdout)

    env = msvc_guard_probe.msvc_env(msvc_guard_probe.vcvars_path(a.vcvars))
    base_exe = build("base", os.path.join(OUT, "base"), env)
    work_exe = build("work", os.path.join(ROOT, "dll", "src"), env)

    files = []
    for p in a.paths:
        files.extend(linker_survey.ue_exes(p) if os.path.isdir(p) else [p])
    files = sorted(set(os.path.normpath(f) for f in files))
    controls = [os.path.normpath(c) for c in a.must_differ]
    print(f"{len(files)} exe(s) and {len(controls)} control(s); base {a.base} vs the working tree")
    before, after = run(base_exe, files + controls), run(work_exe, files + controls)

    blind = [c for c in controls if before.get(c) == after.get(c)]
    for c in controls:
        print(f"{'CONTROL ' if c not in blind else 'BLIND   '} {c}\n      base {before.get(c)}\n      work {after.get(c)}")
    diffs = [f for f in files if before.get(f) != after.get(f)]
    errors = [f for f in files if "ERROR" in (before.get(f) or "ERROR")]
    with open(os.path.join(OUT, "sweep.tsv"), "w", encoding="utf-8", newline="\n") as fh:
        fh.write("path\tbase tier1\tbase branch\tbase tier23\twork tier1\twork branch\twork tier23\n")
        for f in files:
            fh.write(f"{f}\t{before.get(f, '')}\t{after.get(f, '')}\n")
    for f in diffs:
        print(f"DIFF  {f}\n      base {before.get(f)}\n      work {after.get(f)}")
    for f in errors:
        print(f"ERROR {f}: could not be read")
    read = len(files) - len(errors)
    print(f"{read} read, {len(diffs)} reading differently (table in {os.path.relpath(os.path.join(OUT, 'sweep.tsv'), ROOT)})")
    ok = not diffs and not blind and read > 0
    print("PASS" if ok else "FAIL")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
