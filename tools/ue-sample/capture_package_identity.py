#!/usr/bin/env python3
"""Record WHICH package a DumperTest test session was run against.

WHY THIS EXISTS
  The packaged binary is deliberately not in git (583 MB per config against a 180 MB
  .git -- see README.md). That decision is right, and it leaves one hole: a stale
  package silently tests yesterday's property zoo. The failure is nasty because it does
  not look like staleness -- it looks like the dumper reading the wrong value.

  Same shape as tools/ghidra/identity/ for the AOB corpus: the artifact lives outside,
  the small thing that VERIFIES it lives in the repo.

  It also records which names are absent on purpose. RawInt/RawFloat/RawDouble are the
  non-UPROPERTY holes the Native-C scan has to find; if they ever START appearing in the
  binary's name tables, someone has added a UPROPERTY to them and that test is dead.

THE SOURCE HASH, and why the first version of this file was not good enough.
  v1 recorded `source_commit` from `git rev-parse HEAD`. That is a claim about MY working
  tree at capture time, not about what the binary was built from -- and it read as if it
  were the latter. On 2026-08-05 the very first packaged build was tested with a STALE
  DumperTestActor.cpp (the live project still held 退 where the repo had 走), the record
  said `8b812a5`, and nothing noticed. The report and the reality computed by different
  code paths, in the tool built to prevent exactly that.
  So the record now hashes the SOURCE FILES, and --project compares the tree the package
  was actually built from against this repo's copy. Pass it; a commit id cannot.

Usage:
  py tools/ue-sample/capture_package_identity.py "D:\\path\\to\\packaged"
  py tools/ue-sample/capture_package_identity.py <root> --project "D:\\Unreal Projects\\DumperTest"
  py tools/ue-sample/capture_package_identity.py <root> --check    # compare, don't write
Exit 0 = written / matches, 1 = mismatch, drift, or a problem in the record.
"""
import argparse
import datetime
import hashlib
import json
import os
import re
import subprocess
import sys

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "package-identity.json")

# Reflected names that MUST be present, and raw members that must NOT be.
# Class names are emitted as UTF-16 (TEXT() in the UHT registration), property names as
# narrow strings in FPropertyParams -- so both encodings have to be searched. Checking
# only ASCII makes every class name look missing from a Shipping build.
MUST_EXIST = ["DumperTestActor", "DumperTestSubsystem", "DumperTestPayload",
              "Text_Even2_TwoNull", "Opt_Int_Unset", "FrozenInt"]

# The ABSENT list asks a NARROWER question than the EXIST list, and must be counted
# differently or it answers a question nobody asked. "Is RawInt reflected?" is answered
# by the NARROW/ASCII string in FPropertyParams -- the same fact the comment above
# states. A UTF-16 hit is a TEXT() literal, which proves the opposite: the member is
# being PRINTED, i.e. it exists as a plain C++ field, which is exactly what the sample
# wants. Counting both encodings made DumperTestHUD.cpp's own readout
# ("RawInt_Ticking=%d  RawFloat_Ticking=%.3f  RawDouble_Ticking=%.3f") declare the
# Native-C test dead on a perfectly correct package -- every build since commit
# b3d8593 would have failed this check.
# The word-boundary guard is the second half: without it "RawInt" matches inside
# "RawInt_Ticking" even in ASCII, so a future ASCII readout would revive the same bug.
MUST_BE_ABSENT = ["RawInt", "RawFloat", "RawDouble"]


def find_exe(root, config):
    base = os.path.join(root, config, "Windows")
    for dirpath, _, files in os.walk(base):
        if os.path.basename(dirpath).lower() == "win64":
            for f in files:
                if f.lower().endswith(".exe"):
                    return os.path.join(dirpath, f)
    return None


def probe(path):
    with open(path, "rb") as fh:
        data = fh.read()

    def count_either(name):
        """Both encodings -- for MUST_EXIST, where either rendering proves presence."""
        return data.count(name.encode("ascii")) + data.count(name.encode("utf-16-le"))

    def count_reflected(name):
        """ASCII only, whole word -- for MUST_BE_ABSENT. See the MUST_BE_ABSENT note."""
        pat = (rb"(?<![A-Za-z0-9_])" + re.escape(name.encode("ascii"))
               + rb"(?![A-Za-z0-9_])")
        return len(re.findall(pat, data))

    return {
        "file": os.path.basename(path),
        "size": len(data),
        "sha256": hashlib.sha256(data).hexdigest(),
        "reflected_present": {n: count_either(n) for n in MUST_EXIST},
        "raw_absent": {n: count_reflected(n) for n in MUST_BE_ABSENT},
    }


