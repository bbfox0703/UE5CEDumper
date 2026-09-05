#pragma once

// ============================================================
// Grimoire — 魔導書 (Book of Spells)
// Constants, magic strings, DynOff namespace
// ============================================================

#include <atomic>
#include <string>    // DynOff::LooksLikeFieldClassName / PickFFieldClassNameOffset
#include <cwchar>    // _wcsnicmp / _wcsicmp — IsCheatEngineExeName

namespace Grimoire {

// --- Logging ---
constexpr const wchar_t* LOG_FOLDER_NAME  = L"UE5CEDumper";
constexpr const wchar_t* LOG_SUBFOLDER    = L"Logs";
// Retention is AGE-based, not generation-based. The old fixed shuffle
// (-0 -> -1 -> ... -> -4, oldest deleted) could not express an age at all: it ran on
// every process start, so four launches of the same game in one afternoon discarded
// everything earlier regardless of date. See the retention block in Sein.cpp.
constexpr int            LOG_RETENTION_DAYS = 21;     // archived runs older than this are deleted
constexpr size_t         LOG_MAX_SIZE_MB  = 8;
constexpr size_t         LOG_MAX_SIZE     = LOG_MAX_SIZE_MB * 1024 * 1024;

// --- Hint Cache ---
constexpr const wchar_t* HINT_CACHE_PREFIX = L"UE5CEDumper";  // File: UE5CEDumper.{COMPUTERNAME}.json

// --- Named Pipe ---
constexpr const wchar_t* PIPE_NAME        = L"\\\\.\\pipe\\UE5DumpBfx";
constexpr const char*    PIPE_NAME_NARROW  = "\\\\.\\pipe\\UE5DumpBfx";
constexpr unsigned long  PIPE_BUF_SIZE    = 65536;
// Slice of Fern::Stop's 5 s connection-drain wait. Each slice re-asserts
// CancelIoEx on the surviving connections, because a single cancel fired before
// the wait can miss a thread that is BETWEEN two reads and will then park in a
// fresh, uncancelled ReadFile (Fern::ReadLine reads one byte per call, so every
// command offers many such gaps). 100 ms: 50 chances inside the budget, and the
// cost when nothing survives is zero — the loop exits on the first wait.
constexpr int            PIPE_STOP_CANCEL_REASSERT_MS = 100;

// --- Userspace pointer plausibility ---
// A candidate x64 pointer is "plausible userspace" iff it sits in the canonical
// low-half range [0x10000, 0x00007FFFFFFFFFFF]: above the first 64KB (never a valid
// heap/module address) and below the x64 user/kernel split. Used pervasively across
// the DLL as a pre-deref garbage/kernel-address guard.
// NOTE: 0x10000 also appears STANDALONE elsewhere with unrelated meanings (min module
// size in AOB scan, PropertiesSize / element-size sanity caps) — those are NOT pointer
// checks; only the paired [MIN, MAX] range test is.
constexpr uintptr_t PTR_USERSPACE_MIN = 0x10000;
constexpr uintptr_t PTR_USERSPACE_MAX = 0x00007FFFFFFFFFFF;
inline bool IsUserspacePointer(uintptr_t p) {
    return p >= PTR_USERSPACE_MIN && p <= PTR_USERSPACE_MAX;
}

// --- UObject offsets ---
// UObjectBase layout: VTable(8) + Flags(4) + Index(4) + Class*(8) + FName(?) + Outer*(8)
// Most offsets are stable, but Outer shifts when CasePreservingName is active (FName = 0x10):
//   Standard (UE4.25-UE5.4, UE5.5+ non-CPN): Outer = 0x20
//   CasePreservingName (UE4.27-CPN):          Outer = 0x28
// NamePrivate at 0x18 reads ComparisonIndex (first 4 bytes), stable regardless of FName size.
constexpr int OFF_UOBJECT_VTABLE       = 0x00;
constexpr int OFF_UOBJECT_FLAGS        = 0x08;
constexpr int OFF_UOBJECT_INDEX        = 0x0C;
constexpr int OFF_UOBJECT_CLASS        = 0x10;
constexpr int OFF_UOBJECT_NAME         = 0x18;

// --- UStruct / FField / FProperty offsets (runtime-detected) ---
// ValidateAndFixOffsets() dynamically detects all offsets below.
// Defaults match UE5.0-5.1 layout (FFieldVariant=0x10 bytes).
// UE5.1.1+ uses FFieldVariant=0x08 bytes, shifting FField::Next/Name/etc by -8.
//
// Version differences (from RE-UE4SS MemberVarLayoutTemplates):
//   UE5.0-5.1.0: FFieldVariant=0x10 → Next=0x20, Name=0x28, Offset_Internal=0x4C
//   UE5.1.1-5.5: FFieldVariant=0x08 → Next=0x18, Name=0x20, Offset_Internal=0x44
// UStruct offsets (Super, Children, ChildProperties) are stable: 0x40/0x48/0x50.
//
// UE4 differences:
//   UE4 <4.25:   No FField/FProperty, properties are UProperty (UObject-derived) in Children chain
//   UE4.25-4.27: FField/FProperty exists, layout similar to UE5.0-5.1 (FFieldVariant=0x10)
//   UE4.27-CPN:  FName=0x10 bytes, shifts FField::Flags+0x8, FFieldClass offsets+0x8,
//                 and UObject::Outer from 0x20 to 0x28

} // namespace Grimoire

