"""X12 - Install-CE-autorun must fall back to the save dialog when the auto-place is DENIED.

    py tools/verify/x12_ce_autorun_denied.py stage      # portable CE copy we own
    py tools/verify/x12_ce_autorun_denied.py allow      # target writable  -> CONTROL
    py tools/verify/x12_ce_autorun_denied.py deny       # target read-only -> THE ARM
    py tools/verify/x12_ce_autorun_denied.py status
    py tools/verify/x12_ce_autorun_denied.py clean

THE ROW. `MainWindowViewModel.InstallCeAutorunAsync` auto-places `ue5_autorun.lua` into a running
Cheat Engine's `autorun\\`. When that write is refused it must take the MANUAL save-dialog fallback
(`FileWriteFault.IsPlacementDenied`) instead of reporting failure - the pre-fix code skipped the
fallback in exactly the case that needs it. The classifier is unit-tested; the LIVE denied-write
path never was.

⭐ WHY THIS IS NOT MAINTAINER-ONLY, WHICH IS HOW IT WAS FILED.
The register said it needs "CE installed under %ProgramFiles%, app run non-elevated, which no
unattended session can stage". Two measurements retire that:

  1. `TryFindCheatEngineDirAsync` resolves CE's folder from the **running** `cheatengine*`
     process's own path - NOT from the registry, and NOT from %ProgramFiles%. So any CE we can
     start decides the target folder, and a copy we own is as real as the installed one.
  2. On this host the installed `C:\\Program Files\\Cheat Engine\\autorun` is **writable
     non-elevated** (measured: a probe file created and deleted). So the installed copy would NOT
     have reproduced the denial anyway - the premise was wrong in both directions.

⚠ NO ACL EDITS, DELIBERATELY. Denying write with `icacls` on a real install is a security-settings
change and is not needed: the auto-place is a `File.WriteAllTextAsync` onto a FIXED file name, and
writing to a **read-only file** raises `UnauthorizedAccessException` - the same type a permission
denial raises, and the one `IsPlacementDenied` is written for. One attribute, fully reversible.

⭐ THE CONTROL RUNS FIRST AND MUST BEHAVE DIFFERENTLY. `allow` leaves the same staged CE with a
WRITABLE target: the auto-place must succeed with no dialog (`autoLocated=true`). Only then does
`deny` mean anything - otherwise "a save dialog appeared" is equally consistent with the app never
having found CE at all, which is the same dialog by a different route (`ceDir == null`). The two
paths are distinguishable in the STATUS TEXT and that difference is the check:

    ceDir == null  -> "Cheat Engine not running - choose its autorun folder..."
    denied         -> "Cheat Engine's autorun folder is not writable - choose where to place it..."

⚠ The copy's `autorun\\` is EMPTIED of CE's own extras. They are harmless but they execute at CE
startup, and an unexplained side effect during a verification run costs more than it saves.
"""
from __future__ import annotations

import ctypes
import os
import pathlib
import shutil

import sys

SRC = pathlib.Path(r"C:\Program Files\Cheat Engine")
DST = pathlib.Path(r"D:\ZZCePortable")
EXE = "cheatengine-x86_64.exe"
TARGET_NAME = "ue5_autorun.lua"          # CeAutorunScriptGenerator.DefaultFileName
AUTORUN = "autorun"                      # CeAutorunScriptGenerator.AutorunFolderName
FILE_ATTRIBUTE_READONLY = 0x1


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(s.encode(enc, "replace").decode(enc, "replace") + "\n")


def target():
    return DST / AUTORUN / TARGET_NAME


def running_ce():
    """(pid, path) for every live process whose exe name starts with 'cheatengine'.

    ⚠ NOT via `wmic` -- it is absent on this Windows build (26200) and raises WinError 2. This
    walks the same Toolhelp snapshot + QueryFullProcessImageNameW the app's own enumerator uses,
    so the name test here matches `TryFindCheatEngineDirAsync`'s (spaces stripped, prefix match).
    """
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    TH32CS_SNAPPROCESS = 0x2
    PROCESS_QUERY_LIMITED_INFORMATION = 0x1000

    class PROCESSENTRY32W(ctypes.Structure):
        _fields_ = [("dwSize", ctypes.c_uint32), ("cntUsage", ctypes.c_uint32),
                    ("th32ProcessID", ctypes.c_uint32),
                    ("th32DefaultHeapID", ctypes.POINTER(ctypes.c_ulong)),
                    ("th32ModuleID", ctypes.c_uint32), ("cntThreads", ctypes.c_uint32),
                    ("th32ParentProcessID", ctypes.c_uint32), ("pcPriClassBase", ctypes.c_long),
                    ("dwFlags", ctypes.c_uint32), ("szExeFile", ctypes.c_wchar * 260)]

    k32.CreateToolhelp32Snapshot.restype = ctypes.c_void_p
    snap = k32.CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0)
    if not snap or snap == ctypes.c_void_p(-1).value:
        return []
    hits = []
    e = PROCESSENTRY32W()
    e.dwSize = ctypes.sizeof(e)
    ok = k32.Process32FirstW(ctypes.c_void_p(snap), ctypes.byref(e))
    while ok:
        if e.szExeFile and pathlib.Path(e.szExeFile).stem.replace(" ", "").lower() \
                .startswith("cheatengine"):
            k32.OpenProcess.restype = ctypes.c_void_p
            h = k32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, False, e.th32ProcessID)
            path = ""
            if h:
                buf = ctypes.create_unicode_buffer(32768)
                n = ctypes.c_uint32(len(buf))
                if k32.QueryFullProcessImageNameW(ctypes.c_void_p(h), 0, buf,
                                                  ctypes.byref(n)):
                    path = buf.value
                k32.CloseHandle(ctypes.c_void_p(h))
            hits.append((str(e.th32ProcessID), path or e.szExeFile))
        ok = k32.Process32NextW(ctypes.c_void_p(snap), ctypes.byref(e))
    k32.CloseHandle(ctypes.c_void_p(snap))
    return hits


