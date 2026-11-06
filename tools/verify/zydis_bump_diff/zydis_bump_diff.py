#!/usr/bin/env python3
"""Would a vendor/zydis bump change anything Denken -- the DLL's only Zydis user -- produces? A differential rig.

    py tools/verify/zydis_bump_diff/zydis_bump_diff.py <pe file>... [--candidate REV] [--base REV]
                                                       [--all-offsets] [--out DIR]

WHY IT EXISTS
  docs/vendor-pin-reviews.md § zydis. Reading 13 upstream commits answered "does this matter?" only by argument; this
  answers it by running the REAL dll/src/Denken.cpp against both revisions over real game binaries. It was written for
  the 2026-11-06 review (a95bb71 vs fcc63c6) and is kept so the next review only has to run it on the new delta.

WHAT IT DOES
  Per revision (base = the gitlink this repo pins, candidate = origin/master unless given):
    1. `git archive`s vendor/zydis include/ + src/ at that revision into OUT/src_<tag>/. The submodule checkout and the
       gitlink are never touched. zycore comes from the checkout, so a candidate whose zycore gitlink differs is
       refused: a bump must keep zycore at what zydis itself pins (sync_tools.ps1 records the time it did not).
    2. Compiles Zydis decoder-only with the defines dll/CMakeLists.txt sets, plus /DNDEBUG like the Release DLL.
    3. COMPILE CHECK: compiles the real dll/src/Denken.cpp against that revision's headers with the repo's flags.
    4. Links denken_<tag>.exe (real Denken.cpp + that Zydis) and zproj_<tag>.dll (a version-neutral projection of one
       decode: enum VALUES shift between revisions, so fields are compared as strings).
  Then per PE file (read from disk with a minimal loader; never mapped as an image, never executed):
    E2E      Denken::Analyze from every .pdata function start at both revisions; the two output files are compared by
             SHA-256. THIS is the deciding check: it is Denken's output, not a projection of what it might read.
    CODE     per-instruction projection diff over every .pdata range (zdiff code); a row marked dk=1 is a field Denken
             branches on or records.
    ALL      (--all-offsets) the same at every byte offset of every executable section: padding, data-in-code and
             misaligned starts. Slow (minutes per binary). Differences here at non-instruction offsets are expected
             when the candidate adds ISA; they matter only if E2E moves too.
    CONTROL  byte sequences the two revisions decode differently, run through Denken: proves the rig CAN see a
             difference, so an all-identical E2E is not two copies of one Zydis.

EXIT
  0  no Denken-visible change: E2E identical on every PE, CODE has no dk=1 row, no compile warning, control differs.
  1  something needs reading: an E2E mismatch, a dk=1 CODE row, a compile warning, or a control that no longer differs
     (the base has moved past the control's commits -- add bytes from the new delta to denken_run.cpp's RunControl).
  2  the rig itself failed (toolchain, git, a file it could not read).

Rules it keeps: everything it writes goes under OUT (default out/zydis_bump_diff/, gitignored); binaries are arguments,
never paths baked in here (CI has no game install, and installs get patched away).
"""
from __future__ import annotations

import argparse
import hashlib
import pathlib
import shutil
import subprocess
import sys
import tarfile

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parents[2]
VZ = ROOT / "vendor" / "zydis"
ZYCORE = VZ / "dependencies" / "zycore"
sys.path.insert(0, str(ROOT / "tools" / "verify"))
from build_dll import msvc_env  # noqa: E402

# The Zydis sources dll/CMakeLists.txt's decoder-only build compiles (ENCODER and FORMATTER off).
ZYDIS_SRC = ["MetaInfo.c", "Mnemonic.c", "Register.c", "SharedData.c", "String.c", "Utils.c", "Zydis.c",
             "Decoder.c", "DecoderData.c", "Segment.c"]
ZDEFS = ["/DZYDIS_STATIC_BUILD", "/DZYCORE_STATIC_BUILD", "/DZYDIS_DISABLE_ENCODER", "/DZYDIS_DISABLE_FORMATTER",
         "/DNDEBUG"]
REPO_DEFS = ["/DUNICODE", "/D_UNICODE", "/DWIN32_LEAN_AND_MEAN", "/DNOMINMAX", "/D_CRT_SECURE_NO_WARNINGS"]


class RigError(Exception):
    pass


def run(cmd, env, cwd, check=True):
    exe = shutil.which(cmd[0], path=env.get("PATH") or env.get("Path"))
    if not exe:
        raise RigError(f"{cmd[0]} is not on the vcvars PATH")
    r = subprocess.run([exe, *cmd[1:]], env=env, cwd=cwd, capture_output=True, text=True, errors="replace")
    out = (r.stdout + r.stderr).strip()
    if check and r.returncode != 0:
        print(out[-4000:])
        raise RigError(f"rc={r.returncode}: {' '.join(map(str, cmd))[:300]}")
    return out


