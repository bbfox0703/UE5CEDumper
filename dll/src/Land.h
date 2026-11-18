#pragma once

// ============================================================
// Land — 蘭特 (完美分身魔法 — Flawless Clones)
// SecondCopy: two copies of the dumper in one game process (a proxy from the game folder and a copy injected later,
// say) -- the later copy takes over. It shuts the earlier copy down, takes the pipe, and serves the earlier copy's CE
// mailbox, so a Cheat Engine script that resolved the earlier copy's g_invokeMailbox keeps working. A copy that
// handed over starts nothing again and passes its C ABI calls to the copy that took over.
// [DUMPER-SECOND-COPY-AUTOSTART] The later copy always takes over, whichever build is newer. "Later" is the loader's
// order: a copy's place in the process's module list.
// [LAND-MEMO-RESIDENT] What a hand-off does not free: the earlier copy's class-walk memo (Ubel's WalkClassEx memo and
// its parked walks, up to their byte bound) stays allocated beside the later copy's own for the session. The memo
// hands out references and a straggling pipe thread of the earlier copy may still hold one, so it is never freed --
// as after a re-init ([FAMILY-EPOCH]); the later copy walks the same classes into its own.
// ============================================================

#include <cstdint>
#include <mutex>
#include <type_traits>
#include <Windows.h>

#include "Mimic.h"

