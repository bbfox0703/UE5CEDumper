"""Group approved units by their enclosing '## ' section: count, bytes, sections vs rows, and the tags. argv[1] = doc,
argv[2] = approved json, argv[3] = units tsv."""
import json
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
lines = open(sys.argv[1], encoding="utf-8").read().split("\n")
units = sorted(json.load(open(sys.argv[2], encoding="utf-8")), key=lambda u: u["start"])
idx = [r.split("\t") for r in open(sys.argv[3], encoding="utf-8").read().splitlines()]
tops = [(int(r[0]), int(r[1]), r[5]) for r in idx if r[2] == "2"]
groups = {}
for u in units:
    top = next((t for t in tops if t[0] <= u["start"] <= t[1]), (0, 0, "(preamble)"))
    g = groups.setdefault(top[2], {"n": 0, "rows": 0, "secs": 0, "bytes": 0, "tags": []})
    g["n"] += 1
    g["rows" if u["kind"] == "row" else "secs"] += 1
    g["bytes"] += sum(len(x.encode("utf-8")) + 1 for x in lines[u["start"] - 1:u["end"]])
    g["tags"].append(u["tag"])
for head, g in groups.items():
    print(f"{g['bytes'] / 1024:6.0f} KB  {g['secs']:3} sec {g['rows']:3} rows  {head[:100]}")
    print("          " + ", ".join(t for t in g["tags"])[:600])
