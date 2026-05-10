# Frieren Naming Convention — UE5CEDumper

All C++ DLL module/namespace/file names in UE5CEDumper use character names from
the anime **"Frieren: Beyond Journey's End"** (葬送的芙莉蓮).

**Rule**: Every Frieren-named entity MUST have a comment explaining its actual function.

> Naming based on the **3rd Official Character Popularity Poll** (2026-03-29,
> 12.7M total votes). See [Sources](#sources) at the end.

---

## Design Principle

**Character personality / ability / story role ↔ Module functional nature.**

Not a mechanical 1:1 port — each mapping is chosen because the character's
narrative identity resonates with what the module *does*.

---

## DLL Module Naming

| File | Frieren Name | Character | Poll # | Actual Function | Why This Name |
|---|---|---|---|---|---|
| **Frieren.cpp** | 芙莉蓮 | Protagonist | #4 (1v1: #1) | ExportAPI: ~30 C ABI exports for CE Lua | Everyone meets her first — the sole gateway to the DLL |
| **Genau.cpp** | 葛納烏 | First-class mage examiner | **#1** | OffsetFinder: AOB signatures, GObjects/GNames/GWorld | The examiner who *screens* candidates — scans & validates every pattern |
| **Macht.cpp** | 黃金鄉的馬哈特 | Seven Sages, transmutation | #5 | Memory: AOBScan, SEH reads, RIP resolution, AVX2 SIMD | Raw elemental power — direct memory manipulation |
| **Aura.cpp** | 斷頭台的奧拉 | Obedience Scale demon | #3 | ObjectArray: FUObjectArray slot enumeration | Weighs every soul on her scale — validates each object slot |
| **Serie.cpp** | 賽莉耶 | Living-history great mage | #6 | FNamePool: FName string resolution (UE5 pool + UE4 TNameEntry) | Remembers every mage's name across millennia — the name oracle |
| **Ubel.cpp** | 尤蓓爾 | Surgical-precision assassin | #15 | UStructWalker: FField chain traversal, property reading | "If she can visualize it, she can cut it" — surgical struct dissection |
| **Fern.cpp** | 費倫 | Frieren's apprentice | #8 | PipeServer: Named Pipe JSON IPC (~30 commands) | The communicator, messenger — bridges worlds |
| **Sein.cpp** | 賽恩 | Priest, journey chronicler | #24 | Logger: 5-category per-process file logging with rotation | The quiet observer who records everything |
| **Himmel.cpp** | 欣梅爾 | Hero, remembered forever | #2 | Signatures: 128+ AOB pattern database | The hero's *legacy* — immutable knowledge left for those who follow |
| **Flamme.cpp** | 弗蘭梅 | Ancient master, knowledge keeper | — | HintCache: per-game AOB result caching | Ancient wisdom passed down — accelerates future scans |
| **Grimoire.h** | 魔導書 | Grimoire | — | Constants, magic strings, DynOff namespace | Book of spells — the configuration tome |
| **Renge.cpp** | 蓮格 | Liaison character | #22 | PipeProtocol: IPC command/event definitions | Communication protocol — the rules of engagement |
| **Stark.cpp** | 修塔爾克 | Brave warrior, frontline | #7 | GameThreadDispatch: MinHook ProcessEvent hook | Charges into the front line — executes on the game thread |
| **Mimic.cpp** | 寶箱怪 | Chest mimic (classic gag) | #21 | Mailbox: CE Lua shared-memory interface | Disguised as an innocent exported struct — actually a secret channel |
| **Methode.cpp** | 梅特黛 | All-capable analyst mage | #16 | CEPlugin: CE Plugin Type 5 interface | Analytical entry point — examines everything |
| **Heiter.cpp** | 海塔 | Priest who started the journey | — | dllmain: DLL entry point, auto-start logic | The one who set the journey in motion — DLL_PROCESS_ATTACH |
| **Lugner.cpp** | 呂格納 | Demon master of disguise | #12 | ProxyVersion: version.dll forwarding proxy | The deceiver — pretends to be the real version.dll |
| **Scharf.h** | 夏爾夫 | Sharp-eyed, scrutinizing examinee | #17 | WalkerAlignment: FProperty offset-vs-alignment validator | Sharp eye for layout flaws — catches misaligned EnumProperty / FName that hint at a wrong FPROPERTY_OFFSET probe |

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
Mimic::                     // Mailbox — disguised channel
Renge::                     // PipeProtocol — liaison rules
Scharf::                    // FProperty alignment validator (header-only)
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
// Macht — 黃金鄉的馬哈特 (七大魔王 — Seven Sages, Transmutation)
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
| 3 | Aura (奧拉) | 1,020,761 | **ObjectArray** |
| 4 | Frieren (芙莉蓮) | 836,891 | **ExportAPI** |
| 5 | Macht (馬哈特) | 811,841 | **Memory** |
| 6 | Serie (賽莉耶) | 707,902 | **FNamePool** |
| 7 | Stark (修塔爾克) | 383,016 | **GameThreadDispatch** |
| 8 | Fern (費倫) | 366,486 | **PipeServer** |
| 9 | Demon Attacking Rufen Region | 365,049 | — |
| 10 | Bought Skeleton (骨頭) | 339,302 | — |
| 11 | Solitär (索莉塔) | — | — |
| 12 | Lügner (呂格納) | — | **ProxyVersion** |
| 13 | Sense (乘斯) | — | — |
| 14 | Linie (莉涅) | — | — |
| 15 | Übel (尤蓓爾) | — | **UStructWalker** |
| 16 | Methode (梅特黛) | — | **CEPlugin** |
| 17 | Scharf (夏爾夫) | — | — |
| 18 | Glück (格呂克) | — | — |
| 19 | Stoltz (修托爾茲) | — | — |
| 20 | Wirbel (維爾貝爾) | — | — |
| 21 | Mimic (寶箱怪) | — | **Mailbox** |
| 22 | Renge (蓮格) | — | **PipeProtocol** |
| 23 | Hero of the South | — | — |
| 24 | Sein (賽恩) | — | **Logger** |
| 25 | Denken (頓肯) | — | — |
| 26 | Kanne (卡妮) | — | — |
| 27 | Land (蘭特) | — | — |
| 28 | Richter (里希特) | — | — |
| 29 | Rivale (リヴァーレ) | — | — |
| 30 | Receptionist (櫃台人員) | — | — |

### One-Vote-Per-Person Top 7

Frieren, Himmel, Stark, Fern, Methode, Mimic, Genau

### Available for Future Use

| Character | Poll # | Suggested Use |
|-----------|--------|---------------|
| Solitär (#11) | 11 | Stealth/concealment features |
| Sense (#13) | 13 | Object destruction/cleanup |
| Linie (#14) | 14 | Object cloning/replication |
| ~~Scharf (#17)~~ | 17 | **In use → FProperty alignment validator (`Scharf.h`)** |
| Glück (#18) | 18 | Lucky heuristics / fallback logic |
| Wirbel (#20) | 20 | Strategy/optimization |
| Denken (#25) | 25 | Deep analysis / type inference |
| Kanne (#26) | 26 | Growth/tree operations |
| Richter (#28) | 28 | Memory region scanning |

---

## Sources

- [Frieren: 8 Most Popular Characters, Officially Ranked By Japan Poll — GameRant](https://gamerant.com/frieren-most-popular-characters-third-popularity-poll/)
- [Himmel Officially Loses No. 1 Spot — CBR](https://www.cbr.com/frieren-official-character-ranking-2026-himmel-lose/)
- [Frieren Character Popularity Poll Results — Oricon](https://us.oricon-group.com/news/8194/)
- [《葬送的芙莉蓮》第三回人氣票選 — 4Gamers](https://www.4gamers.com.tw/news/detail/78111/frieren-beyond-journeys-characters-popularity-vote-2026)
- [Genau Takes Top Spot in 3rd Popularity Poll — ANIME FREAKS](https://times.abema.tv/en/articles/-/10235832)
