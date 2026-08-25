#pragma once

// ============================================================
// Fern — 費倫 (芙莉蓮的弟子 — Frieren's Apprentice)
// PipeServer: Named Pipe JSON IPC server (99 commands)
//
// ⚠ DERIVED, not hand-maintained: `grep -c 'constexpr const char* CMD' dll/src/Renge.h`,
// asserted by `tools/check_derived_counts.py` in CI. It said "~30" against a real 99 until
// build 3262 — the seventh stale copy that gate was built for, and it survived because the
// registry covered the docs but not the two SOURCE headers the docs derive from
// (audit #5 AD7/AD22).
//
// Multi-connection model (Path A — docs/multipipe-eval.md §9): the server
// accepts up to kMaxPipeInstances clients and serves EACH on its own thread
// with its own handle, fully serial within itself (read→dispatch→write). Two
// threads therefore never touch the same handle, so the synchronous pipe never
// hits the WriteFile-while-ReadFile-parked deadlock that sank the single-handle
// worker-pool attempt. The UI opens TWO connections (interactive + bulk lanes)
// and routes commands; the DLL itself is lane-agnostic — it only supports
// concurrent connections. CE Lua stays on the Mimic mailbox (off-pipe).
// ============================================================

#include <Windows.h>
#include "Routine.h"   // Routine::SafeThread — ~std::thread on a joinable thread terminates (B14)
#include <string>
#include <thread>
#include <atomic>
#include <unordered_map>
#include <mutex>
#include <condition_variable>
#include <memory>
#include <vector>

class Fern {
public:
    // NOT Stop() — see Stop's `graceful` parameter. This destructor runs from the
    // CRT's static-destructor pass (s_pipeServer is a namespace-scope static in
    // Frieren.cpp), i.e. during DLL_PROCESS_DETACH, where the full teardown is both
    // useless and unsafe. (audit #5 D5/F1)
    ~Fern() { Stop(/*graceful=*/false); }
    bool Start();
    // graceful=true  — an explicit, in-process teardown (UE5_Shutdown / CE Disable /
    //                  UE5_StopPipeServer): drain, cancel and join everything.
    // graceful=false — reached from ~Fern() at process exit: do the minimum.
    void Stop(bool graceful = true);
    bool IsClientConnected() const { return m_clientConnected.load(); }

private:
    // Max concurrent client connections. 2 UI lanes (interactive + bulk) + 1
    // slack for reconnect transients. CE uses the Mimic mailbox, not the pipe.
    static constexpr DWORD kMaxPipeInstances = 3;

    // Per-connection state. Each accepted client gets one of these, served by
    // its own HandleConnection thread on its own handle. writeMutex serializes
    // the (single) thread's response writes against this connection's own watch
    // event writes — writes to DIFFERENT connections need no shared lock.
    struct Connection {
        HANDLE             pipe{INVALID_HANDLE_VALUE};
        // Monotonic id, assigned under m_connMutex at accept (audit #5 F2). Needed
        // because the per-command cancel is owned by the connections that RAISED it,
        // and a raw pointer cannot answer "is that same connection still live" once
        // the object is gone and the allocator has reused the address.
        uint64_t           seq{0};
        std::mutex         writeMutex;
        std::atomic<bool>  inFlight{false};   // true only while inside DispatchCommand
        // What that in-flight command IS, and when it started. Written just before
        // DispatchCommand and read ONLY by Stop's drain-timeout diagnostic, so the hot
        // path pays one small copy per command and the teardown can finally name the
        // connection that would not leave. "2 left" is not actionable; "2 left: lane
        // busy 4980 ms in 'walk_instance'" is. (2026-08-04: the first Stop ever logged
        // with connections attached timed out at the full 5 s and the log could not say
        // why.)
        std::mutex         diagMutex;         // guards cmdName only
        std::string        cmdName;           // last/current command on this connection
        std::atomic<long long> cmdStartMs{0}; // steady-clock ms when it began
        std::atomic<bool>  closed{false};     // CloseHandle done exactly once
        // A duplicate of the SERVING thread's own handle, so Stop can call
        // CancelSynchronousIo on it. CancelIoEx cancels ASYNCHRONOUS requests; these
        // pipe instances are created without FILE_FLAG_OVERLAPPED, so a thread parked in
        // ReadFile has no pending IRP for CancelIoEx to find and it returns
        // ERROR_NOT_FOUND. Measured 2026-08-05 on the first Stop that ever caught two
        // idle connections: "0 accepted, 2 had nothing pending", then 49 re-asserts over
        // the full 5 s budget, all reporting the same. CancelSynchronousIo is the API for
        // a synchronous operation blocking a known thread, and it needs the THREAD handle
        // -- which only the serving thread itself can hand us.
        std::atomic<HANDLE> servingThread{nullptr};