def git(repo, *args):
    r = subprocess.run(["git", "-C", str(repo), *args], capture_output=True, text=True)
    if r.returncode != 0:
        raise RigError(f"git {' '.join(args)}: {r.stderr.strip()[:300]}")
    return r.stdout.strip()


def pinned_zydis():
    """The gitlink this repo records for vendor/zydis (not the checkout's HEAD, which a manual checkout can move)."""
    line = git(ROOT, "ls-tree", "HEAD", "vendor/zydis")
    return line.split()[2]


def zycore_gitlink(rev):
    line = git(VZ, "ls-tree", rev, "dependencies/zycore")
    return line.split()[2] if line else ""


def export_tree(out, tag, rev):
    dst = out / f"src_{tag}"
    if dst.exists():
        shutil.rmtree(dst)
    dst.mkdir(parents=True)
    tar = out / f"src_{tag}.tar"
    subprocess.run(["git", "-C", str(VZ), "archive", "--format=tar", "-o", str(tar), rev, "include", "src"], check=True)
    with tarfile.open(tar) as t:
        t.extractall(dst, filter="data")
    tar.unlink()
    return dst


def build(env, out, tag, rev):
    sha = git(VZ, "rev-parse", rev)
    src = export_tree(out, tag, rev)
    obj = out / f"obj_{tag}"
    if obj.exists():
        shutil.rmtree(obj)
    obj.mkdir()
    zinc = ZYCORE / "include"
    inc = [f"/I{src / 'include'}", f"/I{src / 'src'}", f"/I{zinc}"]
    run(["cl", "/nologo", "/c", "/O2", "/MT", "/W0", "/D_CRT_SECURE_NO_WARNINGS", *ZDEFS, *inc,
         *[str(src / "src" / f) for f in ZYDIS_SRC], f"/Fo{obj}\\"], env, obj)
    lib = out / f"zydis_{tag}.lib"
    run(["lib", "/nologo", f"/OUT:{lib}", *[str(obj / (pathlib.Path(f).stem + ".obj")) for f in ZYDIS_SRC]], env, obj)
    print(f"[{tag}] {rev} = {sha}; decoder-only lib {lib.stat().st_size:,} bytes")

    dll = out / f"zproj_{tag}.dll"
    run(["cl", "/nologo", "/O2", "/MT", "/W3", "/LD", *ZDEFS, *inc, f'/DZP_TAG="{tag}@{sha[:8]}"', f"/I{HERE}",
         str(HERE / "zproj_lib.c"), str(lib), f"/Fe{dll}", f"/Fo{obj}\\"], env, obj)

    dobj = obj / "Denken.obj"
    text = run(["cl", "/nologo", "/c", "/O2", "/MT", "/std:c++latest", "/utf-8", "/W4", "/permissive-", "/EHa",
                "/wd4201", *REPO_DEFS, *ZDEFS, f"/I{src / 'include'}", f"/I{zinc}",
                str(ROOT / "dll" / "src" / "Denken.cpp"), f"/Fo{dobj}"], env, obj)
    warnings = [line for line in text.splitlines() if "warning" in line.lower()]
    print(f"[{tag}] COMPILE CHECK dll/src/Denken.cpp: {len(warnings)} warning(s)"
          + "".join("\n    " + w for w in warnings))
    exe = out / f"denken_{tag}.exe"
    run(["cl", "/nologo", "/O2", "/MT", "/std:c++latest", "/utf-8", "/W3", "/EHa", "/wd4201", *REPO_DEFS, *ZDEFS,
         f"/I{ROOT / 'dll' / 'src'}", f"/I{HERE}", f"/I{src / 'include'}", f"/I{zinc}",
         str(HERE / "denken_run.cpp"), str(dobj), str(lib), f"/Fe{exe}", f"/Fo{obj}\\"], env, obj)
    return {"sha": sha, "dll": dll, "exe": exe, "lib_bytes": lib.stat().st_size, "warnings": len(warnings)}


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def run_exe(exe, *args):
    r = subprocess.run([str(exe), *map(str, args)], capture_output=True, text=True, errors="replace")
    if r.returncode != 0:
        raise RigError(f"{exe.name} {' '.join(map(str, args))[:200]} rc={r.returncode}: {(r.stdout + r.stderr)[-600:]}")
    return r.stdout.strip()


