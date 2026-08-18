"""Three-cell probe: why are OUR Start-menu .lnk files absent from AppsFolder?

    py lnk_probe.py create      # writes 3 probe shortcuts
    py lnk_probe.py clean       # deletes them again

The confound to avoid: AppsFolder keys legacy shortcuts by TARGET, so a second
shortcut to an already-listed exe is deduplicated and its absence proves
nothing. Every target below is therefore unique to its probe.

  ZZProbeC -> C:\\Windows\\System32\\where.exe        (C: drive, novel target)
  ZZProbeD -> D:\\Github\\UE5CEDumper\\dist\\UE5DumpUI.exe   (D: drive, the real subject
                                                       under a NEW name)
  ZZProbeD2 -> D:\\Github\\UE5CEDumper\\dist\\UE5Dumper.dll  (D:, non-exe control)

Reading the result:
  C appears, D does not      -> the DRIVE / path is the discriminator
  both C and D appear        -> our existing .lnk files are MALFORMED, rewrite them
  neither appears            -> AppsFolder takes no new .lnk at all right now

Shortcuts are written through IShellLinkW + IPersistFile, the same COM path
WScript.Shell uses, with WorkingDirectory and Description filled in so the file
is canonical rather than minimal.
"""
import ctypes
import ctypes.wintypes as w
import os
import pathlib
import sys

PROGRAMS = pathlib.Path(os.environ["APPDATA"]) / "Microsoft/Windows/Start Menu/Programs"

PROBES = {
    "ZZProbeC": r"C:\Windows\System32\where.exe",
    "ZZProbeD": r"D:\Github\UE5CEDumper\dist\UE5DumpUI.exe",
    "ZZProbeD2": r"D:\Github\UE5CEDumper\dist\UE5Dumper.dll",
}

CLSID_ShellLink = "{00021401-0000-0000-C000-000000000046}"
IID_IShellLinkW = "{000214F9-0000-0000-C000-000000000046}"
IID_IPersistFile = "{0000010B-0000-0000-C000-000000000046}"

ole32 = ctypes.OleDLL("ole32")


class GUID(ctypes.Structure):
    _fields_ = [("Data1", ctypes.c_ulong), ("Data2", ctypes.c_ushort),
                ("Data3", ctypes.c_ushort), ("Data4", ctypes.c_ubyte * 8)]


def guid(s):
    g = GUID()
    ole32.CLSIDFromString(ctypes.c_wchar_p(s), ctypes.byref(g))
    return g


def make_lnk(path, target, desc):
    """Create one .lnk via IShellLinkW, letting COM build the target IDList."""
    ole32.CoInitialize(None)
    ppv = ctypes.c_void_p()
    ole32.CoCreateInstance(ctypes.byref(guid(CLSID_ShellLink)), None, 1,
                           ctypes.byref(guid(IID_IShellLinkW)), ctypes.byref(ppv))
    vt = ctypes.cast(ppv, ctypes.POINTER(ctypes.POINTER(ctypes.c_void_p))).contents

    def call(slot, *args):
        proto = ctypes.WINFUNCTYPE(ctypes.c_long, ctypes.c_void_p,
                                   *[type(a) for a in args])
        return proto(vt[slot])(ppv, *args)

    # IShellLinkW vtable: 0-2 IUnknown, 3 GetPath, ... 20 SetPath,
    # 9 SetDescription, 11 SetWorkingDirectory, 17 SetIconLocation
    call(20, ctypes.c_wchar_p(target))                       # SetPath
    call(9, ctypes.c_wchar_p(desc))                          # SetDescription
    call(11, ctypes.c_wchar_p(str(pathlib.Path(target).parent)))  # SetWorkingDirectory
    call(17, ctypes.c_wchar_p(target), ctypes.c_int(0))      # SetIconLocation

    pf = ctypes.c_void_p()
    qi = ctypes.WINFUNCTYPE(ctypes.c_long, ctypes.c_void_p,
                            ctypes.c_void_p, ctypes.c_void_p)(vt[0])
    qi(ppv, ctypes.byref(guid(IID_IPersistFile)), ctypes.byref(pf))
    pvt = ctypes.cast(pf, ctypes.POINTER(ctypes.POINTER(ctypes.c_void_p))).contents
    save = ctypes.WINFUNCTYPE(ctypes.c_long, ctypes.c_void_p,
                              ctypes.c_wchar_p, ctypes.c_int)(pvt[6])
    hr = save(pf, str(path), 1)
    return hr


def create():
    for name, target in PROBES.items():
        if not pathlib.Path(target).exists():
            print(f"SKIP {name}: target missing {target}")
            continue
        dst = PROGRAMS / f"{name}.lnk"
        hr = make_lnk(dst, target, f"AppsFolder index probe {name}")
        size = dst.stat().st_size if dst.exists() else -1
        print(f"{name:10s} hr=0x{hr & 0xFFFFFFFF:08X} size={size:5d}  -> {target}")
    print("\nNow run:  Get-StartApps | Where-Object Name -match 'ZZProbe'")


def clean():
    for name in PROBES:
        dst = PROGRAMS / f"{name}.lnk"
        if dst.exists():
            dst.unlink()
            print(f"deleted {dst.name}")
        else:
            print(f"absent  {dst.name}")


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    cmd = sys.argv[1] if len(sys.argv) > 1 else ""
    if cmd == "create":
        create()
    elif cmd == "clean":
        clean()
    else:
        print(__doc__)
