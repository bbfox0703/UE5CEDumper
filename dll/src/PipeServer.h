#pragma once

// ============================================================
// PipeServer.h — Named Pipe IPC server
// ============================================================

#include <Windows.h>
#include <string>
#include <thread>
#include <atomic>
#include <unordered_map>
#include <mutex>

class PipeServer {
public:
    ~PipeServer() { Stop(); }
    bool Start();
    void Stop();
    bool IsClientConnected() const { return m_clientConnected.load(); }

    // Push an event to the connected client
    void PushEvent(const std::string& jsonLine);

private:
    std::thread        m_acceptThread;
    std::atomic<bool>  m_running{false};
    std::atomic<bool>  m_clientConnected{false};
    HANDLE             m_pipe{INVALID_HANDLE_VALUE};
    std::mutex         m_pipeMutex;     // Protects m_pipe access across threads
    std::mutex         m_writeMutex;

    // Watch entries
    struct WatchEntry {
        uintptr_t           addr;
        uint32_t            size;
        uint32_t            interval_ms;
        std::thread         watchThread;
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
        std::thread       scanThread;
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
        std::thread       scanThread;
    };
    RescanState m_rescan;
    void RunRescan(bool scanGObjects, bool scanGWorld);

    void AcceptLoop();
    void HandleClient(HANDLE pipe);
    std::string DispatchCommand(const std::string& jsonLine);
    void StartWatch(uintptr_t addr, uint32_t size, uint32_t interval_ms);
    void StopWatch(uintptr_t addr);
    void StopAllWatches();
    bool WriteLine(HANDLE pipe, const std::string& line);
    std::string ReadLine(HANDLE pipe);
};
