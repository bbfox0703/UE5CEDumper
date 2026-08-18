#!/usr/bin/env python3
r"""Create, remove or inspect a Start Menu Startup shortcut for UE5DumpUI.exe.

Python twin of startup-shortcut.ps1 -- same CLI verbs, same resolution order, same
refusal rules, same exit codes. Two reasons it exists rather than one script:

  * Bitdefender's Advanced Threat Defense quarantined the .ps1 (and build.ps1, and two
    other tools) the first time it ran. The detection was BEHAVIOURAL and fired on the
    process chain -- an unsigned parent spawning pwsh spawning powershell, which then
    wrote a .lnk into the Startup folder. That is a textbook persistence shape and an AV
    is right to look at it. Nothing here tries to look like anything else; a second
    implementation simply gives a machine where the PowerShell host is the problem a way
    to do the same job through a different interpreter.
  * Ad-hoc tooling in this repo is Python by preference anyway (build.ps1 stays the build
    entry point). Every tools/check_*.py is stdlib-only; so is this.

Stdlib only: the .lnk is authored through the documented Shell API (IShellLinkW +
IPersistFile) via ctypes, which is exactly what WScript.Shell wraps -- no pywin32, and no
hand-rolled MS-SHLLINK binary writer, which would have to reimplement a parser to read
back shortcuts other tools created.

CURRENT USER ONLY, by design. The Startup folder comes from SHGetKnownFolderPath
(FOLDERID_Startup), never from %APPDATA% + a literal path: that path is localised on
non-English Windows and relocatable by folder redirection or policy. The all-users
Startup folder is deliberately unsupported -- it needs elevation, and a debugging tool
that silently starts for every account on the machine is not a default anyone asked for.

The target executable is resolved (unless --exe is given) from, in order:
    <script dir>\UE5DumpUI.exe, <script dir>\..\dist\UE5DumpUI.exe,
    <script dir>\dist\UE5DumpUI.exe, <cwd>\UE5DumpUI.exe
The first is the shipped case: build.ps1 copies this next to the exe, so a user who
unzips a release and runs it there needs no arguments. The rest let it work straight out
of the repo, and match how inject-ue.ps1 finds UE5Dumper.dll.

SAFETY, and it is not decoration -- the Startup folder holds other programs' shortcuts:
  * install refuses to overwrite a shortcut of that name pointing somewhere else
  * remove refuses to delete one whose target is not UE5DumpUI.exe
  * both print what they found and both take --force
  * every write is read back, because IPersistFile::Save reports success by not failing,
    which is not the same as having written what you asked for -- and a wrong Startup
    shortcut gives no feedback at all until the next sign-in

Usage:
  py scripts/startup_shortcut.py                 # STATUS (default) -- read-only
  py scripts/startup_shortcut.py install
  py scripts/startup_shortcut.py install --minimized
  py scripts/startup_shortcut.py remove
  py scripts/startup_shortcut.py --exe D:\path\UE5DumpUI.exe install
  py scripts/startup_shortcut.py --startup-dir <dir> install    # see below

--startup-dir points the whole thing at a different folder. It exists so the write path
can be exercised in a temp directory during development: testing 'install' against the
REAL Startup folder is the one action here that is genuinely a persistence change to the
machine, and it should not be something a test run does casually.

Exit codes: 0 = success / installed, 1 = error, 2 = not installed (status or remove),
3 = installed but the target is missing or foreign (status). 2 and 3 are distinct so a
wrapper can tell "never set up" from "set up and since broken" -- the second is what a
moved or re-extracted dist\ looks like, and it is the failure users actually hit.
"""
import argparse
import ctypes
import os
import sys
from ctypes import POINTER, byref, c_void_p, c_int, c_wchar_p

EXE_LEAF = "UE5DumpUI.exe"
DESCRIPTION = "UE5CEDumper - Unreal Engine object/offset dumper UI"

SW_SHOWNORMAL = 1
SW_SHOWMINNOACTIVE = 7

if os.name == "nt":
    from ctypes import wintypes
    ole32 = ctypes.OleDLL("ole32")
    shell32 = ctypes.OleDLL("shell32")


# ---- COM plumbing -----------------------------------------------------------

