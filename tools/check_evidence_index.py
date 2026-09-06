#!/usr/bin/env python3
"""Fail CI when `docs/evidence/` rots — an orphaned artifact, or one the index does not list.

WHY THIS EXISTS
  `docs/evidence/` holds runtime artifacts that COMMITTED CLAIMS rest on, copied out before the
  21-day log sweep deletes them (`Grimoire::LOG_RETENTION_DAYS`). It is the one place in this repo
  where a file's whole justification lives in a *different* file — which is precisely the shape
  `docs/working-lessons.md` §1.12 identifies as this project's dominant defect: the report and the
  reported thing drifting apart while everything stays green.

  Left to discipline, this directory rots in two directions and neither is visible:
    * a claim is deleted or rewritten, and its evidence silently becomes an orphan nobody dares
      remove because nobody knows what it supported;
    * evidence is added and the index is not updated, so the next person cannot tell what is here.

⭐ AGE IS NOT THE CRITERION, ORPHANHOOD IS.
  A 2026 artifact whose row is still open must be kept forever; yesterday's artifact whose claim was
  deleted is already garbage. So this check never expires anything on a timer — it asserts that every
  artifact still points at a live claim, and lets the maintainer decide what to do when one does not.
  The date in the path is for human navigation and growth review, not for expiry.

WHAT IS CHECKED
  1. every `docs/evidence/<YYYY-MM>/<slug>/` has a `README.md`;
  2. that README names at least one claim tag `[SOMETHING-YYYY-MM-DD]`;
  3. every claim tag it names still appears SOMEWHERE ELSE under `docs/` — the citing document. A tag
     that exists only inside the evidence README is an orphan;
  4. every directory appears in `docs/evidence/README.md`'s index table;
  5. no directory holds only a README (an artifact-less claim belongs in the doc, not here).
  Growth is reported, not gated: the maintainer sees the total and decides.

WHAT IS **NOT** CHECKED, deliberately
  Whether the artifact actually supports the claim. Nothing mechanical can do that; it is why the
  per-directory README must quote the decisive lines. This check keeps the bookkeeping honest so a
  human review is about content rather than archaeology.
"""
import os
import re
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EVID = os.path.join(ROOT, "docs", "evidence")
INDEX = os.path.join(EVID, "README.md")

MONTH_RE = re.compile(r"^\d{4}-\d{2}$")
TAG_RE = re.compile(r"\[([A-Z0-9][A-Z0-9_]*(?:-[A-Z0-9]+)*-\d{4}-\d{2}-\d{2})\]")


def docs_text_excluding(skip_paths):
    """Every .md under docs/ except the evidence READMEs themselves."""
    out = []
    for dirpath, _dirs, files in os.walk(os.path.join(ROOT, "docs")):
        for f in files:
            if not f.endswith(".md"):
                continue
            p = os.path.join(dirpath, f)
            if p in skip_paths:
                continue
            try:
                with open(p, encoding="utf-8", errors="replace") as fh:
                    out.append(fh.read())
            except OSError:
                pass
    return "\n".join(out)


def main():
    if not os.path.isdir(EVID):
        print("check_evidence_index: OK -- docs/evidence/ does not exist yet, nothing to check")
        return 0

    problems, dirs, total_bytes, artifact_count = [], [], 0, 0

    for month in sorted(os.listdir(EVID)):
        mpath = os.path.join(EVID, month)
        if not os.path.isdir(mpath):
            continue
        if not MONTH_RE.match(month):
            problems.append("docs/evidence/%s is not a YYYY-MM directory. The tree is date-first so "
                            "growth and review windows are visible; put the artifact under its month."
                            % month)
            continue
        for slug in sorted(os.listdir(mpath)):
            spath = os.path.join(mpath, slug)
            if not os.path.isdir(spath):
                problems.append("docs/evidence/%s/%s is a loose file; artifacts live in a slug "
                                "directory with a README that states the claim." % (month, slug))
                continue
            dirs.append("%s/%s" % (month, slug))
            files = [f for f in sorted(os.listdir(spath)) if os.path.isfile(os.path.join(spath, f))]
            for f in files:
                sz = os.path.getsize(os.path.join(spath, f))
                total_bytes += sz
                if f != "README.md":
                    artifact_count += 1
            if "README.md" not in files:
                problems.append("docs/evidence/%s/%s has no README.md -- an artifact with no stated "
                                "claim is an orphan on arrival." % (month, slug))
                continue
            if len(files) == 1:
                problems.append("docs/evidence/%s/%s holds only a README and no artifact. A claim "
                                "with nothing to re-examine belongs in the citing doc, not here."
                                % (month, slug))

    skip = {INDEX}
    for d in dirs:
        skip.add(os.path.join(EVID, *d.split("/"), "README.md"))
    elsewhere = docs_text_excluding(skip)

    try:
        with open(INDEX, encoding="utf-8", errors="replace") as fh:
            index_text = fh.read()
    except OSError:
        print("CHECK FAILED: docs/evidence/README.md (the index) is missing.")
        return 1

    for d in dirs:
        if d not in index_text:
            problems.append("docs/evidence/%s is not listed in the index (docs/evidence/README.md). "
                            "The index is how anyone finds out what is here." % d)
        rp = os.path.join(EVID, *d.split("/"), "README.md")
        try:
            with open(rp, encoding="utf-8", errors="replace") as fh:
                rtext = fh.read()
        except OSError:
            continue
        tags = sorted(set(TAG_RE.findall(rtext)))
        if not tags:
            problems.append("docs/evidence/%s/README.md names no claim tag like "
                            "[SOMETHING-2026-09-06]. Say which claim this supports, or delete it." % d)
            continue
        for tag in tags:
            if ("[%s]" % tag) not in elsewhere:
                problems.append("docs/evidence/%s/README.md cites [%s], which appears NOWHERE ELSE "
                                "under docs/. Either the claim was deleted (so this artifact is an "
                                "ORPHAN -- decide: re-point it or remove it) or the tag is a typo."
                                % (d, tag))

    if problems:
        print("CHECK FAILED: docs/evidence/ bookkeeping is out of step with the claims it serves.\n")
        for p in problems:
            print("  * " + p)
        print("\nSee docs/evidence/README.md for what may live there. Age is not the criterion for "
              "removal -- orphanhood is.")
        return 1

    print("check_evidence_index: OK -- %d directory(ies), %d artifact(s), %.1f KB total; every "
          "claim tag resolves to a citing doc." % (len(dirs), artifact_count, total_bytes / 1024.0))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
