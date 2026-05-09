// ============================================================
// Fern — 費倫 (芙莉蓮的弟子 — Frieren's Apprentice)
// PipeServer: Named Pipe JSON IPC implementation
// ============================================================

#include "Fern.h"
#include "Renge.h"
#include "Grimoire.h"
#define LOG_CAT "PIPE:svr"
#include "Sein.h"
#include "Macht.h"
#include "Genau.h"
#include "Aura.h"
#include "Serie.h"
#include "Ubel.h"
#include "Flamme.h"
#include "BuildInfo.h"

#include <json.hpp>
#include <chrono>
#include <vector>

using json = nlohmann::json;

// Forward declare ExportAPI functions (extern "C" must be at global scope)
extern "C" bool      UE5_Init();
extern "C" uintptr_t UE5_FindInstanceOfClass(const char* className);
extern "C" uintptr_t UE5_GetObjectClass(uintptr_t obj);
extern "C" uintptr_t UE5_FindFunctionByName(uintptr_t classAddr, const char* funcName);
extern "C" int32_t   UE5_CallProcessEvent(uintptr_t instance, uintptr_t ufunc, uintptr_t params);

// ScanProgress — global progress state updated by UE5_Init(), read by scan_status
namespace ScanProgress {
    extern std::atomic<int>  phase;
    extern std::string       statusText;
    extern std::mutex        statusMutex;
    std::string GetStatusText();
}

bool Fern::Start() {
    if (m_running.load()) {
        LOG_WARN("PipeServer: Already running");
        return true;
    }

    m_running = true;
    m_acceptThread = std::thread(&Fern::AcceptLoop, this);

    LOG_INFO("PipeServer: Started on %ls", Grimoire::PIPE_NAME);
    return true;
}

void Fern::Stop() {
    if (!m_running.exchange(false)) return; // Already stopped
    StopAllWatches();

    // Join background rescan thread if running
    m_rescan.running.store(false);
    if (m_rescan.scanThread.joinable()) {
        m_rescan.scanThread.join();
    }

    // Audit fix #3: join the initial-scan thread too. Previously this was
    // missed, so destructing the static s_pipeServer with a still-joinable
    // m_scan.scanThread would call ~thread → std::terminate. RunScan() runs
    // UE5_Init (a bounded AOB scan, typically 2-8s) and exits naturally;
    // we have no signal to abort it mid-flight, so just wait for it.
    m_scan.running.store(false);
    if (m_scan.scanThread.joinable()) {
        m_scan.scanThread.join();
    }

    // Close the pipe to unblock ConnectNamedPipe / ReadFile
    {
        std::lock_guard<std::mutex> lock(m_pipeMutex);
        if (m_pipe != INVALID_HANDLE_VALUE) {
            DisconnectNamedPipe(m_pipe);
            CloseHandle(m_pipe);
            m_pipe = INVALID_HANDLE_VALUE;
        }
    }

    if (m_acceptThread.joinable()) {
        m_acceptThread.join();
    }

    m_clientConnected = false;
    LOG_INFO("PipeServer: Stopped");
}

void Fern::AcceptLoop() {
    while (m_running.load()) {
        // Create a new pipe instance
        HANDLE pipe = CreateNamedPipeW(
            Grimoire::PIPE_NAME,
            PIPE_ACCESS_DUPLEX,
            PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT,
            1,                          // Max instances
            Grimoire::PIPE_BUF_SIZE,
            Grimoire::PIPE_BUF_SIZE,
            0,                          // Default timeout
            nullptr                     // Default security
        );

        if (pipe == INVALID_HANDLE_VALUE) {
            LOG_ERROR("PipeServer: CreateNamedPipe failed (err=%lu)", GetLastError());
            std::this_thread::sleep_for(std::chrono::seconds(1));
            continue;
        }

        {
            std::lock_guard<std::mutex> lock(m_pipeMutex);
            m_pipe = pipe;
        }
        LOG_INFO("PipeServer: Waiting for client connection...");

        // Wait for a client to connect
        BOOL connected = ConnectNamedPipe(pipe, nullptr);
        if (!connected && GetLastError() != ERROR_PIPE_CONNECTED) {
            if (!m_running.load()) break; // Normal shutdown
            LOG_ERROR("PipeServer: ConnectNamedPipe failed (err=%lu)", GetLastError());
            std::lock_guard<std::mutex> lock(m_pipeMutex);
            if (m_pipe == pipe) { CloseHandle(pipe); m_pipe = INVALID_HANDLE_VALUE; }
            continue;
        }

        if (!m_running.load()) {
            std::lock_guard<std::mutex> lock(m_pipeMutex);
            if (m_pipe == pipe) { CloseHandle(pipe); m_pipe = INVALID_HANDLE_VALUE; }
            break;
        }

        LOG_INFO("PipeServer: Client connected");
        m_clientConnected = true;

        HandleClient(pipe);

        // Client disconnected
        m_clientConnected = false;
        StopAllWatches();
        {
            std::lock_guard<std::mutex> lock(m_pipeMutex);
            // Only close if Stop() hasn't already closed it
            if (m_pipe == pipe) {
                DisconnectNamedPipe(pipe);
                CloseHandle(pipe);
                m_pipe = INVALID_HANDLE_VALUE;
            }
        }

        LOG_INFO("PipeServer: Client disconnected");
    }
}

std::string Fern::ReadLine(HANDLE pipe) {
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
        if (line.size() > Grimoire::PIPE_BUF_SIZE) {
            Sein::Warn("PIPE:cmd", "PipeServer: Line too long, dropping");
            return "";
        }
    }
    return "";
}

bool Fern::WriteLine(HANDLE pipe, const std::string& line) {
    std::lock_guard<std::mutex> lock(m_writeMutex);
    std::string data = line + "\n";
    DWORD written;
    return WriteFile(pipe, data.c_str(), static_cast<DWORD>(data.size()), &written, nullptr) != 0;
}

void Fern::HandleClient(HANDLE pipe) {
    // Batch-suppress repetitive command logging (e.g. 244 x get_object_list)
    std::string lastCmd;
    int repeatCount = 0;

    auto flushRepeat = [&]() {
        if (repeatCount > 1) {
            Sein::Debug("PIPE:cmd", "PipeServer: ... repeated %dx: %s", repeatCount, lastCmd.c_str());
        }
        repeatCount = 0;
        lastCmd.clear();
    };

    while (m_running.load()) {
        std::string line = ReadLine(pipe);
        if (line.empty()) { flushRepeat(); break; } // Disconnected

        // Extract command name for dedup (fast: find "cmd":" in JSON)
        std::string cmd;
        auto pos = line.find("\"cmd\":\"");
        if (pos != std::string::npos) {
            auto start = pos + 7;
            auto end = line.find('"', start);
            if (end != std::string::npos) cmd = line.substr(start, end - start);
        }

        if (cmd == lastCmd && !cmd.empty()) {
            ++repeatCount;
        } else {
            flushRepeat();
            Sein::Debug("PIPE:cmd", "PipeServer: Received: %s", line.c_str());
            lastCmd = cmd;
            repeatCount = 1;
        }

        std::string response = DispatchCommand(line);
        if (!response.empty()) {
            if (!WriteLine(pipe, response)) {
                flushRepeat();
                Sein::Error("PIPE:cmd", "PipeServer: Failed to write response");
                break;
            }
        }
    }
}

void Fern::PushEvent(const std::string& jsonLine) {
    if (!m_clientConnected.load()) return;
    HANDLE pipe;
    {
        std::lock_guard<std::mutex> lock(m_pipeMutex);
        pipe = m_pipe;
    }
    if (pipe == INVALID_HANDLE_VALUE) return;
    WriteLine(pipe, jsonLine);
}

