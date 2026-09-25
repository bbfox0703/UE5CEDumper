"""A second, drive-scan-detectable game folder that ships the SAME exe name as a hosted fixture.

    py tools/verify/shared_exe_twin.py make <binaries-win64-dir> <exe-name> <shape>   # build it under out/pathshape/<shape>/
    py tools/verify/shared_exe_twin.py clean <shape>                                    # remove it

WHY. `[PROXY-CONFIRM-SHARED-EXE]` (docs/todo.md) is about two games in DIFFERENT folders that ship one exe name: the
confirmed / injected records are keyed by the bare name, so neither folder may use them, and the Proxy Deploy panel
must say so. Its live check (docs/path-shape-live-plan.md S5 #18) needs Scan drives to list two such rows. This makes
the second one without a second multi-GB package: `<shape>/SharedExeTwin/DumperTest51/Binaries/Win64/<exe-name>` is a
HARD LINK to the fixture's real exe (same bytes, so the import-table analysis is the real one), beside an empty
`Engine/Binaries/Win64` so `LooksLikeUeGameRoot` accepts the folder as a cooked tree.

Only the one exe is linked, so the scan picks exactly that name for the twin's row. Nothing here launches: every S5
row acts on files. A hard link needs the same volume; `make` refuses otherwise. `clean` removes the twin only (the
fixture's own exe keeps its data: deleting one link of a file leaves the others).
"""
import os
import pathlib
import shutil
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
BASE = ROOT / "out" / "pathshape"
TWIN = "SharedExeTwin"


def shape_dir(shape: str) -> pathlib.Path:
    # Resolve through path_shape_folders so the shape names stay defined in one place.
    sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
    import path_shape_folders as psf  # noqa: E402
    return psf.shape_dir(shape)


def make(bin_dir: str, exe: str, shape: str):
    src = pathlib.Path(bin_dir) / exe
    if not src.is_file():
        raise SystemExit(f"no such exe: {src}")
    root = shape_dir(shape) / TWIN
    if os.path.splitdrive(str(src))[0].lower() != os.path.splitdrive(str(root))[0].lower():
        raise SystemExit("refused: a hard link needs the fixture and the twin on one volume")
    if root.exists():
        raise SystemExit(f"already there: {root} (clean first)")
    win64 = root / "DumperTest51" / "Binaries" / "Win64"
    win64.mkdir(parents=True)
    (root / "Engine" / "Binaries" / "Win64").mkdir(parents=True)
    os.link(src, win64 / exe)
    print(f"  twin: {win64 / exe}\n     == {src} (hard link)")


def clean(shape: str):
    root = shape_dir(shape) / TWIN
    if not root.exists():
        print(f"  nothing at {root}")
        return
    shutil.rmtree(root)
    print(f"  removed {root}")


def main(a):
    if len(a) == 4 and a[0] == "make":
        make(a[1], a[2], a[3])
    elif len(a) == 2 and a[0] == "clean":
        clean(a[1])
    else:
        raise SystemExit(__doc__)


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    main(sys.argv[1:])
