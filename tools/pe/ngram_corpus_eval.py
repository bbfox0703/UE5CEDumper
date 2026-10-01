#!/usr/bin/env python3
"""Would a BIGGER n-gram index be a better one? Measured, per source set, on games it never saw.

    py tools/pe/ngram_corpus_eval.py list    --roots D:/UE_Analyze_data
    py tools/pe/ngram_corpus_eval.py scan    --roots D:/UE_Analyze_data --out out/ngram-eval
    py tools/pe/ngram_corpus_eval.py analyze --out out/ngram-eval --curve 20
    py tools/pe/ngram_corpus_eval.py build   --out out/ngram-eval --set all --thresholds 16 8
    py tools/pe/ngram_corpus_eval.py verify  --out out/ngram-eval --set all --index <file>
    py tools/pe/ngram_corpus_eval.py recon   --out out/ngram-eval --index <file> --minus <file> \
                                             --binary <sha16> ...

WHY THIS EXISTS. `aob_specificity.py` quotes an out-of-sample violation rate (27 / 7,345) that was
measured by hand once and has no script. The question "should the index be built from more
binaries, the shipped games included" turns on that number, so it has to be re-derivable for ANY
source set — and rebuilding a 10 MB index per candidate set, per held-out game, is hours.

THE SHORTCUT, AND WHY IT IS EXACT. A pattern's bound depends only on the index entries for its own
literal windows (a few thousand keys in total). `scan` records, per binary, the EXACT count of each
of those windows; an index built from any subset S then holds, for a window w,
`max(count_b[w] for b in S)` if that is >= threshold, else nothing. `SubsetIndex` answers `lookup`
from exactly that, and the real `score()` / `verdict()` run on it unchanged. `build` writes a real
index file for a set and `verify` compares the two WINDOW BY WINDOW. A record is only valid for the
window set it was scanned with, so each carries that set's fingerprint and `analyze` refuses a
mismatch: an uncounted window would otherwise read as one that occurs zero times, i.e. CLEAR.

WHAT A TARGET IS. A PROGRAM, not a file: a modular build's module DLLs are summed into one program
(per-file, one modular game supplied 52 of 92 third-party targets, nearly all of them modules no
engine-global pattern can hit). And the headline rate is per GROUP — a (pattern, group) pair is
violated when any program of that group exceeds the bound — because the archive holds several
patches of some games and one violating pattern would otherwise count once per copy.

LEAKAGE. A held-out group is scored by an index that contains NO program of that group, where a
group is one developer's titles (two builds of one game, or a studio's sequel, share studio code).
`--publisher` reruns it with one PUBLISHER per group as a sensitivity check.

WHAT IT CANNOT REMOVE. The targets are unseen by the INDEX, not by the PATTERNS: the patterns were
authored on, and swept against, most of these games, and only the survivors are scored. Rows for
the groups no sweep ever covered (`--map`) are the closest thing here to a new game.

`scan` also caches each binary's n-gram counts (>= FLOOR) so `build` and `recon` need no second
pass over the PEs. Paths are arguments, and so is the LAYOUT (which folders hold self-built builds,
which titles share a developer): it is a local file beside the scan output, see `LAYOUT` below.
"""
import argparse
import collections
import glob
import gzip
import hashlib
import io
import json
import os
import struct
import sys
import time

_HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(_HERE))
sys.path.insert(0, _HERE)
sys.path.insert(0, os.path.join(REPO, "tools", "ghidra"))
sys.stdout.reconfigure(encoding="utf-8", errors="replace")

NS = (4, 5, 6)
FLOOR = 8                     # cached counts keep everything a threshold >= 8 index could hold

# WHICH folder is which, and which titles share a developer, is LOCAL knowledge: it lives in a
# layout file beside the scan output, never in this script.
#   {"tier_dirs":  {"<folder name>": "self" | "tp", ...},     folders holding self-built / other
#    "aliases":    {"<title folder>": "<group>", ...},        one developer -> one group
#    "publishers": {"<group prefix>": "<publisher>", ...},    --publisher
#    "src_group":  {"<pattern src tag>": "<group>", ...}}     the authored-from-this-game split
LAYOUT = {"tier_dirs": {}, "aliases": {}, "publishers": {}, "src_group": {}}


def load_layout(path):
    if not path or not os.path.exists(path):
        raise SystemExit("layout file not found: %s\nIt names the folders that hold self-built "
                         "builds and the ones that hold other programs; see this file's LAYOUT "
                         "comment for the shape." % path)
    with io.open(path, encoding="utf-8") as f:
        raw = json.load(f)
    for k in LAYOUT:
        LAYOUT[k] = {a.lower(): b for a, b in raw.get(k, {}).items()}
    if not LAYOUT["tier_dirs"]:
        raise SystemExit("%s: no tier_dirs" % path)


def enumerate_binaries(roots, extra=()):
    """The builder's own source list (so this measures the files an index would be built from),
    plus `extra`: "Group=path" for a binary outside the roots, always third-party.

    Tier, group and program are NOT decided here — `place()` derives them from the path at analyze
    time, so a correction to the layout never needs a rescan."""
    from build_ngram_index import find_sources, sha256_of
    out = find_sources(roots)
    seen = {r["sha"]: r["path"] for r in out}
    for spec in extra:
        group, _, p = spec.partition("=")
        sha = sha256_of(p)
        if sha in seen:
            print("extra %s: already in the roots as %s" % (p, seen[sha]))
            continue
        seen[sha] = p
        out.append({"sha": sha, "path": p, "name": os.path.basename(p), "group": group,
                    "size": os.path.getsize(p), "extra": True})
    return out


_CFG_CACHE = {}


