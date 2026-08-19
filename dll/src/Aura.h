#pragma once

// ============================================================
// Aura — 斷頭台的阿烏拉 (服從之秤 — Obedience Scale)
// ObjectArray: FUObjectArray slot enumeration and validation
// ============================================================

#include <cctype>
#include <cstdint>
#include <functional>
#include <string>
#include <unordered_set>
#include <utility>
#include <vector>

#include "Ubel.h"   // For ::ClassInfo (defined at global scope in Ubel.h, despite the filename) used by WalkClassesBatch
#include "Radar.h"
#include "Orden.h"  // For Radar::Candidate / DataType / ScanType used by ScanForValue / RefineCandidates
#include "GraphPath.h"   // GraphPathResult / GraphPathStep + the pure BFS core used by FindObjectGraphPath

// FUObjectItem structure (in FChunkedFixedUObjectArray)
// Size varies by UE version — auto-detected at Init() time:
//   UE5 (most):  16 bytes { Object*(8), Flags(4), SerialNumber(4) }
//   UE4 / some UE5: 24 bytes { Object*(8), Flags(4), ClusterRootIndex(4), SerialNumber(4), _pad(4) }
// NOTE: this struct mirrors the CLASSIC layout (Object* at +0x00, UE4.x..UE5.6 only).
// UE5.7+ reordered the item — int64 FlagsAndRefCount moved to +0x00, pushing UObject*
// to +0x08 — so do NOT read `.Object` straight off a raw item pointer. Use
// Aura::GetByIndex(), which applies the auto-detected within-item object-ptr offset.
struct FUObjectItem {
    uintptr_t Object;           // UObject* — at +0x00 on classic layout ONLY (see note above)
    int32_t   Flags;
    int32_t   SerialNumber;
};

