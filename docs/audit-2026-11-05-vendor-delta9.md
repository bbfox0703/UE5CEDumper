# Vendor audit #9 — RE-UE4SS / UEPseudo / Dumper-7 / zydis deltas + the UE 5.9 branch · 2026-11-05

> **Provenance.** `vendor/` reference clones are gitignored and **unpinned**; every machine must re-sync with
> `sync_tools.ps1`. That script never moves `vendor/UnrealEngine`, so check a new tag out by hand. State audited here:
>
> | clone / submodule | before (audit #8) | after |
> |---|---|---|
> | `vendor/UnrealEngine` | `5.8.4-release` `f45729c0b6f0` | **unchanged**: no newer release or preview tag exists. The fetch brought new branches `origin/5.9` and `origin/dev-5.9` (13,636 commits since the merge-base `f5c25e2ba290`, 2026-05-01), moved `origin/5.8` by 6 commits, and `origin/ue6-main` reads `Build.version` 6.0.0 |
> | `vendor/RE-UE4SS` | `cdbc0b66` | **`3da47fe9`** (27 commits, no merges) |
> | `…/deps/first/Unreal` (UEPseudo) | `fbb4fdd3` | **`2b77aa1f`** (10 commits; RE-UE4SS moved its pin) |
> | `…/deps/first/patternsleuth` | pin `1d90b02c`, upstream `8bcbd43` | unchanged; nothing on origin after `8bcbd43` |
> | `vendor/Dumper-7` | `90d1f39` | **`2c1eae3`** (7 commits: 3 non-merge, 4 merges) |
> | `vendor/minhook` (submodule, compiled in) | `d94c64d`, 3 behind | unchanged; upstream has nothing after `8af6b4a` |
> | `vendor/zydis` (submodule, compiled in) | `a95bb71` (5.0.0), 0 behind | **13 behind** `origin/master`; pin unchanged (HOLD, below) |
> | `vendor/nlohmann/json.hpp` (committed) | 3.12.0 | unchanged |

> **`sync_tools.ps1` printed a false `FETCH FAILED` on this run, and that is fixed (`795ce835`).** RE-UE4SS's new
> commits move its UEPseudo pin. A plain `git fetch` then also fetches that submodule by default (on-demand), through
> its `git@github.com:` URL from `.gitmodules`. Without an SSH key that fetch fails, so the exit code went non-zero even
> though `origin/main` had moved. The clone fetch now passes `--no-recurse-submodules`; the RE-UE4SS block already syncs
> those submodules itself, over https. The failure needs a pin-moving fetch to show, so the fix is read from source and
> from this run's log. A no-change re-run of the script is the regression check.

**Method.** One read-only workflow (`wf_7c64a1f7-b1a`). Four readers each took one area: UEPseudo, RE-UE4SS core, Epic's
engine (`origin/5.8` past 5.8.4, and `origin/5.9`'s net diff over 34 layout-critical headers plus six more), and the
small ones (Dumper-7, zydis, minhook, patternsleuth). Every finding a reader marked actionable got an adversarial
verifier (default posture: refute). The small-ones area had nothing actionable, so it got no verifier. 30 findings;
**none refuted**; one corrected (UE4SS-2611-F2's reasoning and scope; UEP9-03's title and reach). The verifiers added
11 "missed" notes, folded in below. The three FIX rows and the Cascade note were re-read by hand before filing. Nothing
was measured on a running game: every row is read from source.

## BOTTOM LINE

**No upstream commit forces code on us, and nothing we support today decodes wrong.** The new commits matter because
reading them exposed **three pre-existing gaps**:

1. A live soft pointer drops its sub-path, so a soft reference to a level actor shows the map's path.
2. Pipe FIRE shows a returned string as 16 raw bytes.
3. Several places, `ue5_dissect.lua` among them, miss the UE 5.5+ narrow string types.

Each of the three is a FIX, not BREAKING: the value is incomplete or shown raw, never wrong.

**UE 5.9 is unreleased, and stock 5.9 moves nothing we read.** No member offset, flag value, vtable slot or reflected
name changes in the core UObject types, and the ProcessEvent slot stays `0x250`. The AOB anchor functions are
byte-identical, or change only under defines that are off in Shipping. What a 5.9 title would hit is bookkeeping: a
missing version needle and fallback row, and Cascade's move to its own package (VND9-05).

## ACTION REGISTER

### VND9-01 · **FIX** · M — a live soft pointer drops its sub-path `[UEP9-02]`

**Ours.** `Ubel.cpp` `ReadSoftObjectPath` returns `PackageName + "." + AssetName`, or the UE4 / 5.0 `AssetPathName`,
and never reads `SubPathString`. The Live Walker prefers that text whenever it is non-empty. The same reader feeds
struct-array sub-fields, Phase G soft arrays, map / set / optional elements and the field-value preview. So a
`TSoftObjectPtr<AActor>` naming `/Game/Map.Map:PersistentLevel.BP_Door_C_3` reads `/Game/Map.Map`, the same text as a
reference to the map itself. Our own snapshot code names this defect: `[LIVEFUNCS-STEP2 B7]` (`ce9e3c1d`) appends
`:<sub-path, N chars, not copied>` because "without it an actor's pointer reads as its level's". That fix covered only
the snapshot, which shows a length and not the text. Every supported version is affected.

**Why the new commits matter.** UEPseudo `4e78802` / `56bd812` decode `SubPathString` as UTF-8 when the reflected type
says so. Engine headers confirm the switch: `SoftObjectPath.h` has `FString SubPathString` at 5.0.0–5.5.4 and
`FUtf8String SubPathString` from **5.6.0** on (5.6.0:468, 5.7.0:477, 5.8.4:470). There is no build switch that restores
the wide string; 5.6 only deprecates the wide overloads (`UE_SOFTOBJECTPATH_FSTRING_DEPRECATED`). The member set,
`sizeof` and the 16-byte `{Data, Num, Max}` header are unchanged, so **nothing of ours misreads today**. The hazard is
in the fix: copying the snapshot's "FString" assumption, or calling `ReadFString`, would misdecode every 5.6–5.8 title.

**Fix shape.** `ProbeSoftPathShape` keeps walking past the first field and also latches `SubPathString`'s FFieldClass
name and offset, beside `SOFTPATH_TOPLEVEL_MEASURED` / `SOFTPATH_SIZE_MEASURED`. `SubPathString` is reflected on every
version through `NoExportTypes.h` (4.18.0:961-966; inside `SoftObjectPath.h` from 5.7.0), so the probe works in
FProperty mode on 4.25+ and on 5.6.0 / 5.6.1 too. `ReadSoftObjectPath` appends `":" + text` when `Num > 1`:
`ReadFUtf8String` for `Utf8StrProperty`, `ReadFString` for `StrProperty`. UProperty mode (4.18–4.24) uses the FString
rule. Unmeasured FProperty mode at version ≥ 506 appends nothing rather than guess the width. Prefer the reflected type
name over a version rule, because a licensee can diverge. Red first: a `dll_core_test` case with a sub-path in each
width. Live: DumperTest58 (its `:PersistentLevel.<actor>` case is recorded in `docs/live-funcs-timeline-plan.md`), plus
one ≤ 5.5 fixture.

### VND9-02 · **FIX** · S — `ue5_dissect.lua` has no Utf8Str / AnsiStr types (UE 5.5+) `[UEP9-03c]` `[UE4SS-2611 missed]`

**Ours.** `TYPE_MAP` has `StrProperty = { vt = vtPointer, size = 8 }` and no `Utf8StrProperty` or `AnsiStrProperty`
entry. On UE 5.5+ such a field reaches `getTypeInfo`'s unknown fallback (`vtDword, fieldSize`), and the scalar branch
emits a `vtDword` row with Bytesize 16. CE's Structure Dissect then shows the low 4 bytes of the Data pointer as a
decimal, where an FString gets a pointer the user can follow. CSX and the pipe surfaces handle both types. From 5.6 an
inline `FSoftObjectPath` reaches this too, through its `SubPathString`. This is the bug class RE-UE4SS fixed in its own
Lua pusher table (`8d97f47e`).

**Fix shape.** Add both types as `{ vt = vtPointer, size = 8 }`, with a `dissect_test.lua` case through
`dissect._getTypeInfo`, and let `DissectLuaResource` pick it up. The CT embeds the dissect script, so check the
mailbox / CT contract rules (`Mimic.h`) for whether this is a contract change.

### VND9-03 · **FIX** · M — Pipe FIRE shows a returned or out string as its 16 raw header bytes `[UE4SS-2611-F4]`

**Ours.** `InvokeParamDialog` decodes every parameter from `result_hex`. `ResolveTrustedLayout` serves only struct
properties, and `DecodeParamValue` has no string arm, so a 16-byte `StrProperty` / `Utf8StrProperty` /
`AnsiStrProperty` falls to `DecodeBySize`, which prints `BitConverter.ToString`. `Fern.cpp` sends only `result_hex`. On
the queued path the post-call bytes are real, because `Stark.cpp` copies `ownedParams` back, and the CE route
(`CeInvokeReturn`) prints the text. A user who FIREs a function that returns or fills an FString from the UI sees
`A0-3B-…`. This affects every supported engine, FString included.

**Fix shape.** After a successful call, the DLL reads each return or out string parameter SEH-safe: Data, Num and a
capped length, wide for `StrProperty` and narrow for the other two. It sends them under an additive key (e.g.
`str_results [{off, text}]`), and `DecodeParamValue` prints it. Leave the engine-allocated buffer alone and never free
it: values left in the parameter block are deliberately leaked (`Frieren.cpp` `MakeSoftReference`, `Fern.cpp` invoke).
Red first: a `DecodeParamValue` test for a 16-byte `StrProperty` return that expects text. This is a pipe protocol
addition, so update `docs/pipe-protocol.md`.

### VND9-04 · **FIX-low** · S–M — the other UE 5.5+ narrow-string gaps `[UEP9-03a/b/d]` + verifier notes

None of these decodes wrong today: each shows hex or a dword, or refuses. Each sits beside a `StrProperty` arm that
works.

- **(a) Struct-array sub-field preview.** Only `StrProperty` gets `(str)`. `InterpretValue` has no string arm, so
  Utf8 / Ansi show the Data pointer's hex. `TArray<FSoftObjectPath>` reaches this from 5.6. Cosmetic.
- **(b) `TOptional<FUtf8String>` / `TOptional<FAnsiString>`.** `isStrInner` is `StrProperty` only. A 16-byte optional
  is classified Intrusive by size, then refused. The engine marks every string class intrusive
  (`bHasIntrusiveUnsetOptionalState`, 5.5.0 / 5.6.0). This needs a 5.5+ UPROPERTY of that type; `SubPathString` cannot
  reach it.
- **(d) Value search.** `kFString` is `StrProperty` only. ⚠ **Widening it needs two more sites switched to the narrow
  decoder**, or it misdecodes: `V1cOptionalGate` (`Ubel.h`, which gates intrusive optionals on Str / Name / Text only)
  and the next-scan reader in `Aura.cpp` (`ReadFStringAt`, wide).
- **Fold in:** `Scharf::RequiredAlignment` and Genau's `GetNaturalAlignment` have no Utf8 / Ansi arm and answer 0. That
  is harmless today: the fallbacks give 8 for a 16-byte value, and Intrusive is decided by size first. The Property
  Search type suggestions (`PropertyTypeSuggestions`) list `StrProperty` only; typing the name still works.

⚠ **Two wrong fixes are easy here.** Adding `Utf8StrProperty` to `isStrInner` without switching the decode from
`ReadFString` to `ReadFUtf8String` misdecodes, and so does widening `kFString` without the two sites in (d).

### VND9-05 · **PREPARE** · S — UE 5.9 bookkeeping, for when a 5.9 preview tag or title arrives `[UE59-03] [UE59-04] [UE59 missed]`

- **Version needle.** `kVersionNeedles` (`VersionNeedleScan.h`) starts at `{ "5.8.", 508 }`. A 5.9 title that hides
  its PE version reads as 508 through the structural ladder. That gives correct results, since everything we read is
  the same, but logs and the UI say 5.8. Add `{ "5.9.", 509 }`, bump the count's `static_assert`, and keep
  `AllNeedlesGateable` happy.
- **ProcessEvent fallback.** `Grimoire.h` has `case 508: return 0x250` and `default: return 0`, so 5.9 extrapolates to
  the right `0x250` but reports "EXTRAPOLATED". No virtual is added or removed in `UObjectBase.h` / `Object.h` on
  `origin/5.9`. Add `case 509` once it is measured (RE-UE4SS `VTableLayout_5_09_Template.ini`, or a 5.9 fixture), let
  `check_processevent_slots.py` confirm it, and fix the "5.9+ does not exist yet" comment.
- **Cascade moves to `/Script/CascadeRuntime`** (`b24378eab4b2` "Cascade moved to Plugin"; `DefaultCascade.ini` carries
  191 `/Script/Engine` → `/Script/CascadeRuntime` redirects, and `Cascade.uplugin` is `EnabledByDefault`). Our
  three engine-package lists (`Aura.h` `kEnginePrefixes`, `DumpAllService.cs`, `scripts/analysis/engine_paths.py`)
  do not know the package, so on a 5.9 title the DLL's game-only filters, Dump All's GameOnly skip and `diff_dumps.py`
  would count `ParticleSystemComponent` and the ~150 `ParticleModule*` classes as **game** classes (the noise
  classifier's leaf-base rule still catches the asset classes). Add `/Script/CascadeRuntime` to all three;
  `tools/check_engine_prefixes.py` keeps them in step.
- **AOB no-regression on the first 5.9 fixture:** include `GWLD_SF_2`'s anchor `FAudioDeviceManager::CreateMainAudioDevice`.
  It is the one anchor whose source changes in 5.9; its pattern wildcards the displacements and avoids the removed
  store, so no break is expected.
- **Beyond 5.9 (note only):** `origin/ue6-main` is 6.0.0. `UeVersionCode` returns 0 for major 6, and
  `AllNeedlesGateable` rejects a needle whose first byte is not `4` / `5`, so a UE6 title would rely on the structural
  ladder alone. For a 6.0 audit.

### Documentation — `[VND9-DOC]`, one bundle

1. **`SubPathString` comments** `[UEP9-09]`. These describe it as an FString for 5.1+: `Ubel.cpp` (the
   `ReadSoftObjectPath` header, the snapshot's "an FString after the FNames" and "N chars", which is a UTF-8 byte count
   on 5.6+), `Frieren.cpp` `MakeSoftReference` ("the soft pointer's FString"), `docs/technical-notes.md` ("+ FString
   SubPathString" for 5.1+) and `docs/live-funcs-timeline-plan.md` (measured on 5.8). Write "FString (≤ 5.5) /
   FUtf8String (5.6+)". `Grimoire.h` already says both. Do this before or with VND9-01, because that fix starts from
   these comments.
2. **USMAP header and one test** `[UE4SS-2611-F2]`, as corrected by the verifier. Add one sentence under
   `[A4-USMAP-ENUM-UNDERLYING]` in the `UsmapExportService` header. It says that a top-level EnumProperty with a null or
   unvalidated UEnum is written `[26][size-derived int]["None"]`, as Dumper-7 does, while RE-UE4SS `23e089e4` writes the
   bare integer, and both read in step in CUE4Parse. It also says that container inners stay `[26][Byte][name-or-None]`
   **on purpose**: UE serializes container enum elements by FName even with a null Enum (5.8.4 `EnumProperty.cpp`
   saving writes `EnumValueName`, `NAME_None` when null; `PropertyArray.cpp` never bulk-serializes an FEnumProperty), and
   CUE4Parse reads them with `ReadFName`. RE-UE4SS's new substitution sits at the top of its recursive `WriteProperty`,
   so it also writes a bare integer for container inners, which misaligns. **Do not "align" ours to it.** Point (1) of
   the header ("and so does this one now") is true at the top level only. Add the test
   `BuildUsmap_EnumProperty_WithNoEnum_WritesNoneAtTheWidthOfItsSize`; it goes green as written, so break the fallback
   once to see it fail.
3. **Correction to audit #7** `[UEP9-10]`, recorded here rather than editing that dated record.
   `audit-2026-09-05-vendor-ue582.md` says `SubPathString` "gained UPROPERTY() between 5.6.1 and 5.8.2". It was always
   reflected, through `NoExportTypes.h`: FString at 4.18.0:961-966, 5.0.0:1625-1633 and 5.5.4:2256-2264, and FUtf8String
   at 5.6.0 / 5.6.1:2284-2292. At 5.7.0 the struct moved into `SoftObjectPath.h`, still `UPROPERTY()`. Audit #7's
   width-correct conclusion stands.

### Recorded, no action (verified)

- **UEP9-01:** `SubPathString` became FUtf8String at 5.6.0. Nothing of ours decodes its text as wide. Our payload size
  (`FSoftObjectPathSizeFor`) is width-independent. The snapshot reads only `Num`. Invoke copies engine-built bytes.
  A reflected `SubPathString` child is decoded by its type name.
- **UEP9-04:** UEPseudo `4390233` adds CASTCLASS flags to the three string property types. We never read CastFlags;
  types are identified by FFieldClass name. Engine values: `CASTCLASS_FUtf8StrProperty` 0x1000000000000000 and
  `FAnsiStrProperty` 0x2000000000000000 from 5.5.0; 5.8.4 adds `FVerseStringProperty` 0x0800000000000000.
- **UEP9-05..08:** FString copy / move and the leak fixes are UE4SS-wrapper hygiene, with no layout change. Calling
  ProcessEvent without UE4SS's own detour (`0fe6856`) is what our Stark trampoline already does, and more. Values in the
  parameter block are caller-owned, and we leak them on purpose. The Kismet directory getters have no counterpart.
- **UE4SS-2611-F1:** USMAP with a null or invalid UEnum. Before `4e6307e6` RE-UE4SS dereferenced null. Ours already
  writes a correctly sized integer, the same bytes as Dumper-7: `ReadPropertyEnum` returns 0 →
  `UsmapExportService` takes the size-derived fallback (pinned by `dll_core_test`).
- **UE4SS-2611-F3:** the Utf8Str / AnsiStr pushers (`8d97f47e`). Our invoke paths already pass both types as narrow
  strings, as parameters and as return values (`ParamBufferBuilder`, `InvokeParamDialog`, Fern invoke,
  `ue5_invoke_helper.lua` `fstringn`, `CeInvokeReturn`). The same bug class does exist in `ue5_dissect.lua`: VND9-02.
- **UE4SS-2611-F5..F9:** the `UObject:CallFunction` break was a Lua userdata dispatch slip; we identify UFunctions by
  meta-class FName. We never grow a game `TArray` (and upstream's `5c8583b1` grows twice in `LuaTArray.cpp`).
  `IterateGameDirectories`' exe/../../Content assumption has no counterpart: proxy deploy anchors on the exe's
  `Binaries\Win64` folder. The teardown-vs-running-batch race cannot happen in Stark: a drained request owns its
  params, and shutdown fails only queued ones. The UEPseudo pin bumps are covered by UEP9.
- **UE59-01/02:** no tag after 5.8.4-release. `origin/5.8` past it holds five Localization Automation commits and one
  AssetRegistry gather-cache change, so a 5.8.5 cut from it would be a no-op for us.
- **UE59-05..11 (stock 5.9):**
  - `FUObjectItem`'s union becomes `int64 FlagsAndRefCount` (still 8 bytes).
  - The Class.h / UnrealType.h / Script.h hunks are attributes, statics or editor-only.
  - Weak, soft, lazy, delegate and `TObjectPtr` layouts are unchanged. `TLazyObjectPtr` is deprecated for removal in
    6.0.
  - The ProcessEvent changes sit inside `DO_BLUEPRINT_GUARD`.
  - `FWorldContext::ThisCurrentWorld` (the hard-coded displacement in three GWorld patterns) is unchanged, including
    the embedded `FSeamlessTravelHandler` and the two FURLs.
  - The UWorld / APlayerController / CMC changes shift offsets we resolve only by reflected name.
  - Remote-object-handle builds move `FRemoteObjectId` into `UObjectBase`; this is off by default and already
    unsupported.
  - Lazy CDO creation and delayed compiled-in registration are opt-in, off by default and aimed at non-game programs.
  - 5.9 sets `RF_MigratingAsset` (0x20000000) on spawned actors, but our 0x20000000 test is gated to 5.0–5.3.
  - Also read and unchanged: SceneComponent.h (ComponentToWorld), DataTable.h (RowMap), EnumProperty.h,
    PropertyOptional.h, ObjectKey.h, TextData / TextHistory. `PrimitiveComponent.h` gains a bitfield, but component
    bools are written only through reflected FBoolProperty masks.
- **Dumper-7 (D79-01..03):**
  - `a168f92` makes the game name and engine version via KismetSystemLibrary null-safe. It is generator-only.
  - `42f37f6` hashes the class name in the BlueprintEvent dynamic lookup. This is an upstream generator regression
    that does not affect us; do not copy it.
  - `b0307ab` is a console message. The 18k-line `CppGenerator.cpp` diff is CR-at-EOL churn.

## SUBMODULES — HOLD

- **zydis (+13): HOLD.** Ten of the 13 commits only add decoding for new ISA extensions (WBNOINVD, IBHF, VEX maps 5 / 7, AVX10.2,
  MOVRS / PREFETCHRST, CMPccXADD-VEX, ACE / BSR0, AMX, USER_MSR / MSR_IMM) that shipped user-mode game code does not
  contain. `daaa71d` widens the ISA-set field, `0f44566` regenerates element-type tables, and `ff4f87e` sets the
  privileged attribute; Denken reads none of those fields. A bump would compile but fix nothing a game exercises, and it
  costs a rebuild, the suite and an in-game check (the v4 → v5 bump broke `Denken.cpp`).
- **minhook: HOLD** unchanged. Still `d94c64d`, 3 behind `8af6b4a`, with nothing newer upstream. Audit #7's triggers
  stand.
- **patternsleuth:** nothing after `8bcbd43`, so audit #8's PS84-01 stands and Himmel needs no re-scan (the maintainer's
  rule: re-scan only when upstream adds commits after `1d90b02c` that carry new AOBs).

## WHAT WAS NOT COVERED

- `origin/5.9`'s 13,636 commits were not read one by one. The net tree diff over the layout-critical headers was read in
  full, and the commit subjects were scanned. A header outside that list that changes a layout we read would be missed.
  The first 5.9 preview fixture is the real check.
- Nothing was run on a game. VND9-01 and VND9-03 need a live check after the fix; VND9-05 needs a 5.9 fixture.
- RE-UE4SS's per-game `CustomGameConfigs` had no change in this delta.
