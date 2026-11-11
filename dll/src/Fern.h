#pragma once

// ============================================================
// Fern — 費倫 (芙莉蓮的弟子 — Frieren's Apprentice)
// PipeServer: Named Pipe JSON IPC server (106 commands)
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
#include "Voll.h"      // Voll::Holder -- who holds the pipe's connections (Stage C)
#include "Kanne.h"     // Kanne::WatchKey / WatchSlot -- a watch's key and the samples its thread leaves (Stage C)
#include <string>
#include <thread>
#include <atomic>
#include <unordered_map>
#include <mutex>
#include <condition_variable>
#include <memory>
#include <set>
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
    // This copy of the DLL serves the pipe. A startup probe cannot tell this server from another copy's when every
    // instance is busy, so UE5_StartPipeServer asks here first. [PIPE-PER-PROCESS-2026-11-06]
    bool IsRunning() const { return m_running.load(); }
    // Stopped, yet some connections from before the stop are still registered: Stop's drain gave up while their
    // handlers ran, so their pipe instances are still open and a probe of the name reads busy. That busy pipe is this
    // copy's own, and a restart must not defer to it. (Start clears the registry, so ask before starting.)
    // [PIPE-PER-PROCESS-2026-11-06]
    bool HasStragglerConnections() {
        std::lock_guard<std::mutex> lock(m_connMutex);
        return !m_running.load() && !m_conns.empty();
    }

