"""Inject UE5Dumper.dll into a live process -- through UE5DumpUI.exe, never from python.exe itself.

    py inject.py --pid 62288                       # inject dist/UE5Dumper.dll
    py inject.py --name DumperTest                 # resolve the PID by image name
    py inject.py --pid 62288 --dll D:/tmp/old.dll  # inject a specific build
    py inject.py --name DumperTest --exe <checkout>/dist/UE5DumpUI.exe   # a worktree has no dist of its own
    py inject.py --list                            # candidate PIDs, nothing else

WHO INJECTS. [AV-INJECT-2026-11-11] The remote thread is started by `UE5DumpUI.exe --inject-elevated <pid> <dll>
<result file>`, the mode the UI launches through UAC for an elevated game. Started plainly, as here, it runs at this
process's integrity, shows nothing and asks nothing. The AV's behavioural layer flagged python.exe doing
CreateRemoteThread(LoadLibraryW) from a terminal, and an AV exception follows the image of the process that acts,
not the script it runs (working-lessons.md 3.8, item 6). So python.exe only finds the target, runs the stale-module
guard below and reads the result. The injector is the one the UI uses, so a difference in outcome between the two is
a real difference. This rig never elevates it: an elevated game needs this whole run elevated.

THE CONTRACT (Program.cs RunElevatedInject, pinned from both ends by InjectElevatedContractTests): exit 0 with the
result file reading `OK <hex>` (the HMODULE's low 32 bits, upper-case, no 0x); exit 1 with the error text; exit 2 for
a bad pid. The file is UTF-8, and some texts carry an em dash. The exe waits 10 s for the remote LoadLibraryW and
passes the DLL path to the target verbatim, so the path given to it is absolute.

FAILS LOUDLY at every step. A silent injector is worthless here: several register rows assert that a scan produced
NOTHING, and "nothing" is exactly what a failed injection also produces (working-lessons.md 1.10a). A LoadLibraryW
that returns NULL in the target, an injector that leaves no result, and one that does not finish in time are each a
failure, not a successful injection.
"""
import argparse
import ctypes
import ctypes.wintypes as w
import os
import subprocess
import sys
import tempfile

k32 = ctypes.WinDLL("kernel32", use_last_error=True)
k32.CloseHandle.argtypes = [w.HANDLE]
k32.CloseHandle.restype = w.BOOL

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
# The exe's own wait for the remote LoadLibraryW is 10 s; the rest is its start-up.
EXE_TIMEOUT_S = 60


class InjectError(RuntimeError):
    pass


def processes():
    """[(image, pid)] from tasklist -- no extra dependency, matches read_mem.py."""
    out = subprocess.run(["tasklist", "/FO", "CSV", "/NH"],
                         capture_output=True, text=True, errors="replace")
    if out.returncode != 0:
        raise InjectError(f"tasklist failed (rc={out.returncode}): {out.stderr.strip()}")
    rows = []
    for line in out.stdout.splitlines():
        parts = [p.strip('"') for p in line.split('","')]
        if len(parts) >= 2 and parts[1].strip('"').isdigit():
            rows.append((parts[0], int(parts[1].strip('"'))))
    return rows


def pid_of(match):
    hits = [(n, p) for n, p in processes() if match.lower() in n.lower()]
    if not hits:
        raise InjectError(f"no running process matches {match!r}")
    if len({p for _, p in hits}) > 1:
        raise InjectError(f"{match!r} is ambiguous: {hits} -- pass --pid")
    return hits[0][1], hits[0][0]


def loaded_modules(pid):
    """[(name, full_path, size)] of modules mapped in `pid`, via Toolhelp32."""
    TH32CS_SNAPMODULE = 0x00000008 | 0x00000010   # SNAPMODULE + SNAPMODULE32

    class MODULEENTRY32W(ctypes.Structure):
        _fields_ = [("dwSize", w.DWORD), ("th32ModuleID", w.DWORD),
                    ("th32ProcessID", w.DWORD), ("GlblcntUsage", w.DWORD),
                    ("ProccntUsage", w.DWORD), ("modBaseAddr", ctypes.POINTER(ctypes.c_byte)),
                    ("modBaseSize", w.DWORD), ("hModule", w.HMODULE),
                    ("szModule", ctypes.c_wchar * 256), ("szExePath", ctypes.c_wchar * 260)]

    k32.CreateToolhelp32Snapshot.restype = w.HANDLE
    snap = k32.CreateToolhelp32Snapshot(TH32CS_SNAPMODULE, pid)
    if not snap or snap == ctypes.c_void_p(-1).value:
        raise InjectError(f"CreateToolhelp32Snapshot({pid}) failed "
                          f"(Win32 {ctypes.get_last_error()})")
    out = []
    try:
        me = MODULEENTRY32W()
        me.dwSize = ctypes.sizeof(MODULEENTRY32W)
        if not k32.Module32FirstW(snap, ctypes.byref(me)):
            raise InjectError(f"Module32FirstW failed (Win32 {ctypes.get_last_error()})")
        while True:
            out.append((me.szModule, me.szExePath, me.modBaseSize))
            if not k32.Module32NextW(snap, ctypes.byref(me)):
                break
    finally:
        k32.CloseHandle(snap)
    return out


