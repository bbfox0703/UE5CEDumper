"""Move approved units of a markdown file byte-identical into an archive file, leaving one `> 📦` pointer block per
group, by the docs-archiving convention (docs/archive/README.md; the newest todo-closed-*.md header states the rule).

    py tools/docs_archive/move_units.py <doc> <units json> <archive file> <header file> <date> [--dry-run]

units.json: [{"start": n, "end": n, "kind": "section"|"row", "tableHeaderLine": n|0, "firstLine": "...", "tag": "..."}]
with 1-based line numbers of the CURRENT doc. A section unit must match a non-blockquote heading's full unit range
(the heading through the line before the next heading of the same or a higher level). A row unit is one table line.

Asserts before writing anything:
  * no unit overlaps another; each start line begins with its firstLine; sections are whole units;
  * new doc minus its pointer blocks == old doc minus the moved lines;
  * archive minus its added lines (header, table header + separator lines) == the moved lines, in order;
  * the moved lines interleaved back at their positions == the old doc, exactly.
"""
import json
import sys

doc_path, units_path, arch_path, head_path, date = sys.argv[1:6]
dry = "--dry-run" in sys.argv

raw = open(doc_path, "rb").read()
crlf = b"\r\n" in raw
text = raw.decode("utf-8").replace("\r\n", "\n")
assert not text.endswith("\n\n\n"), "unexpected trailing blank lines"
lines = text.split("\n")
units = sorted(json.load(open(units_path, encoding="utf-8")), key=lambda u: u["start"])
arch_name = arch_path.replace("\\", "/").split("/")[-1]


def heading_level(ln):
    if not ln.startswith("#"):
        return 0
    lv = len(ln) - len(ln.lstrip("#"))
    return lv if 1 <= lv <= 6 and ln[lv:lv + 1] == " " else 0


# Code fences: a heading inside one is not a heading.
in_code, code = False, set()
for i, ln in enumerate(lines, 1):
    if ln.startswith("```"):
        in_code = not in_code
    elif in_code:
        code.add(i)


LAST = len(lines) - 1 if lines[-1] == "" else len(lines)   # the "" after the file's final newline is not a line


def unit_end(start):
    lv = heading_level(lines[start - 1])
    for j in range(start + 1, LAST + 1):
        if j not in code and 0 < heading_level(lines[j - 1]) <= lv:
            return j - 1
    return LAST


def only_old_pointer(a, b):
    """Lines a..b are blank lines around an earlier pass's `> 📦` pointer block (which stays where it is)."""
    body = [lines[k - 1] for k in range(a, b + 1)]
    quoted = [x for x in body if x.strip()]
    return bool(quoted) and quoted[0].startswith("> 📦") and all(x.startswith(">") for x in quoted)


prev_end = 0
for u in units:
    s, e = u["start"], u["end"]
    assert 1 <= s <= e <= len(lines), f"range out of file: {u}"
    assert s > prev_end, f"overlap at {s}"
    assert lines[s - 1].startswith(u["firstLine"].rstrip()[:60]), f"line {s} does not begin with firstLine: {lines[s - 1][:80]!r}"
    if u["kind"] == "section":
        assert heading_level(lines[s - 1]) and s not in code, f"{s} is not a heading"
        ue = unit_end(s)
        assert e == ue or (e < ue and only_old_pointer(e + 1, ue)), f"{s}: end {e} is not the unit's end {ue}"
    else:
        h = u["tableHeaderLine"]
        # A row is one `|` line plus any continuation lines this file writes under it (`1. …`), up to the next row.
        assert lines[s - 1].startswith("|"), f"{s} is not a table row"
        assert all(not lines[k - 1].startswith("|") and lines[k - 1].strip() for k in range(s + 1, e + 1)), f"{s}-{e} spans more than one row"
        assert e == LAST or lines[e].startswith("|") or not lines[e].strip(), f"{s}-{e} stops inside a row"
        assert h and lines[h - 1].startswith("|") and lines[h].startswith("|") and set(lines[h].replace("|", "").replace(":", "").strip()) <= {"-"}, f"{h} is not a table header + separator"
        # This file's tables let a row run on over blank lines and paragraphs, so membership is: no heading and no
        # second header separator between the table's header and the row.
        assert h < s and not any(heading_level(lines[k - 1]) and k not in code for k in range(h + 2, s)), f"a heading sits between {h} and row {s}"
        assert not any(lines[k - 1].startswith("|---") for k in range(h + 2, s)), f"another table starts between {h} and row {s}"
    prev_end = e

