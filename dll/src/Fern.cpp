// ============================================================
// Fern — 費倫 (芙莉蓮的弟子 — Frieren's Apprentice)
// PipeServer: Named Pipe JSON IPC implementation
// ============================================================

// BEFORE every include, not just before Sein.h. Fern.h pulls in Routine.h, which
// EXPANDS LOG_WARN / LOG_ERROR in its own inline bodies — so the category is bound
// at that point, and defining it afterwards was both a C4005 redefinition warning
// and a real misrouting: the B14 thread guard's "UNCAUGHT exception - contained"
// messages were logging under "" (init-0.log) instead of PIPE:svr (pipe-0.log),
// i.e. the one place a torn-down pipe server reports itself was in the wrong file.
// Fern.cpp was the only TU with this ordering; the other nine Routine.h users
// already defined it first.
#define LOG_CAT "PIPE:svr"

#include "Fern.h"
#include "Renge.h"
#include "Grimoire.h"
#include "Sein.h"
#include "Macht.h"
#include "Genau.h"
#include "Aura.h"
#include "Serie.h"
#include "Ubel.h"
#include "Flamme.h"
#include "Stark.h"
#include "Radar.h"
#include "Tot.h"
#include "Routine.h"   // Routine::RunThreadGuarded — a throw out of a thread proc is std::terminate (B14)
#include "Wirbel.h"
#include "Laufen.h"
#include "Hemmung.h"  // Hemmung::SetDilation/ResetDilation/GetSnapshot for time_* commands
#include "Dunste.h"    // Dunste::SetEnabled/SetSpeed/SetPreset/GetStatus for fly_*
#include "Schlacht.h"  // Schlacht::SetEnabled/GetStatus for seethrough_*
#include "Solitar.h"   // Solitar::ResolveProtectBits for get_trainer_offsets
#include "Solide.h"    // Solide::AddForce/RemoveForce/GetState/FindStealthMeter for force_field / stealth
#include "Grausam.h"   // Grausam::SetForegroundLock — keep game thread alive when backgrounded
#include "Edel.h"
#include "Linie.h"     // Live PE profiler — pe_profile_start/stop/get
#include "Sense.h"     // Diagnostics — dispatch timing + process facts
#include "BuildStamp.h"

#include <json.hpp>
#include <algorithm>
#include <chrono>
#include <cstdlib>   // malloc/free for by-value FString INPUT-param buffers
#include <cstring>
#include <sstream>
#include <unordered_set>   // widget-base set for pe_profile_get is_widget classification
#include <vector>

using json = nlohmann::json;

// This DLL's own module handle (defined in Heiter.cpp DllMain). Used to self-report
// the load path in the init response: for a proxy build g_hDllModule IS the proxy
// DLL that the OS actually loaded (the mutex WINNER — a passive-forwarder second
// proxy returns before init and never reports), for a manual inject / CE .CT it is
// UE5Dumper.dll. This is the only correct proxy attribution: module-list enumeration
// mis-attributes when two proxies coexist because they share the PE ProductName.
extern HMODULE g_hDllModule;

// Forward declare ExportAPI functions (extern "C" must be at global scope)
extern "C" bool      UE5_Init();
extern "C" uintptr_t UE5_FindInstanceOfClass(const char* className);
extern "C" uintptr_t UE5_GetObjectClass(uintptr_t obj);
extern "C" uintptr_t UE5_FindFunctionByName(uintptr_t classAddr, const char* funcName);
extern "C" int32_t   UE5_CallProcessEventDirect(uintptr_t instance, uintptr_t ufunc, uintptr_t params);
extern "C" int32_t   UE5_CallProcessEvent(uintptr_t instance, uintptr_t ufunc, uintptr_t params);
extern "C" int32_t   UE5_GetDebugCameraState();
extern "C" int32_t   UE5_SetDebugCamera(int32_t enable);
extern "C" int32_t   UE5_SetGodMode(int32_t enable);
extern "C" int32_t   UE5_GetGodMode();
extern "C" int32_t   UE5_GetProtectState(int32_t* outWant, int32_t* outLive, int32_t* outResolvable);
// Size-aware variant: the queued request owns a copy of the param buffer, so a
// timed-out invoke can't use-after-free this handler's stack-local paramBuf.
extern "C" int32_t   UE5_CallProcessEventEx(uintptr_t instance, uintptr_t ufunc, uintptr_t params, uint32_t paramsSize);
extern "C" bool      UE5_EnsureGameThreadHook();
extern "C" bool      UE5_IsGameThreadHookActive();
extern "C" int       UE5_GetProcessEventOffset();

// ============================================================
// Radar wire helpers — parse "100" / "-42" / "3.14" / "true" /
// "0x..." into the right little-endian byte layout for DataType, and
// format the inverse for response payloads. Wire schema uses strings
// to avoid JSON-number precision loss at 64-bit ints; the helpers
// also tolerate leading 0x for unsigned-int types so the user can
// paste pointer-shaped values directly into Exact-mode scans.
// ============================================================
namespace {

// Vector value parser. Accepts "X,Y,Z" or "X Y Z" with optional spaces;
// rejects malformed input (wrong component count, non-numeric tokens).
// Writes 12 little-endian bytes (3 floats) into `out`. Phase 2B.
bool ParseVectorBytes(const std::string& raw, uint8_t out[12]) {
    std::memset(out, 0, 12);
    if (raw.empty()) return false;

    // Tokenise on commas + whitespace. Resilient to "1, 2, 3" /
    // "1,2,3" / "1 2 3" / "1.5, -2.5, 3" alike.
    std::vector<std::string> toks;
    std::string cur;
    auto flush = [&]() {
        if (!cur.empty()) { toks.push_back(cur); cur.clear(); }
    };
    for (char c : raw) {
        if (c == ',' || std::isspace(static_cast<unsigned char>(c))) flush();
        else cur.push_back(c);
    }
    flush();
    if (toks.size() != 3) return false;

    float floats[3] = {0.0f, 0.0f, 0.0f};
    try {
        for (int i = 0; i < 3; ++i) floats[i] = std::stof(toks[static_cast<size_t>(i)]);
    } catch (...) {
        return false;
    }
    std::memcpy(out, floats, 12);
    return true;
}

bool ParseValueBytes(Radar::DataType dt, const std::string& raw, uint8_t out[8],
                     Radar::RoundMode roundMode = Radar::RoundMode::Round) {
    std::memset(out, 0, 8);
    if (raw.empty()) return false;

    // Trim surrounding whitespace -- the UI's NumericTextBox sometimes
    // ships a trailing newline.
    size_t lo = 0, hi = raw.size();
    while (lo < hi && std::isspace(static_cast<unsigned char>(raw[lo]))) ++lo;
    while (hi > lo && std::isspace(static_cast<unsigned char>(raw[hi - 1]))) --hi;
    if (lo >= hi) return false;
    std::string s = raw.substr(lo, hi - lo);

    auto isHexPrefix = [](const std::string& str) {
        return str.size() > 2 && str[0] == '0' && (str[1] == 'x' || str[1] == 'X');
    };

    // Rounding-mode integer coercion (build 1672): a fractional input (e.g.
    // "10.9") would throw in the integer stoll/stoull paths below. For an integer
    // type, reduce it to the displayed integer via the mode (Round 10.9->11,
    // Trunc->10, Ceil->11) and rewrite `s` to that integer string — the per-type
    // switch then range-checks it normally. Mirrors BuildNumericTargets so the
    // concrete-width and meta paths coerce identically. Hex + clean integers are
    // left untouched; float/double/bool keep their own parse.
    const bool isIntType =
        dt == Radar::DataType::Int8   || dt == Radar::DataType::Int16  ||
        dt == Radar::DataType::Int32  || dt == Radar::DataType::Int64  ||
        dt == Radar::DataType::UInt8  || dt == Radar::DataType::UInt16 ||
        dt == Radar::DataType::UInt32 || dt == Radar::DataType::UInt64;
    if (isIntType && !isHexPrefix(s)) {
        bool cleanInt = false;
        try { size_t pos = 0; (void)std::stoll(s, &pos, 0); cleanInt = (pos == s.size()); } catch (...) {}
        if (!cleanInt) {
            try {
                size_t pos = 0;
                double dv = std::stod(s, &pos);
                if (pos == s.size()) {
                    char nb[40];
                    std::snprintf(nb, sizeof(nb), "%.0f", Radar::ReduceRounded(dv, roundMode));
                    s = nb;
                }
            } catch (...) {}
        }
    }

    try {
        switch (dt) {
            case Radar::DataType::Int8: {
                long long v = std::stoll(s, nullptr, 0);
                if (v < INT8_MIN || v > INT8_MAX) return false;
                int8_t t = static_cast<int8_t>(v);
                std::memcpy(out, &t, 1);
                return true;
            }
            case Radar::DataType::Int16: {
                long long v = std::stoll(s, nullptr, 0);
                if (v < INT16_MIN || v > INT16_MAX) return false;
                int16_t t = static_cast<int16_t>(v);
                std::memcpy(out, &t, 2);
                return true;
            }
            case Radar::DataType::Int32: {
                long long v = std::stoll(s, nullptr, 0);
                if (v < INT32_MIN || v > INT32_MAX) return false;
                int32_t t = static_cast<int32_t>(v);
                std::memcpy(out, &t, 4);
                return true;
            }
            case Radar::DataType::Int64: {
                long long v = std::stoll(s, nullptr, 0);
                int64_t t = static_cast<int64_t>(v);
                std::memcpy(out, &t, 8);
                return true;
            }
            case Radar::DataType::UInt8: {
                unsigned long long v = std::stoull(s, nullptr, isHexPrefix(s) ? 16 : 0);
                if (v > UINT8_MAX) return false;
                uint8_t t = static_cast<uint8_t>(v);
                std::memcpy(out, &t, 1);
                return true;
            }
            case Radar::DataType::UInt16: {
                unsigned long long v = std::stoull(s, nullptr, isHexPrefix(s) ? 16 : 0);
                if (v > UINT16_MAX) return false;
                uint16_t t = static_cast<uint16_t>(v);
                std::memcpy(out, &t, 2);
                return true;
            }
            case Radar::DataType::UInt32: {
                unsigned long long v = std::stoull(s, nullptr, isHexPrefix(s) ? 16 : 0);
                if (v > UINT32_MAX) return false;
                uint32_t t = static_cast<uint32_t>(v);
                std::memcpy(out, &t, 4);
                return true;
            }
            case Radar::DataType::UInt64: {
                unsigned long long v = std::stoull(s, nullptr, isHexPrefix(s) ? 16 : 0);
                uint64_t t = static_cast<uint64_t>(v);
                std::memcpy(out, &t, 8);
                return true;
            }
            case Radar::DataType::Float: {
                float t = std::stof(s);
                std::memcpy(out, &t, 4);
                return true;
            }
            case Radar::DataType::Double: {
                double t = std::stod(s);
                std::memcpy(out, &t, 8);
                return true;
            }
            case Radar::DataType::Bool: {
                // Accept: true / false / 1 / 0 (case insensitive)
                std::string lower = s;
                for (auto& c : lower) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
                if (lower == "true"  || lower == "1") { out[0] = 1; return true; }
                if (lower == "false" || lower == "0") { out[0] = 0; return true; }
                return false;
            }
            // String types pass through targetString separately; the
            // byte path is unused. Vector types use ParseVectorBytes
            // into a 12-byte buffer. Fall through to false so a caller
            // that forgets to dispatch fails loudly.
            case Radar::DataType::FString:
            case Radar::DataType::FName:
            case Radar::DataType::FText:
            case Radar::DataType::FVector:
            case Radar::DataType::FRotator:
            case Radar::DataType::FTransform:
            // Multi-numeric meta types are parsed via BuildNumericTargets,
            // not this single-width helper. Fail loudly if misrouted.
            case Radar::DataType::NumericNoByte:
            case Radar::DataType::NumericAll:
                return false;
        }
    } catch (...) {
        return false;
    }
    return false;
}

// Top-N cap for the class-noise histogram on the wire (begin/refine responses).
// The UI's "counts partial" warning compares class_distinct against this.
static constexpr int kClassHistogramMaxRows = 40;

// Per-request cap for query_group_slot_leaves. A slot keeps at most `per_slot_cap`
// leaves (clamped 8..4096 on begin_group_scan), and this is one expanded row's
// detail — not a scan result — so the whole list fits in one response at the
// default. Kept a hard server-side ceiling anyway: `limit` comes off the wire.
static constexpr int kGroupSlotLeafMaxRows = 4096;

// Serialize a class histogram (already sorted count-desc) to the wire, capped
// at `maxRows` so the noise picker stays small. Shared by the value + group
// begin/refine responses (class_histogram = [{class_name, count}, ...]).
json HistogramToJson(const std::vector<std::pair<std::string, int>>& hist, int maxRows) {
    json arr = json::array();
    const int n = (std::min)(static_cast<int>(hist.size()), maxRows);
    for (int i = 0; i < n; ++i) {
        json o;
        o["class_name"] = hist[i].first;
        o["count"]      = hist[i].second;
        arr.push_back(std::move(o));
    }
    return arr;
}

// Parse a request's optional "exclude_classes" string array into a vector
// (empty when absent / not an array). Drives the server-side class-noise filter
// on query_candidates / query_group_candidates.
std::vector<std::string> ParseExcludeClasses(const json& request) {
    std::vector<std::string> out;
    auto it = request.find("exclude_classes");
    if (it != request.end() && it->is_array()) {
        for (const auto& e : *it)
            if (e.is_string()) out.push_back(e.get<std::string>());
    }
    return out;
}

// Build the wire JSON for one candidate. Per-(class,field) metadata and
// per-object metadata are pulled from the session's shared descriptor /
// instance pools the candidate indexes into (V3-A) — the wire shape is
// unchanged, the fields are just reassembled from the interned pools.
json CandidateToJson(const Radar::Candidate& c,
                     Radar::DataType dt,
                     const std::vector<Radar::FieldDescriptor>& descriptors,
                     const std::vector<Radar::InstanceRecord>&  instances) {
    const Radar::FieldDescriptor& desc = descriptors[c.descriptorIdx];
    const Radar::InstanceRecord&  inst = instances[c.instanceIdx];

    json item;
    item["addr"]                = Renge::AddrToStr(c.addr);
    item["instance_addr"]       = Renge::AddrToStr(inst.instanceAddr);
    item["instance_index"]      = inst.instanceIndex;
    item["field_offset"]        = desc.fieldOffset;
    item["instance_name"]       = inst.instanceName;
    item["class_name"]          = desc.className;
    item["defining_class_name"] = desc.definingClassName;
    item["field_name"]          = Radar::FieldDisplayName(desc, c.elementIndex);
    item["field_type"]          = desc.fieldType;
    item["bool_field_mask"]     = desc.boolFieldMask;
    // Native-C (P1): badge a raw-hole hit + the width it was interpreted at, so
    // the UI can distinguish unmanaged native values from reflected ones. Only
    // emitted when set (absent => reflected, keeps the wire lean + back-compat).
    if (desc.isNativeC) {
        item["is_native_c"] = true;
        item["guessed_type"] = desc.guessedType;
    }
    // Value rendering (numeric per dt / multi per fieldType / vector
    // "X, Y, Z" / string prevStr) is the single source of truth in Radar,
    // shared with the server-side filter/sort so the wire + the ordered view
    // always agree on a candidate's displayed value.
    item["value"] = Radar::FormatCandidateValue(c, dt, desc);
    return item;
}

// Wire JSON for ONE leaf of a group slot: the resolved field name / offset /
// type / current value / addresses.
//
// Shared by the row's REPRESENTATIVE leaf and by query_group_slot_leaves' full
// list, so the two can never disagree about what a leaf is called or holds. The
// value comes from Radar::GroupSlotValueString — the same call the server-side
// filter matches against, which is the whole point: this area's entire bug
// history is two code paths answering one question differently.
json GroupLeafToJson(const Radar::GroupSlotMatch& m,
                     const Radar::SlotSpec& spec,
                     const std::vector<Radar::FieldDescriptor>& descriptors) {
    json lj;
    if (m.descriptorIdx >= descriptors.size()) return lj;
    const Radar::FieldDescriptor& d = descriptors[m.descriptorIdx];
    lj["field_name"]      = Radar::FieldDisplayName(d, m.elementIndex);
    lj["field_offset"]    = m.offset;
    lj["field_type"]      = d.fieldType;
    lj["bool_field_mask"] = d.boolFieldMask;
    // Native-C (P2): badge a raw-hole leaf + its interpreted width (omitted for
    // reflected leaves — back-compat / lean wire).
    if (d.isNativeC) {
        lj["is_native_c"]  = true;
        lj["guessed_type"] = d.guessedType;
    }
    // Absolute leaf address (direct: owner+offset; deep: container element).
    lj["addr"]        = Renge::AddrToStr(m.leafAddr);
    // Owning object of the leaf (P4): the candidate actor for an own-block leaf,
    // or an owned sub-object for a cross-object leaf — drives handoffs.
    lj["owner_addr"]  = Renge::AddrToStr(m.ownerAddr);
    // Owning object's class (P4 inc 2) — drives the per-slot Pivot handoff.
    lj["owner_class"] = m.ownerClass;
    lj["leaf_value"]  = Radar::GroupSlotValueString(m, spec, descriptors);
    return lj;
}

// Build the wire JSON for one group-scan candidate (build 1276). A group hit is
// OBJECT-level: one owning UObject + a nested `slots` array. Each slot carries
// the user's target value, its converging matched offsets, a `locked` flag (the
// field is identified once a single offset remains), and — for the
// representative (first) match — the resolved field name / offset / type / leaf
// value / leaf address so the UI's per-slot row can drive the same handoffs
// (Open in Live Walker / Locate in GWorld / Copy) as a single-value candidate.
/// @param highlight  The active server-side filter, or "". When set, each slot reports
///                   the leaf that MATCHED it rather than the first one kept.
///
/// WHY THE PARAMETER EXISTS. The filter walks every leaf in every slot (className /
/// definingClass / fieldName / value — Radar.cpp BuildGroupOrderedView), while this
/// function reported `matches[0]`. So filtering for `424242` returned rows whose visible
/// values contained no 424242 anywhere: the filter was right, the row was showing a
/// different leaf of the same candidate. Two code paths answering the same question
/// differently — and the user reasonably read it as a wrong result.
json GroupCandidateToJson(const Radar::GroupCandidate& gc,
                          const std::vector<Radar::SlotSpec>&        slots,
                          const std::vector<Radar::FieldDescriptor>& descriptors,
                          const std::vector<Radar::InstanceRecord>&  instances,
                          const std::string& highlight = "") {
    const Radar::InstanceRecord& inst = instances[gc.instanceIdx];

    json item;
    item["instance_addr"]  = Renge::AddrToStr(inst.instanceAddr);
    item["instance_index"] = inst.instanceIndex;
    item["instance_name"]  = inst.instanceName;

    // All slot matches of one candidate share the owning object's class.
    std::string className, definingClass;
    for (const auto& sl : gc.slotMatches) {
        if (!sl.empty()) {
            className     = descriptors[sl[0].descriptorIdx].className;
            definingClass = descriptors[sl[0].descriptorIdx].definingClassName;
            break;
        }
    }
    item["class_name"]          = className;
    item["defining_class_name"] = definingClass;

    json slotsJson = json::array();
    // ONE assignment for the whole row — no two slots may display the same leaf.
    // The rule lives in Radar, beside the filter it has to agree with; see
    // Radar::PickGroupWitnessAssignment for why it is not written here. The filter
    // is split the same way the filter itself splits it (space = AND), so naming
    // two fields puts one in each slot.
    const std::vector<size_t> picks = Radar::PickGroupWitnessAssignment(
        gc.slotMatches, slots, descriptors, Radar::SplitFilterTerms(highlight));

    for (size_t s = 0; s < gc.slotMatches.size(); ++s) {
        const Radar::SlotSpec& spec = slots[s];
        const auto& matches = gc.slotMatches[s];

        // Start from the displayed leaf so the row and query_group_slot_leaves
        // emit a leaf through exactly one encoder.
        json sj = matches.empty() ? json::object()
                                  : GroupLeafToJson(matches[picks[s]], spec, descriptors);
        sj["slot_index"] = static_cast<int>(s);
        sj["value"]      = spec.value;
        sj["scan_type"]  = Radar::NameOf(spec.st);  // per-slot predicate (P2)
        if (spec.st == Radar::ScanType::Between)
            sj["value2"] = spec.value2;             // Between upper bound

        json offsets = json::array();
        for (const auto& m : matches) offsets.push_back(m.offset);
        sj["matched_offsets"] = offsets;
        sj["locked"]          = (matches.size() == 1);

        // How many leaves this slot actually holds. `locked` already says "exactly
        // one"; this says how much the single displayed value is standing in for, so
        // a row can no longer imply the candidate matched on one field when it
        // matched on thirty. `query_group_slot_leaves` returns the other thirty BY
        // NAME — before it existed, `matched_offsets` was the only trace of them and
        // a raw integer cannot tell a user that 1308 is `FrozenInt`.
        if (!matches.empty())
            sj["match_count"] = static_cast<int>(matches.size());
        slotsJson.push_back(std::move(sj));
    }
    item["slots"] = slotsJson;
    return item;
}

}  // namespace

// ScanProgress — global progress state updated by UE5_Init(), read by scan_status
namespace ScanProgress {
    extern std::atomic<int>  phase;
    extern std::string       statusText;
    extern std::mutex        statusMutex;
    std::string GetStatusText();
}

bool Fern::Start() {
    if (m_running.load()) {
        LOG_WARN("PipeServer: Already running");
        return true;
    }
    // Stop() clears m_running in its FIRST statement, so for the whole teardown
    // window the server reads as "stopped" while its threads are still alive.
    // Starting there would move-assign onto a joinable m_acceptThread, which the
    // standard defines as std::terminate — the game dies with no log and no dump.
    if (m_stopping.load(std::memory_order_acquire)) {
        LOG_WARN("PipeServer: Start refused — a Stop() is still in progress");
        return false;
    }

    // Clear the sticky shutdown latch left by a prior Stop()/UE5_Shutdown().
    // Without this, re-enabling the CE script in the same game process leaves
    // g_shutdown set, so every long-running op (value scan, instance find,
    // snapshot capture, SDK dump) aborts on its first Tot::Requested() poll.
    Tot::ResetShutdown();
    Tot::ResetPerCommand();

    {
        std::lock_guard<std::mutex> lock(m_connMutex);
        m_conns.clear();
        m_listenPipe = INVALID_HANDLE_VALUE;
        m_clientConnected = false;
    }

    m_running = true;
    m_acceptThread = std::thread(&Fern::AcceptLoop, this);
    m_monitorThread = std::thread(&Fern::MonitorLoop, this);

    LOG_INFO("PipeServer: Started on %ls (maxInstances=%lu)", Grimoire::PIPE_NAME, kMaxPipeInstances);
    return true;
}

void Fern::Stop(bool graceful) {
    if (!m_running.exchange(false)) return; // Already stopped

    // ── Process-exit path (~Fern(), audit #5 D5/F1) ────────────────────────────
    // Everything below this block is teardown that DLL_PROCESS_DETACH must not do,
    // and Heiter.cpp's DETACH case already refuses to do for exactly these reasons —
    // it just could not speak for a static destructor, which the CRT runs anyway
    // (dllmain_crt_process_detach calls __scrt_dllmain_uninitialize_c()
    // unconditionally; `is_terminating` gates only __scrt_uninitialize_crt).
    //
    // Two things go wrong when the full body runs here:
    //   1. ExitProcess has ALREADY terminated the accept, monitor and connection
    //      threads. A dead connection thread can never erase itself from m_conns, so
    //      the drain predicate below is unsatisfiable BY CONSTRUCTION and the whole
    //      5 s budget burns on every exit. Measured 2026-08-14 on DumperTest: with a
    //      client still registered, `conn drain TIMEOUT, 1 left (5030 ms, 49 cancel
    //      re-asserts)` and 6,046 ms to exit; with it disconnected first, `satisfied,
    //      0 left (0 ms)` and 1,105 ms. One variable, 5.5x apart.
    //   2. Worse than slow: this body takes m_connMutex, Sein's log mutex and both
    //      Radar session mutexes AFTER their holders were killed. MSDN is explicit
    //      that detach code taking a lock a terminated thread held deadlocks the
    //      process — a game that never closes.
    //
    // The OS reclaims the handles, threads and memory. That is the same reasoning
    // Heiter.cpp:288-301 applies to its own DETACH body and Routine.h:51-56 applies
    // to every feature worker; Fern::Stop's explicit join()/wait_for calls were
    // simply not on that list.
    //
    // Note this path deliberately takes NO lock — not even to report m_conns.size(),
    // which is the count a reader would most want. Reading it means m_connMutex, and
    // that is hazard 2 above.
    //
    // The only case this gives up on is FreeLibrary of this DLL with the process
    // still alive (DETACH with lpReserved == NULL, threads still running). Nothing
    // in this repo does that — the injector LoadLibrarys and never unloads, and CE's
    // Disable calls UE5_Shutdown, which reaches Stop(graceful=true) through the
    // normal path — and Heiter.cpp's no-op DETACH already relies on the same fact.
    if (!graceful) {
        LOG_INFO("PipeServer: Stop entry (process exit — skipping drain/joins, "
                 "the OS reclaims this)");
        m_clientConnected = false;
        return;
    }
    // Closed only on the way out. m_running goes false in the line above, so
    // without this the whole teardown window looks "stopped" to Start(), which
    // would then move-assign over a still-joinable m_acceptThread — a
    // standard-mandated std::terminate that kills the game with no log.
    m_stopping.store(true, std::memory_order_release);
    struct StoppingGuard {
        std::atomic<bool>& f;
        ~StoppingGuard() { f.store(false, std::memory_order_release); }
    } stoppingGuard{ m_stopping };

    // Phase timings: reconstructing this teardown from the outside cost a full
    // investigation because "Stopped" was the only line Stop() ever logged, and a
    // 5 s stall was indistinguishable from the 5 s connection-drain TIMEOUT.
    const auto tStart = std::chrono::steady_clock::now();
    auto elapsedMs = [&tStart] {
        return static_cast<long long>(std::chrono::duration_cast<std::chrono::milliseconds>(
            std::chrono::steady_clock::now() - tStart).count());
    };
    size_t connsAtEntry;
    {
        std::lock_guard<std::mutex> lock(m_connMutex);
        connsAtEntry = m_conns.size();
    }
    LOG_INFO("PipeServer: Stop entry (conns=%zu)", connsAtEntry);

    // Abort any in-flight long-running operation BEFORE joining threads, so a
    // scan blocking the accept thread bails promptly and the join below
    // completes fast (otherwise disabling the script / closing can hang while
    // a long scan finishes). Sticky — never auto-cleared.
    Tot::RequestShutdown();

    // Unblock everyone (Path A multi-connection teardown). CancelIoEx every live
    // connection so a thread blocked in ReadFile/WriteFile returns
    // ERROR_OPERATION_ABORTED and runs its OWN cleanup (each connection handle is
    // closed by its owning thread — no cross-thread close race). A thread inside
    // DispatchCommand exits via the m_running loop check after it returns;
    // Tot::RequestShutdown above makes a long scan bail first.
    //
    // The listen instance is NOT closed here, and that is the whole point. This
    // block used to call CloseHandle(m_listenPipe) under the comment "proven
    // unblock". The logs disprove it: the listen instance is a SYNCHRONOUS handle
    // (AcceptLoop's CreateNamedPipeW passes no FILE_FLAG_OVERLAPPED), so closing
    // it does not abort the accept thread's parked ConnectNamedPipe — it BLOCKS
    // until that call completes, i.e. until somebody connects. Under m_connMutex,
    // which every connection thread needs to unregister itself.
    //
    // Measured on Elliot 2026-08-04: 9.4 s and 13.3 s for two disables where the
    // user happened to reconnect the UI — and on the third, with the UI already
    // disconnected, "PipeServer: Stopped" was NEVER logged at all. The teardown
    // thread stayed wedged in the game process holding m_connMutex for the rest of
    // the session. The delay was never a timeout; it was "until a client shows up".
    // Cancel every live connection's I/O. MUST be called with m_connMutex held:
    // a connection thread erases itself from m_conns (under this lock) BEFORE it
    // calls CloseConnOnce, so anything still in the registry has an open handle.
    //
    // Returns how many cancels the kernel ACCEPTED. The rest came back
    // ERROR_NOT_FOUND — "no I/O was pending on this handle right now" — which is
    // the reading that matters: the thread was between two ReadFile calls (this
    // server reads a byte at a time, so a 40-byte command is 40 chances to be in
    // the gap) and will issue a FRESH read that this one-shot cancel can never
    // reach. That is why the cancel is re-asserted in the drain loop below rather
    // than fired once and trusted. Measured 2026-08-05: two connections idle in
    // ReadFile survived the single cancel and burned the whole 5 s budget.
    auto cancelLiveConns = [this](int& accepted, int& notFound) {
        accepted = notFound = 0;
        for (auto& c : m_conns) {
            if (!c || c->pipe == INVALID_HANDLE_VALUE ||
                c->closed.load(std::memory_order_relaxed))
                continue;
            if (CancelIoEx(c->pipe, nullptr)) ++accepted;
            else                              ++notFound;
            // AND the synchronous one, which is the case that actually occurs here.
            // CancelIoEx is kept because it is correct for anything genuinely async and
            // costs nothing when there is nothing to find; CancelSynchronousIo is what
            // frees a thread sitting in a blocking ReadFile on a non-overlapped handle.
            if (HANDLE th = c->servingThread.load(std::memory_order_acquire))
                CancelSynchronousIo(th);
        }
    };

    bool listenerParked;
    int cancelAccepted = 0, cancelNotFound = 0;
    {
        std::lock_guard<std::mutex> lock(m_connMutex);
        listenerParked = (m_listenPipe != INVALID_HANDLE_VALUE);
        cancelLiveConns(cancelAccepted, cancelNotFound);
    }
    // One line per Stop — cold, and it is the line that was missing when the
    // 2026-08-05 drain timeout could say the threads were "idle in ReadFile (the
    // I/O cancel should have freed it)" but not whether the cancel had anything
    // to free. Those are different bugs.
    if (cancelAccepted || cancelNotFound) {
        LOG_INFO("PipeServer: Stop cancel issued: %d accepted, %d had nothing pending",
                 cancelAccepted, cancelNotFound);
    }

    // Wake the accept thread by COMPLETING its ConnectNamedPipe instead of closing
    // its handle: connect to our own pipe and immediately drop it. m_running is
    // already false, so AcceptLoop takes its !m_running branch, closes its OWN
    // handle and breaks — which also repairs a leak this function used to cause,
    // since nulling m_listenPipe made AcceptLoop's `if (m_listenPipe == pipe)`
    // guard fail and the instance was never closed by anyone.
    //
    // Outside the lock on purpose: the connect can block briefly, and AcceptLoop
    // needs m_connMutex to finish. Gated on listenerParked so we do not poke a
    // DIFFERENT process's server — the pipe name is machine-global, so a second
    // instrumented game would otherwise see a phantom connect.
    if (listenerParked) {
        HANDLE poke = CreateFileW(Grimoire::PIPE_NAME, GENERIC_READ, 0, nullptr,
                                  OPEN_EXISTING, 0, nullptr);
        if (poke != INVALID_HANDLE_VALUE) {
            CloseHandle(poke);
        } else {
            // ERROR_FILE_NOT_FOUND: the instance is already gone. ERROR_PIPE_BUSY:
            // every instance is taken, so nothing is parked in ConnectNamedPipe.
            // Both mean there is nothing to wake — not a failure.
            LOG_INFO("PipeServer: Stop wake-poke found no parked listener (err=%lu)", GetLastError());
        }
    }

    LOG_INFO("PipeServer: Stop cancels+wake done (%lld ms)", elapsedMs());

    // Stop watches AFTER cancelling connection I/O so a watch-thread WriteFile
    // can't deadlock its own join on a stuck pipe. This is the ONLY StopAllWatches
    // call — the duplicate that used to sit before the cancel block was a leftover
    // from before the Path A refactor, and it is exactly the ordering this comment
    // says not to use.
    StopAllWatches();

    // Join background scan threads. These joins are UNBOUNDED — the comment here used
    // to claim RunScan/RunRescan were "bounded AOB scans", but RunRescan runs Genau's
    // Extra Scan, a full .data sweep. What actually bounds them is Tot::RequestShutdown()
    // above plus the cancel polls Genau's loops now carry; without those, and since
    // UE5_Shutdown runs on the CE Lua caller's thread, CE's UI froze for the remainder
    // of the sweep. (B18)
    m_rescan.running.store(false);
    if (m_rescan.scanThread.joinable()) {
        m_rescan.scanThread.join();
    }
    m_scan.running.store(false);
    if (m_scan.scanThread.joinable()) {
        m_scan.scanThread.join();
    }
    LOG_INFO("PipeServer: Stop watches+scan joins done (%lld ms)", elapsedMs());

    // Wait for every connection thread to self-unregister (they are detached;
    // they erase themselves from m_conns + notify on exit). Bounded so a wedged
    // handler can't hang shutdown forever.
    bool drained;
    size_t connsLeft;
    int reasserts = 0;
    {
        std::unique_lock<std::mutex> lock(m_connMutex);
        // Slice the 5 s budget and RE-ASSERT the cancel each slice instead of
        // waiting on one shot fired before the wait began. A thread parked in
        // ReadFile that the first cancel missed (it was between reads) is
        // otherwise unreachable for the whole budget — which is exactly what the
        // 2026-08-05 capture showed: 2 stragglers, both "idle in ReadFile", 5002 ms.
        // Same write-on-drift shape as the re-assert workers: assert the state you
        // want repeatedly rather than assuming one assertion landed.
        //
        // Cheap: each slice is one CancelIoEx per surviving connection, and the
        // loop only runs while connections survive — the common case exits on the
        // first wait with zero re-asserts.
        const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(5);
        for (;;) {
            drained = m_connCv.wait_for(lock, std::chrono::milliseconds(Grimoire::PIPE_STOP_CANCEL_REASSERT_MS),
                                        [this] { return m_conns.empty(); });
            if (drained || std::chrono::steady_clock::now() >= deadline) break;
            int acc = 0, nf = 0;
            cancelLiveConns(acc, nf);   // safe: lock is held again after wait_for returns
            ++reasserts;
        }
        connsLeft = m_conns.size();
        // Name the stragglers while we still hold the registry lock. Without this the
        // timeout says only "N left", which is not enough to act on — the 2026-08-04
        // run burned the full 5 s budget and the log could not say whether the threads
        // were parked in ReadFile (the cancel missed them) or stuck inside a command
        // (the cancel cannot help until it returns). Those need opposite fixes.
        if (!drained) {
            long long nowMs = std::chrono::duration_cast<std::chrono::milliseconds>(
                std::chrono::steady_clock::now().time_since_epoch()).count();
            for (const auto& c : m_conns) {
                if (!c) continue;
                std::string what;
                { std::lock_guard<std::mutex> dlk(c->diagMutex); what = c->cmdName; }
                Connection::Phase ph = c->phase.load(std::memory_order_relaxed);
                long long phStart = c->phaseStartMs.load(std::memory_order_relaxed);
                long long started = c->cmdStartMs.load(std::memory_order_relaxed);
                (void)started;
                // Build the age into a NAMED local. The obvious one-liner passes
                // .c_str() of a temporary through a ternary — legal (the temporary
                // outlives the full-expression) but not worth making a reader verify.
                std::string age;
                if (phStart > 0) age = " for " + std::to_string(nowMs - phStart) + " ms";
                LOG_WARN("PipeServer:   straggler: %s%s, last cmd '%s'",
                         PhaseName(ph), age.c_str(),
                         what.empty() ? "(none yet)" : what.c_str());
            }
        }
    }
    // Distinguish "satisfied" from "timed out" explicitly: a 5 s stall elsewhere
    // and a 5 s expiry of THIS wait produce identical wall-clock, and telling them
    // apart from the outside is what made the 2026-08-04 investigation expensive.
    // The re-assert count is the ONLY externally visible difference between "the
    // first cancel worked" and "the first cancel missed and a later one caught it".
    // Both print `satisfied`; without the count a fix for the missed-window case
    // could not be told from it never having been needed.
    LOG_INFO("PipeServer: Stop conn drain %s, %zu left (%lld ms, %d cancel re-assert%s)",
             drained ? "satisfied" : "TIMEOUT", connsLeft, elapsedMs(),
             reasserts, reasserts == 1 ? "" : "s");

    if (m_acceptThread.joinable()) {
        m_acceptThread.join();
    }
    LOG_INFO("PipeServer: Stop accept join done (%lld ms)", elapsedMs());
    if (m_monitorThread.joinable()) {
        m_monitorThread.join();
    }
    LOG_INFO("PipeServer: Stop monitor join done (%lld ms)", elapsedMs());

    // No handler thread is running now — free every remaining value-scan session.
    Radar::SessionManager::Instance().DropAll();
    Radar::GroupSessionManager::Instance().DropAll();
    Linie::Reset();   // drop any live PE-profile recording + free the table
    Ubel::ClearNameCache();   // same reason as the last-connection teardown (D5/F3)

    m_clientConnected = false;
    LOG_INFO("PipeServer: Stopped");
}

