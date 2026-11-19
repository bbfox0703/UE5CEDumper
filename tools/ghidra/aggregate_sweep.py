#!/usr/bin/env python3
"""aggregate_sweep.py — turn the per-binary scan_*.tsv files into the decisions.

    py tools/ghidra/aggregate_sweep.py out/sweep [> out/sweep/REPORT.md]

The sweep produces one TSV per PROGRAM. What we actually need are cross-binary answers:

  1. REGRESSION   — per target, per oracle: which pattern is SELECTED and does it reach truth?
                    This is the only question that decides whether adding AOBs broke anything.
  2. HOTSPOTS     — which patterns cost the most (hits the runtime validator must chew through
                    before reaching a correct match) and what do we get back for that cost?
  3. BAND AUDIT   — is a pattern's priority band justified by its specificity (literal bytes)
                    and its measured noise? The band rule is: <8 literal bytes => 800+.
  4. DEAD WEIGHT  — patterns that hit but are NEVER correct on any oracle: pure cost.
  5. LOAD-BEARING — patterns that are the SELECTED-and-correct one somewhere: removing these
                    would regress that engine version.

MONOLITHIC vs MODULAR matters and is reported separately. A Satisfactory engine DLL is a
4-30 MB .text; a shipped monolithic game EXE is 100-200 MB of mixed engine + game + middleware
code. Collision counts measured only on modular DLLs understate real-world noise several-fold,
so noise is reported per-MB and the monolithic-only figure is called out.
"""
import os, sys, glob, collections

OUT = sys.argv[1] if len(sys.argv) > 1 else "out/sweep"

rows = []
for p in sorted(glob.glob(os.path.join(OUT, "scan_*.tsv"))):
    with open(p, encoding="utf-8") as f:
        hdr = f.readline().rstrip("\n").split("\t")
        for ln in f:
            v = ln.rstrip("\n").split("\t")
            if len(v) != len(hdr):
                continue
            d = dict(zip(hdr, v))
            for k in ("pri", "pat_bytes", "lit_bytes", "lit_nibbles", "hits", "ok",
                      "decoy", "distinct_targets", "wasted"):
                d[k] = int(d[k])
            d["exec_mb"] = float(d["exec_mb"])
            d["hits_per_mb"] = float(d["hits_per_mb"])
            rows.append(d)

if not rows:
    sys.exit(f"no scan_*.tsv under {OUT}")

# A program with no executable bytes is a BROKEN Ghidra import (base 0000:0000, 0 functions).
# Several of the supplied projects contain these; scoring them would dilute every average.
broken = sorted({r["prog"] for r in rows if r["exec_mb"] <= 0})
rows = [r for r in rows if r["exec_mb"] > 0]

# A program counts as an ORACLE only if truth was actually supplied for it. Do NOT test
# `verdict != "NO-TRUTH"`: MISS is emitted whenever a pattern has zero hits, truth or no truth,
# so that test marks every noise probe as an oracle. Only the four scored verdicts below can be
# produced when a truth VA was present.
SCORED = {"UNIQUE-OK", "OK-FIRST", "OK-BEHIND", "DECOY-ONLY"}
progs = {}
for r in rows:
    progs.setdefault((r["tag"], r["prog"]), {"mb": r["exec_mb"], "truth": False})
    if r["verdict"] in SCORED:
        progs[(r["tag"], r["prog"])]["truth"] = True

oracles = {k for k, v in progs.items() if v["truth"]}
probes = {k for k, v in progs.items() if not v["truth"]}
TARGETS = ["GObjects", "GNames", "GWorld", "SparseDelegates", "GEngine"]

# The report is UTF-8 (it uses ✅/⚠️/❌). Writing via stdout inherits the console code page,
# which on a zh-TW box is cp950 and raises UnicodeEncodeError on the first tick mark.
_rep = open(os.path.join(OUT, "REPORT.md"), "w", encoding="utf-8")
W = _rep.write
W("# AOB sweep — aggregated report\n\n")
W(f"- programs scanned: **{len(progs)}**  (oracles with PDB truth: **{len(oracles)}**, "
  f"noise probes: **{len(probes)}**)\n")
