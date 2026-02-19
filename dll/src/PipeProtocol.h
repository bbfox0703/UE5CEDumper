#pragma once

// ============================================================
// PipeProtocol.h — JSON IPC protocol definitions
// ============================================================

#include <json.hpp>
#include <string>
#include <cstdint>
#include <sstream>
#include <iomanip>

namespace PipeProtocol {

// Command strings
constexpr const char* CMD_INIT             = "init";
constexpr const char* CMD_GET_POINTERS     = "get_pointers";
constexpr const char* CMD_GET_OBJECT_COUNT = "get_object_count";
constexpr const char* CMD_GET_OBJECT_LIST  = "get_object_list";
constexpr const char* CMD_GET_OBJECT       = "get_object";
constexpr const char* CMD_FIND_OBJECT      = "find_object";
constexpr const char* CMD_SEARCH_OBJECTS   = "search_objects";
constexpr const char* CMD_WALK_CLASS       = "walk_class";
constexpr const char* CMD_READ_MEM         = "read_mem";
constexpr const char* CMD_WRITE_MEM        = "write_mem";
constexpr const char* CMD_WATCH            = "watch";
constexpr const char* CMD_UNWATCH          = "unwatch";

// Event types
constexpr const char* EVT_WATCH            = "watch";

// Address to hex string "0x..."
inline std::string AddrToStr(uintptr_t addr) {
    std::ostringstream oss;
    oss << "0x" << std::uppercase << std::hex << addr;
    return oss.str();
}

// Hex string "0x..." to address
inline uintptr_t StrToAddr(const std::string& str) {
    return std::stoull(str, nullptr, 16);
}

// Bytes to hex string (no 0x prefix)
inline std::string BytesToHex(const uint8_t* data, size_t len) {
    std::ostringstream oss;
    for (size_t i = 0; i < len; ++i) {
        oss << std::hex << std::setfill('0') << std::setw(2) << std::uppercase
            << static_cast<int>(data[i]);
    }
    return oss.str();
}

// Hex string to bytes
inline std::vector<uint8_t> HexToBytes(const std::string& hex) {
    std::vector<uint8_t> bytes;
    for (size_t i = 0; i + 1 < hex.size(); i += 2) {
        uint8_t byte = static_cast<uint8_t>(strtoul(hex.substr(i, 2).c_str(), nullptr, 16));
        bytes.push_back(byte);
    }
    return bytes;
}

// Build a success response
inline nlohmann::json MakeResponse(int id, const nlohmann::json& data = {}) {
    nlohmann::json res;
    res["id"] = id;
    res["ok"] = true;
    if (!data.is_null() && !data.empty()) {
        res.merge_patch(data);
    }
    return res;
}

// Build an error response
inline nlohmann::json MakeError(int id, const std::string& errorMsg) {
    return {
        {"id", id},
        {"ok", false},
        {"error", errorMsg}
    };
}

// Build a push event (no id)
inline nlohmann::json MakeEvent(const std::string& eventType, const nlohmann::json& data = {}) {
    nlohmann::json evt;
    evt["event"] = eventType;
    if (!data.is_null() && !data.empty()) {
        evt.merge_patch(data);
    }
    return evt;
}

} // namespace PipeProtocol
