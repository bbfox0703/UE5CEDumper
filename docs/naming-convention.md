# Frieren Naming Convention — UE5CEDumper

All C++ DLL module/namespace/file names in UE5CEDumper use character names from
the anime **"Frieren: Beyond Journey's End"** (葬送的芙莉蓮).

**Rule**: Every Frieren-named entity MUST have a comment explaining its actual function.

> In-use assignments were informed by the **3rd Official Character Popularity Poll**
> (2026-03-29, 12.7M votes). The pool of names available for *new* modules is the full
> **[Wikipedia character roster](#full-character-roster--name-pool)** below.
> See [Sources](#sources) at the end.

---

## Design Principle

**Character personality / ability / story role ↔ Module functional nature.**

Not a mechanical 1:1 port — each mapping is chosen because the character's
narrative identity resonates with what the module *does*.

---

## DLL Module Naming

| File | Frieren Name | 日文名 | Character | Poll # | Actual Function | Why This Name |
|---|---|---|---|---|---|---|
| **Frieren.cpp** | 芙莉蓮 | フリーレン | Protagonist | #4 (1v1: #1) | ExportAPI: 59 C ABI exports for CE Lua | Everyone meets her first — the sole gateway to the DLL |
| **Genau.cpp** | 葛納烏 | ゲナウ | First-class mage examiner | **#1** | OffsetFinder: AOB signatures, GObjects/GNames/GWorld | The examiner who *screens* candidates — scans & validates every pattern |
| **Macht.cpp** | 黃金鄉馬哈特 | マハト | Seven Sages, transmutation | #5 | Memory: AOBScan, SEH reads, RIP resolution, AVX2 SIMD | Raw elemental power — direct memory manipulation |
| **Aura.cpp** | 斷頭台的阿烏拉 | アウラ | Obedience Scale demon | #3 | ObjectArray: FUObjectArray slot enumeration | Weighs every soul on her scale — validates each object slot |
| **Serie.cpp** | 賽莉耶 | ゼーリエ | Living-history great mage | #6 | FNamePool: FName string resolution (UE5 pool + UE4 TNameEntry) | Remembers every mage's name across millennia — the name oracle |
| **Ubel.cpp** | 尤蓓爾 | ユーベル | Surgical-precision assassin | #15 | UStructWalker: FField chain traversal, property reading | "If she can visualize it, she can cut it" — surgical struct dissection |
| **Fern.cpp** | 費倫 | フェルン | Frieren's apprentice | #8 | PipeServer: Named Pipe JSON IPC (99 commands) | The communicator, messenger — bridges worlds |
| **Sein.cpp** | 贊恩 | ザイン | Priest, journey chronicler | #24 | Logger: 5-category per-process file logging with rotation | The quiet observer who records everything |
| **Himmel.cpp** | 欣梅爾 | ヒンメル | Hero, remembered forever | #2 | Signatures: 128+ AOB pattern database | The hero's *legacy* — immutable knowledge left for those who follow |
| **Flamme.cpp** | 弗蘭梅 | フランメ | Ancient master, knowledge keeper | — | HintCache: per-game AOB result caching | Ancient wisdom passed down — accelerates future scans |
| **Grimoire.h** | 魔導書 | グリモワール | Grimoire | — | Constants, magic strings, DynOff namespace | Book of spells — the configuration tome |
| **Renge.cpp** | 蓮格 | レンゲ | Liaison character | #22 | PipeProtocol: IPC command/event definitions | Communication protocol — the rules of engagement |
| **Stark.cpp** | 修塔爾克 | シュタルク | Brave warrior, frontline | #7 | GameThreadDispatch: MinHook ProcessEvent hook | Charges into the front line — executes on the game thread |
| **Mimic.cpp** | 寶箱怪 | ミミック | Chest mimic (classic gag) | #21 | Mailbox: CE Lua shared-memory interface | Disguised as an innocent exported struct — actually a secret channel |
| **Methode.cpp** | 梅特戴 | メトーデ | All-capable analyst mage | #16 | CEPlugin: CE Plugin Type 5 interface | Analytical entry point — examines everything |
| **Heiter.cpp** | 海塔 | ハイター | Priest who started the journey | — | dllmain: DLL entry point, auto-start logic | The one who set the journey in motion — DLL_PROCESS_ATTACH |
| **Lugner.cpp** | 琉古納 | リュグナー | Demon master of disguise | #12 | ProxyVersion: version.dll forwarding proxy | The deceiver — pretends to be the real version.dll |
| **Scharf.h** | 夏爾夫 | シャルフ | Sharp-eyed, scrutinizing examinee | #17 | WalkerAlignment: FProperty offset-vs-alignment validator | Sharp eye for layout flaws — catches misaligned EnumProperty / FName that hint at a wrong FPROPERTY_OFFSET probe |
| **Wirbel.cpp** | 威亞貝爾 | ヴィアベル | Northern squad leader, pragmatic soldier | #20 | Teleport: marker save/recall + cursor teleport (BugIt-style) | Swift battlefield repositioning — the soldier who relocates first |
| **Tot.h** | 托托 | トート | "Saint of the End", Greater Demon | — | Cancellation: cooperative cancel flag for long-running ops | The End — signals every long loop to stop (was `Cancel`) |
| **Lineal.h** | 莉涅爾 | リネアール | First-class mage, 15-yr undercover spy | — | PackedItem: UE5.7+ packed FUObjectItem reconstruction | The straightedge — realigns the non-standard packed layout (was `PackedItem`) |
| **Radar.cpp** | 拉達爾 | ラダール | Shadow Warrior, plateau village chief | — | ValueScan: CE-style by-value First/Next Scan | The sweep — scans every object for a matching value (was `ValueScan`) |
| **Solitar.cpp** | 索莉塔 | ソリテール | Greater demon studying humanity | #11 | GodMode: force AActor::bCanBeDamaged (damage immunity) + re-assert worker | Overwhelming, near-unkillable mage — invulnerability; reuses the FBoolProperty bit-write Wirbel uses for the cursor |
| **Orden.h** | 歐爾登 | オルデン | Noble house head ("order") | — | GroupMatch: source-agnostic SDR/assignment core for multi-value group scan | Brings *order* to a scattered set of values — assigns each value to its leaf slot (header-only, pure) |
| **Edel.cpp** | 艾德爾 | エーデル | Hypnosis-magic mage ("noble") | — | CurrentTarget: auto-detect the actor the player is targeting (GWorld→PlayerController→Pawn, score outgoing object-ptr fields) | Reads what the player's mind is fixed on — the focused enemy, so the user needn't guess a class-name keyword |
| **Laufen.cpp** | 拉歐芬 | ラオフェン | High-speed-movement mage ("to run") | — | MovementTuning: force per-pawn CMC float knobs (MaxWalkSpeed/GravityScale/JumpZVelocity) × multiplier + re-assert worker | Runs faster than anyone — scales the pawn's movement; the float analogue of Solitar's bool-bit force |
| **Grausam.cpp** | 格勞薩姆 | グラオザーム | Seven Sages, master of illusion magic | #— | ForegroundLock: MinHook user32!GetForegroundWindow → always report the game's own window as foreground | Master of illusion — casts the illusion that the game is always the foreground app, so `t.IdleWhenNotForeground` / focus-loss pause never fires |
| **Denken.cpp** | 登肯 | デンケン | Elderly first-class mage, ex-Chancellor | #13 | NativeDisasm: Zydis x64 decode of a native UFunction → `[this+off]` property xref | Reads the machine's own reasoning — decodes what a compiled function actually touches |
| **Dunste.cpp** | 敦斯特 | ドゥンスト | Exam proctor ("vapors") | — | Fly: no-gravity keyboard-driven 3D flight (CMC MOVE_Flying + Velocity drive) | Vapour — moves through the air with nothing holding it up |
| **Hemmung.cpp** | 赫姆恩 | ヘムング | — ("inhibition") | — | TimeDilation: hold reflected time-dilation floats at an absolute value | Inhibition — slows the world itself |
| **Linie.cpp** | 莉妮耶 | リーニエ | Shadow Warrior, copies techniques ("line") | — | LivePEProfiler: per-`UFunction*` fire-count table recorded from the ProcessEvent hook | Watches and records what actually ran — a line drawn through the frame |
| **Schlacht.cpp** | 沙拉赫特 | シュラハト | — ("battle") | — | SeeThrough: hide the occluders between the camera and the view target | Clears the battlefield of what blocks the line of sight |
| **Solide.cpp** | 佐利德 | ゾリーデ | Blindfold swordsman ("solid") | — | ForceField: hold a discovered reflected field at a value across every live instance of a class | Holds a stance no matter what pushes back — the multi-instance sibling of Hemmung |
| **Sense.cpp** | 森瑟 | ゼンセ | Second-exam proctor, tea ceremony ("scythe") | #14 | Diagnostics: per-command dispatcher timing + process counters | Measures with a steady hand — the numbers every perf claim is checked against |
| **Neu.h** | 諾伊 | ノイ | — ("new") | — | `UEnum::Names` layout parse (legacy `TArray` vs UE5.6+ `FNameData`) | The NEW enum container — reads the struct-of-arrays UE 5.6 introduced |
| **Routine.h** | 路蒂涅 | ルティーネ | Shadow Warrior librarian | — | Periodic-worker scaffolding shared by the six re-assert / hold modules | Scheduled, repeating work — the librarian's rounds (header-only) |
| **Lugner_Dxgi.cpp / _Dinput8.cpp / _Winmm.cpp** | 琉古納 | リュグナー | Demon master of disguise | #12 | The other three proxy DLLs (dxgi / dinput8 / winmm) | Same deceiver, three more disguises — see `Lugner.cpp` |
| **BuildStamp.h/.cpp** | — | — | — (generic leaf utility) | — | Build number / git hash / config string baked in at compile time | Kept English by design: a generic leaf utility, per the exception in Rules below |
| **Utf8Helpers.h** | — | — | — (generic leaf utility) | — | UTF-8 sanitisation + UTF-16→UTF-8 encoding + FString/FUtf8String width decode | Kept English by design (the rule names this file explicitly) |
| **GraphPath.h** | — | — | — (algorithm helper) | — | Pure BFS shortest-path core, lives inside `Aura::` | Kept English by design: an algorithm helper inside an existing namespace |

---

## File Rename Map

| Before | After | Header |
|---|---|---|
| `ExportAPI.h/.cpp` | `Frieren.h/.cpp` | `Frieren.h` |
| `OffsetFinder.h/.cpp` | `Genau.h/.cpp` | `Genau.h` |
| `Memory.h/.cpp` | `Macht.h/.cpp` | `Macht.h` |
| `ObjectArray.h/.cpp` | `Aura.h/.cpp` | `Aura.h` |
| `FNamePool.h/.cpp` | `Serie.h/.cpp` | `Serie.h` |
| `UStructWalker.h/.cpp` | `Ubel.h/.cpp` | `Ubel.h` |
| `PipeServer.h/.cpp` | `Fern.h/.cpp` | `Fern.h` |
| `Logger.h/.cpp` | `Sein.h/.cpp` | `Sein.h` |
| `Signatures.h` | `Himmel.h` | `Himmel.h` |
| `HintCache.h/.cpp` | `Flamme.h/.cpp` | `Flamme.h` |
| `Constants.h` | `Grimoire.h` | `Grimoire.h` |
| `PipeProtocol.h` | `Renge.h` | `Renge.h` |
| `GameThreadDispatch.h/.cpp` | `Stark.h/.cpp` | `Stark.h` |
| `Mailbox.h/.cpp` | `Mimic.h/.cpp` | `Mimic.h` |
| `CEPlugin.cpp` | `Methode.cpp` | *(no header)* |
| `dllmain.cpp` | `Heiter.cpp` | *(no header)* |
| `ProxyVersion.cpp` | `Lugner.cpp` | *(no header)* |
| *(generated)* | `Lugner_Winmm.cpp/.asm` + `ProxyWinmm.def` | *(no header)* |

**Unchanged**: `BuildInfo.h.in`, `version.rc`

> **New (post-577)**: `Scharf.h` introduced for the FProperty alignment helper extracted from Ubel.cpp. No prior file rename — born Frieren-named.

---

## Namespace Structure

```
Frieren::                   // ExportAPI — the gateway (extern "C", no namespace wrapper)
Genau::                     // OffsetFinder — the examiner
Macht::                     // Memory — raw power
Aura::                      // ObjectArray — the scale
Serie::                     // FNamePool — name oracle
Ubel::                      // UStructWalker — surgical dissection
Fern::                      // PipeServer — messenger (also class name)
Sein::                      // Logger — chronicler
Himmel::                    // Signatures — hero's legacy (header-only)
Flamme::                    // HintCache — ancient wisdom
Stark::                     // GameThreadDispatch — frontline warrior
Wirbel::                    // Teleport — swift battlefield repositioning
Solitar::                   // GodMode — force AActor::bCanBeDamaged (damage immunity)
Mimic::                     // Mailbox — disguised channel
Renge::                     // PipeProtocol — liaison rules
Scharf::                    // FProperty alignment validator (header-only)
Tot::                       // Cancellation — cooperative cancel flag (header-only; was Cancel)
Lineal::                    // PackedItem — UE5.7+ packed FUObjectItem reconstruction (header-only; was PackedItem)
Radar::                     // ValueScan — CE-style by-value scan (was ValueScan)
Orden::                     // GroupMatch — source-agnostic SDR matcher (multi-value group scan; header-only)
Edel::                      // CurrentTarget — auto-detect the player's current target actor
Laufen::                    // MovementTuning — force per-pawn CMC float knobs (speed/gravity/jump) × multiplier + re-assert
Dunste::                    // Fly — no-gravity keyboard-driven 3D flight (CMC MOVE_Flying + Velocity drive, re-assert worker)
Grausam::                   // ForegroundLock — hook GetForegroundWindow so the game always thinks it is foreground (defeat idle/pause when unfocused)
Hemmung::                   // TimeDilation — hold reflected time-dilation floats at an ABSOLUTE value
Solide::                    // ForceField — hold a discovered field across every live instance of a class
Schlacht::                  // SeeThrough — hide the occluders between camera and view target
Linie::                     // LivePEProfiler — per-UFunction fire counts from the ProcessEvent hook
Denken::                    // NativeDisasm — Zydis x64 decode: native UFunction → [this+off] property xref
Sense::                     // Diagnostics — per-command dispatcher timing + process counters
Neu::                       // UEnum::Names layout parse (legacy TArray vs UE5.6+ FNameData; header-only)
Routine::                   // Periodic-worker scaffolding for the six re-assert workers (header-only)
BuildStamp::                // Build number / git hash / config (generic leaf utility, English by design)
Utf8Helpers::               // UTF-8 sanitise / UTF-16 encode / FString width decode (English by design)
Grimoire::                  // Constants — spell book
DynOff::                    // Dynamic offsets (in Grimoire.h, unchanged)
```

> **Note**: No `UE5::` root prefix — flat namespaces matching the original code style.

---

## Comment Format

Every Frieren-named file MUST include this header:

```cpp
// {EnglishName} — {中文名} ({meaning/title})
// {Actual function description}
```

### Examples

```cpp
// Genau — 葛納烏 (一級魔法使篩選考官 — First-Class Mage Examiner)
// OffsetFinder: AOB pattern scanning for GObjects, GNames, GWorld pointers
namespace Genau {
    // ...
}
```

```cpp
// Macht — 黃金鄉馬哈特 (萬物成金魔法 — Seven Sages, Transmutation)
// Memory: AOB scanning, SEH-protected reads/writes, RIP-relative resolution
namespace Macht {
    // ...
}
```

```cpp
// Mimic — 寶箱怪 (芙莉蓮的經典梗 — The Classic Gag)
// Mailbox: CE Lua shared-memory command interface (no CreateRemoteThread needed)
namespace Mimic {
    // ...
}
```

---

## UI Naming (No Change)

The C# UI keeps standard English names for panels/services/ViewModels.
Only internal constants reference Frieren terms:

```csharp
// Grimoire — 魔導書 — Application constants and magic strings
public static class Constants  // class name stays English for IDE discoverability
{
    public const string PipeName = @"\\.\pipe\UE5DumpBfx";  // unchanged
    // ...
}
```

---

## 3rd Popularity Poll Reference (2026-03-29)

Total votes: **12,700,122** | Voting period: 2026-03-08 ~ 2026-03-29

### Top 30 (Total Votes)

| # | Character | Votes | Used In |
|---|-----------|-------|---------|
| 1 | Genau (葛納烏) | 1,396,535 | **OffsetFinder** |
| 2 | Himmel (欣梅爾) | 1,327,500 | **Signatures** |
| 3 | Aura (阿烏拉) | 1,020,761 | **ObjectArray** |
| 4 | Frieren (芙莉蓮) | 836,891 | **ExportAPI** |
| 5 | Macht (馬哈特) | 811,841 | **Memory** |
| 6 | Serie (賽莉耶) | 707,902 | **FNamePool** |
| 7 | Stark (修塔爾克) | 383,016 | **GameThreadDispatch** |
| 8 | Fern (費倫) | 366,486 | **PipeServer** |
| 9 | Demon Attacking Rufen Region | 365,049 | — |
| 10 | Bought Skeleton (骨頭) | 339,302 | — |
| 11 | Solitär (索莉塔) | — | — |
| 12 | Lügner (琉古納) | — | **ProxyVersion** |
| 13 | Sense (乘斯) | — | **Diagnostics** |
| 14 | Linie (莉涅) | — | **LivePEProfiler** |
| 15 | Übel (尤蓓爾) | — | **UStructWalker** |
| 16 | Methode (梅特戴) | — | **CEPlugin** |
| 17 | Scharf (夏爾夫) | — | — |
| 18 | Glück (格呂克) | — | — |
| 19 | Stoltz (修托爾茲) | — | — |
| 20 | Wirbel (威亞貝爾) | — | — |
| 21 | Mimic (寶箱怪) | — | **Mailbox** |
| 22 | Renge (蓮格) | — | **PipeProtocol** |
| 23 | Hero of the South (南方勇者) | — | — |
| 24 | Sein (贊恩) | — | **Logger** |
| 25 | Denken (鄧肯) | — | **NativeDisasm** |
| 26 | Kanne (卡妮) | — | — |
| 27 | Land (蘭特) | — | — |
| 28 | Richter (里希特) | — | — |
| 29 | Rivale (利瓦雷) | — | — |
| 30 | Receptionist (櫃台人員) | — | — |

### One-Vote-Per-Person Top 7

Frieren, Himmel, Stark, Fern, Methode, Mimic, Genau

### Available for Future Use

> Superseded by the **Full Character Roster** below, which draws the available-name
> pool from the complete Wikipedia character list (not just the poll Top 30).

---

## Full Character Roster — Name Pool

Source: **[List of Frieren characters — Wikipedia](https://en.wikipedia.org/wiki/List_of_Frieren_characters)**.
This is the authoritative pool of names usable for new DLL modules.

**Legend**: 🟢 in use · 🟡 reserved (designed, not yet built) · ⬜ available

**File-name rule**: C++ identifiers strip the umlaut dots — `ä→a ö→o ü→u`
(matching the existing `Übel→Ubel`, `Lügner→Lugner` convention), e.g.
`Glück→Gluck`, `Böse→Bose`, `Löwe→Lowe`, `Dünste→Dunste`, `Lektüre→Lekture`.
German meanings are given because most names map cleanly to a module function.

**日文名 column**: the official katakana name as it appears in the Japanese
original (葬送のフリーレン). The Latin spellings used here are the German source
words the author chose for module-function fit — the canonical name is the kana.
A few diverge from a strict German transliteration (e.g. **Wirbel → ヴィアベル**,
**Lineal → リネアール**, **Solitär → ソリテール**); those follow the manga, not the
German spelling.

### Frieren's Party & The Hero Party

| Character | 日文名 | File form | Meaning / role | Status | Suggested use |
|---|---|---|---|---|---|
| Frieren | フリーレン | Frieren | Protagonist, slayer-mage | 🟢 ExportAPI | — |
| Fern | フェルン | Fern | Frieren's apprentice | 🟢 PipeServer | — |
| Stark | シュタルク | Stark | Frontline warrior | 🟢 GameThreadDispatch | — |
| Sein | ザイン | Sein | Healing priest, chronicler | 🟢 Logger | — |
| Himmel | ヒンメル | Himmel | The hero, remembered forever | 🟢 Signatures | — |
| Heiter | ハイター | Heiter | Priest who started the journey | 🟢 dllmain | — |
| Eisen | アイゼン | Eisen | Dwarf warrior, "iron", sturdy/retired | ⬜ | Robust/stable core or legacy-stable path |

### Demons — Confidant & Seven Sages of Destruction

| Character | 日文名 | File form | Meaning / role | Status | Suggested use |
|---|---|---|---|---|---|
| Aura | アウラ | Aura | Scales of Obedience (mind control) | 🟢 ObjectArray | — |
| Macht | マハト | Macht | Gold transmutation curse | 🟢 Memory | — |
| Schlacht | シュラハト | Schlacht | "The Omniscient", precognition | 🟢 SeeThrough | See-through occluders — trace camera→pawn each worker tick and hide the nearest non-Pawn/Character actor blocking the view (SetActorHiddenInGame), restored as the view moves; the Omniscient's sight passes through the world (`Schlacht.cpp/.h`, Stage 1 nearest occluder, build 1987) |
| Grausam | グラオザーム | Grausam | Master of illusion magic ("cruel") | 🟢 ForegroundLock | Hook user32!GetForegroundWindow so the game always believes it is foreground — casts the *illusion* of focus to defeat `t.IdleWhenNotForeground` idle / focus-loss pause, keeping the game thread alive for invokes/POV while the tool or CE holds the real foreground (`Grausam.cpp/.h`, build ~1950) |
| Böse | ベーゼ | Bose | Immortal Sage, barrier magic ("evil") | ⬜ | Protection / guard / anti-tamper shield |

### Demons — Greater & Other

| Character | 日文名 | File form | Meaning / role | Status | Suggested use |
|---|---|---|---|---|---|
| Lügner | リュグナー | Lugner | Master of disguise / envoy | 🟢 ProxyVersion | — |
| Solitär | ソリテール | Solitar | Greater demon studying humanity | 🟢 GodMode | Force AActor::bCanBeDamaged via FBoolProperty bit + re-assert worker (`Solitar.cpp`, build 1251) |
| Tot | トート | Tot | "Saint of the End", end-curse | 🟢 Cancellation | Cooperative cancel flag for long-running ops (`Tot.h`, was `Cancel`) |
| Rivale | リヴァーレ | Rivale | "Bloody God of War", forges weapons | ⬜ | Builder / generator (CT / AA script) |
| Qual | クヴァール | Qual | Creator of Zoltraak (universal magic) | ⬜ | Foundational engine / AOB pattern compiler |
| Linie | リーニエ | Linie | Reads opponent mana ("line") | 🟢 LivePEProfiler | Live ProcessEvent call profiler — opt-in per-UFunction* fire-count table recorded from Stark's PE hook during a Start/Stop window (`Linie.cpp`, build 2103) |
| Draht | ドラート | Draht | Lügner's assistant ("wire") | ⬜ | Wiring / binding / IPC plumbing |
| Revolte | レヴォルテ | Revolte | Four-handed general, four swords | ⬜ | Parallelism / multi-threaded dispatch |
| Hemmung | ヘムング | Hemmung | Mist→energy ("inhibition") | 🟢 TimeDilation | Hold the game's reflected time-dilation floats — global `AWorldSettings::TimeDilation` (whole-world slow-mo / freeze / fast-forward) + per-pawn `AActor::CustomTimeDilation` — at an absolute value via a write-on-drift re-assert worker (the absolute-value sibling of Laufen); *inhibits* the game clock against slow-mo abilities / Sequencer tracks. CE `CMD_TIME=15` (`Hemmung.cpp/.h`, build 2147) |
| Solide | ゾリーデ | Solide | Blindfold swordsman ("solid") | 🟢 ForceField | Force-and-hold a discovered reflected field (bool ON/OFF / ObjectProperty→null / numeric→absolute) across all live instances of a class via a write-on-drift re-assert worker; + player stealth/visibility-meter auto-finder (`MatchStealthField`). Powers Property Search "Force" + the Teleport Stealth card — the honest subset of "enemies can't detect you" (`Solide.cpp/.h`, build 2168) |
| Jung | ユング | Jung | Curious demon child ("young") | ⬜ | Experimental / sandbox features |
| Zart | ツァルト | Zart | "Lingering Shadow", spatial transference | ⬜ | Memory remap/relocate (note: Wirbel owns teleport) |

### Mages — Continental Magic Association

| Character | 日文名 | File form | Meaning / role | Status | Suggested use |
|---|---|---|---|---|---|
| Serie | ゼーリエ | Serie | Living-history great mage | 🟢 FNamePool | — |
| Genau | ゲナウ | Genau | First-Exam proctor / examiner | 🟢 OffsetFinder | — |
| Methode | メトーデ | Methode | All-capable analyst, detection | 🟢 CEPlugin | — |
| Sense | ゼンゼ | Sense | Second-Exam proctor ("scythe") | 🟢 Diagnostics | Self-health telemetry — *harvests* how long each pipe command occupies the DLL dispatcher (the head-of-line blocking multipipe-eval.md blames for UI lag but nothing measured) + Win32 process facts + game-thread health. Pipe-only `get_diagnostics` / `reset_diagnostics` (`Sense.cpp/.h`, build 2308) |
| Falsch | ファルシュ | Falsch | By-the-book proctor ("false") | ⬜ | Validation / assertion / error detection |
| Lernen | レルネン | Lernen | Serie's apprentice ("to learn") | ⬜ | Adaptive heuristics / calibration |
| Lineal | リネアール | Lineal | 15-year undercover spy ("ruler") | 🟢 PackedItem reconstruct | UE5.7+ packed FUObjectItem split/rejoin (`Lineal.h`, was `PackedItem`) |

### Mages — First-Class Exam & Others

| Character | 日文名 | File form | Meaning / role | Status | Suggested use |
|---|---|---|---|---|---|
| Übel | ユーベル | Ubel | Cleaving-magic assassin | 🟢 UStructWalker | — |
| Wirbel | ヴィアベル | Wirbel | Magic Corps captain ("whirl") | 🟢 Teleport | — |
| Scharf | シャルフ | Scharf | Petals→steel blades ("sharp") | 🟢 WalkerAlignment | — |
| Denken | デンケン | Denken | Court magician, Macht's student | 🟢 NativeDisasm | — |
| Land | ラント | Land | Creates flawless clones | ⬜ | Cloning / replication / duplicate detection |
| Kanne | カンネ | Kanne | Controls water ("watering can") | ⬜ | Flow / streaming / pipelining |
| Lawine | ラヴィーネ | Lawine | Freezes water ("avalanche") | ⬜ | Snapshot/freeze or bulk cascade |
| Edel | エーデル | Edel | Hypnosis magic ("noble") | 🟢 CurrentTarget | Auto-detect the actor the player is currently targeting: GWorld→PlayerController→Pawn, score the player's outgoing object-ptr fields (`Edel.cpp/.h`, build 1400) |
| Richter | リヒター | Richter | Staff repair ("judge") | ⬜ | Scoring / judging / recovery-repair |
| Laufen | ラオフェン | Laufen | High-speed movement ("to run") | 🟢 MovementTuning | Force per-pawn UCharacterMovementComponent float knobs (MaxWalkSpeed/GravityScale/JumpZVelocity) × a multiplier of the captured base + re-assert worker; float analogue of Solitar (`Laufen.cpp/.h`, build 1788) |
| Ehre | エーレ | Ehre | Controls rocks ("honor") | ⬜ | Foundation / stability |
| Blei | ブライ | Blei | Edel's teammate ("lead" metal) | ⬜ | Weighting / ballast |
| Dünste | ドゥンスト | Dunste | Edel's teammate ("vapors") | 🟢 Fly | No-gravity keyboard-driven 3D flight of the local pawn: force CMC MOVE_Flying (raw enum-byte, collision preserved) held by a re-assert worker that samples the keyboard (GetAsyncKeyState) and drives CMC Velocity each ~60 Hz tick — vapors drift weightlessly through the air (`Dunste.cpp/.h`) |
| Ton | トーン | Ton | Lone-wolf exam mage ("clay") | ⬜ | Shaping / serialization / formatting |

### Northern Empire — Special Forces & Shadow Warriors

| Character | 日文名 | File form | Meaning / role | Status | Suggested use |
|---|---|---|---|---|---|
| Phrase | フラーゼ | Phrase | Special Forces captain | ⬜ | Parsing / syntax / expression eval |
| Kanone | カノーネ | Kanone | Special Forces ("cannon") | ⬜ | Bulk / heavy blast scan |
| Neu | ノイ | Neu | Discovers undercover ("new") | 🟢 EnumNames | UEnum::Names parse — legacy `TArray<TPair<FName,int64>>` vs the UE5.6+ FNameData struct-of-arrays disguised at the same offset (`Neu.h`, build 1266) |
| Weg | ヴェーク | Weg | Magic Special Forces trooper ("way/path") | ⬜ | Routing / path resolution / address-path walk |
| Grau | グラウ | Grau | Straight-laced trooper ("gray") | ⬜ | Neutral baseline |
| Lager | ラーガー | Lager | Carefree trooper ("storage/depot") | ⬜ | Cache / buffer pool / storage |
| Löwe (Held) | レーヴェ | Lowe | Governor / anti-magic ("lion") | ⬜ | Aggressive / dominant heuristic |
| Radar | ラダール | Radar | Shadow Warrior chief | 🟢 ValueScan | CE-style by-value First/Next Scan (`Radar.cpp/.h`, was `ValueScan`) |
| Schritt | シュリット | Schritt | Shadow Warrior ("step") | ⬜ | Stepping / single-step iteration |
| Routine | ルティーネ | Routine | Shadow Warrior librarian | 🟢 | **`Routine.h`** — periodic-worker scaffolding shared by the six re-assert / hold modules (sliced sleep + guarded tick) |
| Kreis | クライス | Kreis | Shadow Warrior blacksmith ("circle") | ⬜ | Ring buffer / loop / cycle |
| Lore | ローレ | Lore | Shadow Warrior nun ("lore") | ⬜ | Knowledge base / metadata store |
| Walross | ヴァルロス | Walross | Ex-Hero Rasen ("walrus") | ⬜ | (thematic) |
| Wolf / Iris / Klematis / Gazelle | ヴォルフ / イーリス / クレマティス / ガゼレ | Wolf / Iris / Klematis / Gazelle | Minor Shadow Warriors | ⬜ | (thematic pool) |

### Other Characters

| Character | 日文名 | File form | Meaning / role | Status | Suggested use |
|---|---|---|---|---|---|
| Flamme | フランメ | Flamme | Ancient master, concealment | 🟢 HintCache | — |
| Gehen | ゲーエン | Gehen | Dwarf who built a canyon bridge | ⬜ | Bridge / IPC connector (strong fit) |
| Glück | グリュック | Gluck | Lord allied with Macht ("luck") | ⬜ | Lucky heuristic / fallback logic |
| Kraft | クラフト | Kraft | Ancient elven monk ("force/power") | ⬜ | Heavy-compute / force utility |
| Orden | オルデン | Orden | Noble house head ("order") | 🟢 GroupMatch | Source-agnostic SDR matcher for multi-value group scan (`Orden.h`, header-only, build 1276) |
| Fass | ファス | Fass | Dwarf seeking ale ("barrel/cask") | ⬜ | Container / buffer |
| Voll | フォル | Voll | Old dwarf friend ("full") | 🟢 PipeAcceptCapacityLog | Pipe-accept capacity logging policy — pure decision for what the accept loop logs when the pool is FULL (all `kMaxPipeInstances` in use → `ERROR_PIPE_BUSY`): once on entry + once on recovery, ERROR only for unexpected codes (`Voll.h`, header-only, [PIPEBUSY] build fix) |
| Milliarde | ミリアルデ | Milliarde | Old elf ("billion") | ⬜ | Large-count handling |
| Lektüre | レクテューレ | Lekture | Denken's late wife ("reading") | ⬜ | Reader / parser |
| Lecker | レッカー | Lecker | Talented cook ("delicious") | ⬜ | Presentation / formatting |
| Granat | グラナト | Granat | Town graf ("garnet") | ⬜ | (thematic) |
| Dach | ダッハ | Dach | Northern count, hires Frieren ("roof") | ⬜ | Top-level container / umbrella / cover |
| Kiesel | キーゼル | Kiesel | Dwarf hunting legendary ale ("pebble") | ⬜ | Granular chunk / small-unit scan |
| Stoltz | シュトルツ | Stoltz | Stark's brother ("proud") | ⬜ | (thematic) |
| Eisen | アイゼン | Eisen | (see Hero Party) | ⬜ | Robust/stable core |

> **Title-only / unnamed roles excluded** (not clean identifiers): Emperor,
> Hero of the South, Sword Village Chief, Stark's Father, Sein's Older Brother.
>
> **Not on the Wikipedia roster** (kept anyway): `Mimic` (寶箱怪 chest-mimic gag),
> `Renge` (蓮格 liaison, poll #22), `Grimoire` (魔導書 — an item, not a character).

### Plain-English module migration status

| Module | File | Function | Status |
|---|---|---|---|
| Cancellation | `Tot.h` (was `Cancel.h`) | cooperative cancel flag | ✅ renamed `Cancel → Tot` |
| Packed item | `Lineal.h` (was `PackedItem.h`) | UE5.7+ packed FUObjectItem reconstruct | ✅ renamed `PackedItem → Lineal` |
| Value scan | `Radar.cpp/.h` (was `ValueScan.*`) | CE-style by-value scan | ✅ renamed `ValueScan → Radar` |
| Graph path | `GraphPath.h` | BFS shortest-path core (under `Aura::`) | ✅ kept — helper inside `Aura::`, by design |
| UTF-8 helpers | `Utf8Helpers.h` | string conversion leaf util | ✅ kept — generic utility, by design |
| Build stamp | `BuildStamp.h/.cpp` | build/version metadata accessors (decouples generated `BuildInfo.h` from heavy TUs) | ✅ kept — generic leaf utility, by design |

---

## Sources

- [List of Frieren characters — Wikipedia](https://en.wikipedia.org/wiki/List_of_Frieren_characters) — **primary name-pool roster**
- [葬送のフリーレン — Wikipedia (日本語)](https://ja.wikipedia.org/wiki/葬送のフリーレン) — **日文名 (katakana) source**
- [葬送的芙莉蓮角色列表 — 維基百科 (中文)](https://zh.wikipedia.org/wiki/葬送的芙莉蓮角色列表) — 中文名 cross-check
- [Frieren: 8 Most Popular Characters, Officially Ranked By Japan Poll — GameRant](https://gamerant.com/frieren-most-popular-characters-third-popularity-poll/)
- [Himmel Officially Loses No. 1 Spot — CBR](https://www.cbr.com/frieren-official-character-ranking-2026-himmel-lose/)
- [Frieren Character Popularity Poll Results — Oricon](https://us.oricon-group.com/news/8194/)
- [《葬送的芙莉蓮》第三回人氣票選 — 4Gamers](https://www.4gamers.com.tw/news/detail/78111/frieren-beyond-journeys-characters-popularity-vote-2026)
- [Genau Takes Top Spot in 3rd Popularity Poll — ANIME FREAKS](https://times.abema.tv/en/articles/-/10235832)