namespace DynOff {

// === UObject — runtime-detected ===
// Most are stable, but Outer shifts when CasePreservingName enlarges FName.
inline int UOBJECT_OUTER      = 0x20;  // OuterPrivate: 0x20 (standard), 0x28 (CPN)

// === UStruct — stable across UE4.25+ and UE5.0-5.5 ===
inline int USTRUCT_SUPER      = 0x40;
inline int USTRUCT_CHILDREN   = 0x48;  // UField* chain (functions; in UE4 <4.25: all properties here)
inline int USTRUCT_CHILDPROPS = 0x50;  // FField* chain (properties; absent in UE4 <4.25)
inline int USTRUCT_PROPSSIZE  = 0x58;
// UStruct::Script — TArray<uint8> Kismet bytecode. Always sits immediately
// after PropertiesSize(int32) + MinAlignment(int32), so == PROPSSIZE + 0x08 for
// every UE 4.18-5.7 layout and every shifted custom-game layout (verified vs
// RE-UE4SS MemberVariableLayout templates). Set in Genau from the calibrated
// PROPSSIZE; default mirrors the UE4.25+/UE5 standard (0x58 + 8 = 0x60).
inline int USTRUCT_SCRIPT     = 0x60;

// === UFunction::Func — native exec-thunk pointer (Path 2 disassembly) ===
// FNativeFuncPtr at the tail of UFunction. For native functions it points at
// the execXxx thunk in .text (what Denken disassembles); for script functions
// it points at UObject::ProcessInternal. Position trails FunctionFlags + the
// RPC ids + FirstPropertyToInit and is version-dependent, so it is detected
// lazily on first Path-2 use (Aura::DetectUFunctionFuncOffset) by finding the
// offset that holds an in-module code pointer across several sampled native
// UFunctions. 0 = not found → Path 2 native analysis disabled (the Path 1
// bytecode path is unaffected).
inline int UFUNCTION_FUNC     = 0;
// Latched once detection has run (success or failure), to avoid re-sampling.
inline std::atomic<bool> bUFunctionFuncDetected{false};

// === FField — defaults for UE5.0-5.1.0 (FFieldVariant=0x10) ===
// UE5.1.1+ shifts these: Next=0x18, Name=0x20
inline int FFIELD_CLASS       = 0x08;  // FFieldClass* — stable
inline int FFIELD_OWNER       = 0x10;  // FFieldVariant Owner — stable position, variable size
inline int FFIELD_NEXT        = 0x20;  // FField* next in chain
inline int FFIELD_NAME        = 0x28;  // FName

// === FProperty (inherits from FField) — defaults for UE5.0-5.1.0 AND UE4.25-4.27 ===
// (both have FFieldVariant = 0x10, so FField is 0x38 and FProperty's own fields follow it)
// UE5.1.1+ shifts these: ElemSize=0x34, Flags=0x38, Offset=0x44
//
// ElementSize is 0x3C, NOT 0x38 — 0x38 is ArrayDim. Verified against the DropIn 4.27.2 PDB:
// ArrayDim@0x38, ElementSize@0x3C, PropertyFlags@0x40, Offset_Internal@0x4C. The old 0x38
// here was internally inconsistent with FPROPERTY_FLAGS (0x38 + 4 != 0x40) and, when the
// dynamic offset validation failed and these defaults were used, read ArrayDim as the
// element size.
inline int FPROPERTY_ELEMSIZE = 0x3C;
inline int FPROPERTY_FLAGS    = 0x40;  // uint64 PropertyFlags
inline int FPROPERTY_OFFSET   = 0x4C;  // int32 Offset_Internal

// === FFieldClass — PROBED, not stable ===
// `FName Name` IS the first declared member, but UE 5.8 made ~FFieldClass() virtual
// (Field.h:101 @5.8.0-release; non-virtual at 5.7.4-release:100). FFieldClass has no
// base class, so the vfptr lands at +0x00 and EVERY member shifts +8 — Name is +0x00
// up to UE 5.7 and +0x08 from UE 5.8.
//
// This one constant cost a full RE session, because it was the ONLY UE offset never
// dynamically verified while every other one self-corrects. When it is wrong the
// damage is total but silent: the ChildProperties probe identifies a candidate by
// resolving this name, so it matches nothing at ANY offset (-> "keeping defaults"),
// and the walker types every property as unknown -> Scalar (-> no drill-down).
// Latched by the JOINT (ChildProperties, FFieldClass::Name) probe in
// Genau::ValidateAndFixOffsets — it must be decided inside that loop, because that
// loop is what discovers ChildProperties in the first place. No later phase can set it.
inline int FFIELDCLASS_NAME   = 0x00;  // pre-probe default = the <=5.7 layout

// Candidate order is LOAD-BEARING. 0x00 is tried first with unchanged semantics, so
// every game that works today latches exactly as it does today and cannot regress.
inline constexpr int kFFieldClassNameProbes[] = { 0x00, 0x08 };

// True iff `s` is a plausible FFieldClass type name. Every FFieldClass name in the
// engine ends in "Property" (IntProperty, ObjectProperty, ...), so a SUFFIX test is
// strictly stronger than a substring find and is what makes the new 0x08 candidate
// safe — at +0x08 on a <=5.7 build sits EClassFlags, and a substring test on garbage
// is far likelier to false-positive than a suffix test.
inline bool LooksLikeFieldClassName(const std::string& s) {
    return s.size() > 8 && s.size() <= 64 &&
           s.compare(s.size() - 8, 8, "Property") == 0;
}

// Pure decision core, reader-templated so it is unit-testable without a live process.
// `resolve(off)` returns the string the FName at fieldClass+off decodes to.
// Returns the winning offset, or -1 if neither candidate looks like a type name.
template <class Resolve>
inline int PickFFieldClassNameOffset(Resolve&& resolve) {
    for (int off : kFFieldClassNameProbes) {
        const std::string n = resolve(off);
        // 0x00 keeps the historical substring semantics so behaviour on every
        // already-working title is bit-identical; 0x08 requires the stricter suffix.
        if (off == 0x00) { if (n.find("Property") != std::string::npos) return off; }
        else             { if (LooksLikeFieldClassName(n))              return off; }
    }
    return -1;
}

// === FStructProperty (subclass of FProperty) ===
// UScriptStruct* — first field after FProperty base layout.
// Derived from FPROPERTY_OFFSET + 0x2C (UE5.0: 0x78, UE5.1.1+: 0x70).
inline int FSTRUCTPROP_STRUCT = 0x78;

// === FArrayProperty (subclass of FProperty) ===
// FProperty* Inner — element type descriptor, same offset as FSTRUCTPROP_STRUCT.
// Both are the first subclass field after FProperty base layout.
inline int FARRAYPROP_INNER   = 0x78;

// === FBoolProperty layout (subclass of FProperty) ===
//   uint8 FieldSize, ByteOffset, ByteMask, FieldMask
// These 4 bytes are consecutive, located after the standard FProperty fields.
// Same offset as FSTRUCTPROP_STRUCT for most builds.
inline int FBOOLPROP_FIELDSIZE = 0x78;

// === UE4 UBoolProperty layout (subclass of UProperty) ===
// Same 4 bytes: FieldSize, ByteOffset, ByteMask, FieldMask
// Offset from UBoolProperty base differs from FBoolProperty because
// UProperty (UObject-derived) has a larger base than FProperty (FField-derived).
// Typical UE4.22: 0x70, may vary ±0x08 by version.
inline int UBOOLPROP_FIELDSIZE = 0x70;

// === TPersistentObjectPtr envelope — where the payload sits inside a soft/lazy ptr ===
//
//   TSoftObjectPtr = TPersistentObjectPtr<FSoftObjectPath>
//   TLazyObjectPtr = TPersistentObjectPtr<FUniqueObjectGuid>   (FUniqueObjectGuid = { FGuid })
//
// UE ≤ 5.2 — and every UE4 (verified at 4.18, 4.27-plus, 5.2.1-release:243):
//     +0x00  FWeakObjectPtr WeakPtr      (2×int32, so 8 bytes at alignof 4)
//     +0x08  mutable int32 TagAtLastTest
//     +0x0C  payload, at the payload's OWN alignment
// UE ≥ 5.3 deleted TagAtLastTest (absent at 5.3.2-release:228, and at 5.4/5.6/5.8),
// so the payload moves up — by 8 for an 8-aligned payload, by only 4 for a 4-aligned one:
//
//   payload             align   ≤5.2    ≥5.3     sizeof ≤5.2 / ≥5.3
//   FSoftObjectPath       8     0x10    0x08     0x30 (5.1-5.2) or 0x28 (≤5.0) / 0x28
//   FUniqueObjectGuid     4     0x0C    0x08     0x1C              / 0x18
//
// ⚠ 0x10 is right for soft ONLY up to 5.2, and is right for lazy in NO era. Both were
// hardcoded 0x10 before this existed, so every UE 5.3+ title read the soft path one
// field late and every title read the lazy GUID 4 bytes into it.
//
// MEASURED, not version-gated: the property's own ElementSize already reaches us, and a
// misdetected version is precisely the case where a hardcoded offset does the most harm.
// -1 = not yet measured; Ubel falls back to a version-derived value until a real
// property is seen. See PersistentObjectPtrEnvelope() in Ubel.cpp.
inline int SOFTPTR_PATH  = -1;   // FSoftObjectPath offset inside TSoftObjectPtr
inline int LAZYPTR_GUID  = -1;   // FGuid           offset inside TLazyObjectPtr

// The pure-arithmetic half of that decision, in the header so the test target can
// pin it — the tests link headers, not Ubel.cpp, so a rule left in the .cpp is a
// rule nothing measures. Ubel owns the latching and the log line; this owns the
// numbers. `latched` < 0 means "nothing measured yet".
//
// ⚠ elemSize is deliberately NOT matched against a table of whole sizes: 0x28 is
// both a UE ≤ 5.0 tagged soft pointer (0x10 envelope + 0x18 FName/FString path)
// and a UE ≥ 5.3 untagged one (0x08 + 0x20 FTopLevelAssetPath path). Subtracting
// the payload size — which the caller knows from the FTopLevelAssetPath form —
// is what makes the answer unique.
constexpr int PersistentPtrEnvelopeFor(int elemSize, int payloadSize,
                                       int taggedEnvelope, int latched,
                                       unsigned ueVersion) {
    if (elemSize > payloadSize) {
        const int candidate = elemSize - payloadSize;
        // Only the two shapes the engine can produce. Any other difference means
        // payloadSize is wrong for this build, and a bogus "measurement" is worse
        // than the default.
        if (candidate == taggedEnvelope || candidate == 0x08) return candidate;
    }
    if (latched >= 0) return latched;
    return (ueVersion >= 503) ? 0x08 : taggedEnvelope;
}

// === UE4 UProperty offsets (UProperty inherits UObject → UField → UProperty) ===
// Used when bUseFProperty == false (UE4 <4.25).
// UField::Next is at UObject_TotalSize (0x28 or 0x30 for CPN).
inline int UFIELD_NEXT        = 0x28;  // UField::Next (standard): 0x28
inline int UPROPERTY_OFFSET   = 0x44;  // UProperty::Offset_Internal
inline int UPROPERTY_ELEMSIZE = 0x34;  // UProperty::ElementSize
inline int UPROPERTY_FLAGS    = 0x38;  // UProperty::PropertyFlags (uint64)

// === FEnumProperty / FByteProperty subclass fields ===
// Both store UEnum* at the same offset relative to FProperty base.
// Derived from FSTRUCTPROP_STRUCT (same subclass extension offset).
inline int FBYTEPROP_ENUM       = 0x78;  // FByteProperty::Enum (UEnum*) — first subclass field (== sizeof(FProperty))
// FEnumProperty has FNumericProperty* UnderlyingProp BEFORE its UEnum* Enum, so Enum sits
// 8 bytes after the FByteProperty position. Verified vs UE5.7.4 EnumProperty.h:143-144.
// (Detection keeps this = FBYTEPROP_ENUM + 8; default mirrors that.)
inline int FENUMPROP_ENUM       = 0x80;  // FEnumProperty::Enum (UEnum*) = FBYTEPROP_ENUM + 8

// ── The sizeof(FProperty) subclass-extension FAMILY (audit #5 G12) ───────────
//
// FSTRUCTPROP_STRUCT / FARRAYPROP_INNER / FBOOLPROP_FIELDSIZE / FBYTEPROP_ENUM all name
// the SAME slot — the first subclass field, i.e. sizeof(FProperty) — and FENUMPROP_ENUM
// sits 8 bytes later because FEnumProperty declares FNumericProperty* UnderlyingProp
// before its UEnum* (UE5.7.4 EnumProperty.h). They are five names for one measurement and
// must move together.
//
// ⚠ WHY THIS HELPER EXISTS. They did NOT move together. Genau's Step 2.5 default block set
// only FSTRUCTPROP_STRUCT and FBOOLPROP_FIELDSIZE to 0x70 and left the other three at the
// UE5.0-era 0x78/0x78/0x80 — so any run that took one of the three "keeping defaults" exit
// paths shipped a SPLIT family for the whole session: TArray element descriptors and every
// enum-name read 8 bytes off, while struct reads were correct. Deterministic, first run, no
// concurrency needed. `docs/test-games.md` records Solarpunk resolving via exactly that
// heuristic fallback with FProperty::Offset +0x44.
//
// Both writers now go through here so the two cannot drift again. Pure and constexpr, so
// dll_helpers_test can pin the invariant — which matters because no test target compiles
// Genau.cpp.
struct PropertyFamily {
    int structProp;     // FStructProperty::Struct
    int arrayInner;     // FArrayProperty::Inner
    int boolFieldSize;  // FBoolProperty::FieldSize
    int byteEnum;       // FByteProperty::Enum
    int enumEnum;       // FEnumProperty::Enum  (== byteEnum + 8)
};

inline constexpr PropertyFamily PropertyFamilyAtBase(int base) {
    return PropertyFamily{ base, base, base, base, base + 8 };
}

// `propOffsetOff` is FProperty::Offset_Internal's offset; the subclass extension begins
// 0x2C past it on every UE4.25-5.8 layout measured.
inline constexpr PropertyFamily PropertyFamilyFor(int propOffsetOff) {
    return PropertyFamilyAtBase(propOffsetOff + 0x2C);
}

// Publish all five together. Never assign a member of this family directly.
inline void ApplyPropertyFamily(const PropertyFamily& f) {
    FSTRUCTPROP_STRUCT  = f.structProp;
    FARRAYPROP_INNER    = f.arrayInner;
    FBOOLPROP_FIELDSIZE = f.boolFieldSize;
    FBYTEPROP_ENUM      = f.byteEnum;
    FENUMPROP_ENUM      = f.enumEnum;
}

// === UEnum — lazy-detected by DetectUEnumNames() ===
inline int UENUM_NAMES          = 0x40;  // UEnum::Names (Neu::EnumNamesLayout region offset)
inline int UENUM_ENTRY_SIZE     = 0x10;  // legacy sizeof(TPair<FName,int64>) = 8+8 = 16 bytes
// UE5.6+ replaced the interleaved TArray<TPair<FName,int64>> at UENUM_NAMES with the
// FNameData struct-of-arrays {tagged FName*, tagged int64*, int32 NumValues}. Set by
// DetectUEnumNames (try-both); the enum reader (Ubel) branches on it. Written before the
// bUEnumNamesDetected release-store, so plain bool (same pattern as bCasePreservingName).
inline bool bEnumNamesNewContainer = false;
// bUEnumNamesDetected uses release/acquire like bOffsetsValidated.
inline std::atomic<bool> bUEnumNamesDetected{false};
// Set when detection was attempted but FAILED — prevents retry storm and
// skips enum resolution entirely (raw int values shown instead).
inline std::atomic<bool> bUEnumNamesFailed{false};

// === Detection state ===
inline bool bCasePreservingName  = false;  // FName is 0x10 bytes (CompIdx + DisplayIdx + Number + pad)
inline bool bUseFProperty        = true;   // true = FField/FProperty (UE4.25+), false = UProperty (UE4 <4.25)
inline bool bTaggedFFieldVariant = false;  // UE5.3+: FFieldVariant is 0x08 tagged ptr (LSB=1 → UObject)
// TWO flags, deliberately. They used to be one, which reported a run as
// "validated=yes" three lines after logging "Offset validation failed — using default
// offsets" (seen on UE 5.8). A user reading the honest-looking summary had no way to
// know the walker was about to be useless.
//
//   bOffsetsProbeRan   — "detection executed and DynOff is settled" (success OR give-up)
//   bOffsetsValidated  — "the values were actually MEASURED and are trustworthy"
//
// The split is mandatory, not cosmetic: FindGEngineSlot / ResolveGEngineDeferred gate on
// the flag purely to order themselves AFTER offset detection, and on the 5.8 run the old
// (bogus) store-true on the give-up path was the ONLY reason &GEngine resolved at all.
// Those gates therefore take bOffsetsProbeRan — flipping them to the strict flag would
// silently regress &GEngine on exactly the builds this split exists to expose.
//
// Both are atomic with release/acquire ordering: the release-store after writing all
// DynOff values fences the preceding non-atomic writes, so any thread that acquire-loads
// either flag and sees 'true' also sees the values.
inline std::atomic<bool> bOffsetsProbeRan{false};
inline std::atomic<bool> bOffsetsValidated{false};

// Why the probe fell back to defaults, for the summary and the pipe. String literals
// only — never a heap string, so there is no lifetime question across threads.
inline const char* g_offsetsFallbackReason = "";

// Strip the FFieldVariant tag bit (LSB) from a pointer if we're on UE 5.3+.
// On UE 5.3+, FFieldVariant stores type info in the LSB:
//   bit 0 = 0 → FField*, bit 0 = 1 → UObject*
// Applied defensively to FField-related pointer reads to prevent misreads
// if an offset probe lands on a tagged Owner field.
inline uintptr_t StripFFieldTag(uintptr_t ptr) {
    return bTaggedFFieldVariant ? (ptr & ~static_cast<uintptr_t>(1)) : ptr;
}

// Check if a tagged FFieldVariant pointer is a UObject (LSB set).
inline bool IsFFieldVariantUObject(uintptr_t ptr) {
    return bTaggedFFieldVariant && (ptr & 1) != 0;
}

} // namespace DynOff

