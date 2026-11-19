#!/usr/bin/env python3
r"""Rebuild a corpus `.rep` from the archived binary and prove the rebuild is the same project.

    py tools/ghidra/reimport_verify.py UE4.10-Game UE4.26-Satisfactory
    py tools/ghidra/reimport_verify.py --all --keep
    py tools/ghidra/reimport_verify.py UE4.27-DQ7R --projs D:/Tools/GHIDRA_REIMPORT

WHY. `docs/todo.md` carries a standing rule: no `.rep` may be deleted until a re-import is
demonstrated end to end, because *"the inputs exist"* is not *"a re-import reproduces the `.rep`"*.
`corpus_relocate.py` proves the first (57/57 binaries present by hash). This proves the second, or
finds where it fails.

WHAT IT COMPARES, AND WHY EACH ONE IS NEEDED. A rebuild is accepted only if all three agree —
they fail independently, and any one alone would pass a rebuild that had lost something:

  1. IDENTITY (`dump_identity.java`) — Ghidra's recorded executable MD5/SHA256, the function /
     symbol / defined-data counts, and a **SHA-256 over every (address, name, type, scope, source)
     in the symbol table**. The digest is the load-bearing part: matching COUNTS would pass a
     rebuild that put the right number of symbols at the wrong addresses, which is precisely the
     failure a mismatched-but-same-named PDB produces.
  2. MEMORY (`dump_blocks.java`) — image base, complete block map, per-block MD5.
  3. SWEEP (`scan_patterns.java`) — byte-identical scan/consensus output under the row's own
     `GS_TRUE`, so the rebuild is graded on the answers the corpus actually exists to give.

WHAT IT DOES NOT PROVE. By default it rebuilds with `-noanalysis`, which is how some of the
corpus's rows are imported. A project that was additionally **disassembled** (`Analyzed=true`,
non-zero instruction count — `dump_identity.java` reports this) carries analysis output that a
default run does not attempt to reproduce; `--analyze` does, at minutes per binary. It also says
nothing about decompiler output or the xref index, for which no digest exists.

PROVE IT CAN FAIL BEFORE TRUSTING A PASS. Two negative controls, each of which must fail:

    # N1 — a same-named binary from a DIFFERENT build must be rejected. Point a row at its
    # sibling build and re-run; expect MISMATCH on the executable hash, the symbol digest, the
    # PDB GUID and the block map.
    py - <<'P'
    import io
    o = io.open("out/pe-corpus-map.tsv", encoding="utf-8").read()
    io.open("out/nc-map.tsv", "w", encoding="utf-8", newline="").write(
        o.replace(r"\ES2\5.5-0517 (4415922863161237626)\\", r"\ES2\5.5 (735055807809773736)\\"))
    P
    py tools/ghidra/reimport_verify.py UE5.5-Everspace2 --map out/nc-map.tsv \
        --projs D:/Tools/GHIDRA_NC --out out/re-N1

    # N2 — a truncated baseline must fail CLOSED, not compare equal on absent fields.
    mkdir -p out/nc-identity && cp out/identity/identity_*.tsv out/nc-identity/
    : > "out/nc-identity/identity_UE4.10-Game__UE4Game-Win64-Shipping.exe@140000000.tsv"
    py tools/ghidra/reimport_verify.py UE4.10-Game --orig-identity out/nc-identity --out out/re-N2

N1 is the one that earns the pass — it is the only check that distinguishes "this binary rebuilds"
from "some binary with the right name rebuilds". It also exercises `stage_for_import`, because
the sibling build's folder name carries parentheses.
"""
import argparse
import collections
import io
import os
import re
import shutil
import subprocess
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pe_sweep import parse_rows                                       # noqa: E402

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
GHIDRA_HOME = os.environ.get("GHIDRA_HOME", r"D:/Tools/ghidra_12.1.2_PUBLIC")
HL = os.path.join(GHIDRA_HOME, "support", "analyzeHeadless.bat")
SCRIPTS = os.path.join(REPO, "tools", "ghidra")