# ⛔ THIS LIST IS BOTH THE CONTENT HASH *AND* THE STALENESS CHECK. A file that is mirrored but
# missing from here is INVISIBLE to drift detection twice over: `hash_sources` will not notice its
# content changing, and the "STALE BINARY" mtime scan will not notice it being edited after a
# package was built. The record then says "in sync" while the packaged binary genuinely differs —
# a false negative in the one tool whose entire job is catching that.
#
# ⚠ Found 2026-09-06: `DumperTestHUD.h/.cpp` had been mirrored but never listed. They are not
# incidental — README.md says the HUD readout *is* the sample's health check, and it is the only
# way to learn the values of the non-UPROPERTY raw fields (there is no reflection to ask), so a
# silent HUD drift breaks precisely the rows that have no other oracle. `DumperTest.Build.cs` was
# not even mirrored; it defines the module, and Batch-2 work adds link libraries to it.
#
# ⚠ Adding names CHANGES source_sha256, so the stored record reads as drift exactly once. That is
# the same one-off the LF-normalisation note below describes — re-capture to settle it.
#
# The rule: this list must equal the contents of tools/ue-sample/DumperTest/Source/DumperTest/.
# Anything deliberately excluded belongs in EXCLUDED below, with the reason.
SOURCES = ["DumperTestActor.h", "DumperTestActor.cpp", "DumperTestTypes.h",
           "DumperTestSubsystem.h", "DumperTestSubsystem.cpp",
           "DumperTestHUD.h", "DumperTestHUD.cpp",
           "DumperTest.Build.cs"]

# Template files the project generates and we never touch (DumperTest.cpp/.h,
# DumperTestCharacter.*, DumperTestGameMode.*). README.md rule 4 says they are deliberately NOT
# mirrored; listing them here would make every stock-template regeneration look like sample drift.
EXCLUDED = ("DumperTest.cpp", "DumperTest.h", "DumperTestCharacter.cpp", "DumperTestCharacter.h",
            "DumperTestGameMode.cpp", "DumperTestGameMode.h")


def hash_sources(src_dir):
    """One digest over the five sample sources, name-and-content, in fixed order.

    Name included so a rename cannot collide, order fixed so the digest is stable.

    LINE ENDINGS ARE NORMALISED TO LF BEFORE HASHING, and that is load-bearing rather
    than tidy. The two trees this digest compares are reached by different transports:
    the repo copy arrives via `git checkout`, which rewrites EOLs whenever
    `core.autocrlf=true` and no `.gitattributes` pins them, while the UE project copy
    is usually a plain folder copy that preserves whatever bytes it was created with.
    Hashing raw bytes therefore reported "STALE PACKAGE" for two byte-different
    renderings of IDENTICAL content -- measured 2026-08-12, where every one of the five
    files differed from its counterpart by exactly its own line count.

    NOTE FOR ANYONE COMPARING AGAINST AN OLD RECORD: this changes the digest. Records
    written before this normalisation carry the raw-bytes value and will read as drift
    once; re-capture to settle it.
    """
    h = hashlib.sha256()
    missing = []
    for name in SOURCES:
        p = os.path.join(src_dir, name)
        if not os.path.exists(p):
            missing.append(name)
            continue
        h.update(name.encode("utf-8"))
        with open(p, "rb") as fh:
            h.update(fh.read().replace(b"\r\n", b"\n"))
    return h.hexdigest(), missing


