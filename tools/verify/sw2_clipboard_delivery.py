r"""SW2 — a clipboard DELIVERY that fails must say so, and must be seen to have failed.

    py tools/verify/sw2_clipboard_delivery.py prepare     # checks, sentinel, hold the clipboard
    <click Tools > Add "Inject DLL" Record to Current CE Table>
    py tools/verify/sw2_clipboard_delivery.py verify      # read the log, release, check the sentinel

THE ROW. 14 delivery sites, the `Action<string>` -> `Func<string,Task<bool>>` split and 5
async-void handlers, pinned only by **gate 17b — a static source check that executes nothing**
(regex over `ui/UE5DumpUI/**/*.cs`; no C# is compiled, no bool is produced, no message rendered),
and 9 of the 14 shipped with no test changes at all. Acceptance: with the clipboard held open by
another process, the UI must say the copy FAILED and tell the user not to paste, AND
`IPlatformService.CopyToClipboardAsync` must actually have returned false.

⭐ BOTH HALVES ARE FILE-OBSERVABLE, which is what makes this a real two-sided check rather than a
screenshot. `%LOCALAPPDATA%\UE5CEDumper\Logs\UE5DumpUI\view-0.log` carries:
  * the PLATFORM line — `WindowsPlatformService.CopyGuardedAsync` logs
    "Clipboard copy FAILED — nothing was copied, the app is unaffected. <type>: <msg>" on the
    exact branch that `return false`s, so it IS the proof that CopyToClipboardAsync returned false;
  * the SITE line — every one of the 14 sites emits its own `_log.Warn` on the statement
    IMMEDIATELY AFTER the one that assigns the failure status text, with no branch between them.
(Serilog opens the file `FileShare.Read`, so Python can read it live; the "we cannot read our own
live log" lesson is a C#-only trap — `File.ReadLines` opens with the wrong share mode.)

⛔ TRAP 1 — THE PLATFORM LINE PREDATES THE FIX. It shipped with `[PASTECRASH-2026-08-18]`, long
before `ClipboardDelivery` existed. So an exe that predates the fix still writes it, and the run
would go green over a binary without the behaviour under test — the SW8 stale-proxy shape exactly.
`prepare` BYTE-GREPS the exe for strings the FIX introduced and refuses if they are missing.

⛔ TRAP 2 — FAILURE WITHOUT A PLATFORM CALL. `ClipboardDelivery.cs:62` returns false when
`platform is null || string.IsNullOrEmpty(payload)` **without ever calling the platform**. An empty
generated payload therefore produces the identical status text and the identical site warn while
the clipboard refusal under test never happens. The PLATFORM line is required for exactly this.

⛔ TRAP 3 — THE WRONG FALSE BRANCH. `CopyGuardedAsync` has a second `return false` that logs
"Clipboard copy did nothing: no main window / no clipboard." A loose grep for "Clipboard copy"
accepts it. The exact prefix is pinned AND a `COMException` type name is required — that is what a
genuinely held clipboard raises.

⛔ TRAP 4 — THE SINGLE-INSTANCE MUTEX. The app takes `Global\UE5DumpUI_SingleInstance` and the
loser exits BEFORE its logger is constructed, so a second launch leaves no trace: clicks drive a
pre-existing process, which writes to the SAME log. If that process is a ~107 MB NON-TRIMMED exe
from an earlier `-Target UI`/`-Target Test` run, the run measured a different program. `prepare`
resolves the running image path and SHA-matches it against `dist\UE5DumpUI.exe`.

⛔ TRAP 5 — CHEAT ENGINE MAKES THE BRANCH UNREACHABLE. 10 of the 14 sites touch the clipboard only
when the CE push did not happen (`if (!sentToCe)`). With CE/AOBMaker reachable the delivery is
never attempted and nothing fails. `prepare` refuses if CE is running.

⛔ TRAP 6 — HOLD EXPIRY IS INDISTINGUISHABLE FROM A BROKEN DETECTOR. If the hold lapses before the
click, the copy SUCCEEDS and nothing clears the status text. The hold is long, its liveness is
asserted at `verify` time, and — the witness the app cannot fake — a SENTINEL is planted before
the lock and must STILL be on the clipboard afterwards.

⛔ TRAP 7 — LOG SCOPING. The logger ARCHIVES the previous file on startup, so byte offsets taken
before the launch point into a file that no longer exists. Scoping is by TIMESTAMP >= t0, the
discipline `retention_backdate.py:386-397` records as a mistake already made once here.
"""
from __future__ import annotations

