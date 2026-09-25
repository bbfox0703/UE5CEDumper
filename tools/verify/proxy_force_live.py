"""Live rig for [PROXY-FORCE-UPDATEALL] / [PROXY-DEPLOY-NOOP-COUNT]: Force Overwrite in the Proxy Deploy tab.

    py tools/verify/proxy_force_live.py backup                 # copy every deployed proxy of ours to out/proxy-backups/
    py tools/verify/proxy_force_live.py report                 # per game: FileVersion, size, SHA-256, == dist?
    py tools/verify/proxy_force_live.py tamper "Lushfoil"      # same FileVersion, DIFFERENT bytes (the reported case)
    py tools/verify/proxy_force_live.py restore <backup-dir>   # put a backup set back

WHY. The maintainer's report: a DLL rebuilt without a build-number bump keeps FileVersion 1.0.0.<build_number>,
so Update All said "already up-to-date" even with Force Overwrite ticked. `tamper` reproduces that exactly: it
appends 16 bytes to a deployed proxy of ours -- the PE and its VERSIONINFO are untouched (FileVersionInfo reads the
resource section, not the file length), so the UI sees the same version while the SHA no longer matches dist.
A forced Update All must then write dist's bytes back; an unforced one must leave the tampered file alone.

It only ever touches OUR proxies (VERSIONINFO ProductName == "UE5CEDumper", the UI's own IsOurProxyDll predicate,
shared through b29_product_name), refuses while the game is running, and `backup` runs before anything else.
Discovery and helpers are proxy_refresh.py's.
"""
import ctypes
import ctypes.wintypes as w
import pathlib
import shutil
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from proxy_refresh import BACKUPS, deployed, dist_map, running, say, sha   # noqa: E402
from b29_product_name import product_name                                 # noqa: E402

ver = ctypes.WinDLL("version")
ver.GetFileVersionInfoSizeW.argtypes = [w.LPCWSTR, ctypes.POINTER(w.DWORD)]
ver.GetFileVersionInfoW.argtypes = [w.LPCWSTR, w.DWORD, w.DWORD, ctypes.c_void_p]
ver.VerQueryValueW.argtypes = [ctypes.c_void_p, w.LPCWSTR, ctypes.POINTER(ctypes.c_void_p), ctypes.POINTER(w.UINT)]


def file_version(p):
    """The StringFileInfo FileVersion string -- what the UI's GetDllVersion compares (FileVersionInfo.FileVersion)."""
    n = ver.GetFileVersionInfoSizeW(str(p), None)
    if not n:
        return None
    buf = ctypes.create_string_buffer(n)
    if not ver.GetFileVersionInfoW(str(p), 0, n, buf):
        return None
    ptr, ln = ctypes.c_void_p(), w.UINT()
    if not ver.VerQueryValueW(buf, r"\VarFileInfo\Translation", ctypes.byref(ptr), ctypes.byref(ln)) or ln.value < 4:
        return None
    lang, cp = ctypes.cast(ptr, ctypes.POINTER(w.WORD))[0], ctypes.cast(ptr, ctypes.POINTER(w.WORD))[1]
    key = rf"\StringFileInfo\{lang:04x}{cp:04x}\FileVersion"
    if not ver.VerQueryValueW(buf, key, ctypes.byref(ptr), ctypes.byref(ln)) or not ln.value:
        return None
    return ctypes.wstring_at(ptr, ln.value).rstrip("\x00")


def ours():
    return [(d, exe, q) for d, exe, q in deployed() if product_name(str(q)) == "UE5CEDumper"]


def report():
    dm = dist_map()
    say(f"dist/proxy: " + ", ".join(f"{n}={file_version(p)} {sha(p)[:12]}" for n, p in sorted(dm.items())))
    for d, exe, q in ours():
        src = dm.get(q.name.lower())
        same = src is not None and sha(src) == sha(q)
        say(f"  {d.name[:34]:34} {q.name:12} v{file_version(q)}  {q.stat().st_size:>10,}  {sha(q)[:12]}"
            f"  {'== dist' if same else '!= dist'}  written {time.strftime('%H:%M:%S', time.localtime(q.stat().st_mtime))}")


def backup():
    dest = BACKUPS / time.strftime("force-live-%Y%m%d-%H%M%S")
    dest.mkdir(parents=True, exist_ok=False)
    for d, exe, q in ours():
        to = dest / d.name / q.name
        to.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(q, to)
        (to.parent / "source.txt").write_text(f"{q}\n{sha(q)}\n", encoding="utf-8")
    say(f"backed up {len(ours())} proxy(ies) to {dest}")


def tamper(match):
    hits = [(d, exe, q) for d, exe, q in ours() if match.lower() in d.name.lower()]
    if len(hits) != 1:
        raise SystemExit(f"{match!r} must match exactly one game with our proxy, matched {len(hits)}")
    d, exe, q = hits[0]
    if running(exe.name):
        raise SystemExit(f"{exe.name} is running -- refusing")
    v0, s0 = file_version(q), sha(q)
    with open(q, "ab") as f:
        f.write(b"PROXYFORCELIVE!!")   # 16 bytes past the PE image: version resource untouched
    say(f"tampered {q}: v{v0} -> v{file_version(q)}, {s0[:12]} -> {sha(q)[:12]}")


def restore(src):
    src = pathlib.Path(src)
    n = 0
    for f in src.glob("*/*.dll"):
        target = pathlib.Path((f.parent / "source.txt").read_text(encoding="utf-8").splitlines()[0])
        shutil.copy2(f, target)
        n += 1
    say(f"restored {n} proxy(ies) from {src}")


if __name__ == "__main__":
    verb = sys.argv[1] if len(sys.argv) > 1 else "report"
    {"report": lambda: report(), "backup": lambda: backup(),
     "tamper": lambda: tamper(sys.argv[2]), "restore": lambda: restore(sys.argv[2])}[verb]()