private:
    // Max concurrent client connections ([PIPE-PER-PROCESS-2026-11-06] Stage B, 3 before):
    // the UI's 2 lanes (interactive + bulk), a rig (tools/verify/pipe_client.py), an MCP
    // client, and 2 for a two-lane reconnect whose old instances are still unwinding (each
    // is closed by its own thread after its command returns). The listener is itself an
    // instance, so this is the most clients at once. Each costs a thread and two
    // PIPE_BUF_SIZE buffers; the scan workers several heavy commands could spawn at once
    // are held by Aura's process-wide budget. It guards no race: [PIPEBUSY-2026-08-18] was
    // a logging bug, fixed by Voll. CE uses the Mimic mailbox, not the pipe.
    // ⚠ A name another copy of the DLL created first keeps THAT copy's limit (measured
    // 2026-11-10); the accept loop logs the limit in force.
    static constexpr DWORD kMaxPipeInstances = 6;
    // Windows takes 1..254; 255 is PIPE_UNLIMITED_INSTANCES.
    static_assert(kMaxPipeInstances >= 1 && kMaxPipeInstances < PIPE_UNLIMITED_INSTANCES,
                  "CreateNamedPipeW takes 1..254 instances");

    // Per-connection state. Each accepted client gets one of these, served by
    // its own HandleConnection thread on its own handle. Every write on a
    // connection comes from that thread -- replies, and since Stage C its watch
    // events (Kanne.h) -- and so does CloseConnOnce, so writeMutex is uncontended
    // today. Writes to DIFFERENT connections need no shared lock.
    struct Connection {
        HANDLE             pipe{INVALID_HANDLE_VALUE};
        // Monotonic id, assigned under m_connMutex at accept (audit #5 F2). Needed
        // because the per-command cancel is owned by the connections that RAISED it,
        // and a raw pointer cannot answer "is that same connection still live" once
        // the object is gone and the allocator has reused the address.
        uint64_t           seq{0};
        // Who connected, and when ([PIPE-PER-PROCESS-2026-11-06] Stage C: pipe_clients and the at-capacity line).
        // Written by the accept thread BEFORE the connection is registered in m_conns and never again, so a reader that
        // got it from m_conns needs no lock.
        DWORD              clientPid{0};      // GetNamedPipeClientProcessId; 0 when it failed
        std::string        clientExe{"?"};    // the client's exe file name, UTF-8, Voll::kMaxHolderExeBytes at most
        long long          connectedMs{0};    // steady-clock ms at accept
        // Who owns what this connection turns on: its client process, so the UI's two lanes are one owner
        // (Voll::ClientOwnerKey). Set with seq, under m_connMutex, before the connection is registered.
        uint64_t           ownerKey{0};
        // [PIPE-PER-PROCESS-2026-11-06] Stage D2-1: this connection marks its requests "background" (the MCP server).
        // Latched by its first such request; what its process holds is released when that process leaves.
        std::atomic<bool>  background{false};
        std::mutex         writeMutex;
        std::atomic<bool>  inFlight{false};   // true only while inside DispatchCommand
        // What that in-flight command IS, and when it started. Written just before
        // DispatchCommand (before inFlight goes up) and read by Stop's drain-timeout
        // diagnostic and by SnapshotHolders, so the hot path pays one small copy per
        // command and the teardown can finally name the connection that would not leave. "2 left" is not actionable; "2 left: lane
        // busy 4980 ms in 'walk_instance'" is. (2026-08-04: the first Stop ever logged
        // with connections attached timed out at the full 5 s and the log could not say
        // why.)
        // diagMutex keeps cmdName and cmdStartMs one pair: both are written under it (HandleConnection) and read
        // under it (SnapshotHolders), so a name never comes with another command's start.
        std::mutex         diagMutex;
        std::string        cmdName;           // last/current command on this connection
        std::atomic<long long> cmdStartMs{0}; // steady-clock ms when it began
        // Per-connection cooperative cancel. Set by MonitorLoop when THIS connection
        // breaks mid-command; read via Tot::Requested() by the handler thread, which
        // binds it with Tot::ConnectionCancelScope. Latched, never cleared -- an
        // orphaned scan must keep seeing it until it unwinds, and the flag outliving
        // the registry erase is free because the handler thread owns the shared_ptr.
        std::atomic<bool>  cancel{false};
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
            Writing,          // inside WriteLine (WriteFile to a client that may have stopped reading)
            StoppingWatches,  // cleanup: joining this connection's watch threads
            Unregistering,    // cleanup: erasing from m_conns / closing the handle
            Done,
        };
        std::atomic<Phase>     phase{Phase::Reading};
        std::atomic<long long> phaseStartMs{0};
    };

    static const char* PhaseName(Connection::Phase p);   // a sentence, for Stop's drain diagnostic
    static const char* PhaseKey(Connection::Phase p);    // a short stable name, for pipe_clients and the holders line
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
                                             // + m_cancelOwners + m_connSeq + m_tearingDown
    // Which connections raised the per-command cancel. Cleared when none of them is
    // still registered — NOT when the registry merely goes empty, which the two-lane
    // split made unreachable (audit #5 F2).
    std::vector<uint64_t>   m_cancelOwners;
    uint64_t                m_connSeq{0};    // assigned under m_connMutex at accept
    // Connections already erased from m_conns whose teardown still runs -- releasing what they owned can wait on the
    // game thread. Stop's drain waits for these too, so its own resets never run beside one.
    size_t                  m_tearingDown{0};
    // A connection's teardown counted in m_tearingDown for its whole length, a throw included: a count left raised
    // made every later Stop wait out its full drain (review, 2026-11-10). Constructed with m_connMutex held; released
    // on every way out, and Stop's drain told.
    struct TeardownCount {
        Fern& f;
        explicit TeardownCount(Fern& fern) : f(fern) { ++f.m_tearingDown; }
        ~TeardownCount() {
            {
                std::lock_guard<std::mutex> lock(f.m_connMutex);
                --f.m_tearingDown;
            }
            f.m_connCv.notify_all();
        }
        TeardownCount(const TeardownCount&) = delete;
        TeardownCount& operator=(const TeardownCount&) = delete;
    };

    // The most clients the pipe takes, as the first instance's handle states it (GetNamedPipeInfo): a name an older
    // copy of the DLL created keeps that copy's limit. This build's until the first instance exists.
    std::atomic<DWORD>      m_capInForce{kMaxPipeInstances};

    // [PIPE-PER-PROCESS-2026-11-06] Stage C: who holds the connections now, for pipe_clients and the at-capacity log
    // line. `selfSeq` marks the asking connection (0: none). Takes m_connMutex only to copy the list.
    std::vector<Voll::Holder> SnapshotHolders(uint64_t selfSeq);

    // Clear the per-command cancel once no connection that raised it is still live.
    // MUST be called with m_connMutex ALREADY HELD.
    void ReevaluatePerCommandCancel();

    // [PIPE-PER-PROCESS-2026-11-06] Stage C: what a client process turned on is its own, and goes when the last
    // connection of that process does. A connection's teardown calls this with its ownerKey. Takes m_profileMu and
    // m_connMutex (in that order) and Schlacht's lock; call it holding none of them.
    void ReleaseOwnedBy(uint64_t owner);
    bool OwnerStillConnected(uint64_t owner);   // takes m_connMutex

    // [PIPE-PER-PROCESS-2026-11-06] Stage D2-1: the owners (client processes) that mark their requests background, whose
    // holds (Voll::HoldLedger) go when they leave. Guarded by m_connMutex; an owner leaves the set when it leaves.
    std::set<uint64_t> m_backgroundOwners;
    bool IsBackgroundOwner(uint64_t owner);     // takes m_connMutex
    void MarkBackground(const std::shared_ptr<Connection>& conn);   // takes m_connMutex

    // The LiveFuncs recording: one writer at a time -- starting one joins the old arm worker and installs a new one,
    // and that pair is not atomic -- and the owner that started it (0: none). Lock order m_profileMu -> m_connMutex.
    std::mutex              m_profileMu;
    uint64_t                m_profileOwner{0};
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

    // Watches, keyed by (connection, address): each client's are its own. A watch's thread reads memory into its
    // slot and does nothing else; its connection's handler sends the samples (Kanne.h says why).
    struct WatchEntry {
        uintptr_t                         addr        = 0;
        uint32_t                          size        = 0;
        uint32_t                          interval_ms = 0;
        std::shared_ptr<Kanne::WatchSlot> slot;          // shared with the thread, which holds nothing else of ours
        Routine::SafeThread               watchThread;
    };
    std::unordered_map<Kanne::WatchKey, std::unique_ptr<WatchEntry>, Kanne::WatchKeyHash> m_watches;
    std::mutex m_watchMutex;

    // Initial Scan (async trigger_scan for proxy DLL mode). Its progress is Frieren's ScanProgress.
    struct ScanState {
        Routine::SingleFlightJob job{"RunScan"};
    };
    ScanState m_scan;
    bool RunScan();   // the job's body: UE5_Init's own answer

    // Extra Scan (user-triggered background rescan for missing pointers)
    struct RescanResult {
        uintptr_t   gobjects       = 0;
        uintptr_t   gworld         = 0;
        const char* gobjectsMethod = "not_found";
        const char* gworldMethod   = "not_found";
        bool        cancelled      = false;   // Stop or shutdown cut the sweep short: "not found" would be a guess
    };
    struct RescanState {
        Routine::SingleFlightJob job{"RunRescan"};
        std::atomic<int>  phase{0};       // 0=idle, 1=GObjects, 2=GWorld, 3=complete
        std::mutex        statusMutex;    // guards statusText and result
        std::string       statusText;
        RescanResult      result;
        RescanResult Results() {
            std::lock_guard<std::mutex> lock(statusMutex);
            return result;
        }
    };
    RescanState m_rescan;
    void RunRescanBody(bool scanGObjects, bool scanGWorld);

    void AcceptLoop();
    void HandleConnection(std::shared_ptr<Connection> conn);
    std::string DispatchCommand(const std::shared_ptr<Connection>& conn, const std::string& jsonLine);
    // Empty, or why the watch was refused.
    std::string StartWatch(const std::shared_ptr<Connection>& conn, uintptr_t addr, uint32_t size, uint32_t interval_ms);
    bool StopWatch(uint64_t connSeq, uintptr_t addr);   // false: this connection watches nothing there
    void StopWatchesForConnection(uint64_t connSeq);
    void StopAllWatches();
    static void JoinWatch(WatchEntry& entry);         // stop its thread and join it: one wake, at most one read
    // Send this connection's samples read since its last command, on its own handler thread.
    void FlushWatchEvents(Connection& conn);
    void CloseConnOnce(Connection& conn);
    bool WriteLine(Connection& conn, const std::string& line);
    std::string ReadLine(HANDLE pipe);
};
