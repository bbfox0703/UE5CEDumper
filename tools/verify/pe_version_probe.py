r"""Replay Genau's Tier-0 (PE VERSIONINFO) decision offline, over any set of UE binaries.

Why this exists: version-detection rows in the register keep prescribing candidate
titles by ENGINE version, but which tier a title reaches is decided by its PE
version RESOURCE, not by what engine built it. G2 step 3 needs a title that FALLS
THROUGH Tier 0 to the memory-string needle -- and five candidates were listed that
structurally cannot, for the same reason Lushfoil could not. One offline sweep
answers "which titles can even reach the tier I want" before anything is launched.

Mirrors `Genau::DetectVersionFromPEResource` (dll/src/Genau.cpp) in order -- `read_resource()` reads the fields,
`decide()` makes the decision (pure, so `--selftest` runs it on every dll/tests/res fixture's own .rc fields):
  1. VS_FIXEDFILEINFO.dwProductVersionMS   5.x -> 500+minor | 4.x -> 400+minor
  2. VS_FIXEDFILEINFO.dwFileVersionMS      same
     (rev 10) either fixed reading yields to the file's own engine build string when that names another version
     (`fixed_reading`): ProductVersion's string, then FileVersion's, read by `engine_build_string_code`; the result is
     a 'build string' reading recording the fixed code it `overruled`
  3. StringFileInfo ProductVersion, then FileVersion, each the first non-empty value over EVERY
     VarFileInfo translation (`first_nonempty`): a '++UEn+Release-' prefix found anywhere whose
     %u.%u gives a version code, else (rev 9) the engine's own build string --
     `engine_build_string_code`, a port of Grimoire::EngineBuildStringCode (`string_reading`).
     Such a code came from a string, so below the 4.11 floor it cannot corroborate itself: only an
     agreeing CrashReportClient makes it refuse the scan.
  4. otherwise -> "unrecognised", and the caller falls back to the memory scan
⚠ Keep in step with that function; a divergence here silently mis-plans a row. tier_triage.py and
tier1_host_survey.py read through read_resource() for that reason, rather than each keeping a copy.

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


# ── Genau's string fallback (ReadUeVersionFromFile), ported ([VER-410-GATE] second review) ───────────
# tier_triage.py and tier1_host_survey.py read their Tier-0 verdict through read_resource() below, so a title is
# planned for the tier the DLL will really reach.
_PREFIXES = ("++UE5+Release-", "++UE4+Release-")
STRING_KEYS = ("ProductVersion", "FileVersion")


def first_nonempty(translations, query, key):
    """ReadVersionInfoString: walk every (lang, codepage) in VarFileInfo\\Translation and take the first non-empty
    value -- DropIn's strings are not under the first translation. `query(lang, cp, key)` -> str or None."""
    for lang, cp in translations:
        v = query(lang, cp, key)
        if v:
            return v
    return ""


# sscanf's "%u.%u": each %u skips whitespace and takes an optional sign; the '.' between is literal.
_SCANF_UU = re.compile(r"[ \t\n\v\f\r]*([+-]?)([0-9]+)\.[ \t\n\v\f\r]*([+-]?)([0-9]+)")


def _scanf_u(sign, digits):
    """%u's value: a minus wraps modulo 2^32, which UeVersionCode then rejects; past 2^32 is no code either."""
    v = int(digits)
    if v > U32:
        return U32
    return (-v) & U32 if sign == "-" else v


def string_reading(strs):
    """The C++ string fallback over {key: value}: ProductVersion, then FileVersion; in each, a `++UE5+Release-` /
    `++UE4+Release-` prefix (its first occurrence, anywhere in the string) whose `%u.%u` gives a version code, else
    the engine build string. -> (kind, key, string, code) or None."""
    for key in STRING_KEYS:
        s = strs.get(key, "")
        if not s:
            continue
        for pre in _PREFIXES:
            p = s.find(pre)
            if p < 0:
                continue
            m = _SCANF_UU.match(s, p + len(pre))
            if m:
                code = ue_version_code(_scanf_u(m.group(1), m.group(2)), _scanf_u(m.group(3), m.group(4)))
                if code:
                    return ("STRING", key, s, code)
        code = engine_build_string_code(s)
        if code:
            return ("BUILD STRING", key, s, code)
    return None