import argparse
import ctypes
import ctypes.wintypes as wintypes
import hashlib
import io
import json
import os
import re
import subprocess
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
DIST_EXE = ROOT / "dist" / "UE5DumpUI.exe"
DIST_DLL = ROOT / "dist" / "UE5Dumper.dll"
VIEW_LOG = Path(os.environ["LOCALAPPDATA"]) / "UE5CEDumper" / "Logs" / "UE5DumpUI" / "view-0.log"
STATE = ROOT / "out" / "sw2_state.json"

# Strings the FIX introduced -- absent from any exe that predates ClipboardDelivery.
FIX_STRINGS = ["reached neither CE nor the clipboard", "so do not paste", "was NOT delivered"]

PLATFORM_FAIL = "Clipboard copy FAILED — nothing was copied, the app is unaffected."
PLATFORM_WRONG = "Clipboard copy did nothing: no main window / no clipboard."
SITE_PREFIX = "CE inject bootstrap (dll="
SITE_SUFFIX = "reached neither CE nor the clipboard"
SITE_SUCCESS = "CE inject bootstrap sent to CE"
SITE_SUCCESS2 = "CE inject bootstrap to clipboard"
SENTINEL = "SW2-SENTINEL-do-not-overwrite-2026-09-09"


def tasklist(image: str) -> list[int]:
    r = subprocess.run(["tasklist", "/FI", "IMAGENAME eq " + image, "/FO", "CSV", "/NH"],
                       capture_output=True, text=True, encoding="utf-8", errors="replace")
    out = []
    for ln in (r.stdout or "").splitlines():
        m = re.match(r'^"([^"]+)","(\d+)"', ln.strip())
        if m and m.group(1).lower() == image.lower():
            out.append(int(m.group(2)))
    return out


def image_path(pid: int) -> str:
    k = ctypes.WinDLL("kernel32", use_last_error=True)
    h = k.OpenProcess(0x1000, False, pid)          # PROCESS_QUERY_LIMITED_INFORMATION
    if not h:
        return ""
    try:
        buf = ctypes.create_unicode_buffer(32768)
        sz = wintypes.DWORD(32768)
        if not k.QueryFullProcessImageNameW(h, 0, buf, ctypes.byref(sz)):
            return ""
        return buf.value
    finally:
        k.CloseHandle(h)


def sha(p) -> str:
    return hashlib.sha256(Path(p).read_bytes()).hexdigest()


def clipboard_text() -> str | None:
    """Read the clipboard directly -- the out-of-process witness."""
    u32 = ctypes.WinDLL("user32", use_last_error=True)
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    u32.GetClipboardData.restype = ctypes.c_void_p
    k32.GlobalLock.restype = ctypes.c_void_p
    k32.GlobalLock.argtypes = [ctypes.c_void_p]
    k32.GlobalUnlock.argtypes = [ctypes.c_void_p]
    for _ in range(20):
        if u32.OpenClipboard(None):
            break
        time.sleep(0.25)
    else:
        return None
    try:
        h = u32.GetClipboardData(13)               # CF_UNICODETEXT
        if not h:
            return ""
        p = k32.GlobalLock(h)
        if not p:
            return ""
        try:
            return ctypes.wstring_at(p)
        finally:
            k32.GlobalUnlock(h)
    finally:
        u32.CloseClipboard()


def set_clipboard(text: str) -> bool:
    u32 = ctypes.WinDLL("user32", use_last_error=True)
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    k32.GlobalAlloc.restype = ctypes.c_void_p
    k32.GlobalLock.restype = ctypes.c_void_p
    k32.GlobalLock.argtypes = [ctypes.c_void_p]
    k32.GlobalUnlock.argtypes = [ctypes.c_void_p]
    u32.SetClipboardData.argtypes = [wintypes.UINT, ctypes.c_void_p]
    u32.SetClipboardData.restype = ctypes.c_void_p
    data = (text + "\0").encode("utf-16-le")
    for _ in range(20):
        if u32.OpenClipboard(None):
            break
        time.sleep(0.25)
    else:
        return False
    try:
        u32.EmptyClipboard()
        h = k32.GlobalAlloc(0x0042, len(data))     # GMEM_MOVEABLE|GMEM_ZEROINIT
        p = k32.GlobalLock(h)
        ctypes.memmove(p, data, len(data))
        k32.GlobalUnlock(h)
        return bool(u32.SetClipboardData(13, h))
    finally:
        u32.CloseClipboard()


