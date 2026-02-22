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

    void AcceptLoop();
    void HandleClient(HANDLE pipe);
    std::string DispatchCommand(const std::string& jsonLine);
    void StartWatch(uintptr_t addr, uint32_t size, uint32_t interval_ms);
    void StopWatch(uintptr_t addr);
    void StopAllWatches();
    bool WriteLine(HANDLE pipe, const std::string& line);
    std::string ReadLine(HANDLE pipe);
};
