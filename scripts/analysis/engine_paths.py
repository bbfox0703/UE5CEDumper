"""Which /Script/ paths are the ENGINE's own modules, for the scripts in this folder.

A copy of the DLL's Aura::IsEnginePackage list (dll/src/Aura.h), which
DumpAllService.EnginePathPrefixes also copies; tools/check_engine_prefixes.py keeps the three equal.
Not every "/Script/" path is engine: the game's own C++ classes live under /Script/<GameModule> too.
A module in neither (an engine plugin such as /Script/CommonUI) counts as the game's, as it does
in the DLL's GameOnly filter.

diff_dumps.py and analyze_dumps.py import this; both are run from a checkout, as
`python scripts/analysis/<script>.py`, which puts this folder on the import path.
"""

ENGINE_PATH_PREFIXES = (
    "/Script/Engine", "/Script/CoreUObject", "/Script/CoreOnline",
    "/Script/UMG", "/Script/Slate", "/Script/SlateCore", "/Script/InputCore",
    "/Script/EnhancedInput", "/Script/PhysicsCore", "/Script/NavigationSystem",
    "/Script/AIModule", "/Script/Niagara", "/Script/Paper2D",
    "/Script/CinematicCamera", "/Script/GameplayCameras", "/Script/MovieScene",
    "/Script/LevelSequence", "/Script/Landscape", "/Script/Foliage",
    "/Script/AnimGraphRuntime", "/Script/AudioMixer", "/Script/ChaosCloth",
    "/Script/ChaosSolverEngine", "/Script/ClothingSystemRuntimeNv",
    "/Script/GeometryCollectionEngine", "/Script/FieldSystemEngine",
    "/Script/ProceduralMeshComponent", "/Script/GameplayTags",
    "/Script/GameplayTasks", "/Script/GameplayAbilities", "/Script/PacketHandler",
    "/Script/PropertyAccess", "/Script/DeveloperSettings", "/Script/AssetRegistry",
    "/Script/MediaAssets", "/Script/HeadMountedDisplay",
)


def is_engine_path(path: str) -> bool:
    """The DLL's rule: collapse the leading slash run to one '/' (a dump writes
    '//Script/Engine/Actor'), then a prefix counts only when it is followed by
    the end, '/' or '.', so '/Script/EngineOverride' is not '/Script/Engine'."""
    rest = (path or "").lstrip("/")
    if not rest:
        return False
    p = "/" + rest
    for prefix in ENGINE_PATH_PREFIXES:
        if p.startswith(prefix) and (len(p) == len(prefix) or p[len(prefix)] in "/."):
            return True
    return False