namespace Aura {

// === Encrypted GObjects Support (GAP #1) ===
// Some anti-cheat games encrypt the Objects pointer in FUObjectArray.
// Set a custom decryption function BEFORE calling Init().
// Default: nullptr (identity, zero overhead for non-encrypted games).
using DecryptFunc = uintptr_t(*)(uintptr_t rawPtr);
void SetDecryptFunc(DecryptFunc func);
uintptr_t DecryptObjectPtr(uintptr_t rawPtr);

// Initialize with the FUObjectArray address found by OffsetFinder
void Init(uintptr_t gobjectsAddr);

// Initialize with the layout FORCED to standard UE5 chunked-extended
// (ObjObjects.Objects @ +0x10, MaxElements @ +0x20, NumElements @ +0x24).
// Used by the static-struct GObjects recovery (Avowed / Obsidian UE5.3) where
// the base is already content-validated as this layout, so layout auto-detection
// (which can mis-pick a relaxed preset reading NumElements at the wrong offset)
// must be bypassed.
// forcedItemSize > 0 also FORCES the FUObjectItem stride (object ptr @ +0x00,
// Classic mode) — needed for Obsidian's 20-byte packed FUObjectItem, which
// stride auto-detection can mis-read as 24. 0 = auto-detect the stride.
void InitWithExtendedLayout(uintptr_t gobjectsAddr, int forcedItemSize = 0);

// Get total number of allocated objects
int32_t GetCount();

// Get max number of objects
int32_t GetMax();

// Get UObject* by index (returns 0 if invalid/null)
uintptr_t GetByIndex(int32_t index);

// Get FUObjectItem by index (returns nullptr if invalid).
// WARNING: the returned struct's `.Object` field is only valid on the CLASSIC layout
// (UE4.x..UE5.6). It is WRONG on UE5.7+ Unpacked (Object* moved to +0x08) and on
// UE5.7+ Packed (Object* is reconstructed from two split fields, not stored). For the
// object pointer ALWAYS use GetByIndex(), which is layout-aware.
FUObjectItem* GetItem(int32_t index);

// Read the SerialNumber of the FUObjectItem at the given index.
// The offset rule is Lineal::SerialOffsetForLayout, which covers every reachable
// stride — 16 (@+0x0C), and 20/24/32 (@+0x10), plus the two UE5.7+ modes. The
// comment here used to say "16-byte or 24-byte" and the code matched it, which
// is why the reachable 20-byte packed item read ClusterRootIndex instead
// (audit #5 A1).
int32_t GetSerialNumber(int32_t index);

// Iterate all valid objects
// Callback: return false to stop iteration
void ForEach(std::function<bool(int32_t idx, uintptr_t obj)> cb);

// Find first object matching name (linear scan)
uintptr_t FindByName(const std::string& name);

// Find first object matching full path (linear scan)
uintptr_t FindByFullName(const std::string& fullName);

// Resolve a query that may be EITHER a bare FName ("Actor") or a path
// ("/Script/Engine.Actor" / "Class /Script/Engine.Actor" / "//Script/Engine/Actor").
// Path first when the query carries a separator, then bare-name fallback. This is
// what `UE5_FindObject` and the pipe's `find_object` call; prefer it over calling
// FindByName directly, or path-shaped input silently resolves to nothing.
uintptr_t FindByNameOrPath(const std::string& query);

// Get the detected FUObjectItem stride in bytes (16 or 24)
int GetItemSize();

// Within-item byte offset of the UObject* for the two DIRECT layouts
// (0x00 classic, 0x08 UE5.7+ unpacked). Not meaningful under packed mode.
int GetItemObjOffset();

// True when the detected layout is the UE5.7+ *** UNVERIFIED *** packed
// FUObjectItem encoding (UObject* reconstructed from two split fields).
bool IsPacked();

// Runtime calibration / force-enable for the packed reconstruction (no rebuild).
// Pass alignBits<=0 / ptrMaskBits==0 / serialOff<0 to leave that field unchanged.
// force=true switches the live layout to packed unconditionally (calibration harness
// for the first real packed game). See Lineal.h for the encoding.
void SetPackedConsts(int alignBits, uint64_t ptrMaskBits, bool force, int serialOff = -1);

// Whether the GObjects array is a flat (non-chunked) FFixedUObjectArray.
// Flat arrays were used in UE4.11-4.20; chunked arrays in UE4.21+ and all UE5.
bool IsFlat();

// Search objects by partial name (case-insensitive), returns up to maxResults
struct SearchResult {
    uintptr_t addr;
    int32_t   index;       // InternalIndex in GObjects
    std::string name;
    std::string className;
    uintptr_t classAddr = 0;   // UClass* — key for find_functions_by_class
    uintptr_t outer;
};

// Search results with diagnostic counters for debugging
struct SearchResultSet {
    std::vector<SearchResult> results;
    int32_t scanned = 0;    // Total indices iterated (= GetCount() at call time)
    int32_t nonNull = 0;    // Objects that were non-null
    int32_t named   = 0;    // Objects whose class name resolved successfully
    bool truncated  = false;// More non-excluded matches exist than the cap returned
    bool aborted    = false;// Tot tripped mid-walk: results AND any total are INCOMPLETE
                            // and must not be published as a count of anything
    // Class-noise histogram for FindInstancesByClass (build with the same
    // count-desc/name-asc shape as the value-scan picker). Tallied over the FULL
    // matched pool — every row satisfying the class+name query, counted BEFORE
    // the exclude-skip and INDEPENDENTLY of the result cap — so an excluded class
    // (or one whose instances all sit past the cap) still appears in the picker
    // and stays untickable. Empty for the SearchByName / internal-caller paths.
    std::vector<std::pair<std::string, int>> classHistogram;
    int32_t classDistinct = 0;   // distinct matched classes (>= classHistogram.size() when capped)
};

// Search every UObject by keyword. `query` is whitespace-tokenized into AND terms;
// each term must match the object name OR the class name (space=AND, field-level OR —
// mirrors the client ObjectTreeFilter). When `instancesOnly`, reflection/type-layer
// rows (IsReflectionMetaClass) are skipped so only live instances are returned. Sets
// SearchResultSet::truncated when the result cap was hit.
SearchResultSet SearchByName(const std::string& query, int maxResults = 200, bool instancesOnly = false);

// Find all instances whose class name matches (case-insensitive partial match)
// AND, optionally, whose OBJECT name contains nameFilter (case-insensitive
// substring). Either query may be empty: an empty className matches every
// class (object-name-only search), an empty nameFilter applies no name gate.
// At least one of the two must be non-empty (the caller enforces this).
// Returns addr, index, name, className, outer for each instance
// newestFirst: walk GObjects from the high (most-recently-allocated) end so the
// newest runtime spawns survive the maxResults cap. Default low->high keeps the
// OLDEST maxResults (CDO / class-default / earliest instances) — ideal for
// finding a Blueprint's template/defaults, but it truncates the newest off the
// end for high-population classes (so "catch the just-spawned enemy" needs newestFirst).
//
// excludeClasses: server-side class-noise filter. A matched row whose class name
// is in this set is SKIPPED before it consumes a result-cap slot — so a wanted
// instance that today sits past the cap survives once the noise classes ahead of
// it are excluded (the UI re-runs the scan when the class-noise picker changes).
// Comparison is EXACT and case-SENSITIVE (names come from rset.classHistogram,
// correctly cased) — deliberately NOT the case-insensitive substring match used
// for the className query. The histogram is still tallied over the full pool
// PRE-exclude so excluded classes stay visible/untickable in the picker.
//
// buildHistogram: when true (the pipe `find_instances` path), the scan does NOT
// stop at the cap — it walks ALL of GObjects so rset.classHistogram counts every
// matched class even when its instances all sit past the cap (the histogram-vanish
// fix), and applies excludeClasses before the cap. When false (the cheap internal
// callers: Wirbel/Edel/Solitar/Mimic/Frieren, which want a bounded first-N scan and
// ignore the histogram), the loop keeps the old early-exit at maxResults and skips
// the tally — so those hot paths are not regressed into a full-array walk.
SearchResultSet FindInstancesByClass(const std::string& className, bool exactMatch = false, int maxResults = 500, bool newestFirst = false, const std::string& nameFilter = "", const std::vector<std::string>& excludeClasses = {}, bool buildHistogram = false);

// LIVE instances of `baseClassName` AND of every class derived from it.
//
// Not expressible through FindInstancesByClass, whose class gate is a NAME test
// and nothing else: exactMatch=true takes only the class itself, and
// exactMatch=false is a case-insensitive SUBSTRING over the class name — for
// "Actor" that captures everything with "actor" anywhere in its name while
// still missing every subclass that does not contain the word. This walks the
// UClass super chain instead, which is the actual derivation relation.
//
// Two behaviours that are part of the contract, not incidental:
//   * Class-default objects are skipped INSIDE the walk, before the result cap.
//     A base like AActor is the ancestor of thousands of classes, each with a
//     Default__ object, and those are constructed at class-load time so they
//     occupy the low GObjects indices — filtering them afterwards would leave a
//     caller with `maxResults` CDOs and zero live instances.
//   * SearchResult::className is the CONCRETE class of each instance, not the
//     queried base, so a caller can still tell the subclasses apart.
//
// Case-insensitive, matching FindInstancesByClass (the name reaches us over the
// pipe). Cost is one super-chain walk per DISTINCT UClass, not per object.
// `truncated` means more derived instances exist than the cap returned.
/// @param outerFilter  0 = no filter. Non-zero = keep only objects whose UObject::Outer
///        IS this address. The read is hoisted ABOVE the class read so the expensive
///        half (class + memo + FName decode) is paid only by the surviving minority.
/// @param totalOut  non-null = keep COUNTING past `maxResults` and report the exact
///        pre-cap match count. `truncated` is then derived from it, so the page flag and
///        the total can never be computed by two different code paths.
SearchResultSet FindInstancesDerivedFrom(const std::string& baseClassName, int maxResults = 500,
                                         uintptr_t outerFilter = 0, int32_t* totalOut = nullptr);

/// Live actors of `levelAddr`, DERIVED from the Outer back-reference.
///
/// `ULevel::Actors` is declared `TArray<TObjectPtr<AActor>> Actors;` with NO UPROPERTY
/// (Engine/Classes/Engine/Level.h:428-429), so reflection never sees it and the field
/// lookup that used to drive walk_world could not ever have matched (audit #5 F8/F9).
/// SpawnActor outers every actor to the level it then adds it to, so the Outer
/// back-reference reconstructs the list without knowing any native offset.
///
/// Deriving from AActor is MANDATORY, not a nicety: ULevelActorContainer and
/// UModelComponent are outered to the level too, so an outer-only test lists them as
/// actors. That gate is structural here — it IS the base class this queries.
///
/// Honest semantics: a SUPERSET of the engine's array by actors already destroyed but
/// not yet collected, and by actors mid-spawn; it is not the engine's array ORDER; and
/// `levelAddr == 0` returns nothing rather than degrading into "every actor in the game".
SearchResultSet FindActorsInLevel(uintptr_t levelAddr, int maxResults, int32_t* totalOut);

// Address-to-Instance reverse lookup result.
//
// Confidence levels (worst to best):
//   exact      — addr IS a UObject pointer (highest confidence)
//   contains   — addr is within UObject + PropertiesSize (high)
//   backward   — backward memory scan found a UObject header (medium —
//                addr is past a NewObject<>'d sub-object not in GObjects)
//   nearest    — closest GObjects entry below addr; addr is BEYOND its
//                PropertiesSize so this is just a "best guess" hint, not a
//                real containment (low — frequently misleading)
struct AddressLookupResult {
    bool        found         = false;
    bool        exactMatch    = false;  // true = addr is a UObject, false = addr is inside a UObject
    std::string matchKind;              // "exact" / "contains" / "backward" / "nearest"
    uintptr_t   objectAddr    = 0;      // The owning UObject address
    int32_t     index         = -1;     // InternalIndex in GObjects
    std::string name;
    std::string className;
    uintptr_t   outer         = 0;
    int32_t     offsetFromBase = 0;     // addr - objectAddr (0 for exact match)
};

// Given an arbitrary address, find which UObject it belongs to.
// First tries exact match (is this address a UObject?), then containment
// (is this address inside a UObject's property data?).
AddressLookupResult FindByAddress(uintptr_t addr);

// === Container-Aware Address Lookup ===

// One nesting hop beyond the outermost container, for a value buried in a
// SEPARATELY-allocated nested container (e.g. a TArray<int> inside a struct
// element of a TMap value inside a struct element of a TArray). The outermost
// container stays in the ContainerMatch fields below; nestedChain holds levels
// 1..N (each is one container drilled INTO, reached from the previous element's
// struct). The final hop's intraOffset is where `addr` lands within the
// deepest element. Empty for a plain 1-level match (back-compat).
struct ContainerHop {
    int32_t     fieldOffset   = 0;   // container offset within the PARENT element struct
    std::string fieldName;           // dotted name within that struct (e.g. "MsTuneData.MsTunes")
    std::string fieldType;           // "ArrayProperty" / "SetProperty" / "MapProperty"
    std::string innerType;           // inner element / value type label
    int32_t     elementIndex  = 0;   // matched element / sparse index
    int32_t     elementSize   = 0;   // per-element / per-pair stride
    int32_t     intraOffset   = 0;   // (addr - elementStart) within this hop's element
    uintptr_t   dataAddr      = 0;   // this hop's buffer base
    bool        mapValueSide  = false; // true when the hit is on the VALUE side of a Map pair
    std::string note;                // "" / "slack" / "freed"
};

// One match for an address that falls inside a UObject field's
// heap-allocated container buffer (TArray::Data / TSparseArray::Data).
struct ContainerMatch {
    uintptr_t   ownerObj      = 0;      // UObject that owns the container field
    int32_t     ownerIndex    = -1;     // GObjects index of owner
    std::string ownerName;
    std::string ownerClassName;
    int32_t     fieldOffset   = 0;      // Field offset within owner UObject
    std::string fieldName;
    std::string fieldType;              // "ArrayProperty" / "SetProperty" / "MapProperty"
    std::string innerType;              // Inner type label (Set: elem; Map: "K → V")
    int32_t     elementIndex  = 0;      // (addr - dataAddr) / stride (sparse index for Map/Set)
    int32_t     elementSize   = 0;      // Per-element / per-pair stride
    int32_t     intraOffset   = 0;      // (addr - elementStart) within element
    uintptr_t   dataAddr      = 0;      // TArray::Data / TSparseArray::Data base
    int32_t     count         = 0;      // Logical element count (allocated only for Map/Set)
    // Diagnostic note about match confidence:
    //   ""       — solid hit (within Count, allocated slot)
    //   "slack"  — Array index is in [Count, Max) — uninitialised / freed slack
    //   "freed"  — Map/Set slot is on the free list (stale data, may still match)
    std::string note;
    // Additional nesting levels for a deeply-nested value (FindInContainersDeep).
    // Empty for a shallow 1-level match. When non-empty, this ContainerMatch's
    // own fields describe the OUTERMOST container and nestedChain[i] each
    // deeper container; the deepest hop's intraOffset locates `addr`.
    std::vector<ContainerHop> nestedChain;
};

// Diagnostic stats from a container scan — surfaced through the pipe so
// the UI can tell the user whether a "not found" was a complete scan
// or got cut off by the deadline.
struct ContainerScanStats {
    int32_t objectsScanned   = 0;   // UObjects iterated
    int32_t objectsTotal     = 0;   // Total in GObjects
    int32_t classesPrimed    = 0;   // Unique classes touched (cache built)
    int64_t durationMs       = 0;
    bool    deadlineHit      = false;
};

// Scan all UObjects' container fields for `addr`. Returns matches where
// addr falls in [Data, Data + bound). Covers ArrayProperty (TArray.Data,
// including slack slots), SetProperty (TSparseArray.Data, including freed
// slots), and MapProperty (TSparseArray.Data of TPair). Has an internal
// time deadline and per-class field cache; cache persists for the DLL
// lifetime so subsequent calls are much faster.
//
// `stats` (optional out param) receives diagnostic counters; if non-null
// the caller can detect a truncated scan via `deadlineHit`.
std::vector<ContainerMatch> FindInContainers(uintptr_t addr, int32_t maxResults = 16,
                                              ContainerScanStats* stats = nullptr);

// Deep variant: when the shallow FindInContainers finds nothing because the
// value lives in a SEPARATELY-allocated nested container (a TArray/TMap/TSet
// whose data buffer is itself stored inside a struct element of an outer
// container), recursively descend struct-array / map-value / set elements up to
// `maxDepth` and report the full nested chain (ContainerMatch.nestedChain).
// Bounded by maxDepth, a per-container element cap, the same 15s deadline, and
// early-out on the first match — so it's only as expensive as needed and never
// hangs. Intended as a fallback (call only when FindInContainers returned empty).
std::vector<ContainerMatch> FindInContainersDeep(uintptr_t addr, int32_t maxResults = 8,
                                                 int32_t maxDepth = 4,
                                                 int32_t maxElemProbe = 256,
                                                 ContainerScanStats* stats = nullptr);

// === Reverse Reference Search (logical-parent navigation) ===
//
// One match for a UObject that holds a pointer to the target UObject.
// Used to answer "what owns this Item?" — UE's `OuterPrivate` is a
// naming-hierarchy parent (often `/Engine/Transient` for runtime objects)
// rather than the logical gameplay parent. Reverse-scanning all UObjects'
// pointer fields and Object array elements gives the actual owner.
struct ReferenceMatch {
    uintptr_t   ownerObj      = 0;
    int32_t     ownerIndex    = -1;
    std::string ownerName;
    std::string ownerClassName;
    int32_t     fieldOffset   = 0;        // Absolute field offset within owner
    std::string fieldName;                // Dotted path (e.g. "Stats.Equipment");
                                          // Map matches append ".Key" / ".Value"
    std::string fieldType;                // "ObjectProperty" / "ClassProperty" /
                                          // "InterfaceProperty" / "WeakObjectProperty" /
                                          // "SoftObjectProperty" / "SoftClassProperty" /
                                          // "LazyObjectProperty" / "OptionalProperty" /
                                          // "DelegateProperty" /
                                          // "MulticastInlineDelegateProperty" /
                                          // "MulticastDelegateProperty" /
                                          // "MulticastSparseDelegateProperty" /
                                          // "ArrayProperty" / "MapProperty" / "SetProperty"
    std::string innerType;                // For Array: inner element type;
                                          // For Set: element type;
                                          // For Map: "<keyType> → <valueType>"
    int32_t     elementIndex  = -1;       // -1 for direct field, >=0 for array/
                                          // map/set element (sparse index for
                                          // Map/Set)
};

// Find UObjects that hold a pointer to `target`. Walks every UObject's:
//   - ObjectProperty / ClassProperty / InterfaceProperty (8B raw pointer)
//   - WeakObjectProperty / SoftObject{Class}Property / LazyObjectProperty
//     (resolves embedded FWeakObjectPtr; only matches when bound to live
//     UObject)
//   - OptionalProperty<T> for pointer-shaped T (Object/Class/Interface/
//     Weak/Soft/Lazy) — same comparison as the bare T at field+0
//   - DelegateProperty (single FScriptDelegate — FWeakObjectPtr target at
//     field+0 — surfaces "X is bound to a delegate on Y" relationships)
//   - MulticastInlineDelegateProperty / MulticastDelegateProperty
//     (FMulticastScriptDelegate := TArray<FScriptDelegate>; walks each
//     binding's FWeakObjectPtr target).
//   - MulticastSparseDelegateProperty (UE 5.0+ only) — global pass after
//     the per-object loop walks FSparseDelegateStorage::SparseDelegates
//     once and checks every binding's FWeakObjectPtr against `target`.
//     Skipped silently when AOB scan failed or UE < 5.0.
//   - TArray of any single-pointer type above (incl. TArray<FScriptDelegate>)
//   - TMap with Object/Class key and/or value (allocated slots only)
//   - TSet with Object/Class element (allocated slots only)
// Walks include fields nested inside StructProperty (depth 3).
//
// Has its own per-class metadata cache (separate from container cache);
// first call primes, subsequent calls are fast.
//
// `stats` mirrors ContainerScanStats — duration and deadline indication.
std::vector<ReferenceMatch> FindReferencesToUObject(uintptr_t target,
                                                     int32_t maxResults = 32,
                                                     ContainerScanStats* stats = nullptr);

// === Related-object graph (forward, owned) — "Related Objects" panel ===
//
// Given any UObject (typically an actor/pawn the user is inspecting), assemble
// the objects most useful for understanding it in one bounded call:
//   - Self / Class / Outer                         (the naming hierarchy)
//   - Controller <-> Pawn counterpart              (reflected by field name)
//   - the sub-objects it OWNS, via an owned walk up to depth 3:
//       depth 1 — direct owned sub-objects (UActorComponents, custom
//                 Health/Stats components, the GAS AbilitySystemComponent)
//       depth 2-3 — each sub-object's owned objects (the ASC's UAttributeSets),
//                 so a GAS AttributeSet is reached even when nested behind a
//                 stats/ability layer: pawn -> stats component -> ASC -> AttributeSet
//                 (some games — e.g. TQ2 — don't hang the ASC directly off the actor).
//
// Discovery is STRUCTURAL — EnumerateOutgoingObjectPtrs (direct ObjectProperty
// fields AND object-pointer containers: OwnedComponents TSet, SpawnedAttributes
// TArray) gated by IsOwnedBy (Outer chains back to the target within 2 hops) —
// the SAME mechanism the cross-object group scan (P4) uses. So a game's custom
// component is found the same way as the engine ASC; the "AbilitySystem (ASC)" /
// "AttributeSet" / "Owned Component" *labels* are a class-name convenience on top
// of the structural walk, not the discovery filter.
//
// Fast and bounded (no full GObjects scan): the reverse "who references this
// object" view is the separate FindReferencesToUObject.
struct RelatedObject {
    uintptr_t   addr        = 0;
    int32_t     index       = -1;     // InternalIndex (Grimoire::OFF_UOBJECT_INDEX); -1 if unreadable
    std::string name;
    std::string className;
    std::string relation;             // "Self" / "Class" / "Outer" / "Controller" /
                                      // "Pawn" / "AbilitySystem (ASC)" / "AttributeSet" /
                                      // "Owned Component" / "Owned Object"
    std::string fieldName;            // field/path on parentAddr that points here ("" for Self/Class/Outer)
    int32_t     fieldOffset = -1;     // offset within parentAddr (CE / GWorld handoff); -1 if N/A
                                      // (-1 for container ELEMENT rows too: the element lives in
                                      //  the heap Data buffer, not at parentAddr+offset; the [idx]
                                      //  is encoded in fieldName instead)
    int32_t     depth       = 0;      // 0 hierarchy/counterpart, 1..3 owned BFS
    uintptr_t   parentAddr  = 0;      // object holding the pointer to this one
};

// See the section comment above. `maxResults` caps the list (default 128 — far
// above any real actor's owned-object count). Returns Self first, then
// Class/Outer/counterpart, then owned sub-objects in BFS order.
std::vector<RelatedObject> GetRelatedObjects(uintptr_t target, int32_t maxResults = 128);

// === Outgoing object-pointer enumeration (public façade) ===
//
// One outgoing object-pointer edge of an object — the public form of the
// internal EnumerateOutgoingObjectPtrs adapter (a file-static template inside
// Aura.cpp). Mirrors that adapter's RAW 8-value emit verbatim (no consumer-side
// normalization): for a CONTAINER element edge, fieldOffset is the container
// HEADER field offset (NOT -1) and elementIndex (>= 0) is the only element
// marker; fieldName is the bare field name plus only a ".Key"/".Value" suffix
// for map pairs (no "[i]" index — that lives in elementIndex). For a DIRECT edge,
// elementIndex is -1 and elemStride/elemValueOffset are 0. (Consumers that want a
// per-element offset compute it from fieldOffset + elementIndex*elemStride +
// elemValueOffset, or treat element rows specially — as Edel does.)
struct OutgoingPtr {
    uintptr_t   target          = 0;
    int32_t     fieldOffset     = -1;   // container HEADER offset for element rows; -1 only if the source emitted -1
    std::string fieldName;
    std::string fieldType;       // "ObjectProperty" / "ArrayProperty" / "MapProperty" / "SetProperty" / ...
    std::string innerType;
    int32_t     elementIndex    = -1;   // >= 0 marks a container element edge
    int32_t     elemStride      = 0;
    int32_t     elemValueOffset = 0;
};

// Collect EVERY outgoing object pointer of `obj` (direct Object/Class/Interface,
// weak/soft/lazy single fields, TArray<UObject*>, interface arrays, TMap, TSet
// of objects) into `out`, with NO ownership gate. The public seam for consumers
// that SCORE outgoing edges rather than BFS them (Edel current-target
// detection). Reuses the same per-class reference-metadata cache the
// reverse/forward graph walks use, so it is fast. Bounded by maxEdges (appends
// stop once out.size() reaches it).
void CollectOutgoingObjectPtrs(uintptr_t obj, std::vector<OutgoingPtr>& out,
                               int32_t maxEdges = 1024);

// === Forward Object-Graph Path Search ("Locate in GWorld") ===
//
// The inverse of FindReferencesToUObject: instead of "who points AT this
// object", answer "how do I REACH this object by following pointers from a
// root". Breadth-first search over the outgoing object-pointer edges of the
// UObject graph (the same edges FindReferencesToUObject walks: direct
// Object/Class/Interface pointers, Weak/Soft/Lazy, TArray/TMap/TSet of
// objects, and fields nested in StructProperty to depth 3) — reusing the
// per-class reference-metadata cache so the first call primes and later calls
// are fast.
//
// `rootObj`   : the UObject to start from (the caller resolves GWorld → UWorld
//               and passes the live UWorld* here; this function is root-agnostic
//               so it stays decoupled from the GWorld globals and is testable).
// `targetObj` : the UObject to reach. For a property VALUE the caller resolves
//               the owning UObject first (Value Search already knows it; an
//               arbitrary address resolves via FindByAddress / FindInContainers).
// `maxDepth`  : maximum hop count root → target (default 5; hard-capped at 32).
// `deadlineMs`: wall-clock budget; the search also bails on Tot::Requested().
// `deep`      : opt-in — ALSO follow object pointers stored inside ONE
//               struct-element container level (TArray<FStruct> / TSet<FStruct> /
//               TMap<*,FStruct> whose element struct holds a UObject*, incl. in an
//               inline sub-struct). This is the graph analogue of the Value Search
//               "Deep" nested-container descent: it reaches objects referenced
//               ONLY from a struct-array element (otherwise not_reachable). Each
//               such edge is one CE-splittable hop (container Data deref + element
//               pointer at index*stride + within-element offset). Deeper nesting
//               (object containers nested INSIDE the element struct — two container
//               levels) is out of scope (not a single splittable hop). Heavier
//               per node (reads each struct-array's elements); bounded by a
//               per-container element cap + the deadline / visited cap.
//
// BFS guarantees the path returned is a SHORTEST (fewest-hop) one, and the
// first such path found in deterministic iteration order. steps is
// root → steps[0].to → … → targetObj; toName/toClassName are resolved for the
// path nodes only. status is "ok" / "not_reachable" / "visited_cap" /
// "cancelled" / "deadline" / "invalid".
//
// NOTE: MulticastSparseDelegateProperty edges (bindings that live in
// CoreUObject's global FSparseDelegateStorage, not in the owning object) are
// intentionally NOT followed here — they are an unusual gameplay path and a
// global-TMap walk per node would be prohibitively expensive.
GraphPathResult FindObjectGraphPath(uintptr_t rootObj, uintptr_t targetObj,
                                    int32_t maxDepth = 5, int32_t deadlineMs = 20000,
                                    bool deep = false);

// === Property Keyword Search ===

struct PropertyMatch {
    std::string className;        // The class this match was emitted from
                                  // (= definingClassName after dedup, since
                                  //  dedup keeps only the defining-class row)
    uintptr_t   classAddr = 0;
    std::string classPath;
    std::string superName;
    std::string propName;
    std::string propType;
    int32_t     propOffset = 0;
    int32_t     propSize   = 0;
    std::string structType;   // StructProperty -> inner struct name
    std::string innerType;    // ArrayProperty -> inner element type

