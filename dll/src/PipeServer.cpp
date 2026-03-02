// ============================================================
// PipeServer.cpp — Named Pipe IPC server implementation
// ============================================================

#include "PipeServer.h"
#include "PipeProtocol.h"
#include "Constants.h"
#define LOG_CAT "PIPE:svr"
#include "Logger.h"
#include "Memory.h"
#include "OffsetFinder.h"
#include "ObjectArray.h"
#include "FNamePool.h"
#include "UStructWalker.h"
#include "BuildInfo.h"

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
    if (!m_running.exchange(false)) return; // Already stopped
    StopAllWatches();

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
            Logger::Warn("PIPE:cmd", "PipeServer: Line too long, dropping");
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

        Logger::Debug("PIPE:cmd", "PipeServer: Received: %s", line.c_str());

        std::string response = DispatchCommand(line);
        if (!response.empty()) {
            if (!WriteLine(pipe, response)) {
                Logger::Error("PIPE:cmd", "PipeServer: Failed to write response");
                break;
            }
        }
    }
}

void PipeServer::PushEvent(const std::string& jsonLine) {
    if (!m_clientConnected.load()) return;
    HANDLE pipe;
    {
        std::lock_guard<std::mutex> lock(m_pipeMutex);
        pipe = m_pipe;
    }
    if (pipe == INVALID_HANDLE_VALUE) return;
    WriteLine(pipe, jsonLine);
}

