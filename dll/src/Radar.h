#pragma once

// ============================================================
// Radar — 拉達爾 (影子戰士 — Shadow Warrior)
// CE-style value scan: First Scan / Next Scan workflow
//
// Walks GObjects + UProperty metadata to find every UPROPERTY-declared
// field whose typed value matches the user's target. Each candidate is
// already enriched with owning UObject + class + defining-class + field
// metadata, so the user sees `ACharacter.Health = 100 @ PlayerPawn_0`
// instead of CE's raw "address = 100".
//
// Session lifecycle:
//   begin_value_scan   → SessionManager::Begin returns sessionId
//   refine_value_scan  → SessionManager::RefineWith runs predicate
//                        against cached candidates, pruning in place
//   end_value_scan     → SessionManager::End drops the session
//
// Idle sessions are reaped by an ACTIVITY-TRIGGERED lazy sweep, not a wall clock
// (audit #5 AB17): every Begin / RefineWith / End sweeps sessions idle past
// kScanSessionIdleExpiry (a Refine/End protects its OWN session first). The real
// backstop for a client that goes fully quiet is the last-client-disconnect
// DropAll (Fern) — a single session left by a connected-but-idle client is bounded
// by the connection, not by the 300s (a wall-clock sweep would have to live on
// Fern's pipe-monitor thread; the read/query path is deliberately NOT swept, being
// hit on every page). SessionManager holds a global mutex; ownership is via
// unique_ptr so move semantics are cheap when refining (caller's lambda runs inside
// the lock holding a reference to the candidates vector — typical refine reads ~50K
// addresses via ReadSafe which is fast enough that a coarse lock is acceptable).
//
// Scope (build 738 MVP + Phase 2 expansion 2026-05-26):
//   Numeric (MVP, build 738):
//     - Types:      Int8/16/32/64, UInt8/16/32/64, Float, Double, Bool
//                   (BoolProperty supports both whole-byte and bitfield)
//     - First scan: Exact / Bigger / Smaller / Between
//     - Next scan:  + Changed / Unchanged / Increased / Decreased
//   String (Phase 2A, build 750):
//     - Types:      FString (StrProperty), FName (NameProperty), FText
//                   (TextProperty — best-effort: cooked FText often
//                   has the display string at a probed offset; the
//                   scan falls back to the raw header bytes if no
//                   string is resolvable)
//     - First scan: Exact / Contains / StartsWith / EndsWith
//     - Next scan:  + Changed / Unchanged
//                   (Bigger/Smaller/Between/Increased/Decreased rejected
//                    DLL-side for string types — no natural ordering)
//   Vector (Phase 2B, build 750):
//     - Types:      FVector / FRotator / FTransform (translation only).
//                   FVector / FRotator are 3 floats (X,Y,Z) or
//                   (Pitch,Yaw,Roll). FTransform scans the Translation
//                   FVector — the most useful cheat axis; Rotation +
//                   Scale are out of scope to keep the UI surface tight.
//     - First scan: Exact / Bigger / Smaller (component-wise; Between
//                   takes a single second vector for upper bound)
//     - Next scan:  + Changed / Unchanged / Increased / Decreased
//   Containers:     TArray<T> (Phase 2C, build 757) + TSet<T> / TMap<K,V>
//                   key|value (V1a, build 927) are all walked element-wise
//                   for any supported leaf DataType. Sparse containers
//                   iterate allocated slots only. Element addresses are raw,
//                   so refine degrades on a container reallocation (SEH-safe
//                   read drops the candidate). TOptional is still deferred (V1c).
//   Excluded:       Native C++ fields (non-UPROPERTY) — UI must show banner
//

// The candidate enrichment matches Aura::FindByAddress conventions so
// "Open in Live Walker" works without further address-→-instance lookup.
// ============================================================

#include <algorithm>
#include <chrono>
#include <cstdint>
#include <memory>
#include <mutex>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <utility>
#include <vector>

namespace Radar {

// Data types supported by the scan engine. Numeric primitives are the
// MVP; string types (FString/FName/FText) and vector types (FVector/
// FRotator/FTransform) are Phase 2 extensions (build 750). TArray<T>
// scan remains deferred — see memory project_value_search_caveats.
enum class DataType : uint8_t {
    // Numeric primitives (MVP, build 738)
    Int8 = 0,
    Int16,
    Int32,
    Int64,
    UInt8,
    UInt16,
    UInt32,
    UInt64,
    Float,
    Double,
    Bool,

    // String types (Phase 2A, build 750). prevValue array unused;
    // candidate's prevStr holds the resolved UTF-8 string.
    FString,
    FName,
    FText,

    // Vector types (Phase 2B, build 750). The candidate's prevValue array
    // holds the CANONICAL form — 3 little-endian doubles (24 bytes) — no
    // matter whether the game's field was 3xfloat or 3xdouble, so one
    // comparison and one renderer serve both. The SOURCE width is a
    // per-field fact carried by FieldDescriptor::vectorWidth, never by the
    // DataType: UE5's LWC made "FVector" 3xdouble while "FVector3f" stayed
    // 3xfloat, and a single game holds both. SizeOf() therefore returns 0
    // here (variable, like the string + multi-numeric types).
    // FTransform scans the Translation FVector only (Rotation + Scale out
    // of scope to keep UI tight) — no struct name maps to it today.
    FVector,
    FRotator,
    FTransform,

    // Multi-numeric "meta" type (build 794). Unlike CE's raw "All" scan
    // (which reinterprets the same untyped bytes as multiple widths),
    // our structured property walk knows each field's DECLARED type, so
    // NumericNoByte means: accept every word/dword/qword/float/double
    // UPROPERTY and compare the user's value against each field using
    // that field's own declared width — no byte-reinterpret false hits.
    // "NoByte" deliberately excludes Int8/UInt8/Bool: 1-byte fields are
    // extremely numerous and a small value (0/1/5/100) would explode the
    // candidate set. Members: Int16/UInt16, Int32/UInt32, Int64/UInt64,
    // Float, Double. SizeOf() returns 0 (variable, like strings); the
    // scan/refine engines resolve the concrete per-field DataType via
    // TryDataTypeFromPropertyTypeName + a pre-built NumericTargetSet.
    NumericNoByte,

    // Multi-numeric "meta" type WITH 1-byte fields (build 796). Same
    // structured per-width compare as NumericNoByte, but additionally
    // includes Int8 (Int8Property) + UInt8 (ByteProperty). Bool is still
    // excluded (it has its own single-type scan). Members: Int8/UInt8,
    // Int16/UInt16, Int32/UInt32, Int64/UInt64, Float, Double. WARNING:
    // small values (0/1/255) match a very large number of 1-byte fields —
    // the UI surfaces a result-volume warning for this type.
    NumericAll,
};

enum class ScanType : uint8_t {
    // Targeted (compare against user-supplied value)
    Exact = 0,
    Bigger,
    Smaller,
    Between,

    // Prev-value (compare against last-observed candidate bytes)
    Changed,
    Unchanged,
    Increased,
    Decreased,