    // === Inheritance-aware fields (build 610+) ===
    //
    // PropertySearch dedupes by (definingClass, propName, offset) so a
    // field declared on AActor and inherited by 4823 children only emits
    // one row. The defining class is the highest-up class in the
    // inheritance chain that actually declares the property; everything
    // below it inherits the same FProperty at the same offset, so writing
    // to that offset on any instance has identical effect.
    std::string definingClassName;     // Class where the FProperty is first declared
    uintptr_t   definingClassAddr = 0; // Address of that class
    std::string definingClassPath;     // Full path of the defining class (for game-vs-engine UI hint)
    int32_t     inheritedByCount = 0;  // Number of OTHER classes (excludes defining)
                                       // that inherit this field. 0 means
                                       // the property is unique to this class.

    // === Internal preview-resolution helper (not serialised) ===
    //
    // After dedup, classAddr / definingClassAddr point to the canonical
    // defining class (often abstract -- AActor / APawn / etc -- with no
    // direct instances). Phase 2 needs an actual non-abstract subclass
    // to find a representative instance. Track the most-derived
    // subclass we observed during the search loop so Phase 2 can find
    // instances even when the defining class is abstract.
    //
    // "Most derived" is approximated by largest PropertiesSize -- a
    // subclass with more bytes in its struct is presumed to be deeper
    // in the inheritance chain and more likely to have live instances
    // (concrete BP classes typically have more fields than the abstract
    // engine bases).
    uintptr_t   previewClassAddr      = 0;
    int32_t     previewPropertiesSize = 0;

    // Preview support — populated in Phase 2 of SearchProperties
    std::string preview;           // Inline value preview from a representative instance
    uintptr_t   fieldAddr   = 0;   // FField/FProperty address (for enum resolve)
    uint64_t    propertyFlags = 0; // CPF_* reflection flags (SaveGame/BlueprintVisible/EditorOnly/...) — auto-detect scorer gating
    uint8_t     boolFieldMask  = 0; // BoolProperty: FieldMask byte
    uint8_t     boolByteOffset = 0; // BoolProperty: ByteOffset within property
    uintptr_t   enumAddr    = 0;   // EnumProperty: UEnum* for name resolution
    std::string keyType;           // MapProperty: key type name
    std::string valueType;         // MapProperty: value type name