std::string PipeServer::DispatchCommand(const std::string& jsonLine) {
    json request;
    try {
        request = json::parse(jsonLine);
    } catch (const json::exception& e) {
        Logger::Error("PIPE:cmd", "PipeServer: JSON parse error: %s", e.what());
        return PipeProtocol::MakeError(0, "Invalid JSON").dump();
    }

    int id = request.value("id", 0);
    std::string cmd = request.value("cmd", "");

    try {
        if (cmd == PipeProtocol::CMD_INIT) {
            extern uint32_t g_cachedUEVersion;
            extern bool     g_cachedVersionDetected;
            json data;
            data["ue_version"]       = g_cachedUEVersion;
            data["version_detected"] = g_cachedVersionDetected;
            data["build_git"]  = BUILD_GIT_SHORT;
            data["build_hash"] = BUILD_GIT_HASH;
            data["build_time"] = BUILD_TIMESTAMP;
            data["build_info"] = BUILD_VERSION_STRING;
            return PipeProtocol::MakeResponse(id, data).dump();
        }

        if (cmd == PipeProtocol::CMD_GET_POINTERS) {
            // These are filled by ExportAPI's cached EnginePointers
            extern uintptr_t   g_cachedGObjects;
            extern uintptr_t   g_cachedGNames;
            extern uintptr_t   g_cachedGWorld;
            extern uint32_t    g_cachedUEVersion;
            extern bool        g_cachedVersionDetected;
            extern const char* g_cachedGObjectsMethod;
            extern const char* g_cachedGNamesMethod;
            extern const char* g_cachedGWorldMethod;
            // AOB Usage Tracking
            extern char        g_cachedPeHash[17];
            extern const char* g_cachedGObjectsPatternId;
            extern const char* g_cachedGNamesPatternId;
            extern const char* g_cachedGWorldPatternId;
            extern int         g_cachedGObjectsTried, g_cachedGObjectsHit;
            extern int         g_cachedGNamesTried,   g_cachedGNamesHit;
            extern int         g_cachedGWorldTried,   g_cachedGWorldHit;

            json data;
            data["gobjects"]         = PipeProtocol::AddrToStr(g_cachedGObjects);
            data["gnames"]           = PipeProtocol::AddrToStr(g_cachedGNames);
            data["gworld"]           = PipeProtocol::AddrToStr(g_cachedGWorld);
            data["ue_version"]       = g_cachedUEVersion;
            data["version_detected"] = g_cachedVersionDetected;
            data["object_count"]     = ObjectArray::GetCount();
            data["gobjects_method"]  = g_cachedGObjectsMethod;
            data["gnames_method"]    = g_cachedGNamesMethod;
            data["gworld_method"]    = g_cachedGWorldMethod;

            // AOB Usage Tracking
            data["pe_hash"] = g_cachedPeHash;
            data["gobjects_pattern_id"] = g_cachedGObjectsPatternId ? g_cachedGObjectsPatternId : "";
            data["gnames_pattern_id"]   = g_cachedGNamesPatternId   ? g_cachedGNamesPatternId   : "";
            data["gworld_pattern_id"]   = g_cachedGWorldPatternId   ? g_cachedGWorldPatternId   : "";
            json scanStats;
            scanStats["gobjects_tried"] = g_cachedGObjectsTried;
            scanStats["gobjects_hit"]   = g_cachedGObjectsHit;
            scanStats["gnames_tried"]   = g_cachedGNamesTried;
            scanStats["gnames_hit"]     = g_cachedGNamesHit;
            scanStats["gworld_tried"]   = g_cachedGWorldTried;
            scanStats["gworld_hit"]     = g_cachedGWorldHit;
            data["scan_stats"] = scanStats;

            // Module info for CE address formatting
            uintptr_t moduleBase = Mem::GetModuleBase(nullptr);
            data["module_base"] = PipeProtocol::AddrToStr(moduleBase);
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

                std::string name = UStructWalker::GetName(obj);
                if (name.empty()) continue; // Skip unnamed objects

                json item;
                item["addr"]  = PipeProtocol::AddrToStr(obj);
                item["name"]  = name;

                uintptr_t cls = UStructWalker::GetClass(obj);
                item["class"] = cls ? UStructWalker::GetName(cls) : "";

                uintptr_t outer = UStructWalker::GetOuter(obj);
                item["outer"] = outer ? PipeProtocol::AddrToStr(outer) : "";

                objects.push_back(item);
            }

            json data;
            data["total"]   = total;
            data["scanned"] = end - offset; // Number of indices scanned (for pagination)
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

        if (cmd == PipeProtocol::CMD_SEARCH_OBJECTS) {
            std::string query = request.value("query", "");
            int limit = request.value("limit", 200);
            if (query.empty()) return PipeProtocol::MakeError(id, "Missing query").dump();

            auto rset = ObjectArray::SearchByName(query, limit);

            json objects = json::array();
            for (const auto& sr : rset.results) {
                json item;
                item["addr"]  = PipeProtocol::AddrToStr(sr.addr);
                item["name"]  = sr.name;
                item["class"] = sr.className;
                item["outer"] = PipeProtocol::AddrToStr(sr.outer);
                objects.push_back(item);
            }

            json data;
            data["total"]   = static_cast<int>(rset.results.size());
            data["scanned"] = rset.scanned;
            data["objects"] = objects;
            return PipeProtocol::MakeResponse(id, data).dump();
        }

        if (cmd == PipeProtocol::CMD_WALK_CLASS) {
            std::string addrStr = request.value("addr", "");
            if (addrStr.empty()) return PipeProtocol::MakeError(id, "Missing addr").dump();

            uintptr_t addr = PipeProtocol::StrToAddr(addrStr);
            ClassInfo ci = UStructWalker::WalkClassEx(addr);

            json classData;
            classData["name"]       = ci.Name;
            classData["full_path"]  = ci.FullPath;
            classData["super_addr"] = PipeProtocol::AddrToStr(ci.SuperClass);
            classData["super_name"] = ci.SuperName;
            classData["props_size"] = ci.PropertiesSize;

            json fields = json::array();
            for (const auto& f : ci.Fields) {
                json fj = {
                    {"addr",   PipeProtocol::AddrToStr(f.Address)},
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
            return PipeProtocol::MakeResponse(id, data).dump();
        }

        // list_enums: enumerate all UEnum objects with their entries
        if (cmd == PipeProtocol::CMD_LIST_ENUMS) {
            int total = ObjectArray::GetCount();
            json enums = json::array();

            for (int i = 0; i < total; ++i) {
                uintptr_t obj = ObjectArray::GetByIndex(i);
                if (!obj) continue;

                // Check if this object's class is "Enum" (UEnum inherits UObject)
                uintptr_t cls = UStructWalker::GetClass(obj);
                if (!cls) continue;
                std::string clsName = UStructWalker::GetName(cls);
                if (clsName != "Enum") continue;

                std::string name = UStructWalker::GetName(obj);
                if (name.empty()) continue;

                // Read enum entries via cached resolver
                auto entries = UStructWalker::GetEnumEntries(obj);

                json enumObj;
                enumObj["addr"]      = PipeProtocol::AddrToStr(obj);
                enumObj["name"]      = name;
                enumObj["full_path"] = UStructWalker::GetFullName(obj);

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
            return PipeProtocol::MakeResponse(id, data).dump();
        }

        if (cmd == PipeProtocol::CMD_WALK_FUNCTIONS) {
            std::string addrStr = request.value("addr", "");
            if (addrStr.empty()) return PipeProtocol::MakeError(id, "Missing addr").dump();

            uintptr_t addr = PipeProtocol::StrToAddr(addrStr);
            auto funcs = UStructWalker::WalkFunctions(addr);

            json funcArr = json::array();
            for (const auto& f : funcs) {
                json fj;
                fj["name"]    = f.name;
                fj["full"]    = f.fullName;
                fj["addr"]    = PipeProtocol::AddrToStr(f.address);
                fj["flags"]   = f.functionFlags;
                fj["ret"]     = f.returnType;

                json params = json::array();
                for (const auto& p : f.params) {
                    json pj;
                    pj["name"]  = p.name;
                    pj["type"]  = p.typeName;
                    pj["size"]  = p.size;
                    pj["out"]   = p.isOut;
                    pj["ret"]   = p.isReturn;
                    params.push_back(pj);
                }
                fj["params"] = params;
                funcArr.push_back(fj);
            }

            json data;
            data["functions"] = funcArr;
            data["count"]     = static_cast<int>(funcArr.size());
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
            if (bytes.empty() || bytes.size() > 65536) {
                return PipeProtocol::MakeError(id, "Invalid write size (max 65536)").dump();
            }
            if (!Mem::WriteBytes(addr, bytes.data(), bytes.size())) {
                return PipeProtocol::MakeError(id, "Write failed").dump();
            }

            return PipeProtocol::MakeResponse(id).dump();
        }

        // === walk_instance: Read live field values from a UObject instance ===
        if (cmd == PipeProtocol::CMD_WALK_INSTANCE) {
            std::string addrStr = request.value("addr", "");
            if (addrStr.empty()) return PipeProtocol::MakeError(id, "Missing addr").dump();

            uintptr_t addr = PipeProtocol::StrToAddr(addrStr);
            std::string classAddrStr = request.value("class_addr", "");
            uintptr_t classAddr = classAddrStr.empty() ? 0 : PipeProtocol::StrToAddr(classAddrStr);
            int32_t arrayLimit = request.value("array_limit", 64);

            auto result = UStructWalker::WalkInstance(addr, classAddr, arrayLimit);

            json data;
            data["addr"]       = PipeProtocol::AddrToStr(result.addr);
            data["name"]       = result.name;
            data["class"]      = result.className;
            data["class_addr"] = PipeProtocol::AddrToStr(result.classAddr);
            data["outer"]      = PipeProtocol::AddrToStr(result.outerAddr);
            data["outer_name"] = result.outerName;
            data["outer_class"]= result.outerClassName;

            json fields = json::array();
            for (const auto& fv : result.fields) {
                json fj;
                fj["name"]   = fv.name;
                fj["type"]   = fv.typeName;
                fj["offset"] = fv.offset;
                fj["size"]   = fv.size;

                if (!fv.hexValue.empty())   fj["hex"]   = fv.hexValue;
                if (!fv.typedValue.empty())  fj["value"] = fv.typedValue;

                // ObjectProperty: pointer info
                if (fv.ptrValue != 0) {
                    fj["ptr"]       = PipeProtocol::AddrToStr(fv.ptrValue);
                    fj["ptr_name"]  = fv.ptrName;
                    fj["ptr_class"] = fv.ptrClassName;
                    if (fv.ptrClassAddr)
                        fj["ptr_class_addr"] = PipeProtocol::AddrToStr(fv.ptrClassAddr);
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
                        fj["array_data_addr"] = PipeProtocol::AddrToStr(fv.arrayDataAddr);
                    if (!fv.arrayInnerType.empty()) {
                        fj["array_inner_type"] = fv.arrayInnerType;
                        if (fv.arrayElemSize > 0)
                            fj["array_elem_size"] = fv.arrayElemSize;
                        if (!fv.arrayInnerStructType.empty())
                            fj["array_struct_type"] = fv.arrayInnerStructType;
                        if (fv.arrayInnerStructAddr != 0)
                            fj["array_struct_class_addr"] = PipeProtocol::AddrToStr(fv.arrayInnerStructAddr);
                    }
                    if (fv.arrayInnerFFieldAddr != 0)
                        fj["array_inner_addr"] = PipeProtocol::AddrToStr(fv.arrayInnerFFieldAddr);
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
                                ej["pa"] = PipeProtocol::AddrToStr(e.ptrAddr);
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
                                        sfj["pa"] = PipeProtocol::AddrToStr(sf.ptrAddr);
                                        sfj["pn"] = sf.ptrName;
                                        sfj["pc"] = sf.ptrClassName;
                                        sfj["pca"] = PipeProtocol::AddrToStr(sf.ptrClassAddr);
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
                        fj["enum_addr"] = PipeProtocol::AddrToStr(fv.arrayEnumAddr);
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
                    if (fv.mapDataAddr != 0)
                        fj["map_data_addr"] = PipeProtocol::AddrToStr(fv.mapDataAddr);
                    if (fv.mapKeyStructAddr != 0) {
                        fj["map_key_struct_addr"] = PipeProtocol::AddrToStr(fv.mapKeyStructAddr);
                        fj["map_key_struct_type"] = fv.mapKeyStructType;
                    }
                    if (fv.mapValueStructAddr != 0) {
                        fj["map_value_struct_addr"] = PipeProtocol::AddrToStr(fv.mapValueStructAddr);
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
                            if (e.keyPtrAddr != 0)       ej["ka"] = PipeProtocol::AddrToStr(e.keyPtrAddr);
                            if (!e.keyPtrClassName.empty()) ej["kc"] = e.keyPtrClassName;
                            if (!e.valuePtrName.empty()) ej["vn"] = e.valuePtrName;
                            if (e.valuePtrAddr != 0)     ej["va"] = PipeProtocol::AddrToStr(e.valuePtrAddr);
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
                        fj["set_data_addr"] = PipeProtocol::AddrToStr(fv.setDataAddr);
                    if (fv.setElemStructAddr != 0) {
                        fj["set_elem_struct_addr"] = PipeProtocol::AddrToStr(fv.setElemStructAddr);
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
                            if (e.keyPtrAddr != 0)    ej["ka"] = PipeProtocol::AddrToStr(e.keyPtrAddr);
                            if (!e.keyPtrClassName.empty()) ej["kc"] = e.keyPtrClassName;
                            elems.push_back(ej);
                        }
                        fj["set_elements"] = elems;
                    }
                }

                // StructProperty: inner struct info
                if (fv.structDataAddr != 0) {
                    fj["struct_data_addr"]  = PipeProtocol::AddrToStr(fv.structDataAddr);
                    fj["struct_class_addr"] = PipeProtocol::AddrToStr(fv.structClassAddr);
                    fj["struct_type"]       = fv.structTypeName;
                }

                // EnumProperty / ByteProperty-with-enum: resolved name, value, and full entries
                if (!fv.enumName.empty()) {
                    fj["enum_name"]  = fv.enumName;
                    fj["enum_value"] = fv.enumValue;
                }
                if (fv.enumAddr != 0 && !fv.enumEntries.empty()) {
                    fj["enum_addr"] = PipeProtocol::AddrToStr(fv.enumAddr);
                    json enumEntries = json::array();
                    for (const auto& ee : fv.enumEntries)
                        enumEntries.push_back({{"v", ee.value}, {"n", ee.name}});
                    fj["enum_entries"] = enumEntries;
                }

                // StrProperty: decoded string value
                if (!fv.strValue.empty()) {
                    fj["str_value"] = fv.strValue;
                }

                fields.push_back(fj);
            }
            data["fields"] = fields;
            return PipeProtocol::MakeResponse(id, data).dump();
        }

        // === read_array_elements: Read scalar elements from a TArray (Phase B) ===
        if (cmd == PipeProtocol::CMD_READ_ARRAY_ELEMS) {
            std::string addrStr = request.value("addr", "");
            if (addrStr.empty())
                return PipeProtocol::MakeError(id, "missing 'addr'").dump();
            uintptr_t addr = PipeProtocol::StrToAddr(addrStr);

            int32_t fieldOffset = request.value("field_offset", 0);
            std::string innerAddrStr = request.value("inner_addr", "");
            uintptr_t innerAddr = innerAddrStr.empty() ? 0 : PipeProtocol::StrToAddr(innerAddrStr);
            std::string innerType = request.value("inner_type", "");
            int32_t elemSize = request.value("elem_size", 0);
            int32_t offset = request.value("offset", 0);
            int32_t limit = request.value("limit", 64);

            if (innerType.empty() || elemSize <= 0)
                return PipeProtocol::MakeError(id, "missing inner_type or invalid elem_size").dump();

            auto result = UStructWalker::ReadArrayElements(
                addr, fieldOffset, innerAddr, innerType, elemSize, offset, limit);

            if (!result.ok)
                return PipeProtocol::MakeError(id, result.error).dump();

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
                    ej["pa"] = PipeProtocol::AddrToStr(e.ptrAddr);
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
                            sfj["pa"] = PipeProtocol::AddrToStr(sf.ptrAddr);
                            sfj["pn"] = sf.ptrName;
                            sfj["pc"] = sf.ptrClassName;
                            sfj["pca"] = PipeProtocol::AddrToStr(sf.ptrClassAddr);
                        }
                        sfs.push_back(sfj);
                    }
                    ej["sf"] = sfs;
                }
                elems.push_back(ej);
            }
            data["elements"] = elems;
            return PipeProtocol::MakeResponse(id, data).dump();
        }

        // === walk_world: Browse GWorld → PersistentLevel → Actors hierarchy ===
        if (cmd == PipeProtocol::CMD_WALK_WORLD) {
            extern uintptr_t g_cachedGWorld;

            // Allow overriding with a custom address
            std::string addrStr = request.value("addr", "");
            uintptr_t worldAddr = 0;
            if (!addrStr.empty()) {
                worldAddr = PipeProtocol::StrToAddr(addrStr);
            } else {
                // g_cachedGWorld is &GWorld (address of the global pointer variable).
                // Must dereference to get the actual UWorld* value.
                if (g_cachedGWorld) {
                    bool ok = Mem::ReadSafe(g_cachedGWorld, worldAddr);
                    Logger::Info("PIPE:world", "GWorld deref: &GWorld=0x%llX -> UWorld*=0x%llX (ReadSafe=%s)",
                        static_cast<unsigned long long>(g_cachedGWorld),
                        static_cast<unsigned long long>(worldAddr),
                        ok ? "ok" : "fail");
                }

                // Fallback: if AOB-resolved GWorld is null/wrong, search GObjects for a UWorld instance.
                // This handles games where the AOB pattern matched the wrong global variable.
                if (!worldAddr) {
                    Logger::Info("PIPE:world", "GWorld pointer is null, searching GObjects for UWorld...");
                    ObjectArray::ForEach([&](int32_t idx, uintptr_t obj) -> bool {
                        uintptr_t cls = UStructWalker::GetClass(obj);
                        if (!cls) return true; // continue
                        std::string clsName = UStructWalker::GetName(cls);
                        if (clsName == "World") {
                            // Skip CDOs (Default__World) — they have null PersistentLevel
                            std::string objName = UStructWalker::GetName(obj);
                            if (objName.rfind("Default__", 0) == 0) {
                                Logger::Debug("PIPE:world", "Skipping CDO '%s' at 0x%llX",
                                    objName.c_str(), static_cast<unsigned long long>(obj));
                                return true; // continue
                            }
                            worldAddr = obj;
                            Logger::Info("PIPE:world", "Found UWorld '%s' via GObjects scan: 0x%llX (index=%d)",
                                objName.c_str(), static_cast<unsigned long long>(obj), idx);
                            return false; // stop
                        }
                        return true; // continue
                    });
                }
            }

            if (!worldAddr) return PipeProtocol::MakeError(id, "GWorld not found — no UWorld instance in GObjects").dump();

            json data;
            data["world_addr"] = PipeProtocol::AddrToStr(worldAddr);
            data["world_name"] = UStructWalker::GetName(worldAddr);

            // Walk UWorld class to find PersistentLevel field offset dynamically
            uintptr_t worldClass = UStructWalker::GetClass(worldAddr);
            if (!worldClass) return PipeProtocol::MakeError(id, "Cannot read UWorld class").dump();

            ClassInfo worldCI = UStructWalker::WalkClass(worldClass);

            // Find PersistentLevel field (ObjectProperty)
            uintptr_t levelAddr = 0;
            int persistentLevelOffset = 0;
            bool foundPersistentLevel = false;
            for (const auto& f : worldCI.Fields) {
                if (f.Name == "PersistentLevel" && f.Size >= 8) {
                    foundPersistentLevel = true;
                    persistentLevelOffset = f.Offset;
                    Mem::ReadSafe(worldAddr + f.Offset, levelAddr);
                    break;
                }
            }

            if (!foundPersistentLevel) {
                data["error"] = "PersistentLevel field not found in UWorld class (WalkClass returned "
                    + std::to_string(worldCI.Fields.size()) + " fields)";
                Logger::Warn("PIPE:world", "%s", data["error"].get<std::string>().c_str());
                return PipeProtocol::MakeResponse(id, data).dump();
            }
            if (!levelAddr) {
                data["error"] = "PersistentLevel is null (CDO or uninitialized world instance)";
                Logger::Warn("PIPE:world", "%s", data["error"].get<std::string>().c_str());
                return PipeProtocol::MakeResponse(id, data).dump();
            }

            data["level_addr"] = PipeProtocol::AddrToStr(levelAddr);
            data["level_name"] = UStructWalker::GetName(levelAddr);
            data["level_offset"] = persistentLevelOffset;

            // Walk ULevel class to find Actors TArray field
            uintptr_t levelClass = UStructWalker::GetClass(levelAddr);
            ClassInfo levelCI = levelClass ? UStructWalker::WalkClass(levelClass) : ClassInfo{};

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
                Mem::TArrayView actorArr;
                if (Mem::ReadTArray(levelAddr + actorsOffset, actorArr)) {
                    int count = (std::min)(actorArr.Count, actorLimit);
                    for (int i = 0; i < count; ++i) {
                        uintptr_t actorAddr = Mem::ReadTArrayElement(actorArr, i);
                        if (!actorAddr) continue;

                        json actorItem;
                        actorItem["addr"]  = PipeProtocol::AddrToStr(actorAddr);
                        actorItem["name"]  = UStructWalker::GetName(actorAddr);
                        actorItem["index"] = UStructWalker::GetIndex(actorAddr);

                        uintptr_t actorCls = UStructWalker::GetClass(actorAddr);
                        actorItem["class"] = actorCls ? UStructWalker::GetName(actorCls) : "";

                        // Try to find OwnedComponents on this actor
                        ClassInfo actorCI = actorCls ? UStructWalker::WalkClass(actorCls) : ClassInfo{};
                        int compsOffset = -1;
                        for (const auto& f : actorCI.Fields) {
                            if (f.Name == "OwnedComponents" && f.TypeName == "ArrayProperty") {
                                compsOffset = f.Offset;
                                break;
                            }
                        }

                        if (compsOffset >= 0) {
                            Mem::TArrayView compArr;
                            if (Mem::ReadTArray(actorAddr + compsOffset, compArr)) {
                                json comps = json::array();
                                int compCount = (std::min)(compArr.Count, 64); // Limit components
                                for (int c = 0; c < compCount; ++c) {
                                    uintptr_t compAddr = Mem::ReadTArrayElement(compArr, c);
                                    if (!compAddr) continue;

                                    json compItem;
                                    compItem["addr"] = PipeProtocol::AddrToStr(compAddr);
                                    compItem["name"] = UStructWalker::GetName(compAddr);
                                    uintptr_t compCls = UStructWalker::GetClass(compAddr);
                                    compItem["class"] = compCls ? UStructWalker::GetName(compCls) : "";
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
            return PipeProtocol::MakeResponse(id, data).dump();
        }

        // === find_instances: Search GObjects for instances of a given class ===
        if (cmd == PipeProtocol::CMD_FIND_INSTANCES) {
            std::string className = request.value("class_name", "");
            bool exactMatch = request.value("exact_match", false);
            int limit = request.value("limit", 500);
            if (className.empty()) return PipeProtocol::MakeError(id, "Missing class_name").dump();

            auto rset = ObjectArray::FindInstancesByClass(className, exactMatch, limit);

            json instances = json::array();
            for (const auto& sr : rset.results) {
                json item;
                item["addr"]  = PipeProtocol::AddrToStr(sr.addr);
                item["index"] = sr.index;
                item["name"]  = sr.name;
                item["class"] = sr.className;
                item["outer"] = PipeProtocol::AddrToStr(sr.outer);
                instances.push_back(item);
            }

            json data;
            data["total"]     = static_cast<int>(rset.results.size());
            data["scanned"]   = rset.scanned;
            data["non_null"]  = rset.nonNull;
            data["named"]     = rset.named;
            data["instances"] = instances;
            return PipeProtocol::MakeResponse(id, data).dump();
        }

        // === search_properties: Keyword search across all UClass properties ===
        if (cmd == PipeProtocol::CMD_SEARCH_PROPERTIES) {
            std::string query = request.value("query", "");
            bool gameOnly = request.value("game_only", true);
            int limit = request.value("limit", 200);
            if (query.empty()) return PipeProtocol::MakeError(id, "Missing query").dump();

            // Parse optional type filter
            std::vector<std::string> typeFilter;
            if (request.contains("types") && request["types"].is_array()) {
                for (const auto& t : request["types"]) {
                    if (t.is_string()) typeFilter.push_back(t.get<std::string>());
                }
            }

            auto searchResult = ObjectArray::SearchProperties(query, typeFilter, gameOnly, limit);

            json matches = json::array();
            for (const auto& m : searchResult.results) {
                json item;
                item["class_name"]  = m.className;
                item["class_addr"]  = PipeProtocol::AddrToStr(m.classAddr);
                item["class_path"]  = m.classPath;
                item["super_name"]  = m.superName;
                item["prop_name"]   = m.propName;
                item["prop_type"]   = m.propType;
                item["prop_offset"] = m.propOffset;
                item["prop_size"]   = m.propSize;
                item["struct_type"] = m.structType;
                item["inner_type"]  = m.innerType;
                matches.push_back(item);
            }

            json data;
            data["total"]           = static_cast<int>(searchResult.results.size());
            data["scanned_classes"] = searchResult.scannedClasses;
            data["scanned_objects"] = searchResult.scannedObjects;
            data["results"]         = matches;
            return PipeProtocol::MakeResponse(id, data).dump();
        }

        // === list_classes: List all UClass objects (optionally game-only) ===
        if (cmd == PipeProtocol::CMD_LIST_CLASSES) {
            bool gameOnly = request.value("game_only", true);
            int limit = request.value("limit", 5000);

            auto listResult = ObjectArray::ListClasses(gameOnly, limit);

            json classes = json::array();
            for (const auto& e : listResult.results) {
                json item;
                item["class_name"]      = e.className;
                item["class_addr"]      = PipeProtocol::AddrToStr(e.classAddr);
                item["class_path"]      = e.classPath;
                item["super_name"]      = e.superName;
                item["property_count"]  = e.propertyCount;
                item["properties_size"] = e.propertiesSize;
                classes.push_back(item);
            }

            json data;
            data["total"]           = static_cast<int>(listResult.results.size());
            data["scanned_objects"] = listResult.scannedObjects;
            data["total_classes"]   = listResult.totalClasses;
            data["classes"]         = classes;
            return PipeProtocol::MakeResponse(id, data).dump();
        }

        // === find_by_address: Reverse lookup — address to UObject instance ===
        if (cmd == PipeProtocol::CMD_FIND_BY_ADDRESS) {
            std::string addrStr = request.value("addr", "");
            if (addrStr.empty()) return PipeProtocol::MakeError(id, "Missing addr").dump();

            uintptr_t queryAddr = PipeProtocol::StrToAddr(addrStr);
            auto lookupResult = ObjectArray::FindByAddress(queryAddr);

            json data;
            data["found"] = lookupResult.found;

            if (lookupResult.found) {
                data["match_type"]       = lookupResult.exactMatch ? "exact" : "contains";
                data["addr"]             = PipeProtocol::AddrToStr(lookupResult.objectAddr);
                data["index"]            = lookupResult.index;
                data["name"]             = lookupResult.name;
                data["class"]            = lookupResult.className;
                data["outer"]            = PipeProtocol::AddrToStr(lookupResult.outer);
                data["offset_from_base"] = lookupResult.offsetFromBase;
                data["query_addr"]       = addrStr;
            }

            return PipeProtocol::MakeResponse(id, data).dump();
        }

        // === get_ce_pointer_info: CE pointer chain info for a GObjects instance ===
        if (cmd == PipeProtocol::CMD_GET_CE_PTR_INFO) {
            std::string addrStr = request.value("addr", "");
            if (addrStr.empty()) return PipeProtocol::MakeError(id, "Missing addr").dump();

            uintptr_t addr = PipeProtocol::StrToAddr(addrStr);
            int fieldOffset = request.value("field_offset", 0);

            extern uintptr_t g_cachedGObjects;
            uintptr_t moduleBase = Mem::GetModuleBase(nullptr);

            // Compute GObjects RVA
            uintptr_t gobjectsRVA = g_cachedGObjects - moduleBase;

            // Find the InternalIndex of this object by scanning
            int32_t internalIndex = UStructWalker::GetIndex(addr);
            if (internalIndex < 0) {
                return PipeProtocol::MakeError(id, "Cannot read InternalIndex").dump();
            }

            int32_t chunkIndex  = internalIndex / Constants::OBJECTS_PER_CHUNK;
            int32_t withinChunk = internalIndex % Constants::OBJECTS_PER_CHUNK;

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
            data["module_base"]    = PipeProtocol::AddrToStr(moduleBase);
            data["gobjects_rva"]   = PipeProtocol::AddrToStr(gobjectsRVA);
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
            offsets.push_back(withinChunk * ObjectArray::GetItemSize()); // item in chunk
            offsets.push_back(chunkIndex * 8);                           // chunk in table
            offsets.push_back(0);                                         // deref FUObjectArray.Objects

            data["ce_offsets"] = offsets;

            // CE base address string: "Module.exe+RVA"
            char ceBase[128];
            snprintf(ceBase, sizeof(ceBase), "\"%s.exe\"+%llX",
                     moduleName.c_str(), static_cast<unsigned long long>(gobjectsRVA));
            data["ce_base"] = ceBase;

            return PipeProtocol::MakeResponse(id, data).dump();
        }

        // === get_offsets: Return all detected FField/FProperty/UStruct offsets ===
        if (cmd == PipeProtocol::CMD_GET_OFFSETS) {
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
            return PipeProtocol::MakeResponse(id, data).dump();
        }

        if (cmd == PipeProtocol::CMD_WATCH) {
            std::string addrStr = request.value("addr", "");
            int size = request.value("size", 4);
            int interval = request.value("interval_ms", 500);
            if (addrStr.empty()) return PipeProtocol::MakeError(id, "Missing addr").dump();
            if (size <= 0 || size > 65536) return PipeProtocol::MakeError(id, "Invalid size (1-65536)").dump();
            if (interval < 50) interval = 50; // Minimum 50ms to prevent CPU spin

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
        Logger::Error("PIPE:cmd", "PipeServer: Exception in command '%s': %s", cmd.c_str(), e.what());
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
    Logger::Info("PIPE:watch", "PipeServer: Watch started on 0x%llX (size=%u, interval=%ums)",
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
        Logger::Info("PIPE:watch", "PipeServer: Watch stopped on 0x%llX", static_cast<unsigned long long>(addr));
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
