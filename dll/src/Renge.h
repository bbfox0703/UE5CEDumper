#pragma once

// ============================================================
// Renge — 蓮格 (連絡人 — Liaison)
// PipeProtocol: IPC command/event definitions
// ============================================================

#include <json.hpp>
#include <string>
#include <cstdint>
#include <sstream>
#include <iomanip>

namespace Renge {

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
constexpr const char* CMD_WALK_INSTANCE    = "walk_instance";
constexpr const char* CMD_WALK_WORLD       = "walk_world";
constexpr const char* CMD_FIND_INSTANCES   = "find_instances";
constexpr const char* CMD_FIND_BY_ADDRESS  = "find_by_address";
constexpr const char* CMD_FIND_REFS_TO_UOBJ = "find_refs_to_uobject";
constexpr const char* CMD_GET_CE_PTR_INFO  = "get_ce_pointer_info";
constexpr const char* CMD_GET_OFFSETS      = "get_offsets";
constexpr const char* CMD_READ_ARRAY_ELEMS = "read_array_elements";
constexpr const char* CMD_LIST_ENUMS       = "list_enums";
constexpr const char* CMD_WALK_FUNCTIONS   = "walk_functions";
constexpr const char* CMD_WATCH             = "watch";
constexpr const char* CMD_UNWATCH           = "unwatch";
constexpr const char* CMD_SEARCH_PROPERTIES = "search_properties";
constexpr const char* CMD_LIST_CLASSES      = "list_classes";
constexpr const char* CMD_RESCAN            = "rescan";
constexpr const char* CMD_RESCAN_STATUS     = "rescan_status";
constexpr const char* CMD_APPLY_RESCAN      = "apply_rescan";
constexpr const char* CMD_TRIGGER_SCAN      = "trigger_scan";
constexpr const char* CMD_INVOKE_FUNCTION       = "invoke_function";
constexpr const char* CMD_WALK_DATATABLE_ROWS   = "walk_datatable_rows";
constexpr const char* CMD_SCAN_STATUS           = "scan_status";
constexpr const char* CMD_SET_UE_VERSION_OVERRIDE = "set_ue_version_override";
constexpr const char* CMD_SET_INVOKE_TIMEOUT       = "set_invoke_timeout";

// Event types
constexpr const char* EVT_WATCH            = "watch";

// Address to hex string "0x..."
inline std::string AddrToStr(uintptr_t addr) {
    std::ostringstream oss;
    oss << "0x" << std::uppercase << std::hex << addr;
    return oss.str();
}

// Strict hex parse: optional "0x"/"0X" prefix, allows trailing whitespace, rejects
// any other trailing garbage (e.g. unsubstituted CE placeholders like "0x[ply_base]").
// Returns true and writes outAddr on success; returns false on failure (outAddr untouched).
inline bool TryStrToAddr(const std::string& str, uintptr_t& outAddr) noexcept {
    if (str.empty()) return false;
    // Reject leading sign — std::stoull would silently accept "-1" and 2's-complement
    // wrap to 0xFFFFFFFFFFFFFFFF, which is never a meaningful address from the UI side.
    size_t front = 0;
    while (front < str.size() && std::isspace(static_cast<unsigned char>(str[front]))) ++front;
    if (front < str.size() && (str[front] == '-' || str[front] == '+')) return false;
    try {
        size_t pos = 0;
        uintptr_t v = std::stoull(str, &pos, 16);
        while (pos < str.size() && std::isspace(static_cast<unsigned char>(str[pos]))) ++pos;
        if (pos != str.size()) return false;
        outAddr = v;
        return true;
    } catch (...) {
        return false;
    }
}

// Hex string "0x..." to address. Noexcept: returns 0 on malformed input.
// Prefer TryStrToAddr at boundaries where you want to surface a clear error to the caller.
inline uintptr_t StrToAddr(const std::string& str) noexcept {
    uintptr_t v = 0;
    TryStrToAddr(str, v);
    return v;
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

} // namespace Renge
