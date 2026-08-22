r"""AC15 — cross-check the Proxy Deploy grid's *paths* by their CONTENTS.

    py tools/verify/ac15_path_witness.py

⭐ WHY. `ac15_steam_oracle.py` proves the scan finds the same 18 games. This proves the grid's
**Binaries Path** column points where the oracle says, without reading the column at all — the
`Status` / `Loaded?` / `Suggested proxy` cells are computed by opening files *in that folder*, so
they are a second witness to the path itself. If the panel resolved a different directory, a row
saying `DeployedOutdated` would sit over a folder holding no proxy of ours.

For each directory the oracle resolved, this reports which of the four proxy names exist and
whether each is OURS (SHA256 vs `dist/proxy`, or an older build of ours) or a third party's.
Read-only.
"""
import hashlib
import os
import subprocess
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
PROXY_SRC = os.path.join(REPO, "dist", "proxy")
NAMES = ["version.dll", "dinput8.dll", "dxgi.dll", "winmm.dll"]


def sha(p):
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def is_ours(path):
    """Ours iff the PE carries our ProductName. Read as bytes so no COM/pywin32 is needed;
    VERSIONINFO stores strings as UTF-16LE, hence the encode."""
    try:
        blob = open(path, "rb").read()
    except OSError:
        return False
    return b"U\x00E\x005\x00C\x00E\x00D\x00u\x00m\x00p\x00e\x00r\x00" in blob


def main():
    current = {n: sha(os.path.join(PROXY_SRC, n)) for n in NAMES
               if os.path.isfile(os.path.join(PROXY_SRC, n))}

    out = subprocess.run([sys.executable,
                          os.path.join(REPO, "tools", "verify", "ac15_steam_oracle.py")],
                         capture_output=True, text=True, encoding="utf-8", errors="replace")
    bins = [l.split("bin  ", 1)[1].strip()
            for l in out.stdout.splitlines() if l.strip().startswith("bin  ")]
    print(f"oracle resolved {len(bins)} Binaries dir(s)\n")

    deployed = current_cnt = stale = foreign = missing_dir = 0
    for b in bins:
        game = b.split("steamapps\\common\\", 1)[-1].split("\\")[0]
        if not os.path.isdir(b):
            print(f"  ✗ MISSING DIR   {game}: {b}")
            missing_dir += 1
            continue
        found = []
        for n in NAMES:
            p = os.path.join(b, n)
            if not os.path.isfile(p):
                continue
            if not is_ours(p):
                found.append(f"{n}=THIRD-PARTY")
                foreign += 1
                continue
            deployed += 1
            if sha(p) == current.get(n):
                found.append(f"{n}=ours@3315")
                current_cnt += 1
            else:
                found.append(f"{n}=ours@OLDER")
                stale += 1
        print(f"  {'•' if found else ' '} {game:<46} {', '.join(found) if found else '(no proxy)'}")

    print(f"\ndirs that do not exist        : {missing_dir}   <- must be 0, or the grid's path is wrong")
    print(f"our proxies found             : {deployed}  ({current_cnt} current, {stale} older)")
    print(f"third-party wrappers          : {foreign}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