    // === Deep / nested match (build 1222) ===
    //
    // True when this row is a synthetic dotted-path leaf discovered by the
    // deep descent into StructProperty members + struct-typed container
    // elements (TArray/TSet<FStruct>, TMap<K,FStruct>). For these:
    //   - propName carries the dotted path (e.g. "SaveSlotList[].MsTuneData.GP")
    //   - classAddr / className are the OWNING class (so Find Instances works)
    //   - fieldAddr is the LEAF FProperty* (so find_property_xrefs works)
    //   - propOffset is informational only (no single class-absolute address
    //     once the path crosses a container) — the UI gates Copy Offset / Freeze
    //     off this flag.
    //   - preview is never resolved (previewClassAddr stays 0 → Phase 2 skips).
    bool        isNested = false;
};

struct PropertySearchResult {
    int scannedClasses = 0;
    // Objects ACTUALLY walked. This used to be assigned the full GObjects count
    // before the loop started, so a search that stopped at the maxResults cap a
    // few percent in still reported the whole pool as scanned — and the UI printed
    // that as "scanned 1,204,338 objects". Measured on DumperTest 2026-08-14:
    // "3 matches from 8 classes (scanned 24445 objects)". (audit #5 D5/F4)
    int scannedObjects = 0;
    // The walk stopped early. `truncated` = the maxResults cap was reached (there
    // are more matches); `aborted` = Tot::Requested() fired (client gone/shutdown).
    // Without these the reply cannot be told from a complete search that found
    // everything there is — the exact shape behind four "the scan missed my field"
    // reports. Both are additive on the wire; a client that ignores them is
    // unchanged.
    bool truncated = false;
    bool aborted   = false;
    std::vector<PropertyMatch> results;
};

// Search for properties matching a keyword across all UClass objects.
// query: case-insensitive substring match on property name.
// typeFilter: optional list of property types (e.g. "FloatProperty"); empty = all types.
// gameOnly: skip engine packages (/Script/Engine, /Script/CoreUObject, etc.)
//
// Results are deduped by (definingClass, propName, offset) -- a field
// declared on AActor and inherited by 4823 children only emits one row,
// keyed by the defining class. The PropertyMatch.inheritedByCount
// records how many other classes share that inherited field.
//
// deep: when true, ALSO descend into StructProperty members and struct-typed
// container elements (TArray/TSet<FStruct>, TMap<K,FStruct>) so a field nested
// inside a struct/container (e.g. SaveSlotList[].MsTuneData.GP) becomes findable
// by name. Such matches set PropertyMatch.isNested = true and carry the dotted
// path in propName. Opt-in (default off) because the schema descent is slower
// and can surface many synthetic rows; the shallow direct-field search is
// unchanged when deep == false.
PropertySearchResult SearchProperties(
    const std::string& query,
    const std::vector<std::string>& typeFilter,
    bool gameOnly,
    int maxResults = 200,
    bool deep = false);

// Batched property search: walk GObjects + class fields ONCE and check
// every property against ALL queries. Returns one PropertySearchResult
// per query (in the same order as the input). Each query gets its own
// dedup index, per-query maxResults limit, and (optionally) per-query
// preview values.
//
// The big win: a 36-query sweep on a 4400-class game drops from
// ~42 sequential seconds (each call re-walks GObjects) to ~1.5 seconds
// (one shared walk; per-property keyword check is cheap).
//
// withPreviews=false skips the Phase-2 instance scan that resolves
// preview values for the wire output. The Interesting Properties tab
// (the primary consumer) doesn't show previews, so the default is off
// and we save another GObjects pass.
std::vector<PropertySearchResult> SearchPropertiesBatch(
    const std::vector<std::string>& queries,
    const std::vector<std::string>& typeFilter,
    bool gameOnly,
    int maxResultsPerQuery = 200,
    bool withPreviews = false);

// Batched class schema walk: invokes Ubel::WalkClassEx once per input
// address and returns results in the same order. The DLL implementation
// is deliberately a trivial loop — every element comes from the exact
// same WalkClassEx call the single-walk `walk_class` pipe command uses,
// so each ClassInfo is byte-identical to a single-call response. The
// optimisation is purely pipe round-trip + JSON serialisation
// amortisation: a 4000-class Full SDK export saves ~4000 × ~0.3ms of
// per-message overhead plus the per-call JSON envelope cost.
//
// Caller is responsible for chunking — a single batch carrying
// thousands of fully-walked classes would produce a multi-megabyte
// JSON payload, so the UI side fans out in ~200-class chunks.
// Note: ClassInfo is defined at global scope in Ubel.h (not inside the
// Ubel namespace), so the unqualified name is correct here.
std::vector<ClassInfo> WalkClassesBatch(const std::vector<uintptr_t>& addrs);

// Walk the SuperStruct chain upward from `classAddr` and return the
// highest-up class that still declares a property at `fieldOffset`.
// Algorithm: a class C declares the property iff
//   fieldOffset >= C.SuperStruct.PropertiesSize  (super doesn't have it)
//   fieldOffset <  C.PropertiesSize              (C does have it)
// If no super exists (UObject is the root), classAddr itself is the
// defining class.
//
// Used by SearchProperties dedup. Cap on chain depth (32) matches
// Ubel's WalkClass inherited-walk to avoid pathological cycles.
uintptr_t FindDefiningClass(uintptr_t classAddr, int32_t fieldOffset);

// === Game Class List ===

struct ClassListEntry {
    std::string className;
    uintptr_t   classAddr;
    std::string classPath;
    std::string superName;
    int32_t     propertyCount;
    int32_t     propertiesSize;
    int32_t     heuristicScore;   // Auto-ranked suspicion score (higher = more interesting for RE)
};

struct ClassListResult {
    int scannedObjects = 0;
    int totalClasses = 0;
    // True when the walk STOPPED at maxResults instead of reaching the end of
    // GObjects — i.e. this list is a page, not the pool. Same contract as
    // SearchResultSet::truncated. Callers that resolve a class BY NAME must
    // surface it: without it, a class sitting past the cap is indistinguishable
    // from one that does not exist, and every consumer reported the latter
    // (audit #5 X2).
    bool truncated = false;
    std::vector<ClassListEntry> results;
};

// List all UClass objects, optionally filtering out engine packages.
ClassListResult ListClasses(bool gameOnly, int maxResults = 5000);

// === Class-noise auto-detect (class-filter Phase 3) ===
//
// SAFE-by-construction classifier for the opt-in "auto-detect system classes"
// suggestion in the class-noise picker. Given a set of class names (from a
// result histogram), it resolves each to its UClass in GObjects and marks it
// "noise" iff EITHER (a) it lives in an engine package (/Script/Engine, /UMG,
// /Slate, /Niagara, /AudioMixer, … — IsEnginePackage), OR (b) its super-chain
// derives from a pure-engine LEAF base that structurally cannot hold gameplay
// save data (UserWidget/Widget, SoundBase, Texture, MaterialInterface,
// ParticleSystem, NiagaraSystem, AnimInstance). It NEVER uses class-name
// substrings and NEVER flags ActorComponent descendants (gameplay state lives
// there) — those are documented hard bans. The UI only PRE-TICKS the picker
// with the result (reversible), never auto-prunes.
struct NoiseClassVerdict {
    std::string className;
    bool        isNoise = false;
    std::string reason;   // human label of the matched rule (empty when not noise)
};

// Classify the given class names (one GObjects pass; unresolved names come back
// isNoise=false). Order mirrors the input; duplicates de-duped.
std::vector<NoiseClassVerdict> ClassifyNoiseClasses(const std::vector<std::string>& classNames);

// True if `classObj`'s super-chain (itself included) has an FName exactly equal
// to any entry in `baseNames`. Bounded 64-hop walk with a self-loop break (the
// reusable generalization of Edel::ClassifyBySuperChain). Pure read-only.
bool ClassDerivesFromAny(uintptr_t classObj, const std::unordered_set<std::string>& baseNames);

// True if `path` (a class full path from Ubel::GetFullName) is in a known UE
// engine/plugin package — the gate behind the "Game classes only" filter and the
// auto-detect package rule. Tolerant of GetFullName's "//Script/Engine/Class"
// double-leading-slash, '/'-separator format. Pure / string-only (unit-tested);
// header-inline so the lightweight DLL test can exercise it without linking the
// whole DLL.
// ============================================================
// Deep-container leaf coverage (audit #5 A4)
// ============================================================

/// Which container shape a leaf was reached through. Lives in the header so the
/// coverage predicate below can be unit-tested; the walker in Aura.cpp is the
/// only producer.
///
/// `Unknown` is FIRST, and that placement is the fix's own safety net. A leaf
/// struct that gains this member and forgets to initialise it value-initialises
/// to the zero enumerator — so if `Array` sat at 0, a missed wiring site would
/// silently mean "statically covered" and reproduce A4 with no compile error and
/// no failing test. Zero must mean "nobody said", and nobody-said is not covered.
enum class ContainerKind {
    Unknown = 0,  // never a valid producer value — see above
    Array,        // TArray.Data buffer, stride = inner element size
    Set,          // TSparseArray.Data buffer, stride = ComputeSetElementStride
    Map,          // TSparseArray.Data buffer, stride = ComputeSetElementStride(pair)
    Direct,       // not a container at all (the snapshot numeric-scope path)
};

/// Is this deep-walk leaf ALREADY reachable through Value Search's static scan
/// index, and therefore safe for the deep pass to skip?
///
/// The deep pass used to skip on `depth < 2` alone, i.e. "depth 1 is covered".
/// That is only true for ARRAYS: `collectStructArrayInner` is reached solely from
/// the ArrayProperty branch, so a struct-sided `TSet<FStruct>` or
/// `TMap<K, FStruct>` element is covered by neither the static index nor the deep
/// pass — an everyday `TMap<FName, FItemData>` inventory count was unfindable with
/// Deep ON *and* OFF.
///
/// `leafIsWholeElement` is `leafName.empty()`: the leaf IS the element (a leaf
/// container's element, or a scalar map side), which the static paths do cover
/// for every kind.
///
/// The sibling consumers already had the right shape and are what made the
/// asymmetry visible: the snapshot path tests `leafName.empty() && depth < 2`,
/// and the group scan uses `depth < 1`.
inline bool DeepLeafCoveredByStaticScanIndex(int depth, ContainerKind kind,
                                             bool leafIsWholeElement) {
    if (depth < 1) return true;    // the object's own direct fields
    if (depth > 1) return false;   // nothing static reaches past one level
    if (leafIsWholeElement) return true;
    return kind == ContainerKind::Array;
}

inline bool IsEnginePackage(const std::string& rawPath) {
    static const char* const kEnginePrefixes[] = {
        "/Script/Engine", "/Script/CoreUObject", "/Script/CoreOnline",
        "/Script/UMG", "/Script/Slate", "/Script/SlateCore", "/Script/InputCore",
        "/Script/EnhancedInput", "/Script/PhysicsCore", "/Script/NavigationSystem",
        "/Script/AIModule", "/Script/Niagara", "/Script/Paper2D",
        "/Script/CinematicCamera", "/Script/GameplayCameras", "/Script/MovieScene",
        "/Script/LevelSequence", "/Script/Landscape", "/Script/Foliage",
        "/Script/AnimGraphRuntime", "/Script/AudioMixer", "/Script/ChaosCloth",
        "/Script/ChaosSolverEngine", "/Script/ClothingSystemRuntimeNv",
        "/Script/GeometryCollectionEngine", "/Script/FieldSystemEngine",
        "/Script/ProceduralMeshComponent", "/Script/GameplayTags",
        "/Script/GameplayTasks", "/Script/GameplayAbilities", "/Script/PacketHandler",
        "/Script/PropertyAccess", "/Script/DeveloperSettings", "/Script/AssetRegistry",
        "/Script/MediaAssets", "/Script/HeadMountedDisplay",
    };

    // GetFullName emits engine paths as "//Script/Engine/Class" — a DOUBLE
    // leading slash with '/' separators (documented format quirk; see dev-log).
    // A strict prefix compare misses that ("//Script…" != "/Script…"), which
    // silently made gameOnly a no-op for EVERY engine class (and made the
    // class-noise auto-detect's package rule miss them too). Collapse the leading
    // slash run to a single '/'; the trailing-char check already accepts '/' as a
    // separator, so "/Script/Engine/Class" then matches correctly.
    size_t firstNonSlash = rawPath.find_first_not_of('/');
    if (firstNonSlash == std::string::npos) return false;   // empty / all slashes
    const std::string path = "/" + rawPath.substr(firstNonSlash);

    for (const auto* prefix : kEnginePrefixes) {
        const std::string pfx(prefix);
        if (path.compare(0, pfx.size(), pfx) == 0) {
            // Exact prefix followed by end-of-string, '/', or '.'.
            if (path.size() == pfx.size() || path[pfx.size()] == '/' || path[pfx.size()] == '.')
                return true;
        }
    }
    return false;
}

// CanonicalizeObjectPath — reduce every spelling of a UObject path to ONE form so
// they can be compared. Pure / string-only (unit-tested); header-inline for the same
// reason as IsEnginePackage above.
//
// Three spellings of the SAME object are in circulation, and this was measured on a
// live UE 5.4 title (Elliot, 2026-08-16), not assumed:
//   1. `Ubel::GetFullName` emits  "//Script/Engine/Actor"  — DOUBLE leading slash,
//      '/' between package and object. This is what our own DLL produces.
//   2. UE itself (and every doc, .CT and Lua caller) writes "/Script/Engine.Actor"
//      — single leading slash, '.' before the object name.
//   3. UE's fully-qualified form prepends the class: "Class /Script/Engine.Actor".
// Comparing (1) against (2) with == is false for every object in the process, which
// is exactly why find-by-path found nothing at all before build 3157.
//
// Canonical form = leading slash run collapsed to one, '.' and ':' (subobject
// separator) rewritten to '/', any leading "ClassName " qualifier dropped.
// Both examples above canonicalize to "/Script/Engine/Actor".
//
// Case is PRESERVED: UE FNames are case-insensitively *compared* but exactly cased,
// and every other name compare in this file (FindByName, ClassDerivesFromAny) is
// exact — a case-folding path compare here would be the only one in the module that
// disagrees with its siblings.
inline std::string CanonicalizeObjectPath(const std::string& raw) {
    // Drop a leading class qualifier ("Class /Script/Engine.Actor"). Object paths
    // never contain a space, so the text after the LAST space is the path.
    size_t start = raw.find_last_of(' ');
    start = (start == std::string::npos) ? 0 : start + 1;

    // Collapse the leading slash run (handles GetFullName's "//").
    size_t firstNonSlash = raw.find_first_not_of('/', start);
    if (firstNonSlash == std::string::npos) return std::string();  // empty / all slashes

    std::string out;
    out.reserve(raw.size() - firstNonSlash + 1);
    out.push_back('/');
    for (size_t i = firstNonSlash; i < raw.size(); ++i) {
        const char c = raw[i];
        // '.' = package→object, ':' = object→subobject. Both are path separators.
        out.push_back((c == '.' || c == ':') ? '/' : c);
    }
    return out;
}

// True when `raw` is written as a PATH rather than a bare object name — i.e. it
// carries a separator. Callers use this to decide whether a path resolve is worth
// attempting before falling back to the (much cheaper) bare-name match, so a plain
// "Actor" keeps its original single-pass cost.
inline bool LooksLikeObjectPath(const std::string& raw) {
    return raw.find('/') != std::string::npos ||
           raw.find('.') != std::string::npos ||
           raw.find(':') != std::string::npos;
}

// PathLeafName — the final segment of a path ("/Script/Engine.Actor" -> "Actor").
// Used as a CHEAP PRE-FILTER: comparing an object's FName against this costs one
// FName read, whereas building its full name walks the whole Outer chain. Only
// objects whose leaf name already matches are worth the expensive compare.
inline std::string PathLeafName(const std::string& raw) {
    const std::string canon = CanonicalizeObjectPath(raw);
    const size_t slash = canon.find_last_of('/');
    return (slash == std::string::npos) ? canon : canon.substr(slash + 1);
}

// IsReflectionMetaClass — true when a UObject's CLASS name denotes the reflection /
// type layer (UClass family, UFunction family, UScriptStruct/UEnum descriptors,
// UPackage) rather than a live gameplay instance. On UE4 (a priority target, where
// UProperty is still a UObject) property descriptors also sit in GObjects; their class
// name always ends in "Property" (IntProperty, ObjectProperty, StructProperty, …) so
// the suffix rule catches them. UE5 makes FProperty non-UObject, so the suffix never
// fires there. Powers the Object Tree "Instances only" server-side gate.
//
// MUST stay in sync with the C# mirror Helpers/ReflectionMetaClassifier. Exact-cased
// match (UE emits meta names exactly cased); pure / string-only so the lightweight DLL
// test can exercise it without linking the whole DLL.
inline bool IsReflectionMetaClass(const std::string& className) {
    if (className.empty()) return false;
    static const char* const kReflectionMetas[] = {
        // Class family (mirrors IsClassLikeMeta / DumpAllService.ClassLikeMetas)
        "Class", "BlueprintGeneratedClass", "AnimBlueprintGeneratedClass",
        "WidgetBlueprintGeneratedClass", "DynamicClass",
        // Function family (UFunction + delegate flavours)
        "Function", "DelegateFunction", "SparseDelegateFunction",
        // Struct / enum descriptors
        "ScriptStruct", "UserDefinedStruct", "Enum", "UserDefinedEnum",
        // Package
        "Package",
    };
    for (const char* m : kReflectionMetas)
        if (className == m) return true;
    // UE4 UProperty descriptors always end in "Property".
    static const std::string kProp = "Property";
    if (className.size() >= kProp.size() &&
        className.compare(className.size() - kProp.size(), kProp.size(), kProp) == 0)
        return true;
    return false;
}

// SplitLowerKeywords — split a raw filter string into non-empty, lowercased terms on
// ASCII whitespace. Mirrors the C# ObjectTreeFilter.SplitTerms so the server-side top
// Search tokenizes identically to the client-side filter. Pure — DLL-test exercisable.
inline std::vector<std::string> SplitLowerKeywords(const std::string& raw) {
    std::vector<std::string> terms;
    std::string cur;
    for (char ch : raw) {
        unsigned char uc = static_cast<unsigned char>(ch);
        if (uc == ' ' || uc == '\t' || uc == '\n' || uc == '\r' || uc == '\f' || uc == '\v') {
            if (!cur.empty()) { terms.push_back(cur); cur.clear(); }
        } else {
            cur.push_back(static_cast<char>(std::tolower(uc)));
        }
    }
    if (!cur.empty()) terms.push_back(cur);
    return terms;
}

// MatchesAllKeywords — term-level AND, field-level OR: every term in `lowerTerms`
// (already lowercased by SplitLowerKeywords) must be a substring of at least one of the
// two fields. Fields are lowercased on the fly. An empty term list matches everything.
// Mirrors C# ObjectTreeFilter.MatchesAllTerms. Pure — DLL-test exercisable.
inline bool MatchesAllKeywords(const std::vector<std::string>& lowerTerms,
                               const std::string& a, const std::string& b) {
    if (lowerTerms.empty()) return true;
    std::string la = a, lb = b;
    for (auto& c : la) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
    for (auto& c : lb) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
    for (const auto& t : lowerTerms) {
        if (la.find(t) == std::string::npos && lb.find(t) == std::string::npos)
            return false;
    }
    return true;
}

// === Snapshot source-level noise classification (header-inline, pure) ===
//
// These power the Snapshot "Auto detect Engine/System noise" option, which skips
// pure engine/system classes BEFORE they enter the capture (vs the post-capture
// Noise Picker that only filters the finished snapshot). They mirror the rules of
// Aura::ClassifyNoiseClasses (the result-list auto-detect) so both surfaces agree.
// Kept header-inline + pure (no game-memory reads) so the lightweight DLL test can
// exercise the precedence + set membership without linking the whole DLL.

// Pure-engine LEAF bases whose instances structurally cannot hold gameplay save
// data (their reflected FName has no U/A prefix). DELIBERATELY excludes
// ActorComponent (gameplay HP/MP lives in components) — a documented hard ban.
// Single source of truth shared by ClassifyNoiseClasses + the snapshot skip.
inline const std::unordered_set<std::string>& SnapshotEngineNoiseBases() {
    static const std::unordered_set<std::string> kBases = {
        "UserWidget", "Widget", "SoundBase", "Texture", "MaterialInterface",
        "ParticleSystem", "NiagaraSystem", "AnimInstance",
    };
    return kBases;
}

// Gameplay bases that must NEVER be source-skipped from a snapshot: their
// instances are the usual carriers of the values users hunt — a Pawn's X/Y/Z, HP
// in components / GAS AttributeSets (kept via ActorComponent), and the
// controller/player-state graph. This guardrail wins over every noise rule below
// because a CAPTURE-time skip is irreversible (the object never enters the store),
// so "Auto detect Engine/System noise" can never drop a player Pawn.
inline const std::unordered_set<std::string>& SnapshotGameplayKeepBases() {
    static const std::unordered_set<std::string> kBases = {
        "Actor", "ActorComponent", "Pawn", "Character",
        "Controller", "PlayerState", "GameInstance",
    };
    return kBases;
}

// Pure precedence for the snapshot source-level noise decision, factored out so
// it is unit-testable without a live process (the derives/package predicates that
// produce these booleans read game memory). The gameplay guardrail wins: a
// keep-base-derived class is never noise. Otherwise: an engine /Script package OR
// an engine leaf base => noise.
inline bool DecideSnapshotNoise(bool derivesFromKeepBase,
                                bool isEnginePackage,
                                bool derivesFromNoiseBase) {
    if (derivesFromKeepBase) return false;
    return isEnginePackage || derivesFromNoiseBase;
}

// === All-Functions Enumeration (Interesting Functions Finder) ===

// Lightweight per-function metadata returned by EnumerateAllFunctions.
// Deliberately omits parameter details — the UI only needs enough to
// score + render a row; full param walk happens on-demand when the
// user picks a function (existing CMD_WALK_FUNCTIONS path).
struct AllFunctionEntry {
    std::string className;
    uintptr_t   classAddr   = 0;
    std::string superName;
    std::string classPath;        // full path for game_only / package filter
    std::string funcName;
    uintptr_t   funcAddr    = 0;
    uint32_t    functionFlags = 0;
    uint8_t     numParms    = 0;
    uint16_t    parmsSize   = 0;
};

struct AllFunctionsResult {
    int scannedObjects   = 0;     // GObjects count walked
    int scannedClasses   = 0;     // UClasses considered (post game-only filter)
    // Emitted functions. Identical to entries.size() by construction (both the
    // outer and inner cap tests fire before the push), so it is NOT an honest
    // pool total and must never be read as one — the pool size cannot be known
    // without paying the WalkFunctions cost for every remaining class, which is
    // exactly what the cap exists to avoid. `truncated` below is the honest
    // signal. (audit #5 Z8)
    int totalFunctions   = 0;
    // The walk stopped early, so `entries` is a PAGE, not the pool:
    //   truncated = hit maxEntries       (more functions exist, unseen)
    //   aborted   = Tot::Requested()     (client gone / shutdown mid-walk)
    // Without these a capped scan is indistinguishable from an exhaustive one,
    // and the Console panel turns it into a positive claim about the game
    // ("No UFUNCTION(exec) commands found in this game"). (audit #5 Z8)
    bool truncated       = false;
    bool aborted         = false;
    std::vector<AllFunctionEntry> entries;
};

// Walk every UClass in GObjects, enumerate its UFunctions, and return a
// flat list of {class, function, addr, flags, paramsSize} tuples.
//
// gameOnly: when true, skips classes whose path matches IsEnginePackage
//   (/Script/Engine, /Script/CoreUObject, etc.) -- typically reduces
//   the result set ~5x for shipping games.
// maxEntries: hard cap to keep the pipe payload bounded. Defaults to
//   100k (well above the ~50k-function ceiling of typical UE games).
//
// Cost is O(GObjects + sum(WalkFunctions)) with a single pass over
// GObjects to identify UClasses, plus one Ubel::WalkFunctions per
// class. Typical 1M-object game completes in 2-10s. UI should run
// this on a worker task with a progress indicator.
AllFunctionsResult EnumerateAllFunctions(bool gameOnly, int maxEntries = 100000);

// === Property Bytecode Cross-Reference (Path 1: Kismet bytecode static xref) ===
//
// Finds which UFunctions reference a target FProperty by scanning each
// function's UStruct::Script (Kismet bytecode) for the FProperty* pointer.
// Variable-access opcodes (EX_InstanceVariable 0x01 / EX_LocalVariable 0x00 /
// EX_Context inner) embed the live, fixed-up FProperty* directly, so a raw
// byte-scan for the 8-byte pointer value is complete AND version-agnostic
// (no opcode table; the 0x00/0x01 preceding byte is only a confidence/kind
// hint). The target address is the field's FProperty* (UProperty* on UE4
// <4.25) — exactly what WalkClassEx / SearchProperties report as fieldAddr.
//
// COVERAGE: Blueprint / script functions only. Native (FUNC_Native) functions
// have empty Script — their property access is in compiled machine code and is
// invisible here. The UI MUST surface this (mirror the value-search caveat
// contract); complementary to the CE access-breakpoint approach which covers
// native but drowns on shared/inlined code.
struct PropertyXref {
    uintptr_t   funcAddr       = 0;
    std::string funcName;
    std::string funcFullName;
    uintptr_t   ownerClassAddr = 0;
    std::string ownerClassName;
    int32_t     occurrences    = 0;    // hit count within this function's bytecode
    int32_t     writeCount     = 0;    // of those, how many are assignment destinations
                                       // (EX_Let* LHS). reads = occurrences - writeCount.
                                       // Best-effort: wrapped LHS (Other.Field/Struct.Member/
                                       // Arr[i] = x) is not detected and counts as a read.
    std::string kind;                  // "instance" (0x01) / "local" (0x00) / "ref"

