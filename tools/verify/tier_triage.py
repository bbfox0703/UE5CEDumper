"""Classify installed UE titles by WHICH version-detection branch they will take.

Answers one question the register needs and no existing tool answers:
  which installed title has an UNRECOGNISED PE VERSIONINFO *and* a findable
  ++UE[45]+Release- tag, so that Genau::DetectVersion falls past the PE fast
  path and Tier 1 of the MEMORY string scan actually fires?

That title is the only sample that can serve:
  * G8/G9 step 3  -- "a Tier 1 game is untouched" needs a Tier 1 line to exist.
  * G2   step 2   -- a clean speed measurement: CountPreUE4Markers only runs in
                     the terminal all-failed branch, so a Tier 1 hit keeps that
                     second whole-image sweep OUT of the measured window.

The PE half calls the SAME version.dll APIs as DetectVersionFromPEResource
(GetFileVersionInfoSizeW / GetFileVersionInfoW / VerQueryValueW) rather than
reimplementing a resource parser, so the verdict cannot drift from the code
under test. The tag half reuses tools/pe/ue_version.py's own regexes.
"""
import ctypes
import ctypes.wintypes as w
import pathlib
import re
import sys

# --- the tag needles, copied from tools/pe/ue_version.py ---------------------
RE_UTF16 = re.compile(
    rb"\+\x00\+\x00U\x00E\x00[45]\x00\+\x00R\x00e\x00l\x00e\x00a\x00s\x00e\x00-\x00"
    rb"((?:[0-9]\x00|\.\x00)+)")
RE_ASCII = re.compile(rb"\+\+UE[45]\+Release-([0-9][0-9.]*)")

ver = ctypes.WinDLL("version.dll")
ver.GetFileVersionInfoSizeW.argtypes = [w.LPCWSTR, ctypes.POINTER(w.DWORD)]
ver.GetFileVersionInfoSizeW.restype = w.DWORD
ver.GetFileVersionInfoW.argtypes = [w.LPCWSTR, w.DWORD, w.DWORD, ctypes.c_void_p]
ver.GetFileVersionInfoW.restype = w.BOOL
ver.VerQueryValueW.argtypes = [ctypes.c_void_p, w.LPCWSTR,
                               ctypes.POINTER(ctypes.c_void_p),
                               ctypes.POINTER(ctypes.c_uint)]
ver.VerQueryValueW.restype = w.BOOL


class FIXEDFILEINFO(ctypes.Structure):
    _fields_ = [("dwSignature", w.DWORD), ("dwStrucVersion", w.DWORD),
                ("dwFileVersionMS", w.DWORD), ("dwFileVersionLS", w.DWORD),
                ("dwProductVersionMS", w.DWORD), ("dwProductVersionLS", w.DWORD),
                ("dwFileFlagsMask", w.DWORD), ("dwFileFlags", w.DWORD),
                ("dwFileOS", w.DWORD), ("dwFileType", w.DWORD),
                ("dwFileSubtype", w.DWORD), ("dwFileDateMS", w.DWORD),
                ("dwFileDateLS", w.DWORD)]


def hi(dword):
    return (dword >> 16) & 0xFFFF


def lo(dword):
    return dword & 0xFFFF


RE_TAG_IN_STRING = re.compile(r"\+\+UE([45])\+Release-(\d+)\.(\d+)")


