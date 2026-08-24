# DumperTest — the UE sample UE5CEDumper is verified against

**What this is.** The C++ source for a stock **UE 5.4 Third Person** project carrying one actor
whose properties exist solely to be found by this dumper. It is not a game and not an AOB oracle —
[reference-builds.md](../../docs/reference-builds.md) covers those. This answers a third question:
*"does the dumper read this property type correctly?"*, with the answer written down in advance.

**Why it exists.** A large share of [todo.md § Pending live-game verification](../../docs/todo.md)
is blocked not on effort but on **finding a game that happens to contain the right UPROPERTY**:

| item | ⬜ since | the blocker, verbatim |
|---|---|---|
| Value Search `TSet`/`TMap` (V1a) | build **927** | *"needs a live game with such UPROPERTYs"* |
| Value Search `TOptional` (V1c) | build **942** | *"the field walk needs a live game with optional UPROPERTYs"* |
| Value Search `NumericAll` bytes | build **796** | *"a value that genuinely lives in an Int8Property"* |
| B28 CJK FText mojibake | build **2599** | *"any game with Chinese/Japanese UI text"* |
| B8 Fly deferred restore | build **2596** | *"needs a game that actually goes quiet when backgrounded"* |

Every one of those is free here, on demand, with a known expected value. A commercial title can
never promise "an even-length FText containing U+4E00 is on screen right now".

-----

## ⛔ THIS DIRECTORY IS A MIRROR. RE-SYNC IT BEFORE YOU TRUST IT, AND AFTER YOU EDIT THE PROJECT

The project that actually gets built and packaged lives at **`D:\Unreal Projects\DumperTest`** on the
maintainer's machine. This directory is a copy, and on **2026-08-24 it was found to be four days
stale** — and the gap was not cosmetic.

**What the drift cost.** The 08-23 package added a whole spawner (`Spawn_Holders`, `Spawn_Decoys`,
`Spawn_DestroyHolders`, `Spawn_ManyComponents`, `Spawn_RecycleChurn`, `Spawn_LastRecycledAddr`,
`Spawn_LateInstance`, `Spawn_Generation`) plus `ADumperTestDerivedHolder` / `ADumperTestHolderDecoy`
— **36 declarations**, and the classes that make Solide L3 falsifiable. None of it was here.
**Two separate sessions grepped this directory, concluded the fixture did not exist, and planned a
C++ authoring task that was already done.** One of them then "corrected" an agent who had it right.

⚠ **And the real hazard was worse than a wasted plan: this copy was BUILDABLE.** The section below
invites a rebuild, and rebuilding from a stale mirror silently produces a package **without** the
spawner — destroying the fixture that five verification rows depend on.

### The rules that follow from that

1. **Never answer "does fixture X exist?" by grepping this directory.** Ask the running game:
   `list_all_functions` / `walk_class` over the pipe. That is the only source that describes the
   binary you are actually testing.
2. ⚠ **Probing the packaged `.exe` needs care too**: **UClass names are UTF-16LE in there, while
   UFunction names are ASCII.** Measured — `DumperTestHolder`: ascii **0**, utf16le 2;
   `Spawn_Holders`: ascii 1, utf16le 0. So `grep -a DumperTestHolder <exe>` finds nothing and reads
   as *"the class does not exist"*. Use `.encode('utf-16-le')`.
3. **After editing the live project, copy the changed files back here in the same sitting.** Until
   that happens the fixture exists only on one machine's D: drive and inside a binary — not in git,
   and not on the other development PC.
4. The stock Third-Person template files (`DumperTest.cpp/.h`, `DumperTestCharacter.*`,
   `DumperTestGameMode.*`) are deliberately **not** mirrored — they are whatever the template
   generates. Only the dumper-specific sources belong here.

-----

## Build it (one pass, ~20 minutes)

> **The project MUST be named `DumperTest`.** The sources use the `DUMPERTEST_API` export macro,
> which UHT derives from the module name. If you want another name, rename the macro in all four
> files to `<YOURNAME>_API` first.

1. **UE 5.4 Editor → New Project → Games → Third Person → C++** (not Blueprint) → name it
   `DumperTest` → Create. Let it compile and open once, then close the editor.
2. Copy `DumperTest/Source/DumperTest/*.h` and `*.cpp` from this folder into the generated
   `Source/DumperTest/`.
3. **Nothing to merge into `Config/`.** There is deliberately no ini change — an earlier draft put
   `t.IdleWhenNotForeground=1` in `DefaultEngine.ini` and it made the project **impossible to
   package**. See *The cook-breaking ini* below; the setting now lives in code behind a switch.
