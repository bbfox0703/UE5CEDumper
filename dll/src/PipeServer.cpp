// ============================================================
// PipeServer.cpp — Named Pipe IPC server implementation
// ============================================================

#include "PipeServer.h"
#include "PipeProtocol.h"
#include "Constants.h"
#include "Logger.h"
#include "Memory.h"
#include "OffsetFinder.h"
#include "ObjectArray.h"
#include "FNamePool.h"
#include "UStructWalker.h"

#include <json.hpp>
#include <chrono>
#include <vector>

using json = nlohmann::json;

bool PipeServer::Start() {
    if (m_running.load()) {
        LOG_WARN("PipeServer: Already running");
        return true;
    }

    m_running = true;
    m_acceptThread = std::thread(&PipeServer::AcceptLoop, this);

    LOG_INFO("PipeServer: Started on %ls", Constants::PIPE_NAME);
    return true;
}

void PipeServer::Stop() {
    m_running = false;
    StopAllWatches();

    // Close the pipe to unblock ConnectNamedPipe
    if (m_pipe != INVALID_HANDLE_VALUE) {
        DisconnectNamedPipe(m_pipe);
        CloseHandle(m_pipe);
        m_pipe = INVALID_HANDLE_VALUE;
    }

    if (m_acceptThread.joinable()) {
        m_acceptThread.join();
    }

    m_clientConnected = false;
    LOG_INFO("PipeServer: Stopped");
}

void PipeServer::AcceptLoop() {
    while (m_running.load()) {
        // Create a new pipe instance
        HANDLE pipe = CreateNamedPipeW(
            Constants::PIPE_NAME,
            PIPE_ACCESS_DUPLEX,
            PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT,
            1,                          // Max instances
            Constants::PIPE_BUF_SIZE,
            Constants::PIPE_BUF_SIZE,
            0,                          // Default timeout
            nullptr                     // Default security
        );

        if (pipe == INVALID_HANDLE_VALUE) {
            LOG_ERROR("PipeServer: CreateNamedPipe failed (err=%lu)", GetLastError());
            std::this_thread::sleep_for(std::chrono::seconds(1));
            continue;
        }

        m_pipe = pipe;
        LOG_INFO("PipeServer: Waiting for client connection...");

        // Wait for a client to connect
        BOOL connected = ConnectNamedPipe(pipe, nullptr);
        if (!connected && GetLastError() != ERROR_PIPE_CONNECTED) {
            if (!m_running.load()) break; // Normal shutdown
            LOG_ERROR("PipeServer: ConnectNamedPipe failed (err=%lu)", GetLastError());
            CloseHandle(pipe);
            continue;
        }

        if (!m_running.load()) {
            CloseHandle(pipe);
            break;
        }

        LOG_INFO("PipeServer: Client connected");
        m_clientConnected = true;

        HandleClient(pipe);

        // Client disconnected
        m_clientConnected = false;
        StopAllWatches();
        DisconnectNamedPipe(pipe);
        CloseHandle(pipe);
        m_pipe = INVALID_HANDLE_VALUE;

        LOG_INFO("PipeServer: Client disconnected");
    }
}

std::string PipeServer::ReadLine(HANDLE pipe) {
    std::string line;
    char ch;
    DWORD bytesRead;

    while (m_running.load()) {
        if (!ReadFile(pipe, &ch, 1, &bytesRead, nullptr) || bytesRead == 0) {
            return ""; // Disconnected or error
        }
        if (ch == '\n') {
            // Strip trailing \r if present
            if (!line.empty() && line.back() == '\r') {
                line.pop_back();
            }
            return line;
        }
        line += ch;

        // Safety: don't let a line grow unbounded
        if (line.size() > Constants::PIPE_BUF_SIZE) {
            LOG_WARN("PipeServer: Line too long, dropping");
            return "";
        }
    }
    return "";
}

bool PipeServer::WriteLine(HANDLE pipe, const std::string& line) {
    std::lock_guard<std::mutex> lock(m_writeMutex);
    std::string data = line + "\n";
    DWORD written;
    return WriteFile(pipe, data.c_str(), static_cast<DWORD>(data.size()), &written, nullptr) != 0;
}

void PipeServer::HandleClient(HANDLE pipe) {
    while (m_running.load()) {
        std::string line = ReadLine(pipe);
        if (line.empty()) break; // Disconnected

        LOG_DEBUG("PipeServer: Received: %s", line.c_str());

        std::string response = DispatchCommand(line);
        if (!response.empty()) {
            if (!WriteLine(pipe, response)) {
                LOG_ERROR("PipeServer: Failed to write response");
                break;
            }
        }
    }
}

void PipeServer::PushEvent(const std::string& jsonLine) {
    if (!m_clientConnected.load() || m_pipe == INVALID_HANDLE_VALUE) return;
    WriteLine(m_pipe, jsonLine);
}