W(f"- distinct patterns: **{len({r['id'] for r in rows})}**\n")
if broken:
    W(f"- **skipped {len(broken)} broken imports** (0 executable bytes): {', '.join(broken)}\n")
W("\n")

# ---------------------------------------------------------------- 1. REGRESSION MATRIX
#
# SELECTED IS NOT THE LANDING. scan_patterns.java's ">>> SELECTED" line names the first pattern that
# *hits*. That is NOT what the runtime settles on: Genau::ScanForTarget validates every match
# and, when they all fail, moves on to the next pattern. So a DECOY-ONLY pattern at the top of
# the list is a FALL-THROUGH (cost), not a wrong answer (correctness). The pattern the runtime
# actually lands on is the first one in priority order that has a correct hit — which is what
# this table reports. Reading the raw SELECTED line as "what we resolve to" overstates risk;
# ignoring the fall-through understates cost. Both numbers are reported here.
def walk(key, target):
    """Replay Genau::ScanForTarget for one (binary, target). Returns
       (landing pattern or None, wasted validations, decoy-only patterns crossed)."""
    rs = sorted([r for r in rows if (r["tag"], r["prog"]) == key and r["target"] == target],
                key=lambda r: r["pri"])
    wasted, crossed = 0, []
    for r in rs:
        if r["ok"]:
            return r, wasted + r["wasted"], crossed
        if r["hits"]:
            wasted += r["hits"]
            crossed.append(r["id"])
    return None, wasted, crossed

W("## 1. Regression matrix — what each oracle actually resolves to\n\n")
W("`Genau::ScanForTarget` walks priority order, validates every match, and moves on when they "
  "all fail. So the cell shows the pattern the runtime **lands on**, with `(Nw)` = the "
  "validations wasted getting there.\n\n")
W("| engine / binary | " + " | ".join(TARGETS) + " |\n")
W("|---|" + "---|" * len(TARGETS) + "\n")
regress, fallthrough = [], []
for key in sorted(oracles):
    tag, prog = key
    cells = []
    for t in TARGETS:
        present = [r for r in rows if (r["tag"], r["prog"]) == key and r["target"] == t
                   and r["verdict"] in SCORED]
        if not present:
            cells.append("—")
            continue
        land, wasted, crossed = walk(key, t)
        if land is None:
            cells.append(f"❌ NONE ({wasted}w)")
            regress.append((tag, prog, t, None, wasted, crossed))
            continue
        mark = "✅" if not crossed and land["verdict"] == "UNIQUE-OK" else "⚠️" if crossed or land["decoy"] else "✅"
        cells.append(f"{mark} {land['id']}" + (f" ({wasted}w)" if wasted else ""))
        if crossed:
            fallthrough.append((tag, prog, t, land, wasted, crossed))
    W(f"| {tag} / {prog[:34]} | " + " | ".join(cells) + " |\n")
W("\n✅ lands correct with no fall-through  ⚠️ lands correct but only after the validator "
  "rejected earlier matches  ❌ nothing resolves  — target not in this module\n\n")

if regress:
    W("### ❌ Unresolvable\n\n")
    for tag, prog, t, _l, wasted, crossed in regress:
        W(f"- **{tag}** `{prog}` **{t}** — no pattern reaches truth "
          f"({wasted} wasted validations, crossed {len(crossed)} pattern(s))\n")
    W("\n")
else:
    W("**Every target present in every oracle resolves to the correct address.**\n\n")