namespace Land {

// What a later copy reads of another copy of the dumper in this process, from that copy's own exports.
struct CopySeen {
    bool    contract  = false;  // exports g_mailboxContract carrying the magic
    int32_t current   = 0;      // its MAILBOX_CONTRACT
    int32_t minimum   = 0;      // its MAILBOX_CONTRACT_MIN
    bool    mailbox   = false;  // exports g_invokeMailbox
    int32_t initState = 0;      // its mailbox's initState; read only when contract && mailbox
};

enum class Verdict { Leave, Retire };

// A copy that has started -- RUNNING, READY, SKIPPED or FAILED -- is retired. IDLE is a copy that never started (the
// proxy that lost the first-loaded-wins mutex runs no logger and no threads, so its shutdown must not be called) or one
// already shut down. Without the contract block there is no initState to read: initState sits at a fixed offset in
// every build that exports the block, and a build from before it is left as it is.
constexpr Verdict Judge(const CopySeen& c) {
    if (!c.contract || !c.mailbox) return Verdict::Leave;
    switch (c.initState) {
        case Mimic::INIT_RUNNING:
        case Mimic::INIT_READY:
        case Mimic::INIT_SKIPPED:
        case Mimic::INIT_FAILED:
            return Verdict::Retire;
        default:
            return Verdict::Leave;
    }
}

// Only an EARLIER copy is taken over: one before this copy in the module list (-1 = not found). The order is strict,
// so of two copies only one can take over from the other -- two that each took over from the other would forward
// every call back and forth until the stack ran out.
constexpr bool IsEarlier(int otherIndex, int ownIndex) {
    return otherIndex >= 0 && ownIndex >= 0 && otherIndex < ownIndex;
}

// The later copy serves the earlier copy's mailbox when the struct is this build's and every script the earlier copy
// accepts is one this copy accepts too: its contract no older than the first with this layout
// (Mimic::MAILBOX_LAYOUT_SINCE), no newer than this copy's, and its oldest accepted script no older than ours. The
// handlers write this build's struct into the earlier copy's memory, so a smaller struct must never be served.
constexpr bool CanServeMailbox(int32_t theirCurrent, int32_t theirMinimum, int32_t ourCurrent, int32_t ourMinimum,
                               int32_t layoutSince) {
    return theirCurrent >= layoutSince && theirCurrent <= ourCurrent && theirMinimum >= ourMinimum;
}

// What the earlier copy's mailbox says when this copy cannot serve it: SKIPPED, "another copy serves this process" -- a
// CE inject script reads READY and SKIPPED as served and leaves the copy alone, and reads any other value as parked and
// calls that copy's UE5_AutoStart. A SERVED mailbox says MirroredState below; the served arm here is that rule's for a
// copy that forwards, kept for the tests that pin it.
constexpr int32_t PublishedState(bool served, int32_t ownState) {
    return served ? ownState : static_cast<int32_t>(Mimic::INIT_SKIPPED);
}

// What an earlier copy's mailbox says while this copy serves it. A copy that forwards its exports (it carries the yield)
// says this copy's own state: a CE inject script that reads it as parked calls that copy's UE5_AutoStart, which reaches
// this copy. An older copy cannot forward, and its UE5_AutoStart would start it again beside this copy, two pollers on
// one mailbox: it says READY or SKIPPED only, the two values an inject script reads as served.
constexpr int32_t MirroredState(int32_t ownState, bool earlierForwards) {
    if (earlierForwards) return ownState;
    return ownState == Mimic::INIT_READY ? ownState : static_cast<int32_t>(Mimic::INIT_SKIPPED);
}

// This copy's own hand-off.
enum class YieldState : int32_t { None = 0, Yielding = 1, Yielded = 2 };

// From the moment it starts shutting down for a later copy, a copy starts nothing of its own again -- no scan, no pipe
// server, no mailbox poller -- so a scan of its own that ends during the hand-off cannot take the pipe back.
constexpr bool RefusesStart(YieldState s) {
    return s != YieldState::None;
}

// Its C ABI exports run in the copy that took over once the hand-off is done. While it is still shutting down they run
// here: its own teardown calls some of them.
constexpr bool Forwards(YieldState s) {
    return s == YieldState::Yielded;
}

// UE5_YieldToLaterCopy's answer to the copy that asked.
enum YieldReply : int32_t {
    YIELD_DONE          = 1,   // this call handed the process over
    YIELD_ALREADY       = 0,   // a hand-off had already begun: this copy's poller is stopped or stopping
    YIELD_NOT_LATER     = -1,  // the asking copy is not later than this one; nothing was done
};

constexpr YieldReply ReplyToYield(YieldState own, bool callerIsLater) {
    if (!callerIsLater) return YIELD_NOT_LATER;
    return own == YieldState::None ? YIELD_DONE : YIELD_ALREADY;
}

// Where an export call goes. A yielded copy forwards; when the successor lacks that export, or the call has already
// been forwarded kMaxForwardHops times on this thread, it refuses instead of running its own body: this copy is shut
// down, and its body could re-arm a hook or touch the game behind the copy that serves.
enum class Route { Local, Forward, Refuse };

constexpr int kMaxForwardHops = 4;

constexpr Route RouteCall(YieldState s, bool successorHasExport, int hopsOnThisThread) {
    if (!Forwards(s)) return Route::Local;
    if (!successorHasExport || hopsOnThisThread >= kMaxForwardHops) return Route::Refuse;
    return Route::Forward;
}

// The value a refused export call returns: false, a null address, or -1 for a signed result (the C ABI's failure
// codes are negative, and 0 is success for several of them).
template <class R>
constexpr R RefusalValue() {
    if constexpr (std::is_void_v<R>) return;
    else if constexpr (std::is_same_v<R, bool>) return false;
    else if constexpr (std::is_integral_v<R> && std::is_signed_v<R>) return static_cast<R>(-1);
    else return R{};
}

template <class R, class... A>
constexpr R RefusalOf(R (*)(A...)) {
    return RefusalValue<R>();
}

// Most copies one process keeps track of; a game with more than this many loaded copies of the dumper is not one any
// front door produces.
constexpr int kMaxCopies = 8;

// How long a pass waits for one earlier copy's hand-off before it goes on. The hand-off itself goes on past it: its
// thread adopts the mailbox whenever the earlier copy's shutdown returns. A scan's joins are bounded only by cancel
// polls.
constexpr DWORD kRetireWaitMs = 30000;

// ---- Runtime (Land.cpp) ----

YieldState State();

// Called by UE5_StartPipeServer before it probes the pipe: every earlier copy Judge retires is handed over, latest first,
// and its mailbox is served here or marked SKIPPED. The caller asks State() again afterwards: a later copy may have taken
// over from this one meanwhile.
void RetireEarlierCopies();

// Whether an earlier copy in this process is one RetireEarlierCopies would retire. Heiter asks before deferring to a busy
// pipe: when that copy is ours, this copy takes over rather than waiting for it.
bool AnyCopyToRetire();

// A hand-off this copy started has not finished: the pipe's holder may still be the copy being shut down.
bool RetireInFlight();

// UE5_YieldToLaterCopy: Begin before the shutdown (it refuses an asking copy that is not later), Finish after it.
YieldReply BeginYield(HMODULE successor);
void FinishYield();

// Held while a start path re-checks RefusesStart and starts something, and by BeginYield: a yield cannot fall between
// the check and the start.
std::unique_lock<std::mutex> LockStart();

// Where an export call of that name goes (LAND_FORWARD).
struct Routing {
    FARPROC target = nullptr;   // the successor's export: call it
    bool    refuse = false;     // return RefusalOf instead of running here
};
Routing RouteExport(const char* exportName);

// Counts this thread's forwarded calls in flight, for RouteCall's hop cap.
struct HopScope {
    HopScope();
    ~HopScope();
    HopScope(const HopScope&) = delete;
    HopScope& operator=(const HopScope&) = delete;
};

} // namespace Land

// First statement of a C ABI export: once this copy has handed over, the call runs in the copy that took over, or is
// refused.
#define LAND_FORWARD(fn, ...)                                                                                 \
    do {                                                                                                      \
        const ::Land::Routing landRoute_ = ::Land::RouteExport(#fn);                                          \
        if (landRoute_.target) {                                                                              \
            const ::Land::HopScope landHop_;                                                                  \
            return reinterpret_cast<decltype(&fn)>(landRoute_.target)(__VA_ARGS__);                           \
        }                                                                                                     \
        if (landRoute_.refuse) return ::Land::RefusalOf(&fn);                                                 \
    } while (0)

// The same, unless `keepHere` -- an export whose state lives in this copy (a class walk begun here before the hand-off)
// finishes here.
#define LAND_FORWARD_UNLESS(keepHere, fn, ...)                                                                \
    do {                                                                                                      \
        if (!(keepHere)) LAND_FORWARD(fn, __VA_ARGS__);                                                       \
    } while (0)