    // String-only targeted predicates (Phase 2A). Reject DLL-side for
    // numeric / vector types since there's no natural substring concept.
    Contains,
    StartsWith,
    EndsWith,
};

// How a fractional float/double is reduced to the integer the game DISPLAYS,
// before any numeric compare (build 1672). Games store HP/MP/progress as floats
// (513.36, 99.6) but show a rounded integer; the user searches/compares against
// what they SEE. This replaces the old implicit half-away round + the ± tolerance
// band: every Float/Double operand (scalar and vector axis) is reduced via this
// mode, in both targeted (Exact/Bigger/Smaller/Between vs a WHOLE target) and
// prev-value (Changed/Unchanged/Increased/Decreased — reduce cur AND prev)
// predicates. A FRACTIONAL target keeps exact-literal compare (precise intent —
// no reduce). Integer fields are reduce-invariant (an integer reduces to itself),
// so integer compares stay strict; the mode only affects them when the user types
// a FRACTIONAL target/bound on an integer width (then the target is coerced to an
// integer via this mode — see BuildNumericTargets). Default Round = the historical
// half-away-from-zero behavior.
enum class RoundMode : uint8_t {
    Round = 0,  // half-away-from-zero (std::round) — e.g. 11.6 -> 12, 11.4 -> 11
    Trunc,      // toward zero        (std::trunc) — e.g. 11.6 -> 11, -11.6 -> -11
    Ceil,       // toward +infinity   (std::ceil)  — e.g. 11.1 -> 12, 99.6 -> 100
};

// Byte count for a given DataType. 1..8 for numeric primitives; 0 for the
// variable-width families — string types (the scan engine reads on demand via
// Ubel::ReadFStringAt / ReadFNameAt / ReadFTextStringAt), the multi-numeric
// meta types, and the VECTOR types (12 or 24 per field — see VectorWidth
// below; it used to answer a flat 12 and every UE5 LWC scan read junk).
size_t SizeOf(DataType dt);

// --- Vector width (LWC) ---
//
// A vector field is 3xfloat (12B) or 3xdouble (24B). UE5's Large World
// Coordinates made the *default* spellings — "Vector" / "Rotator" — double
// backed while the explicit float variants ("Vector3f" / "Rotator3f") stayed
// 12 bytes, so the STRUCT NAME does not determine the width and neither does
// the engine version: one UE5 game holds fields of both widths.
//
// The width comes from the property's own REFLECTED ElementSize, which is the
// rule docs/teleport-spec.md §5.3 already fixed for this repo ("Never key this
// off a version number") and which Wirbel/Laufen/Dunste/Schlacht each already
// apply locally. These two helpers are the shared, testable statement of it.
constexpr int32_t VECTOR_WIDTH_FLOAT  = 12;   // 3 x float  (UE4, FVector3f)
constexpr int32_t VECTOR_WIDTH_DOUBLE = 24;   // 3 x double (UE5 LWC FVector)
// Bytes of the canonical decoded form stored in Candidate::prevValue and used
// for every vector target buffer that crosses into Aura.
constexpr int32_t VECTOR_CANON_BYTES  = 24;   // 3 x double

// True for a reflected struct size we can read as an X/Y/Z triple. Anything
// else (FVector2D at 8/16, FVector4 at 16/32, a namesake struct that is not a
// vector at all) must be REFUSED rather than read at a guessed width — that
// refusal is what stops a same-named-but-different-layout struct entering the
// candidate set.
bool IsSupportedVectorWidth(int32_t width);

// Decode `width` bytes at `src` into 3 doubles. width == 12 reads 3 floats and
// widens; width == 24 reads 3 doubles. Any other width returns false and
// leaves `out` untouched. `src` must hold at least `width` readable bytes.
bool DecodeVectorBytes(const uint8_t* src, int32_t width, double out[3]);

// Store a decoded triple in the canonical VECTOR_CANON_BYTES form (3 doubles).
// `dst` must have room for VECTOR_CANON_BYTES.
void StoreVectorCanonical(const double v[3], uint8_t* dst);

// Human-readable name (matches the JSON wire shape: "Int32" / "Float" / ...)
const char* NameOf(DataType dt);

// Wire string for a ScanType ("Exact" / "Bigger" / "Increased" / ...). Inverse
// of TryParseScanType; used to echo a session's stored per-slot predicate back
// to the UI (the group scan persists ScanType as an enum, not the raw string).
const char* NameOf(ScanType st);

// Parse from wire string; returns true on match, false on unknown.
bool TryParseDataType(const std::string& s, DataType& out);
bool TryParseScanType(const std::string& s, ScanType& out);

// Wire string for a RoundMode ("Round" / "Trunc" / "Ceil"). Inverse of
// TryParseRoundMode; used to echo a group session's stored per-slot mode back.
const char* NameOf(RoundMode rm);
// Parse the wire rounding-mode string; returns true on match. Unknown / empty
// maps to false (caller defaults to RoundMode::Round — backward compatible with
// pre-build-1672 clients that send no rounding_mode field).
bool TryParseRoundMode(const std::string& s, RoundMode& out);

// Reduce a value to the integer-valued double the game DISPLAYS, per the mode:
// Round = half-away-from-zero, Trunc = toward zero, Ceil = toward +infinity. The
// single primitive behind the float compare predicate, the fractional-target
// integer coercion (BuildNumericTargets / ParseValueBytes), and the C# mirror
// (SnapshotNumeric.Reduce). Pure.
double ReduceRounded(double x, RoundMode m);

// True when the scan type compares against the stored prevValue.
// (Changed / Unchanged / Increased / Decreased)
bool IsPrevValueScanType(ScanType st);

// True when the scan type is valid for the FIRST scan (no prevValue yet).
// Numeric: Exact / Bigger / Smaller / Between.
// String:  Exact / Contains / StartsWith / EndsWith.
// Vector:  Exact / Bigger / Smaller / Between (component-wise).
bool IsFirstScanType(ScanType st);

// True when the data type is one of the Phase 2 string family
// (FString / FName / FText) — those use the candidate's prevStr and
// the CompareStringPredicate path instead of byte comparisons.
bool IsStringDataType(DataType dt);

// True when the data type is one of the Phase 2 vector family
// (FVector / FRotator / FTransform). Reads SizeOf(dt) bytes per
// candidate and compares X / Y / Z component-wise with the shared rounding mode.
bool IsVectorDataType(DataType dt);

// True when the scan type is one of the string-only substring
// predicates (Contains / StartsWith / EndsWith). Caller must reject
// these for non-string DataTypes.
bool IsSubstringScanType(ScanType st);

// True when the data type is a multi-numeric "meta" type
// (NumericNoByte / NumericAll) that fans out over a fixed set of
// concrete numeric member types instead of a single fixed-width compare.
bool IsMultiNumericDataType(DataType dt);

// Concrete member DataTypes a multi-numeric meta type expands to.
// NumericNoByte -> { Int16, UInt16, Int32, UInt32, Int64, UInt64,
//                    Float, Double }.
// NumericAll    -> the above plus { Int8, UInt8 }.
// Empty for non-meta data types.
const std::vector<DataType>& MultiNumericMembers(DataType dt);

// Map a UE property type-name string (as it appears in
// ClassInfo.Fields[].TypeName) to its concrete scalar DataType. The
// full numeric member set is recognised — "IntProperty"->Int32,
// "FloatProperty"->Float, "Int16Property"->Int16, "Int8Property"->Int8,
// "ByteProperty"->UInt8, etc. "EnumProperty"->UInt8 (audit #5 AB14): enums are
// 1-byte in the overwhelming majority; the underlying width is not in the name, so
// a rare wider enum is read at its low byte — acceptable for the small indices
// enums hold, and it was invisible otherwise. Returns false for BoolProperty +
// non-numeric names. (NumericNoByte simply never feeds byte-width names
// here because its PropertyTypeNames union excludes them.) Used by the
// scan + refine engines to resolve each candidate's own width.
bool TryDataTypeFromPropertyTypeName(const std::string& propTypeName, DataType& out);

// Inverse of TryDataTypeFromPropertyTypeName: a concrete numeric DataType ->
// its canonical UE property-type string ("IntProperty" / "FloatProperty" /
// "ByteProperty" / ...). Returns "" for non-numeric / meta / Bool types.
// Used by the Native-C value scan to stamp synthetic raw-hole descriptors with
// a fieldType the refine path (TryDataTypeFromPropertyTypeName) round-trips.
const char* PropertyTypeNameOf(DataType dt);

// Snapshot capture (Phase A1a): given the property type-name of each field
// of a class (in ClassInfo.Fields order), select those whose declared type
// is a member of the given numeric meta scope (NumericNoByte / NumericAll),
// pairing each selected field's index with its resolved concrete DataType.
// Pure / std-only so it reuses the exact same MultiNumericMembers +
// TryDataTypeFromPropertyTypeName invariant the value scan relies on — a
// field captured here is guaranteed to resolve to a fixed width via
// SizeOf(dt). Non-numeric, Bool, and out-of-scope (e.g. 1-byte under
// NumericNoByte) fields are omitted.
struct SnapshotFieldPick {
    int32_t  fieldIndex;
    DataType dt;
};
std::vector<SnapshotFieldPick> SelectSnapshotNumericFields(
    const std::vector<std::string>& propTypeNames, DataType numericScope);

// Snapshot array capture (Phase A1b): pick the index of the best "inner key"
// field for a struct-array element, given the element struct's field type names
// + field names (parallel vectors). Prefers a NameProperty (FName) whose name
// looks like an id/name/tag/key/row; then any NameProperty; then the first
// integer field. Returns -1 if none (caller falls back to the element index).
// Pure / std-only. Used to give cargo/inventory rows a reorder-immune key
// (e.g. FCargoSlot.ItemID) so the same logical slot joins across snapshots.
int SelectArrayInnerKey(const std::vector<std::string>& typeNames,
                        const std::vector<std::string>& fieldNames);

// Pre-parsed multi-numeric target. Holds one little-endian byte buffer
// per member DataType whose width can represent the user's value (e.g.
// "70000" yields Int32/UInt32/Int64/UInt64/Float/Double entries but no
// Int16/UInt16; "100.5" yields only Float/Double; "-5" yields the
// signed + float members but no unsigned ones). BuildNumericTargets
// populates it; the scan/refine engines Find() the entry matching each
// field's resolved DataType — a missing entry means "value can't fit
// this width" and the field is skipped (no candidate / pruned).
struct NumericTargetSet {
    // Why an entry can exist without a usable byte pattern (audit #5 AB4).
    //
    // "Does the target fit this width" is the right question for Exact and the
    // WRONG one for the ordered predicates. Every Int8 field is smaller than 500,
    // but 500 has no int8 encoding, so the old set emitted no Int8 entry and the
    // engines skipped every 1-byte field — a Smaller/Bigger scan silently lost a
    // whole width class, with no marker anywhere.
    //
    // Clamping is NOT the fix and looks like it is: `Smaller 500` clamped to 127
    // becomes `cur < 127` (ApplyOrdered's Smaller is a STRICT <), which drops the
    // field holding exactly 127. It restores almost all the missing rows and
    // re-introduces the same silent single-row loss in a form far harder to see.
    // There is no int8 byte pattern meaning "cur <= 127", so the answer cannot
    // live in a fixed-width buffer at all — it has to be a verdict.
    enum class Fit : uint8_t {
        Encoded,     // `bytes` holds the target at this width — compare normally.
        AlwaysTrue,  // No encoding exists, but EVERY value of this width satisfies
                     // the predicate (e.g. Smaller 500 over Int8). Match without
                     // reading a target.
    };
    struct Entry {
        DataType dt;
        uint8_t  bytes[8];
        Fit      fit = Fit::Encoded;
    };
    std::vector<Entry> entries;

