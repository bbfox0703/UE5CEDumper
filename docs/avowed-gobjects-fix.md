# Avowed — GObjects Decoy Misdetection Fix (tracking)

> **Status:** ✅ **DONE — GObjects + GWorld both LIVE-VERIFIED end-to-end (builds 1235-1240).** GObjects via centralized AOBs `GOBJ_AV1`/`GOBJ_AV2` (method `aob`, UE5-Extended layout, ~213K objects, clean); GWorld via `instance_scan_recovery` picking the active game world by `OwningGameInstance` (handles World-Partition `_Generated_` cells). Two GObjects AOBs for patch resilience. (Merged long ago — `GOBJ_AV1`/`AV2` are in `dll/src/Himmel.h`.)
>
> _Original root-cause summary:_ 🟢 FULLY GROUND-TRUTHED via Ghidra decompilation of `AllocateUObjectIndex`. Avowed's GObjects is a **STATIC `FUObjectArray` at RVA `0xB5BE398`** (no AOB matches — even patternsleuth fails). Decompilation revealed the decisive fact: **Obsidian packs `FUObjectItem` to 20 bytes (`0x14`), not the standard 24** — every prior read used stride 24 → misaligned → garbage names + bogus counts (`7,667,809` was the string bytes `0x00750061`="ua"). **Fix (build 1235):** `Genau::FindGObjectsStaticStruct` finds the static struct (code `lea/mov [rip]` refs → window → content validation across candidate strides) and reports the stride that decodes cleanly; `Aura::InitWithExtendedLayout(base, stride)` forces UE5 chunked-extended layout (NumElements @ +0x24, 65536/chunk) AND the 20-byte item stride. In-game verification pending.
> **Owner:** bbfox0703 (now owns the Steam copy — local iteration possible).
> **Game:** Avowed (Obsidian Entertainment), `Avowed-Win64-Shipping.exe`, **UE 5.3** (UE503).
> **First diagnosed build:** 1162 (`609ecfb-dirty`). **Phase 1 shipped:** committed `a467082` on `dev` (build 1225); verified on build 1228 (`0b3a4e5`).

This document tracks a long-running, test-gated fix. Because the maintainer does not own Avowed and depends on a remote tester across a wide timezone gap, each verify cycle is slow. Other development continues on the repo in parallel — keep this fix's changes small and isolated.

---

## TL;DR

It is **not** "GObjects AOB not found". GObjects is found, but **intermittently resolves to the wrong address** — a `.data` decoy structure that passes the relaxed validator yet yields **0 usable objects**. The *correct* GObjects (≈7.6M objects) is reachable: on the launches where the AOB decoy fails to validate, the existing `data_scan` fallback already finds it. The fix makes that recovery happen deterministically whenever the chosen GObjects yields `Count==0`.

**VERIFIED:** on the Steam build (1228), the AOB again picked a decoy (`0x7FF6E8546868`, Count=0) → recovery evaluated 8 candidates → switched to the real heap array (`0x1B066692800`, Count=7,667,809) → `Objects=7667809` and full DynOff offset detection (Guid struct, FField/UStruct offsets) succeeded. The game is now fully usable in the dumper.

---

## Evidence (from tester logs, 3 launches, build 1162)

Log folder: `Avowed-Win64-Shipping (1)/` (init-*, offsets-*, scan-*).

| Launch | GObjects | Method | Count | ItemSize | Result |
|--------|----------|--------|-------|----------|--------|
| init-1 (15:44) | `0x2077FF02800` (**heap**) | **data_scan** fallback | **7,667,809** | 24 | ✅ works |
| init-2 (15:38) | `0x7FF6844D6868` (**.data**) | aob `GOBJ_V3` | **0** | 16 | ❌ broken |
| init-0 (16:15) | `0x7FF6844D6868` (**.data**) | aob `GOBJ_V3` | **0** | 16 | ❌ broken |

