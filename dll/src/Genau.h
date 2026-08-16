#pragma once

// ============================================================
// Genau — 葛納烏 (一級魔法使篩選考官 — First-Class Mage Examiner)
// OffsetFinder: AOB pattern scanning for GObjects, GNames, GWorld
// ============================================================

#include <cstdint>
#include <functional>
#include <vector>
#include <string>

namespace Genau {

// Callback for reporting scan progress (phase 0-7, status text).
// Phase: 0=idle, 1=version, 2=GObjects, 3=GNames, 4=GWorld, 5=init, 6=dynoff, 7=complete
using ScanProgressFn = std::function<void(int phase, const char* text)>;

// Revision of the UE-version *detection logic* (DetectVersionDetailed + tier/anchor
// rules + publisher bias). The HintCache stamps each saved version with this value so
// a later launch can trust a cached version and SKIP the slow memory string scan.
//
// Because DetectVersionDetailed scans the module's own STATIC image (deterministic per
// binary), re-detecting the same peHash always yields the same answer — so a cache hit
// is safe regardless of publisher or confidence. The only reason to re-detect is if this
// logic changed and an older cached value might now be detected differently.
//
// >>> BUMP THIS whenever DetectVersionFromPEResource / DetectVersionDetailed / tier rules /
//     HasUEAnchorNearby / publisher-bias change, so every cached version is recomputed once
//     under the new logic. Do NOT tie it to the build number — that would re-detect on every
//     rebuild and defeat the cache for stripped-version games (SquareEnix).
// rev 2 (build ~2394): VERSIONINFO StringFileInfo ProductVersion/FileVersion fallback,
// UTF-16LE pass in the Tier-1 memory scan, and Tier 1 no longer requires the needle
// table's trailing '.' (real tags are "++UE4+Release-4.27", so it never matched before).
// rev 3: pre-UE4 (UE3) marker check in DetectVersionDetailed's terminal branch, returning
// Grimoire::PRE_UE4_SENTINEL_VERSION. The bump is MANDATORY, not cosmetic: a UE3 game that
// already ran under rev 2 is cached as ueVersion=504, and the cache-reuse branch would restore
// that forever without ever re-detecting — i.e. the fix would silently not apply to the very
// machine that hit the problem.
// rev 4 (audit #5 G8 + G9): TIER RULES CHANGED, so this bump is mandatory under the rule
// above. G8 — Tier 2's context test was an 8-byte strstr while its comment and buffer both
// said 16; it is now a raw 16-byte clamped search (NUL-immune, unlike a wider strstr) AND
// carries the same UE-anchor gate Tier 3 always had, so widening cannot manufacture a
// confident hit out of a "Release Notes 5.4.0". G9 — a Tier 3 candidate no longer retires
// its pattern, so a later Tier 2 hit on the SAME needle is found; without that, a stray
// bundled "5.5.0" out-raced a real "Release-4.27" later in the module, which is verbatim
// the case DetectVersionDetailed's own comment claims the design prevents.
// Both were measured NO-OPS on all 85 real PE images in the local corpus (Tier 2 has never
// fired on any of them — see the trailing-dot finding in the audit register), so the bump
// costs one re-detect per cached game at ~0.35 s, not a re-scan of anything expensive.
// rev 5 (audit #5 G11): Tier 2 now matches the BARE needle. The table's trailing '.' is a
// Tier 3 device (it forces a three-component "X.Y.Z"); applying it to Tier 2 meant Tier 2
// could never match UE's own TWO-component tag "++UE4+Release-4.27", and MEASURED over the
// 170 PE images in the local corpus it had fired 0 times. Post-fix it fires on 6, and on
// all six it AGREES with the version Tier 1 independently reports — two detectors
// cross-validating. Tier 1 answers first on all six, so no effective verdict changed on any
// binary we own; the gain is a working fallback for images whose full "++UEx+Release-" tag
// is stripped but a "Release-X.Y" fragment survives. Bump is mandatory: a game cached under
// rev 4 would keep its old tier/confidence without ever re-detecting.
constexpr uint32_t kVersionDetectLogicRev = 5;

struct EnginePointers {
    uintptr_t GObjects  = 0;   // FUObjectArray*
    uintptr_t GNames    = 0;   // FNamePool* or TNameEntryArray*
    uintptr_t GWorld    = 0;   // UWorld**
    uint32_t  UEVersion = 0;   // e.g. 500, 501, 503, 504, 427, 422
    bool      bUE4NameArray = false;   // true = TNameEntryArray (UE4 <4.23), false = FNamePool
    bool      bVersionDetected = true; // false = PE/memory scan failed, version is inferred or default
    bool      bUserOverride    = false;// true = ueVersion came from a user-set persistent override
    bool      bLowConfidence   = false;// true = detection used Tier 3 bare-pattern OR publisher-bias fallback

