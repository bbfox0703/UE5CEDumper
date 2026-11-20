"""Join an archive workflow's proposals with its refuters' verdicts. A unit is approved only when a verdict for it
exists and says move=true; a unit no refuter judged stays. Prints the per-region tally and every refusal.

    py tools/docs_archive/approve.py <workflow output file> <out dir>

The workflow output holds {"proposals": [{"region", "file", "move": [unit, ...]}], "verdicts": [{"region", "start",
"move", "reason"}]} (a top-level "result" wrapper is unwrapped). Writes <out dir>/approved_todo.json and
<out dir>/approved_register.json, each a unit list for move_units.py.
"""
import json
import pathlib
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
src, out_dir = sys.argv[1], pathlib.Path(sys.argv[2])
raw = open(src, encoding="utf-8").read()
d = json.loads(raw[raw.find("{"):raw.rfind("}") + 1])
d = d.get("result", d)
verdict = {(v["region"], v["start"]): v for v in d["verdicts"]}
out = {"docs/todo.md": [], "docs/verification-register.md": []}
for p in d["proposals"]:
    ok = refused = unjudged = 0
    for u in p["move"]:
        v = verdict.get((p["region"], u["start"]))
        if v is None:
            unjudged += 1
            print(f"  UNJUDGED {p['region']} {u['start']} {u['tag']}")
        elif v["move"]:
            ok += 1
            out[p["file"]].append(u)
        else:
            refused += 1
            print(f"  KEPT {p['region']} {u['start']}-{u['end']} {u['tag']}: {v['reason'][:220]}")
    print(f"{p['region']}: proposed {len(p['move'])}, approved {ok}, refused {refused}, unjudged {unjudged}")
out_dir.mkdir(parents=True, exist_ok=True)
for key, name in (("docs/todo.md", "approved_todo.json"), ("docs/verification-register.md", "approved_register.json")):
    with open(out_dir / name, "w", encoding="utf-8") as fh:
        json.dump(out[key], fh, ensure_ascii=False, indent=1)
print({k: len(v) for k, v in out.items()})
