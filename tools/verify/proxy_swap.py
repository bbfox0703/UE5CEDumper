r"""Swap ONE deployed proxy DLL in a real game install for a staged / red / green arm, and put it back.

    py tools/verify/proxy_swap.py --win64 <Binaries\Win64> --proxy dxgi --exe <Game-Win64-Shipping.exe> --tag l33 state
    py tools/verify/proxy_swap.py ... save                 # keep a byte copy of what is deployed now
    py tools/verify/proxy_swap.py ... install <file.dll>   # e.g. out\staged\<spec>\proxy\dxgi.dll, dist\proxy\dxgi.dll
    py tools/verify/proxy_swap.py ... restore              # the saved copy back, verified by hash

The generic form of l62_winmm_swap.py. Every path is an ARGUMENT: an install location is not fixed
(Steam libraries move, and a trademark sign hides a folder from a name match), so none is written
down here. Find the folder from `proxy_refresh.py report` or the Steam libraryfolders.vdf.

WHY. A proxy-mode row that needs a staged DLL cannot inject it: the deployed proxy already serves
the pipe, and a fresh injection sees "pipe already exists" and stays inert (working-lessons 2.6).
The staged code has to BE the proxy the game loads by name (`stage_dll.py` with "proxies": [...]).
This writes into a real game install, so every write is verified by hash, and `restore` puts back
the exact bytes that were there, not "the dist build". `save` keeps its copy under out\<tag>\ and
refuses to overwrite it; every write is refused while the game runs (a mapped DLL is not replaced).
"""
import argparse
import hashlib
import json
import os
import shutil
import subprocess
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
PROXIES = ("version", "dinput8", "dxgi", "winmm")


def sha(p):
    return hashlib.sha256(open(p, "rb").read()).hexdigest()


def game_running(exe):
    r = subprocess.run(["tasklist", "/FI", "IMAGENAME eq " + exe, "/NH"], capture_output=True, text=True,
                       encoding="utf-8", errors="replace")
    return exe.lower() in (r.stdout or "").lower()


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--win64", required=True, help="the game's Binaries\\Win64 folder")
    ap.add_argument("--proxy", required=True, choices=PROXIES)
    ap.add_argument("--exe", required=True, help="the game's process image name, to refuse writes while it runs")
    ap.add_argument("--tag", required=True, help="out\\<tag>\\ keeps the as-found copy")
    ap.add_argument("cmd", choices=("state", "save", "install", "restore"))
    ap.add_argument("src", nargs="?", help="install: the DLL to put in place")
    a = ap.parse_args()

    if not os.path.isdir(a.win64):
        raise SystemExit("no such folder: " + a.win64)
    deployed = os.path.join(a.win64, a.proxy + ".dll")
    save_dir = os.path.join(REPO, "out", a.tag)
    saved = os.path.join(save_dir, a.proxy + ".as-found.dll")
    manifest = os.path.join(save_dir, a.proxy + ".as-found.json")

    if a.cmd == "state":
        print("deployed : %s" % (sha(deployed)[:16] if os.path.exists(deployed) else "ABSENT"))
        print("saved    : %s" % (sha(saved)[:16] if os.path.exists(saved) else "none"))
        print("game     : %s" % ("RUNNING" if game_running(a.exe) else "not running"))
        return 0
    if game_running(a.exe):
        raise SystemExit("REFUSED: %s is running -- kill it first" % a.exe)
    if a.cmd == "save":
        if os.path.exists(saved):
            raise SystemExit("REFUSED: %s already exists (sha %s) -- it is the as-found copy" % (saved, sha(saved)[:16]))
        if not os.path.exists(deployed):
            raise SystemExit("REFUSED: nothing deployed at " + deployed)
        os.makedirs(save_dir, exist_ok=True)
        shutil.copy2(deployed, saved)
        st = os.stat(deployed)
        meta = {"from": deployed, "sha256": sha(deployed), "size": st.st_size, "mtime": st.st_mtime}
        json.dump(meta, open(manifest, "w", encoding="utf-8"), indent=1)
        if sha(saved) != meta["sha256"]:
            raise SystemExit("the saved copy does not hash like the deployed file")
        print("saved %s  sha %s" % (saved, meta["sha256"][:16]))
    elif a.cmd == "install":
        if not a.src or not os.path.exists(a.src):
            raise SystemExit("install needs an existing source DLL")
        if not os.path.exists(saved):
            raise SystemExit("REFUSED: run `save` first, so the as-found file can be put back")
        shutil.copy2(a.src, deployed)
        if sha(deployed) != sha(a.src):
            raise SystemExit("the deployed file does not hash like %s" % a.src)
        print("installed %s  sha %s" % (a.src, sha(deployed)[:16]))
    elif a.cmd == "restore":
        meta = json.load(open(manifest, encoding="utf-8"))
        if sha(saved) != meta["sha256"]:
            raise SystemExit("REFUSED: the saved copy no longer hashes as captured")
        shutil.copy2(saved, deployed)
        if sha(deployed) != meta["sha256"]:
            raise SystemExit("the restored file does not hash as captured")
        print("restored the as-found %s.dll  sha %s" % (a.proxy, meta["sha256"][:16]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
