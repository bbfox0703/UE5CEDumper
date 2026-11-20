"""List every approved unit whose range is not one whole unit (or a run of whole sibling units), with what the index
says. argv[1] = doc, argv[2] = approved json, argv[3] = units tsv."""
import json
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
lines = open(sys.argv[1], encoding="utf-8").read().split("\n")
units = json.load(open(sys.argv[2], encoding="utf-8"))
idx = [tuple(r.split("\t")) for r in open(sys.argv[3], encoding="utf-8").read().splitlines()]
idx = [(int(a), int(b), int(c), int(d), e, f) for a, b, c, d, e, f in idx]
by_start = {r[0]: r for r in idx}
bad = 0
for u in sorted(units, key=lambda x: x["start"]):
    if u["kind"] != "section":
        continue
    s, e = u["start"], u["end"]
    r = by_start.get(s)
    if not r:
        print(f"NOT A HEADING {s}-{e} {u['tag']}")
        bad += 1
        continue
    if r[1] == e:
        continue
    # a run of whole sibling units starting at s?
    run_end, lvl, k = r[1], r[2], s
    while run_end < e:
        nxt = by_start.get(run_end + 1)
        if not nxt or nxt[2] != lvl:
            break
        run_end = nxt[1]
    tag = "SIBLING-RUN-OK" if run_end == e else "MISMATCH"
    if tag == "MISMATCH":
        bad += 1
    print(f"{tag} {s}-{e} (unit ends {r[1]}, run reaches {run_end}) L{r[2]} {u['tag']}")
print(f"{bad} bad")
