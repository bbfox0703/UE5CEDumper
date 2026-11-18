// ============================================================
// Land — 蘭特 (完美分身魔法 — Flawless Clones)
// SecondCopy, the runtime half: find the other copies of the dumper in this process, take over from the earlier ones
// that started and serve their CE mailboxes; and this copy's own hand-off when a later copy takes over from it. The
// decisions are Land.h's.
// ============================================================

#include "Land.h"

#include "BuildStamp.h"
#include "Grimoire.h"
#include "Macht.h"
#include "Mimic.h"
#include "Routine.h"
#include "Schlacht.h"
#include "Sein.h"
#include "Tot.h"

#include <algorithm>
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
std::mutex s_startMutex;
std::atomic<int> s_retiresInFlight{0};
thread_local int t_forwardHops = 0;

// One pass at a time in this copy; s_retired is the copies this copy already took over (or started to), so a later
// pass -- this copy's pipe started again after a CE untick, say -- does not hand one over twice or read the state this
// copy now mirrors into its mailbox as a copy that started. Cleared nowhere: a copy once taken over stays taken over.
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
    if (!h || !GetModuleFileNameW(h, w, MAX_PATH)) return "(unknown path)";
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
CopySeen Probe(HMODULE h, Mimic::MailboxData*& box, Mimic::MailboxContract*& contract) {
    CopySeen seen;
    box = nullptr;
    contract = nullptr;
    if (const FARPROC c = GetProcAddress(h, Grimoire::DUMPER_IDENTITY_EXPORT)) {
        Mimic::MailboxContract mc{};
        if (Macht::ReadSafe(reinterpret_cast<uintptr_t>(c), mc) && mc.magic == Mimic::MAILBOX_CONTRACT_MAGIC) {
            seen.contract = true;
            seen.current = mc.current;
            seen.minimum = mc.minimum;
            contract = reinterpret_cast<Mimic::MailboxContract*>(c);
        }
    }
    if (const FARPROC m = GetProcAddress(h, Grimoire::DUMPER_MAILBOX_EXPORT)) {
        seen.mailbox = true;
        box = reinterpret_cast<Mimic::MailboxData*>(m);
        int32_t state = 0;
        if (seen.contract && Macht::ReadSafe(reinterpret_cast<uintptr_t>(&box->initState), state))
            seen.initState = state;
    }
    return seen;
}

// Every copy of the dumper in this process, this one included, in the loader's order. EnumProcessModules lists the
// loader's in-load-order module list, so a copy injected later comes after a proxy the game loaded at start.
std::vector<HMODULE> CopiesInLoadOrder() {
    std::vector<HMODULE> out;
    for (const auto& m : Macht::GetLoadedModules())
        if (m.hModule == g_hDllModule || Macht::IsDumperModule(m.hModule)) out.push_back(m.hModule);
    return out;
}

int IndexOf(const std::vector<HMODULE>& copies, HMODULE h) {
    for (size_t i = 0; i < copies.size(); ++i)
        if (copies[i] == h) return static_cast<int>(i);
    return -1;
}

// A copy loaded after this one that has started serves this process: this copy takes over from nothing then. It runs
// beside that copy, as two copies did before Land, until that copy's own pass or a later one decides.
bool LaterCopyStarted(const std::vector<HMODULE>& copies, int own, HMODULE& which) {
    for (int i = own + 1; i < static_cast<int>(copies.size()); ++i) {
        Mimic::MailboxData* box = nullptr;
        Mimic::MailboxContract* contract = nullptr;
        if (Judge(Probe(copies[i], box, contract)) == Verdict::Retire) { which = copies[i]; return true; }
    }
    return false;
}