        // WHERE the serving thread actually is. `inFlight` only says "inside
        // DispatchCommand", so everything else -- parked in ReadFile, blocked in
        // WriteFile on a client that stopped reading, waiting on writeMutex, or joining
        // watch threads during cleanup -- collapsed into one bucket that the drain
        // diagnostic then LABELLED "idle in ReadFile". That label is an inference, and
        // three fixes were aimed at it: "stuck inside a command" (wrong), "CancelIoEx
        // missed the window" (wrong -- 49 re-asserts, all nothing-pending), and
        // "CancelSynchronousIo is the right API" (wrong -- still timed out). Each one
        // treated the label as an observation. This makes it one.
        enum class Phase : uint8_t {
            Reading,          // inside ReadLine's blocking ReadFile
            Dispatching,      // inside DispatchCommand
            Writing,          // inside WriteLine (WriteFile, or waiting on writeMutex)
            StoppingWatches,  // cleanup: joining this connection's watch threads
            Unregistering,    // cleanup: erasing from m_conns / closing the handle
            Done,
        };
        std::atomic<Phase>     phase{Phase::Reading};
        std::atomic<long long> phaseStartMs{0};
    };

    static const char* PhaseName(Connection::Phase p);
    static void SetPhase(Connection& conn, Connection::Phase p);

    Routine::SafeThread        m_acceptThread;
    std::atomic<bool>  m_running{false};
    // True for the duration of Stop(). m_running goes false on Stop()'s first
    // line, so it cannot tell Start() "a teardown is still unwinding" — and
    // starting during that window move-assigns onto a joinable thread, which is
    // std::terminate.
    std::atomic<bool>  m_stopping{false};
    std::atomic<bool>  m_clientConnected{false};   // mirror of (!m_conns.empty())

    // Registry of live connections + the instance currently parked in
    // ConnectNamedPipe. Stop() reads it only to decide whether to send a
    // wake-connect; it must NOT close that handle (a synchronous listen instance
    // blocks the close until somebody connects — see Fern::Stop).
    std::vector<std::shared_ptr<Connection>> m_conns;
    HANDLE                  m_listenPipe{INVALID_HANDLE_VALUE};
    std::mutex              m_connMutex;     // guards m_conns + m_listenPipe + m_clientConnected
                                             // + m_cancelOwners + m_connSeq
    // Which connections raised the per-command cancel. Cleared when none of them is
    // still registered — NOT when the registry merely goes empty, which the two-lane
    // split made unreachable (audit #5 F2).
    std::vector<uint64_t>   m_cancelOwners;
    uint64_t                m_connSeq{0};    // assigned under m_connMutex at accept

    // Clear the per-command cancel once no connection that raised it is still live.
    // MUST be called with m_connMutex ALREADY HELD.
    void ReevaluatePerCommandCancel();
    std::condition_variable m_connCv;        // notified when a connection thread exits

    // Disconnect monitor (cooperative cancellation). A bulk-lane scan blocks its
    // connection thread inside DispatchCommand, so that thread can't notice the
    // client vanishing. The monitor peeks each in-flight connection every ~200ms
    // and trips per-command cancellation on a broken pipe so the orphaned scan
    // bails. It peeks ONLY while a connection's inFlight flag is set (the thread
    // is then CPU-bound in DispatchCommand, not in ReadFile/WriteFile), so there
    // is no concurrent read/write on the handle.
    Routine::SafeThread          m_monitorThread;
    void MonitorLoop();

    // Watch entries — per-connection. A watch's event is written to the
    // connection that registered it (owner); on that connection's disconnect,
    // only its watches are stopped (and joined before the Connection is freed,
    // so `owner` stays valid for the watch thread's lifetime).
    struct WatchEntry {
        uintptr_t           addr;
        uint32_t            size;
        uint32_t            interval_ms;
        Connection*         owner{nullptr};
        Routine::SafeThread         watchThread;
        std::atomic<bool>   active{true};
    };
    std::unordered_map<uintptr_t, std::unique_ptr<WatchEntry>> m_watches;
    std::mutex m_watchMutex;

    // Initial Scan (async trigger_scan for proxy DLL mode)
    struct ScanState {
        std::atomic<bool> running{false};
        std::atomic<int>  phase{0};       // 0=idle, 1..6=scanning, 7=complete
        std::string       statusText;
        std::mutex        statusMutex;
        Routine::SafeThread       scanThread;
        bool              completed = false;
    };
    ScanState m_scan;
    void RunScan();

    // Extra Scan (user-triggered background rescan for missing pointers)
    struct RescanState {
        std::atomic<bool> running{false};
        std::atomic<int>  phase{0};       // 0=idle, 1=GObjects, 2=GWorld, 3=complete
        std::string       statusText;
        std::mutex        statusMutex;
        uintptr_t         foundGObjects = 0;
        uintptr_t         foundGWorld   = 0;
        const char*       gobjectsMethod = "not_found";
        const char*       gworldMethod   = "not_found";
        Routine::SafeThread       scanThread;
    };
    RescanState m_rescan;
    void RunRescan(bool scanGObjects, bool scanGWorld);
    // The actual work; RunRescan is the guarded wrapper that always clears `running`.
    void RunRescanBody(bool scanGObjects, bool scanGWorld);

    void AcceptLoop();
    void HandleConnection(std::shared_ptr<Connection> conn);
    std::string DispatchCommand(const std::shared_ptr<Connection>& conn, const std::string& jsonLine);
    void StartWatch(const std::shared_ptr<Connection>& conn, uintptr_t addr, uint32_t size, uint32_t interval_ms);
    void StopWatch(uintptr_t addr);
    void StopWatchesForConnection(Connection* owner);
    void StopAllWatches();
    void CloseConnOnce(Connection& conn);
    bool WriteLine(Connection& conn, const std::string& line);
    std::string ReadLine(HANDLE pipe);
};
