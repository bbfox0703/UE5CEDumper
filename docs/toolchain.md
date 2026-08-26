# Toolchain — what a machine needs, and why

The companion to [`tools/bootstrap.py`](../tools/bootstrap.py). **This file is the reasoning;
the script is the mechanism.** Read a row here when the script tells you something is missing
and you want to know whether you actually care.

> 🚀 **Just want to get going?**
> ```bat
> bootstrap.cmd
> ```
> That is `py tools/bootstrap.py --check` — it installs nothing and prints a table of what you
> have and what you are missing. Add `--install` once you have read what it intends to do.
>
> | | |
> |---|---|
> | `bootstrap.cmd` | the **develop** set — `build,gates,re`. This is the default *because the point of the script is that nobody remembers the list*, so it errs towards "everything I actually use". |
> | `bootstrap.cmd --tiers build` | the narrow case: a CI box, or a machine that only compiles |
> | `bootstrap.cmd --all` | every tier, including Cheat Engine and the GitHub CLI |
> | `bootstrap.cmd --dry-run` | print the exact commands it *would* run |
> | `bootstrap.cmd --install --verify` | install, then prove it with the 13 gates |

-----

## 0. The one thing to get right

⚠⚠ **The winget id for Visual Studio 2026 has NO YEAR in it.**

| id | what it actually is |
|---|---|
| `Microsoft.VisualStudio.Community` | ✅ **Visual Studio Community 2026** (18.9.1, moniker `vs18-community`) |
| `Microsoft.VisualStudio.2026.Community` | ❌ **does not exist** — `winget show` returns "no package found" |
| `Microsoft.VisualStudio.2022.Community` | the *old* one (17.14.x) |

2017 / 2019 / 2022 carry the year; 2026 dropped it. A script written from the 2022 pattern
silently installs nothing, or installs VS2022 — and VS2022 is not what this is built and
CI-tested on.

⚠ **Never parse winget's printed output.** It is localized — on this machine it answers in
Chinese (`版本:`, `未找到符合輸入條件的套件。`). Use exit codes, and prefer a functional probe
of the tool itself over asking the package manager what it thinks it installed.

-----

## 1. Tiers — install only the tier you need

| Tier | flag | You want to… | Roughly |
|---|---|---|---|
| **A — build** | `build` | compile the DLL + UI, run `build.ps1 -Mode Publish` | VS2026, .NET 10, Git, submodules |
| **B — gates** | `gates` | run `py tools/check_all.py` and `dotnet test` | Python 3, (+ Lua for the CE-script rigs) |
| **C — re** | `re` | author AOB signatures, run the Ghidra sweep, disassemble a game, read a crash dump | Ghidra + JDK 21, capstone / pefile / numpy, WinDbg, sqlite3, Rust + patternsleuth, cloc |
| **D — live** | `live` | drive a real game with Cheat Engine | CE 7.7+, the AOBMaker plugin, the games |
| **E — contrib** | `contrib` | open PRs the way this repo does | GitHub CLI, PowerShell 7 |

⭐ **`A + B + C` is the DEFAULT, and it is the "I can develop here" set** — not the minimum to
compile. The narrower `--tiers build` exists for a CI box or a machine that only compiles;
everything in C is what the day-to-day work actually reaches for, which is precisely the list
this script exists because nobody remembers.

Tiers **D** and **E** are opt-in (`--all`): D needs games and licences, and E is only for
opening PRs.

-----

## 2. Tier A — build the thing

Everything here is REQUIRED. `build.ps1 -Mode Publish` fails without it.

| Tool | Route | Notes |
|---|---|---|
| **Git for Windows** | winget `Git.Git` · **admin** | Not just SCM — `dll/CMakeLists.txt` shells `git rev-parse` at **configure** time, and one CI gate shells `git ls-files` with no try/except. |
| **Submodules** | `git submodule update --init --recursive` | `vendor/minhook`, `vendor/zydis`, and zydis's own nested `zycore`. `vendor/nlohmann/json.hpp` is **committed** — there is no third submodule to miss. |
| **Visual Studio 2026 Community** | winget `Microsoft.VisualStudio.Community` · **admin** | Supplies cl / link / lib / **ml64** / rc, the Windows SDK, **and CMake + Ninja**. |
| **.NET SDK 10.0.x** | winget `Microsoft.DotNet.SDK.10` **or** the VS component — pick **one** · **admin** | `build.ps1` hard-exits before *any* C++ phase if `dotnet` is absent, so even `-Target DLL` needs it. |
| **Windows PowerShell 5.1** | in-box, cannot install | `build.cmd` spawns `powershell`, which on Windows is always 5.1. See §7. |
| **nuget.org reachable** | — | `PublishAot=true` restores `Microsoft.DotNet.ILCompiler` at publish time. A private-feed-only machine fails with a *restore* error that reads like a toolchain error. |

