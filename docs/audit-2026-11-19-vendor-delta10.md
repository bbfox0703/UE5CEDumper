# Vendor audit #10 — RE-UE4SS / UEPseudo / patternsleuth / zydis deltas + UE 5.9.0-preview-1 · 2026-11-19

> **Provenance.** `vendor/` reference clones are gitignored and **unpinned**; every machine must re-sync with
> `sync_tools.ps1`, which never moves `vendor/UnrealEngine`. State audited here:
>
> | clone / submodule | before (audit #9) | after |
> |---|---|---|
> | `vendor/UnrealEngine` | `5.8.4-release` `f45729c0b6f0`; audit #9 read `origin/5.9` at `87b91043482e` | **unchanged checkout**. New tag **`5.9.0-preview-1`** = `acf0265dd829` (tagged 2026-11-09, 1,088 commits past `87b91043482e`); `origin/5.9` tip `32e0d4024481`; `origin/5.8` `f97cae4acf01` (8 commits past 5.8.4) |
> | `vendor/RE-UE4SS` | `3da47fe9` | **`682a75f1`** (35 commits) |
> | `…/deps/first/Unreal` (UEPseudo) | `2b77aa1f` | **`029670f9`** (6 commits; RE-UE4SS moved its pin) |
> | `…/deps/first/patternsleuth` | pin `1d90b02c`, upstream `8bcbd43` | pin unchanged; upstream **`8e0c071`** (9 commits past the pin, all read) |
> | `vendor/Dumper-7` | `2c1eae3` | unchanged |
> | `vendor/minhook` (submodule, compiled in) | `d94c64d`, 3 behind | unchanged; 0 commits after the last review |
> | `vendor/zydis` (submodule, compiled in) | `a95bb71` (5.0.0) | pin unchanged; **10 commits after the last review** (`fcc63c6` → `4b31c34`): **HOLD** (below) |
> | `vendor/nlohmann/json.hpp` (committed) | 3.12.0 | unchanged |

**Method.** One read-only workflow (`wf_ac9862a2-9df`). Five readers each took one area: RE-UE4SS core, UEPseudo,
patternsleuth, zydis, and Epic's engine (the preview tag against audit #9's `origin/5.9`, audit #9's VND9-05 bullets
against the preview tag, `origin/5.8` past 5.8.4). Every area with an actionable finding got an adversarial verifier
(default posture: refute); patternsleuth had none. 40 findings; **none refuted**; six corrected (`UE4SS-2611b-01`'s
failure mode, `UEP10-02`'s scope, `ZYD10-07` FIX-low → PREPARE, `VND10-02` PREPARE → DOC, `VND10-03` incomplete,
`VND10-04`'s count). The zydis reader ran `tools/verify/zydis_bump_diff/zydis_bump_diff.py` on four x64 binaries.
Nothing was measured on a running game; every other row is read from source and from upstream's configs.

## BOTTOM LINE

- **Nothing breaks on a stock engine or a title we have.** UE 5.9.0-preview-1 moves no layout, flag, reflected name or
  virtual slot we read; ProcessEvent is still at `0x250` at the tag and at the `origin/5.9` tip.
- **audit #9's `[VND9-05]` is now due**: its trigger ("a 5.9 preview tag arrives") fired. Three of its four bullets hold;
  the ProcessEvent one is corrected to wording only (`VND10-A2`).
- **Phantom Blade Zero** (UE 5.7.4, a licensee layout, from RE-UE4SS's new CustomGameConfig) would fail in three
  stacked places: GObjects, the field-class name, and every offset derived from `UStruct::ChildProperties`. Not
  installed here, so predicted from the config, not measured (`VND10-A1`).
- **UEPseudo `c3dcec8`** fixes a property cache keyed by a struct address that a freed struct leaves behind. Six of our
  address-keyed caches have that shape with no witness; reopening the recorded A10 decision is the maintainer's call
  (`VND10-A3`).
- **zydis: HOLD.** Denken's and Falsch's output is byte-identical at the pin and at `4b31c34`. The bump rig no longer
  prints all of Denken's output (`VND10-A4`).

## ACTION REGISTER

### VND10-A1 · **FIX-low** · M — Phantom Blade Zero's licensee layout `[UE4SS-2611b-01..04]`

From `24480ac3` (merged `ef15eba8`, PR #1484), `assets/CustomGameConfigs/Phantom Blade Zero/MemberVariableLayout.ini`
and `UE4SS_Signatures/GUObjectArray.lua`. The game is in neither Steam library on this machine. **PBZ dumps only when
all four land**; each is red-first in dll_core_test with a synthetic PBZ shape and no-steal checks against the stock rows.

1. **GObjects (`-01`, corrected).** `TUObjectArray` is `{MaxChunks 0x0, NumChunks 0x4, Objects 0x8, Num 0x10,
   Max 0x14, PreAllocated 0x18}`; no Tier 1 or relaxed row validates at the array's base or at `base+0x10`. The verifier
   found the worse outcome: our `mov reg,[rip+X]` GObjects rows deliver `base+0x18` (PBZ's Objects field), where relaxed
   F's pass 2 validates (Num 153,466, Max 4,259,840, a chunk table at +0) and `Aura::DetectLayout` then reads the wrong
   fields: a scan that "finds" GObjects with 0 usable objects. The Extra Scan's ascending sweep reaches the same address.
   **Fix:** a PBZ row at both anchors (`{obj 0x08, max 0x14, num 0x10, maxC 0x00, numC 0x04}` relative to ObjObjects,
   `{0x18, 0x24, 0x20, 0x10, 0x14}` to the base), appended last, in Genau's Tier 1 presets and Aura's
   `s_chunkedPresets` / `s_ue4ExtendedPresets`; or one invariant-gated permutation search over the 0x20 window
   (`MaxElements == MaxChunks * 65536`, `1 <= NumChunks <= MaxChunks`, `Num <= NumChunks * 65536`, `Num <= Max`). A
   `kStaticGeometries` row is **not** enough alone: the static-struct recovery calls `Aura::InitWithExtendedLayout`,
   which forces the stock layout; a PBZ geometry needs its own dispatch arm calling `Aura::Init`.
2. **Field-class name (`-02`, confirmed).** `FFieldClass::Name` is at `+0x30`; `kFFieldClassNameProbes` is
   `{0x00, 0x08}`, so ChildProperties is never found and every property walks as Scalar, silently. **Fix:** append
   `0x30` (suffix rule) after `0x00` / `0x08`, so stock engines latch what they latch today.
3. **UStruct offsets (`-03`, confirmed).** PBZ inserts 0x50 bytes after `ChildProperties`: `PropertiesSize 0xA8`,
   `Script 0xB0`, `UFunction::FunctionFlags 0x100`. We derive `PropertiesSize = ChildProperties + 8`, and
   `FunctionFlagsPrimaryFor` accepts only 0x30..0x80. **Fix:** measure `PropertiesSize` (the int32 equal to
   `sizeof(UObject)` on `CoreUObject.Object`'s class, cross-checked on structs of known size), comparing `MinAlignment`
   as **int16** (5.6+ packs `StructStateFlags` beside it); widen the FunctionFlags window so 0xA8 yields 0x100.
4. **The stock GUObjectArray site (`-04`, confirmed PREPARE).** Upstream's `lea` + three-call pattern hits exactly once
   on DT 5.4, DT51, DT56 and DT58 (`+0x10`, or `+0x0` on 5.8), where our two-call `GOBJ_V10` misses. Add it as a
   `RipBoth` row (adjustment `-0x10`), **ranked above every `mov`-Objects row** (`GOBJ_V13` at 210 is the first): on
   PBZ a `mov` hit otherwise reaches relaxed F first. GObjects sweep for no-regression. A DLL Himmel row, not AOBMaker
   work.

### VND10-A2 · **PREPARE → due** · S — UE 5.9 bookkeeping, audit #9's `[VND9-05]` with corrections `[VND10-02..05]`

- **Version needle** (`VND10-03`, completed): add `{ "5.9.", 509 }` to `kVersionNeedles`, bump the `static_assert`,
  **and bump `Genau::kVersionDetectLogicRev` 11 → 12** with a dll_core_test check (precedents: GWT-BRANCH,
  VER-410-FIXED-VS-STRING), or `Flamme::CachedDetectionTrusted` keeps a rev-11 verdict for any 5.9 binary seen before.
  Add "UE 5.9" to the UI's `UeVersionOverrideOptions` (it stops at 5.8 while `Grimoire.h` accepts 509). Before shipping,
  run the needle sweep over the corpus: a head-of-table needle wins Tier 2/3 by order.
- **ProcessEvent** (`VND10-02`, corrected to wording): the slot is still `0x250` at the tag and the branch tip, but
  **do not add `case 509`**: `dll_helpers_test` pins `ProcessEventVTableSlotFor(509) == 0` ("no measurement"), the
  fallback log calls every row "measured, UVTD PDB oracle", and `check_processevent_slots.py` reads only templates. Keep
  509 unmeasured until RE-UE4SS ships `VTableLayout_5_09` or a 5.9 fixture's PDB measures it; only the "5.9+ does not
  exist yet" wording in `Grimoire.h`, `Frieren.cpp` and the test changes now.
- **Cascade** (`VND10-04`): add `/Script/CascadeRuntime` to `Aura.h` `kEnginePrefixes`, `DumpAllService.cs` and
  `scripts/analysis/engine_paths.py`. Count where quoted: CascadeRuntime declares 133 UCLASSes, 117 of them
  `ParticleModule*` (`DefaultCascade.ini`: 193 `/Script/Engine` → `/Script/CascadeRuntime` redirects).
- **GWLD_SF_2** (`VND10-05`): unchanged at the tag; keep it in the first 5.9 fixture's AOB no-regression run.

### VND10-A3 · **FIX-low, maintainer's call** · M — address-keyed caches with no witness `[UEP10-02]`

UEPseudo `c3dcec8` replaces a property cache keyed by `(UClass*, FName)` with one keyed by `(ObjectIndex, Serial)` and
a staleness check. Ours, each serving a hit with no check that the same class still sits at the address and never
erased: `Aura::GetClassContainers`, `Aura::GetClassRefMeta`, `Ubel::GetCachedStructFields`, `Ubel`'s `s_enumCache`,
and (the verifier's addition) the thread-local verdict caches of `Aura::IsSnapshotNoiseClass` /
`IsSnapshotGameplayClass`, which live for the whole pipe connection and can drop a recycled class's matches or rows.
`[WALKCLASS-MEMO-REUSE]` (build 3741) covered only the two class-walk caches. **Do not copy upstream's key**
(`UEP10-03`): it works only because its constructor forces a serial through a ProcessEvent call, which a passive
observer must not do (working-lessons §4.3). If reopened: store `Ubel`'s class witness with each entry, rebuild on a
mismatch and park the old entry (callers recurse holding references); for `s_enumCache`, an input-bytes witness. The
A10 REOPEN-IF triggers have not literally fired; what moved is the premise of A10's reasons (a) and (b), through build
3741.

### VND10-A4 · **PREPARE** · S — the zydis bump rig no longer prints all of Denken's output `[ZYD10-07]`

`tools/verify/zydis_bump_diff/denken_run.cpp` prints each access as offset, size, occurrences, writes and confidence;
`[DENKEN-WRITE-SITES]` (2026-11-17) added `writeSites`, `writeSitesViaThis` and `writeSitesTruncated`, which reach the
UI and the MCP server. The rig's header ("the full canonical result") and `zydis_bump_diff.py` overstate it. The CODE
sweep already covers every Zydis input the write sites are built from, so the exposure is narrow (a dismissed `dk=1`
row; call / jmp targets in leaf ranges without `.pdata`). **Fix:** print the three fields (site RVAs relative to the
base) and record the new-format hashes in the next pin review, saying the format changed. Not AOBMaker work.

### Documentation — `[VND10-DOC]`

- **patternsleuth** (`PS10-03`): reviewed through `8e0c071` (2026-11-19). The no-rescan check is counted from RE-UE4SS's
  pin, which has not moved, so it lists commits on every run; the next check is
  `git -C vendor/RE-UE4SS/deps/first/patternsleuth log --oneline 8e0c071..origin/master -- patternsleuth/src/resolvers`.
- **zydis** (`ZYD10-01`): HOLD, recorded in `vendor-pin-reviews.md` and `tools/vendor_pin_reviews.json`. A delta-only
  run (`--base fcc63c6`) exits 1 only because no control case covers the delta (`ZYD10-08`); the decisive run from the
  pin exits 0.
- **UE 5.9 reflection trimming** (`VND10-06`): opt-in per target (`TargetRules.Trim.cs`: a UHT type deny list, a native
  function keep list, a DesktopMinimal profile that can leave Cascade out). A trimmed 5.9 title showing fewer UFunctions
  or no Cascade classes is by design, not a walker fault.

### Recorded, no action (verified)

- **#1501, ICppStructOps through `FStructOpsFakeVTable` on 5.7+** (`UE4SS-2611b-05/06`, `UEP10-06`): nothing of ours
  calls or reads `ICppStructOps`; the 5.7 / 5.8 templates' new lines are that struct alone. A future feature that
  constructs, copies or destroys a USTRUCT through CppStructOps must go through the fake vtable on 5.7+.
- **Soft-path sub-path accessors** (`UEP10-01`, `UE4SS-2611b-10`): match what `[VND9-01]` shipped in build 3687.
- **New config AOBs for globals we never resolve** (`UE4SS-2611b-09`): PBZ GNatives, Dawnwalker ConsoleManager /
  GUObjectHashTables.
- **PBZ's other changes** (`UE4SS-2611b-08`): FField Next / Owner swapped (Genau probes Next from +0x10 and lands right;
  `FFIELD_OWNER` is never read), FUObjectArray's extra 0x28-byte tail, UClass / UScriptStruct shifts.
- **UEPseudo hooks, Conv_NameToString gating** (`UEP10-04/05`); **RE-UE4SS Lua, v4.0.0 upgrade notes, housekeeping**
  (`UE4SS-2611b-11..13`).
- **UE 5.9**: new hard-coded ENames (`ClassProperty` 42, `SoftClassProperty` 43, `QueuedActor` 296) are harmless
  (`VND10-07`); `origin/5.8` past 5.8.4 changes nothing for us (`VND10-08`); `origin/5.9` past the tag changes nothing in
  the layout set (`VND10-09`).
- **patternsleuth `8bcbd43`** (StaticFNameConst on 5.7+) is audit #8's `PS84-01`; the other eight commits are a memory
  testkit (`PS10-01/02`).

## SUBMODULES — HOLD

- **zydis** `a95bb71` → candidate `4b31c34`: HOLD. The rig on DumperTest Shipping (5.4), DumperTest58 Shipping,
  UE427_3rdPerson Shipping and Nekopara (1,269,946 `.pdata` starts): Denken's E2E output and Falsch's verdicts
  byte-identical; 0 Denken-visible CODE rows, 0 OK→FAIL, 0 length changes. The delta's only visible decode change is
  66-prefixed PTWRITE going OK → DECODING_ERROR (#UD on hardware, 0 occurrences).
- **minhook** `d94c64d`: nothing after the last review; its HOLD stands.

## WHAT WAS NOT COVERED

- No game ran: PBZ is not installed, and every PBZ row is predicted from upstream's config.
- `vendor/UnrealEngine` stays on `5.8.4-release`; the preview tag was read by path-restricted diffs.
- Dumper-7 and minhook brought nothing new to read.