# (strings, translations-to-values, want). Each mirrors a branch of the C++ reader the old port did not.
_READER_SELFTEST = [
    ("branch-first prefix", {"ProductVersion": "++UE4+Release-4.15-CL-0"}, ("STRING", "ProductVersion", 415)),
    ("version-first with a full 4.18+ branch reads through the prefix",
     {"ProductVersion": "4.18.3-3832480+++UE4+Release-4.18"}, ("STRING", "ProductVersion", 418)),
    ("the prefix path checks no agreement (as the C++)",
     {"ProductVersion": "4.10.2-0+++UE4+Release-4.11"}, ("STRING", "ProductVersion", 411)),
    ("a prefix whose M.m gives no code falls through to the next key",
     {"ProductVersion": "++UE4+Release-4.99", "FileVersion": "4.11.0-0+UE4"}, ("BUILD STRING", "FileVersion", 411)),
    ("a prefix with no M.m after it falls through too",
     {"ProductVersion": "++UE4+Release-", "FileVersion": "++UE5+Release-5.4-CL-0"}, ("STRING", "FileVersion", 504)),
    ("sscanf skips whitespace before each number", {"ProductVersion": "++UE4+Release- 4.15-CL-0"},
     ("STRING", "ProductVersion", 415)),
    ("a minus wraps to no code, and the next key answers",
     {"ProductVersion": "++UE4+Release--4.15", "FileVersion": "4.11.0-0+UE4"}, ("BUILD STRING", "FileVersion", 411)),
    ("the IS Defense build string", {"ProductVersion": "4.10.2-0+++depot+UE4-Releases+4.10"},
     ("BUILD STRING", "ProductVersion", 410)),
    ("a game version alone reads nothing", {"ProductVersion": "1.0.0.0", "FileVersion": "1.0.10897.0"}, None),
]

def build_string_of(strs):
    """The file's own engine build string, as the C++ looks for it beside a fixed-field reading: ProductVersion's string,
    then FileVersion's, the first that `engine_build_string_code` reads. -> (key, string, code) or None."""
    for key in STRING_KEYS:
        s = strs.get(key, "")
        code = engine_build_string_code(s)
        if code:
            return key, s, code
    return None


def fixed_reading(fixed_key, fixed_code, strs):
    """A fixed field's code, as ReadUeVersionFromFile takes it. -> the fields read_resource() reports.
    ([VER-410-FIXED-VS-STRING] rev 10) The fixed fields are the game's to set; where the file's own engine build string
    names another version, the string's is taken, as a 'build string' reading that records the code it `overruled`."""
    b = build_string_of(strs)
    if b and b[2] != fixed_code:
        return {"kind": "build string", "code": b[2], "key": b[0], "string": b[1], "overruled": fixed_code}
    return {"kind": "fixed", "code": fixed_code, "key": fixed_key, "string": None, "overruled": None}


def decide(pmaj, pmin, fmaj, fmin, strs):
    """What ReadUeVersionFromFile takes from one file once its VS_FIXEDFILEINFO and strings are in hand: the fixed
    ProductVersion, then the fixed FileVersion, each through `fixed_reading`; with neither a version code, the strings
    (`string_reading`); else 'unrecognised'. -> the decision fields read_resource() reports (kind, code, key, string,
    overruled). Pure, and split from the reading, so --selftest runs the whole decision: while it sat inline in
    read_resource, the fixed-field branch could be reverted to rev 9 with every selftest case still green
    ([VER-410-FIXED-VS-STRING] review)."""
    for key, (maj, mnr) in (("ProductVersion", (pmaj, pmin)), ("FileVersion", (fmaj, fmin))):
        code = ue_version_code(maj, mnr)
        if code:
            return fixed_reading(key, code, strs)
    r = string_reading(strs)
    if r:
        return {"kind": r[0].lower(), "code": r[3], "key": r[1], "string": r[2], "overruled": None}
    return {"kind": "unrecognised", "code": None, "key": None, "string": None, "overruled": None}