### ⚠ The VS component list is load-bearing — `--add Workload.NativeDesktop` alone is not enough

Verified against this machine's own VS catalog
(`%ProgramData%\Microsoft\VisualStudio\Packages\_Instances\*\catalog.json`):

| component | dependency type of `Workload.NativeDesktop` |
|---|---|
| `Microsoft.VisualStudio.Component.VC.Tools.x86.x64` | **Recommended** |
| `Microsoft.VisualStudio.Component.VC.CMake.Project` | **Recommended** |
| `Microsoft.VisualStudio.Component.Windows11SDK.26100` | **Recommended** |

An unattended `--add Microsoft.VisualStudio.Workload.NativeDesktop` installs only the workload's
**Required** tier, which is IDE plumbing — **no compiler, no CMake, no SDK**. The four ids live
in [`.vsconfig`](../.vsconfig) so the script and the IDE read the same list.

⛔ **Do not use `--includeRecommended`** — the workload has ~18 recommended entries and drags in
ARM64 toolsets, ATL, ASAN, Vcpkg, IntelliCode. This repo is x64-only.

**Fresh install:**

```bat
winget install --id Microsoft.VisualStudio.Community --exact --source winget --override "--quiet --wait --norestart --config .vsconfig"
```

**Already have VS2026, missing components:**

```bat
"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vs_installer.exe" modify --installPath "<vswhere -latest -property installationPath>" --config .vsconfig --quiet --norestart
```

### ⚠ VS **Build Tools** will not be detected

`build.ps1`'s two `vswhere` calls pass neither `-products *` nor `-prerelease`, and vswhere's
documented default product filter is Community/Professional/Enterprise. A BuildTools-only or
Insiders-only machine has a complete toolchain and still dies with
*"No Visual Studio installation found (need C++ Desktop workload)"*.

The repo disagrees with itself here: `tools/verify/build_dll.py` passes `-products * -prerelease`
and `tools/verify/compile_sdk_header.py` passes `-products *`, while `build.ps1` passes neither.
Until that is reconciled, **install the Community SKU**. `bootstrap.py` reports a BuildTools-only
machine as `MISSING-INVISIBLE` rather than "present", because that is what `build.ps1` will see.

-----

## 3. Tier B — gates and tests

| Tool | Required? | Route | Notes |
|---|---|---|---|
| **CPython 3.12 + the `py` launcher** | REQUIRED | winget `Python.Python.3.12` **and** `Python.Launcher` · user scope, no admin | Every doc and CI line spells `py`, not `python`. |
| *(pip packages)* | — | **none** | ⭐ **Every gate is stdlib-only.** There is no `pip install` step for Tier B. |
| **Lua 5.4** | OPTIONAL | winget `DEVCOM.Lua` · user scope | Runs the CE-Lua rigs in `scripts/tests/`. Deliberately **not** wired into CI — a step that silently skips when its tool is missing is the defect those rigs exist to prevent. |

⚠ **The `python` alias trap.** On a fresh Windows box, `python` resolves to the WindowsApps
App-Execution-Alias stub: `Get-Command python` *succeeds* while no interpreter exists. Detection
must assert that `py --version` prints a real version, never that a command named `python` exists.

-----

## 4. Tier C — offline RE / corpus

⭐ **In the DEFAULT tier set**, even though every row is marked `opt` in the script's table.
The two words mean different things and the distinction is the point: `opt` means *the build and
the gates do not need it*, so a missing one never blocks verification — but you almost certainly
need it to work on this project, so the script checks for it by default and tells you it is gone.
Nothing here is touched by `build.ps1 -Mode Publish` or `py tools/check_all.py`.