// ============================================================
// SerializeField — Convert a LiveFieldValue to JSON.
// Shared by walk_instance and walk_datatable_rows handlers.
// ============================================================
static json SerializeField(const Ubel::LiveFieldValue& fv) {
    json fj;
    fj["name"]   = fv.name;
    fj["type"]   = fv.typeName;
    fj["offset"] = fv.offset;
    fj["size"]   = fv.size;

    if (!fv.hexValue.empty())   fj["hex"]   = fv.hexValue;
    if (!fv.typedValue.empty())  fj["value"] = fv.typedValue;
    if (fv.guessed)              fj["guessed"] = true;

    // ObjectProperty: pointer info
    if (fv.ptrValue != 0) {
        fj["ptr"]       = Renge::AddrToStr(fv.ptrValue);
        fj["ptr_name"]  = fv.ptrName;
        fj["ptr_class"] = fv.ptrClassName;
        if (fv.ptrClassAddr)
            fj["ptr_class_addr"] = Renge::AddrToStr(fv.ptrClassAddr);
    }

    // BoolProperty: bit field info
    if (fv.boolBitIndex >= 0) {
        fj["bool_bit"] = fv.boolBitIndex;
        fj["bool_mask"] = fv.boolFieldMask;
        fj["bool_byte_offset"] = fv.boolByteOffset;
    }

    // ArrayProperty: element count + inner type info + inline elements
    if (fv.arrayCount >= 0) {
        fj["count"] = fv.arrayCount;
        if (fv.arrayDataAddr != 0)
            fj["array_data_addr"] = Renge::AddrToStr(fv.arrayDataAddr);
        if (!fv.arrayInnerType.empty()) {
            fj["array_inner_type"] = fv.arrayInnerType;
            if (fv.arrayElemSize > 0)
                fj["array_elem_size"] = fv.arrayElemSize;
            if (!fv.arrayInnerStructType.empty())
                fj["array_struct_type"] = fv.arrayInnerStructType;
            if (fv.arrayInnerStructAddr != 0)
                fj["array_struct_class_addr"] = Renge::AddrToStr(fv.arrayInnerStructAddr);
            // Phase G layout metadata for soft arrays — lets exporters lay out
            // per-element FName leaves at FSoftObjectPath sub-offsets.
            if (fv.softArrayFNameSize > 0) {
                fj["soft_fname_size"] = fv.softArrayFNameSize;
                fj["soft_top_level_asset_path"] = fv.softArrayIsTopLevelAssetPath;
            }
        }
        if (fv.arrayInnerFFieldAddr != 0)
            fj["array_inner_addr"] = Renge::AddrToStr(fv.arrayInnerFFieldAddr);
        // Phase B/D: inline element values (scalar or pointer)
        if (!fv.arrayElements.empty()) {
            json elems = json::array();
            for (const auto& e : fv.arrayElements) {
                json ej;
                ej["i"] = e.index;
                ej["v"] = e.value;
                ej["h"] = e.hex;
                if (!e.enumName.empty())
                    ej["en"] = e.enumName;
                if (e.rawIntValue != 0 || !e.enumName.empty())
                    ej["rv"] = e.rawIntValue;
                // Phase D: pointer element fields
                if (e.ptrAddr != 0) {
                    ej["pa"] = Renge::AddrToStr(e.ptrAddr);
                    ej["pn"] = e.ptrName;
                    ej["pc"] = e.ptrClassName;
                }
                // Phase F: struct sub-fields
                if (!e.structFields.empty()) {
                    json sfs = json::array();
                    for (const auto& sf : e.structFields) {
                        json sfj = {{"n", sf.name}, {"t", sf.typeName},
                                    {"o", sf.offset}, {"s", sf.size}, {"v", sf.value}};
                        // Pointer resolution for ObjectProperty sub-fields
                        if (sf.ptrAddr != 0) {
                            sfj["pa"] = Renge::AddrToStr(sf.ptrAddr);
                            sfj["pn"] = sf.ptrName;
                            sfj["pc"] = sf.ptrClassName;
                            sfj["pca"] = Renge::AddrToStr(sf.ptrClassAddr);
                        }
                        sfs.push_back(sfj);
                    }
                    ej["sf"] = sfs;
                }
                elems.push_back(ej);
            }
            fj["elements"] = elems;
        }

        // CE DropDownList: full enum entries for this array field
        if (fv.arrayEnumAddr != 0 && !fv.arrayEnumEntries.empty()) {
            fj["enum_addr"] = Renge::AddrToStr(fv.arrayEnumAddr);
            json entries = json::array();
            for (const auto& ee : fv.arrayEnumEntries)
                entries.push_back({{"v", ee.value}, {"n", ee.name}});
            fj["enum_entries"] = entries;
        }
    }

    // MapProperty: key/value type info + inline elements
    if (fv.mapCount >= 0) {
        fj["map_count"]      = fv.mapCount;
        fj["map_key_type"]   = fv.mapKeyType;
        fj["map_value_type"] = fv.mapValueType;
        fj["map_key_size"]   = fv.mapKeySize;
        fj["map_value_size"] = fv.mapValueSize;
        if (fv.mapValueOffset != 0)
            fj["map_value_offset"] = fv.mapValueOffset;
        if (fv.mapDataAddr != 0)
            fj["map_data_addr"] = Renge::AddrToStr(fv.mapDataAddr);
        if (fv.mapKeyStructAddr != 0) {
            fj["map_key_struct_addr"] = Renge::AddrToStr(fv.mapKeyStructAddr);
            fj["map_key_struct_type"] = fv.mapKeyStructType;
        }
        if (fv.mapValueStructAddr != 0) {
            fj["map_value_struct_addr"] = Renge::AddrToStr(fv.mapValueStructAddr);
            fj["map_value_struct_type"] = fv.mapValueStructType;
        }
        if (!fv.containerElements.empty()) {
            json elems = json::array();
            for (const auto& e : fv.containerElements) {
                json ej;
                ej["i"] = e.index;
                ej["k"] = e.key;
                ej["v"] = e.value;
                if (!e.keyHex.empty())   ej["kh"] = e.keyHex;
                if (!e.valueHex.empty()) ej["vh"] = e.valueHex;
                if (!e.keyPtrName.empty())   ej["kn"] = e.keyPtrName;
                if (e.keyPtrAddr != 0)       ej["ka"] = Renge::AddrToStr(e.keyPtrAddr);
                if (!e.keyPtrClassName.empty()) ej["kc"] = e.keyPtrClassName;
                if (!e.valuePtrName.empty()) ej["vn"] = e.valuePtrName;
                if (e.valuePtrAddr != 0)     ej["va"] = Renge::AddrToStr(e.valuePtrAddr);
                if (!e.valuePtrClassName.empty()) ej["vc"] = e.valuePtrClassName;
                elems.push_back(ej);
            }
            fj["map_elements"] = elems;
        }
    }

    // SetProperty: element type info + inline elements
    if (fv.setCount >= 0) {
        fj["set_count"]     = fv.setCount;
        fj["set_elem_type"] = fv.setElemType;
        fj["set_elem_size"] = fv.setElemSize;
        if (fv.setDataAddr != 0)
            fj["set_data_addr"] = Renge::AddrToStr(fv.setDataAddr);
        if (fv.setElemStructAddr != 0) {
            fj["set_elem_struct_addr"] = Renge::AddrToStr(fv.setElemStructAddr);
            fj["set_elem_struct_type"] = fv.setElemStructType;
        }
        if (!fv.containerElements.empty()) {
            json elems = json::array();
            for (const auto& e : fv.containerElements) {
                json ej;
                ej["i"] = e.index;
                ej["k"] = e.key;
                if (!e.keyHex.empty()) ej["kh"] = e.keyHex;
                if (!e.keyPtrName.empty()) ej["kn"] = e.keyPtrName;
                if (e.keyPtrAddr != 0)    ej["ka"] = Renge::AddrToStr(e.keyPtrAddr);
                if (!e.keyPtrClassName.empty()) ej["kc"] = e.keyPtrClassName;
                elems.push_back(ej);
            }
            fj["set_elements"] = elems;
        }
    }

    // StructProperty: inner struct info
    if (fv.structDataAddr != 0) {
        fj["struct_data_addr"]  = Renge::AddrToStr(fv.structDataAddr);
        fj["struct_class_addr"] = Renge::AddrToStr(fv.structClassAddr);
        fj["struct_type"]       = fv.structTypeName;
    }

    // EnumProperty / ByteProperty-with-enum: resolved name, value, and full entries
    if (!fv.enumName.empty()) {
        fj["enum_name"]  = fv.enumName;
        fj["enum_value"] = fv.enumValue;
    }
    if (fv.enumAddr != 0 && !fv.enumEntries.empty()) {
        fj["enum_addr"] = Renge::AddrToStr(fv.enumAddr);
        json enumEntries = json::array();
        for (const auto& ee : fv.enumEntries)
            enumEntries.push_back({{"v", ee.value}, {"n", ee.name}});
        fj["enum_entries"] = enumEntries;
    }

    // StrProperty: decoded string value
    if (!fv.strValue.empty()) {
        fj["str_value"] = fv.strValue;
    }

    return fj;
}