// Disconnect monitor: while a command is in-flight, peek the pipe to detect
// the client vanishing and request per-command cancellation so the orphaned
// scan bails. Peeks only while m_commandInFlight (handler is CPU-bound in
// DispatchCommand, not touching the pipe) — no concurrent read/write.
void Fern::MonitorLoop() {
  // Allocates while peeking the pipe and formatting logs; a raw thread proc. (B14)
  Routine::RunThreadGuarded("PipeServer: MonitorLoop", [&] {
    while (m_running.load()) {
        std::this_thread::sleep_for(std::chrono::milliseconds(200));
        if (!m_running.load()) break;

        // Snapshot the in-flight connections (keep them alive via shared_ptr for
        // the peek). Only peek while inFlight — the thread is then CPU-bound in
        // DispatchCommand, NOT in ReadFile/WriteFile and NOT closing its handle,
        // so the peek can't race the connection's own I/O or close.
        std::vector<std::shared_ptr<Connection>> inflight;
        {
            std::lock_guard<std::mutex> lock(m_connMutex);
            for (auto& c : m_conns) {
                if (c->inFlight.load(std::memory_order_relaxed)
                    && !c->closed.load(std::memory_order_relaxed)
                    && c->pipe != INVALID_HANDLE_VALUE)
                    inflight.push_back(c);
            }
        }

        for (auto& c : inflight) {
            if (c->closed.load(std::memory_order_relaxed)) continue;
            HANDLE p = c->pipe;
            if (p == INVALID_HANDLE_VALUE) continue;

            // Non-destructive: PeekNamedPipe reads only buffer state. A closed
            // client end surfaces as FALSE + ERROR_BROKEN_PIPE.
            DWORD avail = 0;
            if (!PeekNamedPipe(p, nullptr, 0, nullptr, &avail, nullptr)) {
                DWORD e = GetLastError();
                if (e == ERROR_BROKEN_PIPE || e == ERROR_PIPE_NOT_CONNECTED
                    || e == ERROR_INVALID_HANDLE) {
                    // Global per-command cancel: only the bulk lane runs
                    // cancellable scans, and a fast light command finishes before
                    // a 200ms peek catches it in-flight, so in practice this only
                    // ever fires for a broken bulk scan. The UI must reconnect
                    // BOTH lanes together so g_perCommand is reset (AcceptLoop
                    // firstConn) before the next session's scans.
                    if (!Tot::g_perCommand.load(std::memory_order_relaxed)) {
                        LOG_WARN("PipeServer: client gone mid-command (err=%lu) — aborting in-flight op", e);
                        Tot::RequestPerCommand();
                    }
                }
            }
        }
    }
  });
}

void Fern::AcceptLoop() {
  // The accept thread allocates (Connection, the registry, log formatting). A throw here
  // is std::terminate — and it also silently ends the server, so the guard reports it. (B14)
  Routine::RunThreadGuarded("PipeServer: AcceptLoop", [&] {
    while (m_running.load()) {
        // Create a new pipe instance (multi-instance: up to kMaxPipeInstances
        // concurrent clients, so the UI's interactive + bulk lanes can connect
        // at once). Synchronous handle (no FILE_FLAG_OVERLAPPED) — safe here
        // because each connection is served by exactly ONE thread doing serial
        // read→dispatch→write on its own handle (no same-handle deadlock).
        HANDLE pipe = CreateNamedPipeW(
            Grimoire::PIPE_NAME,
            PIPE_ACCESS_DUPLEX,
            PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT,
            kMaxPipeInstances,
            Grimoire::PIPE_BUF_SIZE,
            Grimoire::PIPE_BUF_SIZE,
            0,                          // Default timeout
            nullptr                     // Default security
        );

        if (pipe == INVALID_HANDLE_VALUE) {
            LOG_ERROR("PipeServer: CreateNamedPipe failed (err=%lu)", GetLastError());
            std::this_thread::sleep_for(std::chrono::seconds(1));
            continue;
        }

        {
            std::lock_guard<std::mutex> lock(m_connMutex);
            m_listenPipe = pipe;
        }
        LOG_INFO("PipeServer: Waiting for client connection...");

        // Wait for a client to connect on this instance.
        BOOL connected = ConnectNamedPipe(pipe, nullptr);
        if (!connected && GetLastError() != ERROR_PIPE_CONNECTED) {
            // Stop() closed m_listenPipe to unblock us, or a real error.
            std::lock_guard<std::mutex> lock(m_connMutex);
            if (m_listenPipe == pipe) { CloseHandle(pipe); m_listenPipe = INVALID_HANDLE_VALUE; }
            if (!m_running.load()) break;
            LOG_ERROR("PipeServer: ConnectNamedPipe failed (err=%lu)", GetLastError());
            continue;
        }

        if (!m_running.load()) {
            std::lock_guard<std::mutex> lock(m_connMutex);
            if (m_listenPipe == pipe) { CloseHandle(pipe); m_listenPipe = INVALID_HANDLE_VALUE; }
            break;
        }

        // Hand this connected instance to a Connection served on its own thread.
        auto conn = std::make_shared<Connection>();
        conn->pipe = pipe;
        bool firstConn;
        size_t connCount;
        {
            std::lock_guard<std::mutex> lock(m_connMutex);
            m_listenPipe = INVALID_HANDLE_VALUE;   // consumed by this connection
            firstConn = m_conns.empty();
            m_conns.push_back(conn);
            connCount = m_conns.size();
            m_clientConnected = true;
        }
        // New session (registry was empty): clear any per-command cancel left by
        // the prior fully-disconnected session. NOT done per-command, so a light
        // command on one lane can't clear a running scan's cancel on the other.
        if (firstConn) Tot::ResetPerCommand();

        LOG_INFO("PipeServer: Client connected (conns=%zu)", connCount);
        // Guarded: only DispatchCommand inside HandleConnection had a handler, so a
        // throw from ReadLine / the JSON pre-parse / WriteLine escaped a DETACHED
        // thread — std::terminate, i.e. the game dies. (B14)
        std::thread([this, conn]() {
            Routine::RunThreadGuarded("PipeServer: connection",
                                      [&] { HandleConnection(conn); });
        }).detach();
    }
    LOG_INFO("PipeServer: AcceptLoop exiting");
  });
}

const char* Fern::PhaseName(Connection::Phase p) {
    switch (p) {
        case Connection::Phase::Reading:         return "parked in ReadFile (waiting for the next command)";
        case Connection::Phase::Dispatching:     return "INSIDE a command (cancel cannot reach it until it returns)";
        case Connection::Phase::Writing:         return "in WriteLine — WriteFile to a client that may have stopped reading, or waiting on writeMutex";
        case Connection::Phase::StoppingWatches: return "in cleanup, JOINING its watch threads (an I/O cancel on this thread does nothing)";
        case Connection::Phase::Unregistering:   return "in cleanup, unregistering — should be about to leave";
        default:                                 return "done";
    }
}

/// Stamp the current phase + when it began, for Stop's drain diagnostic only.
void Fern::SetPhase(Connection& conn, Connection::Phase p) {
    conn.phase.store(p, std::memory_order_relaxed);
    conn.phaseStartMs.store(
        std::chrono::duration_cast<std::chrono::milliseconds>(
            std::chrono::steady_clock::now().time_since_epoch()).count(),
        std::memory_order_relaxed);
}

std::string Fern::ReadLine(HANDLE pipe) {
    std::string line;
    char ch;
    DWORD bytesRead;

    while (m_running.load()) {
        if (!ReadFile(pipe, &ch, 1, &bytesRead, nullptr) || bytesRead == 0) {
            return ""; // Disconnected or error
        }
        if (ch == '\n') {
            // Strip trailing \r if present
            if (!line.empty() && line.back() == '\r') {
                line.pop_back();
            }
            return line;
        }
        line += ch;

        // Safety: don't let a line grow unbounded
        if (line.size() > Grimoire::PIPE_BUF_SIZE) {
            Sein::Warn("PIPE:cmd", "PipeServer: Line too long, dropping");
            return "";
        }
    }
    return "";
}

bool Fern::WriteLine(Connection& conn, const std::string& line) {
    // Per-connection write mutex: serializes this connection's response write
    // against its own watch-event writes. Writes to OTHER connections use their
    // own mutex, so the two UI lanes never contend. CloseConnOnce takes the same
    // mutex, so a write can never run on a just-closed handle.
    std::lock_guard<std::mutex> lock(conn.writeMutex);
    if (conn.closed.load(std::memory_order_relaxed) || conn.pipe == INVALID_HANDLE_VALUE)
        return false;
    std::string data = line + "\n";
    DWORD written;
    return WriteFile(conn.pipe, data.c_str(), static_cast<DWORD>(data.size()), &written, nullptr) != 0;
}

// Close a connection's handle exactly once. Called by the connection's OWN
// thread in HandleConnection cleanup (after its watches are stopped), so the
// writeMutex here is uncontended; it still guards against a stray in-flight
// write. Stop() never closes connection handles — it CancelIoEx's them and lets
// each owning thread close its own.
void Fern::CloseConnOnce(Connection& conn) {
    if (conn.closed.exchange(true)) return;
    std::lock_guard<std::mutex> lock(conn.writeMutex);
    if (conn.pipe != INVALID_HANDLE_VALUE) {
        DisconnectNamedPipe(conn.pipe);
        CloseHandle(conn.pipe);
        conn.pipe = INVALID_HANDLE_VALUE;
    }
}

void Fern::HandleConnection(std::shared_ptr<Connection> conn) {
    HANDLE pipe = conn->pipe;

    // Publish a real handle to THIS thread so Stop can cancel the synchronous ReadFile
    // it will spend almost all its life parked in. GetCurrentThread() is a pseudo-handle
    // that always means "the calling thread", so it is useless to anyone else -- it has
    // to be duplicated into a genuine one. Closed in the cleanup below.
    {
        HANDLE dup = nullptr;
        if (DuplicateHandle(GetCurrentProcess(), GetCurrentThread(),
                            GetCurrentProcess(), &dup,
                            THREAD_TERMINATE, FALSE, DUPLICATE_SAME_ACCESS)) {
            conn->servingThread.store(dup, std::memory_order_release);
        } else {
            Sein::Warn("PIPE:cmd", "PipeServer: could not duplicate serving-thread handle "
                       "(err=%lu) — this connection cannot be woken out of ReadFile on Stop",
                       GetLastError());
        }
    }

    // Batch-suppress repetitive command logging (e.g. 244 x get_object_list)
    std::string lastCmd;
    int repeatCount = 0;

    auto flushRepeat = [&]() {
        if (repeatCount > 1) {
            Sein::Debug("PIPE:cmd", "PipeServer: ... repeated %dx: %s", repeatCount, lastCmd.c_str());
        }
        repeatCount = 0;
        lastCmd.clear();
    };

    while (m_running.load()) {
        SetPhase(*conn, Connection::Phase::Reading);
        std::string line = ReadLine(pipe);
        if (line.empty()) { flushRepeat(); break; } // Disconnected / I/O cancelled

        // Extract command name for dedup (fast: find "cmd":" in JSON)
        std::string cmd;
        auto pos = line.find("\"cmd\":\"");
        if (pos != std::string::npos) {
            auto start = pos + 7;
            auto end = line.find('"', start);
            if (end != std::string::npos) cmd = line.substr(start, end - start);
        }

        if (cmd == lastCmd && !cmd.empty()) {
            ++repeatCount;
        } else {
            flushRepeat();
            Sein::Debug("PIPE:cmd", "PipeServer: Received: %s", line.c_str());
            lastCmd = cmd;
            repeatCount = 1;
        }

        // Mark in-flight so the monitor can peek THIS connection for a
        // mid-command disconnect (a long bulk scan blocks this thread). inFlight
        // is set only around DispatchCommand (CPU-bound; not touching the pipe),
        // never around ReadLine/WriteLine, so the monitor's peek never races I/O.
        // Time exactly the inFlight window. That span is the CPU-bound,
        // pipe-free stretch during which this connection's dispatcher is
        // unavailable to any other command — i.e. precisely the head-of-line
        // blocking docs/multipipe-eval.md blames for UI lag, which until now
        // nothing measured. See Sense.h.
        conn->inFlight.store(true, std::memory_order_relaxed);
        SetPhase(*conn, Connection::Phase::Dispatching);
        // Record WHICH command, for Stop's drain-timeout diagnostic only. `cmd` is
        // already parsed above for the repeat-suppression, so this costs one small
        // string copy per command and nothing on any read path.
        {
            std::lock_guard<std::mutex> dlk(conn->diagMutex);
            conn->cmdName = cmd;
        }
        conn->cmdStartMs.store(
            std::chrono::duration_cast<std::chrono::milliseconds>(
                std::chrono::steady_clock::now().time_since_epoch()).count(),
            std::memory_order_relaxed);
        // QPC, not GetTickCount64: its ~15.6 ms granularity floored every sub-tick
        // dispatch to 0, so a live run of 1397 walk_instance calls reported
        // "0 ms total, max 15 ms" — an artefact of which calls straddled a tick,
        // not a measurement. Most pipe commands sit far below that granularity.
        const uint64_t dispatchStart = Sense::NowTicks();
        std::string response = DispatchCommand(conn, line);
        const uint64_t dispatchUs = Sense::TicksToUs(Sense::NowTicks() - dispatchStart);
        conn->inFlight.store(false, std::memory_order_relaxed);
        Sense::RecordDispatch(cmd.empty() ? std::string("(unparsed)") : cmd, dispatchUs);

        if (!response.empty()) {
            SetPhase(*conn, Connection::Phase::Writing);
            if (!WriteLine(*conn, response)) {
                flushRepeat();
                Sein::Error("PIPE:cmd", "PipeServer: Failed to write response");
                break;
            }
        }
    }
    conn->inFlight.store(false, std::memory_order_relaxed);

    // Connection cleanup. Stop THIS connection's watches first (joins their
    // threads while the handle is still valid + owner pointer alive), then
    // unregister and close our own handle. Drop value-scan sessions only when
    // this was the LAST connection (sessions are bulk-lane-only; the UI's two
    // lanes disconnect together on close).
    SetPhase(*conn, Connection::Phase::StoppingWatches);
    StopWatchesForConnection(conn.get());

    SetPhase(*conn, Connection::Phase::Unregistering);
    bool last;
    {
        std::lock_guard<std::mutex> lock(m_connMutex);
        auto it = std::find(m_conns.begin(), m_conns.end(), conn);
        if (it != m_conns.end()) m_conns.erase(it);
        last = m_conns.empty();
        m_clientConnected = !m_conns.empty();
    }

    CloseConnOnce(*conn);

    // Release the duplicated thread handle. Exchange first so Stop, which reads it
    // under m_connMutex, can never see a handle this thread has already closed --
    // and by here we are past the erase from m_conns, so Stop cannot be iterating us.
    if (HANDLE th = conn->servingThread.exchange(nullptr, std::memory_order_acq_rel))
        CloseHandle(th);

    if (last) {
        Radar::SessionManager::Instance().DropAll();
        Radar::GroupSessionManager::Instance().DropAll();
        Linie::Reset();   // last client gone — drop any live PE-profile recording
        Sense::Reset();   // ...and restart diagnostics so the next session's numbers are its own
        // Un-hide any see-through occluders + stop its worker — the header contract is
        // "un-hidden on disable / disconnect", and there's no UI left to toggle it.
        // Cheap no-op when see-through was never enabled. (M3)
        Schlacht::SetEnabled(false);
        // Ubel's per-UObject name cache is keyed by a raw address with no
        // generation/serial and is never revalidated on hit, so once UE recycles a
        // UObject slot every name-bearing reply serves the DESTROYED object's name.
        // Its only two purge sites were begin_snapshot and trigger_scan — neither
        // reachable from ordinary browsing — so this teardown dropped every other
        // per-session resource and left the one that can serve wrong data. A UI
        // reconnect is now a full reset. (audit #5 D5/F3)
        //
        // This does NOT fix the in-session case (a level change while connected);
        // that needs the (InternalIndex, SerialNumber) witness that cluster ③ shares
        // with D1/U4-U6 and D3/A10, and is deliberately left to that pass.
        //
        // Cost is a lazy repopulate on the next connect, which is what every other
        // line in this block already accepts.
        Ubel::ClearNameCache();
    }

    m_connCv.notify_all();
    LOG_INFO("PipeServer: Client disconnected");
}

// (PushEvent removed in the Path A multi-connection refactor — watch events now
// write directly to their owning connection's handle; see StartWatch below.)

// ============================================================
// FillPointerSnapshot — Populate a JSON object with the engine pointer state
// (GObjects/GNames/GWorld + scan metadata + per-game settings).
//
// Shared by CMD_GET_POINTERS and CMD_SCAN_STATUS (completion). Without this
// shared helper the two paths drift — sparse_delegates was added to GET_POINTERS
// alone in the PR-#194 first iteration and the panel was empty post-trigger_scan
// because the UI consumes scan_status, not get_pointers, after a fresh scan.
// invoke_timeout_ms / is_user_override / is_low_confidence / publisher_thumbprint
// hit the same trap (UI showed defaults instead of the real values until a manual
// re-fetch). One helper, two call sites — bug class closed.
// ============================================================
static void FillPointerSnapshot(json& data) {
    extern uintptr_t   g_cachedGObjects;
    extern uintptr_t   g_cachedGNames;
    extern uintptr_t   g_cachedGWorld;
    extern uintptr_t   g_cachedSparseDelegates;
    extern uint32_t    g_cachedUEVersion;
    extern bool        g_cachedVersionDetected;
    extern bool        g_cachedIsUserOverride;
    extern bool        g_cachedIsLowConfidence;
    extern bool        g_cachedVersionTooOld;
    extern const char* g_cachedPublisherThumbprint;
    extern const char* g_cachedGObjectsMethod;
    extern const char* g_cachedGNamesMethod;
    extern const char* g_cachedGWorldMethod;
    extern const char* g_cachedSparseDelegatesMethod;
    extern char        g_cachedPeHash[17];
    extern const char* g_cachedGObjectsPatternId;
    extern const char* g_cachedGNamesPatternId;
    extern const char* g_cachedGWorldPatternId;
    extern const char* g_cachedSparseDelegatesPatternId;
    extern int         g_cachedGObjectsTried, g_cachedGObjectsHit;
    extern int         g_cachedGNamesTried,   g_cachedGNamesHit;
    extern int         g_cachedGWorldTried,   g_cachedGWorldHit;
    extern uintptr_t   g_cachedGObjectsScanAddr;
    extern uintptr_t   g_cachedGNamesScanAddr;
    extern uintptr_t   g_cachedGWorldScanAddr;
    extern uintptr_t   g_cachedSparseDelegatesScanAddr;
    extern const char* g_cachedGWorldAob;
    extern int         g_cachedGWorldAobPos;
    extern int         g_cachedGWorldAobLen;
    extern uintptr_t   g_cachedGEngine;
    extern const char* g_cachedGEngineMethod;
    extern const char* g_cachedGEnginePatternId;
    extern uintptr_t   g_cachedGEngineScanAddr;
    extern const char* g_cachedGEngineAob;
    extern int         g_cachedGEngineAobPos;
    extern int         g_cachedGEngineAobLen;

    data["gobjects"]             = Renge::AddrToStr(g_cachedGObjects);
    data["gnames"]               = Renge::AddrToStr(g_cachedGNames);
    data["gworld"]               = Renge::AddrToStr(g_cachedGWorld);
    data["sparse_delegates"]     = Renge::AddrToStr(g_cachedSparseDelegates);
    data["ue_version"]           = g_cachedUEVersion;
    data["version_detected"]     = g_cachedVersionDetected;
    data["is_user_override"]     = g_cachedIsUserOverride;
    data["is_low_confidence"]    = g_cachedIsLowConfidence;
    data["is_version_too_old"]   = g_cachedVersionTooOld;
    // build_number: compile-time DLL build (e.g. 648). Also emitted on the
    // init response; surfacing it on every snapshot lets the UI's
    // get_pointers refreshes preserve the value across panel state rebuilds.
    data["build_number"]         = BuildStamp::BuildNumber();
    data["publisher_thumbprint"] = g_cachedPublisherThumbprint ? g_cachedPublisherThumbprint : "";
    data["object_count"]         = Aura::GetCount();
    // FUObjectItem layout (so the UI can flag the *** UNVERIFIED *** UE5.7+ packed mode).
    //   classic    — UObject* at item+0x00  (UE4.x..UE5.6)
    //   unpacked57 — UObject* at item+0x08  (UE5.7+ reordered, direct ptr)
    //   packed57   — UObject* reconstructed from two split fields (UNVERIFIED)
    data["item_packed"]          = Aura::IsPacked();
    data["item_obj_offset"]      = Aura::GetItemObjOffset();
    data["item_size"]            = Aura::GetItemSize();
    data["item_layout_mode"]     = Aura::IsPacked() ? "packed57"
                                   : (Aura::GetItemObjOffset() != 0 ? "unpacked57" : "classic");
    data["gobjects_method"]         = g_cachedGObjectsMethod;
    data["gnames_method"]           = g_cachedGNamesMethod;
    data["gworld_method"]           = g_cachedGWorldMethod;
    data["sparse_delegates_method"] = g_cachedSparseDelegatesMethod;

    data["pe_hash"]                     = g_cachedPeHash;
    data["gobjects_pattern_id"]         = g_cachedGObjectsPatternId        ? g_cachedGObjectsPatternId        : "";
    data["gnames_pattern_id"]           = g_cachedGNamesPatternId          ? g_cachedGNamesPatternId          : "";
    data["gworld_pattern_id"]           = g_cachedGWorldPatternId          ? g_cachedGWorldPatternId          : "";
    data["sparse_delegates_pattern_id"] = g_cachedSparseDelegatesPatternId ? g_cachedSparseDelegatesPatternId : "";
    json scanStats;
    scanStats["gobjects_tried"] = g_cachedGObjectsTried;
    scanStats["gobjects_hit"]   = g_cachedGObjectsHit;
    scanStats["gnames_tried"]   = g_cachedGNamesTried;
    scanStats["gnames_hit"]     = g_cachedGNamesHit;
    scanStats["gworld_tried"]   = g_cachedGWorldTried;
    scanStats["gworld_hit"]     = g_cachedGWorldHit;
    data["scan_stats"] = scanStats;

    data["gobjects_scan_addr"]         = Renge::AddrToStr(g_cachedGObjectsScanAddr);
    data["gnames_scan_addr"]           = Renge::AddrToStr(g_cachedGNamesScanAddr);
    data["gworld_scan_addr"]           = Renge::AddrToStr(g_cachedGWorldScanAddr);
    data["sparse_delegates_scan_addr"] = Renge::AddrToStr(g_cachedSparseDelegatesScanAddr);

    data["gworld_aob"]     = g_cachedGWorldAob ? g_cachedGWorldAob : "";
    data["gworld_aob_pos"] = g_cachedGWorldAobPos;
    data["gworld_aob_len"] = g_cachedGWorldAobLen;

    // &GEngine (the slot, not the object). Empty aob == no AOB hit, in which case the UI
    // must treat a GameEngine-rooted export the way it treats a recovered GWorld: address
    // only, no restart-proof CE symbol.
    data["gengine"]            = Renge::AddrToStr(g_cachedGEngine);
    data["gengine_method"]     = g_cachedGEngineMethod    ? g_cachedGEngineMethod    : "not_found";
    data["gengine_pattern_id"] = g_cachedGEnginePatternId ? g_cachedGEnginePatternId : "";
    data["gengine_scan_addr"]  = Renge::AddrToStr(g_cachedGEngineScanAddr);
    data["gengine_aob"]        = g_cachedGEngineAob ? g_cachedGEngineAob : "";
    data["gengine_aob_pos"]    = g_cachedGEngineAobPos;
    data["gengine_aob_len"]    = g_cachedGEngineAobLen;

    data["invoke_timeout_ms"] = Stark::GetInvokeTimeoutMs();

    uintptr_t moduleBase = Macht::GetModuleBase(nullptr);
    data["module_base"] = Renge::AddrToStr(moduleBase);
    wchar_t moduleNameW[MAX_PATH] = {};
    GetModuleFileNameW(reinterpret_cast<HMODULE>(moduleBase), moduleNameW, MAX_PATH);
    std::wstring modulePath(moduleNameW);
    auto lastSlash = modulePath.find_last_of(L"\\/");
    std::wstring moduleFileName = (lastSlash != std::wstring::npos)
        ? modulePath.substr(lastSlash + 1) : modulePath;
    std::string moduleName;
    for (wchar_t wc : moduleFileName) {
        moduleName += (wc < 128) ? static_cast<char>(wc) : '?';
    }
    data["module_name"] = moduleName;
    // The ONLY unambiguous answer to "which process is this pipe talking to". The pipe name
    // is a single global, so two injected games both serve it and a connecting client lands on
    // whichever instance is free -- the UI cannot otherwise tell it attached to the wrong game.
    data["pid"] = static_cast<uint32_t>(GetCurrentProcessId());

    // load_mode: how the dumper got into this process, from THIS module's own file
    // name (g_hDllModule). "proxy:version.dll" | "proxy:dinput8.dll" | "proxy:dxgi.dll"
    // (the OS-loaded proxy — the mutex winner, correct even when 2 proxies coexist) /
    // "injected" (UE5Dumper.dll via CreateRemoteThread or CE .CT) / "loaded:<name>" /
    // "unknown". The UI folds a proxy load into per-game "confirmed-working" LKG.
    {
        std::string selfName;
        wchar_t selfPathW[MAX_PATH] = {};
        if (g_hDllModule && GetModuleFileNameW(g_hDllModule, selfPathW, MAX_PATH)) {
            std::wstring selfPath(selfPathW);
            auto ss = selfPath.find_last_of(L"\\/");
            std::wstring selfFile = (ss != std::wstring::npos) ? selfPath.substr(ss + 1) : selfPath;
            for (wchar_t wc : selfFile)
                selfName += (wc < 128) ? static_cast<char>(towlower(wc)) : '?';
        }
        std::string loadMode;
        if (selfName == "version.dll" || selfName == "dinput8.dll" || selfName == "dxgi.dll")
            loadMode = "proxy:" + selfName;
        else if (selfName == "ue5dumper.dll")
            loadMode = "injected";
        else if (!selfName.empty())
            loadMode = "loaded:" + selfName;
        else
            loadMode = "unknown";
        data["load_mode"] = loadMode;
    }

    // Per-launch session token: the game process's creation time (FILETIME,
    // 100ns intervals since 1601, hi:lo packed → hex). Unique per launch even
    // when the EXE loads at a CONSTANT base (no effective ASLR, e.g. SEED) —
    // module_base alone could NOT distinguish launches on such games, so the
    // Snapshot/SPC stale-session gate (PeHash-CreationTime) folds this in. The
    // DLL runs in-process, so GetCurrentProcess() is the game. (build 1227)
    FILETIME ftCreate{}, ftExit{}, ftKernel{}, ftUser{};
    std::string creationTimeHex = "0";
    if (GetProcessTimes(GetCurrentProcess(), &ftCreate, &ftExit, &ftKernel, &ftUser)) {
        ULARGE_INTEGER ct{};
        ct.LowPart  = ftCreate.dwLowDateTime;
        ct.HighPart = ftCreate.dwHighDateTime;
        std::ostringstream ctOss;
        ctOss << std::hex << std::uppercase << ct.QuadPart;
        creationTimeHex = ctOss.str();
    }
    data["process_creation_time"] = creationTimeHex;
}

// ============================================================
// SerializeField — Convert a LiveFieldValue to JSON.
// Shared by walk_instance and walk_datatable_rows handlers.
//
// `lean` omits the keys a CE XML export provably never reads — see the LEAN
// contract on EncodeInstanceWalkToJson below. It only ever REMOVES keys; every
// key it still emits is byte-identical to the full shape, which is what makes
// "lean and full produce the same XML" testable rather than hoped for.
// ============================================================
static json SerializeField(const Ubel::LiveFieldValue& fv, bool lean = false) {
    json fj;
    fj["name"]   = fv.name;
    fj["type"]   = fv.typeName;
    fj["offset"] = fv.offset;
    fj["size"]   = fv.size;

    // hex/value are the single biggest droppable pair (measured ~15% of the
    // payload): a CE record is structural (description + offset + CE type), so
    // the live VALUE never reaches the XML.
    if (!lean && !fv.hexValue.empty())    fj["hex"]   = fv.hexValue;
    if (!lean && !fv.typedValue.empty())  fj["value"] = fv.typedValue;
    if (fv.guessed)                       fj["guessed"] = true;

    // ObjectProperty: pointer info
    if (fv.ptrValue != 0) {
        fj["ptr"]       = Renge::AddrToStr(fv.ptrValue);
        // ptr_name is display-only; the export labels a pointer leaf with the
        // pointed-to CLASS, never its object name.
        if (!lean)
            fj["ptr_name"]  = fv.ptrName;
        fj["ptr_class"] = fv.ptrClassName;
        if (fv.ptrClassAddr)
            fj["ptr_class_addr"] = Renge::AddrToStr(fv.ptrClassAddr);
    }

    // BoolProperty: bit field info
    if (fv.boolBitIndex >= 0) {
        fj["bool_bit"] = fv.boolBitIndex;
        // mask + byte offset are CSX-only (description text / Binary bit leaf).
        if (!lean) {
            fj["bool_mask"] = fv.boolFieldMask;
            fj["bool_byte_offset"] = fv.boolByteOffset;
        }
    }

    // ArrayProperty: element count + inner type info + inline elements
    if (fv.arrayCount >= 0) {
        fj["count"] = fv.arrayCount;
        if (fv.arrayDataAddr != 0)
            fj["array_data_addr"] = Renge::AddrToStr(fv.arrayDataAddr);
        if (!fv.arrayInnerType.empty()) {
            fj["array_inner_type"] = fv.arrayInnerType;
            if (fv.arrayElemSize > 0)
                fj["array_elem_size"] = fv.arrayElemSize;
            if (!fv.arrayInnerStructType.empty())
                fj["array_struct_type"] = fv.arrayInnerStructType;
            if (fv.arrayInnerStructAddr != 0)
                fj["array_struct_class_addr"] = Renge::AddrToStr(fv.arrayInnerStructAddr);
            // Phase G layout metadata for soft arrays — lets exporters lay out
            // per-element FName leaves at FSoftObjectPath sub-offsets.
            if (fv.softArrayFNameSize > 0) {
                fj["soft_fname_size"] = fv.softArrayFNameSize;
                fj["soft_top_level_asset_path"] = fv.softArrayIsTopLevelAssetPath;
            }
        }
        // array_inner_addr is the Inner FProperty* handle for a follow-up
        // read_array_elements call — no exporter reads it.
        if (!lean && fv.arrayInnerFFieldAddr != 0)
            fj["array_inner_addr"] = Renge::AddrToStr(fv.arrayInnerFFieldAddr);
        // Phase B/D: inline element values (scalar or pointer)
        if (!fv.arrayElements.empty()) {
            json elems = json::array();
            for (const auto& e : fv.arrayElements) {
                json ej;
                ej["i"] = e.index;
                ej["v"] = e.value;
                // Per-element raw hex: the largest single unused key measured
                // (~9% of a whole export payload). Element LABELS come from "v".
                if (!lean) ej["h"] = e.hex;
                if (!e.enumName.empty())
                    ej["en"] = e.enumName;
                if (e.rawIntValue != 0 || !e.enumName.empty())
                    ej["rv"] = e.rawIntValue;
                // Phase D: pointer element fields
                if (e.ptrAddr != 0) {
                    ej["pa"] = Renge::AddrToStr(e.ptrAddr);
                    if (!lean) ej["pn"] = e.ptrName;   // display-only, as above
                    ej["pc"] = e.ptrClassName;
                }
                // Phase F: struct sub-fields
                if (!e.structFields.empty()) {
                    json sfs = json::array();
                    for (const auto& sf : e.structFields) {
                        // "v" (the sub-field's VALUE) is display-only: a CE
                        // sub-leaf is built from n/t/o/s.
                        json sfj = lean
                            ? json{{"n", sf.name}, {"t", sf.typeName},
                                   {"o", sf.offset}, {"s", sf.size}}
                            : json{{"n", sf.name}, {"t", sf.typeName},
                                   {"o", sf.offset}, {"s", sf.size}, {"v", sf.value}};
                        // Pointer resolution for ObjectProperty sub-fields
                        if (sf.ptrAddr != 0) {
                            sfj["pa"] = Renge::AddrToStr(sf.ptrAddr);
                            sfj["pn"] = sf.ptrName;
                            sfj["pc"] = sf.ptrClassName;
                            sfj["pca"] = Renge::AddrToStr(sf.ptrClassAddr);
                        }
                        sfs.push_back(sfj);
                    }
                    ej["sf"] = sfs;
                }
                elems.push_back(ej);
            }
            fj["elements"] = elems;
        }

        // CE DropDownList: full enum entries for this array field
        if (fv.arrayEnumAddr != 0 && !fv.arrayEnumEntries.empty()) {
            fj["enum_addr"] = Renge::AddrToStr(fv.arrayEnumAddr);
            json entries = json::array();
            for (const auto& ee : fv.arrayEnumEntries)
                entries.push_back({{"v", ee.value}, {"n", ee.name}});
            fj["enum_entries"] = entries;
        }
    }

    // MapProperty: key/value type info + inline elements
    if (fv.mapCount >= 0) {
        fj["map_count"]      = fv.mapCount;
        fj["map_key_type"]   = fv.mapKeyType;
        fj["map_value_type"] = fv.mapValueType;
        fj["map_key_size"]   = fv.mapKeySize;
        fj["map_value_size"] = fv.mapValueSize;
        if (fv.mapValueOffset != 0)
            fj["map_value_offset"] = fv.mapValueOffset;
        // The stride this walk used. The UI cannot derive it — the formula needs
        // alignof(Key)/alignof(Value), which never cross the wire — so it must be
        // told, or it re-implements the formula and drifts (audit #5 V2).
        if (fv.mapStride != 0)
            fj["map_stride"] = fv.mapStride;
        if (fv.mapDataAddr != 0)
            fj["map_data_addr"] = Renge::AddrToStr(fv.mapDataAddr);
        if (fv.mapKeyStructAddr != 0) {
            fj["map_key_struct_addr"] = Renge::AddrToStr(fv.mapKeyStructAddr);
            fj["map_key_struct_type"] = fv.mapKeyStructType;
        }
        if (fv.mapValueStructAddr != 0) {
            fj["map_value_struct_addr"] = Renge::AddrToStr(fv.mapValueStructAddr);
            fj["map_value_struct_type"] = fv.mapValueStructType;
        }
        if (!fv.containerElements.empty()) {
            json elems = json::array();
            for (const auto& e : fv.containerElements) {
                json ej;
                ej["i"] = e.index;
                ej["k"] = e.key;
                ej["v"] = e.value;
                // kh is display-only; vh is NOT (the export parses it as a
                // little-endian int for the value DropDownList).
                if (!lean && !e.keyHex.empty()) ej["kh"] = e.keyHex;
                if (!e.valueHex.empty()) ej["vh"] = e.valueHex;
                if (!e.keyPtrName.empty())   ej["kn"] = e.keyPtrName;
                if (e.keyPtrAddr != 0)       ej["ka"] = Renge::AddrToStr(e.keyPtrAddr);
                if (!e.keyPtrClassName.empty()) ej["kc"] = e.keyPtrClassName;
                if (!e.valuePtrName.empty()) ej["vn"] = e.valuePtrName;
                if (e.valuePtrAddr != 0)     ej["va"] = Renge::AddrToStr(e.valuePtrAddr);
                if (!e.valuePtrClassName.empty()) ej["vc"] = e.valuePtrClassName;
                elems.push_back(ej);
            }
            fj["map_elements"] = elems;
        }
    }

    // SetProperty: element type info + inline elements
    if (fv.setCount >= 0) {
        fj["set_count"]     = fv.setCount;
        fj["set_elem_type"] = fv.setElemType;
        fj["set_elem_size"] = fv.setElemSize;
        if (fv.setStride != 0)
            fj["set_stride"] = fv.setStride;   // see map_stride above
        if (fv.setDataAddr != 0)
            fj["set_data_addr"] = Renge::AddrToStr(fv.setDataAddr);
        if (fv.setElemStructAddr != 0) {
            fj["set_elem_struct_addr"] = Renge::AddrToStr(fv.setElemStructAddr);
            fj["set_elem_struct_type"] = fv.setElemStructType;
        }
        if (!fv.containerElements.empty()) {
            json elems = json::array();
            for (const auto& e : fv.containerElements) {
                json ej;
                ej["i"] = e.index;
                ej["k"] = e.key;
                if (!lean && !e.keyHex.empty()) ej["kh"] = e.keyHex;   // display-only
                if (!e.keyPtrName.empty()) ej["kn"] = e.keyPtrName;
                if (e.keyPtrAddr != 0)    ej["ka"] = Renge::AddrToStr(e.keyPtrAddr);
                if (!e.keyPtrClassName.empty()) ej["kc"] = e.keyPtrClassName;
                elems.push_back(ej);
            }
            fj["set_elements"] = elems;
        }
    }

    // StructProperty: inner struct info
    if (fv.structDataAddr != 0) {
        fj["struct_data_addr"]  = Renge::AddrToStr(fv.structDataAddr);
        fj["struct_class_addr"] = Renge::AddrToStr(fv.structClassAddr);
        fj["struct_type"]       = fv.structTypeName;
    }

    // EnumProperty / ByteProperty-with-enum: resolved name, value, and full entries.
    // The resolved name/value are display-only — the export's DropDownList is
    // built from enum_entries + enum_addr, which lean keeps.
    if (!lean && !fv.enumName.empty()) {
        fj["enum_name"]  = fv.enumName;
        fj["enum_value"] = fv.enumValue;
    }
    if (fv.enumAddr != 0 && !fv.enumEntries.empty()) {
        fj["enum_addr"] = Renge::AddrToStr(fv.enumAddr);
        json enumEntries = json::array();
        for (const auto& ee : fv.enumEntries)
            enumEntries.push_back({{"v", ee.value}, {"n", ee.name}});
        fj["enum_entries"] = enumEntries;
    }

    // StrProperty: decoded string value. The export emits an FString leaf
    // structurally (pointer deref + Unicode/CodePage flags), never the text.
    if (!lean && !fv.strValue.empty()) {
        fj["str_value"] = fv.strValue;
    }

    return fj;
}

