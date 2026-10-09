r"""Replay Genau's Tier-0 (PE VERSIONINFO) decision offline, over any set of UE binaries.

Why this exists: version-detection rows in the register keep prescribing candidate
titles by ENGINE version, but which tier a title reaches is decided by its PE
version RESOURCE, not by what engine built it. G2 step 3 needs a title that FALLS
THROUGH Tier 0 to the memory-string needle -- and five candidates were listed that
structurally cannot, for the same reason Lushfoil could not. One offline sweep
answers "which titles can even reach the tier I want" before anything is launched.

Mirrors `Genau::DetectVersionFromPEResource` (dll/src/Genau.cpp) in order:
  1. VS_FIXEDFILEINFO.dwProductVersionMS   5.x -> 500+minor | 4.x -> 400+minor
  2. VS_FIXEDFILEINFO.dwFileVersionMS      same
  3. StringFileInfo ProductVersion/FileVersion containing '++UEn+Release-', or (rev 9) one that is
     the engine's own build string -- `engine_build_string_code`, a port of
     Grimoire::EngineBuildStringCode. Such a code came from a string, so below the 4.11 floor it
     cannot corroborate itself: only an agreeing CrashReportClient makes it refuse the scan.
  4. otherwise -> "unrecognised", and the caller falls back to the memory scan
⚠ Keep in step with that function; a divergence here silently mis-plans a row.

    py pe_version_probe.py <exe> [<exe> ...]
    py pe_version_probe.py --scan "D:\SteamLibrary\steamapps\common" [more roots]
    py pe_version_probe.py --selftest      # the port against the C++ helper test's cases
"""
import io, os, re, sys, ctypes, struct
from ctypes import wintypes

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

ver = ctypes.WinDLL("version", use_last_error=True)
ver.GetFileVersionInfoSizeW.argtypes = [wintypes.LPCWSTR, ctypes.POINTER(wintypes.DWORD)]
ver.GetFileVersionInfoW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, ctypes.c_void_p]
ver.VerQueryValueW.argtypes = [ctypes.c_void_p, wintypes.LPCWSTR,
                               ctypes.POINTER(ctypes.c_void_p), ctypes.POINTER(wintypes.UINT)]

FALLTHROUGH = "FALLS THROUGH -> memory-string Tier 1"

# ── Grimoire::EngineBuildStringCode, ported ([VER-410-GATE] rev 9) ───────────────────────────────
# The C++ reads each number as a greedy decimal run that fails above 0xFFFFFFFF; `[0-9]+` plus the
# U32 check is the same boundary, and fullmatch is its "ends exactly here".
U32 = 0xFFFFFFFF
_BRANCH_FIRST = re.compile(r"\+\+UE([0-9]+)\+Release-([0-9]+)\.([0-9]+)-CL-([0-9]+)")
_VERSION_FIRST = re.compile(r"([0-9]+)\.([0-9]+)\.([0-9]+)-([0-9]+)\+(.*)", re.S)
_SIMPLE_BRANCH = re.compile(r"UE([0-9]+)")
_FULL_BRANCHES = (re.compile(r"\+\+depot\+UE([0-9]+)-Releases\+([0-9]+)\.([0-9]+)"),
                  re.compile(r"\+\+UE([0-9]+)\+Release-([0-9]+)\.([0-9]+)"))


def ue_version_code(major, minor):
    """Grimoire::UeVersionCode."""
    if major == 5 and minor <= 9:
        return 500 + minor
    if major == 4 and minor <= 27:
        return 400 + minor
    return 0


def engine_build_string_code(s):
    """The engine version a UBT build string names, as a version code; 0 when it names none."""
    if s.startswith("++UE"):
        m = _BRANCH_FIRST.fullmatch(s)
        if not m:
            return 0
        bmaj, maj, mnr, cl = map(int, m.groups())
        if max(bmaj, maj, mnr, cl) > U32 or bmaj != maj:
            return 0
        return ue_version_code(maj, mnr)
    m = _VERSION_FIRST.fullmatch(s)
    if not m:
        return 0
    maj, mnr, patch, cl = map(int, m.groups()[:4])
    if max(maj, mnr, patch, cl) > U32:
        return 0
    code = ue_version_code(maj, mnr)
    if not code:
        return 0
    branch = m.group(5)
    b = _SIMPLE_BRANCH.fullmatch(branch)
    if b:
        return code if int(b.group(1)) == maj else 0
    for rx in _FULL_BRANCHES:
        f = rx.fullmatch(branch)
        if f:
            bm, rm, rn = map(int, f.groups())
            return code if bm == maj and rm == maj and rn == mnr else 0
    return 0