| Tool | Route | Serves |
|---|---|---|
| **JDK 21** (a JDK, not a JRE) | winget `Microsoft.OpenJDK.21` · admin | Ghidra's runtime. Must be a JDK — Ghidra compiles `tools/ghidra/*.java` at runtime. Floor is hard at 21. |
| **Ghidra 12.x PUBLIC** | **manual download** + unzip + set `GHIDRA_HOME` | Not in winget. Plain unzip, no admin. |
| **bash ≥ 4.3** | ships with Git | `tools/ghidra/sweep.sh` is bash-specific (`BASH_SOURCE`, arrays, `wait -n`). Do **not** install MSYS2/Cygwin — a second `bash` on PATH is a new failure mode. |
| **pyghidra** | Ghidra's **own** venv — never plain PyPI | `pip install pyghidra` succeeds and then fails at `pyghidra.start()` without a Ghidra install. |
| **capstone + pefile** | `py -m pip install capstone pefile` | `tools/pe/disasm_function.py` — step 2 of the non-standard-UE playbook. |
| **numpy** | `py -m pip install numpy` | Rebuilding the AOB n-gram index (`tools/pe/build_ngram_index.py`). The index itself is git-tracked, so this is only needed to REGENERATE it — but it is in the default develop set, because "I forgot I needed numpy" is exactly the failure this script exists to prevent. |
| **WinDbg** | winget `Microsoft.WinDbg` | Reading the crash dumps behind `tools/pe/minidump_triage.py`, and `gflags` page-heap runs. ⚠ `gflags` itself ships in the **Windows Kits** Debuggers folder that the VS SDK component already installs — not with WinDbg's Store package. |
| **sqlite3 CLI** | winget `SQLite.SQLite` | Opening `%LOCALAPPDATA%\\UE5CEDumper\\Snapshots\\*.db` by hand. ⚠ **Nothing in the repo needs it** — the app reaches SQLite through NuGet and Python through its bundled stdlib module. Convenience only, and listed here so it is not mistaken for a dependency. |
| **Rust (rustup) + nightly** | winget `Rustlang.Rustup` · user scope | Building patternsleuth, which pins `channel = "nightly"`. |
| **patternsleuth** | `git clone` **outside** `vendor/` | Not on crates.io and has no releases. Do not `cargo install --git` — rustup reads `rust-toolchain.toml` from the cwd, so the pin would be ignored. |
| **Cheat Engine SOURCE clone** | `git clone` (read-only) | Keeps the `docs/ce-*.md` `file:line` references honest. Never built, so **no Lazarus/FPC**. |
| **cloc** | winget `AlDanial.Cloc` | ⚠ casing matters — `AlDanial.cloc` is not found. |

-----

## 5. Tier D — live-game verification

| Tool | Route | Notes |
|---|---|---|
| **Cheat Engine 7.7+** | **manual download** · admin | Not in winget. Prefer the **GitHub Releases** installer — cheatengine.org's carries bundled-offer checkboxes. Install to the default `%ProgramFiles%\Cheat Engine`: at least one rig hardcodes `cheatengine-x86_64-SSE4-AVX2.exe` there. |
| **AOBMaker CE plugin** | build from a **private** sibling repo | Needs `gh auth login` with the maintainer's account. It is a **native C++ CMake target** — reuses Tier A's toolchain, adds nothing new. |
| **Steam / Epic / the games / DumperTest** | human only | Accounts, licences, GB. |

-----

## 6. Tier E — contributing

| Tool | Route | Notes |
|---|---|---|
| **GitHub CLI** | winget `GitHub.cli` · admin, then `gh auth login` | For `gh pr create --base main --head dev` → `gh pr merge N --merge`. Never `--admin`. **Skip on a build-only machine** — no repo script invokes `gh`. |
| **PowerShell 7** | winget `Microsoft.PowerShell` · msixbundle, **no admin** | Convenience only. ⛔ Must not be wired into the build — see §7. |

-----

## 7. Two PowerShells, and why the build uses the old one

`build.cmd` spawns `powershell` (Windows PowerShell **5.1**) on purpose, even though the
maintainer's interactive shell is pwsh 7. Do not "fix" it:

* `Microsoft.VisualStudio.DevShell.dll` is a .NET Framework assembly.
* `build.ps1` pins `[Console]::OutputEncoding` before configuring CMake, and that pin is what
  keeps `msvc_deps_prefix` consistent — get it wrong and **a header edit silently stops
  triggering a rebuild**.

⚠ 5.1 also has a trap of its own: `Get-FileHash` is a *script function* there (a compiled cmdlet
under pwsh 7), so it needs an on-disk module load that an AV can block. `build.ps1` computes
SHA-256 directly via `System.Security.Cryptography` for that reason.

-----

## 8. Do NOT install these — they are already inside something else

Installing both halves of a row is a real failure mode, not just waste.