def pe_branch(path):
    """Replicate DetectVersionFromPEResource. Return (verdict, detail)."""
    size = w.DWORD(0)
    n = ver.GetFileVersionInfoSizeW(str(path), ctypes.byref(size))
    if not n:
        return ("NO_RESOURCE", "GetFileVersionInfoSizeW=0")
    buf = ctypes.create_string_buffer(n)
    if not ver.GetFileVersionInfoW(str(path), 0, n, buf):
        return ("NO_RESOURCE", "GetFileVersionInfoW failed")

    ptr = ctypes.c_void_p()
    ln = ctypes.c_uint(0)
    if not ver.VerQueryValueW(buf, "\\", ctypes.byref(ptr), ctypes.byref(ln)):
        return ("NO_RESOURCE", "VerQueryValue(root) failed")
    fi = ctypes.cast(ptr, ctypes.POINTER(FIXEDFILEINFO)).contents

    pmaj, pmin = hi(fi.dwProductVersionMS), lo(fi.dwProductVersionMS)
    fmaj, fmin = hi(fi.dwFileVersionMS), lo(fi.dwFileVersionMS)

    if pmaj == 5 and pmin <= 9:
        return ("PE_HIT", f"Product {pmaj}.{pmin} -> {500 + pmin}")
    if pmaj == 4 and pmin <= 27:
        return ("PE_HIT", f"Product {pmaj}.{pmin} -> {400 + pmin}")
    if fmaj == 5 and fmin <= 9:
        return ("PE_HIT", f"File {fmaj}.{fmin} -> {500 + fmin}")
    if fmaj == 4 and fmin <= 27:
        return ("PE_HIT", f"File {fmaj}.{fmin} -> {400 + fmin}")

    # StringFileInfo last resort -- walk EVERY (lang, codepage) like
    # ReadVersionInfoString does, not just the default translation.
    if ver.VerQueryValueW(buf, "\\VarFileInfo\\Translation",
                          ctypes.byref(ptr), ctypes.byref(ln)) and ln.value >= 4:
        raw = ctypes.string_at(ptr, ln.value)
        for i in range(0, ln.value - 3, 4):
            lang = int.from_bytes(raw[i:i + 2], "little")
            cp = int.from_bytes(raw[i + 2:i + 4], "little")
            for key in ("ProductVersion", "FileVersion"):
                sub = f"\\StringFileInfo\\{lang:04x}{cp:04x}\\{key}"
                if not ver.VerQueryValueW(buf, sub, ctypes.byref(ptr),
                                          ctypes.byref(ln)):
                    continue
                s = ctypes.wstring_at(ptr, ln.value).rstrip("\x00")
                m = RE_TAG_IN_STRING.search(s)
                if not m:
                    continue
                maj, mn = int(m.group(2)), int(m.group(3))
                if (maj == 5 and mn <= 9) or (maj == 4 and mn <= 27):
                    return ("PE_HIT", f"{key} string '{s}'")
    return ("PE_MISS", f"Product={pmaj}.{pmin} File={fmaj}.{fmin} unrecognised")


def tag_scan(path, chunk=64 << 20):
    """Release tags present in the file bytes (chunked, overlapping)."""
    found = set()
    overlap = 128
    prev = b""
    with open(path, "rb") as fh:
        while True:
            blk = fh.read(chunk)
            if not blk:
                break
            data = prev + blk
            found |= {m.group(1).decode("utf-16-le").rstrip(".")
                      for m in RE_UTF16.finditer(data)}
            found |= {m.group(1).decode().rstrip(".")
                      for m in RE_ASCII.finditer(data)}
            prev = data[-overlap:]
    return sorted(found)


def main(argv):
    rows = []
    for raw in argv:
        p = pathlib.Path(raw)
        if not p.exists():
            print(f"MISSING  {p}")
            continue
        verdict, detail = pe_branch(p)
        tags = tag_scan(p)
        mb = p.stat().st_size // (1024 * 1024)
        if verdict == "PE_HIT":
            branch = "short-circuits at PE (no ladder)"
        elif tags:
            branch = "*** PE MISS + TAG PRESENT -> Tier 1 fires ***"
        else:
            branch = "PE MISS, no tag -> falls through (Elliot-shaped)"
        rows.append((branch, verdict, ",".join(tags) or "-", mb, detail, str(p)))
        print(f"{verdict:11s} tag={','.join(tags) or '-':8s} {mb:5d}MB  "
              f"{branch}\n            {detail}\n            {p}")

    print("\n===== titles that make Tier 1 fire =====")
    hits = [r for r in rows if r[0].startswith("***")]
    if not hits:
        print("NONE among the probed set.")
    for r in hits:
        print(f"  {r[5]}   tag={r[2]}  {r[3]}MB  ({r[4]})")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