namespace Grimoire {

// --- Object Array ---
// Oldest engine this dumper can read. UE 4.11 is where FUObjectItem appears (16 bytes:
// Object / ClusterAndFlags / SerialNumber). 4.10 and earlier store raw UObjectBase* at stride 8
// in a TStaticIndirectArrayThreadSafeRead whose chunk table is INLINE — a shape ArrayLayout
// cannot express. Read off Epic's source at tags 4.10.2-release / 4.11.0-release.
constexpr uint32_t MIN_SUPPORTED_UE_VERSION = 411;

// Sentinel UEVersion meaning "POSITIVELY IDENTIFIED as pre-UE4" (Unreal Engine 3 or older),
// as distinct from "detection found nothing, so we are guessing". Fits the existing
// major*100+minor convention so it reads as UE 3.0 and sorts below MIN_SUPPORTED_UE_VERSION —
// which is the whole point: the existing too-old gate fires on it with no new flag to plumb,
// and the value survives the HintCache round-trip for free (a bool computed inside detection
// would be absent on launch 2, because a cache hit skips detection entirely).
//
// A pre-UE4 engine is not "a version we are behind on", it is a DIFFERENT OBJECT MODEL: no
// FUObjectArray, no FUObjectItem, no FNamePool, and UObject::Class / UObject::Name are not at
// the offsets every scan validator hardcodes (measured on a UE3 shipping binary: Outer @+0x40,
// Class @+0x50, vs OFF_UOBJECT_CLASS = 0x10 here). So ValidateCyclicClassChain rejects even a
// CORRECT UE3 GObjects address, and neither a UE-version override nor an Extra Scan can bridge
// it. Skipping the scan and saying so is the only honest answer.
constexpr uint32_t PRE_UE4_SENTINEL_VERSION = 300;

constexpr int OBJECTS_PER_CHUNK        = 64 * 1024;

// --- FNamePool ---
constexpr int FNAME_CHUNK_SIZE         = 0x20000;  // 128 KB per chunk
constexpr int FNAME_STRIDE             = 2;         // Alignment stride

// --- Teleport (Wirbel) — docs/teleport-spec.md ---
constexpr int    TELEPORT_SLOTS           = 3;       // marker slots
constexpr double TELEPORT_DEFAULT_ZOFFSET = 100.0;   // ≈ capsule half height + margin
constexpr double TELEPORT_TRACE_DIST      = 100000.0;// screen-center ray length (1 km)
constexpr int    TELEPORT_MAPNAME_CAP     = 128;     // marker map-name buffer size

// --- GodMode (Solitar) — docs/godmode-spec.md ---
constexpr int    PROTECT_REASSERT_MS      = 300;     // re-assert worker tick (write-on-drift)

// --- Movement tuning (Laufen) — Super Jump / Gravity / Move Speed ---
constexpr int    MOVE_REASSERT_MS         = 250;     // re-assert worker tick (write-on-drift)
constexpr double MOVE_MULT_MIN            = 0.1;     // 10%  — UI slider floor
constexpr double MOVE_MULT_MAX            = 10.0;    // 1000% — UI slider ceiling

// --- Time dilation (Hemmung) — global slow-mo / freeze-time / speed-up ---
constexpr int    TIME_REASSERT_MS         = 250;     // re-assert worker tick (write-on-drift)
constexpr double TIME_DILATION_MIN        = 0.0;     // 0 = frozen (near-freeze if the game NaNs on exact 0)
constexpr double TIME_DILATION_MAX        = 100.0;   // 100x — DLL safety clamp (UI ceilings are lower: world 3x, pawn 10x)

// --- Force-field hold (Solide) — Property Search "Force" + stealth-meter zero ---
constexpr int    SOLIDE_REASSERT_MS       = 300;     // re-assert worker tick (write-on-drift)
constexpr int    SOLIDE_MAX_INSTANCES     = 256;     // per-job pool cap (bounded FindInstancesByClass)

// --- Fly (Dunste) — no-gravity 3D flight ---
constexpr int     FLY_TICK_MS            = 16;       // fly worker tick (~60 Hz)
constexpr double  FLY_SPEED_MIN          = 50.0;     // uu/s — UI slider floor
constexpr double  FLY_SPEED_MAX          = 20000.0;  // uu/s — UI slider ceiling
constexpr double  FLY_SPEED_DEFAULT      = 1200.0;   // uu/s (~2× default walk speed)
constexpr double  FLY_TURN_DEG_PER_S     = 120.0;    // yaw rate for the turn keys (deg/s)
constexpr uint8_t MOVE_FLYING            = 5;        // EMovementMode::MOVE_Flying (UE4/UE5 stable)

// --- See-through occluders (Schlacht) ---
constexpr int     SCHLACHT_TICK_MS       = 100;      // occluder worker tick (~10 Hz — hiding actors doesn't need 60 Hz)
constexpr uint8_t SCHLACHT_TRACE_CHANNEL = 0;        // ETraceTypeQuery index (0 = TraceTypeQuery1 == Visibility on stock projects)
constexpr double  SCHLACHT_TRACE_DIST    = 100000.0; // uu — camera-forward ray length (LineTraceSingle returns the NEAREST hit)
constexpr double  SCHLACHT_TRACE_STEP    = 2.0;      // uu — advance the ray start just past each hit surface (pierce loop)
constexpr int     SCHLACHT_PIERCE_DEFAULT = 1;       // hide this many nearest occluders by default
constexpr int     SCHLACHT_PIERCE_MAX     = 10;      // UI/clamp ceiling for the pierce depth
constexpr int     SCHLACHT_MAX_EXTRA_ITERS = 16;     // extra trace iterations beyond pierceN (skipped Pawns / dupes)
} // namespace Grimoire

