"""Independent check of a split, from git's copy of the original: (1) the new doc minus this pass's pointer blocks is
the original minus some lines; (2) those removed lines, in order, are exactly the archive body minus the table header
triplets it added; (3) no removed line is a changed line (every new-doc line is an original line, in order).
argv[1] = path in repo (e.g. docs/todo.md), argv[2] = the archive file, argv[3] = the pointer marker text
(e.g. "> 📦 **Archived 2026-11-20:**"), argv[4] = the approved units JSON. Run it BEFORE committing the move:
it compares against git's HEAD copy."""
import subprocess
import sys

doc, arch, marker = sys.argv[1:4]
orig = subprocess.run(["git", "show", f"HEAD:{doc}"], capture_output=True, check=True).stdout.decode("utf-8").replace("\r\n", "\n").split("\n")
new = open(doc, encoding="utf-8").read().replace("\r\n", "\n").split("\n")

kept = []
i = 0
while i < len(new):
    if new[i].startswith(marker):
        i += 1
        while i < len(new) and new[i].startswith(">"):   # a wrapped pointer
            i += 1
        assert i < len(new) and new[i] == "", f"pointer at {i} not followed by a blank line"
        i += 1
        continue
    kept.append(new[i])
    i += 1

# kept must be a subsequence of orig; collect what is skipped.
removed, j = [], 0
for ln in kept:
    while j < len(orig) and orig[j] != ln:
        removed.append(orig[j])
        j += 1
    assert j < len(orig), "a kept line is not in the original, in order"
    j += 1
removed.extend(orig[j:])

a = open(arch, encoding="utf-8").read().replace("\r\n", "\n").split("\n")
body = a[a.index("-----") + 2:]
if body and body[-1] == "":
    body = body[:-1]
# Rebuild what the archive body must be straight from git's original and the unit list: per unit in order, the
# table's header + separator (from the original) before the first moved row of each table, then the unit's lines.
import json
units = sorted(json.load(open(sys.argv[4], encoding="utf-8")), key=lambda u: u["start"])
expect, moved_expect, seen = [], [], set()
for u in units:
    if u["kind"] == "row" and u["tableHeaderLine"] not in seen:
        h = u["tableHeaderLine"]
        expect += ["", orig[h - 1], orig[h]]
        seen.add(h)
    expect += orig[u["start"] - 1:u["end"]]
    moved_expect += orig[u["start"] - 1:u["end"]]
print(f"original {len(orig)} lines, new {len(new)}, kept {len(kept)}, removed {len(removed)}, "
      f"archive body {len(body)}, rebuilt {len(expect)} ({len(seen)} table header(s))")
moved_lines = set()
for u in units:
    moved_lines.update(range(u["start"], u["end"] + 1))
assert len(removed) == len(moved_expect), "a different number of lines left the doc"
assert kept == [orig[n - 1] for n in range(1, len(orig) + 1) if n not in moved_lines], "the doc minus pointers is not the original minus the units"
assert body == expect, "the archive body is not the units' lines plus their table headers"
print("OK: byte-identical split")