# Fields that MUST differ between an original and a rebuild, or that say nothing about identity.
# Every one is excluded deliberately; nothing is excluded because it happened to disagree.
IGNORE_EXACT = {
    "tag",                       # the rebuild is labelled separately on purpose
    "meta.Date Created",         # a rebuild is by definition created later
    "meta.FSRL",                 # carries the source path
    "meta.Executable Location",  # ditto, and the corpus's recorded paths are stale anyway
    "has_pdb_applied",           # derived label (a function-count heuristic), not identity
    "analysis_state",            # derived label; compared explicitly below instead
    # A REBUILD IS SUPPOSED TO BE ON THE CURRENT GHIDRA. Grading a version difference as a
    # MISMATCH does not scale: on 12.2 or 13.0 EVERY project would suddenly be
    # "irreproducible" against a pinned original. The version an artifact was born on is not a
    # property worth preserving — the strategy is to rebuild on the installed Ghidra and, if a new
    # release breaks something, stay on the working version until it is fixed. Reported as an
    # informational line below, never as a failure.
    "meta.Created With Ghidra Version",
}
IGNORE_PREFIX = ("meta.SourceFile",)   # hundreds of PDB build-tree paths; provenance, not identity
RIP_SLACK = (1 << 31) + 0x10000        # see compare_block_maps


def load_kv(path):
    d = {}
    with io.open(path, encoding="utf-8", errors="replace") as f:
        for line in f:
            k, _, v = line.rstrip("\n").rstrip("\r").partition("\t")
            d[k] = v
    return d


def load_map(path):
    out = collections.defaultdict(list)
    if not os.path.exists(path):
        return out
    with io.open(path, encoding="utf-8") as f:
        f.readline()
        for line in f:
            c = line.rstrip("\n").split("\t")
            if len(c) >= 4:
                out[c[0]].append((c[1], c[2], c[3]))
    return out


def run(cmd, log_path, env=None):
    e = dict(os.environ)
    e["_JAVA_OPTIONS"] = "-Xmx%s" % os.environ.get("SWEEP_XMX", "14G")
    if env:
        e.update(env)
    with io.open(log_path, "w", encoding="utf-8", errors="replace") as lf:
        return subprocess.call(cmd, stdout=lf, stderr=subprocess.STDOUT, env=e)


def sanitize(n):
    return "".join(c if (c.isascii() and (c.isalnum() or c in "._-")) else "_" for c in n)


def stage_for_import(path, staging):
    """-> a path `analyzeHeadless.bat` can actually open.

    `analyzeHeadless.bat` is a cmd batch file, and cmd's parser breaks on `(` / `)` in an argument
    even inside double quotes — it fails the same way from bash, from `subprocess`, and from a
    plain shell, so it is the launcher, not the caller. The archive's own folder naming can carry
    parentheses (both ES2 rows live under `…\\ES2\\5.5 (735055807809773736)\\`), and no amount of
    care in this script lets such a path be imported as it stands.

    8.3 short names are the textbook workaround and are DISABLED on this volume (the short path
    comes back identical to the long one), so the fix is to hardlink into a clean directory —
    instant and free on the same NTFS volume, unlike a copy of a 169 MB binary. The PDB is linked
    too: analysis finds it by sitting beside the executable, and staging only the exe would produce
    a rebuild silently missing its symbols."""
    if not any(c in path for c in "()"):
        return path
    os.makedirs(staging, exist_ok=True)
    dst = os.path.join(staging, os.path.basename(path))
    for src, out in ((path, dst),
                     (os.path.splitext(path)[0] + ".pdb", os.path.splitext(dst)[0] + ".pdb")):
        if not os.path.exists(src):
            continue
        if os.path.exists(out):
            os.remove(out)
        try:
            os.link(src, out)                      # same volume: instant, no extra space
        except OSError:
            shutil.copy2(src, out)                 # different volume, or links unavailable
    return dst


