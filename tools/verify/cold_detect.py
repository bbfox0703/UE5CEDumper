"""Force a COLD version re-detect for one title by removing its cache entry.

    py cold_detect.py show
    py cold_detect.py drop <PE_HASH> --apply
    py cold_detect.py restore <PE_HASH>

Why a tool rather than an edit: the file holds every other game's hints as
well, so the register's rule is a json load -> del -> dump round-trip, never a
text rewrite (a regex over JSON is how a sibling key gets clipped). Every drop
writes a per-key backup first, so `restore` puts the exact original object back
without touching anything else.
"""
import json
import os
import pathlib
import shutil
import sys

STORE = pathlib.Path(os.environ["LOCALAPPDATA"]) / "UE5CEDumper" / "UE5CEDumper.MSI-NB.json"
BACKUPS = pathlib.Path(__file__).parent / "cache-backups"

FIELDS = ("ueVersion", "versionDetected", "lowConfidence", "versionDetectRev",
          "scanCount", "exeName")


def load():
    return json.loads(STORE.read_text(encoding="utf-8"))


def save(j):
    STORE.write_text(json.dumps(j, indent=2, ensure_ascii=False), encoding="utf-8")


def show():
    j = load()
    print(f"{STORE}  ({len(j['games'])} games)")
    for k, v in sorted(j["games"].items()):
        if not isinstance(v, dict):
            continue
        bits = "  ".join(f"{f}={v.get(f)}" for f in FIELDS if f in v)
        print(f"  {k}  {bits}")


def drop(key, apply):
    j = load()
    if key not in j["games"]:
        print(f"key {key} not present -- already cold")
        return 1
    entry = j["games"][key]
    BACKUPS.mkdir(exist_ok=True)
    whole = BACKUPS / f"{key}.wholefile.json"
    single = BACKUPS / f"{key}.entry.json"
    print(f"=== BEFORE  {key}  ({entry.get('exeName')}) ===")
    print(json.dumps(entry, indent=2, ensure_ascii=False))
    if not apply:
        print("\n(dry run -- pass --apply to actually drop it)")
        return 0
    shutil.copy2(STORE, whole)
    single.write_text(json.dumps(entry, indent=2, ensure_ascii=False), encoding="utf-8")
    del j["games"][key]
    save(j)
    print(f"\nbacked up whole file -> {whole}")
    print(f"backed up entry      -> {single}")
    print(f"DROPPED. remaining games = {len(j['games'])}")
    return 0


def restore(key):
    single = BACKUPS / f"{key}.entry.json"
    if not single.exists():
        print(f"no backup at {single}")
        return 1
    j = load()
    j["games"][key] = json.loads(single.read_text(encoding="utf-8"))
    save(j)
    print(f"restored {key}; games = {len(j['games'])}")
    return 0


def main(argv):
    if not argv:
        print(__doc__)
        return 1
    cmd = argv[0]
    if cmd == "show":
        show()
        return 0
    if cmd == "drop":
        return drop(argv[1], "--apply" in argv)
    if cmd == "restore":
        return restore(argv[1])
    print(__doc__)
    return 1


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.exit(main(sys.argv[1:]))
