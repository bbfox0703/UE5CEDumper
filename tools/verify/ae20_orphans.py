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

⭐ WHY `--dlls` AND `lock` EXIST (added 2026-08-24)
    Cancelling a 1-DLL-per-row pass is NOT a discriminating test of the
    `[ORPHANCANCEL-2026-08-20]` fix, and the first re-run on disk proved it: the cancel
    landed in the 11 ms gap BETWEEN rows, so the interrupted row had recycled nothing and
    the tally matched the log (3 == 3) — which the PRE-fix code would also have reported.
    A run only discriminates when a NON-SUCCESSFUL row has already recycled something,
    because that is the arithmetic the fix moved out of `if (result.Success)`.

    The fix record names the deterministic way to get there — the sibling hole the finding
    did not mention and `APartlyLockedRow_AlsoCountsWhatItRecycled` pins: a row that
    recycles one of its DLLs and then hits a LOCK returns `Success == false`, so
    everything it recycled used to go unreported. No cancel, no timing.

        create --count 2 --dlls version,dxgi   # two proxies per row
        lock ZZAe20Orphan000 dxgi.dll          # hold it open with NO sharing

    `lock` opens with `dwShareMode == 0`, so the service's own
    `FileStream(..., FileShare.Read | FileShare.Delete)` fails with
    ERROR_SHARING_VIOLATION (0x80070020) — the exact `catch` that appends to `locked`.
    Expect `1 file(s) recycled` in the summary and exactly one `Recycled leftover proxy`
    line in the log; the pre-fix code said 0 against that same 1.