// ============================================================
// Host-process identification
// ============================================================

/// True when `exeLeafName` is a Cheat Engine executable. Cheat Engine is NEVER a scan
/// target: if our DLL is loaded into it (as a not-yet-enabled plugin, or injected by
/// hand) the auto-start path must refuse rather than AOB-scan CE and open the game pipe
/// inside it.
///
/// **A PREFIX test on purpose.** The first version of this guard was an exact-name list
/// — `cheatengine-x86_64.exe`, `cheatengine-i386.exe`, `Cheat Engine.exe` — and a live
/// capture then named the real executable **`cheatengine-x86_64-SSE4-AVX2.exe`**, which
/// matched none of them. CE ships several CPU-feature variants and can add more; what is
/// stable is the stem. Matching the stem also covers `cheatengine-i386.exe`,
/// `cheatengine-x86_64.exe` and any future `-AVX512`-style suffix without another edit.
///
/// Case-insensitive (Windows filenames are). Deliberately NOT a substring search: a game
/// legitimately called e.g. `MyCheatEngineClone.exe` must not be refused, so the match is
/// anchored at the start. (Audit #4 B34, corrected by in-game verification.)
inline bool IsCheatEngineExeName(const wchar_t* exeLeafName) {
    if (!exeLeafName || !*exeLeafName) return false;
    // "cheatengine-*.exe" — every shipped variant of the main executable.
    if (_wcsnicmp(exeLeafName, L"cheatengine", 11) == 0) return true;
    // The installer's launcher shim, which has a space and no suffix.
    if (_wcsicmp(exeLeafName, L"Cheat Engine.exe") == 0) return true;
    return false;
}