class GUID(ctypes.Structure):
    _fields_ = [("Data1", ctypes.c_uint32),
                ("Data2", ctypes.c_uint16),
                ("Data3", ctypes.c_uint16),
                ("Data4", ctypes.c_byte * 8)]

    def __init__(self, text):
        super().__init__()
        ole32.CLSIDFromString(ctypes.c_wchar_p(text), byref(self))


CLSID_ShellLink  = "{00021401-0000-0000-C000-000000000046}"
IID_IShellLinkW  = "{000214F9-0000-0000-C000-000000000046}"
IID_IPersistFile = "{0000010B-0000-0000-C000-000000000046}"
FOLDERID_Startup = "{B97D20BB-F46A-4C97-BA10-5E3608430854}"

CLSCTX_INPROC_SERVER = 1
STGM_READ = 0
SLGP_RAWPATH = 4          # return the stored path, do not expand environment variables
MAX_PATH = 260

# IShellLinkW vtable slots (shobjidl_core.h order; 0-2 are IUnknown).
SL_GetPath, SL_SetDescription = 3, 7
SL_SetWorkingDirectory, SL_GetWorkingDirectory = 9, 8
SL_GetArguments, SL_SetArguments = 10, 11
SL_GetShowCmd, SL_SetShowCmd = 14, 15
SL_SetIconLocation, SL_SetPath = 17, 20
# IPersistFile vtable slots.
PF_Load, PF_Save = 5, 6
IUnknown_Release = 2


def _method(ptr, index, *argtypes):
    """Bind vtable slot `index` on the COM interface at `ptr`.

    restype=HRESULT makes ctypes raise OSError on a failed call, so every Shell API
    failure surfaces as an exception with the real HRESULT instead of a silent no-op.
    """
    vtbl = ctypes.cast(ptr, POINTER(POINTER(c_void_p)))[0]
    proto = ctypes.WINFUNCTYPE(ctypes.HRESULT, c_void_p, *argtypes)
    return proto(vtbl[index])


def _release(ptr):
    if ptr:
        vtbl = ctypes.cast(ptr, POINTER(POINTER(c_void_p)))[0]
        ctypes.WINFUNCTYPE(ctypes.c_ulong, c_void_p)(vtbl[IUnknown_Release])(ptr)


class ShellLink:
    """RAII wrapper over one IShellLinkW + its IPersistFile."""

    def __init__(self):
        self.psl = c_void_p()
        self.ppf = c_void_p()
        ole32.CoCreateInstance(byref(GUID(CLSID_ShellLink)), None,
                               CLSCTX_INPROC_SERVER, byref(GUID(IID_IShellLinkW)),
                               byref(self.psl))
        _method(self.psl, 0, POINTER(GUID), POINTER(c_void_p))(
            self.psl, byref(GUID(IID_IPersistFile)), byref(self.ppf))

    def close(self):
        _release(self.ppf); self.ppf = c_void_p()
        _release(self.psl); self.psl = c_void_p()

    def __enter__(self):
        return self

    def __exit__(self, *exc):
        self.close()

    # -- read --
    def load(self, path):
        _method(self.ppf, PF_Load, c_wchar_p, ctypes.c_uint32)(self.ppf, path, STGM_READ)

    def _str(self, slot, size=MAX_PATH * 4):
        buf = ctypes.create_unicode_buffer(size)
        if slot == SL_GetPath:
            # GetPath takes an extra WIN32_FIND_DATAW* (NULL is allowed) and flags.
            _method(self.psl, slot, c_wchar_p, c_int, c_void_p, ctypes.c_uint32)(
                self.psl, buf, size, None, SLGP_RAWPATH)
        else:
            _method(self.psl, slot, c_wchar_p, c_int)(self.psl, buf, size)
        return buf.value

    @property
    def target(self):
        return self._str(SL_GetPath)

    @property
    def arguments(self):
        return self._str(SL_GetArguments)

    @property
    def working_dir(self):
        return self._str(SL_GetWorkingDirectory)

    @property
    def show_cmd(self):
        v = c_int()
        _method(self.psl, SL_GetShowCmd, POINTER(c_int))(self.psl, byref(v))
        return v.value

    # -- write --
    def write(self, path, target, arguments, working_dir, show_cmd):
        _method(self.psl, SL_SetPath, c_wchar_p)(self.psl, target)
        _method(self.psl, SL_SetArguments, c_wchar_p)(self.psl, arguments or "")
        # Without this the shortcut inherits the Startup folder as its working directory,
        # so anything the app resolves relative to itself (the .CT, UE5Dumper.dll, the
        # bundled native libs) is looked for in the wrong place.
        _method(self.psl, SL_SetWorkingDirectory, c_wchar_p)(self.psl, working_dir)
        _method(self.psl, SL_SetDescription, c_wchar_p)(self.psl, DESCRIPTION)
        _method(self.psl, SL_SetIconLocation, c_wchar_p, c_int)(self.psl, target, 0)
        _method(self.psl, SL_SetShowCmd, c_int)(self.psl, show_cmd)
        _method(self.ppf, PF_Save, c_wchar_p, wintypes.BOOL)(self.ppf, path, True)


