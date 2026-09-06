// ============================================================
// Ubel — 尤蓓爾 (外科式暗殺者 — Surgical Assassin)
// UStructWalker: FField chain traversal and property reading
// ============================================================

#include "Ubel.h"
#include "Macht.h"
#define LOG_CAT "WALK"
#include "Sein.h"
#include "Grimoire.h"
#include "Serie.h"
#include "Aura.h"
#include "Genau.h"
#include "Utf8Helpers.h"
#include "Scharf.h"
#include "Neu.h"     // UEnum::Names layout (legacy TArray vs UE5.6+ FNameData)
#include "Tot.h"     // cooperative cancellation for the unbounded gap-fill loop

#include <algorithm>
#include <atomic>
#include <cctype>
#include <chrono>
#include <cstring>
#include <mutex>
#include <list>
#include <unordered_map>
#include <unordered_set>

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <Windows.h>

// Defined in ExportAPI.cpp — cached UE version for layout branching
extern uint32_t g_cachedUEVersion;

namespace Ubel {

// Hard cap on container/array elements read per request across every
// Read*ArrayElements phase — clamps (end - offset) so one malformed Num can't
// make the reader walk unbounded memory. Shared by all array-reader functions.
static constexpr int32_t kArrayElementsPerRequestCap = 4096;

// Lazy UE5.5+ version marker — set true the first time a class walk encounters a
// reflected Utf8StrProperty / AnsiStrProperty (added in UE5.5). Atomic so the
// parallel GObjects walks can set it without a lock. Read via SawUtf8OrAnsiStr().
static std::atomic<bool> s_sawUtf8OrAnsiStr{false};

// File-scope enum cache: keyed by UEnum* → vector of (value, name) pairs.
// Shared between ResolveEnumValue (lookup) and GetEnumEntries (full list export).
static std::unordered_map<uintptr_t, std::vector<std::pair<int64_t, std::string>>> s_enumCache;

// File-scope GetName cache: keyed by UObject* → (witness, resolved name string).
// Dramatically reduces FNamePool lookups for ObjectProperty fields that
// reference the same UClass repeatedly (e.g., many fields pointing to the same class).
//
// The key is an address the engine RECYCLES, so every hit is revalidated against the
// FName bytes the string was decoded from (Ubel::NameWitness — see its comment in
// Ubel.h for why those bytes, and not an (InternalIndex, SerialNumber) pair). Without
// that, a level change made every name-bearing response serve the DESTROYED object's
// name for the rest of the process, while the class was read fresh — so the two
// disagreed with no error anywhere. (audit #5 U6 + F3's in-session half)
struct NameCacheEntry {
    Ubel::NameWitness witness;
    std::string       name;
};
static std::unordered_map<uintptr_t, NameCacheEntry> s_nameCache;

// ── Cache mutexes (thread-safety for parallel GObjects walks) ──────────────
// Aura's value/reference/container scans walk the whole GObjects array across
// N worker threads, all of which call WalkClass(Ex) / GetName / ResolveEnumValue
// / GetCachedStructFields. Those memoize into the file-scope maps above + below,
// so concurrent first-touches would race. Each cache gets its own mutex; the
// expensive walk/read happens WITHOUT the lock (only the find/insert is guarded),
// and every cache-returning function hands back either a value copy or a
// reference into a node-based unordered_map (whose element references stay valid
// across other threads' inserts), so callers can read lock-free after lookup.
// These touches are per-class / per-match (NOT per-object), so contention is low.
// All locks are leaf-level (no function holds one while acquiring another), so
// there is no lock-ordering / deadlock concern. s_calibrationMutex guards the
// one-time CorrectSubclassOffsets DynOff writes via double-checked locking.
static std::mutex s_enumCacheMutex;
static std::mutex s_nameCacheMutex;
static std::mutex s_walkClassCacheMutex;
static std::mutex s_structFieldCacheMutex;
static std::mutex s_calibrationMutex;

// Read FName from an address and resolve to string
static std::string ReadFName(uintptr_t fnameAddr) {
    // FName is typically: int32 ComparisonIndex, int32 Number
    int32_t compIndex = 0;
    int32_t number = 0;

    if (!Macht::ReadSafe(fnameAddr, compIndex)) return "";
    Macht::ReadSafe(fnameAddr + 4, number);

    return Serie::GetString(compIndex, number);
}

// The same decode, from BYTES already in hand rather than from a live address.
//
// Three call sites open-coded this as `memcpy(&idx, p, 4); Serie::GetString(idx)` and
// all three dropped FName::Number, so Slot_1 / Slot_2 / Slot_3 every rendered as "Slot"
// — while ReadFNameAt, reading the same 8 bytes through the function above, returned the
// suffix. The panel and value search disagreed about one field. (audit #5 U8)
//
// Number sits at +4 in EVERY configuration: UE declares it immediately after
// ComparisonIndex, and the case-preserving DisplayIndex is appended AFTER it (verified in
// vendor/UnrealEngine .../UObject/NameTypes.h:1258-1267). That is why this takes a byte
// count and not DynOff::bCasePreservingName — the 0x10 FName is wider at the TAIL, so the
// two fields we read are at fixed offsets. `size` still gates the Number read, because a
// caller holding only 4 bytes has no Number to decode and must keep the old behaviour.
static std::string DecodeFNameBytes(const uint8_t* bytes, int32_t size) {
    if (!bytes || size < 4) return "";
    int32_t compIndex = 0, number = 0;
    memcpy(&compIndex, bytes, 4);
    if (size >= 8) memcpy(&number, bytes + 4, 4);
    return Serie::GetString(compIndex, number);
}

// ============================================================
// ResolveEnumValue — resolve an enum integer value to its name string.
// Uses a per-UEnum cache (static unordered_map) for performance.
// Triggers lazy DetectUEnumNames() on first call.
// ============================================================
static std::string ResolveEnumValue(uintptr_t enumAddr, int64_t value) {
    if (!enumAddr) return "";

    // Lazy init: trigger DetectUEnumNames on first call.
    // bUEnumNamesFailed prevents retry storm (was causing 25-45 second delays).
    if (!DynOff::bUEnumNamesDetected.load(std::memory_order_acquire))
        Genau::DetectUEnumNames();
    if (DynOff::bUEnumNamesFailed.load(std::memory_order_acquire))
        return "";  // Detection failed — show raw int values instead

    // Fast path: already cached. Hold the lock only for the lookup; the
    // returned name is copied out so we read lock-free thereafter.
    {
        std::lock_guard<std::mutex> lk(s_enumCacheMutex);
        auto it = s_enumCache.find(enumAddr);
        if (it != s_enumCache.end()) {
            for (const auto& [v, n] : it->second)
                if (v == value) return n;
            return "";  // Value not in this (cached) enum
        }
    }

    // Slow path: parse UEnum::Names WITHOUT the lock (game-memory reads are the
    // expensive part), then insert. The container is either the legacy
    // TArray<TPair<FName,int64>> or the UE5.6+ FNameData struct-of-arrays; the
    // format is a per-game constant established by DetectUEnumNames, so we build
    // the layout for that KNOWN format (Neu::BuildLayout — no per-enum guessing).
    auto readMem = [](uintptr_t a, void* o, size_t n) -> bool {
        return Macht::ReadBytesSafe(a, o, n);
    };
    const Neu::EnumNamesFormat fmt = DynOff::bEnumNamesNewContainer
        ? Neu::EnumNamesFormat::FNameData57
        : Neu::EnumNamesFormat::Legacy;
    const int fnameSize = DynOff::SizeofFName();

    std::vector<std::pair<int64_t, std::string>> entries;
    Neu::EnumNamesLayout layout;
    // False ONLY when a mid-table read broke the loop below. BuildLayout returning
    // false is a COMPLETE answer, not a truncated one: Neu rejects count == 0 /
    // num <= 0 (Neu.h), so a legitimately member-less UEnum — and any address that
    // is not a UEnum — lands there, and caching "" for it is correct and must stay
    // cached, or every lookup re-probes. A half-read table is the opposite case.
    bool tableComplete = true;
    if (Neu::BuildLayout(readMem, enumAddr + DynOff::UENUM_NAMES, fmt, fnameSize, 16384, layout)) {
        entries.reserve(layout.count);
        for (int32_t i = 0; i < layout.count; ++i) {
            int32_t nameIdx = 0;
            int64_t val = 0;
            if (!Neu::ReadEntry(readMem, layout, i, nameIdx, val)) break;
            std::string name = Serie::GetString(nameIdx);
            entries.push_back({val, std::move(name)});
        }
        tableComplete = ShouldPublishEnumTable(true, layout.count, entries.size());
        // Report what was STORED, not what was intended. This used to print
        // layout.count unconditionally, so a truncated table logged as a full one —
        // the report and the reality computed by different code paths (audit #4's
        // own root cause), which is what hid this defect.
        LOG_DEBUG("ResolveEnumValue: UEnum 0x%llX — read %zu of %d entries (%s)%s",
            static_cast<unsigned long long>(enumAddr), entries.size(), layout.count,
            fmt == Neu::EnumNamesFormat::FNameData57 ? "FNameData" : "legacy",
            tableComplete ? "" : " — TRUNCATED, not cached");
    }

    // Insert (another thread may have built the same enum meanwhile — emplace
    // is a no-op then, and we read the existing entry while holding the lock).
    {
        std::lock_guard<std::mutex> lk(s_enumCacheMutex);
        auto it = s_enumCache.find(enumAddr);
        if (it != s_enumCache.end()) {
            for (const auto& [v, n] : it->second)
                if (v == value) return n;
            return "";  // Value not in this (cached) enum
        }

        // A TRUNCATED table is answered from but never published. Nothing in dll/src
        // erases s_enumCache, so caching a half-read table permanently splits one
        // UEnum: values below the break point resolve, values above it render as raw
        // integers — in the Live Walker, the Property Grid and every CE export, for
        // the rest of the process, with no retry. Leaving it uncached costs a re-read
        // per lookup and lets the next one recover. (audit #5, found while fixing U4)
        if (!tableComplete) {
            for (const auto& [v, n] : entries)
                if (v == value) return n;
            return "";
        }

        it = s_enumCache.emplace(enumAddr, std::move(entries)).first;
        for (const auto& [v, n] : it->second)
            if (v == value) return n;
        return "";  // Value not in enum
    }
}

// ============================================================
// GetEnumEntries — return all cached entries for a UEnum address.
// Triggers cache population if not yet cached.
// Used by PipeServer to send full enum lists for CE DropDownList.
// ============================================================
std::vector<LiveFieldValue::EnumEntry> GetEnumEntries(uintptr_t enumAddr) {
    if (!enumAddr) return {};

    // Trigger cache population (value -999999 won't match any real enum entry)
    ResolveEnumValue(enumAddr, -999999);

    std::lock_guard<std::mutex> lk(s_enumCacheMutex);
    auto it = s_enumCache.find(enumAddr);
    if (it == s_enumCache.end()) {
        // ResolveEnumValue above ran and still published nothing, which since the
        // truncation fix means exactly one thing: a mid-table read failed, so there
        // is no trustworthy full list to hand CE. Say so — an empty DropDownList is
        // otherwise indistinguishable from a member-less UEnum, and unlike the old
        // behaviour (a silently partial list cached forever) the next call retries.
        Sein::Warn("WALK:safe",
            "GetEnumEntries: UEnum 0x%llx has no cached table — truncated read, retry pending",
            (unsigned long long)enumAddr);
        return {};
    }

    std::vector<LiveFieldValue::EnumEntry> result;
    result.reserve(it->second.size());
    for (const auto& [v, n] : it->second)
        result.push_back({v, n});
    return result;
}

// ============================================================
// ReadFString — read an FString (TArray<wchar_t>) from a live
// instance and convert UTF-16 → UTF-8.
// Returns empty string on failure or if string is empty/too long.
// Output passes through Utf8Helpers::Sanitize so JSON serialization
// downstream can never trip on game-memory corruption.
// ============================================================
static std::string ReadFString(uintptr_t instanceAddr, int32_t offset) {
    // FString = TArray<wchar_t> = { wchar_t* Data (8B), int32 Count (4B), int32 Max (4B) }
    // Read Data+Count in ONE shot: two separate reads can pair a fresh Data
    // pointer with a stale Count if the string reallocs between them, giving an
    // out-of-bounds buffer read. 12 bytes cover {Data, Count}.
    uint8_t hdr[12];
    if (!Macht::ReadBytesSafe(instanceAddr + offset, hdr, sizeof(hdr))) return "";
    uintptr_t data = 0;
    int32_t count = 0;
    std::memcpy(&data, hdr, sizeof(data));
    std::memcpy(&count, hdr + 8, sizeof(count));

    if (!data || !IsPlausibleStringCount(count)) return "";  // audit #5 U10: cap bounds a garbage Count, not string length

    // Read wchar_t buffer (count includes null terminator in most UE builds)
    std::vector<wchar_t> wbuf(count, 0);
    if (!Macht::ReadBytesSafe(data, wbuf.data(), count * sizeof(wchar_t)))
        return "";

    // Ensure null termination
    wbuf.back() = 0;

    // UTF-16 → UTF-8 via Utf8Helpers::EncodeUtf16. The helper is the same
    // surrogate-aware logic the FName wide path uses (Serie::GetString) —
    // shared so a single test target covers both. Result is then routed
    // through Sanitize for belt-and-braces against any future regression.
    size_t actualLen = 0;
    while (actualLen < wbuf.size() && wbuf[actualLen] != 0) ++actualLen;
    std::string utf8 = Utf8Helpers::EncodeUtf16(wbuf.data(), actualLen);
    return Utf8Helpers::Sanitize(utf8);
}

// ============================================================
// ReadFUtf8String — read a UE5.5+ FUtf8String / FAnsiString
// (TArray<char>, 1-byte elements) from a live instance.
// Shares FString's { Data ptr (8B), int32 Count (4B), int32 Max (4B) }
// header, differing only in element size (1 byte vs wchar_t's 2).
// FUtf8String holds UTF-8 bytes directly; FAnsiString holds ANSI
// (codepage) bytes — for ASCII the two are identical, and non-ASCII
// ANSI is best-effort (any invalid sequence is scrubbed by Sanitize so
// downstream JSON can never trip). Returns empty on failure / empty /
// over-long string. Used for both new UE5.5+ string property variants.
// ============================================================
static std::string ReadFUtf8String(uintptr_t instanceAddr, int32_t offset) {
    // Single-shot {Data, Count} read — same torn-read guard as ReadFString.
    uint8_t hdr[12];
    if (!Macht::ReadBytesSafe(instanceAddr + offset, hdr, sizeof(hdr))) return "";
    uintptr_t data = 0;
    int32_t count = 0;
    std::memcpy(&data, hdr, sizeof(data));
    std::memcpy(&count, hdr + 8, sizeof(count));

    if (!data || !IsPlausibleStringCount(count)) return "";  // audit #5 U10: cap bounds a garbage Count, not string length

    // count includes the null terminator in most UE builds.
    std::vector<char> bytes(count, 0);
    if (!Macht::ReadBytesSafe(data, bytes.data(), static_cast<size_t>(count)))
        return "";
    bytes.back() = 0;

    size_t actualLen = 0;
    while (actualLen < bytes.size() && bytes[actualLen] != 0) ++actualLen;
    return Utf8Helpers::Sanitize(std::string(bytes.data(), actualLen));
}

// ============================================================
// ReadSoftObjectPath — resolve FSoftObjectPath at the given
// address to a human-readable asset path string.
//
// UE4 / UE5.0: FSoftObjectPath = { FName AssetPathName; FString SubPathString; }
// UE5.1+:      FSoftObjectPath = { FTopLevelAssetPath { FName PackageName; FName AssetName; }; FString SubPathString; }
// ============================================================
static std::string ReadSoftObjectPath(uintptr_t addr) {
    if (!addr) return "";

    int fnameSize = DynOff::SizeofFName();

    bool isTopLevelAssetPath = (g_cachedUEVersion >= 501);

    if (isTopLevelAssetPath) {
        // UE5.1+: FTopLevelAssetPath = { FName PackageName, FName AssetName }
        std::string packageName = ReadFName(addr);
        std::string assetName   = ReadFName(addr + fnameSize);

        if (packageName.empty() || packageName == "None") return "";
        if (assetName.empty() || assetName == "None")
            return packageName;
        return packageName + "." + assetName;
    } else {
        // UE4 / UE5.0: FName AssetPathName
        //
        // There used to be a "fallback: try UE5.1+ layout in case version was
        // misdetected" block here. It could never run: its guard was the exact
        // negation of the condition that reaches this branch, and it re-read the
        // same `addr` the line above had already read. Deleted rather than
        // repaired — a genuinely misdetected 5.1+ game returns above with a
        // non-None PackageName and never arrives here at all.
        std::string assetPathName = ReadFName(addr);
        if (!assetPathName.empty() && assetPathName != "None")
            return assetPathName;
        return "";
    }
}

// ============================================================
// PersistentObjectPtrEnvelope — offset of the payload inside a
// TPersistentObjectPtr (i.e. a soft or lazy object pointer).
//
// UE ≤ 5.2 carries `mutable int32 TagAtLastTest` between the FWeakObjectPtr and
// the payload; UE ≥ 5.3 deleted it. The full layout table and the version
// evidence live on DynOff::SOFTPTR_PATH in Grimoire.h.
//
// We derive the envelope by SUBTRACTION rather than by a version gate:
//
//     envelope = ElementSize − sizeof(payload)
//
// ⚠ ElementSize alone is ambiguous and must not be matched on its own — 0x28 is
// both a ≤5.0 tagged soft pointer (0x10 + FName/FString path) and a ≥5.3
// untagged one (0x08 + FTopLevelAssetPath). Subtracting the payload size, which
// the FTopLevelAssetPath discriminator already tells us, makes it unique.
//
// Returns the measured envelope and latches it, or the version-derived default
// when `elemSize` is not one of the shapes we can account for (0 for a caller
// that has no size to offer, a static C-array whose stride we were handed, a
// packed licensee fork). Latching matters because the fallback is the case we
// are trying to stop trusting.
// ============================================================
static int PersistentObjectPtrEnvelope(int32_t elemSize, int32_t payloadSize,
                                       int taggedEnvelope, int& latched,
                                       const char* what) {
    const int envelope = DynOff::PersistentPtrEnvelopeFor(
        elemSize, payloadSize, taggedEnvelope, latched, g_cachedUEVersion);

    // Latch only a real measurement. The header's fallback arms return either the
    // existing latch or a version-derived guess, and re-latching a guess would turn
    // one unmeasured call into a permanent "measured" answer.
    const bool measured = (elemSize > payloadSize) && (elemSize - payloadSize == envelope);
    if (measured && latched != envelope) {
        // "DYNO" routes to offsets.log (Sein.cpp's category table), which is where a
        // measured offset belongs and where this fix's verification row will look
        // for its DLL-side observable.
        Sein::Info("DYNO:PersistPtr",
                   "%s payload envelope measured: +0x%02X "
                   "(ElementSize 0x%X - payload 0x%X, UEver=%u)%s",
                   what, envelope, elemSize, payloadSize, g_cachedUEVersion,
                   latched < 0 ? "" : "  <-- CHANGED, a previous measurement disagreed");
        latched = envelope;
    }
    return envelope;
}

// FSoftObjectPath payload size: FTopLevelAssetPath (2 FNames) or FName, + FString header.
static int32_t SoftObjectPathPayloadSize() {
    // sizeof(FName), NOT the UObject Name->Outer slot. See DynOff::bCasePreservingName.
    const int fnameSize = DynOff::SizeofFName();
    // The AlignUp and the reason it is load-bearing live on DynOff::FSoftObjectPathSizeFor,
    // in the header so the test target can pin it.
    return static_cast<int32_t>(
        DynOff::FSoftObjectPathSizeFor(fnameSize, g_cachedUEVersion >= 501));
}

// Offset of FSoftObjectPath inside a TSoftObjectPtr. `elemSize` is the property's
// own ElementSize; pass 0 when the caller has none.
static int SoftPathOffset(int32_t elemSize) {
    return PersistentObjectPtrEnvelope(elemSize, SoftObjectPathPayloadSize(), 0x10,
                                       DynOff::SOFTPTR_PATH, "TSoftObjectPtr");
}

// Offset of the FGuid inside a TLazyObjectPtr. FUniqueObjectGuid is a bare FGuid
// (4×uint32, alignof 4), so the tagged envelope is 0x0C — NOT 0x10. There is no
// era in which 0x10 is correct here.
static int LazyGuidOffset(int32_t elemSize) {
    return PersistentObjectPtrEnvelope(elemSize, 0x10, 0x0C,
                                       DynOff::LAZYPTR_GUID, "TLazyObjectPtr");
}

// ============================================================
// TryDecodeFStringAt — read a { Data(8B), Num(4B), Max(4B) } FString header at
// `addr`, read its buffer, and decode it as UTF-16 OR UTF-8 (element width
// auto-detected by Utf8Helpers::DecodeFStringBuffer). Used by the FText reader,
// where it carries a matching length ceiling (num > 8192) plus a Max-window and
// heap-pointer gate the by-offset ReadFString cannot use (it has no header sibling
// to corroborate). ReadFString now shares the same 8192-char bound
// (Ubel::kMaxFStringChars, audit #5 U10) so a long StrProperty resolves too.
// Returns "" if `addr` does not hold a plausible, decodable FString.
// ============================================================
static std::string TryDecodeFStringAt(uintptr_t addr) {
    if (!addr) return "";

    // Single-shot 16-byte header read: {Data(8), Num(4), Max(4)}.
    uint8_t hdr[16];
    if (!Macht::ReadBytesSafe(addr, hdr, sizeof(hdr))) return "";
    uintptr_t data = 0;
    int32_t num = 0, cap = 0;
    std::memcpy(&data, hdr, sizeof(data));
    std::memcpy(&num, hdr + 8, sizeof(num));
    std::memcpy(&cap, hdr + 12, sizeof(cap));

    // Plausibility gate for a real FString header (steps the probe over
    // garbage): non-null Data, Num in [2, 8192] (includes the trailing null
    // unit), Max in [Num, 4*Num+256]. The Max window is generous — a localized
    // dialogue FString can carry reserved capacity — because the real
    // discriminators are the null-terminator position + content check inside
    // DecodeFStringBuffer, not Max. Data must also look like a user-space
    // heap pointer, which rejects most non-FString 16-byte regions cheaply.
    if (!data || num < 2 || num > 8192) return "";
    if (cap < num || cap > num * 4 + 256) return "";
    if (data < 0x10000 || data >= 0x7FFFFFFFFFFFull) return "";

    // Read up to Num*2 bytes so the UTF-16 hypothesis can be tested; fall back
    // to Num bytes when the buffer sits near the end of a committed page.
    const size_t wantWide = static_cast<size_t>(num) * 2;
    std::vector<uint8_t> buf(wantWide, 0);
    size_t got = 0;
    if (Macht::ReadBytesSafe(data, buf.data(), wantWide)) {
        got = wantWide;
    } else if (Macht::ReadBytesSafe(data, buf.data(), static_cast<size_t>(num))) {
        got = static_cast<size_t>(num);
    } else {
        return "";
    }
    return Utf8Helpers::DecodeFStringBuffer(buf.data(), got, num);
}

// ============================================================
// ReadFTextString — read the display string from an FText.
//
// FText = { ITextData* TextData (8B); ... } -- only that leading pointer is read
// here, and it is at +0x00 in every version. The TAIL changed at UE 5.4:
// UE4.18-5.3 is TSharedRef<ITextData,ThreadSafe> {obj ptr; ref-controller ptr}
// + uint32 Flags@+0x10 (sizeof 0x18); UE5.4-5.8 is TRefCountPtr<ITextData>
// {obj ptr} + uint32 Flags@+0x08 (sizeof 0x10), the refcount having moved into
// ITextData itself (it now derives from IRefCountedObject). Neither tail is read.
// The display FString lives at a version/fork-dependent spot inside ITextData,
// in one of two shapes:
//   (a) INLINE   — the FString sits by value at ITextData+offset (UE4 / UE5.0
//                  FTextHistory_Base source string / by-value display string).
//   (b) INDIRECT — ITextData+offset holds a POINTER to a ref-counted block
//                  carrying the FString: TSharedPtr<FString> (UE<=5.3) has it
//                  at +0x00; FRefCountedDisplayString {atomic refcount; FString}
//                  (UE5.4+) has it at +0x08.
// Each candidate FString is decoded width-agnostically (UTF-16 OR UTF-8 — some
// cooked builds, e.g. stock UE5.6, store the display string as UTF-8, which a
// blind UTF-16 decode turned into 亂碼 / an empty result before).
// ============================================================
static std::string ReadFTextString(uintptr_t ftextAddr) {
    if (!ftextAddr) return "";

    // Read ITextData* (first 8 bytes of FText)
    uintptr_t textDataPtr = 0;
    if (!Macht::ReadSafe(ftextAddr, textDataPtr) || !textDataPtr) return "";

    // Bounded, layout-agnostic scan of the FTextData object. We don't hardcode
    // per-version offsets because the display FString lands in different spots
    // across UE4 / UE5.0 inline histories, the UE5.4+ ref-counted pointer, and
    // licensee forks. FTextData is small, so an 8-byte-strided window from just
    // past the vtable to 0x90 covers every observed layout; the strict header
    // gate in TryDecodeFStringAt keeps false positives negligible.
    const int kScanBegin = 0x08;   // skip the vtable at +0x00
    const int kScanEnd   = 0x90;

    // Pass 1: FString stored INLINE at FTextData+offset (UE4 / UE5.0 by-value
    // display or source string). Ascending order → the earliest real header
    // wins, matching how a by-value display string precedes trailing fields.
    for (int off = kScanBegin; off <= kScanEnd; off += 8) {
        std::string s = TryDecodeFStringAt(textDataPtr + off);
        if (!s.empty()) return s;
    }

    // Pass 2: pointer INDIRECTION (UE<=5.3 TSharedPtr<FString> at inner +0x00;
    // UE5.4+ FRefCountedDisplayString {atomic refcount; FString} at inner +0x08).
    // Only reached when no inline FString resolved.
    for (int off = kScanBegin; off <= kScanEnd; off += 8) {
        uintptr_t inner = 0;
        if (!Macht::ReadSafe(textDataPtr + off, inner) || !inner) continue;
        for (int io = 0x00; io <= 0x18; io += 8) {
            std::string s = TryDecodeFStringAt(inner + io);
            if (!s.empty()) return s;
        }
    }

    return "";
}

uintptr_t GetClass(uintptr_t uobjectAddr) {
    if (!uobjectAddr) return 0;
    uintptr_t cls = 0;
    Macht::ReadSafe(uobjectAddr + Grimoire::OFF_UOBJECT_CLASS, cls);
    return cls;
}

uintptr_t GetOuter(uintptr_t uobjectAddr) {
    if (!uobjectAddr) return 0;
    uintptr_t outer = 0;
    Macht::ReadSafe(uobjectAddr + DynOff::UOBJECT_OUTER, outer);
    return outer;
}

std::string GetName(uintptr_t uobjectAddr) {
    if (!uobjectAddr) return "";

    // Read the FName bytes FIRST — they are both the cache's witness and the decode's
    // input, so this replaces ReadFName rather than adding a read. Same tolerance
    // ReadFName has and for the same reason: ComparisonIndex must read, Number may
    // fail and default to 0. Kept as two reads, not one 8-byte load, so an unaligned
    // or half-mapped address behaves exactly as it did before.
    const uintptr_t fnameAddr = uobjectAddr + Grimoire::OFF_UOBJECT_NAME;
    NameWitness live{};
    if (!Macht::ReadSafe(fnameAddr, live.comparisonIndex)) return "";
    Macht::ReadSafe(fnameAddr + 4, live.number);

    // Check name cache first — avoids repeated FNamePool lookups. The key is an
    // address the engine recycles, so a hit is only served when the bytes it was
    // decoded from still read the same; otherwise fall through and re-decode.
    {
        std::lock_guard<std::mutex> lk(s_nameCacheMutex);
        auto it = s_nameCache.find(uobjectAddr);
        if (it != s_nameCache.end() && it->second.witness == live)
            return it->second.name;
    }

    std::string name = Serie::GetString(live.comparisonIndex, live.number);

    // Only cache non-empty names (empty could be transient read failure).
    // Assign, not try_emplace: a stale entry MUST be replaced, and that is safe here
    // precisely because this cache hands out copies — the hit above returns by value
    // with the copy made under the lock, so no reference into this map ever escapes.
    // (The two class caches do hand out references, which is why they cannot do this.)
    if (!name.empty()) {
        std::lock_guard<std::mutex> lk(s_nameCacheMutex);
        s_nameCache[uobjectAddr] = NameCacheEntry{live, name};
    }

    return name;
}

void ClearNameCache() {
    // Release the per-UObject name cache. Called at the start of each snapshot
    // capture / engine re-scan, and from Fern's last-connection teardown / Fern::Stop.
    // swap()-with-empty frees the bucket array too, not just the strings.
    //
    // Two reasons remain, and STALENESS IS NO LONGER ONE OF THEM: every hit is now
    // witnessed against the FName bytes it was decoded from, so a recycled address
    // cannot return a destroyed object's name even between purges (audit #5 U6/F3).
    // What is left is (a) bounding growth — one entry per GObjects slot ever named,
    // millions of strings and ~150-200 MB on a 2M-object game — and (b) covering a
    // Serie::Init re-run, the one event that can remap a ComparisonIndex and so make
    // an unchanged witness decode to a different string.
    //
    // The class/struct/enum layout caches are intentionally kept: they are per-class
    // (small, layout-stable) and expensive to rebuild. Note they are NOT witnessed,
    // so a name baked into ClassInfo::Name/FullPath/SuperName by WalkClass can still
    // be stale after a recycle — that is a separate open finding, not an oversight.
    std::lock_guard<std::mutex> lk(s_nameCacheMutex);
    std::unordered_map<uintptr_t, NameCacheEntry>().swap(s_nameCache);
}

bool SawUtf8OrAnsiStr() {
    return s_sawUtf8OrAnsiStr.load(std::memory_order_relaxed);
}

int32_t GetIndex(uintptr_t uobjectAddr) {
    if (!uobjectAddr) return -1;
    int32_t index = -1;
    Macht::ReadSafe(uobjectAddr + Grimoire::OFF_UOBJECT_INDEX, index);
    return index;
}

// Public thin wrappers around the file-static ReadFString / ReadFName /
// ReadFTextString helpers so cross-TU consumers (Aura Radar path)
// don't need to duplicate the FString header decode + UTF-16 sanitize
// logic. Keeping the static helpers in place avoids touching the
// thousand-plus existing call sites in this TU.
std::string ReadFStringAt(uintptr_t instanceAddr, int32_t offset) {
    if (!instanceAddr) return "";
    return ReadFString(instanceAddr, offset);
}

std::string ReadFNameAt(uintptr_t instanceAddr, int32_t offset) {
    if (!instanceAddr) return "";
    return ReadFName(instanceAddr + offset);
}

std::string ReadFTextStringAt(uintptr_t instanceAddr, int32_t offset) {
    if (!instanceAddr) return "";
    return ReadFTextString(instanceAddr + offset);
}

std::string GetFullName(uintptr_t uobjectAddr) {
    if (!uobjectAddr) return "";

    // Build path by walking Outer chain
    std::vector<std::string> parts;
    uintptr_t current = uobjectAddr;

    // Safety limit to prevent infinite loops
    for (int i = 0; i < 64 && current != 0; ++i) {
        std::string name = GetName(current);
        if (name.empty()) break;
        parts.push_back(name);
        current = GetOuter(current);
    }

    // Reverse to get outermost first
    std::reverse(parts.begin(), parts.end());

    // Join with '/' for packages, '.' for subobjects
    // Convention: Package/SubPackage.ObjectName:SubObject
    std::string result;
    for (size_t i = 0; i < parts.size(); ++i) {
        if (i == 0) {
            result = "/" + parts[i];
        } else if (i == parts.size() - 1 && parts.size() > 2) {
            result += "." + parts[i];
        } else {
            result += "/" + parts[i];
        }
    }

    return result;
}

// Read the type name from an FFieldClass* (FProperty mode)
static std::string GetFieldTypeName(uintptr_t ffieldAddr) {
    // FField::ClassPrivate at offset 0x08 -> FFieldClass*
    uintptr_t fieldClass = 0;
    if (!Macht::ReadSafe(ffieldAddr + DynOff::FFIELD_CLASS, fieldClass) || !fieldClass) {
        return "Unknown";
    }

    // FFieldClass has Name (FName) at offset 0x00
    return ReadFName(fieldClass + DynOff::FFIELDCLASS_NAME);
}

// Read the type name from a UProperty* (UObject subclass, UE4 UProperty mode).
// UProperty inherits UObject, so its Class at +0x10 is a UClass whose Name is the type.
static std::string GetUPropertyTypeName(uintptr_t upropAddr) {
    uintptr_t cls = 0;
    if (!Macht::ReadSafe(upropAddr + Grimoire::OFF_UOBJECT_CLASS, cls) || !cls) return "";
    if (!Grimoire::IsUserspacePointer(cls)) return "";

    uint32_t nameIdx = 0;
    if (!Macht::ReadSafe(cls + Grimoire::OFF_UOBJECT_NAME, nameIdx)) return "";
    return Serie::GetString(nameIdx);
}

// Walk the FField chain starting from the first field (UE4.25+ / UE5)
static void WalkFFieldChain(uintptr_t firstField, std::vector<FieldInfo>& fields) {
    // UE5.3+: ChildProperties may come from an FFieldVariant read — strip tag bit defensively
    uintptr_t current = DynOff::StripFFieldTag(firstField);
    int safetyLimit = 4096;
    // Cycle detection: a destroyed class with recycled memory can form a chain
    // that loops back on itself. The counter alone would still iterate 4096x
    // reading garbage; the seen-set short-circuits the moment we revisit a node.
    std::unordered_set<uintptr_t> seen;
    seen.reserve(64);

    while (current != 0 && safetyLimit-- > 0) {
        if (!seen.insert(current).second) {
            Sein::Warn("WALK:safe", "WalkFFieldChain: cycle detected at 0x%llx, aborting",
                (unsigned long long)current);
            break;
        }
        // UE5.3+: if tag bit indicates UObject rather than FField, skip this entry
        if (DynOff::IsFFieldVariantUObject(current)) break;

        FieldInfo fi{};
        fi.Address = current;

        // Read field name
        fi.Name = ReadFName(current + DynOff::FFIELD_NAME);

        // Read type name from FFieldClass
        fi.TypeName = GetFieldTypeName(current);

        // Lazy UE5.5+ version marker (see Ubel::SawUtf8OrAnsiStr): a reflected
        // Utf8StrProperty / AnsiStrProperty proves the engine is >= UE5.5. Cheap
        // — a string compare on a name we already read.
        if (!s_sawUtf8OrAnsiStr.load(std::memory_order_relaxed)
            && (fi.TypeName == "Utf8StrProperty" || fi.TypeName == "AnsiStrProperty"))
            s_sawUtf8OrAnsiStr.store(true, std::memory_order_relaxed);

        // Read offset and size (FProperty fields, may not be valid for non-property FFields)
        Macht::ReadSafe<int32_t>(current + DynOff::FPROPERTY_OFFSET, fi.Offset);
        Macht::ReadSafe<int32_t>(current + DynOff::FPROPERTY_ELEMSIZE, fi.Size);
        // ArrayDim sits immediately before ElementSize (adjacent int32s) on every
        // UE 4.18-5.7 layout (see Genau Step 9). Reading it lets a static C-array
        // UPROPERTY (Type Foo[N]) report its full Size*ArrayDim footprint so the
        // Native-C hole scan doesn't treat its tail as unmanaged. Garbage/zero -> 1.
        {
            int32_t arrayDim = 1;
            if (Macht::ReadSafe<int32_t>(current + DynOff::FPROPERTY_ELEMSIZE - 4, arrayDim)
                && arrayDim >= 1 && arrayDim <= 0x10000)
                fi.ArrayDim = arrayDim;
        }
        Macht::ReadSafe<uint64_t>(current + DynOff::FPROPERTY_FLAGS, fi.PropertyFlags);

        // Alignment sanity warning. See Scharf.h for the per-type rules — uses
        // the engine-reported ElemSize for variable-width types (EnumProperty), and respects
        // CasePreservingName mode for FName layout. Earlier inline check assumed all
        // EnumProperty was 4-byte and all NameProperty was 8-byte aligned, which produced
        // up to ~75 false positives per game on UE 5.x AActor-derived BPs.
        if (fi.Offset > 0 && !fi.TypeName.empty()
            && Scharf::IsAlignmentSuspicious(
                   fi.TypeName, fi.Offset, fi.Size, DynOff::bCasePreservingName)) {
            Sein::Warn("WALK", "Misaligned field '%s' (%s, size=%d) at offset 0x%X — possible wrong FPROPERTY_OFFSET",
                fi.Name.c_str(), fi.TypeName.c_str(), fi.Size, fi.Offset);
        }

        if (!fi.Name.empty()) {
            fields.push_back(fi);
        }

        // Move to next FField (strip tag bit for UE5.3+ safety)
        uintptr_t next = 0;
        if (!Macht::ReadSafe(current + DynOff::FFIELD_NEXT, next)) break;
        current = DynOff::StripFFieldTag(next);
    }
}

// Walk the UProperty chain (UE4 <4.25) — properties are UObject-derived (UField chain)
static void WalkUPropertyChain(uintptr_t firstField, std::vector<FieldInfo>& fields) {
    uintptr_t current = firstField;
    int safetyLimit = 4096;
    // Cycle detection — see WalkFFieldChain rationale.
    std::unordered_set<uintptr_t> seen;
    seen.reserve(64);

    while (current != 0 && safetyLimit-- > 0) {
        if (!seen.insert(current).second) {
            Sein::Warn("WALK:safe", "WalkUPropertyChain: cycle detected at 0x%llx, aborting",
                (unsigned long long)current);
            break;
        }
        FieldInfo fi{};
        fi.Address = current;

        // UProperty is UObject-derived, so Name is at UObject::Name
        fi.Name = ReadFName(current + Grimoire::OFF_UOBJECT_NAME);

        // Type name: UProperty's class name (e.g., "IntProperty", "FloatProperty")
        uintptr_t cls = 0;
        if (Macht::ReadSafe(current + Grimoire::OFF_UOBJECT_CLASS, cls) && cls) {
            fi.TypeName = ReadFName(cls + Grimoire::OFF_UOBJECT_NAME);
        } else {
            fi.TypeName = "Unknown";
        }

        // UE4 Children chain includes non-property UField types (UFunction, UEnum, etc.).
        // Skip them — they don't have UProperty layout (Offset/Size/Flags are garbage).
        if (fi.TypeName.find("Property") == std::string::npos) {
            uintptr_t next = 0;
            if (!Macht::ReadSafe(current + DynOff::UFIELD_NEXT, next)) break;
            current = next;
            continue;
        }

        // Read UProperty-specific fields
        Macht::ReadSafe<int32_t>(current + DynOff::UPROPERTY_OFFSET, fi.Offset);
        Macht::ReadSafe<int32_t>(current + DynOff::UPROPERTY_ELEMSIZE, fi.Size);
        // ArrayDim precedes ElementSize on the UProperty layout too (UE4 <4.25);
        // see the FProperty path above. Garbage/zero -> keep the default 1.
        {
            int32_t arrayDim = 1;
            if (Macht::ReadSafe<int32_t>(current + DynOff::UPROPERTY_ELEMSIZE - 4, arrayDim)
                && arrayDim >= 1 && arrayDim <= 0x10000)
                fi.ArrayDim = arrayDim;
        }
        Macht::ReadSafe<uint64_t>(current + DynOff::UPROPERTY_FLAGS, fi.PropertyFlags);

        // UE4 UBoolProperty -> FieldMask byte
        if (fi.TypeName == "BoolProperty") {
            uint8_t boolBytes[4] = {};
            for (int tryOff : { DynOff::UBOOLPROP_FIELDSIZE, DynOff::UBOOLPROP_FIELDSIZE - 4,
                                DynOff::UBOOLPROP_FIELDSIZE + 4, DynOff::UBOOLPROP_FIELDSIZE + 8,
                                DynOff::UBOOLPROP_FIELDSIZE - 8 }) {
                if (tryOff < 0) continue;
                if (!Macht::ReadBytesSafe(current + tryOff, boolBytes, 4)) continue;
                uint8_t fieldSize = boolBytes[0];
                uint8_t fieldMask = boolBytes[3];
                if (fieldSize == 1 && fieldMask != 0 && (fieldMask & (fieldMask - 1)) == 0) {
                    fi.boolFieldMask = fieldMask;
                    break;
                }
            }
        }

        if (!fi.Name.empty()) {
            fields.push_back(fi);
        }

        // Move to next UField via UField::Next
        uintptr_t next = 0;
        if (!Macht::ReadSafe(current + DynOff::UFIELD_NEXT, next)) break;
        current = next;
    }
}

// Cache for WalkClass results — class/struct field metadata doesn't change at
// runtime, so we cache by class address to avoid re-reading the FField chain
// on every WalkInstance call. This dramatically speeds up repeated drilldown/
// back navigation for large classes (e.g., 182 fields → 0ms vs re-walking).
static std::unordered_map<uintptr_t, ClassInfo> s_walkClassCache;

// --- LRU bound for the cache above (audit #5 U5) ---
//
// Legal here and ONLY here: WalkClass returns ClassInfo BY VALUE and every
// s_walkClassCache touch copies under s_walkClassCacheMutex, so evicting an entry
// cannot invalidate anything a caller is holding. The ENRICHED cache below hands
// out `const ClassInfo&` and is NOT bounded for exactly that reason.
//
// Recency is a list of addresses, newest at the front; the map from address to
// its list position makes touch-on-hit O(1). Both are guarded by the SAME mutex
// as the cache, so they cannot drift from it.
static std::list<uintptr_t> s_walkLru;
static std::unordered_map<uintptr_t, std::list<uintptr_t>::iterator> s_walkLruPos;

// Move `addr` to the front. Caller MUST hold s_walkClassCacheMutex.
static void TouchWalkLru(uintptr_t addr) {
    auto it = s_walkLruPos.find(addr);
    if (it == s_walkLruPos.end()) return;
    s_walkLru.splice(s_walkLru.begin(), s_walkLru, it->second);
}

// Insert-or-refresh `addr`, evicting the least recently used entries until the
// cache is within its bound. Caller MUST hold s_walkClassCacheMutex.
static void PublishWalkClass(uintptr_t addr, const ClassInfo& info) {
    auto [entry, inserted] = s_walkClassCache.try_emplace(addr, info);
    if (!inserted) { TouchWalkLru(addr); return; }

    s_walkLru.push_front(addr);
    s_walkLruPos[addr] = s_walkLru.begin();

    while (s_walkLru.size() > Ubel::kMaxWalkClassCacheEntries) {
        uintptr_t victim = s_walkLru.back();
        s_walkLru.pop_back();
        s_walkLruPos.erase(victim);
        s_walkClassCache.erase(victim);
    }
}

// Synthesize the native field layout for intrinsic UE core structs that carry no
// reflected child UPROPERTYs. FDateTime / FTimespan serialize a single `int64 Ticks`
// via custom serialization, not reflection, so their FField chain is empty and the
// UI can neither expand nor edit them. Injecting the known layout (one int64 Ticks at
// offset 0) lets the Live Walker drill in and in-place edit the raw ticks. Display-only
// formatting (readable date / duration) lives in the UI; the raw int64 is what gets
// read, edited, and exported. Keep this list small and exact — only structs whose
// native layout is stable and that genuinely lack reflected members.
static void InjectIntrinsicStructFields(const std::string& structName,
                                        std::vector<FieldInfo>& fields) {
    if (structName == "DateTime" || structName == "Timespan") {
        FieldInfo ticks{};
        ticks.Address  = 0;            // synthetic — no backing FProperty
        ticks.Name     = "Ticks";
        ticks.TypeName = "Int64Property";
        ticks.Offset   = 0;
        ticks.Size     = 8;
        fields.push_back(ticks);
    }
}

void GetClassCacheStats(size_t& outEntries, size_t& outFields, size_t& outApproxBytes) {
    std::lock_guard<std::mutex> lk(s_walkClassCacheMutex);
    outEntries = s_walkClassCache.size();
    outFields = 0;
    outApproxBytes = 0;
    for (const auto& kv : s_walkClassCache) {
        const ClassInfo& ci = kv.second;
        outFields += ci.Fields.size();
        outApproxBytes += EstimateClassInfoBytes(ci.Name.size(), ci.FullPath.size(),
                                                 ci.SuperName.size(), ci.Fields.size(),
                                                 sizeof(FieldInfo));
    }
}

ClassInfo WalkClass(uintptr_t uclassAddr) {
    ClassInfo info{};
    if (!uclassAddr) return info;

    // Check cache first. Return a copy so callers read lock-free; node-based
    // unordered_map keeps the entry alive regardless of later inserts.
    {
        std::lock_guard<std::mutex> lk(s_walkClassCacheMutex);
        auto cacheIt = s_walkClassCache.find(uclassAddr);
        if (cacheIt != s_walkClassCache.end()) {
            TouchWalkLru(uclassAddr);   // the lock is exclusive, so mutating on read is fine
            return cacheIt->second;
        }
    }

    info.Address = uclassAddr;
    info.Name = GetName(uclassAddr);
    info.FullPath = GetFullName(uclassAddr);

    // Read SuperStruct
    Macht::ReadSafe(uclassAddr + DynOff::USTRUCT_SUPER, info.SuperClass);
    if (info.SuperClass) {
        info.SuperName = GetName(info.SuperClass);
        // Where this class's OWN properties start -- see the field's comment in Ubel.h.
        Macht::ReadSafe(info.SuperClass + DynOff::USTRUCT_PROPSSIZE, info.SuperPropertiesSize);
    }

    // Read PropertiesSize. The return is NOT discarded: it is half of the
    // memoization gate below (ReadSafe zeroes its out-param on failure, and 0 is a
    // legitimate PropertiesSize, so the value alone cannot distinguish "this class
    // declares nothing" from "this address is not mapped").
    const bool propsSizeReadOk =
        Macht::ReadSafe(uclassAddr + DynOff::USTRUCT_PROPSSIZE, info.PropertiesSize);

    // A FAULT here means uclassAddr is not mapped at all, which is a different fact
    // from "the value looks wrong": USTRUCT_PROPSSIZE is a small in-object offset
    // (childPropsOff + 8), so even a mis-derived one still lands inside a mapped
    // object. Only an unmapped page faults — and that verdict is offset-independent,
    // which is why bailing on it is safe on a forked layout where the value test
    // would not be. Skips 4096 bounded-but-real FNamePool lookups down a garbage
    // FField chain. Falls through to the same un-memoized exit as the value gate.
    if (!propsSizeReadOk) {
        Sein::Warn("WALK:safe",
            "WalkClass: 0x%llx is not readable at +0x%X — not a UStruct, or freed memory",
            (unsigned long long)uclassAddr, DynOff::USTRUCT_PROPSSIZE);
        return info;
    }

    LOG_DEBUG("WalkClass: %s (super=%s, size=%d) at 0x%llX",
              info.Name.c_str(), info.SuperName.c_str(), info.PropertiesSize,
              static_cast<unsigned long long>(uclassAddr));

    // Walk the property chain — dispatch based on UProperty vs FProperty mode
    if (DynOff::bUseFProperty) {
        // UE4.25+ / UE5: FField chain via ChildProperties
        // Tag-bit stripping is handled inside WalkFFieldChain for UE5.3+ safety
        uintptr_t childProps = 0;
        if (Macht::ReadSafe(uclassAddr + DynOff::USTRUCT_CHILDPROPS, childProps) && childProps) {
            WalkFFieldChain(childProps, info.Fields);
        }
    } else {
        // UE4 <4.25: UProperty chain via Children (UField chain includes properties)
        uintptr_t children = 0;
        if (Macht::ReadSafe(uclassAddr + DynOff::USTRUCT_CHILDREN, children) && children) {
            WalkUPropertyChain(children, info.Fields);
        }
    }

    // Capture the own-properties floor HERE -- after the own chain walk and BEFORE the
    // super chain is prepended below, which is the only point where info.Fields holds
    // exactly this class's own properties. See ClassInfo::OwnPropertiesStart.
    for (const auto& f : info.Fields) {
        if (info.OwnPropertiesStart < 0 || f.Offset < info.OwnPropertiesStart)
            info.OwnPropertiesStart = f.Offset;
    }

    // Walk inherited fields from SuperStruct chain.
    // Optimization: if a super class is already cached, reuse its fields
    // (which already include ITS super chain) instead of re-walking.
    uintptr_t super = info.SuperClass;
    int depth = 0;
    while (super != 0 && depth < 32) {
        // Check if super is already in cache — if so, use its full field list
        // (which includes its own supers) and stop walking further. Copy the
        // cached super's fields out while holding the lock.
        {
            std::lock_guard<std::mutex> lk(s_walkClassCacheMutex);
            auto superCacheIt = s_walkClassCache.find(super);
            if (superCacheIt != s_walkClassCache.end()) {
                // A base class is reused by every subclass, so it is exactly what must
                // not be evicted for being "old" — the chain walk is a use.
                TouchWalkLru(super);
                const auto& superFields = superCacheIt->second.Fields;
                info.Fields.insert(info.Fields.begin(), superFields.begin(), superFields.end());
                break;  // cached super already includes its entire inheritance chain
            }
        }

        if (DynOff::bUseFProperty) {
            uintptr_t superChildProps = 0;
            if (Macht::ReadSafe(super + DynOff::USTRUCT_CHILDPROPS, superChildProps) && superChildProps) {
                std::vector<FieldInfo> inherited;
                WalkFFieldChain(superChildProps, inherited);
                info.Fields.insert(info.Fields.begin(), inherited.begin(), inherited.end());
            }
        } else {
            uintptr_t superChildren = 0;
            if (Macht::ReadSafe(super + DynOff::USTRUCT_CHILDREN, superChildren) && superChildren) {
                std::vector<FieldInfo> inherited;
                WalkUPropertyChain(superChildren, inherited);
                info.Fields.insert(info.Fields.begin(), inherited.begin(), inherited.end());
            }
        }

        uintptr_t nextSuper = 0;
        Macht::ReadSafe(super + DynOff::USTRUCT_SUPER, nextSuper);
        super = nextSuper;
        ++depth;
    }

    // Intrinsic core structs (FDateTime / FTimespan) reflect no child UPROPERTYs, so
    // the chain above yields nothing. Synthesize their native layout so the UI can
    // expand and edit the raw ticks. Only fires when no real fields were found, so any
    // engine build that *does* reflect them is left untouched.
    if (info.Fields.empty()) {
        InjectIntrinsicStructFields(info.Name, info.Fields);
    }

    // Sort by offset for clean display
    std::sort(info.Fields.begin(), info.Fields.end(),
              [](const FieldInfo& a, const FieldInfo& b) { return a.Offset < b.Offset; });

    LOG_INFO("WalkClass: %s — %zu fields", info.Name.c_str(), info.Fields.size());

    // Cache the result for subsequent WalkInstance calls. Concurrent builders of the
    // same class produce an equal ClassInfo (idempotent), so first-writer wins just as
    // harmlessly as last-writer — but try_emplace is NOT interchangeable with
    // `cache[addr] = info` here, because an assign-over-existing destroys the entry's
    // Fields vector. No reference into THIS map escapes (the hit at the top of this
    // function and the super-chain reuse below both COPY under s_walkClassCacheMutex);
    // it is s_walkClassExCache that hands out `const ClassInfo&`, and both maps follow
    // the same rule so the two cannot drift. Node-based map + no erase/clear anywhere
    // in dll/src ⇒ entries never move. (B10)
    //
    // The publish is GATED (audit #5 U4). It used to be unconditional, so any caller
    // handing in a non-UStruct address poisoned the cache permanently — and the caller
    // that does exactly that is in-tree and shipped: WalkInstance walks the class FIRST
    // and only then applies IsPlausiblePropertiesSize to decide the address is recycled, and
    // UE5_WalkClassBegin (Frieren) is used by ue5_dissect.lua as its is-this-an-instance
    // probe, so it feeds raw INSTANCE addresses in by design. Deliberately gating the
    // PUBLISH and not the walk: DynOff::USTRUCT_PROPSSIZE is derived (childPropsOff+8,
    // Genau), never independently probed, so on a forked layout a pre-walk bail would
    // turn "fields fine, size wrong" into "no fields at all". Refusing to memoize only
    // costs a re-walk.
    if (ShouldPublishClassWalk(propsSizeReadOk, info.PropertiesSize)) {
        std::lock_guard<std::mutex> lk(s_walkClassCacheMutex);
        PublishWalkClass(uclassAddr, info);
    } else {
        // Name the term that actually fired. The old text asserted a disjunction it
        // had not measured ("not a UStruct, or recycled memory") about classes that
        // demonstrably parsed — P3R's two SaveGame classes walked their fields on the
        // very same line. (SANEPROPS-2026-08-26)
        Sein::Warn("WALK:safe",
            "WalkClass: refusing to cache 0x%llx — PropertiesSize=%d is %s (read %s); "
            "the class pointer looks recycled",
            (unsigned long long)uclassAddr, info.PropertiesSize,
            info.PropertiesSize < 0 ? "negative" : "beyond the plausibility ceiling",
            propsSizeReadOk ? "ok" : "FAILED");
    }

    return info;
}

// --- WalkClassEx: enriched field type metadata ---

// Helper: read an FProperty* from a field address at a given offset, validate it returns
// a known property type name.  Returns the inner FProperty* address and its type name,
// or (0, "") if not found.
static std::pair<uintptr_t, std::string> ProbeInnerProperty(uintptr_t fieldAddr, int baseOffset) {
    static const int kProbeDeltas[] = { 0, 8, 4, 0xC, -4, -8, 0x10, -0x10 };
    for (int delta : kProbeDeltas) {
        int off = baseOffset + delta;
        if (off < 0) continue;
        uintptr_t inner = 0;
        if (!Macht::ReadSafe(fieldAddr + off, inner) || !inner) continue;
        std::string tn = GetFieldTypeName(inner);
        if (!tn.empty() && tn != "Unknown" && tn.find("Property") != std::string::npos)
            return { inner, tn };
    }
    return { 0, "" };
}

// Public: resolve an FProperty/UProperty address to (name, type), validating
// that it is a real property (type name contains "Property"). Reuses
// GetFieldTypeName (FProperty) / the UProperty UClass-name path (UE4).
bool ResolvePropertyNameType(uintptr_t fieldAddr, std::string& outName, std::string& outType) {
    outName.clear();
    outType.clear();
    if (!fieldAddr) return false;

    if (DynOff::bUseFProperty) {
        outType = GetFieldTypeName(fieldAddr);                 // FFieldClass name
        if (outType.empty() || outType == "Unknown"
            || outType.find("Property") == std::string::npos)
            return false;
        outName = ReadFName(fieldAddr + DynOff::FFIELD_NAME);
    } else {
        // UE4 UProperty is a UObject: its UClass name (e.g. "FloatProperty") is the type.
        uintptr_t cls = 0;
        if (!Macht::ReadSafe(fieldAddr + Grimoire::OFF_UOBJECT_CLASS, cls) || !cls)
            return false;
        outType = GetName(cls);
        if (outType.empty() || outType.find("Property") == std::string::npos)
            return false;
        outName = GetName(fieldAddr);                          // UProperty name (OFF_UOBJECT_NAME)
    }

    // Reject non-ASCII / empty names (failed deref landing on garbage).
    if (outName.empty() || static_cast<unsigned char>(outName[0]) < 0x20
        || static_cast<unsigned char>(outName[0]) >= 0x7F)
        return false;
    return true;
}

// Helper: given an FProperty* for StructProperty/ObjectProperty/ClassProperty,
// read the UScriptStruct*/UClass* at the subclass extension offset and return its name.
static std::string ReadSubclassTypeName(uintptr_t propAddr) {
    uintptr_t ptr = 0;
    if (!Macht::ReadSafe(propAddr + DynOff::FSTRUCTPROP_STRUCT, ptr) || !ptr) return "";
    std::string name = GetName(ptr);
    if (name.empty() || name[0] < 0x20 || name[0] >= 0x7F) return "";
    return name;
}

// Forward declaration -- definition lives further down this file (line ~2441).
// WalkClassEx calls it at the top to ensure FSTRUCTPROP_STRUCT is calibrated
// before any caller reads FProperty subclass extension fields.
static void CorrectSubclassOffsets(const std::vector<FieldInfo>& fields);

// Memo for the ENRICHED walk. Separate from s_walkClassCache because the two hold
// different things: that one holds WalkClass's plain fields, this one holds the same
// fields plus every structType / objClassName / innerType / enumName / boolFieldMask
// read on top of them. Four call sites were already commented `// cached` — they were
// not, and the difference is not academic: WalkClass's Fields is the FLATTENED
// inheritance chain, so an Actor subclass carries 100-300 FieldInfo × 14 std::string,
// deep-copied on every call, and snapshot capture / group scan reach this per struct-
// array ELEMENT from every ParallelGObjectsScan worker at once. (B10)
static std::unordered_map<uintptr_t, ClassInfo> s_walkClassExCache;
static std::mutex s_walkClassExCacheMutex;

const ClassInfo& WalkClassEx(uintptr_t uclassAddr) {
    // Reference-return needs a stable object for the failure case too.
    static const ClassInfo s_emptyClassInfo{};
    if (!uclassAddr) return s_emptyClassInfo;

    {
        std::lock_guard<std::mutex> lk(s_walkClassExCacheMutex);
        auto it = s_walkClassExCache.find(uclassAddr);
        if (it != s_walkClassExCache.end()) return it->second;
    }

    ClassInfo info = WalkClass(uclassAddr);

    // Same memoization gate WalkClass applies, for the same reason (audit #5 U4) —
    // this cache is the more widely consulted of the two (Property/Value Search,
    // snapshot capture, CE export, Solitar, Solide), so leaving it poisonable while
    // fixing only WalkClass would close the smaller half. `propsSizeReadOk` is true
    // by construction here: WalkClass returns early on a read fault, so an unmapped
    // address arrives with PropertiesSize == 0 and no fields, and only the value test
    // can fire. Refusing to memoize means refusing to RETURN too — the signature is a
    // reference into this map — so a rejected class reads as empty rather than as
    // garbage fields. That trade is bounded: WalkInstance already hard-fails on this
    // exact predicate, so an engine fork that mis-derives USTRUCT_PROPSSIZE is broken
    // before reaching here; this widens an existing failure rather than creating one.
    // Placed BEFORE CorrectSubclassOffsets so a garbage class cannot calibrate the
    // process-wide FSTRUCTPROP_STRUCT offset off its own bogus fields.
    if (!ShouldPublishClassWalk(true, info.PropertiesSize)) {
        // Logged, because this refusal is INVISIBLE otherwise and it is not a cache
        // miss: the signature returns a reference into the map, so a refused class
        // reads as a class with NO FIELDS to all ~26 external callers (Aura's
        // container / ref caches treat `Address != cls` as the refusal signal). On
        // P3R that silently hid both USaveGame classes from Value Search, Group Scan,
        // snapshot capture, CE export, Solitar and Solide. WalkClass has already
        // warned for this same address on this same path, so this adds one line per
        // refused class, not a flood. (SANEPROPS-2026-08-26)
        Sein::Warn("WALK:safe",
            "WalkClassEx: 0x%llx REFUSED (PropertiesSize=%d) — returning an EMPTY "
            "ClassInfo, so every caller will see this class as having no fields",
            (unsigned long long)uclassAddr, info.PropertiesSize);
        return s_emptyClassInfo;
    }

    // Calibrate FSTRUCTPROP_STRUCT (and the FProperty subclass extension
    // offsets that share its slot) BEFORE reading them. Historically this
    // calibration only ran inside WalkInstance -- which meant any caller
    // that hit WalkClassEx without a prior WalkInstance (e.g. the Value
    // Search tab's GObjects walk, build 738+) saw uncalibrated reads:
    // ReadSubclassTypeName returns "" for every StructProperty, the
    // nested-struct recursion in Aura::ScanForValue bails, and the user
    // gets 0 candidates on GAS / FGameplayAttributeData scans.
    //
    // CorrectSubclassOffsets is idempotent (guarded by an atomic), so
    // calling it on every WalkClassEx is a no-op after the first
    // successful probe. The cost on cold call is bounded: at most 7
    // probe-delta reads per StructProperty until one validates.
    CorrectSubclassOffsets(info.Fields);

    // Enrich each field with extended type metadata
    for (auto& fi : info.Fields) {
        if (!fi.Address) continue;

        const auto& tn = fi.TypeName;

        // StructProperty -> UScriptStruct name
        if (tn == "StructProperty") {
            fi.structType = ReadSubclassTypeName(fi.Address);
        }

        // ObjectProperty / ClassProperty / WeakObjectProperty / SoftObjectProperty / SoftClassProperty
        // / InterfaceProperty -> target UClass name
        // FObjectPropertyBase::PropertyClass is at the same offset as FStructProperty::Struct
        else if (tn == "ObjectProperty" || tn == "ClassProperty"
              || tn == "WeakObjectProperty" || tn == "SoftObjectProperty"
              || tn == "SoftClassProperty" || tn == "InterfaceProperty"
              || tn == "LazyObjectProperty") {
            fi.objClassName = ReadSubclassTypeName(fi.Address);
        }

        // ArrayProperty -> inner type
        else if (tn == "ArrayProperty") {
            auto [innerProp, innerTn] = ProbeInnerProperty(fi.Address, DynOff::FARRAYPROP_INNER);
            if (innerProp) {
                fi.innerType = innerTn;
                if (innerTn == "StructProperty")
                    fi.innerStructType = ReadSubclassTypeName(innerProp);
                else if (innerTn == "ObjectProperty" || innerTn == "ClassProperty")
                    fi.innerObjClass = ReadSubclassTypeName(innerProp);
            }
        }

        // OptionalProperty (UE 5.2+) -> wrapped value type.
        // FOptionalProperty is FProperty + FProperty* ValueProperty — same
        // shape as FArrayProperty, so reuse the same Inner offset probe.
        else if (tn == "OptionalProperty") {
            auto [innerProp, innerTn] = ProbeInnerProperty(fi.Address, DynOff::FARRAYPROP_INNER);
            if (innerProp) {
                fi.innerType = innerTn;
                if (innerTn == "StructProperty")
                    fi.innerStructType = ReadSubclassTypeName(innerProp);
                else if (innerTn == "ObjectProperty" || innerTn == "ClassProperty")
                    fi.innerObjClass = ReadSubclassTypeName(innerProp);
            }
        }

        // MapProperty -> key type + value type
        // FMapProperty layout: KeyProp at ext+0, ValueProp at ext+8 (same probe as WalkInstance)
        else if (tn == "MapProperty") {
            static const int kProbeDeltas[] = { 0, 8, 4, 0xC, -4, -8, 0x10, -0x10 };
            for (int delta : kProbeDeltas) {
                int tryOff = DynOff::FSTRUCTPROP_STRUCT + delta;
                if (tryOff < 0) continue;
                uintptr_t keyProp = 0;
                if (!Macht::ReadSafe(fi.Address + tryOff, keyProp) || !keyProp) continue;
                std::string keyTn = GetFieldTypeName(keyProp);
                if (keyTn.empty() || keyTn == "Unknown" || keyTn.find("Property") == std::string::npos)
                    continue;
                // Found KeyProp — ValueProp is at +8
                uintptr_t valueProp = 0;
                Macht::ReadSafe(fi.Address + tryOff + 8, valueProp);
                std::string valTn = valueProp ? GetFieldTypeName(valueProp) : "";
                if (valTn.empty() || valTn.find("Property") == std::string::npos) continue;

                fi.keyType = keyTn;
                fi.valueType = valTn;
                if (keyTn == "StructProperty")   fi.keyStructType = ReadSubclassTypeName(keyProp);
                if (valTn == "StructProperty")   fi.valueStructType = ReadSubclassTypeName(valueProp);
                break;
            }
        }

        // SetProperty -> element type
        else if (tn == "SetProperty") {
            auto [elemProp, elemTn] = ProbeInnerProperty(fi.Address, DynOff::FARRAYPROP_INNER);
            if (elemProp) {
                fi.elemType = elemTn;
                if (elemTn == "StructProperty")
                    fi.elemStructType = ReadSubclassTypeName(elemProp);
            }
        }

        // EnumProperty -> UEnum name
        else if (tn == "EnumProperty") {
            uintptr_t enumPtr = 0;
            if (Macht::ReadSafe(fi.Address + DynOff::FENUMPROP_ENUM, enumPtr) && enumPtr) {
                std::string ename = GetName(enumPtr);
                if (!ename.empty() && ename[0] >= 0x20 && ename[0] < 0x7F)
                    fi.enumName = ename;
            }
        }

        // ByteProperty -> check if it has an associated UEnum
        else if (tn == "ByteProperty") {
            uintptr_t enumPtr = 0;
            if (Macht::ReadSafe(fi.Address + DynOff::FBYTEPROP_ENUM, enumPtr) && enumPtr) {
                std::string ename = GetName(enumPtr);
                if (!ename.empty() && ename[0] >= 0x20 && ename[0] < 0x7F)
                    fi.enumName = ename;
            }
        }

        // BoolProperty -> FieldMask byte
        else if (tn == "BoolProperty") {
            uint8_t boolBytes[4] = {};
            int baseOff = DynOff::bUseFProperty ? DynOff::FBOOLPROP_FIELDSIZE : DynOff::UBOOLPROP_FIELDSIZE;
            for (int tryOff : { baseOff, baseOff - 4, baseOff + 4, baseOff + 8, baseOff - 8 }) {
                if (tryOff < 0) continue;
                if (!Macht::ReadBytesSafe(fi.Address + tryOff, boolBytes, 4)) continue;
                uint8_t fieldSize = boolBytes[0];
                uint8_t fieldMask = boolBytes[3];
                if (fieldSize == 1 && fieldMask != 0 && (fieldMask & (fieldMask - 1)) == 0) {
                    fi.boolFieldMask = fieldMask;
                    break;
                }
            }
        }
    }

    // try_emplace, never assign: a concurrent builder of the same class may already
    // have published an entry whose reference another thread is reading. The two
    // results are equal (the enrichment is a pure function of the same reads), so
    // keeping the existing one costs nothing and keeps every handed-out reference
    // valid. Node-based map + no erase/clear anywhere ⇒ entries never move. (B10)
    // Only reachable for a class that passed the memoization gate above.
    std::lock_guard<std::mutex> lk(s_walkClassExCacheMutex);
    return s_walkClassExCache.try_emplace(uclassAddr, std::move(info)).first->second;
}

// ============================================================
// Shared reflection field lookup — see Ubel.h. Semantics preserved from
// the original Frieren.cpp Debug Camera helper (DbgCam_FieldOffset):
// case-insensitive exact match first, then the first contains/excluding
// fuzzy match restricted to `typeFilter` (when given).
// ============================================================

static bool FieldNameIEquals(const std::string& a, const char* b) {
    size_t bl = std::strlen(b);
    if (a.size() != bl) return false;
    for (size_t i = 0; i < a.size(); ++i)
        if (std::tolower(static_cast<unsigned char>(a[i]))
            != std::tolower(static_cast<unsigned char>(b[i])))
            return false;
    return true;
}

static bool FieldNameIContains(const std::string& hay, const char* needle) {
    std::string h = hay, n = needle;
    std::transform(h.begin(), h.end(), h.begin(),
                   [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
    std::transform(n.begin(), n.end(), n.begin(),
                   [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
    return h.find(n) != std::string::npos;
}

bool FindField(uintptr_t classAddr, const char* exact,
               const char* contains, const char* excluding,
               const char* typeFilter, FieldInfo& out) {
    if (!classAddr || !exact) return false;
    ClassInfo ci = WalkClass(classAddr);
    const FieldInfo* fuzzy = nullptr;
    for (const auto& f : ci.Fields) {
        if (FieldNameIEquals(f.Name, exact)) { out = f; return true; }
        if (!fuzzy && contains && FieldNameIContains(f.Name, contains)
            && (!excluding || !FieldNameIContains(f.Name, excluding))
            && (!typeFilter || f.TypeName == typeFilter))
            fuzzy = &f;
    }
    if (fuzzy) { out = *fuzzy; return true; }
    return false;
}

int32_t FindFieldOffset(uintptr_t classAddr, const char* exact,
                        const char* contains, const char* excluding,
                        const char* typeFilter) {
    FieldInfo fi{};
    return FindField(classAddr, exact, contains, excluding, typeFilter, fi)
        ? fi.Offset : -1;
}

// Read UFunction::FunctionFlags (+ the NumParms/ParmsSize/ReturnValueOffset that
// sit at fixed offsets past it) into `fi`. Version-aware offset probe shared by
// WalkFunctions and ResolveFunctionInfo. `funcAddr` must already be a validated
// UFunction*; all reads are SEH-safe via Macht::ReadSafe.
static void ReadFuncFlagsAndParams(uintptr_t funcAddr, FunctionInfo& fi) {
    // Version- AND case-preserving-aware. The table, the measurements behind it, and
    // why 0xC0 must never appear here live on DynOff::FunctionFlagsOffsetFor
    // (Grimoire.h) — in the header so the test target can pin every row.
    uint32_t funcFlags = 0;
    int funcFlagsOff = -1;
    const int primary = DynOff::FunctionFlagsOffsetFor(g_cachedUEVersion,
                                                       DynOff::bCasePreservingName);

    if (Macht::ReadSafe<uint32_t>(funcAddr + primary, funcFlags) && funcFlags != 0) {
        funcFlagsOff = primary;
    } else {
        // Fallback: try all known offsets (skip primary, already tried).
        for (int tryOff : DynOff::FUNCTIONFLAGS_SWEEP) {
            if (tryOff == primary) continue;
            if (Macht::ReadSafe<uint32_t>(funcAddr + tryOff, funcFlags) && funcFlags != 0) {
                funcFlagsOff = tryOff;
                break;
            }
        }
    }
    fi.functionFlags = funcFlags;

    // NumParms/ParmsSize/ReturnValueOffset are at fixed offsets relative to
    // FunctionFlags (stable across all UE versions):
    //   +0x04 = NumParms (uint8)  +0x06 = ParmsSize (uint16)  +0x08 = ReturnValueOffset (uint16)
    if (funcFlagsOff >= 0) {
        Macht::ReadSafe<uint8_t> (funcAddr + funcFlagsOff + 0x04, fi.numParms);
        Macht::ReadSafe<uint16_t>(funcAddr + funcFlagsOff + 0x06, fi.parmsSize);
        Macht::ReadSafe<uint16_t>(funcAddr + funcFlagsOff + 0x08, fi.returnValueOffset);
    }
}

// Resolve a single UFunction* to its (name, fullName, flags, numParms, parmsSize).
// Validates the meta-class name == "Function" first — this guards against stale/
// recycled pointers (e.g. a UFunction* recorded by the Live PE profiler whose slot
// was reused after a GC / level-load): a recycled object won't deref to a class
// named "Function". All reads are SEH-safe via Macht::ReadSafe, so a dead pointer
// fails safe. Returns false when funcAddr is not (or no longer) a UFunction.
bool ResolveFunctionInfo(uintptr_t funcAddr, FunctionInfo& out) {
    if (!funcAddr) return false;
    uintptr_t metaClass = 0;
    if (!Macht::ReadSafe(funcAddr + Grimoire::OFF_UOBJECT_CLASS, metaClass) || !metaClass)
        return false;
    if (ReadFName(metaClass + Grimoire::OFF_UOBJECT_NAME) != "Function")
        return false;
    out = FunctionInfo{};
    out.address  = funcAddr;
    out.name     = GetName(funcAddr);
    out.fullName = GetFullName(funcAddr);
    ReadFuncFlagsAndParams(funcAddr, out);
    return true;
}

// --- WalkFunctions: enumerate UFunctions of a UClass ---

std::vector<FunctionInfo> WalkFunctions(uintptr_t uclassAddr) {
    std::vector<FunctionInfo> funcs;
    if (!uclassAddr) return funcs;

    // CPF_ReturnParm = 0x0400, CPF_OutParm = 0x0100
    constexpr uint64_t CPF_ReturnParm = 0x0400;
    constexpr uint64_t CPF_OutParm    = 0x0100;

    // Walk the UField::Children chain (UStruct::Children at 0x48)
    // This chain contains UFunctions (and possibly other UField types)
    uintptr_t child = 0;
    if (!Macht::ReadSafe(uclassAddr + DynOff::USTRUCT_CHILDREN, child) || !child)
        return funcs;

    int safetyLimit = 4096;
    std::unordered_set<uintptr_t> seenChildren;
    seenChildren.reserve(64);
    while (child != 0 && safetyLimit-- > 0) {
        if (!seenChildren.insert(child).second) {
            Sein::Warn("WALK:safe", "WalkFunctions: Children cycle at 0x%llx, aborting",
                (unsigned long long)child);
            break;
        }
        // Check if this child is a UFunction by reading its class name
        uintptr_t childClass = 0;
        if (Macht::ReadSafe(child + Grimoire::OFF_UOBJECT_CLASS, childClass) && childClass) {
            std::string clsName = ReadFName(childClass + Grimoire::OFF_UOBJECT_NAME);

            if (clsName == "Function") {
                FunctionInfo fi{};
                fi.name = GetName(child);
                fi.fullName = GetFullName(child);
                fi.address = child;

                // FunctionFlags + NumParms/ParmsSize/ReturnValueOffset — version-aware
                // probe shared with ResolveFunctionInfo (the Live PE profiler path).
                ReadFuncFlagsAndParams(child, fi);

                // Walk the UFunction's own property chain (its parameters)
                // UFunction inherits UStruct, so ChildProperties is at USTRUCT_CHILDPROPS
                if (DynOff::bUseFProperty) {
                    uintptr_t paramChain = 0;
                    if (Macht::ReadSafe(child + DynOff::USTRUCT_CHILDPROPS, paramChain) && paramChain) {
                        uintptr_t cur = DynOff::StripFFieldTag(paramChain);
                        int paramLimit = 256;
                        std::unordered_set<uintptr_t> seenParams;
                        while (cur != 0 && paramLimit-- > 0) {
                            if (!seenParams.insert(cur).second) {
                                Sein::Warn("WALK:safe", "WalkFunctions: param FField cycle at 0x%llx", (unsigned long long)cur);
                                break;
                            }
                            if (DynOff::IsFFieldVariantUObject(cur)) break;

                            FunctionParam param{};
                            param.name = ReadFName(cur + DynOff::FFIELD_NAME);
                            param.typeName = GetFieldTypeName(cur);
                            Macht::ReadSafe<int32_t>(cur + DynOff::FPROPERTY_ELEMSIZE, param.size);
                            Macht::ReadSafe<int32_t>(cur + DynOff::FPROPERTY_OFFSET, param.offset);

                            uint64_t propFlags = 0;
                            Macht::ReadSafe<uint64_t>(cur + DynOff::FPROPERTY_FLAGS, propFlags);

                            param.isReturn = (propFlags & CPF_ReturnParm) != 0;
                            param.isOut = (propFlags & CPF_OutParm) != 0;

                            // StructProperty -> read UScriptStruct name + sub-field layout
                            if (param.typeName == "StructProperty") {
                                param.structType = ReadSubclassTypeName(cur);
                                // Phase B: walk the UScriptStruct to discover sub-fields
                                uintptr_t structPtr = 0;
                                if (Macht::ReadSafe(cur + DynOff::FSTRUCTPROP_STRUCT, structPtr) && structPtr) {
                                    ClassInfo structInfo = WalkClass(structPtr);
                                    for (const auto& sf : structInfo.Fields)
                                        param.structFields.push_back({sf.Name, sf.TypeName, sf.Offset, sf.Size});
                                }
                            }
                            // Stage 1: Object/Class/Soft/Weak/Lazy/Interface params
                            // expose their target UClass name (FObjectPropertyBase::
                            // PropertyClass lives at the same FProperty subclass
                            // extension slot as FStructProperty::Struct — mirrors
                            // the WalkClassEx field-side enrichment at line 599).
                            else if (param.typeName == "ObjectProperty"     || param.typeName == "ClassProperty"
                                  || param.typeName == "WeakObjectProperty" || param.typeName == "SoftObjectProperty"
                                  || param.typeName == "SoftClassProperty"  || param.typeName == "InterfaceProperty"
                                  || param.typeName == "LazyObjectProperty") {
                                param.objClassName = ReadSubclassTypeName(cur);
                            }

                            if (param.isReturn)
                                fi.returnType = param.typeName;

                            if (!param.name.empty())
                                fi.params.push_back(param);

                            uintptr_t next = 0;
                            if (!Macht::ReadSafe(cur + DynOff::FFIELD_NEXT, next)) break;
                            cur = DynOff::StripFFieldTag(next);
                        }
                    }
                } else {
                    // UE4 <4.25: UProperty chain via Children
                    uintptr_t paramChain = 0;
                    if (Macht::ReadSafe(child + DynOff::USTRUCT_CHILDREN, paramChain) && paramChain) {
                        uintptr_t cur = paramChain;
                        int paramLimit = 256;
                        std::unordered_set<uintptr_t> seenParams;
                        while (cur != 0 && paramLimit-- > 0) {
                            if (!seenParams.insert(cur).second) {
                                Sein::Warn("WALK:safe", "WalkFunctions: param UProperty cycle at 0x%llx", (unsigned long long)cur);
                                break;
                            }
                            FunctionParam param{};
                            param.name = ReadFName(cur + Grimoire::OFF_UOBJECT_NAME);

                            uintptr_t paramCls = 0;
                            if (Macht::ReadSafe(cur + Grimoire::OFF_UOBJECT_CLASS, paramCls) && paramCls)
                                param.typeName = ReadFName(paramCls + Grimoire::OFF_UOBJECT_NAME);

                            Macht::ReadSafe<int32_t>(cur + DynOff::UPROPERTY_ELEMSIZE, param.size);
                            Macht::ReadSafe<int32_t>(cur + DynOff::UPROPERTY_OFFSET, param.offset);

                            uint64_t propFlags = 0;
                            Macht::ReadSafe<uint64_t>(cur + DynOff::UPROPERTY_FLAGS, propFlags);

                            param.isReturn = (propFlags & CPF_ReturnParm) != 0;
                            param.isOut = (propFlags & CPF_OutParm) != 0;

                            // UE4 StructProperty -> read UScriptStruct name + sub-field layout
                            if (param.typeName == "StructProperty") {
                                uintptr_t structPtr = 0;
                                // UStructProperty::Struct is at UPROPERTY subclass extension offset
                                if (Macht::ReadSafe(cur + DynOff::UPROPERTY_OFFSET + 0x2C, structPtr) && structPtr) {
                                    std::string sn = GetName(structPtr);
                                    if (!sn.empty() && sn[0] >= 0x20 && sn[0] < 0x7F)
                                        param.structType = sn;
                                    // Phase B: walk the UScriptStruct to discover sub-fields
                                    ClassInfo structInfo = WalkClass(structPtr);
                                    for (const auto& sf : structInfo.Fields)
                                        param.structFields.push_back({sf.Name, sf.TypeName, sf.Offset, sf.Size});
                                }
                            }
                            // Stage 1 (UE4 <4.25 path): same UProperty subclass
                            // extension slot as UStructProperty::Struct holds
                            // UObjectPropertyBase::PropertyClass — both are the
                            // first derived field after the UProperty base.
                            else if (param.typeName == "ObjectProperty"     || param.typeName == "ClassProperty"
                                  || param.typeName == "WeakObjectProperty" || param.typeName == "SoftObjectProperty"
                                  || param.typeName == "SoftClassProperty"  || param.typeName == "InterfaceProperty"
                                  || param.typeName == "LazyObjectProperty") {
                                uintptr_t classPtr = 0;
                                if (Macht::ReadSafe(cur + DynOff::UPROPERTY_OFFSET + 0x2C, classPtr) && classPtr) {
                                    std::string cn = GetName(classPtr);
                                    if (!cn.empty() && cn[0] >= 0x20 && cn[0] < 0x7F)
                                        param.objClassName = cn;
                                }
                            }

                            if (param.isReturn) fi.returnType = param.typeName;
                            if (!param.name.empty()) fi.params.push_back(param);

                            uintptr_t next = 0;
                            if (!Macht::ReadSafe(cur + DynOff::UFIELD_NEXT, next)) break;
                            cur = next;
                        }
                    }
                }

                funcs.push_back(std::move(fi));
            }
        }

        // Move to next UField via UField::Next
        uintptr_t next = 0;
        if (!Macht::ReadSafe(child + DynOff::UFIELD_NEXT, next)) break;
        child = next;
    }

    LOG_INFO("WalkFunctions: %zu functions found at 0x%llX",
             funcs.size(), static_cast<unsigned long long>(uclassAddr));
    return funcs;
}

// --- Live Instance Walking ---

/// Infer the expected element size from a well-known property type name.
/// Used as a fallback when FPROPERTY_ELEMSIZE reads 0 or garbage (e.g. Inner
/// FProperty in ArrayProperty where the ELEMSIZE offset doesn't apply).
static int32_t InferScalarSize(const std::string& typeName) {
    // Numeric scalars
    if (typeName == "FloatProperty")  return 4;
    if (typeName == "IntProperty")    return 4;
    if (typeName == "UInt32Property") return 4;
    if (typeName == "DoubleProperty") return 8;
    if (typeName == "Int64Property")  return 8;
    if (typeName == "UInt64Property") return 8;
    if (typeName == "Int16Property")  return 2;
    if (typeName == "UInt16Property") return 2;
    if (typeName == "Int8Property")   return 1;
    if (typeName == "ByteProperty")   return 1;
    if (typeName == "BoolProperty")   return 1;
    // Engine types with known fixed sizes
    // sizeof(FName): { ComparisonIndex(4) + Number(4) } = 8, and 0xC -- NOT 0x10 -- under
    // WITH_CASE_PRESERVING_NAME (+ DisplayIndex(4), alignof 4, no trailing padding).
    // 0x10 is the UObject NamePrivate->Outer SLOT, which is a different question: that
    // gap exists because OuterPrivate is an 8-aligned pointer, not because FName is 16.
    // This MUST be dynamic: ValidateArrayElemSize treats InferScalarSize as authoritative
    // and OVERRIDES the engine's reported ElementSize -- which for a NameProperty is
    // ALREADY the correct 0xC, so a wrong value here is actively substituted for a right one.
    // Feeds TArray<FName> stride, ComputeSetElementStride, and the TMap key size handed to
    // ComputeMapValueOffset -- which applies the pair padding ITSELF, so its input must be
    // the UNPADDED sizeof.
    if (typeName == "NameProperty")   return DynOff::SizeofFName();
    if (typeName == "ObjectProperty") return 8;  // UObject* on x64
    if (typeName == "ClassProperty")  return 8;  // UClass* (inherits ObjectProperty)
    if (typeName == "WeakObjectProperty")  return 8;  // FWeakObjectPtr = { int32 + int32 }
    // sizeof(TLazyObjectPtr) = FWeakObjectPtr(8) + the persistent-ptr envelope + FGuid(16).
    // ⛔ NOT a fixed 0x20. FUniqueObjectGuid is a bare FGuid (4×uint32, alignof 4), so there is
    // no pad after the tag: 0x1C up to 5.2, and 0x18 from 5.3 where TagAtLastTest was deleted.
    // 0x20 is the FWeakObjectPtr+Tag+pad+FGuid model audit A1 deleted, and it is wrong in EVERY
    // era. Dynamic for exactly the reason NameProperty above is: ValidateArrayElemSize treats
    // this as AUTHORITATIVE and overrides the engine's own ElementSize, so a wrong value here is
    // actively substituted for a right one — and ResolveInnerSize returns it before it ever asks
    // the engine, so nothing downstream could correct it.
    if (typeName == "LazyObjectProperty")  return LazyGuidOffset(0) + 0x10;
    // FScriptInterface = { UObject* + void* } — fixed 16
    if (typeName == "InterfaceProperty")   return 16;
    // NOTE: FScriptDelegate ({ FWeakObjectPtr(8) + FName }) is 16 or 24 depending on
    // CasePreservingName. TSoftObjectPtr / TSoftClassPtr also vary by UE version.
    // Do NOT override these here — let readSize prevail; the array readers force
    // their own correct stride.
    return 0;
}

/// Validate an element size read from FPROPERTY_ELEMSIZE for ArrayProperty Inner.
/// The Inner FProperty's ELEMSIZE offset often returns garbage because the inner
/// property has different metadata layout than top-level FField chain members.
/// Returns the validated size (overridden for known types, capped for unknown).
///
/// Logging: both branches log at Debug level. UE 5.7 + CasePreservingName
/// (e.g. TQ2) firing this 194 times per session was just recovery noise —
/// the override / zero-fallback path is well-tested and the next-line
/// "FArrayProperty::Inner found ... elemSize=N" Info entry already shows the
/// resolved size. Keep at Debug for developer diagnosis without polluting
/// user logs.
static int32_t ValidateArrayElemSize(int32_t readSize, const std::string& typeName) {
    int32_t expected = InferScalarSize(typeName);
    if (expected > 0) {
        // For known types, we know the exact size — override if it doesn't match
        if (readSize != expected) {
            Sein::Debug("WALK:ArrayP", "elemSize=%d is invalid for '%s' (expected=%d), overriding",
                readSize, typeName.c_str(), expected);
            return expected;
        }
        return readSize;
    }

    // For complex types (StructProperty, MapProperty, etc.), sanity-cap.
    // Legitimate struct sizes can be a few hundred bytes; anything > 65536 is garbage.
    if (readSize <= 0) {
        return 0;  // Caller handles zero-size case
    }
    if (readSize > 65536) {
        Sein::Debug("WALK:ArrayP", "elemSize=%d is unreasonably large for '%s', zeroing",
            readSize, typeName.c_str());
        return 0;
    }

    return readSize;
}

// ============================================================
// ResolveInnerSize — internal helper. Given an inner FProperty* + its
// type name, returns the authoritative per-element size:
//   1. Fixed sizes via InferScalarSize
//   2. FPROPERTY_ELEMSIZE validated by ValidateArrayElemSize
//   3. For StructProperty: UScriptStruct::PropertiesSize
// Returns 0 when undetermined.
// ============================================================
static int32_t ResolveInnerSize(uintptr_t innerProp, const std::string& innerTn) {
    int32_t es = InferScalarSize(innerTn);
    if (es > 0) return es;

    int32_t rawElemSize = 0;
    Macht::ReadSafe<int32_t>(innerProp + DynOff::FPROPERTY_ELEMSIZE, rawElemSize);
    es = ValidateArrayElemSize(rawElemSize, innerTn);
    if (es > 0) return es;

    if (innerTn == "StructProperty") {
        uintptr_t innerStruct = 0;
        if (Macht::ReadSafe(innerProp + DynOff::FSTRUCTPROP_STRUCT, innerStruct) && innerStruct) {
            int32_t ps = 0;
            if (Macht::ReadSafe(innerStruct + DynOff::USTRUCT_PROPSSIZE, ps)
                && ps > 0 && ps <= 65536)
                return ps;
        }
    }
    return 0;
}

// ============================================================
// GetArrayInnerElemSize — public helper used by container-aware
// Address Finder (Aura::FindInContainers).
//
// Probes the FArrayProperty's Inner FProperty at FARRAYPROP_INNER + delta
// (matching WalkInstance's probe list), validates the inner type, then
// resolves an authoritative element size.
// Returns 0 when the size cannot be determined.
// ============================================================
int32_t GetArrayInnerElemSize(uintptr_t fieldAddr) {
    if (!fieldAddr || !DynOff::bUseFProperty) return 0;

    auto [inner, innerTn] = ProbeInnerProperty(fieldAddr, DynOff::FARRAYPROP_INNER);
    if (!inner) return 0;

    return ResolveInnerSize(inner, innerTn);
}

// ============================================================
// GetSetElementStride — per-element stride within an FSetProperty's
// TSparseArray.Data buffer (used by container-aware Address Finder).
// stride = ComputeSetElementStride(elemSize)
// Returns 0 when the inner element size cannot be determined.
// ============================================================
int32_t GetSetElementStride(uintptr_t fieldAddr) {
    if (!fieldAddr || !DynOff::bUseFProperty) return 0;

    auto [inner, innerTn] = ProbeInnerProperty(fieldAddr, DynOff::FARRAYPROP_INNER);
    if (!inner) return 0;

    int32_t es = ResolveInnerSize(inner, innerTn);
    if (es <= 0) return 0;
    return Macht::ComputeSetElementStride(es);
}

// ============================================================
// GetStructAlignment — read UScriptStruct::MinAlignment.
//
// UStruct lays out `int32 PropertiesSize;` immediately followed by MinAlignment,
// so it sits at USTRUCT_PROPSSIZE + 4 (which is also why USTRUCT_SCRIPT is
// PROPSSIZE + 8). MinAlignment is int16 in UE 5.8 — StructStateFlags takes the
// other half of that word — and int32 in UE4 / early UE5. Reading the LOW 16 BITS
// is correct for BOTH on little-endian x64 because alignments are small; reading
// it as int32 would pick up StructStateFlags on newer engines.
//
// Returns 0 when it cannot be read or is not a sane power of two, which every
// caller treats as "unknown" and falls back to the previous behaviour.
//
// Exists because Scharf::RequiredAlignment deliberately returns 0 for
// StructProperty ("variable-layout ... skip validation") — it is a VALIDATION
// helper, and using it as a LAYOUT ORACLE meant every struct-valued TMap silently
// took ComputeMapValueOffset's size guess. That guess says "8 bytes or more =>
// align 8", so TMap<int32, FVector> put the value at +8 when FVector is 4-aligned
// and really sits at +4 — wrong for element 0, and wrong again in the stride.
// ============================================================
static int32_t GetStructAlignment(uintptr_t scriptStruct) {
    if (!scriptStruct || !Grimoire::IsUserspacePointer(scriptStruct)) return 0;
    int16_t minAlign = 0;
    if (!Macht::ReadSafe(scriptStruct + DynOff::USTRUCT_PROPSSIZE + 4, minAlign)) return 0;
    return Macht::SanitizeAlign(minAlign);
}

// ============================================================
// ResolveElementAlignment — the real alignment of a TMap key/value or a
// container element. Uses the per-type rule for everything Scharf can answer,
// and UScriptStruct::MinAlignment for StructProperty (which Scharf cannot).
// Returns 0 when still unknown.
// ============================================================
static int32_t ResolveElementAlignment(const std::string& typeName, int32_t size,
                                       uintptr_t structAddr) {
    if (typeName == "StructProperty")
        return GetStructAlignment(structAddr);
    return Scharf::RequiredAlignment(typeName, size, DynOff::bCasePreservingName);
}

// ============================================================
// GetContainerInnerStructAddr — resolve the inner-element UScriptStruct* of
// an ArrayProperty / SetProperty whose element is a StructProperty. Both probe
// Inner at FARRAYPROP_INNER (same offset). Returns 0 when the element is not a
// struct or the address can't be resolved. Used by the recursive deep
// container scan to descend into struct elements (separate nested allocations).
// ============================================================
uintptr_t GetContainerInnerStructAddr(uintptr_t fieldAddr) {
    if (!fieldAddr || !DynOff::bUseFProperty) return 0;
    auto [inner, innerTn] = ProbeInnerProperty(fieldAddr, DynOff::FARRAYPROP_INNER);
    if (!inner || innerTn != "StructProperty") return 0;
    uintptr_t innerStruct = 0;
    if (Macht::ReadSafe(inner + DynOff::FSTRUCTPROP_STRUCT, innerStruct) && innerStruct)
        return innerStruct;
    return 0;
}

// ============================================================
// GetMapPairStride — per-pair stride within an FMapProperty's
// TSparseArray.Data buffer (used by container-aware Address Finder).
// pair_size = ComputeMapValueOffset(keySize, valueSize) + valueSize
// stride    = ComputeSetElementStride(pair_size)
// Returns 0 when key or value size cannot be determined.
// ============================================================
bool GetMapPairLayout(uintptr_t fieldAddr, MapPairLayout& out) {
    out = {};
    if (!fieldAddr || !DynOff::bUseFProperty) return false;

    // Probe KeyProp (same offset as ArrayProperty Inner) — mirrors WalkInstance.
    static const int kProbeDeltas[] = { 0, 8, 4, 0xC, -4, -8, 0x10, -0x10 };
    for (int delta : kProbeDeltas) {
        int tryOff = DynOff::FSTRUCTPROP_STRUCT + delta;
        if (tryOff < 0) continue;
        uintptr_t keyProp = 0;
        if (!Macht::ReadSafe(fieldAddr + tryOff, keyProp) || !keyProp) continue;
        if (!Grimoire::IsUserspacePointer(keyProp)) continue;

        std::string keyTn = GetFieldTypeName(keyProp);
        if (keyTn.empty() || keyTn.find("Property") == std::string::npos) continue;

        // ValueProp follows at +8 within the FMapProperty's tail.
        uintptr_t valueProp = 0;
        if (!Macht::ReadSafe(fieldAddr + tryOff + 8, valueProp) || !valueProp) continue;
        std::string valTn = GetFieldTypeName(valueProp);
        if (valTn.empty() || valTn.find("Property") == std::string::npos) continue;

        int32_t keySize = ResolveInnerSize(keyProp, keyTn);
        int32_t valSize = ResolveInnerSize(valueProp, valTn);
        if (keySize <= 0 || valSize <= 0) return false;

        // Use the value property's REAL alignment (not a size guess) so the
        // pair stride + value offset match WalkInstance exactly — the deep
        // container scan indexes map slots by the same stride the UI shows
        // (and FName/FWeakObjectPtr values are 8 bytes but 4-aligned, so a
        // size guess would mis-stride the whole buffer). See ComputeMapValueOffset.
        // Resolve key / value UScriptStruct* FIRST — the alignments below need them.
        // (Also used by the deep container scan, so a TMap<K, FStruct> (or
        // <FStruct, V>) can be descended into.)
        if (keyTn == "StructProperty")
            Macht::ReadSafe(keyProp + DynOff::FSTRUCTPROP_STRUCT, out.keyStructAddr);
        if (valTn == "StructProperty")
            Macht::ReadSafe(valueProp + DynOff::FSTRUCTPROP_STRUCT, out.valueStructAddr);

        int32_t keyAlign  = ResolveElementAlignment(keyTn, keySize, out.keyStructAddr);
        int32_t valAlign  = ResolveElementAlignment(valTn, valSize, out.valueStructAddr);
        // alignof(TPair<K,V>) == max(alignof(K), alignof(V)); the stride must be a
        // multiple of it, or every element after index 0 lands at a wrong address.
        int32_t pairAlign = (keyAlign > valAlign) ? keyAlign : valAlign;

        int32_t valOffset = Macht::ComputeMapValueOffset(keySize, valSize, valAlign);
        int32_t pairSize  = valOffset + valSize;
        out.keySize     = keySize;
        out.valueSize   = valSize;
        out.valueOffset = valOffset;
        out.pairStride  = Macht::ComputeSetElementStride(pairSize, pairAlign);
        return true;
    }
    return false;
}

int32_t GetMapPairStride(uintptr_t fieldAddr) {
    MapPairLayout layout;
    return GetMapPairLayout(fieldAddr, layout) ? layout.pairStride : 0;
}

// === Reflected struct preview (audit U17) ===
//
// THE decoder for "what is inside this struct", and now the ONLY one. It reads
// each member at its own offset with its OWN declared width, so a UE5 LWC FVector
// (3 doubles) yields three real components instead of six float halves, and a
// vtable needs no special case at all — there is no member at +0, so nothing
// prints from there.
//
// This body used to be inline inside WalkInstance and nowhere else, which is why
// every OTHER surface (TMap keys/values, TSet elements, the Property Search
// preview column, DataTable rows) fell through to the byte-blind
// InterpretValue("StructProperty", ...) despite already holding the
// UScriptStruct*. Shared rather than copied on purpose: a second copy is how the
// report and the reality end up computed by different code paths.
//
// The width handling is pure and lives in Ubel.h (PreviewScalarValue), so it is
// unit-pinned; only NameProperty and Object/ClassProperty need process state and
// they are handled here.
std::string InterpretStructByLayout(const uint8_t* buf, int32_t size,
                                    const ClassInfo& si, int previewLimit) {
    if (!buf || size <= 0 || previewLimit <= 0) return "";

    std::string preview;
    int shown = 0;
    const int kMaxScanFields = 20;
    for (size_t idx = 0; idx < si.Fields.size() && static_cast<int>(idx) < kMaxScanFields; ++idx) {
        const auto& sf = si.Fields[idx];
        if (shown >= previewLimit) { preview += ", ..."; break; }

        int32_t sfSize = sf.Size;
        int32_t expected = InferScalarSize(sf.TypeName);
        if (expected > 0 && sfSize != expected) sfSize = expected;
        if (sf.Offset < 0 || sf.Offset + sfSize > size) continue;   // beyond the buffer

        const uint8_t* p = buf + sf.Offset;
        std::string val = PreviewScalarValue(sf.TypeName, p, sfSize);
        if (val.empty()) {
            if (sf.TypeName == "NameProperty" && sfSize >= 4) {
                val = DecodeFNameBytes(p, sfSize);   // Number included (U8)
                if (val.empty()) val = "None";
            } else if ((sf.TypeName == "ObjectProperty" || sf.TypeName == "ClassProperty")
                       && sfSize >= 8) {
                uintptr_t ptr; memcpy(&ptr, p, 8);
                val = ptr ? GetName(ptr) : "null";   // GetName uses the name cache
            } else {
                continue;   // not previewable
            }
        }
        if (!preview.empty()) preview += ", ";
        preview += sf.Name + "=" + val;
        ++shown;
    }
    return preview.empty() ? "" : "{" + preview + "}";
}

// Resolve a UScriptStruct* and decode `buf` through its reflected layout.
// Returns "" when the layout cannot be resolved, so the caller can fall back.
std::string InterpretStructAt(const uint8_t* buf, int32_t size,
                              uintptr_t structClassAddr, int previewLimit) {
    if (!structClassAddr) return "";
    const ClassInfo& si = WalkClassEx(structClassAddr);   // memoized
    if (si.Fields.empty()) return "";
    return InterpretStructByLayout(buf, size, si, previewLimit);
}

std::string InterpretValue(const std::string& typeName, const void* data, int32_t size) {
    if (!data || size <= 0) return "";

    auto bytes = static_cast<const uint8_t*>(data);

    if (typeName == "FloatProperty" && size >= 4) {
        float v;
        memcpy(&v, bytes, 4);
        // 10 decimal places; if fractional part is all zeros, show as integer
        char buf[64];
        snprintf(buf, sizeof(buf), "%.10f", v);
        std::string s(buf);
        auto dot = s.find('.');
        if (dot != std::string::npos) {
            bool allZero = true;
            for (size_t i = dot + 1; i < s.size(); ++i) {
                if (s[i] != '0') { allZero = false; break; }
            }
            if (allZero) s.erase(dot);
        }
        return s;
    }
    if (typeName == "DoubleProperty" && size >= 8) {
        double v;
        memcpy(&v, bytes, 8);
        // 15 decimal places; if fractional part is all zeros, show as integer
        char buf[80];
        snprintf(buf, sizeof(buf), "%.15f", v);
        std::string s(buf);
        auto dot = s.find('.');
        if (dot != std::string::npos) {
            bool allZero = true;
            for (size_t i = dot + 1; i < s.size(); ++i) {
                if (s[i] != '0') { allZero = false; break; }
            }
            if (allZero) s.erase(dot);
        }
        return s;
    }
    if (typeName == "IntProperty" && size >= 4) {
        int32_t v;
        memcpy(&v, bytes, 4);
        return std::to_string(v);
    }
    if (typeName == "UInt32Property" && size >= 4) {
        uint32_t v;
        memcpy(&v, bytes, 4);
        return std::to_string(v);
    }
    if (typeName == "Int64Property" && size >= 8) {
        int64_t v;
        memcpy(&v, bytes, 8);
        // std::to_string never uses scientific notation for integers
        return std::to_string(v);
    }
    if (typeName == "UInt64Property" && size >= 8) {
        uint64_t v;
        memcpy(&v, bytes, 8);
        return std::to_string(v);
    }
    if (typeName == "Int16Property" && size >= 2) {
        int16_t v;
        memcpy(&v, bytes, 2);
        return std::to_string(v);
    }
    if (typeName == "UInt16Property" && size >= 2) {
        uint16_t v;
        memcpy(&v, bytes, 2);
        return std::to_string(v);
    }
    if (typeName == "ByteProperty" && size >= 1) {
        return std::to_string(bytes[0]);
    }
    if (typeName == "Int8Property" && size >= 1) {
        return std::to_string(static_cast<int8_t>(bytes[0]));
    }
    if (typeName == "BoolProperty") {
        // Note: for bitfield bools, the caller should pass the correct byte
        // and use FieldMask to determine the bit value. This fallback handles
        // the simple case where the raw byte is passed.
        return bytes[0] ? "true" : "false";
    }
    if (typeName == "NameProperty" && size >= 4) {
        // FName — resolve via FNamePool, Number included (audit #5 U8)
        return DecodeFNameBytes(bytes, size);
    }

    // StructProperty: byte-blind float hint, LAST RESORT ONLY (audit U3).
    // The whole decode — including the vtable-skip decision that used to drop
    // leading members silently — lives in Ubel.h::InterpretStructBytes so it is
    // pure and unit-pinned; no target compiles this .cpp. Callers that can
    // resolve the UScriptStruct* must prefer the reflected-layout preview
    // (WalkInstance's "{Name=Value}"), which is width-correct and labelled.
    if (typeName == "StructProperty" && size >= 4) {
        return InterpretStructBytes(bytes, size);
    }

    return ""; // Unknown type — caller shows hex
}

// Prefer the reflected layout, fall back to the byte-blind hint.
//
// The four container surfaces (TMap key, TMap value, TSet element) and the
// preview column all ALREADY resolve the UScriptStruct* a few lines above their
// decode and then threw it away — that was the whole of U17. Struct-typed values
// now decode as "{X=1.5, Y=-2, Z=90}" with each member read at its own declared
// width; everything else is unchanged.
static std::string PreferLayout(const uint8_t* buf, int32_t size,
                                uintptr_t structAddr, const std::string& typeName) {
    if (typeName == "StructProperty" && structAddr) {
        std::string byLayout = InterpretStructAt(buf, size, structAddr, /*previewLimit=*/8);
        if (!byLayout.empty()) return byLayout;
    }
    return InterpretValue(typeName, buf, size);
}


// ============================================================
// IsScalarArrayType — check if an inner type name supports inline
// element reading (Phase B). Returns true for numeric, bool, enum,
// and name types. StructProperty, ObjectProperty, MapProperty, etc.
// are NOT scalar and are handled in Phase D/E.
// ============================================================
bool IsScalarArrayType(const std::string& innerTypeName) {
    return innerTypeName == "FloatProperty"
        || innerTypeName == "DoubleProperty"
        || innerTypeName == "IntProperty"
        || innerTypeName == "UInt32Property"
        || innerTypeName == "Int64Property"
        || innerTypeName == "UInt64Property"
        || innerTypeName == "Int16Property"
        || innerTypeName == "UInt16Property"
        || innerTypeName == "ByteProperty"
        || innerTypeName == "Int8Property"
        || innerTypeName == "BoolProperty"
        || innerTypeName == "NameProperty"
        || innerTypeName == "EnumProperty";
}

// ============================================================
// ReadArrayElements — read scalar elements from a TArray (Phase B).
//
// Reads up to `limit` elements starting at index `offset`.
// For EnumProperty / ByteProperty-with-enum, resolves enum names
// via the UEnum* stored in the Inner FProperty.
// ============================================================
ReadArrayResult ReadArrayElements(
    uintptr_t instanceAddr, int32_t fieldOffset,
    uintptr_t innerFFieldAddr, const std::string& innerTypeName,
    int32_t elemSize, int32_t offset, int32_t limit)
{
    ReadArrayResult result;
    result.ok = false;

    if (elemSize <= 0 || elemSize > 256) {
        result.error = "Invalid element size";
        return result;
    }

    // Read TArray header
    Macht::TArrayView arr;
    if (!Macht::ReadTArray(instanceAddr + fieldOffset, arr)) {
        result.error = "TArray read failed";
        return result;
    }
    result.totalCount = arr.Count;

    if (arr.Count <= 0 || !arr.Data) {
        result.ok = true;
        result.readCount = 0;
        return result;
    }

    // Clamp offset/limit
    if (offset < 0) offset = 0;
    if (offset >= arr.Count) {
        result.ok = true;
        result.readCount = 0;
        return result;
    }
    int32_t end = offset + limit;
    if (end > arr.Count) end = arr.Count;
    if (end - offset > kArrayElementsPerRequestCap) end = offset + kArrayElementsPerRequestCap;  // hard cap per request

    // For enum arrays: read UEnum* once from Inner FProperty
    uintptr_t enumPtr = 0;
    if (innerFFieldAddr) {
        if (innerTypeName == "EnumProperty") {
            Macht::ReadSafe(innerFFieldAddr + DynOff::FENUMPROP_ENUM, enumPtr);
        } else if (innerTypeName == "ByteProperty") {
            uintptr_t candidateEnum = 0;
            if (Macht::ReadSafe(innerFFieldAddr + DynOff::FBYTEPROP_ENUM, candidateEnum) && candidateEnum) {
                // Validate it's a UEnum
                uintptr_t enumClass = GetClass(candidateEnum);
                std::string enumClassName = enumClass ? GetName(enumClass) : "";
                if (enumClassName == "Enum" || enumClassName == "UserDefinedEnum")
                    enumPtr = candidateEnum;
            }
        }
    }
    result.enumAddr = enumPtr;  // Expose for CE DropDownList sharing

    // Read elements
    std::vector<uint8_t> buf(elemSize, 0);
    result.elements.reserve(end - offset);

    for (int32_t i = offset; i < end; ++i) {
        LiveFieldValue::ArrayElement elem;
        elem.index = i;

        uintptr_t elemAddr = arr.Data + static_cast<int64_t>(i) * elemSize;
        if (!Macht::ReadBytesSafe(elemAddr, buf.data(), elemSize)) {
            elem.value = "???";
            elem.hex = "??";
            result.elements.push_back(std::move(elem));
            continue;
        }

        // Build per-element hex string
        std::string hex;
        hex.reserve(elemSize * 2);
        for (int b = 0; b < elemSize; ++b) {
            char hx[3];
            snprintf(hx, sizeof(hx), "%02X", buf[b]);
            hex += hx;
        }
        elem.hex = std::move(hex);

        // Interpret value
        if (enumPtr) {
            // Enum element: read raw integer value and resolve name. Same byte-enum-
            // unsigned rule as the struct-field path via ReadEnumRawValue (audit #5 U9);
            // this path already read size 1 unsigned, so routing it here only shares the
            // rule so the two enum readers cannot drift.
            int64_t rawVal = ReadEnumRawValue(buf.data(), elemSize);
            elem.rawIntValue = rawVal;
            elem.enumName = ResolveEnumValue(enumPtr, rawVal);
            elem.value = elem.enumName.empty() ? std::to_string(rawVal) : elem.enumName;
        } else {
            elem.value = InterpretValue(innerTypeName, buf.data(), elemSize);
            // Store FName ComparisonIndex for NameProperty CE DropDownList
            if (innerTypeName == "NameProperty" && elemSize >= 4) {
                int32_t nameIdx = 0;
                memcpy(&nameIdx, buf.data(), 4);
                elem.rawIntValue = nameIdx;
            }
        }

        result.elements.push_back(std::move(elem));
    }

    result.ok = true;
    result.readCount = static_cast<int32_t>(result.elements.size());
    return result;
}

// ============================================================
// IsPointerArrayType — check if an inner type name is a pointer
// type whose elements are raw UObject* pointers (Phase D).
// ObjectProperty and ClassProperty store UObject* (8 bytes).
// WeakObjectProperty is deferred to Phase E.
// SoftObjectProperty, LazyObjectProperty, InterfaceProperty have
// different internal layouts — handled inline by WalkInstance.
// ============================================================
bool IsPointerArrayType(const std::string& innerTypeName) {
    return innerTypeName == "ObjectProperty"
        || innerTypeName == "ClassProperty";
}

// ============================================================
// ReadPointerArrayElements — read pointer elements from a TArray
// of UObject pointers (Phase D).
//
// For each element, reads the 8-byte pointer, then resolves the
// object name and class name via GetName/GetClass.
// ============================================================
ReadArrayResult ReadPointerArrayElements(
    uintptr_t instanceAddr, int32_t fieldOffset,
    int32_t elemSize, int32_t offset, int32_t limit)
{
    ReadArrayResult result;
    result.ok = false;

    // ObjectProperty elements are always 8 bytes (UObject pointer on x64).
    // Force to 8 regardless of what was passed — garbage elemSize values
    // (e.g., 524808 from bad FPROPERTY_ELEMSIZE reads) cause massive
    // address offsets and SEH faults, destroying performance.
    elemSize = 8;

    // Read TArray header
    Macht::TArrayView arr;
    if (!Macht::ReadTArray(instanceAddr + fieldOffset, arr)) {
        result.error = "TArray read failed";
        return result;
    }
    result.totalCount = arr.Count;

    if (arr.Count <= 0 || !arr.Data) {
        result.ok = true;
        result.readCount = 0;
        return result;
    }

    // Clamp offset/limit
    if (offset < 0) offset = 0;
    if (offset >= arr.Count) {
        result.ok = true;
        result.readCount = 0;
        return result;
    }
    int32_t end = offset + limit;
    if (end > arr.Count) end = arr.Count;
    if (end - offset > kArrayElementsPerRequestCap) end = offset + kArrayElementsPerRequestCap;  // hard cap per request

    result.elements.reserve(end - offset);

    for (int32_t i = offset; i < end; ++i) {
        LiveFieldValue::ArrayElement elem;
        elem.index = i;

        uintptr_t ptr = 0;
        uintptr_t elemAddr = arr.Data + static_cast<int64_t>(i) * elemSize;
        if (!Macht::ReadSafe(elemAddr, ptr)) {
            elem.value = "???";
            elem.hex = "????????????????";
            result.elements.push_back(std::move(elem));
            continue;
        }

        // Hex of the pointer value
        char hexBuf[20];
        snprintf(hexBuf, sizeof(hexBuf), "%016llX", static_cast<unsigned long long>(ptr));
        elem.hex = hexBuf;
        elem.ptrAddr = ptr;

        if (ptr) {
            elem.ptrName = GetName(ptr);
            uintptr_t cls = GetClass(ptr);
            if (cls) {
                elem.ptrClassName = GetName(cls);
            }

            // Display value: "Name (ClassName)" or just hex address if name fails
            if (!elem.ptrName.empty()) {
                elem.value = elem.ptrName;
                if (!elem.ptrClassName.empty())
                    elem.value += " (" + elem.ptrClassName + ")";
            } else {
                elem.value = hexBuf;  // Fallback to hex address
            }
        } else {
            elem.value = "null";
        }

        result.elements.push_back(std::move(elem));
    }

    result.ok = true;
    result.readCount = static_cast<int32_t>(result.elements.size());
    return result;
}

// ============================================================
// ResolveWeakObjectPtr — resolve FWeakObjectPtr to UObject* (Phase E).
//
// FWeakObjectPtr = { int32 ObjectIndex, int32 SerialNumber }.
// ObjectIndex is a GObjects index. The SerialNumber must match the
// FUObjectItem's serial to confirm the object is still alive.
// Returns the UObject* or 0 if stale/invalid.
// ============================================================
uintptr_t ResolveWeakObjectPtr(int32_t objectIndex, int32_t serialNumber) {
    if (objectIndex <= 0) return 0;
    uintptr_t obj = Aura::GetByIndex(objectIndex);
    if (!obj) return 0;
    int32_t actualSerial = Aura::GetSerialNumber(objectIndex);
    if (actualSerial != serialNumber) return 0;  // stale reference
    return obj;
}

// ============================================================
// IsWeakPointerArrayType — check if inner type is a weak-pointer type
// (Phase E). Currently only WeakObjectProperty.
// ============================================================
bool IsWeakPointerArrayType(const std::string& innerTypeName) {
    return innerTypeName == "WeakObjectProperty";
}

// ============================================================
// ReadWeakObjectArrayElements — read FWeakObjectPtr elements from a
// TArray (Phase E). Each element is { int32 ObjectIndex, int32 Serial }.
// Resolves each to a UObject* via GObjects + serial verification.
// ============================================================
ReadArrayResult ReadWeakObjectArrayElements(
    uintptr_t instanceAddr, int32_t fieldOffset,
    int32_t elemSize, int32_t offset, int32_t limit)
{
    ReadArrayResult result;
    result.ok = false;

    // FWeakObjectPtr is always 8 bytes { int32 ObjectIndex, int32 SerialNumber }.
    // Force to 8 to avoid garbage elemSize from FPROPERTY_ELEMSIZE.
    elemSize = 8;

    // Read TArray header
    Macht::TArrayView arr;
    if (!Macht::ReadTArray(instanceAddr + fieldOffset, arr)) {
        result.error = "TArray read failed";
        return result;
    }
    result.totalCount = arr.Count;

    if (arr.Count <= 0 || !arr.Data) {
        result.ok = true;
        result.readCount = 0;
        return result;
    }

    // Clamp offset/limit
    if (offset < 0) offset = 0;
    if (offset >= arr.Count) {
        result.ok = true;
        result.readCount = 0;
        return result;
    }
    int32_t end = offset + limit;
    if (end > arr.Count) end = arr.Count;
    if (end - offset > kArrayElementsPerRequestCap) end = offset + kArrayElementsPerRequestCap;

    result.elements.reserve(end - offset);

    for (int32_t i = offset; i < end; ++i) {
        LiveFieldValue::ArrayElement elem;
        elem.index = i;

        uintptr_t elemAddr = arr.Data + static_cast<int64_t>(i) * elemSize;

        // Read FWeakObjectPtr { int32 ObjectIndex, int32 SerialNumber }
        int32_t objIdx = 0, serial = 0;
        if (!Macht::ReadSafe(elemAddr, objIdx) || !Macht::ReadSafe(elemAddr + 4, serial)) {
            elem.value = "???";
            elem.hex = "????????????????";
            result.elements.push_back(std::move(elem));
            continue;
        }

        // Hex: ObjectIndex + SerialNumber
        char hexBuf[20];
        snprintf(hexBuf, sizeof(hexBuf), "%08X%08X", objIdx, serial);
        elem.hex = hexBuf;

        // Resolve via GObjects
        uintptr_t ptr = ResolveWeakObjectPtr(objIdx, serial);
        elem.ptrAddr = ptr;

        if (ptr) {
            elem.ptrName = GetName(ptr);
            uintptr_t cls = GetClass(ptr);
            if (cls) {
                elem.ptrClassName = GetName(cls);
            }

            if (!elem.ptrName.empty()) {
                elem.value = elem.ptrName;
                if (!elem.ptrClassName.empty())
                    elem.value += " (" + elem.ptrClassName + ")";
            } else {
                elem.value = hexBuf;
            }
        } else if (objIdx > 0) {
            elem.value = "null (stale)";
        } else {
            elem.value = "null";
        }

        result.elements.push_back(std::move(elem));
    }

    result.ok = true;
    result.readCount = static_cast<int32_t>(result.elements.size());
    return result;
}

// ============================================================
// Phase F: IsStructArrayType
// ============================================================
bool IsStructArrayType(const std::string& innerTypeName) {
    return innerTypeName == "StructProperty";
}

// ============================================================
// Phase F: Cached struct field layout for struct array expansion
// ============================================================
struct CachedStructField {
    std::string name;
    std::string typeName;
    int32_t     offset = 0;
    int32_t     size = 0;
    uint8_t     boolFieldMask = 0;   // BoolProperty: FieldMask byte
    uintptr_t   enumAddr = 0;        // EnumProperty / ByteProperty-with-enum: UEnum*
    std::string nestedTypeName;      // StructProperty: struct type name
};

static std::unordered_map<uintptr_t, std::vector<CachedStructField>> s_structFieldCache;

static const std::vector<CachedStructField>& GetCachedStructFields(uintptr_t structAddr) {
    {
        std::lock_guard<std::mutex> lk(s_structFieldCacheMutex);
        auto it = s_structFieldCache.find(structAddr);
        if (it != s_structFieldCache.end())
            return it->second;   // ref stays valid after unlock (node stability)
    }

    // Walk the struct to get field layout (no lock held — WalkClass/GetName
    // take their own leaf locks).
    ClassInfo ci = WalkClass(structAddr);
    std::vector<CachedStructField> cached;
    cached.reserve(ci.Fields.size());

    for (const auto& fi : ci.Fields) {
        CachedStructField cf;
        cf.name     = fi.Name;
        cf.typeName = fi.TypeName;
        cf.offset   = fi.Offset;

        // Audit #5 U15: fi.Size is FPROPERTY_ELEMSIZE read RAW, which this file
        // documents (ValidateArrayElemSize, ReadPointerArrayElements,
        // ReadWeakObjectArrayElements, ReadLazyObjectArrayElements) as returning
        // garbage for members of certain UScriptStruct layouts. A garbage size makes
        // ReadStructArrayElements' bounds guard either drop the sub-field silently
        // (too large) or mis-size it (too small), before any type branch can run.
        //
        // The fallback fires ONLY when the engine's own number is implausible — the
        // hardcoded table is a garbage-ELEMSIZE backstop, NOT an authority over the
        // engine. The two sibling interpreters in this file DO override
        // unconditionally, and that is deliberately not copied here: at least one of
        // InferScalarSize's constants is too large. TLazyObjectPtr is
        // TPersistentObjectPtr<FUniqueObjectGuid>, and our own vendored UE 5.8 declares
        // that as { FWeakObjectPtr WeakPtr; TObjectID ObjectID; } — 8 + 16 = 0x18, or
        // 0x1C on the older layout that still carried `int32 TagAtLastTest`. Neither is
        // the 0x20 InferScalarSize returns, and an inflated width pushes
        // cf.offset + cf.size past the element buffer, silently DROPPING a sub-field
        // the engine had sized correctly. (The 0x20 itself is a separate, pre-existing,
        // still-unvetted question — see the audit doc; do not "fix" it from here.)
        //
        // The plausibility bound matches the one WalkInstance already uses for the
        // same judgement on a field size.
        cf.size     = fi.Size;
        if (cf.size <= 0 || cf.size > 256) {
            if (int32_t expected = InferScalarSize(fi.TypeName); expected > 0)
                cf.size = expected;
        }

        // BoolProperty: read FieldMask from FBoolProperty/UBoolProperty
        if (fi.TypeName == "BoolProperty" && fi.Address) {
            uint8_t boolBytes[4] = {};
            int baseOff = DynOff::bUseFProperty ? DynOff::FBOOLPROP_FIELDSIZE : DynOff::UBOOLPROP_FIELDSIZE;
            for (int tryOff : { baseOff, baseOff - 4, baseOff + 4, baseOff + 8, baseOff - 8 }) {
                if (tryOff < 0) continue;
                if (!Macht::ReadBytesSafe(fi.Address + tryOff, boolBytes, 4)) continue;
                uint8_t fieldSize = boolBytes[0];
                uint8_t fieldMask = boolBytes[3];
                // fieldSize == 1, not `>= 1 && <= 8`. Five of the seven copies of this
                // probe already required 1; these two had drifted. FieldSize is the
                // bitfield CONTAINER size and UHT only ever emits a 1-byte one, so the
                // loose form bought nothing and accepted 8 -- which is exactly the low
                // byte an 8-aligned pointer can present when the probe lands off-field.
                if (fieldSize == 1 && fieldMask != 0 && (fieldMask & (fieldMask - 1)) == 0) {
                    cf.boolFieldMask = fieldMask;
                    break;
                }
            }
        }

        // EnumProperty: read UEnum*
        if (fi.TypeName == "EnumProperty" && fi.Address) {
            Macht::ReadSafe(fi.Address + DynOff::FENUMPROP_ENUM, cf.enumAddr);
        }

        // ByteProperty: check for UEnum* (ByteProperty-with-enum)
        if (fi.TypeName == "ByteProperty" && fi.Address) {
            Macht::ReadSafe(fi.Address + DynOff::FBYTEPROP_ENUM, cf.enumAddr);
        }

        // StructProperty: read nested struct type name
        if (fi.TypeName == "StructProperty" && fi.Address) {
            uintptr_t nestedStruct = 0;
            if (Macht::ReadSafe(fi.Address + DynOff::FSTRUCTPROP_STRUCT, nestedStruct) && nestedStruct) {
                cf.nestedTypeName = GetName(nestedStruct);
            }
        }

        cached.push_back(std::move(cf));
    }

    Sein::Debug("WALK:ArrayF", "Cached struct fields for 0x%llX: %d fields",
        static_cast<unsigned long long>(structAddr), static_cast<int>(cached.size()));

    std::lock_guard<std::mutex> lk(s_structFieldCacheMutex);
    auto [ins, _] = s_structFieldCache.emplace(structAddr, std::move(cached));
    return ins->second;
}

// ============================================================
// Phase F: ReadStructArrayElements
// ============================================================
ReadArrayResult ReadStructArrayElements(
    uintptr_t instanceAddr, int32_t fieldOffset,
    uintptr_t innerStructAddr, int32_t elemSize,
    int32_t offset, int32_t limit)
{
    ReadArrayResult result;

    Macht::TArrayView arr;
    if (!Macht::ReadTArray(instanceAddr + fieldOffset, arr)) {
        result.error = "Failed to read TArray header";
        return result;
    }
    result.totalCount = arr.Count;
    if (arr.Count <= 0 || !arr.Data) {
        result.ok = true;
        return result;
    }

    int32_t end = (std::min)(offset + limit, arr.Count);
    if (offset >= arr.Count) { result.ok = true; return result; }

    // Reject garbage elemSize early — prevents massive address strides
    if (elemSize <= 0 || elemSize > 65536) {
        result.error = "Invalid struct element size";
        return result;
    }

    // Get cached field layout
    const auto& cachedFields = GetCachedStructFields(innerStructAddr);
    if (cachedFields.empty()) {
        result.ok = true;  // Struct has no fields — return empty elements
        return result;
    }

    // Cap element buffer at 1024 bytes to avoid stack overflow
    const int32_t maxBufSize = 1024;
    int32_t readSize = (std::min)(elemSize, maxBufSize);

    for (int32_t i = offset; i < end; ++i) {
        LiveFieldValue::ArrayElement elem;
        elem.index = i;

        uintptr_t elemAddr = arr.Data + static_cast<uintptr_t>(i) * elemSize;

        // Bulk read element bytes
        std::vector<uint8_t> buf(readSize, 0);
        if (!Macht::ReadBytesSafe(elemAddr, buf.data(), readSize)) {
            elem.value = "???";
            result.elements.push_back(std::move(elem));
            continue;
        }

        // Hex of first 16 bytes (or less)
        int hexLen = (std::min)(readSize, 16);
        std::string hexStr;
        hexStr.reserve(hexLen * 2);
        for (int h = 0; h < hexLen; ++h) {
            char hx[3];
            snprintf(hx, sizeof(hx), "%02X", buf[h]);
            hexStr += hx;
        }
        elem.hex = hexStr;

        // Build compact value string and sub-fields
        std::string compact = "{";
        bool first = true;

        for (const auto& cf : cachedFields) {
            // Skip fields that extend beyond our read buffer
            if (cf.offset < 0 || cf.offset + cf.size > readSize) continue;

            LiveFieldValue::ArrayElement::StructSubField sf;
            sf.name     = cf.name;
            sf.typeName = cf.typeName;
            sf.offset   = cf.offset;
            sf.size     = cf.size;

            // Interpret based on type
            if (cf.typeName == "BoolProperty") {
                uint8_t byteVal = buf[cf.offset];
                bool boolVal = (cf.boolFieldMask != 0)
                    ? (byteVal & cf.boolFieldMask) != 0
                    : byteVal != 0;
                sf.value = boolVal ? "true" : "false";
            } else if ((cf.typeName == "EnumProperty" || cf.typeName == "ByteProperty") && cf.enumAddr) {
                // audit #5 U9: a byte enum must be read UNSIGNED — reading through int8_t
                // sign-extended the UHT MAX=255 sentinel (and any enumerator >= 128) to a
                // negative int that never matched the UEnum table. ReadEnumRawValue keeps
                // size 1 unsigned and 2/4/8 signed, matching the array-enum sibling below.
                int64_t rawVal = ReadEnumRawValue(buf.data() + cf.offset, cf.size);
                sf.value = ResolveEnumValue(cf.enumAddr, rawVal);
                if (sf.value.empty()) sf.value = std::to_string(rawVal);
            } else if (cf.typeName == "StructProperty") {
                sf.value = cf.nestedTypeName.empty() ? "{Struct}" : "{" + cf.nestedTypeName + "}";
            } else if (cf.typeName == "WeakObjectProperty") {
                // Audit #5 U12: FWeakObjectPtr is { int32 ObjectIndex; int32
                // SerialNumber } — two ints, NOT an address. This used to share the
                // raw-pointer arm below, which memcpy'd both ints into a uintptr_t and
                // published Serial<<32|Index as sf.ptrAddr. A null ref was right only
                // by accident (both ints zero), and a STALE ref — live index, serial no
                // longer matching — was reported as a live one. Resolve it the way the
                // rest of this file already does; the bytes are already in buf.
                //
                // Bounds are checked HERE against the 8 bytes actually read, not
                // inherited from cf.size: the loop's guard uses the engine's declared
                // width, which this file documents as sometimes garbage, so a
                // garbage-SMALL width would let the guard pass and this memcpy overrun.
                int32_t objIdx = 0, serial = 0;
                if (cf.offset + 8 <= readSize) {
                    memcpy(&objIdx, buf.data() + cf.offset,     4);
                    memcpy(&serial, buf.data() + cf.offset + 4, 4);
                }
                uintptr_t ptr = ResolveWeakObjectPtr(objIdx, serial);
                sf.ptrAddr = ptr;
                if (ptr) {
                    sf.ptrName = GetName(ptr);
                    uintptr_t cls = GetClass(ptr);
                    if (cls) {
                        sf.ptrClassName = GetName(cls);
                        sf.ptrClassAddr = cls;
                    }
                    sf.value = !sf.ptrName.empty() ? sf.ptrName : "ptr";
                } else {
                    // Same wording as ReadWeakObjectArrayElements: a live index whose
                    // serial no longer matches is a DEAD reference, not a null one.
                    sf.value = (objIdx > 0) ? "null (stale)" : "null";
                }
            } else if (cf.typeName == "ObjectProperty" || cf.typeName == "ClassProperty"
                    || cf.typeName == "InterfaceProperty") {
                // Raw UObject* at field+0. InterfaceProperty belongs here (and only
                // here): FScriptInterface is { UObject* +0x00; void* +0x08 } = 16 bytes,
                // so the old `cf.size == 8` gate matched neither arm and reported every
                // BOUND interface as "null" (audit #5 U14). The declared width is 16,
                // the pointer is still 8 bytes at offset 0.
                //
                // The gate is now BOUNDS, not width: all three hold a UObject* at
                // field+0, so the only question is whether those 8 bytes are inside the
                // element buffer. That is also what makes the fix independent of
                // cf.size, which the engine sometimes reports as garbage. The old
                // 4-byte arm is gone — it claimed a 32-bit pointer case that does not
                // exist on x64, and it fired only on a garbage width.
                uintptr_t ptr = 0;
                if (cf.offset + 8 <= readSize) {
                    memcpy(&ptr, buf.data() + cf.offset, 8);
                }
                sf.ptrAddr = ptr;
                if (ptr) {
                    sf.ptrName = GetName(ptr);
                    uintptr_t cls = GetClass(ptr);
                    if (cls) {
                        sf.ptrClassName = GetName(cls);
                        sf.ptrClassAddr = cls;
                    }
                    sf.value = !sf.ptrName.empty() ? sf.ptrName : "ptr";
                } else {
                    sf.value = "null";
                }
            } else if (cf.typeName == "SoftObjectProperty" || cf.typeName == "SoftClassProperty") {
                sf.value = ReadSoftObjectPath(elemAddr + cf.offset + SoftPathOffset(cf.size));
                if (sf.value.empty()) sf.value = "(none)";
            } else if (cf.typeName == "LazyObjectProperty") {
                uintptr_t gAddr = elemAddr + cf.offset + LazyGuidOffset(cf.size);
                uint32_t ga = 0, gb = 0, gc = 0, gd = 0;
                Macht::ReadSafe(gAddr, ga); Macht::ReadSafe(gAddr + 4, gb);
                Macht::ReadSafe(gAddr + 8, gc); Macht::ReadSafe(gAddr + 12, gd);
                char gs[48]; snprintf(gs, sizeof(gs), "{%08X-%08X-%08X-%08X}", ga, gb, gc, gd);
                sf.value = gs;
            } else if (cf.typeName == "TextProperty") {
                sf.value = ReadFTextString(elemAddr + cf.offset);
                if (sf.value.empty()) sf.value = "(empty)";
            } else if (cf.typeName == "StrProperty") {
                sf.value = "(str)";
            } else if (cf.typeName == "ArrayProperty") {
                sf.value = "(Array)";
            } else if (cf.typeName == "MapProperty") {
                sf.value = "(Map)";
            } else if (cf.typeName == "SetProperty") {
                sf.value = "(Set)";
            } else {
                // Scalar: use InterpretValue
                sf.value = InterpretValue(cf.typeName, buf.data() + cf.offset, cf.size);
                if (sf.value.empty()) {
                    // Fallback: hex of the field bytes
                    std::string fhex;
                    int flen = (std::min)(cf.size, 8);
                    for (int h = 0; h < flen; ++h) {
                        char hx[3];
                        snprintf(hx, sizeof(hx), "%02X", buf[cf.offset + h]);
                        fhex += hx;
                    }
                    sf.value = fhex;
                }
            }

            // Append to compact string
            if (!first) compact += ", ";
            first = false;
            compact += cf.name + "=" + sf.value;

            elem.structFields.push_back(std::move(sf));
        }
        compact += "}";
        elem.value = compact;

        result.elements.push_back(std::move(elem));
    }

    result.ok = true;
    result.readCount = static_cast<int32_t>(result.elements.size());
    return result;
}

// ============================================================
// Phase G: IsSoftObjectArrayType — TSoftObjectPtr / TSoftClassPtr arrays.
// Element layout: FWeakObjectPtr(8B) + Tag(4B) + pad(4B) + FSoftObjectPath
// Stride varies by UE version; reader falls back to 0x28 when elemSize is
// invalid. Per-element data resolves to the asset path string and (when
// loaded) the live UObject* via the embedded FWeakObjectPtr.
// ============================================================
bool IsSoftObjectArrayType(const std::string& innerTypeName) {
    return innerTypeName == "SoftObjectProperty"
        || innerTypeName == "SoftClassProperty";
}

// ============================================================
// Phase G: ReadSoftObjectArrayElements — read TSoftObjectPtr elements.
// For each element resolves the FSoftObjectPath asset name (UI display)
// and the embedded FWeakObjectPtr to a live UObject* when the asset is
// currently loaded (so CE / Address Finder can navigate to it).
// ============================================================
ReadArrayResult ReadSoftObjectArrayElements(
    uintptr_t instanceAddr, int32_t fieldOffset,
    int32_t elemSize, int32_t offset, int32_t limit)
{
    ReadArrayResult result;
    result.ok = false;

    // Validate stride. TSoftObjectPtr layout:
    //   FWeakObjectPtr(8) [+ Tag(4) + pad(4), UE ≤ 5.2 only] + FSoftObjectPath
    // FSoftObjectPath spans:
    //   UE4 / UE5.0: FName + FString(16)         → 8|16 + 16 = 24 or 32
    //   UE5.1+:      FName x2 + FString(16)      → 16|32 + 16 = 32 or 48
    // Combined element size ranges 0x20 .. 0x48 across versions — the low end
    // dropped from 0x28 when UE 5.3 deleted TagAtLastTest (see DynOff::SOFTPTR_PATH).
    // When FPROPERTY_ELEMSIZE returned garbage, derive a plausible fallback.
    if (elemSize < 0x18 || elemSize > 0x80) {
        int pathSize = SoftObjectPathPayloadSize();
        // Pass 0: there is no trustworthy ElementSize here by definition, so take
        // whatever has already been measured, or the version-derived default.
        int derived = SoftPathOffset(0) + pathSize;
        Sein::Warn("WALK:ArrayG", "Invalid SoftObject elemSize=%d, defaulting to 0x%X",
            elemSize, derived);
        elemSize = derived;
    }

    Macht::TArrayView arr;
    if (!Macht::ReadTArray(instanceAddr + fieldOffset, arr)) {
        result.error = "TArray read failed";
        return result;
    }
    result.totalCount = arr.Count;

    if (arr.Count <= 0 || !arr.Data) {
        result.ok = true;
        result.readCount = 0;
        return result;
    }

    if (offset < 0) offset = 0;
    if (offset >= arr.Count) {
        result.ok = true;
        result.readCount = 0;
        return result;
    }
    int32_t end = offset + limit;
    if (end > arr.Count) end = arr.Count;
    if (end - offset > kArrayElementsPerRequestCap) end = offset + kArrayElementsPerRequestCap;

    result.elements.reserve(end - offset);

    for (int32_t i = offset; i < end; ++i) {
        LiveFieldValue::ArrayElement elem;
        elem.index = i;

        uintptr_t elemAddr = arr.Data + static_cast<int64_t>(i) * elemSize;

        // Asset path string (display value)
        const int softPathOff = SoftPathOffset(static_cast<int32_t>(elemSize));
        std::string assetPath = ReadSoftObjectPath(elemAddr + softPathOff);

        // Surface the AssetPathName / PackageName ComparisonIndex so the CE XML
        // exporter can build a shared DropDownList for the FName leaf. The
        // exporter used to bake "+10"; it now takes softPathOff off the wire.
        // 0 maps to "None" — leave rawIntValue=0 for those so the DropDown
        // dedup naturally drops them.
        // ⚠ MUST be softPathOff, not a literal 0x10. These keys are attached to the CE
        // leaf the exporter emits AT softPathOff, so reading them from anywhere else
        // mismatches every entry in the DropDownList. Before the A1 fix both were 0x10
        // and at least agreed with each other; splitting them is strictly worse than the
        // original bug, and it is the CE table -- the artifact that ships.
        uint32_t pathFNameIdx = 0;
        if (Macht::ReadSafe(elemAddr + softPathOff, pathFNameIdx) && pathFNameIdx != 0
            && pathFNameIdx != 0xFFFFFFFFu) {
            elem.rawIntValue = static_cast<int64_t>(pathFNameIdx);
        }

        // Hex of the first 16 bytes (FWeakObjectPtr + Tag)
        uint8_t headerBuf[16] = {};
        if (Macht::ReadBytesSafe(elemAddr, headerBuf, 16)) {
            std::string hex;
            hex.reserve(32);
            for (int b = 0; b < 16; ++b) {
                char hx[3];
                snprintf(hx, sizeof(hx), "%02X", headerBuf[b]);
                hex += hx;
            }
            elem.hex = std::move(hex);
        }

        // Try resolving the embedded FWeakObjectPtr to a live UObject*.
        // TPersistentObjectPtr stores a FWeakObjectPtr at +0x00.
        int32_t objIdx = 0, serial = 0;
        if (Macht::ReadSafe(elemAddr, objIdx) && Macht::ReadSafe(elemAddr + 4, serial)) {
            uintptr_t resolved = ResolveWeakObjectPtr(objIdx, serial);
            if (resolved) {
                elem.ptrAddr = resolved;
                elem.ptrName = GetName(resolved);
                uintptr_t cls = GetClass(resolved);
                if (cls) elem.ptrClassName = GetName(cls);
            }
        }

        // Display: prefer asset path; fall back to "(unloaded)" / "(none)"
        if (!assetPath.empty()) {
            elem.value = assetPath;
        } else if (elem.ptrAddr) {
            elem.value = !elem.ptrName.empty()
                ? (elem.ptrClassName.empty() ? elem.ptrName
                                              : elem.ptrName + " (" + elem.ptrClassName + ")")
                : "(loaded)";
        } else {
            elem.value = "(none)";
        }

        result.elements.push_back(std::move(elem));
    }

    result.ok = true;
    result.readCount = static_cast<int32_t>(result.elements.size());
    return result;
}

// ============================================================
// Phase H: IsLazyObjectArrayType — TLazyObjectPtr arrays.
// Element layout: FWeakObjectPtr(8B) + envelope + FGuid(16B) = 0x1C (≤5.2) or 0x18 (≥5.3).
// ⚠ NOT 0x20. FUniqueObjectGuid is a bare FGuid (alignof 4) so there is no pad after the tag,
// and 5.3 deleted the tag outright. The old "= 0x20" here was the model audit A1 removed.
// ============================================================
bool IsLazyObjectArrayType(const std::string& innerTypeName) {
    return innerTypeName == "LazyObjectProperty";
}

// ============================================================
// Phase H: ReadLazyObjectArrayElements — read TLazyObjectPtr elements.
// Display value is the formatted FGuid; when the lazy ptr is currently
// resolved, the embedded FWeakObjectPtr yields a live UObject*.
// ============================================================
ReadArrayResult ReadLazyObjectArrayElements(
    uintptr_t instanceAddr, int32_t fieldOffset,
    int32_t elemSize, int32_t offset, int32_t limit)
{
    ReadArrayResult result;
    result.ok = false;

    // ⛔ DO NOT force a constant here. This read `elemSize = 0x20;` — the
    // FWeakObjectPtr(8)+Tag(4)+pad(4)+FGuid(16) model audit A1 deleted — and it cost two things:
    // element 0 read correctly while every index ≥1 drifted 4 bytes (≤5.2) or 8 (≥5.3), and
    // LazyGuidOffset(0x20) computes 0x10, which PersistentPtrEnvelopeFor REJECTS, so the
    // `TLazyObjectPtr payload envelope measured` line could never be emitted from THIS path —
    // making an operator who reached lazy via an array (the obvious route) score a correct fix
    // as FAILED. Route through the same envelope the scalar path uses: LazyGuidOffset MEASURES
    // from a real ElementSize and latches it, and falls back to the version default on garbage,
    // which is what the old forced constant was really guarding against.
    elemSize = LazyGuidOffset(elemSize) + 0x10;   // envelope + sizeof(FGuid)

    Macht::TArrayView arr;
    if (!Macht::ReadTArray(instanceAddr + fieldOffset, arr)) {
        result.error = "TArray read failed";
        return result;
    }
    result.totalCount = arr.Count;

    if (arr.Count <= 0 || !arr.Data) {
        result.ok = true;
        result.readCount = 0;
        return result;
    }

    if (offset < 0) offset = 0;
    if (offset >= arr.Count) {
        result.ok = true;
        result.readCount = 0;
        return result;
    }
    int32_t end = offset + limit;
    if (end > arr.Count) end = arr.Count;
    if (end - offset > kArrayElementsPerRequestCap) end = offset + kArrayElementsPerRequestCap;

    result.elements.reserve(end - offset);

    for (int32_t i = offset; i < end; ++i) {
        LiveFieldValue::ArrayElement elem;
        elem.index = i;

        uintptr_t elemAddr = arr.Data + static_cast<int64_t>(i) * elemSize;

        // FGuid inside the TLazyObjectPtr (4 x uint32). NOT +0x10 — FUniqueObjectGuid
        // is a bare FGuid at alignof 4, so it sits at +0x0C on UE ≤ 5.2 and +0x08
        // from 5.3. See DynOff::LAZYPTR_GUID.
        const int guidOff = LazyGuidOffset(elemSize);
        uint32_t a = 0, b = 0, c = 0, d = 0;
        Macht::ReadSafe(elemAddr + guidOff + 0,  a);
        Macht::ReadSafe(elemAddr + guidOff + 4,  b);
        Macht::ReadSafe(elemAddr + guidOff + 8,  c);
        Macht::ReadSafe(elemAddr + guidOff + 12, d);

        char guidStr[48];
        snprintf(guidStr, sizeof(guidStr), "{%08X-%08X-%08X-%08X}", a, b, c, d);

        // Hex: full 0x20 element bytes (cap header at 16)
        uint8_t headerBuf[16] = {};
        if (Macht::ReadBytesSafe(elemAddr, headerBuf, 16)) {
            std::string hex;
            hex.reserve(32);
            for (int bi = 0; bi < 16; ++bi) {
                char hx[3];
                snprintf(hx, sizeof(hx), "%02X", headerBuf[bi]);
                hex += hx;
            }
            elem.hex = std::move(hex);
        }

        // Resolve FWeakObjectPtr to UObject* if loaded
        int32_t objIdx = 0, serial = 0;
        if (Macht::ReadSafe(elemAddr, objIdx) && Macht::ReadSafe(elemAddr + 4, serial)) {
            uintptr_t resolved = ResolveWeakObjectPtr(objIdx, serial);
            if (resolved) {
                elem.ptrAddr = resolved;
                elem.ptrName = GetName(resolved);
                uintptr_t cls = GetClass(resolved);
                if (cls) elem.ptrClassName = GetName(cls);
            }
        }

        // Display: GUID + resolved name when loaded
        if (elem.ptrAddr && !elem.ptrName.empty()) {
            elem.value = std::string(guidStr) + " " + elem.ptrName;
            if (!elem.ptrClassName.empty())
                elem.value += " (" + elem.ptrClassName + ")";
        } else {
            elem.value = guidStr;
        }

        result.elements.push_back(std::move(elem));
    }

    result.ok = true;
    result.readCount = static_cast<int32_t>(result.elements.size());
    return result;
}

// ============================================================
// Phase I: IsInterfaceArrayType — TScriptInterface arrays.
// Element layout: FScriptInterface = { UObject* +0x00, void* +0x08 } = 16
// ============================================================
bool IsInterfaceArrayType(const std::string& innerTypeName) {
    return innerTypeName == "InterfaceProperty";
}

// ============================================================
// Phase I: ReadInterfaceArrayElements — read TScriptInterface elements.
// Each element exposes the underlying UObject* directly — same display
// shape as Phase D so CE XML / CSX export can treat it identically.
// ============================================================
ReadArrayResult ReadInterfaceArrayElements(
    uintptr_t instanceAddr, int32_t fieldOffset,
    int32_t elemSize, int32_t offset, int32_t limit)
{
    ReadArrayResult result;
    result.ok = false;

    // FScriptInterface is fixed 16 bytes
    elemSize = 16;

    Macht::TArrayView arr;
    if (!Macht::ReadTArray(instanceAddr + fieldOffset, arr)) {
        result.error = "TArray read failed";
        return result;
    }
    result.totalCount = arr.Count;

    if (arr.Count <= 0 || !arr.Data) {
        result.ok = true;
        result.readCount = 0;
        return result;
    }

    if (offset < 0) offset = 0;
    if (offset >= arr.Count) {
        result.ok = true;
        result.readCount = 0;
        return result;
    }
    int32_t end = offset + limit;
    if (end > arr.Count) end = arr.Count;
    if (end - offset > kArrayElementsPerRequestCap) end = offset + kArrayElementsPerRequestCap;

    result.elements.reserve(end - offset);

    for (int32_t i = offset; i < end; ++i) {
        LiveFieldValue::ArrayElement elem;
        elem.index = i;

        uintptr_t elemAddr = arr.Data + static_cast<int64_t>(i) * elemSize;

        uintptr_t objPtr = 0;
        uintptr_t ifacePtr = 0;
        bool okObj = Macht::ReadSafe(elemAddr,     objPtr);
        bool okIfc = Macht::ReadSafe(elemAddr + 8, ifacePtr);
        if (!okObj || !okIfc) {
            elem.value = "???";
            elem.hex = "????????????????????????????????";
            result.elements.push_back(std::move(elem));
            continue;
        }

        char hexBuf[40];
        snprintf(hexBuf, sizeof(hexBuf), "%016llX%016llX",
            static_cast<unsigned long long>(objPtr),
            static_cast<unsigned long long>(ifacePtr));
        elem.hex = hexBuf;
        elem.ptrAddr = objPtr;

        if (objPtr) {
            elem.ptrName = GetName(objPtr);
            uintptr_t cls = GetClass(objPtr);
            if (cls) {
                elem.ptrClassName = GetName(cls);
            }

            if (!elem.ptrName.empty()) {
                elem.value = elem.ptrName;
                if (!elem.ptrClassName.empty())
                    elem.value += " (" + elem.ptrClassName + ")";
            } else {
                char ptrHex[20];
                snprintf(ptrHex, sizeof(ptrHex), "%016llX",
                    static_cast<unsigned long long>(objPtr));
                elem.value = ptrHex;
            }
        } else {
            elem.value = "null";
        }

        result.elements.push_back(std::move(elem));
    }

    result.ok = true;
    result.readCount = static_cast<int32_t>(result.elements.size());
    return result;
}

// ============================================================
// Phase J: IsDelegateArrayType — TArray<FScriptDelegate>.
// Element layout: { FWeakObjectPtr(8B) + FName(8B|16B) } = 16 or 24
// ============================================================
bool IsDelegateArrayType(const std::string& innerTypeName) {
    return innerTypeName == "DelegateProperty";
}

// ============================================================
// Phase J: ReadDelegateArrayElements — read FScriptDelegate elements.
// Each element exposes the bound UObject* + FName (function), and the
// ptrAddr is set so CE XML / Live Walker can drill into the target.
// ============================================================
ReadArrayResult ReadDelegateArrayElements(
    uintptr_t instanceAddr, int32_t fieldOffset,
    int32_t /*elemSize*/, int32_t offset, int32_t limit)
{
    ReadArrayResult result;
    result.ok = false;

    // FScriptDelegate stride depends on FName width (CasePreservingName)
    int fnameSize = DynOff::SizeofFName();
    int32_t elemSize = 8 + fnameSize;

    Macht::TArrayView arr;
    if (!Macht::ReadTArray(instanceAddr + fieldOffset, arr)) {
        result.error = "TArray read failed";
        return result;
    }
    result.totalCount = arr.Count;

    if (arr.Count <= 0 || !arr.Data) {
        result.ok = true;
        result.readCount = 0;
        return result;
    }

    if (offset < 0) offset = 0;
    if (offset >= arr.Count) {
        result.ok = true;
        result.readCount = 0;
        return result;
    }
    int32_t end = offset + limit;
    if (end > arr.Count) end = arr.Count;
    if (end - offset > kArrayElementsPerRequestCap) end = offset + kArrayElementsPerRequestCap;

    result.elements.reserve(end - offset);

    for (int32_t i = offset; i < end; ++i) {
        LiveFieldValue::ArrayElement elem;
        elem.index = i;

        uintptr_t elemAddr = arr.Data + static_cast<int64_t>(i) * elemSize;

        // Hex of the full FScriptDelegate bytes
        std::vector<uint8_t> rawBuf(elemSize, 0);
        if (Macht::ReadBytesSafe(elemAddr, rawBuf.data(), elemSize)) {
            std::string hex;
            hex.reserve(elemSize * 2);
            for (auto b : rawBuf) {
                char hx[3];
                snprintf(hx, sizeof(hx), "%02X", b);
                hex += hx;
            }
            elem.hex = std::move(hex);
        }

        // Resolve target via FWeakObjectPtr
        int32_t objIdx = 0, serial = 0;
        Macht::ReadSafe(elemAddr, objIdx);
        Macht::ReadSafe(elemAddr + 4, serial);
        uintptr_t target = ResolveWeakObjectPtr(objIdx, serial);

        std::string funcName = ReadFName(elemAddr + 8);

        if (target) {
            elem.ptrAddr = target;
            elem.ptrName = GetName(target);
            uintptr_t cls = GetClass(target);
            if (cls) elem.ptrClassName = GetName(cls);
        }

        // Display: "TargetName::FunctionName"
        if (target && !funcName.empty()) {
            elem.value = (elem.ptrName.empty() ? std::string("?") : elem.ptrName)
                + "::" + funcName;
        } else if (!funcName.empty()) {
            elem.value = "(stale)::" + funcName;
        } else if (objIdx > 0) {
            elem.value = "(stale)";
        } else {
            elem.value = "(unbound)";
        }

        result.elements.push_back(std::move(elem));
    }

    result.ok = true;
    result.readCount = static_cast<int32_t>(result.elements.size());
    return result;
}

// ============================================================
// Phase K: IsMulticastDelegateArrayType — TArray<FMulticastScriptDelegate>.
// Each element is itself a TArray<FScriptDelegate> header (16 bytes).
// We can show binding count + a short preview of target::function names,
// but cannot drill further (each element would need its own container view).
// ============================================================
bool IsMulticastDelegateArrayType(const std::string& innerTypeName) {
    return innerTypeName == "MulticastDelegateProperty"
        || innerTypeName == "MulticastInlineDelegateProperty";
}

ReadArrayResult ReadMulticastDelegateArrayElements(
    uintptr_t instanceAddr, int32_t fieldOffset,
    int32_t /*elemSize*/, int32_t offset, int32_t limit)
{
    ReadArrayResult result;
    result.ok = false;

    // Each element: FMulticastScriptDelegate { TArray<FScriptDelegate> } = 16 bytes
    constexpr int32_t elemSize = 16;
    int fnameSize = DynOff::SizeofFName();
    int32_t innerStride = 8 + fnameSize;

    Macht::TArrayView arr;
    if (!Macht::ReadTArray(instanceAddr + fieldOffset, arr)) {
        result.error = "TArray read failed";
        return result;
    }
    result.totalCount = arr.Count;

    if (arr.Count <= 0 || !arr.Data) {
        result.ok = true;
        result.readCount = 0;
        return result;
    }

    if (offset < 0) offset = 0;
    if (offset >= arr.Count) {
        result.ok = true;
        result.readCount = 0;
        return result;
    }
    int32_t end = offset + limit;
    if (end > arr.Count) end = arr.Count;
    if (end - offset > kArrayElementsPerRequestCap) end = offset + kArrayElementsPerRequestCap;

    result.elements.reserve(end - offset);

    for (int32_t i = offset; i < end; ++i) {
        LiveFieldValue::ArrayElement elem;
        elem.index = i;

        uintptr_t elemAddr = arr.Data + static_cast<int64_t>(i) * elemSize;

        // Read inner TArray<FScriptDelegate> header { Data*, Count, Max }
        uintptr_t innerData = 0;
        int32_t   innerCount = 0;
        Macht::ReadSafe(elemAddr,     innerData);
        Macht::ReadSafe(elemAddr + 8, innerCount);
        if (innerCount < 0 || innerCount > 4096) innerCount = 0;  // sanity clamp

        // Hex: 16-byte TArray header
        uint8_t headerBuf[16] = {};
        if (Macht::ReadBytesSafe(elemAddr, headerBuf, 16)) {
            std::string hex;
            hex.reserve(32);
            for (int b = 0; b < 16; ++b) {
                char hx[3];
                snprintf(hx, sizeof(hx), "%02X", headerBuf[b]);
                hex += hx;
            }
            elem.hex = std::move(hex);
        }

        // Build display: "(N bindings) [Target1::Fn1, Target2::Fn2, ...]"
        std::string display;
        if (innerCount == 0 || !innerData) {
            display = "(0 bindings)";
        } else {
            display = "(" + std::to_string(innerCount)
                + " binding" + (innerCount > 1 ? "s" : "") + ")";

            int previewCount = (std::min)(innerCount, 4);
            std::vector<std::string> bindings;
            bindings.reserve(previewCount);
            for (int j = 0; j < previewCount; ++j) {
                uintptr_t bindAddr = innerData + static_cast<int64_t>(j) * innerStride;
                int32_t bobjIdx = 0, bserial = 0;
                if (!Macht::ReadSafe(bindAddr,     bobjIdx)) continue;
                if (!Macht::ReadSafe(bindAddr + 4, bserial)) continue;
                uintptr_t btarget = ResolveWeakObjectPtr(bobjIdx, bserial);
                std::string bfunc = ReadFName(bindAddr + 8);

                if (btarget && !bfunc.empty()) {
                    bindings.push_back(GetName(btarget) + "::" + bfunc);
                } else if (!bfunc.empty()) {
                    bindings.push_back("(stale)::" + bfunc);
                }
            }

            if (!bindings.empty()) {
                display += " [";
                for (size_t k = 0; k < bindings.size(); ++k) {
                    if (k > 0) display += ", ";
                    display += bindings[k];
                }
                if (innerCount > previewCount) display += ", ...";
                display += "]";
            }
        }

        elem.value = std::move(display);

        result.elements.push_back(std::move(elem));
    }

    result.ok = true;
    result.readCount = static_cast<int32_t>(result.elements.size());
    return result;
}

// ============================================================
// CorrectSubclassOffsets — one-time calibration of FSTRUCTPROP_STRUCT
// and related subclass extension offsets.
//
// The derivation formula (Offset_Internal + 0x2C) may be wrong for
// newer UE versions (e.g., UE5.7 uses +0x30). We calibrate by probing
// a known StructProperty FField for the UScriptStruct* pointer.
// If a non-zero delta is found, all subclass offsets are updated.
// ============================================================
static void CorrectSubclassOffsets(const std::vector<FieldInfo>& fields) {
    static std::atomic<bool> s_checked{false};
    if (s_checked.load(std::memory_order_acquire)) return;  // fast path: already calibrated

    // Slow path: serialize calibration so the parallel GObjects walkers can't
    // race on the DynOff:: writes below. Double-checked under the lock.
    std::lock_guard<std::mutex> lk(s_calibrationMutex);
    if (s_checked.load(std::memory_order_acquire)) return;
    if (!DynOff::bUseFProperty) { s_checked.store(true, std::memory_order_release); return; }

    static const int kProbeDeltas[] = { 0, 4, -4, 8, -8, 0xC, -0xC };
    for (const auto& fi : fields) {
        if (fi.TypeName != "StructProperty") continue;

        for (int delta : kProbeDeltas) {
            int tryOff = DynOff::FSTRUCTPROP_STRUCT + delta;
            if (tryOff < 0) continue;
            uintptr_t candidate = 0;
            if (!Macht::ReadSafe(fi.Address + tryOff, candidate) || !candidate) continue;
            // Validate: must be a UScriptStruct (UObject) with a readable ASCII name
            std::string sname = GetName(candidate);
            if (sname.empty() || sname[0] < 0x20 || sname[0] >= 0x7F) continue;

            if (delta != 0) {
                int corrected = DynOff::FSTRUCTPROP_STRUCT + delta;
                Sein::Info("WALK", "CorrectSubclassOffsets: delta=%d, FSTRUCTPROP 0x%X -> 0x%X (validated with '%s' -> '%s')",
                    delta, DynOff::FSTRUCTPROP_STRUCT, corrected, fi.Name.c_str(), sname.c_str());
                // (G12) Fourth writer of the sizeof(FProperty) family — one expression, so it
                // cannot drift from Genau's three. Note FARRAYPROP_INNER may legitimately
                // differ later (UE5.3+ puts EArrayPropertyFlags before Inner); the helper only
                // sets the shared STARTING point, and the ArrayProperty probe further down
                // this file re-probes it with delta=8. That probe is deliberately NOT routed
                // through the helper for exactly that reason.
                DynOff::ApplyPropertyFamily(DynOff::PropertyFamilyAtBase(corrected));
            }
            s_checked.store(true, std::memory_order_release);
            return;
        }
        // This StructProperty probe failed — try next one
    }
    // No StructProperty found in this class; will retry on next WalkInstance call
}

// ============================================================
// Guess What — Gap detection + heuristic type guessing
// ============================================================

// Format name for guessed fields: "?0xCC_ptr", "?0xD0_float", etc.
static std::string FormatGuessedName(int32_t offset, const char* hint) {
    char buf[32];
    snprintf(buf, sizeof(buf), "?0x%X_%s", offset, hint);
    return buf;
}

// Pointer validation: address in userspace range AND target is readable
static bool IsLikelyPointer(uint64_t val) {
    if (!Grimoire::IsUserspacePointer(val)) return false;
    uint8_t probe = 0;
    return Macht::ReadSafe(static_cast<uintptr_t>(val), probe);
}

// "Clean" float: fractional part is exactly .0 or .5 (common in game data)
static bool IsCleanFloat(float fVal) {
    float absVal = fVal < 0 ? -fVal : fVal;
    if (absVal > 1e8f) return false; // avoid int overflow in cast
    float frac = absVal - static_cast<float>(static_cast<int>(absVal));
    return frac == 0.0f || frac == 0.5f;
}

// Float validation: improved IEEE 754 check.
// Key improvement over CE: prefer int32 over float for small integer values.
// Returns 0 = not float, 1 = normal confidence (Float?), 2 = high confidence (Float)
static int IsLikelyFloat(float fVal, int32_t iVal) {
    if (fVal == 0.0f) return 0;

    uint32_t bits = 0;
    memcpy(&bits, &fVal, 4);
    uint32_t exponent = (bits >> 23) & 0xFF;

    // Exponent 0 = denormalized, 255 = Inf/NaN — reject
    if (exponent == 0 || exponent == 255) return 0;

    // Exponent range [100, 170] covers ~1e-8 to ~1e13
    if (exponent < 100 || exponent > 170) return 0;

    float absVal = fVal < 0 ? -fVal : fVal;

    // High confidence: clean float (.0 or .5) in reasonable game range
    if (IsCleanFloat(fVal) && absVal <= 1000.0f) return 2;

    // If int interpretation is "human readable", prefer int
    bool humanInt = (iVal >= -10000 && iVal <= 10000) || (iVal != 0 && iVal % 100 == 0);
    if (humanInt) return 0;

    // Normal confidence: magnitude in [0.001, 1e6]
    if (absVal >= 0.001f && absVal <= 1e6f) return 1;

    return 0;
}

// Double validation: similar to float but for 8-byte values.
// Returns 0 = not double, 1 = normal (Double?), 2 = high confidence (Double)
static int IsLikelyDouble(double dVal) {
    if (dVal == 0.0) return 0;

    uint64_t bits = 0;
    memcpy(&bits, &dVal, 8);
    uint64_t exponent = (bits >> 52) & 0x7FF;

    if (exponent == 0 || exponent == 2047) return 0;

    // Bias 1023. Range [950, 1100] covers ~1e-22 to ~1e23
    if (exponent < 950 || exponent > 1100) return 0;

    double absVal = dVal < 0 ? -dVal : dVal;

    // High confidence: clean (.0 or .5) in reasonable range
    if (absVal <= 1e8 && absVal <= 1000.0) {
        double frac = absVal - static_cast<double>(static_cast<int64_t>(absVal));
        if (frac == 0.0 || frac == 0.5) return 2;
    }

    // Check if same bytes as int64 are small — prefer int
    int64_t iVal = 0;
    memcpy(&iVal, &dVal, 8);
    if (iVal >= -10000 && iVal <= 10000) return 0;

    // Normal-confidence magnitude band. Kept WIDER than float's [0.001, 1e6]
    // because doubles exist precisely to hold values beyond float precision
    // (e.g. UE5 LWC large world coordinates), but the old 1e12 upper bound was
    // far too permissive — random 8-byte patterns that land in [1e9, 1e12] as a
    // double were getting flagged "Double?" as noise. Trimming to 1e9 keeps
    // realistic large-world doubles while cutting that garbage tail.
    if (absVal >= 0.001 && absVal <= 1e9) return 1;

    return 0;
}

// Guess types for a gap region and append results to outFields.
// Declared in Ubel.h (non-static) so the Native-C value scan can reuse it.
void GuessGapTypes(uintptr_t baseAddr, int32_t gapStart, int32_t gapEnd,
                   std::vector<LiveFieldValue>& outFields)
{
    // Perf: read the WHOLE gap once into a reused buffer, then guess in-buffer.
    // The old path did one SEH read per position PLUS a per-BYTE SEH read in the
    // zero-run probe — pathologically slow when this runs over every hole of every
    // object during a Native-C snapshot of a 433K-object game (FF7 Rebirth). One
    // bulk read replaces all of them. On a faulting bulk read (e.g. a gap straddling
    // an unmapped tail) or an over-large gap, `gb` stays null and the helpers fall
    // back to the original per-read SEH path — so behavior (the guesses) is identical,
    // only the source of the bytes changes. Output is byte-for-byte unchanged.
    const int32_t gapLen = gapEnd - gapStart;
    if (gapLen <= 0) return;
    // Hard safety bound, independent of any caller: a single object's gap can
    // never legitimately exceed the gap-fill work cap.
    // A larger gapLen means the caller fed a garbage size — refuse it rather
    // than walk it one byte at a time (each faulting read is a costly SEH). This
    // guards EVERY caller (WalkInstance gap-fill + Aura Native-C scan).
    if (gapLen > kMaxGapFillBytes) {
        Sein::Warn("WALK:guess",
            "GuessGapTypes: gap [0x%X,0x%X) len=%d exceeds sane bound, refusing",
            (unsigned)gapStart, (unsigned)gapEnd, gapLen);
        return;
    }
    static thread_local std::vector<uint8_t> s_gapBuf;
    const uint8_t* gb = nullptr;
    if (gapLen <= 0x10000) {
        s_gapBuf.resize(static_cast<size_t>(gapLen));
        if (Macht::ReadBytesSafe(baseAddr + gapStart, s_gapBuf.data(),
                                 static_cast<size_t>(gapLen)))
            gb = s_gapBuf.data();
    }
    auto readChunk = [&](int32_t at, uint8_t* dst, int32_t len) -> bool {
        if (gb) { std::memcpy(dst, gb + (at - gapStart), static_cast<size_t>(len)); return true; }
        return Macht::ReadBytesSafe(baseAddr + at, dst, len);
    };
    auto byteAt = [&](int32_t at, uint8_t& b) -> bool {
        if (gb) { b = gb[at - gapStart]; return true; }
        return Macht::ReadSafe<uint8_t>(baseAddr + at, b);
    };

    int32_t pos = gapStart;

    // Cancellation throttle: the slow path advances 1 byte per faulting SEH
    // read, so a wide gap over unmapped memory must stay abortable. Poll the
    // cooperative cancel flag (set by Fern's disconnect monitor / shutdown)
    // every ~1KB of progress and bail with partial guesses.
    uint32_t iterCount = 0;
    while (pos < gapEnd) {
        if ((++iterCount & 0x3FF) == 0 && Tot::Requested())
            return;
        int32_t remaining = gapEnd - pos;

        // Read up to 8 bytes for analysis (from the bulk buffer, or direct on fallback)
        uint8_t buf[8] = {};
        int32_t readLen = std::min(remaining, static_cast<int32_t>(8));
        if (!readChunk(pos, buf, readLen)) {
            pos++;
            continue;
        }

        // --- Priority 1: All-zeros check (padding) ---
        if (remaining >= 4 && (pos % 4) == 0) {
            bool allZero = true;
            for (int i = 0; i < readLen; i++) {
                if (buf[i] != 0) { allZero = false; break; }
            }
            if (allZero) {
                // Count consecutive zero bytes from pos, aligned to 4
                int32_t zeroRun = 0;
                for (int32_t probe = pos; probe < gapEnd && zeroRun < 256; probe++) {
                    uint8_t b = 0;
                    if (!byteAt(probe, b) || b != 0) break;
                    zeroRun++;
                }
                if (zeroRun >= 4) {
                    zeroRun = zeroRun & ~3; // align to 4-byte
                    LiveFieldValue fv;
                    fv.name = FormatGuessedName(pos, "padding");
                    fv.typeName = "Padding";
                    fv.offset = pos;
                    fv.size = zeroRun;
                    fv.hexValue = std::string(std::min(zeroRun * 2, 32), '0');
                    if (zeroRun > 16) fv.hexValue += "...";
                    fv.guessed = true;
                    outFields.push_back(std::move(fv));
                    pos += zeroRun;
                    continue;
                }
            }
        }

        // --- Priority 2: Pointer (8 bytes, 8-byte aligned) ---
        if (remaining >= 8 && (pos % 8) == 0) {
            uint64_t val = 0;
            memcpy(&val, buf, 8);
            if (val != 0 && IsLikelyPointer(val)) {
                LiveFieldValue fv;
                fv.name = FormatGuessedName(pos, "ptr");
                fv.typeName = "Pointer?";
                fv.offset = pos;
                fv.size = 8;
                char hexBuf[20];
                snprintf(hexBuf, sizeof(hexBuf), "%016llX",
                    static_cast<unsigned long long>(val));
                fv.hexValue = hexBuf;
                char addrBuf[24];
                snprintf(addrBuf, sizeof(addrBuf), "0x%llX",
                    static_cast<unsigned long long>(val));
                fv.typedValue = addrBuf;
                fv.guessed = true;
                outFields.push_back(std::move(fv));
                pos += 8;
                continue;
            }
        }

        // --- Priority 3: Float (4 bytes, 4-byte aligned) ---
        if (remaining >= 4 && (pos % 4) == 0) {
            float fVal = 0;
            int32_t iVal = 0;
            memcpy(&fVal, buf, 4);
            memcpy(&iVal, buf, 4);

            int floatConf = IsLikelyFloat(fVal, iVal);
            if (floatConf > 0) {
                LiveFieldValue fv;
                fv.name = FormatGuessedName(pos, "float");
                fv.typeName = (floatConf == 2) ? "Float" : "Float?";
                fv.offset = pos;
                fv.size = 4;
                char hexBuf[12];
                snprintf(hexBuf, sizeof(hexBuf), "%02X%02X%02X%02X",
                    buf[0], buf[1], buf[2], buf[3]);
                fv.hexValue = hexBuf;
                char valBuf[64];
                snprintf(valBuf, sizeof(valBuf), "%.6g", fVal);
                fv.typedValue = valBuf;
                fv.guessed = true;
                outFields.push_back(std::move(fv));
                pos += 4;
                continue;
            }
        }

        // --- Priority 4: Double (8 bytes, 8-byte aligned, only if not pointer) ---
        if (remaining >= 8 && (pos % 8) == 0) {
            double dVal = 0;
            memcpy(&dVal, buf, 8);
            int dblConf = IsLikelyDouble(dVal);

            // Float/double aliasing guard ("prefer the integer reading first").
            // An 8-aligned slot whose LOW 4 bytes are exactly zero is
            // byte-identical to [int32 0 / padding][float at +4]. A real double
            // almost never has its low 32 mantissa bits all zero UNLESS it is a
            // deliberately clean whole/.5 value (100.0, 12.5, …). So when the low
            // half is zero AND the double is NOT such a clean value (e.g. 0.875,
            // 0.25), drop the double: the int32 0 IS the "integer" reading, so we
            // let Int32(+0) emit it and the next position surface the real float
            // — instead of swallowing all 8 bytes as a noisy "Double?". (UE
            // gameplay data is float-dominated; standalone doubles are rare
            // outside UE5 LWC large coords, which have nonzero low bytes anyway.)
            // High-confidence clean doubles (dblConf==2) are never touched.
            if (dblConf == 1) {
                bool lowHalfZero =
                    (buf[0] == 0 && buf[1] == 0 && buf[2] == 0 && buf[3] == 0);
                // dVal is bounded by IsLikelyDouble's accepted band here, so the
                // int64 cast for the fractional test cannot overflow.
                double da = dVal < 0 ? -dVal : dVal;
                double dfrac = da - static_cast<double>(static_cast<int64_t>(da));
                bool cleanValue = (dfrac == 0.0 || dfrac == 0.5);
                if (lowHalfZero && !cleanValue)
                    dblConf = 0;   // fall through to Int32(+0), then Float(+4)
            }

            if (dblConf > 0) {
                LiveFieldValue fv;
                fv.name = FormatGuessedName(pos, "double");
                fv.typeName = (dblConf == 2) ? "Double" : "Double?";
                fv.offset = pos;
                fv.size = 8;
                char hexBuf[20];
                for (int i = 0; i < 8; i++)
                    snprintf(hexBuf + i * 2, 3, "%02X", buf[i]);
                fv.hexValue = hexBuf;
                char valBuf[80];
                snprintf(valBuf, sizeof(valBuf), "%.10g", dVal);
                fv.typedValue = valBuf;
                fv.guessed = true;
                outFields.push_back(std::move(fv));
                pos += 8;
                continue;
            }
        }

        // --- Priority 5: Int32 (4 bytes, 4-byte aligned) ---
        if (remaining >= 4 && (pos % 4) == 0) {
            int32_t val = 0;
            memcpy(&val, buf, 4);
            LiveFieldValue fv;
            fv.name = FormatGuessedName(pos, "i32");
            fv.typeName = "Int32?";
            fv.offset = pos;
            fv.size = 4;
            char hexBuf[12];
            snprintf(hexBuf, sizeof(hexBuf), "%02X%02X%02X%02X",
                buf[0], buf[1], buf[2], buf[3]);
            fv.hexValue = hexBuf;
            fv.typedValue = std::to_string(val);
            fv.guessed = true;
            outFields.push_back(std::move(fv));
            pos += 4;
            continue;
        }

        // --- Priority 6: Int16 (2 bytes, 2-byte aligned) ---
        if (remaining >= 2 && (pos % 2) == 0) {
            int16_t val = 0;
            memcpy(&val, buf, 2);
            LiveFieldValue fv;
            fv.name = FormatGuessedName(pos, "i16");
            fv.typeName = "Int16?";
            fv.offset = pos;
            fv.size = 2;
            char hexBuf[6];
            snprintf(hexBuf, sizeof(hexBuf), "%02X%02X", buf[0], buf[1]);
            fv.hexValue = hexBuf;
            fv.typedValue = std::to_string(val);
            fv.guessed = true;
            outFields.push_back(std::move(fv));
            pos += 2;
            continue;
        }

        // --- Priority 7: Byte (1 byte, fallback) ---
        {
            LiveFieldValue fv;
            fv.name = FormatGuessedName(pos, "byte");
            fv.typeName = "Byte?";
            fv.offset = pos;
            fv.size = 1;
            char hexBuf[4];
            snprintf(hexBuf, sizeof(hexBuf), "%02X", buf[0]);
            fv.hexValue = hexBuf;
            fv.typedValue = std::to_string(buf[0]);
            fv.guessed = true;
            outFields.push_back(std::move(fv));
            pos += 1;
        }
    }
}

// Format a struct-preview scalar (float/double) WITHOUT scientific notation
// for human-range magnitudes. `%g` flips to scientific past a few digits (e.g.
// 18328.64 -> "1.833e+04"), which is unreadable in the Value column preview;
// the leaf drilldown already uses %f. We mirror that: %f for the broad
// int32/int64-ish range with trailing zeros trimmed (2245.0000 -> "2245",
// 129.7000 -> "129.7"), falling back to %g only for truly huge/tiny values
// where a wall of digits would be worse.
// FmtPreviewNum moved to Ubel.h as FormatPreviewNumber (pure, unit-pinned) so the
// reflected-struct preview and this file cannot drift apart. Audit U17.

InstanceWalkResult WalkInstance(uintptr_t instanceAddr, uintptr_t classAddr, int32_t arrayLimit, int32_t previewLimit, bool fillGaps) {
    // Clamp arrayLimit to sane range [1, 16384]
    if (arrayLimit < 1) arrayLimit = 1;
    if (arrayLimit > 16384) arrayLimit = 16384;
    // Clamp previewLimit to sane range [0, 6]
    if (previewLimit < 0) previewLimit = 0;
    if (previewLimit > 6) previewLimit = 6;
    InstanceWalkResult result;
    result.addr = instanceAddr;

    if (!instanceAddr) return result;

    // Guard against already-freed objects: if the instance memory is no longer
    // committed/readable, bail out immediately. Without this, a walk on an
    // object that has since been destroyed (e.g. UMG Text widget refreshed
    // after removal) can follow recycled memory into garbage FField/FName
    // chains — ReadSafe catches AVs but infinite loops on valid-looking trash
    // still hang the pipe worker and the UI with it.
    if (!Macht::IsAddrReadable(instanceAddr, Grimoire::OFF_UOBJECT_CLASS + sizeof(uintptr_t))) {
        Sein::Warn("WALK:safe",
            "WalkInstance: instance 0x%llx not readable (freed?), skipping",
            (unsigned long long)instanceAddr);
        return result;
    }

    if (!classAddr)
        classAddr = GetClass(instanceAddr);

    result.classAddr = classAddr;
    result.className = classAddr ? GetName(classAddr) : "";

    // Detect whether instanceAddr is a real UObject or raw struct data
    // (e.g., struct element inside Map/Array/Set container).
    // A real UObject has a ClassPrivate at OFF_UOBJECT_CLASS that points to
    // a valid UClass with a resolvable FName. Raw struct data has arbitrary
    // bytes at that offset — reading GetName/GetOuter produces garbage.
    bool isRawStruct = false;
    if (classAddr) {
        uintptr_t testClass = 0;
        Macht::ReadSafe(instanceAddr + Grimoire::OFF_UOBJECT_CLASS, testClass);
        if (!testClass || !Grimoire::IsUserspacePointer(testClass)) {
            isRawStruct = true;
        } else {
            std::string testName = GetName(testClass);
            isRawStruct = testName.empty();
        }
    }

    if (isRawStruct) {
        // Raw struct data — use class name as display name, skip outer
        result.name = result.className;
    } else {
        result.name = GetName(instanceAddr);

        // Read OuterPrivate (only valid for real UObjects)
        uintptr_t outerAddr = GetOuter(instanceAddr);
        result.outerAddr = outerAddr;
        if (outerAddr) {
            result.outerName      = GetName(outerAddr);
            uintptr_t outerClass  = GetClass(outerAddr);
            result.outerClassName = outerClass ? GetName(outerClass) : "";
        }
    }

    // Detect class/struct definition objects — show their field definitions
    // instead of trying to read live instance data from the metaclass layout.
    // E.g., DataTable RowStruct (ObjectProperty) points to a UScriptStruct definition;
    // GetClass() returns the "ScriptStruct" metaclass, whose fields are empty/useless.
    // Instead, treat instanceAddr as the class definition and walk its own FField chain.
    if (result.className == "ScriptStruct" || result.className == "Class" ||
        result.className == "BlueprintGeneratedClass" ||
        result.className == "WidgetBlueprintGeneratedClass") {
        result.isDefinition = true;
        const ClassInfo& ci = WalkClassEx(instanceAddr);
        if (!ci.Name.empty()) result.name = ci.Name;

        for (const auto& fi : ci.Fields) {
            LiveFieldValue fv;
            fv.name     = fi.Name;
            fv.typeName = fi.TypeName;
            fv.offset   = fi.Offset;
            fv.size     = fi.Size;

            // Definition view: show field metadata only — do NOT read values
            // from the definition object's memory.  The field offsets describe
            // the data layout for *instances* of this struct, not the
            // UScriptStruct/UClass C++ object itself (which has a completely
            // different memory layout).

            // Show extended type info for compound types
            if (!fi.structType.empty())
                fv.typedValue = fi.structType;
            else if (!fi.objClassName.empty())
                fv.typedValue = "\xE2\x86\x92 " + fi.objClassName;  // "→ ClassName"
            else if (!fi.enumName.empty())
                fv.typedValue = fi.enumName;

            result.fields.push_back(fv);
        }
        return result;
    }

    // Walk the class to get field layout (cached after first call)
    auto walkClassStart = std::chrono::steady_clock::now();
    ClassInfo ci = WalkClass(classAddr);
    auto walkClassEnd = std::chrono::steady_clock::now();
    auto walkClassMs = std::chrono::duration_cast<std::chrono::milliseconds>(walkClassEnd - walkClassStart).count();

    result.propsSize = ci.PropertiesSize;

    // Stale/garbage gate: a real UStruct::PropertiesSize is bounded (see
    // kMaxPlausiblePropertiesSize). An implausible value means classAddr points at
    // recycled memory — the instance was freed and its slot reused while the
    // user was elsewhere (e.g. a long Snapshot/Class-Pivot pass), then Live
    // Walker re-walked the stale address on return. Bail BEFORE the gap-fill:
    // otherwise a bogus 827 MB PropertiesSize becomes one giant gap and
    // GuessGapTypes spins ~8e8 per-byte SEH reads, wedging the single-threaded
    // pipe worker (and the UI with it). propsSize is zeroed so the UI's
    // "0 fields + propsSize>0" fill_gaps auto-retry never fires.
    if (!IsPlausiblePropertiesSize(ci.PropertiesSize)) {
        Sein::Warn("WALK:safe",
            "WalkInstance: instance 0x%llx class 0x%llx has implausible PropertiesSize=%d "
            "(stale/recycled object?), skipping field/gap walk",
            (unsigned long long)instanceAddr, (unsigned long long)classAddr, ci.PropertiesSize);
        result.isStale   = true;
        result.propsSize = 0;
        return result;
    }

    // Pre-pass: calibrate subclass extension offsets using StructProperty probe.
    // Must run BEFORE the main loop so ArrayProperty fields use corrected offsets.
    CorrectSubclassOffsets(ci.Fields);

    // Timing: track per-category time for performance diagnostics
    int64_t tObj = 0, tStruct = 0, tArray = 0, tScalar = 0;
    int nObj = 0, nStruct = 0, nArray = 0, nScalar = 0;
    auto loopStart = std::chrono::steady_clock::now();

    for (const auto& fi : ci.Fields) {
        auto fieldStart = std::chrono::steady_clock::now();

        // RAII timer guard: fires on scope exit (including `continue`)
        // so ALL field handlers are timed, not just the scalar fallthrough.
        struct FieldTimerGuard {
            const std::chrono::steady_clock::time_point& start;
            const FieldInfo& fi;
            int64_t& tObj; int64_t& tStruct; int64_t& tArray; int64_t& tScalar;
            int& nObj; int& nStruct; int& nArray; int& nScalar;
            ~FieldTimerGuard() {
                auto end = std::chrono::steady_clock::now();
                auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(end - start).count();
                const auto& tn = fi.TypeName;
                if (tn == "ObjectProperty" || tn == "ClassProperty" || tn == "WeakObjectProperty" ||
                    tn == "SoftObjectProperty" || tn == "SoftClassProperty" ||
                    tn == "LazyObjectProperty" || tn == "InterfaceProperty")
                    { tObj += ms; nObj++; }
                else if (tn == "StructProperty") { tStruct += ms; nStruct++; }
                else if (tn == "ArrayProperty" || tn == "MapProperty" || tn == "SetProperty")
                    { tArray += ms; nArray++; }
                else { tScalar += ms; nScalar++; }
                if (ms > 500) {
                    Sein::Warn("WALK:perf", "Slow field '%s' (%s) took %lldms",
                        fi.Name.c_str(), tn.c_str(), static_cast<long long>(ms));
                }
            }
        } _fieldTimer{fieldStart, fi, tObj, tStruct, tArray, tScalar, nObj, nStruct, nArray, nScalar};

        LiveFieldValue fv;
        fv.name     = fi.Name;
        fv.typeName = fi.TypeName;
        fv.offset   = fi.Offset;
        fv.size     = fi.Size;

        // Handle WeakObjectProperty: FWeakObjectPtr { int32 ObjectIndex, int32 SerialNumber }
        if (fi.TypeName == "WeakObjectProperty") {
            int32_t objIdx = 0, serial = 0;
            Macht::ReadSafe(instanceAddr + fi.Offset, objIdx);
            Macht::ReadSafe(instanceAddr + fi.Offset + 4, serial);
            uintptr_t ptr = ResolveWeakObjectPtr(objIdx, serial);
            if (ptr) {
                fv.ptrValue = ptr;
                fv.ptrName = GetName(ptr);
                uintptr_t cls = GetClass(ptr);
                if (cls) {
                    fv.ptrClassName = GetName(cls);
                    fv.ptrClassAddr = cls;
                }
            }
            char buf[20];
            snprintf(buf, sizeof(buf), "%08X%08X", objIdx, serial);
            fv.hexValue = buf;
            result.fields.push_back(std::move(fv));
            continue;
        }

        // Handle ObjectProperty / ClassProperty: read pointer, resolve name/class
        // Always 8 bytes (pointer) — don't gate on fi.Size which can be garbage.
        if (fi.TypeName == "ObjectProperty" || fi.TypeName == "ClassProperty") {
            uintptr_t ptr = 0;
            if (Macht::ReadSafe(instanceAddr + fi.Offset, ptr) && ptr) {
                fv.ptrValue = ptr;
                fv.ptrName = GetName(ptr);
                fv.ptrClassName = "";
                uintptr_t ptrCls = GetClass(ptr);
                if (ptrCls) {
                    fv.ptrClassName = GetName(ptrCls);
                    fv.ptrClassAddr = ptrCls;
                }
            }
            // Hex of the pointer
            char buf[20];
            snprintf(buf, sizeof(buf), "%016llX", static_cast<unsigned long long>(ptr));
            fv.hexValue = buf;
            fv.size = 8;
            result.fields.push_back(std::move(fv));
            continue;
        }

        // Handle SoftObjectProperty / SoftClassProperty: read FSoftObjectPath asset path.
        // TSoftObjectPtr layout: +0x00 FWeakObjectPtr(8B), then the path — at +0x10 on
        // UE ≤ 5.2 (Tag(4B) + pad(4B) in between) and at +0x08 from 5.3, which deleted
        // the tag. Measured from ElementSize; see DynOff::SOFTPTR_PATH.
        // When the asset is currently loaded the embedded FWeakObjectPtr resolves
        // to the live UObject*; expose it via ptrValue so the UI can drill in
        // and the Address Finder can route through soft references.
        if (fi.TypeName == "SoftObjectProperty" || fi.TypeName == "SoftClassProperty") {
            uintptr_t fieldAddr = instanceAddr + fi.Offset;
            std::string assetPath = ReadSoftObjectPath(fieldAddr + SoftPathOffset(fi.Size));
            fv.strValue = assetPath;

            // Resolve embedded FWeakObjectPtr at +0x00 → live UObject* (when loaded)
            int32_t objIdx = 0, serial = 0;
            Macht::ReadSafe(fieldAddr,     objIdx);
            Macht::ReadSafe(fieldAddr + 4, serial);
            uintptr_t target = ResolveWeakObjectPtr(objIdx, serial);
            if (target) {
                fv.ptrValue = target;
                fv.ptrName = GetName(target);
                uintptr_t cls = GetClass(target);
                if (cls) {
                    fv.ptrClassName = GetName(cls);
                    fv.ptrClassAddr = cls;
                }
            }

            // Display: prefer asset path; fall back to resolved name when loaded
            if (!assetPath.empty()) {
                fv.typedValue = assetPath;
            } else if (target && !fv.ptrName.empty()) {
                fv.typedValue = fv.ptrClassName.empty()
                    ? fv.ptrName
                    : fv.ptrName + " (" + fv.ptrClassName + ")";
            } else {
                fv.typedValue = "(none)";
            }

            // Hex from raw bytes (cap at 32 bytes)
            int showBytes = (fi.Size > 0 && fi.Size <= 64) ? (std::min)(fi.Size, (int32_t)32) : 0;
            if (showBytes > 0) {
                std::vector<uint8_t> rawBuf(showBytes, 0);
                if (Macht::ReadBytesSafe(fieldAddr, rawBuf.data(), showBytes)) {
                    std::string hex;
                    hex.reserve(showBytes * 2);
                    for (int i = 0; i < showBytes; ++i) {
                        char hx[3];
                        snprintf(hx, sizeof(hx), "%02X", rawBuf[i]);
                        hex += hx;
                    }
                    if (fi.Size > 32) hex += "...";
                    fv.hexValue = hex;
                }
            }
            result.fields.push_back(std::move(fv));
            continue;
        }

        // Handle LazyObjectProperty: read FUniqueObjectGuid (FGuid = 4 x uint32).
        // TLazyObjectPtr layout: +0x00 FWeakObjectPtr(8B) [+ Tag(4B) on UE <= 5.2] + FGuid.
        // ⚠ NOT +0x10, in any era: FUniqueObjectGuid is a bare FGuid at alignof 4, so there
        // is no pad after the tag -- the GUID is at +0x0C up to 5.2 and +0x08 from 5.3.
        // See DynOff::LAZYPTR_GUID. This site was missed by the A1 sweep while all three
        // siblings were converted, so it stayed 4 bytes off on EVERY version.
        // Mirror Soft path: resolve embedded FWeakObjectPtr when the lazy ptr
        // is currently bound to a live UObject.
        if (fi.TypeName == "LazyObjectProperty") {
            uintptr_t fieldAddr = instanceAddr + fi.Offset;
            uintptr_t guidAddr = fieldAddr + LazyGuidOffset(fi.Size);
            uint32_t a = 0, b = 0, c = 0, d = 0;
            Macht::ReadSafe(guidAddr + 0, a);
            Macht::ReadSafe(guidAddr + 4, b);
            Macht::ReadSafe(guidAddr + 8, c);
            Macht::ReadSafe(guidAddr + 12, d);

            char guidStr[48];
            snprintf(guidStr, sizeof(guidStr), "{%08X-%08X-%08X-%08X}", a, b, c, d);
            fv.strValue = guidStr;

            // Resolve embedded FWeakObjectPtr at +0x00 → live UObject* (when loaded)
            int32_t objIdx = 0, serial = 0;
            Macht::ReadSafe(fieldAddr,     objIdx);
            Macht::ReadSafe(fieldAddr + 4, serial);
            uintptr_t target = ResolveWeakObjectPtr(objIdx, serial);
            if (target) {
                fv.ptrValue = target;
                fv.ptrName = GetName(target);
                uintptr_t cls = GetClass(target);
                if (cls) {
                    fv.ptrClassName = GetName(cls);
                    fv.ptrClassAddr = cls;
                }
            }

            // Display: GUID + resolved name when loaded
            if (target && !fv.ptrName.empty()) {
                fv.typedValue = std::string(guidStr) + " " + fv.ptrName;
                if (!fv.ptrClassName.empty())
                    fv.typedValue += " (" + fv.ptrClassName + ")";
            } else {
                fv.typedValue = guidStr;
            }

            // Hex from raw bytes
            int showBytes = (fi.Size > 0 && fi.Size <= 64) ? (std::min)(fi.Size, (int32_t)32) : 0;
            if (showBytes > 0) {
                std::vector<uint8_t> rawBuf(showBytes, 0);
                if (Macht::ReadBytesSafe(fieldAddr, rawBuf.data(), showBytes)) {
                    std::string hex;
                    hex.reserve(showBytes * 2);
                    for (int i = 0; i < showBytes; ++i) {
                        char hx[3];
                        snprintf(hx, sizeof(hx), "%02X", rawBuf[i]);
                        hex += hx;
                    }
                    if (fi.Size > 32) hex += "...";
                    fv.hexValue = hex;
                }
            }
            result.fields.push_back(std::move(fv));
            continue;
        }

        // Handle InterfaceProperty: FScriptInterface = { UObject* ObjectPointer(8B); void* InterfacePointer(8B) }
        if (fi.TypeName == "InterfaceProperty") {
            uintptr_t objPtr = 0;
            uintptr_t ifacePtr = 0;
            Macht::ReadSafe(instanceAddr + fi.Offset, objPtr);
            Macht::ReadSafe(instanceAddr + fi.Offset + 8, ifacePtr);

            if (objPtr) {
                fv.ptrValue = objPtr;
                fv.ptrName = GetName(objPtr);
                uintptr_t cls = GetClass(objPtr);
                if (cls) {
                    fv.ptrClassName = GetName(cls);
                    fv.ptrClassAddr = cls;
                }
            }

            char buf[48];
            snprintf(buf, sizeof(buf), "%016llX %016llX",
                static_cast<unsigned long long>(objPtr),
                static_cast<unsigned long long>(ifacePtr));
            fv.hexValue = buf;
            result.fields.push_back(std::move(fv));
            continue;
        }

        // Handle ArrayProperty: read TArray header + Inner element type
        if (fi.TypeName == "ArrayProperty") {
            Macht::TArrayView arr;
            if (Macht::ReadTArray(instanceAddr + fi.Offset, arr)) {
                fv.arrayCount = arr.Count;
                fv.arrayDataAddr = arr.Data;
                // Hex of the TArray header (Data ptr + Count + Max)
                char buf[48];
                snprintf(buf, sizeof(buf), "%016llX %08X %08X",
                    static_cast<unsigned long long>(arr.Data), arr.Count, arr.Max);
                fv.hexValue = buf;
            } else {
                fv.arrayCount = 0;
            }

            // Read FArrayProperty::Inner (FProperty*) to get element type info.
            // Note: on UE5.3+, FArrayProperty stores EArrayPropertyFlags (a uint8, so 1B + 7B
            // pad) BEFORE Inner, so Inner can be at FARRAYPROP_INNER + 8. The probe list
            // includes delta=8 to handle this.  Delta=0xC covers the case where the base
            // offset hasn't been corrected yet (0x74 + 0xC = 0x80 for TQ2).
            if (DynOff::bUseFProperty) {
                static const int kInnerProbeOffsets[] = { 0, 8, 4, 0xC, -4, -8, 0x10, -0x10 };
                bool innerFound = false;
                for (int delta : kInnerProbeOffsets) {
                    int tryOff = DynOff::FARRAYPROP_INNER + delta;
                    if (tryOff < 0) continue;
                    uintptr_t inner = 0;
                    if (!Macht::ReadSafe(fi.Address + tryOff, inner) || !inner) continue;
                    // Skip obvious garbage addresses to avoid SEH faults
                    if (!Grimoire::IsUserspacePointer(inner)) continue;

                    // Validate: Inner must be an FField with a readable FFieldClass name
                    std::string innerTypeName = GetFieldTypeName(inner);

                    if (!innerTypeName.empty() && innerTypeName != "Unknown"
                        && innerTypeName.find("Property") != std::string::npos) {
                        fv.arrayInnerType = innerTypeName;

                        // Read element size from Inner FProperty and validate.
                        // Inner FProperty's ELEMSIZE offset often returns garbage because
                        // the inner property metadata layout differs from top-level FFields.
                        int32_t rawElemSize = 0;
                        Macht::ReadSafe<int32_t>(inner + DynOff::FPROPERTY_ELEMSIZE, rawElemSize);
                        fv.arrayElemSize = ValidateArrayElemSize(rawElemSize, innerTypeName);

                        // If inner is StructProperty, also read the UScriptStruct name
                        if (innerTypeName == "StructProperty") {
                            uintptr_t innerStruct = 0;
                            if (Macht::ReadSafe(inner + DynOff::FSTRUCTPROP_STRUCT, innerStruct) && innerStruct) {
                                fv.arrayInnerStructType = GetName(innerStruct);
                                fv.arrayInnerStructAddr = innerStruct;  // Phase F: store for struct array expansion
                                // Fallback: FProperty::ElementSize often reads 0 for StructProperty inners.
                                // Use UScriptStruct::PropertiesSize as the actual element size.
                                if (fv.arrayElemSize <= 0) {
                                    int32_t propsSize = 0;
                                    if (Macht::ReadSafe(innerStruct + DynOff::USTRUCT_PROPSSIZE, propsSize) && propsSize > 0 && propsSize <= 65536) {
                                        fv.arrayElemSize = propsSize;
                                        Sein::Info("WALK:ArrayP", "Fallback: used PropertiesSize=%d for '%s' struct '%s'",
                                            propsSize, fi.Name.c_str(), fv.arrayInnerStructType.c_str());
                                    }
                                }
                            }
                        }

                        Sein::Info("WALK:ArrayP", "FArrayProperty::Inner found at FField+0x%X (delta=%d) for '%s' -> '%s' elemSize=%d",
                            tryOff, delta, fi.Name.c_str(), innerTypeName.c_str(), fv.arrayElemSize);
                        // Persist corrected FARRAYPROP_INNER if delta != 0
                        if (delta != 0) {
                            Sein::Info("WALK:ArrayP", "Correcting FARRAYPROP_INNER: 0x%X -> 0x%X",
                                DynOff::FARRAYPROP_INNER, tryOff);
                            DynOff::FARRAYPROP_INNER = tryOff;
                        }
                        fv.arrayInnerFFieldAddr = inner;
                        innerFound = true;
                        break;
                    }
                }

                // Phase B: read inline scalar element values (up to arrayLimit)
                if (innerFound && IsScalarArrayType(fv.arrayInnerType)
                    && arr.Data && fv.arrayCount > 0
                    && fv.arrayElemSize > 0) {
                    auto elemResult = ReadArrayElements(
                        instanceAddr, fi.Offset,
                        fv.arrayInnerFFieldAddr, fv.arrayInnerType,
                        fv.arrayElemSize, 0, arrayLimit);
                    if (elemResult.ok && !elemResult.elements.empty()) {
                        fv.arrayElements = std::move(elemResult.elements);
                        Sein::Debug("WALK:ArrayP", "Inline elements: %d read for '%s'",
                            static_cast<int>(fv.arrayElements.size()), fi.Name.c_str());
                    }
                    // Populate full enum entries for CE DropDownList
                    if (elemResult.enumAddr) {
                        fv.arrayEnumAddr = elemResult.enumAddr;
                        fv.arrayEnumEntries = GetEnumEntries(elemResult.enumAddr);
                    }
                }

                // Phase D: read pointer array element names (up to arrayLimit)
                if (innerFound && IsPointerArrayType(fv.arrayInnerType)
                    && arr.Data && fv.arrayCount > 0
                    && fv.arrayElemSize > 0) {
                    auto ptrResult = ReadPointerArrayElements(
                        instanceAddr, fi.Offset, fv.arrayElemSize, 0, arrayLimit);
                    if (ptrResult.ok && !ptrResult.elements.empty()) {
                        fv.arrayElements = std::move(ptrResult.elements);
                        Sein::Debug("WALK:ArrayP", "Ptr elements: %d read for '%s'",
                            static_cast<int>(fv.arrayElements.size()), fi.Name.c_str());
                    }
                }

                // Phase E: read weak object pointer array element names (up to arrayLimit)
                if (innerFound && IsWeakPointerArrayType(fv.arrayInnerType)
                    && arr.Data && fv.arrayCount > 0
                    && fv.arrayElemSize > 0) {
                    auto weakResult = ReadWeakObjectArrayElements(
                        instanceAddr, fi.Offset, fv.arrayElemSize, 0, arrayLimit);
                    if (weakResult.ok && !weakResult.elements.empty()) {
                        fv.arrayElements = std::move(weakResult.elements);
                        Sein::Debug("WALK:ArrayP", "Weak ptr elements: %d read for '%s'",
                            static_cast<int>(fv.arrayElements.size()), fi.Name.c_str());
                    }
                }

                // Phase F: read struct array element fields (up to arrayLimit)
                if (innerFound && IsStructArrayType(fv.arrayInnerType)
                    && fv.arrayInnerStructAddr != 0
                    && arr.Data && fv.arrayCount > 0
                    && fv.arrayElemSize > 0) {
                    auto structResult = ReadStructArrayElements(
                        instanceAddr, fi.Offset,
                        fv.arrayInnerStructAddr, fv.arrayElemSize, 0, arrayLimit);
                    if (structResult.ok && !structResult.elements.empty()) {
                        fv.arrayElements = std::move(structResult.elements);
                        Sein::Debug("WALK:ArrayP", "Struct elements: %d read for '%s'",
                            static_cast<int>(fv.arrayElements.size()), fi.Name.c_str());
                    }
                }

                // Phase G: TSoftObjectPtr / TSoftClassPtr arrays
                if (innerFound && IsSoftObjectArrayType(fv.arrayInnerType)) {
                    // Stamp soft-array layout metadata even when arr is empty
                    // — the CE XML / CSX exporter needs it to lay out the
                    // per-element FName leaf(s) at pathOffset / pathOffset+fnameSize.
                    fv.softArrayFNameSize = DynOff::SizeofFName();
                    fv.softArrayIsTopLevelAssetPath = (g_cachedUEVersion >= 501);
                    fv.softArrayPathOffset = SoftPathOffset(fv.arrayElemSize);

                    if (arr.Data && fv.arrayCount > 0) {
                        auto softResult = ReadSoftObjectArrayElements(
                            instanceAddr, fi.Offset, fv.arrayElemSize, 0, arrayLimit);
                        if (softResult.ok && !softResult.elements.empty()) {
                            fv.arrayElements = std::move(softResult.elements);
                            Sein::Debug("WALK:ArrayP", "Soft elements: %d read for '%s'",
                                static_cast<int>(fv.arrayElements.size()), fi.Name.c_str());
                        }
                    }
                }

                // Phase H: TLazyObjectPtr arrays
                if (innerFound && IsLazyObjectArrayType(fv.arrayInnerType)
                    && arr.Data && fv.arrayCount > 0) {
                    auto lazyResult = ReadLazyObjectArrayElements(
                        instanceAddr, fi.Offset, fv.arrayElemSize, 0, arrayLimit);
                    if (lazyResult.ok && !lazyResult.elements.empty()) {
                        fv.arrayElements = std::move(lazyResult.elements);
                        Sein::Debug("WALK:ArrayP", "Lazy elements: %d read for '%s'",
                            static_cast<int>(fv.arrayElements.size()), fi.Name.c_str());
                    }
                }

                // Phase I: TScriptInterface arrays
                if (innerFound && IsInterfaceArrayType(fv.arrayInnerType)
                    && arr.Data && fv.arrayCount > 0) {
                    auto ifaceResult = ReadInterfaceArrayElements(
                        instanceAddr, fi.Offset, fv.arrayElemSize, 0, arrayLimit);
                    if (ifaceResult.ok && !ifaceResult.elements.empty()) {
                        fv.arrayElements = std::move(ifaceResult.elements);
                        Sein::Debug("WALK:ArrayP", "Interface elements: %d read for '%s'",
                            static_cast<int>(fv.arrayElements.size()), fi.Name.c_str());
                    }
                }

                // Phase J: TArray<FScriptDelegate>
                if (innerFound && IsDelegateArrayType(fv.arrayInnerType)
                    && arr.Data && fv.arrayCount > 0) {
                    auto delResult = ReadDelegateArrayElements(
                        instanceAddr, fi.Offset, fv.arrayElemSize, 0, arrayLimit);
                    if (delResult.ok && !delResult.elements.empty()) {
                        fv.arrayElements = std::move(delResult.elements);
                        Sein::Debug("WALK:ArrayP", "Delegate elements: %d read for '%s'",
                            static_cast<int>(fv.arrayElements.size()), fi.Name.c_str());
                    }
                }

                // Phase K: TArray<FMulticastScriptDelegate>
                if (innerFound && IsMulticastDelegateArrayType(fv.arrayInnerType)
                    && arr.Data && fv.arrayCount > 0) {
                    auto mcastResult = ReadMulticastDelegateArrayElements(
                        instanceAddr, fi.Offset, fv.arrayElemSize, 0, arrayLimit);
                    if (mcastResult.ok && !mcastResult.elements.empty()) {
                        fv.arrayElements = std::move(mcastResult.elements);
                        Sein::Debug("WALK:ArrayP", "Multicast elements: %d read for '%s'",
                            static_cast<int>(fv.arrayElements.size()), fi.Name.c_str());
                    }
                }

                if (!innerFound) {
                    // Diagnostic: hex dump around FARRAYPROP_INNER to help identify correct offset
                    uint8_t dumpBuf[64] = {};
                    int dumpStart = DynOff::FARRAYPROP_INNER - 16;
                    if (dumpStart < 0) dumpStart = 0;
                    Macht::ReadBytesSafe(fi.Address + dumpStart, dumpBuf, 64);
                    char hexDump[200] = {};
                    for (int i = 0; i < 64 && i < (int)sizeof(hexDump)/3; i++)
                        snprintf(hexDump + i*3, 4, "%02X ", dumpBuf[i]);
                    Sein::Info("WALK:ArrayP", "Inner NOT found for '%s' (FField=0x%llX, FARRAYPROP_INNER=0x%X, FSTRUCTPROP_STRUCT=0x%X)",
                        fi.Name.c_str(), static_cast<unsigned long long>(fi.Address),
                        DynOff::FARRAYPROP_INNER, DynOff::FSTRUCTPROP_STRUCT);
                    Sein::Info("WALK:ArrayP", "  hex @+0x%X..+0x%X: %s", dumpStart, dumpStart+64, hexDump);
                }
            } else {
                // UProperty mode (UE4 <4.25): UArrayProperty::Inner is a UProperty* (UObject subclass).
                // Located at end of UProperty base class = UPROPERTY_OFFSET + 0x2C (standard delta).
                int baseOff = DynOff::UPROPERTY_OFFSET + 0x2C;
                static const int kUPropProbeOffsets[] = { 0, 8, -8, 0x10, -0x10, 4, -4 };
                bool innerFound = false;
                for (int delta : kUPropProbeOffsets) {
                    int tryOff = baseOff + delta;
                    if (tryOff < 0) continue;
                    uintptr_t inner = 0;
                    if (!Macht::ReadSafe(fi.Address + tryOff, inner) || !inner) continue;
                    if (!Grimoire::IsUserspacePointer(inner)) continue;

                    std::string innerTypeName = GetUPropertyTypeName(inner);
                    if (!innerTypeName.empty() && innerTypeName.find("Property") != std::string::npos) {
                        fv.arrayInnerType = innerTypeName;
                        // Read element size and validate (same garbage-guard as FProperty mode)
                        int32_t rawElemSize = 0;
                        Macht::ReadSafe<int32_t>(inner + DynOff::UPROPERTY_ELEMSIZE, rawElemSize);
                        fv.arrayElemSize = ValidateArrayElemSize(rawElemSize, innerTypeName);

                        if (innerTypeName == "StructProperty") {
                            // UStructProperty::Struct at same base offset
                            uintptr_t innerStruct = 0;
                            if (Macht::ReadSafe(inner + baseOff, innerStruct) && innerStruct) {
                                fv.arrayInnerStructType = GetName(innerStruct);
                                fv.arrayInnerStructAddr = innerStruct;
                                // Fallback: use UScriptStruct::PropertiesSize when ElementSize is 0
                                if (fv.arrayElemSize <= 0) {
                                    int32_t propsSize = 0;
                                    if (Macht::ReadSafe(innerStruct + DynOff::USTRUCT_PROPSSIZE, propsSize) && propsSize > 0 && propsSize <= 65536) {
                                        fv.arrayElemSize = propsSize;
                                        Sein::Info("WALK:ArrayP", "Fallback: used PropertiesSize=%d for '%s' struct '%s'",
                                            propsSize, fi.Name.c_str(), fv.arrayInnerStructType.c_str());
                                    }
                                }
                            }
                        }

                        Sein::Info("WALK:ArrayP", "UArrayProperty::Inner at UProperty+0x%X (delta=%d) for '%s' -> '%s' elemSize=%d",
                            tryOff, delta, fi.Name.c_str(), innerTypeName.c_str(), fv.arrayElemSize);
                        fv.arrayInnerFFieldAddr = inner;  // reuse field for UProperty* too
                        innerFound = true;
                        break;
                    }
                }

                // Phase B-F: same inline element reading as FProperty mode
                if (innerFound && IsScalarArrayType(fv.arrayInnerType)
                    && arr.Data && fv.arrayCount > 0 && fv.arrayElemSize > 0) {
                    auto elemResult = ReadArrayElements(
                        instanceAddr, fi.Offset,
                        fv.arrayInnerFFieldAddr, fv.arrayInnerType,
                        fv.arrayElemSize, 0, arrayLimit);
                    if (elemResult.ok && !elemResult.elements.empty()) {
                        fv.arrayElements = std::move(elemResult.elements);
                    }
                }
                if (innerFound && IsPointerArrayType(fv.arrayInnerType)
                    && arr.Data && fv.arrayCount > 0 && fv.arrayElemSize > 0) {
                    auto ptrResult = ReadPointerArrayElements(
                        instanceAddr, fi.Offset, fv.arrayElemSize, 0, arrayLimit);
                    if (ptrResult.ok && !ptrResult.elements.empty()) {
                        fv.arrayElements = std::move(ptrResult.elements);
                    }
                }
                if (innerFound && IsWeakPointerArrayType(fv.arrayInnerType)
                    && arr.Data && fv.arrayCount > 0 && fv.arrayElemSize > 0) {
                    auto weakResult = ReadWeakObjectArrayElements(
                        instanceAddr, fi.Offset, fv.arrayElemSize, 0, arrayLimit);
                    if (weakResult.ok && !weakResult.elements.empty()) {
                        fv.arrayElements = std::move(weakResult.elements);
                    }
                }
                if (innerFound && IsStructArrayType(fv.arrayInnerType)
                    && fv.arrayInnerStructAddr != 0
                    && arr.Data && fv.arrayCount > 0 && fv.arrayElemSize > 0) {
                    auto structResult = ReadStructArrayElements(
                        instanceAddr, fi.Offset,
                        fv.arrayInnerStructAddr, fv.arrayElemSize, 0, arrayLimit);
                    if (structResult.ok && !structResult.elements.empty()) {
                        fv.arrayElements = std::move(structResult.elements);
                    }
                }
                // Phase G: Soft object arrays (UProperty mode)
                if (innerFound && IsSoftObjectArrayType(fv.arrayInnerType)) {
                    fv.softArrayFNameSize = DynOff::SizeofFName();
                    fv.softArrayIsTopLevelAssetPath = (g_cachedUEVersion >= 501);
                    fv.softArrayPathOffset = SoftPathOffset(fv.arrayElemSize);
                    if (arr.Data && fv.arrayCount > 0) {
                        auto softResult = ReadSoftObjectArrayElements(
                            instanceAddr, fi.Offset, fv.arrayElemSize, 0, arrayLimit);
                        if (softResult.ok && !softResult.elements.empty()) {
                            fv.arrayElements = std::move(softResult.elements);
                        }
                    }
                }
                // Phase H: Lazy object arrays (UProperty mode)
                if (innerFound && IsLazyObjectArrayType(fv.arrayInnerType)
                    && arr.Data && fv.arrayCount > 0) {
                    auto lazyResult = ReadLazyObjectArrayElements(
                        instanceAddr, fi.Offset, fv.arrayElemSize, 0, arrayLimit);
                    if (lazyResult.ok && !lazyResult.elements.empty()) {
                        fv.arrayElements = std::move(lazyResult.elements);
                    }
                }
                // Phase I: Interface arrays (UProperty mode)
                if (innerFound && IsInterfaceArrayType(fv.arrayInnerType)
                    && arr.Data && fv.arrayCount > 0) {
                    auto ifaceResult = ReadInterfaceArrayElements(
                        instanceAddr, fi.Offset, fv.arrayElemSize, 0, arrayLimit);
                    if (ifaceResult.ok && !ifaceResult.elements.empty()) {
                        fv.arrayElements = std::move(ifaceResult.elements);
                    }
                }
                // Phase J: Delegate arrays (UProperty mode)
                if (innerFound && IsDelegateArrayType(fv.arrayInnerType)
                    && arr.Data && fv.arrayCount > 0) {
                    auto delResult = ReadDelegateArrayElements(
                        instanceAddr, fi.Offset, fv.arrayElemSize, 0, arrayLimit);
                    if (delResult.ok && !delResult.elements.empty()) {
                        fv.arrayElements = std::move(delResult.elements);
                    }
                }
                // Phase K: Multicast delegate arrays (UProperty mode)
                if (innerFound && IsMulticastDelegateArrayType(fv.arrayInnerType)
                    && arr.Data && fv.arrayCount > 0) {
                    auto mcastResult = ReadMulticastDelegateArrayElements(
                        instanceAddr, fi.Offset, fv.arrayElemSize, 0, arrayLimit);
                    if (mcastResult.ok && !mcastResult.elements.empty()) {
                        fv.arrayElements = std::move(mcastResult.elements);
                    }
                }

                if (!innerFound) {
                    Sein::Info("WALK:ArrayP", "UArrayProperty::Inner NOT found for '%s' (UProperty=0x%llX, baseOff=0x%X)",
                        fi.Name.c_str(), static_cast<unsigned long long>(fi.Address), baseOff);
                }
            }

            result.fields.push_back(std::move(fv));
            continue;
        }

        // Handle MapProperty: read TMap (wraps TSet<TPair<Key,Value>>)
        if (fi.TypeName == "MapProperty") {
            Macht::TSparseArrayView sa;
            if (Macht::ReadTSparseArray(instanceAddr + fi.Offset, sa)) {
                fv.mapCount = sa.MaxIndex - sa.NumFreeIndices;
                if (fv.mapCount < 0) fv.mapCount = 0;
                fv.mapDataAddr = sa.Data;
                // Hex header: Data + MaxIndex + NumFree
                char buf[48];
                snprintf(buf, sizeof(buf), "%016llX %08X %08X",
                    static_cast<unsigned long long>(sa.Data), sa.MaxIndex, sa.NumFreeIndices);
                fv.hexValue = buf;
            } else {
                fv.mapCount = 0;
            }

            // Probe for KeyProp and ValueProp FProperty*
            if (DynOff::bUseFProperty) {
                static const int kProbeOffsets[] = { 0, 8, 4, 0xC, -4, -8, 0x10, -0x10 };
                for (int delta : kProbeOffsets) {
                    int tryOff = DynOff::FSTRUCTPROP_STRUCT + delta;
                    if (tryOff < 0) continue;
                    uintptr_t keyProp = 0;
                    if (!Macht::ReadSafe(fi.Address + tryOff, keyProp) || !keyProp) continue;
                    if (!Grimoire::IsUserspacePointer(keyProp)) continue;

                    std::string keyTypeName = GetFieldTypeName(keyProp);
                    if (keyTypeName.empty() || keyTypeName == "Unknown"
                        || keyTypeName.find("Property") == std::string::npos) continue;

                    // Found KeyProp — ValueProp is at +8 from KeyProp
                    uintptr_t valueProp = 0;
                    Macht::ReadSafe(fi.Address + tryOff + 8, valueProp);
                    std::string valueTypeName = valueProp ? GetFieldTypeName(valueProp) : "";

                    if (!valueTypeName.empty() && valueTypeName.find("Property") != std::string::npos) {
                        fv.mapKeyType = keyTypeName;
                        fv.mapValueType = valueTypeName;
                        // Route through the SAME validator every ArrayProperty path uses.
                        // This read is documented in this file to return garbage: the
                        // observed 1073742336 is 0x40000200, the low dword of
                        // EPropertyFlags — i.e. exactly what you get when
                        // DynOff::FPROPERTY_ELEMSIZE lands on PropertyFlags (Genau derives
                        // it blind as bestProbe - 0x10 and can still report
                        // bOffsetsValidated=true — audit #5 G1). Unvalidated it became a
                        // per-element std::vector size: a ~1 GiB commit + zero-fill on every
                        // iteration, plus a ~1 GiB stride that made every element wild.
                        int32_t rawKeySize = 0, rawValSize = 0;
                        Macht::ReadSafe<int32_t>(keyProp + DynOff::FPROPERTY_ELEMSIZE, rawKeySize);
                        Macht::ReadSafe<int32_t>(valueProp + DynOff::FPROPERTY_ELEMSIZE, rawValSize);
                        fv.mapKeySize   = ValidateArrayElemSize(rawKeySize, keyTypeName);
                        fv.mapValueSize = ValidateArrayElemSize(rawValSize, valueTypeName);
                        Sein::Info("WALK:MapP", "FMapProperty KeyProp='%s'(%d) ValueProp='%s'(%d) at delta=%d for '%s'",
                            keyTypeName.c_str(), fv.mapKeySize, valueTypeName.c_str(), fv.mapValueSize,
                            delta, fi.Name.c_str());

                        // If key/value is StructProperty, read UScriptStruct* for navigation
                        if (keyTypeName == "StructProperty") {
                            uintptr_t kStruct = 0;
                            if (Macht::ReadSafe(keyProp + DynOff::FSTRUCTPROP_STRUCT, kStruct) && kStruct) {
                                fv.mapKeyStructAddr = kStruct;
                                fv.mapKeyStructType = GetName(kStruct);
                            }
                        }
                        if (valueTypeName == "StructProperty") {
                            uintptr_t vStruct = 0;
                            if (Macht::ReadSafe(valueProp + DynOff::FSTRUCTPROP_STRUCT, vStruct) && vStruct) {
                                fv.mapValueStructAddr = vStruct;
                                fv.mapValueStructType = GetName(vStruct);
                            }
                        }

                        // Read inline element values if count is manageable
                        if (fv.mapCount > 0
                            && sa.Data && fv.mapKeySize > 0 && fv.mapValueSize > 0) {
                            // Key/value alignment from the real per-type rule (NOT a size
                            // guess) — FName/FWeakObjectPtr are 8 bytes but 4-aligned, so a
                            // Map<Enum, Name> puts the value at +4. Wrong align => wrong
                            // offset AND stride => every element reads garbage. For a
                            // StructProperty this reads UScriptStruct::MinAlignment, which
                            // Scharf deliberately will not answer (it is a validation helper,
                            // not a layout oracle) — without it every struct-valued TMap fell
                            // through to the size guess.
                            int32_t keyAlign = ResolveElementAlignment(
                                keyTypeName, fv.mapKeySize, fv.mapKeyStructAddr);
                            int32_t valAlign = ResolveElementAlignment(
                                valueTypeName, fv.mapValueSize, fv.mapValueStructAddr);
                            // alignof(TPair<K,V>) == max(alignof(K), alignof(V)); the stride
                            // must be a multiple of it or the TPair's trailing padding is
                            // dropped and every element past index 0 reads at a wrong address.
                            int32_t pairAlign = (keyAlign > valAlign) ? keyAlign : valAlign;
                            int32_t valOffset = Macht::ComputeMapValueOffset(
                                fv.mapKeySize, fv.mapValueSize, valAlign);
                            int32_t pairSize = valOffset + fv.mapValueSize;
                            int32_t stride = Macht::ComputeSetElementStride(pairSize, pairAlign);
                            fv.mapValueOffset = valOffset;
                            fv.mapStride = stride;   // publish the stride actually used (audit #5 V2)
                            Sein::Debug("WALK:MapP", "Reading %d map entries for '%s': Data=0x%llX KeySz=%d ValSz=%d ValOff=%d Stride=%d MaxIdx=%d NumBits=%d",
                                fv.mapCount, fi.Name.c_str(), (unsigned long long)sa.Data,
                                fv.mapKeySize, fv.mapValueSize, valOffset, stride, sa.MaxIndex, sa.numBits);
                            int read = 0;
                            int skipped = 0;
                            for (int32_t idx = 0; idx < sa.MaxIndex && read < fv.mapCount && read < arrayLimit; ++idx) {
                                if (!Macht::IsSparseIndexAllocated(sa, idx)) { skipped++; continue; }
                                uintptr_t elemAddr = sa.Data + static_cast<uintptr_t>(idx) * stride;
                                LiveFieldValue::ContainerElement ce;
                                ce.index = idx;
                                // Read key bytes
                                std::vector<uint8_t> keyBuf(fv.mapKeySize);
                                if (Macht::ReadBytesSafe(elemAddr, keyBuf.data(), fv.mapKeySize)) {
                                    ce.key = PreferLayout(keyBuf.data(), fv.mapKeySize, fv.mapKeyStructAddr,
                                                                  keyTypeName);
                                    // Hex
                                    std::string kh;
                                    int klen = (std::min)(fv.mapKeySize, 16);
                                    for (int h = 0; h < klen; ++h) {
                                        char hx[3]; snprintf(hx, sizeof(hx), "%02X", keyBuf[h]);
                                        kh += hx;
                                    }
                                    ce.keyHex = kh;
                                    // Pointer key: resolve name, addr, class
                                    if (keyTypeName == "ObjectProperty" || keyTypeName == "ClassProperty") {
                                        uintptr_t ptr = 0;
                                        memcpy(&ptr, keyBuf.data(), (std::min)(fv.mapKeySize, (int32_t)sizeof(ptr)));
                                        if (ptr) {
                                            ce.keyPtrAddr = ptr;
                                            ce.keyPtrName = GetName(ptr);
                                            uintptr_t cls = GetClass(ptr);
                                            if (cls) ce.keyPtrClassName = GetName(cls);
                                        }
                                    }
                                    // FName key: resolve name
                                    if (keyTypeName == "NameProperty" && ce.key.empty()) {
                                        ce.key = ce.keyHex;  // fallback
                                    }
                                }
                                // Read value bytes (at aligned offset within pair)
                                std::vector<uint8_t> valBuf(fv.mapValueSize);
                                if (Macht::ReadBytesSafe(elemAddr + valOffset, valBuf.data(), fv.mapValueSize)) {
                                    ce.value = PreferLayout(valBuf.data(), fv.mapValueSize, fv.mapValueStructAddr,
                                                                    valueTypeName);
                                    std::string vh;
                                    int vlen = (std::min)(fv.mapValueSize, 16);
                                    for (int h = 0; h < vlen; ++h) {
                                        char hx[3]; snprintf(hx, sizeof(hx), "%02X", valBuf[h]);
                                        vh += hx;
                                    }
                                    ce.valueHex = vh;
                                    if (valueTypeName == "ObjectProperty" || valueTypeName == "ClassProperty") {
                                        uintptr_t ptr = 0;
                                        memcpy(&ptr, valBuf.data(), (std::min)(fv.mapValueSize, (int32_t)sizeof(ptr)));
                                        if (ptr) {
                                            ce.valuePtrAddr = ptr;
                                            ce.valuePtrName = GetName(ptr);
                                            uintptr_t cls = GetClass(ptr);
                                            if (cls) ce.valuePtrClassName = GetName(cls);
                                        }
                                    }
                                }
                                fv.containerElements.push_back(std::move(ce));
                                ++read;
                            }
                            Sein::Debug("WALK:MapP", "Read %d/%d map entries for '%s' (skipped %d unallocated)", read, fv.mapCount, fi.Name.c_str(), skipped);
                        } else if (fv.mapCount > 0 || sa.Data != 0) {
                            // Only warn when the map *should* have been readable. An empty
                            // TMap (count=0, Data=null) is a normal default-initialised state,
                            // not a walker failure — silencing those drowned out real cases.
                            Sein::Warn("WALK:MapP", "Cannot read map elements for '%s': count=%d Data=0x%llX KeySz=%d ValSz=%d",
                                fi.Name.c_str(), fv.mapCount, (unsigned long long)sa.Data, fv.mapKeySize, fv.mapValueSize);
                        }
                        break;
                    }
                }
            } else {
                // UProperty mode (UE4 <4.25): UMapProperty has KeyProp + ValueProp as UProperty*.
                int baseOff = DynOff::UPROPERTY_OFFSET + 0x2C;
                static const int kUPropProbeOffsets[] = { 0, 8, -8, 0x10, -0x10, 4, -4 };
                for (int delta : kUPropProbeOffsets) {
                    int tryOff = baseOff + delta;
                    if (tryOff < 0) continue;
                    uintptr_t keyProp = 0;
                    if (!Macht::ReadSafe(fi.Address + tryOff, keyProp) || !keyProp) continue;
                    if (!Grimoire::IsUserspacePointer(keyProp)) continue;

                    std::string keyTypeName = GetUPropertyTypeName(keyProp);
                    if (keyTypeName.empty() || keyTypeName.find("Property") == std::string::npos) continue;

                    uintptr_t valueProp = 0;
                    Macht::ReadSafe(fi.Address + tryOff + 8, valueProp);
                    std::string valueTypeName = (valueProp && valueProp > Grimoire::PTR_USERSPACE_MIN
                        && valueProp < Grimoire::PTR_USERSPACE_MAX) ? GetUPropertyTypeName(valueProp) : "";

                    if (!valueTypeName.empty() && valueTypeName.find("Property") != std::string::npos) {
                        fv.mapKeyType = keyTypeName;
                        fv.mapValueType = valueTypeName;
                        // Same validation as the FProperty twin above — see the comment there.
                        int32_t rawKeySize = 0, rawValSize = 0;
                        Macht::ReadSafe<int32_t>(keyProp + DynOff::UPROPERTY_ELEMSIZE, rawKeySize);
                        Macht::ReadSafe<int32_t>(valueProp + DynOff::UPROPERTY_ELEMSIZE, rawValSize);
                        fv.mapKeySize   = ValidateArrayElemSize(rawKeySize, keyTypeName);
                        fv.mapValueSize = ValidateArrayElemSize(rawValSize, valueTypeName);
                        Sein::Info("WALK:MapP", "UMapProperty KeyProp='%s'(%d) ValueProp='%s'(%d) at delta=%d for '%s'",
                            keyTypeName.c_str(), fv.mapKeySize, valueTypeName.c_str(), fv.mapValueSize,
                            delta, fi.Name.c_str());

                        if (keyTypeName == "StructProperty") {
                            uintptr_t kStruct = 0;
                            if (Macht::ReadSafe(keyProp + baseOff, kStruct) && kStruct) {
                                fv.mapKeyStructAddr = kStruct;
                                fv.mapKeyStructType = GetName(kStruct);
                            }
                        }
                        if (valueTypeName == "StructProperty") {
                            uintptr_t vStruct = 0;
                            if (Macht::ReadSafe(valueProp + baseOff, vStruct) && vStruct) {
                                fv.mapValueStructAddr = vStruct;
                                fv.mapValueStructType = GetName(vStruct);
                            }
                        }

                        // Read inline element values
                        if (fv.mapCount > 0 && sa.Data && fv.mapKeySize > 0 && fv.mapValueSize > 0) {
                            // Key/value alignment from the real per-type rule (NOT a size
                            // guess) — FName/FWeakObjectPtr are 8 bytes but 4-aligned, so a
                            // Map<Enum, Name> puts the value at +4. Wrong align => wrong
                            // offset AND stride => every element reads garbage. For a
                            // StructProperty this reads UScriptStruct::MinAlignment, which
                            // Scharf deliberately will not answer (it is a validation helper,
                            // not a layout oracle) — without it every struct-valued TMap fell
                            // through to the size guess.
                            int32_t keyAlign = ResolveElementAlignment(
                                keyTypeName, fv.mapKeySize, fv.mapKeyStructAddr);
                            int32_t valAlign = ResolveElementAlignment(
                                valueTypeName, fv.mapValueSize, fv.mapValueStructAddr);
                            // alignof(TPair<K,V>) == max(alignof(K), alignof(V)); the stride
                            // must be a multiple of it or the TPair's trailing padding is
                            // dropped and every element past index 0 reads at a wrong address.
                            int32_t pairAlign = (keyAlign > valAlign) ? keyAlign : valAlign;
                            int32_t valOffset = Macht::ComputeMapValueOffset(
                                fv.mapKeySize, fv.mapValueSize, valAlign);
                            int32_t pairSize = valOffset + fv.mapValueSize;
                            int32_t stride = Macht::ComputeSetElementStride(pairSize, pairAlign);
                            fv.mapValueOffset = valOffset;
                            fv.mapStride = stride;   // publish the stride actually used (audit #5 V2)
                            int read = 0;
                            for (int32_t idx = 0; idx < sa.MaxIndex && read < fv.mapCount && read < arrayLimit; ++idx) {
                                if (!Macht::IsSparseIndexAllocated(sa, idx)) continue;
                                uintptr_t elemAddr = sa.Data + static_cast<uintptr_t>(idx) * stride;
                                LiveFieldValue::ContainerElement ce;
                                ce.index = idx;
                                std::vector<uint8_t> keyBuf(fv.mapKeySize);
                                if (Macht::ReadBytesSafe(elemAddr, keyBuf.data(), fv.mapKeySize)) {
                                    ce.key = PreferLayout(keyBuf.data(), fv.mapKeySize, fv.mapKeyStructAddr,
                                                                  keyTypeName);
                                    std::string kh;
                                    int klen = (std::min)(fv.mapKeySize, 16);
                                    for (int h = 0; h < klen; ++h) {
                                        char hx[3]; snprintf(hx, sizeof(hx), "%02X", keyBuf[h]);
                                        kh += hx;
                                    }
                                    ce.keyHex = kh;
                                    if (keyTypeName == "ObjectProperty" || keyTypeName == "ClassProperty") {
                                        uintptr_t ptr = 0;
                                        memcpy(&ptr, keyBuf.data(), (std::min)(fv.mapKeySize, (int32_t)sizeof(ptr)));
                                        if (ptr) {
                                            ce.keyPtrAddr = ptr;
                                            ce.keyPtrName = GetName(ptr);
                                            uintptr_t cls = GetClass(ptr);
                                            if (cls) ce.keyPtrClassName = GetName(cls);
                                        }
                                    }
                                    if (keyTypeName == "NameProperty" && ce.key.empty()) {
                                        ce.key = ce.keyHex;
                                    }
                                }
                                std::vector<uint8_t> valBuf(fv.mapValueSize);
                                if (Macht::ReadBytesSafe(elemAddr + valOffset, valBuf.data(), fv.mapValueSize)) {
                                    ce.value = PreferLayout(valBuf.data(), fv.mapValueSize, fv.mapValueStructAddr,
                                                                    valueTypeName);
                                    std::string vh;
                                    int vlen = (std::min)(fv.mapValueSize, 16);
                                    for (int h = 0; h < vlen; ++h) {
                                        char hx[3]; snprintf(hx, sizeof(hx), "%02X", valBuf[h]);
                                        vh += hx;
                                    }
                                    ce.valueHex = vh;
                                    if (valueTypeName == "ObjectProperty" || valueTypeName == "ClassProperty") {
                                        uintptr_t ptr = 0;
                                        memcpy(&ptr, valBuf.data(), (std::min)(fv.mapValueSize, (int32_t)sizeof(ptr)));
                                        if (ptr) {
                                            ce.valuePtrAddr = ptr;
                                            ce.valuePtrName = GetName(ptr);
                                            uintptr_t cls = GetClass(ptr);
                                            if (cls) ce.valuePtrClassName = GetName(cls);
                                        }
                                    }
                                }
                                fv.containerElements.push_back(std::move(ce));
                                ++read;
                            }
                            Sein::Debug("WALK:MapP", "Read %d/%d map entries for '%s'", read, fv.mapCount, fi.Name.c_str());
                        }
                        break;
                    }
                }
            }

            result.fields.push_back(std::move(fv));
            continue;
        }

        // Handle SetProperty: read TSet (TSparseArray of elements)
        if (fi.TypeName == "SetProperty") {
            Macht::TSparseArrayView sa;
            if (Macht::ReadTSparseArray(instanceAddr + fi.Offset, sa)) {
                fv.setCount = sa.MaxIndex - sa.NumFreeIndices;
                if (fv.setCount < 0) fv.setCount = 0;
                fv.setDataAddr = sa.Data;
                char buf[48];
                snprintf(buf, sizeof(buf), "%016llX %08X %08X",
                    static_cast<unsigned long long>(sa.Data), sa.MaxIndex, sa.NumFreeIndices);
                fv.hexValue = buf;
            } else {
                fv.setCount = 0;
            }

            // Probe for ElementProp FProperty*
            if (DynOff::bUseFProperty) {
                static const int kProbeOffsets[] = { 0, 8, 4, 0xC, -4, -8, 0x10, -0x10 };
                for (int delta : kProbeOffsets) {
                    int tryOff = DynOff::FSTRUCTPROP_STRUCT + delta;
                    if (tryOff < 0) continue;
                    uintptr_t elemProp = 0;
                    if (!Macht::ReadSafe(fi.Address + tryOff, elemProp) || !elemProp) continue;
                    if (!Grimoire::IsUserspacePointer(elemProp)) continue;

                    std::string elemTypeName = GetFieldTypeName(elemProp);
                    if (elemTypeName.empty() || elemTypeName == "Unknown"
                        || elemTypeName.find("Property") == std::string::npos) continue;

                    fv.setElemType = elemTypeName;
                    // Validated like the Map twins and every ArrayProperty path — an
                    // unvalidated ELEMSIZE became a per-element std::vector size.
                    int32_t rawElemSize = 0;
                    Macht::ReadSafe<int32_t>(elemProp + DynOff::FPROPERTY_ELEMSIZE, rawElemSize);
                    fv.setElemSize = ValidateArrayElemSize(rawElemSize, elemTypeName);
                    Sein::Info("WALK:SetP", "FSetProperty ElementProp='%s'(%d) at delta=%d for '%s'",
                        elemTypeName.c_str(), fv.setElemSize, delta, fi.Name.c_str());

                    // If element is StructProperty, read UScriptStruct* for navigation
                    if (elemTypeName == "StructProperty") {
                        uintptr_t eStruct = 0;
                        if (Macht::ReadSafe(elemProp + DynOff::FSTRUCTPROP_STRUCT, eStruct) && eStruct) {
                            fv.setElemStructAddr = eStruct;
                            fv.setElemStructType = GetName(eStruct);
                            // Fallback: use UScriptStruct::PropertiesSize when ElementSize is 0
                            if (fv.setElemSize <= 0) {
                                int32_t propsSize = 0;
                                if (Macht::ReadSafe(eStruct + DynOff::USTRUCT_PROPSSIZE, propsSize) && propsSize > 0 && propsSize <= 65536) {
                                    fv.setElemSize = propsSize;
                                    Sein::Info("WALK:SetP", "Fallback: used PropertiesSize=%d for '%s' struct '%s'",
                                        propsSize, fi.Name.c_str(), fv.setElemStructType.c_str());
                                }
                            }
                        }
                    }

                    // Read inline element values if count is manageable
                    if (fv.setCount > 0
                        && sa.Data && fv.setElemSize > 0) {
                        int32_t stride = Macht::ComputeSetElementStride(fv.setElemSize);
                        fv.setStride = stride;   // publish the stride actually used (audit #5 V2)
                        int read = 0;
                        for (int32_t idx = 0; idx < sa.MaxIndex && read < fv.setCount && read < arrayLimit; ++idx) {
                            if (!Macht::IsSparseIndexAllocated(sa, idx)) continue;
                            uintptr_t elemAddr = sa.Data + static_cast<uintptr_t>(idx) * stride;
                            LiveFieldValue::ContainerElement ce;
                            ce.index = idx;
                            std::vector<uint8_t> elemBuf(fv.setElemSize);
                            if (Macht::ReadBytesSafe(elemAddr, elemBuf.data(), fv.setElemSize)) {
                                ce.key = PreferLayout(elemBuf.data(), fv.setElemSize, fv.setElemStructAddr,
                                                              elemTypeName);
                                std::string eh;
                                int elen = (std::min)(fv.setElemSize, 16);
                                for (int h = 0; h < elen; ++h) {
                                    char hx[3]; snprintf(hx, sizeof(hx), "%02X", elemBuf[h]);
                                    eh += hx;
                                }
                                ce.keyHex = eh;
                                if (elemTypeName == "ObjectProperty" || elemTypeName == "ClassProperty") {
                                    uintptr_t ptr = 0;
                                    memcpy(&ptr, elemBuf.data(), (std::min)(fv.setElemSize, (int32_t)sizeof(ptr)));
                                    if (ptr) {
                                        ce.keyPtrAddr = ptr;
                                        ce.keyPtrName = GetName(ptr);
                                        uintptr_t cls = GetClass(ptr);
                                        if (cls) ce.keyPtrClassName = GetName(cls);
                                    }
                                }
                            }
                            fv.containerElements.push_back(std::move(ce));
                            ++read;
                        }
                        Sein::Debug("WALK:SetP", "Read %d/%d set entries for '%s'", read, fv.setCount, fi.Name.c_str());
                    }
                    break;
                }
            } else {
                // UProperty mode (UE4 <4.25): USetProperty::ElementProp is a UProperty*.
                int baseOff = DynOff::UPROPERTY_OFFSET + 0x2C;
                static const int kUPropProbeOffsets[] = { 0, 8, -8, 0x10, -0x10, 4, -4 };
                for (int delta : kUPropProbeOffsets) {
                    int tryOff = baseOff + delta;
                    if (tryOff < 0) continue;
                    uintptr_t elemProp = 0;
                    if (!Macht::ReadSafe(fi.Address + tryOff, elemProp) || !elemProp) continue;
                    if (!Grimoire::IsUserspacePointer(elemProp)) continue;

                    std::string elemTypeName = GetUPropertyTypeName(elemProp);
                    if (elemTypeName.empty() || elemTypeName.find("Property") == std::string::npos) continue;

                    fv.setElemType = elemTypeName;
                    // Validated like the Map twins and every ArrayProperty path — an
                    // unvalidated ELEMSIZE became a per-element std::vector size.
                    int32_t rawElemSize = 0;
                    Macht::ReadSafe<int32_t>(elemProp + DynOff::UPROPERTY_ELEMSIZE, rawElemSize);
                    fv.setElemSize = ValidateArrayElemSize(rawElemSize, elemTypeName);
                    Sein::Info("WALK:SetP", "USetProperty ElementProp='%s'(%d) at delta=%d for '%s'",
                        elemTypeName.c_str(), fv.setElemSize, delta, fi.Name.c_str());

                    if (elemTypeName == "StructProperty") {
                        uintptr_t eStruct = 0;
                        if (Macht::ReadSafe(elemProp + baseOff, eStruct) && eStruct) {
                            fv.setElemStructAddr = eStruct;
                            fv.setElemStructType = GetName(eStruct);
                            // Fallback: use UScriptStruct::PropertiesSize when ElementSize is 0
                            if (fv.setElemSize <= 0) {
                                int32_t propsSize = 0;
                                if (Macht::ReadSafe(eStruct + DynOff::USTRUCT_PROPSSIZE, propsSize) && propsSize > 0 && propsSize <= 65536) {
                                    fv.setElemSize = propsSize;
                                    Sein::Info("WALK:SetP", "Fallback: used PropertiesSize=%d for '%s' struct '%s'",
                                        propsSize, fi.Name.c_str(), fv.setElemStructType.c_str());
                                }
                            }
                        }
                    }

                    if (fv.setCount > 0 && sa.Data && fv.setElemSize > 0) {
                        int32_t stride = Macht::ComputeSetElementStride(fv.setElemSize);
                        fv.setStride = stride;   // publish the stride actually used (audit #5 V2)
                        int read = 0;
                        for (int32_t idx = 0; idx < sa.MaxIndex && read < fv.setCount && read < arrayLimit; ++idx) {
                            if (!Macht::IsSparseIndexAllocated(sa, idx)) continue;
                            uintptr_t elemAddr = sa.Data + static_cast<uintptr_t>(idx) * stride;
                            LiveFieldValue::ContainerElement ce;
                            ce.index = idx;
                            std::vector<uint8_t> elemBuf(fv.setElemSize);
                            if (Macht::ReadBytesSafe(elemAddr, elemBuf.data(), fv.setElemSize)) {
                                ce.key = PreferLayout(elemBuf.data(), fv.setElemSize, fv.setElemStructAddr,
                                                              elemTypeName);
                                std::string eh;
                                int elen = (std::min)(fv.setElemSize, 16);
                                for (int h = 0; h < elen; ++h) {
                                    char hx[3]; snprintf(hx, sizeof(hx), "%02X", elemBuf[h]);
                                    eh += hx;
                                }
                                ce.keyHex = eh;
                                if (elemTypeName == "ObjectProperty" || elemTypeName == "ClassProperty") {
                                    uintptr_t ptr = 0;
                                    memcpy(&ptr, elemBuf.data(), (std::min)(fv.setElemSize, (int32_t)sizeof(ptr)));
                                    if (ptr) {
                                        ce.keyPtrAddr = ptr;
                                        ce.keyPtrName = GetName(ptr);
                                        uintptr_t cls = GetClass(ptr);
                                        if (cls) ce.keyPtrClassName = GetName(cls);
                                    }
                                }
                            }
                            fv.containerElements.push_back(std::move(ce));
                            ++read;
                        }
                        Sein::Debug("WALK:SetP", "Read %d/%d set entries for '%s'", read, fv.setCount, fi.Name.c_str());
                    }
                    break;
                }
            }

            result.fields.push_back(std::move(fv));
            continue;
        }

        // Handle StructProperty: extract inner UScriptStruct* for navigation
        if (fi.TypeName == "StructProperty") {
            // Try the derived offset first, then probe nearby offsets
            static const int kStructPtrProbeOffsets[] = { 0, 4, -4, 8, -8, 0x10, -0x10 };
            bool found = false;
            for (int delta : kStructPtrProbeOffsets) {
                int tryOffset = DynOff::FSTRUCTPROP_STRUCT + delta;
                if (tryOffset < 0) continue;
                uintptr_t candidate = 0;
                if (!Macht::ReadSafe(fi.Address + tryOffset, candidate) || !candidate) continue;
                // Skip obvious garbage addresses to avoid SEH faults
                if (!Grimoire::IsUserspacePointer(candidate)) continue;
                // Validate: must be a UScriptStruct (inherits UObject), so GetName should return ASCII
                std::string sname = GetName(candidate);
                if (!sname.empty() && sname[0] >= 0x20 && sname[0] < 0x7F) {
                    fv.structClassAddr = candidate;
                    fv.structTypeName  = sname;
                    fv.structDataAddr  = instanceAddr + fi.Offset;
                    if (delta != 0) {
                        Sein::Info("WALK:StructP", "FStructProperty::Struct at FField+0x%X (base=0x%X, delta=%d) for '%s' -> '%s'",
                            tryOffset, DynOff::FSTRUCTPROP_STRUCT, delta, fi.Name.c_str(), sname.c_str());
                        // Persist correction to DynOff (CorrectSubclassOffsets handles the global
                        // update, but if it didn't run yet or missed, update here too)
                        DynOff::FSTRUCTPROP_STRUCT = tryOffset;
                    }
                    found = true;
                    break;
                }
            }
            if (!found) {
                Sein::Debug("WALK:StructP", "FStructProperty::Struct not found for '%s' (FField=0x%llX, probed 0x%X +/- 16)",
                    fi.Name.c_str(), static_cast<unsigned long long>(fi.Address), DynOff::FSTRUCTPROP_STRUCT);
            }

            // Generate field-based preview using cached WalkClass data.
            // Much more accurate than InterpretValue's "interpret all bytes as floats".
            // previewLimit controls how many sub-fields to show (0 = skip preview entirely).
            if (found && fv.structClassAddr && previewLimit > 0) {
                // Memoized, but NOT free: WalkClass returns ClassInfo BY VALUE, so a
                // cache HIT is a hash lookup plus a deep copy of the flattened super
                // chain (Fields carries the whole inheritance chain — 100-300 FieldInfo
                // x 14 std::string on an ordinary Actor subclass). This sits in a
                // per-field loop, so the copy is paid once per struct field. The comment
                // here used to read "just hash lookup", which is exactly the claim a
                // reader checks before leaving a call inside a loop (audit #5 U18).
                // Not switched to WalkClassEx's by-reference form: the enclosing block
                // is itself calibrating DynOff::FSTRUCTPROP_STRUCT, and WalkClassEx runs
                // CorrectSubclassOffsets, which writes that same global.
                ClassInfo si = WalkClass(fv.structClassAddr);
                uintptr_t structBase = instanceAddr + fi.Offset;

                // Bulk read struct bytes — single cross-process read for both
                // preview AND hex display (was N individual ReadSafe calls).
                int32_t readSize = fi.Size;
                if (readSize <= 0 || readSize > 1024) {
                    readSize = si.PropertiesSize;
                    if (readSize <= 0 || readSize > 1024) readSize = 0;
                }
                std::vector<uint8_t> structBuf;
                bool hasBuf = false;
                if (readSize > 0) {
                    structBuf.resize(readSize, 0);
                    hasBuf = Macht::ReadBytesSafe(structBase, structBuf.data(), readSize);
                }

                // Preview: one shared decoder, so this path and every container /
                // preview / DataTable path cannot drift apart (audit U17).
                if (hasBuf) {
                    std::string preview = InterpretStructByLayout(
                        structBuf.data(), readSize, si, previewLimit);
                    if (!preview.empty()) fv.typedValue = preview;
                }

                // Hex display: reuse bulk-read buffer (no second ReadBytesSafe)
                int32_t hexSize = (readSize <= 256) ? readSize : 0;
                if (hexSize > 0 && hasBuf) {
                    fv.size = hexSize;
                    std::string hex;
                    hex.reserve(hexSize * 2);
                    for (int i = 0; i < hexSize; ++i) {
                        char hx[3]; snprintf(hx, sizeof(hx), "%02X", structBuf[i]);
                        hex += hx;
                    }
                    fv.hexValue = hex;
                }
                result.fields.push_back(std::move(fv));
                continue;
            }
            // Fall through to generic scalar handler if struct not resolved
        }

        // BoolProperty: extract FieldMask/ByteOffset for bitfield display
        if (fi.TypeName == "BoolProperty") {
            // FBoolProperty/UBoolProperty layout:
            //   uint8 FieldSize, ByteOffset, ByteMask, FieldMask
            // FProperty (UE4.25+/UE5): at FBOOLPROP_FIELDSIZE (~0x78)
            // UProperty (UE4 <4.25):   at UBOOLPROP_FIELDSIZE (~0x70)
            uint8_t boolBytes[4] = {};
            bool boolInfoRead = false;

            // Build probe list: try version-specific offset first, then nearby
            int baseOff = DynOff::bUseFProperty ? DynOff::FBOOLPROP_FIELDSIZE : DynOff::UBOOLPROP_FIELDSIZE;
            for (int tryOff : { baseOff, baseOff - 4, baseOff + 4, baseOff + 8, baseOff - 8 }) {
                if (tryOff < 0) continue;
                if (!Macht::ReadBytesSafe(fi.Address + tryOff, boolBytes, 4)) continue;

                uint8_t fieldSize  = boolBytes[0];
                uint8_t byteOff    = boolBytes[1];
                uint8_t byteMask   = boolBytes[2];
                uint8_t fieldMask  = boolBytes[3];

                // Validate: FieldSize should be 1 (single byte), ByteOffset typically 0-7,
                // FieldMask should be a single bit (power of 2) and non-zero
                if (fieldSize == 1 && fieldMask != 0 && (fieldMask & (fieldMask - 1)) == 0 &&
                    byteOff <= 7 && byteMask != 0 && (byteMask & (byteMask - 1)) == 0) {
                    fv.boolFieldMask = fieldMask;
                    fv.boolByteOffset = byteOff;

                    // Compute bit index from FieldMask
                    int bitIdx = 0;
                    uint8_t mask = fieldMask;
                    while (mask > 1) { mask >>= 1; ++bitIdx; }
                    fv.boolBitIndex = bitIdx;

                    boolInfoRead = true;
                    break;
                }
            }

            // Read actual value using FieldMask
            uint8_t rawByte = 0;
            int readOffset = fi.Offset + fv.boolByteOffset;
            if (Macht::ReadSafe(instanceAddr + readOffset, rawByte)) {
                char hexBuf[3];
                snprintf(hexBuf, sizeof(hexBuf), "%02X", rawByte);
                fv.hexValue = hexBuf;

                if (boolInfoRead) {
                    bool value = (rawByte & fv.boolFieldMask) != 0;
                    char desc[64];
                    snprintf(desc, sizeof(desc), "%s (bit %d, mask 0x%02X)",
                             value ? "true" : "false", fv.boolBitIndex, fv.boolFieldMask);
                    fv.typedValue = desc;
                } else {
                    fv.typedValue = rawByte ? "true" : "false";
                }
            }

            result.fields.push_back(std::move(fv));
            continue;
        }

        // Handle EnumProperty: read underlying int, resolve via UEnum
        if (fi.TypeName == "EnumProperty") {
            uintptr_t enumPtr = 0;
            Macht::ReadSafe(fi.Address + DynOff::FENUMPROP_ENUM, enumPtr);

            // Validate enum size: FPROPERTY_ELEMSIZE can be garbage for fields in
            // UScriptStruct layouts. Default to 1 (uint8, most common for BP enums).
            int32_t enumSize = fi.Size;
            if (enumSize != 1 && enumSize != 2 && enumSize != 4 && enumSize != 8)
                enumSize = 1;

            // Read raw value based on validated size
            int64_t rawVal = 0;
            if (enumSize == 1) { uint8_t v = 0; Macht::ReadSafe(instanceAddr + fi.Offset, v); rawVal = v; }
            else if (enumSize == 2) { int16_t v = 0; Macht::ReadSafe(instanceAddr + fi.Offset, v); rawVal = v; }
            else if (enumSize == 4) { int32_t v = 0; Macht::ReadSafe(instanceAddr + fi.Offset, v); rawVal = v; }
            else if (enumSize == 8) { int64_t v = 0; Macht::ReadSafe(instanceAddr + fi.Offset, v); rawVal = v; }

            fv.enumValue = rawVal;
            fv.size = enumSize;
            if (enumPtr) {
                fv.enumName = ResolveEnumValue(enumPtr, rawVal);
                fv.enumAddr = enumPtr;
                fv.enumEntries = GetEnumEntries(enumPtr);
            }
            fv.typedValue = fv.enumName.empty() ? std::to_string(rawVal) : fv.enumName;

            // Populate hex
            uint8_t buf[8] = {};
            Macht::ReadBytesSafe(instanceAddr + fi.Offset, buf, enumSize);
            std::string hex;
            hex.reserve(enumSize * 2);
            for (int i = 0; i < enumSize; ++i) { char hx[3]; snprintf(hx, sizeof(hx), "%02X", buf[i]); hex += hx; }
            fv.hexValue = hex;
            result.fields.push_back(std::move(fv));
            continue;
        }

        // Handle ByteProperty: check if it has a UEnum* (byte-sized enum)
        if (fi.TypeName == "ByteProperty") {
            uintptr_t enumPtr = 0;
            Macht::ReadSafe(fi.Address + DynOff::FBYTEPROP_ENUM, enumPtr);
            if (enumPtr) {
                // Validate it's actually a UEnum by checking its class name
                uintptr_t enumClass = GetClass(enumPtr);
                std::string enumClassName = enumClass ? GetName(enumClass) : "";
                if (enumClassName == "Enum" || enumClassName == "UserDefinedEnum") {
                    uint8_t rawVal = 0;
                    Macht::ReadSafe(instanceAddr + fi.Offset, rawVal);
                    fv.enumValue = rawVal;
                    fv.enumName = ResolveEnumValue(enumPtr, rawVal);
                    fv.enumAddr = enumPtr;
                    fv.enumEntries = GetEnumEntries(enumPtr);
                    fv.typedValue = fv.enumName.empty() ? std::to_string(rawVal) : fv.enumName;
                    char hx[3];
                    snprintf(hx, sizeof(hx), "%02X", rawVal);
                    fv.hexValue = hx;
                    result.fields.push_back(std::move(fv));
                    continue;
                }
            }
            // Fall through to generic scalar handling below
        }

        // Handle StrProperty / FUtf8StrProperty / FAnsiStrProperty: read the
        // string container → UTF-8. UE5.5+ added the 1-byte Utf8/Ansi variants
        // (FFieldClass names "Utf8StrProperty" / "AnsiStrProperty"); they share
        // FString's header but use ReadFUtf8String for the 1-byte payload.
        {
            const bool isWideStr = (fi.TypeName == "StrProperty");
            const bool isByteStr = (fi.TypeName == "Utf8StrProperty" ||
                                    fi.TypeName == "AnsiStrProperty");
            if (isWideStr || isByteStr) {
                fv.strValue = isWideStr ? ReadFString(instanceAddr, fi.Offset)
                                        : ReadFUtf8String(instanceAddr, fi.Offset);
                fv.typedValue = fv.strValue.empty() ? "(empty)" : fv.strValue;
                // Hex of the TArray header (Data ptr + Count)
                uintptr_t strData = 0;
                int32_t strCount = 0;
                Macht::ReadSafe(instanceAddr + fi.Offset, strData);
                Macht::ReadSafe(instanceAddr + fi.Offset + 8, strCount);
                char buf[48];
                snprintf(buf, sizeof(buf), "%016llX %08X",
                    static_cast<unsigned long long>(strData), strCount);
                fv.hexValue = buf;
                result.fields.push_back(std::move(fv));
                continue;
            }
        }

        // Verse property types (UEFN / Verse-authored content). WHICH names appear is a
        // build-flag question, and the DEFAULT is not the Verse VM: UBT's
        // TargetRules.bUseVerseBPVM defaults to TRUE, which yields WITH_VERSE_BPVM=1 and
        // WITH_VERSE_VM=0. VValue/VRestValue/VCell are compiled ONLY under
        // WITH_VERSE_VM=1 (and of those three only VRestValueProperty is ever emitted --
        // nothing in the engine constructs an FVValueProperty). In a default BPVM build
        // the same UPROPERTY comes out as "VerseDynamicProperty" instead, and the
        // VerseCell codegen case does not exist. Only VerseStringProperty -- which wraps
        // Verse::FNativeString -- is unconditional. None of these is a plain
        // FString/scalar, so label them and show the raw pointer rather than
        // mis-decoding. (Recognized-but-not-decoded; safe.)
        // Deliberately NOT matched here: "VerseDynamicProperty" and "VerseClassProperty"
        // (an FClassProperty subclass, also unguarded). Both fall through to the generic
        // hex path, which is safe, and no injectable Verse title exists to verify a
        // decoder against.
        if (fi.TypeName == "VValueProperty"  || fi.TypeName == "VRestValueProperty" ||
            fi.TypeName == "VCellProperty"   || fi.TypeName == "VerseStringProperty") {
            fv.typedValue = "(Verse: " + fi.TypeName + ")";
            uintptr_t vptr = 0;
            Macht::ReadSafe(instanceAddr + fi.Offset, vptr);
            char buf[20];
            snprintf(buf, sizeof(buf), "%016llX", static_cast<unsigned long long>(vptr));
            fv.hexValue = buf;
            result.fields.push_back(std::move(fv));
            continue;
        }

        // Handle TextProperty: FText = { ITextData* Data; ... } -- Data sits at +0x00 in
        // every version; only the tail after it changed at UE 5.4 (see ReadFTextString).
        // Dereference Data pointer, then probe for FString at common offsets within ITextData.
        if (fi.TypeName == "TextProperty") {
            fv.strValue = ReadFTextString(instanceAddr + fi.Offset);
            fv.typedValue = fv.strValue.empty() ? "(empty)" : fv.strValue;

            // Hex: show the ITextData pointer
            uintptr_t textDataPtr = 0;
            Macht::ReadSafe(instanceAddr + fi.Offset, textDataPtr);
            char buf[20];
            snprintf(buf, sizeof(buf), "%016llX", static_cast<unsigned long long>(textDataPtr));
            fv.hexValue = buf;
            result.fields.push_back(std::move(fv));
            continue;
        }

        // Handle DelegateProperty: single FScriptDelegate = { FWeakObjectPtr(8B), FName(8/16B) }
        if (fi.TypeName == "DelegateProperty") {
            int fnameSize = DynOff::SizeofFName();
            uintptr_t fieldAddr = instanceAddr + fi.Offset;

            int32_t objIdx = 0, serial = 0;
            Macht::ReadSafe(fieldAddr, objIdx);
            Macht::ReadSafe(fieldAddr + 4, serial);
            uintptr_t target = ResolveWeakObjectPtr(objIdx, serial);
            std::string funcName = ReadFName(fieldAddr + 8);

            if (target && !funcName.empty()) {
                std::string targetName = GetName(target);
                fv.typedValue = targetName + "::" + funcName;
                fv.ptrValue = target;
                fv.ptrName = targetName;
                uintptr_t cls = GetClass(target);
                if (cls) {
                    fv.ptrClassName = GetName(cls);
                    fv.ptrClassAddr = cls;
                }
            } else if (!funcName.empty()) {
                fv.typedValue = "(stale)::" + funcName;
            } else {
                fv.typedValue = "(unbound)";
            }

            // Hex: FWeakObjectPtr + FName raw bytes
            int delegateSize = 8 + fnameSize;
            std::vector<uint8_t> buf(delegateSize, 0);
            if (Macht::ReadBytesSafe(fieldAddr, buf.data(), delegateSize)) {
                std::string hex;
                hex.reserve(delegateSize * 2);
                for (auto b : buf) { char hx[3]; snprintf(hx, sizeof(hx), "%02X", b); hex += hx; }
                fv.hexValue = hex;
            }
            result.fields.push_back(std::move(fv));
            continue;
        }

        // Handle OptionalProperty (UE 5.2+): TOptional<T>.
        // Two storage layouts:
        //   - Intrusive (UE 5.4+ for pointer types): T occupies the field
        //     directly; "unset" is encoded as null/zero. Inner size == fi.Size.
        //   - Non-intrusive (older + non-pointer T): { T value; uint8 bIsSet; }.
        //     Trailing flag byte lives at field + sizeof(T).
        // Scalar/struct inner types use the trailing-flag form. Object/Class/
        // Interface and the FWeakObjectPtr-shaped types (Weak/Soft/Lazy) treat
        // null/zero as the unset sentinel.
        if (fi.TypeName == "OptionalProperty") {
            uintptr_t fieldAddr = instanceAddr + fi.Offset;
            // Probe inner ValueProperty (same offset as ArrayProperty::Inner —
            // both subclasses are FProperty + FProperty*).
            auto [innerProp, probedTn] = ProbeInnerProperty(fi.Address, DynOff::FARRAYPROP_INNER);
            std::string innerTn = !fi.innerType.empty() ? fi.innerType : probedTn;
            int32_t innerSize = innerProp ? ResolveInnerSize(innerProp, innerTn) : 0;

            const bool isObjectLike = innerTn == "ObjectProperty"
                                   || innerTn == "ClassProperty"
                                   || innerTn == "InterfaceProperty";
            const bool isWeakLike   = innerTn == "WeakObjectProperty"
                                   || innerTn == "SoftObjectProperty"
                                   || innerTn == "SoftClassProperty"
                                   || innerTn == "LazyObjectProperty";
            // Heap-backed types whose UE definitions specialize TOptional via
            // FIntrusiveUnsetOptionalState (see Misc/Optional.h ::IsSet) — the
            // "set" flag is stored *inside* T's normal fields rather than as a
            // trailing byte. Reading bIsSet at field+sizeof(T) lands on the
            // next UPROPERTY's memory and produces both false positives (e.g.
            // OptionalText hex all zeros yet flagged set) and false negatives
            // depending on neighbour layout.
            //
            //   FString  : Data backing TArray uses Max == -1 as sentinel
            //              (UnrealString.h.inl line ~212). Layout
            //              { TCHAR* Data(8B), int32 Num(4B), int32 Max(4B) }
            //              → check int32 at field+12.
            //   FName    : ComparisonIndex == 0xFFFFFFFF when unset
            //              (NameTypes.h line ~76). Layout
            //              { uint32 ComparisonIndex(4B), uint32 Number(4B) }
            //              → check uint32 at field+0.
            //   FText    : TextData (TSharedPtr-shaped pointer) == nullptr
            //              when unset (Internationalization/Text.h line ~837).
            //              Layout starts with the pointer → check uintptr at
            //              field+0.
            const bool isStrInner   = (innerTn == "StrProperty");
            const bool isNameInner  = (innerTn == "NameProperty");
            const bool isTextInner  = (innerTn == "TextProperty");

            bool isSet = false;

            if (isObjectLike) {
                uintptr_t ptr = 0;
                Macht::ReadSafe(fieldAddr, ptr);
                if (ptr) {
                    isSet = true;
                    fv.ptrValue = ptr;
                    fv.ptrName  = GetName(ptr);
                    uintptr_t cls = GetClass(ptr);
                    if (cls) {
                        fv.ptrClassName = GetName(cls);
                        fv.ptrClassAddr = cls;
                    }
                }
            } else if (isWeakLike) {
                // Embedded FWeakObjectPtr at field+0; unset sentinel is { 0, 0 }.
                int32_t objIdx = 0, serial = 0;
                Macht::ReadSafe(fieldAddr,     objIdx);
                Macht::ReadSafe(fieldAddr + 4, serial);
                isSet = (objIdx != 0 || serial != 0);
                uintptr_t resolved = ResolveWeakObjectPtr(objIdx, serial);
                if (resolved) {
                    fv.ptrValue = resolved;
                    fv.ptrName  = GetName(resolved);
                    uintptr_t cls = GetClass(resolved);
                    if (cls) {
                        fv.ptrClassName = GetName(cls);
                        fv.ptrClassAddr = cls;
                    }
                }
            } else if (isStrInner) {
                int32_t arrayMax = 0;
                Macht::ReadSafe(fieldAddr + 12, arrayMax);
                isSet = (arrayMax != -1);
                if (isSet) {
                    std::string s = ReadFString(fieldAddr, 0);
                    if (!s.empty()) fv.strValue = std::move(s);
                }
            } else if (isNameInner) {
                uint32_t compIdx = 0;
                Macht::ReadSafe(fieldAddr, compIdx);
                isSet = (compIdx != 0xFFFFFFFFu);
                if (isSet) {
                    std::string n = ReadFName(fieldAddr);
                    if (!n.empty()) fv.strValue = std::move(n);
                }
            } else if (isTextInner) {
                uintptr_t textData = 0;
                Macht::ReadSafe(fieldAddr, textData);
                isSet = (textData != 0);
                // FText display (audit #5 U11): decode via ReadFTextString, which follows
                // the ITextData* at FText+0 and scans it for the display FString — the SAME
                // decoder the plain TextProperty path uses. The old code read an inline
                // FString at FText+0x10 -- the uint32 Flags on UE<=5.3, and past the END of the
                // 16-byte FText on 5.4+ (the display string is NOT there either way), so it
                // produced garbage or "" for a real value.
                if (isSet) {
                    std::string s = ReadFTextString(fieldAddr);
                    if (!s.empty()) fv.strValue = std::move(s);
                }
            } else if (innerSize > 0) {
                // Scalar/struct (no intrusive specialization): trailing
                // bIsSet at field + innerSize.
                uint8_t bIsSet = 0;
                Macht::ReadSafe(fieldAddr + innerSize, bIsSet);
                isSet = (bIsSet != 0);
            }

            // Inner-struct surfacing: when the wrapped T is a StructProperty
            // and the value is set, expose the same {structClassAddr,
            // structDataAddr, structTypeName} triple that single-value
            // StructProperty fields produce. This drives Live Walker
            // drill-down and CE XML / CSX export through the standard
            // struct path — no additional field on LiveFieldValue required.
            //
            // Layout reminder: TOptional<T> for struct T is always
            // non-intrusive — { T value; uint8 bIsSet; } — so the value
            // lives at fieldAddr+0 (same as the bare struct case).
            //
            // Address Finder + Find Refs descend through OptionalProperty
            // mirroring StructProperty (see Aura.cpp::CollectContainersRecursive
            // and CollectRefMetaRecursive), so a UObject pointer buried
            // inside an Optional<Struct> still surfaces in the reverse scan.
            const bool isStructInner = (innerTn == "StructProperty");
            bool gotStructPreview = false;
            if (isStructInner && isSet && innerProp) {
                // Probe FStructProperty::Struct (UScriptStruct*) on the inner
                // FProperty. Mirrors the single-StructProperty handler's
                // probe so we self-correct mis-detected DynOff offsets.
                static const int kStructPtrProbeOffsets[] = {
                    0, 4, -4, 8, -8, 0x10, -0x10
                };
                for (int delta : kStructPtrProbeOffsets) {
                    int tryOff = DynOff::FSTRUCTPROP_STRUCT + delta;
                    if (tryOff < 0) continue;
                    uintptr_t candidate = 0;
                    if (!Macht::ReadSafe(innerProp + tryOff, candidate)
                        || !candidate) continue;
                    if (!Grimoire::IsUserspacePointer(candidate)) continue;
                    std::string sname = GetName(candidate);
                    if (sname.empty() || sname[0] < 0x20 || sname[0] >= 0x7F)
                        continue;
                    fv.structClassAddr = candidate;
                    fv.structTypeName  = sname;
                    fv.structDataAddr  = fieldAddr;
                    break;
                }

                // Inline preview from cached WalkClass (matches single-value
                // StructProperty path: bulk-read the struct, format the first
                // `previewLimit` scalar sub-fields).
                if (fv.structClassAddr && previewLimit > 0) {
                    // Memoized, and a HIT still deep-copies the flattened super chain —
                    // WalkClass returns by value. Sibling of the array-element preview
                    // above; same per-element loop, same cost (audit #5 U18).
                    ClassInfo si = WalkClass(fv.structClassAddr);
                    int32_t readSize = innerSize;
                    if (readSize <= 0 || readSize > 1024) {
                        readSize = si.PropertiesSize;
                        if (readSize <= 0 || readSize > 1024) readSize = 0;
                    }
                    std::vector<uint8_t> structBuf;
                    bool hasBuf = false;
                    if (readSize > 0) {
                        structBuf.resize(readSize, 0);
                        hasBuf = Macht::ReadBytesSafe(fieldAddr,
                                                     structBuf.data(),
                                                     readSize);
                    }

                    std::string preview;
                    int shown = 0;
                    const int kMaxScanFields = 20;
                    for (size_t idx = 0;
                         idx < si.Fields.size()
                         && static_cast<int>(idx) < kMaxScanFields; ++idx) {
                        const auto& sf = si.Fields[idx];
                        if (shown >= previewLimit) {
                            preview += ", ...";
                            break;
                        }
                        int32_t sfSize = sf.Size;
                        int32_t expected = InferScalarSize(sf.TypeName);
                        if (expected > 0 && sfSize != expected)
                            sfSize = expected;
                        if (!hasBuf || sf.Offset < 0
                            || sf.Offset + sfSize > readSize) continue;
                        const uint8_t* p = structBuf.data() + sf.Offset;
                        std::string val;
                        if (sf.TypeName == "FloatProperty" && sfSize == 4) {
                            float v; memcpy(&v, p, 4);
                            val = FormatPreviewNumber(v);
                        } else if (sf.TypeName == "DoubleProperty"
                                   && sfSize == 8) {
                            double v; memcpy(&v, p, 8);
                            val = FormatPreviewNumber(v);
                        } else if (sf.TypeName == "IntProperty"
                                   && sfSize == 4) {
                            int32_t v; memcpy(&v, p, 4);
                            val = std::to_string(v);
                        } else if (sf.TypeName == "BoolProperty") {
                            val = p[0] ? "true" : "false";
                        } else if (sf.TypeName == "ByteProperty"
                                   || sf.TypeName == "Int8Property") {
                            val = std::to_string(p[0]);
                        } else if (sf.TypeName == "NameProperty"
                                   && sfSize >= 4) {
                            val = DecodeFNameBytes(p, sfSize);   // Number included (U8)
                            if (val.empty()) val = "None";
                        } else if ((sf.TypeName == "ObjectProperty"
                                    || sf.TypeName == "ClassProperty")
                                   && sfSize >= 8) {
                            uintptr_t ptr; memcpy(&ptr, p, 8);
                            val = ptr ? GetName(ptr) : "null";
                        } else {
                            continue;
                        }
                        if (!preview.empty()) preview += ", ";
                        preview += sf.Name + "=" + val;
                        ++shown;
                    }
                    if (!preview.empty()) {
                        fv.typedValue = "{" + preview + "}";
                        gotStructPreview = true;
                    }
                }
            }

            // Build display string.
            if (!isSet) {
                fv.typedValue = "(unset)";
            } else if (isObjectLike || isWeakLike) {
                if (!fv.ptrName.empty()) {
                    fv.typedValue = fv.ptrClassName.empty()
                        ? fv.ptrName
                        : fv.ptrName + " (" + fv.ptrClassName + ")";
                } else if (isWeakLike) {
                    fv.typedValue = "(stale)";
                } else {
                    fv.typedValue = "(set)";
                }
            } else if (gotStructPreview) {
                // Struct preview already populated fv.typedValue above.
            } else if (isStructInner) {
                // Struct inner but no scalar sub-fields previewable — at
                // least confirm the wrapper type rather than rendering
                // garbage via InterpretValue("StructProperty", ...).
                fv.typedValue = fv.structTypeName.empty()
                    ? "(set)"
                    : "{" + fv.structTypeName + "}";
            } else if (isStrInner || isTextInner) {
                // FString / FText: surface the resolved contents in quotes,
                // matching the bare StrProperty / TextProperty single-value
                // display. Empty contents stay quoted as "" so the user sees
                // "set but empty" rather than mis-reading as "(set)".
                fv.typedValue = "\"" + fv.strValue + "\"";
            } else if (isNameInner) {
                fv.typedValue = fv.strValue.empty() ? "(set)" : fv.strValue;
            } else if (innerSize > 0) {
                std::vector<uint8_t> buf(innerSize, 0);
                if (Macht::ReadBytesSafe(fieldAddr, buf.data(), innerSize)) {
                    std::string interp = InterpretValue(innerTn, buf.data(), innerSize);
                    fv.typedValue = interp.empty() ? "(set)" : interp;
                } else {
                    fv.typedValue = "(set)";
                }
            } else {
                fv.typedValue = "(set)";
            }

            // Hex over reported size (defensive cap; struct inners may push
            // sizeof(TOptional<T>) above the 64B scalar cap, so allow up to
            // 256B when we know the inner is a sized struct).
            int32_t hexCap = isStructInner ? 256 : 64;
            int32_t showBytes = (fi.Size > 0 && fi.Size <= hexCap) ? fi.Size : 0;
            if (showBytes > 0) {
                std::vector<uint8_t> rawBuf(showBytes, 0);
                if (Macht::ReadBytesSafe(fieldAddr, rawBuf.data(), showBytes)) {
                    std::string hex;
                    hex.reserve(showBytes * 2);
                    for (auto b : rawBuf) {
                        char hx[3];
                        snprintf(hx, sizeof(hx), "%02X", b);
                        hex += hx;
                    }
                    fv.hexValue = hex;
                }
            }

            result.fields.push_back(std::move(fv));
            continue;
        }

        // Handle MulticastSparseDelegateProperty:
        // Field stores only `FSparseDelegate { uint8 bIsBound; }` — actual
        // FScriptDelegate bindings live in CoreUObject's static
        //   FSparseDelegateStorage::SparseDelegates :
        //     TMap<UObjectBase*, TMap<FName, TSharedPtr<FMulticastScriptDelegate>>>
        //
        // When the AOB resolver finds the static (Genau::FindSparseDelegateStorage),
        // we walk it via Aura::WalkSparseDelegateBindings and surface the
        // bindings using the same implicit-DelegateProperty-array layout as
        // MulticastInline/MulticastDelegate so drill-down + CE XML / CSX export
        // work uniformly. Falls back to the bound-flag-only string when:
        //   - bIsBound = 0 (nothing to look up)
        //   - AOB scan failed (older / unsupported builds)
        //   - the live outer key does not look like a raw pointer (the walker's
        //     runtime shape probe; guards a hypothetical FObjectKey-keyed build)
        if (fi.TypeName == "MulticastSparseDelegateProperty") {
            uintptr_t fieldAddr = instanceAddr + fi.Offset;
            uint8_t bIsBound = 0;
            Macht::ReadSafe(fieldAddr, bIsBound);

            // Hex over the property's reported size (typically 1, sometimes
            // padded to 4 or 8). Cap defensively so a garbage size doesn't
            // make us read megabytes.
            int32_t showBytes = (fi.Size > 0 && fi.Size <= 16) ? fi.Size : 1;
            std::vector<uint8_t> rawBuf(showBytes, 0);
            if (Macht::ReadBytesSafe(fieldAddr, rawBuf.data(), showBytes)) {
                std::string hex;
                hex.reserve(showBytes * 2);
                for (auto b : rawBuf) {
                    char hx[3];
                    snprintf(hx, sizeof(hx), "%02X", b);
                    hex += hx;
                }
                fv.hexValue = hex;
            }

            if (!bIsBound) {
                fv.typedValue = "(sparse, unbound)";
                result.fields.push_back(std::move(fv));
                continue;
            }

            // Try to walk the storage. arrayLimit caps how many bindings we
            // load inline; the implicit array stays drillable past that limit
            // via the standard read_array_elements pipe path.
            Aura::SparseDelegateResult sr = Aura::WalkSparseDelegateBindings(
                instanceAddr, fi.Name, arrayLimit);

            if (!sr.resolved || !sr.supported || !sr.ownerFound || !sr.nameFound) {
                // Couldn't resolve — surface the original bound-flag string so
                // the user still knows the field is bound, just opaque.
                if (!sr.supported) {
                    fv.typedValue = "(sparse, bound — UE < 5.0 unsupported)";
                } else if (!sr.resolved) {
                    fv.typedValue = "(sparse, bound — FSparseDelegateStorage AOB not found)";
                } else if (!sr.ownerFound) {
                    fv.typedValue = "(sparse, bound — owner not in storage)";
                } else {
                    fv.typedValue = "(sparse, bound — function name not in storage)";
                }
                result.fields.push_back(std::move(fv));
                continue;
            }

            // Walker succeeded. Expose as implicit DelegateProperty array.
            int fnameSize = DynOff::SizeofFName();
            int32_t delegateElemSize = 8 + fnameSize;
            int32_t bindingCount = static_cast<int32_t>(sr.bindings.size());

            fv.arrayCount = bindingCount;
            fv.arrayInnerType = "DelegateProperty";
            fv.arrayElemSize = delegateElemSize;
            // arrayDataAddr left at 0: bindings live in a TArray inside
            // FMulticastScriptDelegate inside a TSharedPtr inside the inner
            // TMap value — not a simple field offset, so read_array_elements
            // can't re-fetch them. Inline arrayElements is the source of truth.

            std::vector<std::string> previewNames;
            for (const auto& b : sr.bindings) {
                LiveFieldValue::ArrayElement elem;
                elem.index = static_cast<int32_t>(fv.arrayElements.size());
                if (b.targetObj) {
                    elem.ptrAddr      = b.targetObj;
                    elem.ptrName      = b.targetName;
                    elem.ptrClassName = b.targetClassName;
                }
                if (b.targetObj && !b.functionName.empty()) {
                    elem.value = (b.targetName.empty() ? std::string("?") : b.targetName)
                        + "::" + b.functionName;
                    if (previewNames.size() < 8) previewNames.push_back(elem.value);
                } else if (!b.functionName.empty()) {
                    elem.value = "(stale)::" + b.functionName;
                    if (previewNames.size() < 8) previewNames.push_back(elem.value);
                } else if (b.objectIndex > 0) {
                    elem.value = "(stale)";
                } else {
                    elem.value = "(unbound)";
                }
                fv.arrayElements.push_back(std::move(elem));
            }

            std::string display;
            if (bindingCount == 0) {
                display = "(0 bindings, sparse)";
            } else {
                display = "(" + std::to_string(bindingCount)
                    + " sparse binding" + (bindingCount > 1 ? "s" : "") + ")";
                if (!previewNames.empty()) {
                    display += " [";
                    for (size_t i = 0; i < previewNames.size(); ++i) {
                        if (i > 0) display += ", ";
                        display += previewNames[i];
                    }
                    if (bindingCount > static_cast<int32_t>(previewNames.size())) display += ", ...";
                    display += "]";
                }
            }
            fv.typedValue = display;

            result.fields.push_back(std::move(fv));
            continue;
        }

        // Handle MulticastInlineDelegateProperty / MulticastDelegateProperty:
        // FMulticastScriptDelegate = { TArray<FScriptDelegate> InvocationList (16B) }
        // FScriptDelegate = { FWeakObjectPtr(8B), FName(8/16B) }
        //
        // Exposes the multicast as an implicit DelegateProperty array so the UI
        // can drill in and CE XML / CSX can emit the bindings. Layout matches
        // Phase J (TArray<FScriptDelegate>) — Offsets=[0] derefs InvocationList.
        if (fi.TypeName == "MulticastInlineDelegateProperty" ||
            fi.TypeName == "MulticastDelegateProperty") {
            int fnameSize = DynOff::SizeofFName();
            int32_t delegateElemSize = 8 + fnameSize;  // FWeakObjectPtr + FName
            uintptr_t fieldAddr = instanceAddr + fi.Offset;

            // Read TArray<FScriptDelegate> header
            uintptr_t data = 0;
            int32_t count = 0;
            Macht::ReadSafe(fieldAddr, data);
            Macht::ReadSafe(fieldAddr + 8, count);

            if (count < 0 || count > 4096) count = 0;  // Sanity clamp (was 256)

            // Expose as implicit DelegateProperty array — drives drill-down,
            // CE XML / CSX export, and IsContainerNavigable in the UI.
            fv.arrayCount = count;
            fv.arrayInnerType = "DelegateProperty";
            fv.arrayElemSize = delegateElemSize;
            fv.arrayDataAddr = data;

            // Read every binding (up to arrayLimit) into ArrayElements.
            // Each element: ptrAddr/ptrName/ptrClassName + display "Target::Func".
            int32_t readMax = (std::min)(count, arrayLimit);
            std::vector<std::string> previewNames;
            for (int32_t i = 0; data && i < readMax; ++i) {
                uintptr_t elemAddr = data + static_cast<int64_t>(i) * delegateElemSize;

                LiveFieldValue::ArrayElement elem;
                elem.index = i;

                // Hex of full FScriptDelegate
                std::vector<uint8_t> rawBuf(delegateElemSize, 0);
                if (Macht::ReadBytesSafe(elemAddr, rawBuf.data(), delegateElemSize)) {
                    std::string hex;
                    hex.reserve(delegateElemSize * 2);
                    for (auto b : rawBuf) {
                        char hx[3];
                        snprintf(hx, sizeof(hx), "%02X", b);
                        hex += hx;
                    }
                    elem.hex = std::move(hex);
                }

                int32_t objIdx = 0, serial = 0;
                Macht::ReadSafe(elemAddr,     objIdx);
                Macht::ReadSafe(elemAddr + 4, serial);
                uintptr_t target = ResolveWeakObjectPtr(objIdx, serial);
                std::string funcName = ReadFName(elemAddr + 8);

                if (target) {
                    elem.ptrAddr = target;
                    elem.ptrName = GetName(target);
                    uintptr_t cls = GetClass(target);
                    if (cls) elem.ptrClassName = GetName(cls);
                }

                if (target && !funcName.empty()) {
                    elem.value = (elem.ptrName.empty() ? std::string("?") : elem.ptrName)
                        + "::" + funcName;
                    if (previewNames.size() < 8) previewNames.push_back(elem.value);
                } else if (!funcName.empty()) {
                    elem.value = "(stale)::" + funcName;
                    if (previewNames.size() < 8) previewNames.push_back(elem.value);
                } else if (objIdx > 0) {
                    elem.value = "(stale)";
                } else {
                    elem.value = "(unbound)";
                }

                fv.arrayElements.push_back(std::move(elem));
            }

            // Build summary string from preview names
            std::string display;
            if (count == 0) {
                display = "(0 bindings)";
            } else {
                display = "(" + std::to_string(count)
                    + " binding" + (count > 1 ? "s" : "") + ")";
                if (!previewNames.empty()) {
                    display += " [";
                    for (size_t i = 0; i < previewNames.size(); ++i) {
                        if (i > 0) display += ", ";
                        display += previewNames[i];
                    }
                    if (count > static_cast<int32_t>(previewNames.size())) display += ", ...";
                    display += "]";
                }
            }

            fv.typedValue = display;

            // Hex: TArray header (Data ptr + Count)
            char buf[48];
            snprintf(buf, sizeof(buf), "%016llX %08X",
                static_cast<unsigned long long>(data), count);
            fv.hexValue = buf;

            result.fields.push_back(std::move(fv));
            continue;
        }

        // Scalar or struct: read raw bytes and interpret.
        // Validate fi.Size against known type sizes: FPROPERTY_ELEMSIZE often returns
        // garbage (e.g., 1073742336) for fields inside certain UScriptStruct layouts.
        int32_t readSize = fi.Size;
        int32_t expectedSize = InferScalarSize(fi.TypeName);
        if (expectedSize > 0) {
            if (readSize != expectedSize) {
                readSize = expectedSize;
                fv.size = readSize;
            }
        } else if (readSize <= 0 || readSize > 256) {
            // Unknown type with zero/garbage size — skip
            readSize = 0;
        }
        if (readSize > 0 && readSize <= 256) {
            std::vector<uint8_t> buf(readSize, 0);
            if (Macht::ReadBytesSafe(instanceAddr + fi.Offset, buf.data(), readSize)) {
                // Build hex string
                std::string hex;
                hex.reserve(readSize * 2);
                for (auto b : buf) {
                    char hx[3];
                    snprintf(hx, sizeof(hx), "%02X", b);
                    hex += hx;
                }
                fv.hexValue = hex;
                fv.typedValue = InterpretValue(fi.TypeName, buf.data(), readSize);
            }
        }

        result.fields.push_back(std::move(fv));
    }

    // --- Guess What: fill gaps between known fields ---
    // Note: works with 0-field classes too — the entire [headerEnd, propsSize] becomes one gap.
    // The gap pass is bounded SEPARATELY from the plausibility gate above: a class
    // can be entirely real and still be too large to sweep byte-wise, and conflating
    // the two is what made a live 3.6 MB USaveGame report as recycled. Say the skip
    // out loud rather than silently returning no guessed rows. (SANEPROPS-2026-08-26)
    //
    // Gated on `fillGaps` as well, so a default walk (fill_gaps defaults to false)
    // never claims to have skipped a pass nobody asked for.
    if (fillGaps && !result.isDefinition && ci.PropertiesSize > kMaxGapFillBytes) {
        result.gapFillSkipped = true;
        Sein::Warn("WALK:guess",
            "WalkInstance: gap-fill SKIPPED for 0x%llx — PropertiesSize=%d exceeds the "
            "%d-byte gap-fill work cap; the reflected fields ARE complete",
            (unsigned long long)instanceAddr, ci.PropertiesSize, kMaxGapFillBytes);
    }
    if (fillGaps && !result.isDefinition &&
        ci.PropertiesSize > 0 && ci.PropertiesSize <= kMaxGapFillBytes) {
        // Determine scan boundaries
        int32_t headerEnd = isRawStruct ? 0 : (DynOff::UOBJECT_OUTER + 8);
        int32_t scanEnd = ci.PropertiesSize;

        // Collect known field intervals from the rendered reflected fields and
        // compute the complement within [headerEnd, scanEnd) via the shared
        // Ubel::ComputeHoles helper. ComputeHoles clamps each interval to the
        // window before merging — clamping the START to headerEnd is what makes
        // the leading region [headerEnd, firstField) survive as a gap (the
        // user-visible "gap before the first field never gets guessed rows" bug,
        // fixed in commit 75ea723), and everything below headerEnd
        // (vtable/flags/class/name/outer) stays excluded so the well-known
        // UObject header is never turned into guessed rows. Clamping the END to
        // scanEnd guards a field with a garbage-huge size from eating trailing
        // gaps. (This per-instance pass intervals on the rendered LiveFieldValue
        // sizes — element-size, not Size*ArrayDim — so its output is unchanged by
        // the new ArrayDim field; the ArrayDim-aware footprint is used only by the
        // class-level Ubel::ComputeClassHoles consumed by the Native-C scan.)
        std::vector<Ubel::Interval> occupied;
        occupied.reserve(result.fields.size());
        for (const auto& f : result.fields) {
            occupied.push_back({ f.offset, f.offset + (f.size > 0 ? f.size : 1) });
        }
        std::vector<Ubel::Interval> gaps = Ubel::ComputeHoles(occupied, headerEnd, scanEnd);

        // Diagnostic (Guess?-only): dump the reflected field footprints + the
        // computed raw gaps so a user comparing against a CE Structure Dissect can
        // see EXACTLY which property covers a given region. A TMap/TSet's inline
        // allocator bytes (data-ptr / num / max / hash) show as raw ints in CE but
        // are correctly "occupied" here by the single Map/Set property, so no
        // guessed rows are emitted there. One compact line per walk; only when
        // gap-filling is on (the Live Walker "Guess?" toggle). Sorted by offset,
        // field count capped to bound the line length.
        {
            std::vector<const LiveFieldValue*> sorted;
            sorted.reserve(result.fields.size());
            for (const auto& f : result.fields)
                if (!f.guessed) sorted.push_back(&f);
            std::sort(sorted.begin(), sorted.end(),
                [](const LiveFieldValue* a, const LiveFieldValue* b) { return a->offset < b->offset; });
            std::string fieldStr;
            fieldStr.reserve(sorted.size() * 24 + 64);
            char lb[128];
            size_t emitted = 0;
            for (const auto* f : sorted) {
                if (emitted++ >= 800) { fieldStr += "...(truncated) "; break; }
                snprintf(lb, sizeof(lb), "0x%X=%d(%s) ",
                         static_cast<unsigned>(f->offset), f->size, f->typeName.c_str());
                fieldStr += lb;
            }
            std::string gapStr;
            for (const auto& g : gaps) {
                snprintf(lb, sizeof(lb), "[0x%X,0x%X) ",
                         static_cast<unsigned>(g.start), static_cast<unsigned>(g.end));
                gapStr += lb;
            }
            Sein::Info("WALK:guess",
                "GuessGaps '%s' (%s) header=0x%X end=0x%X fields=%zu gaps=%zu | FIELDS: %s| GAPS: %s",
                result.name.c_str(), result.className.c_str(),
                static_cast<unsigned>(headerEnd), static_cast<unsigned>(scanEnd),
                sorted.size(), gaps.size(), fieldStr.c_str(), gapStr.c_str());
        }

        // Fill each gap with guessed types
        size_t beforeCount = result.fields.size();
        for (const auto& gap : gaps) {
            GuessGapTypes(instanceAddr, gap.start, gap.end, result.fields);
        }

        // Sort all fields by offset
        if (result.fields.size() > beforeCount) {
            std::sort(result.fields.begin(), result.fields.end(),
                [](const LiveFieldValue& a, const LiveFieldValue& b) {
                    return a.offset < b.offset;
                });
        }
    }

    auto loopEnd = std::chrono::steady_clock::now();
    auto totalMs = std::chrono::duration_cast<std::chrono::milliseconds>(loopEnd - loopStart).count();

    Sein::Info("WALK:perf", "WalkInstance '%s' (%s): %zu fields in %lldms (WalkClass=%lldms) "
        "| Obj:%d/%lldms Struct:%d/%lldms Array:%d/%lldms Scalar:%d/%lldms",
        result.name.c_str(), result.className.c_str(), result.fields.size(), totalMs, walkClassMs,
        nObj, tObj, nStruct, tStruct, nArray, tArray, nScalar, tScalar);

    // A class with many fields and ZERO object/struct/array properties does not exist in a
    // real UE build — a UGameEngine alone has hundreds. When this fires, property TYPE
    // resolution is broken (FFieldClass::Name at the wrong offset), every field falls into
    // the classifier's catch-all Scalar branch, and the Live Walker shows no drill-down.
    // This is the shape UE 5.8 presented before FFIELDCLASS_NAME became a probed offset:
    // 279/279 Scalar on GameEngine, 29/29 on Level. Loud beats silent — the failure is
    // otherwise invisible except as "the tree looks wrong".
    if (nObj == 0 && nStruct == 0 && nArray == 0 &&
        static_cast<size_t>(nScalar) == result.fields.size() && result.fields.size() > 8) {
        Sein::Warn("WALK:perf",
            "All %zu fields of '%s' typed Scalar with no Obj/Struct/Array — property type "
            "resolution is probably broken (FFieldClass::Name=+0x%02X). Expect no drill-down.",
            result.fields.size(), result.className.c_str(), DynOff::FFIELDCLASS_NAME);
    }

    return result;
}

// ============================================================
// ResolvePropertyPreviews — fill PropertyMatch.preview with
// live values from representative instances (Phase 2 of search).
// ============================================================
void ResolvePropertyPreviews(
    std::vector<Aura::PropertyMatch>& matches,
    const std::unordered_map<uintptr_t, uintptr_t>& instanceMap)
{
    for (auto& m : matches) {
        auto it = instanceMap.find(m.classAddr);
        if (it == instanceMap.end()) continue;

        uintptr_t inst = it->second;
        if (!inst) continue;

        const std::string& t = m.propType;
        int32_t off = m.propOffset;
        int32_t sz  = m.propSize;

        // --- Scalar primitives: read bytes + InterpretValue ---
        if (t == "FloatProperty" || t == "DoubleProperty" ||
            t == "IntProperty" || t == "UInt32Property" ||
            t == "Int64Property" || t == "UInt64Property" ||
            t == "Int16Property" || t == "UInt16Property" ||
            t == "ByteProperty" || t == "Int8Property" ||
            t == "NameProperty")
        {
            if (sz > 0 && sz <= 64) {
                uint8_t buf[64] = {};
                if (Macht::ReadBytesSafe(inst + off, buf, sz)) {
                    m.preview = InterpretValue(t, buf, sz);
                }
            }
            continue;
        }

        // --- BoolProperty: bitfield-aware ---
        if (t == "BoolProperty") {
            uint8_t rawByte = 0;
            int readOff = off + m.boolByteOffset;
            if (Macht::ReadSafe(inst + readOff, rawByte)) {
                if (m.boolFieldMask != 0) {
                    m.preview = (rawByte & m.boolFieldMask) ? "true" : "false";
                } else {
                    m.preview = rawByte ? "true" : "false";
                }
            }
            continue;
        }

        // --- StrProperty / Utf8StrProperty / AnsiStrProperty: read string ---
        if (t == "StrProperty" || t == "Utf8StrProperty" || t == "AnsiStrProperty") {
            std::string s = (t == "StrProperty") ? ReadFString(inst, off)
                                                 : ReadFUtf8String(inst, off);
            if (s.empty()) {
                m.preview = "(empty)";
            } else {
                // Truncate long strings — on a CHARACTER boundary. `s.resize(50)` split
                // multi-byte sequences (2 CJK strings in 3), and nlohmann's strict dump()
                // then threw on the invalid UTF-8, turning the entire search_properties
                // response into {"error":...} — zero rows for a search that matched.
                m.preview = "\"" + Utf8Helpers::TruncateUtf8(s, 50) + "\"";
            }
            continue;
        }

        // --- Verse VM property types: not a readable scalar/string ---
        if (t == "VValueProperty" || t == "VRestValueProperty" ||
            t == "VCellProperty"  || t == "VerseStringProperty") {
            m.preview = "(Verse)";
            continue;
        }

        // --- EnumProperty: read raw int + resolve enum name ---
        if (t == "EnumProperty") {
            int64_t rawVal = 0;
            if (sz == 1)      { uint8_t v = 0; Macht::ReadSafe(inst + off, v); rawVal = v; }
            else if (sz == 2) { int16_t v = 0; Macht::ReadSafe(inst + off, v); rawVal = v; }
            else if (sz == 4) { int32_t v = 0; Macht::ReadSafe(inst + off, v); rawVal = v; }
            else if (sz == 8) { int64_t v = 0; Macht::ReadSafe(inst + off, v); rawVal = v; }

            if (m.enumAddr) {
                std::string enumName = ResolveEnumValue(m.enumAddr, rawVal);
                if (!enumName.empty()) {
                    m.preview = enumName;
                    continue;
                }
            }
            m.preview = std::to_string(rawVal);
            continue;
        }

        // --- WeakObjectProperty / LazyObjectProperty: FWeakObjectPtr at +0x00 ---
        // Audit #5 U13. Both used to sit in the raw-pointer branch below, which read
        // 8 bytes as a UObject* with NO size gate at all — so an FWeakObjectPtr
        // { int32 ObjectIndex; int32 SerialNumber } was published as the address
        // Serial<<32|Index and printed as a plausible-looking "0x…". TLazyObjectPtr
        // begins with the same FWeakObjectPtr (then the envelope: +0x08 Tag and the FGuid at
        // +0x0C up to 5.2, the FGuid straight at +0x08 from 5.3 — NOT a fixed +0x10, which is
        // the model audit A1 deleted), so it resolves
        // identically; its FGuid is the honest fallback when nothing is loaded, which
        // is what ReadLazyObjectArrayElements already displays.
        if (t == "WeakObjectProperty" || t == "LazyObjectProperty") {
            int32_t objIdx = 0, serial = 0;
            Macht::ReadSafe(inst + off,     objIdx);
            Macht::ReadSafe(inst + off + 4, serial);
            uintptr_t resolved = ResolveWeakObjectPtr(objIdx, serial);
            if (resolved) {
                std::string name = GetName(resolved);
                m.preview = name.empty() ? "(loaded)" : name;
            } else if (t == "LazyObjectProperty") {
                const int gOff = LazyGuidOffset(sz);
                uint32_t ga = 0, gb = 0, gc = 0, gd = 0;
                Macht::ReadSafe(inst + off + gOff,      ga);
                Macht::ReadSafe(inst + off + gOff + 4,  gb);
                Macht::ReadSafe(inst + off + gOff + 8,  gc);
                Macht::ReadSafe(inst + off + gOff + 12, gd);
                char gs[48];
                snprintf(gs, sizeof(gs), "{%08X-%08X-%08X-%08X}", ga, gb, gc, gd);
                m.preview = gs;
            } else {
                // Same wording as ReadWeakObjectArrayElements: a live index whose
                // serial no longer matches is a DEAD reference, not a null one.
                m.preview = (objIdx > 0) ? "null (stale)" : "null";
            }
            continue;
        }

        // --- SoftObjectProperty / SoftClassProperty: FSoftObjectPath asset path ---
        // TSoftObjectPtr = FWeakObjectPtr(8) [+ Tag(4) + pad(4) on UE ≤ 5.2] +
        // FSoftObjectPath, so the readable value is the asset path at the measured
        // envelope (+0x10 up to 5.2, +0x08 from 5.3), never a pointer.
        // SoftClassProperty was read as a raw pointer here and SoftObjectProperty had
        // no branch at all (so it silently got NO preview) — audit #5 U13. Display
        // order matches WalkInstance's own soft-pointer handler: path, then the
        // resolved target when the asset happens to be loaded, then "(none)".
        if (t == "SoftObjectProperty" || t == "SoftClassProperty") {
            std::string assetPath = ReadSoftObjectPath(inst + off + SoftPathOffset(sz));
            if (!assetPath.empty()) {
                m.preview = assetPath;
                continue;
            }
            int32_t objIdx = 0, serial = 0;
            Macht::ReadSafe(inst + off,     objIdx);
            Macht::ReadSafe(inst + off + 4, serial);
            uintptr_t target = ResolveWeakObjectPtr(objIdx, serial);
            std::string name = target ? GetName(target) : std::string();
            m.preview = name.empty() ? "(none)" : name;
            continue;
        }

        // --- ObjectProperty / ClassProperty / InterfaceProperty: raw UObject* at +0 ---
        // InterfaceProperty had no branch anywhere in this function, so it got no
        // preview at all (audit #5 U13). FScriptInterface is { UObject* +0x00;
        // void* +0x08 }, so its first 8 bytes ARE a genuine object pointer — the same
        // reason CeXmlExportService.IsRawObjectPtrSlot includes it and the weak-like
        // types above it does not.
        if (t == "ObjectProperty" || t == "ClassProperty" || t == "InterfaceProperty") {
            uintptr_t ptr = 0;
            Macht::ReadSafe(inst + off, ptr);
            if (!ptr) {
                m.preview = "null";
            } else {
                std::string name = GetName(ptr);
                if (!name.empty()) {
                    m.preview = name;
                } else {
                    char buf[24];
                    snprintf(buf, sizeof(buf), "0x%llX", static_cast<unsigned long long>(ptr));
                    m.preview = buf;
                }
            }
            continue;
        }

        // --- StructProperty: try float hint, fallback to type name ---
        if (t == "StructProperty") {
            if (sz > 0 && sz <= 256) {
                std::vector<uint8_t> buf(sz, 0);
                if (Macht::ReadBytesSafe(inst + off, buf.data(), sz)) {
                    std::string hint = InterpretValue(t, buf.data(), sz);
                    if (!hint.empty()) {
                        m.preview = hint;
                        continue;
                    }
                }
            }
            // Fallback: show struct type name
            if (!m.structType.empty()) {
                m.preview = "{" + m.structType + "}";
            }
            continue;
        }

        // --- ArrayProperty: read TArray count ---
        if (t == "ArrayProperty") {
            Macht::TArrayView arr;
            if (Macht::ReadTArray(inst + off, arr) && arr.Count >= 0) {
                m.preview = "[" + std::to_string(arr.Count) + " x " +
                            (m.innerType.empty() ? "?" : m.innerType) + "]";
            }
            continue;
        }

        // --- MapProperty: read FScriptMap count ---
        if (t == "MapProperty") {
            // FScriptMap layout: FScriptSet { FHashAllocator {SparseArray {Data(8) Count(4) ...}}}
            // The count is at offset +8 within the FScriptMap (FScriptSet.Elements.Count)
            int32_t count = 0;
            Macht::ReadSafe(inst + off + 8, count);
            if (count >= 0 && count < 1000000) {
                std::string keyStr = m.keyType.empty() ? "?" : m.keyType;
                std::string valStr = m.valueType.empty() ? "?" : m.valueType;
                // Shorten type names: remove "Property" suffix
                auto shorten = [](const std::string& s) -> std::string {
                    const std::string suffix = "Property";
                    if (s.size() > suffix.size() &&
                        s.compare(s.size() - suffix.size(), suffix.size(), suffix) == 0)
                        return s.substr(0, s.size() - suffix.size());
                    return s;
                };
                m.preview = "{Map: " + std::to_string(count) + ", " +
                            shorten(keyStr) + "\xe2\x86\x92" + shorten(valStr) + "}";
            }
            continue;
        }

        // --- SetProperty: read count ---
        if (t == "SetProperty") {
            int32_t count = 0;
            Macht::ReadSafe(inst + off + 8, count);
            if (count >= 0 && count < 1000000) {
                m.preview = "{Set: " + std::to_string(count) + "}";
            }
            continue;
        }

        // --- TextProperty: just show type ---
        if (t == "TextProperty") {
            m.preview = "(FText)";
            continue;
        }
    }
}

// ============================================================
// DataTable Row Browsing
// ============================================================

// Probe for RowMap offset within a DataTable instance.
// RowMap (TMap<FName, uint8*>) is NOT reflected — must scan memory.
// Returns the byte offset of the TSparseArray within the DataTable, or -1 if not found.
static int32_t ProbeRowMapOffset(uintptr_t dataTableAddr, const ClassInfo& ci) {
    // The scan is bounded BY THE OBJECT, and it covers the whole of it.
    //
    // Two things were wrong here until 2026-08-23 [DTROWMAP-2026-08-23], and
    // together they made this report a NEIGHBOURING table's rows as this table's:
    //
    //  (1) It started at the end of the reflected fields and only went FORWARD.
    //      RowMap is declared immediately after RowStruct, so in a COOKED build --
    //      where the WITH_EDITORONLY_DATA members between them are stripped -- it
    //      lands at +0x30, in the hole between RowStruct (+0x28..0x30) and the
    //      bools (+0x80). Measured on UE 5.4: endReflected = 152, real RowMap = 48.
    //      The target sat BEHIND the scan start, so no amount of forward range
    //      could reach it. This was not a tuning problem.
    //
    //  (2) It ran +0..+256 from there with no bound tied to the object.
    //      UDataTable's PropertiesSize is 176, so 232 of its 257 candidate offsets
    //      were OUTSIDE the object. DataTables get allocated near each other, so
    //      the overrun lands in another UDataTable and validates on a real RowMap:
    //      real FName row names, real row pointers, a plausible count. Not an
    //      error -- a confident wrong answer. Proven with ReadProcessMemory, with
    //      this DLL out of the loop: Table_Big+240 and Table_Small+48 were the
    //      same address and served the same eight rows for a 100-row table.
    //
    // So: walk every 8-aligned offset of THIS class's own storage and never past
    // PropertiesSize. Offsets a reflected field already claims are tried LAST --
    // RowMap is by definition not one of them, but preferring the holes keeps a
    // reflected TMap/TSet on some other class from being mistaken for it, and the
    // fallback means a wrong Size on a reflected field cannot hide the real thing.
    // The validation below is unchanged: it was never the problem, and it is what
    // keeps a whole-object scan honest.
    constexpr int32_t kSparseArrayBytes = 0x38;   // Macht::ReadTSparseArray reads +0x00..+0x37

    int32_t scanBegin = (ci.SuperPropertiesSize > 0 ? ci.SuperPropertiesSize : 0);
    scanBegin = (scanBegin + 7) & ~7;
    // Bounded by the gap-fill work cap as well as by the object. Raising the
    // plausibility ceiling to 64 MB makes a mis-derived USTRUCT_PROPSSIZE reach this
    // double loop, which is O(PropertiesSize/8 x fields) with a TSparseArray read per
    // step. A real UDataTable's PropertiesSize is ~176 bytes, so 1 MB is ~6000x
    // headroom and this clamp is unobservable on any real table. (SANEPROPS-2026-08-26)
    const int32_t propsCap = (ci.PropertiesSize < kMaxGapFillBytes)
                                 ? ci.PropertiesSize : kMaxGapFillBytes;
    const int32_t scanEnd = propsCap - kSparseArrayBytes;
    if (scanEnd < scanBegin) {
        Sein::Warn("WALK", "ProbeRowMapOffset: no room to scan (propsSize=%d, "
                   "superPropsSize=%d) — a UDataTable smaller than a TSparseArray "
                   "means the class layout is not what we think it is",
                   ci.PropertiesSize, ci.SuperPropertiesSize);
        return -1;
    }

    // True when a reflected field already owns this byte. Size 0 still claims one
    // byte: a bitfield bool reports Size 1, but a defensive 0 must not make the
    // range empty and silently mark the offset free.
    auto claimedByReflected = [&ci](int32_t off) {
        for (const auto& fi : ci.Fields) {
            const int32_t sz = fi.Size > 0 ? fi.Size : 1;
            if (off >= fi.Offset && off < fi.Offset + sz) return true;
        }
        return false;
    };

    int fnameSize = DynOff::FNameSlotIn8Aligned();
    int pairSize  = fnameSize + 8;  // FName + uint8*
    // alignof(TPair<FName, uint8*>) == 8 (the pointer). Both CPN states already
    // give an 8-aligned pair here, so this changes no value today — it is passed
    // so the site cannot silently drift if fnameSize or the value type changes.
    int stride    = Macht::ComputeSetElementStride(pairSize, 8);

    // Pass 0 = the holes between reflected fields (where a non-reflected member
    // must live); pass 1 = everything else still inside the object, as a fallback
    // so a wrong reflected Size cannot hide the real RowMap. Neither pass ever
    // leaves [scanBegin, scanEnd] — that bound is the whole point.
    for (int pass = 0; pass < 2; ++pass) {
    for (int32_t candidate = scanBegin; candidate <= scanEnd; candidate += 8) {
        const bool claimed = claimedByReflected(candidate);
        if ((pass == 0) == claimed)
            continue;                       // pass 0 wants holes, pass 1 wants the rest
        Macht::TSparseArrayView sa;
        if (!Macht::ReadTSparseArray(dataTableAddr + candidate, sa))
            continue;

        // Basic sanity
        if (sa.Data == 0 || sa.MaxIndex <= 0)
            continue;
        if (sa.NumFreeIndices < 0 || sa.NumFreeIndices > sa.MaxIndex)
            continue;
        int32_t count = sa.MaxIndex - sa.NumFreeIndices;
        if (count <= 0)
            continue;

        // Validate Data pointer range
        if (!Grimoire::IsUserspacePointer(sa.Data))
            continue;

        // Extra validation: read first allocated element
        bool validated = false;
        for (int32_t idx = 0; idx < sa.MaxIndex && idx < 32; ++idx) {
            if (!Macht::IsSparseIndexAllocated(sa, idx))
                continue;

            uintptr_t elemAddr = sa.Data + (idx * stride);

            // Read FName key
            int32_t compIndex = 0;
            if (!Macht::ReadSafe(elemAddr, compIndex))
                break;
            std::string keyName = Serie::GetString(compIndex);
            if (keyName.empty() || keyName == "None")
                break;  // Legit RowMap entries have non-None row names

            // Read uint8* value pointer
            uintptr_t rowPtr = 0;
            if (!Macht::ReadSafe(elemAddr + fnameSize, rowPtr))
                break;
            if (!Grimoire::IsUserspacePointer(rowPtr))
                break;

            validated = true;
            break;
        }

        if (validated) {
            Sein::Info("WALK", "ProbeRowMapOffset: found RowMap at DataTable+0x%X "
                         "(count=%d, stride=%d, pass=%s, scanned 0x%X..0x%X of a "
                         "0x%X-byte object)", candidate, count, stride,
                         pass == 0 ? "hole" : "claimed",
                         scanBegin, scanEnd, ci.PropertiesSize);
            return candidate;
        }
    }
    }

    Sein::Warn("WALK", "ProbeRowMapOffset: could not find RowMap in 0x%X..0x%X "
                 "(propsSize=0x%X). NOT widening the scan past the object: doing so "
                 "is how a NEIGHBOURING UDataTable's RowMap used to be served as this "
                 "one's [DTROWMAP-2026-08-23].",
                 scanBegin, scanEnd, ci.PropertiesSize);
    return -1;
}

DataTableWalkResult WalkDataTableRows(uintptr_t dataTableAddr, int32_t offset, int32_t limit) {
    DataTableWalkResult result;
    if (!dataTableAddr) {
        result.error = "null address";
        return result;
    }

    // Verify this is a DataTable
    uintptr_t classAddr = GetClass(dataTableAddr);
    std::string className = classAddr ? GetName(classAddr) : "";
    if (className != "DataTable") {
        result.error = "not a DataTable (class=" + className + ")";
        return result;
    }

    // Walk the DataTable's class to find RowStruct field
    const ClassInfo& ci = WalkClassEx(classAddr);
    if (ci.Fields.empty()) {
        result.error = "DataTable class has no reflected fields";
        return result;
    }

    // Find RowStruct ObjectProperty and read its pointer value
    uintptr_t rowStructAddr = 0;
    for (const auto& fi : ci.Fields) {
        if (fi.Name == "RowStruct" && (fi.TypeName == "ObjectProperty" || fi.TypeName == "ClassProperty")) {
            Macht::ReadSafe(dataTableAddr + fi.Offset, rowStructAddr);
            break;
        }
    }
    if (!rowStructAddr) {
        result.error = "RowStruct not found or null";
        return result;
    }

    result.rowStructAddr = rowStructAddr;
    result.rowStructName = GetName(rowStructAddr);

    // Get field layout from RowStruct
    const ClassInfo& rowCI = WalkClassEx(rowStructAddr);
    if (rowCI.Fields.empty()) {
        result.error = "RowStruct has no fields (name=" + result.rowStructName + ")";
        return result;
    }

    // Probe for RowMap offset
    int32_t rowMapOffset = ProbeRowMapOffset(dataTableAddr, ci);
    if (rowMapOffset < 0) {
        result.error = "RowMap not found by probing";
        return result;
    }
    result.rowMapOffset = rowMapOffset;

    // Read TSparseArray
    Macht::TSparseArrayView sa;
    if (!Macht::ReadTSparseArray(dataTableAddr + rowMapOffset, sa)) {
        result.error = "Failed to read TSparseArray at RowMap offset";
        return result;
    }

    int fnameSize = DynOff::FNameSlotIn8Aligned();
    int pairSize  = fnameSize + 8;
    // alignof(TPair<FName, uint8*>) == 8 (the pointer). Both CPN states already
    // give an 8-aligned pair here, so this changes no value today — it is passed
    // so the site cannot silently drift if fnameSize or the value type changes.
    int stride    = Macht::ComputeSetElementStride(pairSize, 8);

    result.fnameSize = fnameSize;
    result.stride    = stride;
    result.rowCount  = sa.MaxIndex - sa.NumFreeIndices;

    // Iterate rows
    int32_t read = 0;
    int32_t skipped = 0;
    for (int32_t idx = 0; idx < sa.MaxIndex && read < limit; ++idx) {
        if (!Macht::IsSparseIndexAllocated(sa, idx))
            continue;
        // Skip entries before 'offset'
        if (skipped < offset) { ++skipped; continue; }

        uintptr_t elemAddr = sa.Data + (idx * stride);

        DataTableRow row;
        row.sparseIndex = idx;

        // Read FName key
        row.rowName = ReadFName(elemAddr);
        if (row.rowName.empty()) row.rowName = "(unnamed)";

        // Read uint8* value pointer (row data address)
        uintptr_t rowPtr = 0;
        if (!Macht::ReadSafe(elemAddr + fnameSize, rowPtr) || !rowPtr) {
            row.rowName += " (null)";
            result.rows.push_back(std::move(row));
            ++read;
            continue;
        }
        row.rowDataAddr = rowPtr;

        // Bulk-read row data buffer (limited to 4KB)
        int32_t rowSize = rowCI.PropertiesSize;
        if (rowSize <= 0 || rowSize > 4096) rowSize = 256;  // fallback
        std::vector<uint8_t> rowBuf(rowSize, 0);
        bool rowBufOk = Macht::ReadBytesSafe(rowPtr, rowBuf.data(), rowSize);

        // Read fields using RowStruct layout
        for (const auto& fi : rowCI.Fields) {
            LiveFieldValue fv;
            fv.name     = fi.Name;
            fv.typeName = fi.TypeName;
            fv.offset   = fi.Offset;
            fv.size     = fi.Size;

            // Extended type info
            if (!fi.structType.empty())
                fv.structTypeName = fi.structType;

            int32_t readSize = fi.Size;
            int32_t expectedSize = InferScalarSize(fi.TypeName);
            if (expectedSize > 0) readSize = expectedSize;

            // Read from bulk buffer for scalar fields
            if (rowBufOk && fi.Offset >= 0 && fi.Offset + readSize <= rowSize) {
                const uint8_t* p = rowBuf.data() + fi.Offset;

                // Hex value
                std::string hex;
                hex.reserve(readSize * 2);
                for (int i = 0; i < readSize; ++i) {
                    char hx[3];
                    snprintf(hx, sizeof(hx), "%02X", p[i]);
                    hex += hx;
                }
                fv.hexValue = hex;

                // Typed value
                fv.typedValue = InterpretValue(fi.TypeName, p, readSize);

                // ObjectProperty: resolve pointer name
                if ((fi.TypeName == "ObjectProperty" || fi.TypeName == "ClassProperty") && readSize >= 8) {
                    uintptr_t ptr = 0;
                    memcpy(&ptr, p, 8);
                    if (ptr) {
                        fv.ptrValue = ptr;
                        fv.ptrName = GetName(ptr);
                        uintptr_t cls = GetClass(ptr);
                        if (cls) {
                            fv.ptrClassName = GetName(cls);
                            fv.ptrClassAddr = cls;
                        }
                    }
                }

                // StructProperty: set struct metadata for navigation
                if (fi.TypeName == "StructProperty" && !fi.structType.empty()) {
                    fv.structDataAddr = rowPtr + fi.Offset;
                    // Read UScriptStruct* from the FProperty
                    uintptr_t structClass = 0;
                    if (Macht::ReadSafe(fi.Address + DynOff::FSTRUCTPROP_STRUCT, structClass) && structClass)
                        fv.structClassAddr = structClass;
                }

                // StrProperty / Utf8StrProperty / AnsiStrProperty: decode value
                if (fi.TypeName == "StrProperty") {
                    fv.strValue = ReadFString(rowPtr, fi.Offset);
                } else if (fi.TypeName == "Utf8StrProperty" ||
                           fi.TypeName == "AnsiStrProperty") {
                    fv.strValue = ReadFUtf8String(rowPtr, fi.Offset);
                } else if (fi.TypeName == "TextProperty") {
                    // Mirrors WalkInstance's TextProperty branch (:5157-5160) on
                    // purpose, including the "(empty)" typedValue: the two readers
                    // must agree, and this is the pair that did not.
                    //
                    // [DTTEXT-2026-08-23] Until now this branch did not exist, so an
                    // FText column of a DataTable came back with NO value and NO
                    // str_value at all -- while the SAME property on the SAME object
                    // in the SAME build rendered fine through walk_instance. The row
                    // still listed the field with its type and its raw hex, so the
                    // column looked present and merely blank, which reads as "this
                    // row has no caption" rather than as "we cannot decode FText
                    // here". Found on a fixture whose FText column is deliberately
                    // CJK, immediately after [DTROWMAP-2026-08-23] stopped the walk
                    // reading the wrong table.
                    fv.strValue = ReadFTextString(rowPtr + fi.Offset);
                    fv.typedValue = fv.strValue.empty() ? "(empty)" : fv.strValue;
                }

                // EnumProperty: resolve enum name
                if (fi.TypeName == "EnumProperty" || (fi.TypeName == "ByteProperty" && !fi.enumName.empty())) {
                    int64_t rawVal = 0;
                    if (readSize == 1) { uint8_t v = 0; memcpy(&v, p, 1); rawVal = v; }
                    else if (readSize == 4) { int32_t v = 0; memcpy(&v, p, 4); rawVal = v; }
                    else if (readSize == 8) { int64_t v = 0; memcpy(&v, p, 8); rawVal = v; }
                    fv.enumValue = rawVal;
                    // Resolve enum address and name
                    uintptr_t enumAddr = 0;
                    if (Macht::ReadSafe(fi.Address + DynOff::FENUMPROP_ENUM, enumAddr) && enumAddr) {
                        fv.enumAddr = enumAddr;
                        fv.enumName = ResolveEnumValue(enumAddr, rawVal);
                    }
                }
            }

            row.fields.push_back(std::move(fv));
        }

        result.rows.push_back(std::move(row));
        ++read;
    }

    result.ok = true;
    Sein::Info("WALK", "WalkDataTableRows: %s — %d rows read (total=%d, offset=%d, stride=%d, RowMap=+0x%X)",
                 result.rowStructName.c_str(), read, result.rowCount, offset, stride, rowMapOffset);
    return result;
}

} // namespace Ubel