def compare_identity(orig, new):
    """-> (dict of per-dimension verdicts, [messages]).

    Graded per dimension, NOT as one boolean, because the corpus is mostly DISASSEMBLED projects
    and the rebuild is `-noanalysis`. A single pass/fail would report those as plain mismatches and
    bury the fact that the binary, the memory image and the sweep answers all matched — which is
    the part that decides whether the archived inputs are sufficient. Analysis output is
    regenerable on demand and nothing reads it from a stored .rep (GROUND-TRUTH.md: the ten scripts
    that need analysis are all authoring tools, and it advises analysing into a throwaway project).
    """
    a, b = load_kv(orig), load_kv(new)
    msgs, v = [], {}

    # FAIL CLOSED ON AN EMPTY BASELINE. `dump_identity.java` opens its writer before it does any
    # work, so a JVM killed mid-run leaves a correctly-named 0-byte file. That parses to {}, and
    # every comparison below is `a.get(k) == b.get(k)` — which on a missing key compares None to
    # None and PASSES. A truncated baseline would silently grade as a perfect rebuild, so the one
    # field that must always be present is checked first and refuses instead.
    for label, d, p in (("original", a, orig), ("rebuild", b, new)):
        if not d.get("symbols_sha256"):
            return ({"input": False}, ["%s identity record has no symbols_sha256 — truncated or "
                                       "0-byte, nothing can be compared against it: %s"
                                       % (label, p)])

    orig_analysed = (a.get("instructions") or "0") != "0"

    def eq(*keys):
        return all(a.get(k) == b.get(k) for k in keys)

    v["input"] = eq("meta.Executable MD5", "meta.Executable SHA256")
    if not v["input"]:
        msgs.append("EXECUTABLE HASH DIFFERS: orig md5=%s rebuild md5=%s — the archived file is "
                    "NOT the one this project was built from"
                    % (a.get("meta.Executable MD5"), b.get("meta.Executable MD5")))
    v["symbols"] = eq("symbols_sha256")
    if not v["symbols"]:
        msgs.append("SYMBOL DIGEST DIFFERS: orig=%s (%s symbols) rebuild=%s (%s symbols)"
                    % (a.get("symbols_sha256"), a.get("symbols"),
                       b.get("symbols_sha256"), b.get("symbols")))
    gv_a, gv_b = a.get("meta.Created With Ghidra Version"), b.get("meta.Created With Ghidra Version")
    if gv_a and gv_b and gv_a != gv_b:
        msgs.append("note: original was built by Ghidra %s, rebuild by %s — expected, and the "
                    "point: the rebuild is on the CURRENT toolchain" % (gv_a, gv_b))
    v["analysis"] = (not orig_analysed) or ((b.get("instructions") or "0") != "0")
    analysis_gap = not v["analysis"]
    if analysis_gap:
        msgs.append("original was DISASSEMBLED (%s instructions, %s functions, PDB Loaded=%s); "
                    "the -noanalysis rebuild has none. Analysis output is NOT reproduced by this "
                    "run — use --analyze to attempt it."
                    % (a.get("instructions"), a.get("functions"), a.get("meta.PDB Loaded")))

    # Everything else, so a difference nobody anticipated still surfaces instead of passing.
    # On a disassembled original every analysis-derived field differs BY CONSTRUCTION when the
    # rebuild is -noanalysis; those are listed in the messages above and excluded from the verdict
    # here, never dropped from the report. `meta.Maximum Address` is in this set because loading a
    # PDB adds Ghidra's synthetic `tdb` block at 0xff00000000, which moves the maximum address.
    derived = {"functions", "instructions", "defined_data", "datatypes", "symbols",
               "symbols_global", "symbols_sha256", "meta.Analyzed", "meta.PDB Loaded",
               "meta.RTTI Found", "meta.Should Ask To Analyze", "meta.Maximum Address"}
    other = []
    for k in sorted(set(a) | set(b)):
        if k in IGNORE_EXACT or k.startswith(IGNORE_PREFIX) or a.get(k) == b.get(k):
            continue
        # Ghidra's metadata map is inconsistent about unset values: some keys are omitted, some
        # are present with an empty string, and which one you get varies between program states,
        # so a key such as `meta.Preferred Root Namespace Category` can differ with BOTH sides
        # empty. Absent and empty both mean unset.
        if k.startswith("meta.") and not a.get(k) and not b.get(k):
            continue
        if analysis_gap and (k in derived or k.startswith("symbols_source.")
                             or k.startswith("meta.# of")):
            continue
        # A `meta.*` key the analysed ORIGINAL has and the -noanalysis rebuild does not AT ALL is
        # analysis output by construction — an analyzer wrote it and none ran here. Detecting that
        # by ASYMMETRY rather than by name is the point: a hand-maintained list like `derived`
        # above misses keys such as `meta.MinGW Relocations`, grading an equivalent row a
        # MISMATCH, and enumerating one more name would only defer the next miss.
        # Keys present on BOTH sides and differing still fail, so this cannot hide a real change.
        if analysis_gap and k.startswith("meta.") and a.get(k) and not b.get(k):
            continue
        # `(x or "")` would render an ABSENT key and an EMPTY one both as '', so a real
        # difference would print as a baffling both-sides-'' line. Show absence as absence.
        def shown(d):
            return "<absent>" if k not in d else repr(d[k][:50])
        other.append("%s: orig=%s rebuild=%s" % (k, shown(a), shown(b)))
    # BOOKKEEPING TOTALS. A full --analyze rebuild can match the original on the symbol-table
    # DIGEST and on every function / instruction / defined-data count and still differ in the
    # data-type count and Ghidra's own symbol total. That is not analysis non-determinism (two
    # rebuilds of one input agree field for field); it is history the original carries that a
    # clean rebuild does not. Kept as its own outcome rather than folded
    # into a pass, and the set is deliberately tiny: any OTHER differing field is still a mismatch.
    v["bookkeeping_only"] = bool(other) and all(
        k.split(":")[0] in ("datatypes", "meta.# of Data Types", "meta.# of Symbols")
        for k in other)
    v["other"] = not other
    msgs += other
    if analysis_gap:
        v["symbols"] = None          # not a meaningful comparison against a -noanalysis rebuild
    return v, msgs


