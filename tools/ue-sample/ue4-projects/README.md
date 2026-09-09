# UE4 fixture projects — what this machine's projects had done to them

⛔ **These projects live OUTSIDE the repo** (`D:\Unreal Projects\`), so anything we add to them
exists on one machine only. This directory is the mirror of every change **we** made, so a fresh
machine — or the second PC — can reconstruct them. It is deliberately NOT a copy of the projects:
Content, Config and the template's own C++ are Epic's or the maintainer's and are not ours to
carry.

⚠ **`tools/ue-sample/DumperTest/` is the opposite case** and the rule there is inverted: that
mirror is the *complete* source and the real project is synced FROM it
(`repackage.py --sync-mirror`). Here the real project is the source and this is a record.

## The projects, and their state

| project | engine | module | PCH mode | fixture | builds? |
|---|---|---|---|---|---|
| `UE415_Flyinh` | 4.15 | `UE415_Flyinh` (pre-existing) | module-wide | installed | ⛔ Windows SDK |
| `UE418_3rdPerson` | 4.18 | `UE418_3rdPerson` — ⭐ **added by us** | IWYU | installed | ⛔ Windows SDK |
| `UE423_Flying` | 4.23 | `UE423_Flying` (pre-existing) | IWYU | installed | ✅ Development **and Shipping** |
| `UE427_3rdPerson` | 4.27 | `UE427_3rdPerson` (pre-existing) | IWYU | installed | ✅ Development **and Shipping** |

*"builds?"* is measured, not inherited — see `docs/todo.md` `[D4B-UE4-2026-09-09]` for the runs and
for why 4.15/4.18 stop where they do (a Windows SDK selection with no override, not the fixture
and not permissions).

## What is mirrored here, and what is not

* **`UE418_3rdPerson/`** — the whole C++ module we added to a project that was **Blueprint-only**
  and therefore could not host a `UPROPERTY` at all: two `.Target.cs`, the `Build.cs`, the module
  header/cpp, and the `.uproject` with its `Modules` entry. ⚠ On the real project,
  `UE418_3rdPerson.uproject.pre-cpp.bak` is the exact undo.
* **Nothing for 4.15 / 4.23 / 4.27** — we added no module there, only the fixture, and the fixture
  is already version-controlled at `tools/ue-sample/ue4-delegate-fixture/`. Its installed form is
  **derived**: `install.py` writes the same pair plus, in a module-wide-PCH module only, one
  `#include "<Module>.h"` line. Mirroring the derived copy would be a second thing to keep in
  step, which is the failure this repo keeps finding.

## Reconstructing on a new machine

```bash
# 1. the module, for UE418_3rdPerson only (the other three already have one)
#    copy this directory's Source/ and .uproject over the project

# 2. the fixture, for each project
py tools/ue-sample/ue4-delegate-fixture/install.py --project <Project>

# 3. build, package, and CHECK THE CLASS ARRIVED -- an exit code is not evidence
py tools/ue-sample/repackage.py --engine <ver> --project <Project> --configs Development,Shipping
py tools/verify/d4b_pad_survey.py --expect-pad 0
```

⭐ **Package Shipping too, and prefer it**: every real title measured in this repo is a Shipping build, so a Development-only result has the population backwards.

⚠ 4.23 additionally needs `--pin-compiler 14.16.27023` — its UBT maps toolset → VS version and
rejects a VS2022 toolset outright. And both 4.23 and 4.27 need `AutomationTool.exe` called
directly, which `repackage.py` does for them; `RunUAT.bat` cannot start on those installs.
