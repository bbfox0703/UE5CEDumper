#pragma once

// ============================================================
// Linie — 莉涅 (讀取魔力者 — the mage who reads an opponent's mana)
// Live ProcessEvent call profiler: per-UFunction* fire-count table,
// recorded from Stark's ProcessEvent hook during an opt-in Start/Stop
// window. Behaviour-based UFunction discovery — the user performs an
// in-game action (open shop / dash) and sees which UFunctions fired,
// ranked by count. Counting is gated by an atomic so the not-recording
// hot path pays only one relaxed load.
// ============================================================

#include <cstdint>
#include <atomic>
#include <memory>
#include <string>
#include <vector>
#include <utility>

namespace Linie {

// [TRACE-UNLOADED-NAMES] What a function was when the recording first saw it, read by the hook while it was being
// dispatched and so certainly alive. A function the game unloads before Stop (a closed UI, content streamed out) no
// longer resolves at read time; this is what names it then. Not strings: the two int32s of each FName, which decode
// later because FNamePool entries are never freed (Ubel::NameWitness), and what the Live Funcs table shows beside a
// name -- its flags, its parameters, whether its class is a widget's.
struct FuncIdentity {
    int32_t  nameIndex     = 0;   // the UFunction's FName {ComparisonIndex, Number}
    int32_t  nameNumber    = 0;
    int32_t  classIndex    = 0;   // its Outer's -- its UClass's -- FName
    int32_t  classNumber   = 0;
    uint32_t functionFlags = 0;
    uint16_t parmsSize     = 0;
    uint8_t  numParms      = 0;
    bool     isWidget      = false;
    bool     captured      = false;   // false: never read (a reader that failed, or none installed)
    uint64_t outer         = 0;       // its Outer (its UClass) when read: a reloaded class is a new object
    // Another function took this address during the recording (review DLL-3): read again when its key changed, so
    // the fields above are the latest occupant's, and the address's count and calls are more than one function's.
    bool     reused        = false;
};
// Reads `ufunc`'s identity on the hook, under the table's lock: loads only -- no allocation, no lock, no string.
// Linie knows nothing of UObjects, so the pipe installs Ubel's reader at Start and a test installs a stub.
using FuncIdentityReader = bool (*)(uintptr_t ufunc, FuncIdentity& out);
// The key checked on every later call, which is a dispatch and so alive: the FName ints and the Outer. Loads only.
using FuncKeyReader = bool (*)(uintptr_t ufunc, int32_t& nameIndex, int32_t& nameNumber, uint64_t& outer);
// A read that fails is tried again on the function's next calls, this many times in all.
inline constexpr int kIdentityTries = 3;

// One profiled function: how many times it fired + WHEN it first fired (its
// position in the recording's call stream, 1-based). firstSeq is the causal
// signal — an action's entry point (e.g. OpenShop) fires BEFORE the reactions
// it triggers (widget creation, On* notifications), so a smaller firstSeq among
// the diff's NEW rows ranks the true entry point above its downstream effects.
//
// CADENCE (Phase E): meanPeriodMs / cv summarise the wall-clock inter-arrival
// gaps between fires (Welford, computed from the timestamp Stark already reads —
// zero extra clock reads on the hot path). A steady TIMER callback fires at a
// regular period (low cv); Tick fires every frame (low cv too, but in the frame
// band ~5-40 ms); an input-driven function fires irregularly (high cv). The UI
// classifies "periodic" from these + gapSamples (it needs enough fires to trust
// the stats). meanPeriodMs/cv are 0 until at least one gap (2 fires) exists.
struct FuncStat {
    uintptr_t func         = 0;
    uint64_t  count        = 0;
    uint64_t  firstSeq     = 0;
    double    meanPeriodMs = 0.0;  // mean inter-arrival gap (ms); 0 with <2 fires
    double    cv           = 0.0;  // stddev/mean of the gaps; 0 with <2 gaps
    uint64_t  gapSamples   = 0;    // number of inter-arrival gaps measured (count-1)
    uint64_t  firstMs      = 0;    // wall-clock of the first and the latest fire, on the clock RecordCall gets
    uint64_t  lastMs       = 0;
    uint64_t  activeMs     = 0;    // the time it kept firing at frame cadence: the sum of its gaps up to kActiveGapMaxMs
    FuncIdentity ident;            // read at its first call (TRACE-UNLOADED-NAMES); last, so aggregate inits stay valid
};

// [LIVEFUNCS-HIDE-PERFRAME] A function that fires every frame through most of the recording: the per-frame noise
// pe_profile_get can leave out so it stops taking the fetch limit's rows from the low-count functions Live Funcs is
// for. A count cannot say it -- every frame for a minute is 3,600 fires at 60 fps and 8,640 at 144 -- so it is a
// cadence: a mean gap inside the frame band (the band the UI's periodic test excludes, FrameBandMaxMs) over enough
// gaps, KEPT UP for at least half of `windowMs`. Kept up is activeMs, the sum of its gaps of kActiveGapMaxMs or less,
// not the span from its first fire to its last: an action done twice six seconds apart spans the recording and
// fired for two seconds of it, and a broadcast to 80 instances in one frame adds nothing.
inline constexpr double   kPerFrameMaxMeanMs = 40.0;
inline constexpr uint64_t kPerFrameMinGaps   = 3;
inline constexpr uint64_t kActiveGapMaxMs    = 100;   // a frame at 10 fps; a longer gap is a pause, not a frame

inline bool IsPerFrame(const FuncStat& s, uint64_t windowMs) {
    if (windowMs == 0 || s.gapSamples < kPerFrameMinGaps || s.meanPeriodMs > kPerFrameMaxMeanMs) return false;
    return s.activeMs * 2 >= windowMs;
}

// Hot-path gate. Defined in Linie.cpp; declared extern so the check inlines at
// Stark's call site (one relaxed atomic load + predicted-not-taken branch when off).
extern std::atomic<bool> g_recording;
inline bool IsRecording() { return g_recording.load(std::memory_order_relaxed); }

struct ArmHint;    // [LIVEFUNCS-STEP2], below
struct ArmState;

// Record one PE fire for `ufunc` at wall-clock `nowMs` (Stark passes the same
// timestamp it already stamped for the responsiveness check — no extra clock
// read). ONLY call when IsRecording() is already true (Stark inlines that check).
// Takes the profile mutex; safe from any thread. `hint`, when given, receives what this address is armed for
// in the recording's ArmState: a default hint when it is armed for nothing.
void RecordCall(uintptr_t ufunc, uint64_t nowMs, ArmHint* hint = nullptr);

// Clear the table (reserve to bound rehash churn), then flip recording on
// under the lock so there is never a half-cleared window. `reader`, when given, reads each function's identity at
// its first call; `keyReader`, when given with it, checks on every later call that the address still holds that
// function, and reads it again when not. `arms`, when given, is the names this recording follows: an identity read
// that matches one arms its address. All are installed under the same lock, so no call of this recording runs
// without them.
void StartRecording(FuncIdentityReader reader = nullptr, FuncKeyReader keyReader = nullptr,
                    std::shared_ptr<ArmState> arms = nullptr);

// Flip recording off; the accumulated counts are retained for a later Snapshot.
void StopRecording();

// == IsRecording(). Named for readers at the pipe layer.
bool IsActive();

// Clear the table + force recording off. Called on client disconnect / shutdown
// so a stale recording never leaks across connections and the map is freed.
void Reset();

// Copy out one FuncStat per distinct function (addr / count / firstSeq / cadence / first and latest fire / the
// identity read at its first call). Safe
// to call while recording. `activityMs` is the window the table was recorded over, from its earliest fire to its
// latest, taken under the same lock: the time the game was dispatching, not the wall clock between Start and Stop,
// which also holds the minutes a game that idles when not foreground spent behind the UI.
void Snapshot(std::vector<FuncStat>& out, uint64_t& activityMs);

// The identity the table read for each of `addrs`, in order; an address it never saw gets an uncaptured one. For
// pe_profile_start to check a traced Start's ticks against the previous recording before StartRecording clears it.
void IdentitiesOf(const std::vector<uintptr_t>& addrs, std::vector<FuncIdentity>& out);

// The functions IsPerFrame picks out of the table as it stands. pe_profile_start reads it BEFORE StartRecording
// clears the table, so the trace can leave out what the previous recording found firing every frame (T5 (b)).
std::vector<uintptr_t> PerFrameFuncs();

// ============================================================
// [LIVEFUNCS-TIMELINE-2026-10-04] The call trace: one record when a call enters ProcessEvent and one when it
// returns, into a ring the user sized, so Stop keeps the calls just before it. The plan, its decisions (T1-T8) and
// the design review (TR1-TR7) are docs/live-funcs-timeline-plan.md.
// ============================================================

// One slot of the ring, and the wire format: pe_trace_get ships the slots as they are and the UI reads the same
// 40 bytes, so a field added here is a protocol change on both sides.
struct TraceRecord {
    uint64_t seqKind;   // the record's sequence number (its write index); kTraceReturnBit set on a return record
    uint64_t ticks;     // QueryPerformanceCounter when the hook saw it
    uint64_t a;         // entry: the UFunction*; return: the sequence number of the call's ENTRY record
    uint64_t b;         // entry: the object it was called on; return: 0
    uint32_t tid;       // the calling thread
    uint32_t flags;     // entry: kTraceScopeRoot when this call opened a ticked-function scope
};
static_assert(sizeof(TraceRecord) == 40, "TraceRecord is the wire format: the UI decodes 40-byte slots");
inline constexpr uint64_t kTraceReturnBit = 1ull << 63;
inline constexpr uint64_t kTraceSeqMask   = kTraceReturnBit - 1;
inline constexpr uint32_t kTraceScopeRoot = 1;

// The slider's range (T1): powers of two from 32 to 512 MB. Linie itself takes any size of two records or more,
// so a test can wrap a small ring; the pipe handler holds the user to this range.
inline constexpr uint64_t kTraceMinBytes = 32ull << 20;
inline constexpr uint64_t kTraceMaxBytes = 512ull << 20;

// A read-only address set for the hook: built at Start and never changed while a recording runs, so the hook reads
// it without a lock. Open addressing; 0 is the empty slot, so a 0 address is never a member.
class AddrSet {
public:
    void Build(const std::vector<uintptr_t>& addrs);
    bool Contains(uintptr_t addr) const;
    size_t Size() const { return count_; }
    bool Empty() const { return count_ == 0; }
private:
    std::vector<uintptr_t> slots_;
    uint64_t mask_  = 0;
    size_t   count_ = 0;
};

struct TraceConfig {
    uint64_t bytes = 0;
    std::vector<uintptr_t> ticked;    // T5 (a): not empty = record only the calls of these and what they call
    std::vector<uintptr_t> exclude;   // T5 (b): never record these, unless the call opens a ticked scope
};
// Busy: the last Stop could not wait out a hook inside its write, so the ring it may still write to stays as it is.
enum class TraceStartStatus { Ok, TooSmall, NoMemory, Busy };

// Stops and frees any earlier trace, allocates the ring and touches every page on the calling thread (so the game
// thread never takes the first lap's page faults), then arms the hook.
TraceStartStatus StartTrace(const TraceConfig& cfg);
// Disarms the hook and waits until no hook is inside a write (TR2). Afterwards the ring does not change.
void StopTrace();
// StopTrace, then releases the memory.
void FreeTrace();

// Hot-path gate, like IsRecording: one relaxed load when no trace runs.
extern std::atomic<bool> g_tracing;
inline bool IsTracing() { return g_tracing.load(std::memory_order_relaxed); }

// What TraceEnter hands TraceReturn for the same call; it lives in the hook's frame across the original call.
struct TraceToken {
    uint64_t entrySeq = 0;
    uint64_t gen      = 0;
    bool     traced   = false;   // an entry record was written: write a return record too
    bool     opened   = false;   // this call opened the thread's ticked scope: its return closes it
};
// `sp` is the hook frame's own stack address (_AddressOfReturnAddress): lower for a call nested inside another on
// the same thread, which is how a ticked scope tells its calls from the ones after it, even when an exception
// unwound the scope's root without a return.
void TraceEnter(uintptr_t ufunc, uintptr_t obj, uintptr_t sp, uint32_t tid, TraceToken& tok);
void TraceReturn(const TraceToken& tok, uint32_t tid);

struct TraceInfo {
    bool     allocated  = false;
    bool     tracing    = false;
    bool     quiesced   = true;    // false: a hook never left its write within StopTrace's wait (the ring is not read)
    uint64_t bytes      = 0;
    uint64_t capacity   = 0;       // records
    uint64_t written    = 0;       // records ever written; the ring keeps [firstValid, written)
    uint64_t firstValid = 0;
    uint64_t gen        = 0;       // one per StartTrace
    uint64_t qpcFreq    = 0;       // ticks per second
    size_t   ticked     = 0;
    size_t   excluded   = 0;
};
TraceInfo GetTraceInfo();
// Records [from, from + maxRecords) clipped to the kept window, in sequence order. False while a trace runs, when
// none is allocated, or when the last stop could not quiesce. `next`, when given, is where the following page
// starts: past this page, never before the window, and at or past `written` once there is nothing more.
// `seen`, when given, is the trace's state under the same lock as the copy, filled even when the copy is refused:
// a reader pages one recording, and a Start between a separate info read and the copy would be another.
bool CopyTrace(uint64_t from, size_t maxRecords, std::vector<TraceRecord>& out, uint64_t* next = nullptr,
               TraceInfo* seen = nullptr);
// The distinct functions and calling objects of the kept window, sorted; computed once per stopped trace. `gen`,
// when given, is the recording they belong to. `idents`, when given, holds one FuncIdentity per entry of `funcs`:
// what the table read at that function's first call. They are taken from the table when the list is first computed
// and kept with the trace, so a recording started since cannot take them back; a function only the trace saw (a
// call traced before StartRecording or after StopRecording) has none.
bool TraceDistinct(std::vector<uintptr_t>& funcs, std::vector<uintptr_t>& objs, uint64_t* gen = nullptr,
                   std::vector<FuncIdentity>* idents = nullptr);
// Free the trace only when it is recording `gen` and that recording has stopped: a reader's release never frees a
// newer recording. True when it freed it.
bool FreeTraceIfGen(uint64_t gen);
// Free a stopped trace that wrote nothing: there is nothing to read, and its ring must not stay in the game until
// the next Start. True when it freed one.
bool ReleaseIfEmpty();

// The trace's wire encoding for the slots (RFC 4648, with padding).
std::string Base64Encode(const uint8_t* data, size_t len);

// ============================================================
// [LIVEFUNCS-STEP2] Parameter snapshots and following a function by name (docs/live-funcs-timeline-plan.md, "Step 2
// design"; T10-T14). A choice or a tick is a NAME -- its function's FName and its class's, as ints -- not an address:
// a widget's functions unload when it closes and come back at new addresses (T10). The table reads each function at
// its first call anyway; a name that matches arms that address, and the hook hands the trace an ArmHint.
// ============================================================

// A function's name as the FNamePool's ints: the function's FName and its class's (its Outer's). Stable for the life
// of the process (FNamePool entries are never freed), so a key taken from an earlier recording still names the
// function after it reloads.
struct NameKey {
    int32_t fnIdx  = 0;
    int32_t fnNum  = 0;
    int32_t clsIdx = 0;
    int32_t clsNum = 0;
};
inline bool operator==(const NameKey& a, const NameKey& b) {
    return a.fnIdx == b.fnIdx && a.fnNum == b.fnNum && a.clsIdx == b.clsIdx && a.clsNum == b.clsNum;
}
inline bool operator<(const NameKey& a, const NameKey& b) {
    if (a.fnIdx != b.fnIdx) return a.fnIdx < b.fnIdx;
    if (a.fnNum != b.fnNum) return a.fnNum < b.fnNum;
    if (a.clsIdx != b.clsIdx) return a.clsIdx < b.clsIdx;
    return a.clsNum < b.clsNum;
}

// What the table's read of one call hands the trace for the same call: integers only, in the hook's own frame.
// `gen` is the trace recording the arm belongs to (1 and up), so a default hint never matches a running trace.
struct ArmHint {
    uint64_t gen   = 0;
    int32_t  ring  = -1;   // the snapshot ring of the choice this address is armed for; -1: no snapshot
    uint32_t arm   = 0;    // which arming of the name: a reload at a new address, or a new class, is a new arm
    uint16_t copy  = 0;    // bytes to copy from the parameter block
    uint8_t  flags = 0;
};
inline constexpr uint8_t kArmTick      = 1;   // the address is a ticked function's: its call opens a scope
inline constexpr uint8_t kArmAfter     = 2;   // take a copy after the call returns too (out parameters, the return)
inline constexpr uint8_t kArmTruncated = 4;   // the parameter block is larger than its ring's slot

// The snapshot limits. The UI's estimate works with the same numbers.
inline constexpr uint32_t kSnapMaxCopy     = 2048;   // bytes copied per call at most
inline constexpr uint32_t kSnapUnknownCopy = 256;    // copied when the parameter size could not be read
inline constexpr uint32_t kSnapMinSlots    = 8;      // a ring that keeps fewer calls refuses the Start
inline constexpr uint32_t kSnapHeaderBytes = 24;     // per slot, before its copy
inline constexpr uint64_t kSnapMinBytes    = 8ull << 20;     // the slider's range (T12); the pipe holds the user to it
inline constexpr uint64_t kSnapMaxBytes    = 128ull << 20;
inline constexpr uint32_t kFuncHasOutParms = 0x00400000;     // UFunction::FunctionFlags: an out parameter or a return

// A ring's slot payload for a choice whose parameter block is `parmsSize` bytes: the block, rounded to 8 and capped;
// kSnapUnknownCopy when the size could not be read (0).
inline uint32_t RingCapFor(uint32_t parmsSize) {
    if (parmsSize == 0) return kSnapUnknownCopy;
    const uint32_t n = parmsSize < kSnapMaxCopy ? parmsSize : kSnapMaxCopy;
    return (n + 7u) & ~7u;
}
// What one arm copies: its function's own parameter size, at most its ring's slot. A size of 0 with flags that were
// read is a function without parameters; with nothing read, the slot is copied whole and the decoder marks what lies
// past the parameters.
inline uint32_t ArmCopyBytes(uint32_t parmsSize, uint32_t functionFlags, uint32_t ringCap) {
    if (parmsSize == 0) return functionFlags != 0 ? 0 : ringCap;
    return parmsSize < ringCap ? parmsSize : ringCap;
}
inline bool ArmTruncated(uint32_t parmsSize, uint32_t ringCap) { return parmsSize > ringCap; }
// The after-return copy: for out parameters and the return value (FUNC_HasOutParms), and whenever the flags could not
// be read -- a copy too many beats a return value lost.
inline bool ArmTakesAfter(uint32_t functionFlags) {
    return functionFlags == 0 || (functionFlags & kFuncHasOutParms) != 0;
}

// One name a recording follows, and what an address with that name is armed for.
struct ArmSpec {
    NameKey  key;
    bool     tick    = false;   // a trace tick: its calls open a scope
    int32_t  ring    = -1;      // a snapshot choice: its ring; -1 when the name is not chosen
    uint32_t ringCap = 0;       // that ring's slot payload, RingCapFor of the choice's parameter size
};
// One arming of a chosen name: the first call of a matching address, or a call after the address's key changed.
struct ArmRecord {
    uintptr_t    addr     = 0;
    FuncIdentity ident;           // what the table read at that call
    uint32_t     spec     = 0;    // index into ArmState::specs
    int32_t      ring     = -1;
    uint64_t     armMs    = 0;    // the table's clock at the arming
};
// The names one recording follows and the arms it made. The table touches it under its lock; its lifetime is the
// shared_ptr's, so a reader that holds one is never left with freed memory.
struct ArmState {
    uint64_t gen = 0;                  // the trace recording it belongs to (1 and up)
    std::vector<ArmSpec>   specs;      // sorted by key, one per key
    std::vector<ArmRecord> log;        // reserved when built: the hook never allocates for it
    size_t                 capacity = 0;
};
inline constexpr size_t kArmLogCapacity = 16384;
// Sorts the specs by key and merges a key both ticked and chosen into one; reserves the log for `logCapacity` arms.
std::shared_ptr<ArmState> BuildArmState(std::vector<ArmSpec> specs, size_t logCapacity);

// Tests replace the clock; nullptr restores QueryPerformanceCounter.
void SetTraceClockForTest(uint64_t (*clock)());

} // namespace Linie