_IS_DEFENSE = "4.10.2-0+++depot+UE4-Releases+4.10"
# ([VER-410-FIXED-VS-STRING] rev 10) (name, fixed key, fixed code, strings, want (kind, key, code, overruled)). The first
# three are dll_core_test's fixtures game_fixed405_buildstring_415 / game_fixed501_buildstring_415 /
# game_fixed420_buildstring_410; the agreeing and bare ones are b25c's and b25d's.
_FIXED_SELFTEST = [
    ("4.5 fixed under a 4.15 build string reads 415", "ProductVersion", 405, {"ProductVersion": "4.15.0-0+UE4"},
     ("build string", "ProductVersion", 415, 405)),
    ("5.1 fixed under FileVersion's branch-first 4.15 reads 415", "ProductVersion", 501,
     {"ProductVersion": "5.1.0.0", "FileVersion": "++UE4+Release-4.15-CL-0"}, ("build string", "FileVersion", 415, 501)),
    ("4.20 fixed under IS Defense's 4.10 string reads 410", "ProductVersion", 420, {"ProductVersion": _IS_DEFENSE},
     ("build string", "ProductVersion", 410, 420)),
    ("an agreeing build string keeps the fixed reading", "ProductVersion", 410, {"ProductVersion": _IS_DEFENSE},
     ("fixed", "ProductVersion", 410, None)),
    ("a bare version string keeps the fixed reading", "ProductVersion", 410, {"ProductVersion": "4.10.3"},
     ("fixed", "ProductVersion", 410, None)),
    ("a prefix-only string (its branch disagrees) is no build string", "ProductVersion", 427,
     {"ProductVersion": "4.10.2-0+++UE4+Release-4.11"}, ("fixed", "ProductVersion", 427, None)),
    ("ProductVersion's build string is looked at before FileVersion's", "ProductVersion", 405,
     {"ProductVersion": "4.15.0-0+UE4", "FileVersion": "++UE4+Release-4.18-CL-0"},
     ("build string", "ProductVersion", 415, 405)),
    ("a FileVersion fixed reading keeps its key", "FileVersion", 410, {"ProductVersion": "1.0.0.0"},
     ("fixed", "FileVersion", 410, None)),
]

_TRANSLATION_SELFTEST = [
    # DropIn's shape: nothing under the first translation, the value under the second.
    ("the second translation answers when the first has nothing",
     [(0x409, 1200), (0x411, 1200)], {(0x411, 1200): "++UE4+Release-4.27-CL-18319896"},
     "++UE4+Release-4.27-CL-18319896"),
    ("an empty value is skipped like a missing one", [(0x409, 1200), (0x411, 1200)],
     {(0x409, 1200): "", (0x411, 1200): "4.11.0-0+UE4"}, "4.11.0-0+UE4"),
    ("the first non-empty wins", [(0x409, 1200), (0x411, 1200)],
     {(0x409, 1200): "A", (0x411, 1200): "B"}, "A"),
    ("no translation, no value", [], {}, ""),
]

# ── dll_core_test's VERSIONINFO fixtures, read from their own .rc scripts ([VER-410-FIXED-VS-STRING] review) ──────────
# dll_core_test builds each dll/tests/res/*.rc into a resource-only DLL and asserts what Genau reads from it. --selftest
# reads the same scripts' fields and runs `decide` on them, so the port is held to the C++ test's answers on the same
# bytes -- that agreement had been checked by hand. A fixture with no row here fails, so a shape the C++ test gains is one
# this port must answer too. Each row is dll_core_test's assertion on that fixture: (kind, key, code, overruled).
_RES_DIR = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "dll", "tests", "res"))
_RC_EXPECT = {
    "b25c_corroborated":             ("fixed", "ProductVersion", 410, None),
    "b25d_bare":                     ("fixed", "ProductVersion", 410, None),
    "game_buildstring_410":          ("build string", "ProductVersion", 410, None),
    "game_buildstring_411":          ("build string", "FileVersion", 411, None),
    "game_only":                     ("unrecognised", None, None, None),
    "game_product_fixedfile_410":    ("fixed", "FileVersion", 410, None),
    "game_fixed405_buildstring_415": ("build string", "ProductVersion", 415, 405),
    "game_fixed501_buildstring_415": ("build string", "FileVersion", 415, 501),
    "game_fixed420_buildstring_410": ("build string", "ProductVersion", 410, 420),
}
_RC_FIXED = re.compile(r"(FILEVERSION|PRODUCTVERSION)[ \t]+(\d+),(\d+),(\d+),(\d+)")
_RC_VALUE = re.compile(r'VALUE "(ProductVersion|FileVersion)", "([^"\\]*)"')
_RC_LANG_BLOCK = re.compile(r'BLOCK "[0-9A-Fa-f]{8}"')