    // Return the buffer for `dt`, or nullptr if the value didn't fit
    // that width.
    //
    // ⚠ This CANNOT express an AlwaysTrue entry — it hands out a pointer into
    // `bytes`, and the verdict is not in `bytes`. It is kept for the call sites
    // that only ever deal with encoded targets; anything on the scan/refine path
    // must use FindEntry so the verdict survives the lookup. Returning `bytes`
    // for an AlwaysTrue entry would compare against a zeroed buffer, which is
    // wrong in a new and quieter way than the bug this fixes.
    const uint8_t* Find(DataType dt) const {
        for (const auto& e : entries) {
            if (e.dt == dt && e.fit == Fit::Encoded) return e.bytes;
        }
        return nullptr;
    }

    // The entry itself, verdict included, or nullptr when this width cannot
    // satisfy the predicate at all (the field really is skipped then).
    const Entry* FindEntry(DataType dt) const {
        for (const auto& e : entries) {
            if (e.dt == dt) return &e;
        }
        return nullptr;
    }
};

// Parse the user's numeric value string into one NumericTargetSet entry
// per member width of `metaDt` (currently NumericNoByte) that can
// represent it. Returns false (and leaves out.entries empty) when the
// string is empty / unparseable / fits no member width. Signed,
// unsigned, and floating interpretations are each attempted and gated:
//   - negative values produce no unsigned entries
//   - hex (0x..) values produce integer entries only
// `roundMode` (build 1672): a NON-integral value (e.g. "10.9") no longer skips
// integer widths — it is reduced to an integer via the mode (Round 10.9->11,
// Trunc->10, Ceil->11) so integer fields can still match a fractional target/
// bound. Float/Double members always keep the exact fractional value. A clean
// integer string is unaffected (the integer parse wins; the mode is a no-op).
// `st` decides what a value that does NOT fit a member's width means (audit #5 AB4):
// for Exact it means "no field of this width can match" and the member is dropped;
// for Smaller/Bigger it may instead mean "EVERY field of this width matches", which
// is emitted as a Fit::AlwaysTrue entry. Defaulted to Exact so the existing callers
// and every Exact/Changed/Unchanged path keep byte-identical behaviour.
// Between is deliberately NOT handled — its two bounds are built by two independent
// calls at four call sites, so a correct fix has to build them jointly. See todo.md.
bool BuildNumericTargets(DataType metaDt, const std::string& raw, NumericTargetSet& out,
                         RoundMode roundMode = RoundMode::Round,
                         ScanType  st        = ScanType::Exact);

// True when the (DataType, ScanType) pair is a legal combination.
// Used by the pipe handler to reject Bigger/Smaller on strings and
// Contains/StartsWith on numerics before the scan engine sees them.
bool IsScanTypeValidFor(DataType dt, ScanType st);

// Map a DataType to the set of UE property type-name strings that
// represent it in ClassInfo.Fields. UInt8 maps to ByteProperty
// (UE's name for one-byte unsigned int). Vector types map to
// StructProperty — caller must additionally check the inner struct's
// name matches "Vector" / "Vector3f" / "Rotator" / "Transform" /
// "Transform3f" before emitting a candidate.
const std::vector<std::string>& PropertyTypeNames(DataType dt);

// Vector DataType → set of UScriptStruct names that represent it
// (handles UE5 "Vector3f" + UE4 "Vector" + UE5 LWC variants). Empty
// for non-vector data types.
const std::vector<std::string>& VectorStructNames(DataType dt);

// ---- Interned candidate metadata (build 924, V3-A) -------------------
//
// A First Scan can return tens of thousands of candidates. Each used to
// carry six std::strings (class / defining-class / field name / field
// type / instance name) copied BY VALUE, which is almost entirely
// redundant: className / definingClassName / fieldName / fieldType /
// boolFieldMask / fieldOffset are functions of the (class, field) pair —
// identical across every candidate of that class+field — and instanceName
// is shared by every field that matches on the same object. Interning
// them into two session-level pools shrinks the per-candidate record from
// ~240 B (+ up to 5 heap strings) to ~72 B (+ 0 heap strings for numeric
// scans). That is what lets a session hold a large candidate set inside
// the injected DLL without bloating the target process (the precondition
// for any later maxResults-cap increase — see todo.md V2/V3).

// Per-(class, field) display metadata. One entry per distinct field the
// scan emits a candidate for; shared via Candidate::descriptorIdx.
// ============================================================
// Container-element refine anchoring (audit #5 A11)
//
// A candidate remembers its value's ABSOLUTE address, and refine (Next Scan)
// re-reads that address. For a DIRECT field that is stable for the object's life.
// For a CONTAINER ELEMENT it is not, and the comment that used to sit on
// `Candidate::addr` claimed the failure was a clean drop. It is not:
//
//   * TArray::RemoveAt does RelocateConstructItems on everything after the
//     removal point (Array.h RemoveAtImpl), so the pinned address stays perfectly
//     mapped and now holds the NEIGHBOUR's value — a silent wrong survivor;
//   * a TSparseArray Add reuses a freed slot in place (SparseArray.h
//     AddUninitialized -> FirstFreeIndex), so a removed-then-refilled slot reads
//     as a live value at the same address;
//   * growth reallocs the buffer, and today every element candidate in that
//     container is simply lost.
//
// The rule below is INDEX-AWARE and per-shape on purpose. A container-wide
// "did anything change" test would drop every candidate in an array that merely
// APPENDED — and appending with slack (Array.h AddUninitialized: `if (ArrayNum ==
// ArrayMax) grow; else ArrayNum++`) relocates nothing, so those candidates are
// correct today. Trading a rare silent-wrong-value for a frequent permanent loss
// of a correct candidate would be a worse scanner, not a better one.
// ============================================================

/// How a candidate's value is reached from its owning object.
///
/// `Unknown` is NOT "no container" — it is "nobody said", and refine falls back to
/// the pre-A11 address-only behaviour for it. The deep-container and group paths
/// carry `Unknown` today, deliberately and visibly, rather than being given a
/// `Direct` all-clear they have not earned.
enum class ValueAnchor : uint8_t {
    Unknown = 0,
    Direct,          // obj + fieldOffset — stable for the object's life
    ArrayElement,    // TArray slot:      data + idx*elemStride + elemIntra
    SparseElement,   // TSet/TMap slot:   same, and gated on the slot's OWN alloc bit
    // A container element at depth >= 2, i.e. one whose container HEADER lives inside
    // an OUTER container's heap buffer. Treated exactly like `Unknown` (pre-A11
    // behaviour), but NAMED so it is distinguishable from a hop that dropped the stamp.
    //
    // The honest reason it is not anchored: validating a depth-2 header first requires
    // proving the depth-1 element did not move — that is an anchor CHAIN, not one
    // anchor. (It is NOT that re-reading a freed header is uniquely dangerous; the
    // pre-A11 path already reads a stale leaf address, so anchoring could not be worse
    // on that axis. Recording the real reason so nobody "fixes" the wrong thing.)
    UnverifiableNested,
};

/// What refine must do with one container-element candidate.
enum class RefineAnchorVerdict : uint8_t {
    KeepAddress,   // the stored absolute address is still correct
    Repoint,       // the buffer moved; recompute from the live Data pointer
    Drop,          // this element is gone, or may have been relocated under us
};

/// Absolute address of container element `idx`. Pure.
constexpr uintptr_t ContainerElemAddr(uintptr_t data, int32_t idx,
                                      int32_t elemStride, int32_t elemIntra) {
    return data + static_cast<uintptr_t>(static_cast<int64_t>(idx) * elemStride)
                + static_cast<uintptr_t>(elemIntra);
}

/// Decide the fate of a container-element candidate whose container header has
/// just been re-read. Pure — every input is already in the caller's hands.
///
/// @param slotAllocated  the candidate's OWN allocation bit, for a sparse
///        container. EXACT: it answers "was MY slot freed", which no count or
///        address comparison can. Pass true for TArray, which has no such bit.
///
/// Known residual, stated rather than hidden: `TArray::Insert` shifts on a count
/// INCREASE, and this keeps those candidates. That is no worse than today, and
/// catching it would need a per-element identity witness (see todo.md).
constexpr RefineAnchorVerdict RefineContainerAnchor(ValueAnchor anchor,
                                                    int32_t   elementIndex,
                                                    int32_t   numAtScan,
                                                    uintptr_t dataAtScan,
                                                    uintptr_t nowData,
                                                    int32_t   nowNum,
                                                    bool      slotAllocated) {
    // Not a container candidate, or nobody stamped one: pre-A11 behaviour.
    if (anchor != ValueAnchor::ArrayElement && anchor != ValueAnchor::SparseElement)
        return RefineAnchorVerdict::KeepAddress;
    // Stamped as a container but missing the data to act on it — degrade to the
    // old behaviour rather than dropping a candidate on our own bookkeeping.
    if (elementIndex < 0 || numAtScan < 0 || !dataAtScan || !nowData)
        return RefineAnchorVerdict::KeepAddress;

    // The slot no longer exists.
    if (elementIndex >= nowNum) return RefineAnchorVerdict::Drop;
    // Sparse: MY slot was freed. Exact, and it is the case a stale address cannot
    // see, because a refilled slot reads as a perfectly live value.
    if (anchor == ValueAnchor::SparseElement && !slotAllocated)
        return RefineAnchorVerdict::Drop;
    // The container SHRANK. For TArray that is RemoveAt, which relocated every
    // element after the removal point and we cannot tell whether ours moved; for a
    // sparse array it means Compact() ran, which relocates too.
    if (nowNum < numAtScan) return RefineAnchorVerdict::Drop;
    // The buffer moved (growth realloc). Everything is still in slot order, so the
    // element is recoverable — today it is simply lost.
    if (nowData != dataAtScan) return RefineAnchorVerdict::Repoint;
    return RefineAnchorVerdict::KeepAddress;
}

/// What a container-element leaf carries from the deep walk to refine (audit #5 A12).
///
/// ⚠ `data` is the buffer base STORED AT SCAN TIME, not derived from stride+intra the
/// way the single-value path derives it. That is deliberate and it is the safer of two
/// equal-cost encodings: the deep walk builds a leaf address through a recursion where
/// an intra-element offset is easy to get wrong (the Map `.Value` side alone is off by
/// `cfe.valueOffset`), and with a DERIVED base a wrong intra makes `dataAtScan` differ
/// from `nowData` on every pass — so the candidate is Repointed by that error, forever,
/// onto a real and plausible-looking neighbouring field. With the base STORED, a wrong
/// intra can only fail to help. Repoint becomes `RepointByBufferMove`, which is exact by
/// construction and needs neither stride nor intra.
///
/// Every member has an NSDMI, and `num` defaults to **-1**, not 0: both downstream hops
/// are default-initialised (`GroupLeafMeta m;` / `GroupSlotMatch sm;`), so a hop that
/// drops the assignment must land on values `RefineContainerAnchor` REFUSES to act on.
/// A 0 there would pass its `numAtScan < 0` guard and then pass the shrink test.
struct LeafAnchor {
    ValueAnchor kind   = ValueAnchor::Unknown;
    uintptr_t   header = 0;    // absolute address of the container header
    uintptr_t   data   = 0;    // buffer base at scan time
    int32_t     num    = -1;   // TArray::Count / TSparseArray::MaxIndex at scan time
};

/// The depth rule, in ONE place. Leaves are emitted with `depth + 1`, so "the container
/// header is a direct field of the scanned object" is `leafDepth == 1` — writing that
/// test at the call site with the walker's own `depth` is an off-by-one nothing can
/// catch, because no test target compiles `Aura.cpp`.
constexpr ValueAnchor AnchorKindForLeaf(bool isSparse, int leafDepth) {
    if (leafDepth != 1) return ValueAnchor::UnverifiableNested;
    return isSparse ? ValueAnchor::SparseElement : ValueAnchor::ArrayElement;
}

/// ⚠ Two factories, not one with a flag, and the parameter NAMES are the point.
/// The walker's own loop variable holds `sa.MaxCapacity` for a sparse container while
/// refine re-reads `sa.MaxIndex`. Stamping the one in hand would make `numAtScan`
/// (capacity) exceed `nowNum` (index) for every TSet/TMap with spare slots, so the
/// shrink rule would DROP every sparse group candidate on the first Next Scan. Naming
/// the parameter `maxIndex` is what stands between the two.
constexpr LeafAnchor MakeArrayLeafAnchor(uintptr_t header, uintptr_t data,
                                         int32_t count, int leafDepth) {
    return LeafAnchor{ AnchorKindForLeaf(/*isSparse=*/false, leafDepth), header, data, count };
}
constexpr LeafAnchor MakeSparseLeafAnchor(uintptr_t header, uintptr_t data,
                                          int32_t maxIndex, int leafDepth) {
    return LeafAnchor{ AnchorKindForLeaf(/*isSparse=*/true, leafDepth), header, data, maxIndex };
}
/// Not reached through any container. Explicit, so `Unknown` keeps meaning "nobody said".
constexpr LeafAnchor MakeDirectLeafAnchor() {
    return LeafAnchor{ ValueAnchor::Direct, 0, 0, -1 };
}

/// New absolute address of a leaf after its container's buffer moved. Exact by
/// construction: every leaf in the buffer shifts by the same delta, whatever its
/// element index, element stride or intra-element offset happened to be.
constexpr uintptr_t RepointByBufferMove(uintptr_t leafAddr, uintptr_t dataAtScan,
                                        uintptr_t nowData) {
    return leafAddr + (nowData - dataAtScan);
}

struct FieldDescriptor {
    std::string className;          // The instance's UClass name
    std::string definingClassName;  // Class where the field is declared
    std::string fieldName;          // BASE name (no array "[i]" suffix)
    std::string fieldType;          // "FloatProperty" / "IntProperty" / ...
    int32_t     fieldOffset   = 0;  // bytes from instanceAddr. For a container anchor
                                    // this is the CONTAINER HEADER's offset, which is
                                    // what lets refine re-read the header without
                                    // storing its address on every candidate.
    // audit #5 A11 — how refine must treat this field's stored absolute address.
    //
    // ⚠ SINGLE-VALUE SESSIONS ONLY. `Session` stamps these three; `GroupSession` does
    // NOT — its `internDesc` interns a descriptor per (short class name, field, offset),
    // and two distinct UClasses sharing a short name already share one, which is
    // harmless for display text and would be an ADDRESS bug for anything used in
    // pointer arithmetic. So a group leaf carries its anchor per-match on
    // `GroupSlotMatch::anchor` instead, and these stay `Unknown`/0 there.
    // Do NOT read `descriptors[groupSlotMatch.descriptorIdx].anchor` — it is not the
    // truth for that session, it is the default. (audit #5 A12)
    ValueAnchor anchor        = ValueAnchor::Unknown;
    int32_t     elemStride    = 0;  // container element stride  (container anchors only)
    int32_t     elemIntra     = 0;  // value offset WITHIN the element (map value / struct inner)
    // BoolProperty bitfield support: when boolFieldMask != 0xFF the field
    // shares a byte with siblings. Read as `(byte & mask) != 0`.
    uint8_t     boolFieldMask = 0xFF;
    // Native-C (P1): true when this descriptor is a synthetic raw-hole leaf
    // (an unmanaged, non-UPROPERTY offset) rather than a reflected field. The
    // refine path is identical (re-read addr, re-resolve width from fieldType);
    // this flag + guessedType only drive UI badging (className stays the owning
    // class, fieldName encodes the offset as "<raw@0xNN>", definingClassName "").
    bool        isNativeC = false;
    std::string guessedType;        // human label of the interpreted width (e.g. "Int32")
    // Vector data types only: the field's REFLECTED source width, 12 (3xfloat)
    // or 24 (3xdouble LWC). 0 for every non-vector field.
    //
    // It has to live here rather than being derived from `fieldType`, which is
    // the bare string "StructProperty" for a vector: the refine (Next Scan)
    // path is handed nothing but the candidate + this descriptor, so without
    // it a second scan cannot know how wide the field it already matched was.
    // (The multi-numeric family re-resolves its width from fieldType, which is
    // a concrete property-type name there; vectors have no such equivalent.)
    int32_t     vectorWidth = 0;
};

// Per-owning-object metadata. One entry per distinct UObject that owns at
// least one candidate; shared via Candidate::instanceIdx. (Each object is
// scanned by exactly one worker thread, so instances never duplicate
// across the per-thread merge.)
//
// Cross-RPC identity model (audit #5 AB7). A candidate is re-read across refines
// by its stored ABSOLUTE address; `instanceIndex` is display metadata, not
// re-validated. If the owning object is freed and its slot recycled by ANOTHER
// object between scans, refine reads the new occupant's bytes. This is NOT patched
// with a `(InternalIndex, SerialNumber)` witness — that pair was refused three
// times and is wrong for a passive observer (working-lessons §4.3: UE zeroes
// SerialNumber on free and allocates it lazily, so most objects carry serial 0
// and a stored (i,0) matches a recycled (i,0), reporting "fresh" exactly when it
// must not). §4.3's correct pattern — witness the INPUT BYTES of a memoized
// decode — does not apply either: a value scan EXPECTS the bytes at the address to
// change (Changed/Increased/…), so there are no stable input bytes to witness. The
// only real identity check left is re-reading the UObject's class pointer to catch
// a slot recycled by a DIFFERENT class — a new, behaviour-changing feature with an
// open product question (AA2 established class-wide targeting can be by design) and
// no unit-test seam (needs a live recycled slot). Deferred as its own item rather
// than forced in a LOW batch. Current mitigations: SEH-safe reads drop a candidate
// whose address became unreadable, and the container anchor (A11/A12) handles
// element relocation.
struct InstanceRecord {
    uintptr_t   instanceAddr  = 0;  // owning UObject
    int32_t     instanceIndex = -1; // GObjects index (-1 if not enumerated)
    std::string instanceName;
};

// One candidate as remembered between RPCs. Holds only per-candidate
// state: the value's address, the last-observed value, and indices into
// the session's FieldDescriptor / InstanceRecord pools. `prevValue` is
// updated to the latest-observed bytes on every successful refine so
// Changed/Unchanged/etc. always compare against "what we saw last time".
//
// For Phase 2 string data types (FString / FName / FText) `prevStr` holds
// the resolved UTF-8 value and `prevValue` is unused. For vector data
// types (FVector / FRotator) `prevValue` holds the CANONICAL decoded triple —
// VECTOR_CANON_BYTES (3 little-endian doubles), whatever the field's own
// width was — so the compare, the renderer and the server-side filter/sort
// never have to re-ask how wide the source was.
// `elementIndex` is >= 0 for a TArray/container element (display name is
// descriptor.fieldName + "[elementIndex]") and -1 for a direct field.
struct Candidate {
    uintptr_t   addr          = 0;   // instance + fieldOffset (the value's address)
    // Widest thing stored here is a canonical vector triple (24B); numerics
    // use SizeOf(dt) <= 8 and strings use prevStr instead.
    uint8_t     prevValue[24] = {};  // last-observed bytes
    std::string prevStr;             // last-observed string (string DataTypes only)
    uint32_t    descriptorIdx = 0;   // -> Session::descriptors
    uint32_t    instanceIdx   = 0;   // -> Session::instances
    int32_t     elementIndex  = -1;  // array/container element index, -1 = direct field
    // audit #5 A11 — the container's element count (TArray::Num / TSparseArray::
    // MaxIndex) AT SCAN TIME. -1 = not a container element. Lands in the padding
    // this struct already carried, so the lean-Candidate constraint above is
    // unaffected. Per-candidate rather than per-container because the alternative
    // is a second pool keyed on (instanceIdx, descriptorIdx), which costs more than
    // the four bytes it saves.
    int32_t     containerNum  = -1;
};

struct Session {
    uint64_t                              id        = 0;
    DataType                              dt        = DataType::Int32;
    std::vector<Candidate>                candidates;
    std::vector<FieldDescriptor>          descriptors;  // shared via Candidate::descriptorIdx
    std::vector<InstanceRecord>           instances;    // shared via Candidate::instanceIdx
    std::chrono::steady_clock::time_point lastUse;