if fallthrough:
    W("### ⚠️ Fall-through (correctness OK, cost non-zero)\n\n")
    W("These rely on the validator rejecting earlier matches. Safe where the validator is "
      "strong (GObjects/GNames/GWorld/GEngine all deref and check structure); worth watching "
      "for **SparseDelegates**, whose validator can only range-check two ints.\n\n")
    for tag, prog, t, land, wasted, crossed in sorted(fallthrough, key=lambda x: -x[4]):
        W(f"- **{tag}** `{prog[:38]}` **{t}** → lands on `{land['id']}` after "
          f"{wasted} wasted validations; crossed {', '.join('`%s`' % c for c in crossed[:6])}"
          f"{' …' if len(crossed) > 6 else ''}\n")
    W("\n")

# ---------------------------------------------------------------- 2. PER-PATTERN ROLLUP
agg = {}
for r in rows:
    a = agg.setdefault(r["id"], {
        "id": r["id"], "target": r["target"], "src": r["src"], "pri": r["pri"],
        "lit": r["lit_bytes"], "plen": r["pat_bytes"],
        "hits": 0, "wasted": 0, "mono_hits": 0, "mono_mb": 0.0,
        "ok_bins": 0, "decoy_only_bins": 0, "hit_bins": 0,
        "sel_ok": [], "sel_bad": [], "probe_hit_bins": 0})
    a["hits"] += r["hits"]
    a["wasted"] += r["wasted"]
    if r["hits"]:
        a["hit_bins"] += 1
    key = (r["tag"], r["prog"])
    # "monolithic" = a game EXE, i.e. >=60 MB of executable bytes. That threshold cleanly
    # separates shipped monolithic titles from Satisfactory's split engine DLLs.
    if r["exec_mb"] >= 60:
        a["mono_hits"] += r["hits"]
        a["mono_mb"] = max(a["mono_mb"], r["exec_mb"])
    if key in oracles:
        if r["ok"]:
            a["ok_bins"] += 1
        elif r["hits"]:
            a["decoy_only_bins"] += 1
        if r["selected"] == "SELECTED":
            (a["sel_ok"] if r["ok"] else a["sel_bad"]).append(r["tag"])
    elif r["hits"]:
        a["probe_hit_bins"] += 1

W("## 2. Hotspots — the most expensive patterns\n\n")
W("`wasted` = matches the runtime validator must reject **before** reaching a correct one, "
  "summed over every binary. That, not the raw hit count, is the real cost.\n\n")
W("| pattern | target | pri | lit | Σhits | Σwasted | ok on | decoy-only on | selected✅ |\n")
W("|---|---|---|---|---|---|---|---|---|\n")
for a in sorted(agg.values(), key=lambda a: -a["wasted"])[:28]:
    W(f"| `{a['id']}` | {a['target']} | {a['pri']} | {a['lit']} | {a['hits']:,} | "
      f"{a['wasted']:,} | {a['ok_bins']} | {a['decoy_only_bins']} | "
      f"{len(a['sel_ok'])} |\n")
W("\n")

# ---------------------------------------------------------------- 3. BAND AUDIT
W("## 3. Band audit — specificity vs priority\n\n")
W("Rule of thumb in Himmel.h: **fewer than ~8 literal bytes ⇒ band 800+**, no matter what "
  "the pattern is anchored on. Rows below violate it in one direction or the other.\n\n")
W("| pattern | target | pri | lit bytes | Σhits | hits/MB (monolithic) | verdict |\n")
W("|---|---|---|---|---|---|---|\n")
bad_band = []
for a in sorted(agg.values(), key=lambda a: (a["target"], a["pri"])):
    hpm = a["mono_hits"] / a["mono_mb"] if a["mono_mb"] else 0.0
    too_high = a["lit"] < 8 and a["pri"] < 800
    too_low = a["lit"] >= 14 and a["pri"] >= 800 and a["ok_bins"] > 0
    if not (too_high or too_low):
        continue
    why = "under-specific for its band" if too_high else "more specific than its band implies"
    bad_band.append(a)
    W(f"| `{a['id']}` | {a['target']} | {a['pri']} | {a['lit']} | {a['hits']:,} | "
      f"{hpm:.1f} | {why} |\n")