def read_shortcut(path):
    """Load an existing .lnk and return its fields, or None. Never writes."""
    if not os.path.isfile(path):
        return None
    with ShellLink() as sl:
        sl.load(path)
        return {"target": sl.target, "arguments": sl.arguments,
                "working_dir": sl.working_dir, "show_cmd": sl.show_cmd}


def write_shortcut(path, target, arguments, show_cmd):
    with ShellLink() as sl:
        sl.write(path, target, arguments, os.path.dirname(target), show_cmd)


# ---- paths ------------------------------------------------------------------

def startup_dir(override=None):
    if override:
        if not os.path.isdir(override):
            raise RuntimeError("--startup-dir does not exist: %s" % override)
        return os.path.abspath(override)
    out = c_void_p()
    shell32.SHGetKnownFolderPath(byref(GUID(FOLDERID_Startup)), 0, None, byref(out))
    try:
        path = ctypes.wstring_at(out)
    finally:
        ole32.CoTaskMemFree(out)
    if not path or not os.path.isdir(path):
        raise RuntimeError("Windows did not report a usable Startup folder (got %r)" % path)
    return path


def resolve_exe(override=None):
    if override:
        if os.path.isfile(override):
            return os.path.abspath(override)
        raise RuntimeError("Executable not found: %s" % override)
    here = os.path.dirname(os.path.abspath(__file__))
    candidates = [os.path.join(here, EXE_LEAF),
                  os.path.join(here, "..", "dist", EXE_LEAF),
                  os.path.join(here, "dist", EXE_LEAF),
                  os.path.join(os.getcwd(), EXE_LEAF)]
    for c in candidates:
        if os.path.isfile(c):
            return os.path.abspath(c)
    raise RuntimeError("%s not found near the script. Looked in:\n    %s\n  Pass --exe "
                       "<path>, or run this from the folder the release was unzipped into."
                       % (EXE_LEAF, "\n    ".join(os.path.normpath(c) for c in candidates)))


def shortcut_path(name, override=None):
    leaf = name if name.lower().endswith(".lnk") else name + ".lnk"
    bad = set('<>:"/\\|?*') | {chr(c) for c in range(32)}
    if any(ch in bad for ch in leaf):
        raise RuntimeError("Invalid character in --name: %r" % name)
    return os.path.join(startup_dir(override), leaf)


def is_our_target(target):
    return bool(target) and os.path.basename(target).lower() == EXE_LEAF.lower()


# ---- output -----------------------------------------------------------------

def ok(m):   print("  [ok]   %s" % m)
def info(m): print("  [info] %s" % m)
def warn(m): print("  [warn] %s" % m)
def fail(m): print("  [fail] %s" % m)


# ---- actions ----------------------------------------------------------------

def do_status(args):
    link = shortcut_path(args.name, args.startup_dir)
    info("Startup folder: %s" % startup_dir(args.startup_dir))
    info("Shortcut:       %s" % link)

    existing = read_shortcut(link)
    if existing is None:
        info("Not installed.")
        print("\n  Install with:  py startup_shortcut.py install")
        return 2

    ok("Installed.")
    info("Target:         %s" % existing["target"])
    if existing["arguments"]:
        info("Arguments:      %s" % existing["arguments"])
    if existing["working_dir"]:
        info("Working dir:    %s" % existing["working_dir"])
    info("Window:         %s" % ("minimized" if existing["show_cmd"] == SW_SHOWMINNOACTIVE
                                 else "normal"))

    if not is_our_target(existing["target"]):
        warn("That target is not %s - this .lnk was not created by this script." % EXE_LEAF)
        return 3
    if not os.path.isfile(existing["target"]):
        # The common real-world break: the release folder was moved, renamed or
        # re-extracted elsewhere. Windows keeps the dead shortcut, the UI just stops
        # appearing at sign-in, and nothing anywhere says why.
        warn("Target no longer exists - the shortcut is dead. Re-run 'install' from the "
             "current folder.")
        return 3
    ok("Target exists.")
    return 0