def compare_block_maps(orig, new):
    """Two `dump_blocks.java` TSVs, compared per block by MD5.

    Not a plain file diff, because loading a PDB makes Ghidra synthesize a `tdb` block at
    0xff00000000 that a `-noanalysis` import does not have. That block is non-executable and sits
    far outside any reachable RIP displacement, so no scan can observe it — the same COMPUTED
    criterion `check_pe_memory.py` uses, not an exception list."""
    def parse(p):
        out, hdr = {}, None
        for line in io.open(p, encoding="utf-8", errors="replace"):
            line = line.rstrip("\n").rstrip("\r")
            if line.startswith("# "):
                continue
            c = line.split("\t")
            if hdr is None:
                hdr = c
                continue
            d = dict(zip(hdr, c))
            out[(int(d["start"], 16), int(d["size"]))] = d
        return out
    a, b = parse(orig), parse(new)
    ex = [(s, s + n) for (s, n), d in a.items() if d["exec"] == "1"]
    lo = min(s for s, _ in ex) - RIP_SLACK if ex else 0
    hi = max(e for _, e in ex) + RIP_SLACK if ex else 0

    def unreachable(start, size, is_exec):
        return (not is_exec) and (start + size <= lo or start >= hi)

    diffs, exempt = [], []
    for key in set(a) | set(b):
        start, size = key
        x, y = a.get(key), b.get(key)
        if x is None or y is None:
            d = x or y
            msg = "%s block %s @%x +%d" % ("original-only" if y is None else "rebuild-only",
                                           d["name"], start, size)
            (exempt if unreachable(start, size, d["exec"] == "1") else diffs).append(msg)
        elif (x["name"], x["exec"], x["init"], x["md5"]) != (y["name"], y["exec"], y["init"],
                                                             y["md5"]):
            diffs.append("block %s @%x +%d differs (md5 %s vs %s)"
                         % (x["name"], start, size, x["md5"][:12], y["md5"][:12]))
    return (not diffs), diffs, exempt


