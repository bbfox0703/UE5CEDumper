"""AC11 half 2 (docs/audit-2026-08-13-early-code-findings.md), at the OS level: what error does
the STAGED publish raise when the target proxy DLL is mapped by a running game?

WHY THIS EXISTS, AND WHY IT IS NOT THE OBVIOUS TEST
    `CopyProxyStaged` publishes by staging, not by a direct copy:

        File.Copy(source, target, overwrite: true)          # direct: opens TARGET for write
    versus
        File.Copy(source, target + ".ue5dump-stage", ...)   # opens a NEW file
        File.Move(stage, target, overwrite: true)           # RENAMES over the target

    Those two hit a locked target through completely different kernel paths, so they do
    NOT surface the same way, and the register row's requirement -- the user's message
    must not change -- is the thing under test, because `DeployAsync` only reports

        "File locked (game running?)"

    from `catch (Exception ex) when (IsTargetUnreplaceable(ex))`.

    A file carrying an IMAGE section (what a loaded DLL is) refuses deletion with
    STATUS_CANNOT_DELETE, which surfaces as ERROR_ACCESS_DENIED (5), not
    ERROR_SHARING_VIOLATION (32). .NET turns 5 into UnauthorizedAccessException -- not an
    IOException at all -- so an IOException-only filter would miss it and fall through to
    the generic handler as "Access to the path ... is denied". Same failure, different
    words, and the words are what the row is about.

WHAT IT MEASURES
    Both publish shapes against the same target, in both states:

        A  target NOT mapped   -- the negative control. Both must SUCCEED; without this
                                  an "error 5" result could just mean a broken path.
        B  target mapped as an image by a live child process

    The child maps it with LOAD_LIBRARY_AS_IMAGE_RESOURCE|DONT_RESOLVE_DLL_REFERENCES so
    a real SEC_IMAGE section exists WITHOUT running DllMain -- our proxy would otherwise
    start a pipe server inside the probe.

    Nothing here touches a game folder: the whole rig runs in out/ac11/.

WHAT TO EXPECT, AND WHAT PASS / FAIL MEAN
    A  both shapes succeed, no residue                         -- the control holds
    B  direct File.Copy  -> ERROR_SHARING_VIOLATION (32)
       staged publish    -> ERROR_ACCESS_DENIED     (5)    <-- they DISAGREE, an OS fact

    PASS: each shape's error is one `IsTargetUnreplaceable` maps to the locked arm, and no
    .ue5dump-stage survives the failed publish. FAIL: an error the filter does not
    recognise (the user would get a generic ErrorOther), a staged publish that SUCCEEDS
    over a mapped image, or staging residue. [STAGELOCK-2026-08-20]

    py tools/verify/ac11_locked_rename.py
"""
import ctypes
import ctypes.wintypes as w
import pathlib
import shutil
import subprocess
import sys
import time

ROOT = pathlib.Path(__file__).resolve().parents[2]
SCRATCH = ROOT / "out" / "ac11"
SRC = ROOT / "dist" / "proxy" / "version.dll"

MOVEFILE_REPLACE_EXISTING = 0x1
MOVEFILE_COPY_ALLOWED = 0x2
ERROR_SHARING_VIOLATION = 32
ERROR_ACCESS_DENIED = 5

k32 = ctypes.WinDLL("kernel32", use_last_error=True)
k32.MoveFileExW.argtypes = [w.LPCWSTR, w.LPCWSTR, w.DWORD]
k32.MoveFileExW.restype = w.BOOL
k32.CopyFileW.argtypes = [w.LPCWSTR, w.LPCWSTR, w.BOOL]
k32.CopyFileW.restype = w.BOOL


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(s.encode(enc, "replace").decode(enc, "replace") + "\n")


def err_name(e):
    return {0: "ok", 5: "ERROR_ACCESS_DENIED", 32: "ERROR_SHARING_VIOLATION",
            80: "ERROR_FILE_EXISTS", 2: "ERROR_FILE_NOT_FOUND"}.get(e, "error %d" % e)


# ---------------------------------------------------------------- child: hold a mapping
CHILD = r'''
import ctypes, ctypes.wintypes as w, sys, time
p = sys.argv[1]
k = ctypes.WinDLL("kernel32", use_last_error=True)
k.LoadLibraryExW.argtypes = [w.LPCWSTR, w.HANDLE, w.DWORD]
k.LoadLibraryExW.restype = w.HMODULE
# 0x20 LOAD_LIBRARY_AS_IMAGE_RESOURCE | 0x1 DONT_RESOLVE_DLL_REFERENCES
h = k.LoadLibraryExW(p, None, 0x20 | 0x1)
if not h:
    print("CHILD-FAIL %d" % ctypes.get_last_error(), flush=True)
    sys.exit(1)
print("MAPPED %d" % h, flush=True)
time.sleep(120)
'''


def direct_copy(src, target):
    """the DIRECT publish shape: open the target for write."""
    ok = k32.CopyFileW(str(src), str(target), False)
    return 0 if ok else ctypes.get_last_error()


