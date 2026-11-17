// ============================================================
// Land — 蘭特 (完美分身魔法 — Flawless Clones)
// SecondCopy, the runtime half: find the other copies of the dumper in this process, retire the ones that started, and
// serve their CE mailboxes; and this copy's own hand-off when a later copy retires it. The decisions are Land.h's.
// ============================================================

#include "Land.h"

#include "BuildStamp.h"
#include "Grimoire.h"
#include "Macht.h"
#include "Mimic.h"
#include "Routine.h"
#include "Sein.h"

#include <atomic>
#include <mutex>
#include <string>
#include <vector>

#include <Winver.h>   // GetFileVersionInfoW / VerQueryValueW

extern HMODULE g_hDllModule;   // Heiter.cpp: this image

namespace Land {
namespace {

std::atomic<int32_t> s_state{static_cast<int32_t>(YieldState::None)};
std::atomic<HMODULE> s_successor{nullptr};

// One retirement pass at a time; s_retired is the copies already handled, so a later pass (this copy's pipe server
// started again after a CE untick, say) neither shuts one down twice nor reads the state this copy now mirrors into
// its mailbox as a copy that started.
std::mutex s_retireMutex;
HMODULE s_retired[kMaxCopies] = {};

bool IsRetired(HMODULE h) {
    for (HMODULE r : s_retired)
        if (r == h) return true;
    return false;
}

void MarkRetired(HMODULE h) {
    for (HMODULE& r : s_retired)
        if (!r) { r = h; return; }
}

std::string PathOf(HMODULE h) {
    wchar_t w[MAX_PATH] = {};
    if (!GetModuleFileNameW(h, w, MAX_PATH)) return "(unknown path)";
    char a[MAX_PATH * 3] = {};
    WideCharToMultiByte(CP_UTF8, 0, w, -1, a, static_cast<int>(sizeof(a)), nullptr, nullptr);
    return a;
}

// The build number a copy carries: its VERSIONINFO's fourth FILEVERSION part (version.rc), 0 when unreadable.
int BuildOf(HMODULE h) {
    wchar_t path[MAX_PATH] = {};
    if (!GetModuleFileNameW(h, path, MAX_PATH)) return 0;
    DWORD handle = 0;
    const DWORD size = GetFileVersionInfoSizeW(path, &handle);
    if (!size) return 0;
    std::vector<uint8_t> buf(size);
    if (!GetFileVersionInfoW(path, handle, size, buf.data())) return 0;
    VS_FIXEDFILEINFO* info = nullptr;
    UINT len = 0;
    if (!VerQueryValueW(buf.data(), L"\\", reinterpret_cast<void**>(&info), &len) || !info) return 0;
    return static_cast<int>(LOWORD(info->dwFileVersionLS));
}

// Read another copy's contract block and mailbox state through its own exports.
CopySeen Probe(HMODULE h, Mimic::MailboxData*& box) {
    CopySeen seen;
    box = nullptr;
    if (const FARPROC c = GetProcAddress(h, Grimoire::DUMPER_IDENTITY_EXPORT)) {
        Mimic::MailboxContract mc{};
        if (Macht::ReadSafe(reinterpret_cast<uintptr_t>(c), mc) && mc.magic == Mimic::MAILBOX_CONTRACT_MAGIC) {
            seen.contract = true;
            seen.current = mc.current;
        }
    }
    if (const FARPROC m = GetProcAddress(h, "g_invokeMailbox")) {
        seen.mailbox = true;
        box = reinterpret_cast<Mimic::MailboxData*>(m);
        int32_t state = 0;
        if (seen.contract && Macht::ReadSafe(reinterpret_cast<uintptr_t>(&box->initState), state))
            seen.initState = state;
    }
    return seen;
}

// The other copies of the dumper in this process, this image excluded.
std::vector<HMODULE> OtherCopies() {
    std::vector<HMODULE> out;
    for (const auto& m : Macht::GetLoadedModules())
        if (m.hModule != g_hDllModule && Macht::IsDumperModule(m.hModule)) out.push_back(m.hModule);
    return out;
}

// The earlier copy's shutdown, run on its own thread so this copy can stop waiting: an old build's pipe stop could
// block until a client arrived, and a scan's joins are bounded only by its cancel polls. The thread owns the call.
struct RetireCall {
    FARPROC yield    = nullptr;   // UE5_YieldToLaterCopy(HMODULE), when the copy exports it
    FARPROC shutdown = nullptr;   // UE5_Shutdown(), every build
    HMODULE self     = nullptr;
};

DWORD WINAPI RetireThread(LPVOID param) {
    auto* call = static_cast<RetireCall*>(param);
    Routine::RunThreadGuarded("Land.Retire", [call] {
        if (call->yield)
            reinterpret_cast<void (*)(HMODULE)>(call->yield)(call->self);
        else
            reinterpret_cast<void (*)()>(call->shutdown)();
    });
    delete call;
    return 0;
}

} // namespace

YieldState State() {
    return static_cast<YieldState>(s_state.load(std::memory_order_acquire));
}

bool AnyCopyToRetire() {
    if (RefusesStart(State())) return false;
    std::lock_guard<std::mutex> lock(s_retireMutex);
    for (HMODULE h : OtherCopies()) {
        if (IsRetired(h)) continue;
        Mimic::MailboxData* box = nullptr;
        if (Judge(Probe(h, box)) == Verdict::Retire) return true;
    }
    return false;
}

void RetireEarlierCopies() {
    if (RefusesStart(State())) return;
    std::lock_guard<std::mutex> lock(s_retireMutex);
    const int ownBuild = BuildStamp::BuildNumber();
    for (HMODULE h : OtherCopies()) {
        if (IsRetired(h)) continue;
        Mimic::MailboxData* box = nullptr;
        const CopySeen seen = Probe(h, box);
        const std::string path = PathOf(h);
        const int build = BuildOf(h);
        if (Judge(seen) == Verdict::Leave) {
            LOG_INFO("Land: another copy of the dumper is loaded at %s (build %d) and is left as it is: %s", path.c_str(),
                     build, !seen.contract ? "it predates the mailbox contract, so its state cannot be read"
                                           : "it has not started (IDLE)");
            continue;   // judged again on this copy's next start: an IDLE copy CE starts later is retired then
        }

        RetireCall* call = new RetireCall;
        call->yield = GetProcAddress(h, "UE5_YieldToLaterCopy");
        call->shutdown = GetProcAddress(h, "UE5_Shutdown");
        call->self = g_hDllModule;
        if (!call->yield && !call->shutdown) {
            LOG_WARN("Land: the copy at %s (build %d) exports no shutdown -- left running beside this one", path.c_str(),
                     build);
            delete call;
            MarkRetired(h);
            continue;
        }
        LOG_WARN("Land: taking over from the earlier copy at %s (build %d, contract %d, initState %d); this copy is "
                 "build %d -- the later copy takes over", path.c_str(), build, seen.current, seen.initState, ownBuild);
        if (build > ownBuild)
            LOG_WARN("Land: that copy is NEWER than this one (build %d > %d); taking over anyway, as the later copy "
                     "always does", build, ownBuild);

        const bool yields = call->yield != nullptr;
        const ULONGLONG t0 = GetTickCount64();
        HANDLE th = CreateThread(nullptr, 0, RetireThread, call, 0, nullptr);
        if (!th) {
            LOG_ERROR("Land: could not start the thread that shuts the earlier copy down (error=%lu) -- left running",
                      GetLastError());
            delete call;
            MarkRetired(h);
            continue;
        }
        const DWORD waited = WaitForSingleObject(th, kRetireWaitMs);
        CloseHandle(th);
        MarkRetired(h);
        if (waited != WAIT_OBJECT_0) {
            LOG_WARN("Land: the earlier copy did not finish shutting down within %lu ms -- its mailbox is left to it, "
                     "and this copy takes the pipe once it lets go", static_cast<unsigned long>(kRetireWaitMs));
            continue;
        }
        const unsigned long ms = static_cast<unsigned long>(GetTickCount64() - t0);

        const bool served = box && CanServeMailbox(seen.current, Mimic::MAILBOX_CONTRACT) && Mimic::AdoptMailbox(box);
        if (!served && box) {
            const int32_t skipped = PublishedState(false, 0);
            if (!Macht::WriteDataBytes(reinterpret_cast<uintptr_t>(&box->initState), &skipped, sizeof(skipped)))
                LOG_WARN("Land: could not mark the earlier copy's mailbox SKIPPED");
        }
        LOG_INFO("Land: the earlier copy %s in %lu ms; its mailbox %s (its contract %d, this copy's %d)",
                 yields ? "yielded" : "shut down", ms,
                 served ? "is served by this copy" : "says SKIPPED -- a script that resolved it is told another copy "
                                                     "serves this process",
                 seen.current, Mimic::MAILBOX_CONTRACT);
    }
}

bool BeginYield(HMODULE successor) {
    int32_t expected = static_cast<int32_t>(YieldState::None);
    if (!s_state.compare_exchange_strong(expected, static_cast<int32_t>(YieldState::Yielding),
                                         std::memory_order_acq_rel))
        return false;
    s_successor.store(successor, std::memory_order_release);
    return true;
}

void FinishYield() {
    s_state.store(static_cast<int32_t>(YieldState::Yielded), std::memory_order_release);
    LOG_WARN("Land: this copy has handed the process to the copy at %s -- its exports run there from now on",
             PathOf(s_successor.load(std::memory_order_acquire)).c_str());
}

FARPROC ForwardTarget(const char* exportName) {
    if (!Forwards(State())) return nullptr;
    const HMODULE successor = s_successor.load(std::memory_order_acquire);
    return successor ? GetProcAddress(successor, exportName) : nullptr;
}

} // namespace Land
