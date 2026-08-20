r"""PROXYLOAD step 1 on OCTOPATH -- baseline, log-folder park, and the module-level detector.

    py tools/verify/octopath_step1_rig.py baseline
    py tools/verify/octopath_step1_rig.py park-logs      # makes 'not observed' REACHABLE
    py tools/verify/octopath_step1_rig.py modules        # THE decisive detector
    py tools/verify/octopath_step1_rig.py newlogs
    py tools/verify/octopath_step1_rig.py unpark-logs

WHY EACH PIECE EXISTS -- every one of these closes a specific way the run would lie.

`park-logs`
  The row's stated PASS is "the Loaded? column stays 'not observed'". `ClassifyLoad` emits that
  string ONLY when the log folder is absent, and OCTOPATH's folder exists with 40+ files one day
  old -- far inside `LogMaxAgeDays`=21. Run the row verbatim and the cell reads `loaded 2026-08-19`
  no matter what loads, so the step FAILS while telling you nothing. Renaming the folder aside is
  what makes the row's own expectation reachable at all. It also removes contaminated evidence:
  every archived run in that folder is a **winmm** run -- there is not one version-proxy line in
  its whole history.

`modules`  <- THE decisive one
  The Loaded? column is keyed to the EXE, not to the proxy, so it structurally cannot say WHICH
  flavour loaded. Both flavours also stamp the same FileVersion, so the panel's version text and
  the log's `build:` line cannot separate them either. What CAN: the loaded-module list. This
  reports, per PID, whether `version.dll` is mapped from the GAME directory (ours won) or only
  from `C:\WINDOWS\System32` (bypassed) -- which is exactly the observation the original finding
  rested on.

  It enumerates EVERY matching PID, because this title SELF-RELAUNCHES (the `[RELAUNCHPIPE]`
  defect): a launcher spawns the shipping exe, two processes write one log folder, and sampling
  one of them is how a bypass and a load look identical.

`newlogs`
  Reads whatever the run produced and reports the `[PROXY]` flavour line -- `version proxy` /
  `Loaded real version.dll` vs `winmm proxy`. A second detector, independent of the module walk.

NOT COVERED HERE, ON PURPOSE
  The deploy/undeploy go through the UI's Proxy Deploy panel, because the row is an assertion
  about that panel's columns; doing the file copy here would route around the code under test.
  Restoring the proxy bytes is `octopath_proxy_swap.py restore`.
"""
import argparse
import ctypes
import glob
import hashlib
import io
import json
import os
import re
import shutil
import sys
import time
from ctypes import wintypes

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

VDF = r"C:\Program Files (x86)\Steam\steamapps\libraryfolders.vdf"
EXE_LEAF = "Octopath_Traveler-Win64-Shipping.exe"
LOG_KEY = "Octopath_Traveler-Win64-Shipping"
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "out")
BASE = os.path.abspath(os.path.join(OUT, "octopath-step1"))
PARKED = None  # computed


def say(s):
    sys.stdout.write(str(s) + "\n")
    sys.stdout.flush()


def sha(p):
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for b in iter(lambda: f.read(1 << 20), b""):
            h.update(b)
    return h.hexdigest()


def stamp(t):
    return time.strftime("%Y-%m-%d %H:%M:%S", time.localtime(t))


def logs_root():
    return os.path.join(os.environ["LOCALAPPDATA"], "UE5CEDumper", "Logs")


def log_dir():
    return os.path.join(logs_root(), LOG_KEY)


def parked_dir():
    # ⚠ OUTSIDE Logs\ on purpose. `Sein::PruneStaleProcessFolders` runs on EVERY DLL init in
    # ANY process and remove_all()s empty/aged sibling folders under Logs\, so a folder parked
    # inside Logs\ could be destroyed by the very launch this run is about. This folder is also
    # the disk evidence [PROXYLOAD-CORR-2026-08-20] was computed from -- losing it costs a
    # measurement that cannot be retaken.
    #
    # ...but still on the SAME VOLUME as Logs\, one level up. A cross-volume destination turns
    # os.rename into a copy+delete (WinError 17), and a 40-file copy+delete of the only evidence
    # of this title's proxy history is exactly the risk the park is supposed to avoid. One level
    # up is out of the sweep's reach and keeps the move atomic.
    return os.path.join(os.environ["LOCALAPPDATA"], "UE5CEDumper", "PARKED-logs-step1")