    // V3-C: cached ordered view (filtered + sorted candidate indices) so pure
    // paging doesn't re-sort. Recomputed by SessionManager::QueryWith only when
    // (viewFilter, viewSortKey, viewSortDesc) change, and invalidated
    // (viewValid=false) after a refine mutates `candidates`. viewSortKey is the
    // raw SortKey value (stored as uint8_t because the enum is declared after
    // this struct).
    bool                                  viewValid    = false;
    std::string                           viewFilter;
    uint8_t                               viewSortKey  = 0;
    bool                                  viewSortDesc = false;
    std::string                           viewExclude;  // canonical key of excluded classes
    std::vector<uint32_t>                 viewOrder;
};

// Reconstruct a candidate's display field name from its descriptor +
// element index: `fieldName` for a direct field, `fieldName[elementIndex]`
// for a TArray/container element. Pure / std-only (unit-tested).
std::string FieldDisplayName(const FieldDescriptor& desc, int32_t elementIndex);

// V1c: byte offset of the bIsSet flag inside a non-intrusive TOptional<T>.
// A non-intrusive optional is laid out `{ T value; bool bIsSet; }` (padded to
// alignof(T)), so the flag sits at offset == sizeof(T) and the wrapped value
// is at offset 0. Returns innerSize when the optional is larger than its value
// (i.e. there's room for the trailing bool), else -1 — meaning "no separate
// flag to gate on" (intrusive/pointer optionals encode unset in the value
// itself, and an unknown/zero innerSize can't be gated). Pure (unit-tested).
int32_t OptionalFlagOffset(int32_t optionalSize, int32_t innerSize);

// --- V3-C: server-side value rendering / filter / sort over a candidate pool ---
//
// The session owns the full candidate set inside the injected DLL; the UI is a
// windowed view. Filter / sort therefore run here (over the DLL's own pools —
// no game-memory reads, so the game thread is never touched) and only a window
// is serialized to the UI. These helpers are pure / std-only (unit-tested).

// Render a candidate's value exactly as the wire `value` field does: numeric
// formatted per `dt`; multi-numeric resolved per the descriptor's fieldType;
// vector "X, Y, Z"; string the resolved prevStr. The single source of truth
// for value rendering (the wire encoder calls this too).
std::string FormatCandidateValue(const Candidate& c, DataType dt,
                                 const FieldDescriptor& desc);

// The "Origin" column exactly as the UI renders it (audit #5 AB16): "Native-C
// (Int32)" for a raw-hole hit, "Native-C" when the width guess is empty, else
// "Reflected". Kept byte-identical to the C# `ScanCandidate.Origin` so the
// server-side keyword filter can match a column the user can see — filtering for
// "native" used to return zero rows over candidates visibly reading "Native-C".
// Pure / std-only (unit-tested).
std::string FormatCandidateOrigin(const FieldDescriptor& desc);

// Decode a numeric candidate's prevValue to a double sort key (lossy for very
// large 64-bit integers but monotonic enough for ordering). Non-numeric dt
// returns 0.
double DecodeNumericToDouble(DataType dt, const uint8_t* bytes);

// Columns the result grid can sort by. ScanOrder = the original scan/refine
// order (the candidate vector order). Wire strings parsed by TryParseSortKey.
enum class SortKey : uint8_t {
    ScanOrder, Address, Value, ClassName, FieldName,
    InstanceName, FieldType, Offset, InstanceIndex
};
bool TryParseSortKey(const std::string& s, SortKey& out);

// Canonicalize an excluded-class list into a stable cache key (sorted, '\n'-
// joined) so the view cache is order-insensitive to the wire list. Pure.
inline std::string CanonicalExcludeKey(const std::vector<std::string>& classes) {
    if (classes.empty()) return std::string();
    std::vector<std::string> v = classes;
    std::sort(v.begin(), v.end());
    std::string key;
    for (const auto& s : v) { key += s; key.push_back('\n'); }
    return key;
}

// Build the display order over a candidate pool: keep only candidates whose
// displayed columns contain `filter` (case-insensitive substring; "" keeps
// all) AND whose class is NOT in `excludeClasses` (the client-side class-noise
// picker, applied server-side over the full set), then stable-sort by
// (sortKey, sortDesc). Returns candidate indices in display order — the caller
// slices the requested window out of it.
std::vector<uint32_t> BuildOrderedView(
    const std::vector<Candidate>&       candidates,
    const std::vector<FieldDescriptor>& descriptors,
    const std::vector<InstanceRecord>&  instances,
    DataType dt, const std::string& filter,
    SortKey sortKey, bool sortDesc,
    const std::unordered_set<std::string>& excludeClasses = {});

// Tally a value-scan candidate pool by owning class name, sorted by count desc
// (ties: class name asc). Computed over the WHOLE pool (pre-filter, pre-exclude)
// so the noise picker stays stable as the user filters/excludes. Pure / std-only.
std::vector<std::pair<std::string, int>> BuildClassHistogram(
    const std::vector<Candidate>&       candidates,
    const std::vector<FieldDescriptor>& descriptors);

// Idle-expiry for a scan session — dropped after this long without a Begin/Refine
// so a misbehaving client can't leak candidate memory. Shared by both the single
// (SessionManager) and group (GroupSessionManager) session managers.
inline constexpr std::chrono::seconds kScanSessionIdleExpiry{300};

class SessionManager {
public:
    static SessionManager& Instance();

