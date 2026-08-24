r"""The 21-day age-based retention sweep, tested by MANUFACTURING backdated files.

    py tools/verify/retention_backdate.py plant     # create the fixtures
    py tools/verify/retention_backdate.py check     # judge what survived
    py tools/verify/retention_backdate.py clean     # remove every fixture

    Between plant and check, TRIGGER the sweep:  launch a game and inject the DLL.
    Sein runs both sweeps during logging init (Sein.cpp:628-629).

WHY THIS EXISTS
  Retention here is BY AGE, not by generation count, so the only way to exercise it is
  to have files that are genuinely old. Waiting three weeks is not a test strategy, and
  a real 21-day-old log is evidence you cannot manufacture on demand -- but a backdated
  timestamp is exactly what the sweep reads, so it is not a simulation of the input, it
  IS the input.

WHAT IS UNDER TEST (read from the source, not assumed)
  Sein.cpp:628  PruneAgedLogs(s_processDir)
      per-FILE, and only in the folder THIS process owns. Removes a regular file whose
      extension is ".log" when (now - mtime) > 21 days. Note it does NOT exempt the
      "-0.log" slot name (Sein.cpp:286-292).
  Sein.cpp:629  PruneStaleProcessFolders(s_logDir, s_processDir)
      per-FOLDER, over every OTHER per-process folder. The age of a folder is the
      NEWEST file inside it -- deliberately not the folder's own mtime, which Windows
      does not reliably bump (Sein.cpp:441-444). `keep` (this process's own folder) is
      never removed. An EMPTY folder is deleted immediately (Sein.cpp:484).

THE CASES, AND WHAT EACH ONE RULES OUT
  Every case is a folder or file under a ZZRET- prefix, so nothing real is touched.

  folder  doomed        newest file at -25d      must DIE
  folder  survivor      newest file at -19d      must LIVE   <- rules out "it deleted everything"
  folder  edge-old      newest file at -21d-6h   must DIE    <- pins the threshold from below
  folder  edge-new      newest file at -21d+6h   must LIVE   <- pins it from above
  folder  mixed         one file -25d, one -1d   must LIVE   <- proves NEWEST-inside, not any/oldest
  folder  empty         no files at all          must DIE    <- the sawFile branch
  file    old .log      -25d, in the game's own  must DIE    <- the per-file sweep
  file    new .log      -19d, same folder        must LIVE
  file    old .txt      -25d, same folder        must LIVE   <- the extension guard
  file    bookmarks     -400d, in Bookmarks\     must LIVE   <- retention is deliberately OFF there

  THE PAIR IS THE PROOF THAT THE SWEEP RAN. If it never executed, `doomed` survives. If
  something unrelated wiped the folder, `survivor` dies too. Only a working age sweep
  produces exactly one of each, and the two edge cases put the boundary where the
  constant says it is (Grimoire.h:21, LOG_RETENTION_DAYS = 21) rather than merely
  "somewhere between 19 and 25 days".

SAFETY
  Every path this script writes or deletes begins with ZZRET-. `clean` refuses anything
  else. Nothing real is renamed, moved, or backdated. ⚠ The one shared resource is the
  GAME'S OWN log folder, which the per-file cases must live in -- the script only ADDS
  ZZRET-prefixed files there and never removes or rewrites an existing one.
"""
import json
import os
import pathlib
import sys
import time

ROOT = pathlib.Path(os.environ["LOCALAPPDATA"]) / "UE5CEDumper"
LOGS = ROOT / "Logs"
BOOKMARKS = ROOT / "Bookmarks"
STATE = pathlib.Path(__file__).resolve().parents[2] / "out" / "retention-plan.json"
PREFIX = "ZZRET-"
DAY = 86400.0

# The folder the DLL will own while the sweep runs. Its per-file sweep only touches
# THIS folder, so the file-level cases have to live here.
OWN = "DumperTest"

FOLDER_CASES = [
    ("doomed",   -25.0,        True),
    ("survivor", -19.0,        False),
    ("edge-old", -21.0 - 0.25, True),
    ("edge-new", -21.0 + 0.25, False),
]


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + "\n")
    sys.stdout.flush()