std::string Fern::DispatchCommand(const std::shared_ptr<Connection>& conn, const std::string& jsonLine) {
    json request;
    try {
        request = json::parse(jsonLine);
    } catch (const json::exception& e) {
        Sein::Error("PIPE:cmd", "PipeServer: JSON parse error: %s", e.what());
        return Renge::MakeError(0, "Invalid JSON").dump();
    }

    // Parse id/cmd INSIDE the try: a syntactically-valid but non-object request
    // (e.g. "42" or "[1]"), or a wrongly-typed "id"/"cmd", makes json::value()
    // throw json::type_error. Outside the try that escaped HandleClient ->
    // AcceptLoop -> std::terminate -> game crash. Defaults (0 / "") keep the
    // catch handler usable when the throw happens before assignment.
    int id = 0;
    std::string cmd;

    try {
        id  = request.value("id", 0);
        cmd = request.value("cmd", "");

        if (cmd == Renge::CMD_INIT) {
            extern uint32_t g_cachedUEVersion;
            extern bool     g_cachedVersionDetected;
            extern bool     g_cachedIsUserOverride;
            extern bool     g_cachedIsLowConfidence;
            extern const char* g_cachedPublisherThumbprint;
            json data;
            data["ue_version"]       = g_cachedUEVersion;
            data["version_detected"] = g_cachedVersionDetected;
            data["is_user_override"] = g_cachedIsUserOverride;
            data["is_low_confidence"] = g_cachedIsLowConfidence;
            data["publisher_thumbprint"] = g_cachedPublisherThumbprint
                ? g_cachedPublisherThumbprint : "";
            data["build_git"]    = BuildStamp::GitShort();
            data["build_hash"]   = BuildStamp::GitHash();
            data["build_time"]   = BuildStamp::Timestamp();
            data["build_info"]   = BuildStamp::VersionString();
            // build_number: VER_BUILD as integer (e.g. 648). UI compares against
            // its own bundled build to detect "DLL not redeployed after rebuild"
            // — common gotcha in proxy mode, where the user updates the UI but
            // forgets to copy the new DLL into the game's Binaries\Win64\ folder.
            data["build_number"] = BuildStamp::BuildNumber();
            return Renge::MakeResponse(id, data).dump();
        }

        // ─────────────────────────────────────────────────────────────────
        // set_ue_version_override { version: int, persist: bool }
        //   version == 0  → clear the override (revert to auto-detect on next launch)
        //   version != 0  → record as the persistent override for this game
        //   persist=false → only update the in-process cached version (no disk write)
        //
        // Updates g_cachedUEVersion immediately so version-dependent code paths
        // (Soft array CE XML layout, FProperty offset selection, etc.) start
        // using the new value on the next request — no re-init / re-scan needed.
        // ─────────────────────────────────────────────────────────────────
        if (cmd == Renge::CMD_SET_UE_VERSION_OVERRIDE) {
            extern uint32_t    g_cachedUEVersion;
            extern bool        g_cachedVersionDetected;
            extern bool        g_cachedIsUserOverride;
            extern bool        g_cachedIsLowConfidence;
            extern char        g_cachedPeHash[17];

            int     newVersion = request.value("version", 0);
            bool    persist    = request.value("persist", true);

            // Defensive bounds — UE 4.18 .. 5.9 plus 0 (clear).
            if (newVersion != 0 && (newVersion < 418 || newVersion > 509)) {
                return Renge::MakeError(id,
                    "version out of supported range (418..509 or 0 to clear)").dump();
            }

            if (persist) {
                // Resolve a process name for the JSON record's gameName.
                wchar_t exeW[MAX_PATH] = {};
                GetModuleFileNameW(nullptr, exeW, MAX_PATH);
                std::wstring exePath(exeW);
                auto lastSlash = exePath.find_last_of(L"\\/");
                std::wstring fileName = (lastSlash != std::wstring::npos)
                    ? exePath.substr(lastSlash + 1) : exePath;
                std::string nameUtf8;
                int sz = WideCharToMultiByte(CP_UTF8, 0, fileName.c_str(), -1,
                                             nullptr, 0, nullptr, nullptr);
                if (sz > 0) {
                    nameUtf8.resize(sz - 1);
                    WideCharToMultiByte(CP_UTF8, 0, fileName.c_str(), -1,
                                        nameUtf8.data(), sz, nullptr, nullptr);
                }
                Flamme::SaveUserOverride(g_cachedPeHash,
                                         static_cast<uint32_t>(newVersion),
                                         nameUtf8.c_str());
            }

            if (newVersion == 0) {
                // Clear: don't change the in-process version (would require re-init);
                // just clear the override flag so UI shows "auto-detected" branding.
                g_cachedIsUserOverride = false;
            } else {
                g_cachedUEVersion       = static_cast<uint32_t>(newVersion);
                g_cachedVersionDetected = true;
                g_cachedIsUserOverride  = true;
                g_cachedIsLowConfidence = false;
            }

            json data;
            data["ue_version"]       = g_cachedUEVersion;
            data["is_user_override"] = g_cachedIsUserOverride;
            data["persisted"]        = persist;
            return Renge::MakeResponse(id, data).dump();
        }

        // ─────────────────────────────────────────────────────────────────
        // set_invoke_timeout — adjust GameThreadDispatch's UFunction timeout.
        // 0 = clear (revert to Stark::kDefaultInvokeTimeoutMs).
        // Persisted alongside the UE version override in the same JSON cache,
        // keyed by PE hash, so the value re-applies on next launch.
        // ─────────────────────────────────────────────────────────────────
        if (cmd == Renge::CMD_SET_INVOKE_TIMEOUT) {
            extern char g_cachedPeHash[17];

            int  timeoutMs = request.value("timeout_ms", 0);
            bool persist   = request.value("persist", true);

            // Defensive bounds — match Stark's clamp band, but allow 0 to clear.
            if (timeoutMs != 0 && (timeoutMs < Stark::kMinInvokeTimeoutMs || timeoutMs > Stark::kMaxInvokeTimeoutMs)) {
                return Renge::MakeError(id,
                    "timeout_ms out of supported range (100..600000 or 0 to clear)").dump();
            }

            if (persist) {
                wchar_t exeW[MAX_PATH] = {};
                GetModuleFileNameW(nullptr, exeW, MAX_PATH);
                std::wstring exePath(exeW);
                auto lastSlash = exePath.find_last_of(L"\\/");
                std::wstring fileName = (lastSlash != std::wstring::npos)
                    ? exePath.substr(lastSlash + 1) : exePath;
                std::string nameUtf8;
                int sz = WideCharToMultiByte(CP_UTF8, 0, fileName.c_str(), -1,
                                             nullptr, 0, nullptr, nullptr);
                if (sz > 0) {
                    nameUtf8.resize(sz - 1);
                    WideCharToMultiByte(CP_UTF8, 0, fileName.c_str(), -1,
                                        nameUtf8.data(), sz, nullptr, nullptr);
                }
                Flamme::SaveInvokeTimeout(g_cachedPeHash, timeoutMs, nameUtf8.c_str());
            }

            // Apply immediately — already-blocked invokes keep their original timeout
            // (future.wait_for is captured at call time; see Stark.cpp::EnqueueInvoke).
            Stark::SetInvokeTimeoutMs(timeoutMs);

            json data;
            data["invoke_timeout_ms"] = Stark::GetInvokeTimeoutMs();
            data["persisted"]         = persist;
            return Renge::MakeResponse(id, data).dump();
        }

        if (cmd == Renge::CMD_GET_POINTERS) {
            json data;
            FillPointerSnapshot(data);
            return Renge::MakeResponse(id, data).dump();
        }

        if (cmd == Renge::CMD_GET_OBJECT_COUNT) {
            json data;
            data["count"] = Aura::GetCount();
            return Renge::MakeResponse(id, data).dump();
        }

        if (cmd == Renge::CMD_GET_OBJECT_LIST) {
            int offset = request.value("offset", 0);
            int limit  = request.value("limit", 200);
            int total  = Aura::GetCount();
            // Opt-in per-object full path (Ubel::GetFullName). Gated behind
            // include_path so the hot Object Tree paginate stays lean — a path
            // string per object costs ~19 MB over 486K objects, and only
            // DumpAllService's GameOnly pass needs it (to skip engine-package
            // classes BEFORE walking them, restoring the pre-walk skip).
            bool includePath = request.value("include_path", false);

            json objects = json::array();
            int end = (std::min)(offset + limit, total);

            for (int i = offset; i < end; ++i) {
                uintptr_t obj = Aura::GetByIndex(i);
                if (!obj) continue;

                std::string name = Ubel::GetName(obj);
                if (name.empty()) continue; // Skip unnamed objects

                json item;
                item["addr"]  = Renge::AddrToStr(obj);
                item["name"]  = name;

                uintptr_t cls = Ubel::GetClass(obj);
                item["class"] = cls ? Ubel::GetName(cls) : "";

                uintptr_t outer = Ubel::GetOuter(obj);
                item["outer"] = outer ? Renge::AddrToStr(outer) : "";

                if (includePath) {
                    item["full_path"] = Ubel::GetFullName(obj);
                }

                objects.push_back(item);
            }

            json data;
            data["total"]   = total;
            data["scanned"] = end - offset; // Number of indices scanned (for pagination)
            data["objects"] = objects;
            return Renge::MakeResponse(id, data).dump();
        }

        // Snapshot capture (experimental — Phase A1a). begin returns the total
        // object count for progress; chunk streams numeric UPROPERTY values per
        // object. Stateless cursor pagination — advance "offset" by "scanned".
        if (cmd == Renge::CMD_BEGIN_SNAPSHOT) {
            std::string dtStr = request.value("data_type", "NumericNoByte");
            Radar::DataType dt;
            if (!Radar::TryParseDataType(dtStr, dt) || !Radar::IsMultiNumericDataType(dt)) {
                return Renge::MakeError(id, "snapshot data_type must be NumericNoByte or NumericAll").dump();
            }
            // Fresh names for this capture: clear the per-UObject name cache so a
            // long session doesn't accumulate millions of entries and so recycled
            // UObject addresses can't surface a destroyed object's stale name.
            Ubel::ClearNameCache();
            json data;
            data["total"] = Aura::GetCount();
            return Renge::MakeResponse(id, data).dump();
        }

        if (cmd == Renge::CMD_SNAPSHOT_CHUNK) {
            int  offset   = request.value("offset", 0);
            int  limit    = request.value("limit", 100);
            int  arrayCap = request.value("array_cap", 256);
            bool gameOnly = request.value("game_only", true);
            // Native-C (P3, opt-in): also capture each object's unmanaged-hole
            // guesses as synthetic "<raw@0xNN>" fields (normalized canonical type).
            bool nativeC  = request.value("native_c", false);
            // Auto-detect Engine/System noise (source-level skip; UI default ON).
            // Default false here keeps any flag-unaware caller's behavior unchanged
            // (full capture) — the UI always sends the checkbox's real value.
            bool autoSkipNoise = request.value("auto_skip_noise", false);
            // Type-family narrowing (opt-in, default Any): IntegersOnly / FloatsOnly
            // drop the other family from every numeric leaf, cutting the DB at the
            // source for type-specific hunts. Orthogonal to data_type (the scope).
            Aura::NumericFamily family =
                Aura::ParseNumericFamily(request.value("numeric_family", std::string("Any")));
            std::string dtStr = request.value("data_type", "NumericNoByte");
            Radar::DataType dt;
            if (!Radar::TryParseDataType(dtStr, dt) || !Radar::IsMultiNumericDataType(dt)) {
                return Renge::MakeError(id, "snapshot data_type must be NumericNoByte or NumericAll").dump();
            }

            auto chunk = Aura::CaptureSnapshotChunk(offset, limit, gameOnly, dt, arrayCap, nativeC, autoSkipNoise, family);

            auto encodeFields = [](const std::vector<Aura::SnapshotField>& src) {
                json arr = json::array();
                for (const auto& f : src) {
                    json fe;
                    fe["name"] = f.name;
                    fe["off"]  = f.offset;
                    fe["type"] = f.type;
                    fe["hex"]  = f.hex;
                    arr.push_back(std::move(fe));
                }
                return arr;
            };

            // Phase-0 telemetry: time the JSON DOM build (the bulk of the serialize
            // cost — the final .dump() is one extra traversal). Reported as serialize_ms
            // so the C# side can show walk / serialize / parse / write per chunk.
            const auto serT0 = std::chrono::steady_clock::now();
            json objects = json::array();
            for (const auto& o : chunk.objects) {
                json item;
                item["index"]       = o.index;
                item["addr"]        = Renge::AddrToStr(o.addr);
                item["name"]        = o.name;
                item["class"]       = o.className;
                item["outer_class"] = o.outerClassName;
                item["path"]        = o.path;
                item["fields"]      = encodeFields(o.fields);

                // Struct-array elements (inner-key capture). Omitted when empty.
                if (!o.arrays.empty()) {
                    json arrays = json::array();
                    for (const auto& a : o.arrays) {
                        json elems = json::array();
                        for (const auto& el : a.elements) {
                            json eo;
                            eo["i"] = el.index;
                            if (!el.keyName.empty()) {
                                eo["key_name"]  = el.keyName;
                                eo["key_value"] = el.keyValue;
                            }
                            eo["fields"] = encodeFields(el.fields);
                            elems.push_back(std::move(eo));
                        }
                        json ao;
                        ao["field"]    = a.field;
                        ao["elements"] = std::move(elems);
                        arrays.push_back(std::move(ao));
                    }
                    item["arrays"] = std::move(arrays);
                }
                objects.push_back(std::move(item));
            }

            json data;
            data["total"]        = chunk.total;
            data["scanned"]      = chunk.scanned;
            data["walk_ms"]      = chunk.walkMs;
            data["serialize_ms"] = std::chrono::duration_cast<std::chrono::milliseconds>(
                std::chrono::steady_clock::now() - serT0).count();
            data["objects"]      = std::move(objects);
            return Renge::MakeResponse(id, data).dump();
        }

        if (cmd == Renge::CMD_GET_OBJECT) {
            std::string addrStr = request.value("addr", "");
            if (addrStr.empty()) return Renge::MakeError(id, "Missing addr").dump();

            uintptr_t addr = Renge::StrToAddr(addrStr);
            json data;
            data["addr"]      = addrStr;
            data["name"]      = Ubel::GetName(addr);
            data["full_name"] = Ubel::GetFullName(addr);

            uintptr_t cls = Ubel::GetClass(addr);
            data["class"]      = cls ? Ubel::GetName(cls) : "";
            data["class_addr"] = Renge::AddrToStr(cls);

            uintptr_t outer = Ubel::GetOuter(addr);
            data["outer"]      = outer ? Ubel::GetName(outer) : "";
            data["outer_addr"] = Renge::AddrToStr(outer);

            return Renge::MakeResponse(id, data).dump();
        }

        if (cmd == Renge::CMD_FIND_OBJECT) {
            std::string path = request.value("path", "");
            if (path.empty()) return Renge::MakeError(id, "Missing path").dump();

            uintptr_t obj = Aura::FindByName(path);
            if (!obj) return Renge::MakeError(id, "Object not found").dump();

            json data;
            data["addr"] = Renge::AddrToStr(obj);
            data["name"] = Ubel::GetName(obj);
            return Renge::MakeResponse(id, data).dump();
        }

        if (cmd == Renge::CMD_SEARCH_OBJECTS) {
            std::string query = request.value("query", "");
            int limit = request.value("limit", 200);
            // Opt-in: hide the reflection/type layer so a global keyword search returns
            // only live gameplay instances (mirrors the Object Tree "Instances only" toggle).
            bool instancesOnly = request.value("instances_only", false);
            if (query.empty()) return Renge::MakeError(id, "Missing query").dump();

            auto rset = Aura::SearchByName(query, limit, instancesOnly);

            json objects = json::array();
            for (const auto& sr : rset.results) {
                json item;
                item["addr"]  = Renge::AddrToStr(sr.addr);
                item["name"]  = sr.name;
                item["class"] = sr.className;
                item["outer"] = Renge::AddrToStr(sr.outer);
                objects.push_back(item);
            }

            json data;
            data["total"]     = static_cast<int>(rset.results.size());
            data["scanned"]   = rset.scanned;
            // True when the cap was hit — the UI flags "more exist; narrow, or Reload + filter".
            data["truncated"] = rset.truncated;
            data["objects"]   = objects;
            return Renge::MakeResponse(id, data).dump();
        }

        // EncodeInstanceWalkToJson — shared serialiser used by both
        // walk_instance (single) and walk_instance_batch. Same contract as
        // EncodeClassInfoToJson below: centralising the emit guarantees the two
        // pipe paths produce byte-identical instance objects, so the CE export
        // can switch to the batch without a field silently changing shape.
        // (Layer 2 of the walk_class_batch safety net.)
        // LEAN contract (build 2351, measured in multipipe-eval.md §10.6):
        // `lean: true` omits the keys a CE XML export provably never reads. It
        // is subtractive ONLY — a lean object is the full object minus keys, so
        // the UI parser needs no new branch (a missing key already falls back to
        // its default) and an older DLL that ignores the flag simply returns the
        // full shape. The header below is 99% dead weight for an export: the
        // exporter reads `fields` and nothing else, and a batch reply is
        // positional, so even `addr` is redundant. `addr` and `stale` are kept
        // anyway — identity for logs/debugging, and the freed-slot signal.
        auto EncodeInstanceWalkToJson = [&](const Ubel::InstanceWalkResult& result,
                                            bool lean = false) -> json {
            json data;
            data["addr"]       = Renge::AddrToStr(result.addr);
            if (!lean) {
                data["name"]       = result.name;
                data["class"]      = result.className;
                data["class_addr"] = Renge::AddrToStr(result.classAddr);
                data["outer"]      = Renge::AddrToStr(result.outerAddr);
                data["outer_name"] = result.outerName;
                data["outer_class"]= result.outerClassName;
            }
            // Optional keys stay OPTIONAL — emitting them unconditionally would
            // change the single-call wire shape and break byte-equivalence.
            if (!lean && result.isDefinition)
                data["is_definition"] = true;
            if (result.isStale)
                data["stale"] = true;
            if (!lean && result.propsSize > 0)
                data["props_size"] = result.propsSize;

            json fields = json::array();
            for (const auto& fv : result.fields) {
                fields.push_back(SerializeField(fv, lean));
            }
            data["fields"] = fields;
            return data;
        };

        // EncodeClassInfoToJson — shared serialiser used by both
        // walk_class (single) and walk_class_batch. Centralising the
        // emit logic guarantees the two pipe paths produce byte-
        // identical class objects, which is the explicit safety
        // contract for SdkExport / DumpAll switching to the batch.
        auto EncodeClassInfoToJson = [](const ClassInfo& ci) -> json {
            json classData;
            classData["name"]       = ci.Name;
            classData["full_path"]  = ci.FullPath;
            classData["super_addr"] = Renge::AddrToStr(ci.SuperClass);
            classData["super_name"] = ci.SuperName;
            classData["props_size"] = ci.PropertiesSize;
            // Where this class's OWN properties begin. "fields" below carries the whole
            // SuperStruct chain, and nothing else in this reply implies the boundary
            // (audit #5 W2).
            classData["super_props_size"] = ci.SuperPropertiesSize;

            json fields = json::array();
            for (const auto& f : ci.Fields) {
                json fj = {
                    {"addr",   Renge::AddrToStr(f.Address)},
                    {"name",   f.Name},
                    {"type",   f.TypeName},
                    {"offset", f.Offset},
                    {"size",   f.Size}
                };
                // Reflection flags (CPF_*) + static-array dim — feed the
                // auto-detect scorer (SaveGame/BlueprintVisible/Net/Transient
                // gating; full footprint = Size * ArrayDim). PropertyFlags is
                // a uint64 with high bits set, so emit it as an "0x" hex
                // string (via AddrToStr's uint→hex) so no JSON-number consumer
                // loses precision. Omitted at defaults (flags 0 / dim 1) to
                // keep the wire lean.
                if (f.PropertyFlags != 0) fj["prop_flags"] = Renge::AddrToStr(f.PropertyFlags);
                if (f.ArrayDim != 1)      fj["array_dim"]  = f.ArrayDim;
                // Extended type metadata (only emit non-empty values)
                if (!f.structType.empty())      fj["struct_type"]       = f.structType;
                if (!f.objClassName.empty())     fj["obj_class"]         = f.objClassName;
                if (!f.innerType.empty())        fj["inner_type"]        = f.innerType;
                if (!f.innerStructType.empty())  fj["inner_struct_type"] = f.innerStructType;
                if (!f.innerObjClass.empty())    fj["inner_obj_class"]   = f.innerObjClass;
                if (!f.keyType.empty())          fj["key_type"]          = f.keyType;
                if (!f.keyStructType.empty())    fj["key_struct_type"]   = f.keyStructType;
                if (!f.valueType.empty())        fj["value_type"]        = f.valueType;
                if (!f.valueStructType.empty())  fj["value_struct_type"] = f.valueStructType;
                if (!f.elemType.empty())         fj["elem_type"]         = f.elemType;
                if (!f.elemStructType.empty())   fj["elem_struct_type"]  = f.elemStructType;
                if (!f.enumName.empty())         fj["enum_name"]         = f.enumName;
                if (f.boolFieldMask != 0)        fj["bool_mask"]         = f.boolFieldMask;
                fields.push_back(fj);
            }
            classData["fields"] = fields;
            return classData;
        };

        if (cmd == Renge::CMD_WALK_CLASS) {
            std::string addrStr = request.value("addr", "");
            if (addrStr.empty()) return Renge::MakeError(id, "Missing addr").dump();

            uintptr_t addr = Renge::StrToAddr(addrStr);
            const ClassInfo& ci = Ubel::WalkClassEx(addr);

            json data;
            data["class"] = EncodeClassInfoToJson(ci);
            return Renge::MakeResponse(id, data).dump();
        }

        // === walk_class_batch: Pipe-amortised variant of walk_class.
        // Loops Ubel::WalkClassEx over addrs[] and returns one class
        // object per addr in order. Each element matches the single
        // walk_class response's "class" field byte-for-byte — both
        // paths share EncodeClassInfoToJson above. Used by
        // SdkExportService Full SDK export and DumpAllService stream
        // to collapse N round-trips into N/chunkSize. ===
        if (cmd == Renge::CMD_WALK_CLASS_BATCH) {
            if (!request.contains("addrs") || !request["addrs"].is_array()) {
                return Renge::MakeError(id, "Missing or non-array 'addrs'").dump();
            }
            std::vector<uintptr_t> addrs;
            addrs.reserve(request["addrs"].size());
            for (const auto& a : request["addrs"]) {
                if (a.is_string()) {
                    auto s = a.get<std::string>();
                    if (!s.empty()) addrs.push_back(Renge::StrToAddr(s));
                }
            }

            auto results = Aura::WalkClassesBatch(addrs);

            json classesArr = json::array();
            for (const auto& ci : results) {
                classesArr.push_back(EncodeClassInfoToJson(ci));
            }

            json data;
            data["classes"] = classesArr;
            data["count"]   = static_cast<int>(results.size());
            return Renge::MakeResponse(id, data).dump();
        }

        // list_enums: enumerate all UEnum objects with their entries
        if (cmd == Renge::CMD_LIST_ENUMS) {
            int total = Aura::GetCount();
            json enums = json::array();

            for (int i = 0; i < total; ++i) {
                if ((i & 0xFFF) == 0 && Tot::Requested()) {
                    Sein::Warn("PIPE:cmd", "list_enums: aborted (client gone / shutdown)");
                    break;  // return partial result
                }
                uintptr_t obj = Aura::GetByIndex(i);
                if (!obj) continue;

                // Check if this object's class is "Enum" (UEnum inherits UObject)
                uintptr_t cls = Ubel::GetClass(obj);
                if (!cls) continue;
                std::string clsName = Ubel::GetName(cls);
                if (clsName != "Enum") continue;

                std::string name = Ubel::GetName(obj);
                if (name.empty()) continue;

                // Read enum entries via cached resolver
                auto entries = Ubel::GetEnumEntries(obj);

                json enumObj;
                enumObj["addr"]      = Renge::AddrToStr(obj);
                enumObj["name"]      = name;
                enumObj["full_path"] = Ubel::GetFullName(obj);

                json entryArr = json::array();
                for (const auto& e : entries) {
                    entryArr.push_back({{"n", e.name}, {"v", e.value}});
                }
                enumObj["entries"] = entryArr;
                enums.push_back(enumObj);
            }

            json data;
            data["enums"] = enums;
            data["count"] = static_cast<int>(enums.size());
            return Renge::MakeResponse(id, data).dump();
        }

        if (cmd == Renge::CMD_WALK_FUNCTIONS) {
            std::string addrStr = request.value("addr", "");
            if (addrStr.empty()) return Renge::MakeError(id, "Missing addr").dump();

            uintptr_t addr = Renge::StrToAddr(addrStr);
            auto funcs = Ubel::WalkFunctions(addr);

            json funcArr = json::array();
            for (const auto& f : funcs) {
                json fj;
                fj["name"]    = f.name;
                fj["full"]    = f.fullName;
                fj["addr"]    = Renge::AddrToStr(f.address);
                fj["flags"]   = f.functionFlags;
                fj["num_parms"]  = f.numParms;
                fj["parms_size"] = f.parmsSize;
                fj["ret_offset"] = f.returnValueOffset;
                fj["ret"]     = f.returnType;

                json params = json::array();
                for (const auto& p : f.params) {
                    json pj;
                    pj["name"]   = p.name;
                    pj["type"]   = p.typeName;
                    pj["size"]   = p.size;
                    pj["offset"] = p.offset;
                    pj["out"]    = p.isOut;
                    pj["ret"]    = p.isReturn;
                    if (!p.structType.empty())
                        pj["struct_type"] = p.structType;
                    // Stage 1 (Invoke param picker): target UClass for
                    // Object/Class/Soft/Weak/Lazy/Interface params. Mirrors
                    // the field-side `obj_class` key used by walk_class.
                    if (!p.objClassName.empty())
                        pj["obj_class"] = p.objClassName;
                    if (!p.structFields.empty()) {
                        json sfArr = json::array();
                        for (const auto& sf : p.structFields) {
                            json sfj;
                            sfj["name"]   = sf.name;
                            sfj["type"]   = sf.typeName;
                            sfj["offset"] = sf.offset;
                            sfj["size"]   = sf.size;
                            sfArr.push_back(sfj);
                        }
                        pj["struct_fields"] = sfArr;
                    }
                    params.push_back(pj);
                }
                fj["params"] = params;
                funcArr.push_back(fj);
            }

            json data;
            data["functions"] = funcArr;
            data["count"]     = static_cast<int>(funcArr.size());
            return Renge::MakeResponse(id, data).dump();
        }

        if (cmd == Renge::CMD_READ_MEM) {
            std::string addrStr = request.value("addr", "");
            int size = request.value("size", 256);
            if (addrStr.empty()) return Renge::MakeError(id, "Missing addr").dump();
            if (size <= 0 || size > 65536) return Renge::MakeError(id, "Invalid size").dump();

            uintptr_t addr = Renge::StrToAddr(addrStr);
            std::vector<uint8_t> buf(size);
            if (!Macht::ReadBytesSafe(addr, buf.data(), size)) {
                return Renge::MakeError(id, "Read failed").dump();
            }

            json data;
            data["bytes"] = Renge::BytesToHex(buf.data(), buf.size());
            return Renge::MakeResponse(id, data).dump();
        }

        if (cmd == Renge::CMD_WRITE_MEM) {
            std::string addrStr = request.value("addr", "");
            std::string hexBytes = request.value("bytes", "");
            if (addrStr.empty() || hexBytes.empty()) {
                return Renge::MakeError(id, "Missing addr or bytes").dump();
            }

            uintptr_t addr = Renge::StrToAddr(addrStr);
            // Reject a malformed pattern instead of writing a silently-mangled one.
            // strtoul mapped every non-hex character to 0x00, so "DE AD BE EF" used to
            // be written as {DE,0A,0D,BE,0E} and answered ok:true. (B46)
            std::vector<uint8_t> bytes;
            if (!Renge::TryHexToBytes(hexBytes, bytes)) {
                return Renge::MakeError(id,
                    "Invalid bytes (need an even-length hex string, no separators): "
                    + hexBytes).dump();
            }
            if (bytes.empty() || bytes.size() > 65536) {
                return Renge::MakeError(id, "Invalid write size (max 65536)").dump();
            }
            if (!Macht::WriteBytes(addr, bytes.data(), bytes.size())) {
                return Renge::MakeError(id, "Write failed").dump();
            }

            return Renge::MakeResponse(id).dump();
        }

        // === walk_instance: Read live field values from a UObject instance ===
        if (cmd == Renge::CMD_WALK_INSTANCE) {
            std::string addrStr = request.value("addr", "");
            if (addrStr.empty()) return Renge::MakeError(id, "Missing addr").dump();

            uintptr_t addr = 0;
            if (!Renge::TryStrToAddr(addrStr, addr))
                return Renge::MakeError(id, "Invalid addr (not a hex number): " + addrStr).dump();
            std::string classAddrStr = request.value("class_addr", "");
            uintptr_t classAddr = 0;
            if (!classAddrStr.empty() && !Renge::TryStrToAddr(classAddrStr, classAddr))
                return Renge::MakeError(id, "Invalid class_addr (not a hex number): " + classAddrStr).dump();
            int32_t arrayLimit = request.value("array_limit", 64);
            int32_t previewLimit = request.value("preview_limit", 2);
            bool fillGaps = request.value("fill_gaps", false);
            bool lean = request.value("lean", false);

            auto result = Ubel::WalkInstance(addr, classAddr, arrayLimit, previewLimit, fillGaps);

            // Shared serialiser — walk_instance_batch emits the SAME function, so
            // single and batch responses cannot drift field-by-field.
            return Renge::MakeResponse(id, EncodeInstanceWalkToJson(result, lean)).dump();
        }

        // ── walk_instance_batch: N instance walks in ONE round-trip ──
        //
        // MEASURED justification (build 2327, multipipe-eval.md §10.4): a Copy CE
        // XML issued 20,357 single walk_instance calls, and the split was
        // dll 30% / ipc 59-73% / ui ~0%. The per-call IPC (0.16-0.21 ms) is roughly
        // TWICE the actual walk (0.08 ms) and is pure round-trip overhead — exactly
        // what collapsing N calls into one removes.
        //
        // Deliberately a trivial loop over the single-call path: that is a
        // STRUCTURAL guarantee of equivalence (layer 1 of the walk_class_batch
        // safety net), not a promise. Any cleverness here would have to be proven
        // instead of being true by construction.
        if (cmd == Renge::CMD_WALK_INSTANCE_BATCH) {
            if (!request.contains("items") || !request["items"].is_array()) {
                return Renge::MakeError(id, "Missing or non-array 'items'").dump();
            }
            // Per-batch defaults; each item may override.
            int32_t defArrayLimit   = request.value("array_limit", 64);
            int32_t defPreviewLimit = request.value("preview_limit", 2);
            bool    defFillGaps     = request.value("fill_gaps", false);
            bool    defLean         = request.value("lean", false);

            json arr = json::array();
            for (const auto& item : request["items"]) {
                // A malformed element must not abort the batch — the UI's fallback
                // replays a failed chunk as single calls, and losing the whole
                // chunk's good entries would defeat that.
                if (!item.is_object()) { arr.push_back(json::object()); continue; }

                uintptr_t a = 0;
                std::string as = item.value("addr", "");
                if (as.empty() || !Renge::TryStrToAddr(as, a)) {
                    arr.push_back(json::object());
                    continue;
                }
                uintptr_t ca = 0;
                std::string cas = item.value("class_addr", "");
                if (!cas.empty()) Renge::TryStrToAddr(cas, ca);

                auto r = Ubel::WalkInstance(a, ca,
                                            item.value("array_limit",   defArrayLimit),
                                            item.value("preview_limit", defPreviewLimit),
                                            item.value("fill_gaps",     defFillGaps));
                arr.push_back(EncodeInstanceWalkToJson(r, item.value("lean", defLean)));

                // Same cooperative-cancel contract as every other bulk loop: a
                // disconnect mid-batch returns what is done rather than walking on.
                if (Tot::Requested()) break;
            }

            json data;
            data["instances"] = arr;
            data["count"]     = static_cast<int>(arr.size());
            return Renge::MakeResponse(id, data).dump();
        }

        // === read_array_elements: Read scalar elements from a TArray (Phase B) ===
        if (cmd == Renge::CMD_READ_ARRAY_ELEMS) {
            std::string addrStr = request.value("addr", "");
            if (addrStr.empty())
                return Renge::MakeError(id, "missing 'addr'").dump();
            uintptr_t addr = Renge::StrToAddr(addrStr);

            int32_t fieldOffset = request.value("field_offset", 0);
            std::string innerAddrStr = request.value("inner_addr", "");
            uintptr_t innerAddr = innerAddrStr.empty() ? 0 : Renge::StrToAddr(innerAddrStr);
            std::string innerType = request.value("inner_type", "");
            int32_t elemSize = request.value("elem_size", 0);
            int32_t offset = request.value("offset", 0);
            int32_t limit = request.value("limit", 64);

            if (innerType.empty() || elemSize <= 0)
                return Renge::MakeError(id, "missing inner_type or invalid elem_size").dump();

            // Validate elemSize from UI — may have cached garbage from older sessions.
            // ReadArrayElements already caps at 256, but validate explicitly here too.
            if (elemSize > 256) {
                Sein::Warn("PIPE:cmd", "read_array_elements: elemSize=%d too large for '%s', rejecting",
                    elemSize, innerType.c_str());
                return Renge::MakeError(id, "elem_size too large (max 256)").dump();
            }

            auto result = Ubel::ReadArrayElements(
                addr, fieldOffset, innerAddr, innerType, elemSize, offset, limit);

            if (!result.ok)
                return Renge::MakeError(id, result.error).dump();

            json data;
            data["total"] = result.totalCount;
            data["read"] = result.readCount;
            data["inner_type"] = innerType;
            data["elem_size"] = elemSize;

            json elems = json::array();
            for (const auto& e : result.elements) {
                json ej;
                ej["i"] = e.index;
                ej["v"] = e.value;
                ej["h"] = e.hex;
                if (!e.enumName.empty())
                    ej["en"] = e.enumName;
                if (e.rawIntValue != 0 || !e.enumName.empty())
                    ej["rv"] = e.rawIntValue;
                // Phase D: pointer element fields
                if (e.ptrAddr != 0) {
                    ej["pa"] = Renge::AddrToStr(e.ptrAddr);
                    ej["pn"] = e.ptrName;
                    ej["pc"] = e.ptrClassName;
                }
                // Phase F: struct sub-fields
                if (!e.structFields.empty()) {
                    json sfs = json::array();
                    for (const auto& sf : e.structFields) {
                        json sfj = {{"n", sf.name}, {"t", sf.typeName},
                                    {"o", sf.offset}, {"s", sf.size}, {"v", sf.value}};
                        // Pointer resolution for ObjectProperty sub-fields
                        if (sf.ptrAddr != 0) {
                            sfj["pa"] = Renge::AddrToStr(sf.ptrAddr);
                            sfj["pn"] = sf.ptrName;
                            sfj["pc"] = sf.ptrClassName;
                            sfj["pca"] = Renge::AddrToStr(sf.ptrClassAddr);
                        }
                        sfs.push_back(sfj);
                    }
                    ej["sf"] = sfs;
                }
                elems.push_back(ej);
            }
            data["elements"] = elems;
            return Renge::MakeResponse(id, data).dump();
        }

        // === walk_world: Browse GWorld → PersistentLevel → Actors hierarchy ===
        if (cmd == Renge::CMD_WALK_WORLD) {
            extern uintptr_t g_cachedGWorld;

            // Allow overriding with a custom address
            std::string addrStr = request.value("addr", "");
            uintptr_t worldAddr = 0;
            if (!addrStr.empty()) {
                worldAddr = Renge::StrToAddr(addrStr);
            } else {
                // g_cachedGWorld is &GWorld (address of the global pointer variable).
                // Must dereference to get the actual UWorld* value.
                if (g_cachedGWorld) {
                    bool ok = Macht::ReadSafe(g_cachedGWorld, worldAddr);
                    Sein::Info("PIPE:world", "GWorld deref: &GWorld=0x%llX -> UWorld*=0x%llX (ReadSafe=%s)",
                        static_cast<unsigned long long>(g_cachedGWorld),
                        static_cast<unsigned long long>(worldAddr),
                        ok ? "ok" : "fail");
                }

                // Fallback: if AOB-resolved GWorld is null/wrong, search GObjects for a UWorld instance.
                // This handles games where the AOB pattern matched the wrong global variable.
                if (!worldAddr) {
                    Sein::Info("PIPE:world", "GWorld pointer is null, searching GObjects for UWorld...");
                    Aura::ForEach([&](int32_t idx, uintptr_t obj) -> bool {
                        uintptr_t cls = Ubel::GetClass(obj);
                        if (!cls) return true; // continue
                        std::string clsName = Ubel::GetName(cls);
                        if (clsName == "World") {
                            // Skip CDOs (Default__World) — they have null PersistentLevel
                            std::string objName = Ubel::GetName(obj);
                            if (objName.rfind("Default__", 0) == 0) {
                                Sein::Debug("PIPE:world", "Skipping CDO '%s' at 0x%llX",
                                    objName.c_str(), static_cast<unsigned long long>(obj));
                                return true; // continue
                            }
                            worldAddr = obj;
                            Sein::Info("PIPE:world", "Found UWorld '%s' via GObjects scan: 0x%llX (index=%d)",
                                objName.c_str(), static_cast<unsigned long long>(obj), idx);
                            return false; // stop
                        }
                        return true; // continue
                    });
                }
            }

            if (!worldAddr) return Renge::MakeError(id, "GWorld not found — no UWorld instance in GObjects").dump();

            json data;
            data["world_addr"] = Renge::AddrToStr(worldAddr);
            data["world_name"] = Ubel::GetName(worldAddr);

            // Log DynOff state for diagnostics
            Sein::Info("PIPE:world", "DynOff: FFIELD_NEXT=0x%02X FFIELD_NAME=0x%02X FPROPERTY_OFFSET=0x%02X "
                "FPROPERTY_ELEMSIZE=0x%02X FSTRUCTPROP_STRUCT=0x%02X bTaggedFFV=%d",
                DynOff::FFIELD_NEXT, DynOff::FFIELD_NAME, DynOff::FPROPERTY_OFFSET,
                DynOff::FPROPERTY_ELEMSIZE, DynOff::FSTRUCTPROP_STRUCT,
                DynOff::bTaggedFFieldVariant ? 1 : 0);

            // Walk UWorld class to find PersistentLevel field offset dynamically
            uintptr_t worldClass = Ubel::GetClass(worldAddr);
            if (!worldClass) return Renge::MakeError(id, "Cannot read UWorld class").dump();

            ClassInfo worldCI = Ubel::WalkClass(worldClass);
            Sein::Info("PIPE:world", "UWorld class '%s' at 0x%llX, %zu fields, propsSize=%d",
                worldCI.Name.c_str(), static_cast<unsigned long long>(worldClass),
                worldCI.Fields.size(), worldCI.PropertiesSize);

            // Find PersistentLevel field (ObjectProperty)
            uintptr_t levelAddr = 0;
            int persistentLevelOffset = 0;
            bool foundPersistentLevel = false;
            for (const auto& f : worldCI.Fields) {
                if (f.Name == "PersistentLevel" && f.Size >= 8) {
                    foundPersistentLevel = true;
                    persistentLevelOffset = f.Offset;
                    Macht::ReadSafe(worldAddr + f.Offset, levelAddr);
                    Sein::Info("PIPE:world", "PersistentLevel: offset=%d, levelAddr=0x%llX",
                        persistentLevelOffset, static_cast<unsigned long long>(levelAddr));
                    break;
                }
            }

            if (!foundPersistentLevel) {
                // Diagnostic: dump all field names + raw FName data for debugging
                Sein::Warn("PIPE:world", "PersistentLevel NOT found in %zu UWorld fields. Dumping first 10:",
                    worldCI.Fields.size());
                int dumpCount = 0;
                for (const auto& f : worldCI.Fields) {
                    if (dumpCount >= 10) break;
                    Sein::Warn("PIPE:world", "  field[%d]: name='%s' type='%s' off=%d size=%d addr=0x%llX",
                        dumpCount, f.Name.c_str(), f.TypeName.c_str(), f.Offset, f.Size,
                        static_cast<unsigned long long>(f.Address));
                    ++dumpCount;
                }

                // Diagnostic: try reading FName at alternate offsets (0x20, 0x28, 0x30) on first FField
                if (!worldCI.Fields.empty()) {
                    uintptr_t firstFF = worldCI.Fields[0].Address;
                    for (int probe = 0x18; probe <= 0x38; probe += 4) {
                        int32_t ci = 0;
                        if (Macht::ReadSafe(firstFF + probe, ci) && ci > 0 && ci < 0x00FFFFFF) {
                            std::string probeName = Serie::GetString(ci);
                            Sein::Warn("PIPE:world", "  probe FField+0x%02X: compIdx=%d -> '%s'",
                                probe, ci, probeName.c_str());
                        }
                    }
                }

                data["error"] = "PersistentLevel field not found in UWorld class (WalkClass returned "
                    + std::to_string(worldCI.Fields.size()) + " fields)";
                Sein::Warn("PIPE:world", "%s", data["error"].get<std::string>().c_str());
                return Renge::MakeResponse(id, data).dump();
            }
            if (!levelAddr) {
                data["error"] = "PersistentLevel is null (CDO or uninitialized world instance)";
                Sein::Warn("PIPE:world", "%s", data["error"].get<std::string>().c_str());
                return Renge::MakeResponse(id, data).dump();
            }

            data["level_addr"] = Renge::AddrToStr(levelAddr);
            data["level_name"] = Ubel::GetName(levelAddr);
            data["level_offset"] = persistentLevelOffset;

            // Walk ULevel class to find Actors TArray field
            uintptr_t levelClass = Ubel::GetClass(levelAddr);
            ClassInfo levelCI = levelClass ? Ubel::WalkClass(levelClass) : ClassInfo{};

            // Find Actors field (ArrayProperty) — it's a TArray<AActor*>
            int actorsOffset = -1;
            for (const auto& f : levelCI.Fields) {
                if (f.Name == "Actors" && f.TypeName == "ArrayProperty") {
                    actorsOffset = f.Offset;
                    break;
                }
            }

            json actors = json::array();
            int actorLimit = request.value("limit", 200);

            // The two failures below used to return `actors: []` with ok:true and NO
            // error, even though this same handler sets data["error"] for the two
            // failures above it — so the UI rendered a populated level as an empty
            // one. Live on DumperTest's stock ThirdPersonMap 2026-08-14: actor_count
            // 0 while world_addr / level_name / level_offset all resolved. That is a
            // real unanswered question about this map, and it was invisible because
            // nothing said which branch fired. (audit #5 D5/F6)
            int actorTotal = -1;
            if (actorsOffset < 0) {
                data["error"] = "ULevel::Actors ArrayProperty not found on this level's class "
                                "— the actor list below is empty because it was never read";
            } else {
                Macht::TArrayView actorArr;
                if (!Macht::ReadTArray(levelAddr + actorsOffset, actorArr)) {
                    data["error"] = "ULevel::Actors TArray unreadable at +"
                                  + std::to_string(actorsOffset)
                                  + " — the actor list below is empty because the read failed";
                } else {
                    actorTotal = actorArr.Count;
                    int count = (std::min)(actorArr.Count, actorLimit);
                    for (int i = 0; i < count; ++i) {
                        uintptr_t actorAddr = Macht::ReadTArrayElement(actorArr, i);
                        if (!actorAddr) continue;

                        json actorItem;
                        actorItem["addr"]  = Renge::AddrToStr(actorAddr);
                        actorItem["name"]  = Ubel::GetName(actorAddr);
                        actorItem["index"] = Ubel::GetIndex(actorAddr);

                        uintptr_t actorCls = Ubel::GetClass(actorAddr);
                        actorItem["class"] = actorCls ? Ubel::GetName(actorCls) : "";

                        // Try to find OwnedComponents on this actor
                        ClassInfo actorCI = actorCls ? Ubel::WalkClass(actorCls) : ClassInfo{};
                        int compsOffset = -1;
                        for (const auto& f : actorCI.Fields) {
                            if (f.Name == "OwnedComponents" && f.TypeName == "ArrayProperty") {
                                compsOffset = f.Offset;
                                break;
                            }
                        }

                        if (compsOffset >= 0) {
                            Macht::TArrayView compArr;
                            if (Macht::ReadTArray(actorAddr + compsOffset, compArr)) {
                                json comps = json::array();
                                int compCount = (std::min)(compArr.Count, 64); // Limit components
                                for (int c = 0; c < compCount; ++c) {
                                    uintptr_t compAddr = Macht::ReadTArrayElement(compArr, c);
                                    if (!compAddr) continue;

                                    json compItem;
                                    compItem["addr"] = Renge::AddrToStr(compAddr);
                                    compItem["name"] = Ubel::GetName(compAddr);
                                    uintptr_t compCls = Ubel::GetClass(compAddr);
                                    compItem["class"] = compCls ? Ubel::GetName(compCls) : "";
                                    comps.push_back(compItem);
                                }
                                actorItem["components"] = comps;
                            }
                        }

                        actors.push_back(actorItem);
                    }
                }
            }

            data["actors"]      = actors;
            data["actor_count"] = static_cast<int>(actors.size());
            // actor_count is the PAGE size. The level's real element count was read
            // one line above and thrown away, so a 500-actor page was indis-
            // tinguishable from a 500-actor level and an actor at index 1877 simply
            // was not there. -1 = never read (see the error above). (audit #5 D5/F6)
            data["actor_total"] = actorTotal;
            data["truncated"]   = (actorTotal > actorLimit);
            return Renge::MakeResponse(id, data).dump();
        }

        // === find_instances: Search GObjects for instances of a given class ===
        if (cmd == Renge::CMD_FIND_INSTANCES) {
            std::string className = request.value("class_name", "");
            std::string nameFilter = request.value("name_filter", "");
            bool exactMatch = request.value("exact_match", false);
            int limit = request.value("limit", 500);
            // Clamp the configurable cap: <1 would return nothing; the 50000 ceiling
            // bounds a pathological broad search to a ~6-9MB single payload (the scan
            // walks all of GObjects either way; this only bounds the returned list).
            if (limit < 1) limit = 1;
            if (limit > 50000) limit = 50000;
            bool newestFirst = request.value("newest_first", false);
            // Server-side class-noise filter: skip these classes BEFORE the cap so a
            // wanted instance past the cap survives once noise is excluded. The UI
            // re-runs find_instances whenever the class-noise picker changes.
            std::vector<std::string> excludeClasses = ParseExcludeClasses(request);
            // Either query is sufficient: class-only (legacy), name-only, or both
            // (AND). Only an empty-empty request is rejected.
            if (className.empty() && nameFilter.empty())
                return Renge::MakeError(id, "Missing class_name or name_filter").dump();

            // buildHistogram=true: full-pool class tally + exclude-before-cap (the
            // pipe path always needs the picker histogram).
            auto rset = Aura::FindInstancesByClass(className, exactMatch, limit, newestFirst, nameFilter, excludeClasses, /*buildHistogram=*/true);

            // Diagnostic: if name resolution ratio is low, dump FNamePool state
            if (rset.nonNull > 1000 && rset.named > 0) {
                double namedRatio = static_cast<double>(rset.named) / rset.nonNull;
                if (namedRatio < 0.70) {
                    Sein::Warn("PIPE:find", "Low name resolution ratio: %.1f%% (%d/%d) — running FNamePool diagnostics",
                                 namedRatio * 100, rset.named, rset.nonNull);
                    Serie::LogDiagnostics();
                }
            }

            json instances = json::array();
            for (const auto& sr : rset.results) {
                json item;
                item["addr"]  = Renge::AddrToStr(sr.addr);
                item["index"] = sr.index;
                item["name"]  = sr.name;
                item["class"] = sr.className;
                // UClass* — key for find_functions_by_class ("Find Func" on a row).
                item["class_addr"] = sr.classAddr ? Renge::AddrToStr(sr.classAddr) : "";
                item["outer"] = Renge::AddrToStr(sr.outer);
                instances.push_back(item);
            }

            json data;
            data["total"]          = static_cast<int>(rset.results.size());
            data["scanned"]        = rset.scanned;
            data["non_null"]       = rset.nonNull;
            data["named"]          = rset.named;
            data["truncated"]      = rset.truncated;
            data["instances"]      = instances;
            // Class-noise picker: full-pool histogram (Top-40) + true distinct count,
            // mirroring the value-scan begin/refine responses.
            data["class_histogram"] = HistogramToJson(rset.classHistogram, kClassHistogramMaxRows);
            data["class_distinct"]  = rset.classDistinct;
            return Renge::MakeResponse(id, data).dump();
        }

        // === search_properties: Keyword search across all UClass properties ===
        if (cmd == Renge::CMD_SEARCH_PROPERTIES) {
            std::string query = request.value("query", "");
            bool gameOnly = request.value("game_only", true);
            int limit = request.value("limit", 200);
            // Opt-in deep descent into nested struct + container-element schemas
            // (default off — keeps the shallow direct-field search fast).
            bool deep = request.value("deep", false);

            // Parse optional type filter
            std::vector<std::string> typeFilter;
            if (request.contains("types") && request["types"].is_array()) {
                for (const auto& t : request["types"]) {
                    if (t.is_string()) typeFilter.push_back(t.get<std::string>());
                }
            }

            // Either query or typeFilter must constrain the search — empty
            // both would scan every property in every class. SearchProperties
            // tolerates an empty query (substring-find returns 0 on empty
            // pattern), so allow it when typeFilter is non-empty.
            if (query.empty() && typeFilter.empty()) {
                return Renge::MakeError(id, "Missing query or type filter").dump();
            }

            auto searchResult = Aura::SearchProperties(query, typeFilter, gameOnly, limit, deep);

            json matches = json::array();
            for (const auto& m : searchResult.results) {
                json item;
                item["class_name"]  = m.className;
                item["class_addr"]  = Renge::AddrToStr(m.classAddr);
                item["class_path"]  = m.classPath;
                item["super_name"]  = m.superName;
                item["prop_name"]   = m.propName;
                item["prop_type"]   = m.propType;
                item["prop_offset"] = m.propOffset;
                item["prop_size"]   = m.propSize;
                item["struct_type"] = m.structType;
                item["inner_type"]  = m.innerType;
                // CPF_* reflection flags (auto-detect scorer gating) — hex
                // string so the uint64 high bits survive; omitted when 0.
                if (m.propertyFlags != 0)
                    item["prop_flags"] = Renge::AddrToStr(m.propertyFlags);
                // Inheritance-aware fields (build 610+) -- after dedup,
                // class_name == defining_class_name (we keep both for
                // forward compat in case the dedup story changes).
                item["defining_class_name"] = m.definingClassName;
                item["defining_class_addr"] = Renge::AddrToStr(m.definingClassAddr);
                item["defining_class_path"] = m.definingClassPath;
                item["inherited_by_count"]  = m.inheritedByCount;
                // FProperty* address — the key for find_property_xrefs
                // ("which methods use this field?"). Populated during the
                // field walk regardless of preview.
                item["field_addr"] = Renge::AddrToStr(m.fieldAddr);
                // Deep-mode nested leaf: prop_name carries a dotted path and
                // there is no class-absolute address. UI gates Copy Offset /
                // Freeze off this flag and keeps finder + Find Funcs. Omitted
                // (defaults false on the C# side) for shallow rows.
                if (m.isNested)
                    item["is_nested"] = true;
                if (!m.preview.empty())
                    item["preview"] = m.preview;
                matches.push_back(item);
            }

            json data;
            data["total"]           = static_cast<int>(searchResult.results.size());
            data["scanned_classes"] = searchResult.scannedClasses;
            // Now the objects actually walked, not the pool size (audit #5 D5/F4).
            data["scanned_objects"] = searchResult.scannedObjects;
            // Additive: a client that ignores these behaves as before. Without them
            // a capped search is indistinguishable from a complete one that found
            // everything, which is how "the scan missed my field" gets reported.
            data["truncated"]       = searchResult.truncated;
            data["aborted"]         = searchResult.aborted;
            data["results"]         = matches;
            return Renge::MakeResponse(id, data).dump();
        }

        // === search_properties_batch: Multi-keyword variant of search_properties.
        // Walks GObjects + class fields ONCE and checks every property
        // against ALL queries. Used by the Interesting Properties tab
        // to fan 36 seed keywords into a single round-trip — drops wall
        // time from ~42s (sequential pipe calls) to ~1.5s on a 4400-
        // class game. Wire schema mirrors search_properties' result rows
        // exactly, just wrapped in a per-query envelope so the C# side
        // can attribute matches back to their seed keyword. ===
        if (cmd == Renge::CMD_SEARCH_PROPERTIES_BATCH) {
            // Required: queries[] array of non-empty strings.
            if (!request.contains("queries") || !request["queries"].is_array()
                || request["queries"].empty()) {
                return Renge::MakeError(id, "Missing or empty 'queries' array").dump();
            }
            std::vector<std::string> queries;
            for (const auto& q : request["queries"]) {
                if (q.is_string()) {
                    auto s = q.get<std::string>();
                    if (!s.empty()) queries.push_back(s);
                }
            }
            if (queries.empty()) {
                return Renge::MakeError(id, "All queries empty after filtering").dump();
            }

            bool gameOnly = request.value("game_only", true);
            int limit = request.value("limit", 200);

            std::vector<std::string> typeFilter;
            if (request.contains("types") && request["types"].is_array()) {
                for (const auto& t : request["types"]) {
                    if (t.is_string()) typeFilter.push_back(t.get<std::string>());
                }
            }

            auto batchResults = Aura::SearchPropertiesBatch(
                queries, typeFilter, gameOnly, limit, /*withPreviews=*/false);

            json perQuery = json::array();
            int totalScannedClasses = batchResults.empty() ? 0 : batchResults[0].scannedClasses;
            int totalScannedObjects = batchResults.empty() ? 0 : batchResults[0].scannedObjects;
            int grandTotal = 0;
            for (size_t qi = 0; qi < queries.size() && qi < batchResults.size(); ++qi) {
                const auto& sr = batchResults[qi];
                grandTotal += static_cast<int>(sr.results.size());
                json matches = json::array();
                for (const auto& m : sr.results) {
                    json item;
                    item["class_name"]  = m.className;
                    item["class_addr"]  = Renge::AddrToStr(m.classAddr);
                    item["class_path"]  = m.classPath;
                    item["super_name"]  = m.superName;
                    item["prop_name"]   = m.propName;
                    item["prop_type"]   = m.propType;
                    item["prop_offset"] = m.propOffset;
                    item["prop_size"]   = m.propSize;
                    item["struct_type"] = m.structType;
                    item["inner_type"]  = m.innerType;
                    if (m.propertyFlags != 0)
                        item["prop_flags"] = Renge::AddrToStr(m.propertyFlags);
                    item["defining_class_name"] = m.definingClassName;
                    item["defining_class_addr"] = Renge::AddrToStr(m.definingClassAddr);
                    item["defining_class_path"] = m.definingClassPath;
                    item["inherited_by_count"]  = m.inheritedByCount;
                    // FProperty* address for find_property_xrefs (set during
                    // the field walk, so available even on this no-preview path).
                    item["field_addr"] = Renge::AddrToStr(m.fieldAddr);
                    // Note: preview omitted intentionally — batch path skips
                    // Phase-2 instance scan. Interesting Properties tab
                    // (the only caller) doesn't display previews.
                    matches.push_back(item);
                }
                json envelope;
                envelope["query"] = queries[qi];
                envelope["results"] = matches;
                envelope["match_count"] = static_cast<int>(sr.results.size());
                // Per-query: the batch loop stops when EVERY query is full, so one
                // seed keyword can be capped while another swept the whole pool.
                // (audit #5 D5/F4)
                envelope["truncated"]   = sr.truncated;
                perQuery.push_back(envelope);
            }

            json data;
            data["query_count"]     = static_cast<int>(queries.size());
            data["total"]           = grandTotal;
            data["scanned_classes"] = totalScannedClasses;
            data["scanned_objects"] = totalScannedObjects;   // walked, not pool size
            data["aborted"]         = !batchResults.empty() && batchResults[0].aborted;
            data["per_query"]       = perQuery;
            return Renge::MakeResponse(id, data).dump();
        }

        // === begin_value_scan: CE-style First Scan. Walks GObjects +
        // UProperty metadata for every UPROPERTY-declared field of the
        // requested DataType across all UObject instances, applies the
        // (scan_type, value[, value2]) predicate, and returns enriched
        // candidates + a session_id for follow-up refine_value_scan
        // calls. See Radar.h for the lifecycle contract.
        //
        // Native C++ fields (non-UPROPERTY) are intentionally NOT
        // scanned -- the UI's Value Search tab MUST surface this caveat
        // in a banner. See memory project_value_search_caveats.
        if (cmd == Renge::CMD_BEGIN_VALUE_SCAN) {
            std::string dtStr = request.value("data_type", "");
            std::string stStr = request.value("scan_type", "Exact");
            std::string valStr = request.value("value", "");
            std::string val2Str = request.value("value2", "");
            bool gameOnly = request.value("game_only", true);
            int  maxResults = request.value("max_results", 50000);
            // Displayed-integer rounding mode (build 1672, replaces the old
            // tolerance slack): Float/Double values are reduced to the integer
            // the game shows before comparing; integer types only consult it to
            // coerce a fractional target/bound. Default Round (legacy half-away);
            // unknown / absent (pre-1672 client) falls back to Round.
            Radar::RoundMode roundMode = Radar::RoundMode::Round;
            Radar::TryParseRoundMode(request.value("rounding_mode", "Round"), roundMode);
            // String scans only: opt-in case sensitivity. Default is
            // CE-style case-insensitive matching.
            bool caseSensitive = request.value("case_sensitive", false);
            // Parallel GObjects walk. Default true (fast). The UI sends
            // parallel=false to force a single-threaded scan when the user wants
            // to avoid concurrent cross-thread reads that some games' anti-tamper
            // flags — slower but stealthier.
            bool parallel = request.value("parallel", true);
            // Per-object batch body read. Default true (fewer SEH reads + better
            // locality). UI sends batch_read=false to force one read per field.
            bool batchRead = request.value("batch_read", true);
            // Opt-in deep-container pass (default off): reach values buried in
            // deeply-nested containers the auto heuristic doesn't flag.
            bool deep = request.value("deep", false);
            // Native-C (P1, opt-in, default off): also scan each object's
            // unmanaged holes (non-UPROPERTY bytes) for the value, at native_align
            // stride (1/2/4/8, default 4). newest_first walks GObjects high-index
            // first so truncated results keep the newest instances (the UI couples
            // newest_first on by default with native_c).
            bool nativeC     = request.value("native_c", false);
            int32_t nativeAlign = request.value("native_align", 4);
            bool newestFirst = request.value("newest_first", false);
            // User-adjustable scan deadline (Value Search "Timeout" slider, 10-60s).
            // Default 15000ms. Clamp to a sane band so a malformed request can't
            // hang the scan thread forever or starve it below a useful budget.
            int32_t deadlineMs = request.value("deadline_ms", 15000);
            if (deadlineMs < 1000)   deadlineMs = 1000;
            if (deadlineMs > 300000) deadlineMs = 300000;
            // "Auto detect Engine/System noise" pre-filter (opt-in, default off):
            // skip pure engine/system classes at the source so their instances never
            // enter the candidate set. Gameplay guardrail (Pawn/Actor/component/...)
            // is enforced DLL-side, so a player Pawn's X/Y/Z is never skipped.
            bool autoSkipNoise = request.value("auto_skip_noise", false);

            Radar::DataType dt;
            if (!Radar::TryParseDataType(dtStr, dt)) {
                return Renge::MakeError(id, "Unknown data_type: " + dtStr).dump();
            }
            Radar::ScanType st;
            if (!Radar::TryParseScanType(stStr, st)) {
                return Renge::MakeError(id, "Unknown scan_type: " + stStr).dump();
            }
            if (!Radar::IsFirstScanType(st)) {
                return Renge::MakeError(id, "scan_type '" + stStr +
                    "' is only valid for refine (no prevValue on first scan)").dump();
            }
            if (!Radar::IsScanTypeValidFor(dt, st)) {
                return Renge::MakeError(id, "scan_type '" + stStr +
                    "' is not valid for data_type '" + dtStr + "'").dump();
            }

            const bool isString = Radar::IsStringDataType(dt);
            const bool isVector = Radar::IsVectorDataType(dt);
            const bool isMulti  = Radar::IsMultiNumericDataType(dt);

            uint8_t targetBytes[12] = {};
            uint8_t target2Bytes[12] = {};
            std::string targetString;
            const uint8_t* target2Ptr = nullptr;
            // Multi-numeric meta scan: per-width target sets replace the
            // single byte buffer. Built once here, pointed at by the
            // pointers passed to ScanForValue.
            Radar::NumericTargetSet multiTargets, multiTargets2;
            const Radar::NumericTargetSet* multiPtr  = nullptr;
            const Radar::NumericTargetSet* multiPtr2 = nullptr;

            if (isString) {
                // String scans take the user's needle verbatim. Empty
                // needle is rejected at the C# layer; defensively
                // accept here so Refine-with-empty (rare) still works.
                targetString = valStr;
            } else if (isVector) {
                if (!ParseVectorBytes(valStr, targetBytes)) {
                    return Renge::MakeError(id, "Invalid 'value' for data_type " + dtStr +
                        " (expected 'X,Y,Z' float triple)").dump();
                }
                if (st == Radar::ScanType::Between) {
                    if (!ParseVectorBytes(val2Str, target2Bytes)) {
                        return Renge::MakeError(id, "Between requires 'value2' for data_type " + dtStr +
                            " (expected 'X,Y,Z' float triple)").dump();
                    }
                    target2Ptr = target2Bytes;
                }
            } else if (isMulti) {
                if (!Radar::BuildNumericTargets(dt, valStr, multiTargets, roundMode)) {
                    return Renge::MakeError(id, "Invalid 'value' for data_type " + dtStr +
                        " (does not fit any numeric width)").dump();
                }
                multiPtr = &multiTargets;
                if (st == Radar::ScanType::Between) {
                    if (!Radar::BuildNumericTargets(dt, val2Str, multiTargets2, roundMode)) {
                        return Renge::MakeError(id, "Between requires a valid 'value2' for data_type " + dtStr).dump();
                    }
                    multiPtr2 = &multiTargets2;
                }
            } else {
                if (!ParseValueBytes(dt, valStr, targetBytes, roundMode)) {
                    return Renge::MakeError(id, "Invalid 'value' for data_type " + dtStr).dump();
                }
                if (st == Radar::ScanType::Between) {
                    if (!ParseValueBytes(dt, val2Str, target2Bytes, roundMode)) {
                        return Renge::MakeError(id, "Between requires 'value2' for data_type " + dtStr).dump();
                    }
                    target2Ptr = target2Bytes;
                }
            }

            auto scanResult = Aura::ScanForValue(
                dt, st, targetBytes, target2Ptr, gameOnly, maxResults,
                roundMode, targetString, caseSensitive, multiPtr, multiPtr2,
                parallel, batchRead, deep, nativeC, nativeAlign, newestFirst, deadlineMs,
                autoSkipNoise);

            uint64_t sessionId = Radar::SessionManager::Instance().Begin(
                dt, std::move(scanResult.candidates),
                std::move(scanResult.descriptors), std::move(scanResult.instances));

            // V3-C: the DLL session OWNS the full candidate set; the UI is a
            // windowed view. Return `total` (full count) + only the FIRST PAGE
            // in scan order. The UI pages / filters / sorts via the separate
            // query_candidates command (server-side over the full set). Before
            // V3-C this echoed back ALL candidates, which didn't scale.
            int pageSize = request.value("page_size", 1000);
            if (pageSize < 0) pageSize = 0;
            json candidates = json::array();
            int totalCount = 0;
            json histogram = json::array();
            int classDistinct = 0;
            Radar::SessionManager::Instance().ViewWith(sessionId,
                [&](const Radar::Session& sess) {
                    totalCount = static_cast<int>(sess.candidates.size());
                    const int n = (std::min)(pageSize, totalCount);
                    for (int i = 0; i < n; ++i)
                        candidates.push_back(CandidateToJson(
                            sess.candidates[i], sess.dt, sess.descriptors, sess.instances));
                    // Class-noise histogram over the FULL set (top 40 + distinct count).
                    auto hist = Radar::BuildClassHistogram(sess.candidates, sess.descriptors);
                    classDistinct = static_cast<int>(hist.size());
                    histogram = HistogramToJson(hist, kClassHistogramMaxRows);
                });

            json data;
            data["session_id"]      = sessionId;
            data["data_type"]       = Radar::NameOf(dt);
            data["total"]           = totalCount;
            data["page_size"]       = pageSize;
            data["scanned_classes"] = scanResult.stats.scannedClasses;
            data["scanned_objects"] = scanResult.stats.scannedObjects;
            data["duration_ms"]     = static_cast<int64_t>(scanResult.stats.durationMs);
            data["deadline_hit"]    = scanResult.stats.deadlineHit;
            data["candidates"]      = candidates;
            data["class_histogram"] = histogram;
            data["class_distinct"]  = classDistinct;
            return Renge::MakeResponse(id, data).dump();
        }

        // === refine_value_scan: CE-style Next Scan. Re-reads each
        // candidate's bytes, prunes with the (scan_type, value[, value2])
        // predicate. prev-value scan types (Changed / Unchanged /
        // Increased / Decreased) compare against the candidate's last
        // observed bytes; targeted scan types (Exact / Bigger / Smaller
        // / Between) compare against the supplied value(s). Updates
        // prevValue on survivors so the NEXT refine compares against
        // bytes captured during THIS refine. ===
        if (cmd == Renge::CMD_REFINE_VALUE_SCAN) {
            uint64_t sessionId = request.value("session_id", 0ULL);
            if (sessionId == 0) {
                return Renge::MakeError(id, "Missing or zero session_id").dump();
            }
            std::string stStr = request.value("scan_type", "");
            std::string valStr = request.value("value", "");
            std::string val2Str = request.value("value2", "");
            // Displayed-integer rounding mode (build 1672, replaces tolerance).
            Radar::RoundMode roundMode = Radar::RoundMode::Round;
            Radar::TryParseRoundMode(request.value("rounding_mode", "Round"), roundMode);
            bool caseSensitive = request.value("case_sensitive", false);

            Radar::ScanType st;
            if (!Radar::TryParseScanType(stStr, st)) {
                return Renge::MakeError(id, "Unknown scan_type: " + stStr).dump();
            }

            // V3-C: like begin, refine returns `total` (surviving count) + only
            // the FIRST PAGE in scan order; the UI re-pages/filters/sorts via
            // query_candidates over the pruned set.
            int pageSize = request.value("page_size", 1000);
            if (pageSize < 0) pageSize = 0;
            int totalCount = 0;
            Radar::DataType dtCaptured = Radar::DataType::Int32;
            json candidates = json::array();
            json histogram = json::array();
            int classDistinct = 0;
            Aura::ValueScanStats stats;
            bool parseFailed = false;
            bool scanTypeInvalid = false;
            bool found = Radar::SessionManager::Instance().RefineWith(sessionId,
                [&](Radar::Session& sess) {
                    const Radar::DataType dt = sess.dt;
                    auto& cs = sess.candidates;
                    dtCaptured = dt;
                    if (!Radar::IsScanTypeValidFor(dt, st)) {
                        scanTypeInvalid = true;
                        return;
                    }

                    const bool isString = Radar::IsStringDataType(dt);
                    const bool isVector = Radar::IsVectorDataType(dt);
                    const bool isMulti  = Radar::IsMultiNumericDataType(dt);

                    uint8_t targetBytes[12] = {};
                    uint8_t target2Bytes[12] = {};
                    const uint8_t* tgtPtr  = nullptr;
                    const uint8_t* tgt2Ptr = nullptr;
                    std::string targetString;
                    Radar::NumericTargetSet multiTargets, multiTargets2;
                    const Radar::NumericTargetSet* multiPtr  = nullptr;
                    const Radar::NumericTargetSet* multiPtr2 = nullptr;

                    if (!Radar::IsPrevValueScanType(st)) {
                        if (isString) {
                            targetString = valStr;
                        } else if (isVector) {
                            if (!ParseVectorBytes(valStr, targetBytes)) {
                                parseFailed = true;
                                return;
                            }
                            tgtPtr = targetBytes;
                            if (st == Radar::ScanType::Between) {
                                if (!ParseVectorBytes(val2Str, target2Bytes)) {
                                    parseFailed = true;
                                    return;
                                }
                                tgt2Ptr = target2Bytes;
                            }
                        } else if (isMulti) {
                            if (!Radar::BuildNumericTargets(dt, valStr, multiTargets, roundMode)) {
                                parseFailed = true;
                                return;
                            }
                            multiPtr = &multiTargets;
                            if (st == Radar::ScanType::Between) {
                                if (!Radar::BuildNumericTargets(dt, val2Str, multiTargets2, roundMode)) {
                                    parseFailed = true;
                                    return;
                                }
                                multiPtr2 = &multiTargets2;
                            }
                        } else {
                            if (!ParseValueBytes(dt, valStr, targetBytes, roundMode)) {
                                parseFailed = true;
                                return;
                            }
                            tgtPtr = targetBytes;
                            if (st == Radar::ScanType::Between) {
                                if (!ParseValueBytes(dt, val2Str, target2Bytes, roundMode)) {
                                    parseFailed = true;
                                    return;
                                }
                                tgt2Ptr = target2Bytes;
                            }
                        }
                    }

                    stats = Aura::RefineCandidates(dt, st, tgtPtr, tgt2Ptr, cs,
                                                   sess.descriptors,
                                                   roundMode, targetString, caseSensitive,
                                                   multiPtr, multiPtr2);
                    totalCount = static_cast<int>(cs.size());
                    const int n = (std::min)(pageSize, totalCount);
                    for (int i = 0; i < n; ++i)
                        candidates.push_back(CandidateToJson(
                            cs[i], dt, sess.descriptors, sess.instances));
                    // Recompute the class histogram over the pruned survivor set.
                    auto hist = Radar::BuildClassHistogram(cs, sess.descriptors);
                    classDistinct = static_cast<int>(hist.size());
                    histogram = HistogramToJson(hist, kClassHistogramMaxRows);
                });

            if (!found) {
                return Renge::MakeError(id, "session_not_found").dump();
            }
            if (scanTypeInvalid) {
                return Renge::MakeError(id, "scan_type '" + stStr +
                    "' is not valid for session's data_type").dump();
            }
            if (parseFailed) {
                return Renge::MakeError(id, "Invalid 'value' or 'value2' for session's data_type").dump();
            }

            json data;
            data["session_id"]   = sessionId;
            data["data_type"]    = Radar::NameOf(dtCaptured);
            data["scan_type"]    = stStr;
            data["total"]        = totalCount;
            data["page_size"]    = pageSize;
            data["duration_ms"]  = static_cast<int64_t>(stats.durationMs);
            data["candidates"]   = candidates;
            data["class_histogram"] = histogram;
            data["class_distinct"]  = classDistinct;
            return Renge::MakeResponse(id, data).dump();
        }

        // === end_value_scan: drop a value-scan session. Idempotent;
        // returns ok=true even when the session was already gone (e.g.
        // 5-minute idle expiry already swept it). ===
        if (cmd == Renge::CMD_END_VALUE_SCAN) {
            uint64_t sessionId = request.value("session_id", 0ULL);
            if (sessionId == 0) {
                return Renge::MakeError(id, "Missing or zero session_id").dump();
            }
            bool ended = Radar::SessionManager::Instance().End(sessionId);
            json data;
            data["session_id"] = sessionId;
            data["ended"]      = ended;
            return Renge::MakeResponse(id, data).dump();
        }

        // === query_candidates: server-side window over a value-scan session
        // (V3-C). The DLL owns the full candidate set; this filters
        // (case-insensitive substring over the displayed columns) + sorts
        // (by sort_key/sort_desc) over the WHOLE set and returns only the
        // requested [offset, offset+limit) window. Pure data work over the
        // DLL's own pools — no game-memory reads, so the game thread is never
        // touched. The ordered view is cached on the session so plain paging
        // doesn't re-sort. ===
        if (cmd == Renge::CMD_QUERY_CANDIDATES) {
            uint64_t sessionId = request.value("session_id", 0ULL);
            if (sessionId == 0) {
                return Renge::MakeError(id, "Missing or zero session_id").dump();
            }
            int offset = request.value("offset", 0);
            int limit  = request.value("limit", 1000);
            std::string filter     = request.value("filter", "");
            std::string sortKeyStr = request.value("sort_key", "");
            bool sortDesc = request.value("sort_desc", false);
            std::vector<std::string> excludeClasses = ParseExcludeClasses(request);
            if (offset < 0) offset = 0;
            if (limit  < 0) limit  = 0;

            Radar::SortKey sortKey;
            if (!Radar::TryParseSortKey(sortKeyStr, sortKey)) {
                return Renge::MakeError(id, "Unknown sort_key: " + sortKeyStr).dump();
            }

            json candidates = json::array();
            int totalCount    = 0;
            int filteredCount = 0;
            std::string dtName;
            bool found = Radar::SessionManager::Instance().QueryWith(
                sessionId, filter, sortKey, sortDesc, excludeClasses,
                [&](const Radar::Session& sess, const std::vector<uint32_t>& order) {
                    totalCount    = static_cast<int>(sess.candidates.size());
                    filteredCount = static_cast<int>(order.size());
                    dtName        = Radar::NameOf(sess.dt);
                    const int begin = (std::min)(offset, filteredCount);
                    const int end   = (std::min)(offset + limit, filteredCount);
                    for (int i = begin; i < end; ++i) {
                        const uint32_t ci = order[i];
                        candidates.push_back(CandidateToJson(
                            sess.candidates[ci], sess.dt,
                            sess.descriptors, sess.instances));
                    }
                });
            if (!found) {
                return Renge::MakeError(id, "session_not_found").dump();
            }

            json data;
            data["session_id"]     = sessionId;
            data["data_type"]      = dtName;
            data["total"]          = totalCount;     // full session size
            data["filtered_total"] = filteredCount;  // matches after filter
            data["offset"]         = offset;
            data["count"]          = static_cast<int>(candidates.size());
            data["candidates"]     = candidates;
            return Renge::MakeResponse(id, data).dump();
        }

        // === begin_group_scan: Multiple values group scan (build 1276). Find
        // objects that SIMULTANEOUSLY hold ALL of N user values (2..4) at
        // DISTINCT numeric-property offsets, in any order. P1: each slot is a
        // NumericNoByte/NumericAll exact match. Returns a session_id + first page
        // of OBJECT-level candidates (nested per-slot matches). ===
        if (cmd == Renge::CMD_BEGIN_GROUP_SCAN) {
            bool gameOnly  = request.value("game_only", true);
            int  maxResults = request.value("max_results", 50000);
            int  pageSize   = request.value("page_size", 1000);
            // Opt-in deep mode: also treat each numeric container / struct-array
            // element as its own block so a group hidden in a deeply-nested array
            // (e.g. ...WeaponTuneList[0].Tunes[N]) is found.
            bool deep = request.value("deep", false);
            // Opt-in cross-object mode (P4): fold each actor's OWNED sub-object
            // (components + GAS AttributeSets) numeric leaves into the actor's block.
            bool crossObject = request.value("cross_object", false);
            // Opt-in Native-C mode (P2): also fold each object's unmanaged-hole
            // leaves (non-UPROPERTY bytes) into its block — object block only,
            // bounded per object. Intentionally noisy on first scan.
            bool nativeC = request.value("native_c", false);
            // Newest-first (P2): walk high-index objects first so a deadline-
            // truncated huge game keeps the newest objects (UI coupled with native).
            bool newestFirst = request.value("newest_first", false);
            // User-adjustable scan deadline (Value Search "Timeout" slider, 10-60s).
            // Default 15000ms; clamped to the same band as begin_value_scan.
            int32_t deadlineMs = request.value("deadline_ms", 15000);
            if (deadlineMs < 1000)   deadlineMs = 1000;
            if (deadlineMs > 300000) deadlineMs = 300000;
            if (pageSize < 0) pageSize = 0;
            // "Auto detect Engine/System noise" pre-filter (opt-in, default off) —
            // same source-level skip + gameplay guardrail as begin_value_scan.
            bool autoSkipNoise = request.value("auto_skip_noise", false);

            if (!request.contains("values") || !request["values"].is_array()) {
                return Renge::MakeError(id, "begin_group_scan requires a 'values' array").dump();
            }
            const auto& valuesJson = request["values"];
            if (valuesJson.size() < 2 || valuesJson.size() > 4) {
                return Renge::MakeError(id, "group scan requires 2..4 values").dump();
            }

            std::vector<Radar::SlotSpec> slots;
            slots.reserve(valuesJson.size());
            for (const auto& vj : valuesJson) {
                std::string dtStr  = vj.value("data_type", "NumericNoByte");
                std::string valStr = vj.value("value", "");
                Radar::DataType dt;
                if (!Radar::TryParseDataType(dtStr, dt)) {
                    return Renge::MakeError(id, "Unknown data_type in values: " + dtStr).dump();
                }
                // Slots fan out over numeric widths (NumericNoByte default /
                // NumericAll); concrete per-slot widths remain a later extension.
                if (!Radar::IsMultiNumericDataType(dt)) {
                    return Renge::MakeError(id, "group slot data_type must be NumericNoByte or NumericAll: " + dtStr).dump();
                }
                // P2: per-slot first-scan predicate (default Exact). The targeted
                // types make sense on the first scan — Exact / Bigger / Smaller /
                // Between (the last carries an upper bound in `value2`). Prev-value
                // types have no baseline yet, and substring types are string-only.
                Radar::ScanType st = Radar::ScanType::Exact;
                std::string stStr = vj.value("scan_type", "Exact");
                if (!Radar::TryParseScanType(stStr, st)) {
                    return Renge::MakeError(id, "Unknown scan_type in values: " + stStr).dump();
                }
                if (Radar::IsPrevValueScanType(st) || Radar::IsSubstringScanType(st)) {
                    return Renge::MakeError(id, "group first-scan scan_type must be Exact / Bigger / Smaller / Between: " + stStr).dump();
                }
                Radar::SlotSpec sp;
                // Per-slot displayed-integer rounding mode (build 1672, replaces
                // tolerance). Parsed BEFORE BuildNumericTargets so a fractional
                // group value/bound coerces to an integer via the slot's mode.
                Radar::TryParseRoundMode(vj.value("rounding_mode", "Round"), sp.roundMode);
                if (!Radar::BuildNumericTargets(dt, valStr, sp.targets, sp.roundMode)) {
                    return Renge::MakeError(id, "Invalid group value '" + valStr + "' (fits no numeric width)").dump();
                }
                if (st == Radar::ScanType::Between) {
                    std::string val2Str = vj.value("value2", "");
                    if (!Radar::BuildNumericTargets(dt, val2Str, sp.targets2, sp.roundMode)) {
                        return Renge::MakeError(id, "Invalid group Between upper value '" + val2Str + "' (fits no numeric width)").dump();
                    }
                    sp.value2 = val2Str;
                }
                sp.dt        = dt;
                sp.st        = st;
                sp.value     = valStr;
                slots.push_back(std::move(sp));
            }
            const int slotCount = static_cast<int>(slots.size());

            // Opt-in leaf budget per slot. Clamped, not trusted: too small silently hides
            // a derived class's own fields (the old fixed 8 did exactly that), too large
            // is a memory footgun on a 500-field object x 4 slots x every candidate.
            int perSlotCap = request.value("per_slot_cap", Orden::kDefaultPerSlotCap);
            if (perSlotCap < 8)    perSlotCap = 8;
            if (perSlotCap > 4096) perSlotCap = 4096;
            auto scanResult = Aura::ScanForValueGroup(slots, gameOnly, maxResults, deep, crossObject, nativeC, newestFirst, deadlineMs, autoSkipNoise, perSlotCap);

            uint64_t sessionId = Radar::GroupSessionManager::Instance().Begin(
                std::move(slots), std::move(scanResult.candidates),
                std::move(scanResult.descriptors), std::move(scanResult.instances));

            // Like begin_value_scan: the DLL session owns the full set; return
            // `total` + only the first page (scan order) — the UI pages/filters/
            // sorts via query_group_candidates.
            json candidates = json::array();
            int totalCount = 0;
            json histogram = json::array();
            int classDistinct = 0;
            Radar::GroupSessionManager::Instance().QueryWith(
                sessionId, "", Radar::SortKey::ScanOrder, false, std::vector<std::string>{},
                [&](const Radar::GroupSession& sess, const std::vector<uint32_t>& order) {
                    totalCount = static_cast<int>(sess.candidates.size());
                    const int n = (std::min)(pageSize, static_cast<int>(order.size()));
                    for (int i = 0; i < n; ++i)
                        candidates.push_back(GroupCandidateToJson(
                            sess.candidates[order[i]], sess.slots, sess.descriptors,
                            sess.instances));
                    // Class-noise histogram over the FULL set (object-level class).
                    auto hist = Radar::BuildGroupClassHistogram(sess.candidates, sess.descriptors);
                    classDistinct = static_cast<int>(hist.size());
                    histogram = HistogramToJson(hist, kClassHistogramMaxRows);
                });

            json data;
            data["session_id"]      = sessionId;
            data["total"]           = totalCount;
            data["page_size"]       = pageSize;
            data["slot_count"]      = slotCount;
            data["scanned_classes"] = scanResult.stats.scannedClasses;
            data["scanned_objects"] = scanResult.stats.scannedObjects;
            data["duration_ms"]     = static_cast<int64_t>(scanResult.stats.durationMs);
            data["deadline_hit"]    = scanResult.stats.deadlineHit;
            data["candidates"]      = candidates;
            data["class_histogram"] = histogram;
            data["class_distinct"]  = classDistinct;
            return Renge::MakeResponse(id, data).dump();
        }

        // === refine_group_scan: Next Scan for a group session. New `values`
        // (same count as the first scan) replace each slot's target; survivors
        // are objects where every slot still matches at a distinct offset
        // (convergence narrows the located offsets toward a lock). ===
        if (cmd == Renge::CMD_REFINE_GROUP_SCAN) {
            uint64_t sessionId = request.value("session_id", 0ULL);
            if (sessionId == 0) {
                return Renge::MakeError(id, "Missing or zero session_id").dump();
            }
            if (!request.contains("values") || !request["values"].is_array()) {
                return Renge::MakeError(id, "refine_group_scan requires a 'values' array").dump();
            }
            const auto& valuesJson = request["values"];
            int pageSize = request.value("page_size", 1000);
            if (pageSize < 0) pageSize = 0;

            json candidates = json::array();
            int  totalCount = 0;
            json histogram = json::array();
            int  classDistinct = 0;
            bool countMismatch = false, parseFailed = false;
            std::string badScanType;
            Aura::ValueScanStats stats;
            bool found = Radar::GroupSessionManager::Instance().RefineWith(sessionId,
                [&](Radar::GroupSession& sess) {
                    if (valuesJson.size() != sess.slots.size()) { countMismatch = true; return; }
                    // Re-target each slot from the new (value, scan_type). The dt is
                    // fixed by the first scan. P2: prev-value predicates carry NO
                    // value (they compare each leaf against its own prevValue), so
                    // only targeted types rebuild the numeric target set.
                    for (size_t s = 0; s < sess.slots.size(); ++s) {
                        std::string valStr = valuesJson[s].value("value", "");
                        std::string stStr  = valuesJson[s].value("scan_type", "Exact");
                        Radar::ScanType st;
                        if (!Radar::TryParseScanType(stStr, st)
                            || Radar::IsSubstringScanType(st)) {
                            badScanType = stStr; return;
                        }
                        sess.slots[s].st        = st;
                        // Per-slot displayed-integer rounding mode (build 1672,
                        // replaces tolerance). Set before BuildNumericTargets so a
                        // fractional refine value/bound coerces via the slot's mode.
                        Radar::TryParseRoundMode(valuesJson[s].value("rounding_mode", "Round"),
                                                 sess.slots[s].roundMode);
                        if (Radar::IsPrevValueScanType(st)) {
                            sess.slots[s].value    = valStr;  // echoed for display (may be "")
                            sess.slots[s].targets  = Radar::NumericTargetSet{};
                            sess.slots[s].value2   = "";
                            sess.slots[s].targets2 = Radar::NumericTargetSet{};
                        } else {
                            Radar::NumericTargetSet nt;
                            if (!Radar::BuildNumericTargets(sess.slots[s].dt, valStr, nt, sess.slots[s].roundMode)) {
                                parseFailed = true;
                                return;
                            }
                            sess.slots[s].targets = std::move(nt);
                            sess.slots[s].value   = valStr;
                            // Between carries an upper bound in value2; other
                            // targeted types clear it so a stale bound can't linger.
                            if (st == Radar::ScanType::Between) {
                                std::string val2Str = valuesJson[s].value("value2", "");
                                Radar::NumericTargetSet nt2;
                                if (!Radar::BuildNumericTargets(sess.slots[s].dt, val2Str, nt2, sess.slots[s].roundMode)) {
                                    parseFailed = true;
                                    return;
                                }
                                sess.slots[s].targets2 = std::move(nt2);
                                sess.slots[s].value2   = val2Str;
                            } else {
                                sess.slots[s].value2   = "";
                                sess.slots[s].targets2 = Radar::NumericTargetSet{};
                            }
                        }
                    }
                    stats = Aura::RefineGroupCandidates(sess.slots, sess.candidates,
                                                        sess.descriptors, sess.instances);
                    totalCount = static_cast<int>(sess.candidates.size());
                    const int n = (std::min)(pageSize, totalCount);
                    for (int i = 0; i < n; ++i)
                        candidates.push_back(GroupCandidateToJson(
                            sess.candidates[i], sess.slots, sess.descriptors, sess.instances));
                    // Recompute the class histogram over the pruned survivor set.
                    auto hist = Radar::BuildGroupClassHistogram(sess.candidates, sess.descriptors);
                    classDistinct = static_cast<int>(hist.size());
                    histogram = HistogramToJson(hist, kClassHistogramMaxRows);
                });

            if (!found)             return Renge::MakeError(id, "session_not_found").dump();
            if (countMismatch)      return Renge::MakeError(id, "refine value count must match the first scan").dump();
            if (!badScanType.empty()) return Renge::MakeError(id, "group refine scan_type must be Exact / Bigger / Smaller / Between / Changed / Unchanged / Increased / Decreased: " + badScanType).dump();
            if (parseFailed)        return Renge::MakeError(id, "Invalid refine value (fits no numeric width)").dump();

            json data;
            data["session_id"]  = sessionId;
            data["total"]       = totalCount;
            data["page_size"]   = pageSize;
            data["duration_ms"] = static_cast<int64_t>(stats.durationMs);
            data["candidates"]  = candidates;
            data["class_histogram"] = histogram;
            data["class_distinct"]  = classDistinct;
            return Renge::MakeResponse(id, data).dump();
        }

        // === end_group_scan: drop a group-scan session (idempotent). ===
        if (cmd == Renge::CMD_END_GROUP_SCAN) {
            uint64_t sessionId = request.value("session_id", 0ULL);
            if (sessionId == 0) {
                return Renge::MakeError(id, "Missing or zero session_id").dump();
            }
            bool ended = Radar::GroupSessionManager::Instance().End(sessionId);
            json data;
            data["session_id"] = sessionId;
            data["ended"]      = ended;
            return Renge::MakeResponse(id, data).dump();
        }

        // === query_group_candidates: server-side window over a group-scan
        // session (filter + sort + page over OBJECT-level rows). ===
        if (cmd == Renge::CMD_QUERY_GROUP_CANDIDATES) {
            uint64_t sessionId = request.value("session_id", 0ULL);
            if (sessionId == 0) {
                return Renge::MakeError(id, "Missing or zero session_id").dump();
            }
            int offset = request.value("offset", 0);
            int limit  = request.value("limit", 1000);
            std::string filter     = request.value("filter", "");
            std::string sortKeyStr = request.value("sort_key", "");
            bool sortDesc = request.value("sort_desc", false);
            std::vector<std::string> excludeClasses = ParseExcludeClasses(request);
            if (offset < 0) offset = 0;
            if (limit  < 0) limit  = 0;

            Radar::SortKey sortKey;
            if (!Radar::TryParseSortKey(sortKeyStr, sortKey)) {
                return Renge::MakeError(id, "Unknown sort_key: " + sortKeyStr).dump();
            }

            json candidates = json::array();
            int totalCount    = 0;
            int filteredCount = 0;
            bool found = Radar::GroupSessionManager::Instance().QueryWith(
                sessionId, filter, sortKey, sortDesc, excludeClasses,
                [&](const Radar::GroupSession& sess, const std::vector<uint32_t>& order) {
                    totalCount    = static_cast<int>(sess.candidates.size());
                    filteredCount = static_cast<int>(order.size());
                    const int begin = (std::min)(offset, filteredCount);
                    const int end   = (std::min)(offset + limit, filteredCount);
                    for (int i = begin; i < end; ++i)
                        candidates.push_back(GroupCandidateToJson(
                            sess.candidates[order[i]], sess.slots, sess.descriptors,
                            sess.instances, filter));
                });
            if (!found) {
                return Renge::MakeError(id, "session_not_found").dump();
            }

            json data;
            data["session_id"]     = sessionId;
            data["total"]          = totalCount;
            data["filtered_total"] = filteredCount;
            data["offset"]         = offset;
            data["count"]          = static_cast<int>(candidates.size());
            data["candidates"]     = candidates;
            return Renge::MakeResponse(id, data).dump();
        }

        // === query_group_slot_leaves: every leaf ONE slot of ONE candidate kept,
        // BY NAME (build 2719).
        //
        // A group row can only display a single assignment, however many the
        // candidate satisfies. On the DumperTest sample a `Changed` + `Unchanged`
        // refine kept {Health.CurrentValue, TickCount} for slot 0 and 36 leaves
        // including FrozenInt for slot 1 — two equally valid pairs, one row. The
        // others existed on the wire only as raw integers in `matched_offsets`,
        // and an integer cannot tell a user that 1308 is `FrozenInt`, so a correct
        // match was reported as a miss four separate times.
        //
        // On demand, per expanded row — never inlined into the paged list, which
        // carries up to 1000 candidates x N slots x per_slot_cap (<= 4096) leaves. ===
        if (cmd == Renge::CMD_QUERY_GROUP_SLOT_LEAVES) {
            uint64_t sessionId = request.value("session_id", 0ULL);
            if (sessionId == 0) {
                return Renge::MakeError(id, "Missing or zero session_id").dump();
            }
            uintptr_t instanceAddr = 0;
            if (!Renge::TryStrToAddr(request.value("instance_addr", ""), instanceAddr)
                || instanceAddr == 0) {
                return Renge::MakeError(id, "Missing or malformed instance_addr").dump();
            }
            const int slotIndex = request.value("slot_index", -1);
            if (slotIndex < 0) {
                return Renge::MakeError(id, "Missing or negative slot_index").dump();
            }
            int offset = request.value("offset", 0);
            int limit  = request.value("limit", kGroupSlotLeafMaxRows);
            if (offset < 0) offset = 0;
            if (limit  < 0) limit  = 0;
            if (limit > kGroupSlotLeafMaxRows) limit = kGroupSlotLeafMaxRows;

            // Optional tie-breaker. One UObject can own MANY candidates: with
            // `deep`, Aura emits one GroupCandidate per container BLOCK and they all
            // intern to the same InstanceRecord ("blocks share", Aura.cpp:8163), so
            // instance_addr alone is ambiguous and first-match-wins would answer an
            // expanded deep row with a DIFFERENT block's fields — a silent wrong
            // answer, in precisely the feature meant to end silent wrong answers.
            // The row already knows its displayed leaf's absolute address; a leaf
            // address belongs to exactly one block, so it identifies the candidate.
            // NOT a candidate index: RefineGroupCandidates rebuilds the vector, so
            // any index is stale after the next refine.
            uintptr_t leafHint = 0;
            Renge::TryStrToAddr(request.value("leaf_addr", ""), leafHint);

            json leaves = json::array();
            int  total = 0;
            bool candidateFound = false, slotFound = false, staleHint = false;
            const bool sessionFound = Radar::GroupSessionManager::Instance().WithSession(
                sessionId, [&](const Radar::GroupSession& sess) {
                    const Radar::GroupCandidate* chosen = nullptr;
                    const Radar::GroupCandidate* firstByAddr = nullptr;
                    int sharingAddr = 0;   // how many candidates this UObject owns
                    for (const Radar::GroupCandidate& gc : sess.candidates) {
                        if (gc.instanceIdx >= sess.instances.size()) continue;
                        if (sess.instances[gc.instanceIdx].instanceAddr != instanceAddr) continue;
                        ++sharingAddr;
                        if (!firstByAddr) firstByAddr = &gc;
                        if (leafHint == 0) break;                 // no hint: first wins
                        if (chosen) break;                        // found it, and not alone
                        if (static_cast<size_t>(slotIndex) >= gc.slotMatches.size()) continue;
                        for (const Radar::GroupSlotMatch& m : gc.slotMatches[slotIndex])
                            if (m.leafAddr == leafHint) { chosen = &gc; break; }
                    }
                    if (!chosen) {
                        // The hint matched nothing: the caller's row is stale (a refine
                        // dropped that leaf). With ONE candidate per address the
                        // fallback is exact — there is nothing to be wrong about. With
                        // several (deep: one per container block) it would be a GUESS,
                        // and answering a stale row with another block's fields is
                        // precisely the silent wrong answer this command exists to end.
                        // Refuse, and let the caller re-query the row.
                        if (leafHint != 0 && sharingAddr > 1) { staleHint = true; return; }
                        chosen = firstByAddr;
                    }
                    if (!chosen) return;
                    candidateFound = true;
                    if (static_cast<size_t>(slotIndex) >= chosen->slotMatches.size()
                        || static_cast<size_t>(slotIndex) >= sess.slots.size()) return;
                    slotFound = true;
                    const auto& matches = chosen->slotMatches[slotIndex];
                    // The object's OWN fields first. Leaves are collected
                    // base-class-first, so without this an actor's list opens with
                    // thirty engine fields and `FrozenInt` is off the bottom of a
                    // scrolling box — the list existed to make it findable.
                    const std::vector<size_t> order =
                        Radar::OrderGroupSlotLeaves(matches, sess.descriptors);
                    total = static_cast<int>(matches.size());
                    const int begin = (std::min)(offset, total);
                    // begin (not offset) + limit: both are already clamped to small
                    // values, so the sum cannot overflow. `offset` comes straight off
                    // the wire and `offset + limit` is signed-overflow UB for a large
                    // one. (query_group_candidates has the same latent pattern.)
                    const int end   = (std::min)(begin + limit, total);
                    for (int i = begin; i < end; ++i) {
                        // A leaf whose descriptorIdx is out of range encodes to {};
                        // emitting it would put a nameless blank row in the list.
                        json lj = GroupLeafToJson(matches[order[i]], sess.slots[slotIndex],
                                                  sess.descriptors);
                        if (!lj.empty()) leaves.push_back(std::move(lj));
                    }
                });
            if (!sessionFound) return Renge::MakeError(id, "session_not_found").dump();
            if (staleHint)     return Renge::MakeError(id, "stale_leaf_addr").dump();
            if (!candidateFound) return Renge::MakeError(id, "candidate_not_found").dump();
            if (!slotFound)      return Renge::MakeError(id, "slot_index out of range").dump();

            json data;
            data["session_id"]    = sessionId;
            data["instance_addr"] = Renge::AddrToStr(instanceAddr);
            data["slot_index"]    = slotIndex;
            data["total"]         = total;
            data["offset"]        = offset;
            data["count"]         = static_cast<int>(leaves.size());
            data["leaves"]        = leaves;
            return Renge::MakeResponse(id, data).dump();
        }

        // === detect_noise_classes: classify class names as engine/system
        // "noise" for the opt-in auto-detect in the class-noise picker. Marks a
        // class noise iff it lives in an engine package OR its super-chain reaches
        // a pure-engine leaf base (Widget/SoundBase/Texture/MaterialInterface/
        // ParticleSystem/NiagaraSystem/AnimInstance). NEVER name-substring; NEVER
        // ActorComponent. The UI only pre-ticks the (reversible) picker. ===
        if (cmd == Renge::CMD_DETECT_NOISE_CLASSES) {
            std::vector<std::string> names;
            if (request.contains("class_names") && request["class_names"].is_array())
                for (const auto& e : request["class_names"])
                    if (e.is_string()) names.push_back(e.get<std::string>());

            auto verdicts = Aura::ClassifyNoiseClasses(names);
            json arr = json::array();
            for (const auto& v : verdicts) {
                json o;
                o["class_name"] = v.className;
                o["is_noise"]   = v.isNoise;
                o["reason"]     = v.reason;
                arr.push_back(std::move(o));
            }
            json data;
            data["classes"] = arr;
            return Renge::MakeResponse(id, data).dump();
        }

        // === list_classes: List all UClass objects (optionally game-only) ===
        if (cmd == Renge::CMD_LIST_CLASSES) {
            bool gameOnly = request.value("game_only", true);
            int limit = request.value("limit", 5000);

            auto listResult = Aura::ListClasses(gameOnly, limit);

            json classes = json::array();
            for (const auto& e : listResult.results) {
                json item;
                item["class_name"]      = e.className;
                item["class_addr"]      = Renge::AddrToStr(e.classAddr);
                item["class_path"]      = e.classPath;
                item["super_name"]      = e.superName;
                item["property_count"]  = e.propertyCount;
                item["properties_size"] = e.propertiesSize;
                item["score"]           = e.heuristicScore;
                classes.push_back(item);
            }

            json data;
            data["total"]           = static_cast<int>(listResult.results.size());
            data["scanned_objects"] = listResult.scannedObjects;
            data["total_classes"]   = listResult.totalClasses;
            data["classes"]         = classes;
            return Renge::MakeResponse(id, data).dump();
        }

        // === list_all_functions: Flat enumeration of every UFunction across
        // every UClass in GObjects -- backs the "Interesting Functions
        // Finder" UI panel. UI does keyword scoring + categorization
        // client-side so the rules can be tuned without DLL rebuild.
        // Per-function payload is intentionally light (no params); UI
        // calls existing CMD_WALK_FUNCTIONS for the chosen class to fetch
        // full param data on demand. ===
        if (cmd == Renge::CMD_LIST_ALL_FUNCTIONS) {
            bool gameOnly = request.value("game_only", true);
            int limit = request.value("limit", 100000);

            auto enumResult = Aura::EnumerateAllFunctions(gameOnly, limit);

            json functions = json::array();
            for (const auto& e : enumResult.entries) {
                json item;
                item["class_name"]    = e.className;
                item["class_addr"]    = Renge::AddrToStr(e.classAddr);
                item["super_name"]    = e.superName;
                item["class_path"]    = e.classPath;
                item["func_name"]     = e.funcName;
                item["func_addr"]     = Renge::AddrToStr(e.funcAddr);
                item["function_flags"]= e.functionFlags;
                item["num_parms"]     = e.numParms;
                item["parms_size"]    = e.parmsSize;
                functions.push_back(item);
            }

            json data;
            data["total"]            = static_cast<int>(enumResult.entries.size());
            data["scanned_objects"]  = enumResult.scannedObjects;
            data["scanned_classes"]  = enumResult.scannedClasses;
            data["total_functions"]  = enumResult.totalFunctions;
            data["functions"]        = functions;
            return Renge::MakeResponse(id, data).dump();
        }

        // === Live ProcessEvent profiler (Linie) — behaviour-based UFunction
        // discovery. Start records every UFunction* the game dispatches through
        // ProcessEvent; the user performs an in-game action; Stop freezes the
        // table; Get resolves + ranks by fire count. Pipe-only. ===
        if (cmd == Renge::CMD_PE_PROFILE_START) {
            // Force the game-thread PE hook up NOW so we count the game's own
            // calls without first issuing an invoke. hook_active=false means the
            // vtable-offset detection failed on this game → counts will stay 0.
            bool hookActive = UE5_EnsureGameThreadHook();
            Linie::StartRecording();
            Sein::Info("PIPE:profile", "pe_profile_start: recording begun (hook_active=%d)",
                       hookActive ? 1 : 0);
            json data;
            data["recording"]   = true;
            data["hook_active"] = hookActive;
            if (!hookActive) {
                // Distinguish the two failure modes so the UI can advise correctly.
                int peOffset = UE5_GetProcessEventOffset();
                data["hook_detail"] = (peOffset >= 0)
                    ? std::string("PE hook couldn't install (memory near ProcessEvent is busy). "
                                  "Change to another map/scene and Start again — or restart the game + re-inject.")
                    : std::string("ProcessEvent not detected — do any invoke first "
                                  "(Teleport -> Get POV), then Start again.");
            }
            return Renge::MakeResponse(id, data).dump();
        }

        if (cmd == Renge::CMD_PE_PROFILE_STOP) {
            Linie::StopRecording();   // idempotent; counts retained for pe_profile_get
            Sein::Info("PIPE:profile", "pe_profile_stop: recording frozen");
            json data;
            data["recording"] = false;
            return Renge::MakeResponse(id, data).dump();
        }

        // ── get_diagnostics: Sense self-health telemetry ──
        // Tier 1 (our own dispatch cost) + Tier 2 (Win32 process facts). Read
        // only; safe to poll. See Sense.h for why this exists.
        if (cmd == Renge::CMD_GET_DIAGNOSTICS) {
            int limit = request.value("limit", 25);
            if (limit < 0) limit = 0;

            json data;
            const uint64_t uptimeMs = Sense::UptimeMs();
            const uint64_t busyUs   = Sense::TotalBusyUs();
            const double   busyMs   = double(busyUs) / 1000.0;
            data["uptime_ms"]        = uptimeMs;
            data["total_dispatches"] = Sense::TotalDispatches();
            // Fractional ms: the commands this exists to measure are mostly
            // sub-millisecond, so an integer would round the interesting ones away.
            data["total_busy_ms"]    = busyMs;
            // The headline number: what fraction of wall-clock was a dispatcher
            // occupied? A high value with a lagging UI is the evidence Phase 1
            // (non-blocking dispatch) would help; a low one says look elsewhere.
            data["busy_percent"] = (uptimeMs > 0)
                ? (busyMs * 100.0 / double(uptimeMs)) : 0.0;

            json cmds = json::array();
            for (const auto& s : Sense::TopCommands(static_cast<size_t>(limit))) {
                json e;
                e["cmd"]      = s.cmd;
                e["count"]    = s.count;
                e["total_ms"] = double(s.totalUs) / 1000.0;
                e["max_ms"]   = double(s.maxUs)   / 1000.0;
                e["last_ms"]  = double(s.lastUs)  / 1000.0;
                e["avg_ms"]   = (s.count > 0)
                    ? (double(s.totalUs) / double(s.count) / 1000.0) : 0.0;
                cmds.push_back(e);
            }
            data["commands"] = cmds;

            const Sense::ProcessStat ps = Sense::SampleProcess();
            json proc;
            proc["working_set_bytes"] = ps.workingSetBytes;
            proc["private_bytes"]     = ps.privateBytes;
            proc["peak_working_set"]  = ps.peakWorkingSet;
            proc["handle_count"]      = ps.handleCount;
            proc["thread_count"]      = ps.threadCount;
            proc["cpu_percent"]       = ps.cpuPercent;   // -1 = needs a 2nd sample
            data["process"] = proc;

            // Game-thread health from Stark — already public, and the other half
            // of "is the game starved?". A stalled game thread makes every
            // invoke-bearing command sit in the dispatcher.
            json gt;
            gt["hook_active"]           = Stark::IsHookActive();
            gt["hook_fire_count"]       = Stark::GetHookFireCount();
            // MsSinceLastHookFire returns UINT64_MAX for "never fired — liveness
            // unknown". Do NOT put that on the wire: it exceeds int64 and every
            // JSON reader with a signed integer type chokes on it (System.Text.Json
            // reports it identically to a fractional value, which sends you looking
            // in the wrong place). -1 is the same "unknown" in a range everyone can
            // parse.
            const uint64_t msSinceFire = Stark::MsSinceLastHookFire();
            gt["ms_since_last_fire"]    = (msSinceFire == UINT64_MAX)
                                            ? int64_t(-1) : int64_t(msSinceFire);
            gt["responsive"]            = Stark::IsGameThreadResponsive();
            gt["invoke_timeout_ms"]     = Stark::GetInvokeTimeoutMs();
            data["game_thread"] = gt;

            // Object-pool size over time is a cheap GC / leak signal. Aura, not
            // the UE5_* export layer — the pipe shouldn't reach through the C ABI
            // to reach something it already links directly.
            data["gobjects_count"] = Aura::GetCount();

            return Renge::MakeResponse(id, data).dump();
        }

        if (cmd == Renge::CMD_RESET_DIAGNOSTICS) {
            Sense::Reset();
            json data;
            data["ok"] = true;
            return Renge::MakeResponse(id, data).dump();
        }

        if (cmd == Renge::CMD_PE_PROFILE_GET) {
            int limit = request.value("limit", 200);

            std::vector<Linie::FuncStat> snap;
            Linie::Snapshot(snap);

            uint64_t totalCalls = 0;
            for (const auto& s : snap) totalCalls += s.count;

            // Sort by fire count desc; resolve only the capped set (name resolution
            // is the cost, so we pay it after the sort + cap, not per stored entry).
            // first_seq (call-stream order) rides along so the UI can re-sort by it.
            std::sort(snap.begin(), snap.end(),
                      [](const Linie::FuncStat& a, const Linie::FuncStat& b) { return a.count > b.count; });

            // Transient-UI discriminator. A widget class's own methods all fire for
            // the FIRST time when the widget is created (e.g. opening a shop), so they
            // flood a baseline-diff as "new" — but the widget is the RESULT of the
            // action, not its entry point. is_widget lets the UI hide them to surface
            // the persistent opener (on a controller / subsystem / component).
            static const std::unordered_set<std::string> kWidgetBases{ "UserWidget", "Widget" };

            json functions = json::array();
            int emitted = 0;
            // Cadence diagnostic (Phase E): count + list the periodic-looking candidates
            // (same band the UI's PeProfileEntry.IsPeriodic uses) so an idle-window
            // recording is verifiable from the log, not just the UI.
            int periodicCount = 0, periodicLogged = 0;
            std::string periodicSummary;
            for (size_t i = 0; i < snap.size() && emitted < limit; ++i) {
                if ((i & 0xFFF) == 0 && Tot::Requested()) break;  // cooperative abort
                FunctionInfo fi{};
                if (!Ubel::ResolveFunctionInfo(snap[i].func, fi)) continue;  // drop stale/recycled
                uintptr_t classAddr = Ubel::GetOuter(snap[i].func);  // UFunction's Outer == its UClass
                std::string cls = Ubel::GetName(classAddr);
                json item;
                item["class_name"] = cls;
                item["func_name"]  = fi.name;
                item["func_addr"]  = Renge::AddrToStr(snap[i].func);
                item["num_parms"]  = fi.numParms;
                item["parms_size"] = fi.parmsSize;
                item["count"]      = snap[i].count;
                item["first_seq"]  = snap[i].firstSeq;        // call-stream position of first fire
                item["function_flags"] = fi.functionFlags;   // let the UI tag Event/Delegate/Callable
                item["is_widget"]  = Aura::ClassDerivesFromAny(classAddr, kWidgetBases);
                item["mean_period_ms"] = snap[i].meanPeriodMs;   // cadence (Phase E): inter-arrival mean
                item["cv"]             = snap[i].cv;             //   + coefficient of variation (regularity)
                item["gap_samples"]    = snap[i].gapSamples;     //   + how many gaps measured
                functions.push_back(item);
                ++emitted;
                // Periodic candidate: enough gaps, regular (low cv), out of the per-frame
                // (Tick) band, within a plausible gameplay-timer window.
                if (snap[i].gapSamples >= 3 && snap[i].cv <= 0.25 &&
                    snap[i].meanPeriodMs > 40.0 && snap[i].meanPeriodMs <= 30000.0) {
                    ++periodicCount;
                    if (periodicLogged < 12) {
                        char buf[192];
                        snprintf(buf, sizeof(buf), "%s%s::%s ~%.0fms cv=%.2f x%llu",
                                 periodicLogged ? ", " : "", cls.c_str(), fi.name.c_str(),
                                 snap[i].meanPeriodMs, snap[i].cv,
                                 (unsigned long long)snap[i].gapSamples);
                        periodicSummary += buf;
                        ++periodicLogged;
                    }
                }
            }

            Sein::Info("PIPE:profile",
                       "pe_profile_get: %d distinct funcs, %llu total calls, %d emitted (limit %d); "
                       "%d periodic-looking [%s]",
                       static_cast<int>(snap.size()), (unsigned long long)totalCalls, emitted, limit,
                       periodicCount, periodicSummary.c_str());

            json data;
            data["recording"]      = Linie::IsActive();
            data["distinct_funcs"] = static_cast<int>(snap.size());
            data["total_calls"]    = totalCalls;
            data["functions"]      = functions;
            return Renge::MakeResponse(id, data).dump();
        }

        // === find_by_address: Reverse lookup — address to UObject instance ===
        // Always runs the standard FindByAddress (UObject containment + backward
        // memory scan). Additionally runs container-aware FindInContainers when:
        //   - request includes "scan_containers": true (UI opt-in), OR
        //   - the standard search returned no exact/containment match
        // Container matches let the UI surface "this address is element [N] of
        // ObjA.SomeArray" so the user can jump straight into that element.
        if (cmd == Renge::CMD_FIND_BY_ADDRESS) {
            std::string addrStr = request.value("addr", "");
            if (addrStr.empty()) return Renge::MakeError(id, "Missing addr").dump();

            uintptr_t queryAddr = Renge::StrToAddr(addrStr);
            auto lookupResult = Aura::FindByAddress(queryAddr);
            bool requestedContainerScan = request.value("scan_containers", false);

            json data;
            data["found"] = lookupResult.found;

            if (lookupResult.found) {
                // match_type: legacy "exact" / "contains" string for back-compat.
                // match_kind: more precise — "exact" / "contains" / "backward" / "nearest".
                // Clients should prefer match_kind to distinguish low-confidence
                // "nearest" fallbacks (addr beyond PropertiesSize) from real containment.
                data["match_type"]       = lookupResult.exactMatch ? "exact" : "contains";
                data["match_kind"]       = lookupResult.matchKind.empty()
                                              ? (lookupResult.exactMatch ? "exact" : "contains")
                                              : lookupResult.matchKind;
                data["addr"]             = Renge::AddrToStr(lookupResult.objectAddr);
                data["index"]            = lookupResult.index;
                data["name"]             = lookupResult.name;
                data["class"]            = lookupResult.className;
                data["outer"]            = Renge::AddrToStr(lookupResult.outer);
                data["offset_from_base"] = lookupResult.offsetFromBase;
                data["query_addr"]       = addrStr;
            }

            // Container scan: opt-in, or fallback when nothing else hit.
            // Heap-allocated TArray data buffers don't fall inside any
            // UObject's PropertiesSize, so this is the only way to attribute
            // those addresses to an owner.
            if (requestedContainerScan || !lookupResult.found) {
                Aura::ContainerScanStats stats;
                auto containerMatches = Aura::FindInContainers(queryAddr, 16, &stats);

                // Deep fallback: the shallow scan only finds values stored
                // INLINE in a container buffer. A value in a SEPARATELY-
                // allocated nested container (TArray inside a struct element of
                // a TMap value inside a struct element of a TArray, …) needs a
                // recursive descent. Only run it when the shallow scan found
                // nothing AND the caller opted in via container_depth > 1, so
                // the common (fast) case is never slowed. (build 1194)
                int containerDepth = request.value("container_depth", 1);
                int containerElemCap = request.value("container_elem_cap", 256);
                bool deepRan = false;
                if (containerMatches.empty() && containerDepth > 1) {
                    Aura::ContainerScanStats deepStats;
                    containerMatches = Aura::FindInContainersDeep(queryAddr, 8, containerDepth,
                                                                  containerElemCap, &deepStats);
                    if (containerMatches.empty()) stats = deepStats;   // surface deep deadline/stats
                    deepRan = true;
                }

                // Surface scan stats so the UI can distinguish "really not in
                // any container" from "scan got cut off by the deadline".
                json scanInfo;
                scanInfo["objects_scanned"] = stats.objectsScanned;
                scanInfo["objects_total"]   = stats.objectsTotal;
                scanInfo["classes_primed"]  = stats.classesPrimed;
                scanInfo["duration_ms"]     = stats.durationMs;
                scanInfo["deadline_hit"]    = stats.deadlineHit;
                scanInfo["deep_scan"]       = deepRan;
                data["container_scan"]      = scanInfo;

                json arr = json::array();
                for (const auto& m : containerMatches) {
                    json mj;
                    mj["owner_addr"]    = Renge::AddrToStr(m.ownerObj);
                    mj["owner_index"]   = m.ownerIndex;
                    mj["owner_name"]    = m.ownerName;
                    mj["owner_class"]   = m.ownerClassName;
                    mj["field_offset"]  = m.fieldOffset;
                    mj["field_name"]    = m.fieldName;
                    mj["field_type"]    = m.fieldType;
                    mj["inner_type"]    = m.innerType;
                    mj["element_index"] = m.elementIndex;
                    mj["element_size"]  = m.elementSize;
                    mj["intra_offset"]  = m.intraOffset;
                    mj["data_addr"]     = Renge::AddrToStr(m.dataAddr);
                    mj["count"]         = m.count;
                    if (!m.note.empty())
                        mj["note"]      = m.note;
                    // Deeply-nested value: emit the chain of additional hops so
                    // the UI can show the full path + drill all levels.
                    if (!m.nestedChain.empty()) {
                        json chain = json::array();
                        for (const auto& h : m.nestedChain) {
                            json hj;
                            hj["field_offset"]   = h.fieldOffset;
                            hj["field_name"]     = h.fieldName;
                            hj["field_type"]     = h.fieldType;
                            hj["inner_type"]     = h.innerType;
                            hj["element_index"]  = h.elementIndex;
                            hj["element_size"]   = h.elementSize;
                            hj["intra_offset"]   = h.intraOffset;
                            hj["data_addr"]      = Renge::AddrToStr(h.dataAddr);
                            hj["map_value_side"] = h.mapValueSide;
                            if (!h.note.empty()) hj["note"] = h.note;
                            chain.push_back(hj);
                        }
                        mj["nested_chain"] = chain;
                    }
                    arr.push_back(mj);
                }
                data["container_matches"] = arr;
            }

            return Renge::MakeResponse(id, data).dump();
        }

        // === find_refs_to_uobject: reverse pointer search ===
        // Given a UObject's address, find every other UObject that holds a
        // pointer to it (direct ObjectProperty/ClassProperty fields,
        // TArray<UObject*> elements, including nested in StructProperty).
        // Resolves the "logical owner" question that UE's OuterPrivate
        // doesn't answer for runtime-spawned objects.
        if (cmd == Renge::CMD_FIND_REFS_TO_UOBJ) {
            std::string addrStr = request.value("addr", "");
            if (addrStr.empty()) return Renge::MakeError(id, "Missing addr").dump();
            uintptr_t target = Renge::StrToAddr(addrStr);
            int32_t maxResults = request.value("max_results", 32);

            Aura::ContainerScanStats stats;
            auto refs = Aura::FindReferencesToUObject(target, maxResults, &stats);

            json data;
            data["query_addr"] = addrStr;

            json scanInfo;
            scanInfo["objects_scanned"] = stats.objectsScanned;
            scanInfo["objects_total"]   = stats.objectsTotal;
            scanInfo["classes_primed"]  = stats.classesPrimed;
            scanInfo["duration_ms"]     = stats.durationMs;
            scanInfo["deadline_hit"]    = stats.deadlineHit;
            data["scan"] = scanInfo;

            json arr = json::array();
            for (const auto& r : refs) {
                json rj;
                rj["owner_addr"]    = Renge::AddrToStr(r.ownerObj);
                rj["owner_index"]   = r.ownerIndex;
                rj["owner_name"]    = r.ownerName;
                rj["owner_class"]   = r.ownerClassName;
                rj["field_offset"]  = r.fieldOffset;
                rj["field_name"]    = r.fieldName;
                rj["field_type"]    = r.fieldType;
                if (!r.innerType.empty())
                    rj["inner_type"] = r.innerType;
                rj["element_index"] = r.elementIndex;
                arr.push_back(rj);
            }
            data["references"] = arr;
            return Renge::MakeResponse(id, data).dump();
        }

        // === get_related_objects: forward owned-object graph for one UObject ===
        // "Related Objects" panel: given any UObject (typically an actor), list
        // itself, its class/outer, its Controller<->Pawn counterpart, and the
        // sub-objects it OWNS (components, and for GAS games the ASC -> its
        // UAttributeSet objects). The fast forward view; the reverse "who points
        // at this" view stays find_refs_to_uobject.
        if (cmd == Renge::CMD_GET_RELATED_OBJECTS) {
            std::string addrStr = request.value("addr", "");
            if (addrStr.empty()) return Renge::MakeError(id, "Missing addr").dump();
            // Strict parse (mirror find_path_from_gworld): an unsubstituted CE
            // placeholder / signed / trailing-garbage addr must be an explicit
            // error, not an ambiguous ok:true empty list.
            uintptr_t target = 0;
            if (!Renge::TryStrToAddr(addrStr, target) || !target)
                return Renge::MakeError(id, "Invalid addr").dump();
            int32_t maxResults = request.value("max_results", 128);

            auto rels = Aura::GetRelatedObjects(target, maxResults);

            json arr = json::array();
            for (const auto& r : rels) {
                json rj;
                rj["addr"]         = Renge::AddrToStr(r.addr);
                rj["index"]        = r.index;
                rj["name"]         = r.name;
                rj["class"]        = r.className;
                rj["relation"]     = r.relation;
                rj["field_name"]   = r.fieldName;
                rj["field_offset"] = r.fieldOffset;
                rj["depth"]        = r.depth;
                rj["parent_addr"]  = Renge::AddrToStr(r.parentAddr);
                arr.push_back(rj);
            }
            json data;
            data["query_addr"] = addrStr;
            data["related"]    = arr;
            return Renge::MakeResponse(id, data).dump();
        }

        // === get_current_target: auto-detect the actor the player is targeting ===
        // "Related Objects" Phase 2 (Edel): resolve GWorld -> PlayerController ->
        // Pawn, score the player's outgoing object-pointer fields, and return a
        // ranked candidate list (best-first) the UI feeds into the Related panel.
        if (cmd == Renge::CMD_GET_CURRENT_TARGET) {
            int32_t maxCandidates = request.value("max_candidates", 8);
            Edel::CurrentTargetResult tr = Edel::DetectCurrentTarget(maxCandidates);

            json arr = json::array();
            for (const auto& c : tr.candidates) {
                json cj;
                cj["addr"]         = Renge::AddrToStr(c.addr);
                cj["index"]        = c.index;
                cj["name"]         = c.name;
                cj["class"]        = c.className;
                cj["score"]        = c.score;
                cj["source_addr"]  = Renge::AddrToStr(c.sourceObject);
                cj["source_class"] = c.sourceClass;
                cj["field_name"]   = c.fieldName;
                cj["field_offset"] = c.fieldOffset;
                cj["reason"]       = c.reason;
                arr.push_back(cj);
            }
            json data;
            data["resolved"]          = tr.resolved;
            data["world"]             = Renge::AddrToStr(tr.world);
            data["player_controller"] = Renge::AddrToStr(tr.playerController);
            data["player_pawn"]       = Renge::AddrToStr(tr.playerPawn);
            data["note"]              = tr.note;
            data["candidates"]        = arr;
            return Renge::MakeResponse(id, data).dump();
        }

        // === resolve_game_engine: locate the live UEngine object ===
        // For the Live Walker "Start from GameEngine" root. Returns the live
        // engine address + class name + whether its standard pointer members
        // (GameViewport / GameInstance) are present and non-null. The UI then
        // walks the address like any instance (no GWorld / AOB involved).
        if (cmd == Renge::CMD_RESOLVE_GAME_ENGINE) {
            Genau::GameEngineInfo info = Genau::FindGameEngine();
            json data;
            data["found"] = (info.engineAddr != 0);
            if (info.engineAddr) {
                data["addr"]             = Renge::AddrToStr(info.engineAddr);
                data["class"]            = info.className;
                data["game_viewport_ok"] = info.gameViewportOk;
                data["game_instance_ok"] = info.gameInstanceOk;
            }
            return Renge::MakeResponse(id, data).dump();
        }

        // === find_path_from_gworld: forward BFS path GWorld -> ... -> target ===
        // "Locate in GWorld": given a target (a UObject, or a property value
        // address), compute the SHORTEST pointer chain from the live UWorld down
        // to it, so the UI can replace the Live Walker breadcrumb spine and land
        // on the target. The inverse of find_refs_to_uobject.
        if (cmd == Renge::CMD_FIND_PATH_FROM_GWORLD) {
            extern uintptr_t g_cachedGWorld;

            std::string targetStr = request.value("target", "");
            if (targetStr.empty()) return Renge::MakeError(id, "Missing target").dump();
            uintptr_t targetAddr = 0;
            if (!Renge::TryStrToAddr(targetStr, targetAddr) || !targetAddr)
                return Renge::MakeError(id, "Invalid target address").dump();

            int32_t maxDepth = request.value("max_depth", 5);

            // --- Resolve the BFS root. Default is the live UWorld (GWorld);
            //     root_kind="engine" roots the SAME forward BFS at the live
            //     UGameEngine instead ("Locate in GameEngine"), so the UI can
            //     reach engine-layer objects (GameInstance / LocalPlayer / engine
            //     subsystems) that the GWorld graph never touches. NOTE: an engine
            //     root is NOT a superset of GWorld — it sits one hop ABOVE the
            //     world, so streaming / World-Partition actors (which the GWorld
            //     path recovers via RecoverViaWorldLevel, itself gated on a World
            //     root) are typically not_reachable from the engine. ---
            const std::string rootKind = request.value("root_kind", "gworld");
            uintptr_t rootObj = 0;
            if (rootKind == "engine") {
                rootObj = Genau::FindGameEngine().engineAddr;
                if (!rootObj) {
                    json data;
                    data["found"]     = false;
                    data["status"]    = "no_engine";
                    data["root_kind"] = rootKind;
                    return Renge::MakeResponse(id, data).dump();
                }
            } else {
                // Mirror walk_world: deref &GWorld, then fall back to a GObjects
                // UWorld instance scan.
                if (g_cachedGWorld)
                    Macht::ReadSafe(g_cachedGWorld, rootObj);
                if (!rootObj) {
                    Aura::ForEach([&](int32_t, uintptr_t obj) -> bool {
                        uintptr_t cls = Ubel::GetClass(obj);
                        if (!cls) return true;
                        if (Ubel::GetName(cls) == "World") {
                            if (Ubel::GetName(obj).rfind("Default__", 0) == 0) return true; // skip CDO
                            rootObj = obj;
                            return false;
                        }
                        return true;
                    });
                }
                if (!rootObj) {
                    json data;
                    data["found"]     = false;
                    data["status"]    = "no_gworld";
                    data["root_kind"] = rootKind;
                    return Renge::MakeResponse(id, data).dump();
                }
            }

            // --- Resolve the target UObject + intra-object offset of the value. ---
            uintptr_t targetObj   = 0;
            int32_t   intraOffset = 0;
            std::string objStr = request.value("object_addr", "");
            if (!objStr.empty()) {
                // Caller already knows the owning UObject (Value Search / Instance
                // Finder) — trust it, skip the expensive FindByAddress scan.
                targetObj = Renge::StrToAddr(objStr);
                if (targetObj && targetAddr >= targetObj && (targetAddr - targetObj) < 0x10000000)
                    intraOffset = static_cast<int32_t>(targetAddr - targetObj);
            }
            if (!targetObj) {
                auto la = Aura::FindByAddress(targetAddr);
                if (la.found) {
                    targetObj   = la.objectAddr;
                    intraOffset = la.offsetFromBase;
                } else {
                    // Maybe the address is inside a heap container buffer.
                    Aura::ContainerScanStats cstats;
                    auto cms = Aura::FindInContainers(targetAddr, 1, &cstats);
                    if (!cms.empty()) {
                        targetObj   = cms[0].ownerObj;
                        intraOffset = 0;  // value is in a heap buffer, not inside the object
                    } else {
                        // Deep fallback (mirrors find_by_address): a value in a
                        // SEPARATELY-allocated nested container (a TArray inside a
                        // struct element of a TMap value inside a struct element of
                        // a TArray, …) isn't found by the shallow scan. Only when the
                        // caller opted in via container_depth > 1, so the common
                        // (fast) path is never slowed. Attributes the value to its
                        // owning UObject so the path search has a reachable target.
                        int containerDepth   = request.value("container_depth", 1);
                        int containerElemCap = request.value("container_elem_cap", 256);
                        if (containerDepth > 1) {
                            Aura::ContainerScanStats dstats;
                            auto dms = Aura::FindInContainersDeep(targetAddr, 1, containerDepth,
                                                                  containerElemCap, &dstats);
                            if (!dms.empty()) {
                                targetObj   = dms[0].ownerObj;
                                intraOffset = 0;
                            }
                        }
                    }
                }
            }
            if (!targetObj) {
                json data;
                data["found"]  = false;
                data["status"] = "invalid_target";
                return Renge::MakeResponse(id, data).dump();
            }

            // deep (opt-in): also follow object pointers inside one struct-element
            // container level (TArray<FStruct> etc.) — reaches objects referenced
            // only from a struct-array element. Heavier; default off.
            bool deep = request.value("deep", false);
            auto path = Aura::FindObjectGraphPath(rootObj, targetObj, maxDepth, 0, deep);

            // Surface the result in the pipe log next to the request (the full
            // trace also lands in the OARR/offsets log). not_reachable with a large
            // `visited` = the object simply isn't referenced from the GWorld graph
            // (e.g. a just-spawned / streaming actor), NOT a depth/timeout issue.
            Sein::Info("PIPE:path",
                "find_path_from_gworld: root_kind=%s root=0x%llX target=0x%llX status=%s found=%d hops=%d visited=%d %lldms (maxDepth=%d)",
                rootKind.c_str(), static_cast<unsigned long long>(rootObj),
                static_cast<unsigned long long>(targetObj), path.status.c_str(),
                path.found ? 1 : 0, path.depthReached, path.visited,
                static_cast<long long>(path.durationMs), maxDepth);

            json data;
            data["found"]               = path.found;
            data["status"]              = path.status;
            data["root_kind"]           = rootKind;
            data["root_addr"]           = Renge::AddrToStr(rootObj);
            data["root_name"]           = Ubel::GetName(rootObj);
            data["target_obj"]          = Renge::AddrToStr(targetObj);
            data["target_name"]         = Ubel::GetName(targetObj);
            {
                uintptr_t tcls = Ubel::GetClass(targetObj);
                data["target_class"] = tcls ? Ubel::GetName(tcls) : "";
            }
            data["target_intra_offset"] = intraOffset;
            data["max_depth"]           = maxDepth;
            data["deep"]                = deep;
            data["depth"]               = path.depthReached;
            data["visited"]             = path.visited;
            data["duration_ms"]         = path.durationMs;

            json steps = json::array();
            for (const auto& s : path.steps) {
                json sj;
                sj["from"]          = Renge::AddrToStr(s.fromObj);
                sj["to"]            = Renge::AddrToStr(s.toObj);
                sj["field_offset"]  = s.fieldOffset;
                sj["field_name"]    = s.fieldName;
                sj["field_type"]    = s.fieldType;
                if (!s.innerType.empty()) sj["inner_type"] = s.innerType;
                sj["element_index"] = s.elementIndex;
                if (s.elemStride > 0)      sj["elem_stride"]       = s.elemStride;
                if (s.elemValueOffset > 0) sj["elem_value_offset"] = s.elemValueOffset;
                sj["to_name"]       = s.toName;
                sj["to_class"]      = s.toClassName;
                steps.push_back(sj);
            }
            data["steps"] = steps;
            return Renge::MakeResponse(id, data).dump();
        }

        // === find_property_xrefs: which UFunctions reference a given FProperty ===
        // Static Kismet-bytecode scan (Blueprint/script functions only; native
        // functions have empty Script and are invisible — UI must surface this).
        if (cmd == Renge::CMD_FIND_PROPERTY_XREFS) {
            std::string addrStr = request.value("prop_addr", "");
            if (addrStr.empty()) return Renge::MakeError(id, "Missing prop_addr").dump();
            uintptr_t propAddr = Renge::StrToAddr(addrStr);
            bool    gameOnly   = request.value("game_only", true);
            int32_t maxResults = request.value("max_results", 200);

            auto res = Aura::FindPropertyXrefs(propAddr, gameOnly, maxResults);

            json data;
            data["query_addr"] = addrStr;

            json scanInfo;
            scanInfo["functions_scanned"]     = res.stats.functionsScanned;
            scanInfo["functions_with_script"] = res.stats.functionsWithScript;
            scanInfo["objects_total"]         = res.stats.objectsTotal;
            scanInfo["duration_ms"]           = res.stats.durationMs;
            scanInfo["deadline_hit"]          = res.stats.deadlineHit;
            data["scan"] = scanInfo;

            json arr = json::array();
            for (const auto& x : res.xrefs) {
                json xj;
                xj["func_addr"]        = Renge::AddrToStr(x.funcAddr);
                xj["func_name"]        = x.funcName;
                xj["func_full"]        = x.funcFullName;
                xj["owner_class"]      = x.ownerClassName;
                xj["owner_class_addr"] = Renge::AddrToStr(x.ownerClassAddr);
                xj["occurrences"]      = x.occurrences;
                xj["write_count"]      = x.writeCount;
                xj["kind"]             = x.kind;
                if (!x.eventName.empty())
                    xj["event"]        = x.eventName;
                arr.push_back(xj);
            }
            data["xrefs"] = arr;
            return Renge::MakeResponse(id, data).dump();
        }

        // === find_functions_by_class: which UFunctions take a class as a param ===
        // Reflection scan over each UFunction's param chain — catches NATIVE
        // functions too (params are reflected even when Script is empty), unlike
        // find_property_xrefs. Same response shape so the UI reuses one parser.
        if (cmd == Renge::CMD_FIND_FUNCTIONS_BY_CLASS) {
            std::string addrStr = request.value("class_addr", "");
            if (addrStr.empty()) return Renge::MakeError(id, "Missing class_addr").dump();
            uintptr_t classAddr = Renge::StrToAddr(addrStr);
            bool    gameOnly   = request.value("game_only", true);
            int32_t maxResults = request.value("max_results", 200);

            auto res = Aura::FindFunctionsByClassParam(classAddr, gameOnly, maxResults);

            json data;
            data["query_addr"] = addrStr;

            json scanInfo;
            scanInfo["functions_scanned"]     = res.stats.functionsScanned;
            scanInfo["functions_with_script"] = res.stats.functionsWithScript;  // reused: matched
            scanInfo["objects_total"]         = res.stats.objectsTotal;
            scanInfo["duration_ms"]           = res.stats.durationMs;
            scanInfo["deadline_hit"]          = res.stats.deadlineHit;
            data["scan"] = scanInfo;

            json arr = json::array();
            for (const auto& x : res.xrefs) {
                json xj;
                xj["func_addr"]        = Renge::AddrToStr(x.funcAddr);
                xj["func_name"]        = x.funcName;
                xj["func_full"]        = x.funcFullName;
                xj["owner_class"]      = x.ownerClassName;
                xj["owner_class_addr"] = Renge::AddrToStr(x.ownerClassAddr);
                xj["occurrences"]      = x.occurrences;
                xj["write_count"]      = x.writeCount;
                xj["kind"]             = x.kind;   // "param" / "return"
                arr.push_back(xj);
            }
            data["xrefs"] = arr;
            return Renge::MakeResponse(id, data).dump();
        }

        // === get_function_code_addr: UFunction->Func (native code entry point) ===
        // For the xref dialog's "Disassemble in CE" — resolves the .text address
        // CE should jump its disassembler to (native funcs) / interpreter (BP).
        if (cmd == Renge::CMD_GET_FUNCTION_CODE_ADDR) {
            std::string addrStr = request.value("func_addr", "");
            if (addrStr.empty()) return Renge::MakeError(id, "Missing func_addr").dump();
            uintptr_t funcAddr = Renge::StrToAddr(addrStr);
            uintptr_t code = Aura::GetFunctionCodeAddr(funcAddr);
            json data;
            data["func_addr"] = addrStr;
            data["code_addr"] = code ? Renge::AddrToStr(code) : "";
            return Renge::MakeResponse(id, data).dump();
        }

        // === walk_function_props: reverse edge — properties a UFunction reads/writes ===
        if (cmd == Renge::CMD_WALK_FUNCTION_PROPS) {
            std::string addrStr = request.value("func_addr", "");
            if (addrStr.empty()) return Renge::MakeError(id, "Missing func_addr").dump();
            uintptr_t funcAddr = Renge::StrToAddr(addrStr);

            auto res = Aura::WalkFunctionPropertyRefs(funcAddr);

            json data;
            data["query_addr"]   = addrStr;
            data["script_bytes"] = res.scriptBytes;
            // Path 2: "bytecode" (exact) / "disasm" (native x64, heuristic) / "none".
            data["method"]       = res.method;
            data["unmapped"]     = res.unmappedAccesses;
            json arr = json::array();
            for (const auto& r : res.refs) {
                json rj;
                rj["prop_addr"]   = Renge::AddrToStr(r.propAddr);
                rj["name"]        = r.name;
                rj["type"]        = r.type;
                rj["occurrences"] = r.occurrences;
                rj["write_count"] = r.writeCount;
                rj["scope"]       = r.scope;
                rj["offset"]      = r.offset;       // class-member offset (disasm; -1 = n/a)
                rj["confidence"]  = r.confidence;   // "high"/"low" (disasm); "" (bytecode)
                arr.push_back(rj);
            }
            data["props"] = arr;
            return Renge::MakeResponse(id, data).dump();
        }

        // === get_ce_pointer_info: CE pointer chain info for a GObjects instance ===
        if (cmd == Renge::CMD_GET_CE_PTR_INFO) {
            std::string addrStr = request.value("addr", "");
            if (addrStr.empty()) return Renge::MakeError(id, "Missing addr").dump();

            uintptr_t addr = Renge::StrToAddr(addrStr);
            int fieldOffset = request.value("field_offset", 0);

            extern uintptr_t g_cachedGObjects;
            uintptr_t moduleBase = Macht::GetModuleBase(nullptr);

            // Compute GObjects RVA
            uintptr_t gobjectsRVA = g_cachedGObjects - moduleBase;

            // Find the InternalIndex of this object by scanning
            int32_t internalIndex = Ubel::GetIndex(addr);
            if (internalIndex < 0) {
                return Renge::MakeError(id, "Cannot read InternalIndex").dump();
            }

            int32_t chunkIndex  = internalIndex / Grimoire::OBJECTS_PER_CHUNK;
            int32_t withinChunk = internalIndex % Grimoire::OBJECTS_PER_CHUNK;

            // Get module name
            wchar_t moduleNameW[MAX_PATH] = {};
            GetModuleFileNameW(reinterpret_cast<HMODULE>(moduleBase), moduleNameW, MAX_PATH);
            // Extract just the filename
            std::wstring modulePath(moduleNameW);
            auto lastSlash = modulePath.find_last_of(L"\\/");
            std::wstring moduleFileName = (lastSlash != std::wstring::npos)
                ? modulePath.substr(lastSlash + 1) : modulePath;
            // Remove .exe extension for CE format
            auto dotPos = moduleFileName.find_last_of(L'.');
            std::wstring moduleNameNoExt = (dotPos != std::wstring::npos)
                ? moduleFileName.substr(0, dotPos) : moduleFileName;

            // Convert to narrow string
            std::string moduleName;
            for (wchar_t wc : moduleNameNoExt) {
                moduleName += (wc < 128) ? static_cast<char>(wc) : '?';
            }

            json data;
            data["module"]         = moduleName;
            data["module_base"]    = Renge::AddrToStr(moduleBase);
            data["gobjects_rva"]   = Renge::AddrToStr(gobjectsRVA);
            data["internal_index"] = internalIndex;
            data["chunk_index"]    = chunkIndex;
            data["within_chunk"]   = withinChunk;
            data["field_offset"]   = fieldOffset;

            if (Aura::IsPacked()) {
                // UE5.7+ PACKED FUObjectItem: the UObject* is bit-packed across two item
                // fields and reconstructed via shift/mask — a native CE pointer chain
                // CANNOT express that. Degrade to the ABSOLUTE object address (valid for
                // this session only; won't survive a restart / ASLR rebase). *** UNVERIFIED ***
                data["packed_layout"] = true;
                data["warning"] =
                    "UE5.7+ packed FUObjectItem layout (UNVERIFIED): the GObjects-relative CE "
                    "pointer chain cannot reconstruct the bit-packed object pointer. Falling back "
                    "to the ABSOLUTE object address — it will NOT survive a game restart or ASLR "
                    "rebase, so re-resolve after each launch.";
                json offsets = json::array();
                offsets.push_back(fieldOffset);     // single hop: absolute object addr + field
                data["ce_offsets"] = offsets;
                data["ce_base"]    = Renge::AddrToStr(addr);  // absolute object address
                return Renge::MakeResponse(id, data).dump();
            }

            // CE offset chain (bottom-to-top), DIRECT layouts (classic / UE5.7+ unpacked):
            // Level 4 (outermost): deref FUObjectArray* → chunkTable (offset 0)
            // Level 3: chunkTable + chunkIndex*8 → chunk
            // Level 2: chunk + withinChunk*itemSize + objOffset → FUObjectItem.Object*
            // Level 1 (innermost): Object + fieldOffset → value
            // NOTE: the item hop adds GetItemObjOffset() so the chain dereferences the
            // Object pointer at its real within-item offset — +0x00 on classic, +0x08 on
            // UE5.7+ unpacked (where FlagsAndRefCount sits at item+0x00).
            data["packed_layout"] = false;
            json offsets = json::array();
            offsets.push_back(fieldOffset);                                              // field offset from UObject*
            offsets.push_back(withinChunk * Aura::GetItemSize() + Aura::GetItemObjOffset()); // item in chunk → Object*
            offsets.push_back(chunkIndex * 8);                                           // chunk in table
            offsets.push_back(0);                                                        // deref FUObjectArray.Objects

            data["ce_offsets"] = offsets;

            // CE base address string: "Module.exe+RVA"
            char ceBase[128];
            snprintf(ceBase, sizeof(ceBase), "\"%s.exe\"+%llX",
                     moduleName.c_str(), static_cast<unsigned long long>(gobjectsRVA));
            data["ce_base"] = ceBase;

            return Renge::MakeResponse(id, data).dump();
        }

        // === get_offsets: Return all detected FField/FProperty/UStruct offsets ===
        // === set_packed_consts: runtime calibration / force-enable for the UE5.7+
        //     *** UNVERIFIED *** packed FUObjectItem reconstruction (no rebuild needed).
        //     Tweak align_bits / ptr_mask_bits (and optional serial_off) until the echoed
        //     reconstructed samples resolve real UObject names; force:true dry-runs packed
        //     mode against a game even when normal detection chose a direct layout. ===
        if (cmd == Renge::CMD_SET_PACKED_CONSTS) {
            int  alignBits = request.value("align_bits", 0);   // <=0 => leave unchanged
            int  serialOff = request.value("serial_off", -1);  // <0  => leave unchanged
            bool force     = request.value("force", false);

            // ptr_mask_bits may arrive as a hex string ("0x3FFF") or a number; 0 => unchanged.
            uint64_t ptrMask = 0;
            if (request.contains("ptr_mask_bits")) {
                const auto& v = request["ptr_mask_bits"];
                if (v.is_string())              ptrMask = Renge::StrToAddr(v.get<std::string>());
                else if (v.is_number_unsigned()) ptrMask = v.get<uint64_t>();
                else if (v.is_number_integer())  ptrMask = static_cast<uint64_t>(v.get<int64_t>());
            }

            Aura::SetPackedConsts(alignBits, ptrMask, force, serialOff);

            json data;
            data["item_packed"]      = Aura::IsPacked();
            data["item_obj_offset"]  = Aura::GetItemObjOffset();
            data["item_size"]        = Aura::GetItemSize();
            data["item_layout_mode"] = Aura::IsPacked() ? "packed57"
                                       : (Aura::GetItemObjOffset() != 0 ? "unpacked57" : "classic");
            // Echo reconstructed samples so the operator can eyeball-calibrate live.
            json samples = json::array();
            int n = Aura::GetCount();
            for (int i = 0; i < 8 && i < n; ++i) {
                uintptr_t obj = Aura::GetByIndex(i);
                json s;
                s["index"] = i;
                s["addr"]  = Renge::AddrToStr(obj);
                s["name"]  = obj ? Ubel::GetName(obj) : "";
                samples.push_back(s);
            }
            data["samples"] = samples;
            return Renge::MakeResponse(id, data).dump();
        }

        if (cmd == Renge::CMD_GET_OFFSETS) {
            json data;
            data["build_info"]         = BuildStamp::VersionString();
            data["validated"]          = DynOff::bOffsetsValidated.load(std::memory_order_acquire);
            data["probe_ran"]          = DynOff::bOffsetsProbeRan.load(std::memory_order_acquire);
            data["fallback_reason"]    = DynOff::g_offsetsFallbackReason;
            data["ffieldclass_name"]   = DynOff::FFIELDCLASS_NAME;
            data["case_preserving"]    = DynOff::bCasePreservingName;
            data["use_fproperty"]      = DynOff::bUseFProperty;
            data["uobject_outer"]      = DynOff::UOBJECT_OUTER;
            data["ustruct_super"]      = DynOff::USTRUCT_SUPER;
            data["ustruct_children"]   = DynOff::USTRUCT_CHILDREN;
            data["ustruct_childprops"] = DynOff::USTRUCT_CHILDPROPS;
            data["ustruct_propssize"]  = DynOff::USTRUCT_PROPSSIZE;
            data["ustruct_script"]     = DynOff::USTRUCT_SCRIPT;
            // FUObjectItem layout self-description (mirrors get_pointers; see FillPointerSnapshot).
            data["item_packed"]        = Aura::IsPacked();
            data["item_obj_offset"]    = Aura::GetItemObjOffset();
            data["item_size"]          = Aura::GetItemSize();
            data["item_layout_mode"]   = Aura::IsPacked() ? "packed57"
                                         : (Aura::GetItemObjOffset() != 0 ? "unpacked57" : "classic");
            if (DynOff::bUseFProperty) {
                data["ffield_class"]       = DynOff::FFIELD_CLASS;
                data["ffield_next"]        = DynOff::FFIELD_NEXT;
                data["ffield_name"]        = DynOff::FFIELD_NAME;
                data["fproperty_elemsize"] = DynOff::FPROPERTY_ELEMSIZE;
                data["fproperty_flags"]    = DynOff::FPROPERTY_FLAGS;
                data["fproperty_offset"]   = DynOff::FPROPERTY_OFFSET;
            } else {
                data["ufield_next"]        = DynOff::UFIELD_NEXT;
                data["uproperty_elemsize"] = DynOff::UPROPERTY_ELEMSIZE;
                data["uproperty_flags"]    = DynOff::UPROPERTY_FLAGS;
                data["uproperty_offset"]   = DynOff::UPROPERTY_OFFSET;
            }
            return Renge::MakeResponse(id, data).dump();
        }

        if (cmd == Renge::CMD_WATCH) {
            std::string addrStr = request.value("addr", "");
            int size = request.value("size", 4);
            int interval = request.value("interval_ms", 500);
            if (addrStr.empty()) return Renge::MakeError(id, "Missing addr").dump();
            if (size <= 0 || size > 65536) return Renge::MakeError(id, "Invalid size (1-65536)").dump();
            if (interval < 50) interval = 50; // Minimum 50ms to prevent CPU spin

            uintptr_t addr = Renge::StrToAddr(addrStr);
            StartWatch(conn, addr, size, interval);
            return Renge::MakeResponse(id).dump();
        }

        if (cmd == Renge::CMD_UNWATCH) {
            std::string addrStr = request.value("addr", "");
            if (addrStr.empty()) return Renge::MakeError(id, "Missing addr").dump();

            uintptr_t addr = Renge::StrToAddr(addrStr);
            StopWatch(addr);
            return Renge::MakeResponse(id).dump();
        }

        // === Extra Scan: user-triggered aggressive pointer recovery ===

        if (cmd == Renge::CMD_RESCAN) {
            if (m_rescan.running.load()) {
                return Renge::MakeError(id, "Rescan already in progress").dump();
            }

            extern uintptr_t g_cachedGObjects;
            extern uintptr_t g_cachedGNames;
            extern uintptr_t g_cachedGWorld;
            extern bool      g_cachedVersionTooOld;

            // The engine was REFUSED, not scanned — Genau's too-old gate returned before any
            // pattern ran (see Genau::FindAll's gate). Extra Scan probes .data against the same
            // UE4/UE5 FUObjectArray presets and the same hardcoded OFF_UOBJECT_CLASS class
            // chain, so on a pre-4.11 / pre-UE4 binary it is a guaranteed no-op that would burn
            // 5-20 s and, worse, contradict the panel text telling the user it cannot help.
            // Refuse it here so the pipe is honest even if a client ignores the disabled button.
            if (g_cachedVersionTooOld) {
                return Renge::MakeError(id, "Unsupported engine — the scan was skipped by design; "
                                            "Extra Scan cannot find UE4/UE5 structures that do "
                                            "not exist in this binary").dump();
            }

            bool needGObj = (g_cachedGObjects == 0);
            bool needGWld = (g_cachedGWorld == 0) && (g_cachedGObjects != 0) && (g_cachedGNames != 0);

            if (!needGObj && !needGWld) {
                json data;
                data["scanning_gobjects"] = false;
                data["scanning_gworld"]   = false;
                data["message"] = "All scannable pointers already found";
                return Renge::MakeResponse(id, data).dump();
            }

            // Reset state
            m_rescan.foundGObjects = 0;
            m_rescan.foundGWorld   = 0;
            m_rescan.gobjectsMethod = "not_found";
            m_rescan.gworldMethod   = "not_found";
            m_rescan.phase.store(0);
            {
                std::lock_guard<std::mutex> lock(m_rescan.statusMutex);
                m_rescan.statusText = "Starting...";
            }
            m_rescan.running.store(true);

            if (m_rescan.scanThread.joinable()) m_rescan.scanThread.join();
            m_rescan.scanThread = std::thread(&Fern::RunRescan, this, needGObj, needGWld);

            json data;
            data["scanning_gobjects"] = needGObj;
            data["scanning_gworld"]   = needGWld;
            return Renge::MakeResponse(id, data).dump();
        }

        if (cmd == Renge::CMD_RESCAN_STATUS) {
            json data;
            data["running"] = m_rescan.running.load();
            data["phase"]   = m_rescan.phase.load();
            {
                std::lock_guard<std::mutex> lock(m_rescan.statusMutex);
                data["status_text"] = m_rescan.statusText;
            }
            // Include results if scan is complete
            if (!m_rescan.running.load() && m_rescan.phase.load() == 3) {
                data["found_gobjects"]   = (m_rescan.foundGObjects != 0);
                data["found_gworld"]     = (m_rescan.foundGWorld != 0);
                data["gobjects_addr"]    = Renge::AddrToStr(m_rescan.foundGObjects);
                data["gworld_addr"]      = Renge::AddrToStr(m_rescan.foundGWorld);
                data["gobjects_method"]  = m_rescan.gobjectsMethod;
                data["gworld_method"]    = m_rescan.gworldMethod;
            }
            return Renge::MakeResponse(id, data).dump();
        }

        if (cmd == Renge::CMD_APPLY_RESCAN) {
            if (m_rescan.running.load()) {
                return Renge::MakeError(id, "Rescan still running").dump();
            }

            extern uintptr_t   g_cachedGObjects;
            extern uintptr_t   g_cachedGNames;
            extern uintptr_t   g_cachedGWorld;
            extern uint32_t    g_cachedUEVersion;
            extern const char* g_cachedGObjectsMethod;
            extern const char* g_cachedGWorldMethod;
            extern bool        g_cachedVersionTooOld;

            // Belt-and-braces twin of the CMD_RESCAN refusal. This matters independently: apply
            // re-enters Aura::Init and Genau::ValidateAndFixOffsets(g_cachedUEVersion) OUTSIDE
            // FindAll, so the gate's early return does not fence a refused version out of the
            // version-dependent code — with the UE3 sentinel that argument would be 300 and
            // select layout branches no supported game can reach.
            if (g_cachedVersionTooOld) {
                return Renge::MakeError(id, "Unsupported engine — nothing to apply; the scan was "
                                            "skipped by design").dump();
            }

            bool applied = false;

            if (m_rescan.foundGObjects && g_cachedGObjects == 0) {
                g_cachedGObjects = m_rescan.foundGObjects;
                g_cachedGObjectsMethod = m_rescan.gobjectsMethod;
                Aura::Init(g_cachedGObjects);
                Sein::Info("PIPE:cmd", "apply_rescan: Applied GObjects=0x%llX (%s)",
                         (unsigned long long)g_cachedGObjects, g_cachedGObjectsMethod);
                applied = true;
            }

            if (m_rescan.foundGWorld && g_cachedGWorld == 0) {
                g_cachedGWorld = m_rescan.foundGWorld;
                g_cachedGWorldMethod = m_rescan.gworldMethod;
                Sein::Info("PIPE:cmd", "apply_rescan: Applied GWorld=0x%llX (%s)",
                         (unsigned long long)g_cachedGWorld, g_cachedGWorldMethod);
                applied = true;
            }

            // If we now have both GObjects+GNames, run full offset detection
            if (g_cachedGObjects && g_cachedGNames) {
                if (!Genau::ValidateAndFixOffsets(g_cachedUEVersion)) {
                    Sein::Warn("PIPE:cmd", "apply_rescan: ValidateAndFixOffsets returned false");
                }

                // Same second pass UE5_Init does: &GEngine can only be VALIDATED once the
                // offsets exist, so a recovery rescan that revives GObjects/GNames has to
                // retry it too — otherwise this path keeps reporting "AOB not found" for
                // GEngine forever even though the offsets it was waiting on just arrived.
                extern uintptr_t   g_cachedGEngine;
                extern const char* g_cachedGEngineMethod;
                extern const char* g_cachedGEnginePatternId;
                extern uintptr_t   g_cachedGEngineScanAddr;
                extern const char* g_cachedGEngineAob;
                extern int         g_cachedGEngineAobPos;
                extern int         g_cachedGEngineAobLen;
                if (g_cachedGEngine == 0) {
                    Genau::EnginePointers eng;   // scratch — carries the AOB metadata triple
                    if (Genau::ResolveGEngineDeferred(eng)) {
                        g_cachedGEngine          = eng.GEngine;
                        g_cachedGEngineMethod    = eng.gengineMethod;
                        g_cachedGEnginePatternId = eng.genginePatternId;
                        g_cachedGEngineScanAddr  = eng.gengineScanAddr;
                        g_cachedGEngineAob       = eng.gengineAob;
                        g_cachedGEngineAobPos    = eng.gengineAobPos;
                        g_cachedGEngineAobLen    = eng.gengineAobLen;
                        Sein::Info("PIPE:cmd", "apply_rescan: Applied GEngine=0x%llX (%s)",
                                   (unsigned long long)g_cachedGEngine, g_cachedGEngineMethod);
                        applied = true;
                    }
                }
            }

            json data;
            data["applied"]      = applied;
            data["gobjects"]     = Renge::AddrToStr(g_cachedGObjects);
            data["gnames"]       = Renge::AddrToStr(g_cachedGNames);
            data["gworld"]       = Renge::AddrToStr(g_cachedGWorld);
            data["object_count"] = Aura::GetCount();
            return Renge::MakeResponse(id, data).dump();
        }

        // ── trigger_scan: UI-initiated deferred scan (proxy DLL mode) ────────
        // The proxy DLL starts the pipe server without scanning. The UI sends
        // this command when the user is ready (game loaded, world active).
        // Async: starts a background thread, returns immediately.
        // Also safe to call in CE/manual inject mode — UE5_Init is idempotent.
        if (cmd == Renge::CMD_TRIGGER_SCAN) {
            if (m_scan.running.load()) {
                return Renge::MakeError(id, "Scan already in progress").dump();
            }

            Sein::Info("PIPE:cmd", "trigger_scan: Starting async engine scan...");

            // Re-scan = game state may have changed; drop stale cached names.
            Ubel::ClearNameCache();

            // Reset state and launch background thread
            m_scan.completed = false;
            m_scan.phase.store(0);
            {
                std::lock_guard<std::mutex> lock(m_scan.statusMutex);
                m_scan.statusText = "Starting...";
            }
            m_scan.running.store(true);

            if (m_scan.scanThread.joinable()) m_scan.scanThread.join();
            m_scan.scanThread = std::thread(&Fern::RunScan, this);

            json data;
            data["started"] = true;
            return Renge::MakeResponse(id, data).dump();
        }

        // ── scan_status: Poll scan progress (pairs with trigger_scan) ────────
        if (cmd == Renge::CMD_SCAN_STATUS) {
            namespace SP = ScanProgress;
            int phase = SP::phase.load(std::memory_order_acquire);

            json data;
            data["running"]     = m_scan.running.load();
            data["phase"]       = phase;
            data["status_text"] = SP::GetStatusText();

            // When complete, include full pointer snapshot — same shape as
            // get_pointers. Single helper guarantees the two paths can't drift.
            if (!m_scan.running.load() && m_scan.completed) {
                data["scanned"] = true;
                FillPointerSnapshot(data);
            }

            return Renge::MakeResponse(id, data).dump();
        }

        // === walk_datatable_rows: Browse DataTable RowMap entries ===
        if (cmd == Renge::CMD_WALK_DATATABLE_ROWS) {
            std::string addrStr = request.value("addr", "");
            if (addrStr.empty()) return Renge::MakeError(id, "Missing addr").dump();

            uintptr_t addr = Renge::StrToAddr(addrStr);
            int32_t offset = request.value("offset", 0);
            int32_t limit  = request.value("limit", 64);

            auto result = Ubel::WalkDataTableRows(addr, offset, limit);

            if (!result.ok)
                return Renge::MakeError(id, result.error).dump();

            json data;
            data["row_count"]       = result.rowCount;
            data["row_map_offset"]  = result.rowMapOffset;
            data["row_struct_addr"] = Renge::AddrToStr(result.rowStructAddr);
            data["row_struct_name"] = result.rowStructName;
            data["fname_size"]      = result.fnameSize;
            data["stride"]          = result.stride;

            json rows = json::array();
            for (const auto& row : result.rows) {
                json rj;
                rj["sparse_index"] = row.sparseIndex;
                rj["row_name"]     = row.rowName;
                rj["data_addr"]    = Renge::AddrToStr(row.rowDataAddr);

                json rowFields = json::array();
                for (const auto& fv : row.fields) {
                    rowFields.push_back(SerializeField(fv));
                }
                rj["fields"] = rowFields;
                rows.push_back(rj);
            }
            data["rows"] = rows;
            return Renge::MakeResponse(id, data).dump();
        }

        // ── invoke_function: Call ProcessEvent via pipe (bypasses CE executeCodeEx) ──
        if (cmd == Renge::CMD_INVOKE_FUNCTION) {
            std::string className   = request.value("class_name", "");
            std::string funcName    = request.value("func_name", "");
            std::string instAddrStr = request.value("instance_addr", "");
            std::string paramsHex   = request.value("params_hex", "");
            int parmsSize           = request.value("parms_size", 0);
            // direct_call: caller has asserted the function is safe to invoke
            // off-thread (FUNC_Native|FUNC_Static — e.g. KismetMathLibrary
            // helpers). Bypasses GameThreadDispatch so the call works on idle
            // main-menu / loading screens where the game thread isn't pumping
            // ProcessEvent. Required by System tab Self-Test which must run
            // even before the user enters live gameplay.
            bool directCall         = request.value("direct_call", false);

            if (funcName.empty()) {
                return Renge::MakeError(id, "func_name is required").dump();
            }

            // Resolve instance address
            uintptr_t instanceAddr = 0;
            if (!instAddrStr.empty()) {
                instanceAddr = Renge::StrToAddr(instAddrStr);
                if (instanceAddr == 0) {
                    return Renge::MakeError(id, "Invalid instance_addr").dump();
                }
            } else if (!className.empty()) {
                instanceAddr = UE5_FindInstanceOfClass(className.c_str());
                if (instanceAddr == 0) {
                    return Renge::MakeError(id,
                        "No instance found for class: " + className).dump();
                }
            } else {
                return Renge::MakeError(id,
                    "Either instance_addr or class_name is required").dump();
            }

            // Get class address from instance
            uintptr_t classAddr = UE5_GetObjectClass(instanceAddr);
            if (classAddr == 0) {
                return Renge::MakeError(id,
                    "Failed to read class from instance " + Renge::AddrToStr(instanceAddr)).dump();
            }

            // Resolve UFunction
            uintptr_t ufuncAddr = UE5_FindFunctionByName(classAddr, funcName.c_str());
            if (ufuncAddr == 0) {
                return Renge::MakeError(id,
                    "Function not found: " + funcName).dump();
            }

            // Build parameter buffer (zero-filled, then overlay hex bytes)
            size_t bufSize = (parmsSize > 0) ? static_cast<size_t>(parmsSize) : 0;
            std::vector<uint8_t> paramBuf(bufSize, 0);

            if (!paramsHex.empty()) {
                std::vector<uint8_t> hexBytes;
                if (!Renge::TryHexToBytes(paramsHex, hexBytes)) {
                    return Renge::MakeError(id,
                        "Invalid params hex (need an even-length hex string): "
                        + paramsHex).dump();
                }
                size_t copyLen = (std::min)(hexBytes.size(), paramBuf.size());
                if (copyLen > 0) {
                    memcpy(paramBuf.data(), hexBytes.data(), copyLen);
                }
            }

            // ── String INPUT params: build by-value FStrings in-process ──
            // An FString param is passed BY VALUE as { CharT* Data; int32 Num;
            // int32 Max } inline in the params buffer, and its Data pointer must
            // be a valid GAME-process address. It is: this DLL is injected, so a
            // heap buffer allocated here lives in the game's address space (the
            // same reason paramBuf.data() works as the params pointer). The UI
            // sends these descriptors and leaves the 16-byte slots zeroed.
            // 字串輸入參數以傳值的 { Data*, Num, Max } 內嵌於 params buffer，其 Data
            // 指標必須是遊戲行程內的有效位址。此 DLL 為注入式，故我們在此配置的 heap
            // buffer 就位於遊戲位址空間（與 paramBuf.data() 可作為 params 同理）。
            std::vector<void*> strAllocs;
            try {
            if (request.contains("str_params") && request["str_params"].is_array()) {
                for (const auto& sp : request["str_params"]) {
                    int off  = sp.value("off", -1);
                    bool wide = sp.value("wide", true);
                    std::string text = sp.value("text", "");
                    // Bounds: the whole 16-byte FString struct must fit.
                    if (off < 0 || static_cast<size_t>(off) + 16 > paramBuf.size()) {
                        Sein::Warn("PIPE:cmd",
                                   "invoke_function: str_param off=%d out of range (parms=%zu)",
                                   off, paramBuf.size());
                        continue;
                    }
                    int32_t num = 0;          // element count incl null terminator
                    void* dataBuf = nullptr;
                    if (wide) {
                        // UTF-8 (JSON) -> UTF-16LE via the OS codec (full Unicode).
                        int wlen = MultiByteToWideChar(CP_UTF8, 0, text.c_str(),
                                                       static_cast<int>(text.size()), nullptr, 0);
                        if (wlen < 0) wlen = 0;
                        num = wlen + 1;
                        wchar_t* wbuf = static_cast<wchar_t*>(malloc(static_cast<size_t>(num) * sizeof(wchar_t)));
                        if (!wbuf) continue;
                        if (wlen > 0) {
                            MultiByteToWideChar(CP_UTF8, 0, text.c_str(),
                                                static_cast<int>(text.size()), wbuf, wlen);
                        }
                        wbuf[wlen] = L'\0';
                        dataBuf = wbuf;
                    } else {
                        // Narrow: raw bytes (FUtf8String = UTF-8, FAnsiString ~ ANSI).
                        num = static_cast<int32_t>(text.size()) + 1;
                        char* cbuf = static_cast<char*>(malloc(static_cast<size_t>(num)));
                        if (!cbuf) continue;
                        if (!text.empty()) memcpy(cbuf, text.data(), text.size());
                        cbuf[text.size()] = '\0';
                        dataBuf = cbuf;
                    }
                    strAllocs.push_back(dataBuf);
                    // Patch FString { Data(+0,8), Num(+8,4), Max(+12,4) } at off.
                    uintptr_t dataPtr = reinterpret_cast<uintptr_t>(dataBuf);
                    memcpy(paramBuf.data() + off,      &dataPtr, sizeof(uintptr_t));
                    memcpy(paramBuf.data() + off + 8,  &num,     sizeof(int32_t));
                    memcpy(paramBuf.data() + off + 12, &num,     sizeof(int32_t));
                }
            }
            }
            catch (...) {
                // A malformed str_params element (a nlohmann type_error thrown mid-loop)
                // must not leak the heap buffers earlier iterations allocated. Free them
                // and rethrow — the dispatch envelope turns it into an error response. (L12)
                for (void* p : strAllocs) free(p);
                throw;
            }

            uintptr_t paramPtr = bufSize > 0
                ? reinterpret_cast<uintptr_t>(paramBuf.data())
                : 0;

            Sein::Info("PIPE:cmd", "invoke_function: %s::%s inst=%s func=%s parms=%d direct=%d",
                         className.c_str(), funcName.c_str(),
                         Renge::AddrToStr(instanceAddr).c_str(),
                         Renge::AddrToStr(ufuncAddr).c_str(),
                         (int)bufSize, directCall ? 1 : 0);

            // Call ProcessEvent. directCall=true uses the direct entry point
            // (no GameThreadDispatch queue), matching Mimic's static-native
            // fast path. Caller is responsible for asserting safety.
            // The queued path uses the size-aware Ex entry so the request owns a
            // copy of paramBuf — otherwise a timeout would leave the game thread
            // dereferencing this freed stack-local buffer (use-after-free).
            int32_t callResult = directCall
                ? UE5_CallProcessEventDirect(instanceAddr, ufuncAddr, paramPtr)
                : UE5_CallProcessEventEx(instanceAddr, ufuncAddr, paramPtr, (uint32_t)bufSize);

            // Free the by-value FString buffers. UE's calling convention makes
            // the CALLER own the params, and a UFUNCTION receives its FString by
            // value (copy) or const-ref (read) — so after ProcessEvent returns
            // the callee no longer needs our Data buffer and freeing is correct.
            // EXCEPTION: a game-thread dispatch TIMEOUT (-5) leaves the request
            // QUEUED with a COPY of paramBuf whose Data pointers alias these
            // buffers; a later drain would deref them, so we deliberately LEAK
            // on -5 to stay crash-safe (matches the CE-side policy).
            // 逾時(-5)時佇列仍持有指向這些 buffer 的複本，稍後排空會解參考，故刻意
            // 洩漏以避免崩潰；其餘路徑呼叫已結束，立即釋放。
            if (callResult != -5) {
                for (void* p : strAllocs) free(p);
                strAllocs.clear();
            }

            // Build response
            json data;
            data["result"]        = callResult;
            data["instance_addr"] = Renge::AddrToStr(instanceAddr);
            data["func_addr"]     = Renge::AddrToStr(ufuncAddr);
            data["parms_size"]    = (int)bufSize;

            // Return post-call buffer (may contain out-param values)
            if (bufSize > 0) {
                data["result_hex"] = Renge::BytesToHex(paramBuf.data(), bufSize);
            }

            if (callResult == 0) {
                data["message"] = "ProcessEvent OK";
            } else {
                std::string errMsg = "ProcessEvent error code " + std::to_string(callResult);
                if (callResult == -1)      errMsg += " (invalid args)";
                else if (callResult == -2) errMsg += " (vtable read failed)";
                else if (callResult == -3) errMsg += " (ProcessEvent offset not found)";
                else if (callResult == -4) errMsg += " (exception during call)";
                else if (callResult == -5) errMsg += " (game-thread dispatch timeout)";
                // -7 does NOT mean "a direct call was made". It is produced ONLY by
                // Stark::EnqueueInvoke — its `if (!s_hookActive) return -7;` guard, or
                // Stark::Shutdown draining the queue with set_value(-7) — and neither
                // path reaches ProcessEvent by any route: the direct fallback lives on
                // the other side of UE5_CallProcessEventEx's `if (Stark::IsHookActive())`
                // and returns 0/-2/-3/-4/-8, never -7. The old text named an execution
                // that provably did not happen, sending the user to inspect the function
                // and the game state when the truth is that nothing was ever dispatched.
                // (audit #5 D5/F7)
                else if (callResult == -7) errMsg += " (game-thread hook is down — the invoke was "
                                                     "never dispatched; re-enable the script and retry)";
                // -8 had no mapping at all, so it fell through as a bare number.
                else if (callResult == -8) errMsg += " (repeating worker invoke refused while the "
                                                     "hook is down)";
                data["error"] = errMsg;
            }

            return Renge::MakeResponse(id, data).dump();
        }

        // ── get_debug_camera_state: read live Debug Camera ON/OFF ──
        if (cmd == Renge::CMD_GET_DEBUG_CAMERA_STATE) {
            int32_t state = UE5_GetDebugCameraState();
            json data;
            data["state"] = state;   // 1=on, 0=off, -1=unknown
            return Renge::MakeResponse(id, data).dump();
        }

        // ── set_debug_camera: robust force on/off (toggle + swap fallback) ──
        if (cmd == Renge::CMD_SET_DEBUG_CAMERA) {
            bool enable = request.value("enable", false);
            Sein::Info("PIPE:cmd", "set_debug_camera: enable=%d", enable ? 1 : 0);
            int32_t state = UE5_SetDebugCamera(enable ? 1 : 0);
            json data;
            data["state"] = state;   // resulting state: 1=on, 0=off, -1=error
            return Renge::MakeResponse(id, data).dump();
        }

        // ── set_god_mode / get_god_mode / get_protect_state (Solitar) ──
        // GodMode ON ⇒ the local pawn's bCanBeDamaged is forced FALSE and
        // re-asserted on a timer. Non-negative state = observed live state
        // (1 immune / 0 can-be-damaged); negative = Solitar::ProtectResult.
        if (cmd == Renge::CMD_SET_GOD_MODE) {
            bool enable = request.value("enable", false);
            Sein::Info("PIPE:cmd", "set_god_mode: enable=%d", enable ? 1 : 0);
            int32_t state = UE5_SetGodMode(enable ? 1 : 0);
            json data;
            data["state"] = state;
            data["code"]  = (state < 0) ? state : 0;
            return Renge::MakeResponse(id, data).dump();
        }
        if (cmd == Renge::CMD_GET_GOD_MODE) {
            int32_t state = UE5_GetGodMode();
            json data;
            data["state"] = state;
            data["code"]  = (state < 0) ? state : 0;
            return Renge::MakeResponse(id, data).dump();
        }
        if (cmd == Renge::CMD_GET_PROTECT_STATE) {
            int32_t want = 0, live = -1, resolvable = 0;
            int32_t code = UE5_GetProtectState(&want, &live, &resolvable);
            json data;
            data["want"]       = want;        // desired toggle (1/0), survives reconnect
            data["godmode"]    = live;        // observed live state (1/0, -1 = no pawn)
            data["resolvable"] = resolvable != 0;
            data["code"]       = code;
            return Renge::MakeResponse(id, data).dump();
        }

        // ── set_foreground_lock / get_foreground_lock (Grausam) ──
        // ON ⇒ hook GetForegroundWindow so the game always believes it is the
        // foreground app; defeats t.IdleWhenNotForeground idle + focus-loss pause
        // so game-thread ops keep working while our UI/CE holds the foreground.
        if (cmd == Renge::CMD_SET_FOREGROUND_LOCK) {
            bool enable = request.value("enable", false);
            Sein::Info("PIPE:cmd", "set_foreground_lock: enable=%d", enable ? 1 : 0);
            int32_t state = Grausam::SetForegroundLock(enable);
            json data;
            data["state"] = state;                    // 1=on, 0=off, <0=error
            data["code"]  = (state < 0) ? state : 0;
            return Renge::MakeResponse(id, data).dump();
        }
        if (cmd == Renge::CMD_GET_FOREGROUND_LOCK) {
            json data;
            data["state"] = Grausam::IsForegroundLockEnabled();  // 1=on, 0=off
            return Renge::MakeResponse(id, data).dump();
        }

        // ── get_movement_params / set_movement_multiplier / reset_movement (Laufen) ──
        // Per-pawn CharacterMovement float knobs (MaxWalkSpeed/GravityScale/
        // JumpZVelocity) forced by a multiplier of their captured base and held by
        // a re-assert worker. Each knob surfaces (owner_addr,field_offset) for the
        // same "Locate in GWorld" handoff teleport_get_pose uses for loc/vel.
        if (cmd == Renge::CMD_GET_MOVEMENT_PARAMS) {
            Laufen::Snapshot snap{};
            int32_t code = Laufen::GetSnapshot(snap);
            auto knobJson = [](const Laufen::KnobInfo& k) {
                json j;
                j["resolved"]   = k.resolved;
                j["current"]    = k.current;
                j["base"]       = k.base;
                j["multiplier"] = k.multiplier;
                j["active"]     = k.active;
                if (k.resolved && k.ownerAddr && k.fieldOffset >= 0) {
                    j["owner_addr"]   = Renge::AddrToStr(k.ownerAddr);
                    j["field_offset"] = k.fieldOffset;
                    j["field_name"]   = k.fieldName;
                }
                return j;
            };
            json data;
            data["code"]    = code;            // 0 ok; negative Laufen::MoveResult
            data["has_cmc"] = snap.hasCmc;
            if (snap.cmcAddr) data["cmc_addr"] = Renge::AddrToStr(snap.cmcAddr);
            json knobs;
            knobs["walk_speed"] = knobJson(snap.knobs[Laufen::KNOB_WALK_SPEED]);
            knobs["gravity"]    = knobJson(snap.knobs[Laufen::KNOB_GRAVITY]);
            knobs["jump"]       = knobJson(snap.knobs[Laufen::KNOB_JUMP]);
            data["knobs"] = knobs;
            // Gravity DIRECTION (UE5.4+); resolved=false on pre-5.4 games.
            json gd;
            gd["resolved"] = snap.gravDir.resolved;
            gd["x"] = snap.gravDir.x; gd["y"] = snap.gravDir.y; gd["z"] = snap.gravDir.z;
            gd["active"]   = snap.gravDir.active;
            if (snap.gravDir.resolved && snap.gravDir.ownerAddr && snap.gravDir.fieldOffset >= 0) {
                gd["owner_addr"]   = Renge::AddrToStr(snap.gravDir.ownerAddr);
                gd["field_offset"] = snap.gravDir.fieldOffset;
                gd["field_name"]   = snap.gravDir.fieldName;
            }
            data["gravity_direction"] = gd;
            return Renge::MakeResponse(id, data).dump();
        }
        if (cmd == Renge::CMD_SET_GRAVITY_DIRECTION) {
            double x = request.value("x", 0.0);
            double y = request.value("y", 0.0);
            double z = request.value("z", 0.0);
            Sein::Info("PIPE:cmd", "set_gravity_direction: (%.3f, %.3f, %.3f)", x, y, z);
            int32_t state = Laufen::SetGravityDirection(x, y, z);   // (0,0,0) = off
            Laufen::GravDirInfo info{};
            Laufen::GetGravityDirection(info);
            json data;
            data["state"]    = state;                    // 1 active / 0 off / negative
            data["code"]     = (state < 0) ? state : 0;
            data["resolved"] = info.resolved;
            data["x"] = info.x; data["y"] = info.y; data["z"] = info.z;
            data["active"]   = info.active;
            if (info.resolved && info.ownerAddr && info.fieldOffset >= 0) {
                data["owner_addr"]   = Renge::AddrToStr(info.ownerAddr);
                data["field_offset"] = info.fieldOffset;
                data["field_name"]   = info.fieldName;
            }
            return Renge::MakeResponse(id, data).dump();
        }
        if (cmd == Renge::CMD_RESET_GRAVITY_DIRECTION) {
            Sein::Info("PIPE:cmd", "reset_gravity_direction");
            int32_t code = Laufen::ResetGravityDirection();
            Laufen::GravDirInfo info{};
            Laufen::GetGravityDirection(info);
            json data;
            data["code"]     = code;
            data["resolved"] = info.resolved;
            data["x"] = info.x; data["y"] = info.y; data["z"] = info.z;
            data["active"]   = info.active;
            return Renge::MakeResponse(id, data).dump();
        }
        if (cmd == Renge::CMD_SET_MOVEMENT_MULTIPLIER) {
            std::string knob = request.value("knob", std::string());
            double mult = request.value("multiplier", 1.0);
            int32_t kid = (knob == "walk_speed") ? Laufen::KNOB_WALK_SPEED
                        : (knob == "gravity")    ? Laufen::KNOB_GRAVITY
                        : (knob == "jump")       ? Laufen::KNOB_JUMP : -1;
            if (kid < 0) return Renge::MakeError(id, "Unknown movement knob: " + knob).dump();
            Sein::Info("PIPE:cmd", "set_movement_multiplier: knob=%s mult=%.3f",
                       knob.c_str(), mult);
            int32_t state = Laufen::SetMultiplier(kid, mult);
            Laufen::KnobInfo info{};
            Laufen::GetKnob(kid, info);
            json data;
            data["state"]      = state;                  // 1 active / negative MoveResult
            data["code"]       = (state < 0) ? state : 0;
            data["current"]    = info.current;
            data["base"]       = info.base;
            data["multiplier"] = info.multiplier;
            data["active"]     = info.active;
            data["resolved"]   = info.resolved;
            if (info.resolved && info.ownerAddr && info.fieldOffset >= 0) {
                data["owner_addr"]   = Renge::AddrToStr(info.ownerAddr);
                data["field_offset"] = info.fieldOffset;
                data["field_name"]   = info.fieldName;
            }
            return Renge::MakeResponse(id, data).dump();
        }
        if (cmd == Renge::CMD_RESET_MOVEMENT) {
            std::string knob = request.value("knob", std::string());
            int32_t kid = (knob == "walk_speed") ? Laufen::KNOB_WALK_SPEED
                        : (knob == "gravity")    ? Laufen::KNOB_GRAVITY
                        : (knob == "jump")       ? Laufen::KNOB_JUMP : -1;
            if (kid < 0) return Renge::MakeError(id, "Unknown movement knob: " + knob).dump();
            Sein::Info("PIPE:cmd", "reset_movement: knob=%s", knob.c_str());
            int32_t code = Laufen::ResetKnob(kid);
            Laufen::KnobInfo info{};
            Laufen::GetKnob(kid, info);
            json data;
            data["code"]       = code;
            data["current"]    = info.current;
            data["base"]       = info.base;
            data["multiplier"] = info.multiplier;
            data["active"]     = info.active;
            data["resolved"]   = info.resolved;
            return Renge::MakeResponse(id, data).dump();
        }

        // ── set_time_dilation / reset_time_dilation / get_time_state (Hemmung) ──
        // Hold a reflected dilation float (global AWorldSettings::TimeDilation or
        // per-pawn AActor::CustomTimeDilation) at an absolute value against per-tick
        // game/Sequencer overwrites, via a re-assert worker. Each target surfaces
        // (owner_addr,field_offset) for the same "Locate in GWorld" handoff the
        // movement knobs use.
        {
            auto timeTargetId = [](const std::string& t) -> int32_t {
                return (t == "global") ? Hemmung::DIL_GLOBAL
                     : (t == "pawn")   ? Hemmung::DIL_PAWN : -1;
            };
            auto dilJson = [](const Hemmung::DilationInfo& d) {
                json j;
                j["resolved"] = d.resolved;
                j["current"]  = d.current;
                j["base"]     = d.base;
                j["value"]    = d.value;
                j["active"]   = d.active;
                if (d.resolved && d.ownerAddr && d.fieldOffset >= 0) {
                    j["owner_addr"]   = Renge::AddrToStr(d.ownerAddr);
                    j["field_offset"] = d.fieldOffset;
                    j["field_name"]   = d.fieldName;
                }
                return j;
            };
            if (cmd == Renge::CMD_SET_TIME_DILATION) {
                std::string target = request.value("target", std::string("global"));
                double value = request.value("value", 1.0);
                int32_t tid = timeTargetId(target);
                if (tid < 0) return Renge::MakeError(id, "Unknown time target: " + target).dump();
                Sein::Info("PIPE:cmd", "set_time_dilation: target=%s value=%.4f",
                           target.c_str(), value);
                int32_t state = Hemmung::SetDilation(tid, value);
                Hemmung::DilationInfo info{};
                Hemmung::GetDilation(tid, info);
                json data;
                data["state"]  = state;                    // 1 active / negative TimeResult
                data["code"]   = (state < 0) ? state : 0;
                data["target"] = target;
                data["dilation"] = dilJson(info);
                return Renge::MakeResponse(id, data).dump();
            }
            if (cmd == Renge::CMD_RESET_TIME_DILATION) {
                std::string target = request.value("target", std::string("global"));
                int32_t tid = timeTargetId(target);
                if (tid < 0) return Renge::MakeError(id, "Unknown time target: " + target).dump();
                Sein::Info("PIPE:cmd", "reset_time_dilation: target=%s", target.c_str());
                int32_t code = Hemmung::ResetDilation(tid);
                Hemmung::DilationInfo info{};
                Hemmung::GetDilation(tid, info);
                json data;
                data["code"]     = code;
                data["target"]   = target;
                data["dilation"] = dilJson(info);
                return Renge::MakeResponse(id, data).dump();
            }
            if (cmd == Renge::CMD_GET_TIME_STATE) {
                Hemmung::Snapshot snap{};
                int32_t code = Hemmung::GetSnapshot(snap);
                json data;
                data["code"] = code;                       // 0 ok; negative TimeResult
                json dils;
                dils["global"] = dilJson(snap.dils[Hemmung::DIL_GLOBAL]);
                dils["pawn"]   = dilJson(snap.dils[Hemmung::DIL_PAWN]);
                data["dilation"] = dils;
                return Renge::MakeResponse(id, data).dump();
            }
        }

        // ── force_field / reset_field / reset_all_fields / get_forced_fields /
        //    find_stealth_meter (Solide) — hold a discovered reflected field ──
        {
            auto kindId = [](const std::string& k) -> int32_t {
                return (k == "bool")        ? Solide::K_BOOL
                     : (k == "object_null") ? Solide::K_OBJECT_NULL
                     : (k == "numeric")     ? Solide::K_NUMERIC : -1;
            };
            auto kindStr = [](int32_t k) -> const char* {
                return (k == Solide::K_BOOL)        ? "bool"
                     : (k == Solide::K_OBJECT_NULL) ? "object_null"
                     : (k == Solide::K_NUMERIC)     ? "numeric" : "?";
            };
            if (cmd == Renge::CMD_FORCE_FIELD) {
                std::string className = request.value("class_name", std::string());
                std::string fieldName = request.value("field_name", std::string());
                std::string kind      = request.value("kind", std::string("bool"));
                if (className.empty() || fieldName.empty())
                    return Renge::MakeError(id, "force_field: missing class_name or field_name").dump();
                int32_t k = kindId(kind);
                if (k < 0) return Renge::MakeError(id, "force_field: unknown kind: " + kind).dump();
                double value = (k == Solide::K_BOOL)
                             ? (request.value("on", false) ? 1.0 : 0.0)
                             : request.value("value", 0.0);
                Sein::Info("PIPE:cmd", "force_field: class=%s field=%s kind=%s value=%.4f",
                           className.c_str(), fieldName.c_str(), kind.c_str(), value);
                int32_t held = Solide::AddForce(className.c_str(), fieldName.c_str(), k, value);
                json data;
                data["held"]     = (held < 0) ? 0 : held;   // live "N held" count
                data["resolved"] = (held > 0);
                data["code"]     = (held < 0) ? held : 0;
                // Was the instance pool capped? AddForce's int32_t return is fully spoken
                // for (negative = ForceResult, non-negative = held count — see audit L2,
                // do not overload it), so read the flag back off the job instead.
                if (held > 0) {
                    std::vector<Solide::ForcedFieldInfo> st;
                    if (Solide::GetState(st) == Solide::FR_OK) {
                        for (const auto& f : st) {
                            if (f.className == className && f.fieldName == fieldName) {
                                data["truncated"] = f.poolTruncated;
                                break;
                            }
                        }
                    }
                }
                return Renge::MakeResponse(id, data).dump();
            }
            if (cmd == Renge::CMD_RESET_FIELD) {
                std::string className = request.value("class_name", std::string());
                std::string fieldName = request.value("field_name", std::string());
                if (className.empty() || fieldName.empty())
                    return Renge::MakeError(id, "reset_field: missing class_name or field_name").dump();
                Sein::Info("PIPE:cmd", "reset_field: class=%s field=%s",
                           className.c_str(), fieldName.c_str());
                int32_t code = Solide::RemoveForce(className.c_str(), fieldName.c_str());
                json data; data["code"] = code;
                return Renge::MakeResponse(id, data).dump();
            }
            if (cmd == Renge::CMD_RESET_ALL_FIELDS) {
                Sein::Info("PIPE:cmd", "reset_all_fields");
                int32_t code = Solide::ClearAll();
                json data; data["code"] = code;
                return Renge::MakeResponse(id, data).dump();
            }
            if (cmd == Renge::CMD_GET_FORCED_FIELDS) {
                std::vector<Solide::ForcedFieldInfo> fields;
                int32_t code = Solide::GetState(fields);
                json arr = json::array();
                for (const auto& f : fields) {
                    json j;
                    j["class_name"] = f.className;
                    j["field_name"] = f.fieldName;
                    j["kind"]       = kindStr(f.kind);
                    j["value"]      = f.value;
                    j["held"]       = f.held;
                    j["truncated"]  = f.poolTruncated;  // held is a floor, not a total
                    if (f.sampleOwner && f.sampleOffset >= 0) {
                        j["owner_addr"]   = Renge::AddrToStr(f.sampleOwner);
                        j["field_offset"] = f.sampleOffset;
                    }
                    arr.push_back(j);
                }
                json data; data["code"] = code; data["fields"] = arr;
                return Renge::MakeResponse(id, data).dump();
            }
            if (cmd == Renge::CMD_FIND_STEALTH_METER) {
                int maxResults = request.value("max", 8);
                std::vector<Solide::StealthCandidate> cands;
                int32_t code = Solide::FindStealthMeter(cands, maxResults);
                json arr = json::array();
                for (const auto& c : cands) {
                    json j;
                    j["class_name"] = c.className;
                    j["class_addr"] = Renge::AddrToStr(c.classAddr);
                    j["field_name"] = c.fieldName;
                    j["prop_type"]  = c.typeName;
                    j["owner_addr"] = Renge::AddrToStr(c.ownerAddr);
                    j["current"]    = c.current;
                    j["score"]      = c.score;
                    arr.push_back(j);
                }
                json data; data["code"] = code; data["candidates"] = arr;
                return Renge::MakeResponse(id, data).dump();
            }
        }

        // ── fly_set / fly_get_state (Dunste) — no-gravity 3D flight ──
        // fly_set applies whichever of {enable, speed, preset} are present, then
        // returns the live status. Input (WASD/numpad/arrows) is sampled DLL-side
        // by the fly worker (GetAsyncKeyState) — the pipe only toggles + configs,
        // so there is no per-frame IPC.
        auto flyStatusJson = [](const Dunste::FlyStatus& st) {
            json data;
            data["code"]          = st.code;          // 0 ok; negative Dunste::FlyResult
            data["active"]        = st.active;
            data["noclip"]        = st.noclip;        // position-drive (through walls)
            data["has_cmc"]       = st.hasCmc;
            data["preset"]        = st.preset;        // 0 WASD / 1 numpad / 2 arrows
            data["speed"]         = st.speed;         // uu/s
            data["mode_resolved"] = st.modeResolved;
            data["current_mode"]  = st.currentMode;   // live MovementMode enum (5 = flying)
            if (st.hasCmc && st.cmcAddr)
                data["cmc_addr"]  = Renge::AddrToStr(st.cmcAddr);
            return data;
        };
        if (cmd == Renge::CMD_FLY_SET) {
            if (request.contains("speed"))
                Dunste::SetSpeed(request.value("speed", 0.0));
            if (request.contains("preset"))
                Dunste::SetPreset(request.value("preset", 0));
            if (request.contains("noclip"))
                Dunste::SetNoclip(request.value("noclip", false));
            int32_t state = -1;
            const bool haveEnable = request.contains("enable");
            if (haveEnable)
                state = Dunste::SetEnabled(request.value("enable", false));
            Sein::Info("PIPE:cmd", "fly_set: enable=%s speed=%s preset=%s noclip=%s",
                       haveEnable ? (request.value("enable", false) ? "1" : "0") : "-",
                       request.contains("speed") ? "y" : "-",
                       request.contains("preset") ? "y" : "-",
                       request.contains("noclip") ? (request.value("noclip", false) ? "1" : "0") : "-");
            Dunste::FlyStatus st{};
            Dunste::GetStatus(st);
            json data = flyStatusJson(st);
            data["state"] = haveEnable ? state : (st.active ? 1 : 0);
            return Renge::MakeResponse(id, data).dump();
        }
        if (cmd == Renge::CMD_FLY_GET_STATE) {
            Dunste::FlyStatus st{};
            Dunste::GetStatus(st);
            return Renge::MakeResponse(id, flyStatusJson(st)).dump();
        }

        // ── seethrough_set / seethrough_get_state (Schlacht) — see-through occluders ──
        // seethrough_set applies {enable} then returns the live status; get_state
        // polls it. Trace/hide run DLL-side on the worker; the pipe only toggles.
        auto seeThroughStatusJson = [](const Schlacht::SeeThroughStatus& st) {
            json data;
            data["code"]         = st.code;          // 0 ok; negative Schlacht::SeeThroughResult
            data["active"]       = st.active;
            // Why the feature may be refused (code -5) and whether it can be
            // retried NOW: the game-thread hook can fail transiently to install
            // and recover later, so the card needs the live value, not a memory
            // of the last failure.
            data["hook_active"]  = UE5_IsGameThreadHookActive();
            data["has_target"]   = st.hasTarget;     // camera + pawn resolved last tick
            data["hidden_count"] = st.hiddenCount;   // occluders currently hidden
            data["pierce_count"] = st.pierceCount;   // nearest occluders to hide along the ray
            return data;
        };
        if (cmd == Renge::CMD_SEE_THROUGH_SET) {
            if (request.contains("count"))
                Schlacht::SetPierceCount(request.value("count", 1));
            int32_t state = -1;
            const bool haveEnable = request.contains("enable");
            if (haveEnable)
                state = Schlacht::SetEnabled(request.value("enable", false));
            Sein::Info("PIPE:cmd", "seethrough_set: enable=%s count=%d",
                       haveEnable ? (request.value("enable", false) ? "1" : "0") : "-",
                       request.contains("count") ? request.value("count", 1) : -1);
            Schlacht::SeeThroughStatus st{};
            Schlacht::GetStatus(st);
            json data = seeThroughStatusJson(st);
            data["state"] = haveEnable ? state : (st.active ? 1 : 0);
            return Renge::MakeResponse(id, data).dump();
        }
        if (cmd == Renge::CMD_SEE_THROUGH_GET_STATE) {
            Schlacht::SeeThroughStatus st{};
            Schlacht::GetStatus(st);
            return Renge::MakeResponse(id, seeThroughStatusJson(st)).dump();
        }

        // ── teleport_*: marker save/recall + cursor teleport (Wirbel) ──
        // Non-zero Wirbel codes are still ok:true responses with a "code"
        // field — the UI maps codes to user-facing hints (teleport-spec §8);
        // MakeError stays reserved for malformed requests.
        if (cmd == Renge::CMD_TELEPORT_GET_POSE) {
            Wirbel::Pose p{};
            char map[Grimoire::TELEPORT_MAPNAME_CAP] = {};
            uint8_t source = 0;
            Wirbel::MovementState mv{};
            int32_t code = Wirbel::GetPoseAndMovement(p, map, sizeof(map), &source, mv);
            json data;
            data["code"] = code;
            if (code == 0) {
                data["x"] = p.X;         data["y"] = p.Y;     data["z"] = p.Z;
                data["pitch"] = p.Pitch; data["yaw"] = p.Yaw; data["roll"] = p.Roll;
                data["map"] = map;
                data["source"] = (source == 1) ? "invoke" : "raw";
                // Feature B: the resolved pawn — for the "Locate in GWorld" handoff
                // (hex string, matching find_path's object_addr / get_current_target's
                // player_pawn). "0x0" when unresolved.
                data["pawn_addr"] = Renge::AddrToStr(mv.PawnAddr);
                // "Locate position vector in GWorld": owner (RootComponent) + the
                // RelativeLocation FVector offset. The UI hands these to
                // find_path_from_gworld so the path lands on the exact location
                // field, not just the pawn. Emitted only when resolved.
                if (mv.LocOwnerAddr && mv.LocFieldOffset >= 0) {
                    data["loc_owner_addr"]   = Renge::AddrToStr(mv.LocOwnerAddr);
                    data["loc_field_offset"] = mv.LocFieldOffset;
                    data["loc_field_name"]   = "RelativeLocation";
                }
                // "Locate rotation in GWorld": Controller.ControlRotation (FRotator).
                if (mv.RotOwnerAddr && mv.RotFieldOffset >= 0) {
                    data["rot_owner_addr"]   = Renge::AddrToStr(mv.RotOwnerAddr);
                    data["rot_field_offset"] = mv.RotFieldOffset;
                    data["rot_field_name"]   = "ControlRotation";
                }
                // Feature A: live velocity/acceleration off the CharacterMovement.
                // has_movement=false on vehicle / custom-framework pawns (no CMC):
                // the vel/acc fields are then absent and the UI shows "unavailable".
                data["has_movement"] = mv.HasMovement;
                if (mv.HasMovement) {
                    data["vel_x"] = mv.VelX; data["vel_y"] = mv.VelY; data["vel_z"] = mv.VelZ;
                    data["acc_x"] = mv.AccX; data["acc_y"] = mv.AccY; data["acc_z"] = mv.AccZ;
                    data["speed"] = mv.Speed;
                    // "Locate velocity vector in GWorld": owner (CharacterMovement)
                    // + the Velocity FVector offset (same handoff as location).
                    if (mv.VelOwnerAddr && mv.VelFieldOffset >= 0) {
                        data["vel_owner_addr"]   = Renge::AddrToStr(mv.VelOwnerAddr);
                        data["vel_field_offset"] = mv.VelFieldOffset;
                        data["vel_field_name"]   = "Velocity";
                    }
                    // "Locate acceleration vector in GWorld": CMC.Acceleration.
                    if (mv.AccOwnerAddr && mv.AccFieldOffset >= 0) {
                        data["acc_owner_addr"]   = Renge::AddrToStr(mv.AccOwnerAddr);
                        data["acc_field_offset"] = mv.AccFieldOffset;
                        data["acc_field_name"]   = "Acceleration";
                    }
                }
            }
            return Renge::MakeResponse(id, data).dump();
        }

        // get_trainer_offsets: one-shot bake bundle for a no-DLL standalone
        // CE-Lua trainer — the decomposed *GWorld->Pawn chain + RootComponent /
        // RelativeLocation / CharacterMovement knob offsets (Wirbel) + the
        // protection bits (Solitar). Read-only; codes map to teleport hints.
        if (cmd == Renge::CMD_GET_TRAINER_OFFSETS) {
            Wirbel::TrainerOffsets t{};
            int32_t code = Wirbel::GetTrainerOffsets(t);
            json data;
            data["code"] = code;
            if (code == 0) {
                json chain = json::array();
                for (int i = 0; i < t.ChainCount; ++i) {
                    json h;
                    h["field"]  = t.Chain[i].Field;
                    h["offset"] = t.Chain[i].Offset;
                    h["deref"]  = t.Chain[i].Deref;
                    chain.push_back(h);
                }
                data["chain"]          = chain;
                data["pawn_to_root"]   = t.PawnToRoot;
                data["root_to_relloc"] = t.RootToRelLoc;
                data["fvector_width"]  = t.FVectorWidth;
                data["pawn_to_cmc"]    = t.PawnToCmc;
                data["walk_speed_off"] = t.WalkSpeedOff;
                data["gravity_off"]    = t.GravityOff;
                data["jump_off"]       = t.JumpOff;
                data["ctrl_rot_off"]   = t.CtrlRotOff;
                data["ctrl_rot_size"]  = t.CtrlRotSize;
                data["pawn_to_controller"] = t.PawnToController;   // fly: reach ControlRotation
                data["move_mode_off"]  = t.MoveModeOff;            // fly: CMC.MovementMode
                data["velocity_off"]   = t.VelocityOff;            // fly: CMC.Velocity
                data["velocity_size"]  = t.VelocitySize;
                // Protection bits (bCanBeDamaged + any matched invincibility bool).
                std::vector<Solitar::ProtectBit> bits;
                json god = json::array();
                if (Solitar::ResolveProtectBits(bits) == 0) {
                    for (const auto& b : bits) {
                        json e;
                        e["name"]        = b.name;
                        e["byte_offset"] = b.byteOffset;
                        e["mask"]        = static_cast<int>(b.mask);
                        e["protect"]     = static_cast<int>(b.protect);
                        god.push_back(e);
                    }
                }
                data["god_bits"] = god;
            }
            return Renge::MakeResponse(id, data).dump();
        }

        if (cmd == Renge::CMD_TELEPORT_SAVE_MARKER) {
            int slot = request.value("slot", -1);
            int32_t code = Wirbel::SaveMarker(slot);
            Sein::Info("PIPE:cmd", "teleport_save_marker: slot=%d -> %d", slot, code);
            json data;
            data["code"] = code;
            data["slot"] = slot;
            if (code == 0) {
                Wirbel::Marker m{};
                if (Wirbel::GetMarker(slot, m) == 0) {
                    data["x"] = m.P.X;         data["y"] = m.P.Y;     data["z"] = m.P.Z;
                    data["pitch"] = m.P.Pitch; data["yaw"] = m.P.Yaw; data["roll"] = m.P.Roll;
                    data["map"] = m.MapName;
                }
            }
            return Renge::MakeResponse(id, data).dump();
        }

        if (cmd == Renge::CMD_TELEPORT_RECALL_MARKER) {
            uint8_t tier = 0;
            int32_t code;
            json data;
            if (request.contains("x") && request.contains("y") && request.contains("z")) {
                // Explicit-pose variant (BugItGo interop): bypasses the marker
                // store and the map check. Rotation restored only when given.
                Wirbel::Pose p{};
                p.X = request.value("x", 0.0);
                p.Y = request.value("y", 0.0);
                p.Z = request.value("z", 0.0);
                bool hasRot = request.contains("pitch") || request.contains("yaw");
                p.Pitch = request.value("pitch", 0.0);
                p.Yaw   = request.value("yaw", 0.0);
                p.Roll  = request.value("roll", 0.0);
                code = Wirbel::RecallExplicit(p, hasRot, &tier);
                Sein::Info("PIPE:cmd", "teleport_recall_marker: explicit -> %d", code);
            } else {
                int slot = request.value("slot", -1);
                bool force = request.value("force", false);
                code = Wirbel::RecallMarker(slot, force, &tier);
                Sein::Info("PIPE:cmd", "teleport_recall_marker: slot=%d force=%d -> %d",
                           slot, force ? 1 : 0, code);
                if (code == Wirbel::TP_ERR_MAP_MISMATCH) {
                    Wirbel::Marker m{};
                    if (Wirbel::GetMarker(slot, m) == 0)
                        data["markerMap"] = m.MapName;
                    char cur[Grimoire::TELEPORT_MAPNAME_CAP] = {};
                    if (Wirbel::GetCurrentMapName(cur, sizeof(cur)))
                        data["map"] = cur;
                }
            }
            data["code"] = code;
            data["tier"] = tier;
            return Renge::MakeResponse(id, data).dump();
        }

        if (cmd == Renge::CMD_TELEPORT_TO_CURSOR) {
            double zOffset = request.value("zOffset", Grimoire::TELEPORT_DEFAULT_ZOFFSET);
            int channel = request.value("channel", 0);
            bool fallbackCenter = request.value("fallbackCenter", true);
            Wirbel::Pose hit{};
            uint8_t tier = 0;
            bool usedCenter = false;
            int32_t code = Wirbel::TeleportToCursor(zOffset, channel, fallbackCenter,
                                                    &hit, &tier, &usedCenter);
            Sein::Info("PIPE:cmd", "teleport_to_cursor: z=%.1f ch=%d -> %d",
                       zOffset, channel, code);
            json data;
            data["code"] = code;
            data["tier"] = tier;
            data["usedCenter"] = usedCenter;
            if (code == 0) {
                data["hitX"] = hit.X;
                data["hitY"] = hit.Y;
                data["hitZ"] = hit.Z;
            }
            return Renge::MakeResponse(id, data).dump();
        }

        if (cmd == Renge::CMD_TELEPORT_GET_MARKERS) {
            json arr = json::array();
            for (int i = 0; i < Grimoire::TELEPORT_SLOTS; ++i) {
                Wirbel::Marker m{};
                json jm;
                jm["slot"] = i;
                if (Wirbel::GetMarker(i, m) == 0) {
                    jm["valid"] = true;
                    jm["x"] = m.P.X;         jm["y"] = m.P.Y;     jm["z"] = m.P.Z;
                    jm["pitch"] = m.P.Pitch; jm["yaw"] = m.P.Yaw; jm["roll"] = m.P.Roll;
                    jm["map"] = m.MapName;
                } else {
                    jm["valid"] = false;
                }
                arr.push_back(jm);
            }
            // Append the system "last" slot as a sentinel entry (slot = -1) so
            // the UI refreshes it together with the real markers in one round
            // trip. It is auto-saved DLL-side before every jump and recalled
            // one-way (teleport_recall_last) — never user-saved/cleared.
            {
                Wirbel::Marker last{};
                json jl;
                jl["slot"] = -1;
                if (Wirbel::GetLast(last) == 0) {
                    jl["valid"] = true;
                    jl["x"] = last.P.X;         jl["y"] = last.P.Y;     jl["z"] = last.P.Z;
                    jl["pitch"] = last.P.Pitch; jl["yaw"] = last.P.Yaw; jl["roll"] = last.P.Roll;
                    jl["map"] = last.MapName;
                } else {
                    jl["valid"] = false;
                }
                arr.push_back(jl);
            }
            json data;
            data["markers"] = arr;
            return Renge::MakeResponse(id, data).dump();
        }

        if (cmd == Renge::CMD_TELEPORT_RECALL_LAST) {
            uint8_t tier = 0;
            int32_t code = Wirbel::RecallLast(&tier);
            Sein::Info("PIPE:cmd", "teleport_recall_last -> %d", code);
            json data;
            data["code"] = code;
            data["tier"] = tier;
            return Renge::MakeResponse(id, data).dump();
        }

        if (cmd == Renge::CMD_TELEPORT_CLEAR_MARKER) {
            int slot = request.value("slot", -1);
            int32_t code = Wirbel::ClearMarker(slot);
            json data;
            data["code"] = code;
            data["slot"] = slot;
            return Renge::MakeResponse(id, data).dump();
        }

        if (cmd == Renge::CMD_TELEPORT_GET_POV) {
            Wirbel::Pov pov{};
            int32_t code = Wirbel::GetPov(pov);
            json data;
            data["code"] = code;
            if (code == 0) {
                data["camX"] = pov.Cam.X;     data["camY"] = pov.Cam.Y;   data["camZ"] = pov.Cam.Z;
                data["pitch"] = pov.Cam.Pitch; data["yaw"] = pov.Cam.Yaw; data["roll"] = pov.Cam.Roll;
                data["fov"] = pov.Fov;
                data["hasPawn"] = pov.HasPawn;
                if (pov.HasPawn) {
                    data["pawnX"] = pov.Pawn.X; data["pawnY"] = pov.Pawn.Y; data["pawnZ"] = pov.Pawn.Z;
                }
                data["source"] = (pov.Source == 1) ? "raw" : "invoke";
                // "Locate in GWorld" for the cached POV fields (camera manager +
                // CameraCachePrivate.POV.Location / .FOV offsets).
                if (pov.CamOwnerAddr) {
                    data["cam_owner_addr"] = Renge::AddrToStr(pov.CamOwnerAddr);
                    // Full drillable struct path from the camera manager so the Live
                    // Walker can drill CameraCachePrivate → POV → leaf (the field is
                    // nested two struct levels deep, not a direct field).
                    if (pov.CamLocFieldOffset >= 0) {
                        data["cam_loc_field_offset"] = pov.CamLocFieldOffset;
                        data["cam_loc_field_name"]   = "CameraCachePrivate.POV.Location";
                    }
                    if (pov.CamRotFieldOffset >= 0) {
                        data["cam_rot_field_offset"] = pov.CamRotFieldOffset;
                        data["cam_rot_field_name"]   = "CameraCachePrivate.POV.Rotation";
                    }
                    if (pov.CamFovFieldOffset >= 0) {
                        data["cam_fov_field_offset"] = pov.CamFovFieldOffset;
                        data["cam_fov_field_name"]   = "CameraCachePrivate.POV.FOV";
                    }
                }
            }
            return Renge::MakeResponse(id, data).dump();
        }

        if (cmd == Renge::CMD_TELEPORT_RELATIVE) {
            double distance = request.value("distance", 0.0);
            bool horizontalOnly = request.value("horizontal", true);
            Wirbel::Pose p{};
            uint8_t tier = 0;
            int32_t code = Wirbel::TeleportRelative(distance, horizontalOnly, p, &tier);
            Sein::Info("PIPE:cmd", "teleport_relative: d=%.1f horiz=%d -> %d",
                       distance, horizontalOnly ? 1 : 0, code);
            json data;
            data["code"] = code;
            data["tier"] = tier;
            if (code == 0) {
                data["x"] = p.X;         data["y"] = p.Y;     data["z"] = p.Z;
                data["pitch"] = p.Pitch; data["yaw"] = p.Yaw; data["roll"] = p.Roll;
            }
            return Renge::MakeResponse(id, data).dump();
        }

        if (cmd == Renge::CMD_SET_MOUSE_CURSOR) {
            bool show = request.value("show", true);
            bool state = false;
            int32_t code = Wirbel::SetMouseCursor(show, &state);
            Sein::Info("PIPE:cmd", "set_mouse_cursor: show=%d -> %d (state=%d)",
                       show ? 1 : 0, code, state ? 1 : 0);
            json data;
            data["code"] = code;
            data["state"] = state;
            return Renge::MakeResponse(id, data).dump();
        }

        if (cmd == Renge::CMD_GET_MOUSE_CURSOR) {
            bool state = false;
            int32_t code = Wirbel::GetMouseCursor(&state);
            json data;
            data["code"] = code;
            if (code == 0) data["state"] = state;
            return Renge::MakeResponse(id, data).dump();
        }

        return Renge::MakeError(id, "Unknown command: " + cmd).dump();

    } catch (const std::exception& e) {
        Sein::Error("PIPE:cmd", "PipeServer: Exception in command '%s': %s", cmd.c_str(), e.what());
        return Renge::MakeError(id, std::string("Internal error: ") + e.what()).dump();
    } catch (...) {
        // Non-std::exception throw would otherwise escape to AcceptLoop ->
        // std::terminate. Cheap insurance against a game crash.
        Sein::Error("PIPE:cmd", "PipeServer: Non-standard exception in command '%s'", cmd.c_str());
        return Renge::MakeError(id, "Internal error (non-standard exception)").dump();
    }
}