std::string Fern::DispatchCommand(const std::string& jsonLine) {
    json request;
    try {
        request = json::parse(jsonLine);
    } catch (const json::exception& e) {
        Sein::Error("PIPE:cmd", "PipeServer: JSON parse error: %s", e.what());
        return Renge::MakeError(0, "Invalid JSON").dump();
    }

    int id = request.value("id", 0);
    std::string cmd = request.value("cmd", "");

    try {
        if (cmd == Renge::CMD_INIT) {
            extern uint32_t g_cachedUEVersion;
            extern bool     g_cachedVersionDetected;
            extern bool     g_cachedIsUserOverride;
            extern bool     g_cachedIsLowConfidence;
            extern const char* g_cachedPublisherThumbprint;
            json data;
            data["ue_version"]       = g_cachedUEVersion;
            data["version_detected"] = g_cachedVersionDetected;
            data["is_user_override"] = g_cachedIsUserOverride;
            data["is_low_confidence"] = g_cachedIsLowConfidence;
            data["publisher_thumbprint"] = g_cachedPublisherThumbprint
                ? g_cachedPublisherThumbprint : "";
            data["build_git"]  = BUILD_GIT_SHORT;
            data["build_hash"] = BUILD_GIT_HASH;
            data["build_time"] = BUILD_TIMESTAMP;
            data["build_info"] = BUILD_VERSION_STRING;
            return Renge::MakeResponse(id, data).dump();
        }

        // ─────────────────────────────────────────────────────────────────
        // set_ue_version_override { version: int, persist: bool }
        //   version == 0  → clear the override (revert to auto-detect on next launch)
        //   version != 0  → record as the persistent override for this game
        //   persist=false → only update the in-process cached version (no disk write)
        //
        // Updates g_cachedUEVersion immediately so version-dependent code paths
        // (Soft array CE XML layout, FProperty offset selection, etc.) start
        // using the new value on the next request — no re-init / re-scan needed.
        // ─────────────────────────────────────────────────────────────────
        if (cmd == Renge::CMD_SET_UE_VERSION_OVERRIDE) {
            extern uint32_t    g_cachedUEVersion;
            extern bool        g_cachedVersionDetected;
            extern bool        g_cachedIsUserOverride;
            extern bool        g_cachedIsLowConfidence;
            extern char        g_cachedPeHash[17];

            int     newVersion = request.value("version", 0);
            bool    persist    = request.value("persist", true);

            // Defensive bounds — UE 4.18 .. 5.9 plus 0 (clear).
            if (newVersion != 0 && (newVersion < 418 || newVersion > 509)) {
                return Renge::MakeError(id,
                    "version out of supported range (418..509 or 0 to clear)").dump();
            }

            if (persist) {
                // Resolve a process name for the JSON record's gameName.
                wchar_t exeW[MAX_PATH] = {};
                GetModuleFileNameW(nullptr, exeW, MAX_PATH);
                std::wstring exePath(exeW);
                auto lastSlash = exePath.find_last_of(L"\\/");
                std::wstring fileName = (lastSlash != std::wstring::npos)
                    ? exePath.substr(lastSlash + 1) : exePath;
                std::string nameUtf8;
                int sz = WideCharToMultiByte(CP_UTF8, 0, fileName.c_str(), -1,
                                             nullptr, 0, nullptr, nullptr);
                if (sz > 0) {
                    nameUtf8.resize(sz - 1);
                    WideCharToMultiByte(CP_UTF8, 0, fileName.c_str(), -1,
                                        nameUtf8.data(), sz, nullptr, nullptr);
                }
                Flamme::SaveUserOverride(g_cachedPeHash,
                                         static_cast<uint32_t>(newVersion),
                                         nameUtf8.c_str());
            }

            if (newVersion == 0) {
                // Clear: don't change the in-process version (would require re-init);
                // just clear the override flag so UI shows "auto-detected" branding.
                g_cachedIsUserOverride = false;
            } else {
                g_cachedUEVersion       = static_cast<uint32_t>(newVersion);
                g_cachedVersionDetected = true;
                g_cachedIsUserOverride  = true;
                g_cachedIsLowConfidence = false;
            }

            json data;
            data["ue_version"]       = g_cachedUEVersion;
            data["is_user_override"] = g_cachedIsUserOverride;
            data["persisted"]        = persist;
            return Renge::MakeResponse(id, data).dump();
        }

        if (cmd == Renge::CMD_GET_POINTERS) {
            // These are filled by ExportAPI's cached EnginePointers
            extern uintptr_t   g_cachedGObjects;
            extern uintptr_t   g_cachedGNames;
            extern uintptr_t   g_cachedGWorld;
            extern uintptr_t   g_cachedSparseDelegates;
            extern uint32_t    g_cachedUEVersion;
            extern bool        g_cachedVersionDetected;
            extern const char* g_cachedGObjectsMethod;
            extern const char* g_cachedGNamesMethod;
            extern const char* g_cachedGWorldMethod;
            extern const char* g_cachedSparseDelegatesMethod;
            // AOB Usage Tracking
            extern char        g_cachedPeHash[17];
            extern const char* g_cachedGObjectsPatternId;
            extern const char* g_cachedGNamesPatternId;
            extern const char* g_cachedGWorldPatternId;
            extern const char* g_cachedSparseDelegatesPatternId;
            extern int         g_cachedGObjectsTried, g_cachedGObjectsHit;
            extern int         g_cachedGNamesTried,   g_cachedGNamesHit;
            extern int         g_cachedGWorldTried,   g_cachedGWorldHit;
            extern uintptr_t   g_cachedGObjectsScanAddr;
            extern uintptr_t   g_cachedGNamesScanAddr;
            extern uintptr_t   g_cachedGWorldScanAddr;
            extern uintptr_t   g_cachedSparseDelegatesScanAddr;
            extern const char* g_cachedGWorldAob;
            extern int         g_cachedGWorldAobPos;
            extern int         g_cachedGWorldAobLen;

            extern bool        g_cachedIsUserOverride;
            extern bool        g_cachedIsLowConfidence;
            extern const char* g_cachedPublisherThumbprint;

            json data;
            data["gobjects"]         = Renge::AddrToStr(g_cachedGObjects);
            data["gnames"]           = Renge::AddrToStr(g_cachedGNames);
            data["gworld"]           = Renge::AddrToStr(g_cachedGWorld);
            data["sparse_delegates"] = Renge::AddrToStr(g_cachedSparseDelegates);
            data["ue_version"]       = g_cachedUEVersion;
            data["version_detected"] = g_cachedVersionDetected;
            data["is_user_override"] = g_cachedIsUserOverride;
            data["is_low_confidence"] = g_cachedIsLowConfidence;
            data["publisher_thumbprint"] = g_cachedPublisherThumbprint
                ? g_cachedPublisherThumbprint : "";
            data["object_count"]            = Aura::GetCount();
            data["gobjects_method"]         = g_cachedGObjectsMethod;
            data["gnames_method"]           = g_cachedGNamesMethod;
            data["gworld_method"]           = g_cachedGWorldMethod;
            data["sparse_delegates_method"] = g_cachedSparseDelegatesMethod;

            // AOB Usage Tracking
            data["pe_hash"] = g_cachedPeHash;
            data["gobjects_pattern_id"]         = g_cachedGObjectsPatternId        ? g_cachedGObjectsPatternId        : "";
            data["gnames_pattern_id"]           = g_cachedGNamesPatternId          ? g_cachedGNamesPatternId          : "";
            data["gworld_pattern_id"]           = g_cachedGWorldPatternId          ? g_cachedGWorldPatternId          : "";
            data["sparse_delegates_pattern_id"] = g_cachedSparseDelegatesPatternId ? g_cachedSparseDelegatesPatternId : "";
            json scanStats;
            scanStats["gobjects_tried"] = g_cachedGObjectsTried;
            scanStats["gobjects_hit"]   = g_cachedGObjectsHit;
            scanStats["gnames_tried"]   = g_cachedGNamesTried;
            scanStats["gnames_hit"]     = g_cachedGNamesHit;
            scanStats["gworld_tried"]   = g_cachedGWorldTried;
            scanStats["gworld_hit"]     = g_cachedGWorldHit;
            data["scan_stats"] = scanStats;

            // AOB scan hit addresses (instruction that references the pointer)
            data["gobjects_scan_addr"]         = Renge::AddrToStr(g_cachedGObjectsScanAddr);
            data["gnames_scan_addr"]           = Renge::AddrToStr(g_cachedGNamesScanAddr);
            data["gworld_scan_addr"]           = Renge::AddrToStr(g_cachedGWorldScanAddr);
            data["sparse_delegates_scan_addr"] = Renge::AddrToStr(g_cachedSparseDelegatesScanAddr);

            // GWorld winning pattern AOB metadata (for CE symbol registration)
            data["gworld_aob"]     = g_cachedGWorldAob ? g_cachedGWorldAob : "";
            data["gworld_aob_pos"] = g_cachedGWorldAobPos;
            data["gworld_aob_len"] = g_cachedGWorldAobLen;

            // Module info for CE address formatting
            uintptr_t moduleBase = Macht::GetModuleBase(nullptr);
            data["module_base"] = Renge::AddrToStr(moduleBase);
            {
                wchar_t moduleNameW[MAX_PATH] = {};
                GetModuleFileNameW(reinterpret_cast<HMODULE>(moduleBase), moduleNameW, MAX_PATH);
                std::wstring modulePath(moduleNameW);
                auto lastSlash = modulePath.find_last_of(L"\\/");
                std::wstring moduleFileName = (lastSlash != std::wstring::npos)
                    ? modulePath.substr(lastSlash + 1) : modulePath;
                std::string moduleName;
                for (wchar_t wc : moduleFileName) {
                    moduleName += (wc < 128) ? static_cast<char>(wc) : '?';
                }
                data["module_name"] = moduleName;
            }

            return Renge::MakeResponse(id, data).dump();
        }

        if (cmd == Renge::CMD_GET_OBJECT_COUNT) {
            json data;
            data["count"] = Aura::GetCount();
            return Renge::MakeResponse(id, data).dump();
        }

        if (cmd == Renge::CMD_GET_OBJECT_LIST) {
            int offset = request.value("offset", 0);
            int limit  = request.value("limit", 200);
            int total  = Aura::GetCount();

            json objects = json::array();
            int end = (std::min)(offset + limit, total);

            for (int i = offset; i < end; ++i) {
                uintptr_t obj = Aura::GetByIndex(i);
                if (!obj) continue;

                std::string name = Ubel::GetName(obj);
                if (name.empty()) continue; // Skip unnamed objects

                json item;
                item["addr"]  = Renge::AddrToStr(obj);
                item["name"]  = name;

                uintptr_t cls = Ubel::GetClass(obj);
                item["class"] = cls ? Ubel::GetName(cls) : "";

                uintptr_t outer = Ubel::GetOuter(obj);
                item["outer"] = outer ? Renge::AddrToStr(outer) : "";

                objects.push_back(item);
            }

            json data;
            data["total"]   = total;
            data["scanned"] = end - offset; // Number of indices scanned (for pagination)
            data["objects"] = objects;
            return Renge::MakeResponse(id, data).dump();
        }

        if (cmd == Renge::CMD_GET_OBJECT) {
            std::string addrStr = request.value("addr", "");
            if (addrStr.empty()) return Renge::MakeError(id, "Missing addr").dump();

            uintptr_t addr = Renge::StrToAddr(addrStr);
            json data;
            data["addr"]      = addrStr;
            data["name"]      = Ubel::GetName(addr);
            data["full_name"] = Ubel::GetFullName(addr);

            uintptr_t cls = Ubel::GetClass(addr);
            data["class"]      = cls ? Ubel::GetName(cls) : "";
            data["class_addr"] = Renge::AddrToStr(cls);

            uintptr_t outer = Ubel::GetOuter(addr);
            data["outer"]      = outer ? Ubel::GetName(outer) : "";
            data["outer_addr"] = Renge::AddrToStr(outer);

            return Renge::MakeResponse(id, data).dump();
        }

        if (cmd == Renge::CMD_FIND_OBJECT) {
            std::string path = request.value("path", "");
            if (path.empty()) return Renge::MakeError(id, "Missing path").dump();

            uintptr_t obj = Aura::FindByName(path);
            if (!obj) return Renge::MakeError(id, "Object not found").dump();

            json data;
            data["addr"] = Renge::AddrToStr(obj);
            data["name"] = Ubel::GetName(obj);
            return Renge::MakeResponse(id, data).dump();
        }

        if (cmd == Renge::CMD_SEARCH_OBJECTS) {
            std::string query = request.value("query", "");
            int limit = request.value("limit", 200);
            if (query.empty()) return Renge::MakeError(id, "Missing query").dump();

            auto rset = Aura::SearchByName(query, limit);

            json objects = json::array();
            for (const auto& sr : rset.results) {
                json item;
                item["addr"]  = Renge::AddrToStr(sr.addr);
                item["name"]  = sr.name;
                item["class"] = sr.className;
                item["outer"] = Renge::AddrToStr(sr.outer);
                objects.push_back(item);
            }

            json data;
            data["total"]   = static_cast<int>(rset.results.size());
            data["scanned"] = rset.scanned;
            data["objects"] = objects;
            return Renge::MakeResponse(id, data).dump();
        }

        if (cmd == Renge::CMD_WALK_CLASS) {
            std::string addrStr = request.value("addr", "");
            if (addrStr.empty()) return Renge::MakeError(id, "Missing addr").dump();

            uintptr_t addr = Renge::StrToAddr(addrStr);
            ClassInfo ci = Ubel::WalkClassEx(addr);

            json classData;
            classData["name"]       = ci.Name;
            classData["full_path"]  = ci.FullPath;
            classData["super_addr"] = Renge::AddrToStr(ci.SuperClass);
            classData["super_name"] = ci.SuperName;
            classData["props_size"] = ci.PropertiesSize;

            json fields = json::array();
            for (const auto& f : ci.Fields) {
                json fj = {
                    {"addr",   Renge::AddrToStr(f.Address)},
                    {"name",   f.Name},
                    {"type",   f.TypeName},
                    {"offset", f.Offset},
                    {"size",   f.Size}
                };
                // Extended type metadata (only emit non-empty values)
                if (!f.structType.empty())      fj["struct_type"]       = f.structType;
                if (!f.objClassName.empty())     fj["obj_class"]         = f.objClassName;
                if (!f.innerType.empty())        fj["inner_type"]        = f.innerType;
                if (!f.innerStructType.empty())  fj["inner_struct_type"] = f.innerStructType;
                if (!f.innerObjClass.empty())    fj["inner_obj_class"]   = f.innerObjClass;
                if (!f.keyType.empty())          fj["key_type"]          = f.keyType;
                if (!f.keyStructType.empty())    fj["key_struct_type"]   = f.keyStructType;
                if (!f.valueType.empty())        fj["value_type"]        = f.valueType;
                if (!f.valueStructType.empty())  fj["value_struct_type"] = f.valueStructType;
                if (!f.elemType.empty())         fj["elem_type"]         = f.elemType;
                if (!f.elemStructType.empty())   fj["elem_struct_type"]  = f.elemStructType;
                if (!f.enumName.empty())         fj["enum_name"]         = f.enumName;
                if (f.boolFieldMask != 0)        fj["bool_mask"]         = f.boolFieldMask;
                fields.push_back(fj);
            }
            classData["fields"] = fields;

            json data;
            data["class"] = classData;
            return Renge::MakeResponse(id, data).dump();
        }

        // list_enums: enumerate all UEnum objects with their entries
        if (cmd == Renge::CMD_LIST_ENUMS) {
            int total = Aura::GetCount();
            json enums = json::array();

            for (int i = 0; i < total; ++i) {
                uintptr_t obj = Aura::GetByIndex(i);
                if (!obj) continue;

                // Check if this object's class is "Enum" (UEnum inherits UObject)
                uintptr_t cls = Ubel::GetClass(obj);
                if (!cls) continue;
                std::string clsName = Ubel::GetName(cls);
                if (clsName != "Enum") continue;

                std::string name = Ubel::GetName(obj);
                if (name.empty()) continue;

                // Read enum entries via cached resolver
                auto entries = Ubel::GetEnumEntries(obj);

                json enumObj;
                enumObj["addr"]      = Renge::AddrToStr(obj);
                enumObj["name"]      = name;
                enumObj["full_path"] = Ubel::GetFullName(obj);

                json entryArr = json::array();
                for (const auto& e : entries) {
                    entryArr.push_back({{"n", e.name}, {"v", e.value}});
                }
                enumObj["entries"] = entryArr;
                enums.push_back(enumObj);
            }

            json data;
            data["enums"] = enums;
            data["count"] = static_cast<int>(enums.size());
            return Renge::MakeResponse(id, data).dump();
        }

        if (cmd == Renge::CMD_WALK_FUNCTIONS) {
            std::string addrStr = request.value("addr", "");
            if (addrStr.empty()) return Renge::MakeError(id, "Missing addr").dump();

            uintptr_t addr = Renge::StrToAddr(addrStr);
            auto funcs = Ubel::WalkFunctions(addr);

            json funcArr = json::array();
            for (const auto& f : funcs) {
                json fj;
                fj["name"]    = f.name;
                fj["full"]    = f.fullName;
                fj["addr"]    = Renge::AddrToStr(f.address);
                fj["flags"]   = f.functionFlags;
                fj["num_parms"]  = f.numParms;
                fj["parms_size"] = f.parmsSize;
                fj["ret_offset"] = f.returnValueOffset;
                fj["ret"]     = f.returnType;

                json params = json::array();
                for (const auto& p : f.params) {
                    json pj;
                    pj["name"]   = p.name;
                    pj["type"]   = p.typeName;
                    pj["size"]   = p.size;
                    pj["offset"] = p.offset;
                    pj["out"]    = p.isOut;
                    pj["ret"]    = p.isReturn;
                    if (!p.structType.empty())
                        pj["struct_type"] = p.structType;
                    if (!p.structFields.empty()) {
                        json sfArr = json::array();
                        for (const auto& sf : p.structFields) {
                            json sfj;
                            sfj["name"]   = sf.name;
                            sfj["type"]   = sf.typeName;
                            sfj["offset"] = sf.offset;
                            sfj["size"]   = sf.size;
                            sfArr.push_back(sfj);
                        }
                        pj["struct_fields"] = sfArr;
                    }
                    params.push_back(pj);
                }
                fj["params"] = params;
                funcArr.push_back(fj);
            }

            json data;
            data["functions"] = funcArr;
            data["count"]     = static_cast<int>(funcArr.size());
            return Renge::MakeResponse(id, data).dump();
        }

        if (cmd == Renge::CMD_READ_MEM) {
            std::string addrStr = request.value("addr", "");
            int size = request.value("size", 256);
            if (addrStr.empty()) return Renge::MakeError(id, "Missing addr").dump();
            if (size <= 0 || size > 65536) return Renge::MakeError(id, "Invalid size").dump();

            uintptr_t addr = Renge::StrToAddr(addrStr);
            std::vector<uint8_t> buf(size);
            if (!Macht::ReadBytesSafe(addr, buf.data(), size)) {
                return Renge::MakeError(id, "Read failed").dump();
            }

            json data;
            data["bytes"] = Renge::BytesToHex(buf.data(), buf.size());
            return Renge::MakeResponse(id, data).dump();
        }

        if (cmd == Renge::CMD_WRITE_MEM) {
            std::string addrStr = request.value("addr", "");
            std::string hexBytes = request.value("bytes", "");
            if (addrStr.empty() || hexBytes.empty()) {
                return Renge::MakeError(id, "Missing addr or bytes").dump();
            }

            uintptr_t addr = Renge::StrToAddr(addrStr);
            auto bytes = Renge::HexToBytes(hexBytes);
            if (bytes.empty() || bytes.size() > 65536) {
                return Renge::MakeError(id, "Invalid write size (max 65536)").dump();
            }
            if (!Macht::WriteBytes(addr, bytes.data(), bytes.size())) {
                return Renge::MakeError(id, "Write failed").dump();
            }

            return Renge::MakeResponse(id).dump();
        }

        // === walk_instance: Read live field values from a UObject instance ===
        if (cmd == Renge::CMD_WALK_INSTANCE) {
            std::string addrStr = request.value("addr", "");
            if (addrStr.empty()) return Renge::MakeError(id, "Missing addr").dump();

            uintptr_t addr = Renge::StrToAddr(addrStr);
            std::string classAddrStr = request.value("class_addr", "");
            uintptr_t classAddr = classAddrStr.empty() ? 0 : Renge::StrToAddr(classAddrStr);
            int32_t arrayLimit = request.value("array_limit", 64);
            int32_t previewLimit = request.value("preview_limit", 2);
            bool fillGaps = request.value("fill_gaps", false);

            auto result = Ubel::WalkInstance(addr, classAddr, arrayLimit, previewLimit, fillGaps);

            json data;
            data["addr"]       = Renge::AddrToStr(result.addr);
            data["name"]       = result.name;
            data["class"]      = result.className;
            data["class_addr"] = Renge::AddrToStr(result.classAddr);
            data["outer"]      = Renge::AddrToStr(result.outerAddr);
            data["outer_name"] = result.outerName;
            data["outer_class"]= result.outerClassName;
            if (result.isDefinition)
                data["is_definition"] = true;
            if (result.propsSize > 0)
                data["props_size"] = result.propsSize;

            json fields = json::array();
            for (const auto& fv : result.fields) {
                fields.push_back(SerializeField(fv));
            }
            data["fields"] = fields;
            return Renge::MakeResponse(id, data).dump();
        }

        // === read_array_elements: Read scalar elements from a TArray (Phase B) ===
        if (cmd == Renge::CMD_READ_ARRAY_ELEMS) {
            std::string addrStr = request.value("addr", "");
            if (addrStr.empty())
                return Renge::MakeError(id, "missing 'addr'").dump();
            uintptr_t addr = Renge::StrToAddr(addrStr);

            int32_t fieldOffset = request.value("field_offset", 0);
            std::string innerAddrStr = request.value("inner_addr", "");
            uintptr_t innerAddr = innerAddrStr.empty() ? 0 : Renge::StrToAddr(innerAddrStr);
            std::string innerType = request.value("inner_type", "");
            int32_t elemSize = request.value("elem_size", 0);
            int32_t offset = request.value("offset", 0);
            int32_t limit = request.value("limit", 64);

            if (innerType.empty() || elemSize <= 0)
                return Renge::MakeError(id, "missing inner_type or invalid elem_size").dump();

            // Validate elemSize from UI — may have cached garbage from older sessions.
            // ReadArrayElements already caps at 256, but validate explicitly here too.
            if (elemSize > 256) {
                Sein::Warn("PIPE:cmd", "read_array_elements: elemSize=%d too large for '%s', rejecting",
                    elemSize, innerType.c_str());
                return Renge::MakeError(id, "elem_size too large (max 256)").dump();
            }

            auto result = Ubel::ReadArrayElements(
                addr, fieldOffset, innerAddr, innerType, elemSize, offset, limit);

            if (!result.ok)
                return Renge::MakeError(id, result.error).dump();

            json data;
            data["total"] = result.totalCount;
            data["read"] = result.readCount;
            data["inner_type"] = innerType;
            data["elem_size"] = elemSize;

            json elems = json::array();
            for (const auto& e : result.elements) {
                json ej;
                ej["i"] = e.index;
                ej["v"] = e.value;
                ej["h"] = e.hex;
                if (!e.enumName.empty())
                    ej["en"] = e.enumName;
                if (e.rawIntValue != 0 || !e.enumName.empty())
                    ej["rv"] = e.rawIntValue;
                // Phase D: pointer element fields
                if (e.ptrAddr != 0) {
                    ej["pa"] = Renge::AddrToStr(e.ptrAddr);
                    ej["pn"] = e.ptrName;
                    ej["pc"] = e.ptrClassName;
                }
                // Phase F: struct sub-fields
                if (!e.structFields.empty()) {
                    json sfs = json::array();
                    for (const auto& sf : e.structFields) {
                        json sfj = {{"n", sf.name}, {"t", sf.typeName},
                                    {"o", sf.offset}, {"s", sf.size}, {"v", sf.value}};
                        // Pointer resolution for ObjectProperty sub-fields
                        if (sf.ptrAddr != 0) {
                            sfj["pa"] = Renge::AddrToStr(sf.ptrAddr);
                            sfj["pn"] = sf.ptrName;
                            sfj["pc"] = sf.ptrClassName;
                            sfj["pca"] = Renge::AddrToStr(sf.ptrClassAddr);
                        }
                        sfs.push_back(sfj);
                    }
                    ej["sf"] = sfs;
                }
                elems.push_back(ej);
            }
            data["elements"] = elems;
            return Renge::MakeResponse(id, data).dump();
        }

        // === walk_world: Browse GWorld → PersistentLevel → Actors hierarchy ===
        if (cmd == Renge::CMD_WALK_WORLD) {
            extern uintptr_t g_cachedGWorld;

            // Allow overriding with a custom address
            std::string addrStr = request.value("addr", "");
            uintptr_t worldAddr = 0;
            if (!addrStr.empty()) {
                worldAddr = Renge::StrToAddr(addrStr);
            } else {
                // g_cachedGWorld is &GWorld (address of the global pointer variable).
                // Must dereference to get the actual UWorld* value.
                if (g_cachedGWorld) {
                    bool ok = Macht::ReadSafe(g_cachedGWorld, worldAddr);
                    Sein::Info("PIPE:world", "GWorld deref: &GWorld=0x%llX -> UWorld*=0x%llX (ReadSafe=%s)",
                        static_cast<unsigned long long>(g_cachedGWorld),
                        static_cast<unsigned long long>(worldAddr),
                        ok ? "ok" : "fail");
                }

                // Fallback: if AOB-resolved GWorld is null/wrong, search GObjects for a UWorld instance.
                // This handles games where the AOB pattern matched the wrong global variable.
                if (!worldAddr) {
                    Sein::Info("PIPE:world", "GWorld pointer is null, searching GObjects for UWorld...");
                    Aura::ForEach([&](int32_t idx, uintptr_t obj) -> bool {
                        uintptr_t cls = Ubel::GetClass(obj);
                        if (!cls) return true; // continue
                        std::string clsName = Ubel::GetName(cls);
                        if (clsName == "World") {
                            // Skip CDOs (Default__World) — they have null PersistentLevel
                            std::string objName = Ubel::GetName(obj);
                            if (objName.rfind("Default__", 0) == 0) {
                                Sein::Debug("PIPE:world", "Skipping CDO '%s' at 0x%llX",
                                    objName.c_str(), static_cast<unsigned long long>(obj));
                                return true; // continue
                            }
                            worldAddr = obj;
                            Sein::Info("PIPE:world", "Found UWorld '%s' via GObjects scan: 0x%llX (index=%d)",
                                objName.c_str(), static_cast<unsigned long long>(obj), idx);
                            return false; // stop
                        }
                        return true; // continue
                    });
                }
            }

            if (!worldAddr) return Renge::MakeError(id, "GWorld not found — no UWorld instance in GObjects").dump();

            json data;
            data["world_addr"] = Renge::AddrToStr(worldAddr);
            data["world_name"] = Ubel::GetName(worldAddr);

            // Log DynOff state for diagnostics
            Sein::Info("PIPE:world", "DynOff: FFIELD_NEXT=0x%02X FFIELD_NAME=0x%02X FPROPERTY_OFFSET=0x%02X "
                "FPROPERTY_ELEMSIZE=0x%02X FSTRUCTPROP_STRUCT=0x%02X bTaggedFFV=%d",
                DynOff::FFIELD_NEXT, DynOff::FFIELD_NAME, DynOff::FPROPERTY_OFFSET,
                DynOff::FPROPERTY_ELEMSIZE, DynOff::FSTRUCTPROP_STRUCT,
                DynOff::bTaggedFFieldVariant ? 1 : 0);

            // Walk UWorld class to find PersistentLevel field offset dynamically
            uintptr_t worldClass = Ubel::GetClass(worldAddr);
            if (!worldClass) return Renge::MakeError(id, "Cannot read UWorld class").dump();

            ClassInfo worldCI = Ubel::WalkClass(worldClass);
            Sein::Info("PIPE:world", "UWorld class '%s' at 0x%llX, %zu fields, propsSize=%d",
                worldCI.Name.c_str(), static_cast<unsigned long long>(worldClass),
                worldCI.Fields.size(), worldCI.PropertiesSize);

            // Find PersistentLevel field (ObjectProperty)
            uintptr_t levelAddr = 0;
            int persistentLevelOffset = 0;
            bool foundPersistentLevel = false;
            for (const auto& f : worldCI.Fields) {
                if (f.Name == "PersistentLevel" && f.Size >= 8) {
                    foundPersistentLevel = true;
                    persistentLevelOffset = f.Offset;
                    Macht::ReadSafe(worldAddr + f.Offset, levelAddr);
                    Sein::Info("PIPE:world", "PersistentLevel: offset=%d, levelAddr=0x%llX",
                        persistentLevelOffset, static_cast<unsigned long long>(levelAddr));
                    break;
                }
            }

            if (!foundPersistentLevel) {
                // Diagnostic: dump all field names + raw FName data for debugging
                Sein::Warn("PIPE:world", "PersistentLevel NOT found in %zu UWorld fields. Dumping first 10:",
                    worldCI.Fields.size());
                int dumpCount = 0;
                for (const auto& f : worldCI.Fields) {
                    if (dumpCount >= 10) break;
                    Sein::Warn("PIPE:world", "  field[%d]: name='%s' type='%s' off=%d size=%d addr=0x%llX",
                        dumpCount, f.Name.c_str(), f.TypeName.c_str(), f.Offset, f.Size,
                        static_cast<unsigned long long>(f.Address));
                    ++dumpCount;
                }

                // Diagnostic: try reading FName at alternate offsets (0x20, 0x28, 0x30) on first FField
                if (!worldCI.Fields.empty()) {
                    uintptr_t firstFF = worldCI.Fields[0].Address;
                    for (int probe = 0x18; probe <= 0x38; probe += 4) {
                        int32_t ci = 0;
                        if (Macht::ReadSafe(firstFF + probe, ci) && ci > 0 && ci < 0x00FFFFFF) {
                            std::string probeName = Serie::GetString(ci);
                            Sein::Warn("PIPE:world", "  probe FField+0x%02X: compIdx=%d -> '%s'",
                                probe, ci, probeName.c_str());
                        }
                    }
                }

                data["error"] = "PersistentLevel field not found in UWorld class (WalkClass returned "
                    + std::to_string(worldCI.Fields.size()) + " fields)";
                Sein::Warn("PIPE:world", "%s", data["error"].get<std::string>().c_str());
                return Renge::MakeResponse(id, data).dump();
            }
            if (!levelAddr) {
                data["error"] = "PersistentLevel is null (CDO or uninitialized world instance)";
                Sein::Warn("PIPE:world", "%s", data["error"].get<std::string>().c_str());
                return Renge::MakeResponse(id, data).dump();
            }

            data["level_addr"] = Renge::AddrToStr(levelAddr);
            data["level_name"] = Ubel::GetName(levelAddr);
            data["level_offset"] = persistentLevelOffset;

            // Walk ULevel class to find Actors TArray field
            uintptr_t levelClass = Ubel::GetClass(levelAddr);
            ClassInfo levelCI = levelClass ? Ubel::WalkClass(levelClass) : ClassInfo{};

            // Find Actors field (ArrayProperty) — it's a TArray<AActor*>
            int actorsOffset = -1;
            for (const auto& f : levelCI.Fields) {
                if (f.Name == "Actors" && f.TypeName == "ArrayProperty") {
                    actorsOffset = f.Offset;
                    break;
                }
            }

            json actors = json::array();
            int actorLimit = request.value("limit", 200);

            if (actorsOffset >= 0) {
                Macht::TArrayView actorArr;
                if (Macht::ReadTArray(levelAddr + actorsOffset, actorArr)) {
                    int count = (std::min)(actorArr.Count, actorLimit);
                    for (int i = 0; i < count; ++i) {
                        uintptr_t actorAddr = Macht::ReadTArrayElement(actorArr, i);
                        if (!actorAddr) continue;

                        json actorItem;
                        actorItem["addr"]  = Renge::AddrToStr(actorAddr);
                        actorItem["name"]  = Ubel::GetName(actorAddr);
                        actorItem["index"] = Ubel::GetIndex(actorAddr);

                        uintptr_t actorCls = Ubel::GetClass(actorAddr);
                        actorItem["class"] = actorCls ? Ubel::GetName(actorCls) : "";

                        // Try to find OwnedComponents on this actor
                        ClassInfo actorCI = actorCls ? Ubel::WalkClass(actorCls) : ClassInfo{};
                        int compsOffset = -1;
                        for (const auto& f : actorCI.Fields) {
                            if (f.Name == "OwnedComponents" && f.TypeName == "ArrayProperty") {
                                compsOffset = f.Offset;
                                break;
                            }
                        }

                        if (compsOffset >= 0) {
                            Macht::TArrayView compArr;
                            if (Macht::ReadTArray(actorAddr + compsOffset, compArr)) {
                                json comps = json::array();
                                int compCount = (std::min)(compArr.Count, 64); // Limit components
                                for (int c = 0; c < compCount; ++c) {
                                    uintptr_t compAddr = Macht::ReadTArrayElement(compArr, c);
                                    if (!compAddr) continue;

                                    json compItem;
                                    compItem["addr"] = Renge::AddrToStr(compAddr);
                                    compItem["name"] = Ubel::GetName(compAddr);
                                    uintptr_t compCls = Ubel::GetClass(compAddr);
                                    compItem["class"] = compCls ? Ubel::GetName(compCls) : "";
                                    comps.push_back(compItem);
                                }
                                actorItem["components"] = comps;
                            }
                        }

                        actors.push_back(actorItem);
                    }
                }
            }

            data["actors"]      = actors;
            data["actor_count"] = static_cast<int>(actors.size());
            return Renge::MakeResponse(id, data).dump();
        }

        // === find_instances: Search GObjects for instances of a given class ===
        if (cmd == Renge::CMD_FIND_INSTANCES) {
            std::string className = request.value("class_name", "");
            bool exactMatch = request.value("exact_match", false);
            int limit = request.value("limit", 500);
            if (className.empty()) return Renge::MakeError(id, "Missing class_name").dump();

            auto rset = Aura::FindInstancesByClass(className, exactMatch, limit);

            // Diagnostic: if name resolution ratio is low, dump FNamePool state
            if (rset.nonNull > 1000 && rset.named > 0) {
                double namedRatio = static_cast<double>(rset.named) / rset.nonNull;
                if (namedRatio < 0.70) {
                    Sein::Warn("PIPE:find", "Low name resolution ratio: %.1f%% (%d/%d) — running FNamePool diagnostics",
                                 namedRatio * 100, rset.named, rset.nonNull);
                    Serie::LogDiagnostics();
                }
            }

            json instances = json::array();
            for (const auto& sr : rset.results) {
                json item;
                item["addr"]  = Renge::AddrToStr(sr.addr);
                item["index"] = sr.index;
                item["name"]  = sr.name;
                item["class"] = sr.className;
                item["outer"] = Renge::AddrToStr(sr.outer);
                instances.push_back(item);
            }

            json data;
            data["total"]     = static_cast<int>(rset.results.size());
            data["scanned"]   = rset.scanned;
            data["non_null"]  = rset.nonNull;
            data["named"]     = rset.named;
            data["instances"] = instances;
            return Renge::MakeResponse(id, data).dump();
        }

        // === search_properties: Keyword search across all UClass properties ===
        if (cmd == Renge::CMD_SEARCH_PROPERTIES) {
            std::string query = request.value("query", "");
            bool gameOnly = request.value("game_only", true);
            int limit = request.value("limit", 200);

            // Parse optional type filter
            std::vector<std::string> typeFilter;
            if (request.contains("types") && request["types"].is_array()) {
                for (const auto& t : request["types"]) {
                    if (t.is_string()) typeFilter.push_back(t.get<std::string>());
                }
            }

            // Either query or typeFilter must constrain the search — empty
            // both would scan every property in every class. SearchProperties
            // tolerates an empty query (substring-find returns 0 on empty
            // pattern), so allow it when typeFilter is non-empty.
            if (query.empty() && typeFilter.empty()) {
                return Renge::MakeError(id, "Missing query or type filter").dump();
            }

            auto searchResult = Aura::SearchProperties(query, typeFilter, gameOnly, limit);

            json matches = json::array();
            for (const auto& m : searchResult.results) {
                json item;
                item["class_name"]  = m.className;
                item["class_addr"]  = Renge::AddrToStr(m.classAddr);
                item["class_path"]  = m.classPath;
                item["super_name"]  = m.superName;
                item["prop_name"]   = m.propName;
                item["prop_type"]   = m.propType;
                item["prop_offset"] = m.propOffset;
                item["prop_size"]   = m.propSize;
                item["struct_type"] = m.structType;
                item["inner_type"]  = m.innerType;
                if (!m.preview.empty())
                    item["preview"] = m.preview;
                matches.push_back(item);
            }

            json data;
            data["total"]           = static_cast<int>(searchResult.results.size());
            data["scanned_classes"] = searchResult.scannedClasses;
            data["scanned_objects"] = searchResult.scannedObjects;
            data["results"]         = matches;
            return Renge::MakeResponse(id, data).dump();
        }

        // === list_classes: List all UClass objects (optionally game-only) ===
        if (cmd == Renge::CMD_LIST_CLASSES) {
            bool gameOnly = request.value("game_only", true);
            int limit = request.value("limit", 5000);

            auto listResult = Aura::ListClasses(gameOnly, limit);

            json classes = json::array();
            for (const auto& e : listResult.results) {
                json item;
                item["class_name"]      = e.className;
                item["class_addr"]      = Renge::AddrToStr(e.classAddr);
                item["class_path"]      = e.classPath;
                item["super_name"]      = e.superName;
                item["property_count"]  = e.propertyCount;
                item["properties_size"] = e.propertiesSize;
                item["score"]           = e.heuristicScore;
                classes.push_back(item);
            }

            json data;
            data["total"]           = static_cast<int>(listResult.results.size());
            data["scanned_objects"] = listResult.scannedObjects;
            data["total_classes"]   = listResult.totalClasses;
            data["classes"]         = classes;
            return Renge::MakeResponse(id, data).dump();
        }

        // === find_by_address: Reverse lookup — address to UObject instance ===
        // Always runs the standard FindByAddress (UObject containment + backward
        // memory scan). Additionally runs container-aware FindInContainers when:
        //   - request includes "scan_containers": true (UI opt-in), OR
        //   - the standard search returned no exact/containment match
        // Container matches let the UI surface "this address is element [N] of
        // ObjA.SomeArray" so the user can jump straight into that element.
        if (cmd == Renge::CMD_FIND_BY_ADDRESS) {
            std::string addrStr = request.value("addr", "");
            if (addrStr.empty()) return Renge::MakeError(id, "Missing addr").dump();

            uintptr_t queryAddr = Renge::StrToAddr(addrStr);
            auto lookupResult = Aura::FindByAddress(queryAddr);
            bool requestedContainerScan = request.value("scan_containers", false);

            json data;
            data["found"] = lookupResult.found;

            if (lookupResult.found) {
                // match_type: legacy "exact" / "contains" string for back-compat.
                // match_kind: more precise — "exact" / "contains" / "backward" / "nearest".
                // Clients should prefer match_kind to distinguish low-confidence
                // "nearest" fallbacks (addr beyond PropertiesSize) from real containment.
                data["match_type"]       = lookupResult.exactMatch ? "exact" : "contains";
                data["match_kind"]       = lookupResult.matchKind.empty()
                                              ? (lookupResult.exactMatch ? "exact" : "contains")
                                              : lookupResult.matchKind;
                data["addr"]             = Renge::AddrToStr(lookupResult.objectAddr);
                data["index"]            = lookupResult.index;
                data["name"]             = lookupResult.name;
                data["class"]            = lookupResult.className;
                data["outer"]            = Renge::AddrToStr(lookupResult.outer);
                data["offset_from_base"] = lookupResult.offsetFromBase;
                data["query_addr"]       = addrStr;
            }

            // Container scan: opt-in, or fallback when nothing else hit.
            // Heap-allocated TArray data buffers don't fall inside any
            // UObject's PropertiesSize, so this is the only way to attribute
            // those addresses to an owner.
            if (requestedContainerScan || !lookupResult.found) {
                Aura::ContainerScanStats stats;
                auto containerMatches = Aura::FindInContainers(queryAddr, 16, &stats);

                // Surface scan stats so the UI can distinguish "really not in
                // any container" from "scan got cut off by the deadline".
                json scanInfo;
                scanInfo["objects_scanned"] = stats.objectsScanned;
                scanInfo["objects_total"]   = stats.objectsTotal;
                scanInfo["classes_primed"]  = stats.classesPrimed;
                scanInfo["duration_ms"]     = stats.durationMs;
                scanInfo["deadline_hit"]    = stats.deadlineHit;
                data["container_scan"]      = scanInfo;

                json arr = json::array();
                for (const auto& m : containerMatches) {
                    json mj;
                    mj["owner_addr"]    = Renge::AddrToStr(m.ownerObj);
                    mj["owner_index"]   = m.ownerIndex;
                    mj["owner_name"]    = m.ownerName;
                    mj["owner_class"]   = m.ownerClassName;
                    mj["field_offset"]  = m.fieldOffset;
                    mj["field_name"]    = m.fieldName;
                    mj["field_type"]    = m.fieldType;
                    mj["inner_type"]    = m.innerType;
                    mj["element_index"] = m.elementIndex;
                    mj["element_size"]  = m.elementSize;
                    mj["intra_offset"]  = m.intraOffset;
                    mj["data_addr"]     = Renge::AddrToStr(m.dataAddr);
                    mj["count"]         = m.count;
                    if (!m.note.empty())
                        mj["note"]      = m.note;
                    arr.push_back(mj);
                }
                data["container_matches"] = arr;
            }

            return Renge::MakeResponse(id, data).dump();
        }

        // === find_refs_to_uobject: reverse pointer search ===
        // Given a UObject's address, find every other UObject that holds a
        // pointer to it (direct ObjectProperty/ClassProperty fields,
        // TArray<UObject*> elements, including nested in StructProperty).
        // Resolves the "logical owner" question that UE's OuterPrivate
        // doesn't answer for runtime-spawned objects.
        if (cmd == Renge::CMD_FIND_REFS_TO_UOBJ) {
            std::string addrStr = request.value("addr", "");
            if (addrStr.empty()) return Renge::MakeError(id, "Missing addr").dump();
            uintptr_t target = Renge::StrToAddr(addrStr);
            int32_t maxResults = request.value("max_results", 32);

            Aura::ContainerScanStats stats;
            auto refs = Aura::FindReferencesToUObject(target, maxResults, &stats);

            json data;
            data["query_addr"] = addrStr;

            json scanInfo;
            scanInfo["objects_scanned"] = stats.objectsScanned;
            scanInfo["objects_total"]   = stats.objectsTotal;
            scanInfo["classes_primed"]  = stats.classesPrimed;
            scanInfo["duration_ms"]     = stats.durationMs;
            scanInfo["deadline_hit"]    = stats.deadlineHit;
            data["scan"] = scanInfo;

            json arr = json::array();
            for (const auto& r : refs) {
                json rj;
                rj["owner_addr"]    = Renge::AddrToStr(r.ownerObj);
                rj["owner_index"]   = r.ownerIndex;
                rj["owner_name"]    = r.ownerName;
                rj["owner_class"]   = r.ownerClassName;
                rj["field_offset"]  = r.fieldOffset;
                rj["field_name"]    = r.fieldName;
                rj["field_type"]    = r.fieldType;
                if (!r.innerType.empty())
                    rj["inner_type"] = r.innerType;
                rj["element_index"] = r.elementIndex;
                arr.push_back(rj);
            }
            data["references"] = arr;
            return Renge::MakeResponse(id, data).dump();
        }

        // === get_ce_pointer_info: CE pointer chain info for a GObjects instance ===
        if (cmd == Renge::CMD_GET_CE_PTR_INFO) {
            std::string addrStr = request.value("addr", "");
            if (addrStr.empty()) return Renge::MakeError(id, "Missing addr").dump();

            uintptr_t addr = Renge::StrToAddr(addrStr);
            int fieldOffset = request.value("field_offset", 0);

            extern uintptr_t g_cachedGObjects;
            uintptr_t moduleBase = Macht::GetModuleBase(nullptr);

            // Compute GObjects RVA
            uintptr_t gobjectsRVA = g_cachedGObjects - moduleBase;

            // Find the InternalIndex of this object by scanning
            int32_t internalIndex = Ubel::GetIndex(addr);
            if (internalIndex < 0) {
                return Renge::MakeError(id, "Cannot read InternalIndex").dump();
            }

            int32_t chunkIndex  = internalIndex / Grimoire::OBJECTS_PER_CHUNK;
            int32_t withinChunk = internalIndex % Grimoire::OBJECTS_PER_CHUNK;

            // Get module name
            wchar_t moduleNameW[MAX_PATH] = {};
            GetModuleFileNameW(reinterpret_cast<HMODULE>(moduleBase), moduleNameW, MAX_PATH);
            // Extract just the filename
            std::wstring modulePath(moduleNameW);
            auto lastSlash = modulePath.find_last_of(L"\\/");
            std::wstring moduleFileName = (lastSlash != std::wstring::npos)
                ? modulePath.substr(lastSlash + 1) : modulePath;
            // Remove .exe extension for CE format
            auto dotPos = moduleFileName.find_last_of(L'.');
            std::wstring moduleNameNoExt = (dotPos != std::wstring::npos)
                ? moduleFileName.substr(0, dotPos) : moduleFileName;

            // Convert to narrow string
            std::string moduleName;
            for (wchar_t wc : moduleNameNoExt) {
                moduleName += (wc < 128) ? static_cast<char>(wc) : '?';
            }

            json data;
            data["module"]         = moduleName;
            data["module_base"]    = Renge::AddrToStr(moduleBase);
            data["gobjects_rva"]   = Renge::AddrToStr(gobjectsRVA);
            data["internal_index"] = internalIndex;
            data["chunk_index"]    = chunkIndex;
            data["within_chunk"]   = withinChunk;
            data["field_offset"]   = fieldOffset;

            // CE offset chain (bottom-to-top):
            // Level 4 (outermost): deref FUObjectArray* → chunkTable (offset 0)
            // Level 3: chunkTable + chunkIndex*8 → chunk
            // Level 2: chunk + withinChunk*itemSize → FUObjectItem.Object (offset 0)
            // Level 1 (innermost): Object + fieldOffset → value
            json offsets = json::array();
            offsets.push_back(fieldOffset);                              // field offset from UObject*
            offsets.push_back(withinChunk * Aura::GetItemSize()); // item in chunk
            offsets.push_back(chunkIndex * 8);                           // chunk in table
            offsets.push_back(0);                                         // deref FUObjectArray.Objects

            data["ce_offsets"] = offsets;

            // CE base address string: "Module.exe+RVA"
            char ceBase[128];
            snprintf(ceBase, sizeof(ceBase), "\"%s.exe\"+%llX",
                     moduleName.c_str(), static_cast<unsigned long long>(gobjectsRVA));
            data["ce_base"] = ceBase;

            return Renge::MakeResponse(id, data).dump();
        }

        // === get_offsets: Return all detected FField/FProperty/UStruct offsets ===
        if (cmd == Renge::CMD_GET_OFFSETS) {
            json data;
            data["build_info"]         = BUILD_VERSION_STRING;
            data["validated"]          = DynOff::bOffsetsValidated.load(std::memory_order_acquire);
            data["case_preserving"]    = DynOff::bCasePreservingName;
            data["use_fproperty"]      = DynOff::bUseFProperty;
            data["uobject_outer"]      = DynOff::UOBJECT_OUTER;
            data["ustruct_super"]      = DynOff::USTRUCT_SUPER;
            data["ustruct_children"]   = DynOff::USTRUCT_CHILDREN;
            data["ustruct_childprops"] = DynOff::USTRUCT_CHILDPROPS;
            data["ustruct_propssize"]  = DynOff::USTRUCT_PROPSSIZE;
            if (DynOff::bUseFProperty) {
                data["ffield_class"]       = DynOff::FFIELD_CLASS;
                data["ffield_next"]        = DynOff::FFIELD_NEXT;
                data["ffield_name"]        = DynOff::FFIELD_NAME;
                data["fproperty_elemsize"] = DynOff::FPROPERTY_ELEMSIZE;
                data["fproperty_flags"]    = DynOff::FPROPERTY_FLAGS;
                data["fproperty_offset"]   = DynOff::FPROPERTY_OFFSET;
            } else {
                data["ufield_next"]        = DynOff::UFIELD_NEXT;
                data["uproperty_elemsize"] = DynOff::UPROPERTY_ELEMSIZE;
                data["uproperty_flags"]    = DynOff::UPROPERTY_FLAGS;
                data["uproperty_offset"]   = DynOff::UPROPERTY_OFFSET;
            }
            return Renge::MakeResponse(id, data).dump();
        }

        if (cmd == Renge::CMD_WATCH) {
            std::string addrStr = request.value("addr", "");
            int size = request.value("size", 4);
            int interval = request.value("interval_ms", 500);
            if (addrStr.empty()) return Renge::MakeError(id, "Missing addr").dump();
            if (size <= 0 || size > 65536) return Renge::MakeError(id, "Invalid size (1-65536)").dump();
            if (interval < 50) interval = 50; // Minimum 50ms to prevent CPU spin

            uintptr_t addr = Renge::StrToAddr(addrStr);
            StartWatch(addr, size, interval);
            return Renge::MakeResponse(id).dump();
        }

        if (cmd == Renge::CMD_UNWATCH) {
            std::string addrStr = request.value("addr", "");
            if (addrStr.empty()) return Renge::MakeError(id, "Missing addr").dump();

            uintptr_t addr = Renge::StrToAddr(addrStr);
            StopWatch(addr);
            return Renge::MakeResponse(id).dump();
        }

        // === Extra Scan: user-triggered aggressive pointer recovery ===

        if (cmd == Renge::CMD_RESCAN) {
            if (m_rescan.running.load()) {
                return Renge::MakeError(id, "Rescan already in progress").dump();
            }

            extern uintptr_t g_cachedGObjects;
            extern uintptr_t g_cachedGNames;
            extern uintptr_t g_cachedGWorld;

            bool needGObj = (g_cachedGObjects == 0);
            bool needGWld = (g_cachedGWorld == 0) && (g_cachedGObjects != 0) && (g_cachedGNames != 0);

            if (!needGObj && !needGWld) {
                json data;
                data["scanning_gobjects"] = false;
                data["scanning_gworld"]   = false;
                data["message"] = "All scannable pointers already found";
                return Renge::MakeResponse(id, data).dump();
            }

            // Reset state
            m_rescan.foundGObjects = 0;
            m_rescan.foundGWorld   = 0;
            m_rescan.gobjectsMethod = "not_found";
            m_rescan.gworldMethod   = "not_found";
            m_rescan.phase.store(0);
            {
                std::lock_guard<std::mutex> lock(m_rescan.statusMutex);
                m_rescan.statusText = "Starting...";
            }
            m_rescan.running.store(true);

            if (m_rescan.scanThread.joinable()) m_rescan.scanThread.join();
            m_rescan.scanThread = std::thread(&Fern::RunRescan, this, needGObj, needGWld);

            json data;
            data["scanning_gobjects"] = needGObj;
            data["scanning_gworld"]   = needGWld;
            return Renge::MakeResponse(id, data).dump();
        }

        if (cmd == Renge::CMD_RESCAN_STATUS) {
            json data;
            data["running"] = m_rescan.running.load();
            data["phase"]   = m_rescan.phase.load();
            {
                std::lock_guard<std::mutex> lock(m_rescan.statusMutex);
                data["status_text"] = m_rescan.statusText;
            }
            // Include results if scan is complete
            if (!m_rescan.running.load() && m_rescan.phase.load() == 3) {
                data["found_gobjects"]   = (m_rescan.foundGObjects != 0);
                data["found_gworld"]     = (m_rescan.foundGWorld != 0);
                data["gobjects_addr"]    = Renge::AddrToStr(m_rescan.foundGObjects);
                data["gworld_addr"]      = Renge::AddrToStr(m_rescan.foundGWorld);
                data["gobjects_method"]  = m_rescan.gobjectsMethod;
                data["gworld_method"]    = m_rescan.gworldMethod;
            }
            return Renge::MakeResponse(id, data).dump();
        }

        if (cmd == Renge::CMD_APPLY_RESCAN) {
            if (m_rescan.running.load()) {
                return Renge::MakeError(id, "Rescan still running").dump();
            }

            extern uintptr_t   g_cachedGObjects;
            extern uintptr_t   g_cachedGNames;
            extern uintptr_t   g_cachedGWorld;
            extern uint32_t    g_cachedUEVersion;
            extern const char* g_cachedGObjectsMethod;
            extern const char* g_cachedGWorldMethod;

            bool applied = false;

            if (m_rescan.foundGObjects && g_cachedGObjects == 0) {
                g_cachedGObjects = m_rescan.foundGObjects;
                g_cachedGObjectsMethod = m_rescan.gobjectsMethod;
                Aura::Init(g_cachedGObjects);
                Sein::Info("PIPE:cmd", "apply_rescan: Applied GObjects=0x%llX (%s)",
                         (unsigned long long)g_cachedGObjects, g_cachedGObjectsMethod);
                applied = true;
            }

            if (m_rescan.foundGWorld && g_cachedGWorld == 0) {
                g_cachedGWorld = m_rescan.foundGWorld;
                g_cachedGWorldMethod = m_rescan.gworldMethod;
                Sein::Info("PIPE:cmd", "apply_rescan: Applied GWorld=0x%llX (%s)",
                         (unsigned long long)g_cachedGWorld, g_cachedGWorldMethod);
                applied = true;
            }

            // If we now have both GObjects+GNames, run full offset detection
            if (g_cachedGObjects && g_cachedGNames) {
                if (!Genau::ValidateAndFixOffsets(g_cachedUEVersion)) {
                    Sein::Warn("PIPE:cmd", "apply_rescan: ValidateAndFixOffsets returned false");
                }
            }

            json data;
            data["applied"]      = applied;
            data["gobjects"]     = Renge::AddrToStr(g_cachedGObjects);
            data["gnames"]       = Renge::AddrToStr(g_cachedGNames);
            data["gworld"]       = Renge::AddrToStr(g_cachedGWorld);
            data["object_count"] = Aura::GetCount();
            return Renge::MakeResponse(id, data).dump();
        }

        // ── trigger_scan: UI-initiated deferred scan (proxy DLL mode) ────────
        // The proxy DLL starts the pipe server without scanning. The UI sends
        // this command when the user is ready (game loaded, world active).
        // Async: starts a background thread, returns immediately.
        // Also safe to call in CE/manual inject mode — UE5_Init is idempotent.
        if (cmd == Renge::CMD_TRIGGER_SCAN) {
            if (m_scan.running.load()) {
                return Renge::MakeError(id, "Scan already in progress").dump();
            }

            Sein::Info("PIPE:cmd", "trigger_scan: Starting async engine scan...");

            // Reset state and launch background thread
            m_scan.completed = false;
            m_scan.phase.store(0);
            {
                std::lock_guard<std::mutex> lock(m_scan.statusMutex);
                m_scan.statusText = "Starting...";
            }
            m_scan.running.store(true);

            if (m_scan.scanThread.joinable()) m_scan.scanThread.join();
            m_scan.scanThread = std::thread(&Fern::RunScan, this);

            json data;
            data["started"] = true;
            return Renge::MakeResponse(id, data).dump();
        }

        // ── scan_status: Poll scan progress (pairs with trigger_scan) ────────
        if (cmd == Renge::CMD_SCAN_STATUS) {
            extern uintptr_t   g_cachedGObjects;
            extern uintptr_t   g_cachedGNames;
            extern uintptr_t   g_cachedGWorld;
            extern uintptr_t   g_cachedSparseDelegates;
            extern uint32_t    g_cachedUEVersion;
            extern bool        g_cachedVersionDetected;
            extern const char* g_cachedGObjectsMethod;
            extern const char* g_cachedGNamesMethod;
            extern const char* g_cachedGWorldMethod;
            extern const char* g_cachedSparseDelegatesMethod;
            extern char        g_cachedPeHash[17];
            extern const char* g_cachedGObjectsPatternId;
            extern const char* g_cachedGNamesPatternId;
            extern const char* g_cachedGWorldPatternId;
            extern const char* g_cachedSparseDelegatesPatternId;
            extern int         g_cachedGObjectsTried, g_cachedGObjectsHit;
            extern int         g_cachedGNamesTried,   g_cachedGNamesHit;
            extern int         g_cachedGWorldTried,   g_cachedGWorldHit;
            extern uintptr_t   g_cachedGObjectsScanAddr;
            extern uintptr_t   g_cachedGNamesScanAddr;
            extern uintptr_t   g_cachedGWorldScanAddr;
            extern uintptr_t   g_cachedSparseDelegatesScanAddr;
            extern const char* g_cachedGWorldAob;
            extern int         g_cachedGWorldAobPos;
            extern int         g_cachedGWorldAobLen;

            // Read progress from ScanProgress namespace (set by UE5_Init)
            namespace SP = ScanProgress;
            int phase = SP::phase.load(std::memory_order_acquire);

            json data;
            data["running"]     = m_scan.running.load();
            data["phase"]       = phase;
            data["status_text"] = SP::GetStatusText();

            // When complete, include full pointer data (same as get_pointers)
            if (!m_scan.running.load() && m_scan.completed) {
                data["scanned"]          = true;
                data["gobjects"]         = Renge::AddrToStr(g_cachedGObjects);
                data["gnames"]           = Renge::AddrToStr(g_cachedGNames);
                data["gworld"]           = Renge::AddrToStr(g_cachedGWorld);
                data["sparse_delegates"] = Renge::AddrToStr(g_cachedSparseDelegates);
                data["ue_version"]       = g_cachedUEVersion;
                data["version_detected"] = g_cachedVersionDetected;
                data["object_count"]            = Aura::GetCount();
                data["gobjects_method"]         = g_cachedGObjectsMethod;
                data["gnames_method"]           = g_cachedGNamesMethod;
                data["gworld_method"]           = g_cachedGWorldMethod;
                data["sparse_delegates_method"] = g_cachedSparseDelegatesMethod;
                data["pe_hash"]          = g_cachedPeHash;
                data["gobjects_pattern_id"]         = g_cachedGObjectsPatternId        ? g_cachedGObjectsPatternId        : "";
                data["gnames_pattern_id"]           = g_cachedGNamesPatternId          ? g_cachedGNamesPatternId          : "";
                data["gworld_pattern_id"]           = g_cachedGWorldPatternId          ? g_cachedGWorldPatternId          : "";
                data["sparse_delegates_pattern_id"] = g_cachedSparseDelegatesPatternId ? g_cachedSparseDelegatesPatternId : "";
                json scanStats;
                scanStats["gobjects_tried"] = g_cachedGObjectsTried;
                scanStats["gobjects_hit"]   = g_cachedGObjectsHit;
                scanStats["gnames_tried"]   = g_cachedGNamesTried;
                scanStats["gnames_hit"]     = g_cachedGNamesHit;
                scanStats["gworld_tried"]   = g_cachedGWorldTried;
                scanStats["gworld_hit"]     = g_cachedGWorldHit;
                data["scan_stats"]          = scanStats;
                data["gobjects_scan_addr"]         = Renge::AddrToStr(g_cachedGObjectsScanAddr);
                data["gnames_scan_addr"]           = Renge::AddrToStr(g_cachedGNamesScanAddr);
                data["gworld_scan_addr"]           = Renge::AddrToStr(g_cachedGWorldScanAddr);
                data["sparse_delegates_scan_addr"] = Renge::AddrToStr(g_cachedSparseDelegatesScanAddr);
                data["gworld_aob"]          = g_cachedGWorldAob ? g_cachedGWorldAob : "";
                data["gworld_aob_pos"]      = g_cachedGWorldAobPos;
                data["gworld_aob_len"]      = g_cachedGWorldAobLen;

                uintptr_t moduleBase = Macht::GetModuleBase(nullptr);
                data["module_base"] = Renge::AddrToStr(moduleBase);
                {
                    wchar_t moduleNameW[MAX_PATH] = {};
                    GetModuleFileNameW(reinterpret_cast<HMODULE>(moduleBase), moduleNameW, MAX_PATH);
                    std::wstring modulePath(moduleNameW);
                    auto lastSlash = modulePath.find_last_of(L"\\/");
                    std::wstring moduleFileName = (lastSlash != std::wstring::npos)
                        ? modulePath.substr(lastSlash + 1) : modulePath;
                    std::string moduleName;
                    for (wchar_t wc : moduleFileName) {
                        moduleName += (wc < 128) ? static_cast<char>(wc) : '?';
                    }
                    data["module_name"] = moduleName;
                }
            }

            return Renge::MakeResponse(id, data).dump();
        }

        // === walk_datatable_rows: Browse DataTable RowMap entries ===
        if (cmd == Renge::CMD_WALK_DATATABLE_ROWS) {
            std::string addrStr = request.value("addr", "");
            if (addrStr.empty()) return Renge::MakeError(id, "Missing addr").dump();

            uintptr_t addr = Renge::StrToAddr(addrStr);
            int32_t offset = request.value("offset", 0);
            int32_t limit  = request.value("limit", 64);

            auto result = Ubel::WalkDataTableRows(addr, offset, limit);

            if (!result.ok)
                return Renge::MakeError(id, result.error).dump();

            json data;
            data["row_count"]       = result.rowCount;
            data["row_map_offset"]  = result.rowMapOffset;
            data["row_struct_addr"] = Renge::AddrToStr(result.rowStructAddr);
            data["row_struct_name"] = result.rowStructName;
            data["fname_size"]      = result.fnameSize;
            data["stride"]          = result.stride;

            json rows = json::array();
            for (const auto& row : result.rows) {
                json rj;
                rj["sparse_index"] = row.sparseIndex;
                rj["row_name"]     = row.rowName;
                rj["data_addr"]    = Renge::AddrToStr(row.rowDataAddr);

                json rowFields = json::array();
                for (const auto& fv : row.fields) {
                    rowFields.push_back(SerializeField(fv));
                }
                rj["fields"] = rowFields;
                rows.push_back(rj);
            }
            data["rows"] = rows;
            return Renge::MakeResponse(id, data).dump();
        }

        // ── invoke_function: Call ProcessEvent via pipe (bypasses CE executeCodeEx) ──
        if (cmd == Renge::CMD_INVOKE_FUNCTION) {
            std::string className   = request.value("class_name", "");
            std::string funcName    = request.value("func_name", "");
            std::string instAddrStr = request.value("instance_addr", "");
            std::string paramsHex   = request.value("params_hex", "");
            int parmsSize           = request.value("parms_size", 0);

            if (funcName.empty()) {
                return Renge::MakeError(id, "func_name is required").dump();
            }

            // Resolve instance address
            uintptr_t instanceAddr = 0;
            if (!instAddrStr.empty()) {
                instanceAddr = Renge::StrToAddr(instAddrStr);
                if (instanceAddr == 0) {
                    return Renge::MakeError(id, "Invalid instance_addr").dump();
                }
            } else if (!className.empty()) {
                instanceAddr = UE5_FindInstanceOfClass(className.c_str());
                if (instanceAddr == 0) {
                    return Renge::MakeError(id,
                        "No instance found for class: " + className).dump();
                }
            } else {
                return Renge::MakeError(id,
                    "Either instance_addr or class_name is required").dump();
            }

            // Get class address from instance
            uintptr_t classAddr = UE5_GetObjectClass(instanceAddr);
            if (classAddr == 0) {
                return Renge::MakeError(id,
                    "Failed to read class from instance " + Renge::AddrToStr(instanceAddr)).dump();
            }

            // Resolve UFunction
            uintptr_t ufuncAddr = UE5_FindFunctionByName(classAddr, funcName.c_str());
            if (ufuncAddr == 0) {
                return Renge::MakeError(id,
                    "Function not found: " + funcName).dump();
            }

            // Build parameter buffer (zero-filled, then overlay hex bytes)
            size_t bufSize = (parmsSize > 0) ? static_cast<size_t>(parmsSize) : 0;
            std::vector<uint8_t> paramBuf(bufSize, 0);

            if (!paramsHex.empty()) {
                auto hexBytes = Renge::HexToBytes(paramsHex);
                size_t copyLen = (std::min)(hexBytes.size(), paramBuf.size());
                if (copyLen > 0) {
                    memcpy(paramBuf.data(), hexBytes.data(), copyLen);
                }
            }

            uintptr_t paramPtr = bufSize > 0
                ? reinterpret_cast<uintptr_t>(paramBuf.data())
                : 0;

            Sein::Info("PIPE:cmd", "invoke_function: %s::%s inst=%s func=%s parms=%d",
                         className.c_str(), funcName.c_str(),
                         Renge::AddrToStr(instanceAddr).c_str(),
                         Renge::AddrToStr(ufuncAddr).c_str(),
                         (int)bufSize);

            // Call ProcessEvent
            int32_t callResult = UE5_CallProcessEvent(instanceAddr, ufuncAddr, paramPtr);

            // Build response
            json data;
            data["result"]        = callResult;
            data["instance_addr"] = Renge::AddrToStr(instanceAddr);
            data["func_addr"]     = Renge::AddrToStr(ufuncAddr);
            data["parms_size"]    = (int)bufSize;

            // Return post-call buffer (may contain out-param values)
            if (bufSize > 0) {
                data["result_hex"] = Renge::BytesToHex(paramBuf.data(), bufSize);
            }

            if (callResult == 0) {
                data["message"] = "ProcessEvent OK";
            } else {
                std::string errMsg = "ProcessEvent error code " + std::to_string(callResult);
                if (callResult == -1)      errMsg += " (invalid args)";
                else if (callResult == -2) errMsg += " (vtable read failed)";
                else if (callResult == -3) errMsg += " (ProcessEvent offset not found)";
                else if (callResult == -4) errMsg += " (exception during call)";
                else if (callResult == -5) errMsg += " (game-thread dispatch timeout)";
                else if (callResult == -7) errMsg += " (hook not active, direct call used)";
                data["error"] = errMsg;
            }

            return Renge::MakeResponse(id, data).dump();
        }

        return Renge::MakeError(id, "Unknown command: " + cmd).dump();

    } catch (const std::exception& e) {
        Sein::Error("PIPE:cmd", "PipeServer: Exception in command '%s': %s", cmd.c_str(), e.what());
        return Renge::MakeError(id, std::string("Internal error: ") + e.what()).dump();
    }
}