def backdate(p: pathlib.Path, days: float):
    t = time.time() + days * DAY
    os.utime(p, (t, t))
    return t


def write(p: pathlib.Path, text="[fake] retention fixture\n"):
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(text, encoding="utf-8")
    return p


def plant():
    if not LOGS.is_dir():
        raise SystemExit("retention: %s does not exist -- run the UI or a game once first" % LOGS)
    plan = {"folders": [], "files": [], "planted_at": time.time()}

    for name, days, must_die in FOLDER_CASES:
        d = LOGS / (PREFIX + name)
        f = write(d / "init-20260101-000000.log")
        backdate(f, days)
        plan["folders"].append({"path": str(d), "must_die": must_die,
                                "why": "newest file at %+.2fd" % days})

    # mixed: an OLD file and a FRESH one. Must LIVE, because the folder's age is the
    # NEWEST file. A rig that keyed on the oldest, or on "any file older than", kills it.
    d = LOGS / (PREFIX + "mixed")
    backdate(write(d / "init-20260101-000000.log"), -25.0)
    backdate(write(d / "scan-20260820-000000.log"), -1.0)
    plan["folders"].append({"path": str(d), "must_die": False,
                            "why": "oldest -25d but NEWEST -1d; age is the newest file"})

    # empty: no files at all -> deleted immediately by the !sawFile branch.
    d = LOGS / (PREFIX + "empty")
    d.mkdir(parents=True, exist_ok=True)
    plan["folders"].append({"path": str(d), "must_die": True, "why": "no files inside"})

    # per-FILE cases, inside the folder the DLL will own.
    own = LOGS / OWN
    if own.is_dir():
        for fname, days, must_die, why in [
            (PREFIX + "old.log", -25.0, True,  "a .log older than retention"),
            (PREFIX + "new.log", -19.0, False, "a .log inside retention"),
            (PREFIX + "old.txt", -25.0, False, "NOT a .log -- the extension guard"),
        ]:
            f = write(own / fname)
            backdate(f, days)
            plan["files"].append({"path": str(f), "must_die": must_die, "why": why})
    else:
        say("  note: %s does not exist yet, so the per-FILE cases were skipped" % own)

    # Bookmarks retention is deliberately OFF (0). A 400-day-old file must survive.
    if BOOKMARKS.is_dir():
        f = write(BOOKMARKS / (PREFIX + "ancient.json"), "{}\n")
        backdate(f, -400.0)
        plan["files"].append({"path": str(f), "must_die": False,
                              "why": "Bookmarks retention is 0 = OFF, deliberately"})
    else:
        say("  note: %s does not exist, Bookmarks control skipped" % BOOKMARKS)

    STATE.parent.mkdir(parents=True, exist_ok=True)
    STATE.write_text(json.dumps(plan, indent=1), encoding="utf-8")
    say("planted %d folder case(s) and %d file case(s)"
        % (len(plan["folders"]), len(plan["files"])))
    for x in plan["folders"] + plan["files"]:
        say("   %-5s %-52s %s" % ("DIE" if x["must_die"] else "LIVE",
                                  pathlib.Path(x["path"]).name, x["why"]))
    say("")
    say("NOW TRIGGER THE SWEEP: launch a game and inject the DLL, then run `check`.")
    return 0


