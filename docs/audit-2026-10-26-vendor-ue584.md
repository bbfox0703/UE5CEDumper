# Vendor audit — UE 5.8.4 + RE-UE4SS (UEPseudo, patternsleuth) / Dumper-7 / minhook · 2026-10-26

> **Provenance.** `vendor/` reference clones are gitignored and **unpinned**: every machine must
> re-sync (`sync_tools.ps1`; it never moves `vendor/UnrealEngine`, so check the tag out by hand —
> since `19953711` the script names a newer release tag when one arrives). State audited here:
>
> | clone / submodule | before (audit #7) | after |
> |---|---|---|
> | `vendor/UnrealEngine` | `5.8.3-release` `396c9f059903` | **`5.8.4-release` `f45729c0b6f0`** (`Build.version` 5.8.4, CompatibleChangelist unchanged 55116800; 113 commits) |
> | `vendor/RE-UE4SS` | `f58e8f84` | **`cdbc0b66`** (43 commits, no merges) |
> | `…/deps/first/Unreal` (UEPseudo) | `47475b99` | **`fbb4fdd3`** (6 commits; RE-UE4SS moved its pin) |
> | `…/deps/first/patternsleuth` | `1d90b02c` | pin unchanged; **upstream has one commit after it**, `8bcbd43` (2026-09-30) |
> | `vendor/Dumper-7` | `dd8fe34` | **`90d1f39`** (44 commits — several dated before `dd8fe34`, because they arrived through a merge) |
> | `vendor/minhook` (submodule, compiled in) | `d94c64d` | **unchanged — still deliberately 3 behind** `8af6b4a`; upstream has nothing after `8af6b4a` (last 2026-09-08) |
> | `vendor/zydis` (submodule) | `a95bb71` | unchanged, 0 behind |
> | `vendor/nlohmann/json.hpp` (committed) | — | 3.12.0 = upstream's latest release (2025-04-11) |

> **`sync_tools.ps1` was wrong in a way this audit hit, and is fixed (`19953711`).** It initialised
> RE-UE4SS's two submodules but never fetched them, so their `origin` refs stayed frozen at the day
> they were cloned. Measured here: patternsleuth's frozen `origin` showed **nothing** after
> `1d90b02c`; after a fetch it showed `8bcbd43`. That is the exact check audit #7's patternsleuth
> rule says to run first, so the script alone would have answered "skip" wrongly. The same fix stops
> a failed fetch from being followed by a stale "0 behind".

**Method.** UE 5.8.4, patternsleuth, nlohmann, minhook and zydis were read by hand (below). The rest
ran as one read-only workflow: four readers (UEPseudo, RE-UE4SS core, RE-UE4SS per-game configs,
Dumper-7), each followed by one adversarial verifier over all of its findings (default posture:
refute), then a completeness critic. 44 findings; **none refuted**; the verifiers corrected four
(severity of CGC2-02, the history in CGC2-04, the scope of D7R-03 and D7R-04 — folded in below).
The critic found no uncovered commit that matters. The two breaking rows were re-read by hand before
filing. Nothing was measured on a running game: every row is read from source, and D7R-01 is a
prediction. Per-finding JSON: `out\va8\digest.txt.json` on the verification PC (scratch).

## BOTTOM LINE

**UE 5.8.4 is a no-op for the DLL and the UI.** `CoreUObject`, `Engine/Classes` and
`Engine/Private/GameFramework` are byte-identical between the tags (subtree hashes). `Core/Public`
changes only the crash-context enum (`ECrashContextType::Debug` becomes an alias of `Deliberate`);
`Engine/Public` changes one editor slot count; the Landscape headers gain non-virtual member
functions (no layout, no vtable). The patch number is consumed nowhere by us. No AOB anchor function
is in the diff.

**Nothing upstream changed forces a code change on us.** The UEPseudo headline — the UE 5.3+
`TSoftObjectPtr` layout fix — is what our DLL **already** does: both subtract the path size from the
element size, accept only an 8- or 16-byte envelope, and fall back by the same version rule
(UEP84-01 / UE4SS-2610-F7). **The findings that matter are older than this sync**, exposed by
reading upstream's handling of the same types:

- **Invoke cannot pass a picked object into a `TWeakObjectPtr` / `TSoftObjectPtr` /
  `TSoftClassPtr` / `TLazyObjectPtr` parameter** (VND584-01). We write the raw `UObject*`, which the
  engine reads as `{ObjectIndex, SerialNumber}` and resolves to null; FIRE reports `ProcessEvent OK`.
  Every 4.18+ title, all four emitters. It was flagged on 2026-08-13 and never filed.
- **`ue5_dissect.lua` shows soft and lazy fields as pointers** (VND584-02) — the third copy of the
  W5 / W9 bug, the CE-XML and CSX copies having been fixed.

## ACTION REGISTER

Severity → effort. Each row is a `docs/todo.md` entry under `[VENDOR-UE584-2026-10-26]`. Finding ids
in brackets.

### VND584-01 · **breaking** · M — invoke writes a picked `UObject*` raw into weak / soft / lazy params `[UEP84-03] [UE4SS-2610-F1]`

`ParamBufferBuilder.cs` `IsPickablePointerType` offers Pick… / self for `WeakObjectProperty`,
`SoftObjectProperty`, `SoftClassProperty`, `LazyObjectProperty` and `InterfaceProperty`, and
`WriteParam` sends all of them to `WriteUInt64LittleEndian` (its doc says these are "written as one
8-byte value, which is what a picked instance becomes"). The CE twins do the same:
`InvokeScriptGenerator.cs` (`writeQword`), `BakedScriptGenerator.cs` (`"pointer"`),
`scripts/ue5_invoke_helper.lua`. The DLL copies `params_hex` raw (`Fern.cpp` invoke_function, Mimic
`HandleInvoke`); nothing converts. `ParamBufferBuilderTests.PickablePointerType_WriteParam_WritesFullEightBytes`
pins the wrong premise.
**Engine:** a by-value soft / weak / lazy pointer starts with `FWeakObjectPtr {int32 ObjectIndex,
int32 ObjectSerialNumber}` (UE 5.8 `WeakObjectPtr.h:570-575`, `PersistentObjectPtr.h:272-274`);
`Internal_GetObjectItem` returns null for serial 0, a negative index or a serial mismatch. A picked
address `0x1F2A3B4C5D0` becomes index `0xA3B4C5D0` (negative), serial `0x1F2`: null, with the soft
path left empty, so `IsValidSoftObjectReference` returns false and `Conv_SoftObjectReferenceToObject`
null for an object that exists. No crash (UE bounds-checks the index); a silent wrong result.
Upstream: RE-UE4SS `03dbd5c0` added a weak setter that goes through the engine's `FWeakObjectPtr`
assignment and never stores the pointer; its soft setter still refuses.
**Fix shape:** weak — convert DLL-side to `{InternalIndex, SerialNumber}` (the serial read from the
object's `FUObjectItem`, as `Ubel::ResolveWeakObjectPtr` reads it), refusing serial 0 (allocating one
is the engine's `AllocateSerialNumber`, and RE-UE4SS's changelog records a 5.3+ crash from getting it
wrong) and the 16-byte remote-handle shape (VND583-15). Soft / SoftClass / Lazy — no qword encodes
them: move them to the empty-only family (`0x0` allowed) with a UI string saying why; building the
path from the object's names is a later M–L item. `InterfaceProperty` is a 16-byte `FScriptInterface
{UObject*, void* Interface}`: the qword write fills the object half only — check it in the same pass.
All four emitters plus the read-back decoder (`InvokeParamDialog`); rewrite the pinning test red
first. `docs/roadmap.md` still describes these types as correctly written 8-byte values.

### VND584-02 · **breaking** · S — `ue5_dissect.lua` maps Soft / SoftClass / Lazy to `vtPointer` `[UEP84-04]`

`scripts/ue5_dissect.lua` (embedded in the UI by `DissectLuaResource` / `DissectScriptGenerator`)
types `SoftObjectProperty`, `SoftClassProperty` and `LazyObjectProperty` as `{ vt = vtPointer, size =
8 }`; `WeakObjectProperty` is already `vtQword`. CE Structure Dissect then shows the weak pair as a
pointer and expanding it dereferences garbage. The CE-XML (W5) and CSX (W9) copies were fixed on
2026-08-13; this one was never changed. **Fix:** `vtQword` like Weak, plus a `dissect_test.lua` case
through `dissect._getTypeInfo`. (`InterfaceProperty` stays `vtPointer`: its first 8 bytes are a
`UObject*`.)

### VND584-03 · **gap** · S — a UE 5.7 licensee `FUObjectArray` variant that `Aura` would misread `[D7R-01]`

Dumper-7 `20299bd → 1f2e2d4 → a508058` add a sixth chunked layout, "UE5.7 variant observed in
Mistfall Hunter": Objects +0x10, MaxElements +0x20, NumElements +0x24, MaxChunks +0x04, NumChunks
+0x08. Stock 5.7.4 is unchanged. **Ours:** Genau validates such an address (relaxed row E, pass 2),
but `Aura::DetectLayout` fails every strict tier and its relaxed A/C block accepts `num > 0` read at
+0x14 — the high dword of the Objects pointer — so `GetCount()` reports a few hundred objects read
through a garbage chunk table; `Count > 0` suppresses Frieren's decoy recovery. Every GObjects walk
would be silently near-empty. **Predicted, not measured** (title not in the corpus); from an
ObjObjects-anchored pattern the same block happens to read correctly. **Fix:** (a) give Aura's
relaxed A/C and B blocks Genau's floor (`num >= 0x1000`, `max >= num`), which sends this case to
relaxed E; (b) add the row to `s_ue4ExtendedPresets` and Genau's presets after UE5-Extended (it
cannot steal a stock array: there +0x04 is ObjLastNonGCIndex, which fails `maxChunks <= 0x5FF`);
optionally the geometry in `kStaticGeometries`. Both pinnable in `dll_core_test`.

### VND584-04 · **gap** · M — by-name function lookup never searches implemented interfaces `[D7R-02]`

UE's `UClass::FindFunctionByName` walks the class's `Interfaces` when the name is not in its own
FuncMap (`Class.cpp@5.8.3:7027-7105`); Dumper-7 `3249d5c / 8661bf5 / 78454c5` now resolve and
dispatch through it. **Ours:** `Ubel::ResolveFunctionInChain` climbs `SuperStruct` only; nothing
reads `UClass::Interfaces`. All by-name resolvers use it (`UE5_FindFunctionByName`, Mimic
`CMD_FIND_FUNCTION`, Fern `invoke_function`, Dunste, Schlacht). An interface UFunction the implementer
does not redeclare (a native implementor of `IGameplayTagAssetInterface::HasMatchingGameplayTag`; a
Blueprint that leaves an interface event unimplemented) is "Function not found". A refusal, not
wrong data. **Fix:** probe `UClass::Interfaces` (`TArray<FImplementedInterface>`, 16-byte elements;
Dumper-7 `OffsetFinder::FindImplementedInterfacesOffset` is the reference) and add a fallback pass
that runs only after both existing passes miss, so every name that resolves today keeps its answer.

### VND584-05 · **latent** · S — the ProcessEvent fingerprint misses clang's byte-narrowed tests; the fallback table is stock-only `[CGC2-01]`

`Frieren.cpp`'s two ProcessEvent fingerprints match only MSVC's dword form (`F7 /0 [reg+disp32],
imm32`, plus SIB). **Measured (read-only scan of Avowed-Win64-Shipping.exe, clang-built: no Rich
header):** 0 dword hits for pattern 2; the hooked function at RVA 0x7BC9940 holds the narrowed pair
`F6 82 B1 00 00 00 04` (+0x2E) and `F6 83 B2 00 00 00 40` (+0x17B). Every Avowed log (9 of 9 runs)
shows `DetectProcessEvent (fallback) ... UE=503 primary=0x268`, then validation OK: it works because
the stock guess happens to be right. The new RE-UE4SS vtable configs show licensees that deviate:
TOW2 ProcessEvent at +0x278 (stock 5.4 +0x268; not new in this delta), Conan Exiles Enhanced +0x268
under its new 5.8 override (stock +0x250), Dune +0x2A8 (stock 5.2 +0x268). A clang-built title with
such a vtable would hook the wrong virtual until the validator condemns it, losing invoke, Live Funcs
and the mailbox's game-thread dispatch. No tracked title is both clang-built and non-stock. **Fix:**
add the narrowed forms (disp = FunctionFlags+1 / imm8 0x04, disp = FunctionFlags+2 / imm8 0x40, SIB
variants) inside the existing two-pattern conjunction; prove it on Avowed (the log must change from
`fallback` to `(pattern): match at vtable+0x268`); rerun the corpus no-regression sweep used for the
SIB variant; say in `Grimoire.h` that the table is a stock-only guess.

### VND584-06 · **latent** · S — `CMD_INVOKE` trusts a cached `UFunction*` `[D7R-03]`

Dumper-7 `015c04b` / `33b2dab`: a cached `UFunction*` goes stale when a Blueprint class reloads, and
a recycled slot can hold another class's same-shaped UFunction. **Ours:** the generated CE Invoke form
resolves `ufuncPtr` once at creation; Mimic `HandleInvoke` checks only that the address is a live
UFunction (meta-class name), and `Ubel.h` itself notes that a freed address re-taken by another
UFunction passes that check. The window is form-open → FIRE (one FIRE per form; map travel in
between). The pipe invoke re-resolves per call; Live Funcs re-keys by FName + Outer. **Fix:** require
the UFunction's Outer on the instance's class chain (or its interfaces, once VND584-04 lands);
otherwise a new error code that makes the FIRE handler re-run FIND once. A change in `CMD_INVOKE`'s
meaning, not its layout: record it in `Mimic.h` per the contract rules, bump if a script must tell the
code apart.

### VND584-07 · **latent** · S — two layout probes compare member FNames case-sensitively `[D7R-04]`

Dumper-7 `2a074f9 / fd735eb / 2c210dd` add name replacements for `min`/`max` because "FBox2D member
names [are] lowercase on some games": a non-CPN FName keeps the casing of its first registration.
**Ours:** `Genau`'s Offset_Internal probe assigns expected offsets with `fn == "A" || fn == "X"` …
`"D"`, and the FField::Name probe compares `name == expectedFirst || name == expectedSecond` — both
case-sensitive (the verifier found the second). A lowercase pool fails the FField::Name probe first
(`UNMEASURED_FFIELD_NAME`), then leaves Offset_Internal / ElementSize at the version table. Every
other reflected-name lookup already ignores case (`Ubel` `FieldNameIEquals`, `Wirbel` `IEquals`). No
title known. **Fix:** compare case-insensitively; a `dll_core_test` case feeding lowercase names.

### VND584-08 · **latent** · S — the soft payload uses an FName size nobody asked to measure `[UEP84-02]`

UEPseudo takes the path size from the reflected `SoftObjectPath` struct and pins `FName`'s size from
NameProperty at init. **Ours:** `SoftObjectPathPayloadSize` and `ReadSoftObjectPath` call the bare
`DynOff::SizeofFName()`, which never triggers `ProbeFNameSize` (only `Ubel::FNameSize()`, reached from
a NameProperty, does). A walk that meets a soft property first uses the rule value: on a 5.1 / 5.2
build with an outlined FName (4 bytes) that latches a wrong +0x08 envelope until a later NameProperty
re-latches (`CHANGED`). No known title. Residual of VND583-14, which proposed measuring
`PropertiesSize` but adopted only the field name. **Fix:** in `ProbeSoftPathShape`, also latch the
struct's `PropertiesSize` and prefer it; have the readers use the probing `FNameSize()`. Test: an
outlined-FName 5.2 fixture in `dll_core_test` SOFTPATH (red: 0x08, green: 0x10).

### Documentation — `[VND584-DOC]`, one bundle

| id | where | what |
|---|---|---|
| UEP84-05 + UE4SS-2610-F8 | `dll/src/Ubel.cpp` Phase G soft-array header and the arithmetic comment below it; `docs/technical-notes.md` soft table | Header still says `Weak(8) + Tag(4) + pad(4) + path` and "falls back to 0x28"; the code already makes the tag ≤ 5.2-only and derives the fallback. The range comment says `0x20 .. 0x48`; the real range is 0x20 (outlined FName, 5.3+) to 0x38 (CPN 5.1–5.2). technical-notes says 0x48 for 5.1–5.2 CPN; it is 0x38. |
| UEP84-06 | `dll/src/Himmel.h`, the `AOB_SPARSE_AV53_1` rationale | "stock UE 5.x PointerHash = ptr>>4 into the Murmur finalizer" — true for 5.0–5.7; 5.8 is `MurmurFinalize64` of the raw pointer. No code depends on it. |
| CGC2-02 | `docs/test-games.md` Ghostwire: Tokyo row | Upstream now names it "customized UE 4.27" (`e7ad28df`); our row says "UE505 detected (possibly UE4)" and predates the raise-only ladder. Reconcile with `README.md` (already lists it under 4.25–4.27); record the `UE=` line at the next session (not installed here). Verifier: gap → doc. |
| CGC2-03 | `docs/test-games.md` Halo section | Cites `Halo Campaign Evolved/UE4SS_Signatures/GUObjectArray.lua`, now split into `steam/` and `gamepass/`; the "paste-ready" `AOB_GOBJECTS_HALO_1` keeps the superseded `45 84 C0 … 4C 8B C9 48 89 01` form at 0x0B. Replace with the current wildcarded pattern (lea at +0x08). Residual of CGC-3. |
| CGC2-04 | `docs/avowed-gobjects-fix.md` (TOW2 paragraph) | Names one extra virtual and says it is layout-irrelevant; TOW2's config has two (`Unk_ResolveSubobject_0`, `Unk_GetConfigOverridePlatform_0`) and ProcessEvent sits at +0x278, not stock +0x268. Verifier: this was already so at `f58e8f84` — incomplete since CGC-6, not made stale by this delta. |
| CGC2-05 | `docs/reversing-nonstandard-ue-games.md`, the unpinned profile count | "36 shipped profiles" — 57 at `cdbc0b66` (VTableLayout.ini 19 → 23, MemberVariableLayout.ini 15 → 20). Pin it to a commit like its sibling line. |
| CGC2-06 | `docs/todo.md`, the Star Wars Jedi: Survivor line | "UE 4.27?" — RE-UE4SS pins 4.26 (unchanged since `f58e8f84`). |

### Recorded, no action (info rows, verified)

- **UE584-01** UE 5.8.4: no-op (BOTTOM LINE).
- **PS84-01** patternsleuth `8bcbd43` hardens `StaticFNameConst` for 5.7+, where `FName(const TCHAR*)`
  became a separate constructor (UE `3bb2a2e7bb54`) and call sites no longer load `FNAME_Add`. We use
  no such resolver. Our `GNAM_V7` call-site AOB keeps `41 B8 01 00 00 00` but targets FF7 Rebirth
  (UE4) only; our symbol route names `??0FName@@QEAA@PEB_WW4EFindName@@@Z`, which 5.8 still declares.
- **UEP84-07 / UE4SS-2610-F6 / CGC2-11** the pointer-hash override and detection concern
  `FUObjectHashTables`; we hash no UObject pointer and read no hash table.
- **UEP84-08 / UE4SS-2610-F12** `StaticValueSize` is `ArrayDim * ElementSize`, which we already read.
- **UEP84-09 / UE4SS-2610-F11** shutdown listener, Linux initializer / target modules: no counterpart.
- **UE4SS-2610-F2** our `out` flag is raw `CPF_OutParm`, so const-ref struct / array params are
  labelled out — cosmetic; the string path is right and must not be "fixed".
- **UE4SS-2610-F3 / F4** the 0x200 params-buffer overflow and the Lua out-param corruption are
  Lua-binding bugs our offset-based marshalling cannot have.
- **UE4SS-2610-F5** the vtable commits change UE4SS's ini-override sums, not its templates; we
  hardcode no UClass / UGameViewportClient / AGameMode / UDataTable slot.
- **UE4SS-2610-F9** `cdbc0b66` (run queued events outside the lock): our game-thread drain already does.
- **UE4SS-2610-F10** EventViewerMod's ≤ 4.21 fix concerns `ProcessLocalScriptFunction`; Live Funcs
  hooks ProcessEvent only.
- **CGC2-07** Titan Quest II's new layout corroborates our measured property family; do **not**
  adopt its `FBoolProperty` 0x74.
- **CGC2-08** the new Avowed config confirms our packed 20-byte `FUObjectItem` and UE 5.3.
- **CGC2-09** UClass member deviations (Jedi FO ClassCastFlags, Talos 2 CDO, Silent Hill F
  NativeFunctionLookupTable): we read no UClass member there.
- **CGC2-10** ~20 configs add StaticConstructObject / FName ctor / ProcessLocalScriptFunction /
  GNatives / GMalloc / GUObjectHashTables AOBs: none is a target of ours.
- **CGC2-12 / 13 / 14** deleted configs unreferenced; new untracked titles stock (Stalker 2 is
  clang-built); tracked titles changed only by boilerplate, their audit-#7 rows unaffected.
- **D7R-05** the UE 5.7 FFieldClass `ClassFlags`/`Id` reorder: we read only `FFieldClass::Name`.
- **D7R-06** the "lazy-init via AppendString" GNames fallback: the same shape is already ours.
- **D7R-07** upstream bounds fixes: ours already right; do not port the name-pool one (it rejects
  the newest block).
- **D7R-08** stale UFunction / BlueprintEvent dispatch: Live Funcs and per-call invoke already cope.
- **D7R-09** generator / SDK-only changes: none affects our readers or SDK export.

## MINHOOK — HOLD unchanged

Upstream has published nothing after `8af6b4a` (2026-09-08), so audit #7's analysis and bump triggers
stand as written. `Lager.h`'s mirror of `buffer.c` (build 3649) is gated by
`check_lager_vendor_sync.py`; the pin did not move, so it is unaffected.

## WHAT WAS NOT COVERED

No running game: VND584-01's null result is read from the engine source, not observed; VND584-03 is a
prediction for a title not in the corpus; Ghostwire: Tokyo is not installed. The completeness critic
checked every commit no reader examined and found none that matters (PolyHook's IAT change, C++ mod
event dispatch, the console-enabler mod, host plumbing, the non-version per-game settings keys, the
new FName-constructor signatures, Dumper-7's generator-only remainder).
