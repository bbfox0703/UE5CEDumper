"""V8 -- the DLL half of the DataTable drill-down cap: is N right, and is it the DATA's N?

    py tools/verify/v8_datatable_cap.py

WHAT IS ALREADY PINNED, AND WHY THIS RIG IS NOT REDUNDANT.
todo.md's "V8 -- what the tests already pin" shows the three UI disclosures are
covered by C# tests (V8_DataTableDrill_Truncated_BadgesCrumbHeaderAndStatus,
V8_ContainerTruncation_FixedCapStatusLine_DoesNotMentionTheSlider,
V8_DataTableDrill_Complete_SaysNothing). Those assert the VIEWMODEL's strings from
SYNTHETIC input. They say nothing about whether the DLL hands the ViewModel a
correct N -- and it did not: [DTROWMAP-2026-08-23] had ProbeRowMapOffset serving a
NEIGHBOURING table's RowMap, so a 100-row table reported 8. Every one of those tests
passed throughout. This rig closes the half they structurally cannot reach.

THE CAP IS DERIVED, NOT QUOTED: Ubel.h declares
    WalkDataTableRows(uintptr_t, int32_t offset, int32_t limit = 64)
and Fern.cpp's handler reads `request.value("limit", 64)`. Both are read at runtime
below rather than hard-coded here, so this file cannot drift from the DLL.

FOUR CHECKS, and the last two are the ones a commercial game cannot give you:

  1. TRUNCATED  Table_Big (100 rows) at the default limit -> row_count 100, 64 rows.
  2. COMPLETE   Table_Small (8 rows) -> row_count 8, 8 rows, nothing truncated.
                This is the NEGATIVE CONTROL. Without it, check 1 cannot distinguish
                "reports truncation correctly" from "always reports truncation".
  3. N FOLLOWS THE DATA   V8_RebuildBigTable(77) -> row_count must become 77.
                A constant that happens to read 100 passes check 1 forever. Changing
                N on demand is the only way to prove the number is read, and you
                cannot ask a shipped game to rebuild a DataTable.
  4. N IS EXACT           V8_RemoveOneTableRow() -> row_count must become 76.
                An off-by-one or a capacity-vs-count confusion survives check 3.

Also asserted throughout: the FText Caption column decodes. It is CJK by construction
("走一步 <i>"), so a mis-decode cannot accidentally produce it -- and it was blank
until [DTTEXT-2026-08-23] added the TextProperty branch the row reader was missing.
"""
from __future__ import annotations

import argparse
import io
import json
import pathlib
import re
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient           # noqa: E402
from ad4_contested import find_live_actor, invoke   # noqa: E402

OUT = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
ROOT = pathlib.Path(__file__).resolve().parents[2]


def derived_cap():
    """Read the default page size out of the DLL source, so this file cannot drift.

    The Fern side must be read from INSIDE the walk_datatable_rows handler. `limit`
    is a parameter name a dozen commands share, and an unscoped
    `request.value("limit", N)` search finds whichever handler happens to come first
    -- it returned 200 (some other command's default) and reported a disagreement
    that did not exist. A detector has to be right about WHERE it reads, not only
    about what it matches.
    """
    h = (ROOT / "dll/src/Ubel.h").read_text(encoding="utf-8", errors="replace")
    m = re.search(r"WalkDataTableRows\s*\([^)]*limit\s*=\s*(\d+)", h, re.S)

    f = (ROOT / "dll/src/Fern.cpp").read_text(encoding="utf-8", errors="replace")
    start = f.find("CMD_WALK_DATATABLE_ROWS")
    block = f[start:start + 1200] if start >= 0 else ""
    m2 = re.search(r'request\.value\("limit",\s*(\d+)\)', block)
    return (int(m.group(1)) if m else None), (int(m2.group(1)) if m2 else None)


def tables(c, actor_addr):
    w = c.request("walk_instance", addr=actor_addr, array_limit=1)
    return {f["name"]: f["ptr"] for f in w["fields"] if f["name"].startswith("Table_")}


def walk(c, addr, **kw):
    r = c.request("walk_datatable_rows", addr=addr, **kw)
    if not r.get("ok"):
        raise SystemExit("v8: FAILED -- walk_datatable_rows(%s): %s" % (addr, r.get("error")))
    return r