"""
import ctypes
import hashlib
import pathlib
import shutil
import sys
import time

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


def proxy_sources(spec):
    """Resolve a comma-separated proxy list to real files beside our version.dll."""
    out = []
    for raw in spec.split(","):
        name = raw.strip()
        if not name:
            continue
        if not name.lower().endswith(".dll"):
            name += ".dll"
        p = OURS.parent / name
        if not p.is_file():
            say("MISSING %s" % p)
            return None
        out.append(p)
    return out or None


def create(count, spec="version"):
    if not OURS.is_file():
        say("MISSING %s" % OURS)
        return 1
    srcs = proxy_sources(spec)
    if not srcs:
        return 1
    if trees():
        say("REFUSING: %d %s* tree(s) already exist -- run clean first" % (len(trees()), PREFIX))
        return 1
    for i in range(count):
        b = bin_of(LIB / f"{PREFIX}{i:03d}")
        b.mkdir(parents=True)
        for s in srcs:
            shutil.copy2(s, b / s.name)
    say("created %d synthetic leftover tree(s) under %s%s%s###" % (count, LIB, chr(92), PREFIX))
    say("  each = <root>%sZZOrphan%sBinaries%sWin64%s{%s}, NO shipping exe beside it"
        % (chr(92), chr(92), chr(92), chr(92), ", ".join(s.name for s in srcs)))
    for s in srcs:
        say("  source %s" % s)
        say("    sha256 %s  (%d bytes)" % (hashlib.sha256(s.read_bytes()).hexdigest(),
                                           s.stat().st_size))
    return 0


def readonly(tree, dll, on=True):
    """Flip the ReadOnly attribute on one staged DLL, AFTER the scan.

    ⭐ THIS, NOT `lock`, IS THE DETERMINISTIC PARTLY-FAILED ROW — measured 2026-08-24.
    A LOCKED file is invisible to the delete-time RE-PLAN: `RemoveOrphanProxyAsync` re-scans
    the folder and the identity re-check cannot open a share-locked file, so it drops out of
    `plan.FilesToRecycle` entirely and the row succeeds at everything it is still asked to do
    (measured: `Cleaned 2 of 2 ... 3 file(s) recycled`, no `is locked` warning anywhere, and
    the DLL still on disk). The `locked` list is unreachable that way.

    A READ-ONLY file still OPENS, so it survives the re-plan and reaches the loop's own
    `(fi.Attributes & FileAttributes.ReadOnly) != 0 -> readOnly.Add(...); continue;` branch.
    `ResolveRemovalOutcome` then returns Success=false with `recycled >= 1` from the row's
    OTHER DLL - which is precisely the accounting the fix moved out of `if (result.Success)`,
    and the case `APartlyLockedRow_AlsoCountsWhatItRecycled` pins at the seam.

    Never clear it on a file we did not stage: `clean` would then leave it behind.
    """
    path = bin_of(LIB / tree) / dll
    if not path.is_file():
        say("MISSING %s" % path)
        return 1
    FILE_ATTRIBUTE_READONLY = 0x1
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    attrs = k32.GetFileAttributesW(str(path))
    new = (attrs | FILE_ATTRIBUTE_READONLY) if on else (attrs & ~FILE_ATTRIBUTE_READONLY)
    if not k32.SetFileAttributesW(str(path), new):
        say("SetFileAttributes failed on %s (err %d)" % (path, ctypes.get_last_error()))
        return 1
    back = k32.GetFileAttributesW(str(path))
    say("%s ReadOnly=%s on %s (attrs 0x%X -> 0x%X)"
        % ("SET" if on else "CLEARED", bool(back & FILE_ATTRIBUTE_READONLY), path, attrs, back))
    if bool(back & FILE_ATTRIBUTE_READONLY) != on:
        say("REFUSING to continue: the attribute did not take")
        return 1
    return 0


def lock(tree, dll, seconds):
    """Hold `dll` open with NO share mode, so any other open is a sharing violation.

    This is what makes a row PARTLY succeed: the service recycles the row's other DLL,
    then this one throws IOException 0x80070020 into the `locked` list -> Success=false
    with recycled >= 1, which is the accounting case the fix moved out of the success
    branch. `FileShare.None` is deliberate: FileShare.Delete would let the recycle through.
    """
    path = bin_of(LIB / tree) / dll
    if not path.is_file():
        say("MISSING %s" % path)
        return 1
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    k32.CreateFileW.restype = ctypes.c_void_p        # a HANDLE is 64-bit; the default int TRUNCATES
    k32.CreateFileW.argtypes = [ctypes.c_wchar_p, ctypes.c_uint32, ctypes.c_uint32,
                                ctypes.c_void_p, ctypes.c_uint32, ctypes.c_uint32,
                                ctypes.c_void_p]
    GENERIC_READ, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL = 0x80000000, 3, 0x80
    h = k32.CreateFileW(str(path), GENERIC_READ, 0, None, OPEN_EXISTING,
                        FILE_ATTRIBUTE_NORMAL, None)
    if h is None or h == 0xFFFFFFFFFFFFFFFF:
        say("could not open %s (err %d)" % (path, ctypes.get_last_error()))
        return 1
    say("HOLDING %s with dwShareMode=0 for %.0fs -- any other open is now a sharing "
        "violation" % (path, seconds))
    sys.stdout.flush()
    try:
        time.sleep(seconds)
    finally:
        k32.CloseHandle(ctypes.c_void_p(h))
    say("released %s" % path)
    return 0


def status():
    ts = trees()
    if not ts:
        say("no %s* trees present" % PREFIX)
        return 0
    present = missing = 0
    held = 0
    for t in ts:
        dlls = [p for p in bin_of(t).glob("*.dll")] if bin_of(t).is_dir() else []
        if dlls:
            present += 1
            held += len(dlls)
        else:
            missing += 1
    say("%d tree(s): %d still hold a proxy (%d DLL(s) total), %d already emptied"
        % (len(ts), present, held, missing))
    return 0


def clean():
    ts = trees()
    if not ts:
        say("nothing to clean")
        return 0
    for t in ts:
        assert t.name.startswith(PREFIX) and t.parent == LIB, "refusing %s" % t
        # The `readonly` verb leaves files rmtree cannot delete. Clearing the bit here is what
        # keeps the fixture self-cleaning -- otherwise a failed arm strands a tree the next
        # `create` then REFUSES to run alongside.
        for p in t.rglob("*"):
            if p.is_file():
                try:
                    p.chmod(0o666)
                except OSError:
                    pass
        shutil.rmtree(t)
    say("removed %d tree(s)" % len(ts))
    return 0


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else "status"
    if cmd == "create":
        n = 40
        spec = "version"
        if "--count" in sys.argv:
            n = int(sys.argv[sys.argv.index("--count") + 1])
        if "--dlls" in sys.argv:
            spec = sys.argv[sys.argv.index("--dlls") + 1]
        sys.exit(create(n, spec))
    if cmd == "readonly":
        sys.exit(readonly(sys.argv[2], sys.argv[3], "--off" not in sys.argv))
    if cmd == "lock":
        secs = 120.0
        if "--seconds" in sys.argv:
            secs = float(sys.argv[sys.argv.index("--seconds") + 1])
        sys.exit(lock(sys.argv[2], sys.argv[3], secs))
    if cmd == "clean":
        sys.exit(clean())
    sys.exit(status())