def win64():
    libs = []
    if os.path.exists(VDF):
        t = open(VDF, encoding="utf-8", errors="replace").read()
        libs = [l.replace("\\\\", "\\") for l in re.findall(r'"path"\s+"([^"]+)"', t)]
    hits = []
    for l in libs:
        for c in glob.glob(os.path.join(l, "steamapps", "common", "*OCTOPATH*")):
            hits += glob.glob(os.path.join(c, "**", EXE_LEAF), recursive=True)
    if len(hits) > 1:
        say("FAIL: %d OCTOPATH installs found -- refusing to guess:" % len(hits))
        for h in hits:
            say("   " + h)
        sys.exit(1)
    return os.path.dirname(hits[0]) if hits else None


# ---------- module enumeration (the decisive detector) ----------
TH32CS_SNAPMODULE = 0x8
TH32CS_SNAPMODULE32 = 0x10
INVALID = ctypes.c_void_p(-1).value
k32 = ctypes.WinDLL("kernel32", use_last_error=True)


class MODULEENTRY32W(ctypes.Structure):
    _fields_ = [("dwSize", wintypes.DWORD), ("th32ModuleID", wintypes.DWORD),
                ("th32ProcessID", wintypes.DWORD), ("GlblcntUsage", wintypes.DWORD),
                ("ProccntUsage", wintypes.DWORD), ("modBaseAddr", ctypes.POINTER(ctypes.c_byte)),
                ("modBaseSize", wintypes.DWORD), ("hModule", wintypes.HMODULE),
                ("szModule", ctypes.c_wchar * 256), ("szExePath", ctypes.c_wchar * 260)]


def pids_named(leaf):
    out = []
    import subprocess
    r = subprocess.run(["tasklist", "/FI", "IMAGENAME eq " + leaf, "/FO", "CSV", "/NH"],
                       capture_output=True, text=True, errors="replace")
    for line in r.stdout.splitlines():
        m = re.match(r'"([^"]+)","(\d+)"', line.strip())
        if m:
            out.append((m.group(1), int(m.group(2))))
    return out


def modules_of(pid):
    k32.CreateToolhelp32Snapshot.restype = wintypes.HANDLE
    snap = k32.CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, pid)
    if snap == INVALID or not snap:
        return None, ctypes.get_last_error()
    me = MODULEENTRY32W()
    me.dwSize = ctypes.sizeof(MODULEENTRY32W)
    mods = []
    if k32.Module32FirstW(snap, ctypes.byref(me)):
        while True:
            mods.append(me.szExePath)
            me2 = MODULEENTRY32W()
            me2.dwSize = ctypes.sizeof(MODULEENTRY32W)
            if not k32.Module32NextW(snap, ctypes.byref(me2)):
                break
            me = me2
    k32.CloseHandle(snap)
    return mods, 0


def cmd_modules(_d):
    d = win64()
    procs = pids_named(EXE_LEAF) + pids_named("Octopath_Traveler.exe")
    if not procs:
        say("no OCTOPATH process is running -- nothing to enumerate")
        say("(a bypass and a game that never booted look IDENTICAL on the log folder;")
        say(" this detector is the one that separates them, so run it while the game is up)")
        return 1
    verdicts = []
    for leaf, pid in procs:
        mods, err = modules_of(pid)
        say("")
        say("=== %s  pid=%d ===" % (leaf, pid))
        if mods is None:
            say("   could not snapshot modules (err %d) -- 64/32-bit or permission" % err)
            continue
        say("   %d modules mapped" % len(mods))
        for want in ("version.dll", "winmm.dll", "dxgi.dll", "dinput8.dll"):
            hits = [m for m in mods if os.path.basename(m).lower() == want]
            for h in hits:
                same = d and os.path.normcase(os.path.dirname(h)) == os.path.normcase(d)
                tag = "  <== OURS, from the GAME DIR" if same else "  (system copy)"
                say("   %-14s %s%s" % (want, h, tag))
                if want == "version.dll":
                    verdicts.append(("game" if same else "system", pid, h))
            if not hits and want == "version.dll":
                say("   %-14s NOT MAPPED AT ALL" % want)
                verdicts.append(("absent", pid, None))
    say("")
    say("--- VERDICT on version.dll ---")
    if not verdicts:
        say("  inconclusive: no snapshot succeeded")
        return 1
    kinds = set(v[0] for v in verdicts)
    if "game" in kinds:
        say("  OUR app-dir version.dll IS MAPPED -> the static import did NOT bypass it.")
        say("  (this contradicts the row's premise, consistent with EVERSPACE)")
    elif kinds == {"system"}:
        say("  ONLY the System32 copy is mapped -> BYPASSED, the row's premise holds here.")
    else:
        say("  version.dll not mapped at all -> unexpected; treat as INCONCLUSIVE.")
    return 0


