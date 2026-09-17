#!/usr/bin/env python3
r"""Author `Content/UsmapProbe/BP_UsmapProbe` -- the COOKED witness L30 and L39 both need.

    py tools/ue-sample/make_usmap_probe.py --engine 5.4 --project DumperTest
    py tools/ue-sample/make_usmap_probe.py --engine 5.4 --project DumperTest --verify-only

WHY THIS EXISTS
  L30 (`[A4-USMAP-ENUM-UNDERLYING]`) and L39 (`[A4-USMAP-CONTAINER-ENUM]`) were both stuck on
  the same missing object, not on any defect: a **cooked .uasset** carrying

    * a non-1-byte enum UPROPERTY            (L30 -- a 1-byte TEnumAsByte is byte-identical
                                              either side of the fix e07d957b, so it cannot
                                              test the row at all), and
    * a `TArray<TEnumAsByte<E>>` UPROPERTY   (L39).

  DumperTest has both shapes in C++, and neither reaches an asset: ADumperTestActor is
  SPAWNED at runtime (DumperTestSubsystem.cpp), never placed, so nothing it owns is written
  into a package -- its usmap record is reflection, not cooked data. A Blueprint SUBCLASS
  fixes exactly that: a BPGC's default object IS cooked into the .uasset, and it drags the
  whole native super chain in with it.

⛔ THE FALSE-PASS MODES THIS SCRIPT IS SHAPED AROUND. All four are cheap to hit and none of
  them looks like a failure:

  1. **A probe equal to its CDO is ABSENT from the cooked stream.** Unversioned property
     serialization writes only what differs from the archetype. Seed the probes in C++ and
     the asset writes nothing, both mappings parse an empty stream, and they "agree".
     -> the constructor leaves them quiet; THIS script writes the loud values.
  2. **A lone property proves a value, never an alignment.** Each probe is followed by a
     guard int32 that is ALSO overridden, so a consumer that reads the enum at the wrong
     width starts the guard at the wrong offset and cannot return the constant.
  3. **A specifier-less UPROPERTY cannot be set at all** -- not from Class Defaults, not from
     `set_editor_property`. The four probes are EditAnywhere for that reason alone.
  4. **An asset nothing references is not cooked.** The probe is deliberately unreferenced
     (a placed instance would change every other row that counts DumperTestActors), so the
     cook has to be told about it -- hence the DefaultGame.ini clause below, which this
     script adds idempotently.

⚠ THE CONSTANTS ARE NOT SPELLED HERE. They are parsed out of `DumperTestTypes.h`'s
  `namespace DumperTestUsmapProbe`, so the asset, README.md's table and
  check_ue_sample_values.py cannot drift apart. Changing a value there changes it here.

⚠ AFTER RUNNING THIS, REPACKAGE. The asset only becomes a witness once it is cooked:
      py tools/ue-sample/repackage.py --engine 5.4 --project DumperTest --configs Shipping
  and re-take any OTHER dumper's .usmap from that same package -- comparing mappings taken
  from two different builds measures the build difference, not the writer's.
"""
import argparse
import os
import re
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, HERE)
from repackage import ENGINE_ROOT, PROJECTS_ROOT      # noqa: E402  single source of truth

TYPES_H = os.path.join(HERE, "DumperTest", "Source", "DumperTest", "DumperTestTypes.h")

PACKAGE_PATH = "/Game/UsmapProbe"
ASSET_NAME = "BP_UsmapProbe"

# The enumerators the asset selects. Spelled here, but ASSERTED against the header below --
# a rename in C++ must not leave this script quietly picking a name that no longer exists.
WIDE_MEMBER = "Wide_High"
LANE_MEMBERS = ["Lane_Left", "Lane_Center", "Lane_Right"]

COOK_CLAUSE = '+DirectoriesToAlwaysCook=(Path="%s")' % PACKAGE_PATH


def parse_constants():
    """AfterWideEnum / AfterLanes out of DumperTestTypes.h, plus the enumerator names."""
    text = open(TYPES_H, encoding="utf-8-sig").read()
    out = {}
    for name in ("AfterWideEnum", "AfterLanes"):
        m = re.search(r"constexpr\s+int32\s+%s\s*=\s*(0x[0-9A-Fa-f]+|\d+)\s*;" % name, text)
        if not m:
            raise SystemExit("make_usmap_probe: %s is not declared in DumperTestTypes.h -- the "
                             "asset's contract lives there, not here" % name)
        out[name] = int(m.group(1), 0)
    for member in [WIDE_MEMBER] + LANE_MEMBERS:
        if not re.search(r"\b%s\s*=" % member, text):
            raise SystemExit("make_usmap_probe: enumerator %s is gone from DumperTestTypes.h; "
                             "this script would author an asset against a name that no longer "
                             "exists" % member)
    return out


