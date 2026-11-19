#pragma once

// ============================================================
// Routine — ルティーネ, 影の戦士の司書 (Shadow Warrior librarian)
// Periodic-worker scaffolding shared by every re-assert / hold module.
//
// The re-assert / hold modules run the same worker: mark the thread cancel-immune, sleep
// a period in short slices so a stop is honoured promptly, do one tick, repeat. The shape
// lives here once, so the exception guard and the sleep slice cannot differ between copies.
//
// An exception escaping a thread entry is std::terminate, not a caught error. A worker
// tick reads live game state and allocates from it, so during the game's own teardown a
// read or a std::vector sizing can throw. Every tick therefore runs inside
// RunTickGuarded, once, here.
//
// LOG_CAT: the macros below expand at the INCLUDE site, so include this header after
// the module's `#define LOG_CAT "..."`. Included before it, this header's guard messages
// log under "" (init-0.log), and the only visible symptom is a C4005
// macro-redefinition warning that reads as cosmetic. Each module's worker then logs
// under its own category.
// ============================================================

#include "Sein.h"
#include "Grimoire.h"
#include "Tot.h"

#include <atomic>
#include <chrono>
#include <exception>
#include <memory>
#include <mutex>
#include <thread>

namespace Routine {

// ============================================================
// SafeThread — a std::thread whose destructor DETACHES instead of terminating.
// ============================================================
//
// `~std::thread()` on a still-joinable thread calls std::terminate() **directly** — no
// exception is thrown, so no `catch` anywhere can help. Same for move-assigning over a
// joinable thread. Either is a fast-fail (0xC0000409 param[0]=7 = FATAL_APP_EXIT), and
// an exception guard cannot help because there is no exception to catch:
//
//   * `UE5_Shutdown` is NOT called when the user closes a game — `DllMain(DETACH)` is a
//     deliberate no-op (joining a thread from DllMain deadlocks on the loader lock), and
//     there is no proxy graceful-exit hook.
//   * So at process exit every feature worker that was switched on is still joinable, its
//     static destructor runs, and with a plain std::thread the FIRST one to destruct kills
//     the process.
//
// Detaching from DllMain would need a hand-maintained LIST of threads. This makes it a
// property of the TYPE instead: declare the thread as a SafeThread and the failure
// cannot occur, including in a module nobody has written yet.
//
// Detaching is the correct action at exit, not a cop-out: on process exit Windows has
// already terminated every other thread before DETACH runs, so there is nothing left to
// wait for — only a handle to release. Deliberate teardown still goes through each
// module's StopWorker(), which joins properly while the loader lock is free.
class SafeThread {
public:
    SafeThread() = default;
    SafeThread(const SafeThread&) = delete;
    SafeThread& operator=(const SafeThread&) = delete;

    /// Adopt a freshly-spawned thread. If one is somehow already running, detach it
    /// rather than terminate — std::thread's own move-assign would terminate here.
    SafeThread& operator=(std::thread&& t) noexcept {
        if (m_t.joinable()) m_t.detach();
        m_t = std::move(t);
        return *this;
    }

    ~SafeThread() { if (m_t.joinable()) m_t.detach(); }

    bool joinable() const noexcept { return m_t.joinable(); }
    void join()   { m_t.join(); }
    void detach() { m_t.detach(); }

private:
    std::thread m_t;
};

// Run one worker tick with the guard a thread entry must have. `warnedOnce` is the
// caller's loop-local latch so a game tearing down for several seconds logs one line,
// not one per tick.
template <typename TickFn>
inline void RunTickGuarded(const char* tag, bool& warnedOnce, TickFn&& tick) {
    try {
        tick();
    } catch (const std::exception& e) {
        if (!warnedOnce) {
            warnedOnce = true;
            LOG_WARN("%s: tick threw (%s) — skipping (game tearing down?)", tag, e.what());
        }
    } catch (...) {
        if (!warnedOnce) {
            warnedOnce = true;
            LOG_WARN("%s: tick threw (unknown) — skipping (game tearing down?)", tag);
        }
    }
}

// Wrap an ENTIRE thread body (or any callback invoked from foreign code) so a throw
// cannot escape into a frame that has no handler.
//
// WHY THIS EXISTS SEPARATELY FROM RunTickGuarded: a feature worker's tick is only one of
// the places where a C++ exception means std::terminate. Every thread body the DLL starts
// needs the guard, and so does every callback entered from foreign code — above all
// Stark's ProcessEvent hook, which runs on the GAME's own thread and is entered from game
// code that has no handler for us.
//
// Returns false when the body threw, so a caller that must react (rather than merely
// survive) can. Never rethrows.
template <typename Fn>
inline bool RunThreadGuarded(const char* tag, Fn&& body) {
    try {
        body();
        return true;
    } catch (const std::exception& e) {
        LOG_ERROR("%s: UNCAUGHT exception (%s) — contained. This would previously have "
                  "terminated the game (0xC0000409).", tag, e.what());
    } catch (...) {
        LOG_ERROR("%s: UNCAUGHT non-standard exception — contained. This would previously "
                  "have terminated the game (0xC0000409).", tag);
    }
    return false;
}

// Sleep `totalMs` in WORKER_SLEEP_SLICE_MS slices so StopWorker()'s join waits at most
// one slice instead of a whole period. Returns false if the stop flag was raised (or a
// shutdown began) while sleeping — i.e. "do not run the tick".
inline bool SleepSliced(int totalMs, const std::atomic<bool>& stop) {
    for (int slept = 0; slept < totalMs; slept += Grimoire::WORKER_SLEEP_SLICE_MS) {
        if (stop.load() || Tot::ShutdownRequested()) return false;
        std::this_thread::sleep_for(
            std::chrono::milliseconds(Grimoire::WORKER_SLEEP_SLICE_MS));
    }
    return !stop.load() && !Tot::ShutdownRequested();
}

// The whole re-assert loop: cancel-immunity, start/stop logging, sliced sleep, guarded
// tick. `tick` owns everything module-specific — including its own drift counting and
// wording, which is deliberate: those WARN strings are individually worded and are
// grepped by the log-verification checklist, so they are not templated away.
//
// The loop also breaks on Tot::ShutdownRequested() (via SleepSliced): a loop that tests
// only its own stop flag lets a worker whose StopWorker never ran keep walking
// reflection against a tearing-down process.
template <typename TickFn>
inline void ReassertLoop(const char* tag, int periodMs,
                         const std::atomic<bool>& stop, TickFn&& tick) {
    Tot::MarkBackgroundWorker();   // ignore per-command cancel; abort only on shutdown
    LOG_INFO("%s: re-assert worker started (%d ms)", tag, periodMs);
    bool warnedThrow = false;
    while (SleepSliced(periodMs, stop))
        RunTickGuarded(tag, warnedThrow, tick);
    LOG_INFO("%s: re-assert worker stopped", tag);
}

// ============================================================
// SingleFlightJob — one background job at a time, started by whichever client asks first.
// ============================================================
// [PIPE-PER-PROCESS-2026-11-06] trigger_scan and rescan each start a background job, and a pipe with six
// clients can be asked twice at once. The job keeps running when the client that started it leaves: its result is
// the process's, read by every later client and by CE, and the UI starts it on one lane, polls it on the other and
// reconnects both together. Only Stop (and shutdown) cancel it.
class SingleFlightJob {
public:
    enum class Start { Started, AlreadyRunning, Stopping };