def linked_config(path, name):
    """The build config the LINKER recorded (the PDB's name), not the file's: a retail game ships
    `Game.exe` for a Shipping build and, sometimes, for a Development one."""
    if path in _CFG_CACHE:
        return _CFG_CACHE[path]
    low = name.lower()
    cfg = None
    if "-win64-shipping" in low:
        cfg = "Shipping"
    elif "-win64-debuggame" in low:
        cfg = "DebugGame"
    else:
        try:
            from pdb_match import pe_codeview
            cv = pe_codeview(path)
        except Exception:
            cv = None
        if cv:
            pdb = os.path.basename(cv[2].replace("\\", "/")).lower()
            cfg = ("Shipping" if "shipping" in pdb else "DebugGame" if "debuggame" in pdb else
                   "Test" if "-test" in pdb else "Development")
        else:
            cfg = "Shipping"              # no CodeView record at all: a stripped retail build
    _CFG_CACHE[path] = cfg
    return cfg


def place(rec, publisher=False):
    """-> {tier, group, config, program} from the stored path, or None for a file that is not a
    game program (Epic's prebuilt EOSSDK, a self-built tree's loose DLL)."""
    name = rec["name"]
    low = name.lower()
    if low.startswith("eossdk"):
        return None

    def grp(title):
        g = LAYOUT["aliases"].get(title.lower(), title)
        if publisher:
            for pre, pub in LAYOUT["publishers"].items():
                if g.lower().startswith(pre):
                    return pub
        return g

    if rec.get("extra"):
        return {"tier": "tp", "group": grp(rec["group"]), "program": "extra/" + name,
                "config": linked_config(rec["path"], name)}
    parts = rec["path"].replace("\\", "/").split("/")
    lows = [p.lower() for p in parts]
    tiers = LAYOUT["tier_dirs"]
    i = next((k for k, p in enumerate(lows) if p in tiers), None)
    if i is None or i + 1 >= len(parts):
        return None
    tier, title = tiers[lows[i]], parts[i + 1]
    if tier == "self":
        if low.endswith(".dll"):
            return None
        cfg = next((c for c in ("DebugGame", "Development", "Shipping") if c.lower() in lows[i:]),
                   None) or linked_config(rec["path"], name)
        return {"tier": "self", "group": "self:" + title, "config": cfg,
                "program": rec["sha"][:16]}
    if i + 3 < len(parts):
        program = title + "/" + parts[i + 2]            # <title>/<build>/...: one build, one program
    elif low.endswith(".dll"):
        program = title + "/modular"
    else:
        program = title + "/" + name
    return {"tier": "tp", "group": grp(title), "program": program,
            "config": linked_config(rec["path"], name)}


def pattern_windows(tsv):
    """-> (pats {id: pattern}, wins {n: sorted int keys}, srcs {id: [src tags]})."""
    from aob_specificity import literal_runs
    wins = {n: set() for n in NS}
    rows = [l.rstrip("\n").split("\t") for l in io.open(tsv, encoding="utf-8")]
    ix = {k: i for i, k in enumerate(rows[0])}
    pats, srcs = {}, {}
    for r in rows[1:]:
        if len(r) <= ix["pattern"]:
            continue
        pat = r[ix["pattern"]]
        try:
            runs = literal_runs(pat)
        except SystemExit:
            continue                       # a mangled-symbol row, not a byte pattern
        pats[r[ix["id"]]] = pat
        srcs[r[ix["id"]]] = r[ix["src"]].replace("+", " ").split()
        for run in runs:
            for n in NS:
                for i in range(len(run) - n + 1):
                    wins[n].add(int.from_bytes(run[i:i + n], "big"))
    return pats, {n: sorted(v) for n, v in wins.items()}, srcs


def windows_fingerprint(wins):
    return hashlib.sha256(json.dumps([wins[n] for n in NS]).encode()).hexdigest()[:16]


def scan_one(job):
    rec, tsv, out_dir, wins, fp = job
    import numpy as np
    from build_ngram_index import exec_sections
    import pe_scan_patterns as P
    from pe_memory import PeMemory
    sha16 = rec["sha"][:16]
    jpath = os.path.join(out_dir, "rec", sha16 + ".json")
    if os.path.exists(jpath):
        with io.open(jpath, encoding="utf-8") as f:
            if json.load(f).get("wins_fp") == fp:
                return sha16, "cached"
    t0 = time.time()
    try:
        bufs = exec_sections(rec["path"])
    except Exception as e:                                   # not a PE32+ this reader handles
        return sha16, "SKIP %s" % e
    exec_mb = sum(len(b) for b in bufs) / 1e6
    if exec_mb < 1:
        return sha16, "SKIP no executable section"
    wcount, save, stats = {}, {}, {}
    for n in NS:
        parts = []
        for buf in bufs:
            if len(buf) <= n:
                continue
            a = np.frombuffer(buf, dtype=np.uint8)
            L = len(a) - n + 1
            v = np.zeros(L, dtype=np.uint64)
            for i in range(n):
                v = (v << np.uint64(8)) | a[i:i + L].astype(np.uint64)
            parts.append(v)
        v = np.concatenate(parts) if len(parts) > 1 else parts[0]
        del parts
        uniq, cnt = np.unique(v, return_counts=True)
        total = int(v.size)
        del v
        wk = np.array(wins[n], dtype=np.uint64)
        pos = np.searchsorted(uniq, wk)
        pos[pos >= uniq.size] = 0
        hit = uniq[pos] == wk
        wcount[n] = {format(int(k), "x"): int(c) for k, c, h in zip(wk, cnt[pos], hit) if h}
        keep = cnt >= FLOOR
        save["k%d" % n] = uniq[keep]
        save["c%d" % n] = cnt[keep].astype(np.uint32)
        stats[n] = {"positions": total, "distinct": int(uniq.size), "kept": int(keep.sum())}
        del uniq, cnt
    np.savez(os.path.join(out_dir, "cache", sha16 + ".npz"), **save)

    sigs, _skipped, _ = P.load_sigs(tsv)
    mem = PeMemory(rec["path"])
    for s in sigs:
        s.n_hits, s.hits = 0, []
    for blk in mem.exec_blocks():
        P.scan_block(sigs, mem.block_bytes(blk), blk.start, mem)
    out = dict(rec)
    out.update({"exec_mb": round(exec_mb, 1), "hits": {s.id: s.n_hits for s in sigs},
                "win": {str(n): wcount[n] for n in NS}, "stats": {str(n): stats[n] for n in NS},
                "wins_fp": fp, "seconds": round(time.time() - t0, 1)})
    with io.open(jpath, "w", encoding="utf-8") as f:
        json.dump(out, f)
    return sha16, "ok %.0fs %.0f MB" % (time.time() - t0, exec_mb)


