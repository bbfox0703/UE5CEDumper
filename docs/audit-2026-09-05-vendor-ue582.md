# Vendor audit — UE 5.8.2 + RE-UE4SS / Dumper-7 · 2026-09-05

**Scope.** `vendor/UnrealEngine` 5.8.0-release → 5.8.2-release (`16d75d847`) ·
`vendor/RE-UE4SS` `662df915` → `24b12662` (105 commits) ·
`vendor/Dumper-7` `c891b17` → `b88241b` (37 commits) · zydis / minhook / nlohmann currency.

**Method.** 10 recon agents, one per upstream area, each finding then re-checked by a dedicated
adversarial verifier whose default posture was refusal; then a synthesis pass and a completeness
critic. 100 agents, 0 errors. **88 findings → 82 survived, 6 refuted.** Every claim is required to
cite a `file:line` in `vendor/` *and*, where it alleges a gap, one in `dll/src/` or `ui/`.

⚠ **This document is agent-produced.** The verify pass exists because the previous vendor audit
inflated 5 gaps of which 4 were later downgraded to info. Re-derive before acting on a row —
the register rows below name the exact command or file for each.

---

## ⛔ READ FIRST — A2's constants were WRONG, and the correction is measured

The completeness critic found that **A2's proposed ProcessEvent vtable table would write wrong
constants for UE 5.0, 5.1 and 5.5**, because A2 was hand-derived from 9 PDBs while a per-version
oracle sat unread inside the tree: `vendor/RE-UE4SS/assets/VTableLayoutTemplates/` (UVTD-dumped,
one `.ini` per engine version 4.07→5.08, `VTableLayout_5_08_Template.ini` newly added in this range).

I re-derived the whole table independently from that oracle — counting non-comment entries and
collapsing the per-section repeated `__vecDelDtor` into one slot — and it **reproduces the critic's
numbers exactly while matching all five independently-known ground truths**:

| ver | 4.25 | 4.26 | 4.27 | 5.0 | 5.1 | 5.2 | 5.3 | 5.4 | 5.5 | 5.6 | 5.7 | 5.8 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| ProcessEvent | 0x210 | 0x218 | **0x220** | 0x258 | 0x260 | 0x268 | 0x268 | **0x268** | 0x278 | **0x260** | **0x260** | **0x250** |

Bold = corroborated by a source outside the oracle: 4.27 reproduces the repo's existing ground truth,
5.4 matches DragonSword (`docs/test-games.md:30`), 5.6 matches Lushfoil (`dll/src/Stark.h:298`),
5.7 matches Solarpunk (`docs/test-games.md:57`), 5.8 matches the audit's own PDB work.
4.26 = 0x218 also matches the FF7-Rebirth note's "stock 4.26" figure.

**So use THIS table in A2, not the one written in the A2 row.** A2's `>= 500 → 0x268` arm is wrong
for 5.0 (0x258), 5.1 (0x260) and 5.5 (0x278). The 5.5 delta has a named mechanism: 5.5 inserts
`GetVersePath` and `CollectSaveOverrides` ahead of ProcessEvent (5.4 = 77 pre-PE virtuals,
5.5 = 79, 5.6 = 76).

Reproduce with:

```bash
py - <<'EOF'
import os
R='vendor/RE-UE4SS/assets/VTableLayoutTemplates'
for v in ['4_25','4_26','4_27','5_00','5_01','5_02','5_03','5_04','5_05','5_06','5_07','5_08']:
    f=os.path.join(R,f'VTableLayout_{v}_Template.ini')
    idx=0; seen=False
    for line in open(f,encoding='utf-8',errors='replace'):
        s=line.strip()
        if not s or s.startswith(';') or s.startswith('['): continue
        if s=='__vecDelDtor':
            if seen: continue      # same slot, repeated per class section
            seen=True
        if s=='ProcessEvent': print(v,hex(idx*8)); break
        idx+=1
EOF
```

The same oracle also independently confirms **A3** (`FirstPropertyToInit=0xC0` at 5.08, which is why
retargeting the dead `>= 550` band would be a regression), **A4**'s marker premise
(`[FFieldClass] Name=0x0` at 5_07 vs `0x8` at 5_08 — measured, not reasoned), and **A6**'s two
derived constants (stock `Offset_Internal 0x44 → FieldSize 0x70` = +0x2C; case-preserving
`0x4C → 0x80` = +0x34). See §3 of the critique for the corroboration list.

---

# VENDOR AUDIT SYNTHESIS — 2026-09-05
Ranges audited: UnrealEngine 5.8.0-release→5.8.2-release (16d75d847) · RE-UE4SS 662df915→24b12662 (105 commits) · Dumper-7 c891b17→b88241b (37 commits)

---

## BOTTOM LINE

**(1) UE 5.8.2 — anything special?** **No.** 5.8.0→5.8.2 is a hotfix pair that changes nothing we read: every one of the 16 layout headers our DLL depends on is byte-identical across the two tags, the only delta on our whole surface is `ENGINE_PATCH_VERSION 0→2`, and our detector collapsing all 5.8.x to `508` is correct rather than lossy. Zero code action; nothing in our tree went stale from the clone moving off 5.8.0.

**(2) RE-UE4SS — anything for us?** **Almost nothing from them, but the cross-check paid for itself.** The two real engine deltas they encode (the FUObjectArray reorder, the `virtual ~FFieldClass()` +8 shift) we already handle and I re-verified byte-exact against Epic's source — but reading their work surfaced **six of our own defects** they never mention: a ProcessEvent vtable fallback table that is wrong for every UE5 band, an uncalibrated pre-4.25 bool offset, a missing 40-byte FUObjectItem stride, a version marker chain that tops out at 5.7, an SDK-export drop, and (from the string-types pass) an **FSoftObjectPath envelope offset that is wrong on every shipping UE 5.3+ title**.

---

## THE UE 5.8.2 ANSWER, CONCRETELY

**Is the 5.8.0→5.8.2 delta layout-relevant to us? No, and this was checked four ways.**

- Per-path `git diff --stat 5.8.0-release..5.8.2-release` returns **NO DIFF** on all of: `CoreUObject/Public/UObject/{UObjectBase.h, UObjectBaseUtility.h, Object.h, UObjectArray.h, Class.h, UnrealType.h, Field.h, ObjectMacros.h, Script.h, PropertyPortFlags.h}`, `Core/Public/UObject/{NameTypes.h, UnrealNames.h}`, `Engine/Classes/Engine/{World.h, GameEngine.h, Engine.h}`.
- A filename sweep over all **1166** changed files finds no other surface-matching header. Within `Runtime/Engine`, 39 files changed and exactly one is under `Classes/` (`Animation/AnimTrackPool.h`).
- The **only** conditional layout change anywhere in our blast radius is `UPackage::bHasBeenEndLoaded` moving from `#if WITH_EDITORONLY_DATA` to `#if WITH_EDITORONLY_DATA || UE_ENABLE_ASSET_READ_LOGGER` (`Package.h:332-349`; new file `Misc/AssetReadLoggerConfig.h:13` defines the macro `0`). It cannot reach us twice over: our DLL reads **no** UPackage member (`grep UPackage dll/src/` → two comments only, `Aura.cpp:1493`, `Aura.h:960`), and even at macro=1 the added 1-byte member lands inside existing padding before the 4-byte-aligned `PackageFlagsPrivate`, so **no UPackage offset shifts at all**.
- Revert check: 12 files in leg 1 + 6 in leg 2 = 18 cumulative, disjoint sets, line counts sum exactly (270+43=313, 233+18=251). Nothing hides across the 5.8.1 boundary. ⚠ Scope caveat for future audits: this disjointness does **not** hold in `Runtime/Engine` (2 files appear in both legs) — use the **line-count** sum, not the file count, since only that catches a partial revert.

**What is now stale because the clone moved off 5.8.0? Nothing.**

- `dll/src/Grimoire.h:136-139` cites `Field.h:101 @5.8.0-release` — still **exactly** correct; `Field.h` is byte-identical across 5.8.0/5.8.1/5.8.2. Do **not** re-pin it to 5.8.2: 5.8.0 is the informative boundary (the release the vfptr shift landed in).
- No 5.8.2 reference build is needed. `tools/ghidra/GROUND-TRUTH.md:546` already records a **measured** patch-level A/B (5.8.0 vs 5.8.1, identical pattern coverage on all five targets). That is stronger evidence than a header diff, since an AOB oracle tests compiled bytes.
- Cheap future shortcut, verified: `UObjectArray.h` and `ObjectMacros.h` are blob-identical across all three 5.8 tags (`9e13bf30` / `c019450a`). On the next 5.8.x point release, compare blob hashes instead of re-reading files.
- Optional cosmetic only: the preset comments at `dll/src/Aura.cpp:304` and `dll/src/Genau.cpp:238` still say "5.8 dev"; they are now confirmed against the release tags.

**One provenance correction worth carrying:** the AssetReadLogger re-gating that changes non-Shipping code shape near `InitUObject`/`StaticExit` (`Obj.cpp:87,:5893,:5968`; `AsyncLoading2.cpp:4771-4781`) landed in **5.8.1**, not 5.8.2 — those files are byte-identical 5.8.1..HEAD. So `UE5.8.1-StackOBotDev` (`docs/reference-builds.md:82`) already carries the post-change shape; only the two 5.8.0 DebugGame rows (`:80`, `:81`) hold the old one.

---

## ACTION REGISTER

Ordered severity → effort. Only rows that survived verification.

---

### A1 · **breaking** · effort M · confidence **medium** — FSoftObjectPath envelope is wrong on every UE 5.3+ title

**What:** `TPersistentObjectPtr` lost `mutable int32 TagAtLastTest` between 5.2.1 and 5.3.2 (commit `f027bfa856ce`; removed line confirmed in `PersistentObjectPtr.h`; 5.3.2/5.4.4/5.5.4/5.6.1/5.7.0/5.8.0/HEAD all declare only `mutable FWeakObjectPtr WeakPtr; TObjectID ObjectID;`, HEAD `PersistentObjectPtr.h:272,:274`). **FSoftObjectPath therefore sits at +0x08, not +0x10, from 5.3 onward.** We hardcode +0x10 everywhere and have no probe: `dll/src/Ubel.cpp:2670`, `:2798`, `:3924`, `:6050`; `ui/UE5DumpUI/Services/CeXmlExportService.cs:3098/:3103` emit the FName leaf at literal `"+10"` and `:3112` the AssetName leaf at `0x10 + fnameSize`; `ui/UE5DumpUI/Models/LiveFieldValue.cs:214` bakes it into the contract comment; `docs/technical-notes.md:371-380` documents the tagged layout as the only layout. `grep -rn "WithoutTag" dll/src` → nothing.

**Failure shape:** on Satisfactory (5.3), Manor Lords (5.5), EverSpace 2 (5.5), Lushfoil (5.6), Titan Quest II (5.7) — all in `docs/test-games.md` — a `SoftObjectProperty` read at +0x10 lands on `AssetName`, and the "AssetName" read at +0x18 lands on `SubPathString`'s FString `Data` pointer. Empty SubPathString (common) → truncated path (`SM_Rock` instead of `/Game/Env/SM_Rock.SM_Rock`). Non-empty → the heap pointer's low 32 bits are read as an FName ComparisonIndex, **fabricating** `SM_Rock.<arbitrary pool name>`. No crash (all reads go through `Macht::ReadSafe`), but the **CE XML export ships an entry whose `+10` offset reads the wrong field** — wrong data in a delivered artifact.

