"""Inject UE5Dumper.dll into a live process -- the PowerShell-free injector.

    py inject.py --pid 62288                       # inject dist/UE5Dumper.dll
    py inject.py --name DumperTest                 # resolve the PID by image name
    py inject.py --pid 62288 --dll D:/tmp/old.dll  # inject a specific build
    py inject.py --list                            # candidate PIDs, nothing else

Exists because `dist/inject-ue.ps1` is the only injector that shipped and this
machine's maintainer does not run ad-hoc PowerShell (the AV's ATD quarantined six
files over a single .ps1 run). Same mechanism as the .ps1 -- VirtualAllocEx the
wide path, CreateRemoteThread on LoadLibraryW -- so a difference in outcome
between the two is a real difference, not a porting artefact.

FAILS LOUDLY at every step. A silent injector is worthless here: several register
rows assert that a scan produced NOTHING, and "nothing" is exactly what a failed
injection also produces (working-lessons.md 1.10a). Every Win32 call checks its
return and raises with GetLastError; a LoadLibraryW that returns NULL in the
target is reported as a failure, not as a successful injection.
"""
import argparse
import ctypes
import ctypes.wintypes as w
import os
import subprocess
import sys

k32 = ctypes.WinDLL("kernel32", use_last_error=True)

PROCESS_ALL_ACCESS = 0x1F0FFF
MEM_COMMIT_RESERVE = 0x3000
MEM_RELEASE = 0x8000
PAGE_READWRITE = 0x04
WAIT_OBJECT_0 = 0x0
INFINITE_ISH_MS = 30000

k32.OpenProcess.argtypes = [w.DWORD, w.BOOL, w.DWORD]
k32.OpenProcess.restype = w.HANDLE
k32.VirtualAllocEx.argtypes = [w.HANDLE, w.LPVOID, ctypes.c_size_t, w.DWORD, w.DWORD]
k32.VirtualAllocEx.restype = w.LPVOID
k32.VirtualFreeEx.argtypes = [w.HANDLE, w.LPVOID, ctypes.c_size_t, w.DWORD]
k32.VirtualFreeEx.restype = w.BOOL
k32.WriteProcessMemory.argtypes = [w.HANDLE, w.LPVOID, w.LPCVOID,
                                   ctypes.c_size_t, ctypes.POINTER(ctypes.c_size_t)]
k32.WriteProcessMemory.restype = w.BOOL
k32.GetModuleHandleW.argtypes = [w.LPCWSTR]
k32.GetModuleHandleW.restype = w.HMODULE
k32.GetProcAddress.argtypes = [w.HMODULE, w.LPCSTR]
k32.GetProcAddress.restype = w.LPVOID
k32.CreateRemoteThread.argtypes = [w.HANDLE, w.LPVOID, ctypes.c_size_t, w.LPVOID,
                                   w.LPVOID, w.DWORD, w.LPVOID]
k32.CreateRemoteThread.restype = w.HANDLE
k32.WaitForSingleObject.argtypes = [w.HANDLE, w.DWORD]
k32.WaitForSingleObject.restype = w.DWORD
k32.GetExitCodeThread.argtypes = [w.HANDLE, ctypes.POINTER(w.DWORD)]
k32.GetExitCodeThread.restype = w.BOOL
k32.IsWow64Process.argtypes = [w.HANDLE, ctypes.POINTER(w.BOOL)]
k32.IsWow64Process.restype = w.BOOL
k32.CloseHandle.argtypes = [w.HANDLE]
k32.CloseHandle.restype = w.BOOL


class InjectError(RuntimeError):
    pass


def _win_fail(what):
    raise InjectError(f"{what} failed (Win32 {ctypes.get_last_error()})")


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


# Names that, if already mapped from somewhere other than System32, are ours.
PROXY_NAMES = ("dxgi.dll", "version.dll", "winmm.dll", "dinput8.dll")


def already_ours(pid):
    """Modules in `pid` that look like a build of ours (an auto-loaded proxy counts)."""
    hits = []
    for name, path, size in loaded_modules(pid):
        low, lpath = name.lower(), path.lower()
        if low == "ue5dumper.dll":
            hits.append((name, path, size))
        elif low in PROXY_NAMES and "system32" not in lpath and "syswow64" not in lpath:
            hits.append((name, path, size))
    return hits


def inject(pid, dll_path):
    dll_path = os.path.abspath(dll_path)
    if not os.path.isfile(dll_path):
        raise InjectError(f"DLL not found: {dll_path}")

    h = k32.OpenProcess(PROCESS_ALL_ACCESS, False, pid)
    if not h:
        _win_fail(f"OpenProcess({pid}) -- if the target is elevated, so must this be")
    remote = None
    thread = None
    try:
        wow64 = w.BOOL(False)
        if k32.IsWow64Process(h, ctypes.byref(wow64)) and wow64.value:
            raise InjectError(f"PID {pid} is 32-bit; UE5Dumper.dll is x64-only")

        buf = (dll_path + "\0").encode("utf-16-le")
        remote = k32.VirtualAllocEx(h, None, len(buf), MEM_COMMIT_RESERVE, PAGE_READWRITE)
        if not remote:
            _win_fail("VirtualAllocEx")

        written = ctypes.c_size_t(0)
        if not k32.WriteProcessMemory(h, remote, buf, len(buf), ctypes.byref(written)):
            _win_fail("WriteProcessMemory")
        if written.value != len(buf):
            raise InjectError(f"short write: {written.value} of {len(buf)} bytes")

        kern = k32.GetModuleHandleW("kernel32.dll")
        if not kern:
            _win_fail("GetModuleHandleW(kernel32)")
        load = k32.GetProcAddress(kern, b"LoadLibraryW")
        if not load:
            _win_fail("GetProcAddress(LoadLibraryW)")

        thread = k32.CreateRemoteThread(h, None, 0, load, remote, 0, None)
        if not thread:
            _win_fail("CreateRemoteThread")

        rc = k32.WaitForSingleObject(thread, INFINITE_ISH_MS)
        if rc != WAIT_OBJECT_0:
            raise InjectError(f"remote LoadLibraryW did not finish (WaitForSingleObject={rc}); "
                              f"the DLL may still be initialising -- do NOT read this as success")

        code = w.DWORD(0)
        if not k32.GetExitCodeThread(thread, ctypes.byref(code)):
            _win_fail("GetExitCodeThread")
        # LoadLibraryW returns HMODULE; the thread exit code truncates it to 32 bits.
        # Zero means LoadLibraryW itself failed inside the target.
        if code.value == 0:
            raise InjectError("LoadLibraryW returned NULL in the target -- the DLL did NOT load "
                              "(bad bitness, missing dependency, or DllMain returned FALSE)")
        return code.value
    finally:
        if thread:
            k32.CloseHandle(thread)
        if remote:
            k32.VirtualFreeEx(h, remote, 0, MEM_RELEASE)
        k32.CloseHandle(h)


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--pid", type=int)
    ap.add_argument("--name")
    ap.add_argument("--dll", default=None,
                    help="default: dist/UE5Dumper.dll relative to the repo root")
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

    dll = a.dll
    if dll is None:
        root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
        dll = os.path.join(root, "dist", "UE5Dumper.dll")

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
    hmod = inject(pid, dll)
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