    /// true = the engine predates anything this dumper can read, so the scan was SKIPPED.
    ///
    /// Covers TWO cases, distinguished downstream by UEVersion alone (no extra field):
    ///   * UEVersion 400..410 — a CONFIDENTLY detected UE 4.0-4.10, i.e. "the right family,
    ///     a version too old". Only reachable via DetectVersionFromPEResource's major==4
    ///     branch; the memory needle table floors at 4.18 and can never go below it.
    ///   * UEVersion == Grimoire::PRE_UE4_SENTINEL_VERSION (300) — positively identified as
    ///     pre-UE4 (UE3) by CountPreUE4Markers, i.e. "not this engine family at all". Set only
    ///     in DetectVersionDetailed's terminal branch, so it requires that the PE resource and
    ///     all three memory tiers already found nothing.
    /// The UI must NOT collapse the two: 4.10's remedy line ("set an override") is actively
    /// wrong for UE3, whose override list has no expressible value and whose structures do not
    /// exist at any version. See str.Pointers.VersionTooOld vs str.Pointers.EnginePreUE4.
    ///
    /// UE 4.10 and earlier have no `FUObjectItem` at all: `FUObjectArray::ObjObjects` is a
    /// `TStaticIndirectArrayThreadSafeRead` of raw `UObjectBase*` (stride 8) whose chunk table is
    /// INLINE, so `ArrayLayout` cannot even express it (`objectsOffset` means "read a pointer
    /// here"; 4.10 needs "take the ADDRESS of here") — see docs/technical-notes.md. Scanning
    /// anyway just burns ~4 s of AVX2 passes to reach "no winner", which is what these titles did
    /// before this flag existed. Only ever set on a CONFIDENTLY detected version: a
    /// low-confidence or user-overridden version is never gated, because misdetecting a working
    /// game as "too old" is far worse than wasting a scan.
    bool      bVersionTooOld   = false;
    // True when any in-FindAll scan bailed on Tot::Requested(), so the pointers below are
    // PARTIAL and must not be persisted or latched. Sibling of bVersionTooOld — same job,
    // different reason: both say "the empty result below is not a measurement". (audit #5 MA1)
    // Deliberately NOT fed by the GEngine report: that resolves outside FindAll via
    // ResolveGEngineDeferred and is not written to the hint cache.
    bool      bScanCancelled   = false;
    const char* publisherThumbprint = nullptr; // e.g. "SQUARE_ENIX" (nullptr if no match) — string literal lifetime
    int       ue4StringOffset = 0x10;  // FNameEntry string offset for UE4 mode
    int       fnameEntryHeaderOffset = 0; // Offset to 2-byte header within FNameEntry (0=standard, 4=hash-prefixed UE4.26)
    int       nameChunksOffset       = 0; // Pool -> Blocks[] offset proved at accept (obfuscated forks only)
    int       namePayloadGap         = 0; // Bytes between the header and the chars; 0 = stock, >0 = obfuscated fork
    uintptr_t nameKeyTableCtx        = 0; // Fork's tag -> XOR key hash map (obfuscated forks only)

    // FSparseDelegateStorage::SparseDelegates address (UE 5.0+; 0 if scan failed
    // or version unsupported). Optional — drives MulticastSparseDelegateProperty
    // drill-down + Find Refs sparse coverage. Populated eagerly during FindAll
    // so the UI can display it; the same value backs Genau::FindSparseDelegateStorage.
    uintptr_t SparseDelegates = 0;

    // &GEngine — the STATIC SLOT holding UEngine*, not the engine object itself.
    // Resolved by AOB after GObjects/GNames/offsets are up (the validator has to ask the
    // reflected class for a "GameViewport" property). 0 = no AOB hit; callers then fall
    // back to Genau::FindGameEngine's GObjects walk, which yields only the OBJECT.
    // Having the slot is what lets a CE symbol auto-follow engine recreation.
    uintptr_t GEngine = 0;

    // Scan method for each pointer: "aob", "data_scan", "string_ref", "pointer_scan", "not_found"
    const char* gobjectsMethod        = "not_found";
    const char* gnamesMethod          = "not_found";
    const char* gworldMethod          = "not_found";
    const char* sparseDelegatesMethod = "not_found";
    const char* gengineMethod         = "not_found";

    // --- AOB Usage Tracking ---
    // PE hash: TimeDateStamp (8 hex) + SizeOfImage (8 hex) = unique game build ID
    char peHash[17] = {0};

    // Winning pattern IDs (point to AobSignature::id constexpr strings in Signatures.h)
    const char* gobjectsPatternId        = nullptr;
    const char* gnamesPatternId          = nullptr;
    const char* gworldPatternId          = nullptr;
    const char* sparseDelegatesPatternId = nullptr;

