"""Index the movable units of a markdown file: every heading that is NOT inside a blockquote, with its level, its
line range (the heading through the line before the next heading of the same or a higher level) and its size.
argv[1] = the file, argv[2] = output TSV. Columns: start, end, level, bytes, has_open(⬜ anywhere in range), heading."""
import sys

src, out = sys.argv[1], sys.argv[2]
lines = open(src, encoding="utf-8").read().split("\n")
heads = []
in_code = False
for i, ln in enumerate(lines, 1):
    if ln.startswith("```"):
        in_code = not in_code
    if in_code:
        continue
    if ln.startswith("#"):
        level = len(ln) - len(ln.lstrip("#"))
        if 1 <= level <= 6 and ln[level:level + 1] == " ":
            heads.append((i, level, ln))
rows = []
for k, (start, level, text) in enumerate(heads):
    end = len(lines)
    for start2, level2, _ in heads[k + 1:]:
        if level2 <= level:
            end = start2 - 1
            break
    body = lines[start - 1:end]
    size = sum(len(x.encode("utf-8")) + 1 for x in body)
    has_open = any("⬜" in x for x in body)
    rows.append((start, end, level, size, int(has_open), text.replace("\t", " ")[:200]))
with open(out, "w", encoding="utf-8") as fh:
    for r in rows:
        fh.write("\t".join(str(c) for c in r) + "\n")
print(f"{len(rows)} units -> {out}")
