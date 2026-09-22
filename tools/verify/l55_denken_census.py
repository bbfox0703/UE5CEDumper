#!/usr/bin/env python3
r"""L55 `[W5-DENKEN-DEADGUARD]`: census every game function's walk_function_props, then diff two runs.

    py tools/verify/l55_denken_census.py run  --label B --log-dir "%LOCALAPPDATA%\UE5CEDumper\Logs\DumperTest-Win64-Shipping"
    py tools/verify/l55_denken_census.py diff out\l55\A.json out\l55\B.json

WHY. 20448583 REMOVED a guard in Denken's native disassembler that was dead in every reachable
state (`callsFollowed >= 1` whenever `depth > 0`). A dead guard has no live trigger by construction,
so the only live check is a REGRESSION: the same package, one fresh process with the DLL built from
HEAD (B) and one with the guard put back (A, `tools/verify/staging/l55-denken-before.json`), must
give IDENTICAL results for every game function -- method, unmapped count, budget flag and the
mapped props -- and identical (N mapped, U unmapped, I instrs, C calls) tuples in offsets-0.log.
Addresses differ under ASLR; nothing else may.

⚠ Each run needs a FRESH process: Denken caches per function, and a warm cache answers from the
previous DLL's analysis. Run with the UI closed (it holds 2 of the 3 pipe slots).
"""
import argparse
import json
import os
import re
import sys
import time

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pipe_client import PipeClient   # noqa: E402

TUPLE = re.compile(r"AnalyzeNativeFunctionProps: (.*?) -> (\d+) mapped props \((\d+) unmapped, (\d+) instrs, (\d+) calls(, BUDGET)?\)")


def run(a):
    out = {"label": a.label, "functions": {}, "log_tuples": []}
    logf = os.path.join(os.path.expandvars(a.log_dir), "offsets-0.log")
    start = os.path.getsize(logf) if os.path.exists(logf) else 0
    with PipeClient() as c:
        out["build"] = c.assert_build()
        st = c.ensure_scanned()
        out["object_count"] = st.get("object_count")
        r = c.request("list_all_functions", game_only=True)
        d = r.get("data", r)
        fns = d.get("functions") or []
        out["total_functions"] = d.get("total_functions")
        out["truncated"] = d.get("truncated")
        t0 = time.time()
        for i, f in enumerate(fns):
            key = "%s.%s" % (f.get("class_path"), f.get("func_name"))
            rr = c.request("walk_function_props", func_addr=f["func_addr"])
            dd = rr.get("data", rr)
            props = sorted((p.get("name"), p.get("offset"), p.get("confidence"), p.get("occurrences"),
                            p.get("write_count"), p.get("scope"), p.get("type"))
                           for p in (dd.get("props") or []))
            rec = {"method": dd.get("method"), "unmapped": dd.get("unmapped"),
                   "budget_hit": dd.get("budget_hit"), "props": props}
            if key in out["functions"]:
                key = "%s#%d" % (key, i)       # overloads by name across the pool: keep both, in order
            out["functions"][key] = rec
            if (i + 1) % 500 == 0:
                print("  %d/%d functions (%.0f s)" % (i + 1, len(fns), time.time() - t0))
    time.sleep(1.0)
    if os.path.exists(logf):
        with open(logf, "rb") as fh:
            fh.seek(start)
            for ln in fh.read().decode("utf-8", "replace").splitlines():
                m = TUPLE.search(ln)
                if m:
                    out["log_tuples"].append([re.sub(r"0x[0-9A-Fa-f]+", "<addr>", m.group(1)),
                                              int(m.group(2)), int(m.group(3)), int(m.group(4)),
                                              int(m.group(5)), bool(m.group(6))])
    os.makedirs(os.path.dirname(a.out) or ".", exist_ok=True)
    json.dump(out, open(a.out, "w", encoding="utf-8"), indent=0)
    meth = {}
    for v in out["functions"].values():
        meth[v["method"]] = meth.get(v["method"], 0) + 1
    print("census %s: %d functions (total_functions %s, truncated %s), methods %s, %d log tuples -> %s"
          % (a.label, len(out["functions"]), out["total_functions"], out["truncated"], meth,
             len(out["log_tuples"]), a.out))
    return 0


def diff(a):
    A = json.load(open(a.a, encoding="utf-8"))
    B = json.load(open(a.b, encoding="utf-8"))
    ka, kb = set(A["functions"]), set(B["functions"])
    print("A %s: %d functions, %d tuples | B %s: %d functions, %d tuples"
          % (A["label"], len(ka), len(A["log_tuples"]), B["label"], len(kb), len(B["log_tuples"])))
    bad = 0
    if ka != kb:
        print("FUNCTION SETS DIFFER: A-only %d, B-only %d" % (len(ka - kb), len(kb - ka)))
        bad += 1
    fields = {"method": 0, "unmapped": 0, "budget_hit": 0, "props": 0}
    for k in sorted(ka & kb):
        for f in fields:
            if A["functions"][k][f] != B["functions"][k][f]:
                fields[f] += 1
                if fields[f] <= 3:
                    print("  DIFF %s %s: A=%s B=%s" % (f, k, str(A["functions"][k][f])[:120],
                                                     str(B["functions"][k][f])[:120]))
    print("per-field differences over %d shared functions: %s" % (len(ka & kb), fields))
    bad += sum(fields.values())
    ta, tb = A["log_tuples"], B["log_tuples"]
    tdiff = sum(1 for x, y in zip(ta, tb) if x[1:] != y[1:]) + abs(len(ta) - len(tb))
    print("offsets-0.log tuples: A %d, B %d, differing (in order, counts only): %d" % (len(ta), len(tb), tdiff))
    bad += tdiff
    nat = sum(1 for v in B["functions"].values() if v["method"] == "disasm")
    nat_props = sum(1 for v in B["functions"].values() if v["method"] == "disasm" and v["props"])
    print("anti-vacuity: %d functions went through the native disassembler (%d with mapped props)" % (nat, nat_props))
    if nat == 0:
        print("VACUOUS: no function reached Denken -- the comparison says nothing")
        return 2
    print("VERDICT: %s" % ("IDENTICAL" if bad == 0 else "DIFFERENT (%d)" % bad))
    return 0 if bad == 0 else 1


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    r = sub.add_parser("run")
    r.add_argument("--label", required=True)
    r.add_argument("--log-dir", required=True, help="the game's %%LOCALAPPDATA%%\\UE5CEDumper\\Logs\\<Process> folder")
    r.add_argument("--out", default=None)
    d = sub.add_parser("diff")
    d.add_argument("a")
    d.add_argument("b")
    a = ap.parse_args()
    if a.cmd == "run":
        a.out = a.out or os.path.join("out", "l55", "%s.json" % a.label)
        return run(a)
    return diff(a)


if __name__ == "__main__":
    sys.exit(main())