    // AOB scan hit addresses (instruction address where the winning pattern matched)
    uintptr_t gobjectsScanAddr        = 0;
    uintptr_t gnamesScanAddr          = 0;
    uintptr_t gworldScanAddr          = 0;
    uintptr_t sparseDelegatesScanAddr = 0;

    // Per-target scan statistics
    int gobjectsPatternsTried = 0;
    int gobjectsPatternsHit   = 0;
    int gnamesPatternsTried   = 0;
    int gnamesPatternsHit     = 0;
    int gworldPatternsTried   = 0;
    int gworldPatternsHit     = 0;

    // GWorld winning pattern AOB metadata (for CreateSymbolScript)
    const char* gworldAob    = nullptr;  // AOB pattern string (e.g. "48 8B 1D ?? ?? ?? ??")
    int         gworldAobPos = 0;        // instrOffset + opcodeLen: displacement offset within match
    int         gworldAobLen = 0;        // instrOffset + totalLen: instruction end for RIP calculation

    // GEngine winning pattern AOB metadata — same contract as the GWorld triple above, so
    // a GameEngine-rooted CE export can be AOB-wrapped exactly like a GWorld-rooted one.
    const char* gengineAob    = nullptr;
    int         gengineAobPos = 0;
    int         gengineAobLen = 0;
    const char* genginePatternId = nullptr;
    uintptr_t   gengineScanAddr  = 0;
};

// Scan and cache all global pointers
// Returns false on failure, error details logged
// progress: optional callback for UI progress reporting (phase 0-7)
bool FindAll(EnginePointers& out, ScanProgressFn progress = nullptr);

// Find GObjects (FUObjectArray) address
// hintPatternId: optional cached winning pattern ID to try first (from HintCache)
uintptr_t FindGObjects(const char* hintPatternId = nullptr);

// Find GNames (FNamePool) address
// hintPatternId: optional cached winning pattern ID to try first (from HintCache)
uintptr_t FindGNames(const char* hintPatternId = nullptr);

// Find GWorld pointer address
// hintPatternId: optional cached winning pattern ID to try first (from HintCache)
uintptr_t FindGWorld(const char* hintPatternId = nullptr);

// Lazily resolve FSparseDelegateStorage::SparseDelegates (UE 4.23+).
// Cached for the DLL lifetime; first call scans, subsequent calls are O(1).
// Returns 0 if no AOB pattern matched (caller should fall back to bIsBound).
//
// Layout support: the outer key is a raw `UObjectBase*` on UE 5.x AND on UE 4.27
// (PDB-verified on DropIn 4.27.2 — the long-standing "UE 4.23-4.27 uses FObjectKey"
// note was wrong, and FObjectKey is 8 bytes there, not 16). Aura's walker probes the
// live key shape at runtime rather than trusting a version number, so 4.23-4.26 —
// for which we still have no symbol evidence — fail safe instead of misreading.
uintptr_t FindSparseDelegateStorage();

// Resolve &GEngine (the static slot holding UEngine*) by AOB.
// MUST be called after GObjects/GNames/offsets are up: the validator derefs the slot
// and asks the reflected class for a non-null "GameViewport" property, which is the
// same version-independent test FindLiveGameEngine uses. Returns 0 if no pattern
// validated; callers then fall back to the GObjects walk (object only, no slot).
//
// That precondition is ENFORCED, not just documented: if DynOff::bOffsetsProbeRan is
// still false this returns 0 immediately (method "deferred") without scanning, because the
// validator cannot possibly succeed and the scan costs 0.2-0.7 s. Call
// ResolveGEngineDeferred once the offsets are up.
//
// PROBE-RAN, not VALIDATED — this comment named the strict flag while the code has always
// read the loose one, and since audit #5's G1 fix the two genuinely diverge (a partially
// probed run now reports validated=false). Grimoire.h:246 explains why the loose flag is
// the correct gate: using the strict one would regress &GEngine on exactly the builds
// where detection falls back to defaults.
uintptr_t FindGEngineSlot();

// Second-pass GEngine resolution, run after ValidateAndFixOffsets + FNamePool init.
//
// FindAll cannot resolve &GEngine: it runs before the dynamic FField/UStruct offsets and the
// FNamePool exist, and ValidateGEngineSlot needs both to look up the reflected "GameViewport"
// property. Before this existed, &GEngine reported "AOB not found" on every game even though
// the patterns were resolving to the correct address (verified against the Everspace 2 PDB).
//
// No-op when out.GEngine is already set. On success it updates the cached slot used by
// FindLiveGameEngine's fast path and republishes the pattern-id / scan-addr / AOB triple so a
// GameEngine-rooted CE export can still be AOB-wrapped.
uintptr_t ResolveGEngineDeferred(EnginePointers& out);

// Detect UE version from memory or PE resources
uint32_t DetectVersion();

// Dynamically detect and fix FField/FProperty/UStruct offsets.
// Must be called AFTER GObjects + GNames are initialized (Aura::Init + Serie::Init).
// ueVersion: detected UE version (e.g. 505 = UE5.5, 427 = UE4.27). Used to determine
// UProperty vs FProperty mode. Pass 0 if unknown (will fall back to heuristic detection).
// Updates DynOff:: namespace variables.
bool ValidateAndFixOffsets(uint32_t ueVersion);

// Lazy-detect UEnum::Names offset by probing known enums (ENetRole, etc.) in GObjects.
// Called on first EnumProperty encounter, NOT during init.
// Sets DynOff::UENUM_NAMES and DynOff::bUEnumNamesDetected on success.
bool DetectUEnumNames();

// === Extra Scan: user-triggered aggressive fallback techniques ===
// These are computationally expensive (seconds, not milliseconds) and are designed
// to be called from a background thread.  They are READ-ONLY — no global state is
// modified.  The caller is responsible for applying results (Aura::Init, etc.)
// on the pipe thread.

// Scan .data section for FUObjectArray by validating structure heuristics.
// Complements FindGObjectsByDataScan (which follows code references instead).
uintptr_t ExtraScanGObjects();

// Collect GObjects candidates that pass structural validation, discovered via
// the data-section RIP-relative pointer scan (same mechanism as the FindGObjects
// data-scan fallback). Used by the post-init decoy-recovery path when the primary
// GObjects yielded 0 usable objects (e.g. Avowed / Obsidian UE5.x, where a .data
// structure coincidentally matches but contains no objects). Skips `avoid`.
// Appends up to `maxCandidates` UNIQUE addresses to `out`; returns the number added.
size_t CollectGObjectsCandidates(std::vector<uintptr_t>& out, uintptr_t avoid = 0,
                                 size_t maxCandidates = 16);

// Locate a STATIC FUObjectArray living in the module's .data/BSS, for games where
// GUObjectArray is a static global that NO AOB pattern matches (Avowed / Obsidian
// UE5.3 — confirmed even RE-UE4SS/patternsleuth's patterns fail). Scans .text for
// `lea/mov reg,[rip+disp]` slots in writable .data, then probes a window around each
// as a standard UE5 chunked FUObjectArray (ObjObjects @ +0x10, NumElements @ +0x24)
// and validates by CONTENT — the first objects must resolve to clean printable-ASCII
// names. Returns the base of the best match (0 if none). Requires Serie initialized.
// outItemStride (optional) receives the FUObjectItem stride that decoded cleanly
// (e.g. 0x14 for Obsidian's packed item, 0x18 standard) — pass it to
// Aura::InitWithExtendedLayout so the stride isn't re-detected (and mis-picked).
uintptr_t FindGObjectsStaticStruct(int* outItemStride = nullptr);

// Find GWorld by iterating GObjects for UWorld instance, then scanning .data
// for a static pointer to that instance.  Requires GObjects + GNames already initialized.
uintptr_t ExtraScanGWorld();

// Gap-fill for ExtraScanGWorld: when NO static .data slot points at the live UWorld
// (ExtraScanGWorld returned 0), recover GWorld via the engine object graph —
// GEngine -> GameViewport -> &World (the address of the live UWorld* FIELD inside the
// viewport, which the engine keeps updated across level transitions). All offsets are
// resolved by reflected member NAME (version-independent); the World field falls back to
// a bounded memory probe if it isn't reflected. Requires GObjects + GNames + offsets.
// Returns a deref-once slot (read once -> current UWorld) or 0.
//
// NOTE: the returned slot is a HEAP object field, not a static module symbol — valid for
// live operations (teleport / live walk / path search) but NOT for cross-session CE
// symbol export. Callers wanting a static anchor should prefer ExtraScanGWorld's slot.
uintptr_t RecoverGWorldViaEngine();

// Result of FindGameEngine — the live UEngine/UGameEngine object for the Live
// Walker "Start from GameEngine" root. engineAddr is 0 when no live engine was
// found. The two *Ok flags report whether the standard pointer members
// (GameViewport / GameInstance) are present AND non-null — i.e. this is the
// active engine, not a CDO or an early-boot stub.
struct GameEngineInfo {
    uintptr_t   engineAddr     = 0;
    uintptr_t   classAddr      = 0;
    std::string className;            // e.g. "GameEngine" or a game subclass
    bool        gameViewportOk = false;
    bool        gameInstanceOk = false;
};

// Resolve the live GEngine object (same reflected-member detection as
// RecoverGWorldViaEngine — version-independent, NOT keyed on the class name)
// and validate its standard pointer fields. Requires GObjects + GNames +
// offsets. Returns an all-zero/false GameEngineInfo when none is found.
GameEngineInfo FindGameEngine();

} // namespace Genau