// ============================================================
// RunScan — Background thread for initial scan (trigger_scan)
// ============================================================
void Fern::RunScan() {
    Sein::Info("PIPE:scan", "RunScan: started");
    // UE5_Init allocates heavily against a live game. The flags below MUST still be
    // cleared if it throws, or the UI waits on a scan that is never coming. (B14)
    Routine::RunThreadGuarded("RunScan", [] { UE5_Init(); });
    m_scan.completed = true;
    m_scan.running.store(false);
    Sein::Info("PIPE:scan", "RunScan: finished");
}

// ============================================================
// RunRescan — Background thread for aggressive pointer recovery
// ============================================================
void Fern::RunRescan(bool scanGObjects, bool scanGWorld) {
    // Guarded, and the `running` flag is cleared in BOTH outcomes: Genau's Extra Scan
    // allocates against a live process, and a throw here used to terminate the game AND
    // (had it not) would have left the UI waiting on a rescan that never finishes. (B14)
    Routine::RunThreadGuarded("RunRescan", [&] { RunRescanBody(scanGObjects, scanGWorld); });
    m_rescan.phase.store(3);
    m_rescan.running.store(false);
}

void Fern::RunRescanBody(bool scanGObjects, bool scanGWorld) {
    Sein::Info("PIPE:rescan", "RunRescan: started (GObjects=%d, GWorld=%d)",
                 scanGObjects, scanGWorld);

    if (scanGObjects) {
        m_rescan.phase.store(1);
        {
            std::lock_guard<std::mutex> lock(m_rescan.statusMutex);
            m_rescan.statusText = "Scanning GObjects (.data heuristic)...";
        }

        uintptr_t result = Genau::ExtraScanGObjects();
        if (result) {
            m_rescan.foundGObjects = result;
            m_rescan.gobjectsMethod = "data_heuristic";
            Sein::Info("PIPE:rescan", "RunRescan: GObjects found at 0x%llX",
                         static_cast<unsigned long long>(result));
        } else {
            Sein::Info("PIPE:rescan", "RunRescan: GObjects not found");
        }
    }

    if (scanGWorld) {
        m_rescan.phase.store(2);
        {
            std::lock_guard<std::mutex> lock(m_rescan.statusMutex);
            m_rescan.statusText = "Scanning GWorld (instance scan)...";
        }

        uintptr_t result = Genau::ExtraScanGWorld();
        if (result) {
            m_rescan.foundGWorld = result;
            m_rescan.gworldMethod = "instance_scan";
            Sein::Info("PIPE:rescan", "RunRescan: GWorld found at 0x%llX",
                         static_cast<unsigned long long>(result));
        } else {
            Sein::Info("PIPE:rescan", "RunRescan: GWorld not found");
        }
    }

    m_rescan.phase.store(3);
    {
        std::lock_guard<std::mutex> lock(m_rescan.statusMutex);
        m_rescan.statusText = "Complete";
    }
    m_rescan.running.store(false);

    Sein::Info("PIPE:rescan", "RunRescan: finished (foundGObj=0x%llX, foundGWld=0x%llX)",
                 static_cast<unsigned long long>(m_rescan.foundGObjects),
                 static_cast<unsigned long long>(m_rescan.foundGWorld));
}