    // Allocate a new session, take ownership of the candidate vector plus
    // its shared descriptor / instance pools, return its sessionId.
    // Triggers a lazy expiry pass on existing sessions so abandoned
    // sessions don't accumulate.
    uint64_t Begin(DataType dt,
                   std::vector<Candidate>       candidates,
                   std::vector<FieldDescriptor> descriptors,
                   std::vector<InstanceRecord>  instances);

    // Run `fn(Session&)` under the manager's lock. Returns false if the
    // session doesn't exist (caller should map to wire error
    // "session_not_found"). `fn` may mutate the session's candidates
    // vector (typical Refine behavior is to prune entries that no longer
    // match); the descriptor / instance pools are append-only and should
    // be treated as read-only by the callback.
    template <typename Fn>
    bool RefineWith(uint64_t sessionId, Fn&& fn) {
        std::lock_guard<std::mutex> lk(mu_);
        auto it = sessions_.find(sessionId);
        if (it == sessions_.end()) return false;
        it->second->lastUse = std::chrono::steady_clock::now();
        fn(*it->second);
        // The candidate vector may have been pruned — drop the cached view
        // so the next QueryWith rebuilds the order against the surviving set.
        it->second->viewValid = false;
        // Reap OTHER idle sessions too (audit #5 AB17). Runs AFTER the bump above
        // so it can never drop the session being refined — a user stepping away
        // mid-refine keeps their candidates, which is the whole point of the 300s.
        ExpireOldSessionsLocked();
        return true;
    }

