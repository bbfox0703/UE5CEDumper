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
    py tools/check_audit_register.py --list     # + derived counts and the open
                                                #   HIGH/MED rows with their segment
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


SEVERITIES = ("HIGH", "MED", "LOW", "INFO")

# §3b's one-line register summary. Matched loosely enough to CATCH a stale one
# (any digits) but anchored on the exact wording, so a reworded sentence fails as
# "missing" rather than silently degrading this gate to a no-op — the failure mode
# extract_patterns.py --check was hardened against and the reason it works.
HEADLINE_RE = re.compile(
    r"\*\*Current register: \d+ open of \d+ · "
    + " · ".join(rf"\d+ {s}" for s in SEVERITIES)
    + r"\.\*\*"
)


def main() -> int:
    audit = AUDIT.read_text(encoding="utf-8")
    devlog = DEVLOG.read_text(encoding="utf-8")

    # One pass collects the same (fid, tick, sev) tuples findall produced, plus the
    # §2 segment heading each row sits under — display data for --list only. Every
    # gate decision below reads just the tuples, so the gate's verdict is unchanged.
    heads = [(h.start(), re.sub(r"\s+[—-]\s+✅.*$", "", h.group(1)).strip())
             for h in re.finditer(r"^#{2,3} (.+)$", audit, re.M)]
    rows = []
    seg_of = {}
    for m in ROW_RE.finditer(audit):
        rows.append((m.group(1), m.group(2), m.group(3)))
        seg = ""
        for pos, title in heads:
            if pos > m.start():
                break
            seg = title
        seg_of[m.group(1)] = seg
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

    # Severity split of the OPEN rows, for the §3b headline sentence below.
    open_by_sev = {k: 0 for k in SEVERITIES}
    for _, tick, sev in rows:
        if not tick:
            open_by_sev[sev] += 1

    if "--list" in sys.argv:
        print(f"register: {open_n} open / {total} total ({len(marked)} marked ✅)")
        print(f"dev-log claims {len(claimed)} finding(s) fixed; {len(claimed & marked)} are marked")
        print(f"open by severity: " +
              " · ".join(f"{open_by_sev[k]} {k}" for k in SEVERITIES))
        # The vetted, actionable tier only. LOW/INFO stay unlisted on purpose: §3c
        # warns they were never vetted to this audit's standard (re-derive before
        # fixing), and a flat listing would present 160+ unvetted leads as if they
        # were confirmed bugs.
        actionable = [(fid, sev) for fid, tick, sev in rows
                      if not tick and sev in ("HIGH", "MED")]
        actionable.sort(key=lambda t: t[1] != "HIGH")  # HIGH first; file order within a tier
        if actionable:
            print()
            print(f"open HIGH/MED ({len(actionable)}), with the §2 segment each sits under:")
            for fid, sev in actionable:
                print(f"  {sev:<4} {fid:<5} {seg_of.get(fid) or '?'}")

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

    # ── §3b's headline sentence must agree with the rows ───────────────────
    # Added 2026-08-17 because it drifted for the third time. The two counts it
    # states are DERIVED from the rows this gate already parses, so a stale one is
    # a lie the gate was standing right next to and not reading. Same model as
    # check_derived_counts.py: on mismatch, print the exact sentence to paste,
    # because the doc that paraphrased a generator's output is the one that went
    # stale while the two that pasted it stayed byte-exact.
    want = (f"**Current register: {open_n} open of {total} · "
            + " · ".join(f"{open_by_sev[k]} {k}" for k in SEVERITIES) + ".**")
    m = HEADLINE_RE.search(audit)
    if not m:
        print("FAIL: §3b's `**Current register: ... **` sentence is missing or its shape "
              "changed, so this gate is blind to it. Restore it as:")
        print(f"  {want}")
        return 1
    if m.group(0) != want:
        print("FAIL: §3b's register headline disagrees with the finding rows.")
        print(f"  states: {m.group(0)}")
        print(f"  actual: {want}")
        print()
        print("Paste the `actual` line over it — do not hand-adjust the numbers.")
        return 1

    print(f"CHECK OK: every finding the dev-log reports fixed is ✅ on its row "
          f"({open_n} open / {total} total), and §3b's headline agrees.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