def rc_fields(text):
    """A fixture script's ((pmaj, pmin, fmaj, fmin), {key: value}), in the one shape those scripts are written in: one
    FILEVERSION and one PRODUCTVERSION line, one StringFileInfo language block, no escape in a value. Anything else
    raises ValueError, so a script this reader cannot see whole fails the selftest instead of answering for part of it."""
    fixed, strs = {}, {}
    for raw in text.splitlines():
        line = raw.strip()
        m = _RC_FIXED.fullmatch(line)
        if m:
            if m.group(1) in fixed:
                raise ValueError(f"two {m.group(1)} lines")
            fixed[m.group(1)] = (int(m.group(2)), int(m.group(3)))
        elif line.startswith(("FILEVERSION", "PRODUCTVERSION")):
            raise ValueError(f"unread fixed-field line {line!r}")
        elif line.startswith(('VALUE "ProductVersion"', 'VALUE "FileVersion"')):
            v = _RC_VALUE.fullmatch(line)
            if not v or v.group(1) in strs:
                raise ValueError(f"unread or repeated string line {line!r}")
            strs[v.group(1)] = v.group(2)
    if set(fixed) != {"FILEVERSION", "PRODUCTVERSION"}:
        raise ValueError("a fixed field is missing")
    if len(_RC_LANG_BLOCK.findall(text)) != 1:
        raise ValueError("not exactly one StringFileInfo language block")
    (pmaj, pmin), (fmaj, fmin) = fixed["PRODUCTVERSION"], fixed["FILEVERSION"]
    return (pmaj, pmin, fmaj, fmin), {k: v for k, v in strs.items() if v}


