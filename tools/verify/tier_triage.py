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

The PE half reads through pe_version_probe.read_resource, the one offline port of
DetectVersionFromPEResource (the same version.dll APIs, every translation, the
engine build strings; its --selftest is a gate). It kept its own copy of the
string fallback until the [VER-410-GATE] second review, and that copy knew only
'++UEn+Release-': a 4.10-4.17 exe carrying its engine build string read PE_MISS
here while the DLL took it at Tier 0. The tag half reuses tools/pe/ue_version.py's
own regexes.
"""
import pathlib
import re
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import pe_version_probe as PV  # noqa: E402

# --- the tag needles, copied from tools/pe/ue_version.py ---------------------
RE_UTF16 = re.compile(
    rb"\+\x00\+\x00U\x00E\x00[45]\x00\+\x00R\x00e\x00l\x00e\x00a\x00s\x00e\x00-\x00"
    rb"((?:[0-9]\x00|\.\x00)+)")
RE_ASCII = re.compile(rb"\+\+UE[45]\+Release-([0-9][0-9.]*)")

def pe_branch(path):
    """DetectVersionFromPEResource's verdict, through pe_version_probe.read_resource. Return (verdict, detail)."""
    r = PV.read_resource(str(path))
    if r["kind"] in ("no resource", "unreadable", "no fixedinfo"):
        return ("NO_RESOURCE", r["kind"])
    if r["code"]:
        if r["kind"] == "fixed":
            fixed = r["prod"] if r["key"] == "ProductVersion" else r["fver"]
            return ("PE_HIT", f"{r['key']} {fixed} -> {r['code']}")
        return ("PE_HIT", f"{r['key']} {r['kind']} '{r['string']}' -> {r['code']}")
    return ("PE_MISS", f"Product={r['prod']} File={r['fver']} unrecognised")


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