def check():
    if not STATE.is_file():
        raise SystemExit("retention: no plan at %s -- run `plant` first" % STATE)
    plan = json.loads(STATE.read_text(encoding="utf-8"))
    fails, rows = [], []
    for x in plan["folders"] + plan["files"]:
        p = pathlib.Path(x["path"])
        gone = not p.exists()
        ok = gone == x["must_die"]
        rows.append((ok, "DIE" if x["must_die"] else "LIVE",
                     "gone" if gone else "present", p.name, x["why"]))
        if not ok:
            fails.append("%s: expected %s, it is %s  (%s)"
                         % (p.name, "gone" if x["must_die"] else "present",
                            "gone" if gone else "present", x["why"]))
    say("%-4s %-5s %-9s %-30s %s" % ("", "want", "actual", "name", "why"))
    for ok, want, actual, name, why in rows:
        say("%-4s %-5s %-9s %-30s %s" % ("OK" if ok else "FAIL", want, actual, name, why))

    died = sum(1 for _, w, a, _, _ in rows if w == "DIE" and a == "gone")
    lived = sum(1 for _, w, a, _, _ in rows if w == "LIVE" and a == "present")
    say("")
    say("%d of the must-die cases died, %d of the must-live cases lived" % (died, lived))
    if died == 0:
        fails.append("NOTHING died -- the sweep did not run at all, so every 'LIVE' "
                     "result above is vacuous")
    say("")
    if fails:
        say("FAIL (%d)" % len(fails))
        for f in fails:
            say("  - %s" % f)
        return 1
    say("PASS -- the age sweep deleted exactly what is past 21 days and kept the rest, "
        "the boundary sits where LOG_RETENTION_DAYS says, and Bookmarks was untouched")
    return 0


def clean():
    n = 0
    for base in (LOGS, BOOKMARKS):
        if not base.is_dir():
            continue
        for p in list(base.iterdir()):
            if not p.name.startswith(PREFIX):
                continue          # never touch anything that is not ours
            if p.is_dir():
                for sub in p.rglob("*"):
                    if sub.is_file():
                        sub.unlink()
                p.rmdir()
            else:
                p.unlink()
            n += 1
    own = LOGS / OWN
    if own.is_dir():
        for p in list(own.iterdir()):
            if p.name.startswith(PREFIX) and p.is_file():
                p.unlink()
                n += 1
    if STATE.is_file():
        STATE.unlink()
    say("removed %d fixture(s); nothing without the %s prefix was touched" % (n, PREFIX))
    return 0


def b19():
    r"""B19: an UNDELETABLE file must cost that file, not the rest of the sweep.

    The defect (build 2603): PruneAgedLogs shared ONE std::error_code between the
    directory iteration and the per-file fs::remove, so a failed remove set `ec`, the
    loop's `if (ec) break` ended the sweep -- and because NTFS enumeration order is
    stable, it ended it at the same file on every launch.

    ⚠ ORDER IS THE WHOLE TEST. The held file must be enumerated BEFORE the deletable
    one, or the old code would have deleted the deletable one first and the run would
    pass on a broken build. Hence the aaa-/zzz- names, and hence this is a separate verb:
    it has to hold a handle open ACROSS the sweep, which `plant` + `check` as two
    processes cannot do.

    Python's open() on Windows does not pass FILE_SHARE_DELETE, so the handle really does
    make the file undeletable -- asserted below rather than assumed.
    """
    import subprocess
    own = LOGS / OWN
    if not own.is_dir():
        raise SystemExit("b19: %s does not exist -- launch the game once first" % own)

    held = write(own / (PREFIX + "aaa-held.log"))        # sorts FIRST
    after = write(own / (PREFIX + "zzz-after.log"))      # sorts LAST
    backdate(held, -25.0)
    backdate(after, -25.0)
    say("planted  %s  (held open, sorts first)" % held.name)
    say("planted  %s  (deletable, sorts last)" % after.name)

    fh = open(held, "rb")     # noqa: SIM115 -- must stay open across the sweep
    try:
        # Prove the handle actually blocks deletion; otherwise the whole arm is vacuous.
        try:
            os.unlink(held)
            say("  ** the held file was deletable anyway -- this arm cannot test B19 **")
            return 1
        except OSError:
            say("  OK: the open handle really does make it undeletable")

        say("")
        say("launching the game + injecting to trigger the sweep ...")
        root = pathlib.Path(__file__).resolve().parents[2]
        subprocess.run([sys.executable, str(root / "tools/verify/launch_dumpertest.py"), "dev"],
                       capture_output=True, text=True)
        time.sleep(4)
        pid = (root / "out" / "host.pid").read_text().strip()
        subprocess.run([sys.executable, str(root / "tools/verify/inject.py"), "--pid", pid],
                       capture_output=True, text=True)
        time.sleep(3)

        held_gone, after_gone = not held.exists(), not after.exists()
        say("")
        say("  held  file (%s): %s   <- must REMAIN (it is locked)"
            % (held.name, "gone" if held_gone else "present"))
        say("  after file (%s): %s   <- must be GONE (the sweep must continue past the lock)"
            % (after.name, "gone" if after_gone else "present"))
        fails = []
        if held_gone:
            fails.append("the locked file was deleted -- impossible, so the fixture is wrong")
        if not after_gone:
            fails.append("*** B19: the file AFTER the undeletable one survived. The sweep "
                         "aborted at the lock, which is exactly the defect.")
        say("")
        if fails:
            for f in fails:
                say("  - %s" % f)
            return 1
        say("PASS -- the sweep skipped the undeletable file and still deleted the one "
            "enumerated after it")
        return 0
    finally:
        fh.close()
        subprocess.run(["taskkill", "/F", "/IM", "DumperTest.exe"],
                       capture_output=True, text=True)
        for p in (held, after):
            if p.exists():
                p.unlink()