4. **Build from the command line. Do not build the solution in Visual Studio** — see the trap below.

   ```bash
   "C:\Program Files\Epic Games\UE_5.4\Engine\Build\BatchFiles\Build.bat" DumperTestEditor Win64 Development -Project="D:\Unreal Projects\DumperTest\DumperTest.uproject" -WaitMutex
   ```

   *Verified 2026-08-05: 27 actions, **30 s**, zero errors.* Then double-click the `.uproject`.
   (Double-clicking it first also works — the editor offers to rebuild out-of-date modules and takes
   the same path.)

   > ### ⚠ `Build.bat … exit 6` and a wall of "找不到 …csproj 的專案資訊"
   >
   > **Symptom:** building the UE-generated `.sln` fails on `DotNetPerforceLib` /
   > `EventLoopUnitTests` with *"目標 Framework 'net6.0' 已不受支援"* and project-info-not-found for
   > every `EpicGames.*` shared library.
   >
   > **Cause:** those are the **engine's own C# Programs**, which the generated solution includes and
   > which target **net6.0**. A machine whose only SDK is .NET 8/9/10 has no net6.0 targeting pack, so
   > NuGet cannot restore them. Visual Studio **2026** makes it worse by running a one-way upgrade on
   > the solution first (`UpgradeLog.htm` + `Backup\` appear next to the `.uproject`).
   >
   > **None of it is needed to build a game module.** UBT ships **precompiled**
   > (`Engine/Binaries/DotNET/UnrealBuildTool/UnrealBuildTool.dll`) and UE bundles its own .NET at
   > `Engine/Binaries/ThirdParty/DotNet/6.0.302/`. The command above touches neither the solution nor
   > the system SDK.
   >
   > **Do NOT install the .NET 6 SDK for this.** It is end-of-life, and it would only satisfy engine
   > programs you will never run.
   >
   > **The C++ toolchain was never the problem.** UBT reported *"Using Visual Studio 2022 14.38.33145
   > toolchain (…\Microsoft Visual Studio\18\Community\VC\Tools\MSVC\14.38.33130)"* — it found a
   > VS2022-era MSVC inside the VS 2026 install and was happy. UE 5.4's UBT knows compilers only up to
   > `VisualStudio2022` (`UEBuildWindows.cs:138`), so if you ever *do* need the IDE, open the solution
   > with **VS 2022** and build **only the `DumperTest` project** (right-click → Build), never
   > *Build Solution*. The `.sln` is disposable — regenerate it from the `.uproject` context menu.
5. Press Play. **Five lines appear top-left once a second:**

   ```
   [DumperTest] frames=1042   TickCount=17  (frames must ALWAYS climb; TickCount climbs only if the 1 Hz timer runs)
   [DumperTest] Health.CurrentValue=83  (must fall, wraps to 100)
   [DumperTest] Health.BaseValue=100  FrozenInt=424242  (both must NOT move)
   [DumperTest] F32_Ticking=826.500 (falls 10.25, wraps)  F64_Ticking=20004.250 (rises 0.25)
   [DumperTest] native (non-UPROPERTY): RawInt_Ticking=700119  RawFloat_Ticking=245.500  RawDouble_Ticking=50008.500
   ```

   **`frames` is on a different clock from everything else, deliberately.** It is driven by
   `Tick`; the values are driven by the 1 Hz timer. The first draft drew the heartbeat *from the
   timer* — so if the timer was dead the screen stayed blank, which looks exactly like the readout
   itself being broken. A diagnostic must not be driven by the thing it is diagnosing.

   | on screen | means |
   |---|---|
   | `frames` climbing, `TickCount` climbing | sample is fully alive — a `0 results` belongs to the scan |
   | `frames` climbing, `TickCount` **frozen** | the 1 Hz timer is dead — the sample's bug |
   | **nothing at all** | a genuine failure **in BOTH configurations** — wrong package, `-DumperTestNoHud` still on the command line, the actor never spawned, or the HUD never installed. (Before build 2719 a blank Shipping screen was *expected*; it no longer is — see below.) |

   > ### The heartbeat is drawn by `ADumperTestHUD`, because the obvious way does not work in Shipping
   >
   > It used to call `GEngine->AddOnScreenDebugMessage`. That prints in Development and is a
   > **no-op in a Shipping package** — verified 2026-08-05 against the installed 5.4 source:
   >
   > ```
   > Engine/Source/Runtime/Engine/Private/UnrealEngine.cpp:11397
   > void UEngine::AddOnScreenDebugMessage(uint64 Key, ...)
   > {
   > #if !(UE_BUILD_SHIPPING || UE_BUILD_TEST)
   > ```
   >
   > The whole body is compiled out, so the message never enters the list and
   > `bEnableOnScreenDebugMessages` / `GAreScreenMessagesEnabled` cannot bring it back. That
   > difference was misread twice — once as "Shipping strips the readout", once as "config" —
   > before anyone opened the function.
   >
   > **`AHUD` is the path that is not gated**, and every step was checked in the same source
   > before writing it: `AHUD::PostRender` (`HUD.cpp:149`) → `DrawHUD` (`:638`) → `DrawText`
   > (`:929`), none carrying a `UE_BUILD_*` gate, with `bShowHUD = true` from the ctor (`:75`).
   > **Development uses the same path on purpose** — a readout that works in one configuration
   > and not the other is what hid this for two rounds.
   >
   > The HUD is installed at runtime by `ADumperTestActor::EnsureHeartbeatHud()` via
   > `APlayerController::ClientSetHUD` (`PlayerController.h:1212`), re-asserted from **`Tick`** —
   > never from the 1 Hz timer, which is the thing the readout exists to measure; installing from
   > there would make a dead timer show as a blank screen again. **No `.uproject`, GameMode or
   > other binary asset is touched**, which is the same
   > reason the actor is spawned by a subsystem rather than placed in a level.
   >
   > ⚠ **`ClientSetHUD` destroys the current HUD** (documented on the declaration). The Third
   > Person template ships no custom HUD so nothing is lost; a project that has one would lose
   > it. `-DumperTestNoHud` opts out of the whole thing.
   >
   > Whatever the screen says, `TickCount` at `+0x518` in Live Walker is authoritative in both
   > configurations.

   **That readout IS the health check.** `ADumperTestActor` is invisible by design — no mesh, no
   HUD, no gameplay — so without it *"is the timer actually running?"* cannot be answered without
   attaching the dumper and walking the object, and **a scan that finds nothing changed looks
   exactly like a game that is not ticking**. That ambiguity cost several rounds of log forensics
   on 2026-08-05 before the readout existed. If the numbers move, the sample is alive and a
   `0 results` belongs to the scan; if they are frozen, the sample is the problem.
   `-DumperTestNoHud` suppresses it for a clean screenshot.

   > ⚠ **`[DumperTest] ADumperTestActor ready at 0x…` does NOT print in a Shipping package.**
   > `UE_LOG(..., Warning, ...)` is compiled to nothing there: the Shipping branch of `Build.h:328`
   > sets `NO_LOGGING = !USE_LOGGING_IN_SHIPPING` (0 by default), and `LogMacros.h:146-158` reduces
   > `UE_LOG` to Fatal-only under `NO_LOGGING`. This README claimed the opposite ("Warning level so
   > it survives a Shipping build's default log verbosity") until 2026-08-05 — the **third** wrong
   > assertion in this file about what a Shipping build keeps, all three made by inferring a gate
   > instead of opening it. The line is real and useful in Development/Test only. Set
   > `bUseLoggingInShipping = true` in the Target.cs if you want it in Shipping too.
6. **Package twice**: Platforms → Windows → Build Configuration → **Shipping**, Package Project;
   then again with **Development**.

> **Do NOT package into `D:\UE_Analyze_Data\Varies Version builds\`.** `inventory_builds.py`
> and `preflight.py` treat that tree as the AOB corpus and CI asserts its row counts; a new folder
> there would drift them. Use a sibling such as `D:\UE_Analyze_Data\DumperTest\5.4\Shipping\`.

**Launch it windowed** so alt-tab is one keystroke — several checks depend on backgrounding it:

```bash
DumperTest.exe -windowed -ResX=1280 -ResY=720
```

Add **`-DumperTestIdle`** *only* for the B8 / Grausam pair — it makes the game thread stall whenever
the window loses focus, which is also what makes every game-thread dispatch (Teleport, invoke, POV)
time out while you are working in the dumper UI. Leave it off for everything else. The startup log
says which mode you are in either way:

```bash
DumperTest.exe -windowed -ResX=1280 -ResY=720 -DumperTestIdle
```

> ### ⚠ The cook-breaking ini — do not put this back
>
> `t.IdleWhenNotForeground` is registered **`ECVF_Cheat`**, and `ConfigUtilities.cpp:245` refuses a
> cheat cvar from any ini except `consolevariables.ini` with an `ensureMsgf(false, …)`. In the
> editor that is a message you can ignore; **in a cook it is 22 errors and
> `Cook failed / ExitCode=25 (Error_UnknownCookFailure)`**. Measured 2026-08-05: it was the *only*
> distinct error in a 198 KB packaging log.
>
> `ConsoleVariables.ini` is not the escape hatch either — `ConfigCacheIni.cpp:4305` reads it from
> **`FPaths::EngineDir()`**, so it is a machine-wide developer file shared by every project and it
> is never packaged.
>
> Setting it **from C++** is a different path and is not blocked. `DISABLE_CHEAT_CVARS`
> (`Build.h:416`) hides cheat cvars from the **console** in Shipping — `IConsoleManager.h` says
> *"hidden in the console and cannot be changed by the user"* — and `ProcessUserConsoleInput` is the
> only place that refuses them. So `UDumperTestSubsystem` sets it with `ECVF_SetByCode`, gated on
> the switch.

**Why two configs.** Shipping vs Development on the *same source* is the config-only A/B that
[todo.md's self-built-samples section](../../docs/todo.md) calls the highest-value first cell: it
*measures* which reflected UFunctions the cooker hollows out (`UCheatManager::Fly/God/Slomo` invoke
successfully and do nothing in Shipping) instead of rediscovering it per game.

-----

## Where the packaged binary lives — NOT in git, and not in CI

**Decided 2026-08-05. Only the source is committed; the package is not.** Both halves have a number
behind them.

**Not in git.** A packaged 5.4 ThirdPerson set measures **583 MB** (the three configs of the existing
5.4 corpus row); one Shipping package alone is 100–200 MB. This repo's entire `.git` is **180 MB**, so
committing even one config would roughly double it — permanently, because history cannot be pruned
without a rewrite. And it would be committing a **build artifact whose source is already here**.

The repo already has the right pattern and it is worth copying exactly: the AOB corpus binaries live
**outside** the repo, and what is committed is the small thing that *verifies* them —
`tools/ghidra/identity/` + `memory-maps/`, **668 KB** total. See
[corpus-preservation.md](../../docs/corpus-preservation.md). So:

* **binary** → outside, e.g. `D:\UE_Analyze_Data\DumperTest\5.4\{Shipping,Development}\`
  (**not** in `Varies Version builds\` — `inventory_builds.py`/`preflight.py` treat that tree as the
  AOB corpus and CI asserts its row counts).
* **repo** → this source, plus **`package-identity.json`** — engine version, source commit, build
  command and both exes' SHA-256 — so *"is the package I am testing built from this source?"* has an
  answer. Without it a stale package silently tests yesterday's property zoo, and the failure does
  not look like staleness: it looks like the dumper reading the wrong value.

  ```bash
  py tools/ue-sample/capture_package_identity.py "D:\UE_Analyze_Data\For Testing\DumperTest" --project "D:\Unreal Projects\DumperTest" --check
  ```

  `--check` compares instead of writing, so a rebuilt package is caught in one second **before** a
  test session rather than halfway through one. Drop `--check` to re-record after a deliberate
  rebuild.

  **Pass `--project`.** It is optional only in the sense that the script runs without it. It names
  the UE project the package was actually built from, and it is the only tree the freshness check
  may legitimately consult — the repo working tree cannot stand in, because `git checkout` stamps
  files with the checkout time and never preserves the author's mtime, so a fresh clone always looks
  "newer" than a correct older package. Without `--project` the script now says the check was
  **skipped** rather than inventing a verdict.

  The record also asserts the **absences**, which is the half worth having: `RawInt` / `RawFloat` /
  `RawDouble` must appear **zero** times in either binary. They are the non-UPROPERTY holes the
  Native-C scan exists to find, so if they ever start appearing someone has reflected them and that
  test is dead without anything failing.

  > **The absence question is asked of ASCII only, and whole-word.** A reflected property name
  > reaches the binary as a **narrow** string in `FPropertyParams`; a `TEXT()` literal reaches it as
  > **UTF-16**. `ADumperTestHUD` prints `RawInt_Ticking=%d …` on screen, so all three names *are* in
  > the binary as UTF-16 — and counting both encodings made the script declare
  > *"the Native-C test is dead"* on a **correct** package, on every build since `b3d8593`. The
  > narrow/wide split is the signal that separates *reflected* from *merely printed*; the word
  > boundary stops `RawInt` matching inside `RawInt_Ticking` if a future readout is ever ASCII.

  *Captured 2026-08-12 from commit `270ac0d`: Development 279,211,008 B · Shipping 132,310,016 B,
  every reflected name present in both, all three raw members absent in both, zero problems.*

  > **Trap:** class names are emitted as **UTF-16** (from `TEXT()` in the UHT registration) while
  > property names are narrow strings. An ASCII-only search therefore reports every class name as
  > missing from a Shipping build — the first grep run against this package did exactly that.

**Not in CI, and size is the lesser reason.** CI has no UE 5.4 install (tens of GB), no GPU and no
display — but the real blocker is that **what this sample tests is a live process being injected into
and walked**: a running game, a ticking game thread, the UI or CE attached. That is the same class of
thing [todo.md § Pending live-game verification](../../docs/todo.md) exists for, and the existing
`check_live_verification.py` gate only checks that the register is *well-formed* — it does not run,
and cannot run, the verification itself. Nothing about packaging this sample changes that.

> **What CI could cheaply do, if it is ever wanted (not built):** a source-level drift gate asserting
> the expected-value table in this README still matches the literals in `DumperTestActor.cpp`. That is
> audit #4's 4a root cause — *the report and the reality are computed by different code paths* —
> applied here: change `I32 = 1234567` in the .cpp without changing the table and the next tester
> scans for a number that is not there. ~40 lines of Python, and it would be the seventh gate.

-----

## Reflection is confirmed complete (2026-08-05, build verified)

Read out of the generated `DumperTestActor.gen.cpp` — this is the evidence that the zoo actually
reaches UE reflection rather than merely compiling:

| emitted param type | count | covers |
|---|---|---|
| `FTextPropertyParams` | **9** | the 8 actor FTexts + `UDumperTestPayload::PayloadText` |
| `FStrPropertyParams` | 6 | 4 `Str_*` + `PayloadString` + `Opt_Str_Set`'s inner |
| **`FGenericPropertyParams`** | **4** | **the four `TOptional`s** — `FOptionalProperty` has no dedicated params type; each also emits an `_Inner` (`Opt_Int_Set_Inner` = `EPropertyGenFlags::Int`) |
| `FSet` / `FMap` / `FArray` | 1 / 2 / 2 | `Set_Int`, both maps, `Arr_Int` + `Arr_Struct` |
| `FInt8` / `FInt16` / `FUInt16` / `FInt64` | 1 each | the byte/width families |
| `FBoolPropertyParams` | 4 | 3 bitfields + `bPlainBool` |
| `FByte` / `FEnum` / `FFloat` / `FDouble` / `FStruct` / `FObject` / `FName` | 3 / 1 / 3 / 1 / 2 / 1 / 2 | |

> A grep for these must include digits — `F[A-Za-z]*PropertyParams` silently misses
> `FInt8`/`FInt16`/`FInt64` and makes the byte families look absent.

-----

## What to expect — these are the acceptance criteria

Find the actor with **Instances → `DumperTestActor`**, then open it in Live Walker.

### B28 — FText (the reason this project exists)

Trigger, restated: an FText whose character count is **EVEN** and which contains a character whose
**low byte is 0x00** (一 U+4E00, 最 U+6700, 言 U+8A00, 退 U+9000). In UTF-16LE such a character is
stored `00 4E` — a NUL at an even byte offset.

| field | value | why it is here |
|---|---|---|
| `Text_Even2_OneNull` | 統一 | 2 chars, one U+xx00 — **primary trigger** |
| `Text_Even2_TwoNull` | 一言 | 2 chars, bytes `00 4E 00 8A` — **strongest trigger** |
| `Text_Even4_TwoNull` | 統一言語 | 4 chars, two U+xx00 |
| `Text_Odd3_OneNull` | 走一步 | **CONTROL** — odd length. If only this renders, parity is still being used as an encoding signal |
| `Text_Even6_NoNull` | 日本語テスト | **CONTROL** — even, but no low byte is 00. Even length alone must trigger nothing |
| `Text_Ascii` | `DumperTest FText ASCII` | **CONTROL for the other direction** — a fix that swings to always-UTF-16 breaks this |
| `Text_Localized` | 統一言語 | Same glyphs, different `FTextHistory` (LOCTEXT). Disagreement with `Text_Even4_TwoNull` means the fault is history traversal, not decoding |
| `Text_Empty` | *(empty)* | the empty display-string path |
| `Name_Cjk` | 統一 | FName holding CJK — the FNamePool path (Serie), which is neither reader above |
| `Str_*` | same four strings | **CONTROL GROUP** — FString never had B28. If an `Str_` is wrong too, the fault is not B28 and the FText result means nothing |

**PASS** = every `Text_*` reads as CJK. **FAIL** = short ASCII punctuation soup (`,{1`, `-N?e`).

> **What this does NOT cover:** Star Trek Voyager stores its FText as **UTF-8**, which is a licensee
> deviation no stock UE build produces. That counter-check still needs STVoyager.

### Value Search / containers

| field | value | check |
|---|---|---|
| `Set_Int` | `{1337, 4242, 8888}` | scan **4242** → row renders as `Set[idx]` |
| `Map_NameToInt` | Alpha:111 Beta:222 Gamma:333 | scan **222** → `Map.Value[idx]` |
| `Map_IntToFloat` | 1:1.5 2:2.5 3:3.5 | non-FName key shape |
| `Arr_Int` | `{10,20,30,40,50}` | |
| `Arr_Struct` | 2 × `FDumperTestStat` — `StatName` Attack/Defence, `Value` 7777/6666, `Label` an FText | struct-element container — the deep-descent level, with an FText inside it |
| `Map_I64ToI32` | 600000000001:6001 600000000002:6002 600000000003:6003 | **audit #5 MG1.** pairAlign 8, unpadded pair 12 → stride 24. A build with the defect strides 20, so 6002 / 6003 read as garbage while 6001 looks fine |
| `Map_StrToInt` | StrAlpha:6101 StrBeta:6102 StrGamma:6103 | **MG1, second witness**, different arithmetic — unpadded pair 20 → stride 32 (defect: 28). Two witnesses so one wrong assumption cannot pass both |
| `Map_IntToVec3f` | 1:(6201 6202 6203) 2:(6211 6212 6213) 3:(6221 6222 6223) | **MG3.** `FDumperTestVec3f` is a 4-aligned POD (`X` `Y` `Z` floats, no FText/pointer/double), so its value sits at +4. The defect's size guess reads +8 — **the only container here that is wrong at element 0**, so scanning 6201 fails outright. Also **A4**'s target: a scalar leaf inside a map's struct side |
| `Set_Big` | 9000..9199 (200 entries) with 9005 removed | **A2 + MG2.** 200 entries push the `TBitArray` past its 128-bit inline buffer onto the heap; 9005 is a LOW index (5), so a build reading the frozen inline words still lists it. MG2: the rendered row count must equal the header count |
| `Set_Struct` | (6301 6302 6303) (6311 6312 6313) | **A4**, set side — a struct element whose scalar leaf the Deep pass must reach |
| `Opt_Int_Set` | **24680** | V1c — appears under the optional's field name; Next Scan prunes |
| `Opt_Float_Set` | 99.5 | |
| `Opt_Str_Set` | `OptionalPresent` | |
| `Opt_Int_Unset` | *(unset)* | **NEGATIVE criterion** — a scan for **0** must NOT surface it (the `bIsSet` gate) |

### The C1 spawner — instances ON DEMAND, and the discriminating set

Added 2026-08-23, mirrored here 2026-08-24. Everything below is created by invoking a
`Spawn_*` UFunction on `ADumperTestActor`; nothing exists in bulk at BeginPlay. ⚠ Pass
`parms_size` on the invoke (read it from `walk_functions`) — see the mirror warning at the top.

| field | value | check |
|---|---|---|
| `SpawnedHolders` | `TArray<TObjectPtr<AActor>>` | the GC root for `Spawn_Holders`; without it the holders are collected mid-test |
| `LateSpawns` | `TArray<TObjectPtr<UObject>>` | the GC root for `Spawn_LateInstance` |
| `HolderValue` | seeded **1000 + global index** — DISTINCT per instance | **Solide L4.** One shared base restored to every instance is invisible if they all start equal; distinct values are the only way that defect shows |
| `HolderIndex` | the same index as an `int32` | a second, independently-typed witness of the same identity |
| `bHolderFlag` | `false` | a bool on a class that exists in bulk — the bitfield side of a Force |
| `LateValue` | `0` on `UDumperTestLateSpawn` | **AA12/AA13 step 3.** That class has **zero live instances until `Spawn_LateInstance` is called, and no subclasses** — a legitimately empty result that must NOT be reported as success. The previous attempt used `NiagaraComponent`, which had two live instances, so the empty case was never exercised |
| `BValue` / `BScalar` | `int32` / `float` on payload **B** | **U4 recycling.** `Spawn_RecycleChurn` alternates payload A and B so a freed GObjects slot is refilled by a **different class** — same-class respawn does not test the guard |

**The three Holder classes are a DISCRIMINATING SET, not three samples** — and this is the
fixture that makes Solide's derivation claim falsifiable at all:

| class | name contains `DumperTestHolder` | derives from `ADumperTestHolder` |
|---|---|---|
| `ADumperTestHolder` | yes | yes (itself) |
| `ADumperTestDerivedHolder` | **no** | **yes** — a substring test MISSES it |
| `ADumperTestHolderDecoy` | **yes** | **no** — a substring test CATCHES it |

⭐ A derivation walk holds `{Holder, Derived}` and skips `Decoy`; a substring match holds
`{Holder, Decoy}` and skips `Derived`. **No single result satisfies both**, so the wrong
implementation cannot pass. ⚠ Keep the spawn counts so the derived total stays **under the
1024/256 caps** — a truncated hold makes *"the decoys were untouched"* mean nothing, because the
walk may simply never have reached them.

### Churn + DataTable — the things that CHANGE between two scans

| field | value | check |
|---|---|---|
| `Set_Name` | `TSet<FName>` | **MG2 step 1, set flavour** — remove one element and the rendered row count must follow the header count |
| `Set_Object` | `TSet<TObjectPtr<UObject>>` | the object-pointer set shape |
| `Table_Small` / `Table_Big` | `UDataTable`, **8** and **100** rows | **V8 / MG2 step 2** — `V8_RebuildBigTable(Rows)` rebuilds `Table_Big`; 100 is the V8 default |
| `Map_Churn` / `Arr_Churn` | grow together, one entry per call | a container that changes **between** two scans, for Next-Scan pruning and Snapshot Mode B |
| `Index` (on `FDumperTestTableRow`) | `int32` row index | the scalar leaf inside a DataTable row |
| `Caption` (on `FDumperTestTableRow`) | 走一步 — **odd (3) chars, contains U+4E00** | the B28 FText trigger **inside a DataTable row**, i.e. reached through a container rather than off the actor directly |

### Numerics, flags, layout

| field | value | check |
|---|---|---|
| `I8_Neg` | **-5** | the unit-tested boundary: Int8 yes / UInt8 no |
| `U8_Small` / `U8_Max` | 1 / **255** | NumericAll byte family; also the "is the result volume usable or does it drown the panel" UX judgement no test can make |
| `I16` / `U16` | -12345 / 54321 | |
| `I32` / `I64` | 1234567 / 8899001122334455 | |
| `F32` | **513.36** | the Round/Trunc/Ceil worked example |
| `F64` | 2718.281828 | |
| `bFlagA` / `bFlagB` / `bFlagC` | 1 / 0 / 1 | three bitfields in one byte — bool masks |
| `Grade` | `Elite` (=2) | enum with a **hole** at 3..6 (`Legend`=7), so index≠value cannot pass by accident |
| `FixedArr` (8 elements) | 100..800 | `ArrayDim > 1` — a different property shape from TArray |
| `Health` — `BaseValue` / `CurrentValue` | Base 100, Current ticking | nested StructProperty in GAS-attribute shape → also the "Flatten GAS attributes" CE-export toggle |
| `Payload` → `PayloadText` / `PayloadString` / `PayloadValue` | 統一言語, same as FString, 909090 | Related Objects edge · Locate-in-GWorld through a pointer · Solide force-to-null (strong ptr, so allowed) |
| `RawInt` / `RawFloat` / `RawDouble` | 0x5A5A5A5A / 777.75 / 31415.926535 | **not** UPROPERTY — the interior holes "Guess What" and the Native-C scan must find |
| `F32_Ticking` / `F64_Ticking` | start 1000.5 / 20000.125 | the only float and double that **move** — see the temporal table below |
| `RawInt_Ticking` / `RawFloat_Ticking` / `RawDouble_Ticking` | start 700000 / 300.25 / 50000.5 | **not** UPROPERTY *and* they move — a Native-C scan that can be refined, which the static three above cannot support |

### Group Scan / Snapshot Mode B (temporal)

A 1 Hz timer drives exactly the documented hard case — *groups need `Unchanged`*:

| field | behaviour |
|---|---|
| `Health.CurrentValue` | falls 1/sec, wraps 1 → 100 (so both **Decreased** and **Increased** occur) |
| `Health.BaseValue` | **never moves** — the `Unchanged` slot a group match needs |
| `TickCount` | rises monotonically |
| `FrozenInt` | 424242, never written again |
| `F32_Ticking` | falls 10.25/sec from 1000.5, wraps after ~96 s — **Decreased** every second, **Increased** on the wrap |
| `F64_Ticking` | rises 0.25/sec from 20000.125 — **Increased**, never wraps |
| `RawInt_Ticking` | rises 7/sec from 700000 — **native, non-UPROPERTY** |
| `RawFloat_Ticking` | falls 3.25/sec from 300.25, wraps after ~91 s — **native, non-UPROPERTY** |
| `RawDouble_Ticking` | rises 0.5/sec from 50000.5 — **native, non-UPROPERTY** |

**Why the ticking numerics exist.** `F32` / `F64` and `RawInt` / `RawFloat` / `RawDouble` are
STATIC by design — `F32 = 513.36` is the Round/Trunc/Ceil worked example and the raw three are the
documented interior holes — so before build 2721 the sample had **no float, double or native-C
target that a `Changed` / `Increased` / `Decreased` refine could survive**. A Native-C first scan
could be run and then had nothing to converge on. These five move on the same 1 Hz clock and are
all **on the HUD**, which for the raw three is the only way to learn their value at all: there is
no reflection to ask, so you need the number off the screen before you can search for it.

They are appended at the END of the class so every offset quoted elsewhere (`TickCount` +0x518,
`FrozenInt` +0x51C, `Opt_Int_Set` +0x468, `Set_Int` +0x358) still points at the same field. That
makes the raw three a **trailing** hole rather than an interior one — the easier case for hole
detection, but still inside `PropertiesSize`, and the interior case is already covered by the
static `RawInt` / `RawFloat` / `RawDouble`.

### B8 / Grausam — the backgrounding pair

Launch with **`-DumperTestIdle`** (see above) and alt-tabbing away **guarantees** the game thread
stops ticking: `ShouldUseIdleMode()` needs only `IsGame() && SupportsWindowedMode() && cvar &&
!HasFocus()`, and the first two are automatic for a packaged build. Without the switch the cvar
stays 0 and neither check below can be staged — the startup log line tells you which you have.

* **B8** — Teleport → Fly ON + Noclip → fly through a wall → alt-tab to the UI, wait >500 ms →
  Disable. **PASS** = `Fly: DISABLED but the pawn's collision is still OFF (game thread
  unresponsive)`, then on refocus `Fly: game thread resumed after N ms — pawn collision restored`.
* **Grausam** — with the foreground lock ON, idle mode must **never** engage while backgrounded.
  The lock's whole job is keeping `FApp::HasFocus()` true through the `WM_ACTIVATEAPP` rewrite, so
  this is a positive test rather than "it seemed to keep working".

### Free, no code needed

* **B29 manual half** — this is a UE game folder. Drop a third-party `dxgi.dll` (ReShade) in and
  click *Inject && Connect*. No commercial game required.
* **B5 active half** — launch through the deployed `version.dll` proxy, connect the UI, click Scan,
  and fire a CE mailbox command while the scan runs.
* **Audit #3 M1/M2/M3** — See-Through needs visible geometry and a pawn; the Third Person template
  has both, and unlike a game the wall is always in the same place.

-----

## What this sample can NOT settle

Stating these so nobody spends a packaging cycle on them:

* **B4** (CE mailbox after the UI dies), **B26**, **B16**, the `.CT` `reg.exe` fallback — nothing
  UE-specific; any process will do.
* **B13/B41** (Recycle Bin) — not UE at all.
* **B2** (symbol-export GWorld) — *possibly*: check whether the **Development** package exports
  `GWorld`. If it does, this replaces the dependency on owning Satisfactory. Unverified.
* **B18** (Extra Scan cancel) — needs GObjects to miss by AOB. Pick a corpus row already known to
  fail rather than trying to engineer it here.
* **Anything about licensee forks** (MindsEye, Avowed) — a stock sample is by definition not a fork.
* **STVoyager's UTF-8 FText** — a licensee deviation; no stock build reproduces it.

-----

## Traps

* **Encoding.** Every CJK literal is a `\uXXXX` escape and the files carry a UTF-8 BOM. Both, on
  purpose: a BOM-less file is re-read through the system code page (on a zh-TW machine CP950, whose
  lead bytes can swallow the following character), which would corrupt exactly the strings B28 is
  about — and the test would then be measuring MSVC, not the dumper. **If you edit a literal, keep
  it escaped.**
* **The actor is spawned by a `UWorldSubsystem`, not placed in the level.** A level is a binary
  asset: not diffable, not reviewable, and "remember to drag the actor in" fails silently in a way
  that looks like a dumper bug. Nothing here requires an asset edit.
* **`Opt_Int_Unset` must stay unset.** It is the only negative criterion in the file; initialising
  it "for tidiness" deletes the test.
* **If UHT rejects a `TOptional`** on a different engine version, comment out those four fields and
  their initialisers — everything else is independent. On 5.4 they are confirmed fine:
  `FOptionalProperty` exists (`PropertyOptional.h`), UHT resolves it (`UhtOptionalProperty.cs`), the
  only inner-type rule is `CanBeContainerValue`, and the engine itself ships `TOptional<FBox>` and
  `TOptional<uint32>` UPROPERTYs.