def do_install(args):
    exe = resolve_exe(args.exe)
    link = shortcut_path(args.name, args.startup_dir)
    info("Startup folder: %s" % startup_dir(args.startup_dir))
    info("Shortcut:       %s" % link)
    info("Target:         %s" % exe)

    existing = read_shortcut(link)
    if existing is not None:
        if existing["target"].lower() == exe.lower():
            info("A shortcut to this exact target already exists - rewriting it.")
        elif args.force:
            warn("Overwriting a shortcut that pointed at: %s" % existing["target"])
        else:
            fail("'%s' already exists and points at:" % os.path.basename(link))
            fail("    %s" % existing["target"])
            fail("Refusing to overwrite it. Re-run with --force, or pick another --name.")
            return 1

    show = SW_SHOWMINNOACTIVE if args.minimized else SW_SHOWNORMAL
    write_shortcut(link, exe, args.args, show)

    written = read_shortcut(link)
    if written is None:
        fail("Save() reported success but no shortcut is there: %s" % link)
        return 1
    if written["target"].lower() != exe.lower():
        fail("Shortcut was written with the wrong target: %s" % written["target"])
        return 1
    ok("Installed - UE5DumpUI will start when %s signs in."
       % (os.environ.get("USERNAME") or "this user"))
    if args.minimized:
        info("It will start minimized.")
    print("\n  Remove with:  py startup_shortcut.py remove")
    return 0


def do_remove(args):
    link = shortcut_path(args.name, args.startup_dir)
    info("Startup folder: %s" % startup_dir(args.startup_dir))
    info("Shortcut:       %s" % link)

    existing = read_shortcut(link)
    if existing is None:
        info("Not installed - nothing to remove.")
        return 2

    # Look before deleting. --name is user-supplied and the Startup folder holds other
    # programs' shortcuts; deleting one we did not create would be silent, and for
    # anything else that lives there, not obviously recoverable.
    if not is_our_target(existing["target"]):
        if not args.force:
            fail("That shortcut points at:")
            fail("    %s" % existing["target"])
            fail("which is not %s, so this script did not create it. Refusing to delete."
                 % EXE_LEAF)
            fail("Re-run with --force if you are sure.")
            return 1
        warn("Deleting a shortcut this script did not create: %s" % existing["target"])

    os.remove(link)
    if os.path.exists(link):
        fail("Delete reported success but the shortcut is still there: %s" % link)
        return 1
    ok("Removed - UE5DumpUI will no longer start at sign-in.")
    return 0


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("action", nargs="?", default="status",
                    choices=["status", "install", "remove"],
                    help="status (default, read-only), install, or remove")
    ap.add_argument("--exe", help="path to %s (default: searched near the script)" % EXE_LEAF)
    ap.add_argument("--name", default="UE5CEDumper",
                    help="shortcut base name (default: UE5CEDumper); .lnk optional")
    ap.add_argument("--args", default="", help="arguments baked into the shortcut")
    ap.add_argument("--minimized", action="store_true", help="start minimized")
    ap.add_argument("--force", action="store_true",
                    help="overwrite / delete even when the shortcut points elsewhere")
    ap.add_argument("--startup-dir",
                    help="use this folder instead of the real Startup folder (testing)")
    args = ap.parse_args()

    if os.name != "nt":
        print("startup_shortcut: Windows only (this authors a Windows .lnk).")
        return 1

    print("\nUE5CEDumper - Startup shortcut (%s)\n" % args.action)
    ole32.CoInitialize(None)
    try:
        code = {"install": do_install, "remove": do_remove}.get(args.action, do_status)(args)
    except OSError as e:
        # Shell API failures arrive here as OSError carrying the HRESULT, which is the
        # only useful thing to show: winerror alone is what distinguishes "access denied
        # by policy" from "the path is bad".
        fail("%s" % e)
        code = 1
    except RuntimeError as e:
        fail("%s" % e)
        code = 1
    finally:
        ole32.CoUninitialize()
    print()
    return code


if __name__ == "__main__":
    sys.exit(main())