// Serves the earlier copy's mailbox here, or marks it as served elsewhere and makes its contract block refuse. Runs on
// the hand-off's own thread once the earlier copy's shutdown has returned, however long the pass waited.
void TakeMailbox(const CopySeen& seen, Mimic::MailboxData* box, Mimic::MailboxContract* contract, bool forwards) {
    if (!box) return;
    // A copy that cannot forward was started again on its own between its shutdown and here (CE's UE5_AutoStart reached
    // it; its shutdown left IDLE): it polls this mailbox itself now, and a second poller would run every command twice
    // (review R2-OLD-COPY-DOUBLE-SERVE). It runs beside this copy, as before Land.
    if (!forwards) {
        int32_t now = Mimic::INIT_IDLE;
        if (Macht::ReadSafe(reinterpret_cast<uintptr_t>(&box->initState), now) && now != Mimic::INIT_IDLE) {
            LOG_WARN("Land: the earlier copy was started again on its own (initState %d) -- it serves its mailbox "
                     "itself, beside this copy", now);
            return;
        }
    }
    const bool layoutOk = CanServeMailbox(seen.current, seen.minimum, Mimic::MAILBOX_CONTRACT,
                                          Mimic::MAILBOX_CONTRACT_MIN, Mimic::MAILBOX_LAYOUT_SINCE);
    if (layoutOk && Mimic::AdoptMailbox(box, forwards)) {
        LOG_INFO("Land: the earlier copy's mailbox is served by this copy (its contract %d, min %d; this copy's %d, "
                 "min %d)", seen.current, seen.minimum, Mimic::MAILBOX_CONTRACT, Mimic::MAILBOX_CONTRACT_MIN);
        return;
    }
    const int32_t skipped = PublishedState(false, 0);
    const bool stateWritten =
        Macht::WriteDataBytes(reinterpret_cast<uintptr_t>(&box->initState), &skipped, sizeof(skipped));
    // A feature script checks the contract block before it writes a command; without the magic it refuses at once
    // ("resolved to the wrong memory") instead of waiting out a command nobody polls. The copy is still recognised as
    // ours by its legacy identity export (Macht::IsDumperModule).
    const uint32_t noMagic = 0;
    const bool magicCleared = contract && Macht::WriteDataBytes(reinterpret_cast<uintptr_t>(&contract->magic),
                                                                &noMagic, sizeof(noMagic));
    LOG_WARN("Land: the earlier copy's mailbox cannot be served by this copy (its contract %d, min %d; this copy "
             "accepts %d..%d from layout %d) -- it says SKIPPED%s, and a CE script that resolved it %s",
             seen.current, seen.minimum, Mimic::MAILBOX_CONTRACT_MIN, Mimic::MAILBOX_CONTRACT,
             Mimic::MAILBOX_LAYOUT_SINCE, stateWritten ? "" : " (the write FAILED)",
             magicCleared ? "is refused at its contract check" : "times out (its contract block could not be cleared)");
}

// One earlier copy's hand-off, on its own thread so the pass can stop waiting: the thread owns the call and takes the
// mailbox over when the earlier copy's shutdown returns, however late.
struct RetireCall {
    FARPROC                  yield    = nullptr;   // UE5_YieldToLaterCopy(HMODULE), when the copy exports it
    FARPROC                  shutdown = nullptr;   // UE5_Shutdown(), every build
    FARPROC                  hidden   = nullptr;   // UE5_HandOverHiddenActors(uint64_t*, int32_t), when exported
    HMODULE                  self     = nullptr;
    CopySeen                 seen;
    Mimic::MailboxData*      box      = nullptr;
    Mimic::MailboxContract*  contract = nullptr;
};

// What one ask came to: the earlier copy answered (Done), has not handed over yet (NotYet), or this copy is itself
// being taken over and left the record where it is (Left).
enum class HiddenAsk { Done, NotYet, Left };