def log_lines_since(t0: str) -> list[str]:
    """⛔ TRAP 7: scope by TIMESTAMP, never by byte offset -- the logger archives on startup."""
    if not VIEW_LOG.is_file():
        return []
    out = []
    for ln in io.open(VIEW_LOG, encoding="utf-8", errors="replace"):
        m = re.match(r"^\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})", ln)
        if m and m.group(1) >= t0:
            out.append(ln.rstrip("\n"))
    return out


def prepare(a) -> int:
    fails = []

    # ---- TRAP 1: the exe must carry the strings the FIX introduced -------------------
    blob = DIST_EXE.read_bytes()
    print("exe        : %s" % DIST_EXE)
    print("             %d bytes  sha %s" % (len(blob), sha(DIST_EXE)[:12]))
    print("             %s" % ("AOT-trimmed" if len(blob) < 70 * 1024 * 1024
                               else "NON-TRIMMED (~107 MB) -- this is NOT the shipped program"))
    for s in FIX_STRINGS:
        n = blob.count(s.encode("utf-16-le")) + blob.count(s.encode("ascii", "ignore"))
        print("  fix-string %-40s x%d" % (repr(s), n))
        if n == 0:
            fails.append("%r is not in the exe -- it predates the ClipboardDelivery fix. The "
                         "PLATFORM log line shipped with [PASTECRASH-2026-08-18] and would "
                         "appear anyway, so the run would be green over the wrong binary." % s)
    if len(blob) >= 70 * 1024 * 1024:
        fails.append("dist\\UE5DumpUI.exe is the NON-TRIMMED build. Re-run "
                     "build.ps1 -Mode Publish -NoBumpBuildNumber.")

    # ---- TRAP 5: Cheat Engine must be closed ----------------------------------------
    for ce in ("cheatengine-x86_64.exe", "cheatengine-i386.exe",
               "cheatengine-x86_64-SSE4-AVX2.exe"):
        if tasklist(ce):
            fails.append("%s is RUNNING. 10 of the 14 sites only touch the clipboard when the "
                         "CE push did not happen (`if (!sentToCe)`), so the delivery would "
                         "never be attempted and nothing would fail." % ce)

    # ---- the site's own precondition -------------------------------------------------
    if not DIST_DLL.is_file():
        fails.append("dist\\UE5Dumper.dll is missing -- InjectCeBootstrapAsync returns before "
                     "it ever reaches the clipboard.")

    # ---- TRAP 4: exactly one UI, and it must BE dist ---------------------------------
    pids = tasklist("UE5DumpUI.exe")
    if len(pids) > 1:
        fails.append("%d UE5DumpUI.exe processes are running -- ambiguous." % len(pids))
    elif len(pids) == 1:
        p = image_path(pids[0])
        print("running UI : pid %d  %s" % (pids[0], p))
        if not p or Path(p).resolve() != DIST_EXE.resolve():
            fails.append("the running UI is %r, NOT dist\\UE5DumpUI.exe. The single-instance "
                         "mutex would send the clicks there and it writes to the SAME log." % p)
        elif sha(p) != sha(DIST_EXE):
            fails.append("the running UI's image differs from dist\\UE5DumpUI.exe on disk.")
    else:
        print("running UI : none -- launch dist\\UE5DumpUI.exe before clicking")

    if fails:
        print("\nSW2 prepare: REFUSED")
        for f in fails:
            print("  - %s" % f)
        return 1

    # ---- TRAP 6: the sentinel, planted BEFORE the lock -------------------------------
    if not set_clipboard(SENTINEL):
        raise SystemExit("could not plant the clipboard sentinel")
    back = clipboard_text()
    print("sentinel   : planted, reads back %r" % back)
    if back != SENTINEL:
        raise SystemExit("the sentinel did not stick -- something else owns the clipboard")

    holder = subprocess.Popen([sys.executable, str(HERE / "clipboard_hold.py"), str(a.hold)],
                              creationflags=0x00000008 | 0x00000200)
    time.sleep(2.5)
    if holder.poll() is not None:
        raise SystemExit("clipboard_hold.py exited immediately -- it could not take the "
                         "clipboard, so nothing would fail")
    t0 = time.strftime("%Y-%m-%d %H:%M:%S")
    STATE.parent.mkdir(parents=True, exist_ok=True)
    STATE.write_text(json.dumps({"t0": t0, "holder_pid": holder.pid,
                                 "ui_sha": sha(DIST_EXE)}), encoding="utf-8")
    print("hold       : pid %d holding the clipboard for %ds" % (holder.pid, a.hold))
    print("t0         : %s  (log lines are scoped to >= this)" % t0)
    print("\nNOW CLICK:  Tools ▸ Add \"Inject DLL\" Record to Current CE Table")
    print("THEN RUN :  py tools/verify/sw2_clipboard_delivery.py verify")
    return 0


