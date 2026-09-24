r"""L62 [W1-WINMM-LOADMODE]: swap OCTOPATH's deployed winmm.dll proxy for a red / green arm, and put it back.

    py tools/verify/l62_winmm_swap.py state
    py tools/verify/l62_winmm_swap.py save                 # keep a byte copy of what is deployed now
    py tools/verify/l62_winmm_swap.py install <winmm.dll>  # e.g. out\staged\<spec>\proxy\winmm.dll, dist\proxy\winmm.dll
    py tools/verify/l62_winmm_swap.py restore              # the saved copy back, verified by hash

WHY. The row's red needs a winmm.dll PROXY built without the fix, because the load-mode classifier
runs inside the module the OS loaded by name (`stage_dll.py` with `"proxies": ["winmm"]` builds one).
This writes into a real game install, so every write is verified by hash, and `restore` puts back
the exact bytes that were there, not "the dist build" (not byte-identical, see octopath_proxy_swap.py).

⚠ Do NOT use out\octopath-proxy-backup\ for this: that is the 2026-08-19 1.0.0.3263 build, which
predates the fix AND most of the fix pass -- a red built from it differs from green in far more
than the classifier. `save` keeps its own copy under out\l62\ and refuses to overwrite it.
It refuses every write while the game runs (a mapped DLL is not replaced in the process).
"""
import hashlib
import json
import os
import shutil
import subprocess
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
WIN64 = r"D:\SteamLibrary\steamapps\common\OCTOPATH TRAVELER\Octopath_Traveler\Binaries\Win64"
DEPLOYED = os.path.join(WIN64, "winmm.dll")
SAVE_DIR = os.path.join(REPO, "out", "l62")
SAVED = os.path.join(SAVE_DIR, "winmm.as-found.dll")
MANIFEST = os.path.join(SAVE_DIR, "winmm.as-found.json")
EXE = "Octopath_Traveler-Win64-Shipping.exe"


def sha(p):
    return hashlib.sha256(open(p, "rb").read()).hexdigest()


def game_running():
    r = subprocess.run(["tasklist", "/FI", "IMAGENAME eq " + EXE, "/NH"], capture_output=True, text=True,
                       encoding="utf-8", errors="replace")
    return EXE.lower() in (r.stdout or "").lower()


def refuse_if_running():
    if game_running():
        raise SystemExit("REFUSED: %s is running -- kill it first" % EXE)


def main():
    if len(sys.argv) < 2 or sys.argv[1] not in ("state", "save", "install", "restore"):
        raise SystemExit(__doc__)
    cmd = sys.argv[1]
    if not os.path.isdir(WIN64):
        raise SystemExit("no such folder: " + WIN64)
    if cmd == "state":
        print("deployed : %s" % (sha(DEPLOYED)[:16] if os.path.exists(DEPLOYED) else "ABSENT"))
        print("saved    : %s" % (sha(SAVED)[:16] if os.path.exists(SAVED) else "none"))
        print("game     : %s" % ("RUNNING" if game_running() else "not running"))
        return 0
    refuse_if_running()
    if cmd == "save":
        if os.path.exists(SAVED):
            raise SystemExit("REFUSED: %s already exists (sha %s) -- it is the as-found copy" % (SAVED, sha(SAVED)[:16]))
        os.makedirs(SAVE_DIR, exist_ok=True)
        shutil.copy2(DEPLOYED, SAVED)
        st = os.stat(DEPLOYED)
        meta = {"from": DEPLOYED, "sha256": sha(DEPLOYED), "size": st.st_size, "mtime": st.st_mtime}
        json.dump(meta, open(MANIFEST, "w", encoding="utf-8"), indent=1)
        if sha(SAVED) != meta["sha256"]:
            raise SystemExit("the saved copy does not hash like the deployed file")
        print("saved %s  sha %s" % (SAVED, meta["sha256"][:16]))
    elif cmd == "install":
        src = sys.argv[2]
        if not os.path.exists(SAVED):
            raise SystemExit("REFUSED: run `save` first, so the as-found file can be put back")
        shutil.copy2(src, DEPLOYED)
        if sha(DEPLOYED) != sha(src):
            raise SystemExit("the deployed file does not hash like %s" % src)
        print("installed %s  sha %s" % (src, sha(DEPLOYED)[:16]))
    elif cmd == "restore":
        meta = json.load(open(MANIFEST, encoding="utf-8"))
        if sha(SAVED) != meta["sha256"]:
            raise SystemExit("REFUSED: the saved copy no longer hashes as captured")
        shutil.copy2(SAVED, DEPLOYED)
        if sha(DEPLOYED) != meta["sha256"]:
            raise SystemExit("the restored file does not hash as captured")
        print("restored the as-found winmm.dll  sha %s" % meta["sha256"][:16])
    return 0


if __name__ == "__main__":
    sys.exit(main())