def cmd_list(a):
    recs = enumerate_binaries(a.roots, a.extra)
    rows = []
    for r in recs:
        pl = place(r)
        if pl:
            rows.append((pl["tier"], pl["group"], pl["program"], pl["config"], r))
    for tier, group, prog, cfg, r in sorted(rows, key=lambda x: (x[0], x[1], x[2], x[4]["name"])):
        print("%-4s %-30s %-11s %6.0f MB  %-44s %s" % (tier, group[:30], cfg, r["size"] / 2**20,
                                                      r["name"][:44], prog[:40]))
    print("\n%d files -> %d programs; groups: self %d, third-party %d; %d files are not programs"
          % (len(rows), len({x[2] for x in rows}),
             len({x[1] for x in rows if x[0] == "self"}), len({x[1] for x in rows if x[0] == "tp"}),
             len(recs) - len(rows)))
    return 0


def cmd_scan(a):
    from concurrent.futures import ProcessPoolExecutor
    os.makedirs(os.path.join(a.out, "rec"), exist_ok=True)
    os.makedirs(os.path.join(a.out, "cache"), exist_ok=True)
    _pats, wins, _srcs = pattern_windows(a.tsv)
    fp = windows_fingerprint(wins)
    print("windows: " + ", ".join("n=%d %d" % (n, len(wins[n])) for n in NS) + "  fp " + fp)
    recs = enumerate_binaries(a.roots, a.extra)
    if a.only:
        recs = [r for r in recs if any(o.lower() in r["name"].lower() for o in a.only)]
    recs.sort(key=lambda r: -r["size"])
    jobs = [(r, a.tsv, a.out, wins, fp) for r in recs]
    t0 = time.time()
    with ProcessPoolExecutor(max_workers=a.workers) as ex:
        for i, (sha16, msg) in enumerate(ex.map(scan_one, jobs), 1):
            print("  [%d/%d] %s  %s" % (i, len(jobs), sha16, msg), flush=True)
    print("done in %.0fs" % (time.time() - t0))
    return 0


# ---------------------------------------------------------------------------------- analysis
def bucket_bound(count):
    """build_ngram_index.bucket_of then `1 << bucket`: the smallest power of two >= count."""
    return 1 << max(0, (int(count) - 1).bit_length())


class SubsetIndex:
    """What an index built from `units` at `threshold` would answer — for pattern windows only.
    A unit is anything with a `win` table: a scanned file, or a program (its files summed)."""

    def __init__(self, units, threshold):
        self.ns = list(NS)
        self.threshold = threshold
        self.max = {n: collections.defaultdict(int) for n in NS}
        for r in units:
            for n in NS:
                m = self.max[n]
                for k, c in r["win"][str(n)].items():
                    if c > m[k]:
                        m[k] = c

    def lookup(self, key):
        n = len(key)
        if n not in self.max:
            return None
        c = self.max[n].get(format(int.from_bytes(key, "big"), "x"), 0)
        return bucket_bound(c) if c >= self.threshold else self.threshold - 1


def load_records(out, fp=None):
    recs = []
    for p in sorted(glob.glob(os.path.join(out, "rec", "*.json"))):
        with io.open(p, encoding="utf-8") as f:
            recs.append(json.load(f))
    if fp is not None:
        stale = [r["name"] for r in recs if r.get("wins_fp") != fp]
        if stale:
            raise SystemExit("%d of %d records were scanned with a DIFFERENT pattern set (window "
                             "fingerprint != %s), e.g. %s. A window they never counted would read "
                             "as absent, i.e. CLEAR. Re-run `scan` with this --tsv."
                             % (len(stale), len(recs), fp, stale[0]))
    return recs


def to_programs(recs, publisher=False):
    by = collections.OrderedDict()
    for r in recs:
        pl = place(r, publisher)
        if pl is None:
            continue
        p = by.get(pl["program"])
        if p is None:
            p = by[pl["program"]] = {"program": pl["program"], "tier": pl["tier"],
                                     "group": pl["group"], "config": pl["config"], "files": [],
                                     "exec_mb": 0.0, "hits": collections.Counter(),
                                     "win": {str(n): collections.Counter() for n in NS},
                                     "_big": 0}
        p["files"].append(r["name"])
        p["exec_mb"] += r["exec_mb"]
        p["hits"].update(r["hits"])
        for n in NS:
            p["win"][str(n)].update(r["win"][str(n)])
        if r["size"] > p["_big"]:
            p["_big"], p["config"], p["name"] = r["size"], pl["config"], r["name"]
    return list(by.values())