std::string PipeServer::DispatchCommand(const std::string& jsonLine) {
    json request;
    try {
        request = json::parse(jsonLine);
    } catch (const json::exception& e) {
        LOG_ERROR("PipeServer: JSON parse error: %s", e.what());
        return PipeProtocol::MakeError(0, "Invalid JSON").dump();
    }

    int id = request.value("id", 0);
    std::string cmd = request.value("cmd", "");

    try {
        if (cmd == PipeProtocol::CMD_INIT) {
            json data;
            data["ue_version"] = 0; // Already initialized via ExportAPI
            // Re-report cached pointers
            return PipeProtocol::MakeResponse(id, data).dump();
        }

        if (cmd == PipeProtocol::CMD_GET_POINTERS) {
            // These are filled by ExportAPI's cached EnginePointers
            extern uintptr_t g_cachedGObjects;
            extern uintptr_t g_cachedGNames;
            extern uint32_t  g_cachedUEVersion;

            json data;
            data["gobjects"]     = PipeProtocol::AddrToStr(g_cachedGObjects);
            data["gnames"]       = PipeProtocol::AddrToStr(g_cachedGNames);
            data["object_count"] = ObjectArray::GetCount();
            return PipeProtocol::MakeResponse(id, data).dump();
        }

        if (cmd == PipeProtocol::CMD_GET_OBJECT_COUNT) {
            json data;
            data["count"] = ObjectArray::GetCount();
            return PipeProtocol::MakeResponse(id, data).dump();
        }

        if (cmd == PipeProtocol::CMD_GET_OBJECT_LIST) {
            int offset = request.value("offset", 0);
            int limit  = request.value("limit", 200);
            int total  = ObjectArray::GetCount();

            json objects = json::array();
            int end = (std::min)(offset + limit, total);

            for (int i = offset; i < end; ++i) {
                uintptr_t obj = ObjectArray::GetByIndex(i);
                if (!obj) continue;

                json item;
                item["addr"]  = PipeProtocol::AddrToStr(obj);
                item["name"]  = UStructWalker::GetName(obj);

                uintptr_t cls = UStructWalker::GetClass(obj);
                item["class"] = cls ? UStructWalker::GetName(cls) : "";

                uintptr_t outer = UStructWalker::GetOuter(obj);
                item["outer"] = PipeProtocol::AddrToStr(outer);

                objects.push_back(item);
            }

            json data;
            data["total"]   = total;
            data["objects"] = objects;
            return PipeProtocol::MakeResponse(id, data).dump();
        }

        if (cmd == PipeProtocol::CMD_GET_OBJECT) {
            std::string addrStr = request.value("addr", "");
            if (addrStr.empty()) return PipeProtocol::MakeError(id, "Missing addr").dump();

            uintptr_t addr = PipeProtocol::StrToAddr(addrStr);
            json data;
            data["addr"]      = addrStr;
            data["name"]      = UStructWalker::GetName(addr);
            data["full_name"] = UStructWalker::GetFullName(addr);

            uintptr_t cls = UStructWalker::GetClass(addr);
            data["class"]      = cls ? UStructWalker::GetName(cls) : "";
            data["class_addr"] = PipeProtocol::AddrToStr(cls);

            uintptr_t outer = UStructWalker::GetOuter(addr);
            data["outer"]      = outer ? UStructWalker::GetName(outer) : "";
            data["outer_addr"] = PipeProtocol::AddrToStr(outer);

            return PipeProtocol::MakeResponse(id, data).dump();
        }

        if (cmd == PipeProtocol::CMD_FIND_OBJECT) {
            std::string path = request.value("path", "");
            if (path.empty()) return PipeProtocol::MakeError(id, "Missing path").dump();

            uintptr_t obj = ObjectArray::FindByName(path);
            if (!obj) return PipeProtocol::MakeError(id, "Object not found").dump();

            json data;
            data["addr"] = PipeProtocol::AddrToStr(obj);
            data["name"] = UStructWalker::GetName(obj);
            return PipeProtocol::MakeResponse(id, data).dump();
        }

        if (cmd == PipeProtocol::CMD_WALK_CLASS) {
            std::string addrStr = request.value("addr", "");
            if (addrStr.empty()) return PipeProtocol::MakeError(id, "Missing addr").dump();

            uintptr_t addr = PipeProtocol::StrToAddr(addrStr);
            ClassInfo ci = UStructWalker::WalkClass(addr);

            json classData;
            classData["name"]       = ci.Name;
            classData["full_path"]  = ci.FullPath;
            classData["super_addr"] = PipeProtocol::AddrToStr(ci.SuperClass);
            classData["super_name"] = ci.SuperName;
            classData["props_size"] = ci.PropertiesSize;

            json fields = json::array();
            for (const auto& f : ci.Fields) {
                fields.push_back({
                    {"addr",   PipeProtocol::AddrToStr(f.Address)},
                    {"name",   f.Name},
                    {"type",   f.TypeName},
                    {"offset", f.Offset},
                    {"size",   f.Size}
                });
            }
            classData["fields"] = fields;

            json data;
            data["class"] = classData;
            return PipeProtocol::MakeResponse(id, data).dump();
        }

        if (cmd == PipeProtocol::CMD_READ_MEM) {
            std::string addrStr = request.value("addr", "");
            int size = request.value("size", 256);
            if (addrStr.empty()) return PipeProtocol::MakeError(id, "Missing addr").dump();
            if (size <= 0 || size > 65536) return PipeProtocol::MakeError(id, "Invalid size").dump();

            uintptr_t addr = PipeProtocol::StrToAddr(addrStr);
            std::vector<uint8_t> buf(size);
            if (!Mem::ReadBytesSafe(addr, buf.data(), size)) {
                return PipeProtocol::MakeError(id, "Read failed").dump();
            }

            json data;
            data["bytes"] = PipeProtocol::BytesToHex(buf.data(), buf.size());
            return PipeProtocol::MakeResponse(id, data).dump();
        }

        if (cmd == PipeProtocol::CMD_WRITE_MEM) {
            std::string addrStr = request.value("addr", "");
            std::string hexBytes = request.value("bytes", "");
            if (addrStr.empty() || hexBytes.empty()) {
                return PipeProtocol::MakeError(id, "Missing addr or bytes").dump();
            }

            uintptr_t addr = PipeProtocol::StrToAddr(addrStr);
            auto bytes = PipeProtocol::HexToBytes(hexBytes);
            if (!Mem::WriteBytes(addr, bytes.data(), bytes.size())) {
                return PipeProtocol::MakeError(id, "Write failed").dump();
            }

            return PipeProtocol::MakeResponse(id).dump();
        }

        if (cmd == PipeProtocol::CMD_WATCH) {
            std::string addrStr = request.value("addr", "");
            int size = request.value("size", 4);
            int interval = request.value("interval_ms", 500);
            if (addrStr.empty()) return PipeProtocol::MakeError(id, "Missing addr").dump();

            uintptr_t addr = PipeProtocol::StrToAddr(addrStr);
            StartWatch(addr, size, interval);
            return PipeProtocol::MakeResponse(id).dump();
        }

        if (cmd == PipeProtocol::CMD_UNWATCH) {
            std::string addrStr = request.value("addr", "");
            if (addrStr.empty()) return PipeProtocol::MakeError(id, "Missing addr").dump();

            uintptr_t addr = PipeProtocol::StrToAddr(addrStr);
            StopWatch(addr);
            return PipeProtocol::MakeResponse(id).dump();
        }

        return PipeProtocol::MakeError(id, "Unknown command: " + cmd).dump();

    } catch (const std::exception& e) {
        LOG_ERROR("PipeServer: Exception in command '%s': %s", cmd.c_str(), e.what());
        return PipeProtocol::MakeError(id, std::string("Internal error: ") + e.what()).dump();
    }
}