def verify(a) -> int:
    st = json.loads(STATE.read_text(encoding="utf-8"))
    t0, hpid = st["t0"], st["holder_pid"]
    fails = []

    # ---- TRAP 6: the hold must still have been alive ---------------------------------
    alive = hpid in [p for p in tasklist("python.exe") + tasklist("py.exe")]
    print("hold       : pid %d alive=%s" % (hpid, alive))
    if not alive:
        fails.append("the clipboard holder had already exited -- the copy may have SUCCEEDED, "
                     "and a stale status line is indistinguishable from a real failure.")

    lines = log_lines_since(t0)
    print("view-0.log : %d line(s) since %s" % (len(lines), t0))

    plat = [l for l in lines if PLATFORM_FAIL in l]
    wrong = [l for l in lines if PLATFORM_WRONG in l]
    site = [l for l in lines if SITE_PREFIX in l and SITE_SUFFIX in l]
    success = [l for l in lines if SITE_SUCCESS in l or SITE_SUCCESS2 in l]

    for l in plat + site:
        print("   %s" % l.strip()[:190])

    # ---- TRAP 2 + TRAP 3: the platform line, and a COMException specifically ---------
    if len(plat) != 1:
        fails.append("expected exactly ONE platform FAILED line, got %d. Without it the site "
                     "warn proves only that ClipboardDelivery returned false -- which an EMPTY "
                     "PAYLOAD also does, without ever calling the platform "
                     "(ClipboardDelivery.cs:62)." % len(plat))
    elif "COMException" not in plat[0]:
        fails.append("the platform line does not name a COMException: %r. A held clipboard "
                     "raises one; anything else means the failure had another cause."
                     % plat[0].strip())
    if wrong:
        fails.append("the OTHER false branch fired (%r) -- that is 'no main window / no "
                     "clipboard', not a refused write." % wrong[0].strip())
    if len(site) != 1:
        fails.append("expected exactly ONE site line %r ... %r, got %d"
                     % (SITE_PREFIX, SITE_SUFFIX, len(site)))
    if success:
        fails.append("a SUCCESS line is present (%r) -- the delivery did not fail."
                     % success[0].strip())

    # ---- release, then the witness the app cannot fake -------------------------------
    if alive:
        subprocess.run(["taskkill", "/F", "/PID", str(hpid)], capture_output=True)
        time.sleep(1.5)
    back = clipboard_text()
    print("clipboard  : %r" % back)
    if back != SENTINEL:
        fails.append("the clipboard no longer holds the sentinel (%r) -- something DID write "
                     "to it, so 'nothing was copied' is false." % back)

    print()
    if fails:
        print("SW2: FAIL")
        for f in fails:
            print("  - %s" % f)
        return 1
    print("SW2: PASS -- with the clipboard held open by another process, the delivery site "
          "reported failure (%r) and the PLATFORM logged a refused write with a COMException, "
          "which is CopyToClipboardAsync having returned false; the sentinel planted before the "
          "lock is still on the clipboard, so nothing was delivered." % SITE_SUFFIX)
    return 0


def main() -> int:
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    p = sub.add_parser("prepare")
    p.add_argument("--hold", type=int, default=300)
    sub.add_parser("verify")
    a = ap.parse_args()
    return prepare(a) if a.cmd == "prepare" else verify(a)


if __name__ == "__main__":
    sys.exit(main())
