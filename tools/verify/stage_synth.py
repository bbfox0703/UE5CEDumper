"""Stage a SYNTHETIC game folder for AC1 (foreign-DLL refusal) and AE4 step 4 (leftover delete).

    py stage_synth.py create      # folder + fake shipping exe + planted foreign dxgi.dll
    py stage_synth.py status
    py stage_synth.py clean

Why synthetic rather than Light Maze (plan §4.1's own preference): "restore" here
means delete a tree we created, so the destructive case the review flagged cannot
arise at all -- there is no original file anywhere in it to lose.

Two deliberate risk reductions over the plan's fallback:
  * The "-Win64-Shipping.exe" is a text stub, not a copied system binary. The
    scanner only pattern-matches the FILENAME, and nothing will ever execute in
    this tree, so there is no reason to put real executable bytes on disk.
  * The foreign DLL is a copy of Intel's tbbmalloc.dll (already present in a game
    folder on this machine), not System32's winhttp.dll. It only has to carry a
    ProductName that is not ours; using a benign third-party library rather than a
    Windows system DLL keeps the shape as far from a real hijack as the test allows.

§4.1 condition 1 is enforced: the SHA-256 of anything planted is recorded, and the
destination is asserted absent beforehand.
"""
import hashlib
import pathlib
import shutil
import sys

ROOT = pathlib.Path(r"D:\SteamLibrary\steamapps\common\ZZSynthProxyTest")
BIN = ROOT / "ZZSynth" / "Binaries" / "Win64"
EXE = BIN / "ZZSynth-Win64-Shipping.exe"
FOREIGN = BIN / "dxgi.dll"
FOREIGN_SRC = pathlib.Path(
    r"D:\SteamLibrary\steamapps\common\Titan Quest II\TQ2\Binaries\Win64\tbbmalloc.dll")

# A second tree that holds ONLY our proxy and no exe -- that is what the orphan
# scanner defines as a leftover, so it gives AE4 step 4 something to delete.
ORPHAN_BIN = (pathlib.Path(r"D:\SteamLibrary\steamapps\common\ZZSynthOrphan")
              / "ZZOrphan" / "Binaries" / "Win64")
OURS = pathlib.Path(r"D:\Github\UE5CEDumper\dist\proxy\version.dll")


def sha(p):
    return hashlib.sha256(pathlib.Path(p).read_bytes()).hexdigest()


def create():
    for d in (BIN, ORPHAN_BIN):
        if d.exists():
            print(f"REFUSING: {d} already exists -- run clean first")
            return 1

    BIN.mkdir(parents=True)
    EXE.write_text("synthetic stub for UE5CEDumper proxy-panel verification\n",
                   encoding="ascii")
    print(f"created {EXE}  ({EXE.stat().st_size} B, filename-only stub)")

    if not FOREIGN_SRC.exists():
        print(f"MISSING foreign source {FOREIGN_SRC}")
        return 1
    assert not FOREIGN.exists(), "destination existed"
    shutil.copy2(FOREIGN_SRC, FOREIGN)
    print(f"planted {FOREIGN}")
    print(f"   source     {FOREIGN_SRC}")
    print(f"   sha256     {sha(FOREIGN)}")
    print(f"   size       {FOREIGN.stat().st_size}")

    ORPHAN_BIN.mkdir(parents=True)
    shutil.copy2(OURS, ORPHAN_BIN / "version.dll")
    print(f"created leftover {ORPHAN_BIN / 'version.dll'} (our proxy, no exe beside it)")
    print(f"   sha256     {sha(ORPHAN_BIN / 'version.dll')}")
    return 0


def status():
    for p in (EXE, FOREIGN, ORPHAN_BIN / "version.dll"):
        print(f"{'PRESENT' if p.exists() else 'absent ':8s} {p}"
              + (f"  sha256={sha(p)[:16]}" if p.exists() and p.suffix == ".dll" else ""))
    return 0


def clean():
    for root in (ROOT, ORPHAN_BIN.parents[2]):
        if root.exists():
            shutil.rmtree(root)
            print(f"removed tree {root}")
        else:
            print(f"absent       {root}")
    print("assert both gone:", not ROOT.exists() and not ORPHAN_BIN.exists())
    return 0


def replant():
    """Put the foreign DLL back after a test overwrote it with ours (AC1 step 7)."""
    if not BIN.exists():
        print(f"no synthetic tree at {BIN} -- run create first")
        return 1
    before = sha(FOREIGN) if FOREIGN.exists() else None
    shutil.copy2(FOREIGN_SRC, FOREIGN)
    print(f"re-planted {FOREIGN}")
    print(f"   was     {before[:16] if before else '(absent)'}")
    print(f"   now     {sha(FOREIGN)}   size {FOREIGN.stat().st_size}")
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    cmd = sys.argv[1] if len(sys.argv) > 1 else "status"
    sys.exit({"create": create, "status": status, "clean": clean,
              "replant": replant}.get(cmd, status)())
