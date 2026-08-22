r"""Can this snapshot corpus even TRIGGER the per-slot cap notice? Answer before verifying it.

    py tools/verify/snapshot_cap_fixture.py                  # every corpus under Snapshots\
    py tools/verify/snapshot_cap_fixture.py <path-to.db>     # one corpus
    py tools/verify/snapshot_cap_fixture.py --cap 1024

WHY THIS EXISTS. The checklist step reads "run a Group match on a common enough value so that some
slot matches more than 256 fields on some object, and the status line must gain the notice". If the
corpus holds no such object, the notice legitimately never appears and the run looks like a PASS —
you clicked, you saw no error, you moved on. **An absence proves nothing until the channel is shown
able to carry the thing.** This tool shows it, or says plainly that this corpus cannot.

WHAT A SLOT IS HERE. A Group-match slot binds within ONE object, so the condition is not "257 fields
in the corpus hold this value" but "257 fields ON A SINGLE OBJECT hold it". Grouping by
`(snapshot_id, obj_addr, numeric_value)` is exactly that shape.

⭐ THE SECOND HALF IS THE ONE PEOPLE SKIP. Step 4 raises Value Search's per-slot cap to 1024 and
re-runs the SNAPSHOT query, which must still show 256 — the snapshot port's cap is fixed and does
not follow the live setting. That reads like a "nothing changed" assertion, which proves nothing on
its own. It becomes a real two-state discriminator only when the corpus has a group **between the
two caps**: over 256 and under 1024. Then

    correct  -> still capped at 256, notice still shown
    broken   -> follows the live cap, all N shown, notice GONE

are visibly different outcomes. This tool reports whether your corpus lands in that window, because
outside it step 4 is vacuous no matter how carefully you click.

MEASURED 2026-08-21 on this machine's only non-empty corpus (`snapshots.6A7EA60310F17000.db`,
DumperTest, 2 snapshots x 644 objects x 12,155 fields): exactly TWO qualifying groups, both
`TraceQueryTestResults` (gobjects index 22738, outer `Package`) holding **264 fields at 0.0** out of
288 — the same object in both captures. Margin over the cap is 8 fields, and nothing anywhere
exceeds 1024, so the corpus sits in the discriminating window.
"""
import argparse
import glob
import os
import pathlib
import sqlite3
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

LIVE_DEFAULT_CAP = 256
RAISED_CAP = 1024


def say(s=""):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + chr(10))
    sys.stdout.flush()


def corpora():
    root = pathlib.Path(os.environ.get("LOCALAPPDATA", "")) / "UE5CEDumper" / "Snapshots"
    return sorted(glob.glob(str(root / "snapshots.*.db")))


def probe(path, cap, raised):
    """-> (verdict, lines). verdict: 'window' | 'over-both' | 'none' | 'empty'."""
    out = []
    # read-only: this is the maintainer's real corpus, and a probe must not be able to damage it
    uri = "file:%s?mode=ro" % str(path).replace("\\", "/")
    con = sqlite3.connect(uri, uri=True)
    try:
        snaps = con.execute("SELECT COUNT(*) FROM snapshots").fetchone()[0]
        fields = con.execute("SELECT COUNT(*) FROM fields").fetchone()[0]
        out.append("  %d snapshot(s), %d field row(s)" % (snaps, fields))
        if not fields:
            return "empty", out

        # a slot binds within one object -> group by (snapshot, object, value)
        rows = con.execute(
            "SELECT snapshot_id, obj_addr, numeric_value, COUNT(*) n FROM fields"
            " WHERE numeric_value IS NOT NULL"
            " GROUP BY snapshot_id, obj_addr, numeric_value"
            " HAVING n > ? ORDER BY n DESC", (cap,)).fetchall()
        if not rows:
            top = con.execute(
                "SELECT COUNT(*) n FROM fields WHERE numeric_value IS NOT NULL"
                " GROUP BY snapshot_id, obj_addr, numeric_value"
                " ORDER BY n DESC LIMIT 1").fetchone()
            out.append("  NO group exceeds %d — the largest is %s."
                       % (cap, top[0] if top else 0))
            out.append("  ⛔ The notice CANNOT fire here. A run against this corpus that sees no")
            out.append("     notice has measured nothing.")
            return "none", out

        out.append("  %d group(s) exceed %d:" % (len(rows), cap))
        for sid, obj, val, n in rows[:8]:
            cls = con.execute(
                "SELECT class_fqn, gobjects_index FROM fields"
                " WHERE snapshot_id=? AND obj_addr=? LIMIT 1", (sid, obj)).fetchone() or ("?", "?")
            out.append("     snap=%-3s %-16s value=%-10s fields=%-5s  %s (gidx %s)"
                       % (sid, obj, val, n, cls[0], cls[1]))
        if len(rows) > 8:
            out.append("     ... and %d more" % (len(rows) - 8))

        biggest = rows[0][3]
        if biggest > raised:
            out.append("")
            out.append("  ⚠ The largest group (%d) also exceeds the RAISED cap %d." % (biggest, raised))
            out.append("     Step 3 is runnable, but step 4 is NOT discriminating on this group:")
            out.append("     a broken build that followed the live cap would still truncate and")
            out.append("     still show the notice, so both outcomes look identical.")
            mid = [r for r in rows if cap < r[3] <= raised]
            if mid:
                out.append("     ✅ but %d group(s) DO sit in the window — use one of those:" % len(mid))
                out.append("        snap=%s %s value=%s fields=%s" % (mid[0][0], mid[0][1], mid[0][2], mid[0][3]))
                return "window", out
            return "over-both", out

        out.append("")
        out.append("  ✅ Largest group is %d — above %d and below %d, i.e. INSIDE the window."
                   % (biggest, cap, raised))
        out.append("     Step 4 discriminates here: correct -> still 256 + notice;")
        out.append("     broken -> all %d shown and the notice disappears." % biggest)
        return "window", out
    finally:
        con.close()


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("db", nargs="?", help="a snapshots.<hash>.db (default: all of them)")
    ap.add_argument("--cap", type=int, default=LIVE_DEFAULT_CAP,
                    help="the snapshot port's fixed per-slot cap (default %d)" % LIVE_DEFAULT_CAP)
    ap.add_argument("--raised", type=int, default=RAISED_CAP,
                    help="the raised Value Search cap step 4 sets (default %d)" % RAISED_CAP)
    a = ap.parse_args()

    paths = [a.db] if a.db else corpora()
    if not paths:
        say("no corpus found under %%LOCALAPPDATA%%\\UE5CEDumper\\Snapshots")
        return 2

    usable = []
    for p in paths:
        say("%s" % pathlib.Path(p).name)
        try:
            verdict, lines = probe(p, a.cap, a.raised)
        except sqlite3.Error as e:
            say("  UNREADABLE: %s" % e)
            say("")
            continue
        for l in lines:
            say(l)
        if verdict == "window":
            usable.append(p)
        say("")

    if usable:
        say("USABLE FIXTURE: %d corpus/corpora can drive BOTH step 3 and step 4." % len(usable))
        for u in usable:
            say("   %s" % u)
        return 0
    say("NO USABLE FIXTURE on this machine. Capture a snapshot of a game with a deeply nested")
    say("struct-heavy object (UE's own TraceQueryTestResults CDO is one) before running the step —")
    say("otherwise the absence of the notice is not evidence of anything.")
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