// [SEETHRU-HANDOFF-LEFTOVER] The actors the earlier copy's See-through left hidden become this copy's: that copy can
// never run another un-hide. Under the start lock BeginYield takes, and only while this copy is not being taken over: a
// copy that hands over never serves its record again, so the actors stay with the earlier copy for the copy taking over
// to ask (review wf_bbd1bbfe-20d D7-1). Bounded, so a copy that kept answering could not hold the hand-off.
HiddenAsk AdoptHiddenActors(FARPROC handOver) {
    if (!handOver) return HiddenAsk::Done;
    std::vector<uint64_t> batch(static_cast<size_t>(Grimoire::LAND_HIDDEN_BATCH));
    int32_t total = 0;
    HiddenAsk outcome = HiddenAsk::Done;
    {
        const auto start = LockStart();
        if (RefusesStart(State())) return HiddenAsk::Left;
        for (int round = 0; round < 16; ++round) {
            const int32_t n = reinterpret_cast<int32_t (*)(uint64_t*, int32_t)>(handOver)(batch.data(),
                                                                                         Grimoire::LAND_HIDDEN_BATCH);
            if (n < 0) {   // the earlier copy has not handed over yet
                if (round == 0) outcome = HiddenAsk::NotYet;
                break;
            }
            if (n == 0) break;
            Schlacht::AdoptLeftovers(batch.data(), (std::min)(n, Grimoire::LAND_HIDDEN_BATCH));
            total += n;
            if (n < Grimoire::LAND_HIDDEN_BATCH) break;
        }
    }
    if (total > 0)
        LOG_WARN("Land: the earlier copy's See-through left %d actor(s) hidden -- this copy holds them now, and its next "
                 "See-through ON or OFF restores them", total);
    return outcome;
}

// After "yielded" the earlier copy answers at once. After "already handing over" it is still handing over -- to a copy
// this one took over or is taking over -- so it is asked again until it has, bounded (review wf_bbd1bbfe-20d D7-1).
void PollHiddenActors(FARPROC handOver) {
    const ULONGLONG t0 = GetTickCount64();
    while (AdoptHiddenActors(handOver) == HiddenAsk::NotYet) {
        if (GetTickCount64() - t0 >= static_cast<ULONGLONG>(Grimoire::LAND_HIDDEN_WAIT_MS) || Tot::ShutdownRequested()) {
            LOG_WARN("Land: the earlier copy had not handed over after %llu ms -- the actors its See-through left hidden, "
                     "if any, stay with it", GetTickCount64() - t0);
            return;
        }
        Sleep(Grimoire::LAND_HIDDEN_POLL_MS);
    }
}

DWORD WINAPI RetireThread(LPVOID param) {
    auto* call = static_cast<RetireCall*>(param);
    Routine::RunThreadGuarded("Land.Retire", [call] {
        const ULONGLONG t0 = GetTickCount64();
        if (call->yield) {
            const int32_t reply = reinterpret_cast<int32_t (*)(HMODULE)>(call->yield)(call->self);
            if (reply == YIELD_NOT_LATER) {
                LOG_WARN("Land: the earlier copy refused the hand-off -- it does not see this copy as loaded after it; "
                         "both keep running");
                return;
            }
            LOG_INFO("Land: the earlier copy %s in %llu ms", reply == YIELD_DONE ? "yielded" : "had already handed over",
                     GetTickCount64() - t0);
            PollHiddenActors(call->hidden);
        } else {
            reinterpret_cast<void (*)()>(call->shutdown)();
            LOG_INFO("Land: the earlier copy shut down in %llu ms", GetTickCount64() - t0);
        }
        // Taken over by a later copy meanwhile: that copy takes this mailbox too (it is earlier than that copy as well).
        if (RefusesStart(State())) {
            LOG_INFO("Land: this copy is itself being taken over -- the earlier copy's mailbox is left to the copy "
                     "taking over");
            return;
        }
        TakeMailbox(call->seen, call->box, call->contract, call->yield != nullptr);
    });
    delete call;
    s_retiresInFlight.fetch_sub(1, std::memory_order_acq_rel);
    return 0;
}

