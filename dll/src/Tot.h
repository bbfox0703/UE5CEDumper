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

// The cancel flag of the pipe connection THIS thread is serving, or nullptr when the
// thread is not a connection handler. Bound by Fern::HandleConnection for the life of
// the connection (see BindConnectionCancel).
//
// ⛔ WHY THE UNBOUND CASE FALLS BACK TO g_perCommand INSTEAD OF MEANING "not cancellable".
// The obvious formulation -- `return (t_connCancel && t_connCancel->load()) || g_shutdown`
// -- was designed, reviewed and REJECTED 2026-09-07. It makes "unbound" behaviourally
// identical to "cancel-immune", which inverts this module's default from FAIL-SAFE to
// FAIL-SILENT: every thread that does not bind silently stops being cancellable, and
// t_cancelImmune becomes semantically dead (so M4's and B4's distinctions vanish without
// a line of them being deleted). That is not hypothetical -- the DLL has at least three
// populations that reach Requested() on threads no connection owns:
//   * Aura's ParallelIndexRanges workers (Aura.cpp:173) and its cancelWatcher
//     (Aura.cpp:221) -- and the watcher's ONLY job is to turn a client disconnect into
//     deadlineHit for the parallel path, which is the default path for every real game
//     (ScanThreadCount, Aura.cpp:129, goes parallel at >= 8192 objects);
//   * Fern::RunScan / RunRescan (Fern.cpp:5276 / 5114) and Frieren's UE5_AutoStart;
//   * the CE remote thread entering the Frieren C-ABI exports.
// Binding those to &conn->cancel is NOT the fix either: the connection can be erased
// while such a thread still runs, so the pointer would dangle in exactly the disconnect
// case the binding exists for.
//
// So the fallback is deliberate and load-bearing: a BOUND thread gets per-connection
// precision, and everything else keeps the pre-2026-09-07 global behaviour unchanged.
// Narrowing that population is a separate, larger piece of work (todo.md).
inline thread_local std::atomic<bool>* t_connCancel = nullptr;

/// Bind this thread to its connection's cancel flag. The caller MUST own a shared_ptr to
/// the connection for the whole bound region -- Fern::HandleConnection does, because the
/// accept thread hands it the shared_ptr BY VALUE (Fern.cpp:976), so the flag cannot
/// outlive the pointer. Use the RAII guard below rather than calling these directly.
inline void BindConnectionCancel(std::atomic<bool>* flag) { t_connCancel = flag; }
inline void UnbindConnectionCancel() { t_connCancel = nullptr; }

/// The cancellation identity of the CURRENT thread, as a value that can be handed to a
/// worker thread this one spawns. Both fields matter: a worker inherits neither the
/// binding nor the immunity of its parent, because thread_locals do not propagate.
struct CancelContext {
    std::atomic<bool>* connCancel = nullptr;
    bool               immune     = false;
};
inline CancelContext CaptureCancelContext() { return CancelContext{t_connCancel, t_cancelImmune}; }

/// Adopt a captured context on THIS thread, restoring the previous one on scope exit.
///
/// ⛔ ONLY safe when the spawning thread OUTLIVES the worker -- i.e. it joins it. That
/// holds for Aura::ParallelIndexRanges (it joins its pool, Aura.cpp:176) and for the
/// ParallelGObjectsScan cancelWatcher, which is why those may adopt a raw
/// `std::atomic<bool>*`. It does NOT hold for Fern::RunScan / RunRescan, which outlive
/// the command that started them -- binding those to a connection would be a
/// use-after-free in exactly the disconnect case the binding exists for. Give those an
/// owning token before propagating anything into them.
struct CancelContextScope {
    CancelContext prev;
    explicit CancelContextScope(const CancelContext& c)
        : prev{t_connCancel, t_cancelImmune} {
        t_connCancel   = c.connCancel;
        t_cancelImmune = c.immune;
    }
    ~CancelContextScope() { t_connCancel = prev.connCancel; t_cancelImmune = prev.immune; }
    CancelContextScope(const CancelContextScope&) = delete;
    CancelContextScope& operator=(const CancelContextScope&) = delete;
};

/// RAII: binds for the enclosing scope, unbinds on every exit path including a throw.
/// ⚠ Scope it to the WHOLE handler, not just the command loop: the disconnect-teardown
/// block runs after the loop and must still see its own connection's cancel.
struct ConnectionCancelScope {
    explicit ConnectionCancelScope(std::atomic<bool>* flag) { BindConnectionCancel(flag); }
    ~ConnectionCancelScope() { UnbindConnectionCancel(); }
    ConnectionCancelScope(const ConnectionCancelScope&) = delete;
    ConnectionCancelScope& operator=(const ConnectionCancelScope&) = delete;
};

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
    // Bound to a connection: consult ONLY that connection's flag. This is the whole
    // point -- a FOREIGN client's death must not truncate this client's scan
    // ([MULTIPIPE-CANCEL-2026-09-07], measured: 5,157 replies of 77 bytes where 720,793
    // were due). Note it does NOT also OR in g_perCommand: doing so would leave the
    // defect exactly as it was.
    if (t_connCancel)
        return t_connCancel->load(std::memory_order_relaxed)
            || g_shutdown.load(std::memory_order_relaxed);
    // Unbound: unchanged global behaviour, deliberately. See t_connCancel's note.
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