    explicit SingleFlightJob(const char* tag) : m_tag(tag) {}
    SingleFlightJob(const SingleFlightJob&) = delete;
    SingleFlightJob& operator=(const SingleFlightJob&) = delete;

    // `reset` runs only for the caller that starts the job, before its thread; `body` runs on that thread and its
    // return is Completed(). `serverRunning` false refuses: a job started after Stop's join would be joined by nobody.
    template <class Reset, class Body>
    Start TryStart(const std::atomic<bool>& serverRunning, Reset&& reset, Body&& body) {
        // The claim is one test-and-set under m_stateMu. A separate check and set lets two callers through: both run
        // their reset, both join and replace one std::thread, and the loser's thread is detached where no Stop
        // would join it. `completed` is cleared in the same step, so Status() never pairs this run's `running` with
        // the last run's answer.
        {
            std::lock_guard<std::mutex> st(m_stateMu);
            if (m_running.load(std::memory_order_relaxed)) return Start::AlreadyRunning;
            m_running.store(true, std::memory_order_release);
            m_completed.store(false, std::memory_order_relaxed);
        }
        bool launched = false;
        struct Unclaim {   // every way out before the thread exists gives the claim back -- a throw included
            SingleFlightJob& job;
            const bool&      launched;
            ~Unclaim() {
                if (launched) return;
                std::lock_guard<std::mutex> st(job.m_stateMu);
                job.m_running.store(false, std::memory_order_release);
            }
        } unclaim{*this, launched};
        std::lock_guard<std::mutex> lk(m_life);
        if (!serverRunning.load()) return Start::Stopping;
        if (m_thread.joinable()) m_thread.join();   // the last run cleared `running` as it ended: a brief join
        reset();
        auto token = std::make_shared<std::atomic<bool>>(false);
        m_token = token;
        m_thread = std::thread([this, token, body = std::forward<Body>(body)]() mutable {
            // Bound to the job's own token, which this thread co-owns, so Tot::Requested() here -- and in any worker
            // it spawns with the captured context and joins -- answers this token or shutdown, never the per-command
            // cancel another client's death raises.
            Tot::CancelContextScope scope(Tot::CancelContext{token.get(), false});
            bool ok = false;
            RunThreadGuarded(m_tag, [&] { ok = body(); });
            std::lock_guard<std::mutex> st(m_stateMu);
            m_completed.store(ok, std::memory_order_relaxed);
            m_running.store(false, std::memory_order_release);
        });
        launched = true;
        return Start::Started;
    }

    // Cancel the running job (its own token; never another client's) and join it.
    void StopAndJoin() {
        std::lock_guard<std::mutex> lk(m_life);
        if (m_token) m_token->store(true, std::memory_order_relaxed);
        if (m_thread.joinable()) m_thread.join();
    }

    bool Running() const { return m_running.load(std::memory_order_acquire); }
    // The last run's body returned true. Meaningful once Running() is false.
    bool Completed() const { return m_completed.load(std::memory_order_relaxed); }

    // Running and Completed as one answer. Read apart, another client's start between the two pairs "not running"
    // with the new run's cleared answer: a status reply that says "finished, nothing found".
    struct Snapshot {
        bool running   = false;
        bool completed = false;
    };
    Snapshot Status() const {
        std::lock_guard<std::mutex> st(m_stateMu);
        return Snapshot{m_running.load(std::memory_order_relaxed), m_completed.load(std::memory_order_relaxed)};
    }

private:
    const char*        m_tag;
    std::atomic<bool>  m_running{false};
    std::atomic<bool>  m_completed{false};
    mutable std::mutex m_stateMu; // every change of running / completed, and Status()'s read of both
    std::mutex         m_life;    // TryStart's join-and-replace against StopAndJoin
    std::shared_ptr<std::atomic<bool>> m_token;
    SafeThread         m_thread;
};

} // namespace Routine
