# Vendor audit — UE 5.8.3 + RE-UE4SS (UEPseudo, patternsleuth) / Dumper-7 / minhook · 2026-09-24

> **Provenance.** `vendor/` reference clones are gitignored and **unpinned**: every machine must
> re-sync (`sync_tools.ps1`, then check out the UE tag by hand, because the script only fetches a
> detached HEAD). State audited here:
>
> | clone / submodule | before | after |
> |---|---|---|
> | `vendor/UnrealEngine` | `5.8.2-release` `16d75d847145` | **`5.8.3-release` `396c9f059903`** (`Build.version` 5.8.3, CompatibleChangelist unchanged 55116800) |
> | `vendor/RE-UE4SS` | `527a483b` | `f58e8f84` (15 commits) |
> | `…/deps/first/Unreal` (UEPseudo) | uninitialised (pin `36e87abe`; `b2e876da` at the 09-05 audit) | **`47475b99`, read for the first time** |
> | `…/deps/first/patternsleuth` | uninitialised (pin `1d90b02c`; `da8bfe4c` at 09-05) | **`1d90b02c`, read for the first time** |
> | `vendor/Dumper-7` | `b88241b` | `dd8fe34` (4 commits) |
> | `vendor/minhook` (submodule, compiled in) | `d94c64d` | **unchanged — deliberately 3 behind** upstream `8af6b4a` (see MINHOOK) |
> | `vendor/zydis` (submodule) | `a95bb71` | unchanged, 0 behind |
>
> The maintainer's installed UE 5.8 editor updated to 5.8.3 the same morning (CL 58210709).

