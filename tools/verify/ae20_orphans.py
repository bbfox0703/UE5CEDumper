"""AE20: stage MANY synthetic leftover-proxy trees so the Delete-checked pass can be cancelled.

    py tools/verify/ae20_orphans.py create [--count 40]
    py tools/verify/ae20_orphans.py status
    py tools/verify/ae20_orphans.py clean

WHY IT IS NOT `stage_synth.py`
    That rig stages exactly ONE orphan, which is right for AE4 step 4. AE20 is the
    CANCEL path, and the row says so itself: "Needs several rows: with one row the
    loop finishes before a human can click". A cancel that arrives after the loop
    ended is indistinguishable from a cancel that does nothing, so the run has to be
    long enough that Cancel lands INSIDE it.

WHY SYNTHETIC, AND WHY THAT IS NOT A COMPROMISE HERE
    The step is destructive — it moves files to the Recycle Bin — and the session
    plan's §4 authorises exactly two writes outside our own files, neither of them
    this one. Every tree here is created by this script under a `ZZAe20Orphan*` name,
    so the only files the app can delete are ones that did not exist minutes earlier.
    The scanner's definition of a leftover is "one of our four proxy DLLs in a
    `…/Binaries/Win64` with no `*-Win64-Shipping.exe` beside it", which is exactly
    what these are — so they are not a weaker stand-in for real leftovers, they ARE
    the thing, minus a game that was never installed.

    `clean` removes only paths matching the `ZZAe20Orphan` prefix, and refuses
    anything else.
"""
import hashlib
import pathlib
import shutil
import sys

LIB = pathlib.Path(r"D:\SteamLibrary\steamapps\common")
PREFIX = "ZZAe20Orphan"
OURS = pathlib.Path(r"D:\Github\UE5CEDumper\dist\proxy\version.dll")


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(s.encode(enc, "replace").decode(enc, "replace") + "\n")


def trees():
    return sorted(p for p in LIB.glob(PREFIX + "*") if p.is_dir())


def bin_of(root):
    return root / "ZZOrphan" / "Binaries" / "Win64"


def create(count):
    if not OURS.is_file():
        say("MISSING %s" % OURS)
        return 1
    if trees():
        say("REFUSING: %d %s* tree(s) already exist -- run clean first" % (len(trees()), PREFIX))
        return 1
    digest = hashlib.sha256(OURS.read_bytes()).hexdigest()
    for i in range(count):
        b = bin_of(LIB / f"{PREFIX}{i:03d}")
        b.mkdir(parents=True)
        shutil.copy2(OURS, b / "version.dll")
    say("created %d synthetic leftover tree(s) under %s\\%s###" % (count, LIB, PREFIX))
    say("  each = <root>\\ZZOrphan\\Binaries\\Win64\\version.dll, NO shipping exe beside it")
    say("  source %s" % OURS)
    say("  sha256 %s  (%d bytes each)" % (digest, OURS.stat().st_size))
    return 0


def status():
    ts = trees()
    if not ts:
        say("no %s* trees present" % PREFIX)
        return 0
    present = missing = 0
    for t in ts:
        (present := present) if False else None
        if (bin_of(t) / "version.dll").is_file():
            present += 1
        else:
            missing += 1
    say("%d tree(s): %d still hold version.dll, %d already emptied" % (len(ts), present, missing))
    return 0


def clean():
    ts = trees()
    if not ts:
        say("nothing to clean")
        return 0
    for t in ts:
        assert t.name.startswith(PREFIX) and t.parent == LIB, "refusing %s" % t
        shutil.rmtree(t)
    say("removed %d tree(s)" % len(ts))
    return 0


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else "status"
    if cmd == "create":
        n = 40
        if "--count" in sys.argv:
            n = int(sys.argv[sys.argv.index("--count") + 1])
        sys.exit(create(n))
    if cmd == "clean":
        sys.exit(clean())
    sys.exit(status())
