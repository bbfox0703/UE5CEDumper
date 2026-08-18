"""Which titles can produce a `DetectVersion: Tier 1 (ascii|utf16)` line -- decided offline.

Two independent facts per binary, because BOTH are required and either alone misleads:
  1. Does it fall THROUGH Tier 0? (replays Genau::DetectVersionFromPEResource's order)
  2. Does the `++UEn+Release-N.N` needle actually EXIST in the image, and in which encoding?
A title that falls through but has no needle detects nothing (Elliot, Echoes of Aincrad);
a title with a needle that resolves at Tier 0 never looks (every stock UE5 title).

⚠ Walks every `Binaries\Win64` directory rather than globbing a fixed depth -- a
fixed-depth glob silently skipped installed titles and an absence claim built on a
silent skip is worthless.
"""
import io, os, re, sys, ctypes, struct
from ctypes import wintypes
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

ver = ctypes.WinDLL("version", use_last_error=True)
ver.GetFileVersionInfoSizeW.argtypes = [wintypes.LPCWSTR, ctypes.POINTER(wintypes.DWORD)]
ver.GetFileVersionInfoW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, ctypes.c_void_p]
ver.VerQueryValueW.argtypes = [ctypes.c_void_p, wintypes.LPCWSTR,
                               ctypes.POINTER(ctypes.c_void_p), ctypes.POINTER(wintypes.UINT)]

SKIP = ("crashreportclient", "unrealcefsubprocess", "crashpad_handler", "epicwebhelper")
ASCII = re.compile(rb"\+\+UE([45])\+Release-(\d+)\.(\d+)")
UTF16 = re.compile(rb"(?:\+\x00){2}U\x00E\x00([45])\x00\+\x00R\x00e\x00l\x00e\x00a\x00s\x00e\x00-\x00"
                   rb"((?:\d\x00)+)\.\x00((?:\d\x00)+)")

def tier0(path):
    d = wintypes.DWORD(0)
    size = ver.GetFileVersionInfoSizeW(path, ctypes.byref(d))
    if not size: return "FALLS THROUGH (no resource)", "-"
    buf = ctypes.create_string_buffer(size)
    if not ver.GetFileVersionInfoW(path, 0, size, buf): return "resource unreadable", "-"
    p, n = ctypes.c_void_p(), wintypes.UINT()
    if not (ver.VerQueryValueW(buf, "\\", ctypes.byref(p), ctypes.byref(n)) and n.value >= 52):
        return "FALLS THROUGH (no fixedinfo)", "-"
    raw = ctypes.string_at(p, n.value)
    fms, _ = struct.unpack_from("<II", raw, 8); pms, pls = struct.unpack_from("<II", raw, 16)
    pmaj, pmin = pms >> 16, pms & 0xFFFF; fmaj, fmin = fms >> 16, fms & 0xFFFF
    prod = f"{pmaj}.{pmin}.{pls >> 16}.{pls & 0xFFFF}"
    if pmaj == 5 and pmin <= 9:  return f"Tier0 -> {500 + pmin}", prod
    if pmaj == 4 and pmin <= 27: return f"Tier0 -> {400 + pmin}", prod
    if fmaj == 5 and fmin <= 9:  return f"Tier0 -> {500 + fmin} (File)", prod
    if fmaj == 4 and fmin <= 27: return f"Tier0 -> {400 + fmin} (File)", prod
    q, m = ctypes.c_void_p(), wintypes.UINT()
    if ver.VerQueryValueW(buf, "\VarFileInfo\Translation", ctypes.byref(q), ctypes.byref(m)) and m.value >= 4:
        a = ctypes.cast(q, ctypes.POINTER(wintypes.WORD))
        for key in ("ProductVersion", "FileVersion"):
            r2, n2 = ctypes.c_void_p(), wintypes.UINT()
            if ver.VerQueryValueW(buf, "\StringFileInfo\%04x%04x\%s" % (a[0], a[1], key),
                                  ctypes.byref(r2), ctypes.byref(n2)) and n2.value:
                s = ctypes.wstring_at(r2, n2.value).rstrip("\x00")
                if "++UE5+Release-" in s or "++UE4+Release-" in s:
                    return f"Tier0 STRING '{s}'", prod
    return "FALLS THROUGH (unrecognised)", prod

def needles(path):
    data = open(path, "rb").read()
    out = {}
    for m in ASCII.finditer(data):
        t = f"++UE{m.group(1).decode()}+Release-{m.group(2).decode()}.{m.group(3).decode()}"
        out[("ascii", t)] = out.get(("ascii", t), 0) + 1
    for m in UTF16.finditer(data):
        t = ("++UE%s+Release-%s.%s" % (m.group(1).decode(),
             m.group(2).replace(b"\x00", b"").decode(), m.group(3).replace(b"\x00", b"").decode()))
        out[("utf16", t)] = out.get(("utf16", t), 0) + 1
    return out

def main():
    roots = sys.argv[1:] or [r"D:\SteamLibrary\steamapps\common",
                             r"C:\Program Files (x86)\Steam\steamapps\common"]
    exes = []
    for root in roots:
        for dirpath, _, files in os.walk(root):
            if not dirpath.lower().endswith(os.path.join("binaries", "win64")):
                continue
            for f in files:
                if f.lower().endswith(".exe") and not any(s in f.lower() for s in SKIP):
                    exes.append(os.path.join(dirpath, f))
    exes = sorted(set(exes))
    hosts = []
    for e in exes:
        v, prod = tier0(e)
        nd = needles(e)
        falls = "FALLS THROUGH" in v
        encs = sorted({k[0] for k in nd})
        tags = sorted({k[1] for k in nd})
        verdict = ("TIER-1 HOST: " + "/".join(encs) if (falls and nd)
                   else "falls through, NO NEEDLE" if falls
                   else "exits at Tier 0")
        print(f"{os.path.basename(e)[:42]:43} prod={prod:10} {v:32} needle={','.join(encs) or '-':12} {tags}")
        if falls and nd:
            hosts.append((e, encs, tags))
    print(f"\n{len(exes)} binaries. {len(hosts)} can actually produce a Tier-1 line:")
    for e, encs, tags in hosts:
        print(f"  {'/'.join(encs):12} {tags}  {e}")

main()