# ======================================================================
# The C# half. DIFFERENT SWEEP, DIFFERENT TRIGGER.
# ======================================================================
# ⚠ THIS EXISTS BECAUSE THE FIRST RUN'S Bookmarks CASE WAS VACUOUS. It was triggered by
# launching the GAME, and the C++ sweep (Sein) never looks at Bookmarks\ at all -- so
# "the 400-day-old bookmark survived" was true of a sweep that was never pointed at it.
# The C# sweep lives in AppDataFolderMaintenance.PruneAged and runs from each STORE's
# constructor, i.e. it needs the UI. A no-sweep control is only meaningful when a
# must-die case in the SAME sweep dies alongside it.
CS_CASES = [
    # (subfolder, filename, days, must_die, why)
    ("Snapshots", "snapshots.ZZRETDOOM.db", -25.0, True,
     "a snapshot group past DataMaxAgeDays=21"),
    ("Snapshots", "snapshots.ZZRETLIVE.db", -19.0, False,
     "inside the window"),
    ("Snapshots", "snapshots.ZZRETGRP.db", -25.0, False,
     "OLD, but its group has a fresh sibling -- groups expire together"),
    ("Snapshots", "snapshots.ZZRETGRP.db-wal", -1.0, False,
     "the fresh sibling that keeps the whole group alive"),
    ("Snapshots", "snapshots-ZZRETNOTOURS.db", -400.0, False,
     "no dot after the prefix, so GameKeyOf refuses it -- never a candidate"),
    # ⚠ THE LAST-ACCESS CASE. Fresh atime, 30-day mtime: it dies only if the sweep reads
    # LastWriteTime. CLAUDE.md warns that keying on last-access would silently disable
    # retention forever -- NTFS atime updates are ON by default, so any AV/backup/indexer
    # read makes every file look like today. Neither this rig's other cases nor
    # tools/verify/l10_step6_age_sweep.py could catch that: both set atime and mtime
    # TOGETHER via os.utime(p, (t, t)), so a sweep reading the wrong stamp passes them
    # identically. l10's header calls that "fine"; it is fine for what l10 tests, but it
    # does mean the hazard CLAUDE.md names was untested until now.
    ("Snapshots", "snapshots.ZZRETATIME.db", -30.0, True,
     "mtime -30d but atime NOW -- dies only if LastWriteTime is what is read"),
    ("Bookmarks", "bookmarks.ZZRETBM.json", -400.0, False,
     "Bookmarks retention is 0 = OFF"),
    ("TeleportCoords", "teleport-coords.zzretcoord.json", -400.0, False,
     "TeleportCoords retention is 0 = OFF (AF11 step 6's negative control)"),
]