# Names worth ASKING about. This is a cheap PRE-FILTER only — it never decides
# anything, exactly as Methode.cpp's own `nameLooksLikeOurs` does not.
PROXY_NAMES = ("dxgi.dll", "version.dll", "winmm.dll", "dinput8.dll")


def already_ours(pid):
    """Modules in `pid` that really ARE a build of ours (an auto-loaded proxy counts).

    ⚠ OWNERSHIP, NOT PATH. [INJECTOWNER-2026-08-24] This used to read:

        # Names that, if already mapped from somewhere other than System32, are ours.
        elif low in PROXY_NAMES and "system32" not in lpath: hits.append(...)

    which is the PRE-FIX rule `B29` removed from the DLL, verbatim — `git show
    229df1d8^:dll/src/Methode.cpp` has the same "not from System32 ⇒ ours" test. The DLL
    was moved to a version-info OWNERSHIP check (`IsOurModule`); this rig was not, so it
    refused to inject into any game carrying a third-party ReShade / SpecialK /
    Ultimate-ASI-Loader wrapper — and its advice ("use a FRESH process") is unfollowable,
    because such a wrapper loads on EVERY launch. Measured on a ReShade `dxgi.dll`:
    refused outright, while `LoadLibraryW("…UE5Dumper.dll")` was never at risk.

    The predicate is now the same one the DLL uses — PE VERSIONINFO `ProductName ==
    "UE5CEDumper"`, enumerating `\\VarFileInfo\\Translation` rather than assuming a
    language block — imported from `b29_product_name` so the two cannot drift.

    ⭐ THE GUARD ITSELF STAYS. The case it was written for is real and still matters: a
    STALE `UE5Dumper.dll` mapped from CE's install folder makes `LoadLibraryW` a silent
    refcount bump on the old binary (`[STALEDLL-2026-08-18]`). Only the *ownership* test
    was missing. The injector exe does not check this itself (the UI checks it before
    calling it), so it stays here.
    """
    try:
        from b29_product_name import product_name
    except ImportError:                       # keep the injector usable if the helper moves
        product_name = None

    hits = []
    for name, path, size in loaded_modules(pid):
        low = name.lower()
        if low != "ue5dumper.dll" and low not in PROXY_NAMES:
            continue                          # pre-filter: not a name we could own
        if product_name is None:
            hits.append((name, path, size))   # cannot decide -> report, do not silently pass
            continue
        if (product_name(path) or "").lower() == "ue5cedumper":
            hits.append((name, path, size))
    return hits


def default_exe():
    return os.path.join(REPO_ROOT, "dist", "UE5DumpUI.exe")