def cmd_baseline(_d):
    d = win64()
    os.makedirs(BASE, exist_ok=True)
    man = {"win64": d, "captured": stamp(time.time()), "files": {}, "logs": {}, "procs": []}
    for f in sorted(os.listdir(d)):
        p = os.path.join(d, f)
        if os.path.isfile(p):
            man["files"][f] = {"size": os.path.getsize(p), "mtime": os.path.getmtime(p),
                               "sha256": sha(p)}
            say("  %-40s %12d  %s  %s" % (f, man["files"][f]["size"],
                                          stamp(man["files"][f]["mtime"]),
                                          man["files"][f]["sha256"][:16]))
    ld = log_dir()
    if os.path.isdir(ld):
        for f in sorted(os.listdir(ld)):
            p = os.path.join(ld, f)
            man["logs"][f] = {"size": os.path.getsize(p), "mtime": os.path.getmtime(p)}
        newest = max((v["mtime"] for v in man["logs"].values()), default=0)
        say("\n  log folder: %d file(s), newest %s" % (len(man["logs"]), stamp(newest)))
    # ui-options.json -- deploying rewrites lastManualProxyByGame and PERMANENTLY replaces the
    # Suggested warning with 'last used'. Without this copy the row can never be re-run here.
    ui = os.path.join(os.environ["LOCALAPPDATA"], "UE5CEDumper", "ui-options.json")
    if os.path.exists(ui):
        shutil.copy2(ui, os.path.join(BASE, "ui-options.json.bak"))
        say("  backed up ui-options.json  sha=%s" % sha(ui)[:16])
    man["procs"] = pids_named(EXE_LEAF) + pids_named("Octopath_Traveler.exe")
    say("  OCTOPATH processes running: %s" % (man["procs"] or "none"))
    with open(os.path.join(BASE, "baseline.json"), "w", encoding="utf-8") as f:
        json.dump(man, f, indent=1)
    say("\nbaseline -> %s" % os.path.join(BASE, "baseline.json"))
    return 0


def cmd_park(_d):
    src, dst = log_dir(), parked_dir()
    if not os.path.isdir(src):
        say("log folder already absent -- 'not observed' is already reachable")
        return 0
    if os.path.exists(dst):
        say("FAIL: %s already exists -- refusing to overwrite a parked copy" % dst)
        return 1
    os.rename(src, dst)
    say("parked  %s\n     -> %s" % (src, dst))
    say("'not observed' is now REACHABLE. Anything that reappears at the original path was")
    say("written by THIS run, which is what makes the observation attributable.")
    return 0


def cmd_unpark(_d):
    src, dst = parked_dir(), log_dir()
    if not os.path.isdir(src):
        say("nothing parked at %s" % src)
        return 1
    if os.path.isdir(dst):
        merged = 0
        for f in os.listdir(dst):
            tgt = os.path.join(src, "RUN-" + f)
            shutil.move(os.path.join(dst, f), tgt)
            merged += 1
        os.rmdir(dst)
        say("moved %d file(s) produced by the run into the parked folder as RUN-*" % merged)
    os.rename(src, dst)
    say("restored %s" % dst)
    return 0


