#!/usr/bin/env python3
r"""B29 — the predicate that separates OUR proxy from a third-party wrapper.

    py tools/verify/b29_product_name.py [extra.dll ...]

`Methode.cpp`'s `IsOurModule` decides, for a module whose FILENAME looks like one of
ours (dxgi/version/winmm/dinput8/UE5Dumper.dll), whether it really is: it reads the
PE VERSIONINFO `ProductName` and requires it to equal "UE5CEDumper", enumerating
`\VarFileInfo\Translation` rather than assuming the 040904B0 language block. If the
answer is no, the CE plugin logs

    CEPlugin: '<name>' is loaded but is not ours (path=<path>) — not a UE5CEDumper proxy

and keeps going, instead of the pre-fix behaviour of reporting "already loaded, no
injection needed" and never producing a pipe.

This rig applies THAT EXACT RULE offline, via the same Win32 API the DLL calls, so the
discriminator can be checked without installing a CE plugin. It cannot prove the CE
plugin logs the line — only that the decision underneath it is correct — and it says
so rather than overclaiming.

⚠ The check is only meaningful with BOTH controls present: a third-party wrapper that
must come back NOT-ours, and one of our own proxies that must come back OURS. A rule
that answers "not ours" for everything would look like a pass on the wrapper alone.
The run FAILS unless at least one of each is seen.
"""
from __future__ import annotations

import ctypes
import ctypes.wintypes as wt
import pathlib
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ver = ctypes.WinDLL("version.dll")
ver.GetFileVersionInfoSizeW.argtypes = [wt.LPCWSTR, ctypes.POINTER(wt.DWORD)]
ver.GetFileVersionInfoSizeW.restype = wt.DWORD
ver.GetFileVersionInfoW.argtypes = [wt.LPCWSTR, wt.DWORD, wt.DWORD, ctypes.c_void_p]
ver.GetFileVersionInfoW.restype = wt.BOOL
ver.VerQueryValueW.argtypes = [ctypes.c_void_p, wt.LPCWSTR,
                               ctypes.POINTER(ctypes.c_void_p), ctypes.POINTER(wt.UINT)]
ver.VerQueryValueW.restype = wt.BOOL


def product_name(path: str) -> str | None:
    """Exactly what IsOurModule reads: ProductName from each Translation block."""
    dummy = wt.DWORD(0)
    size = ver.GetFileVersionInfoSizeW(path, ctypes.byref(dummy))
    if size == 0:
        return None                      # no VERSIONINFO at all -> not one of ours
    buf = ctypes.create_string_buffer(size)
    if not ver.GetFileVersionInfoW(path, 0, size, buf):
        return None
    block = ctypes.c_void_p()
    blen = wt.UINT(0)
    if not ver.VerQueryValueW(buf, r"\VarFileInfo\Translation",
                              ctypes.byref(block), ctypes.byref(blen)) or not block:
        return None
    count = blen.value // 4
    words = (wt.WORD * (count * 2)).from_address(block.value)
    for i in range(count):
        lang, cp = words[i * 2], words[i * 2 + 1]
        val = ctypes.c_void_p()
        vlen = wt.UINT(0)
        sub = "\\StringFileInfo\\%04x%04x\\ProductName" % (lang, cp)
        if ver.VerQueryValueW(buf, sub, ctypes.byref(val), ctypes.byref(vlen)) and val:
            s = ctypes.wstring_at(val.value, vlen.value).rstrip("\x00")
            if s:
                return s
    return None


REPO = pathlib.Path(__file__).resolve().parents[2]
CASES = [
    # (label, path, expected_is_ours)
    ("OUR proxy (dist)", REPO / "dist" / "proxy" / "dxgi.dll", True),
    ("OUR injected DLL", REPO / "dist" / "UE5Dumper.dll", True),
    ("ReShade wrapper",
     pathlib.Path(r"D:\SteamLibrary\steamapps\common\SEED BATTLE DESTINY REMASTERED"
                  r"\Game_SBDR\Binaries\Win64\dxgi.dll"), False),
    ("Windows' real dxgi", pathlib.Path(r"C:\Windows\System32\dxgi.dll"), False),
]


def main() -> int:
    for extra in sys.argv[1:]:
        CASES.append(("(cli) " + pathlib.Path(extra).name, pathlib.Path(extra), None))

    print("%-22s %-11s %-30s %s" % ("case", "IsOurModule", "ProductName", "path"))
    print("-" * 110)
    saw_ours = saw_foreign = False
    bad = 0
    for label, p, expect in CASES:
        if not p.exists():
            print("%-22s %-11s %-30s %s" % (label, "SKIP", "(file not found)", p))
            continue
        pn = product_name(str(p))
        ours = (pn is not None and pn.casefold() == "ue5cedumper")
        saw_ours |= ours
        saw_foreign |= not ours
        flag = ""
        if expect is not None and ours != expect:
            flag, bad = "   <-- WRONG", bad + 1
        print("%-22s %-11s %-30s %s%s"
              % (label, str(ours), (pn if pn is not None else "(no VERSIONINFO)")[:30], p, flag))

    print()
    assert saw_ours, ("no file came back as OURS — a rule that says 'not ours' for "
                      "everything would pass the wrapper case vacuously")
    assert saw_foreign, "no file came back as NOT ours — the wrapper control is missing"
    if bad:
        print("FAIL: %d case(s) decided the wrong way" % bad)
        return 1
    print("PASS: the discriminator separates our binaries from third-party wrappers.")
    print("NOTE: this checks the DECISION only. That the CE plugin actually LOGS")
    print("      \"is loaded but is not ours\" still needs the plugin installed in CE.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