# Shapes no fixture carries: (name, (pmaj, pmin, fmaj, fmin), strings, want (kind, key, code, overruled)).
_DECIDE_SELFTEST = [
    ("a fixed FileVersion under a game ProductVersion is overruled too", (1, 0, 4, 5), {"ProductVersion": "4.15.0-0+UE4"},
     ("build string", "ProductVersion", 415, 405)),
    ("a fixed ProductVersion outside 4.x / 5.x falls to the fixed FileVersion", (4, 99, 4, 10), {},
     ("fixed", "FileVersion", 410, None)),
    ("the fixed ProductVersion is read before the fixed FileVersion", (4, 20, 4, 10), {},
     ("fixed", "ProductVersion", 420, None)),
    ("a 5.x fixed field its own branch-first build string agrees with stays fixed", (5, 4, 5, 4),
     {"ProductVersion": "++UE5+Release-5.4-CL-0"}, ("fixed", "ProductVersion", 504, None)),
    ("no fixed code: a prefix string reads", (1, 0, 1, 0), {"ProductVersion": "++UE4+Release-4.27-CL-18319896"},
     ("string", "ProductVersion", 427, None)),
    ("no fixed code and no strings: unrecognised", (0, 0, 0, 0), {}, ("unrecognised", None, None, None)),
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
    for name, strs, want in _READER_SELFTEST:
        r = string_reading(strs)
        got = (r[0], r[1], r[3]) if r else None
        ok = got == want
        bad += not ok
        print(f"  {'ok ' if ok else 'BAD'}  reader: {name} -> {got} (want {want})")
    for name, translations, values, want in _TRANSLATION_SELFTEST:
        got = first_nonempty(translations, lambda lang, cp, key: values.get((lang, cp)), "ProductVersion")
        ok = got == want
        bad += not ok
        print(f"  {'ok ' if ok else 'BAD'}  translations: {name} -> {got!r} (want {want!r})")
    for name, fkey, fcode, strs, want in _FIXED_SELFTEST:
        r = fixed_reading(fkey, fcode, strs)
        got = (r["kind"], r["key"], r["code"], r["overruled"])
        ok = got == want
        bad += not ok
        print(f"  {'ok ' if ok else 'BAD'}  fixed vs build string: {name} -> {got} (want {want})")
    for name, fields, strs, want in _DECIDE_SELFTEST:
        r = decide(*fields, strs)
        got = (r["kind"], r["key"], r["code"], r["overruled"])
        ok = got == want
        bad += not ok
        print(f"  {'ok ' if ok else 'BAD'}  decide: {name} -> {got} (want {want})")
    rcs = sorted(f[:-3] for f in os.listdir(_RES_DIR) if f.endswith(".rc"))
    fixtures = sorted(set(rcs) | set(_RC_EXPECT))
    for name in fixtures:
        want = _RC_EXPECT.get(name)
        if name not in rcs:
            got = "no such fixture in dll/tests/res"
        elif want is None:
            got = "a fixture with no expected reading in _RC_EXPECT"
        else:
            try:
                with open(os.path.join(_RES_DIR, name + ".rc"), encoding="utf-8") as fh:
                    fields, strs = rc_fields(fh.read())
                r = decide(*fields, strs)
                got = (r["kind"], r["key"], r["code"], r["overruled"])
            except ValueError as e:
                got = f"unreadable: {e}"
        ok = got == want
        bad += not ok
        print(f"  {'ok ' if ok else 'BAD'}  fixture {name}.rc -> {got} (want {want})")
    total = (len(_SELFTEST) + len(_READER_SELFTEST) + len(_TRANSLATION_SELFTEST) + len(_FIXED_SELFTEST)
             + len(_DECIDE_SELFTEST) + len(fixtures))
    print(f"selftest: {total - bad}/{total} passed")
    return 1 if bad else 0

def read_resource(path):
    """Genau's ReadUeVersionFromFile, offline. -> dict:
      kind   'no resource' | 'unreadable' | 'no fixedinfo' | 'fixed' | 'string' | 'build string' | 'unrecognised'
      code   the version code the DLL takes at Tier 0 (None when it falls through to the memory scan)
      key    for a fixed reading 'ProductVersion' / 'FileVersion' (which fixed field), else the string's key
      string the string read, when the reading came from one
      overruled  the fixed fields' code when the file's engine build string named another and was taken, else None
      prod / fver   the fixed fields as dotted text (None without a VS_FIXEDFILEINFO)
      strs   {key: first non-empty value over every translation}"""
    out = {"kind": "no resource", "code": None, "key": None, "string": None, "overruled": None,
           "prod": None, "fver": None, "strs": {}}
    dummy = wintypes.DWORD(0)
    size = ver.GetFileVersionInfoSizeW(path, ctypes.byref(dummy))
    if not size:
        return out
    buf = ctypes.create_string_buffer(size)
    if not ver.GetFileVersionInfoW(path, 0, size, buf):
        out["kind"] = "unreadable"
        return out

    translations = []
    q, m = ctypes.c_void_p(), wintypes.UINT()
    if ver.VerQueryValueW(buf, r"\VarFileInfo\Translation", ctypes.byref(q), ctypes.byref(m)) and m.value >= 4:
        a = ctypes.cast(q, ctypes.POINTER(wintypes.WORD))
        translations = [(a[2 * i], a[2 * i + 1]) for i in range(m.value // 4)]

    def query(lang, cp, key):
        r2, n2 = ctypes.c_void_p(), wintypes.UINT()
        sub = r"\StringFileInfo\%04x%04x\%s" % (lang, cp, key)
        if ver.VerQueryValueW(buf, sub, ctypes.byref(r2), ctypes.byref(n2)) and n2.value:
            return ctypes.wstring_at(r2, n2.value).split("\x00", 1)[0]   # the C++ converts up to the first NUL
        return None

    out["strs"] = {k: v for k in STRING_KEYS if (v := first_nonempty(translations, query, k))}

    p, n = ctypes.c_void_p(), wintypes.UINT()
    if not (ver.VerQueryValueW(buf, "\\", ctypes.byref(p), ctypes.byref(n)) and n.value >= 52):
        out["kind"] = "no fixedinfo"   # the C++ returns here, before the strings
        return out
    raw = ctypes.string_at(p, n.value)
    fms, fls = struct.unpack_from("<II", raw, 8)
    pms, pls = struct.unpack_from("<II", raw, 16)
    pmaj, pmin = pms >> 16, pms & 0xFFFF
    fmaj, fmin = fms >> 16, fms & 0xFFFF
    out["prod"] = f"{pmaj}.{pmin}.{pls >> 16}.{pls & 0xFFFF}"
    out["fver"] = f"{fmaj}.{fmin}.{fls >> 16}.{fls & 0xFFFF}"

    out.update(decide(pmaj, pmin, fmaj, fmin, out["strs"]))
    return out


def probe(path):
    """-> (verdict, product, file, strings). Verdict contains FALLTHROUGH when usable."""
    r = read_resource(path)
    k = r["kind"]
    if k == "no resource":
        return (f"no resource -- {FALLTHROUGH}", None, None, {})
    if k == "unreadable":
        return ("resource UNREADABLE", None, None, {})
    if k == "no fixedinfo":
        return (f"no VS_FIXEDFILEINFO -- {FALLTHROUGH}", None, None, {})
    strs = r["strs"]
    if k == "fixed":
        return (f"Tier0 {r['key']} -> {r['code']}", r["prod"], r["fver"], strs)
    if k == "string":
        return (f"Tier0 STRING {r['key']}='{r['string']}'", r["prod"], r["fver"], strs)
    if k == "build string":
        over = f" (overrules fixed {r['overruled']})" if r["overruled"] else ""
        return (f"Tier0 BUILD STRING {r['key']}='{r['string']}' -> {r['code']}{over}", r["prod"], r["fver"], strs)
    return (f"unrecognised -- {FALLTHROUGH}", r["prod"], r["fver"], strs)

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