    // Read-only access — same lock as RefineWith. `fn(const Session&)`.
    template <typename Fn>
    bool ViewWith(uint64_t sessionId, Fn&& fn) {
        std::lock_guard<std::mutex> lk(mu_);
        auto it = sessions_.find(sessionId);
        if (it == sessions_.end()) return false;
        fn(static_cast<const Session&>(*it->second));
        return true;
    }

    // V3-C server-side window query. Ensures the session's cached ordered view
    // matches (filter, sortKey, sortDesc) — recomputing via BuildOrderedView
    // only when those params changed or the view was invalidated by a refine —
    // then calls `fn(const Session&, const std::vector<uint32_t>& order)` under
    // the lock; the caller slices the requested window out of `order`. Returns
    // false if the session doesn't exist. Reads only the DLL's own pools (no
    // game memory), so it never touches the game thread.
    template <typename Fn>
    bool QueryWith(uint64_t sessionId, const std::string& filter,
                   SortKey sortKey, bool sortDesc,
                   const std::vector<std::string>& excludeClasses, Fn&& fn) {
        std::lock_guard<std::mutex> lk(mu_);
        auto it = sessions_.find(sessionId);
        if (it == sessions_.end()) return false;
        Session& s = *it->second;
        s.lastUse = std::chrono::steady_clock::now();
        const uint8_t keyRaw = static_cast<uint8_t>(sortKey);
        const std::string exclKey = CanonicalExcludeKey(excludeClasses);
        if (!s.viewValid || s.viewFilter != filter
            || s.viewSortKey != keyRaw || s.viewSortDesc != sortDesc
            || s.viewExclude != exclKey) {
            std::unordered_set<std::string> exclSet(excludeClasses.begin(), excludeClasses.end());
            s.viewOrder = BuildOrderedView(s.candidates, s.descriptors,
                                           s.instances, s.dt, filter,
                                           sortKey, sortDesc, exclSet);
            s.viewFilter   = filter;
            s.viewSortKey  = keyRaw;
            s.viewSortDesc = sortDesc;
            s.viewExclude  = exclKey;
            s.viewValid    = true;
        }
        fn(static_cast<const Session&>(s), s.viewOrder);
        return true;
    }

    // Drop a specific session. Returns false if not found.
    bool End(uint64_t sessionId);

    // Drop sessions whose lastUse is older than kExpirySeconds. Called lazily from
    // Begin / RefineWith / End, so any value-scan activity reaps abandoned sessions
    // (audit #5 AB17 — it used to fire ONLY from Begin, so a client that scanned
    // once and went quiet never triggered it).
    void ExpireOldSessions();

    // Drop every session — called from pipe shutdown / DLL unload.
    void DropAll();

    struct Stats {
        size_t   sessionCount    = 0;
        size_t   totalCandidates = 0;
        uint64_t nextId          = 0;
    };
    Stats GetStats();

private:
    SessionManager() = default;
    SessionManager(const SessionManager&) = delete;
    SessionManager& operator=(const SessionManager&) = delete;

    // The sweep body, assuming mu_ is already held. `ExpireOldSessions()` locks and
    // calls this; RefineWith / End call it directly under their own lock so a Refine
    // can protect its session (bump lastUse) BEFORE sweeping. (audit #5 AB17)
    void ExpireOldSessionsLocked();

    // 5-minute idle expiry — matches discrete's Phase 27b precedent.
    // Long enough that a user can step away mid-refine without losing
    // candidates; short enough that abandoned sessions clear on the next
    // value-scan operation (Begin / RefineWith / End all sweep — audit #5 AB17).
    static constexpr std::chrono::seconds kExpirySeconds = kScanSessionIdleExpiry;

    std::mutex                                                  mu_;
    std::unordered_map<uint64_t, std::unique_ptr<Session>>      sessions_;
    uint64_t                                                    nextId_ = 1;
};

// ============================================================
// Multiple values group scan (build 1276) — object-aware "group scan".
//
// A group scan looks for objects (blocks) that SIMULTANEOUSLY hold ALL of N
// user values (2..4) at DISTINCT numeric-property offsets, in any order. The
// pure combinatorial match lives in Orden.h (source-agnostic); the structures
// below persist a group session so refine can re-read the located offsets.
//
// One logical hit = one owning UObject + a per-slot CONVERGENCE LIST of the
// offsets whose current value still satisfies that slot. On the first scan a
// slot may match several offsets; refine re-reads them and shrinks each list. A
// slot list of size 1 is "locked" (the field is identified). The owning object
// stays a candidate only while a System of Distinct Representatives exists
// across the slots (checked via Orden::HasDistinctAssignment in Aura's
// scan/refine — kept out of this header to avoid an Orden<->Radar include cycle,
// since Orden.h includes Radar.h).
//
// Descriptor / instance pools are reused exactly as the single-value Session,
// so a slot match is a lean index pair. GroupSessionManager mirrors
// SessionManager (same 300s expiry, coarse lock, V3-C view cache). Kept a
// SIBLING of SessionManager — not folded in — so the battle-tested single-value
// path is untouched.
// ============================================================

// One input value slot of a group scan. `targets` is the pre-parsed multi-width
// target set (BuildNumericTargets); `value` is the raw user string echoed back
// on the wire. P2: `st` is the per-slot predicate — a first-scan targeted type
// (Exact / Bigger / Smaller) on begin, plus the prev-value types (Changed /
// Unchanged / Increased / Decreased) on refine, where the compare is against
// each matched leaf's stored prevValue and `targets` is unused. `roundMode` is
// the float displayed-integer reduction (build 1672, replaces the old tolerance
// band); only meaningful for Float/Double leaves + fractional-target coercion.
struct SlotSpec {
    DataType         dt = DataType::NumericNoByte;  // per-slot meta or concrete width
    ScanType         st = ScanType::Exact;
    std::string      value;                          // original user value (display/echo)
    NumericTargetSet targets;                        // pre-parsed per fitting width
    RoundMode        roundMode = RoundMode::Round;   // float displayed-integer reduction
    std::string      value2;                         // Between upper bound (display/echo)
    NumericTargetSet targets2;                       // pre-parsed upper bound (Between only)
};

// One converging match of a slot inside a candidate object. `offset` is the byte
// offset from the owning object to the leaf value (== descriptor.fieldOffset for
// a direct field; a separate field so containers can carry an element-address
// offset later). `prevValue` is the last-observed bytes at that leaf so the wire
// can render it and a future prev-value refine can compare.
struct GroupSlotMatch {
    uint32_t  descriptorIdx = 0;   // -> GroupSession::descriptors
    int32_t   elementIndex  = -1;  // -1 = direct field; >=0 = container element index
    int32_t   offset        = 0;   // bytes from the OWNING object to the leaf (direct); 0 for deep
    uintptr_t leafAddr      = 0;   // ABSOLUTE address of the value — direct: owner+offset;
                                   // deep (container element): the element's own address.
                                   // Refine re-reads this. ⚠ "stale on realloc = drop" was
                                   // OPTIMISTIC and is corrected here (audit #5 A11): a TArray
                                   // RemoveAt relocates the tail in place and a sparse slot is
                                   // REUSED on the next Add, so a stale element address survives
                                   // the read and returns the WRONG value. `anchor` below is what
                                   // lets refine re-read the container instead of trusting this
                                   // (audit #5 A12).
    uintptr_t ownerAddr     = 0;   // object directly holding the leaf: the candidate actor for an
                                   // own-block leaf, or an OWNED sub-object for a cross-object leaf
                                   // (P4). Drives the per-slot handoffs to the right object.
    std::string ownerClass;        // class name of ownerAddr's object: the candidate class for an
                                   // own-block leaf, the OWNED sub-object's class for a cross-object
                                   // leaf (P4 inc 2). Drives the per-slot Pivot handoff to the right class.
    // audit #5 A12 — the container this leaf lives in, as it was AT SCAN TIME, so refine
    // can re-read the header and re-anchor instead of trusting `leafAddr`. Defaults to
    // `Unknown`/-1, which the rule refuses to act on: this struct is default-initialised
    // at its producer, so a hop that drops the copy must degrade, never mis-fire.
    LeafAnchor anchor;
    uint8_t   prevValue[16] = {};  // last-observed leaf bytes
};

// One group candidate: an owning object plus, per slot, the convergence list of
// matches. slotMatches.size() == session.slots.size(); every inner list is
// non-empty while the candidate lives (an emptied slot drops the candidate).
struct GroupCandidate {
    uint32_t                                 instanceIdx = 0;  // -> GroupSession::instances
    std::vector<std::vector<GroupSlotMatch>> slotMatches;
};

// A group session's persistent leaf memory is the PRODUCT of three factors:
// candidates x slots x per-slot leaves (each a GroupSlotMatch, ~120 B + a string).
// Two are clamped — slots to 2..4 and the per-slot cap to [8, 4096] — but the
// candidate count rides `max_results`, which Fern does NOT clamp, so the product
// has no upper bound and a broad scan on a large game could hold multiple GB inside
// the target process (audit #5 AB19: the per-slot clamp's own comment names "x every
// candidate" as the hazard it does not cover). These pure helpers give the session
// manager a total-leaf backstop that bounds the THIRD factor's contribution.

// Total GroupSlotMatch leaf objects held across a candidate pool. Pure.
size_t GroupSessionLeafCount(const std::vector<GroupCandidate>& candidates);

// How many LEADING candidates (scan order) fit within `budgetLeaves` total leaves.
// Always keeps at least one candidate when the pool is non-empty (a single
// candidate is itself bounded by slots x perSlotCap and cannot exceed the budget).
// Returns candidates.size() when the whole pool fits. Pure / testable.
size_t GroupCandidatesWithinLeafBudget(const std::vector<GroupCandidate>& candidates,
                                       size_t budgetLeaves);

// Total-leaf backstop for a retained group session. ~120 B per GroupSlotMatch, so
// 4,000,000 leaves is roughly half a GB of leaf records — far above any realistic
// scan (tens of thousands of leaves) yet well under an OOM. It bounds the retained
// session even when an unclamped max_results asked for the entire object pool.
inline constexpr size_t kMaxGroupSessionLeaves = 4'000'000;

struct GroupSession {
    uint64_t                              id = 0;
    std::vector<SlotSpec>                 slots;
    std::vector<GroupCandidate>           candidates;
    std::vector<FieldDescriptor>          descriptors;  // shared via GroupSlotMatch::descriptorIdx
    std::vector<InstanceRecord>           instances;    // shared via GroupCandidate::instanceIdx
    std::chrono::steady_clock::time_point lastUse;
    // Candidates the memory backstop dropped at Begin (audit #5 AB19). Non-zero only
    // when a session exceeded kMaxGroupSessionLeaves total leaves — normally 0. Set
    // by GroupSessionManager::Begin; a future Fern change can surface it on the wire
    // beside `total` so the trim is never fully silent.
    size_t                                candidatesDroppedForMemory = 0;

