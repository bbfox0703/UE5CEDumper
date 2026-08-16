#!/usr/bin/env python3
"""Audit-register consistency gate.

WHY THIS EXISTS
    docs/audit-2026-08-13-early-code-findings.md tracks ~277 findings as table rows,
    and its open/closed count is DERIVED from a per-row ✅ marker. A fix session
    marks the queue's grouped row (① ② ③ …) done but has twice now forgotten to mark
    the INDIVIDUAL finding rows the group covered — so the register kept reporting
    findings as open that had shipped weeks earlier. The doc records the first
    occurrence itself ("eleven findings fixed in §2's prose blocks had never been
    ✅-marked on their table rows, so the register counted them open"); the second
    was 2026-08-17 (AB3, AB5, A6, AA4-AA7, AA25).

    Both times the marker was the ONLY thing wrong: the fix shipped, the dev-log
    recorded it, the commit message named the finding. Nothing connected those to
    the row, so the register silently drifted.

WHAT IT CHECKS
    Every finding ID that docs/dev-log.md claims is fixed must have a ✅ on its
    table row in the audit doc. That is the whole rule — it is deliberately
    one-directional: a ✅ with no dev-log entry is not an error (findings can be
    closed as refuted, or fixed before the dev-log convention), but a dev-log
    "shipped/fixed" claim with an unmarked row IS.

USAGE
    py tools/check_audit_register.py            # gate: exit 1 on drift
    py tools/check_audit_register.py --list     # also print the derived counts
"""
from __future__ import annotations

import io
import re
import sys
from pathlib import Path

# The console on the maintainer's machine is cp950; a bare print of the tick this
# gate is ABOUT dies with UnicodeEncodeError before it can report anything.
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ROOT = Path(__file__).resolve().parent.parent
AUDIT = ROOT / "docs" / "audit-2026-08-13-early-code-findings.md"
DEVLOG = ROOT / "docs" / "dev-log.md"

# A finding row: | **AB3** ✅ | MED | ... — the optional dagger markers (†‡¤) and a
# bold severity cell are both real in this file and both must be tolerated. Keep this
# in step with the derive command quoted at the top of the audit doc's §3c.
ROW_RE = re.compile(
    r"^\| \*\*([A-Z]{1,2}\d+)\*\*( ✅)?(?: †| ‡| ¤)? \| (?:\*\*)?(HIGH|MED|LOW|INFO)(?:\*\*)? \|",
    re.M,
)

# Where the dev-log asserts a finding shipped. TWO sources, because the file uses
# two conventions and a gate that reads only one is a gate that reports OK while
# seeing nothing:
#
#   1. The entry HEADING, which by convention names what the entry fixed:
#        "## 2026-08-17 - AA4-AA7: ue5_dissect.lua stops reporting failure as success"
#        "## 2026-08-17 - AB3+AB5: the vector scan learns UE5's LWC width"
#      Only the part BEFORE the first colon is read, so a finding merely discussed in
#      the title's prose is not swept in. Ranges (AA4-AA7) are expanded; joined lists
#      (AB3+AB5, AD1/AD2, "A1, A2") are split.
#   2. A bolded **ID** in the body within a short distance of an explicit
#      fixed/shipped word. Deliberately narrow: the dev-log also records findings it
#      REFUTED, and marking one of those as done would be a different kind of lie.
HEADING_RE = re.compile(r"^## [^\n]*?\s[-–]\s([^:\n]+):", re.M)
ID_RE = re.compile(r"\b([A-Z]{1,2})(\d+)\b")
RANGE_RE = re.compile(r"\b([A-Z]{1,2})(\d+)\s*[-–]\s*([A-Z]{1,2})?(\d+)\b")

BODY_CLAIM_RE = re.compile(
    r"(?:"
    r"\*\*([A-Z]{1,2}\d+)\*\*[^\n]{0,80}?(?:✅|SHIPPED|shipped|FIXED|fixed)"
    r"|(?:✅|SHIPPED|FIXED)[^\n]{0,80}?\*\*([A-Z]{1,2}\d+)\*\*"
    r")"
)


def ids_from_heading(text: str) -> set[str]:
    """IDs named in a dev-log entry's title, expanding AA4-AA7 style ranges."""
    out: set[str] = set()
    for pre, lo, post, hi in RANGE_RE.findall(text):
        # A range only makes sense within one prefix: "AA4-AA7", not "A1-B9".
        if post and post != pre:
            continue
        lo_i, hi_i = int(lo), int(hi)
        if lo_i <= hi_i and hi_i - lo_i < 40:
            out.update(f"{pre}{n}" for n in range(lo_i, hi_i + 1))
    out.update(f"{p}{n}" for p, n in ID_RE.findall(text))
    return out


def main() -> int:
    audit = AUDIT.read_text(encoding="utf-8")
    devlog = DEVLOG.read_text(encoding="utf-8")

    rows = ROW_RE.findall(audit)
    if not rows:
        print("FAIL: no finding rows matched — the row format changed and this gate is blind.")
        return 1

    marked = {fid for fid, tick, _ in rows if tick}
    known = {fid for fid, _, _ in rows}

    claimed: set[str] = set()
    for title in HEADING_RE.findall(devlog):
        claimed |= ids_from_heading(title) & known
    for a, b in BODY_CLAIM_RE.findall(devlog):
        fid = a or b
        if fid in known:
            claimed.add(fid)

    missing = sorted(claimed - marked, key=lambda s: (len(s), s))

    total, open_n = len(rows), len(rows) - len(marked)
    if "--list" in sys.argv:
        print(f"register: {open_n} open / {total} total ({len(marked)} marked ✅)")
        print(f"dev-log claims {len(claimed)} finding(s) fixed; {len(claimed & marked)} are marked")

    if missing:
        print(f"FAIL: {len(missing)} finding(s) the dev-log says are fixed have NO ✅ on their row:")
        for fid in missing:
            print(f"  {fid}")
        print()
        print("Mark the row as `| **<ID>** ✅ | <SEV> |` in "
              "docs/audit-2026-08-13-early-code-findings.md, then re-derive the count in §3c.")
        print("Marking the grouped queue row (① ② ③) is NOT enough — the count reads the "
              "individual rows.")
        return 1

    print(f"CHECK OK: every finding the dev-log reports fixed is ✅ on its row "
          f"({open_n} open / {total} total).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
