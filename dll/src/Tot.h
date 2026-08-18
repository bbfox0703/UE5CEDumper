#pragma once

// ============================================================
// Tot — 托托, 終極聖女托托 ("Saint of the End")
// Cancellation: cooperative cancel flag for long-running DLL operations.
//
// The pipe server (Fern) processes one command at a time, synchronously,
// on a single connection. A long scan therefore BLOCKS the pipe thread
// until it returns. Two failure modes motivated this module:
//
//   1. "Game won't close": disabling the CE script calls UE5_Shutdown ->
//      Fern::Stop(), which JOINS the accept thread. If that thread is mid
//      scan, the join blocks until the scan finishes (potentially long /
//      unbounded). RequestShutdown() lets every long loop bail promptly so
//      the join completes fast.
//
//   2. "DLL keeps scanning after the UI closes": the client disconnects but
//      the blocked handler can't notice until it returns. Fern's monitor
//      thread peeks the pipe while a command is in-flight and calls
//      RequestPerCommand() on a broken pipe, so the orphaned scan bails and
//      the pipe frees for the next (reconnecting) client.
//
// Long loops poll Requested() every N iterations (cheap relaxed atomic load)
// and bail with an empty / partial result. Per-command cancellation is reset
// when a fresh session connects into an empty connection registry (Fern
// AcceptLoop firstConn), NOT per-command — a light command on one lane must not
// clear a running scan's cancel on another lane; shutdown is sticky. Background
// re-assert workers opt out of the per-command cancel via MarkBackgroundWorker()
// (they live for the game process, not a single pipe command). (M4)
//
// The CE mailbox poller (Mimic) opts out too, via MarkCancelImmune() — it serves CE,
// so a PIPE client's death must not cancel its work — but it is NOT a background
// worker, because it carries the user's one-shot invokes. See t_cancelImmune. (B4)
// ============================================================

#include <atomic>

namespace Tot {

// Set when the connected client disconnects mid-command (Fern monitor).
// Cleared by ResetPerCommand() when a fresh session connects into an empty
// registry (Fern AcceptLoop firstConn) — kept latched until then so an orphaned
// in-flight scan on the dropped connection keeps aborting (do NOT clear it at
// disconnect: the orphaned scan must still see the cancel until it unwinds).
inline std::atomic<bool> g_perCommand{false};

// Sticky: set at the TOP of UE5_Shutdown() (and by Fern::Stop()) so in-flight
// ops abort and the accept-thread join completes quickly, AND so every module's
// StartWorker* refuses to (re)spawn a re-assert worker during the shutdown window
// (join → Stark::Shutdown → pipe stop) — otherwise a pipe/mailbox command landing
// in that window could revive a just-joined worker that nothing joins again (M5).
// Cleared only by Fern::Start() (ResetShutdown) so re-enabling the CE script in
// the same game process brings the server back to a non-aborting state — otherwise
// every long op would bail on its first Requested() poll.
inline std::atomic<bool> g_shutdown{false};

// Marks the current thread as a background re-assert worker (Solide/Hemmung/
// Laufen/Solitar/Dunste/Schlacht). Such a thread lives for the whole game
// process, not a single pipe command, so it ignores the per-command cancel (set
// when a pipe client drops mid-command) and aborts only on real shutdown —
// otherwise a client disconnect silently freezes every hold. Notably Solide's
// only instance source (Aura::FindInstancesByClass) polls Requested() at n=0 and
// would bail to an EMPTY set every re-assert tick while a client is gone. (M4)
inline thread_local bool t_backgroundWorker = false;

// Immunity from the PER-COMMAND cancel, split out from "is a background worker"
// because one flag was answering two different questions:
//   (a) should this thread ignore a PIPE client's mid-command disconnect?
//   (b) is this a REPEATING worker, so refuse the off-game-thread invoke fallback?
// For the re-assert workers both answers are yes, which is why one flag sufficed —
// until the CE mailbox poller (Mimic) needed (a) without (b). It serves CE, not the
// pipe, so a pipe disconnect must not cancel its work; but it carries the user's
// ONE-SHOT CE invokes, so marking it a background worker would make
// UE5_CallProcessEventEx refuse them with -8 whenever the PE hook is down. (B4)
inline thread_local bool t_cancelImmune = false;
inline void MarkCancelImmune() { t_cancelImmune = true; }

// Every background worker is also cancel-immune — set both so existing call sites
// keep their exact behaviour.
inline void MarkBackgroundWorker() { t_backgroundWorker = true; t_cancelImmune = true; }

/// True on a re-assert / feature worker thread. Read by the invoke path to refuse
/// the "direct ProcessEvent call off the game thread" fallback for REPEATING
/// worker invokes: a user's one-shot invoke risking that path is a trade they
/// asked for, a 10 Hz worker silently taking it for minutes is not.
inline bool IsBackgroundWorker() { return t_backgroundWorker; }

// True when any in-flight long-running operation should abort. On a background
// worker thread only real shutdown aborts (see MarkBackgroundWorker); on a pipe
// command thread a mid-command client disconnect (per-command) aborts too — so
// the SAME resolve helper honours the cancel when called from a pipe command but
// keeps running when called from a re-assert worker.
inline bool Requested() {
    if (t_cancelImmune)
        return g_shutdown.load(std::memory_order_relaxed);
    return g_perCommand.load(std::memory_order_relaxed)
        || g_shutdown.load(std::memory_order_relaxed);
}

// True once teardown has begun (g_shutdown). Used by the worker (re)start gate to
// refuse spawning a re-assert worker during the shutdown window. (M5)
inline bool ShutdownRequested() { return g_shutdown.load(std::memory_order_relaxed); }

// Is the per-command cancel still OWED, i.e. is at least one of the connections
// that raised it still registered?
//
// The latch used to clear only when a fresh session connected into an EMPTY
// registry ("firstConn"). That was right when there was one connection; with the
// two-lane split it is not (audit #5 F2): if the bulk lane drops mid-scan while
// the light lane stays up, the registry is never empty, firstConn never fires,
// and the latch stays set FOREVER -- so every subsequent scan on the surviving
// lane aborts instantly, for the life of the process.
//
// Ownership is the right question, not emptiness. Pure and header-inline so the
// decision is unit-tested rather than inferred from Fern's threading; Fern
// supplies both lists and holds no policy.
//
// It must NOT clear early: an orphaned scan has to keep seeing the cancel until
// it unwinds. That holds because a connection is erased from the live list
// strictly AFTER its handler returned.
inline bool PerCommandStillOwed(const uint64_t* owners, size_t nOwners,
                                const uint64_t* live, size_t nLive) {
    for (size_t i = 0; i < nOwners; ++i)
        for (size_t j = 0; j < nLive; ++j)
            if (owners[i] == live[j]) return true;
    return false;
}

inline void RequestPerCommand() { g_perCommand.store(true,  std::memory_order_relaxed); }
inline void ResetPerCommand()   { g_perCommand.store(false, std::memory_order_relaxed); }
inline void RequestShutdown()   { g_shutdown.store(true,    std::memory_order_relaxed); }
inline void ResetShutdown()     { g_shutdown.store(false,   std::memory_order_relaxed); }

} // namespace Tot