def cmd_newlogs(_d):
    ld = log_dir()
    if not os.path.isdir(ld):
        say("NO log folder at %s" % ld)
        say("=> the DLL never initialised in a process named %s during this run." % LOG_KEY)
        say("   That is CONSISTENT with a bypass, but also with: the game never booted, the")
        say("   deploy failed, or DllMain faulted. Settle it with `modules`, not with this.")
        return 0
    files = sorted(os.listdir(ld))
    say("log folder EXISTS: %d file(s)" % len(files))
    for f in files:
        p = os.path.join(ld, f)
        say("   %-28s %8d  %s" % (f, os.path.getsize(p), stamp(os.path.getmtime(p))))
    say("")
    say("--- [PROXY] flavour lines (the only text that names the flavour) ---")
    found = False
    for f in files:
        if not f.endswith(".log"):
            continue
        for line in open(os.path.join(ld, f), encoding="utf-8", errors="replace"):
            if "[PROXY]" in line or "ProxyStart" in line:
                say("   " + line.rstrip()[:200])
                found = True
    if not found:
        say("   (none)")
    return 0


HOLD_SUFFIX = ".holdback-step1"


def cmd_holdback(_d):
    """Take the existing proxy out of play by RENAMING it in place.

    Deliberately NOT the panel's Undeploy button: `UndeployAsync` sweeps every one of
    `AllProxyDllNames()` and calls `File.Delete` -- a hard unlink, NOT the Recycle Bin (that is
    the separate orphan-cleanup path). A rename is atomic, copies zero bytes, preserves size and
    all three timestamps, and the `.holdback-*` suffix is invisible to every removal path we
    ship, so nothing of ours will tidy it away mid-run.

    Removing winmm is not optional: `Heiter.cpp` DLL_PROCESS_ATTACH creates
    `Local\\UE5CEDumper_PrimaryProxy_<pid>` and, on ERROR_ALREADY_EXISTS, returns TRUE *before*
    `Sein::Init()` -- deliberately without logging. So "version.dll loaded but wrote no log" is a
    DESIGNED outcome whenever another of our proxies attached first. Leave winmm in place and a
    load and a bypass are indistinguishable.
    """
    d = win64()
    moved = 0
    for n in ("version.dll", "dinput8.dll", "dxgi.dll", "winmm.dll"):
        p = os.path.join(d, n)
        if os.path.exists(p):
            h = p + HOLD_SUFFIX
            if os.path.exists(h):
                say("FAIL: %s already exists -- refusing" % h)
                return 1
            os.rename(p, h)
            say("held back %-14s -> %s  (sha unchanged %s)" % (n, os.path.basename(h), sha(h)[:16]))
            moved += 1
    if not moved:
        say("no proxy-named DLLs to hold back")
    say("")
    say("Win64 now contains:")
    for f in sorted(os.listdir(d)):
        say("   " + f)
    return 0


def cmd_unholdback(_d):
    d = win64()
    done = 0
    for f in sorted(os.listdir(d)):
        if not f.endswith(HOLD_SUFFIX):
            continue
        orig = os.path.join(d, f[: -len(HOLD_SUFFIX)])
        if os.path.exists(orig):
            os.remove(orig)
            say("removed the run's %s" % os.path.basename(orig))
        os.rename(os.path.join(d, f), orig)
        say("restored %-14s sha=%s" % (os.path.basename(orig), sha(orig)[:16]))
        done += 1
    if not done:
        say("nothing held back")
    say("")
    say("Win64 now contains:")
    for f in sorted(os.listdir(d)):
        say("   " + f)
    return 0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("action", choices=["baseline", "park-logs", "unpark-logs",
                                       "modules", "newlogs", "holdback", "unholdback"])
    a = ap.parse_args()
    if not win64():
        say("FAIL: OCTOPATH Binaries\\Win64 not found")
        return 1
    return {"baseline": cmd_baseline, "park-logs": cmd_park, "unpark-logs": cmd_unpark,
            "modules": cmd_modules, "newlogs": cmd_newlogs,
            "holdback": cmd_holdback, "unholdback": cmd_unholdback}[a.action](None)


if __name__ == "__main__":
    sys.exit(main())
