"""Triage a UE game .exe for its engine version BEFORE spending a Ghidra import on it.

    py tools/pe/ue_version.py <exe> [<exe> ...]

Reads the compiled-in branch tag (`++UE4+Release-4.18` / `++UE5+Release-5.1`) out of the
binary, in both UTF-16LE and ASCII. When present that tag is authoritative — it is the engine
branch the binary was built from, unlike the PE ProductVersion, which some publishers leave at
a stale fallback (The Adventures of Elliot reports 4.27 and is really 5.4).

HIT RATE IS ABOUT HALF — measured 4 of 7 shipped games:

    Palworld     -> 5.1    Manor Lords -> 5.5
    Octopath     -> 4.18   Grimhook    -> 5.1
    Elliot / Titan Quest II / Solarpunk -> UNKNOWN (tag stripped)

So `UNKNOWN` means UNKNOWN, never "not interesting", and this is a filter rather than a gate.
The reliable version source stays the DLL's own runtime detection (`Genau::DetectVersion` —
see the `UE5_Init: Complete (UEnnn, ...)` line in the game's `init-0.log`), which reconciles
the PE data against what it finds in memory.

It still earns its place: it can recover a version the corpus would otherwise carry as "4.x,
version stripped" — Octopath Traveler reads as 4.18, which its pattern fingerprint corroborates
structurally (identical to DQ XI S, a known 4.18: GNames 7/28 hit, sparse 0/10).

A modular build (Satisfactory) reports a ~0 MB stub here: the engine lives in sibling
`*-Win64-Shipping.dll` modules, so point this at those instead.
"""
import pathlib
import re
import sys

# `++UE5+Release-5.1` with a NUL between every character.
RE_UTF16 = re.compile(
    rb"\+\x00\+\x00U\x00E\x00[45]\x00\+\x00R\x00e\x00l\x00e\x00a\x00s\x00e\x00-\x00"
    rb"((?:[0-9]\x00|\.\x00)+)")
RE_ASCII = re.compile(rb"\+\+UE[45]\+Release-([0-9][0-9.]*)")


def scan(path):
    """Return every distinct engine version tagged in the file, sorted."""
    blob = pathlib.Path(path).read_bytes()
    found = {m.group(1).decode("utf-16-le").rstrip(".") for m in RE_UTF16.finditer(blob)}
    found |= {m.group(1).decode().rstrip(".") for m in RE_ASCII.finditer(blob)}
    return sorted(found)


def main(argv):
    if not argv:
        print(__doc__)
        return 1
    for path in argv:
        try:
            versions = scan(path)
            size_mb = pathlib.Path(path).stat().st_size // (1024 * 1024)
            label = ",".join(versions) if versions else "UNKNOWN"
            print(f"{label:12s} {size_mb:5d} MB  {path}")
        except OSError as exc:
            print(f"{'ERR':12s} {'':5s}     {path}  ({exc})")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