| Provided by | Do **not** separately install | What goes wrong |
|---|---|---|
| VS2026 → `VC.CMake.Project` | `Kitware.CMake`, `Ninja-build.Ninja` | **The worst one.** `build.ps1` enters the DevShell, which *prepends* VS's paths — so it uses VS's cmake while a bare `cmake` uses the winget one. They then fight over one `build/` dir, and the configuring cmake's absolute path is baked into `build.ninja` + `CMakeCache.txt`. VS ships cmake **4.3.1-msvc1** and ninja **1.13.2**, well clear of the 3.25 floor. |
| VS2026 → `VC.Tools.x86.x64` | any standalone MASM | `ml64.exe` is a *file inside the toolset*; there is no standalone package. It is genuinely needed — the proxy DLLs' jmp-thunks are `.asm`, and the build always configures them, so a missing ml64 kills the whole configure. |
| VS2026 → `Windows11SDK.*` | winget `Microsoft.WindowsSDK.*` | Same payload, same `Windows Kits\10` tree, multiple GB duplicated. |
| the VS **Installer** | winget `Microsoft.VisualStudio.Locator` | vswhere is dropped by the Installer regardless of workloads. Worse: `build.ps1` probes **PATH first**, so a winget portable copy *shadows* the real one. Repair-only fallback. |
| .NET SDK 10 | any MSBuild | MSBuild ships inside the SDK. |
| Git for Windows | MSYS2 / Cygwin | A second `bash` on PATH is a new failure mode. |
| Git for Windows | *nothing* — you still need it | VS ships MinGit, but it is not on PATH outside VS-managed shells and has no Git Bash. |
| Ghidra's bundled venv | `pip install pyghidra` | Imports fine, then fails at `pyghidra.start()`. |
| CE's bundled Lua 5.3 DLL | — | A DLL + import lib, not an interpreter, and the wrong minor version. |

### Explicitly **not** dependencies

**7-Zip** (zero references — release packaging uses `Compress-Archive`), **SQLite CLI** (nothing
in the repo invokes it — it is offered in tier C purely to open a snapshot `.db` by hand),
**psutil / pywin32** (deliberately absent — 60+ files
use `ctypes` precisely so the verify rigs need zero installs), **any dotnet workload or global
tool** (there is no `dotnet-tools.json`), **IDA / x64dbg** (mentioned only as contributor
experience, not tooling).

⛔ **Never run `dotnet restore --force` or "update packages".** SkiaSharp and HarfBuzzSharp are
version-pinned because NuGet structurally cannot enforce Avalonia's native ABI; restoring latest
produced `STATUS_HEAP_CORRUPTION` at runtime. Both the csproj and `build.ps1` hard-fail on
forbidden `PackageReference`s.

-----

## 9. Order, admin, and the PATH refresh

```
0. PREFLIGHT  — PowerShell 5.1? winget? elevated? network?     (installs nothing)
1. Git                              ADMIN    → PATH refresh
2. VS2026 + .vsconfig components    ADMIN    → (no refresh: build.ps1 enters the DevShell itself)
3. .NET SDK 10                      ADMIN    → PATH refresh
4. Python 3.12 + py launcher        user     → PATH refresh
5. git submodule update --init --recursive
   ── Tier A + B done ──
6. Lua (user) · JDK 21 (ADMIN) → Ghidra unzip → pyghidra from Ghidra's venv
   rustup (user) → nightly → patternsleuth clone · pip capstone pefile · cloc
7. Cheat Engine (ADMIN, manual) → AOBMaker plugin
8. gh (ADMIN) → gh auth login · pwsh 7 (user)
```

⚠ **PATH refresh is not optional.** winget installers write the *registry* PATH; a running
process's environment does not update. `bootstrap.py` rebuilds `PATH` from
`HKLM\…\Session Manager\Environment` + `HKCU\Environment` after each marked step. If it cannot,
it stops and tells you to open a new shell — it will never report a false MISSING on a stale PATH.

**Elevation:** the script checks at preflight. If a selected tier has an admin-requiring missing
item and you are not elevated, it stops with exit **10** and prints the commands — it will *not*
install the user-scope half and leave you with a half-built environment.

-----

## 10. What cannot be scripted