// ============================================================
// RunScan — Background thread for initial scan (trigger_scan)
// ============================================================
void Fern::RunScan() {
    Sein::Info("PIPE:scan", "RunScan: started");
    UE5_Init();  // Updates ScanProgress phases 1-7
    m_scan.completed = true;
    m_scan.running.store(false);
    Sein::Info("PIPE:scan", "RunScan: finished");
}

// ============================================================
// RunRescan — Background thread for aggressive pointer recovery
// ============================================================
void Fern::RunRescan(bool scanGObjects, bool scanGWorld) {
    Sein::Info("PIPE:rescan", "RunRescan: started (GObjects=%d, GWorld=%d)",
                 scanGObjects, scanGWorld);

    if (scanGObjects) {
        m_rescan.phase.store(1);
        {
            std::lock_guard<std::mutex> lock(m_rescan.statusMutex);
            m_rescan.statusText = "Scanning GObjects (.data heuristic)...";
        }

        uintptr_t result = Genau::ExtraScanGObjects();
        if (result) {
            m_rescan.foundGObjects = result;
            m_rescan.gobjectsMethod = "data_heuristic";
            Sein::Info("PIPE:rescan", "RunRescan: GObjects found at 0x%llX",
                         static_cast<unsigned long long>(result));
        } else {
            Sein::Info("PIPE:rescan", "RunRescan: GObjects not found");
        }
    }

    if (scanGWorld) {
        m_rescan.phase.store(2);
        {
            std::lock_guard<std::mutex> lock(m_rescan.statusMutex);
            m_rescan.statusText = "Scanning GWorld (instance scan)...";
        }

        uintptr_t result = Genau::ExtraScanGWorld();
        if (result) {
            m_rescan.foundGWorld = result;
            m_rescan.gworldMethod = "instance_scan";
            Sein::Info("PIPE:rescan", "RunRescan: GWorld found at 0x%llX",
                         static_cast<unsigned long long>(result));
        } else {
            Sein::Info("PIPE:rescan", "RunRescan: GWorld not found");
        }
    }

    m_rescan.phase.store(3);
    {
        std::lock_guard<std::mutex> lock(m_rescan.statusMutex);
        m_rescan.statusText = "Complete";
    }
    m_rescan.running.store(false);

    Sein::Info("PIPE:rescan", "RunRescan: finished (foundGObj=0x%llX, foundGWld=0x%llX)",
                 static_cast<unsigned long long>(m_rescan.foundGObjects),
                 static_cast<unsigned long long>(m_rescan.foundGWorld));
}

