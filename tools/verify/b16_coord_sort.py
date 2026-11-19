"""B16, its Group/Map half -- stage a Coordinate Library dataset that can actually
EXERCISE the Group and Map sort columns, and predict every order so the screen can
be read.

    py b16_coord_sort.py stage      # back up + write the designed dataset
    py b16_coord_sort.py predict    # print the predicted orders only
    py b16_coord_sort.py restore    # put the original file back

WHY STAGING IS THE WHOLE JOB. The shipped file has `group` EMPTY on all five
entries and `map` IDENTICAL on all five, so neither column can order anything on
it. No path in the UI produces distinct values by itself, and `Map` has no
row-editor field at all, so the JSON is the only way in.

TWO TRAPS THIS RIG EXISTS TO DISARM.

1. GROUP-ASCENDING IS STRUCTURALLY VACUOUS FROM THE BASELINE, so the register's
   own procedure ("set distinct groups, click Group") CANNOT FAIL on that click.
   The VM's display order is ALREADY group-ascending -- `CompareForDisplay`
   (TeleportViewModel.cs) does `string.Compare(a.Group, b.Group,
   OrdinalIgnoreCase)` then natural Label, and the wired header comparer
   (TeleportPanel.axaml.cs `CoordSortComparers["Group"]`) is
   `DataGridSortComparers.Ordinal(r => r.Group)`, which despite the name IS
   OrdinalIgnoreCase (DataGridSortComparers.cs `Ordinal<T>`) --
   the same comparison on the same field. A stable sort reproduces the baseline
   exactly, and so does a header that does nothing.
   ▶ THE FIX: never click Group-ascending from the baseline. Sort by X first, THEN
   click Group ascending -- it must return to group order. A dead header leaves the
   X order on screen.

2. AN ORDER THAT DIFFERS ONLY DEEP IN THE LIST is easy to misread off a screenshot.
   ▶ THE FIX: this dataset is constructed so all five predicted orders differ AT
   THE FIRST ROW. One glance at row 1 identifies which sort is active.

⚠ SCOPE, as `CoordSortComparers` in TeleportPanel.axaml.cs states it: Group and Map
are NOT part of the B16 trimming defect, which kills the five columns sorting on a
NESTED path (`Entry.X`) or a mismatched one (`Distance` vs `DistanceText`).
Group/Map bind and sort on the same direct path, so trimming leaves them live. This
row is a COMPLETENESS check on two columns outside the defect, not a test of it.
Still worth the five minutes -- the wired custom comparer REPLACES Avalonia's
default on these two, which is real behaviour of its own -- but it is not the
defect class.
"""
import json
import os
import pathlib
import shutil
import sys

LIB = pathlib.Path(os.environ["LOCALAPPDATA"]) / "UE5CEDumper" / "TeleportCoords"
F = LIB / "teleport-coords.dumpertest.json"
KEEP = LIB / "teleport-coords.dumpertest.json.b16-original"

# label, group, map, x -- chosen so baseline / Group-desc / Map-asc / Map-desc /
# X-asc all differ at ROW 1 (B-two / F-six / C-three / D-four / E-five).
ROWS = [
    ("A-one",   "GC", "MB", 400.0),
    ("B-two",   "GA", "MD", 600.0),
    ("C-three", "GE", "MA", 200.0),
    ("D-four",  "GB", "MF", 500.0),
    ("E-five",  "GD", "MC", 100.0),
    ("F-six",   "GF", "ME", 300.0),
]


def natural_label(s):
    return s.lower()


def predictions():
    lab = {r[0]: r for r in ROWS}
    baseline = [r[0] for r in sorted(ROWS, key=lambda r: (r[1].lower(), natural_label(r[0])))]
    return [
        ("BASELINE (no click) = Group asc, then Label", baseline),
        ("X ascending  (the intermediate state)", [r[0] for r in sorted(ROWS, key=lambda r: r[3])]),
        ("Group ASCENDING  (must equal baseline)", baseline),
        ("Group DESCENDING", [r[0] for r in sorted(ROWS, key=lambda r: r[1].lower(), reverse=True)]),
        ("Map ASCENDING", [r[0] for r in sorted(ROWS, key=lambda r: r[2].lower())]),
        ("Map DESCENDING", [r[0] for r in sorted(ROWS, key=lambda r: r[2].lower(), reverse=True)]),
    ]


def predict():
    preds = predictions()
    for name, order in preds:
        print("  %-44s %s" % (name, " | ".join(order)))
    firsts = {}
    for name, order in preds:
        firsts.setdefault(order[0], []).append(name)
    print()
    print("  row-1 discriminator:")
    for first, names in sorted(firsts.items()):
        mark = "OK " if len(names) <= 2 else "!! "
        print("    %s%-8s <- %s" % (mark, first, "; ".join(n.split("(")[0].strip() for n in names)))
    # Group asc and baseline SHARE row 1 by construction; that is the documented
    # vacuity, which is why the X-first step exists. Nothing else may collide.
    bad = [f for f, n in firsts.items() if len(n) > 2]
    print()
    print("  collisions beyond the known Group-asc/baseline pair:",
          ", ".join(bad) if bad else "none")
    return 0 if not bad else 1


def stage():
    if not F.is_file():
        raise SystemExit("b16: %s does not exist -- launch the UI once first" % F)
    if not KEEP.is_file():
        shutil.copy2(F, KEEP)
        print("  kept the original -> %s" % KEEP.name)
    else:
        print("  original already kept (%s) -- not overwriting it" % KEEP.name)
    doc = {
        "version": 1,
        "module": "dumpertest",
        "entries": [
            {"uid": "b16r%02d" % i, "label": lb, "group": gp, "map": mp,
             "x": x, "y": 1000.0 + i * 10, "z": 90.0 + i, "pitch": 0.0,
             "yaw": float(10 * i), "roll": 0.0}
            for i, (lb, gp, mp, x) in enumerate(ROWS)
        ],
        "zTolerance": 0,
    }
    F.write_text(json.dumps(doc, indent=2), encoding="utf-8")
    print("  wrote %d entries -> %s" % (len(ROWS), F))
    print()
    return predict()


def restore():
    if not KEEP.is_file():
        raise SystemExit("b16: no kept original at %s -- nothing to restore" % KEEP)
    shutil.copy2(KEEP, F)
    print("  restored %s from %s (%d B)" % (F.name, KEEP.name, F.stat().st_size))
    return 0


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else "predict"
    raise SystemExit({"stage": stage, "predict": predict, "restore": restore}
                     .get(cmd, lambda: (_ for _ in ()).throw(SystemExit(__doc__)))())
