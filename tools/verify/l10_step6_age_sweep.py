r"""L10 step 6, second clause (`AF11`) — `TeleportCoords\` must NEVER be age-swept.

    py tools/verify/l10_step6_age_sweep.py

THE RULE (CLAUDE.md's app-data layout, and `AF11`'s chosen side). Retention is the STORE's call,
not the folder's: `Snapshots\` passes `Constants.DataMaxAgeDays` (**21**), `Bookmarks\` and
`TeleportCoords\` pass **0** — sweep off, deliberately, because a hand-authored coordinate library
is not a regenerable multi-GB capture. `AF11`'s fix routed `CoordinateLibraryStore` through
`AppDataFolderMaintenance.Prepare(..., maxAgeDays: 0, ...)` from its constructor.

⚠ WHY A POSITIVE CONTROL IS THE WHOLE TEST. "The old file survived" is equally well explained by
"the sweep never ran at all" — which would hide a broken sweep everywhere else. So a synthetic
`Snapshots\` group of the SAME age is planted at the same time. The result is only meaningful if
the two disagree:

    TeleportCoords\ file, 30 days old   -> must SURVIVE   (maxAgeDays 0)
    Snapshots\      group, 30 days old  -> must be DELETED (maxAgeDays 21)   <- proves it ran

A third assertion guards the blast radius: every REAL file already in both folders must be
byte-identical afterwards (SHA-256, not eyeballed) and keep its mtime.

⚠ `LastWriteTimeUtc` is what the sweep reads, and it is what the store STAMPS on use — never
last-access, whose NTFS auto-updates are on by default. `os.utime` sets both here, which is fine:
the point is to make the file look old to the predicate that is actually consulted.

Everything planted is named `zztest` / `ZZTEST` so nothing real can be confused for it, and the rig
removes its own leftovers on every exit path.
"""
import hashlib
import os
import pathlib
import subprocess
import sys
import time

ROOT = pathlib.Path.home() / "AppData/Local/UE5CEDumper"
TC, SNAP = ROOT / "TeleportCoords", ROOT / "Snapshots"
UI = pathlib.Path(__file__).resolve().parent.parent.parent / "dist" / "UE5DumpUI.exe"
OLD_DAYS = 30
PLANTED_TC = TC / "teleport-coords.zztest.json"
PLANTED_SNAP = [SNAP / ("snapshots.ZZTEST0000000000.db" + s) for s in ("", "-wal", "-shm")]


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + "\n")


def snapshot(d):
    out = {}
    for f in sorted(d.glob("*")):
        if f.is_file():
            out[f.name] = (hashlib.sha256(f.read_bytes()).hexdigest()[:16],
                           int(f.stat().st_mtime))
    return out


def age(p, days):
    t = time.time() - days * 86400
    os.utime(p, (t, t))


def cleanup():
    for p in [PLANTED_TC] + PLANTED_SNAP:
        try:
            p.unlink()
        except OSError:
            pass


def main():
    fails = []
    if not UI.exists():
        say("FAIL: %s not found" % UI)
        return 1
    for d in (TC, SNAP):
        d.mkdir(parents=True, exist_ok=True)

    before_tc, before_snap = snapshot(TC), snapshot(SNAP)
    say("real files before:  TeleportCoords %d   Snapshots %d" % (len(before_tc), len(before_snap)))

    cleanup()
    PLANTED_TC.write_text('{"zztest": true, "coords": []}', encoding="utf-8")
    age(PLANTED_TC, OLD_DAYS)
    for p in PLANTED_SNAP:
        p.write_bytes(b"ZZTEST" + b"\x00" * 64)
        age(p, OLD_DAYS)
    say("planted, both aged %d days (limit is DataMaxAgeDays = 21):" % OLD_DAYS)
    say("   %s" % PLANTED_TC.name)
    for p in PLANTED_SNAP:
        say("   %s" % p.name)

    # ⚠ NOT a byte offset into the existing init-0.log. Every process start ROTATES that
    # file, so the launch below creates a fresh one and a pre-recorded offset (tens of KB)
    # slices past the whole run -- the first version of this rig printed an empty
    # maintenance section while the log plainly held the delete line. Record a wall-clock
    # instant instead and filter the UI's own folder by timestamp; seconds resolution is
    # ample here because the events are ~18 s apart.
    logdir = ROOT / "Logs"
    launch_mark = time.strftime("%Y-%m-%d %H:%M:%S")

    say("")
    say("launching the UI (nothing is clicked; the sweep runs from the store constructor)")
    proc = None
    try:
        proc = subprocess.Popen([str(UI)], cwd=str(UI.parent))
        time.sleep(18)
    finally:
        if proc:
            proc.terminate()
            try:
                proc.wait(timeout=15)
            except Exception:
                proc.kill()
        say("UI closed")
    time.sleep(1.5)

    tc_alive = PLANTED_TC.exists()
    snap_alive = [p.name for p in PLANTED_SNAP if p.exists()]
    say("")
    say("== the two outcomes must DISAGREE, or nothing is proven ==")
    say("   TeleportCoords\\%s  survived: %s   <-- must be True (sweep OFF)"
        % (PLANTED_TC.name, tc_alive))
    say("   Snapshots  group still present: %d/%d %s   <-- must be 0 (swept at 21 days)"
        % (len(snap_alive), len(PLANTED_SNAP), snap_alive))

    if len(snap_alive) == len(PLANTED_SNAP):
        fails.append("POSITIVE CONTROL FAILED: the 30-day-old Snapshots group was NOT swept, so "
                     "'TeleportCoords survived' is equally explained by the sweep never running "
                     "-- this run proves nothing either way")
    elif snap_alive:
        fails.append("the Snapshots group was only PARTIALLY swept (%s left) -- CLAUDE.md's "
                     "group-expiry invariant says a game's files go as a GROUP" % snap_alive)
    if not tc_alive:
        fails.append("AF11: the 30-day-old TeleportCoords file was DELETED -- the coordinate "
                     "library is being age-swept, which is the decision AF11 explicitly rejected")

    after_tc, after_snap = snapshot(TC), snapshot(SNAP)
    lost = [n for n in before_tc if n not in after_tc] + [n for n in before_snap if n not in after_snap]
    changed = ([n for n in before_tc if n in after_tc and after_tc[n][0] != before_tc[n][0]]
               + [n for n in before_snap if n in after_snap and after_snap[n][0] != before_snap[n][0]])
    say("")
    say("   pre-existing REAL files lost: %d %s" % (len(lost), lost or ""))
    say("   pre-existing REAL files changed (sha256): %d %s" % (len(changed), changed or ""))
    if lost:
        fails.append("the run destroyed %d pre-existing file(s): %s" % (len(lost), lost))
    if changed:
        fails.append("the run modified %d pre-existing file(s): %s" % (len(changed), changed))

    say("")
    say("== the maintenance lines this launch wrote ==")
    seen = 0
    for f in sorted(logdir.glob("UE5DumpUI/init-0.log")):
        try:
            txt = f.read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue
        for l in txt.splitlines():
            if "AppDataFolderMaintenance" in l and l.startswith("[") and l[1:20] >= launch_mark:
                say("   %s" % l.strip()[:170])
                seen += 1
    if not seen:
        fails.append("no AppDataFolderMaintenance line from this launch -- the deletions above "
                     "are unexplained, and an unlogged sweep is its own problem")

    cleanup()
    say("")
    say("planted files removed")
    for x in fails:
        say("FAIL: %s" % x)
    if not fails:
        say("PASS (L10 step 6, second clause / AF11 retention)")
    return 1 if fails else 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    finally:
        cleanup()
