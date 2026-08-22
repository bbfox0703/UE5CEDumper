r"""B19 — one locked archive must not stop the log-retention sweep for the rest of the folder.

    py tools/verify/b19_locked_log.py

THE DEFECT (Sein.cpp:268-295, in the fix's own comment). `PruneAgedLogs` shared **one**
`std::error_code` between the directory iteration and the per-file `fs::remove`. A failed remove set
`ec`, the loop's `if (ec) break` then ended the sweep — and because NTFS enumeration order is
stable, it ended it at the SAME entry on every launch. One undeletable file therefore switched the
advertised 21-day retention off for everything after it, permanently and silently. The fix declares
a fresh `error_code` inside the per-file lambda.

⭐ **SO THE TEST HAS TO BE ABOUT ORDER, NOT ABOUT THE LOCKED FILE.** "The locked file survived" is
true under the fix AND under the defect, and it is also true if the sweep never ran at all. Three
staged files whose enumeration order is known and asserted:

    b19a-…  aged, unlocked, BEFORE the lock   -> must be DELETED   (proves the sweep ran)
    b19b-…  aged, LOCKED                      -> must SURVIVE      (proves the lock bites)
    b19c-…  aged, unlocked, AFTER the lock    -> must be DELETED   ⭐ THE WITNESS

Under the old code `c` survives, because the sweep broke at `b`. Under the fix it goes. `a` is what
stops a sweep that never ran from reading as a pass, and `b` is what stops a lock that never held
from reading as one — the rig verifies the lock by trying the delete itself first.

⚠ NTFS enumeration order is asserted, not assumed: the rig lists the directory the same way
`fs::directory_iterator` does (FindNextFileW, via `os.scandir`) and refuses to run unless it really
sees a → b → c in that order.

⚠ The three files are written into the REAL log folder, because that is the only folder the DLL
sweeps. They carry a `b19`-prefixed name that nothing else produces, and the rig removes whatever
survives on the way out — including on failure.
"""
import ctypes
import os
import pathlib
import subprocess
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.stdout.reconfigure(encoding="utf-8", errors="replace")

EXE = (r"D:\UE_Analyze_data\for testing\DumperTest\Development\Windows"
       r"\DumperTest\Binaries\Win64\DumperTest.exe")
LOGDIR = pathlib.Path(os.environ["LOCALAPPDATA"]) / "UE5CEDumper" / "Logs" / "DumperTest"
RETENTION_DAYS = 21           # Grimoire::LOG_RETENTION_DAYS
AGED_DAYS = 40                # comfortably past it
NAMES = ["b19a-19700101-000001.log", "b19b-19700101-000002.log", "b19c-19700101-000003.log"]


def say(s=""):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + chr(10))
    sys.stdout.flush()


def scandir_order(dirpath, wanted):
    """The order FindNextFileW yields our three files — the same walk the DLL does."""
    seen = []
    for e in os.scandir(dirpath):
        if e.name in wanted:
            seen.append(e.name)
    return seen


def main():
    if not LOGDIR.is_dir():
        say("NOT_RUNNABLE: %s does not exist — launch and inject DumperTest once first" % LOGDIR)
        return 2

    subprocess.run(["taskkill", "/F", "/IM", "DumperTest.exe"], capture_output=True)
    time.sleep(1.5)

    paths = [LOGDIR / n for n in NAMES]
    for p in paths:
        p.write_bytes(b"B19 staged archive - safe to delete\r\n")
    old = time.time() - AGED_DAYS * 86400
    for p in paths:
        os.utime(p, (old, old))
    say("staged 3 archives in %s, mtime %d days old (retention is %d)"
        % (LOGDIR.name, AGED_DAYS, RETENTION_DAYS))

    # ── the enumeration order the DLL will see, asserted not assumed ──────
    order = scandir_order(LOGDIR, set(NAMES))
    say("enumeration order: %s" % " -> ".join(order))
    if order != NAMES:
        say("NOT_RUNNABLE: the directory does not yield a -> b -> c, so 'after the lock' is")
        say("              undefined and the witness would prove nothing.")
        for p in paths:
            p.unlink(missing_ok=True)
        return 2

    locked = paths[1]
    fh = None
    try:
        # Python's open() on Windows omits FILE_SHARE_DELETE, so this blocks the remove.
        fh = open(locked, "rb")

        # ⚠ PROVE THE LOCK BITES. A lock that does not hold turns arm (b) into a
        # tautology and arm (c) into a test of nothing.
        try:
            os.remove(locked)
            say("NOT_RUNNABLE: the 'locked' file deleted anyway — this Python build shares delete")
            return 2
        except PermissionError:
            say("lock verified: deleting %s from this process fails with PermissionError" % locked.name)

        say("")
        say("launching + injecting DumperTest (the sweep runs at logger init)")
        p = subprocess.Popen([EXE, "-windowed", "-ResX=1024", "-ResY=576",
                              "-ExecCmds=t.MaxFPS 60"])
        time.sleep(22)
        r = subprocess.run([sys.executable, str(HERE / "inject.py"), "--name", "DumperTest"],
                           capture_output=True, text=True, encoding="utf-8", errors="replace")
        say((r.stdout or r.stderr or "").strip()[-120:])
        if r.returncode != 0:
            say("FAIL: could not inject — the sweep never ran")
            return 2
        time.sleep(3)

        say("")
        alive = {n: (LOGDIR / n).exists() for n in NAMES}
        for n in NAMES:
            say("   %-28s %s" % (n, "STILL THERE" if alive[n] else "deleted"))

        fails = []
        if alive[NAMES[0]]:
            fails.append("(a) the aged unlocked file BEFORE the lock survived — the sweep did not "
                         "run at all, so nothing here was measured")
        if not alive[NAMES[1]]:
            fails.append("(b) the LOCKED file was deleted — the lock did not hold during the sweep, "
                         "so (c) proves nothing")
        if alive[NAMES[2]]:
            fails.append("(c) ⭐ the aged unlocked file AFTER the lock survived — the sweep stopped "
                         "at the undeletable entry. This is exactly B19.")

        say("")
        if fails:
            say("B19: FAIL")
            for f in fails:
                say("   - %s" % f)
            return 1
        say("B19: PASS — the sweep skipped the locked archive and kept going. The file after it in")
        say("     enumeration order was still deleted, which is the half the old shared error_code")
        say("     could not do.")
        return 0
    finally:
        if fh:
            fh.close()
        subprocess.run(["taskkill", "/F", "/IM", "DumperTest.exe"], capture_output=True)
        time.sleep(0.5)
        left = [p for p in paths if p.exists()]
        for p in left:
            try:
                p.unlink()
            except OSError as e:
                say("⚠ could not clean up %s: %s" % (p.name, e))
        if left:
            say("cleaned up %d staged file(s)" % len(left))


if __name__ == "__main__":
    raise SystemExit(main())