    // V3-C cached ordered view (same contract as Session::view*).
    bool                                  viewValid    = false;
    std::string                           viewFilter;
    uint8_t                               viewSortKey  = 0;
    bool                                  viewSortDesc = false;
    std::string                           viewExclude;  // canonical key of excluded classes
    std::vector<uint32_t>                 viewOrder;
};

// Build the display order over a group-candidate pool: keep candidates whose
// owning class / instance / any slot field-name or value contains `filter`
// (case-insensitive; "" keeps all), then stable-sort by (sortKey, sortDesc).
// Object-level keys (ClassName / InstanceName / InstanceIndex) plus slot-0's
// Offset / Value are supported; unsupported keys fall back to scan order. Pure /
// std-only. Returns candidate indices in display order.
// Exposed so the row RENDERER can agree with the FILTER. BuildGroupOrderedView walks
// every leaf of every slot; the JSON row used to show slotMatches[0]. Filtering for a
// value therefore returned rows that did not visibly contain it. Same helpers, one
// answer. `needleLower` must already be lower-cased (ToLowerAscii).
bool GroupTextContainsCI(const std::string& hay, const std::string& needleLower);
std::string GroupSlotValueString(const GroupSlotMatch& sm, const SlotSpec& spec,
                                 const std::vector<FieldDescriptor>& descriptors);
std::string ToLowerAscii(const std::string& s);

// True when a rendered leaf value is numerically zero ("0", "0.0", "-0.00", and
// the vector form "0, 0, 0"). Text that is not a number at all returns false.
//
// A zero carries almost no information in a game: engine bookkeeping fields sit at
// 0 by default, so a row reading `PrimaryActorTick.TickInterval=0,
// InitialLifeSpan=0` is the least useful pair a candidate can show while its real
// fields matched too. Used as a tie-break INSIDE each witness-selection rule, never
// as a rule of its own — an explicitly filtered-for zero must still win.
bool IsZeroValueText(const std::string& s);

// Split a group filter into whitespace-separated, lower-cased terms.
//
// **space = AND** — the repo-wide keyword-box rule (CLAUDE.md): terms are ANDed,
// fields are ORed. The group filter was the last box treating its input as one
// substring, which made a two-field request impossible to express: a scan can hold
// 2 x 36 = 72 valid pairings and nothing distinguishes `FrozenInt` from the other
// 35 unchanged fields, so naming BOTH fields is the only way to ask for a specific
// pairing. `tickcount frozenint` now means "keep candidates where some field
// matches each", and the witness picker gives each slot one of the named fields.
std::vector<std::string> SplitFilterTerms(const std::string& filter);

// Choose ONE leaf per slot so a displayed row is a genuine ASSIGNMENT: no two
// slots may show the same leaf. Returns one index into `slotMatches[s]` per slot
// (0 for an empty slot, which cannot happen while a candidate lives).
//
// Three passes per slot, in order:
//   1. a free leaf matching one of the filter `terms` (already lower-cased; empty
//      = no filter). A term an EARLIER slot already used is tried last, so
//      `tickcount frozenint` puts TickCount in one slot and FrozenInt in the
//      other instead of both slots fighting over the first term. This is the only
//      way to ask for a SPECIFIC pairing: with 2 x 36 kept leaves there are 72
//      valid ones and nothing in the data distinguishes the one you meant,
//   2. a free leaf DEFINED BY THE SAME class/struct as a leaf already chosen for
//      an earlier slot — a group scan looks for values that belong TOGETHER, so
//      `Health.CurrentValue` beside `Health.BaseValue` beats the same value
//      beside `PrimaryActorTick.TickInterval`,
//   3. any free leaf.
//
// The three passes are greedy (they never revisit an earlier slot), so they can
// seat two slots on one leaf where a proper matching exists — a duplicate the
// group-scan contract forbids. A final augmenting-path repair (audit #5 AB18)
// fixes exactly that: it keeps each slot's greedy pick when already distinct and
// otherwise bumps an earlier slot to a less-preferred leaf, guaranteeing a
// distinct assignment whenever one exists (the caller established via
// HasDistinctAssignment that it does). Only if that precondition is violated can
// a duplicate remain.
//
// WHY THIS LIVES HERE and not in the wire encoder. Every earlier version of this
// rule was written inside Fern.cpp's JSON builder, where no test could reach it
// and where it could silently disagree with BuildGroupOrderedView — which walks
// EVERY leaf of every slot. Four separate "the scan missed my field" reports were
// all that one disagreement. Same file as the filter, same helpers, one answer.
//
// It still shows only ONE of the possible assignments; `query_group_slot_leaves`
// is what surfaces the rest.
std::vector<size_t> PickGroupWitnessAssignment(
    const std::vector<std::vector<GroupSlotMatch>>& slotMatches,
    const std::vector<SlotSpec>&                   slots,
    const std::vector<FieldDescriptor>&            descriptors,
    const std::vector<std::string>&                terms);

// Display order for ONE slot's kept leaves: the fields the object's OWN class
// declares first, everything inherited or nested in a struct after. Stable within
// each tier, so scan order (which is offset order) survives.
//
// Leaves are collected base-class-first, so an actor's list opens with
// `PrimaryActorTick.TickInterval`, `InitialLifeSpan`, `CustomTimeDilation`,
// `AttachmentReplication.*` … and the fields a user was actually looking for —
// `FrozenInt`, `TickCount`, `I32` — sit thirty rows down a scrolling box. Same
// observation that made `perSlotCap = 8` hide every derived-class field (D2); this
// is its display half. NOT a re-ranking of the witness: ordering a list cannot take
// a pairing away from anyone.
//
// Tier is decided by the descriptor's own `definingClassName == className`, not by
// guessing from the offset — a high offset correlates with "declared late" but is
// not that predicate, and substituting a cheap proxy for a predicate the data
// already carries is how this area's last regression happened.
std::vector<size_t> OrderGroupSlotLeaves(
    const std::vector<GroupSlotMatch>&  matches,
    const std::vector<FieldDescriptor>& descriptors);

std::vector<uint32_t> BuildGroupOrderedView(
    const std::vector<GroupCandidate>&  candidates,
    const std::vector<SlotSpec>&        slots,
    const std::vector<FieldDescriptor>& descriptors,
    const std::vector<InstanceRecord>&  instances,
    const std::string& filter, SortKey sortKey, bool sortDesc,
    const std::unordered_set<std::string>& excludeClasses = {});

// Tally a group-candidate pool by the candidate's OBJECT-level class (first
// non-empty slot's first match descriptor className — same key BuildGroupOrdered-
// View sorts/filters on, NOT per-slot owner_class), sorted count desc / name asc.
// Computed over the whole pool. Pure / std-only.
std::vector<std::pair<std::string, int>> BuildGroupClassHistogram(
    const std::vector<GroupCandidate>&  candidates,
    const std::vector<FieldDescriptor>& descriptors);

// Sibling of SessionManager for group sessions. Same lifecycle/expiry/lock/view
// contract; the single-value SessionManager is left untouched.
class GroupSessionManager {
public:
    static GroupSessionManager& Instance();