void Fern::StartWatch(const std::shared_ptr<Connection>& conn, uintptr_t addr, uint32_t size, uint32_t interval_ms) {
    StopWatch(addr); // Stop existing watch on same address

    std::lock_guard<std::mutex> lock(m_watchMutex);

    auto entry = std::make_unique<WatchEntry>();
    entry->addr = addr;
    entry->size = size;
    entry->interval_ms = interval_ms;
    entry->owner = conn.get();   // event target; valid until this conn's watches
                                 // are stopped+joined in HandleConnection cleanup
    entry->active = true;

    WatchEntry* ptr = entry.get();
    entry->watchThread = std::thread([this, ptr]() {
      // Fully unguarded before B14's scope correction, and it allocates on every tick:
      // the buffer, BytesToHex's string, the json object, and dump(). A bad_alloc here
      // terminated the game.
      Routine::RunThreadGuarded("PipeServer: watch", [&] {
        std::vector<uint8_t> buf(ptr->size);
        while (ptr->active.load() && m_running.load()) {
            if (Macht::ReadBytesSafe(ptr->addr, buf.data(), ptr->size)) {
                json data;
                data["addr"]      = Renge::AddrToStr(ptr->addr);
                data["bytes"]     = Renge::BytesToHex(buf.data(), buf.size());
                data["timestamp"] = std::chrono::duration_cast<std::chrono::milliseconds>(
                    std::chrono::system_clock::now().time_since_epoch()).count();

                // Write the event to the connection that registered this watch
                // (the interactive lane). WriteLine no-ops if that connection
                // closed; the watch is stopped on its connection's disconnect.
                if (ptr->owner)
                    WriteLine(*ptr->owner, Renge::MakeEvent(Renge::EVT_WATCH, data).dump());
            }
            std::this_thread::sleep_for(std::chrono::milliseconds(ptr->interval_ms));
        }
      });
    });

    m_watches[addr] = std::move(entry);
    Sein::Info("PIPE:watch", "PipeServer: Watch started on 0x%llX (size=%u, interval=%ums)",
             static_cast<unsigned long long>(addr), size, interval_ms);
}