- GNames (`0x7FF684282F40`) and GWorld (`0x7FF684064B50`) resolve consistently and correctly in all three runs. Only GObjects flaps.
- Module-base addresses (`0x7FF6...`) are stable across launches; the **real** GObjects lives on the **heap** (`0x207...`) and moves per launch (ASLR), so it is only reachable via `data_scan` (which follows code RIP-relative refs), not a fixed AOB displacement.

### Key log lines

Broken launch — `scan-0.log`:
```
ValidateGObjects: Valid at 0x7FF6844D6868 (relaxed Flat, Num=40321, Objects=0x197F76E7630 [flat])
GOBJ_V3: 938 matches, validated -> 0x7FF6844D6868
```
`offsets-0.log`:
```
GObjects@0x7FF6844D6868: +08:00009D81_07050005 (hi dword=0x9D81=40321) +10:0 +14:0
ObjectArray: Flat (non-chunked) array layout detected
ObjectArray: Initialized at 0x7FF6844D6868, Count=0, ItemSize=16
```

Working launch — `scan-1.log`:
```
=== GObjects: 51 patterns tried, 10 with hits, NONE validated ===
FindGObjects: All patterns failed, trying data-section scan fallback...
FindGObjectsByDataScan: Found 1149519 static pointers ...
ValidateGObjects: Valid at 0x2077FF02800 (relaxed Flat, Num=32758, Objects=0x20703978348 [flat])
FindAll: Complete — GObjects=0x2077FF02800 (data_scan) ...   → Count=7,667,809
```

---

## Root cause

1. `Sig::GOBJECTS_PATTERNS` entry **`GOBJ_V3` is very loose** (938 matches on this build). `ScanForTarget` accepts the first match that passes `ValidateGObjects`.
2. `ValidateGObjects` **Tier-2 relaxed-Flat** path (`Genau.cpp`) accepts the `.data` decoy: `NumElements` read at `+0x0C` is a coincidental `40321` (in the plausible `0x1000..0x400000` range), and the **single** `ValidateCyclicClassChain` check passes because the decoy's *first* item happens to look like a valid UObject. It does **not** verify that *many* consecutive items are valid, so a sparse (~23% valid) decoy slips through.
3. The decoy's true element count (the offset `Aura::Init` later reads) is **0** → `Aura` initializes with `Count=0` → the dumper sees an empty object array → every downstream feature is dead.
4. Because AOB "succeeded", `FindGObjects` never falls through to the `data_scan` fallback that *does* find the real heap array. Whether the decoy validates is **heap-state / ASLR dependent**, hence the intermittency.

RE-UE4SS notes, for the sibling Obsidian/UE5 title **The Outer Worlds 2**, *"Changes were made to UObject"*.
⚠ That is **not** a layout note `[VND583-DOC CGC-6]`. The TOW2 config ships no `MemberVariableLayout.ini`
at all, and the change is in its `VTableLayout.ini`: `[UObject]` gains an extra virtual (`Unk_0`)
between `PreSave_1` and `ResolveSubobject`, which is a vtable override. It says nothing about
Obsidian's object or array layout, so it does not corroborate the failure above.

---

## Fix plan

### Phase 1 — DLL decoy recovery (zero-regression) — ✅ LIVE-VERIFIED (Steam build 1228)

Strictly additive: a recovery step in `UE5_Init` that runs **only when the chosen GObjects yields `Aura::GetCount() == 0`**. Working games (Count > 0) never enter it, so there is **no regression risk** to the 20+ already-supported titles.

Mechanism:
- New `Genau::CollectGObjectsCandidates(out, avoid, maxCandidates)` — reuses the proven data-section RIP-relative scan (`DataScanGObjectsCandidates`, extracted from `FindGObjectsByDataScan`) to gather up to N **unique** addresses that pass `ValidateGObjects`, skipping the known-bad `avoid` address.
- `UE5_Init` (Frieren.cpp): on `Count==0`, iterate candidates; for each, `Aura::Init(cand)` and accept the first that yields `Count>0` **and** at least one resolvable (non-"None") FName. The name gate is the real discriminator — it rejects count-only decoys that `ValidateGObjects` cannot. On success, update `ptrs.GObjects` / `g_cachedGObjects` / method = `data_scan_recovery`, then run `ValidateAndFixOffsets` on the good array. On total failure, restore the original GObjects (state stays consistent).