def dk_rows(tsv):
    n = 0
    with open(tsv, encoding="utf-8", errors="replace") as f:
        for line in f:
            cols = line.split("\t")
            if len(cols) > 3 and cols[3] == "1":
                n += 1
    return n


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("pe", nargs="+", help="x64 PE files to sweep (a game's Shipping exe, a DumperTest fixture)")
    ap.add_argument("--base", help="base revision (default: the gitlink this repo pins)")
    ap.add_argument("--candidate", default="origin/master", help="candidate revision (default: origin/master)")
    ap.add_argument("--all-offsets", action="store_true", help="also diff every byte offset of executable sections")
    ap.add_argument("--out", default=str(ROOT / "out" / "zydis_bump_diff"), help="scratch folder (gitignored)")
    a = ap.parse_args()
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(errors="backslashreplace")

    try:
        base = a.base or pinned_zydis()
        for p in a.pe:
            if not pathlib.Path(p).is_file():
                raise RigError(f"no such file: {p}")
        checkout_zycore = git(ZYCORE, "rev-parse", "HEAD")
        for rev in (base, a.candidate):
            want = zycore_gitlink(rev)
            if want != checkout_zycore:
                raise RigError(f"zydis {rev} pins zycore {want[:12]} but the checkout holds {checkout_zycore[:12]}: "
                               "this rig compiles against the checkout's zycore, so the comparison would not be the "
                               "bump. Read the zycore delta first.")
        out = pathlib.Path(a.out)
        out.mkdir(parents=True, exist_ok=True)
        env = msvc_env()
        env["VSLANG"] = "1033"   # English tool messages: localized ones do not survive a cp950 console
        banner = subprocess.run([shutil.which("cl", path=env.get("PATH") or env.get("Path")) or "cl"], env=env,
                                capture_output=True, text=True, errors="replace").stderr.strip().splitlines()
        print("compiler:", banner[0] if banner else "?")   # cl prints its version on stderr, its usage on stdout
        b = build(env, out, "pin", base)
        c = build(env, out, "master", a.candidate)
        if b["sha"] == c["sha"]:
            raise RigError("base and candidate are the same commit")
        print(f"decoder-only lib growth: {c['lib_bytes'] - b['lib_bytes']:+,} bytes")
        zdiff = out / "zdiff.exe"
        run(["cl", "/nologo", "/O2", "/MT", "/W3", f"/I{HERE}", str(HERE / "zdiff.c"), f"/Fe{zdiff}", f"/Fo{out}\\"],
            env, out)

        needs_reading = []
        if b["warnings"] or c["warnings"]:
            needs_reading.append("a Denken.cpp compile warning")

        ctl_b = run_exe(b["exe"], "--control").splitlines()
        ctl_c = run_exe(c["exe"], "--control").splitlines()
        differing = sum(1 for x, y in zip(ctl_b, ctl_c) if x != y)
        print(f"CONTROL: {differing} of {len(ctl_b)} control case(s) differ between the revisions")
        for x, y in zip(ctl_b, ctl_c):
            if x != y:
                print(f"    base:      {x}\n    candidate: {y}")
        if differing == 0:
            needs_reading.append("a positive control that no longer differs (add bytes from the new delta)")

        for i, pe in enumerate(a.pe):
            name = pathlib.Path(pe).name
            eb, ec = out / f"e2e_{i}_pin.txt", out / f"e2e_{i}_master.txt"
            sb = run_exe(b["exe"], pe, eb)
            run_exe(c["exe"], pe, ec)
            same = sha256(eb) == sha256(ec)
            print(f"E2E  {name}: {sb} -> {'IDENTICAL' if same else 'DIFFERENT'} (sha256 {sha256(eb)[:12]})")
            if not same:
                needs_reading.append(f"an E2E difference on {name} ({eb.name} vs {ec.name})")
            modes = ["code"] + (["all"] if a.all_offsets else [])
            for mode in modes:
                tsv = out / f"zd_{mode}_{i}.tsv"
                stats = run_exe(zdiff, b["dll"], c["dll"], pe, mode, tsv)
                dk = dk_rows(tsv)
                last = [line for line in stats.splitlines() if line.startswith(mode)]
                print(f"{mode.upper():4s} {name}: {last[-1] if last else stats.splitlines()[-1]}; Denken-visible rows {dk}")
                if mode == "code" and dk:
                    needs_reading.append(f"{dk} Denken-visible per-instruction change(s) in {name} ({tsv.name}); "
                                         f"group them with summarize.py {tsv} --dk-only")
    except RigError as e:
        print(f"RIG FAILED: {e}")
        return 2

    if needs_reading:
        print("\nNEEDS READING:\n  - " + "\n  - ".join(needs_reading))
        return 1
    print("\nNO DENKEN-VISIBLE CHANGE: the candidate decodes nothing Denken reads differently on these binaries.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