**Fix:** add a runtime-probed `DynOff::bWeakObjectPtrWithoutTag` on the Dumper-7 model (`vendor/Dumper-7/Dumper/Settings.cpp:10-35` finds `LoadAsset`'s `Asset` SoftObjectProperty and compares its ElementSize; `CppGenerator.cpp:4985` then uses `0x8 : 0xC`). Fall back to matching the field's own `FPROPERTY_ELEMSIZE` against the two candidate sizes (0x28/0x30 non-CPN, 0x38/0x48 CPN). Replace the hardcoded `+0x10` at the four `Ubel.cpp` sites; publish the resolved offset over the pipe beside `softArrayIsTopLevelAssetPath` (`Ubel.h:416`, `Fern.cpp:1484`) so the three `CeXmlExportService.cs` literals go away; update `LiveFieldValue.cs:214` and `technical-notes.md:371-380`.

**Same probe closes a second open lead:** `docs/audit-2026-08-13-early-code-findings.md:3364-3384` concluded the TagAtLastTest removal was "a LAZY-only defect" — that verdict rests on the now-falsified assumption that the tag is always present for soft. Note lazy needs `0x08 : 0x0C` (FGuid is 4-aligned), never `0x10`.

**Before shipping:** log the raw `FPROPERTY_ELEMSIZE` of one SoftObjectProperty on a live UE 5.3+ title (Satisfactory or Manor Lords) and record the number with game + version. That single measurement is the only thing separating this from `confidence high`.

**Do NOT touch** the `>= 501` FTopLevelAssetPath cut at `Ubel.cpp:335` — verified correct against 5.0.3 (`SoftObjectPath.h:252,:255` = `FName AssetPathName`) vs 5.1.1 (`:352,:355` = `FTopLevelAssetPath AssetPath`). That is a separate, correct axis.

---

### A2 · **gap** · effort S · confidence **high** — ProcessEvent vtable version-table fallback is wrong for every UE5 band

**What:** `dll/src/Frieren.cpp:1559-1560` says `>= 550 → 0x228` / `>= 500 → 0x220`. The `>= 550` arm is unreachable (encoding is major*100+minor, capped at 509 by `Fern.cpp:1721`), so **every UE5 game takes the 0x220 arm**, which is wrong by 0x28–0x48. PDB-measured on 9 reference binaries by resolving `??_7UObject@@6B@` + `?ProcessEvent@UObject@@UEAAXPEAVUFunction@@PEAX@Z` and locating the slot in `.rdata`:

| Engine | Measured slot | Config coverage |
|---|---|---|
| 4.27.2 | **0x220** | Shipping (reproduces existing ground truth — validates the method) |
| 5.3 / 5.4 | **0x268** | 5.4 Shipping **and** Development both 0x268 |
| 5.6 / 5.7 | **0x260** | 5.7 Shipping measured; 5.6 proven header-identical to 5.7 (zero-line diff of the non-editor virtual list) |
| 5.8.x | **0x250** | 5.8.0 Shipping, 5.8.1 Shipping, 5.8 Development, 5.8 Titan DebugGame — all 0x250 |

Live-game corroboration: Solarpunk UE5.7 `vtable+0x260` (`docs/test-games.md:57`), DragonSword UE5.4 `vtable+0x268` (`docs/test-games.md:30`), Lushfoil UE5.6 `0x260` (`dll/src/Stark.h:298`). The engine mechanism cross-checks: 5.8 deletes `PostInterpChange` (5.7.4 `Object.h:423`) and `IsDestructionThreadSafe` (`:625`), both non-editor and both declared before ProcessEvent → −2 slots → 0x260 − 0x10 = 0x250.

**Fix** at `dll/src/Frieren.cpp:1559`:
```
if      (g_cachedUEVersion >= 580) primary = 0x250;
else if (g_cachedUEVersion >= 560) primary = 0x260;
else if (g_cachedUEVersion >= 500) primary = 0x268;
else if (g_cachedUEVersion >= 427) primary = 0x220;   // unchanged
```
Record the per-band derivation in the comment. Mark 5.0/5.1/5.2 (no binary, no field data — they inherit 0x268, no worse than today's blanket 0x220) and 5.5 (header delta only: 5.6 removes 3 non-editor virtuals but adds 2, net −1, putting 5.5 at 0x268) as **UNVERIFIED**.

**Scope:** fallback-only. I replayed the repo's own patterns and masks (`Frieren.cpp:1423-1428` + the SIB variants at `:1470-1473`) over every slot in [0x100,0x300) on all 8 reference binaries — exactly **one** match per binary, always the real ProcessEvent. Do **not** touch detector order; the pattern scan stays primary. Also note the `{8,-8,16,-16}` delta loop at `Frieren.cpp:1585-1600` is effectively dead (it returns `primary` on the first readable slot), so widening it buys nothing.

**Blast radius today:** when the fallback fires on a UE5 game, `Stark::ShouldActOnValidationFailure` (`Stark.h:305`) removes the hook after 1500 ms and re-arms; invokes time out and Teleport/GodMode/Fly/See-through/Live-Funcs report unavailable. Degraded, not wrong data, not a crash — hence gap. The repo has already seen this shape: `Stark.h:301` records "DumperTest UE 5.4 Development: fallback primary=0x220, 0 fires".

**Prerequisite doc fix (do it in the same commit):** `docs/verification-register.md:3690-3693` currently says "⚠ **Do NOT 'fix' the version table instead**" and cites "Lushfoil 5.6 → table `0x228`". Two errors: the table cannot yield 0x228 (the `>= 550` band is unreachable — it yields 0x220), and its "slot position is a **build-flag** property, not a version property" is contradicted by the new measurements (5.8 Shipping/Development/DebugGame all 0x250; 5.4 Shipping+Development both 0x268). Update it to say the table is now measured, that the pattern scan remains primary regardless, and keep 5.0–5.2/5.5 flagged unverified. **See the flagged disagreement below.**

---

### A3 · **gap (latent)** · effort S · confidence **high** — delete the three dead `>= 550` bands, and fix the FunctionFlags sweep order

**What:** three sites gate on `g_cachedUEVersion >= 550`, which no detection path can produce (`VersionNeedleScan.h:57` tops at `{"5.8.", 508}`; `Genau.cpp:2740` caps at 509; `Fern.cpp:1721` rejects >509; the raise-only markers cap at 507):
- `dll/src/Ubel.cpp:1347` → `primary = 0xC0` (`ReadFuncFlagsAndParams`, the shipped struct-walk path — **missed by the original finder**)
- `dll/src/Aura.cpp:5989` → `primary = 0xC0`
- `dll/src/Frieren.cpp:1559` → `0x228` (subsumed by A2)

**Delete, do not repair.** RE-UE4SS's own templates say `FunctionFlags = 0xB0` in **all nine** versions 4.25→5.08 (`MemberVariableLayout_5_08_Template.ini:838`, and `[UStruct] UEP_TotalSize = 0xB0` corroborates). Retargeting `>= 550` to `>= 505` would be a **regression**: at 5.8, offset 0xC0 is `FirstPropertyToInit`, an `FProperty*` — non-zero for most functions, so the `!= 0` accept at `Ubel.cpp:1352` would latch a pointer's low dword as FunctionFlags, and NumParms/ParmsSize/ReturnValueOffset would then come from 0xC4/0xC6/0xC8.

**Second, real defect in the same code:** the fallback sweep `{ 0xB0, 0xC0, 0x88, 0x98, 0xA8, 0xB8 }` (`Ubel.cpp:1356`, `Aura.cpp:5994`) tries **0xC0 before 0xB8** — and 0xB8 is the correct FunctionFlags for a `WITH_CASE_PRESERVING_NAME` build (`MemberVariableLayout_4_27_CasePreserving_Template.ini:786`; `[UStruct]` total 0xB8 vs 0xB0, a uniform +8). Either drop 0xC0 or move 0xB8 ahead of it. Both readers are also CPN-blind (neither consults `DynOff::bCasePreservingName`, unlike `Aura.cpp:3146`, `Ubel.h:415`, `Neu.h:57`) and there is no runtime probe for this offset — correct shape is `primary = base + (bCasePreservingName ? 8 : 0)`. Latent (no CPN title measured); fold into **A9**.

Rewrite the comment at `Ubel.cpp:1341-1343` to record `0xB0 = UE 4.25 through 5.8` with an explicit DO-NOT. No test pins any of these constants, so the deletion is free.

---

### A4 · **gap** · effort S · confidence **high** — the raise-only marker chain stops at 507; a stripped 5.8 game badges as UE 5.7

**What:** `dll/src/Frieren.cpp` has markers for 503 (`:402-407`, tagged FFieldVariant), 504 (`:412-426`, CMC::GravityDirection), 507 (`:434-440`, `GetItemObjOffset()==0x08 && GetItemSize()==24`), and lazy 505/506 (`:640-648`, gated `< 506`). There is no 508 marker, and the 507 predicate is satisfied by a 5.8 binary too (`FUObjectItem` is unchanged 5.7→5.8, `UObjectArray.h:41-99`). A string-stripped 5.8 title reconciles to 507 and stops.

**A sound 5.8 marker already exists, latched and even already on the wire.** UE 5.8 made `~FFieldClass()` virtual (5.7.4 `Field.h:100` non-virtual → 5.8.0/5.8.1/5.8.2 `Field.h:101` `virtual`, **unconditional** — not gated on `UE_WITH_CONSTINIT_UOBJECT`), and `FFieldClass` has no base with `FName Name` first (`Field.h:66,:74`), so `Name` moves 0x00→0x08. Binary oracle: `??_7FFieldClass@@6B@` present in the 5.8 StackOBot Shipping PDB, absent in 5.7.4, with `??_7FField@@6B@` present in both as control. We already probe it (`Grimoire.h:153` `{0x00,0x08}`, `:169` `PickFFieldClassNameOffset` with a strict "ends in Property" suffix test on the 0x08 arm), latch it at `Genau.cpp:3716`, and publish it at `Fern.cpp:4962` as `ffieldclass_name`.

**Fix** — insert after `Frieren.cpp:440`:
```cpp
if (DynOff::bUseFProperty && g_cachedUEVersion >= 500 && g_cachedUEVersion < 508
    && DynOff::FFIELDCLASS_NAME == 0x08) {
    LOG_WARN("UE5_Init: structural marker (FFieldClass::Name@+0x08 = vfptr, UE5.8+) "
             "but version=%u — raising floor to 508.", g_cachedUEVersion);
    ptrs.UEVersion = 508; g_cachedUEVersion = 508;
}
```
The `>= 500` guard is **not** cosmetic: without it a hypothetical FFieldClass-probe false positive on a UE4 title could raise 427→508, crossing the `>= 500`/`>= 501` gates at `Aura.cpp:3695` and `Ubel.cpp:335/:2758/:4205/:4367` — *that* raise would be breaking, unlike the harmless 507→508. The 504 marker at `:417` already carries the same guard.

**Do not bump** `Genau::kVersionDetectLogicRev` (`Genau.h:60`) — `Frieren.cpp:396-401` documents this ladder as runtime-only and re-applied every init. No UI change needed: `PointerPanelViewModel.cs:337` already offers "UE 5.8" and `Fern.cpp:1722` already accepts 508.

**Caveat to put in the log line:** this floor inherits the probe's confidence — a wrong `FFIELDCLASS_NAME` latch mislabels the version *and* has already broken the property walk, so the badge is not independent corroboration. Pin the raise predicate in **both** directions in `Test_FFieldClassName_Probe` (`dll/tests/dll_helpers_test.cpp:6691-6749`). Acceptance test cannot be run today (no string-stripped 5.8 title exists); file as ⬜ in `docs/verification-register.md`.

---

### A5 · **gap** · effort S · confidence **high (mechanism) / medium (prevalence)** — UE 5.7+ Test builds give a 40-byte FUObjectItem that is not a stride candidate

**What:** at 5.7.0-release, `Build.h`'s `UE_BUILD_TEST` block newly defines `ENABLE_STATNAMEDEVENTS (!FORCE_USE_STATS && !USE_STATS_WITHOUT_ENGINE)` and `ENABLE_STATNAMEDEVENTS_UOBJECT (ENABLE_STATNAMEDEVENTS)` (`Build.h:307-311`, identical at 5.8.2). At 5.6.1 both were global 0 (`Build.h:232-237`). That admits **both** `TStatId StatID` and `PROFILER_CHAR* StatIDStringStorage` into FUObjectItem (`UObjectArray.h:103-108`) → 24+8+8 = **40 bytes**, Object still at +0x08. Our sweep is `{16, 24, 32, 20}` (`dll/src/Aura.cpp:1071`); `Genau.cpp:148` `{16,24,20}` and `Genau.cpp:690` `{0x14,0x18,0x10,0x20}` also lack it.

**⚠ The failure mode is worse than "detection goes tentative."** `gcd(40,20)=20`, so stride 20 on the +0x08 pass hits the real Object field 1 in 2. Because `StatID`/`StatIDStringStorage` are lazily nullptr, the off-item reads return 0, which `ProbeStride` (`Aura.cpp:653-656`) counts as **`null`, not `bad`** — so `qualityOk` at `Aura.cpp:1098` (`bestNamed > bestBad`) **passes**, and `Aura.cpp:1106-1113` logs a confident "size=20, object-ptr offset=+0x08 (UE5.7+ reordered item) — 100 named, 0 bad". The tentative/LOG_ERROR net at `:1130-1152` never runs. `GetByIndex(i)` then resolves object i/2: **~50% of the pool vanishes silently**.

**Fix:** `{16, 24, 32, 20, 40}` with `NUM_CANDIDATES 5` — that exactly fills `ProbeResult results[5]` (`Aura.cpp:713`) and the existing static_assert; a 6th would need both widened. Regression risk is low for the same reason 32 was safe (against a real 20-byte item, stride 40 scores 100/0 vs the true stride's 200/0). `dll/src/Lineal.h` `SerialOffsetForLayout` already returns the correct serial offset for 40 in both modes — add the two rows at `dll/tests/dll_helpers_test.cpp:855-878` and fix the comment at `:840`, which hardcodes "The reachable stride set is {16, 20, 24, 32}". Consider `Genau.cpp:148` and `:690` so the static-GObjects resolver does not miss the same game.

**If any guard is added,** the hole to close is that a divisor stride with ~50% nulls and 0 bad reads as a confident success — not the 1130-1152 fallback.

**Unobserved:** `docs/reference-builds.md` has **zero** Test-configuration rows (43 Shipping / 30 DebugGame / 19 Development). Header-derived only.

---

### A6 · **gap** · effort S · confidence **high** — `DynOff::UBOOLPROP_FIELDSIZE` is the one pre-4.25 offset nothing calibrates

**What:** `dll/src/Grimoire.h:201` `inline int UBOOLPROP_FIELDSIZE = 0x70;` has **zero writers** repo-wide (nine read sites: `Ubel.cpp:753-755, 1261, 2467, 5015`; `Frieren.cpp:933`; `Solitar.cpp:183`; `Wirbel.cpp:1240`). `Genau.cpp:4133` derives the whole FProperty subclass-extension family from the probed `propOffsetOff` but has **no `else` arm** for UProperty mode — while every other UProperty-mode offset in the same function *is* derived (`UPROPERTY_OFFSET` `:4050`, `ELEMSIZE` `:4075`, `FLAGS` `:4114`, `UFIELD_NEXT` `:3964`).

**Failure shape, named in our own tree:** DQ XI S (`docs/test-games.md:19`, UE4.22, UProperty mode, `UField::Next=+0x38`, "+0x10 shift") puts the true `UBoolProperty::FieldSize` at 0x80. Ubel's local spread `{base, ±4, +8, −8}` covers only 0x68..0x78, so all five probes land on pointer fields, correctly reject, and `boolFieldMask` stays 0 — after which `Ubel.cpp:2592-2594` and `:5054-5055` fall back to `byteVal != 0`, so a **native C++ bitfield bool reports true whenever any sibling in its byte is set**.

**Why gap not breaking:** write paths fail safe (`Solitar.cpp:186-197` returns false; `Wirbel.cpp:1239-1254` returns `TP_ERR_REFLECTION`), and Blueprint bools are native (FieldMask 0xFF) where `byteVal != 0` is accidentally right. Wrong values are confined to native bitfields (`AActor::bCanBeDamaged`/`bHidden`, component flags), plus a scan that cannot mask a packed bool (`Aura.cpp:7702`, `:8251`).

**Fix** at `dll/src/Genau.cpp:4133`:
```cpp
else if (propOffsetOff >= 0) DynOff::UBOOLPROP_FIELDSIZE = propOffsetOff + 0x2C;
```
The `>= 0` guard matters — an unmeasured probe must leave 0x70 alone, as the FProperty arm does. **KEEP Ubel's `{base, ±4, +8, −8}` spread**: under `WITH_CASE_PRESERVING_NAME` the true relation is **+0x34**, not +0x2C (the 12-byte `RepNotifyFunc` FName pushes the four trailing pointers up 8), and `base+8` is what covers it. Do not narrow it afterwards.

**Also tighten** `dll/src/Ubel.cpp:2472` and `dll/src/Frieren.cpp:936`, which accept `fieldSize >= 1 && fieldSize <= 8` — the low byte of an 8-aligned pointer satisfies that, so those two can latch a garbage mask instead of falling through to 0. The other five copies already require `== 1`.

**Pin it** with a `constexpr int UBoolPropFieldSizeFor(int)` in `Grimoire.h` beside `PropertyFamilyFor` (Genau.cpp is compiled by no test target), asserted at 0x44→0x70 (stock) and 0x54→0x80 (DQ XI S shift), next to `Test_PropertyFamilyIsCoherent` (`dll_helpers_test.cpp:5367`). **Verification row** needs a paired observable: the offsets log line for `UBoolProperty::FieldSize` on a UProperty-mode game **plus** two sibling bitfield bools on the same native class showing *different* values in ClassStructPanel. Negative control on a stock pre-4.25 title (OctoPath 4.18 / NEKOPALIVE 4.11) — must be byte-identical, since 0x44+0x2C == the existing default.

---

### A7 · **gap (low end)** · effort S · confidence **high** — SDK export drops own-properties of empty-base structs

**What:** `ui/UE5DumpUI/Services/SdkExportService.cs:434` splits own-from-inherited on `f.Offset >= ownStart` with `ownStart = superPropsSize` (`:429-432`). A property below the super's reported size is classified inherited and never emitted.

**⚠ The upstream rationale is measurably false and must not be repeated.** I compiled probes on MSVC 14.51 x64: MSVC does **not** reuse a base's trailing padding — non-POD dtor, non-trivial ctor, vtable, POD and UObject-shaped bases all place the derived member at the base's full padded `sizeof`. (That is the Itanium/Clang/GCC rule, not MSVC's, and we only ever run in MSVC-built Windows PEs.) The **only** intruding shape is **empty-base optimization**: `struct Empty {}` sizeof 1 → derived member at offset **0**.

**Real, narrow scope:** UE ships genuinely empty non-editor USTRUCTs that are inherited from — `FEmptyPayload` (`Engine/Classes/Animation/AnimData/AnimDataNotifications.h:87-91`, with `FBracketPayload`/`FAnimationTrackPayload`/`FAttributePayload` deriving), plus `FUniversalObjectLocatorEmptyPayload`, `FPropertyBagMissingStruct`, `FARPointUpdatePayload`. `GENERATED_BODY()` adds no data, so `sizeof == 1` and `PropertiesSize == 1`. `SdkExportService.cs:90` enumerates `ScriptStruct`, so these reach `EmitStructBody`. Cost: exactly the offset-0 property, silently. **No UClass is affected** (every UClass descends from the fat, fully-packed UObject).

**Fix:** capture the class's own lowest property offset in `dll/src/Ubel.cpp` between the own-ChildProperties walk (`:914`) and the super-chain prepend (`:941/:951/:958`), **before** the sort at `:977`; publish it beside `super_props_size` at `dll/src/Fern.cpp:2108` as `own_props_start`; set `ownStart = min(superPropsSize, own_props_start)` at `SdkExportService.cs:430`, keeping the `:432` legacy fallback. Regression test alongside `ui/UE5DumpUI.Tests/SdkExportServiceTests.cs:339-365` using the **real** shape (`SuperPropertiesSize = 1`, own field at `Offset 0`), not an invented deep-intrusion case. **Skip** Dumper-7's `#pragma pack` / `SDK_ALIGN` layer — that addresses base re-emission fidelity, which our emitter already handles by padding the base to its reflected size.

**Related, do not conflate:** `Ubel.cpp:6197` (`ProbeRowMapOffset`) makes the same `superPropsSize`-as-floor assumption but UDataTable derives from UObject, so it is unreachable.

---

### A8 · **gap (documentation)** · effort S · confidence **high** — USMAP / SDK / .jsonl exports only cover loaded classes, and we never say so

**What:** all three exports page the live pool at export time and take whatever is there (`ui/UE5DumpUI/Services/UsmapExportService.cs:107-131`, `SdkExportService.cs:71-95`, `DumpAllService.cs:119-123/:157-165`). Blueprint classes, structs and enums exist in memory only after their asset loads, so an export taken at the title screen misses most BP reflection. Nothing in `docs/` or the UI says this — `docs/export-formats.md` has **zero** occurrences of "usmap", despite `CLAUDE.md:294` claiming that doc covers "USMAP export rules".

**Fix:** (a) add the USMAP section `CLAUDE.md:294` already promises to `docs/export-formats.md`, with the loaded-only caveat covering `.usmap`, the SDK header and the `.jsonl` Dump All; (b) fix the stale index row either way; (c) one UI line next to the export buttons (`Resources/Strings/en.axaml:808-813`, currently bare labels with no tooltip), modelled on the existing disclosure pattern `str.VS.Banner` (`en.axaml:116`) / `str.Dump.MetaNote` (`en.axaml:72`) — e.g. *"ⓘ Covers only classes loaded right now — Blueprint types appear once their asset loads. Load a level first for full coverage."*

**Do NOT build force-loading.** UE4SS's own opt-in (`bc73b384`) needs `UAssetRegistry::LoadAllAssets()` driven on the game thread and its tooltip warns it needs several GB and "the game is likely to crash if you keep playing afterwards".

---

### A9 · **gap (latent, dormant)** · effort M · confidence **high on layout / zero population** — the case-preserving-FName bundle

**Status:** WITH_CASE_PRESERVING_NAME = WITH_EDITORONLY_DATA (`NameTypes.h:32-33`) = 0 in every packaged shipping build. `docs/verification-register.md:4331` records "swept 9, ALL FALSE"; the one title ever claimed CPN (Titan Quest II) was live-injected and logged `votes standard=20, CPN=0` (`docs/verification-register.md:6176`, `docs/test-games.md:13`). **Zero known live victim** — but the tree currently encodes a wrong constant in ~15 places and three docs assert it as fact, so bundle it and land it once.

**The fact:** UE declares CPN `FName` as ComparisonIndex(4) + Number(4) + DisplayIndex(4) = **12 bytes, alignof 4** (`Core/Public/UObject/NameTypes.h:1257-1267`, no `alignas` on `class FName` at `:631`). Our `0x10` is the **UObject Name→Outer slot gap**, not `sizeof`. Dumper-7 draws exactly this distinction: it derives 0x10 as the slot span (`OffsetFinder.cpp:317`) and then sets `Off::InSDK::Name::FNameSize = 0xC` (`:361-367`), re-deriving it from a real NameProperty's ElementSize (`:385-396`).

**Work, in one change:**
1. **Measure, don't hardcode 0xC.** Add `DynOff::FNAME_SIZE` from the modal `+DynOff::FPROPERTY_ELEMSIZE` over N sampled `NameProperty` FFields (that offset is itself probed independently at `Genau.cpp:4040-4073`). Prefer this to Dumper-7's single named `PlayerStart.PlayerStartTag`, whose own comment concedes it fails on small template games. Fall back to the bool-derived value on <N agreement.
2. **Split the two constants.** Keep `bCasePreservingName` answering the **slot** question — 0x10 is correct for `UOBJECT_OUTER` and `UFIELD_NEXT` (`Genau.cpp:3277/:3324/:3768`); do not touch those.
3. **Fail-WRONG sites → sizeof form:** `Ubel.cpp:340, 1606, 2757, 3085, 3187, 4204, 4366, 5594, 5661`; `Aura.cpp:3146, 3169`, and the `scriptDelegateSize` line inside `Aura.cpp:3705`.
4. **Must STAY padded (do not blanket-replace):** `Aura.cpp:6283` (both `innerStride` and `sharedPtrOffset`), the `innerStride` line at `Aura.cpp:3705`, `Ubel.cpp:6226` and `:6365`, and their three C# consumers `CeXmlExportService.cs:3525`, `CsxExportService.cs:869`, `LiveWalkerViewModel.cs:1444`.
5. **`Neu.h` needs BOTH, chosen by format:** `:149` (FNameData57 array) takes sizeof; `:157/:160` (Legacy `TPair<FName,int64>`) takes the padded slot. A single `fnameStride` parameter is unfixable under CPN — make `BuildLayout`/`ReadEntry` take the format's own stride and update callers `Ubel.cpp:159` and `Genau.cpp:5313`.
6. **`Scharf.h:57`** → return 4 for NameProperty in **both** arms (`alignof(FName)` is always 4). ⚠ This is **not** log-only as originally filed: `Ubel.cpp:1755-1759 ResolveElementAlignment` → `Macht::ComputeMapValueOffset` (`Macht.h:426`) / `ComputeSetElementStride` (`Macht.h:410`) is a **data** path. Update `dll/tests/dll_helpers_test.cpp:437-439` (+`:459`) in the same commit — it currently pins the wrong premise as a test.
7. **Invert the NameProperty override.** `Ubel.cpp:1613-1616` `ValidateArrayElemSize` currently *discards* the engine's correct 12 in favour of our 16. Stop overriding for this type; log at Warn when the engine's value disagrees with the measured `FNAME_SIZE`.
8. **Fold in A3's CPN half:** `ReadFuncFlagsAndParams` (`Ubel.cpp:1340`) and `ReadFunctionFlags` (`Aura.cpp:5988`) ignore `bCasePreservingName`; correct shape is `primary = base + (bCasePreservingName ? 8 : 0)` (0xB8, per the CasePreserving template).
9. **Docs asserting the wrong model:** `docs/lessons-learned.md:91`, `docs/technical-notes.md:21`, `docs/dll-spec.md:175`, `docs/audit-2026-08-13-early-code-findings.md:178` and `:4271` ("the engine's correct 16"), `dll/src/Grimoire.h:281` ("FName is 0x10 bytes … + pad" — there is no such pad), and the in-code comment at `dll/src/Ubel.cpp:1600-1606`. ⚠ That last one **is not a measurement**: `git log -S` traces it to commit `c65fdfc0`, whose message shows it was propagated for internal consistency and explicitly left U2 unverified.
10. **Stale exemplars:** `docs/roadmap.md:430` and `dll/src/Himmel.h:1056` still say TQ2 is CPN.
11. **Bound the CPN-2 REFUTED verdict** at `docs/audit-2026-08-13-early-code-findings.md:2689-2694` and `:3347-3352`: `FName::Number` is at +4 for UE ≥ 5.1, but at **+8** on CPN builds of UE ≤ 5.0 and all UE4 (4.25.4 `NameTypes.h:883-891`, 5.0.3 `:920-928` = ComparisonIndex/DisplayIndex/Number vs 5.1.1 `:1096-1106`). The current blanket "do not re-raise" is version-incomplete and already misled one auditor. If code is ever touched, do **not** add a version gate — publish `DynOff::FNAME_NUMBER` from the vote `Genau.cpp:3179` **already runs** over `+0x18/+0x1C` (which currently asserts the ≤5.0 order at `:3225-3231` and contradicts `Ubel.cpp:107-112`) and read it at `Ubel.cpp:95/:118/:487` and `Frieren.cpp:759`.
12. **Record the new ambiguity:** an Outer at +0x28 is consistent with CPN (sizeof 12) *or* with `UE_STORE_OBJECT_LIST_INTERNAL_INDEX` — never with 16. **See the flagged disagreement below.**

Leave the register row open. No packaged game exercises this; unit tests are the only realistic vehicle, matching the existing U2 disposition.

---

### A10 · **info (doc-only)** · effort S · confidence **high** — corrections bundle

Land together; none affects behaviour, none trips a gate (`tools/check_derived_counts.py` pins none of these lines).

| File:line | Fix |
|---|---|
| `docs/technical-notes.md:22` | **Delete** the stale line numbers `Aura.cpp:258 / Genau.cpp:192` (drifted 46 lines since commit `075a8769`, 2026-06-30 — they now land on a `} // namespace` and a MaxElements comment). Replace with a greppable pointer (the `"UE5.8"` row in the preset tables) rather than renumbering to 304/238, which only resets the clock. Mention the **third** encoding site: the Tier-2 relaxed row `"F"` at `Genau.cpp:310`, same layout with a permuted tuple `{0x08,0x0C,0x00,0x14,0x10}` — three rows to edit, not two. |
| `dll/src/Grimoire.h:328-329`, `dll/src/Genau.cpp:5021-5023`, `dll/src/Genau.cpp:5061` (user-visible LOG_WARN), `docs/technical-notes.md:11` and `:161` | Scope the "TStaticIndirectArrayThreadSafeRead + inline chunk table + `GetUObjectArray()` singleton" reasoning to **4.8–4.10** (all the 4.10.4 oracles measured). Name ≤4.7 separately: flat `TArray<UObjectBase*>` at FUObjectArray+0x10 (Data/Num/Max = 0x10/0x18/0x1C, i.e. our Flat-Base preset with Num and Max transposed), plain `extern GUObjectArray`, UObjectBase layout identical to 4.11 — blocked by the stride-8 raw-pointer element (8 divides every candidate and would alias catastrophically), no 4.7 pattern, no 4.7 oracle. Do **not** write "blocked only by"; state what is measured and mark the rest untested. Leave `dll/src/Himmel.h:135-141` and `docs/technical-notes.md:185-200` alone — already correctly scoped to 4.10. Floor stays 411. |
| `docs/verification-register.md:3690-3693` | See **A2** — the table yields 0x220 not 0x228 for Lushfoil, and the "build-flag not version property" claim is now contradicted by cross-config measurements. |
| `dll/src/Ubel.cpp:351-356` | Delete the unreachable "Fallback: try UE5.1+ layout in case version was misdetected" block — its guard is the exact negation of the condition that reaches it, and it re-reads the same `addr`. Do **not** "repair" it: a misdetected 5.1+ game returns at `:350` with a non-None PackageName and never reaches it. |
| `dll/src/Grimoire.h:435-436` | "`DLL_PROCESS_DETACH` … cannot distinguish a FreeLibrary unload from process exit" is a **false Win32 primitive** (`lpReserved` is exactly that discriminator; `Fern.cpp:543` states it correctly). Replace with the reason that actually carries the decision: even in the FreeLibrary case DETACH must not join threads under the loader lock. Leave `Heiter.cpp:424-425` alone — its "its `reserved` parameter is unnamed" is true and points at our own declaration at `:288`. |
| `dll/src/Grimoire.h:186-188` | `FARRAYPROP_INNER` is described as "the first subclass field after FProperty base layout" — inaccurate: `EArrayPropertyFlags ArrayFlags` precedes `Inner`, so Inner sits 8 bytes past the family base and is recovered by `Ubel.cpp:1026 ProbeInnerProperty`'s +8 delta. |
| `dll/src/Ubel.cpp:412` and `:5172` | FText comments still describe the pre-5.4 shape (`ITextData* Data` + `void* SharedRefController`); 5.4+ is `TRefCountPtr<ITextData>` + `uint32 Flags` (`Text.h:941-944`). The conclusions they justify remain correct in both representations — comment refresh only. |
| `dll/src/Ubel.cpp:5156-5159` | The comment implies the four Verse names we handle are the default set; they exist only under `WITH_VERSE_VM=1`, while `VerseDynamicProperty` is what the **default** (`bUseVerseBPVM=true`) configuration emits. |
| `docs/architecture.md:178` | `└── vendor/ ← Git submodules` is wrong for 3 of 6 entries. Retitle (`2 submodules + 1 committed header + 3 gitignored reference clones` — counts verified) and tag lines `:179-184`, pointing Dumper-7 / RE-UE4SS / UnrealEngine at `sync_tools.ps1`. Do **not** bill it as onboarding breakage: nothing links against the clones. |
| `docs/roadmap.md:633-637` | Fold 5.9 into the **existing** version-ceiling deferral row rather than opening a new one, and fix its wording: without a publisher thumbprint the fallback is the flat `504` at `Genau.cpp:4988`, not "a bias fallback". |
| `sync_tools.ps1:202-204` | The zydis comment blames "the v5 tag is not in the fetched tag set". Live `ls-remote` tops out at `refs/tags/v4.1.1` — **no v5 tag exists upstream at all**, and v4.1.x lives on `maintenance/v4`, not an ancestor of master. As written it invites a deeper tag fetch that cannot help. Also `:69` "~944 KB" is stale (~900 KB) since the 2026-08-23 `eol=lf` pin. |
| `docs/reversing-nonstandard-ue-games.md:41` | ⛔ **SPENT / INVALIDATED 2026-09-05.** Asked for a sentence saying UEPseudo is browsed on GitHub and *not* checked out. Commit `bc41ef71` checked it out and wired it into `sync_tools.ps1`, so that sentence is now flatly FALSE. The real defect at that line was a rootless relative path, fixed instead. Do not reinstate. |

---

### A11 · **idea** · effort S–M — optional, none is a defect

- **`FVerseStringProperty` is decodable to text.** Two hops: `FNativeString` → `FCopyOnWrite` → `TSharedPtr<FCopyOnWriteContents>` whose first member is `FUtf8String` at +0x00 (`VVMNativeString.h:33-44,:107,:130`; `SharedPointer.h:1286`; `SharedPointerInternals.h:429-445` confirms the TSharedPtr points at the object, not the controller). Split it out of the opaque block at `Ubel.cpp:5160`, guard the pointer with `Grimoire::IsUserspacePointer` (`Grimoire.h:50`), call `ReadFUtf8String(ptr, 0)` (`Ubel.cpp:301`). **Apply at both sites** — `:5160` and the Property-Search preview at `:5984-5987` — or the two panels will disagree. Treat `ptr == 0` as "(empty)", not a decode failure (`TIsZeroConstructType<FNativeString>::Value = true`). **Deferred:** no Verse/UEFN title exists in our corpus, and this is a standing opportunity, not a 5.8.2 delta (the three Verse headers are byte-identical 5.8.0..5.8.2).
- **Record the two unhandled Verse property names** in a comment beside `Ubel.cpp:5157-5161` — `"VerseClassProperty"` (FClassProperty layout, unguarded upstream since 5.7, a real `UClass*` at +0) and `"VerseDynamicProperty"` — noting both fall through to the safe hex path and no injectable sample exists. If ever built, do the whole family behind **one** shared predicate, never a 30th literal across ~50 exact-match sites.
- **nlohmann/json 3.11.3 → 3.12.0.** Pure currency: no advisory (`security-advisories` and `advisories?affects=` both return `[]`), and all three candidate fixes are unreachable here (`get_ptr` has **zero** call sites repo-wide; the `parse(FILE*)` overload is never used; #4506's errno path is blocked by the `errno = 0` reset at `vendor/nlohmann/json.hpp:8620` plus MSVC's `strtoull` never producing EINTR). If bumped, verify against the published v3.12.0 sha256 and rebuild with `build.ps1 -Target DLL`. ⚠ **Do not** run `-Target Test` to validate — it does not compile the DLL and it overwrites `dist\UE5DumpUI.exe` with the ~107 MB non-trimmed build.
- **Optionally surface `ffieldclass_name`** in the System tab — `Fern.cpp:4962` already publishes it and no `ui/` file consumes it.

---

## NO-ACTION LIST — changed upstream (or looks changed), provably does not reach us

**Do not re-chase these.**

### UE 5.8.0 → 5.8.2
- `UPackage::bHasBeenEndLoaded` guard widened (`Package.h:332-349`) — macro defaults 0, we read no UPackage member, and even at 1 the byte fits existing padding so **no** offset shifts. Discard the "shifting PackageFlagsPrivate/PackageId/LoadedPath" clause from the original finding.
- `UObjectGlobals.h:3392` `UE_DEPRECATED(5.9)` on `OnPackageLoadCompleted` + an `#if` swap around `OnEndLoadPackage` — **static** delegate members, no instance layout, zero references in our tree.
- `Class.cpp:5185-5190` `bSuccess &= ClassDefaultObject->Rename(...)` — editor rename path.
- `SavePackage2.cpp:99` `GMemoryMappedBulkDataAlignment 1→16` — cook-time CVar default.
- `Matrix.inl:429-445` `RemoveScaling` now ignores its Tolerance parameter — inline engine math we never compile against.
- `GameEngine.cpp` — a comment typo plus 2 lines added inside `SwitchGameWindowToUseGameViewport`. Our GWorld patterns anchor `UGameEngine::Tick` (`Himmel.h:813`) and `UWorld::FinishDestroy` (`Himmel.h:104`); no *changed function* is one any pattern anchors in, and AOB matching is address-independent so inserted code cannot shift a match. ⚠ Note the sharper form: it is false that "the diff touches no file the accessor paths compile from" — GameEngine.cpp *did* change; the correct statement is about functions, not files.
- `Obj.cpp:87/:5893/:5968` + `AsyncLoading2.cpp:4771-4781` AssetReadLogger re-gating — **landed in 5.8.1**, changes non-Shipping code shape near `InitUObject`/`StaticExit`, which no pattern of ours anchors on.
- Version constants (`Version.h:60` PATCH 0→2, static_assert `:67`; `Build.version` CompatibleChangelist 0→55116800) — `grep "PatchVersion|ENGINE_PATCH" dll/src/ ui/` returns **nothing**, every version gate in the tree is minor-granular (`Frieren.cpp:377,412,640,1560`; `Genau.cpp:3345,3350,3364,5098,5102`; `Ubel.cpp:335,2758,4205,4367`; `KnownStructLayouts.cs:57`), and the value `508` has exactly one occurrence (the needle-table row) and **zero consumers**. Collapsing 5.8.x to 508 is the only representable outcome and is correct.

### 5.8 surface confirmed already handled (re-verified byte-exact — record so it is not re-derived)
- **"UE5.8" ArrayLayout preset** `{0x00,0x0C,0x08,0x14,0x10}` matches `FChunkedFixedUObjectArray` exactly (`UObjectArray.h:715-725`), with `ObjObjects` now first (`:1483`, "Placed first so hot fields are at offset 0"). Live at `Aura.cpp:304`, `Genau.cpp:238`, relaxed row `Genau.cpp:310`. Anti-steal walked preset-by-preset: Default/MindsEye fail the ≥0x1000 num floor, Multiversus fails max≥num, Flat/UE4-Extended read PreAllocatedObjects' high dword, UE5-Extended is rejected by `numC < 1` on `OpenForDisregardForGC` **and** by `LooksLikeDataPtr`. The **decode** ordering that matters is `Aura::DetectLayout`, not `Genau::ValidateGObjects`.
- **`FFieldClass::Name` +0x08** — `virtual ~FFieldClass()` is **unconditional** at 5.8 (`Field.h:101`, not gated on `UE_WITH_CONSTINIT_UOBJECT`), so +0x08 holds for every 5.8 configuration. Probed at `Grimoire.h:153/:169`, latched `Genau.cpp:3705-3716`, one writer repo-wide, unit-tested `dll_helpers_test.cpp:6691-6749`. Binary oracle for the record: `grep -a -c -F '??_7FFieldClass@@6B@' <Shipping.pdb>` = 1 on 5.8, 0 on 5.7.4, with `??_7FField@@6B@` = 1 in both as control.
- **FUObjectItem** 24 B, `Object` @+0x08, packing off (`UE_PACK_FUOBJECT_ITEM` is nowhere `#define`d in the whole engine). Packed constants also correct (alignBits 3, PtrMask 0x3FFF from `EInternalObjectFlags_MinFlagBitIndex = 14`, packed serial 0x0C).
- **UEnum::FNameData / FNamePool / FNameEntry** unchanged (FNameData head is a zero-line diff 5.7.4↔5.8.2; `FNameEntryAllocator` members identical; `FNameBlockOffsetBits` still 16; header still `{bIsWide:1, LowercaseProbeHash:5, Len:10}`). `UENUM_NAMES` is runtime-probed 0x30..0x120 with FName-substring validation (`Genau.cpp:5314-5348`); the FName header format is probed (`Serie.cpp:253-289`). `Grimoire.h:267`'s 0x40 is a fallback default, not a hardcoded offset.
- **Walk spine unchanged 5.7.4→5.8.2:** UObjectBase (0x28), UField::Next, UStruct Super/Children/ChildProperties/PropertiesSize/Script (0x40/0x48/0x50/0x58/0x60 — so `Script == PropertiesSize + 8` still holds), FProperty (Offset_Internal 0x44, sizeof 0x70, so `PropertyFamilyFor(x) = x + 0x2C`), UFunction, FFieldVariant, FOptionalProperty, FEnumProperty, TArray/FString/FText/FSoftObjectPath. `EClassCastFlags` gained nothing; `EPropertyFlags` gained only `CPF_ForcePostConstructLink = 0x2000000000000000`, colliding with none of the four CPF constants we read (0x4, 0x400, 0x01000000, 0x0800000000).
- **UClass grew** `bNeedsPostLoadSubobjectInstancing` + `PropertiesStartOffset` (ClassFlags 0xD4→0xD8, ClassCastFlags 0xD8→0xE0, CDO 0x110→0x118, total 0x208→0x210) — **we read no UClass member offset at all**; `grep "ClassDefaultObject|ClassCastFlags|FuncMap|AllFunctionsCache|PropertiesStartOffset"` over `dll/src/` + `ui/` returns only comments and a test name, and CDO identification is a `Default__` name compare at ~25 sites.
- **UEnum gained `EUnderlyingType`** (EnumFlags 0x59→0x5A) — declared **after** `Names`, and we never read EnumFlags. Same field is why Dumper-7's one engine-side change exists; we do not read it either.
- **5.8 partial classes** (`CLASS_Partial`, negative `PropertiesStartOffset`, negative `Offset_Internal`) — dormant (the only `UCLASS(Partial)` declarations in 5.8.2 are six `LowLevelTests/FoundationTests` classes), and our `FieldInfo::Offset` is `int32_t` with sign-extending address arithmetic. ⛔ The "our guards would drop such fields" mechanism was **REFUTED** — the eight cited clamps are TArray **pagination** cursors (`Ubel.h:956` says so) and the other two are UScriptStruct-inner guards that partials cannot reach. Do not relax any of them on the strength of that record.
- **Compile-time levers, all default-off**, with their diagnostic tells: `UE_PACK_FUOBJECT_ITEM` (handled — `Lineal.h:52-62`); `UE_WITH_REMOTE_OBJECT_HANDLE` (32 B item, covered by the stride sweep); `UE_STRIP_DEPRECATED_PROPERTIES` (**new at 5.8** for `FField::FlagsPrivate`, `Field.h:655`; would shrink FField 0x30→0x28 — absorbed by the Phase B probe); `UE_WITH_CONSTINIT_UOBJECT` (layout-neutral — the added virtual joins an already-polymorphic class); `UE_FNAME_OUTLINE_NUMBER` (**no detection** — ⚠ its tell is *not* garbage names: `Serie.cpp:578` returns "" for `Len<=0`, so it presents as **numbered names coming back EMPTY** while plain names resolve, visible as a high `lenZero=N` in the FNAM probe log line); `UE_STORE_OBJECT_LIST_INTERNAL_INDEX` (see flagged disagreement).
- **Verse property classes** `FVerseClassProperty` (5.7+) and `FVerseDynamicProperty` (5.6+) are unhandled but fail **SAFE** (correct full-width hex, blank typedValue) and are unreachable outside Verse-authored content — a grep of all of `Engine/Plugins` + `Engine/Source` for the `verse::type` family returns only the UHT parser and `ScriptMacros.h`, so a stock 5.7/5.8 shipping title contains **zero** instances.
- **`FFieldPathProperty`** has no DLL decoder — pre-existing since before build 700, byte-identical 5.7.4↔5.8.2, and already tracked at `docs/todo.md:1841` + `docs/roadmap.md:54` with the same effort/risk/rarity assessment. **Do not open a duplicate row.**
- **FText 5.4 `TSharedRef`→`TRefCountPtr`** (0x18→0x10) — our decoder reads the `ITextData*` at FText+0 (correct in both) and then scans layout-agnostically; `InferScalarSize` deliberately has no TextProperty entry so `TArray<FText>` strides by the engine's own ElemSize; and we refuse FText params outright (`ParamBufferBuilder.cs:272`), so the ref-counting half has no analogue.
- **`SubPathString` → `FUtf8String` at 5.6** — same 16-byte TArray header, and we never read the sub-path. Note the *load-bearing* half the original finding missed: it gained `UPROPERTY()` between 5.6.1 and 5.8.2 (`SoftObjectPath.h:465-470`), so it is now reflection-reachable — and already width-correct, because `FUtf8String` reflects as `FUtf8StrProperty` → name `"Utf8StrProperty"` → `ReadFUtf8String` (`Ubel.cpp:5134-5140`, `:5967-5970`, `:6467-6472`).
- **No 5.9 needle needed today.** A 5.9 build with intact VERSIONINFO already resolves to 509 (`Genau.cpp:2740`) and the pipe already accepts it (`Fern.cpp:1721`); a stripped one lands on 504 and then **self-corrects upward** via the runtime markers (`Frieren.cpp:434-440` → 507, and the 505/506 refine at `:640-649` stays alive precisely because 504 < 506). Nothing in the 505–509 band is version-gated, so the residue is a cosmetic badge. If 5.9 and 6.0 are ever added, do them together so the `kVersionDetectLogicRev` bump is paid once, extend **only** `ScanVersionTier23`'s firstByte loop (`VersionNeedleScan.h:276` — Tier 1 gates on the prefix's `+`, not the needle's first byte), and widen `Fern.cpp:1721`'s 509 bound.

### RE-UE4SS
- **FUObjectHashTables** — evaluated, **DO NOT ADOPT**. It needs an AOB for a Meyers singleton (`UObjectHash.cpp:816`) plus a hardcoded per-version offset table, which is what `CLAUDE.md`'s "never hardcoded" rule forbids; upstream ships **no built-in scanner** (only 4 `.lua` files across 3 of 34 game dirs, one of which bakes literal RIP displacements), `ShouldUseHashTableIteration` has **zero callers**, and their own Changelog still calls it "unused and a WIP". Our contract is structurally inexpressible through it anyway: our class gate is a case-insensitive **substring** name match (`Aura.cpp:1582-1591`) / super-chain name match (`:1667`) against a `UClass*`-keyed map; `buildHistogram=true` (`Fern.cpp:2744`) requires a complete pre-exclude tally a bucket cannot produce; and `newestFirst`/`sr.index` are GObjects-index contracts a TSet/TArray has neither of. Plus a namable misread: under `UE_UOBJECT_HASH_USES_ARRAYS` a 2+ element bucket puts the TArray Data pointer in `Elements[0]`, which the `FSetHashBucket` rule (`UObjectHash.cpp:101-104`) would hand back as a `UObject*` — silently wrong objects, with **no offset-only discriminator** between the two 16-byte encodings. File as `docs/ue4ss-hashtables-eval.md` (⛔ EVALUATED, DO NOT ADOPT, shaped like `docs/ce-ccode-eval.md`) and index it, so it is not re-proposed.
- **Gameplay-class churn** (UWorld ~24 renumbered bitfields + 0xA98→0xAE8, AActor 0x2B0→0x2A8, UGameViewportClient 0x3C0→0x3E0, ULocalPlayer, AGameModeBase; AGameMode vanished from the template) — every gameplay field we read is resolved by **reflected name** via `Ubel::FindFieldOffset`, which returns −1 on miss and is guarded at every consumer (`Hemmung.cpp:67-68`, `Edel.cpp:53-54`), so a vanished field no-ops a feature rather than becoming a wild read. The one non-reflected field (UGameViewportClient's `UWorld*`) tries reflection first and then a probe bounded by a **runtime-read** PropertiesSize (`Genau.cpp:4645-4666`), which absorbs the 0x3C0→0x3E0 growth automatically. **Do not add a 5.8 version branch here.**
- **Pre-4.25 STATS / DebugGame offsets** — upstream must hand-patch ~27 offset tables (`6e297d85`) gated on a **manual** `DebugBuild` flag because theirs are PDB-derived; ours are content-probed over ranges that already span the +8 shift (`Genau.cpp:3750` Children 0x28..0x80, `:3924` UField::Next 0x20..0x48, `:4012` Offset_Internal 0x28..0x70, with an `unmeasured` bit surfaced as `validated=NO (DEFAULTS)`). DQ XI S's +0x10 is the in-the-wild proof. ⚠ Two provenance notes: the cited `AdjustOffsetsForStatsBuild` no longer exists at RE-UE4SS main (moved into the uncloned `deps/first/Unreal` by `053d2a61`) — cite it blob-pinned at `6e297d85`; and their newer `7a608a63` adds a separate `StatsMode` because "any target can force STATS on regardless of its configuration", so the per-config table is a **default, not a determinant**.
- **The Conan Exiles ordering bug** (offsets computed before version detection) — our pipeline is ordered the other way (`Frieren.cpp:157 FindAll` → `:355 ValidateAndFixOffsets`), our unknown-version default is FProperty (the opposite of theirs), we self-correct FProperty→UProperty (`Genau.cpp:3742-3771`), and we already ship their `[EngineVersionOverride]` equivalent (`Flamme.h:74` → `Genau.cpp:4929`).
- **FF7 Rebirth's vtable fork** (+2 UObject virtuals ahead of ProcessEvent → 0x228 vs stock 4.26's 0x218) — the pattern scan (`Frieren.cpp:1513`, 0x100–0x300) is immune. ⚠ Correct the mechanism when recording: the version fallback is **not** "only just" immune, it is simply wrong here — `Frieren.cpp:1588-1592` returns `primary` on the first readable slot and never reaches the delta loop. Do not widen the window; the fire-count validator is the designed backstop.
- **The four Lua hook exception-safety fixes, the Lua stack growth, the wofstream/extended-path/uninitialised-FILE\* fixes, the proxy error-message rework, the ImGui/UVTD/config churn, and all ~20 submodule bumps** — all UE4SS-internal, several describing bug classes we handle more strictly (our ProcessEvent detour is `Routine::RunThreadGuarded`-wrapped, per-request SEH-isolated at `Stark.cpp:193`, per-promise try/caught at `:203-206`, with `Shutdown` draining every promise with −7 at `:346-359`; and `Stark.cpp:402` pushes a **copy** of the shared_ptr so a throw cannot break a waiter's promise).
- **Their 30s→120s scan give-up** has **no counterpart**: we have no AOB deadline at all (`dll/src/Macht.cpp` has zero chrono/GetTickCount hits; aborts are cooperative `Tot::Requested()` polls). The nearest structural analogue is `ProxyDeployViewModel.cs:1144` `PostInjectConnectWindow = 45s`, against a measured ~10 s worst-case init — ~4.5× headroom, and lapsing is non-destructive (it aborts nothing; the user just clicks Connect).
- **`.jmap` (trumank) and `.idmap`** — not worth adopting. ⚠ Correct the reuse map before anyone re-scopes: `.jmap` is **not** a re-serialisation of data we already have — object_flags, vtable, class_flags, class_cast_flags, CDO, `UClass::Interfaces`, and UEnum CppType/CppForm/EnumFlags all need **new runtime-probed offsets** (none exists in `Grimoire.h:88-268`), `min_alignment` needs de-static-ing `Ubel.cpp:1742` plus a wire field, `ue_binja` needs Binary Ninja (0 references repo-wide; our offline path is Ghidra), and jmap→.usmap is redundant since `UsmapExportService.cs` already emits v4. For `.idmap`: the only genuine delta is image-relative `execXxx` thunk symbols, and our nearest builder is `SdkExportService.GenerateFunctionSignature` (`:632`) — **not** InvokeScriptGenerator/ParamBufferBuilder — while `Aura::GetFunctionCodeAddr` is per-function, absolute-addressed, and refuses every non-`FUNC_Native` function (`Aura.cpp:6011-6013`), so it is a new bulk pipe command, not a wiring job.

### Dumper-7
- **Exactly one line under `Dumper/Engine/` changed in 37 commits**: a missing `!` at `UnrealObjects.cpp:619` (`GetSizeSignedPair`, a real logic inversion that made all enums 1-byte unsigned). It concerns `UEnum::UnderlyingType` — genuinely new at 5.8 (absent at 5.7.4) — which **we never read**; our enum width comes from the property's own ElementSize behind a `{1,2,4,8}` gate (`Ubel.cpp:5071-5083`, `:5991-6006`), and our SDK emitter infers the C++ type from entry values (`SdkExportService.cs:599-627`).
- **No** `OffsetFinder`, `Offsets.h`, `Enums.h`, `UnrealObjects.h`, `ObjectArray` or `NameArray` file changed at all. `Settings.h` gained only tool-behaviour flags. `CppGenerator.cpp`'s 18k-line diffstat is line-ending normalisation (390 lines whitespace-insensitive) plus a pure move into `SharedPredefinedMembers.cpp`, which has 22 `Off::` references and **zero** hardcoded offset literals.
- **The delegate assertion removal is not a layout hint** — `0ff0337` deleted a `static_assert(false, …)` planted as a zero-size PredefinedMember in a never-instantiated template stub (MSVC evaluates non-dependent `static_assert(false)` at definition time). `.Size = PropertySizes::DelegateProperty` was untouched and is still runtime-derived. Advance the audited-commit marker to `b88241b`.

### Vendor hygiene
- **All five repos are at upstream tip**, verified by live `git ls-remote` (not cached refs): Dumper-7 `b88241b64`, RE-UE4SS `24b126628`, UnrealEngine `16d75d847` (= the peeled `5.8.2-release` tag, `HEAD...tag` = `0 0`), minhook `d94c64d32`, zydis `a95bb7101`. Both submodule pins clean (leading space on every `submodule status --recursive` row); `git status --porcelain -- vendor/` empty.
- **`vendor/zydis/dependencies/zycore`** at `75a36c45` is **correct** — it matches exactly what `zydis@a95bb71` pins (`git ls-tree` confirms). Upstream zycore master has moved; bumping it independently would be a **de-sync**, not an update.
- **zydis version trap:** the header says 5.0.0 (`Zydis.h:89`) while `git describe` says `v4.0.0-121-ga95bb71`. No v5 tag exists upstream (highest is `v4.1.1`, and v4.1.x lives on `maintenance/v4`, not an ancestor of master). `Denken.cpp:155` (`op.mem.disp.size == 0`) is the correct v5 presence test against the vendored `DecoderTypes.h:148-165`; `has_displacement` has zero live call sites. This is a compile-time coupling, so a mismatch would be a build error, never silent bad data.
- **nlohmann/json.hpp is pristine 3.11.3** — sha256 `9bea4c80…` matches the published v3.11.3 release artifact, and `git log --follow` shows **one** commit (the initial import) with a clean worktree, which is a stronger permanent integrity check than a sha typed into a doc. Do **not** add a sha256 to `docs/architecture.md`: it would be an ungated, line-ending-sensitive hand-maintained constant (this repo has already been burned by exactly that class — `.gitattributes` `[FREEZEINJECT-CRLF-2026-08-20]`), and version/URL/SPDX already live at `json.hpp:1-7`.
- **Do NOT add a vendor CI gate.** `.github/workflows/ci.yml:52` inits only minhook+zydis and the three reference clones are gitignored, so the clones are absent from CI by construction and a submodule behind-count gate would go red on any upstream commit — noise on a ~10-minute AOT job. A report is the right shape and now exists. ⚠ Also do **not** naively widen `tools/bootstrap.py:200` to treat `+`/`U` as not-ok: `bootstrap.py:570-585` runs each not-ok row's install unconditionally under `--install`, so that would make `git submodule update --init --recursive` **destroy an in-progress dependency bump**. Put the drift in the detail string instead — and note `git status -sb` and `sync_tools.ps1:218-228` already report it.
- **UEPseudo (`vendor/RE-UE4SS/deps/first/Unreal`) is uninitialised** — record only. The per-version layout oracle our DLL actually cites (`Grimoire.h:71`, `:98-99`) is `assets/MemberVarLayoutTemplates/` (32 templates, 4.07→5.08, on disk), not UEPseudo. If ever wanted: `git -C vendor/RE-UE4SS -c url."https://github.com/".insteadOf="git@github.com:" submodule update --init deps/first/Unreal`. Leave `deps/first/patternsleuth` alone per `docs/toolchain.md:162`. ⛔ **SPENT 2026-09-05 — REVERSED by `bc41ef71`, which deliberately DID teach `sync_tools.ps1` to init it (RE-UE4SS is a gitignored clone, so its submodules sit on the clone side of the split, not the pinned side). Left standing this would send the next reader to undo a shipped build change.** ~~Do not teach `sync_tools.ps1` to init it~~ — `sync_tools.ps1:8-14` deliberately separates gitignored read-only clones from index-pinned compiled-in submodules.
- **`vendor/UnrealEngine` (5.6 GB) correctly absent from `docs/corpus-preservation.md`** — that doc's charter is the Ghidra corpus / archive roots / Steam uninstalls, and the clone is regenerable in one command (`sync_tools.ps1:138`). Ownership sits at `docs/toolchain.md:272`. ⚠ Two rationale corrections if this is ever recorded: the Footprint table's smallest row is **897.0 MB** (`:525`), not 1.6 GB, so the size argument does not stand — use the charter argument; and "nothing is imported from it" should be narrowed to "no `.rep` imports it", since `tools/ghidra/GROUND-TRUTH.md:450-468` makes the clone the corpus's **strongest** source of truth for structure questions.
- **`ue6-main`** was current at the 2026-09-05 10:37 fetch and has **already drifted** (remote `b356c957`, local `2e835f313`) — it is Epic's live mainline. `release` and `ue5-main` still match exactly. No 5.9 branch or tag exists anywhere (262 tags checked); 5.8.2-release is the newest release tag. For scale if a UE6 sweep is ever proposed: `ue6-main` is 18,507 commits ahead of `release`, and `blob:none` makes exploration expensive (a single `cat-file -t` on one missing commit blocked past 120 s lazy-fetching) — scope any probe to specific CoreUObject paths.

---

## REFUTED — do not re-raise

1. **"MSVC lets a derived struct place its members inside a non-POD base's trailing padding."** Measured false on MSVC 14.51 x64 across five base shapes (non-POD dtor, non-trivial ctor, vtable, POD, UObject-shaped) — the derived member always starts at the base's full padded `sizeof`. That is the Itanium rule. Only **empty-base optimization** intrudes, and only for empty USTRUCTs (→ **A7**, ~1000× narrower than filed). Dumper-7's `bHasReusedTrailingPadding` addresses *base re-emission fidelity*, a different concern.
2. **`Aura::FindDefiningClass` mis-attributes a padding-intruding property.** Cannot occur: the input is always a UClass (gated by `IsClassLikeMeta`, `Aura.cpp:4204-4211`, and the class-meta skip at `:7193`), a UObject-derived class is never empty and no UCLASS is over-`alignas`'d, and for Blueprint classes `FProperty::SetupOffset` (`Property.cpp:1269-1276`) makes own offsets ≥ super PropertiesSize by construction. Its comment at `Aura.cpp:4250-4252` already states the assumption.
3. **"UE4SS's proxy `MessageBoxA`/`ExitProcess` is a pattern our dxgi AppCompat audit forbids."** `docs/audit-2026-08-26-dxgi-appcompat-crash.md:225` says the opposite in bold — *"The rule is not 'do less in DllMain'"* — the rule is about **exports** called before the CRT. Their `load_original_dll` has one call site, inside `DLL_PROCESS_ATTACH`; their exports are naked asm thunks that cannot reach it; and our equivalent site is already behind `CrtReady()` (`Lugner.cpp:55`). Recording this as written would license stripping a legitimate diagnostic.
4. **"Off-thread TMap/TSet container reads are a hazard UE4SS just recognised."** One of the two cited sites is a `"(Map)"`/`"(Set)"` string literal, not a walk; the upstream commit concerns FUObjectHashTables, which we do not use; and the residual is the product's founding premise, already handled by SEH readers (`Macht.h:28-37`), torn-header rejection (`Macht.h:338-339`), and the arrayLimit clamp (`Ubel.cpp:3700-3702`). **Do not route container reads through Stark's game-thread dispatch.**
5. **"UE 5.8's `UEnum::UnderlyingType` would make our SDK-header enum width exact."** Our SDK export emits **no enum declarations at all** — `GenerateEnumDefinition`/`InferEnumUnderlyingType` are reachable only from unit tests, `GenerateFullSdkAsync`'s filter at `SdkExportService.cs:90` never collects Enum objects, and `.usmap` has no underlying-type field. (The *real* adjacent gap is separate and unfiled here: the generated header references enum types it never defines, so it does not compile regardless of width.)
6. **"sync_tools.ps1's good rewrite is uncommitted and one `git checkout --` from being lost."** The ~6-minute window was opened **and closed by this audit itself**; the work is commit `7850bf53` and the tree is clean. The underlying "no submodule handling" complaint is also not a gap: `build.ps1:573-579` self-heals an empty submodule during any build and CI inits explicitly.
7. **"`ReadSoftObjectPath`'s fallback protects against version misdetection."** It cannot fire (its guard is the negation of the condition that reaches it, and it re-reads the same address) — and the scenario it names never reaches it anyway (→ **A10**).
8. **"`UE_STORE_OBJECT_LIST_INTERNAL_INDEX` explains a future Outer-at-0x28."** In the hash-table context, `UObjectHash.cpp:39-40` static-asserts `sizeof(FName)==4` and `sizeof(UObjectBase)==40` ("this optimization exploits the 4 bytes padding after the FName"), so the index lands in existing padding and Outer stays at 0x20. **⚠ Partially disputed — see below.**

---

## VERIFIER DISAGREEMENTS — flagged, with which side I believe

### D1 — Should the ProcessEvent version table be fixed, or deleted? **Fix it.** (→ A2)
Three verifiers said do not touch it (`ue4ss-58-support` U4SS58-03 "measure, don't derive"; U4SS58-04 "delete the arm"; both leaning on `docs/verification-register.md:3690`, "⚠ Do NOT 'fix' the version table instead"). One verifier (`ue58-reflection-surface` F1) **did the measurement** they asked for: 9 PDB-resolved binaries plus three live-game confirmations, cross-checked against the engine's own virtual-list delta. **I believe F1.** U4SS58-03's objection was specifically that a −0x10 derivation off an unmeasured base is arithmetic, not truth — and F1 replaced the whole base with ground truth. The register's standing instruction was written when no measurement existed, and it contains an error of its own (it claims the table yields 0x228 for Lushfoil; the `>= 550` band is unreachable so it yields 0x220). U4SS58-04's separate point — delete the dead `>= 550` bands — is **also right** and is covered by A3; the two actions are complementary, not exclusive. The register text must be updated in the same commit or the fix will read as violating a standing decision.

### D2 — Does `UE_STORE_OBJECT_LIST_INTERNAL_INDEX` move `UObject::Outer` to +0x28? **Unresolved. Low confidence. One command settles it.**
Three verifiers (`ue58-reflection-surface` F10 and F12, `ue4ss-string-types` CPN-6) state it does: adding `int32 ObjectListInternalIndex` to `FNameAndObjectHashIndex` after an 8-byte `FName` gives 12 bytes → Outer padded from 0x20 to 0x28. One verifier (`ue4ss-hashtables` HT7) refutes it via `UObjectHash.cpp:39-40`'s `static_assert(sizeof(FName)==4)` / `static_assert(sizeof(UObjectBase)==40)` — with a 4-byte FName (`UE_FNAME_OUTLINE_NUMBER=1`) the index fits the existing padding and Outer stays at 0x20.

Both are internally consistent; they differ on whether those asserts are **unconditionally active whenever the index flag is on**, or gated on the *other* flag (`UE_UOBJECT_HASH_USES_ARRAYS`). **Resolving command:** read the `#if` guard enclosing `Engine/Source/Runtime/CoreUObject/Private/UObject/UObjectHash.cpp:30-40` in `vendor/UnrealEngine`. If the asserts sit inside the index flag's own block, HT7 is right and the combination does not compile; if they sit under the array-hash flag, F10/F12/CPN-6 are right and the combination is buildable.

**No action either way** (both flags default 0, nothing observed, and our Outer is voted at runtime over `+0x20` vs `+0x28` at `Genau.cpp:3195-3249`). But the **diagnostic** matters and should be recorded with its confidence marked: *if* buildable, an Outer at +0x28 is ambiguous between CPN (sizeof(FName)=12) and this flag (sizeof(FName)=8) — never 16 — which would make `DetectCasePreservingName` mis-latch and drive 16-byte FName reads across `Aura.cpp:3146`, `Ubel.h:415`, `Neu.h:57`. Its tell is the inverse of the outline-number one: Outer probes cleanly at +0x28 **yet** name reads at +0x18 are correct 8-byte FNames.

### D3 — Is `sizeof(FName)` under CPN 12 or 16? **12 — but measure it, don't hardcode.** (→ A9)
Our own tree (`dll/src/Ubel.cpp:1600-1606`, and `docs/audit-2026-08-13-early-code-findings.md:178/:4271`) asserts "the engine's correct 16". The UE header (`NameTypes.h:1257-1267`) and Dumper-7 (`Off::InSDK::Name::FNameSize = 0xC`) both say 12. **I believe the header + Dumper-7.** The counter-claim was traced by `git log -S` to commit `c65fdfc0`, whose own message shows it was propagated for internal consistency and explicitly left U2 unverified — it is not an observation. Since no CPN title has ever been measured, the fix must be a **probe** (read a real NameProperty's engine-reported ElementSize), not a corrected constant.

### D4 — Was the SDK-export finding's blast radius right? **No — verifier wins, by measurement.** (→ A7)
The finder described a broad MSVC tail-padding hazard; the verifier compiled probes and found the mechanism does not exist on MSVC, narrowing scope to empty-base structs and one lost offset-0 property. The **fix survives**; the justification must not, or it will be cited as precedent for a defect class that does not exist here.

### D5 — Process note, not a technical finding
`sync_tools.ps1` was written and committed (`7850bf53`, 234 lines) into a clean tree **during** this audit, under the maintainer's git identity. The tree is now clean and the commit is sound on the path exercised (`-SkipClones`, exit 0, correct behind-counts and zydis version decode) — but its clone loop and `-UpdateSubmodules` paths have **never run**. Exercise those before relying on them, and note that an audit run mutating the repo mid-audit is worth a look independent of what it produced.

---

# COMPLETENESS CRITIQUE — what this sweep did NOT cover

## 1. Upstream surface NOT swept at all

**(a) `vendor/RE-UE4SS/assets/` — 82 files changed in the audited range; the audit opened two lines of it.**
The report cites `MemberVariableLayout_5_08_Template.ini:838` and `..._4_27_CasePreserving_Template.ini:786` and treats the rest as read. It is not. In `662df915..24b12662`:
- **`assets/VTableLayoutTemplates/` — 14 files changed, `VTableLayout_5_08_Template.ini` newly added (+1450).** This is a per-version, PDB-derived (UVTD) vtable listing that contains `ProcessEvent` for every version 4.07→5.08 (`VTableLayout_5_08_Template.ini:158-159`). **A2 was measured by hand from 9 PDBs while an in-tree per-version oracle sat unread.** It reproduces A2 exactly on all four anchors and fills both of A2's UNVERIFIED bands — with a different answer than A2 guessed (see §3.1).
- **`assets/MemberVarLayoutTemplates/` — 31 files changed, +4126/−194,** including `MemberVariableLayout_5_08_Template.ini` newly added (+1058) and a wholesale re-annotation of FName sizes in every 4.x template (`4_27_Template.ini` `NamePrivate`: `Size: 0x0004 Padding: 0x4` → `Size: 0x0008`), plus a `[FUObjectHashTables]` section added to *every* version template.
- **`assets/CustomGameConfigs/` — 26 files, 11 titles touched, several new** (Halo Campaign Evolved, The Outer Worlds 2, Split Fiction, StarRupture, Project Silverfish, Far Far West, Conan Exiles Enhanced, …). These carry per-game engine quirks in exactly the form our `Himmel.h` presets / `docs/test-games.md` rows do — e.g. `The Outer Worlds 2/UE4SS-settings.ini:63-64` pins `MajorVersion=5 / MinorVersion=4`, `:42 bUseUObjectArrayCache=false`, `:60 DefaultFNameToStringMethod=Scan`. The report's ue4ss-misc pass folded all of this into "the ImGui/UVTD/**config churn**".

**(b) `vendor/RE-UE4SS/deps/first/Unreal` (UEPseudo) — the gitlink MOVED and was never read.**
`git diff 662df915..24b12662 -- deps/first/Unreal` = `b2e876da → 36e87abe`. The report itself notes `AdjustOffsetsForStatsBuild` "moved into the uncloned `deps/first/Unreal` by `053d2a61`" — i.e. it knows UE4SS's engine-layout code now lives there — and then classifies "all ~20 submodule bumps" as UE4SS-internal. Every engine-layout change UE4SS made in this range is invisible to a diff of `main`. This is the largest single hole.

**(c) `deps/first/patternsleuth` — gitlink `da8bfe4c → 1d90b02c`, uncloned, unread, both audits.** patternsleuth is an AOB/resolver database for UE globals (GUObjectArray / GNames / GEngine) — the same problem domain as `dll/src/Himmel.h`. `docs/toolchain.md:162` is a policy about *cloning* it, not evidence it holds nothing.

**(d) Dumper-7's `.idmap` rewrite.** `Dumper/Generator/Private/Generators/IDAMappingGenerator.cpp` **+1081 lines** and a new `Public/Generators/IDAMappingLayouts.h` (+121) — a full binary format (`FileMagic = 0xD7`, packed `Struct`/`Member`/`Enum`/`ExecFunc`/`NamedVTable` records) with new vtable-naming emission (`GenerateVTableName`, `WriteNamedVTableToStream`). The report's no-action entry characterises the whole delta as "image-relative `execXxx` thunk symbols". Also unmentioned: `MappingGenerator.cpp` (the `.usmap` writer) changed — I read it, it is a `Data.str()` → `Data.view()` perf refactor only, **no format change** (`:417-444`), so the usmap conclusion survives, but it was asserted, not checked.

**(e) An engine surface our DLL depends on that is absent from the report's own dependency list: Kismet bytecode opcodes.** `dll/src/Aura.cpp:5555` hardcodes `op != 0x46 && op != 0x1C` (EX_LocalFinalFunction / EX_FinalFunction), `:5922` `case 0x00 → "local"`, `:6152` 0x00/0x01/0x02, `dll/src/Aura.h:1148`, `ui/UE5DumpUI/Models/PropertyXref.cs:46`. The report's "what our DLL depends on" list names `Script.h` only via UFunction. I verified 5.8.2 (`Script.h:196+`: `EX_LocalVariable=0x00`, `EX_FinalFunction=0x1C`, `EX_LocalFinalFunction=0x46`) — correct today, and `EExprToken` uses explicit values with reserved gaps (`Script.h:199 "// = 0x03,"`), so insertions do not renumber. **Pre-5.x values remain unverified** (the partial clone only has `release`).

**(f) `ui/UE5DumpUI/Services/KnownStructLayouts.cs`** — a hardcoded UE-version-keyed engine struct table (FVector/FRotator LWC split at `:57-70`) that no vendor pass touched. Fails safe via the `engineSize` contradiction guard at `:44`, so **info**, but it is a UE-layout dependency living in `ui/`.

**(g) Build-config axes with no upstream check:** dedicated-server (`WITH_SERVER_CODE`) and `UE_BUILD_TEST` (A5 is header-derived, and `docs/reference-builds.md` has zero Test rows — the report says so). No action proposed for either.

**(h) Cheat Engine.** `docs/ce-*.md` are declared mirrors of an external master and CE is a hard runtime dependency of every emitted artifact; no upstream-CE currency check was in scope. Naming it so it is a deliberate exclusion rather than an oversight.

---

## 2. Conclusions resting on an unread source or an unverified assumption

1. **A2's 5.0/5.1/5.2 and 5.5 bands.** Explicitly filed UNVERIFIED and derived by header reasoning ("net −1, putting 5.5 at 0x268"). The oracle in (a) contradicts it. **This is not a caveat — the proposed patch would write a wrong constant for 5.5.**
2. **D3 (`sizeof(FName)` under CPN = 12) was decided on header + Dumper-7 while a PDB-derived measurement was in the tree.** `MemberVariableLayout_4_27_CasePreserving_Template.ini` annotates every FName as `Size: 0x000C` (`Padding: 0x4`) and puts `NamePrivate=0x18 / OuterPrivate=0x28`. Conclusion unchanged; **evidence class upgrades from "reading headers" to "measured"**, which matters because D3 overturns an in-tree assertion.
3. **A6's two constants were derived, and are in fact measured — in the same unread corpus.** Stock pre-4.25: `Offset_Internal=0x44 → FieldSize=0x70` in `4_18`/`4_22`/`4_24` templates = **+0x2C** exactly. CPN 4.27: `Offset_Internal=0x4C → FieldSize=0x80` = **+0x34** exactly. Both of A6's numbers, both confirmed, neither cited.
4. **"A filename sweep over all 1166 changed files finds no other surface-matching header."** The match rule is never stated, so the negative is unauditable. I re-ran it independently and it holds for our surface: the only `CoreUObject/Public/` files touched across both legs are `Misc/AssetReadLogger.h`, `Misc/AssetReadLoggerConfig.h`, `UObject/Package.h`, `UObject/UObjectGlobals.h` — all four already addressed. `Runtime/Core/` changes are Apple/Mac/Unix/Windows platform files plus `Math/Matrix.inl`. **The conclusion is right; the stated evidence did not establish it.**
5. **The revert check's arithmetic does not describe the legs.** Leg 1 = **823** files, leg 2 = **403**, cumulative **1166** (823+403 = 1226 ≠ 1166, so the sets are *not* disjoint at repo scale). CoreUObject-scoped they are 9 and 3, not "12 and 6". The check was run on some unnamed subset; the report presents it as global.
6. **A2's "the pattern scan stays primary, so the table is fallback-only"** rests on a replay over 8 reference binaries — none in a Test configuration and none from a licensee fork, the two cases where the fallback actually fires.
7. **"Advance the audited-commit marker to `b88241b`" has no landing site.** `docs/reversing-nonstandard-ue-games.md:124` is the only live file pinning vendor commits and it says `vendor/Dumper-7@c891b17` and `vendor/RE-UE4SS@2352d15b` — the second is neither the last-audited (`662df915`) nor current (`24b12662`), i.e. a third, older value. No `docs/vendor-audit-*.md` exists (`ls docs/ | grep -i vendor` → empty), so A10's doc bundle omits the one file that is now stale.

---

## 3. Top 5 follow-up checks, ranked

**1 — Amend A2 from the UE4SS vtable oracle BEFORE landing it.** Highest value: it changes a constant in a proposed patch.
```bash
R=vendor/RE-UE4SS/assets/VTableLayoutTemplates
# slot index = entries from [UObjectBase] to ProcessEvent, collapsing the repeated __vecDelDtor
```
Result I measured (dedup rule validated against four independent anchors — 4.27=0x220 repo ground truth, 5.4=0x268 DragonSword, 5.6/5.7=0x260 Lushfoil/Solarpunk, 5.8=0x250 A2's own PDB work):

| ver | 4.27 | 5.0 | 5.1 | 5.2 | 5.3 | 5.4 | **5.5** | 5.6 | 5.7 | 5.8 |
|---|---|---|---|---|---|---|---|---|---|---|
| ProcessEvent | 0x220 | **0x258** | **0x260** | 0x268 | 0x268 | 0x268 | **0x278** | 0x260 | 0x260 | 0x250 |

A2's patch (`>= 500 → 0x268`) is wrong for 5.0, 5.1 and 5.5. Mechanism for 5.5 confirmed by diffing the pre-ProcessEvent virtual lists: 5.5 inserts `GetVersePath` and `CollectSaveOverrides` (5.4 = 77 entries, 5.5 = 79, 5.6 = 76). Caveat to record: these are editor-PDB dumps; the editor-only virtuals (`RegenerateClass`, `MarkAsEditorOnlySubobject`) sit *after* ProcessEvent, which is why they agree with Shipping measurements.

**2 — Read the two uncloned RE-UE4SS submodules.** This is where the engine knowledge went.
```bash
git -C vendor/RE-UE4SS -c url."https://github.com/".insteadOf="git@github.com:" \
    submodule update --init deps/first/Unreal
git -C vendor/RE-UE4SS/deps/first/Unreal diff --stat b2e876da..36e87abe -- \
    include/Unreal/ src/
```
Then the same for `deps/first/patternsleuth` `da8bfe4c..1d90b02c`, scoped to its UE resolvers, against `dll/src/Himmel.h`. (Cloning is a working-tree change — confirm with the maintainer first; `docs/toolchain.md:162` currently says leave patternsleuth alone.)

**3 — Cross-check every 5.8 constant the audit derived from headers against `MemberVariableLayout_5_08_Template.ini`.** I did this for the spine and it corroborates the report exactly: `[UObjectBase]` total 0x28 / `NamePrivate` 0x18 / `OuterPrivate` 0x20; `[UStruct]` 0x40/0x48/0x50/0x58, `Script` 0x60; `[FField]` total 0x30 with `FlagsPrivate` 0x28; `[FProperty]` `Offset_Internal` 0x44, total 0x70; `[UEnum] Names=0x40, UnderlyingType=0x59, EnumFlags=0x5A`; `[UClass] ClassFlags=0xD8 / ClassCastFlags=0xE0 / ClassDefaultObject=0x118`; `[UFunction] FunctionFlags=0xB0` with **`FirstPropertyToInit=0xC0`** — A3's exact claim, now PDB-backed; `[FFieldClass] Name=0x8` at 5_08 vs **`Name=0x0` at 5_07**, which is A4's marker premise measured rather than reasoned. Remaining to check: the `FChunkedFixedUObjectArray` tuple behind our "UE5.8" preset (`Aura.cpp:304`) against `[FUObjectArray] ObjObjects=0x0, ObjFirstGCIndex=0x20`.

**4 — Read the 11 changed per-game configs against our preset/test-game tables.**
```bash
git -C vendor/RE-UE4SS diff 662df915..24b12662 -- assets/CustomGameConfigs/
```
Look for `MajorVersion/MinorVersion` overrides, `bUseUObjectArrayCache=false`, `DefaultFNameToStringMethod`, and any `UE4SS_Signatures/*.lua` — upstream's field observations on titles we may already track in `docs/test-games.md`.

**5 — Fix the vendor-pin provenance, which A10 misses.** `docs/reversing-nonstandard-ue-games.md:124` pins `Dumper-7@c891b17` + `RE-UE4SS@2352d15b`; both are stale and the second is a third distinct value. Either update it in A10's bundle or create the missing `docs/vendor-audit-*.md` so "advance the audited-commit marker" has somewhere to land. Confirm first with `grep -rn "c891b17\|2352d15b\|662df915" docs/ CLAUDE.md` (currently 1 live hit + 1 archived).

**Low-confidence, evidence missing:** whether `EExprToken` values in (e) held before UE5 — I could not read a 4.x `Script.h` (the clone is `blob:none` on `release` only). A `MemberVarLayoutTemplates`-style oracle does not cover opcodes; the cheapest source would be a 4.27 `Script.h` from another checkout.