Files: `dll/src/Genau.h`, `dll/src/Genau.cpp`, `dll/src/Frieren.cpp`. (None overlap the parallel Aura/Fern work in flight at diagnosis time.)

Cost: adds ~0.7s to init **only on the broken-launch path** (one data-section sweep). Working launches are unaffected.

Expected effect: converts the broken launches (init-0/2) into working ones — recovery runs the same data-scan that already succeeded in init-1 → lands on `0x207...` → 7.6M objects, names resolve.

### Phase 2 — optional perf/cleanup (NOT required for functionality) — ⬜ backlog

Phase 1 makes the game fully usable; Phase 2 only improves init time and robustness. Now iterable locally (maintainer owns the Steam copy).

- **Reduce init cost.** The recovery adds ~10s on Avowed (a full data-section sweep + 8-candidate eval) because the loose AOB winner pre-empts the fallback. Tighten `ValidateGObjects` at the source so the decoy is rejected up front: in the relaxed-Flat path, validate the **first K consecutive items** at a probed stride and require a high valid ratio (decoy ≈23% vs real ≈100%). ⚠ Touches the shared validator used by all games → regression-test across the test-game matrix before merge. Alternatively/additionally, persist a per-exe *method preference* ("prefer data_scan") so subsequent launches skip the doomed AOB pass (the heap address itself moves per launch — do **not** persist the address).
- **NumElements/ItemSize note (low priority).** `Aura` reads `Count=7,667,809 / ItemSize=24` consistently and DynOff detection succeeds, so the array is interpreted correctly. The stale `ValidateGObjects` relaxed-Flat count (`Num=32758`) is read at a different offset but is unused once recovery hands Aura the right base — cosmetic only.
- **Let the UI "Extra Scan" rescue this case too**: `apply_rescan` is gated on `g_cachedGObjects == 0`, but a decoy is non-zero, so the button cannot overwrite it. Relax the gate to also allow overwrite when `Aura::GetCount() == 0`. (Less urgent now that auto-recovery handles it at init.)

---

## Test protocol (for the remote tester)