if not bad_band:
    W("| — | | | | | | all patterns sit in a band consistent with their specificity |\n")
W("\n")

# ---------------------------------------------------------------- 4. DEAD WEIGHT
W("## 4. Dead weight — hits everywhere, never correct anywhere\n\n")
W("Patterns that fire on at least one binary but reach truth on **no** oracle. "
  "Each is pure validator cost unless it is the only thing covering an engine we cannot test.\n\n")
W("| pattern | target | pri | lit | Σhits | binaries hit | ok on |\n|---|---|---|---|---|---|---|\n")
dead = [a for a in agg.values() if a["ok_bins"] == 0 and a["hits"] > 0]
for a in sorted(dead, key=lambda a: -a["hits"]):
    W(f"| `{a['id']}` | {a['target']} | {a['pri']} | {a['lit']} | {a['hits']:,} | "
      f"{a['hit_bins']} | 0 |\n")
if not dead:
    W("| — | | | | | | none |\n")
W("\n")

W("### Never hits anything, anywhere\n\n")
never = sorted([a for a in agg.values() if a["hits"] == 0], key=lambda a: (a["target"], a["pri"]))
if never:
    W("Zero cost, but also zero evidence they work — they are insurance for engine builds "
      "not represented in the corpus.\n\n")
    by_t = collections.defaultdict(list)
    for a in never:
        by_t[a["target"]].append(f"`{a['id']}`")
    for t, ids in by_t.items():
        W(f"- **{t}** ({len(ids)}): {', '.join(ids)}\n")
else:
    W("None — every pattern fires somewhere.\n")
W("\n")

# ---------------------------------------------------------------- 5. LOAD-BEARING
W("## 5. Load-bearing patterns — removing these regresses an engine version\n\n")
W("| pattern | target | pri | is the SELECTED-and-correct pattern on |\n|---|---|---|---|\n")
for a in sorted(agg.values(), key=lambda a: (a["target"], a["pri"])):
    if a["sel_ok"]:
        W(f"| `{a['id']}` | {a['target']} | {a['pri']} | {', '.join(sorted(set(a['sel_ok'])))} |\n")
W("\n")

# ---------------------------------------------------------------- 6. NOISE ON MONOLITHIC
W("## 6. Noise on monolithic game EXEs\n\n")
W("Per-MB hit density on shipped monolithic binaries only — the figure that predicts how much "
  "a pattern will cost in a real game, as opposed to a 4 MB Satisfactory DLL.\n\n")
W("| pattern | target | pri | lit | hits/MB | Σhits (monolithic) |\n|---|---|---|---|---|---|\n")
mono = [a for a in agg.values() if a["mono_mb"] > 0 and a["mono_hits"] > 0]
for a in sorted(mono, key=lambda a: -(a["mono_hits"] / a["mono_mb"]))[:22]:
    W(f"| `{a['id']}` | {a['target']} | {a['pri']} | {a['lit']} | "
      f"{a['mono_hits']/a['mono_mb']:.1f} | {a['mono_hits']:,} |\n")
W("\n")

# ---------------------------------------------------------------- 7. COVERAGE PER TARGET
W("## 7. Coverage — how many patterns reach truth per oracle\n\n")
W("| engine / binary | " + " | ".join(TARGETS) + " |\n|---|" + "---|" * len(TARGETS) + "\n")
for key in sorted(oracles):
    tag, prog = key
    cells = []
    for t in TARGETS:
        rs = [r for r in rows if (r["tag"], r["prog"]) == key and r["target"] == t]
        ok = sum(1 for r in rs if r["ok"])
        hit = sum(1 for r in rs if r["hits"])
        cells.append(f"{ok}/{hit}" if hit else "—")
    W(f"| {tag} / {prog[:34]} | " + " | ".join(cells) + " |\n")
W("\n`correct / hitting` — a low first number with a high second means the validator is doing "
  "all the work.\n")

_rep.close()
print(f"wrote {os.path.join(OUT, 'REPORT.md')}")