    uint64_t Begin(std::vector<SlotSpec>        slots,
                   std::vector<GroupCandidate>  candidates,
                   std::vector<FieldDescriptor> descriptors,
                   std::vector<InstanceRecord>  instances);

    // Run `fn(GroupSession&)` under the lock; may prune candidates. Invalidates
    // the cached view afterwards.
    template <typename Fn>
    bool RefineWith(uint64_t sessionId, Fn&& fn) {
        std::lock_guard<std::mutex> lk(mu_);
        auto it = sessions_.find(sessionId);
        if (it == sessions_.end()) return false;
        it->second->lastUse = std::chrono::steady_clock::now();
        fn(*it->second);
        it->second->viewValid = false;
        // Reap OTHER idle group sessions (audit #5 AB17) — after the bump, so the
        // session being refined is protected.
        ExpireOldSessionsLocked();
        return true;
    }

    // V3-C window query — ensures the cached ordered view matches (filter,
    // sortKey, sortDesc), then `fn(const GroupSession&, const std::vector<uint32_t>&)`.
    template <typename Fn>
    bool QueryWith(uint64_t sessionId, const std::string& filter,
                   SortKey sortKey, bool sortDesc,
                   const std::vector<std::string>& excludeClasses, Fn&& fn) {
        std::lock_guard<std::mutex> lk(mu_);
        auto it = sessions_.find(sessionId);
        if (it == sessions_.end()) return false;
        GroupSession& s = *it->second;
        s.lastUse = std::chrono::steady_clock::now();
        const uint8_t keyRaw = static_cast<uint8_t>(sortKey);
        const std::string exclKey = CanonicalExcludeKey(excludeClasses);
        if (!s.viewValid || s.viewFilter != filter
            || s.viewSortKey != keyRaw || s.viewSortDesc != sortDesc
            || s.viewExclude != exclKey) {
            std::unordered_set<std::string> exclSet(excludeClasses.begin(), excludeClasses.end());
            s.viewOrder = BuildGroupOrderedView(s.candidates, s.slots, s.descriptors,
                                                s.instances, filter, sortKey, sortDesc, exclSet);
            s.viewFilter   = filter;
            s.viewSortKey  = keyRaw;
            s.viewSortDesc = sortDesc;
            s.viewExclude  = exclKey;
            s.viewValid    = true;
        }
        fn(static_cast<const GroupSession&>(s), s.viewOrder);
        return true;
    }

    // Read-only access to a whole session WITHOUT building or invalidating the
    // cached ordered view. Neither sibling is usable for a per-row detail fetch
    // that does not care about display order: QueryWith rebuilds the view
    // whenever the caller's (filter, sort, exclude) differ from the UI's current
    // ones, and RefineWith drops it outright — so either would throw away the
    // cached order every time a user expands a row.
    template <typename Fn>
    bool WithSession(uint64_t sessionId, Fn&& fn) {
        std::lock_guard<std::mutex> lk(mu_);
        auto it = sessions_.find(sessionId);
        if (it == sessions_.end()) return false;
        it->second->lastUse = std::chrono::steady_clock::now();
        fn(static_cast<const GroupSession&>(*it->second));
        return true;
    }

    bool End(uint64_t sessionId);
    void ExpireOldSessions();
    void DropAll();

private:
    GroupSessionManager() = default;
    GroupSessionManager(const GroupSessionManager&) = delete;
    GroupSessionManager& operator=(const GroupSessionManager&) = delete;

    // Sweep body assuming mu_ is held — see SessionManager::ExpireOldSessionsLocked.
    void ExpireOldSessionsLocked();

    static constexpr std::chrono::seconds kExpirySeconds = kScanSessionIdleExpiry;

    std::mutex                                                      mu_;
    std::unordered_map<uint64_t, std::unique_ptr<GroupSession>>     sessions_;
    uint64_t                                                        nextId_ = 1;
};

// Typed compare predicate. Returns true if rawBytes (size = SizeOf(dt))
// satisfies (scanType, targetBytes, target2Bytes) where target2Bytes is
// only consulted for ScanType::Between.
//
// For prev-value scan types (Changed / Unchanged / Increased / Decreased)
// targetBytes is the candidate's prevValue snapshot.
//
// `roundMode` is the displayed-integer reduction applied to Float/Double only
// (build 1672, replaces the old ± tolerance slack). Let r(x) = reduce(x, mode)
// (Round/Trunc/Ceil); `a` = target, `b` = target2, `c` = cur:
//   Exact      a whole: r(c) == a              (513 finds 513.36; Ceil-> 514 finds it)
//              a fractional: c == a             (precise intent — exact, no reduce)
//   Bigger     a whole: r(c) > a   else c > a
//   Smaller    a whole: r(c) < a   else c < a
//   Between    a,b whole: a <= r(c) <= b   else a <= c <= b  (literal range)
//   Changed    r(c) != r(prev)               (the DISPLAYED value changed)
//   Unchanged  r(c) == r(prev)
//   Increased  r(c) >  r(prev)
//   Decreased  r(c) <  r(prev)
// For Int8/16/32/64 + UInt8/16/32/64 + Bool the mode is a no-op (an integer
// reduces to itself), so integer compares stay strict — the mode only affects
// them upstream, when a FRACTIONAL target/bound is coerced to an integer in
// BuildNumericTargets / ParseValueBytes.
//
// Exposed for the dll_helpers_test unit suite; the scan + refine
// engines call this internally.
bool ComparePredicate(DataType dt, ScanType st,
                      const uint8_t* rawBytes,
                      const uint8_t* targetBytes,
                      const uint8_t* target2Bytes = nullptr,
                      RoundMode      roundMode     = RoundMode::Round);

// Entry-taking overload — the one the scan/refine engines must use (audit #5 AB4).
//
// It exists so the AlwaysTrue verdict is honoured in ONE place, here in Radar,
// rather than at each consumer. That split is deliberate: `Radar.cpp` is compiled
// by `dll_helpers_test` and `Aura.cpp` is compiled by no test target at all, so
// putting the decision here makes it unit-testable and leaves the engines with a
// mechanical `Find` -> `FindEntry` substitution that has nothing to get wrong.
//
// `target` may be null — that is "this width cannot satisfy the predicate", and
// the caller should already have skipped the field; returning false keeps it safe.
bool ComparePredicate(DataType dt, ScanType st,
                      const uint8_t* rawBytes,
                      const NumericTargetSet::Entry* target,
                      const uint8_t* target2Bytes = nullptr,
                      RoundMode      roundMode     = RoundMode::Round);

// String predicate. `cur` is the latest-observed string at the
// candidate field; `target` is either the user-supplied search string
// (targeted predicates) or the candidate's prevStr (Changed /
// Unchanged). Supports Exact / Contains / StartsWith / EndsWith /
// Changed / Unchanged; all other scan types return false.
//
// CE-style default is case-insensitive — `caseSensitive=true` keeps the
// raw comparison (used when the user explicitly opts in). Matching
// uses byte-level comparison after lowercasing ASCII letters — non-
// ASCII bytes (UTF-8 multibyte sequences) compare bitwise. This is
// sufficient for the cheat use cases that hit string scans (English
// item names, savegame keys, dialogue tags); a full Unicode case fold
// would need an ICU dependency we don't ship and isn't justified yet.
bool CompareStringPredicate(ScanType           st,
                            const std::string& cur,
                            const std::string& target,
                            bool               caseSensitive);

// Vector predicate over ALREADY-DECODED triples. `cur` is the value read from
// the candidate field, `target` the user's value, `target2` the second corner
// (Between only; may be null). All three are 3 doubles — decode the field's
// own bytes with DecodeVectorBytes(src, FieldDescriptor::vectorWidth, ...)
// first. Taking doubles rather than raw bytes is deliberate: the three buffers
// no longer share a width once LWC is in play (a 24-byte field compared against
// a target typed as text), so a byte-level predicate would need a width per
// argument and would silently mis-read if any of them drifted.
//
// Each axis runs the SAME displayed-integer-reduction compare as the scalar
// ComparePredicate (build 1672 — replaces the old per-axis ± tolerance), so a
// whole target axis matches the reduced axis value and a fractional target axis
// compares literally (positions typed as decimals keep full precision). The
// combine across axes:
//   Exact / Bigger / Smaller / Between / Unchanged  ALL three axes satisfy
//   Changed / Increased / Decreased                 ANY axis satisfies
// (game movement is rarely uniform across axes — match if ANY component moved.)
// Substring scan types reject for vectors (return false).
bool CompareVectorPredicate(ScanType      st,
                            const double* cur,
                            const double* target,
                            const double* target2  = nullptr,
                            RoundMode     roundMode = RoundMode::Round);

}  // namespace Radar