1. Use the build that contains Phase 1 (build # noted at the top once tagged).
2. Inject into Avowed, let it auto-start, reach the point where the UI connects.
3. **Capture the whole DLL log folder** (`%LOCALAPPDATA%\UE5CEDumper\Logs\<Avowed pid subfolder>\`) — specifically `init-*.log`, `offsets-*.log`, `scan-*.log`. Zip and send back.
4. **Launch the game ≥3 times** (separate runs) so we sample both the decoy-hit and decoy-miss cases.
5. Success criteria, per launch:
   - `init-*.log` → `UE5_Init: Complete (... Objects=<large number, not 0>)`.
   - If the line `UE5_Init: Recovery SUCCESS — GObjects 0x... -> 0x... (Count=...)` appears, Phase 1 fired and worked.
   - UI shows a populated object tree / non-zero object count; Instance Finder / Property Search return results.
6. If any launch still shows `Objects=0` with **no** recovery-success line, capture that launch's `scan-*.log` + `offsets-*.log` — we need to see why no candidate passed the name gate.

---

## Progress log

- **2026-06-16** — Diagnosed from a 3-launch log set (build 1162). Confirmed intermittent decoy (`0x7FF6844D6868`) vs real heap GObjects (`0x207...`, 7.6M objects). Root cause = loose `GOBJ_V3` + permissive relaxed-Flat `ValidateGObjects` blocking the `data_scan` fallback. Implemented Phase 1 (zero-regression `Count==0` recovery): `Genau::CollectGObjectsCandidates` + `DataScanGObjectsCandidates` refactor (`Genau.h`/`Genau.cpp`) + recovery loop in `UE5_Init` (`Frieren.cpp`). DLL build 1225 compiles clean. Committed `a467082` + pushed to `origin/dev`.
- **2026-06-16 (same day)** — Maintainer bought the Steam copy. **Phase 1 partially verified on build 1228 (`0b3a4e5`):** one launch recovered correctly (`-> 0x1B066692800, Count=7667809`, DynOff fully detected). BUT a later launch recovered to `0x232C43C1550` with only **Count≈1.4K–7K** (UI showed 7296 total, ~13% named) — broken. Scan log for that launch shows the data scan surfaced 8 candidates and the **true master array WAS present** (candidate #2 `0x232CC4E2800`, via the same `instr@0x7FF6DD08A7B5` that yielded the 7.6M array the prior launch), but the recovery's **first-match gate accepted candidate #1** `0x232C43C1550` (a small partial object-list whose code ref `0x7FF6DCFC3246` sorts earlier). Root cause = selection, not reachability.
- **2026-06-16/17 — Selection fix (build 1230, `Frieren.cpp`):** recovery now evaluates ALL candidates, samples up to 256 spread non-null objects per candidate for a name-resolution ratio, and among those passing (≥8 sampled, ≥25% resolved) picks the one with the **largest object count** — the master array (~millions) dominates partial lists (~thousands). Per-candidate `Count=…, names …/… resolved` now logged. DLL+UI rebuilt to matched build 1230, C# tests green. Re-verification in-game: ⏳ pending (test across several launches to confirm it consistently lands on the ~7.6M array).
- **2026-06-17 — Selection STILL wrong on build 1231 (logged), root-caused + refixed (build 1232):** two launches landed on Count=672 and Count=32758 (garbage/partial), NOT the 7.6M master. Recovery log was decisive — the master array WAS candidate #1 (`0x2A0EDC22C80`, `Count=7667809`, via the known-good `instr@0x7FF6DD08A7B5`) but resolved only **3/256 names in the SPREAD sample** → failed the `≥25% ratio` gate → rejected; a small dense partial-list (`0x2A409560000`, Count 672, 66/256) passed the ratio and won. Root cause: the true master is **huge and SPARSE** (live named objects are a tiny fraction of capacity), so a spread sample under-resolves it. **Fix (build 1232, `Frieren.cpp`):** scan the **FIRST slots** (dense permanent core: CoreUObject classes/packages) instead of spreading, early-out at 8 names, and qualify on `resolved ≥ 2` (real object array) rather than a ratio — then pick **max count**. The master (7.6M) now dominates. Also clear `g_cachedGObjectsPatternId` on recovery so the **UI's** cache re-save (it mirrors these globals over the pipe) stops re-introducing the decoy `GOBJ_V3` patternId. DLL+UI matched build 1232, tests green. ⏳ re-test in-game (redeploy DLL to the game's dxgi-proxy location — Avowed loads it from the game folder, not `dist\`).
- **2026-06-17 — 2nd GObjects AOB + docs + ship (build 1244):** owner added a second, patch-resilient GObjects pattern **`GOBJ_AV2`** = `48 8B 15 ?? ?? ?? ?? C1 E8 10 48 8D 0C 89 C1 E1 02 48 03 0C C2` (the GENERIC FUObjectItem chunk-index codegen — `idx>>16`=chunk, `(idx&0xffff)*0x14` via `lea*5;shl<<2` which bakes in the 20-byte stride; ~10+ identical sites, so a game patch that changes `AllocateUObjectIndex` (where `GOBJ_AV1` lives) won't break resolution). Both resolve to GObjects+0x10 (adjustment -0x10), priority 10; `ValidateGObjects` picks the real base among the non-unique matches. LIVE log confirms the full chain: `winner: GOBJ_AV1 -> 0x7FF66E92E398`, `ValidateGObjects: Valid (preset UE5-Extended, Num=213354, Max=2162688)`, `GWorld at 0x... -> UWorld (index=104275, 2 candidate(s), active (OwningGameInstance set))`. Updated `docs/test-games.md` (Avowed entry + naming + GWorld 25/27) and README (5.3-5.4 matrix + GWorld 32/32). Committing builds 1230-1244 to `dev` and merging to `main`.
- **2026-06-17 — GWorld decoy recovery + active-world selection (builds 1239-1240, LIVE-VERIFIED 1239):** Avowed's GWorld AOB (`GWLD_G42_4`) lands on a decoy (`*GWorld` = garbage outside the heap). **1239:** rewrote `Genau::ExtraScanGWorld` — instead of "pick the first UWorld then look for a pointer to it" (Avowed has many UWorlds; the first is a UI "stage" with no global pointing to it), it now **scans `.data` for any slot that POINTS to a non-CDO UWorld** (that slot IS a `UWorld*` global; the decoy points to non-world garbage and is skipped). LIVE-VERIFIED: `Start from GWorld` → `ADR_07_PRO` with PersistentLevel etc. **1240:** owner flagged World-Partition `_Generated_` transient sub-worlds (higher GObjects index, `OwningGameInstance==0`) — "highest index" alone would pick those on a late connect. So among `.data`-referenced worlds, by index DESCENDING, pick the first whose **`OwningGameInstance` is non-null** (the active game world); property offset located by NAME via `FindPropertyOffsetByName` (walks the FField chain with runtime DynOff offsets — names are stable, offsets aren't), fallback highest-index. Also cleared `g_cachedGWorldPatternId` on recovery so System tab shows `instance_scan_recovery` not the stale `GWLD_G42_4`. DLL+UI matched build 1240, tests green. ⏳ verify 1240 picks the active world on a late connect.
- **2026-06-17 — AOB moved to the centralized signature list (build 1238, architecture fix):** owner's point — a per-game AOB shouldn't be hardcoded inline; it belongs in `Himmel.h` with the ~128 others, found via the normal scan so the method is a proper `aob` hit (not a recovery sub-method). Added `GOBJ_AV1` = `48 8B 15 ?? ?? ?? ?? 4D 63 C0 49 C1 E0 04` (`AObTarget::GObjects`, instrOffset 0, opcodeLen 3, totalLen 7, **adjustment -0x10**, priority 10) to `Sig::GOBJECTS_PATTERNS`. Now `FindGObjects`→`ScanForTarget` matches it (priority 10, before the decoy-matching `GOBJ_V3`), `ValidateGObjects` validates (UE5-Extended preset; `ValidateCyclicClassChain` already tries stride 20), method = `aob`, winning pattern `GOBJ_AV1`. Aura's normal `DetectLayout` (UE5-Extended `ValidateChunkedLayout` passes — Avowed's chunk allocator is standard: elemPerChunk 65536, only `FUObjectItem` is packed) + `DetectItemSize` (content-detects stride 20) handle the rest. The inline AOB + `outViaAob` plumbing were removed; `FindGObjectsStaticStruct` (BSS scan + forced stride) stays as the pure no-AOB fallback (Avowed no longer triggers it — `Count>0` via AOB). GWorld recovery (1237) unchanged. DLL+UI matched build 1238, tests green.
- **2026-06-17 — AOB fast path CONFIRMED + GWorld recovery + clearer label (build 1237):** build 1236 log proved the AOB fast path engages: `FindGObjectsStaticStruct: GObjects via AOB at 0x7FF66E92E398 (stride=20, instr@...)` — instant resolution, no BSS scan (Count=196058 in-game). The System-tab "AOB failed" refers to the PRIMARY `GOBJ_*` scan (still a decoy); the fast-path AOB is internal to recovery — so the method label now distinguishes `aob_static_recovery` vs `static_struct_recovery` (`outViaAob`). **GWorld:** in-game (not just main menu) `Start from GWorld` still empty — `*GWorld` (`0x7FFC7D7DD290`) is outside the object heap/module → not a UWorld → GWorld's AOB (`GWLD_G42_4`) landed on a decoy too. Added a GWorld recovery in `UE5_Init`: validate `*GWorld`'s class name == "World"; if not, `Genau::ExtraScanGWorld` (iterate the now-working GObjects for a live UWorld instance, then find the static `.data` pointer to it) → `g_cachedGWorld`, method `instance_scan_recovery`. Gated on a working object array (zero regression). DLL+UI matched build 1237, tests green. ⏳ verify GWorld in-game.
- **2026-06-17 — LIVE-VERIFIED + AOB fast path (build 1236):** build 1235 verified in-game — `GObjects -> 0x7FF66E92E398 (Count=95549, stride=20)`, clean object tree, DynOff calibrated. Owner found a unique AOB anchored on the chunk-table load inside `AllocateUObjectIndex`: `48 8B 15 ?? ?? ?? ?? 4D 63 C0 49 C1 E0 04` (the `MOV RDX,[0x14b5be3a8]` where `0x14b5be3a8` = GObjects+0x10). Added as a fast path in `FindGObjectsStaticStruct`: AOB match → `base = ripTarget - 0x10` → `ScoreGObjectsStaticBase` content-validates + detects stride → return; on no-match/fail, fall through to the BSS scan. So resolution is instant on this build, robust on others. `Start from GWorld` issue noted as separate (GWorld `*ptr` outside the heap — main-menu or GWorld mis-resolution; NOT stride/DynOff). DLL+UI matched build 1236, tests green.
- **2026-06-17 — DEFINITIVE GROUND TRUTH via Ghidra (build 1235):** decompiled Avowed.exe with the headless analyzer (project `D:\Tools\GHIDRA_Projs\Avowed.rep`). Found `FUObjectArray::AllocateUObjectIndex` = `FUN_14814d2f0` (it references the `"Unable to add more objects to disregard for GC pool"` + `"Attempting to add %s at index %d"` strings and does `NumElements++`). Its single caller does `LEA RCX,[0x14b5be398]` → **GUObjectArray = `0x14b5be398` (RVA `0xB5BE398`)**. Decompilation (with `param_1`=`int*` GObjects base) nailed the whole layout: `ObjObjects.Objects`=base+0x10 (`*(longlong*)(param_1+4)`), `NumElements`=base+0x24 (`param_1[9]++`), `MaxElements`=base+0x20 (`param_1[8]`), `NumChunks`=base+0x2C (`param_1[0xb]`), 65536 elems/chunk (`memset(...,0x140000)`, `index>>16`/`index&0xffff`), and the killer: **`FUObjectItem` stride = `0x14` = 20 bytes** (`(index & 0xffff) * 0x14`; item = {Object@+0x00, Flags@+0x08, ClusterRoot@+0x0C, Serial@+0x10} = 20, no padding). UObject layout standard (`InternalIndex`@+0x0C, `NamePrivate`@+0x18). **This 20-byte item is why every prior read produced garbage** (stride 24 misaligns every object). Notes: patternsleuth's GUObjectArray code-patterns don't match Avowed; the user's Ghidra auto-analysis hadn't created the string xrefs, so resolved via the 3 patternsleuth candidate funcs + caller `LEA`. **Fix:** `ScoreGObjectsStaticBase` now probes strides {0x14,0x18,0x10,0x20} and returns the one that decodes to clean names; the stride is forced into Aura via `InitWithExtendedLayout(base, stride)`. DLL+UI matched build 1235, tests green. ⏳ in-game verify.
- **2026-06-17 — GROUND TRUTH via RE-UE4SS + patternsleuth (builds 1233-1234):** heuristics exhausted; pulled real data instead of guessing.
  - **UEPseudo (RE-UE4SS) UE5.3 layout:** `FUObjectArray.ObjObjects` @ +0x10, `NumElements` @ +0x24, `FUObjectItem` stride 0x18. Our `UE5-Extended` preset already matches it.
  - **patternsleuth CLI on Avowed.exe:** `GUObjectArray` resolver → `ResolveError: expected at least one value` — **NO code pattern matches** (its 7 GUObjectArray patterns, which we already carry as `PS1`-`PS7`, all miss). But the string anchor `"Unable to add more objects to disregard for GC pool"` resolves 3 candidate functions (`AllocateUObjectIndex`).
  - **Disassembled `AllocateUObjectIndex` (0x147A604E0) via capstone:** it references a `.data` cluster ~`0x14B5BE008` (`lea rcx,[rip]; call`), and that region is **zero-init BSS** (`.data` VirtualSize 0x404AE9 ≫ RawSize 0x49200) — i.e. a static, runtime-filled global = **GUObjectArray**. So GObjects = moduleBase + RVA ≈ `0xB5BE008`. Our heap candidates (`0x27..`) were never it; `7667809` was `0x00750061`("ua") read as int.
  - **Why we missed it:** (1) no AOB matches Avowed; (2) the data scan dereferences `.data` slots (right for heap-allocated GObjects, wrong for a static struct — the slot IS the struct); (3) relaxed validation accepted a decoy.
  - **Fix (build 1234):** `Genau::FindGObjectsStaticStruct` — scan `.text` for `lea/mov reg,[rip]` slots in writable `.data`, probe a window (`-0x58..+0x10`) around each as a UE5 chunked `FUObjectArray`, and validate by CONTENT (`ScoreGObjectsStaticBase`: ObjObjects@+0x10 → chunk0 → first 64 items at stride 0x18 → require ≥6 clean printable-ASCII names, majority clean — directly implements the "first objects must be clean, not garbage/CJK" idea). `Aura::InitWithExtendedLayout` forces `{Objects@+0x10, Max@+0x20, Num@+0x24}` so auto-detect can't mis-pick a relaxed preset reading the count at the wrong offset. Wired into `UE5_Init` recovery as the PRIMARY path (heap-candidate selection kept as fallback for genuinely heap-allocated GObjects). DLL+UI matched build 1234, C# tests green. ⏳ in-game verify.
- **2026-06-17 — Hint-cache correctness (build 1231, `Flamme.*` + `Frieren.cpp`):** `SaveResults` runs inside `FindAll` (before recovery), so it records the decoy's `method:"aob"` + winning `patternId:"GOBJ_V3"`. Since `LoadHints`/`ExtractHint` only returns a pattern hint when `method=="aob"`, that would make the **next launch prioritise the decoy pattern**. Added `Flamme::UpdateGObjectsMethod(peHash, "data_scan_recovery")`, called on recovery success: rewrites `gObjects.method` → `data_scan_recovery` and **clears `patternId`**, so the hint is inert next launch (full clean scan; cached `ueVersion` still reused). The misleading decoy patternId no longer survives in the cache. (NOTE: the UI System-tab "Test Extra Scan"/"Self-Test" buttons do NOT write the gObjects method — they don't touch the hint cache.) DLL+UI matched build 1231, tests green. Optional follow-up (deferred): a `preferDataScan` flag to also SKIP the doomed AOB scan next launch (~6.8s) — needs care so recovery still fires.

---

## Open questions / risks

- ~~Does `CollectGObjectsCandidates` surface the real array within the cap (16)?~~ **Resolved:** on build 1228 it evaluated 8 candidates and recovered successfully.
- ~~Is `7,667,809` the true object count?~~ **Resolved (good enough):** `Aura` reads it consistently and DynOff detection succeeds on it; any residual count-offset nit is cosmetic (Phase 2 note).
- Recovery re-runs every decoy launch (~10s, no persistence yet) — acceptable; addressable via Phase 2 method-preference persistence if init time becomes annoying.