**Method, and its limits.** Ten recon areas, one agent each; every non-info finding went to its own
adversarial verifier (default posture: refute), and the info rows of each area to one batch check.
139 findings; **none refuted**; one info row (CGC-8, a per-game census) judged incomplete.
⚠ **Not everything ran.** The run hit the usage window: 6 patternsleuth verifiers (PS-02, -03, -04,
-05, -09, -10), the info batch checks of `uepseudo-generated`, `uepseudo-layout` and
`patternsleuth`, and the planned synthesis + completeness critic never ran. Those rows are marked
**UNVERIFIED** below; the synthesis in this file was done by hand from the journal, not by an agent.
The per-finding JSON (claims, both-side citations, verifier reasons) was kept under `out\`
(`audit_full.json`, `audit_digest.md`) on the verification PC — scratch, not durable.

## BOTTOM LINE

**UE 5.8.3 is a no-op for the DLL and the UI.** The whole `Core/Public`, `CoreUObject/Public` and
`Engine/Private/GameFramework` trees are byte-identical between the two tags (subtree hashes), every
layout/gameplay header we read is the same blob, the patch number is consumed nowhere by us (5.8.3
still detects as `508`), no AOB anchor function changed, and the only cooked reflection change
(`ULandscapeComponent::bUserTriggeredChangeRequested` gains `CPF_NonTransactional`) is outside our
surface (UE583-01…06, 10, 11). **Measured, not only read:** DumperTest58 was repackaged with the
5.8.3 editor (Shipping + Development) and `tools/verify/fixture_smoke.py` diffed the DLL's own
verdicts against the 5.8.2 runs — object count identical (Shipping 34557), **57 of 57** offset /
object-array / FNamePool verdict lines identical, every AOB winner identical, on both configs.

**The findings that matter are older than this sync.** Reading UEPseudo and the RE-UE4SS per-game
configs for the first time surfaced real, pre-existing DLL defects on UE4-era and licensee titles —
chiefly **FunctionFlags keyed on version only** (VND583-01), which is wrong on two titles we track.

## ACTION REGISTER

Verified rows only, severity → effort. Each row is a `docs/todo.md` entry under
`[VENDOR-UE583-2026-09-24]`. Finding IDs in brackets.

### VND583-01 · **breaking** · S–M — `UFunction::FunctionFlags` is keyed on version only `[CGC-1] [UEP-G1] [UEP-02]`
Before 4.25 it is the only UStruct/UFunction offset not taken from a probe: `FunctionFlagsOffsetFor`
(`dll/src/Grimoire.h:466-472`) keys on version + CPN, and both readers (`Ubel.cpp:1530-1565`,
`Aura.cpp:6428-6440`) accept the first non-zero dword from `FUNCTIONFLAGS_SWEEP` (`Grimoire.h:480`).
On a shifted layout the table lands inside `UStruct::ScriptObjectReferences`. **FF7 Remake**
(tracked, 4.18 fork): FunctionFlags is at +0x90 (RE-UE4SS FF7R `MemberVariableLayout.ini`, and
every `test [reg+0x90]` of FUNC_Native / FUNC_HasOutParms in `ff7remake_.exe`). **DQ XI S** (4.18,
measured +0x10 shift in its own 2026-09-05 logs): true offset 0x98, table gives 0x88 = the low dword
of `ScriptObjectReferences.Data` — wrong for almost every Blueprint UFunction. Native functions are
unaffected (empty ScriptObjectReferences). The relation `FunctionFlags − PropertiesSize` is 0x48 on
4.08–4.24 and 0x58 on 4.25–5.08 (CPN included) in all 31 UEPseudo tables and the `assets/*.ini`
templates. **Fix:** primary = measured `DynOff::USTRUCT_PROPSSIZE` + (FProperty ? 0x58 : 0x48)
whenever the probe measured it; the version table only as fallback; replace `!= 0` acceptance with a
vote (NumParms at +4 equals the param-chain count, ParmsSize at +6 ≥ the last param's end), which
also covers Split Fiction's +4 tail (CGC-5). Pin 0x40→0x88, 0x50→0x98, 0x58→0xB0, 0x60→0xB8 in
`dll_helpers_test`. **Acceptance:** DQ XI S / FF7R logs name the chosen offset and a Blueprint
function's flags decode sanely.

### VND583-02 · **gap** · S — `UField::Next` is never measured in FProperty mode `[CGC-2] [UE4SS-2609-F1]`
`DetectUPropertyMode` returns for ≥ 425 without touching `UFIELD_NEXT` (`Genau.cpp:3378-3383`); the
FProperty arm of `ValidateAndFixOffsets` probes only `FField::Next` (`:3962-4034`); the UField::Next
probe exists only in the UProperty arm (`:4034-4086`). On a 4.25+ title whose UObject has an extra
8-byte tail — **The Pathless** (new upstream config: UField Next 0x30, SuperStruct 0x48,
FunctionFlags 0xB8) — `Ubel::WalkFunctions` (`Ubel.cpp:1762`) steps the wrong member and every
function list is truncated or wandering, while the run still reports validated. Property walks
self-correct. **Fix:** probe UField::Next over 0x20..0x48 on a UClass's Children chain in the
FProperty arm (next is a `Function` object or null); feed the shift into VND583-01's vote.

### VND583-03 · **gap** · S — `alignof(FName)` is 8 on non-CPN UE 4.11–4.21 `[UEP-01] [UE4SS-2609-F6]`
FName's member union includes a `uint64 CompositeComparisonValue` on 4.x through 4.21 (removed in
4.22), measured in the PDB templates and in NEKOPALIVE's log. `Scharf.h:80` returns 4 for
`NameProperty`; `ResolveElementAlignment` (`Ubel.cpp:1965-1970`) feeds the UProperty-mode TMap walk
(`Ubel.cpp:5460-5473`), so a TMap pairing an FName with a ≤4-aligned partner gets a stride 4 short
(e.g. 20 vs 24). Not yet shown wrong on a real title (downgraded from breaking). **Fix:** measure
it — `UScriptStruct::MinAlignment` of `/Script/CoreUObject.PrimaryAssetType` (single-FName struct)
through `GetStructAlignment`; or cross-check the computed value offset against the value property's
own `Offset_Internal`.

### VND583-04 · **gap** · S — `UEnum::Names` on 4.9–4.14 stores a `uint8` value; Neu reads 8 bytes `[UEP-03]`
`TArray<TPair<FName,uint8>>` there: 16-byte pairs, 1-byte value at +8 and 7 padding bytes that the
shipped code never initialises (NEKOPALIVE: MSVC writes only the qword key and the byte value).
`Neu::ReadEntry` (`dll/src/Neu.h:159-168`) reads an int64, so leftover heap bytes decide whether an
enum name resolves (`Ubel.cpp:147-148` exact match). **Fix:** latch the value width in
`DetectUEnumNames` (ENetRole's values are 0..n-1: int64 reads disagree, low bytes match → 1-byte),
version rule 411 ≤ ver < 415 as fallback; Neu unit test with garbage padding.

### VND583-05 · **gap** · S — top-level `WeakObjectProperty` shows raw hex instead of a null/stale label `[D7-01]`
`Ubel.cpp:4490-4508` is the only weak reader that publishes no label; the Live Walker Value column
shows index+serial hex. **Fix:** `fv.typedValue = objIdx > 0 ? "null (stale)" : "null"` when the
pointer does not resolve; same wording as the three siblings; `LiveFieldValue` display test.

### VND583-06 · **gap** · M — weak pointers to Garbage objects still resolve (up to ~61 s) `[D7-02]`
`ResolveWeakObjectPtr` (`Ubel.cpp:2747-2754`) checks index, live slot and serial only; UE's `Get()`
also rejects Unreachable|Garbage (`UObjectArray.h:1115-1121`). Label, do not null:
`Name (Class) [garbage]` — UE5 `RF_Garbage`/`RF_MirroredGarbage` (0x40000000) at `UObject+0x08`
(`OFF_UOBJECT_FLAGS`, `Grimoire.h:113`, defined but unread); UE4 PendingKill in `FUObjectItem.Flags`.

### VND583-07 · **gap** · M — 09-05 A9 (case-preserving-names bundle) steps 1, 7, 11 were never built and have no row `[BK-A9]`
Measured `FNAME_SIZE`, removing the NameProperty ElementSize override (`Ubel.cpp:1800`,
`:1833-1849`, `:1878-1879`), `FNAME_NUMBER`/member order. The Pathless is **not** a CPN victim
(refuted part of the finding). No CPN title has been measured; file the row, do not change code on
upstream notes alone.

### Latent (real mechanism, no title met yet)
| id | finding | fix in one line |
|---|---|---|
| VND583-08 `[D7-03]` | `ResolveWeakObjectPtr` lacks UE's `serial == 0 → null`; a `{N,0}` pair resolves (false Find-Refs hits via `Aura.cpp:3856-3859`) | add the check; stale label only when `objIdx > 0 && serial != 0` |
| VND583-09 `[UEP-L1]` | UE 5.2 gets the 5.3 FField/FProperty default layout (`Genau.cpp:3471` `>= 502`), and `:4277` treats that unmeasured default as proof | `>= 503`; gate the inference on a measured FField::Next |
| VND583-10 `[UEP-L2] [BK-A5] [PS-02 UNVERIFIED]` | static-struct GObjects recovery hardcodes the ≤5.7 array (Objects +0x10, Num +0x24) and ≤5.6 item (Object +0x00) (`Genau.cpp:684-700`, `Aura.cpp:1353-1360`) | score both 5.8 geometry and both item offsets; hand a 5.8 winner to `Aura::Init` |
| VND583-11 `[UEP-L3]` | `PropertyFamilyFor` (`Grimoire.h:621-622`) ignores CPN: family at Offset_Internal+0x34, not +0x2C | add a CPN argument, pass `DynOff::bCasePreservingName` at the 3 Genau sites |
| VND583-12 `[UEP-04]` | version labels off by one: FNameData is 5.7+, legacy int64 pairs 4.15–5.6, `UStruct::MinAlignment` int16 since 5.6 | relabel at the next touch |
| VND583-13 `[UEP-05]` | `UE_USE_COMPACT_SET_AS_DEFAULT` (5.7+, source-engine opt-in) makes reflected TSet/TMap 16-byte compact sets | guard: header only + log when a Set/Map ElementSize is not the sparse 0x50 |
| VND583-14 `[UEP-06]` | FSoftObjectPath shape gated on `>= 501`; wrong when the version is misdetected across 5.0/5.1 | resolve from the `SoftObjectPath` struct (PropertiesSize + `AssetPath` field) |
| VND583-15 `[D7-05]` | `UE_WITH_REMOTE_OBJECT_HANDLE` (5.6+, opt-in) also grows FWeakObjectPtr 8 → 16 bytes | add to the existing watch-item; no code |
| VND583-16 `[UE583-08] [BK-X3]` | `crc_authority_survey.py --oracle` rebuilds `tools/ue-crc-oracle.json` from installed editors: re-running now drops the 5.8.2 row | merge by sha256 before any re-harvest; do not run `--oracle` until then |
| VND583-17 `[PS-01]` | `tools/ghidra/find_gobjects.java` searches its GC-pool anchor only as UTF-16; since 5.8.0 it is a narrow string | search both encodings (+ absolute-address slots) |
| VND583-18 `[MH-3] [MH-4]` | see MINHOOK | — |

### Documentation (22 rows) — `[VND583-DOC]`, one bundle
Fixed in this commit: the vendor-pin provenance in `docs/reversing-nonstandard-ue-games.md`
(`[BK-F5] [D7-06] [UE4SS-2609-F10]`) and `DumperTest58.rc` trap (c) (`[UE583-09] [TC583-6] [BK-X4]`,
the stub now carries **5.8.3.0**, measured with `pe_version_probe.py`). Still open (fix at the next
touch of each file): `[UE583-07]` 09-05 D2 is resolved (`UE_STORE_OBJECT_LIST_INTERNAL_INDEX`
cannot move `UObject::Outer` to +0x28 — an Outer at +0x28 means CPN only) · `[MH-6]` MinHook reach is
±1 GB, not ±2 GB, and `UNSUPPORTED_FUNCTION` is a second install-failure code · `[BK-A10]`
`sync_tools.ps1:69` nlohmann note · `[BK-F2] [BK-F4]` the 09-05 follow-ups #2/#4 (this audit did
them; `reversing-nonstandard-ue-games.md`'s config count 34 → 36) · `[BK-X6]` the 09-05 audit's
point-in-time statements (add a provenance note, do not edit its body) · `[D7-04]` Solide's
"GObjects[0] trap" rationale for refusing Force-null on weak pointers is wrong in every version ·
`[CGC-3]` `docs/test-games.md` quotes a superseded Halo GObjects signature · `[CGC-4]` `GOBJ_V10`
labelled "Split Fiction (UE5.5+)" — upstream pins Split Fiction at 5.4 and V10 cannot match its site
· `[CGC-6]` `avowed-gobjects-fix.md` reads TOW2's "Changes were made to UObject" as layout; it is a
vtable override · `[UEP-D1]` ~a dozen comments put the FFieldVariant shrink at 5.1.1; it is 5.3.0 ·
`[UEP-D2]` CPN shift descriptions (FField::Flags moves +4; FProperty's leading fields do not) ·
`[UEP-D3]` stale version ranges in layout comments · `[UEP-11]` FUObjectHashTables "DO NOT ADOPT"
stands but three supporting reasons are stale · `[PS-11]` toolchain / playbook / Himmel provenance
docs predate patternsleuth being on disk · `[PS-12]` `GNAM_V6` labelled "GSpots UE5+" but its
`mov ecx,0x808` is the UE 4.22 `TNameEntryArray` allocation.

### Ideas (optional)
Verified: `[UEP-I0]` a template-driven gate for the other hand-transcribed layout tables, like
`check_processevent_slots.py` · `[BK-A11]` the deferred `FCName=` log-token rename. **UNVERIFIED
(patternsleuth, verifier never ran):** `[PS-03]` port patternsleuth's GUObjectArray string anchor +
caller walk (reported exact on both 5.8.3 configs) · `[PS-04]` the open "UE5 non-Shipping GNames"
todo is solved by patternsleuth's FNamePool technique (EName-string intersection + callee-filtered
singleton init) · `[PS-05]` `GOBJ_PS6` was deleted upstream as order-dependent; the three-operand
replacement is exact · `[PS-09]` BuildConfiguration / Stats / EngineVersionFingerprint as a
diagnostic prior · `[PS-10]` a pattern-free GWorld tier from the `UGameEngine::Tick` string anchor.
**Re-verify before acting on any PS row.**

## MINHOOK — HOLD, bump on a trigger `[MH-1…MH-8]`

Only four MinHook targets exist: `UObject::ProcessEvent` (`Stark.cpp:302`) and three user32 exports
(`Grausam.cpp:216/236/244`); only `hde64` is compiled. Every target starts with instruction forms the
HDE rewrite does not touch — the trampolines would be byte-identical after a bump (MH-2), and the
VirtualQuery fix does not touch near-module allocation (MH-5). **Risk of bumping (MH-3):** the new
HDE decodes VEX/EVEX/XOP but leaves the escaped opcode in `hs.opcode2`, which the unchanged
`trampoline.c:218` reads as a Jcc for 0x80–0x8F. **Risk of holding (MH-4):** the pinned HDE
mis-decodes 67-prefixed memory operands, `popcnt`, `psadbw`/`maskmovq` with reg ≤ 1 — none in our
targets. **Bump when:** upstream tags a release containing `72fca12..8af6b4a` (ideally with
`trampoline.c:218` guarded by `hs.opcode == 0x0F`); or a title logs
`MH_CreateHook failed: MH_ERROR_UNSUPPORTED_FUNCTION`; or a new hook target's first 5 bytes hold
VEX, 0F38/0F3A or tzcnt/lzcnt (then it is required). Bump = move the gitlink,
`build.ps1 -Target DLL` + `-Target Test`, then a live ProcessEvent-hook check.

## STATUS OF THE 2026-09-05 REGISTER `[BK-*]`

A1–A4, A6–A8, A10 (bar one line), A11 (bar a rename) are **done**; A5 is done in `Aura` but its
deliberately-deferred static-resolver half was never filed (→ VND583-10); A9 is partial and
unfiled (→ VND583-07). Follow-ups #2 and #4 were lost until this audit did them; #5's vendor pin
went stale again (fixed here).

## WHAT WAS NOT COVERED

The unverified rows above; the three info batch checks that did not run (`uepseudo-generated`,
`uepseudo-layout`, `patternsleuth` — their info rows stand on the finder alone); no completeness
critic ran, so surface outside the ten areas (e.g. the remaining ~990 UEPseudo files beyond the two
halves' scope, RE-UE4SS `assets/` beyond the configs and templates) is unswept; and CGC-8's census
of per-game `VTableLayout.ini` files is incomplete (19 files exist).