moved = set()
for u in units:
    moved.update(range(u["start"], u["end"] + 1))

# Groups: consecutive section units (only blank lines may separate... no: strictly adjacent) share one pointer; rows
# share one pointer per table, placed before that table's header line.
section_groups, row_tables = [], {}
for u in units:
    if u["kind"] == "row":
        row_tables.setdefault(u["tableHeaderLine"], []).append(u)
    elif section_groups and section_groups[-1][-1]["end"] + 1 == u["start"]:
        section_groups[-1].append(u)
    else:
        section_groups.append([u])


def tags_of(group):
    seen = []
    for u in group:
        t = u["tag"].strip() or u["firstLine"].strip()[:60]
        if t not in seen:
            seen.append(t)
    return seen


def pointer(group, rows):
    tags = tags_of(group)
    shown = ", ".join(f"`{t}`" if t.startswith("[") else t for t in tags[:12])
    more = f" and {len(tags) - 12} more" if len(tags) > 12 else ""
    what = f"{len(group)} closed row(s) of the table below" if rows else f"{len(group)} closed unit(s)"
    return [f"> 📦 **Archived {date}:** {what} moved byte-identical to "
            f"[archive/{arch_name}](archive/{arch_name}): {shown}{more}. Grep the tag; tags resolve across files.", ""]


before = {}   # line number -> pointer lines to insert before it
for g in section_groups:
    before.setdefault(g[0]["start"], []).extend(pointer(g, False))
for h, rows in row_tables.items():
    before.setdefault(h, []).extend(pointer(rows, True))

new_lines, pointer_idx = [], set()
for i, ln in enumerate(lines, 1):
    if i in before:
        for p in before[i]:
            pointer_idx.add(len(new_lines))
            new_lines.append(p)
    if i not in moved:
        new_lines.append(ln)

# Archive body: units in order; before the first moved row of a table, a blank line + header + separator.
body, added_idx, done_tables = [], set(), set()
for u in units:
    if u["kind"] == "row" and u["tableHeaderLine"] not in done_tables:
        h = u["tableHeaderLine"]
        for extra in ("", lines[h - 1], lines[h]):
            added_idx.add(len(body))
            body.append(extra)
        done_tables.add(h)
    body.extend(lines[u["start"] - 1:u["end"]])

header = open(head_path, encoding="utf-8").read().replace("\r\n", "\n").rstrip("\n").split("\n") + ["", "-----", ""]
arch_lines = header + body

# The assertions.
moved_seq = [lines[i - 1] for i in sorted(moved)]
kept_seq = [lines[i - 1] for i in range(1, len(lines) + 1) if i not in moved]
assert [ln for k, ln in enumerate(new_lines) if k not in pointer_idx] == kept_seq, "doc minus pointers != old minus moved"
assert [ln for k, ln in enumerate(body) if k not in added_idx] == moved_seq, "archive body minus added != moved"
rebuilt, mi, ki = [], 0, 0
for i in range(1, len(lines) + 1):
    if i in moved:
        rebuilt.append(moved_seq[mi]); mi += 1
    else:
        rebuilt.append(kept_seq[ki]); ki += 1
assert rebuilt == lines, "interleave does not rebuild the original"

old_bytes = len(text.encode("utf-8"))
new_text = "\n".join(new_lines)
moved_bytes = sum(len(x.encode("utf-8")) + 1 for x in moved_seq)
print(f"{doc_path}: {len(units)} unit(s), {len(moved)} lines / {moved_bytes:,} bytes moved; "
      f"{old_bytes:,} -> {len(new_text.encode('utf-8')):,} bytes, {len(lines):,} -> {len(new_lines):,} lines; "
      f"{len(section_groups)} section pointer(s), {len(row_tables)} table pointer(s)")
if not dry:
    out = new_text.replace("\n", "\r\n") if crlf else new_text
    open(doc_path, "wb").write(out.encode("utf-8"))
    open(arch_path, "wb").write(("\n".join(arch_lines) + "\n").encode("utf-8"))
    print(f"wrote {doc_path} and {arch_path}")