void PipeServer::StartWatch(uintptr_t addr, uint32_t size, uint32_t interval_ms) {
    StopWatch(addr); // Stop existing watch on same address

    std::lock_guard<std::mutex> lock(m_watchMutex);

    auto entry = std::make_unique<WatchEntry>();
    entry->addr = addr;
    entry->size = size;
    entry->interval_ms = interval_ms;
    entry->active = true;

    WatchEntry* ptr = entry.get();
    entry->watchThread = std::thread([this, ptr]() {
        std::vector<uint8_t> buf(ptr->size);
        while (ptr->active.load() && m_running.load()) {
            if (Mem::ReadBytesSafe(ptr->addr, buf.data(), ptr->size)) {
                json data;
                data["addr"]      = PipeProtocol::AddrToStr(ptr->addr);
                data["bytes"]     = PipeProtocol::BytesToHex(buf.data(), buf.size());
                data["timestamp"] = std::chrono::duration_cast<std::chrono::milliseconds>(
                    std::chrono::system_clock::now().time_since_epoch()).count();

                PushEvent(PipeProtocol::MakeEvent(PipeProtocol::EVT_WATCH, data).dump());
            }
            std::this_thread::sleep_for(std::chrono::milliseconds(ptr->interval_ms));
        }
    });

    m_watches[addr] = std::move(entry);
    LOG_INFO("PipeServer: Watch started on 0x%llX (size=%u, interval=%ums)",
             static_cast<unsigned long long>(addr), size, interval_ms);
}

void PipeServer::StopWatch(uintptr_t addr) {
    std::lock_guard<std::mutex> lock(m_watchMutex);
    auto it = m_watches.find(addr);
    if (it != m_watches.end()) {
        it->second->active = false;
        if (it->second->watchThread.joinable()) {
            it->second->watchThread.join();
        }
        m_watches.erase(it);
        LOG_INFO("PipeServer: Watch stopped on 0x%llX", static_cast<unsigned long long>(addr));
    }
}

void PipeServer::StopAllWatches() {
    std::lock_guard<std::mutex> lock(m_watchMutex);
    for (auto& [addr, entry] : m_watches) {
        entry->active = false;
        if (entry->watchThread.joinable()) {
            entry->watchThread.join();
        }
    }
    m_watches.clear();
}