def captions_ok(rows):
    """Every row's Caption must decode to the seeded CJK, indexed."""
    bad = []
    for row in rows:
        cap = next((f for f in row["fields"] if f["name"] == "Caption"), None)
        idx = next((f.get("value") for f in row["fields"] if f["name"] == "Index"), "?")
        want = "走一步 %s" % idx
        got = (cap or {}).get("value")
        if got != want:
            bad.append((row["row_name"], want, got))
    return bad


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.parse_args(argv)
    fails = []

    cap_h, cap_f = derived_cap()
    print("derived default page size: Ubel.h=%s  Fern.cpp=%s" % (cap_h, cap_f), file=OUT)
    if cap_h != cap_f or cap_h is None:
        fails.append("the two declared defaults disagree (%s vs %s)" % (cap_h, cap_f))
    CAP = cap_h or 64

    with PipeClient() as c:
        c.assert_build()
        c.ensure_scanned()
        a = find_live_actor(c)
        t = tables(c, a["addr"])
        print("tables: %s\n" % json.dumps(t), file=OUT)

        # ---- 1. TRUNCATED ----
        r = walk(c, t["Table_Big"])
        big_n, big_rows = r["row_count"], len(r["rows"])
        print("[1] Table_Big at the default limit: row_count=%d rows=%d (cap %d)"
              % (big_n, big_rows, CAP), file=OUT)
        ok1 = big_n == 100 and big_rows == CAP and big_n > big_rows
        if not ok1:
            fails.append("1: expected row_count 100 with %d rows, got %d/%d"
                         % (CAP, big_n, big_rows))
        full = walk(c, t["Table_Big"], limit=1000)
        print("    raising the limit to 1000: row_count=%d rows=%d"
              % (full["row_count"], len(full["rows"])), file=OUT)
        ok1 = ok1 and full["row_count"] == len(full["rows"]) == 100
        badcap = captions_ok(full["rows"])
        print("    FText Caption decodes on all %d rows: %s"
              % (len(full["rows"]), "yes" if not badcap else "NO -- %s" % badcap[:2]), file=OUT)
        if badcap:
            fails.append("1: %d row(s) have a wrong/blank Caption" % len(badcap))

        # ---- 2. NEGATIVE CONTROL ----
        s = walk(c, t["Table_Small"])
        small_n, small_rows = s["row_count"], len(s["rows"])
        print("\n[2] NEGATIVE CONTROL Table_Small: row_count=%d rows=%d -> %s"
              % (small_n, small_rows,
                 "complete, nothing to disclose" if small_n == small_rows
                 else "TRUNCATED (wrong)"), file=OUT)
        ok2 = small_n == small_rows == 8
        if not ok2:
            fails.append("2: expected 8/8, got %d/%d" % (small_n, small_rows))

        # ---- 3. N FOLLOWS THE DATA ----
        print("\n[3] N follows the data -- V8_RebuildBigTable(77)", file=OUT)
        invoke(c, a["addr"], "V8_RebuildBigTable", parms_size=4,
               params_hex=(77).to_bytes(4, "little").hex())
        t2 = tables(c, a["addr"])
        moved = t2["Table_Big"] != t["Table_Big"]
        r3 = walk(c, t2["Table_Big"], limit=1000)
        print("    Table_Big %s -> %s (%s)   row_count=%d rows=%d"
              % (t["Table_Big"], t2["Table_Big"],
                 "new object" if moved else "same object",
                 r3["row_count"], len(r3["rows"])), file=OUT)
        ok3 = r3["row_count"] == len(r3["rows"]) == 77
        if not ok3:
            fails.append("3: after RebuildBigTable(77) expected 77, got %d/%d"
                         % (r3["row_count"], len(r3["rows"])))
        r3c = walk(c, t2["Table_Big"])
        print("    and at the default limit again: row_count=%d rows=%d"
              % (r3c["row_count"], len(r3c["rows"])), file=OUT)
        ok3 = ok3 and r3c["row_count"] == 77 and len(r3c["rows"]) == CAP

        # ---- 4. N IS EXACT ----
        print("\n[4] N is exact -- V8_RemoveOneTableRow()", file=OUT)
        invoke(c, a["addr"], "V8_RemoveOneTableRow")
        r4 = walk(c, t2["Table_Big"], limit=1000)
        print("    row_count=%d rows=%d (expected 76)"
              % (r4["row_count"], len(r4["rows"])), file=OUT)
        ok4 = r4["row_count"] == len(r4["rows"]) == 76
        if not ok4:
            fails.append("4: after RemoveOneTableRow expected 76, got %d/%d"
                         % (r4["row_count"], len(r4["rows"])))

    print("\n" + "=" * 72, file=OUT)
    print("1 truncated case reports the true N and caps the rows : %s" % ("PASS" if ok1 else "FAIL"), file=OUT)
    print("2 NEGATIVE CONTROL: an 8-row table is not truncated   : %s" % ("PASS" if ok2 else "FAIL"), file=OUT)
    print("3 N follows the data (100 -> 77)                      : %s" % ("PASS" if ok3 else "FAIL"), file=OUT)
    print("4 N is exact (77 -> 76)                               : %s" % ("PASS" if ok4 else "FAIL"), file=OUT)
    print("\nV8 DLL half: %s" % ("PASS" if not fails else "FAIL"), file=OUT)
    for f in fails:
        print("   - %s" % f, file=OUT)
    print("\nSTILL OWED, and it is one look: the row asks whether the three UI strings"
          "\nare actually PAINTED (not truncated, not covered). That is a pixel question"
          "\nthe ViewModel tests cannot answer -- see [PARAMSSORT-2026-08-22], where a"
          "\ncorrect VM string sat in a TextBlock with no TextWrapping and clipped itself.",
          file=OUT)
    OUT.flush()
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