def readonly(path, on):
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    a = k32.GetFileAttributesW(str(path))
    new = (a | FILE_ATTRIBUTE_READONLY) if on else (a & ~FILE_ATTRIBUTE_READONLY)
    if not k32.SetFileAttributesW(str(path), new):
        say("SetFileAttributes failed on %s (err %d)" % (path, ctypes.get_last_error()))
        return False
    back = bool(k32.GetFileAttributesW(str(path)) & FILE_ATTRIBUTE_READONLY)
    if back != on:
        say("REFUSING: the ReadOnly attribute did not take on %s" % path)
        return False
    return True


def stage():
    if not (SRC / EXE).is_file():
        say("MISSING %s -- no Cheat Engine install to copy from" % (SRC / EXE))
        return 1
    if DST.exists():
        say("REFUSING: %s already exists -- run clean first" % DST)
        return 1
    say("copying %s -> %s (this is a COPY; the real install is never touched)" % (SRC, DST))
    shutil.copytree(SRC, DST)
    ar = DST / AUTORUN
    ar.mkdir(parents=True, exist_ok=True)
    # CE's own extras execute at startup. Harmless, but an unexplained side effect during a
    # verification run is worse than the realism it buys.
    removed = 0
    for p in ar.iterdir():
        if p.is_file():
            p.unlink()
            removed += 1
        else:
            shutil.rmtree(p)
            removed += 1
    target().write_text("", encoding="utf-8")   # empty = a no-op Lua chunk at CE startup
    say("staged: %s" % (DST / EXE))
    say("  autorun emptied (%d CE extra(s) removed from the COPY)" % removed)
    say("  planted %s (0 bytes)" % target())
    say("")
    say("NEXT: run `allow`, start the copy, do the UI action -> auto-place must SUCCEED.")
    say("      then `deny`, repeat -> the save-dialog fallback must appear.")
    return 0


def set_mode(on):
    t = target()
    if not t.is_file():
        say("MISSING %s -- run stage first" % t)
        return 1
    if not readonly(t, on):
        return 1
    # Prove the attribute actually changes the OUTCOME, not just the metadata: the app's write is
    # an ordinary open-for-write, so do exactly that.
    try:
        with open(t, "a", encoding="utf-8"):
            pass
        wrote = True
    except OSError as e:
        wrote = False
        why = "%s: %s" % (type(e).__name__, e)
    say("%s -> ReadOnly=%s ; an open-for-write %s"
        % (t, on, "SUCCEEDS" if wrote else "is REFUSED (%s)" % why))
    if on and wrote:
        say("REFUSING: deny mode but the file is still writable -- the arm would be vacuous")
        return 1
    if not on and not wrote:
        say("REFUSING: allow mode but the file is not writable -- the control would be vacuous")
        return 1
    return 0


def status():
    say("portable CE staged : %s" % DST.is_dir())
    if DST.is_dir():
        t = target()
        ro = None
        if t.is_file():
            ro = bool(ctypes.WinDLL("kernel32").GetFileAttributesW(str(t))
                      & FILE_ATTRIBUTE_READONLY)
        say("  %s exists=%s ReadOnly=%s size=%s"
            % (t, t.is_file(), ro, t.stat().st_size if t.is_file() else "-"))
    ce = running_ce()
    say("running cheatengine* process(es): %d" % len(ce))
    for pid, path in ce:
        owned = pathlib.Path(path).parent == DST
        say("  pid %s  %s   %s" % (pid, path, "<-- OUR COPY" if owned else "<-- NOT ours"))
    if len(ce) > 1:
        say("⚠ more than one CE is running -- TryFindCheatEngineDirAsync returns the FIRST match, "
            "so the folder under test would be ambiguous. Close the others.")
    return 0


def clean():
    if not DST.exists():
        say("nothing to clean")
        return 0
    if running_ce():
        say("REFUSING: a cheatengine* process is still running -- close it first")
        return 1
    assert DST.name.startswith("ZZ"), "refusing %s" % DST
    for p in DST.rglob("*"):
        if p.is_file():
            try:
                p.chmod(0o666)
            except OSError:
                pass
    shutil.rmtree(DST)
    say("removed %s" % DST)
    return 0


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else "status"
    if cmd == "stage":
        sys.exit(stage())
    if cmd == "deny":
        sys.exit(set_mode(True))
    if cmd == "allow":
        sys.exit(set_mode(False))
    if cmd == "clean":
        sys.exit(clean())
    sys.exit(status())