    // v2a: for hits inside a Blueprint ubergraph (ExecuteUbergraph_*), the BP
    // event(s) whose entry offset precedes the reference (comma-joined, distinct).
    // Empty for non-ubergraph functions. Best-effort: shared sub-graphs reached
    // from multiple events can mis-attribute (nearest-preceding heuristic).
    std::string eventName;
    // Transient (not serialised): byte offsets of the FProperty* within the
    // ubergraph Script, used by the post-scan attribution pass then cleared.
    std::vector<int32_t> ubergraphOffsets;
};

struct PropertyXrefStats {
    int32_t functionsScanned    = 0;   // objects whose class name == "Function"
    int32_t functionsWithScript = 0;   // of those, Script.Num > 0
    int32_t objectsTotal        = 0;   // GObjects count
    int64_t durationMs          = 0;
    bool    deadlineHit         = false;
};

struct PropertyXrefResult {
    std::vector<PropertyXref> xrefs;
    PropertyXrefStats         stats;
};

// Scan every UFunction's bytecode for references to `propAddr` (an FProperty*).
// gameOnly skips functions whose owning class is an engine package. Parallel
// GObjects walk (relies on Ubel's mutex-guarded caches, build 792).
PropertyXrefResult FindPropertyXrefs(uintptr_t propAddr, bool gameOnly,
                                     int32_t maxResults = 200);

// === Class-level reflection xref: functions taking a class as a param ===
//
// Which UFunctions declare `classAddr` (a UClass* / UScriptStruct*) as a direct
// parameter or return value. Complements FindPropertyXrefs (per-field bytecode,
// which misses functions taking a whole class by value/ref) — pure reflection
// over each function's param chain, so it ALSO catches native (FUNC_Native)
// functions whose Script is empty. Reuses the PropertyXref result shape: `kind`
// = "param"/"return", `occurrences` = matching param count, `writeCount` = 0.
// v1: direct params only (array/map element types are a future enhancement).
PropertyXrefResult FindFunctionsByClassParam(uintptr_t classAddr, bool gameOnly,
                                             int32_t maxResults = 200);

// Resolve a UFunction's native code entry point (UFunction->Func) — the .text
// address to disassemble (native funcs) or the interpreter (BP funcs). 0 if the
// Func offset isn't detected yet or the slot isn't a code pointer. See Aura.cpp.
uintptr_t GetFunctionCodeAddr(uintptr_t funcAddr);

// === Reverse edge: function -> properties it reads/writes ===
//
// Given ONE UFunction, parse its Kismet bytecode and list every FProperty it
// references, with read/write classification. Opcode-anchored scan: for each
// variable/struct-member/persistent-frame opcode, the following 8-byte pointer
// is validated via Ubel::ResolvePropertyNameType (type must contain "Property")
// — so non-property pointers (UFunction*, literals) are rejected. Writes are
// detected with the same EX_Let* heuristic as FindPropertyXrefs.
//
// Blueprint/script functions only (native functions have empty bytecode).
// Best-effort: a write whose LHS is wrapped (Other.Field / Struct.Member /
// Arr[i] = x) may be reported as a read.
struct FunctionPropRef {
    uintptr_t   propAddr    = 0;
    std::string name;
    std::string type;          // "FloatProperty" / "StructProperty" / ...
    int32_t     occurrences = 0;
    int32_t     writeCount  = 0;   // reads = occurrences - writeCount
    std::string scope;             // "instance" (class member) / "local" (function
                                   // local/param) / "default" / "sparse" / "struct" /
                                   // "frame". UI defaults to instance-only so BP
                                   // compiler temporaries (CallFunc_*) don't drown it.
    // === Path 2 (native disasm) fields ===
    int32_t     offset     = -1;   // class-member offset the access mapped to (-1 = n/a)
    std::string confidence;        // "high" (base proven `this`) / "low" — disasm only;
                                   // empty for the exact bytecode path.
};

struct FunctionPropRefResult {
    int32_t scriptBytes = 0;       // UStruct::Script.Num (0 = native / empty)
    std::vector<FunctionPropRef> refs;
    // "bytecode" = Path 1 Kismet scan (exact). "disasm" = Path 2 native x64
    // disassembly (heuristic — see FunctionPropRef::confidence). "none" =
    // native but analysis unavailable (Func offset unresolved / unreadable).
    std::string method;
    int32_t unmappedAccesses = 0;  // disasm: [reg+off] hits with no matching property
};

FunctionPropRefResult WalkFunctionPropertyRefs(uintptr_t funcAddr);

// === Sparse Delegate Storage Walker ===
//
// Resolves bindings for a MulticastSparseDelegateProperty. The field on a
// UObject only stores `FSparseDelegate { uint8 bIsBound; }` — actual binding
// list lives in CoreUObject's static
//   FSparseDelegateStorage::SparseDelegates :
//     TMap<UObjectBase*, TMap<FName, TSharedPtr<TMulticastScriptDelegate>>>
//
// This walker locates the static via Genau::FindSparseDelegateStorage(),
// linearly scans the outer TSparseArray for the matching owner key, then
// scans the inner TSparseArray for the matching FName key, derefs the
// TSharedPtr, and walks the InvocationList.
//
// Layout support: the outer key is a raw UObjectBase* on UE 5.x AND on UE 4.27
// (PDB-verified). Rather than trust a version number, the walker probes the first
// occupied outer key: if it does not look like a userspace pointer it returns
// supported=false, so an FObjectKey-keyed build (4.23-4.26 is unverified) fails safe.
struct SparseDelegateBinding {
    int32_t     objectIndex    = 0;   // raw FWeakObjectPtr.ObjectIndex
    int32_t     serialNumber   = 0;   // raw FWeakObjectPtr.SerialNumber
    uintptr_t   targetObj      = 0;   // resolved live UObject* (0 if stale)
    std::string targetName;           // resolved target name (empty if stale)
    std::string targetClassName;      // resolved target class name (empty if stale)
    std::string functionName;         // FName of bound function
};

struct SparseDelegateResult {
    bool resolved   = false;     // AOB worked + walker ran (may have 0 bindings)
    bool supported  = true;      // false = current UE version not supported
    bool ownerFound = false;     // outer key matched
    bool nameFound  = false;     // inner key matched
    std::vector<SparseDelegateBinding> bindings;
};

// Walk FSparseDelegateStorage to enumerate bindings for `fieldName` on
// `ownerObj`. Returns immediately if the AOB resolver hasn't found the
// static (resolved=false) or if UE version isn't supported.
SparseDelegateResult WalkSparseDelegateBindings(uintptr_t ownerObj,
                                                 const std::string& fieldName,
                                                 int32_t maxBindings = 64);

// === Value Search (CE-style First Scan / Next Scan workflow) ===
//
// Walks GObjects + UProperty metadata to find every UPROPERTY-declared
// field matching `dt` whose typed value satisfies the predicate. Each
// candidate is enriched with owning UObject + class + defining-class +
// field metadata via the same machinery FindByAddress / SearchProperties
// already use.
//
// Native C++ fields (non-UPROPERTY) are NOT visible to this scan — the
// UI is contractually required to surface this limitation. See
// `project_value_search_caveats` memory for the rationale and the
// TArray<T> crash-risk plan that gates the v2 expansion past primitives.

struct ValueScanStats {
    int32_t scannedClasses = 0;   // Unique classes with matching-type fields
    int32_t scannedObjects = 0;   // UObject instances iterated
    int64_t durationMs     = 0;
    bool    deadlineHit    = false;
};

struct ValueScanResult {
    std::vector<Radar::Candidate>       candidates;
    // Shared metadata pools the candidates index into (V3-A). Moved into
    // the Radar::Session alongside the candidates by SessionManager::Begin.
    std::vector<Radar::FieldDescriptor> descriptors;
    std::vector<Radar::InstanceRecord>  instances;
    ValueScanStats                          stats;
};

// First Scan: walk every UPROPERTY field matching `dt` across all
// UObject instances, applying the (st, targetBytes, target2Bytes,
// roundMode) predicate. Skips UClass meta-objects -- only live
// instances + CDOs are scanned.
//
// Numeric path: targetBytes / target2Bytes carry the predicate target.
// Vector path (FVector/FRotator/FTransform): same buffers, 12 bytes each.
// String path (FString/FName/FText): targetBytes/target2Bytes ignored;
// targetString carries the user's search string and caseSensitive
// controls the comparison.
//
// Valid scan types for first scan:
//   Numeric / Vector: Exact / Bigger / Smaller / Between.
//   String:           Exact / Contains / StartsWith / EndsWith.
// Pipe handler rejects invalid (dt, st) combinations upstream so the
// scan engine doesn't have to second-guess.
//
// `roundMode` (Round/Trunc/Ceil) only affects Float/Double and vector
// comparisons — it reduces each float to the integer the game DISPLAYS before
// comparing (so "338" finds a real 337.6 under Round). Integer + string types
// are reduce-invariant; the mode only reaches them when a fractional target is
// coerced to an integer at parse time (BuildNumericTargets / ParseValueBytes).
//
// Returns at most maxResults candidates; the scan also bails on a 15s
// deadline (stats.deadlineHit fires when this happens). Used by the
// Value Search tab.
//
// Multi-numeric meta path (dt == NumericNoByte): targetBytes /
// target2Bytes are ignored. Every word/dword/qword/float/double field
// is accepted; each is compared using its OWN resolved DataType against
// the matching entry in `multiTargets` (and `multiTargets2` for
// Between). A field whose width can't represent the value (no matching
// entry) is skipped. `multiTargets` must be non-null for this path.
// Shared defaults for the single- and group-value scans (keep ScanForValue and
// ScanForValueGroup in lockstep): 100000-candidate ceiling and a 15s wall-clock budget.
constexpr int32_t kValueScanDefaultMaxResults = 100000;
constexpr int32_t kValueScanDefaultDeadlineMs = 15000;

ValueScanResult ScanForValue(
    Radar::DataType dt,
    Radar::ScanType st,
    const uint8_t*      targetBytes,
    const uint8_t*      target2Bytes,
    bool                gameOnly,
    int32_t             maxResults    = kValueScanDefaultMaxResults,
    Radar::RoundMode    roundMode     = Radar::RoundMode::Round,
    const std::string&  targetString  = "",
    bool                caseSensitive = false,
    const Radar::NumericTargetSet* multiTargets  = nullptr,
    const Radar::NumericTargetSet* multiTargets2 = nullptr,
    // When false, the GObjects walk runs single-threaded (no worker threads
    // spawned) so concurrent cross-thread reads can't trip a game's anti-tamper.
    // Default true = full parallel scan (fast). Exposed via the pipe `parallel`
    // field on begin_value_scan and the Value Search "Parallel scan" toggle.
    bool                parallel      = true,
    // When true (default), each object's fixed-width leaf fields are read in ONE
    // body read (per-thread reused buffer) instead of one SEH read per field —
    // fewer reads + better locality on the scattered GObjects walk. Falls back
    // to per-field reads on a faulting batch read or when the class isn't a good
    // batch candidate. Exposed via the pipe `batch_read` field + the Value
    // Search "Batch read" toggle. Strings / container data are always direct.
    bool                batchRead     = true,
    // Opt-in (default off): force the recursive deep-container leaf pass for EVERY
    // class, not only those whose struct-array elements own containers (the auto
    // `needsDeepWalk` heuristic). Reaches values buried in deeply-nested containers
    // the heuristic misses. Heavier per object — exposed via the "Deep" toggle.
    bool                deep          = false,
    // Native-C (P1, opt-in, default off): ALSO scan each object's UNMANAGED holes
    // — the byte ranges within [UObject header, class PropertiesSize) that no
    // reflected property covers — for the requested numeric value, interpreting
    // the raw bytes at the user's width. Finds native (non-UPROPERTY) C++ members
    // (HP/MP/...). Numeric/multi-numeric dt only (skipped for string/vector/bool).
    // Intentionally noisy on first scan — pair with newestFirst + Next-Scan refine.
    // See docs/native-c-value-scan-spec.md.
    bool                nativeC       = false,
    // Stride (1/2/4/8, default 4) for sliding within each hole when nativeC is on.
    int32_t             nativeAlign   = 4,
    // Walk GObjects high-index-first so that when results hit maxResults the
    // SURVIVORS are the most-recently-allocated instances (just-spawned pawns)
    // rather than low-index CDOs/templates. Default false = ascending (oldest
    // first). Recommended alongside nativeC (the UI couples the two). Applies to
    // the whole scan (reflected + native), affecting only which matches survive
    // truncation, not which exist.
    bool                newestFirst   = false,
    // Scan deadline in milliseconds (default 15000 = 15s). When the GObjects walk
    // exceeds this wall-clock budget the scan bails early (stats.deadlineHit fires)
    // and returns whatever matched so far. Exposed via the pipe `deadline_ms` field
    // + the Value Search "Timeout" slider (10-60s) so huge games that don't finish
    // in 15s can be given a longer budget. <= 0 falls back to the 15s default.
    int32_t             deadlineMs    = kValueScanDefaultDeadlineMs,
    // Pre-filter "Auto detect Engine/System noise" (opt-in, default OFF here; UI
    // checkbox also defaults unchecked). When true, pure engine/system classes are
    // skipped at the SOURCE (before the per-object field walk) so their instances
    // never enter the candidate set — a looser, heuristic complement to the exact
    // post-scan class picker. A gameplay guardrail force-keeps Actor/Pawn/Character/
    // ActorComponent/Controller/PlayerState/GameInstance-derived classes (see
    // SnapshotGameplayKeepBases), so a player Pawn's X/Y/Z and HP/MP in components /
    // AttributeSets are NEVER source-skipped. Reuses the snapshot IsSnapshotNoiseClass
    // verdict so all surfaces agree. Locked at First Scan; refine reuses survivors.
    bool                preFilterNoise = false);

// Refine an existing candidate vector in place: re-read each
// candidate's bytes (or string, for FString/FName/FText DataTypes),
// apply the predicate, prune entries that no longer match. For
// prev-value scan types (Changed / Unchanged / Increased / Decreased)
// the candidate's prevValue/prevStr snapshot is used in place of the
// targeted inputs. Snapshots are updated to the latest-observed
// state on survivors so the NEXT prev-value refine compares against
// what was seen during THIS refine -- standard CE Next Scan semantics.
//
// Multi-numeric meta path (dt == NumericNoByte): each candidate's
// concrete DataType is re-resolved from its stored fieldType; targeted
// predicates compare against `multiTargets`/`multiTargets2` (matched by
// that width), prev-value predicates against the candidate's prevValue.
ValueScanStats RefineCandidates(
    Radar::DataType                          dt,
    Radar::ScanType                          st,
    const uint8_t*                               targetBytes,
    const uint8_t*                               target2Bytes,
    std::vector<Radar::Candidate>&           candidates,
    const std::vector<Radar::FieldDescriptor>& descriptors,
    // audit #5 A11 — needed to re-derive a container HEADER address
    // (instanceAddr + descriptor.fieldOffset) so a container-element candidate can
    // be re-anchored instead of re-read at a possibly stale absolute address.
    const std::vector<Radar::InstanceRecord>&  instances,
    Radar::RoundMode                             roundMode     = Radar::RoundMode::Round,
    const std::string&                           targetString  = "",
    bool                                         caseSensitive = false,
    const Radar::NumericTargetSet*           multiTargets  = nullptr,
    const Radar::NumericTargetSet*           multiTargets2 = nullptr);

// ------------------------------------------------------------------
// Multiple values group scan (build 1276). Object-aware "group scan": find
// objects (blocks) that SIMULTANEOUSLY hold ALL of N user values at DISTINCT
// numeric-property offsets, in any order. The pure SDR match is
// Orden::MatchGroup; this layer enumerates each object's numeric leaves
// (direct fields + depth-capped StructProperty descent, mirroring
// ScanForValue's reach — numeric containers are P3) and persists per-slot
// convergence lists so a refine can re-read the located offsets.
//
// Runs single-threaded (mirrors CaptureSnapshotChunk): group result sets are
// small by construction (the AND across slots is highly selective), so the
// parallel scan machinery isn't warranted for P1. Honors Tot::Requested() + a
// 15s deadline + maxResults.
struct GroupScanResult {
    std::vector<Radar::GroupCandidate>  candidates;
    std::vector<Radar::FieldDescriptor> descriptors;  // shared via GroupSlotMatch::descriptorIdx
    std::vector<Radar::InstanceRecord>  instances;     // shared via GroupCandidate::instanceIdx
    ValueScanStats                      stats;
};

// First scan. `slots` carry the pre-parsed per-slot targets (caller enforces
// the 2..4 count). A block becomes a candidate only when Orden::MatchGroup
// finds a System of Distinct Representatives across all slots. The leaf
// enumeration scope is derived from the slots (1-byte fields are read only
// when a slot wants them) so it stays lean by default.
// `deep` (opt-in, default off): additionally treat each numeric CONTAINER as its
// own block — a numeric TArray/TSet's elements, or each struct-array/map element's
// inner numeric fields — via the shared recursive WalkContainerLeaves walker, so a
// group hidden inside a deeply-nested container (e.g. SaveSlotList[1].MsTuneData.
// MsTunes[0].WeaponTuneList[0].Tunes[N]) is found. Matches WITHIN one array/element
// (the user's "array as a block" rule). Bounded by the same depth/element caps as
// snapshot capture; only runs for objects that actually own containers.
// `crossObject` (P4, opt-in, default off): merge the numeric leaves of the
// sub-objects each actor OWNS (its components + a GAS ASC's SpawnedAttributes,
// reached via EnumerateOutgoingObjectPtrs and gated by an Outer-chains-back test)
// INTO the actor's own block, so a group whose values are distributed across
// {actor, components, AttributeSets} is matched. Ownership + value driven, NOT
// class-name driven (selectivity comes from the value AND). See group-value-scan-spec.md §3.2.
GroupScanResult ScanForValueGroup(
    const std::vector<Radar::SlotSpec>& slots,
    bool                                gameOnly,
    int32_t                             maxResults  = kValueScanDefaultMaxResults,
    bool                                deep        = false,
    bool                                crossObject = false,
    // Native-C (P2, opt-in, default off): also fold each object's unmanaged-hole
    // leaves (non-UPROPERTY bytes within [header, PropertiesSize)) into its block,
    // so a group including a native value matches. Object block only (never deep);
    // EMIT-ON-MATCH (a raw leaf is kept only when its bytes satisfy a slot), bounded
    // to <= 64 matching raw leaves per object. See native-c-value-scan-spec.md §7.
    bool                                nativeC     = false,
    // Walk GObjects newest-first (high index → low) so a 15s-deadline truncation on
    // a huge game keeps the most-recently-allocated objects (just-spawned UI/actors
    // holding native values) instead of low-index CDOs/templates. The UI couples this
    // on with native-C. Default false (ascending).
    bool                                newestFirst = false,
    // Scan deadline in milliseconds (default 15000 = 15s). When the group walk
    // exceeds this budget it bails early (stats.deadlineHit fires) and returns the
    // matches found so far. Exposed via the pipe `deadline_ms` field + the Value
    // Search "Timeout" slider (10-60s). <= 0 falls back to the 15s default.
    int32_t                             deadlineMs  = kValueScanDefaultDeadlineMs,
    // Pre-filter "Auto detect Engine/System noise" (opt-in, default OFF; UI checkbox
    // defaults unchecked). When true, pure engine/system classes are skipped at the
    // SOURCE (before each object's leaf enumeration) so they never enter the group
    // candidate set. Same gameplay guardrail as ScanForValue / snapshot capture
    // (Actor/Pawn/component/... force-kept via SnapshotGameplayKeepBases), so a
    // player Pawn / its components / AttributeSets are never source-skipped.
    bool                                preFilterNoise = false,
    // How many satisfying leaves to KEEP PER SLOT on each object. This is NOT a
    // performance knob -- it decides what a later Changed/Decreased refine can re-read,
    // because the kept list IS the refine's input. Leaves arrive in field-declaration
    // order (base class first), so too small a value silently excludes a derived class's
    // OWN fields behind its base class's: at the old default of 8 every AActor stored
    // just PrimaryActorTick/CustomTimeDilation, and a Changed refine pruned every
    // candidate to zero. Raise it for objects with unusually many numeric fields. The
    // scan WARNs when it truncates rather than dropping the extras silently.
    int                                 perSlotCap = Orden::kDefaultPerSlotCap);

// Next scan (P1: exact per slot). Re-reads each candidate's per-slot
// convergence offsets, keeps those still equal to the slot's NEW target,
// updates prevValue, and drops the candidate when any slot empties OR no
// distinct cross-slot assignment survives. `slots` carry the NEW targets.
ValueScanStats RefineGroupCandidates(
    const std::vector<Radar::SlotSpec>&        slots,
    std::vector<Radar::GroupCandidate>&        candidates,
    const std::vector<Radar::FieldDescriptor>& descriptors,
    const std::vector<Radar::InstanceRecord>&  instances);

// ------------------------------------------------------------------
// Numeric type-family filter (build 1600+). An ORTHOGONAL narrowing applied
// ON TOP of the numericScope meta type (NumericNoByte / NumericAll): the scope
// decides which widths are eligible, the family decides whether to keep the
// integer leaves, the float leaves, or both. Cuts a huge game's snapshot DB at
// the source when the hunt is type-specific (HP/MP/coords -> floats; counts/
// flags/IDs -> integers). Default Any keeps every eligible numeric (the prior
// behaviour). Bool is never in a numeric meta scope, so it is unaffected.
enum class NumericFamily : uint8_t {
    Any          = 0,   // keep every eligible numeric (integer + float/double)
    IntegersOnly = 1,   // keep Int8/16/32/64 + UInt8/16/32/64; drop Float/Double
    FloatsOnly   = 2,   // keep Float/Double; drop every integer width
};

// Pure (header-inline so the DLL test can link it): does a concrete per-field
// DataType pass the family filter? Float/Double are the "float" family; every
// other numeric meta member (Int8..UInt64) is the "integer" family. Any keeps
// all. Non-numeric dt (string/vector/bool) never reaches here in the snapshot
// path, so they default to kept under Any and dropped under either narrowing —
// callers only call this for numeric leaves.
inline bool NumericDataTypeInFamily(Radar::DataType dt, NumericFamily fam) {
    if (fam == NumericFamily::Any) return true;
    const bool isFloat = (dt == Radar::DataType::Float || dt == Radar::DataType::Double);
    return fam == NumericFamily::FloatsOnly ? isFloat : !isFloat;
}

// Parse the wire string ("Any" / "IntegersOnly" / "FloatsOnly"); unknown -> Any.
inline NumericFamily ParseNumericFamily(const std::string& s) {
    if (s == "IntegersOnly") return NumericFamily::IntegersOnly;
    if (s == "FloatsOnly")   return NumericFamily::FloatsOnly;
    return NumericFamily::Any;
}

// Snapshot capture (experimental — Phase A1a). A type-agnostic, streamed
// capture of every numeric UPROPERTY of every (scoped) UObject, used by the
// UI to persist snapshots for diff / SPC / pivot. Stateless cursor
// pagination (mirrors GetCount/GetByIndex + get_object_list): each chunk
// walks [offset, offset+limit) GObjects indices. Reuses Ubel::WalkClassEx
// (cached) + Radar::SelectSnapshotNumericFields. Array elements are
// captured in Phase A1b.
// ------------------------------------------------------------------
struct SnapshotField {
    std::string name;
    int32_t     offset = 0;
    std::string type;       // "FloatProperty" / "IntProperty" / ... (declared type)
    std::string hex;        // little-endian raw bytes, hex (no 0x prefix)
};

// One element of a struct-array (Phase A1b). Carries an inner-key (e.g.
// FCargoSlot.ItemID = "Fuel") so the same logical slot joins across snapshots
// regardless of array reordering, plus the element's numeric inner fields.
struct SnapshotArrayElement {
    int32_t     index = 0;         // element position (fallback key when keyName empty)
    std::string keyName;           // inner-key field name (e.g. "ItemID"); "" if none
    std::string keyValue;          // rendered inner-key value (e.g. "Fuel" / "42")
    std::vector<SnapshotField> fields;  // numeric inner fields (e.g. Quantity)
};

struct SnapshotArray {
    std::string field;             // owning ArrayProperty name (e.g. "Cargo")
    std::vector<SnapshotArrayElement> elements;
};

struct SnapshotObject {
    int32_t     index = -1;        // GObjects index (stable in-session join key)
    uintptr_t   addr  = 0;         // session-local; for CE export handoff
    std::string name;              // FName (numeric suffix normalised UI-side)
    std::string className;         // owning UClass short name
    std::string outerClassName;    // immediate outer's UClass name (loose join)
    std::string path;              // Ubel::GetFullName (cross-session identity)
    std::vector<SnapshotField> fields;
    std::vector<SnapshotArray>  arrays;   // struct-array inner-key capture (A1b)
};

struct SnapshotChunkResult {
    int32_t total   = 0;   // GObjects count
    int32_t scanned = 0;   // indices iterated this chunk (advance offset by this)
    int64_t walkMs  = 0;   // Phase-0 telemetry: parallel walk+merge wall-time for this chunk
    std::vector<SnapshotObject> objects;  // only objects with >=1 numeric field
};

// Capture a chunk of objects [offset, offset+limit) with their numeric
// scalar UPROPERTY values + struct-array element inner fields (inner-key
// capture). gameOnly skips engine-package classes. numericScope must be a
// multi-numeric meta type (NumericNoByte default / NumericAll); a non-meta
// type captures nothing. arrayCap bounds elements captured per struct array.
//
// captureNativeC (P3, opt-in, default off): ALSO append each object's
// unmanaged-hole guesses (non-UPROPERTY raw bytes interpreted via the Guess-What
// engine, normalized to canonical property types, Pointer/Padding dropped) as
// synthetic "<raw@0xNN>" fields, so the snapshot carries native values for SPC
// diff / Class Pivot. See native-c-value-scan-spec.md §8.
//
// skipNoiseClasses ("Auto detect Engine/System noise", opt-in, default off here
// for back-compat / UI default ON): when true, pure engine/system classes are
// skipped BEFORE the costly per-field walk so they never enter the snapshot —
// cutting capture time + DB size at the source. A gameplay guardrail force-keeps
// Actor/component/Pawn-derived classes (see SnapshotGameplayKeepBases), so a
// player Pawn's X/Y/Z is never dropped. Single-pass: no histogram pre-scan.
//
// family (build 1600+, default Any): orthogonal type-family narrowing applied to
// EVERY numeric leaf (top-level scalar, struct-array element, Native-C raw hole).
// IntegersOnly drops Float/Double; FloatsOnly drops every integer width. Cuts the
// DB at the source for type-specific hunts (floats = HP/MP/coords; integers =
// counts/flags/IDs) without touching the shared Radar DataType machinery.
SnapshotChunkResult CaptureSnapshotChunk(int32_t offset, int32_t limit,
                                         bool gameOnly,
                                         Radar::DataType numericScope,
                                         int32_t arrayCap = 256,
                                         bool captureNativeC = false,
                                         bool skipNoiseClasses = false,
                                         NumericFamily family = NumericFamily::Any);

// === Path-scoped cycle guard (audit A3, build 3168) ===
//
// A recursive struct walk needs to refuse to re-enter a UScriptStruct it is
// ALREADY INSIDE (FFoo holding a TArray<FFoo> would recurse forever). It must
// NOT refuse a struct type it merely visited EARLIER on a different branch —
// those are sibling fields and both are real.
//
// The distinction is the whole of A3. `ScanForValue`'s index builder threaded
// one `unordered_set` through the entire per-class walk and never erased, which
// turns "am I inside this?" into "have I ever seen this?" — so only the FIRST
// field of a given struct type in a class contributed leaves, and every later
// one was dropped SUBTREE AND ALL, across unrelated branches. An ordinary actor
// yielded `Location` but never `Velocity`/`Scale3D`/`Extent`; inside a single
// FTransform, `Translation` blocked `Scale3D`. Silent, and total for the session.
//
// The two walkers that got it right (`CollectSchemaLeaves` — Property Search
// Deep — and `CollectGroupLeaves` — Group Scan) both scope to the active path,
// which is why those surfaces find `MaxHealth` while single-value Value Search
// did not. That asymmetry is a distinct in-the-scanner cause for the
// "Value Search can't find field X" family in working-lessons §5.
//
// RAII because the fix is otherwise one `erase` per `return` in a lambda with
// many early exits, and the first one anybody forgets silently restores the bug.
// Header-inline and dependency-free so `dll_helpers_test` can compile it — no
// test target builds Aura.cpp, so this is the only way to pin the semantics.
class StructPathGuard {
public:
    StructPathGuard(std::unordered_set<uintptr_t>& path, uintptr_t node)
        : path_(path), node_(node), entered_(path.insert(node).second) {}
    ~StructPathGuard() { if (entered_) path_.erase(node_); }

    StructPathGuard(const StructPathGuard&)            = delete;
    StructPathGuard& operator=(const StructPathGuard&) = delete;

    /// False when `node` is already on the active path — the caller must return.
    bool Entered() const { return entered_; }

private:
    std::unordered_set<uintptr_t>& path_;
    uintptr_t                      node_;
    bool                           entered_;
};

} // namespace Aura
