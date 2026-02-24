# Development Log

> Implementation status, known challenges, fixes, and next steps. Moved from CLAUDE.md.

> Last updated: 2026-02-23 (ArrayProperty Phase B scalar element values)
> Latest commit: `0ff3d4a` on `dev` branch (merged to main via PR #54)

-----

## Component Status

| Component | File(s) | Status | Notes |
|-----------|---------|--------|-------|
| Memory module | `Memory.cpp/.h` | Done | AOBScan (exec sections only), ResolveRIP, ReadSafe (SEH) |
| Logger | `Logger.cpp/.h` | Done | Category-routed multi-file: 5 files (init, scan, offsets, pipe, walk) via prefix-match routing, early buffering, SUMMARY level, 2-file rotation, 5MB cap, thread-safe; per-process mirror logging (subfolder per process name, max 20 subfolders) |
| AOB Signatures | `Signatures.h` | Done | 51 patterns + 4 symbol exports from 6 sources (original, patternsleuth, RE-UE4SS, Dumper-7, UE4 Dumper.CT, UEDumper) |
| OffsetFinder — GObjects | `OffsetFinder.cpp` | Working | 27 AOB patterns + deref variants + MSVC symbol export (`GUObjectArray`); validated in TQ2 (UE5.7) |
| OffsetFinder — GNames | `OffsetFinder.cpp` | Partial | 17 AOB patterns + FName ctor export + `.data` pointer-scan + UE4 TNameEntryArray validator; unified `ValidateGNamesAny` tries FNamePool→Structural→UE4; structural validator hardened with exact header format + corroboration |
| OffsetFinder — GWorld | `OffsetFinder.cpp` | Done | 7 patterns, non-critical |
| OffsetFinder — Version | `OffsetFinder.cpp` | Working | PE VERSIONINFO (ProductVersion + FileVersion) + 3-tier memory string scan (++UE prefix → Release context → bare pattern with digit guard) |
| OffsetFinder — DynOff | `OffsetFinder.cpp`, `Constants.h` | Untested | Dynamic FField/FProperty/UStruct offset detection via known struct probing |
| ObjectArray | `ObjectArray.cpp/.h` | Done | FChunkedFixedUObjectArray + FFixedUObjectArray (flat) auto-detection, ForEach, GetByIndex; auto-detect FUObjectItem stride {16,24,20} via FNamePool validation with scoring + fallback; flat array detected via chunk[1] pointer validation |
| FNamePool | `FNamePool.cpp/.h` | Done | Triple-mode: UE5 FNamePool (multi-format header A/B, chunk index) + UE4 TNameEntryArray (double-deref, 0x4000 chunk size) + hash-prefixed FNamePool (4B hash + 2B header, auto-detected) |
| UStructWalker | `UStructWalker.cpp/.h` | Done | FField chain, SuperStruct inheritance, GetFullName; uses DynOff for all offsets |
| ExportAPI | `ExportAPI.cpp/.h` | Done | C ABI exports, UE5_AutoStart, WalkClass Begin/Get/End; calls ValidateAndFixOffsets |
| PipeServer | `PipeServer.cpp/.h` | Done | Named pipe, JSON dispatch, watch/unwatch, get_offsets, walk_world, find_instances, get_ce_pointer_info; get_object_list returns `scanned` count for proper pagination |
| CE XML Export | `CeXmlExportService.cs` | Done | Hierarchical XML: correct CE tree addressing model (each child relative to parent's resolved addr; pointer=Offsets=[0], inline=no Offsets). Signed int, BoolProperty bit fields, pointer ShowAsHex, StructProperty auto-expansion, ArrayProperty Phase C (group deref TArray.Data, elements at simple offsets) |
| CE Cheat Table | `scripts/UE5CEDumper.CT` | Done | injectDLL-only flow (no executeCodeEx), 15s countdown; DLL path auto-detect via OpenDialog1 |
| CEPlugin interface | `CEPlugin.cpp` | Done | CE plugin Type 5 main menu; g_isCEPlugin flag |
| DLL auto-start | `dllmain.cpp` | Done | AutoStartThreadProc, 1s delay, g_isCEPlugin check; per-process mirror log init |
| Avalonia UI | `ui/` | Skeleton done | Object tree + Instance Finder + Class struct panels; pipe integration working; dynamic window title (process name on connect); GWorld error display |

-----

## What Is Working (Confirmed in Testing)

- **DLL injection** via `injectDLL()` in CE Lua — confirmed in TQ2
- **Auto-start thread** — spawned on `DLL_PROCESS_ATTACH`, waits 1s, detects CE plugin vs game process correctly
- **GObjects scan** — found at `0x7FF71B7A1820` in TQ2 (UE5.7), 483,670 objects, NumElements validation passed
- **UE version detection** — PE VERSIONINFO returns UE5.7 for TQ2 correctly
- **Build system** — CMake + Ninja + MSVC via `vswhere`, reproducible Release build
- **CE plugin mode** — `g_isCEPlugin = true` suppresses auto-start when loaded into CE.exe itself
- **Logging** — Dual-channel: `UE5Dumper-scan-0.log` + `UE5Dumper-pipe-0.log`; category tags; SUMMARY level; auto-switches Scan→Pipe after `UE5_Init()`
- **Pipe Server** — Named pipe `\\.\pipe\UE5DumpBfx` accepts connections, JSON dispatch working
- **Live Data Walker** — `walk_class`, `walk_instance`, `walk_world` commands functional (type names correct on TQ2)
- **Instance Finder** — `find_instances` command returns matching objects by class name
- **CE XML Export** — `get_ce_pointer_info` returns Cheat Engine-compatible XML pointer records
- **FF7Re Object Tree** — working after FNAME_STRIDE=4 fix + stride detection v1 (24 bytes detected); names correct, Instance Finder functional
- **TQ2 DetectItemSize v2** — stride 16 correctly detected; FNamePool validation prevents false positives
- **Tower of Mask (UE4.27)** — Full pipeline working: stride 24, DynOff offsets correct, 62661 objects, Live Walker walks classes. GWorld null but UWorld found via GObjects fallback
- **DQ I&II HD-2D Remake (UE5.05)** — Full pipeline working: stride 24, 128678 objects, standard FNamePool, Object Tree + Instance Finder. GWorld null. Guid/Vector struct-based offset detection works.
- **OctoPath Traveler (UE4)** — GObjects found (Layout B, 35087 objects), GNames via UE4 TNameEntryArray, Object Tree populates.
- **EverSpace 2 (UE5.5)** — Full pipeline: stride 24 (tiebreaker), GNames via pointer-scan fallback, 1,158,676 objects, DynOff validated.
- **Hogwarts Legacy (UE4.27)** — Full pipeline: stride 24, 379,793 objects, FProperty mode.
- **FF7R flat array confirmed (build 1.0.0.27)** — 165,792 objects, Flat FFixedUObjectArray, UProperty fallback working.
- **TQ2/EverSpace 2/Hogwarts Legacy/IDOLM@STER/Romancing SaGa 2/Tower of Mask/Ghostwire: Tokyo** — Start from GWorld working (build 1.0.0.27). CDO skip fix effective.
- **EnumProperty + StrProperty support (build 1.0.0.40)** — Full pipeline: lazy UEnum::Names detection, per-UEnum cache, FString UTF-16→UTF-8. Int8Property added. ByteProperty-with-enum handled.
- **Lushfoil Photography Sim (UE5.6, build 1.0.0.40)** — All working. 58630 objects.
- **Manor Lords (UE5.5, build 1.0.0.40)** — All working.
- **Satisfactory (UE5.3, build 1.0.0.40)** — Working. GWorld fails. 35776 objects.
- **ArrayProperty Phase A inner type (build 1.0.0.61)** — ✅ VERIFIED. DLL reads `FArrayProperty::Inner` to show `[N x TypeName (SizeB)]`. Confirmed on TQ2.
- **ArrayProperty Phase B scalar element values (build 1.0.0.61)** — ✅ VERIFIED on EverSpace 2: `EditorStarterShipTypes [9 x ByteProperty (1B)] = [EShip::Scout, ...]`, `IncursionEventLocations [35 x NameProperty (8B)] = [S01L04, ...]`. 52 tests pass.
- **Logger category restructuring (build 1.0.0.61)** — DLL: 5 category files. UI: 3 Serilog loggers. 42 UI tests pass.
- **Address format toggle (build 1.0.0.58, PR #53)** — 3 modes (hex no-prefix, hex 0x-prefix, module+offset).
- **ArrayProperty Phase C CE XML export (build 1.0.0.61)** — Extended CeXmlExportService. 58 tests pass. **NOT YET VERIFIED** — needs EverSpace 2 test.
- **CE XML pointer chain fix (build 1.0.0.61)** — Fundamental fix to CE XML address resolution model. 58 tests pass. **NOT YET VERIFIED** — needs EverSpace 2 re-test.

-----

## Known Challenges & Failures

### 1. GNames AOB Patterns Fail on TQ2 (UE5.7)

All 4 LEA-based patterns resolve to wrong globals (encrypted data, C strings, SRWLOCK, wide strings). **Root cause**: TQ2 (UE5.7) uses non-standard GNames referencing or patterns appear in unexpected order.

**Fix**: `FindGNamesByPointerScan` — scan game module `.data` section for 8-byte-aligned pointers that dereference to a `"None"` FNameEntry. The pointer is `FNamePool.Blocks[0]`; try offsets `{0x10, 0x00, 0x08, 0x18, 0x20}` to find pool base. **Status**: Implemented, confirmed working on EverSpace 2 and Hogwarts Legacy.

---

### 2. CE executeCodeEx Returns nil for All DLL Calls

**Root cause**: When DLL is loaded as CE plugin, `getAddress("UE5_Init")` returns address in CE's process space. `executeCodeEx` tries `CreateRemoteThread` in the game process with that address → crash → nil.

**Fix**: Removed all `executeCodeEx` calls. DLL now auto-starts via `AutoStartThreadProc` on `DLL_PROCESS_ATTACH`. CE Lua only calls `injectDLL()` and waits.

---

### 3. CT DLL_PATH Resolution

**Root cause**: `File → Open` in CE updates `OpenDialog1.FileName`, not `SaveDialog1.FileName`.

**Fix**: Try `OpenDialog1.FileName` first, fall back to `SaveDialog1.FileName`, then `getCheatEngineDir()`.

---

### 4. ValidateGNames False Failure (FRWLock Misread)

**Root cause**: Only tried offset `+0x00` for Blocks[0], but standard layout has `FRWLock (8B)` at +0x00 and Blocks[0] at +0x10.

**Fix**: Try offsets `{0x10, 0x00, 0x08, 0x20, 0x40}` in order; added hex dump of first 128 bytes on failure.

---

### 5. FField/FProperty Offset Mismatch on TQ2 (UE5.7) — CasePreservingName

**Symptom**: All field names showed as "rty" (last 3 chars of "Property"), all offsets as 0x0.

**Root cause**: TQ2 uses CasePreservingName — FName is 0x10 bytes instead of 0x8. This shifts FField::Flags from 0x30 to 0x38 and all FProperty fields by +0x8.

**Fix**: Added `DynOff` namespace in `Constants.h` with mutable `inline int` offsets. Implemented `ValidateAndFixOffsets()` in `OffsetFinder.cpp` — probes known structs (Guid, Vector) to dynamically discover correct offsets. Updated `UStructWalker.cpp` to use `DynOff::*`.

---

### 6. FF7 Rebirth (FF7Re): Garbled Names + Wrong UE Version + GWorld Empty

**Root causes**: (1) Version detection false positive ("5.6." in game data → reported UE 5.06), (2) GNames structural validator accepted garbage via `strstr`, (3) GWorld AOB pattern false match.

**Fixes**: (1) 3-tier version detection (exact prefix → Release context → bare pattern with digit guard), (2) Replaced `strstr` with exact FNameEntry header format matching + `CorroborateFNameChunk`, (3) PE FileVersion fallback.

---

### 7. OctoPath Traveler: Pipe Connection Failed (UE4 TNameEntryArray)

**Root cause**: OctoPath uses `TNameEntryArray` (flat chunked pointer array), not `FNamePool`. All validators failed → GNames=0 → `FindAll` returned false → pipe server never started.

**Fix**: Added `ValidateGNamesUE4()` for UE4 TNameEntryArray layout (double-deref, tries string offsets 0x10/0x06/0x0C/0x08). `ValidateGNamesAny()` wrapper tries FNamePool→Structural→UE4 in sequence.

---

### 8. OctoPath Traveler: Object Tree Empty (Pagination Bug)

**Root cause**: DLL skips objects with empty names but UI advanced offset by `result.Objects.Count` (returned count) instead of by number of indices scanned.

**Fix**: (1) DLL returns `"scanned"` field = number of indices iterated. (2) UI advances `offset += result.Scanned`. (3) Break condition changed to `_allNodes.Count >= 2000`.

---

### 9. FF7 Rebirth: GNames Not Found (Hash-Prefixed FNameEntry)

**Root cause**: FF7Re (UE4.26, Square Enix fork) uses hash-prefixed FNameEntry: `[4B ComparisonId][2B header][string]`. Standard validator expected header at offset +0, but it's at +4 here.

**Fix**: ValidateGNames, ValidateGNamesStructural, and LooksLikeNoneEntry all try header at offsets +0 and +4. `FNamePool::GetString()` reads header at `entry + s_headerOffset`, string at `entry + s_headerOffset + 2`. `FNamePool.cpp` sets `s_stride=4` when `headerOffset >= 4`.

---

### 10. OctoPath + FF7Re: Object Names Empty / Garbled (FUObjectItem Size Mismatch)

**Root cause**: `ObjectArray.h` hardcoded `FUObjectItem = 16 bytes`, but UE4 uses 24 bytes. LCM(16,24)=48 caused every 3rd 16-byte slot to accidentally align on FF7Re.

**Fix**: Added `DetectItemSize()` — dynamic stride detection via FNamePool validation. Version override: if `bUE4NameArray && version >= 500` → force 422.

---

### 11. OctoPath: DetectItemSize Fails on Null item[0] + FF7Re: FNAME_STRIDE Wrong

**Root causes**: (1) `DetectItemSize()` gave up if item[0] was null, (2) Hash-prefixed FNameEntry has stride=4, not stride=2.

**Fix v1**: (1) Rewrite: scan first 384 bytes in 8-byte steps for first valid UObject, then try strides from anchor. (2) `s_stride` variable set from `headerOffset`. **Status**: FF7Re fixed ✅, TQ2 regressed ❌, OctoPath still fails ❌.

---

### 12. TQ2 Regression + OctoPath Still Empty (DetectItemSize Anchor-Scan Flaws)

**Root causes**: (1) Anchor-scan found false valid UObject at non-boundary offset, (2) OctoPath's scan range (384B) was too small for its many null early slots.

**Fix v2**: No anchor scan — walk at stride-aligned positions only. FNamePool-based validation. Deep scan phase for sparse early slots. Order: {16, 24, 20}. **Status**: TQ2 ✅, FF7Re still fails ❌ (no tiebreaker), OctoPath still fails ❌.

---

### 13. FF7Re Stride Tiebreaker + OctoPath Flat Array (DetectItemSize v3)

**Root causes**: (1) No tiebreaker when named counts are equal (LCM alignment), (2) OctoPath uses flat (non-chunked) GObjects array.

**Fix v3**: (1) Tiebreaker: prefer stride with fewer bad items when named counts equal. (2) Flat array Phase 3: if Phases 1+2 find nothing, try `chunkTable` directly as item base. **Status**: TQ2 ✅, FF7Re ✅, OctoPath ❌, Tower of Mask ✅.

---

### 14. OctoPath: DetectLayout Selects Wrong Layout (Objects Pointer is Code Address)

**Root cause**: Alternate layout check `*(addr+0x04) > 0` selected layout with Objects pointer pointing into `.text` section (machine code).

**Fix (DetectLayout v2)**: Added `LooksLikeHeapPtr()` helper. Rewrote DetectLayout with 4 candidates (A/B/C/D), each validating Objects pointer is a heap address.

---

### 15. FF7R (Remake Intergrade): ValidateGObjects False Positive + V12 Deref Bug

**Root causes**: (1) Deref variant of V6 pattern found a heap address where `*(addr + 0x14)` coincidentally equaled 32759, (2) V12/RE2 patterns did `*(target) - 0x10` instead of `target - 0x10`.

**Fix**: Rewrote `ValidateGObjects` to validate chunk chain (chunk[0] must be heap pointer, not in game module). Fixed V12/RE2 handlers to try `ValidateGObjects(target - 0x10)` first.

---

### 16. FF7R (Remake Intergrade): Truncated Names + UProperty Mode Misdetection

**Root causes**: (1) Stride 16 won over 24 by named count (LCM alignment), (2) Version defaulted to 504 → FProperty mode, but FF7R needs UProperty mode.

**Fix**: (1) Added `DetectStride()` in `FNamePool.cpp`. (2) Added FProperty-to-UProperty fallback in `ValidateAndFixOffsets()`. **Status**: UProperty fallback ✅, stride still wrong ❌.

---

### 17. FF7R: Stride 16 Wins Over 24 Despite High Bad Count

**Root cause**: Comparison used `named` count as primary metric; early exit `if (outNamed >= 5) break` capped all strides at 5 named items.

**Fix (v4)**: Removed early exit. Added `ComputeStrideScore()`: `named * 10 - bad * 3`. **Status (build 1.0.0.23)**: v4 insufficient (both scores negative). v5 fallback: when all scores negative, pick stride with fewest bad items. Build 1.0.0.25 correctly selects stride 24. But names still garbled — root cause is flat array, not stride. See #18.

---

### 18. FF7R: Flat FFixedUObjectArray Misidentified as Chunked

**Root cause**: FF7R uses UE4.18 `FFixedUObjectArray` (flat, single indirection). Code treated Objects pointer as a chunk table, reading UObject internal memory as FUObjectItems → high bad counts for ALL strides.

**Detection signal**: For 165792 objects, 3 chunks needed. But `chunk[1] = *(arrayBase + 8) = 0x40000000` is NOT a valid heap pointer.

**Fix**: Added flat array pre-detection: check if `chunk[1]` is a valid heap pointer when `numElements > OBJECTS_PER_CHUNK`. If not, try flat layout first. **Status (build 1.0.0.26)**: P0 did not trigger — `LooksLikeHeapPtr(0x40000000)` returned true. See #19.

---

### 19. FF7R: LooksLikeHeapPtr Too Lenient for Flat Array Detection

**Root cause**: `0x40000000` (EObjectFlags::Const) passes all three `LooksLikeHeapPtr` checks. Real heap pointers on x64 Windows with ASLR are > 4GB.

**Fix (build 1.0.0.27)**: Two-layer validation at P0 flat check site: (1) Magnitude check — if `chunk1 < 0x100000000`, trigger extra validation. (2) `ReadSafe` dereference — if unmapped, mark as not a valid chunk pointer.

**Status**: ✅ **CONFIRMED WORKING**. FF7R Object Tree correct (Package, Class, AnimNotifyState, etc.). 165792 objects, flat array working.

---

### 20. "Start from GWorld" Fails in UI — CDO Detection + Missing Error Display

**Root causes**: (1) GObjects fallback for GWorld found `Default__World` (CDO) first — CDOs have all instance pointers null. (2) UI ignored the `"error"` field in `ok=true` responses.

**Fix (build 1.0.0.27)**: (1) DLL skips CDOs (`objName.rfind("Default__", 0) == 0`). (2) `WorldWalkResult.cs` added `Error` property. (3) `DumpService.cs` reads `res["error"]`. (4) `LiveWalkerViewModel.cs` calls `SetError()` when `world.Error` is set.

**Status**: Mostly confirmed — GWorld works on 7/17 tested games. See [test-games.md](test-games.md) for full list.

-----

## Next Steps

1. ~~Test FF7R with flat array fix~~ → ✅ Done (build 1.0.0.27)
2. ~~Fix "Start from GWorld" UI~~ → ✅ Done: 7/17 games working
3. ~~CE XML Export overhaul~~ → ✅ Done: signed int, BoolProperty bits, pointer hex, StructProperty auto-expansion
4. ~~Float/Double display formatting~~ → ✅ Done: Float 10dp, Double 15dp, strip-to-integer
5. ~~CT instructions fix~~ → ✅ Done: removed misleading step 2, simplified to 4 steps
6. ~~EnumProperty + StrProperty support~~ → ✅ Done (build 1.0.0.40)
7. ~~Address format toggle~~ → ✅ Done (PR #53): 3 modes (hex/0x/module+offset)
8. ~~Logger category restructuring~~ → ✅ Done (PR #54): DLL 5 files + UI 3 files, 2-file rotation
9. ~~ArrayProperty Phase A inner type~~ → ✅ Done: Phase A verified on TQ2
10. ~~ArrayProperty Phase B scalar element values~~ → ✅ Done: Verified on EverSpace 2
11. **Fix FF7R UE version display** — shows 504 instead of 418. Cosmetic; need version override for flat array / UProperty fallback games
12. **Investigate GWorld failure on remaining games** — FF7R, FF7Re, DQ XI S, DQ I&II, DQ III, OctoPath, Star Wars Jedi, Satisfactory
13. **Investigate UE version misdetection** — DQ I&II/DQ III/Ghostwire: Tokyo all show UE505 but may be UE4
14. **Investigate FF7Re CE address-to-object lookup failure** — works on DQ but not FF7Re
15. **ArrayProperty Phase C CE XML export** — 58 tests pass, **NOT YET VERIFIED** on EverSpace 2
16. **CE XML pointer chain fix** — 58 tests pass, **NOT YET VERIFIED** on EverSpace 2
17. Continue Avalonia UI development — all 17 tested games have working Object Tree