void Fern::StartWatch(uintptr_t addr, uint32_t size, uint32_t interval_ms) {
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
            if (Macht::ReadBytesSafe(ptr->addr, buf.data(), ptr->size)) {
                json data;
                data["addr"]      = Renge::AddrToStr(ptr->addr);
                data["bytes"]     = Renge::BytesToHex(buf.data(), buf.size());
                data["timestamp"] = std::chrono::duration_cast<std::chrono::milliseconds>(
                    std::chrono::system_clock::now().time_since_epoch()).count();

                PushEvent(Renge::MakeEvent(Renge::EVT_WATCH, data).dump());
            }
            std::this_thread::sleep_for(std::chrono::milliseconds(ptr->interval_ms));
        }
    });

    m_watches[addr] = std::move(entry);
    Sein::Info("PIPE:watch", "PipeServer: Watch started on 0x%llX (size=%u, interval=%ums)",
             static_cast<unsigned long long>(addr), size, interval_ms);
}

void Fern::StopWatch(uintptr_t addr) {
    // Audit fix #4: extract the entry under m_watchMutex but join the thread
    // OUTSIDE the lock. The watch thread calls PushEvent → m_pipeMutex /
    // m_writeMutex; if we held m_watchMutex while joining and another
    // thread tried to take both locks in the opposite order, we'd deadlock.
    // Safer to release m_watchMutex before the blocking join.
    std::unique_ptr<WatchEntry> entry;
    {
        std::lock_guard<std::mutex> lock(m_watchMutex);
        auto it = m_watches.find(addr);
        if (it == m_watches.end()) return;
        it->second->active = false;
        entry = std::move(it->second);
        m_watches.erase(it);
    }

    if (entry && entry->watchThread.joinable()) {
        entry->watchThread.join();
    }
    Sein::Info("PIPE:watch", "PipeServer: Watch stopped on 0x%llX",
             static_cast<unsigned long long>(addr));
}

void Fern::StopAllWatches() {
    // Audit fix #4: same pattern as StopWatch — extract under lock, join
    // afterwards. Set every entry's active=false first so all watch threads
    // start exiting in parallel; then drain the map and join each.
    std::vector<std::unique_ptr<WatchEntry>> toJoin;
    {
        std::lock_guard<std::mutex> lock(m_watchMutex);
        toJoin.reserve(m_watches.size());
        for (auto& [addr, entry] : m_watches) {
            entry->active = false;
        }
        for (auto& [addr, entry] : m_watches) {
            toJoin.push_back(std::move(entry));
        }
        m_watches.clear();
    }

    for (auto& entry : toJoin) {
        if (entry && entry->watchThread.joinable()) {
            entry->watchThread.join();
        }
    }
}
