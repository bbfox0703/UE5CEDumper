// ============================================================
// Sense — Diagnostics: self-health telemetry. See Sense.h for the why.
// ============================================================

#include "Sense.h"
#include "Sein.h"

#include <Windows.h>
#include <psapi.h>
#include <tlhelp32.h>

#include <algorithm>
#include <mutex>
#include <unordered_map>

namespace Sense {
namespace {

// A DEDICATED mutex, not a shared one: this is touched on every pipe command,
// and borrowing a lock that a long scan also holds would make the diagnostics
// contend with exactly the thing they exist to measure.
std::mutex                                  g_mutex;
std::unordered_map<std::string, CommandStat> g_stats;
uint64_t                                    g_totalDispatches = 0;
uint64_t                                    g_totalBusyUs     = 0;
uint64_t                                    g_startTick       = GetTickCount64();

// ── CPU% differencing state (guarded by g_cpuMutex, separate from g_mutex so a
// Tier 2 sample never blocks the Tier 1 hot path) ──
std::mutex g_cpuMutex;
uint64_t   g_lastCpuSampleTick = 0;   // GetTickCount64 at the previous sample
uint64_t   g_lastCpu100ns      = 0;   // kernel+user, 100ns units

// QPC frequency is fixed for the process lifetime, so read it once.
LARGE_INTEGER QpcFreq() {
    static LARGE_INTEGER f = [] { LARGE_INTEGER q{}; QueryPerformanceFrequency(&q); return q; }();
    return f;
}

uint64_t FileTimeTo100ns(const FILETIME& ft) {
    ULARGE_INTEGER u;
    u.LowPart  = ft.dwLowDateTime;
    u.HighPart = ft.dwHighDateTime;
    return u.QuadPart;
}

// Thread count for THIS process. Walks a system-wide thread snapshot (there is
// no cheaper documented API), which is why SampleProcess is on demand only.
// Returns 0 on failure — a missing diagnostic is not worth an error path.
uint32_t CountOwnThreads() {
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0);
    if (snap == INVALID_HANDLE_VALUE) return 0;

    uint32_t n = 0;
    const DWORD pid = GetCurrentProcessId();
    THREADENTRY32 te{};
    te.dwSize = sizeof(te);
    if (Thread32First(snap, &te)) {
        do {
            // dwSize is a "how much did the OS fill in" contract: the owner PID
            // field is only valid when the entry is large enough to contain it.
            if (te.dwSize >= FIELD_OFFSET(THREADENTRY32, th32OwnerProcessID)
                             + sizeof(te.th32OwnerProcessID)
                && te.th32OwnerProcessID == pid) {
                ++n;
            }
            te.dwSize = sizeof(te);
        } while (Thread32Next(snap, &te));
    }
    CloseHandle(snap);
    return n;
}

} // namespace

uint64_t NowTicks() {
    LARGE_INTEGER t{};
    QueryPerformanceCounter(&t);
    return static_cast<uint64_t>(t.QuadPart);
}

uint64_t TicksToUs(uint64_t deltaTicks) {
    const uint64_t f = static_cast<uint64_t>(QpcFreq().QuadPart);
    if (f == 0) return 0;
    // Scale before dividing: dividing first would floor every sub-second delta to zero.
    return (deltaTicks * 1000000ULL) / f;
}

void RecordDispatch(const std::string& cmd, uint64_t elapsedUs) {
    std::lock_guard<std::mutex> lock(g_mutex);
    auto& s = g_stats[cmd];
    if (s.cmd.empty()) s.cmd = cmd;
    s.count   += 1;
    s.totalUs += elapsedUs;
    s.lastUs   = elapsedUs;
    if (elapsedUs > s.maxUs) s.maxUs = elapsedUs;

    g_totalDispatches += 1;
    g_totalBusyUs     += elapsedUs;
}