// The process-wide pass lock (Grimoire::LAND_PASS_MUTEX_FORMAT). Null when it cannot be had: the pass then runs alone in
// this copy, which is what every copy did before the lock existed.
struct PassLock {
    HANDLE h = nullptr;
    bool held = false;
    PassLock() {
        wchar_t name[64] = {};
        swprintf_s(name, Grimoire::LAND_PASS_MUTEX_FORMAT, static_cast<unsigned long>(GetCurrentProcessId()));
        h = CreateMutexW(nullptr, FALSE, name);
        if (!h) return;
        const DWORD w = WaitForSingleObject(h, kRetireWaitMs);
        held = (w == WAIT_OBJECT_0 || w == WAIT_ABANDONED);
    }
    ~PassLock() {
        if (held) ReleaseMutex(h);
        if (h) CloseHandle(h);
    }
    PassLock(const PassLock&) = delete;
    PassLock& operator=(const PassLock&) = delete;
};

} // namespace

YieldState State() {
    return static_cast<YieldState>(s_state.load(std::memory_order_acquire));
}

bool RetireInFlight() {
    return s_retiresInFlight.load(std::memory_order_acquire) > 0;
}

std::unique_lock<std::mutex> LockStart() {
    return std::unique_lock<std::mutex>(s_startMutex);
}

bool AnyCopyToRetire() {
    if (RefusesStart(State())) return false;
    std::lock_guard<std::mutex> lock(s_retireMutex);
    const auto copies = CopiesInLoadOrder();
    const int own = IndexOf(copies, g_hDllModule);
    HMODULE later = nullptr;
    if (own < 0 || LaterCopyStarted(copies, own, later)) return false;
    for (int i = own - 1; i >= 0; --i) {
        if (IsRetired(copies[i])) continue;
        Mimic::MailboxData* box = nullptr;
        Mimic::MailboxContract* contract = nullptr;
        if (Judge(Probe(copies[i], box, contract)) == Verdict::Retire) return true;
    }
    return false;
}