def bounds_for(index_or_pair, pats, separate=False):
    """{id: (bound, verdict)}. `separate`: (idxA, idxB), the bound is the LARGER of the two."""
    from aob_specificity import score, verdict
    out = {}
    for pid, pat in pats.items():
        if separate:
            ba, _n, _w, lg, lt = score(index_or_pair[0], pat)
            bb = score(index_or_pair[1], pat)[0]
            b = None if ba is None or bb is None else max(ba, bb)
            floor = index_or_pair[0].threshold - 1
        else:
            b, _n, _w, lg, lt = score(index_or_pair, pat)
            floor = index_or_pair.threshold - 1
        out[pid] = (b, verdict(b, lg, lt, floor)[0])
    return out


def tally(bounds, targets, acc, worst, authored=None):
    """Add `targets` judged against `bounds` into `acc`, at program AND group level."""
    by_group = collections.defaultdict(list)
    for t in targets:
        by_group[t["group"]].append(t)
    for g, progs in by_group.items():
        for pid, (b, v) in bounds.items():
            if b is None:
                continue
            clear = v == "CLEAR"
            g_viol = False
            for t in progs:
                n = t["hits"].get(pid, 0)
                acc["p_pairs"] += 1
                acc["p_clear"] += clear
                if n > b:
                    acc["p_viol"] += 1
                    acc["p_clear_viol"] += clear
                    g_viol = True
                    worst.append((n - b, pid, t.get("name", "?"), n, b, v, g))
            acc["g_pairs"] += 1
            acc["g_clear"] += clear
            acc["g_viol"] += g_viol
            acc["g_clear_viol"] += g_viol and clear
            if authored is not None and g.lower() in authored.get(pid, ()):
                acc["g_authored_pairs"] += 1
                acc["g_authored_viol"] += g_viol


def fmt(acc):
    def r(v, p):
        return "%4d/%-6d %5.2f%%" % (v, p, 100.0 * v / p if p else 0)
    return "group %s  CLEAR %s | program %s  CLEAR %s" % (
        r(acc["g_viol"], acc["g_pairs"]), r(acc["g_clear_viol"], acc["g_clear"]),
        r(acc["p_viol"], acc["p_pairs"]), r(acc["p_clear_viol"], acc["p_clear"]))


def tightness(bounds, ref):
    clear = sum(1 for b, v in bounds.values() if v == "CLEAR")
    raised = sum(1 for p, (b, v) in bounds.items()
                 if b is not None and ref[p][0] is not None and b > ref[p][0])
    return clear, raised