def csharp():
    import subprocess
    root = pathlib.Path(__file__).resolve().parents[2]
    t0 = time.time()
    planted = []
    for sub, name, days, must_die, why in CS_CASES:
        d = ROOT / sub
        if not d.is_dir():
            say("  note: %s does not exist -- skipping its case(s)" % d)
            continue
        f = write(d / name, "{}\n")
        backdate(f, days)
        if "ZZRETATIME" in name:
            # mtime stays OLD, atime becomes NOW -- the whole point of this case.
            os.utime(f, (time.time(), time.time() + days * DAY))
        planted.append((f, must_die, why))
    if not planted:
        raise SystemExit("csharp: no target folder exists; open the UI once first")
    say("planted %d case(s)" % len(planted))

    say("")
    say("launching the UI to run the store constructors ...")
    subprocess.Popen([str(root / "dist" / "UE5DumpUI.exe")],
                     stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    time.sleep(12)
    subprocess.run(["taskkill", "/F", "/IM", "UE5DumpUI.exe"],
                   capture_output=True, text=True)
    time.sleep(1)

    fails = []
    for f, must_die, why in planted:
        gone = not f.exists()
        ok = gone == must_die
        say("%-4s %-5s %-9s %-38s %s" % ("OK" if ok else "FAIL",
                                         "DIE" if must_die else "LIVE",
                                         "gone" if gone else "present", f.name, why))
        if not ok:
            fails.append("%s: expected %s, got %s (%s)"
                         % (f.name, "gone" if must_die else "present",
                            "gone" if gone else "present", why))
    if not any(m for _, m, _ in planted if m) or \
       not any((not f.exists()) for f, m, _ in planted if m):
        fails.append("NOTHING died -- the C# sweep did not run (did a store get "
                     "constructed?), so every LIVE result here is vacuous")

    # Independent witness: PruneAged's own line (AppDataFolderMaintenance.cs:192).
    # ⚠ MUST be scoped to THIS run. The first version grepped every log and proudly
    # printed lines from four days earlier, which is a witness that can never fail --
    # exactly the shape this whole rig exists to avoid. Only lines written since the
    # fixtures were planted count.
    since = time.strftime("%Y-%m-%d %H:%M:%S", time.localtime(t0))
    hits = []
    for lg in LOGS.rglob("*.log"):
        try:
            txt = lg.read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue
        for ln in txt.splitlines():
            if "AppDataFolderMaintenance: deleted" in ln and ln[1:20] >= since:
                hits.append(ln.strip()[:130])
    say("")
    if hits:
        for h in hits:
            say("witness (this run): %s" % h)
        # The COUNT is the sharp part, and it is DERIVED, never hardcoded. It was a
        # literal "deleted 1" until the last-access case became the second doomed group,
        # and the check duly went red on a correct sweep -- a stale expectation is the
        # same defect class this rig hunts, so it now counts the doomed Snapshots
        # fixtures instead.
        want = sum(1 for f, m, _ in planted
                   if m and f.parent.name == "Snapshots")
        if not any(("deleted %d " % want) in h for h in hits):
            fails.append("the sweep logged a deletion count other than %d, but exactly %d "
                         "Snapshots fixture(s) were doomed -- something else was swept too"
                         % (want, want))
    else:
        say("witness (this run): NONE -- no AppDataFolderMaintenance line since %s" % since)
        fails.append("the sweep left no log line for this run, so the only evidence is "
                     "the files themselves")

    for f, _, _ in planted:
        if f.exists():
            f.unlink()
    say("")
    if fails:
        say("FAIL (%d)" % len(fails))
        for x in fails:
            say("  - %s" % x)
        return 1
    say("PASS -- the C# store sweep expired the aged snapshot group, kept the one inside "
        "the window, kept a group held alive by a fresh sibling, ignored a non-matching "
        "name, and left both zero-retention folders alone")
    return 0


if __name__ == "__main__":
    v = sys.argv[1] if len(sys.argv) > 1 else "check"
    raise SystemExit({"plant": plant, "check": check, "clean": clean,
                      "b19": b19, "csharp": csharp}
                     .get(v, lambda: (_ for _ in ()).throw(SystemExit(__doc__)))())