std::vector<CommandStat> TopCommands(size_t limit) {
    std::vector<CommandStat> out;
    {
        std::lock_guard<std::mutex> lock(g_mutex);
        out.reserve(g_stats.size());
        for (const auto& kv : g_stats) out.push_back(kv.second);
    }
    // Heaviest TOTAL first: the question is which command OWNS the dispatcher.
    // Tie-break on max then name so the order is stable between polls (an
    // unstable order makes a live panel jitter for no reason).
    std::sort(out.begin(), out.end(), [](const CommandStat& a, const CommandStat& b) {
        if (a.totalUs != b.totalUs) return a.totalUs > b.totalUs;
        if (a.maxUs   != b.maxUs)   return a.maxUs   > b.maxUs;
        return a.cmd < b.cmd;
    });
    if (limit > 0 && out.size() > limit) out.resize(limit);
    return out;
}

uint64_t TotalDispatches() {
    std::lock_guard<std::mutex> lock(g_mutex);
    return g_totalDispatches;
}

uint64_t TotalBusyUs() {
    std::lock_guard<std::mutex> lock(g_mutex);
    return g_totalBusyUs;
}

uint64_t UptimeMs() {
    std::lock_guard<std::mutex> lock(g_mutex);
    uint64_t now = GetTickCount64();
    return (now >= g_startTick) ? (now - g_startTick) : 0;
}

void Reset() {
    {
        std::lock_guard<std::mutex> lock(g_mutex);
        g_stats.clear();
        g_totalDispatches = 0;
        g_totalBusyUs     = 0;
        g_startTick       = GetTickCount64();
    }
    // Drop the CPU baseline too, so the first sample of the next session reports
    // "unknown" rather than averaging across the gap.
    std::lock_guard<std::mutex> lock(g_cpuMutex);
    g_lastCpuSampleTick = 0;
    g_lastCpu100ns      = 0;
    Sein::Info("SENSE", "Diagnostics counters reset");
}

ProcessStat SampleProcess() {
    ProcessStat st{};
    HANDLE self = GetCurrentProcess();

    PROCESS_MEMORY_COUNTERS_EX pmc{};
    pmc.cb = sizeof(pmc);
    if (GetProcessMemoryInfo(self, reinterpret_cast<PROCESS_MEMORY_COUNTERS*>(&pmc), sizeof(pmc))) {
        st.workingSetBytes = pmc.WorkingSetSize;
        st.privateBytes    = pmc.PrivateUsage;
        st.peakWorkingSet  = pmc.PeakWorkingSetSize;
    }

    DWORD handles = 0;
    if (GetProcessHandleCount(self, &handles)) st.handleCount = handles;

    st.threadCount = CountOwnThreads();

    FILETIME create{}, exit{}, kernel{}, user{};
    if (GetProcessTimes(self, &create, &exit, &kernel, &user)) {
        const uint64_t cpu100ns = FileTimeTo100ns(kernel) + FileTimeTo100ns(user);
        const uint64_t nowTick  = GetTickCount64();

        std::lock_guard<std::mutex> lock(g_cpuMutex);
        if (g_lastCpuSampleTick != 0 && nowTick > g_lastCpuSampleTick
            && cpu100ns >= g_lastCpu100ns) {
            const double wallMs = double(nowTick - g_lastCpuSampleTick);
            const double cpuMs  = double(cpu100ns - g_lastCpu100ns) / 10000.0;
            // Normalise by core count so 100% means "the whole machine", which is
            // what a user comparing against Task Manager expects.
            DWORD cores = 0;
            SYSTEM_INFO si{};
            GetSystemInfo(&si);
            cores = si.dwNumberOfProcessors ? si.dwNumberOfProcessors : 1;
            st.cpuPercent = (cpuMs / (wallMs * double(cores))) * 100.0;
            if (st.cpuPercent < 0.0) st.cpuPercent = 0.0;
        }
        g_lastCpuSampleTick = nowTick;
        g_lastCpu100ns      = cpu100ns;
    }

    return st;
}

} // namespace Sense
