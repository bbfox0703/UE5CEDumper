#!/usr/bin/env python3
"""Hold Dump Explorer's Compare to diff_dumps.py on a pair of REAL dumps.  [DUMPDIFF-UI]

    py tools/verify/dumpdiff_real_parity.py <old.jsonl> <new.jsonl> [--include-engine] [--name NAME]

WHY. DumpDiffService (ui/UE5DumpUI) is a C# port of scripts/analysis/diff_dumps.py, held to it by 26 small
committed cases. Real dumps (tens of thousands of types, every property type a game uses) cannot be committed,
so this rig computes the script's result for a pair -- its canonical(), the same plain data the committed
cases carry -- writes it to out/dumpdiff-real/<name>/, and runs the one C# test that reads that folder
(DumpDiffParityTests.A_real_pair_named_by_the_environment_matches_too, skipped unless DUMPDIFF_REAL_DIR is set).

EXIT. 0 when the port computes what the script computes; 1 when it does not, or the test did not run (a test run
that ran nothing is a failure, not a pass); 2 on bad arguments.
"""
from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts" / "analysis"))
import diff_dumps  # noqa: E402  (the script is the reference, imported from its own folder)

TEST = "FullyQualifiedName~DumpDiffParityTests.A_real_pair_named_by_the_environment_matches_too"


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("old")
    ap.add_argument("new")
    ap.add_argument("--include-engine", action="store_true")
    ap.add_argument("--name", default=None, help="folder name under out/dumpdiff-real/ (default: <old>__<new>)")
    args = ap.parse_args()
    old, new = Path(args.old).resolve(), Path(args.new).resolve()
    for p in (old, new):
        if not p.is_file():
            print(f"error: not a file: {p}")
            return 2

    name = args.name or f"{old.stem}__{new.stem}" + ("__engine" if args.include_engine else "")
    out = ROOT / "out" / "dumpdiff-real" / name
    out.mkdir(parents=True, exist_ok=True)
    diff = diff_dumps.diff_dumps(diff_dumps.load_dump(old), diff_dumps.load_dump(new),
                                 include_engine=args.include_engine)
    (out / "expected.json").write_text(diff_dumps._expected_text(diff, args.include_engine), encoding="utf-8",
                                       newline="\n")
    (out / "pair.json").write_text(json.dumps({"old": str(old), "new": str(new)}, indent=1), encoding="utf-8")
    c = diff_dumps.canonical(diff)
    print(f"script: classes +{len(c['classes']['added'])} -{len(c['classes']['removed'])} "
          f"~{len(c['classes']['changed'])} ={c['classes']['unchanged']}; structs "
          f"{c['structs']['skipped'] or 'compared'}; enums {c['enums']['skipped'] or 'compared'}")

    env = dict(os.environ, DUMPDIFF_REAL_DIR=str(out))
    r = subprocess.run(["dotnet", "test", str(ROOT / "ui" / "UE5DumpUI.Tests" / "UE5DumpUI.Tests.csproj"),
                        "-c", "Release", "--filter", TEST], cwd=ROOT, env=env, capture_output=True, text=True,
                       encoding="utf-8", errors="replace")
    text = r.stdout + r.stderr
    (out / "dotnet-test.log").write_text(text, encoding="utf-8")
    counts = {}
    for line in text.splitlines():
        key, sep, value = line.strip().partition(":")
        if sep and key in ("total", "succeeded", "failed", "skipped"):
            counts[key] = value
    print("port:  ", ", ".join(f"{k} {v.strip()}" for k, v in counts.items()) or "(no test summary)")
    ran = counts.get("succeeded", "0").strip() == "1" and counts.get("failed", "1").strip() == "0"
    if r.returncode != 0 or not ran:
        print(f"FAIL: the port differs, or the test did not run (exit {r.returncode}); see {out / 'dotnet-test.log'}")
        return 1
    print(f"PASS: the port computes what diff_dumps.py computes ({out})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