def staged_publish(src, target):
    """the STAGED publish shape: copy to a sibling, then rename over the target.

    The `finally { File.Delete(stagePath) }` of the real CopyProxyStaged is mirrored
    here on purpose. Without it this rig reports a `.ue5dump-stage` leftover that the
    product does NOT leave -- a false finding against the very step it is checking.
    """
    stage = pathlib.Path(str(target) + ".ue5dump-stage")
    try:
        ok = k32.CopyFileW(str(src), str(stage), False)
        if not ok:
            return ("copy-to-stage", ctypes.get_last_error(), stage)
        e = 0
        if not k32.MoveFileExW(str(stage), str(target),
                               MOVEFILE_REPLACE_EXISTING | MOVEFILE_COPY_ALLOWED):
            e = ctypes.get_last_error()
        return ("rename-over-target", e, stage)
    finally:
        try:
            if stage.exists():
                stage.unlink()
        except OSError:
            pass


def main():
    if not SRC.is_file():
        say("FAIL: %s missing" % SRC)
        return 1
    SCRATCH.mkdir(parents=True, exist_ok=True)
    target = SCRATCH / "version.dll"
    shutil.copy2(SRC, target)
    fails = []

    say("source : %s (%d bytes)" % (SRC, SRC.stat().st_size))
    say("target : %s" % target)

    # ---------------- A. negative control: nothing holds the file -------------
    say("\n-- A. NEGATIVE CONTROL: target not mapped, both shapes must SUCCEED --")
    e = direct_copy(SRC, target)
    say("   direct File.Copy over target      : %s" % err_name(e))
    if e:
        fails.append("negative control: direct copy failed")
    what, e, stage = staged_publish(SRC, target)
    say("   staged copy+rename (%s): %s" % (what, err_name(e)))
    if e:
        fails.append("negative control: staged publish failed")
    if stage.exists():
        fails.append("negative control left a .ue5dump-stage behind")
        stage.unlink()
    say("   leftover .ue5dump-stage           : %s" % (list(SCRATCH.glob("*.ue5dump-stage")) or "none"))

    # ---------------- B. the real question: target mapped as an image ---------
    say("\n-- B. target MAPPED AS AN IMAGE by a live process (what a running game does) --")
    child = subprocess.Popen([sys.executable, "-c", CHILD, str(target)],
                             stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    line = child.stdout.readline().strip()
    if not line.startswith("MAPPED"):
        say("   FAIL: child could not map it: %s" % line)
        child.kill()
        return 1
    say("   child pid %d holds an image section (%s)" % (child.pid, line))
    time.sleep(0.3)

    try:
        e_direct = direct_copy(SRC, target)
        say("   OLD  direct File.Copy over target : %s" % err_name(e_direct))
        what, e_staged, stage = staged_publish(SRC, target)
        say("   NEW  staged publish, failed at %-18s: %s" % (what, err_name(e_staged)))
        leftovers = list(SCRATCH.glob("*.ue5dump-stage"))
        say("   leftover .ue5dump-stage           : %s"
            % ([p.name for p in leftovers] or "none"))
    finally:
        child.kill()
        child.wait(timeout=10)

    # ---------------- the verdict ---------------------------------------------
    #
    # The two publish shapes ALWAYS produce DIFFERENT OS errors: a direct copy opens the target
    # (SHARING_VIOLATION) while a rename must first delete it (ACCESS_DENIED). That disagreement
    # is an OS fact, not a defect, and ProxyDeployService accepts it rather than removing it:
    # it catches `Exception ex when (IsTargetUnreplaceable(ex))`, which maps
    # UnauthorizedAccessException AND the sharing-violation IOException to the same locked arm.
    #
    # So the question is not 'do the two shapes agree' but 'is each shape's error one the
    # filter recognises'. Asking the first gives a permanent false FAIL against correct code.
    say('')
    say('-- what DeployAsync reports NOW (IsTargetUnreplaceable, ProxyDeployService.cs:1007) --')
    CAUGHT = {ERROR_SHARING_VIOLATION: 'IOException (sharing violation)',
              ERROR_ACCESS_DENIED:     'UnauthorizedAccessException'}
    for label, e in (('OLD direct copy', e_direct), ('NEW staged publish', e_staged)):
        if e == 0:
            say('   %-19s-> SUCCEEDED (the lock did not stop it)' % label)
        elif e in CAUGHT:
            say('   %-19s-> %-24s-> %-32s-> LOCKED arm' % (label, err_name(e), CAUGHT[e]))
        else:
            say('   %-19s-> %-24s-> NOT recognised -> generic ErrorOther' % (label, err_name(e)))

    for label, e in (('direct copy', e_direct), ('staged publish', e_staged)):
        if e != 0 and e not in CAUGHT:
            fails.append('the %s failed with %s, which IsTargetUnreplaceable does NOT map to the '
                         'locked arm -- the user would get a generic ErrorOther' % (label, err_name(e)))
    if e_staged == 0:
        fails.append('the staged publish SUCCEEDED over a mapped image -- unexpected')
    if leftovers:
        fails.append('a .ue5dump-stage survived the failed publish')

    say("")
    if fails:
        for f in fails:
            say("FAIL: %s" % f)
        return 1
    say("PASS: every locked-target failure maps to the LOCKED arm, no staging residue")
    return 0


if __name__ == "__main__":
    sys.exit(main())