// Stop + join every watch registered by a given connection. Called in that
// connection's HandleConnection cleanup BEFORE the Connection is closed/freed,
// so the watch threads (which hold a raw owner pointer) never outlive it.
void Fern::StopWatchesForConnection(Connection* owner) {
    std::vector<std::unique_ptr<WatchEntry>> toJoin;
    {
        std::lock_guard<std::mutex> lock(m_watchMutex);
        for (auto it = m_watches.begin(); it != m_watches.end(); ) {
            if (it->second->owner == owner) {
                it->second->active = false;
                toJoin.push_back(std::move(it->second));
                it = m_watches.erase(it);
            } else {
                ++it;
            }
        }
    }
    for (auto& entry : toJoin) {
        if (entry && entry->watchThread.joinable()) entry->watchThread.join();
    }
}

void Fern::StopWatch(uintptr_t addr) {
    // Audit fix #4: extract the entry under m_watchMutex but join the thread
    // OUTSIDE the lock. The watch thread writes events via WriteLine (the
    // connection's writeMutex); if we held m_watchMutex while joining and
    // another thread took both locks in the opposite order, we'd deadlock.
    // Safer to release m_watchMutex before the blocking join.
    std::unique_ptr<WatchEntry> entry;
    {
        std::lock_guard<std::mutex> lock(m_watchMutex);
        auto it = m_watches.find(addr);
        if (it == m_watches.end()) return;
        it->second->active = false;
        entry = std::move(it->second);
        m_watches.erase(it);
    }

    if (entry && entry->watchThread.joinable()) {
        entry->watchThread.join();
    }
    Sein::Info("PIPE:watch", "PipeServer: Watch stopped on 0x%llX",
             static_cast<unsigned long long>(addr));
}

void Fern::StopAllWatches() {
    // Audit fix #4: same pattern as StopWatch — extract under lock, join
    // afterwards. Set every entry's active=false first so all watch threads
    // start exiting in parallel; then drain the map and join each.
    std::vector<std::unique_ptr<WatchEntry>> toJoin;
    {
        std::lock_guard<std::mutex> lock(m_watchMutex);
        toJoin.reserve(m_watches.size());
        for (auto& [addr, entry] : m_watches) {
            entry->active = false;
        }
        for (auto& [addr, entry] : m_watches) {
            toJoin.push_back(std::move(entry));
        }
        m_watches.clear();
    }

    for (auto& entry : toJoin) {
        if (entry && entry->watchThread.joinable()) {
            entry->watchThread.join();
        }
    }
}