| Item | Why | What you do |
|---|---|---|
| **winget itself** | Comes from the Microsoft Store (App Installer). There is no bootstrapping the bootstrapper. | Install "App Installer" from the Store, or the `Microsoft.DesktopAppInstaller` msixbundle from the winget-cli releases page. |
| **Elevation** | The VS / .NET / Git / JDK / gh / CE installers all elevate. | Re-run from an elevated shell. |
| **Cheat Engine** | No winget package at all. | Download from the **GitHub Releases** page (offer-free), run elevated, keep the default path. |
| **Ghidra** | No winget package; ~400 MB zip; needs `GHIDRA_HOME` persisted. | Unzip to a **space-free** path, `setx GHIDRA_HOME …`. ⚠ Do not hard-pin a version — always rebuild artifacts on the installed Ghidra. |
| **pyghidra** | Created by Ghidra, not pip. | Run `<GHIDRA_HOME>\support\pyghidraRun.bat` once, or install offline from `<GHIDRA_HOME>\Ghidra\Features\PyGhidra\pypkg\dist`. |
| **AOBMaker** | The sibling repo is private. | `gh auth login`, clone, `cmake --build`, copy the DLL into CE's `plugins\` (elevated). |
| **`vendor/UnrealEngine`** (`sync_tools.ps1`) | The Epic repo needs your GitHub account **linked to an Epic Games account**; without it the clone fails with a bare auth error. | Link the accounts, then clone by hand. ⚠ **Nothing builds against it** — reference only. `bootstrap.py` deliberately skips `sync_tools.ps1`. |
| **`gh auth login`** | Interactive browser OAuth. | Run it once. |

-----

## 11. Proving the environment works

Cheap first. The script never runs stage 2 when a required Tier A tool is missing — that failure
surfaces as `fatal error C1083` / `LNK2019` / ILCompiler `exited with code 9009`, all of which
read as "the repo is broken" rather than "you are missing X".

**Stage 1 — the gates (~7 s).** Needs only Python + Git; no VS, no .NET SDK, no CMake.

```bat
py tools/check_all.py
```

⚠ **Derive the gate count from its own `N gate(s) run` line.** It was 4, then 12, then 13. Never
assert a literal.

**Stage 2 — the build (minutes).**

```bat
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Mode Publish -NoBumpBuildNumber
```

* `-NoBumpBuildNumber` is **mandatory** — a build number consumed on one machine is gone.
* Success = exit 0 **and** `dist\UE5DumpUI.exe` at **~54 MB**.
* ⚠⚠ **A ~107 MB `dist\UE5DumpUI.exe` is a FAILURE of this check**, not a pass — that is the
  non-trimmed build. Only `-Mode Publish` leaves an AOT-trimmed `dist\`.
* ⛔ Never verify with `-Target Test` or `-Target UI`. Both publish the UI and overwrite
  `dist\UE5DumpUI.exe` with the non-trimmed build — the cheapest way to destroy the shippable
  binary.

**Exit codes**

| Exit | Meaning |
|---|---|
| 0 | Every REQUIRED tool in the selected tiers is present, and every selected verification stage is green |
| 1 | A REQUIRED tool is missing or its install failed — **neither** verification stage runs |
| 2 | Tools complete, but a verification stage failed |
| 3 | Required complete; an OPTIONAL tool in a selected tier is missing (usually a manual-only item) |
| 10 | Preflight refused (no PowerShell 5.1, no winget, not elevated, no network) |

-----

## 12. Measured on the maintainer's machine (2026-08-26)

A reference point, not a requirement — the repo pins almost none of these.

| | |
|---|---|
| VS | Community **18.9.12120.119** at `C:\Program Files\Microsoft Visual Studio\18\Community` (⚠ VS2022 17.14 also present) |
| MSVC toolset | 14.51.36231 (also 14.44, and 14.38 for the DumperTest fixture) |
| VS-bundled CMake / Ninja | **4.3.1-msvc1** / **1.13.2** |
| Windows SDK | 10.0.26100.0 — the repo pins none; CMake takes the newest installed |
| .NET SDK | **10.0.400** (`global.json` has **no** `sdk` block — nothing pins it locally) |
| Python | **3.12.10**, launcher 3.13, user scope · capstone 5.0.9, pefile 2024.8.26, numpy 2.5.2 |
| Java | OpenJDK **21.0.12** |
| Ghidra | **12.1.2** PUBLIC at `D:\Tools\ghidra_12.1.2_PUBLIC` |
| Lua / Git / gh / winget / pwsh | 5.4.6 / 2.55.0 / 2.98.0 / 1.29 / 7.6.5 |
| Gates | 13, stdlib-only, ~7 s |

⚠ The measured **version floor** for the Python tooling is **3.9** (runtime builtin generics
without `from __future__ import annotations` in one rig); nothing uses `match`. 3.12 is what is
tested.
