"""[ENUMSLOT-ANY-NAME] -- does a DataTable's TEnumAsByte column name its enumerator?

    py tools/verify/enumslot_datatable.py

WHY THIS EXISTS. A TEnumAsByte member is a ByteProperty, and FByteProperty keeps its UEnum in
its own subclass slot; FEnumProperty keeps its UEnum one pointer further on, after the
underlying property. `WalkDataTableRows` read every enum column at the EnumProperty slot, so a
TEnumAsByte column was read one pointer past the end of its FByteProperty. Build 3596 made it
read the type's own slot, but no fixture DataTable had such a column, so the fix rested on the
code alone. DumperTest 5.4's `FDumperTestTableRow.RowLane` is that column (tools/ue-sample/
README.md): `Lane_Left` / `Lane_Center` / `Lane_Right` (11 / 33 / 22) by the row's `Index`
mod 3, never `Lane_None`.

TWO CHECKS PER ROW, and they are not the same claim:
  * the cell's BYTE is the seeded value -- the column, the row data and the package are right.
    It does not touch the enum slot, so it must hold on every build, old or new.
  * `enum_name` is the seeded enumerator -- the slot is read right. This is the check a build
    before 3596 is expected to fail while still passing the first.
Both tables are read: `Table_Small` (8 rows, whole) and `Table_Big`'s first page.

Inject any build (`inject.py --dll` for an old one) into a fresh DumperTest; nothing else first.
Exit 0 = every row named right, 1 = a name is wrong or missing, 2 = a byte is wrong (the
fixture or the package, not the slot -- the name check is meaningless then).
"""
import io
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient           # noqa: E402
from ad4_contested import find_live_actor     # noqa: E402

OUT = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
LANES = [("Lane_Left", 11), ("Lane_Center", 33), ("Lane_Right", 22)]   # by Index % 3


def check_table(c, label, addr):
    r = c.request("walk_datatable_rows", addr=addr)
    if not r.get("ok", True) or "rows" not in r:
        raise SystemExit("enumslot: FAILED -- walk_datatable_rows(%s): %s" % (label, r.get("error")))
    bad_bytes, bad_names, shown = [], [], 0
    for row in r["rows"]:
        # Keyed case-insensitively, as UE compares FNames: the pool keeps the casing registered
        # FIRST, and the Development package names this column `index` (measured 2026-09-28).
        fields = {f["name"].lower(): f for f in row["fields"]}
        lane, idx = fields.get("rowlane"), fields.get("index", {}).get("value")
        if lane is None or idx is None:
            raise SystemExit("enumslot: FAILED -- %s row %s has no RowLane / Index column: the package "
                             "predates the column" % (label, row["row_name"]))
        want_name, want_val = LANES[int(idx) % 3]
        got_hex, got_name = lane.get("hex", ""), lane.get("enum_name", "")
        if got_hex.upper() != "%02X" % want_val:
            bad_bytes.append((row["row_name"], want_val, got_hex))
        if got_name != want_name:
            bad_names.append((row["row_name"], want_name, got_name or "(none)"))
        if shown < 3:
            print("  %s %s: type=%s hex=%s enum_name=%r" % (label, row["row_name"], lane.get("type"),
                                                          got_hex, got_name), file=OUT)
            shown += 1
    print("%s: row_count %s, %d read; bytes wrong %d, names wrong %d"
          % (label, r.get("row_count"), len(r["rows"]), len(bad_bytes), len(bad_names)), file=OUT)
    for b in (bad_bytes + bad_names)[:5]:
        print("    ", b, file=OUT)
    return len(r["rows"]), bad_bytes, bad_names


def main():
    with PipeClient(timeout=120.0) as c:
        print("build:", c.request("get_offsets").get("build_info"), file=OUT)
        actor = find_live_actor(c)
        w = c.request("walk_instance", addr=actor["addr"], array_limit=1)
        tables = {f["name"]: f["ptr"] for f in w["fields"] if f["name"].startswith("Table_")}
        rows = bytes_wrong = names_wrong = 0
        for label in ("Table_Small", "Table_Big"):
            if not tables.get(label):
                raise SystemExit("enumslot: FAILED -- %s is null on the actor" % label)
            n, bb, bn = check_table(c, label, tables[label])
            rows, bytes_wrong, names_wrong = rows + n, bytes_wrong + len(bb), names_wrong + len(bn)
    if bytes_wrong:
        print("VERDICT: %d cell byte(s) wrong -- the fixture or the package, not the enum slot" % bytes_wrong,
              file=OUT)
        return 2
    if names_wrong:
        print("VERDICT: %d of %d rows name the wrong enumerator or none" % (names_wrong, rows), file=OUT)
        return 1
    print("VERDICT: all %d rows carry the seeded byte AND name its enumerator" % rows, file=OUT)
    return 0


if __name__ == "__main__":
    sys.exit(main())