void RetireEarlierCopies() {
    if (RefusesStart(State())) return;
    const PassLock passLock;
    if (!passLock.held)
        LOG_WARN("Land: the process-wide take-over lock could not be had -- this copy's pass runs without it");
    std::lock_guard<std::mutex> lock(s_retireMutex);
    if (RefusesStart(State())) return;   // taken over while waiting for the lock

    const auto copies = CopiesInLoadOrder();
    const int own = IndexOf(copies, g_hDllModule);
    if (own < 0) {
        LOG_WARN("Land: this copy is not in the process's module list -- taking over from nothing");
        return;
    }
    HMODULE later = nullptr;
    if (LaterCopyStarted(copies, own, later)) {
        LOG_INFO("Land: a copy loaded after this one has started (%s) -- it serves this process; this copy takes over "
                 "from nothing", PathOf(later).c_str());
        return;
    }
    const int ownBuild = BuildStamp::BuildNumber();
    // Latest first: a copy that serves an earlier copy's mailbox is stopped before that mailbox is taken.
    for (int i = own - 1; i >= 0; --i) {
        const HMODULE h = copies[i];
        if (IsRetired(h)) continue;
        Mimic::MailboxData* box = nullptr;
        Mimic::MailboxContract* contract = nullptr;
        const CopySeen seen = Probe(h, box, contract);
        const std::string path = PathOf(h);
        const int build = BuildOf(h);
        if (Judge(seen) == Verdict::Leave) {
            // Judged again on this copy's next pass; a copy CE revives after this copy serves runs beside it.
            LOG_INFO("Land: another copy of the dumper is loaded at %s (build %d) and is left as it is: %s",
                     path.c_str(), build,
                     !seen.contract ? "it predates the mailbox contract, so its state cannot be read"
                                    : "it has not started (IDLE)");
            continue;
        }

        auto* call = new RetireCall;
        call->yield = GetProcAddress(h, Grimoire::DUMPER_YIELD_EXPORT);
        call->shutdown = GetProcAddress(h, Grimoire::DUMPER_SHUTDOWN_EXPORT);
        call->hidden = GetProcAddress(h, Grimoire::DUMPER_HAND_OVER_HIDDEN_EXPORT);
        call->self = g_hDllModule;
        call->seen = seen;
        call->box = box;
        call->contract = contract;
        if (!call->yield && !call->shutdown) {
            LOG_WARN("Land: the copy at %s (build %d) exports no shutdown -- left running beside this one",
                     path.c_str(), build);
            delete call;
            MarkRetired(h);
            continue;
        }
        LOG_WARN("Land: taking over from the earlier copy at %s (build %d, contract %d, initState %d); this copy is "
                 "build %d -- the later copy takes over", path.c_str(), build, seen.current, seen.initState, ownBuild);
        if (build > ownBuild)
            LOG_WARN("Land: that copy is NEWER than this one (build %d > %d); taking over anyway, as the later copy "
                     "always does", build, ownBuild);

        MarkRetired(h);
        s_retiresInFlight.fetch_add(1, std::memory_order_acq_rel);
        HANDLE th = CreateThread(nullptr, 0, RetireThread, call, 0, nullptr);
        if (!th) {
            LOG_ERROR("Land: could not start the hand-off thread (error=%lu) -- the earlier copy keeps running",
                      GetLastError());
            s_retiresInFlight.fetch_sub(1, std::memory_order_acq_rel);
            delete call;
            continue;
        }
        const DWORD waited = WaitForSingleObject(th, kRetireWaitMs);
        CloseHandle(th);
        if (waited != WAIT_OBJECT_0)
            LOG_WARN("Land: the earlier copy is still shutting down after %lu ms -- this copy goes on; its mailbox is "
                     "taken over when that shutdown returns, and the pipe is claimed once it is free",
                     static_cast<unsigned long>(kRetireWaitMs));
        if (RefusesStart(State())) {
            LOG_INFO("Land: this copy is itself being taken over -- its pass stops here");
            return;
        }
    }
}

YieldReply BeginYield(HMODULE successor) {
    const auto copies = CopiesInLoadOrder();
    const bool callerIsLater = IsEarlier(IndexOf(copies, g_hDllModule), IndexOf(copies, successor));
    const std::lock_guard<std::mutex> start(s_startMutex);
    const YieldReply reply = ReplyToYield(State(), callerIsLater);
    if (reply != YIELD_DONE) return reply;
    s_successor.store(successor, std::memory_order_release);
    s_state.store(static_cast<int32_t>(YieldState::Yielding), std::memory_order_release);
    return reply;
}

void FinishYield() {
    s_state.store(static_cast<int32_t>(YieldState::Yielded), std::memory_order_release);
    LOG_WARN("Land: this copy has handed the process to the copy at %s -- its exports run there from now on",
             PathOf(s_successor.load(std::memory_order_acquire)).c_str());
}

Routing RouteExport(const char* exportName) {
    const YieldState s = State();
    if (!Forwards(s)) return {};
    const HMODULE successor = s_successor.load(std::memory_order_acquire);
    const FARPROC target = successor ? GetProcAddress(successor, exportName) : nullptr;
    switch (RouteCall(s, target != nullptr, t_forwardHops)) {
        case Route::Forward:
            return {target, false};
        case Route::Refuse: {
            static std::atomic<bool> s_logged{false};
            if (!s_logged.exchange(true))
                LOG_WARN("Land: %s refused in a copy that handed over -- %s", exportName,
                         target ? "it was forwarded too many times on one thread"
                                : "the copy that took over does not export it");
            return {nullptr, true};
        }
        case Route::Local:
        default:
            return {};
    }
}

HopScope::HopScope() { ++t_forwardHops; }
HopScope::~HopScope() { --t_forwardHops; }

} // namespace Land