def ensure_cook_clause(project_dir):
    """Cook the probe even though nothing references it. Idempotent, and it says why."""
    ini = os.path.join(project_dir, "Config", "DefaultGame.ini")
    text = open(ini, encoding="utf-8-sig").read() if os.path.isfile(ini) else ""
    if COOK_CLAUSE in text:
        return "already present"

    block = (
        "\n[/Script/UnrealEd.ProjectPackagingSettings]\n"
        "; The usmap probe is DELIBERATELY unreferenced -- a placed instance would add a\n"
        "; second ADumperTestActor to the world and change every row that counts them. An\n"
        "; unreferenced asset is not cooked, so the cook has to be told about it by hand.\n"
        "; Authored by tools/ue-sample/make_usmap_probe.py; see its header for L30 / L39.\n"
        + COOK_CLAUSE + "\n")
    if "[/Script/UnrealEd.ProjectPackagingSettings]" in text:
        # Append inside the existing section rather than opening a second one.
        text = text.replace("[/Script/UnrealEd.ProjectPackagingSettings]\n",
                            "[/Script/UnrealEd.ProjectPackagingSettings]\n" + COOK_CLAUSE + "\n",
                            1)
    else:
        text = text + block
    open(ini, "w", encoding="utf-8", newline="\n").write(text)
    return "added"