def compare_files(orig_dir, new_dir, stems):
    """Byte-compare this row's sweep output, for the SELECTED programs only.

    The rebuild is run under the ORIGINAL tag and separated by output directory, not by a
    relabelled tag. The tag is written INTO these files (`# tag`, `tag=`, and column 1 of the scan
    TSV), so relabelling would make every file differ for a reason that has nothing to do with the
    rebuild — and the obvious fix, normalising the tag back out before comparing, is a rewrite of
    the evidence. Different directory, identical bytes, no substitution.

    Files are enumerated from the STEM LIST rather than by globbing the tag, so `--program` on a
    modular row does not report the DLLs that were deliberately not rebuilt as missing."""
    msgs, ok, n = [], True, 0
    for stem in stems:
        for name in ("scan_%s.txt" % stem, "scan_%s.tsv" % stem, "consensus_%s.txt" % stem):
            p, q = os.path.join(orig_dir, name), os.path.join(new_dir, name)
            if not os.path.exists(p):
                continue                       # the original sweep produced none for this program
            if not os.path.exists(q):
                ok = False
                msgs.append("rebuild did not produce %s" % name)
                continue
            n += 1
            if open(p, "rb").read() != open(q, "rb").read():
                ok = False
                msgs.append("DIFFERS: %s" % name)
    if n == 0:
        ok = False
        msgs.append("no original sweep files to compare in %s (nothing was checked)" % orig_dir)
    return ok, msgs, n


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("tags", nargs="*")
    ap.add_argument("--all", action="store_true")
    ap.add_argument("--projs", default=r"D:/Tools/GHIDRA_REIMPORT")
    ap.add_argument("--map", default=os.path.join(REPO, "out", "pe-corpus-map.tsv"))
    ap.add_argument("--orig-identity", default=os.path.join(REPO, "out", "identity"))
    ap.add_argument("--orig-blocks", default=os.path.join(REPO, "out", "blocks-dump"))
    ap.add_argument("--orig-sweep", default=os.path.join(REPO, "out", "sweep-ref-ghidra2"))
    ap.add_argument("--out", default=os.path.join(REPO, "out", "reimport-verify"))
    ap.add_argument("--keep", action="store_true",
                    help="keep the rebuilt projects (default: delete, they are large)")
    ap.add_argument("--analyze", action="store_true",
                    help="import WITH auto-analysis (which also loads the PDB). Required to "
                         "rebuild a project whose original is disassembled; costs minutes to "
                         "hours per binary and produces a .rep ~8x larger.")
    ap.add_argument("--program", default=None,
                    help="only this program (substring). Use to rebuild ONE dll of a modular row.")
    args = ap.parse_args()

    rows = {t: (p, g, tr) for t, p, g, tr in parse_rows(os.path.join(SCRIPTS, "sweep.sh"))}
    pmap = load_map(args.map)
    tags = args.tags or (sorted(rows) if args.all else [])
    if not tags:
        print("give one or more tags, or --all")
        return 2
    os.makedirs(args.out, exist_ok=True)
    os.makedirs(args.projs, exist_ok=True)
    patterns = os.path.join(args.orig_sweep, "patterns.tsv")

    results = []
    for tag in tags:
        if tag not in rows:
            print("!! %s is not a sweep row" % tag)
            continue
        progs = pmap.get(tag)
        if progs and args.program:
            progs = [p for p in progs if args.program.lower() in p[0].lower()]
            if not progs:
                print("!! %s has no program matching %r" % (tag, args.program))
                continue
        if not progs:
            print("!! %s has no verified binary in %s — cannot rebuild it" % (tag, args.map))
            results.append((tag, "NO-INPUT", ["no hash-verified binary for this row"], 0))
            continue
        proj = "RE_" + sanitize(tag)
        proj_dir = os.path.join(args.projs, proj + ".rep")
        for suffix in (".rep", ".gpr"):
            p = os.path.join(args.projs, proj + suffix)
            if os.path.isdir(p):
                shutil.rmtree(p, ignore_errors=True)
            elif os.path.exists(p):
                os.remove(p)

        print("\n== %s  (%d program(s))" % (tag, len(progs)))
        t0 = time.time()
        # ONE AT A TIME: a Ghidra project takes an exclusive lock, so concurrent imports into the
        # same project fail with LockException and only the first gets in.
        failed = False
        for prog, ib, path in progs:
            log = os.path.join(args.out, "log_import_%s_%s.txt" % (sanitize(tag), sanitize(prog)))
            src = stage_for_import(path, os.path.join(args.projs, "_staging"))
            if src != path:
                print("   staged %-46s (path has parentheses; see stage_for_import)" % prog)
            cmd = [HL, args.projs, proj, "-import", src]
            if not args.analyze:
                cmd.append("-noanalysis")
            rc = run(cmd, log)
            print("   import %-46s %s" % (prog, "ok" if rc == 0 else "FAILED rc=%d" % rc))
            if rc != 0:
                failed = True
        if failed:
            results.append((tag, "IMPORT-FAILED", ["see %s" % args.out], time.time() - t0))
            continue

        env_common = {"GI_OUT": args.out, "GI_TAG": tag,
                      "GB_OUT": args.out, "GB_TAG": tag,
                      "GS_OUT": args.out, "GS_TAG": tag,
                      "GS_TSV": patterns, "GS_TRUE": rows[tag][2]}
        for script in ("dump_identity.java", "dump_blocks.java", "scan_patterns.java"):
            log = os.path.join(args.out, "log_%s_%s.txt" % (script.split(".")[0], sanitize(tag)))
            rc = run([HL, args.projs, proj, "-process", "-noanalysis", "-readOnly",
                      "-scriptPath", SCRIPTS, "-postScript", script], log, env_common)
            if rc != 0:
                print("   !! %s rc=%d" % (script, rc))

        msgs, dims = [], []
        for prog, ib, path in progs:
            stem = "%s__%s@%s" % (sanitize(tag), sanitize(prog), sanitize(ib))
            fo = os.path.join(args.orig_identity, "identity_%s.tsv" % stem)
            fn = os.path.join(args.out, "identity_%s.tsv" % stem)
            if not os.path.exists(fo):
                msgs.append("%s: no ORIGINAL identity record — nothing to compare against" % prog)
                dims.append({"input": False})
            elif not os.path.exists(fn):
                msgs.append("%s: rebuild produced no identity record" % prog)
                dims.append({"input": False})
            else:
                v, im = compare_identity(fo, fn)
                dims.append(v)
                msgs += ["%s: %s" % (prog, m) for m in im]
        bok, bm, bn = True, [], 0
        for prog, ib, path in progs:
            stem = "%s__%s@%s" % (sanitize(tag), sanitize(prog), sanitize(ib))
            fo = os.path.join(args.orig_blocks, "blocks_%s.tsv" % stem)
            fn = os.path.join(args.out, "blocks_%s.tsv" % stem)
            if not (os.path.exists(fo) and os.path.exists(fn)):
                bok = False
                bm.append("%s: missing a block map (orig=%s rebuild=%s)"
                          % (prog, os.path.exists(fo), os.path.exists(fn)))
                continue
            bn += 1
            k, d, ex = compare_block_maps(fo, fn)
            bok &= k
            bm += ["%s: %s" % (prog, x) for x in d]
            bm += ["%s: accepted as unreachable — %s" % (prog, x) for x in ex]
        stems = ["%s__%s@%s" % (sanitize(tag), sanitize(p), sanitize(ib)) for p, ib, _ in progs]
        sok, sm, sn = compare_files(args.orig_sweep, args.out, stems)
        msgs += bm + sm

        def all_dim(k):
            vals = [d.get(k) for d in dims if d.get(k) is not None]
            return bool(vals) and all(vals)

        input_ok = all_dim("input")
        fields_ok = all_dim("other")
        bookkeeping_only = (not fields_ok) and all(
            d.get("other") or d.get("bookkeeping_only") for d in dims)
        # `symbols` is None on a disassembled original — there is nothing meaningful to compare.
        sym_checked = any(d.get("symbols") is not None for d in dims)
        sym_ok = all_dim("symbols") if sym_checked else None
        analysis_ok = all_dim("analysis")
        if not (input_ok and bok and sok):
            verdict = "MISMATCH"
        elif sym_ok is False:
            verdict = "MISMATCH"
        elif not (fields_ok or bookkeeping_only):
            verdict = "MISMATCH"
        elif analysis_ok and not fields_ok:
            verdict = "REBUILT-EQUIVALENT"
        elif analysis_ok:
            verdict = "REBUILT-IDENTICAL"
        else:
            # Binary, memory image and sweep answers all reproduce; only the stored analysis does
            # not, and this run never tried to regenerate it.
            verdict = "REBUILT-MODULO-ANALYSIS"
        results.append((tag, verdict, msgs, time.time() - t0))
        print("   %s  (%d block map(s), %d sweep file(s) compared, %.0fs)"
              % (verdict, bn, sn, results[-1][3]))
        for m in msgs:
            print("      %s" % m)
        if not args.keep:
            for suffix in (".rep", ".gpr"):
                p = os.path.join(args.projs, proj + suffix)
                shutil.rmtree(p, ignore_errors=True) if os.path.isdir(p) else (
                    os.remove(p) if os.path.exists(p) else None)

    print("\n=== summary ===")
    tally = collections.Counter(r[1] for r in results)
    for k, v in tally.most_common():
        print("   %-20s %d" % (k, v))
    for tag, st, msgs, secs in results:
        if st != "REBUILT-IDENTICAL":
            print("\n   %s -> %s" % (tag, st))
            for m in msgs[:12]:
                print("      %s" % m)
    print("\n   REBUILT-IDENTICAL        binary, symbol table, memory image and sweep output all")
    print("                            reproduced byte-for-byte from the archived inputs.")
    print("   REBUILT-MODULO-ANALYSIS  same, except the original also carried DISASSEMBLY that a")
    print("                            -noanalysis rebuild does not regenerate. Nothing reads")
    print("                            stored analysis, but it is a real difference — say so.")
    # Exit 0 covers both, because a MODULO-ANALYSIS result is a PASS of what this tool tests.
    # Calling it a failure would push someone to 'fix' it by running hours of analysis that
    # nothing consumes; calling it identical would overstate the result. It gets its own name.
    return 0 if all(r[1] in ("REBUILT-IDENTICAL", "REBUILT-EQUIVALENT",
                             "REBUILT-MODULO-ANALYSIS") for r in results) else 1


if __name__ == "__main__":
    sys.exit(main())