def inject(pid, dll_path, exe_path):
    """Has UE5DumpUI.exe load `dll_path` into `pid`; returns the HMODULE's low 32 bits."""
    dll_path = os.path.abspath(dll_path)       # the exe hands it to the target verbatim
    if not os.path.isfile(dll_path):
        raise InjectError(f"DLL not found: {dll_path}")
    exe_path = os.path.abspath(exe_path)
    if not os.path.isfile(exe_path):
        raise InjectError(f"injector not found: {exe_path} -- publish dist (build.ps1), or pass --exe "
                          f"(a worktree has no dist; name the main checkout's)")

    fd, result_path = tempfile.mkstemp(prefix="ue5inject_", suffix=".txt")
    os.close(fd)                               # empty until the exe writes it
    try:
        try:
            proc = subprocess.run([exe_path, "--inject-elevated", str(pid), dll_path, result_path],
                                  timeout=EXE_TIMEOUT_S)
        except subprocess.TimeoutExpired:
            raise InjectError(f"UE5DumpUI.exe did not finish within {EXE_TIMEOUT_S} s; the DLL may still be "
                              f"initialising -- do NOT read this as success") from None
        with open(result_path, encoding="utf-8-sig", errors="replace") as f:
            text = f.read().strip()
    finally:
        try:
            os.remove(result_path)
        except OSError:
            pass

    if proc.returncode == 0 and text.startswith("OK "):
        try:
            return int(text[3:], 16)
        except ValueError:
            raise InjectError(f"UE5DumpUI.exe reported success with an unreadable handle: {text!r}") from None
    if text and not text.startswith("OK "):
        # Its own words: "LoadLibraryW returned NULL ...", "OpenProcess failed (Win32 5). The game may be ...".
        raise InjectError(f"{text} (UE5DumpUI.exe exit {proc.returncode})")
    raise InjectError(f"UE5DumpUI.exe exited {proc.returncode} with "
                      f"{'the result ' + repr(text) if text else 'no result'} -- the DLL may have loaded "
                      f"anyway: check with --modules before injecting again")


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--pid", type=int)
    ap.add_argument("--name")
    ap.add_argument("--dll", default=None,
                    help="default: dist/UE5Dumper.dll relative to the repo root")
    ap.add_argument("--exe", default=None,
                    help="the injector, UE5DumpUI.exe; default: dist/UE5DumpUI.exe relative to the repo root")
    ap.add_argument("--list", action="store_true")
    ap.add_argument("--allow-stale", action="store_true",
                    help="inject even though a build of ours is already mapped (this is a "
                         "refcount bump, not a load -- see the guard in main)")
    ap.add_argument("--modules", action="store_true",
                    help="list the target's mapped modules and exit; use to answer "
                         "'is an old proxy already in this game?' without injecting")
    a = ap.parse_args(argv)

    if a.list:
        for n, p in sorted(processes()):
            print(f"{p:>8}  {n}")
        return 0

    if a.pid and a.name:
        return _die("pass --pid or --name, not both")
    if not a.pid and not a.name:
        return _die("pass --pid or --name")

    pid, name = (a.pid, None) if a.pid else pid_of(a.name)
    if name is None:
        name = dict((p, n) for n, p in processes()).get(pid, "<gone>")

    if a.modules:
        for n, path, size in loaded_modules(pid):
            print(f"{size:>12,}  {n:<28} {path}")
        return 0

    dll = a.dll if a.dll is not None else os.path.join(REPO_ROOT, "dist", "UE5Dumper.dll")
    exe = a.exe if a.exe is not None else default_exe()

    print(f"target : {name} (pid {pid})")

    # ---- STALE-MODULE GUARD -------------------------------------------------
    # A game may ALREADY have an old build of ours mapped -- most often a proxy
    # auto-loaded out of the game folder. LoadLibraryW then merely bumps that
    # module's refcount and returns it: the injection reports success, the logs
    # fill up, and every measurement is of the OLD binary. The failure is totally
    # silent and it is fatal to any before/after comparison. Not hypothetical --
    # a ~6-month-old UE5Dumper.dll sits in Cheat Engine's install folder on this
    # machine (todo.md's STALEDLL item).
    prior = already_ours(pid)
    if prior:
        print("STALE MODULE(S) ALREADY MAPPED:")
        for n, path, size in prior:
            print(f"   {n}  {size:,} bytes  {path}")
        if not a.allow_stale:
            print("inject.py: FAILED -- refusing to inject. LoadLibraryW would return the "
                  "module listed above instead of loading the file you asked for, and "
                  "nothing in the logs would say so. Use a FRESH process, or pass "
                  "--allow-stale if you really do want the refcount bump.", file=sys.stderr)
            return 1
        print("   (--allow-stale given; continuing -- this is a refcount bump, NOT a load)")
    print(f"dll    : {dll}  ({os.path.getsize(dll):,} bytes)"
          if os.path.isfile(dll) else f"dll    : {dll}  (MISSING)")
    print(f"via    : {exe}")
    hmod = inject(pid, dll, exe)
    print(f"loaded : HMODULE(low32)=0x{hmod:08X}  -- LoadLibraryW returned non-NULL")
    return 0


def _die(msg):
    print(f"inject.py: {msg}", file=sys.stderr)
    return 2


if __name__ == "__main__":
    try:
        sys.exit(main())
    except InjectError as e:
        print(f"inject.py: FAILED -- {e}", file=sys.stderr)
        sys.exit(1)