UE_SCRIPT = r'''
import unreal

TAG = "USMAPPROBE:"
PKG = "%(pkg)s"
NAME = "%(name)s"
FULL = PKG + "/" + NAME
WIDE = unreal.DumperTestWideGrade.%(wide)s
LANES = [%(lanes)s]
AFTER_WIDE = %(after_wide)d
AFTER_LANES = %(after_lanes)d
VERIFY_ONLY = %(verify_only)s


def say(*parts):
    print(TAG, " ".join(str(p) for p in parts))


def cdo_of(bp):
    return unreal.get_default_object(bp.generated_class())


if not VERIFY_ONLY:
    if not unreal.EditorAssetLibrary.does_directory_exist(PKG):
        unreal.EditorAssetLibrary.make_directory(PKG)

    if unreal.EditorAssetLibrary.does_asset_exist(FULL):
        bp = unreal.EditorAssetLibrary.load_asset(FULL)
        say("reusing existing asset")
    else:
        factory = unreal.BlueprintFactory()
        factory.set_editor_property("parent_class", unreal.DumperTestActor)
        bp = unreal.AssetToolsHelpers.get_asset_tools().create_asset(
            NAME, PKG, unreal.Blueprint, factory)
        say("created asset")

    cdo = cdo_of(bp)
    cdo.set_editor_property("probe_wide_enum", WIDE)
    cdo.set_editor_property("probe_after_wide_enum", AFTER_WIDE)
    cdo.set_editor_property("probe_lanes", LANES)
    cdo.set_editor_property("probe_after_lanes", AFTER_LANES)

    unreal.BlueprintEditorLibrary.compile_blueprint(bp)
    saved = unreal.EditorAssetLibrary.save_asset(FULL, only_if_is_dirty=False)
    say("saved:", saved)

# -- read back from a FRESHLY loaded asset, so "saved" is measured and not assumed --------
bp = unreal.EditorAssetLibrary.load_asset(FULL)
if bp is None:
    say("FAIL: asset does not exist:", FULL)
else:
    cdo = cdo_of(bp)
    got_wide = cdo.get_editor_property("probe_wide_enum")
    got_after_wide = cdo.get_editor_property("probe_after_wide_enum")
    got_lanes = list(cdo.get_editor_property("probe_lanes"))
    got_after_lanes = cdo.get_editor_property("probe_after_lanes")

    say("class      :", bp.generated_class().get_name())
    # ⚠ NEITHER `Class.get_super_class()` NOR `Blueprint.parent_class` exists -- both
    # raise, and the commandlet then exits -1 AFTER printing everything above, which
    # reads exactly like a failed save. An isinstance check says the same thing (the
    # CDO really is an ADumperTestActor) and is a real assertion rather than a label.
    is_child = isinstance(cdo, unreal.DumperTestActor)
    say("is a DumperTestActor:", is_child)
    say("wide_enum  :", got_wide, "want", WIDE)
    say("after_wide : 0x%%08X" %% int(got_after_wide), "want 0x%%08X" %% AFTER_WIDE)
    say("lanes      :", got_lanes, "want", LANES)
    say("after_lanes: 0x%%08X" %% int(got_after_lanes), "want 0x%%08X" %% AFTER_LANES)

    ok = (is_child and got_wide == WIDE and int(got_after_wide) == AFTER_WIDE
          and got_lanes == LANES and int(got_after_lanes) == AFTER_LANES)
    say("VERDICT:", "OK" if ok else "MISMATCH")
'''


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--engine", required=True, help="engine version, e.g. 5.4")
    ap.add_argument("--project", required=True, help="project folder name, e.g. DumperTest")
    ap.add_argument("--verify-only", action="store_true",
                    help="read the asset back and report; author nothing")
    ap.add_argument("--timeout", type=int, default=900)
    args = ap.parse_args()

    engine = ENGINE_ROOT % args.engine
    editor = os.path.join(engine, "Engine", "Binaries", "Win64", "UnrealEditor-Cmd.exe")
    project_dir = os.path.join(PROJECTS_ROOT, args.project)
    uproject = os.path.join(project_dir, args.project + ".uproject")
    for p in (editor, uproject):
        if not os.path.isfile(p):
            raise SystemExit("make_usmap_probe: not found: %s" % p)

    consts = parse_constants()
    print("contract   : AfterWideEnum=0x%08X  AfterLanes=0x%08X"
          % (consts["AfterWideEnum"], consts["AfterLanes"]))
    print("            (parsed from DumperTestTypes.h, not spelled in this script)")

    if not args.verify_only:
        print("cook clause: %s -- %s" % (COOK_CLAUSE, ensure_cook_clause(project_dir)))

    body = UE_SCRIPT % {
        "pkg": PACKAGE_PATH,
        "name": ASSET_NAME,
        "wide": WIDE_MEMBER.upper(),
        "lanes": ", ".join("unreal.DumperTestLane.%s" % m.upper() for m in LANE_MEMBERS),
        "after_wide": consts["AfterWideEnum"],
        "after_lanes": consts["AfterLanes"],
        "verify_only": "True" if args.verify_only else "False",
    }
    tmpdir = tempfile.mkdtemp(prefix="usmap_probe_")
    script = os.path.join(tmpdir, "author_probe.py")
    open(script, "w", encoding="utf-8", newline="\n").write(body)

    cmd = [editor, uproject, "-run=pythonscript", "-script=" + script,
           "-unattended", "-nosplash", "-stdout", "-FullStdOutLogOutput"]
    print("$ %s ... -script=%s" % (os.path.basename(editor), script))
    proc = subprocess.run(cmd, capture_output=True, text=True, errors="replace",
                          timeout=args.timeout)

    lines = [ln for ln in proc.stdout.splitlines() if "USMAPPROBE:" in ln]
    for ln in lines:
        print("  " + ln.split("USMAPPROBE:", 1)[1].strip())

    # ⚠ A Python exception inside the commandlet prints to LogPython and then exits -1.
    # Without this the run looks like it stopped mid-way for no reason -- measured: a
    # single unexposed API call cost two blind re-runs before these lines were shown.
    errs = [ln for ln in proc.stdout.splitlines()
            if "LogPython: Error" in ln or "Traceback" in ln or "TypeError" in ln
            or "AttributeError" in ln]
    for ln in errs[:25]:
        print("  PY! " + ln.strip())

    verdict = [ln for ln in lines if "VERDICT:" in ln]
    if proc.returncode != 0:
        print("\nEDITOR EXIT %d -- last 20 log lines:" % proc.returncode)
        for ln in proc.stdout.splitlines()[-20:]:
            print("  " + ln)
        return 1
    if not verdict:
        print("\nthe script produced no verdict -- the commandlet ran but the asset step did "
              "not report. Last 20 log lines:")
        for ln in proc.stdout.splitlines()[-20:]:
            print("  " + ln)
        return 1
    if "OK" not in verdict[0]:
        print("\nMISMATCH -- the asset does not carry the contract above.")
        return 1

    print("\nOK -- %s carries the probe. It is NOT a witness until it is COOKED:" % ASSET_NAME)
    print("    py tools/ue-sample/repackage.py --engine %s --project %s --configs Shipping"
          % (args.engine, args.project))
    return 0


if __name__ == "__main__":
    sys.exit(main())
