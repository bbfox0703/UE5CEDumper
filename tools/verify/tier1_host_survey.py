r"""Which titles can produce a `DetectVersion: Tier 1 (ascii|utf16)` line -- decided offline.

Two independent facts per binary, because BOTH are required and either alone misleads:
  1. Does it fall THROUGH Tier 0? (replays Genau::DetectVersionFromPEResource's order)
  2. Does the `++UEn+Release-N.N` needle actually EXIST in the image, and in which encoding?
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

if __name__ == "__main__":
    main()