/// May this DLL create BACKGROUND THREADS in the given host process?
///
/// Takes the full host-executable path (what `GetModuleFileNameW(nullptr, …)` returns) and
/// answers the one question `DllMain` has to settle *before* any thread exists. It is a
/// separate function from `IsCheatEngineExeName` for a reason that cost a HIGH finding:
/// `DllMain` cannot be reached from a test, so the decision it makes has to live somewhere
/// a test CAN reach, taking exactly the value Windows hands us (audit #5 AB1).
///
/// **False for Cheat Engine, and only for Cheat Engine.** CE loads a plugin DLL and then
/// unloads it — `Settings → Plugins → Add` does LoadLibrary → `CEPlugin_GetVersion` →
/// FreeLibrary, and every CE exit unloads every plugin before writing its settings. A
/// thread of ours still running in that image executes unmapped memory and takes CE down.
/// `DLL_PROCESS_DETACH` cannot save us: it cannot distinguish a FreeLibrary unload from
/// process exit, and joining threads under the loader lock would deadlock.
///
/// **Fails OPEN.** An empty or unreadable host path returns `true`, preserving the
/// behaviour that shipped for every non-CE host rather than silently disabling the DLL on
/// a path we failed to read.
inline bool HostAllowsBackgroundThreads(const wchar_t* hostExePath) {
    if (!hostExePath || !*hostExePath) return true;
    // Take the last separator of EITHER kind — a path can arrive with '/' on Windows.
    const wchar_t* back = wcsrchr(hostExePath, L'\\');
    const wchar_t* fwd  = wcsrchr(hostExePath, L'/');
    const wchar_t* sep  = (fwd && (!back || fwd > back)) ? fwd : back;
    const wchar_t* leaf = sep ? sep + 1 : hostExePath;
    return !IsCheatEngineExeName(leaf);
}

namespace Grimoire {

// Every re-assert worker sleeps its period in slices of this, so StopWorker()'s join
// waits at most one slice rather than a whole period. Was 8 bare `25`s across four
// modules (R5). Keep it well under the shortest period (PROTECT/MOVE/TIME/SOLIDE) —
// a slice longer than a period would turn the sliced sleep into a single long one.
constexpr int     WORKER_SLEEP_SLICE_MS = 25;

// Deferred-restore poll: a disable while the game thread is paused cannot undo what
// the feature did, so a short-lived worker waits for the thread to come back and
// restores then. The tick is slow (this is a "has the user clicked back into the game
// yet" poll, not a trace loop); the bound stops a thread outliving everything if they
// never return — realistically the game is closed by then, which makes the leftover
// moot anyway. Shared by Schlacht (un-hide occluders) and Dunste (re-enable the pawn's
// collision) — same poll, same reason, so deliberately NOT per-module constants.
constexpr int     PENDING_RESTORE_TICK_MS = 250;
constexpr int     PENDING_RESTORE_MAX_MS  = 5 * 60 * 1000;   // 5 minutes

} // namespace Grimoire
