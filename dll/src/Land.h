#pragma once

// ============================================================
// Land — 蘭特 (完美分身魔法 — Flawless Clones)
// SecondCopy: two copies of the dumper in one game process (a proxy from the game folder and a copy injected later,
// say) -- the later copy takes over. It shuts the earlier copy down, takes the pipe, and serves the earlier copy's CE
// mailbox, so a Cheat Engine script that resolved the earlier copy's g_invokeMailbox keeps working. A copy that
// handed over starts nothing again and passes its C ABI calls to the copy that took over.
// The maintainer's rule (2026-11-17, [DUMPER-SECOND-COPY-AUTOSTART]): the later copy always takes over, whichever
// build is newer.
// ============================================================

#include <cstdint>
#include <Windows.h>

#include "Mimic.h"

namespace Land {

// What a later copy reads of another copy of the dumper in this process, from that copy's own exports.
struct CopySeen {
    bool    contract  = false;  // exports g_mailboxContract carrying the magic
    int32_t current   = 0;      // its MAILBOX_CONTRACT
    bool    mailbox   = false;  // exports g_invokeMailbox
    int32_t initState = 0;      // its mailbox's initState; read only when contract && mailbox
};

enum class Verdict { Leave, Retire };

// A copy that has started -- RUNNING, READY, SKIPPED or FAILED -- is retired. IDLE is a copy that never started (the
// proxy that lost the first-loaded-wins mutex runs no logger and no threads, so its shutdown must not be called) or one
// already shut down. Without the contract block there is no initState to read: initState sits at a fixed offset in
// every build that exports the block, and a build from before it is left as it is.
constexpr Verdict Judge(const CopySeen& c) {
    (void)c;
    return Verdict::Leave;
}

// The later copy serves the earlier copy's mailbox only when both use one layout. A contract number names one layout
// and one meaning: Mimic.h's bump rules, which tools/check_mailbox_contract.py enforces on a layout change.
constexpr bool CanServeMailbox(int32_t theirCurrent, int32_t ourCurrent) {
    (void)theirCurrent; (void)ourCurrent;
    return false;
}

// What the earlier copy's mailbox says after the hand-off. Served, it says what this copy's own says. Not served, it says
// SKIPPED, "another copy serves this process": a CE inject script reads READY and SKIPPED as served and leaves the copy
// alone, and reads any other value as parked and calls that copy's UE5_AutoStart.
constexpr int32_t PublishedState(bool served, int32_t ownState) {
    (void)served;
    return ownState;
}

// This copy's own hand-off.
enum class YieldState : int32_t { None = 0, Yielding = 1, Yielded = 2 };

// From the moment it starts shutting down for a later copy, a copy starts nothing of its own again -- no scan, no pipe
// server, no mailbox poller -- so a scan of its own that ends during the hand-off cannot take the pipe back.
constexpr bool RefusesStart(YieldState s) {
    (void)s;
    return false;
}

// Its C ABI exports run in the copy that took over once the hand-off is done. While it is still shutting down they run
// here: its own teardown calls some of them.
constexpr bool Forwards(YieldState s) {
    (void)s;
    return false;
}

// Most copies one process keeps track of; a game with more than this many loaded copies of the dumper is not one any
// front door produces.
constexpr int kMaxCopies = 8;

// How long a later copy waits for an earlier copy's shutdown. Its pipe drain alone takes up to 5 s, and its scan joins
// are bounded only by cancel polls.
constexpr DWORD kRetireWaitMs = 30000;

// ---- Runtime (Land.cpp) ----

YieldState State();

// Called by UE5_StartPipeServer before it probes the pipe: every earlier copy Judge retires is shut down, and its
// mailbox is served here or marked SKIPPED.
void RetireEarlierCopies();

// Whether another copy in this process is one RetireEarlierCopies would retire. Heiter asks before deferring to a busy
// pipe: when that copy is ours, this copy takes over rather than waiting for it.
bool AnyCopyToRetire();

// UE5_YieldToLaterCopy: Begin before the shutdown, Finish after it. Begin returns false when a hand-off already began.
bool BeginYield(HMODULE successor);
void FinishYield();

// The successor's export of that name, once this copy has yielded; null otherwise.
FARPROC ForwardTarget(const char* exportName);

template <class Fn>
Fn Forward(const char* exportName) {
    return reinterpret_cast<Fn>(ForwardTarget(exportName));
}

} // namespace Land

// First statement of every C ABI export: once this copy has handed over, the call runs in the copy that took over.
#define LAND_FORWARD(fn, ...) \
    do { if (auto landForward_ = ::Land::Forward<decltype(&fn)>(#fn)) return landForward_(__VA_ARGS__); } while (0)
