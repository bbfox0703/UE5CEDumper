r"""PROXYLOAD step 1 -- back up / swap / restore OCTOPATH's deployed proxy, verifiably.

    py tools/verify/octopath_proxy_swap.py backup
    py tools/verify/octopath_proxy_swap.py state
    py tools/verify/octopath_proxy_swap.py restore

WHY A RIG AND NOT THREE COPY COMMANDS
  This writes into a REAL game installation, authorised once by the maintainer for one purpose.
  The deployed winmm.dll is **not** byte-identical to `dist/proxy/winmm.dll` (same SIZE,
  different sha -- the 2026-08-20 rebuild), so "just redeploy from dist" does NOT put the install
  back as found. The only safe restore is a byte copy of what was there, verified by hash.

  `backup` refuses to overwrite an existing backup, so re-running it cannot destroy the original.
  `restore` refuses unless the backup hashes match what was captured. `state` writes nothing.

WHAT IT DELIBERATELY DOES NOT DO
  It does not deploy version.dll. That is done through the UI's Proxy Deploy panel on purpose --
  the row is about what that panel's Status / Loaded? / Suggested columns say, so the deploy has
  to go through the real `ProxyDeployService` code path rather than around it.
"""
import argparse
import glob
import hashlib
import json
import os
import re
import shutil
import sys
import io
import time

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

VDF = r"C:\Program Files (x86)\Steam\steamapps\libraryfolders.vdf"
PROXY_NAMES = ("version.dll", "dinput8.dll", "dxgi.dll", "winmm.dll", "UE5Dumper.dll")
BACKUP_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                          "..", "..", "out", "octopath-proxy-backup")
MANIFEST = "manifest.json"


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


def win64_dir():
    libs = []
    if os.path.exists(VDF):
        txt = open(VDF, encoding="utf-8", errors="replace").read()
        libs = [l.replace("\\\\", "\\") for l in re.findall(r'"path"\s+"([^"]+)"', txt)]
    for l in libs:
        for cand in glob.glob(os.path.join(l, "steamapps", "common", "*OCTOPATH*")):
            hits = glob.glob(os.path.join(cand, "**", "Octopath_Traveler-Win64-Shipping.exe"),
                             recursive=True)
            if hits:
                return os.path.dirname(hits[0])
    return None


def present(d):
    out = {}
    for n in PROXY_NAMES:
        p = os.path.join(d, n)
        if os.path.exists(p):
            out[n] = {"size": os.path.getsize(p), "sha256": sha(p),
                      "mtime": os.path.getmtime(p)}
    return out


def log_folder_state():
    f = os.path.join(os.environ["LOCALAPPDATA"], "UE5CEDumper", "Logs",
                     "Octopath_Traveler-Win64-Shipping")
    if not os.path.isdir(f):
        return {"exists": False, "path": f}
    files = [os.path.join(f, x) for x in os.listdir(f)]
    newest = max((os.path.getmtime(x) for x in files), default=0)
    return {"exists": True, "path": f, "count": len(files), "newest_mtime": newest,
            "newest_str": stamp(newest)}


def cmd_state(d):
    say("Win64 dir : %s" % d)
    cur = present(d)
    if not cur:
        say("  (no proxy-named DLLs present)")
    for n, m in sorted(cur.items()):
        say("  %-16s size=%-10d mtime=%s  sha=%s"
            % (n, m["size"], stamp(m["mtime"]), m["sha256"][:16]))
    lf = log_folder_state()
    say("")
    say("Log folder: %s" % lf["path"])
    if lf["exists"]:
        say("  EXISTS -- %d file(s), newest %s" % (lf["count"], lf["newest_str"]))
        say("  => 'not observed' is UNREACHABLE for this title; the discriminator is")
        say("     whether 'newest' ADVANCES across a launch.")
    else:
        say("  absent -> 'not observed' is reachable")
    return 0


def cmd_backup(d):
    bdir = os.path.abspath(BACKUP_DIR)
    man = os.path.join(bdir, MANIFEST)
    if os.path.exists(man):
        say("REFUSING: a backup already exists at %s" % bdir)
        say("Delete it by hand if you are certain it is stale. Refusing protects the ORIGINAL")
        say("bytes -- a second backup taken after the swap would capture the wrong file.")
        return 1
    os.makedirs(bdir, exist_ok=True)
    cur = present(d)
    if not cur:
        say("nothing to back up (no proxy-named DLLs present)")
    for n in cur:
        shutil.copy2(os.path.join(d, n), os.path.join(bdir, n))
        got = sha(os.path.join(bdir, n))
        if got != cur[n]["sha256"]:
            say("FAIL: backup copy of %s does not match the original hash" % n)
            return 1
        say("backed up %-16s sha=%s  (verified)" % (n, got[:16]))
    payload = {"win64": d, "files": cur, "log_folder": log_folder_state(),
               "captured": stamp(time.time())}
    with open(man, "w", encoding="utf-8") as f:
        json.dump(payload, f, indent=1)
    say("")
    say("manifest: %s" % man)
    return 0


def cmd_restore(d):
    bdir = os.path.abspath(BACKUP_DIR)
    man = os.path.join(bdir, MANIFEST)
    if not os.path.exists(man):
        say("FAIL: no manifest at %s -- nothing to restore from" % man)
        return 1
    payload = json.load(open(man, encoding="utf-8"))
    want = payload["files"]

    # Verify the backup itself first: restoring from a corrupted backup is worse than not
    # restoring, because it looks like it worked.
    for n, m in want.items():
        b = os.path.join(bdir, n)
        if not os.path.exists(b):
            say("FAIL: backup file missing: %s" % b)
            return 1
        if sha(b) != m["sha256"]:
            say("FAIL: backup file %s does not match its recorded hash -- refusing" % n)
            return 1
    say("backup verified (%d file(s))" % len(want))

    # Remove any proxy-named DLL that was NOT there originally.
    for n in PROXY_NAMES:
        p = os.path.join(d, n)
        if os.path.exists(p) and n not in want:
            os.remove(p)
            say("removed  %-16s (was not present before the run)" % n)

    # Put back the originals, byte for byte.
    for n, m in want.items():
        p = os.path.join(d, n)
        if os.path.exists(p) and sha(p) == m["sha256"]:
            say("already OK %-14s" % n)
            continue
        shutil.copy2(os.path.join(bdir, n), p)
        got = sha(p)
        if got != m["sha256"]:
            say("FAIL: restored %s does not match the original hash" % n)
            return 1
        say("restored %-16s sha=%s  (verified)" % (n, got[:16]))

    say("")
    say("--- final state ---")
    cmd_state(d)
    now = present(d)
    ok = (set(now) == set(want)
          and all(now[n]["sha256"] == want[n]["sha256"] for n in want))
    say("")
    say("RESTORED EXACTLY: %s" % ok)
    return 0 if ok else 1


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("action", choices=["state", "backup", "restore"])
    a = ap.parse_args()
    d = win64_dir()
    if not d:
        say("FAIL: could not locate OCTOPATH's Binaries\\Win64 directory")
        return 1
    return {"state": cmd_state, "backup": cmd_backup, "restore": cmd_restore}[a.action](d)


if __name__ == "__main__":
    sys.exit(main())
