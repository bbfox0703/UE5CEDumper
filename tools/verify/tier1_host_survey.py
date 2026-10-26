r"""Which titles can produce a `DetectVersion: Tier 1 (ascii|utf16)` line -- decided offline.

Two independent facts per binary, because BOTH are required and either alone misleads:
  1. Does it fall THROUGH Tier 0? (replays Genau::DetectVersionFromPEResource's order)
  2. Does the `++UEn+Release-N.N` needle actually EXIST in the image, and in which encoding? Or, since
     [GWT-BRANCH], a licensee's own branch on a UE depot that names the engine version (Ghostwire's
     `++ue4+ue4_main+4.27`), which Genau::ScanVersionBranchName reads when the first is absent -- reported
     as `branch ...`, so a title that only it can detect is not mis-planned as "NO NEEDLE".
A title that falls through but has no needle detects nothing (Elliot, Echoes of Aincrad);
a title with a needle that resolves at Tier 0 never looks (every stock UE5 title).

⚠ Walks every `Binaries\Win64` directory rather than globbing a fixed depth -- a
fixed-depth glob silently skipped installed titles and an absence claim built on a
silent skip is worthless.

Fact 1 reads through pe_version_probe.read_resource, the one offline port of Genau's reader (every
translation, the engine build strings; its --selftest is a gate). This file kept its own copy until
the [VER-410-GATE] second review, and that copy knew only '++UEn+Release-': a 4.10-4.17 exe carrying
its engine build string read "FALLS THROUGH" here while the DLL took it at Tier 0, which is the
mis-planned row this survey exists to prevent.
"""
import os, pathlib, re, sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import pe_version_probe as PV  # noqa: E402  (it also makes stdout UTF-8)

SKIP = ("crashreportclient", "unrealcefsubprocess", "crashpad_handler", "epicwebhelper")
ASCII = re.compile(rb"\+\+UE([45])\+Release-(\d+)\.(\d+)")
UTF16 = re.compile(rb"(?:\+\x00){2}U\x00E\x00([45])\x00\+\x00R\x00e\x00l\x00e\x00a\x00s\x00e\x00-\x00"
                   rb"((?:\d\x00)+)\.\x00((?:\d\x00)+)")

def tier0(path):
    r = PV.read_resource(path)
    k = r["kind"]
    if k == "no resource": return "FALLS THROUGH (no resource)", "-"
    if k == "unreadable": return "resource unreadable", "-"
    if k == "no fixedinfo": return "FALLS THROUGH (no fixedinfo)", "-"
    prod = r["prod"]
    if k == "fixed":
        return f"Tier0 -> {r['code']}" + (" (File)" if r["key"] == "FileVersion" else ""), prod
    if r["code"]:
        return f"Tier0 {k.upper()} '{r['string']}' -> {r['code']}", prod
    return "FALLS THROUGH (unrecognised)", prod

# Genau::ScanVersionBranchName's grammar, ported: a NUL-terminated printable string of at most 96 characters,
# "++UE4+" / "++UE5+" with the UE in either case, holding a whole "M.m" token after a '+' or '-', followed by the end,
# a '+' or a '-', M the depot's major and M.m a row of kVersionNeedles. Keep in step with that function.
# A lookahead, so every start offset is tried as the DLL tries every '+': a refused run must not hide a branch that
# starts inside it ("++UE5+x++UE4+4.27").
BRANCH_ASCII = re.compile(rb"(?=(\+\+[Uu][Ee][45]\+[\x20-\x7e]{0,90}?\x00))")
BRANCH_UTF16 = re.compile(rb"(?=(\+\x00\+\x00[Uu]\x00[Ee]\x00[45]\x00\+\x00(?:[\x20-\x7e]\x00){0,90}?\x00\x00))")
# kVersionNeedles' rows without the trailing dot, in the table's order.
BARE = ["5.8", "5.7", "5.6", "5.5", "5.4", "5.3", "5.2", "5.1", "5.0", "4.27", "4.26", "4.25", "4.24", "4.23", "4.22",
        "4.21", "4.20", "4.19", "4.18"]
TABLE = {tuple(int(x) for x in b.split(".")) for b in BARE}
TIER1_PREFIXES = ("++UE5+Release-", "++UE4+Release-")


def tier1_finds(data):
    """Genau::ScanVersionTier1's verdict: an Epic prefix followed at once by a table version, either encoding.
    Only then does the DLL stop before the branch reader; an Epic tag naming a version outside the table does not."""
    for enc in ("ascii", "utf-16-le"):
        bares = [b.encode(enc) for b in BARE]
        for prefix in TIER1_PREFIXES:
            pre = prefix.encode(enc)
            i = data.find(pre)
            while i >= 0:
                if any(data.startswith(b, i + len(pre)) for b in bares):
                    return True
                i = data.find(pre, i + 1)
    return False


def branch_name_version(text):
    """Genau::BranchNameVersionIndex: the (major, minor) a branch name names, else None."""
    if len(text) < 6 or not re.fullmatch(r"\+\+[Uu][Ee][45]\+", text[:6]):
        return None
    major = text[4]
    for p in range(5, len(text) - 3):
        if text[p] not in "+-" or text[p + 1] != major or text[p + 2] != ".":
            continue
        m = re.match(r"[0-9]+", text[p + 3:])
        if not m:
            continue
        q = p + 3 + m.end()
        if q < len(text) and text[q] not in "+-":
            continue
        if (int(major), int(m.group(0))) in TABLE and str(int(m.group(0))) == m.group(0):
            return int(major), int(m.group(0))
    return None


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
    if out and tier1_finds(data):
        return out   # Epic's tag wins, as ScanVersionTier1 runs first
    for enc, rx, width in (("ascii", BRANCH_ASCII, 1), ("utf16", BRANCH_UTF16, 2)):
        for m in rx.finditer(data):
            raw = m.group(1)[:-width]
            text = raw.decode("utf-16-le" if width == 2 else "ascii")
            if branch_name_version(text):
                t = "branch " + text
                out[(enc, t)] = out.get((enc, t), 0) + 1
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

if __name__ == "__main__":
    main()