def redact(text, args):
    """Replace this machine's absolute paths with placeholders before the record is written.

    ⛔ WHY THIS LIVES IN THE GENERATOR. The committed record used to carry `<UEPROJECTS>`,
    `<CORPUS>` and `<DRIVE>` placeholders — put there BY HAND during the 2026-09-06 leak sweep.
    This tool knew nothing about them, so the very next capture wrote the raw paths straight back
    and silently undid the redaction. That is exactly the failure `docs/todo.md` names in its own
    words: *"After hand-editing a generated file, back-port or the next --apply is a regression."*
    Three stale generators were found in one day by that pattern; this was the fourth.

    ⚠ The placeholders are not decoration. `package-identity.json` is COMMITTED and public, and
    an absolute path is exactly the shape the machine-name / account-name sweep spent a day
    removing from this repo.

    Longest-first, so a nested root cannot be half-replaced by its parent.
    """
    subs = []
    if getattr(args, "project", None):
        subs.append((os.path.dirname(os.path.abspath(args.project)), "<UEPROJECTS>"))
        subs.append((os.path.abspath(args.project), "<PROJECT>"))
    if getattr(args, "root", None):
        # the corpus root is the package root's parent (…\For Testing\DumperTest -> …\For Testing)
        subs.append((os.path.dirname(os.path.abspath(args.root)), "<CORPUS>"))
    for pf in (os.environ.get("ProgramFiles"), os.environ.get("ProgramFiles(x86)")):
        if pf and len(pf) > 3:
            subs.append((os.path.splitdrive(pf)[0] + os.sep, "<DRIVE>" + os.sep))
    for home in (os.environ.get("USERPROFILE"),):
        if home:
            subs.append((home, "<HOME>"))

    for real, token in sorted(subs, key=lambda s: -len(s[0])):
        for form in (real, real.replace("\\", "\\\\"), real.replace("\\", "/")):
            if not form:
                continue
            tok = token.replace("\\", "\\\\") if "\\\\" in form else token
            # case-insensitive on Windows, where the same path arrives in several casings
            i = 0
            low, flow = text.lower(), form.lower()
            while True:
                j = low.find(flow, i)
                if j < 0:
                    break
                text = text[:j] + tok + text[j + len(form):]
                low = text.lower()
                i = j + len(tok)
    return text


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("root", help="folder holding Development/ and Shipping/")
    ap.add_argument("--project", default=None,
                    help="the UE project the package was built from; its sources are "
                         "compared against this repo's copy")
    ap.add_argument("--check", action="store_true", help="compare against the stored record")
    args = ap.parse_args()

    repo = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    try:
        commit = subprocess.run(["git", "-C", repo, "rev-parse", "--short", "HEAD"],
                                capture_output=True, text=True).stdout.strip()
    except OSError:
        commit = "unknown"

    repo_src = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                            "DumperTest", "Source", "DumperTest")
    repo_hash, repo_missing = hash_sources(repo_src)

    record = {
        "engine": "UE 5.4",
        # A claim about the working tree at capture time. Informative only -- the
        # source_sha256 below is what actually binds the record to a source state.
        "source_commit": commit,
        "source_sha256": repo_hash,
        "captured_utc": datetime.datetime.now(datetime.timezone.utc)
                                 .replace(microsecond=0).isoformat(),
        "build_command": (r'"C:\Program Files\Epic Games\UE_5.4\Engine\Build\BatchFiles\Build.bat" '
                          r'DumperTestEditor Win64 Development -Project="<project>\DumperTest.uproject" '
                          r'-WaitMutex'),
        "packaged_root": args.root,
        "configs": {},
        "problems": [],
    }

    for name in repo_missing:
        record["problems"].append("repo source '%s' is missing -- the record cannot bind "
                                  "the package to a source state" % name)

    # Which tree may be used for the temporal (mtime) freshness check below. ONLY the
    # tree the compiler actually read qualifies -- see the long note at the check itself.
    mtime_src = None

    if args.project:
        proj_src = os.path.join(args.project, "Source", "DumperTest")
        proj_hash, proj_missing = hash_sources(proj_src)
        record["built_from"] = {"path": proj_src, "source_sha256": proj_hash}
        if not proj_missing:
            mtime_src = proj_src
        for name in proj_missing:
            record["problems"].append("project source '%s' is missing from %s" % (name, proj_src))
        if not proj_missing and proj_hash != repo_hash:
            record["problems"].append(
                "STALE PACKAGE: the project this was built from does not match the repo "
                "sources (project %s vs repo %s). Copy tools/ue-sample/DumperTest/Source/"
                "DumperTest/* over and re-package, or the values you are about to verify "
                "are not the ones documented." % (proj_hash[:12], repo_hash[:12]))

    # Police EVERY config that is actually packaged, not a hardcoded pair.
    #
    # This loop used to read `("Development", "Shipping")`, which left **DebugGame
    # completely unpoliced**: it could be stale, or built from different sources, or
    # missing a fixture entirely, and nothing here would have said so. Found 2026-08-24,
    # when all three configs were rebuilt and only two of them could be attested.
    #
    # Discovery rather than a longer hardcoded list, because the failure mode of a list is
    # exactly what happened: a config gets added to the packaging step and nobody thinks to
    # add it here. `--check` already compares the stored config SET against the observed one
    # (`want` vs `have` below), so a config that appears or disappears is caught either way.
    #
    # The canonical two are still DEMANDED: discovery must not turn "Development vanished"
    # into a quietly smaller, still-passing record.
    KNOWN_CONFIGS = ("Development", "Shipping", "DebugGame", "Test")
    present = [c for c in KNOWN_CONFIGS if os.path.isdir(os.path.join(args.root, c))]
    for required in ("Development", "Shipping"):
        if required not in present:
            record["problems"].append(
                "%s: config directory is missing from %s -- the package is incomplete"
                % (required, args.root))

    for cfg in present:
        exe = find_exe(args.root, cfg)
        if not exe:
            record["problems"].append("%s: no Binaries/Win64 exe found" % cfg)
            continue
        info = probe(exe)
        # Was this exe produced AFTER the sources it claims to embody?
        #
        # Note for anyone who remembers R8: that finding was rejected for using mtime as a
        # proxy for CONTENT freshness, which it is not. Here the question is literally
        # temporal -- "was the binary produced after this edit?" -- and mtime is the direct
        # answer, not a stand-in for one. Different question, so the same tool is right.
        #
        # BUT IT IS ONLY THE RIGHT ANSWER FOR THE TREE THE COMPILER READ. This check used
        # to run against `repo_src`, and that made it fire on every machine that is not the
        # one that did the build: `git checkout` stamps files with the CHECKOUT time and
        # deliberately never preserves the author's mtime, while a folder copy of the
        # package usually DOES preserve the exe's -- so a freshly cloned repo always looks
        # "newer" than a correct older package. Measured 2026-08-12: all five repo sources
        # carried one identical checkout stamp six days AFTER the exes, and both configs
        # were reported STALE while the same comparison against the build tree passed.
        # So: only compare against the project tree, and when there is no project tree to
        # compare against, say the check was skipped rather than inventing a verdict.
        exe_mtime = os.path.getmtime(exe)
        info["exe_mtime"] = datetime.datetime.fromtimestamp(
            exe_mtime, datetime.timezone.utc).replace(microsecond=0).isoformat()
        if mtime_src is None:
            record.setdefault("checks_skipped", [])
            note = ("freshness (mtime) check skipped: pass --project <the UE project this "
                    "package was built from> to enable it. The repo working tree cannot "
                    "stand in -- git stamps checkout time, not edit time.")
            if note not in record["checks_skipped"]:
                record["checks_skipped"].append(note)
        else:
            newest_src, newest_name = 0.0, None
            for name in SOURCES:
                p = os.path.join(mtime_src, name)
                if os.path.exists(p) and os.path.getmtime(p) > newest_src:
                    newest_src, newest_name = os.path.getmtime(p), name
            if newest_name and newest_src > exe_mtime:
                record["problems"].append(
                    "STALE BINARY: %s was built before '%s' was last edited in %s -- "
                    "re-package before trusting any value from it"
                    % (cfg, newest_name, mtime_src))
        for n, c in info["reflected_present"].items():
            if c == 0:
                record["problems"].append("%s: reflected name '%s' is MISSING from the binary" % (cfg, n))
        for n, c in info["raw_absent"].items():
            if c:
                record["problems"].append(
                    "%s: '%s' appears %d time(s) -- it is supposed to be a NON-UPROPERTY hole; "
                    "someone reflected it and the Native-C test is dead" % (cfg, n, c))
        record["configs"][cfg] = info

    text = redact(json.dumps(record, indent=2) + "\n", args)
    if args.check:
        if not os.path.exists(OUT):
            print("no stored record at %s" % OUT)
            return 1
        stored = json.load(open(OUT, encoding="utf-8"))

        # An ABSENT config is not a match. `drift` iterates the freshly computed configs,
        # so a root with no exes at all produces an empty dict, an empty drift list, and
        # -- before this guard -- a cheerful "package matches the stored identity" with
        # exit 0. A typo in the path, a half-copied package or a not-yet-archived cook all
        # land there, and the caller is told the opposite of the truth. Compare the SETS
        # first, then the hashes.
        want = set(stored.get("configs", {}))
        have = set(record["configs"])
        missing = sorted(want - have)
        if missing:
            print("package INCOMPLETE under %s -- the record has %s but no exe was found "
                  "for %s" % (args.root, ", ".join(sorted(want)), ", ".join(missing)))
            return 1
        if not have:
            print("no packaged exe found under %s (and the stored record names none "
                  "either) -- nothing was compared" % args.root)
            return 1

        drift = [c for c in record["configs"]
                 if stored.get("configs", {}).get(c, {}).get("sha256") != record["configs"][c]["sha256"]]
        if drift:
            print("package DRIFT in %s -- rebuilt since the record was written" % ", ".join(drift))
            return 1

        # --check computes `problems` and used to throw them away, so a package could be
        # "matching" and unusable at the same time. Report them; they do not change the
        # exit code, which stays a statement about identity.
        print("package matches the stored identity")
        for p in record["problems"]:
            print("  PROBLEM (identity still matches): %s" % p)
        for note in record.get("checks_skipped", []):
            print("  NOTE: %s" % note)
        return 0

    with open(OUT, "w", encoding="utf-8", newline="") as fh:
        fh.write(text)
    print("wrote %s" % OUT)
    for p in record["problems"]:
        print("  PROBLEM: %s" % p)
    for note in record.get("checks_skipped", []):
        print("  NOTE: %s" % note)
    return 1 if record["problems"] else 0


if __name__ == "__main__":
    sys.exit(main())