# Every case of dll_helpers_test's Test_EngineBuildStringCode, so the port is held to the same answers.
_SELFTEST = [
    ("4.10.2-0+++depot+UE4-Releases+4.10", 410), ("4.10.4-2872498+++depot+UE4-Releases+4.10", 410),
    ("4.9.2-0+++depot+UE4-Releases+4.9", 409), ("4.18.3-3832480+++UE4+Release-4.18", 418),
    ("4.11.0-0+UE4", 411), ("++UE4+Release-4.15-CL-0", 415), ("++UE4+Release-4.15-CL-3450819", 415),
    ("++UE4+Release-4.18-CL-3832480", 418), ("++UE4+Release-4.27-CL-18319896", 427),
    ("4.10.1", 0), ("4.10.3", 0), ("4.10.2.0", 0), ("1.0.10897.0", 0), ("4.5.0.0", 0), ("", 0),
    ("4.10.2-0+++depot+UE4-Releases+4.9", 0), ("4.10.2-0+++UE4+Release-4.11", 0), ("4.10.2-0+MyGame", 0),
    ("4.10.2-0+++depot+UE5-Releases+4.10", 0), ("4.10.2-0+++depot+UE4-Releases+5.10", 0), ("4.11.0-0+UE5", 0),
    ("++UE4+Release-5.4-CL-0", 0), ("4.10.2-0+++depot+UE4-Releases+4.1", 0),
    ("4.1.0-0+++depot+UE4-Releases+4.10", 0), ("4.100.0-0+UE4", 0), ("4.10.2-+UE4", 0), ("++UE4+Release-4.15", 0),
    ("4.10.2-0+++depot+UE4-Releases+4.10x", 0), ("4.11.0-0+UE4 ", 0), ("++UE4+Release-4.15-CL-0x", 0),
    ("++UE4+Release-4.15-CL-0 ", 0),
    # Port-only: the C++ decimal reader fails above 0xFFFFFFFF, so a 2^32 changelist is no build string.
    ("4.10.2-4294967296+UE4", 0), ("4.10.2-4294967295+UE4", 410),
]


def selftest():
    # Only from this file's own command line. Reached through an import, it is an importer's `--selftest` (the
    # CRC survey's) answered by the wrong selftest, and a pass would hide that its own never ran.
    if __name__ != "__main__":
        print("pe_version_probe: --selftest reached from an import, not this file's command line")
        return 1
    bad = 0
    for s, want in _SELFTEST:
        got = engine_build_string_code(s)
        ok = got == want
        bad += not ok
        print(f"  {'ok ' if ok else 'BAD'}  {s!r:48} -> {got} (want {want})")
    print(f"selftest: {len(_SELFTEST) - bad}/{len(_SELFTEST)} passed")
    return 1 if bad else 0

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
    if ver.VerQueryValueW(buf, r"\VarFileInfo\Translation", ctypes.byref(q), ctypes.byref(m)) and m.value >= 4:
        a = ctypes.cast(q, ctypes.POINTER(wintypes.WORD))
        for key in ("ProductVersion", "FileVersion"):
            r2, n2 = ctypes.c_void_p(), wintypes.UINT()
            sub = r"\StringFileInfo\%04x%04x\%s" % (a[0], a[1], key)
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
        code = engine_build_string_code(s)
        if code:
            return (f"Tier0 BUILD STRING {key}='{s}' -> {code}", prod, fver, strs)
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
    if argv[0] == "--selftest":
        sys.exit(selftest())
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

# Only when run: crc_authority_survey.py imports this module, and a main() at import read the survey's own
# argv -- harmless while no flag meant anything here, but `--selftest` would run this selftest and exit in place
# of the survey's.
if __name__ == "__main__":
    main()