def cmd_analyze(a):
    pats, wins, srcs = pattern_windows(a.tsv)
    recs = load_records(a.out, windows_fingerprint(wins))
    scanned = set.intersection(*(set(r["hits"]) for r in recs))
    pats = {k: v for k, v in pats.items() if k in scanned}
    sg = LAYOUT["src_group"]
    authored = {p: {sg[t.lower()].lower() for t in toks if t.lower() in sg}
                for p, toks in srcs.items()}
    progs = to_programs(recs, a.publisher)
    selfs = [p for p in progs if p["tier"] == "self"]
    self_ship = [p for p in selfs if p["config"] == "Shipping"]
    self_non = [p for p in selfs if p["config"] != "Shipping"]
    tp = [p for p in progs if p["tier"] == "tp"]
    tp_ship = [p for p in tp if p["config"] == "Shipping"]
    tp_non = [p for p in tp if p["config"] != "Shipping"]
    groups = sorted({p["group"] for p in tp})
    swept = set()
    if a.map and os.path.exists(a.map):
        names = {os.path.basename(l.split("\t")[3].strip().replace("\\", "/")).lower()
                 for l in io.open(a.map, encoding="utf-8").read().splitlines()[1:] if "\t" in l}
        swept = {p["group"] for p in tp if any(f.lower() in names for f in p["files"])}
    unswept = [g for g in groups if g not in swept]

    print("files %d -> programs %d: self Shipping %d, self non-Shipping %d, third-party %d "
          "(%d Shipping, %d other config) in %d groups%s"
          % (len(recs), len(progs), len(self_ship), len(self_non), len(tp), len(tp_ship),
             len(tp_non), len(groups), ", grouped by PUBLISHER" if a.publisher else ""))
    print("third-party non-Shipping builds: %s"
          % ", ".join("%s (%s)" % (p["name"], p["config"]) for p in tp_non))
    if a.map:
        print("groups no sweep ever covered (%d): %s" % (len(unswept), ", ".join(unswept)))
    print("patterns scored: %d   (unseen by the INDEX; most were authored on these games)\n"
          % len(pats))
    report = {"files": len(recs), "programs": len(progs), "groups": len(groups), "rows": []}

    for T in a.thresholds:
        print("=" * 132)
        print("THRESHOLD %d      violations / pairs: per (pattern, GROUP), then per (pattern, "
              "program)" % T)

        def row(label, acc, extra=""):
            print("%-52s %s  %s" % (label, fmt(acc), extra))
            report["rows"].append({"T": T, "label": label, **dict(acc)})

        def single(label, idx, targets, extra="", auth=None):
            acc, worst = collections.Counter(), []
            b = bounds_for(idx, pats)
            tally(b, targets, acc, worst, auth)
            row(label, acc, extra)
            return acc, worst, b

        if T == 16 and a.committed and os.path.exists(a.committed):
            from aob_specificity import Index
            single("COMMITTED index file  ->  third-party Shipping", Index(a.committed), tp_ship)
            single("COMMITTED index file  ->  self non-Shipping", Index(a.committed), self_non)
        base = SubsetIndex(self_ship, T)
        _acc, _w, b_ship = single("self Shipping (%d)  ->  itself (must be 0)" % len(self_ship),
                                  base, self_ship)
        _acc, worst_tp, _b = single("self Shipping  ->  third-party Shipping", base, tp_ship)
        if tp_non:
            single("self Shipping  ->  third-party non-Shipping", base, tp_non)
        single("self Shipping  ->  self non-Shipping", base, self_non)

        full_self = SubsetIndex(selfs, T)
        b_self = bounds_for(full_self, pats)
        acc_b, worst_b = collections.Counter(), []
        tally(b_self, tp, acc_b, worst_b, authored)
        row("self ALL configs (%d)  ->  third-party" % len(selfs), acc_b,
            "CLEAR patterns %d" % tightness(b_self, b_self)[0])

        acc_m, acc_s, acc_t = (collections.Counter() for _ in range(3))
        worst_m, per_group = [], []
        tm, ts = [0, 0], [0, 0]
        for g in groups:
            held = [p for p in tp if p["group"] == g]
            rest = [p for p in tp if p["group"] != g]
            bm = bounds_for(SubsetIndex(selfs + rest, T), pats)
            bs = bounds_for((full_self, SubsetIndex(rest, T)), pats, separate=True)
            bt = bounds_for(SubsetIndex(rest, T), pats)
            am, ab = collections.Counter(), collections.Counter()
            tally(bm, held, am, worst_m, authored)
            tally(b_self, held, ab, [])
            acc_m.update(am)
            tally(bs, held, acc_s, [])
            tally(bt, held, acc_t, [])
            for acc_list, b in ((tm, bm), (ts, bs)):
                c, r = tightness(b, b_self)
                acc_list[0] += c
                acc_list[1] += r
            per_group.append((g, len(held), sum(p["exec_mb"] for p in held), ab, am))
        k = float(len(groups))
        row("self ALL + third-party MERGED, leave-one-group-out", acc_m,
            "CLEAR patterns %.1f, bounds raised %.1f (fold mean)" % (tm[0] / k, tm[1] / k))
        row("self ALL | third-party SEPARATE (max), same folds", acc_s,
            "CLEAR patterns %.1f, bounds raised %.1f" % (ts[0] / k, ts[1] / k))
        row("third-party ONLY (no self-built), same folds", acc_t)
        print("%-52s authored-from-the-held-out-group pairs: %d of %d group pairs, %d of the "
              "violations" % ("", acc_m["g_authored_pairs"], acc_m["g_pairs"],
                              acc_m["g_authored_viol"]))
        if unswept:
            for label, src_groups in (("self ALL  ->  never-swept groups", None),
                                      ("MERGED leave-one-group-out  ->  never-swept", "m")):
                acc = collections.Counter()
                for g in unswept:
                    held = [p for p in tp if p["group"] == g]
                    if src_groups is None:
                        tally(b_self, held, acc, [])
                    else:
                        rest = [p for p in tp if p["group"] != g]
                        tally(bounds_for(SubsetIndex(selfs + rest, T), pats), held, acc, [])
                row(label, acc)
        everything = SubsetIndex(selfs + tp, T)
        _acc, _w, b_all = single("everything (%d programs)  ->  itself (must be 0)"
                                 % len(selfs + tp), everything, selfs + tp)
        c, r = tightness(b_all, b_self)
        lost = sorted(p for p in pats if b_self[p][1] == "CLEAR" and b_all[p][1] != "CLEAR")
        print("\n  everything indexed vs self ALL: CLEAR %d -> %d, bounds raised on %d of %d "
              "scored patterns; CLEAR lost by: %s"
              % (tightness(b_self, b_self)[0], c, r,
                 sum(1 for b, v in b_all.values() if b is not None), " ".join(lost)))
        print("\n  worst excess, self-Shipping index on third-party Shipping:")
        for ex, pid, name, n, b, v, g in sorted(worst_tp, reverse=True)[:8]:
            print("     %-16s %-40s hits %6d  bound %6d  %s" % (pid, name[:40], n, b, v))
        print("  worst excess that SURVIVES with third-party merged (leave-one-group-out):")
        for ex, pid, name, n, b, v, g in sorted(worst_m, reverse=True)[:10]:
            print("     %-16s %-40s hits %6d  bound %6d  %s%s" % (
                pid, name[:40], n, b, v,
                "  [authored from this group]" if g.lower() in authored.get(pid, ()) else ""))

        if a.per_group:
            print("\n  per held-out group: violating patterns under self ALL -> under MERGED "
                  "(CLEAR ones in brackets)")
            for g, n, mb, ab, am in sorted(per_group, key=lambda x: -x[3]["g_viol"]):
                print("     %-38s %2d prog %7.0f MB   %3d (%d)  ->  %3d (%d)%s" % (
                    g[:38], n, mb, ab["g_viol"], ab["g_clear_viol"], am["g_viol"],
                    am["g_clear_viol"], "" if g in swept or not a.map else "   never swept"))

        if a.curve:
            import random
            print("\n  learning curve, %d seeds: a random third of the groups is the FIXED test "
                  "set, the index grows from the rest" % a.curve)
            n_test = max(1, len(groups) // 3)
            n_pool = len(groups) - n_test
            for k_in in sorted({0, 1, 2, 4, 8, 12, 16, n_pool} & set(range(n_pool + 1))):
                rates, crates = [], []
                for seed in range(a.curve):
                    order = groups[:]
                    random.Random(seed).shuffle(order)
                    test, pool = set(order[:n_test]), order[n_test:]
                    inset = set(pool[:k_in])
                    idx = SubsetIndex(selfs + [p for p in tp if p["group"] in inset], T)
                    acc = collections.Counter()
                    tally(bounds_for(idx, pats), [p for p in tp if p["group"] in test], acc, [])
                    rates.append(100.0 * acc["g_viol"] / acc["g_pairs"])
                    crates.append(100.0 * acc["g_clear_viol"] / max(1, acc["g_clear"]))
                print("     %2d groups added   group rate mean %5.2f%% (min %5.2f max %5.2f)   "
                      "CLEAR mean %5.2f%% (max %5.2f)" % (
                          k_in, sum(rates) / len(rates), min(rates), max(rates),
                          sum(crates) / len(crates), max(crates)))
        print()

    if a.patterns:
        from aob_specificity import score, verdict
        print("=" * 132)
        for pid in a.patterns:
            if pid not in pats:
                print("%s: not in the scanned pattern set" % pid)
                continue
            print("%s   %s" % (pid, pats[pid]))
            for T in a.thresholds:
                for label, src in (("self Shipping", self_ship), ("self ALL configs", selfs),
                                   ("everything", selfs + tp)):
                    b, n, win, lg, lt = score(SubsetIndex(src, T), pats[pid])
                    print("   T=%-2d index %-17s bound %-8s %-9s window %s" % (
                        T, label, b, verdict(b, lg, lt, T - 1)[0], win.hex(" ") if win else "-"))
            for label, src in (("self Shipping", self_ship), ("self non-Shipping", self_non),
                               ("third-party", tp)):
                hs = sorted(((p["hits"].get(pid, 0), p["name"]) for p in src), reverse=True)
                print("   hits on %-18s max %d (%s), programs with a hit: %d of %d" % (
                    label, hs[0][0] if hs else 0, hs[0][1] if hs else "-",
                    sum(1 for h, _ in hs if h), len(hs)))
    with io.open(os.path.join(a.out, "report.json"), "w", encoding="utf-8") as f:
        json.dump(report, f, indent=1)
    return 0


# ------------------------------------------------------------------------- build / verify
def pick_set(recs, name, exclude=()):
    """FILES (not programs) of one source set — what a real builder walks."""
    ex = {e.lower() for e in exclude}
    out = []
    for r in recs:
        pl = place(r)
        if pl is None or pl["group"].lower() in ex:
            continue
        if name == "all" or (name == "self" and pl["tier"] == "self") \
                or (name == "tp" and pl["tier"] == "tp") \
                or (name == "self-shipping" and pl["tier"] == "self"
                    and pl["config"] == "Shipping"):
            out.append(dict(r, group=pl["group"], config=pl["config"]))
    return out


def merged_tables(out, recs, np, log=None, batch=16):
    """{n: (keys, cls)} over the cached per-binary tables (counts >= FLOOR), `cls` being the count
    class `bucket * 2 + (count >= 16)`: monotone in the count, so the MAX class per key is the
    class of the max count, and it carries what both a T=8 and a T=16 index need (which keys, and
    their bucket). Packed under the key so one plain sort per batch does the whole merge."""
    from build_ngram_index import bucket_of
    acc = {n: np.empty(0, np.uint64) for n in NS}
    for start in range(0, len(recs), batch):
        chunk = recs[start:start + batch]
        parts = {n: [acc[n]] for n in NS}
        for r in chunk:
            z = np.load(os.path.join(out, "cache", r["sha"][:16] + ".npz"))
            for n in NS:
                c = z["c%d" % n]
                cls = bucket_of(c, np).astype(np.uint64) * np.uint64(2) + (c >= 16)
                parts[n].append((z["k%d" % n] << np.uint64(8)) | cls)
        for n in NS:
            w = np.sort(np.concatenate(parts[n]))
            last = np.empty(w.shape, dtype=bool)          # sorted: the LAST word of a key is its max
            if w.size:
                last[-1] = True
                np.not_equal(w[1:] >> np.uint64(8), w[:-1] >> np.uint64(8), out=last[:-1])
            acc[n] = w[last]
        if log:
            log("  [%d/%d] union(>=%d) n=4 %d  n=5 %d  n=6 %d" % (
                min(start + batch, len(recs)), len(recs), FLOOR, *(acc[n].size for n in NS)))
    return {n: (acc[n] >> np.uint64(8), (acc[n] & np.uint64(0xFF)).astype(np.uint8)) for n in NS}


def set_tag(a):
    return a.set + "".join("-no-" + e.replace(" ", "_") for e in a.exclude_group)


def cmd_build(a):
    import numpy as np
    from build_ngram_index import FORMAT_VERSION, write_index
    recs = pick_set(load_records(a.out), a.set, a.exclude_group)
    tabs = merged_tables(a.out, recs, np, log=(lambda s: print(s, flush=True)) if a.verbose
                         else None)
    for T in a.thresholds:
        path = os.path.join(a.out, "index-%s-T%d.bin.gz" % (set_tag(a), T))
        meta = {"format": "UE5CEDumper AOB n-gram specificity index (EVALUATION BUILD)",
                "format_version": FORMAT_VERSION, "threshold": T, "ns": list(NS),
                "source_files": len(recs),
                "source_exec_mb": round(sum(r["exec_mb"] for r in recs), 1)}
        kept = {}
        for n in NS:
            k, cls = tabs[n]
            m = (cls & 1).astype(bool) if T >= 16 else np.ones(k.shape, dtype=bool)
            kept[n] = (k[m], cls[m] >> 1)
        write_index(path, meta, NS, T, kept)        # the production writer, so one format
        raw = sum(kept[n][0].size * (n + 1) for n in NS)
        print("T=%-2d %-28s %3d files %6.0f MB code  records n4/5/6 = %s  raw %.1f MB  "
              "gz %.1f MB  -> %s" % (
                  T, set_tag(a), len(recs), sum(r["exec_mb"] for r in recs),
                  "/".join("%d" % kept[n][0].size for n in NS), raw / 2**20,
                  os.path.getsize(path) / 2**20, os.path.basename(path)))
    return 0


def cmd_verify(a):
    """A real index file and SubsetIndex must agree on EVERY window, not only on each pattern's
    minimum: a shortcut wrong on the non-limiting windows would pass a per-pattern comparison."""
    from aob_specificity import Index
    idx = Index(a.index)
    _pats, wins, _srcs = pattern_windows(a.tsv)
    recs = pick_set(load_records(a.out, windows_fingerprint(wins)), a.set, a.exclude_group)
    sub = SubsetIndex(recs, idx.threshold)
    total = diff = 0
    for n in NS:
        for k in wins[n]:
            key = int(k).to_bytes(n, "big")
            total += 1
            if idx.lookup(key) != sub.lookup(key):
                diff += 1
                if diff <= 10:
                    print("   n=%d %s  file=%s  subset=%s" % (n, key.hex(" "), idx.lookup(key),
                                                            sub.lookup(key)))
    print("real index (%d files, T=%d) vs SubsetIndex: %d of %d windows differ"
          % (len(recs), idx.threshold, diff, total))
    return 1 if diff else 0


# ----------------------------------------------------------------------------------- recon
def table_keys(idx, n, np):
    off, cnt, _thr, stride = idx.tables[n]
    raw = np.frombuffer(idx.buf, dtype=np.uint8, count=cnt * stride, offset=off).reshape(cnt, stride)
    keys = np.zeros(cnt, dtype=np.uint64)
    for j in range(n):
        keys = (keys << np.uint64(8)) | raw[:, j].astype(np.uint64)
    return keys


def member(sorted_keys, v, np):
    p = np.searchsorted(sorted_keys, v)
    p[p >= sorted_keys.size] = 0
    return sorted_keys[p] == v


def degree(uniq, counts, v, np):
    """counts[...] where v is in uniq, else 0."""
    p = np.searchsorted(uniq, v)
    p[p >= uniq.size] = 0
    return np.where(uniq[p] == v, counts[p], 0).astype(np.int32)


def best_run(mask, v, np, top=2000):
    """The run of True with the most DISTINCT 6-grams -> (distinct, bytes, exact). Ranking by
    length alone returns padding and tables: long, covered, and one sequence repeated. Runs are
    tried longest first until none left can beat the best; `exact` is False when `top` ran out
    first, and the figure is then a lower bound."""
    d = np.diff(np.concatenate((np.zeros(1, np.int8), mask.view(np.int8), np.zeros(1, np.int8))))
    starts, ends = np.flatnonzero(d == 1), np.flatnonzero(d == -1)
    if not starts.size:
        return (0, 0, True)
    lens = ends - starts
    best, exact = (0, 0), True
    order = np.argsort(lens)[::-1]
    for k, i in enumerate(order):
        if lens[i] <= best[0]:
            break                              # a shorter run cannot hold more distinct 6-grams
        if k >= top:
            exact = False
            break
        distinct = int(np.unique(v[starts[i]:ends[i]]).size)
        if distinct > best[0]:
            best = (distinct, int(lens[i]) + 5)
    return best + (exact,)


def cmd_recon(a):
    """What an index says about ONE binary's code, measured with that binary in hand.

    covered   a position whose 6-gram is in the index.
    only      covered, and NOT in a `--minus` index: what this index holds beyond that one (minus
              = the self-built index -> code no stock build has; minus = the same set with the
              binary's group left out -> what is there only because this game was a source).
    forced    covered, and the index holds exactly ONE 6-gram with that 5-byte prefix: the binary
              agrees with the index's only continuation. two-way adds: only one predecessor.
    budget B  the longest window whose every position is covered and whose total branching,
              sum(log2(out-degree)), is at most B bits — B bits of search buys that window.

    Every "run" is ranked by DISTINCT 6-grams and reported with them, and stops at a repeat
    (period <= 16: padding, a one-entry table). All of it is an upper view
    of an index holder's position: the binary chose the seed and where each run stops, and a
    holder of the index alone has neither."""
    import numpy as np
    from aob_specificity import Index
    from build_ngram_index import exec_sections
    idx = Index(a.index)
    keys = table_keys(idx, 6, np)
    upref, ucount = np.unique(keys >> np.uint64(8), return_counts=True)
    usuf, scount = np.unique(keys & np.uint64(0xFFFFFFFFFF), return_counts=True)
    print("index %s: T=%d, %d files, %d 6-grams; 5-byte prefixes %d, out-degree 1 for %.1f%%, "
          "mean out-degree %.2f" % (os.path.basename(a.index), idx.threshold,
                                    idx.meta.get("source_files", 0), keys.size, upref.size,
                                    100.0 * (ucount == 1).mean(), ucount.mean()))
    minus = []
    for p in a.minus:
        mk = table_keys(Index(p), 6, np)
        minus.append((os.path.basename(p).replace("index-", "").replace(".bin.gz", ""), mk))
        print("  minus %s: %d 6-grams" % (minus[-1][0], mk.size))
    recs = {r["sha"][:16]: r for r in load_records(a.out)}
    for sha16 in a.binary:
        r = recs[sha16]
        tot = 0
        share = collections.Counter()
        runs, budget = {}, {}
        for buf in exec_sections(r["path"]):
            if len(buf) <= 6:
                continue
            arr = np.frombuffer(buf, dtype=np.uint8)
            L = len(arr) - 5
            v = np.zeros(L, dtype=np.uint64)
            for i in range(6):
                v = (v << np.uint64(8)) | arr[i:i + L].astype(np.uint64)
            present = member(keys, v, np)
            od = degree(upref, ucount, v >> np.uint64(8), np)
            ind = degree(usuf, scount, v & np.uint64(0xFFFFFFFFFF), np)
            masks = {"covered": present, "forced": present & (od == 1)}
            masks["forced two-way"] = masks["forced"] & (ind == 1)
            for label, mk in minus:
                masks["only (not in %s)" % label] = present & ~member(mk, v, np)
            del ind
            tot += L
            # A run or a window may not pass through a REPEAT (padding, a table of one entry
            # repeated): those are the longest covered stretches in any binary and hold a handful
            # of sequences. The SHARES below still count them; only the runs exclude them.
            periodic = np.zeros(L, dtype=bool)
            for per in (1, 2, 3, 4, 8, 16):
                periodic[per:] |= v[per:] == v[:-per]
            for name, m in masks.items():
                share[name] += int(m.sum())
                br = best_run(m & ~periodic, v, np)
                old = runs.get(name, (0, 0, True))
                runs[name] = (br if br[0] > old[0] else old)[:2] + (old[2] and br[2],)
            # milli-bits, as integers: a float cumsum loses the fractions at this length
            cost = np.where(present & ~periodic, np.rint(np.log2(np.maximum(od, 1)) * 1000), 10**9)
            del periodic
            cs = np.concatenate((np.zeros(1, np.int64), np.cumsum(cost.astype(np.int64))))
            del cost, od
            for B in a.bits:
                end = np.searchsorted(cs, cs[:-1] + B * 1000, side="right") - 1
                length = end - np.arange(L)
                best = budget.get(B, (0, 0))
                seen_end = set()
                for i in np.argsort(length)[::-1][:20000]:
                    if length[i] <= best[0] or len(seen_end) >= 400:
                        break
                    if int(end[i]) in seen_end:
                        continue
                    seen_end.add(int(end[i]))
                    distinct = int(np.unique(v[i:end[i]]).size)
                    if distinct > best[0]:
                        best = (distinct, int(length[i]) + 5)
                budget[B] = best
                del end, length
            del cs, v, present, masks
        pl = place(r) or {"tier": "?", "group": "?"}
        print("\n  %s  [%s, %s]  %.0f MB code" % (r["name"], pl["tier"], pl["group"],
                                                r["exec_mb"]))
        for name in share:
            d, nbytes, exact = runs.get(name, (0, 0, True))
            print("     %-34s %6.2f%% of positions   richest run %s%5d distinct 6-grams in %6d B"
                  % (name, 100.0 * share[name] / tot, " " if exact else ">=", d, nbytes))
        for B in a.bits:
            d, nbytes = budget.get(B, (0, 0))
            print("     search budget %3d bits             richest window %5d distinct 6-grams "
                  "in %6d B" % (B, d, nbytes))
    return 0


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    sub = ap.add_subparsers(dest="cmd", required=True)
    tsv_default = os.path.join(REPO, "out", "sweep", "patterns.tsv")

    p = sub.add_parser("list")
    p.add_argument("--roots", nargs="+", required=True)
    p.add_argument("--extra", nargs="*", default=[], help="Group=path of a binary outside the roots")
    p.set_defaults(fn=cmd_list)

    p = sub.add_parser("scan")
    p.add_argument("--roots", nargs="+", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--tsv", default=tsv_default)
    p.add_argument("--workers", type=int, default=3)
    p.add_argument("--extra", nargs="*", default=[], help="Group=path of a binary outside the roots")
    p.add_argument("--only", nargs="*", default=[], help="name substrings (a smoke test)")
    p.set_defaults(fn=cmd_scan)

    p = sub.add_parser("analyze")
    p.add_argument("--out", required=True)
    p.add_argument("--tsv", default=tsv_default)
    p.add_argument("--thresholds", type=int, nargs="+", default=[16, 8])
    p.add_argument("--curve", type=int, default=0, help="seeds for the learning curve")
    p.add_argument("--patterns", nargs="*", default=[])
    p.add_argument("--publisher", action="store_true", help="one PUBLISHER per group")
    p.add_argument("--per-group", action="store_true")
    p.add_argument("--map", default=None, help="the sweep's corpus map: marks never-swept groups")
    p.add_argument("--committed", default=os.path.join(_HERE, "aob-ngram-index.bin.gz"))
    p.set_defaults(fn=cmd_analyze)

    for name, fn in (("build", cmd_build), ("verify", cmd_verify)):
        p = sub.add_parser(name)
        p.add_argument("--out", required=True)
        p.add_argument("--set", required=True, choices=["self-shipping", "self", "tp", "all"])
        p.add_argument("--exclude-group", nargs="*", default=[])
        if name == "build":
            p.add_argument("--thresholds", type=int, nargs="+", default=[16])
            p.add_argument("--verbose", action="store_true")
        else:
            p.add_argument("--index", required=True)
            p.add_argument("--tsv", default=tsv_default)
        p.set_defaults(fn=fn)

    p = sub.add_parser("recon")
    p.add_argument("--out", required=True)
    p.add_argument("--index", required=True)
    p.add_argument("--minus", nargs="*", default=[], help="reference indexes to subtract")
    p.add_argument("--bits", type=int, nargs="*", default=[0, 16, 64])
    p.add_argument("--binary", nargs="+", required=True, help="sha16 of scanned binaries")
    p.set_defaults(fn=cmd_recon)

    for sp in sub.choices.values():
        sp.add_argument("--layout", default=None,
                        help="the local layout file (default: <out>/layout.json)")
    a = ap.parse_args()
    if any(t < FLOOR for t in getattr(a, "thresholds", [])):
        print("threshold below %d: the cache does not hold those counts" % FLOOR)
        return 2
    if a.cmd == "build" and any(t not in (8, 16) for t in a.thresholds):
        print("build writes T=8 or T=16: the merged count class keeps only that distinction")
        return 2
    if a.cmd != "scan":
        load_layout(a.layout or os.path.join(getattr(a, "out", None) or ".", "layout.json"))
    return a.fn(a)


if __name__ == "__main__":
    sys.exit(main())
