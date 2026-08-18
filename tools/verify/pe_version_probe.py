"""Replay Genau's Tier-0 (PE VERSIONINFO) decision offline, over any set of UE binaries.

Why this exists: version-detection rows in the register keep prescribing candidate
titles by ENGINE version, but which tier a title reaches is decided by its PE
version RESOURCE, not by what engine built it. G2 step 3 needs a title that FALLS
THROUGH Tier 0 to the memory-string needle -- and five candidates were listed that
structurally cannot, for the same reason Lushfoil could not. One offline sweep
answers "which titles can even reach the tier I want" before anything is launched.

Mirrors `Genau::DetectVersionFromPEResource` (dll/src/Genau.cpp) in order:
  1. VS_FIXEDFILEINFO.dwProductVersionMS   5.x -> 500+minor | 4.x -> 400+minor
  2. VS_FIXEDFILEINFO.dwFileVersionMS      same
  3. StringFileInfo ProductVersion/FileVersion containing '++UEn+Release-'
  4. otherwise -> "unrecognised", and the caller falls back to the memory scan
⚠ Keep in step with that function; a divergence here silently mis-plans a row.

    py pe_version_probe.py <exe> [<exe> ...]
    py pe_version_probe.py --scan "D:\SteamLibrary\steamapps\common" [more roots]
"""
import io, os, sys, ctypes, struct
from ctypes import wintypes

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

ver = ctypes.WinDLL("version", use_last_error=True)
ver.GetFileVersionInfoSizeW.argtypes = [wintypes.LPCWSTR, ctypes.POINTER(wintypes.DWORD)]
ver.GetFileVersionInfoW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, ctypes.c_void_p]
ver.VerQueryValueW.argtypes = [ctypes.c_void_p, wintypes.LPCWSTR,
                               ctypes.POINTER(ctypes.c_void_p), ctypes.POINTER(wintypes.UINT)]

FALLTHROUGH = "FALLS THROUGH -> memory-string Tier 1"

def probe(path):
    """-> (verdict, product, file, strings). Verdict contains FALLTHROUGH when usable."""
    dummy = wintypes.DWORD(0)
    size = ver.GetFileVersionInfoSizeW(path, ctypes.byref(dummy))
    if not size:
        return (f"no resource -- {FALLTHROUGH}", None, None, {})
    buf = ctypes.create_string_buffer(size)
    if not ver.GetFileVersionInfoW(path, 0, size, buf):
        return ("resource UNREADABLE", None, None, {})
    p, n = ctypes.c_void_p(), wintypes.UINT()
    if not (ver.VerQueryValueW(buf, "\\" , ctypes.byref(p), ctypes.byref(n)) and n.value >= 52):
        return (f"no VS_FIXEDFILEINFO -- {FALLTHROUGH}", None, None, {})
    raw = ctypes.string_at(p, n.value)
    fms, fls = struct.unpack_from("<II", raw, 8)
    pms, pls = struct.unpack_from("<II", raw, 16)
    pmaj, pmin = pms >> 16, pms & 0xFFFF
    fmaj, fmin = fms >> 16, fms & 0xFFFF
    prod = f"{pmaj}.{pmin}.{pls >> 16}.{pls & 0xFFFF}"
    fver = f"{fmaj}.{fmin}.{fls >> 16}.{fls & 0xFFFF}"

    strs = {}
    q, m = ctypes.c_void_p(), wintypes.UINT()
    if ver.VerQueryValueW(buf, "\VarFileInfo\Translation", ctypes.byref(q), ctypes.byref(m)) and m.value >= 4:
        a = ctypes.cast(q, ctypes.POINTER(wintypes.WORD))
        for key in ("ProductVersion", "FileVersion"):
            r2, n2 = ctypes.c_void_p(), wintypes.UINT()
            sub = "\StringFileInfo\%04x%04x\%s" % (a[0], a[1], key)
            if ver.VerQueryValueW(buf, sub, ctypes.byref(r2), ctypes.byref(n2)) and n2.value:
                strs[key] = ctypes.wstring_at(r2, n2.value).rstrip("\x00")

    if pmaj == 5 and pmin <= 9:  return (f"Tier0 ProductVersion -> {500 + pmin}", prod, fver, strs)
    if pmaj == 4 and pmin <= 27: return (f"Tier0 ProductVersion -> {400 + pmin}", prod, fver, strs)
    if fmaj == 5 and fmin <= 9:  return (f"Tier0 FileVersion -> {500 + fmin}", prod, fver, strs)
    if fmaj == 4 and fmin <= 27: return (f"Tier0 FileVersion -> {400 + fmin}", prod, fver, strs)
    for key, s in strs.items():
        for pre in ("++UE5+Release-", "++UE4+Release-"):
            if pre in s:
                return (f"Tier0 STRING {key}='{s}'", prod, fver, strs)
    return (f"unrecognised -- {FALLTHROUGH}", prod, fver, strs)

def collect(roots):
    out = []
    for root in roots:
        if os.path.isfile(root):
            out.append(root); continue
        for dirpath, _, files in os.walk(root):
            for f in files:
                lf = f.lower()
                if lf.endswith(".exe") and ("shipping" in lf or "debuggame" in lf):
                    out.append(os.path.join(dirpath, f))
    return sorted(set(out))

def main():
    argv = sys.argv[1:]
    if not argv:
        print(__doc__); return
    targets = collect(argv[1:] if argv[0] == "--scan" else argv)
    usable = []
    for t in targets:
        verdict, prod, fver, strs = probe(t)
        label = os.path.basename(t)
        flag = "  <== usable for a Tier-1 row" if FALLTHROUGH in verdict else ""
        print(f"{label[:44]:45} prod={str(prod):11} file={str(fver):11} {verdict}{flag}")
        if FALLTHROUGH in verdict:
            usable.append((t, strs))
    print(f"\n{len(targets)} scanned, {len(usable)} fall through Tier 0")
    for t, strs in usable:
        print(f"  {t}\n      strings={strs}")

main()
