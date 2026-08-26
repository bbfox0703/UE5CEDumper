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
#include <utility>   // std::move — ApplyPayload moves payload values out

#include "Stark.h"   // game-thread liveness for the shared response envelope

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
constexpr const char* CMD_WALK_CLASS_BATCH = "walk_class_batch";
constexpr const char* CMD_READ_MEM         = "read_mem";
constexpr const char* CMD_WRITE_MEM        = "write_mem";
constexpr const char* CMD_WALK_INSTANCE    = "walk_instance";
// N instance walks in ONE round-trip. MEASURED justification in
// multipipe-eval.md §10.4: a Copy CE XML issued 20,357 single walk_instance
// calls at 0.08 ms of real work carrying 0.16-0.21 ms of pure round-trip
// overhead. Same trivial-loop-over-the-single-path shape as walk_class_batch.
constexpr const char* CMD_WALK_INSTANCE_BATCH = "walk_instance_batch";
constexpr const char* CMD_WALK_WORLD       = "walk_world";
constexpr const char* CMD_FIND_INSTANCES   = "find_instances";
constexpr const char* CMD_FIND_BY_ADDRESS  = "find_by_address";
constexpr const char* CMD_FIND_REFS_TO_UOBJ = "find_refs_to_uobject";
constexpr const char* CMD_FIND_PATH_FROM_GWORLD = "find_path_from_gworld";
constexpr const char* CMD_GET_RELATED_OBJECTS = "get_related_objects";
constexpr const char* CMD_GET_CURRENT_TARGET = "get_current_target";
constexpr const char* CMD_DETECT_NOISE_CLASSES = "detect_noise_classes";
constexpr const char* CMD_RESOLVE_GAME_ENGINE = "resolve_game_engine";
constexpr const char* CMD_FIND_PROPERTY_XREFS = "find_property_xrefs";
constexpr const char* CMD_FIND_FUNCTIONS_BY_CLASS = "find_functions_by_class";
constexpr const char* CMD_GET_FUNCTION_CODE_ADDR = "get_function_code_addr";
constexpr const char* CMD_WALK_FUNCTION_PROPS = "walk_function_props";
constexpr const char* CMD_GET_CE_PTR_INFO  = "get_ce_pointer_info";
constexpr const char* CMD_GET_OFFSETS      = "get_offsets";
constexpr const char* CMD_READ_ARRAY_ELEMS = "read_array_elements";
constexpr const char* CMD_LIST_ENUMS       = "list_enums";
constexpr const char* CMD_WALK_FUNCTIONS   = "walk_functions";
constexpr const char* CMD_WATCH             = "watch";
constexpr const char* CMD_UNWATCH           = "unwatch";
constexpr const char* CMD_SEARCH_PROPERTIES = "search_properties";
constexpr const char* CMD_SEARCH_PROPERTIES_BATCH = "search_properties_batch";
constexpr const char* CMD_LIST_CLASSES      = "list_classes";
constexpr const char* CMD_LIST_ALL_FUNCTIONS = "list_all_functions";
constexpr const char* CMD_RESCAN            = "rescan";
constexpr const char* CMD_RESCAN_STATUS     = "rescan_status";
constexpr const char* CMD_APPLY_RESCAN      = "apply_rescan";
constexpr const char* CMD_TRIGGER_SCAN      = "trigger_scan";
constexpr const char* CMD_INVOKE_FUNCTION       = "invoke_function";
constexpr const char* CMD_WALK_DATATABLE_ROWS   = "walk_datatable_rows";
constexpr const char* CMD_SCAN_STATUS           = "scan_status";
constexpr const char* CMD_SET_UE_VERSION_OVERRIDE = "set_ue_version_override";
constexpr const char* CMD_SET_INVOKE_TIMEOUT       = "set_invoke_timeout";
constexpr const char* CMD_BEGIN_VALUE_SCAN         = "begin_value_scan";
constexpr const char* CMD_REFINE_VALUE_SCAN        = "refine_value_scan";
constexpr const char* CMD_END_VALUE_SCAN           = "end_value_scan";
constexpr const char* CMD_QUERY_CANDIDATES         = "query_candidates";
// Multiple values group scan (build 1276) — object-aware "group scan".
constexpr const char* CMD_BEGIN_GROUP_SCAN         = "begin_group_scan";
constexpr const char* CMD_REFINE_GROUP_SCAN        = "refine_group_scan";
constexpr const char* CMD_END_GROUP_SCAN           = "end_group_scan";
constexpr const char* CMD_QUERY_GROUP_CANDIDATES   = "query_group_candidates";
// Every leaf one slot of one candidate kept, BY NAME (build 2719). A group row
// can only display one assignment; the rest existed on the wire solely as raw
// integers in `matched_offsets`, which cannot tell a user that offset 1308 is
// `FrozenInt`. Fetched on demand for the expanded row, never inlined into the
// paged list (a page is up to 1000 candidates x N slots x per_slot_cap leaves).
constexpr const char* CMD_QUERY_GROUP_SLOT_LEAVES   = "query_group_slot_leaves";
constexpr const char* CMD_GET_DEBUG_CAMERA_STATE   = "get_debug_camera_state";
constexpr const char* CMD_SET_DEBUG_CAMERA         = "set_debug_camera";
// UE5.7+ packed FUObjectItem calibration (runtime tune of the reconstruction constants +
// optional force-enable) — lets the first real packed game be calibrated without a rebuild.
constexpr const char* CMD_SET_PACKED_CONSTS        = "set_packed_consts";

// Teleport (Wirbel) — marker save/recall + cursor teleport (docs/teleport-spec.md §7)
constexpr const char* CMD_TELEPORT_GET_POSE        = "teleport_get_pose";
constexpr const char* CMD_TELEPORT_SAVE_MARKER     = "teleport_save_marker";
constexpr const char* CMD_TELEPORT_RECALL_MARKER   = "teleport_recall_marker";
constexpr const char* CMD_TELEPORT_TO_CURSOR       = "teleport_to_cursor";
constexpr const char* CMD_TELEPORT_GET_MARKERS     = "teleport_get_markers";
constexpr const char* CMD_TELEPORT_CLEAR_MARKER    = "teleport_clear_marker";
constexpr const char* CMD_TELEPORT_RECALL_LAST     = "teleport_recall_last";
constexpr const char* CMD_TELEPORT_GET_POV         = "teleport_get_pov";
constexpr const char* CMD_TELEPORT_RELATIVE        = "teleport_relative";
constexpr const char* CMD_SET_MOUSE_CURSOR         = "set_mouse_cursor";
constexpr const char* CMD_GET_MOUSE_CURSOR         = "get_mouse_cursor";

// GodMode (Solitar) — force AActor::bCanBeDamaged (docs/godmode-spec.md §6.1)
constexpr const char* CMD_SET_GOD_MODE             = "set_god_mode";
constexpr const char* CMD_GET_GOD_MODE             = "get_god_mode";
constexpr const char* CMD_GET_PROTECT_STATE        = "get_protect_state";

// Foreground lock (Grausam) — hook GetForegroundWindow so the game always thinks
// it is the foreground app, defeating t.IdleWhenNotForeground / focus-loss pause.
constexpr const char* CMD_SET_FOREGROUND_LOCK      = "set_foreground_lock";
constexpr const char* CMD_GET_FOREGROUND_LOCK      = "get_foreground_lock";

// Movement tuning (Laufen) — per-pawn CMC float knobs held against per-tick
// overwrites: walk_speed (P1) / gravity / jump (P2/P3). "knob" string selects
// which: "walk_speed" | "gravity" | "jump".
constexpr const char* CMD_GET_MOVEMENT_PARAMS      = "get_movement_params";
constexpr const char* CMD_SET_MOVEMENT_MULTIPLIER  = "set_movement_multiplier";
constexpr const char* CMD_RESET_MOVEMENT           = "reset_movement";
// Gravity DIRECTION vector (UE5.4+ GravityDirection); x/y/z normalized DLL-side.
constexpr const char* CMD_SET_GRAVITY_DIRECTION    = "set_gravity_direction";
constexpr const char* CMD_RESET_GRAVITY_DIRECTION  = "reset_gravity_direction";

// Time dilation (Hemmung) — hold a reflected dilation float at an absolute value
// (global slow-mo/freeze/speed-up) held by a re-assert worker. "target" string
// selects which: "global" (AWorldSettings::TimeDilation) | "pawn"
// (AActor::CustomTimeDilation). get_time_state polls both.
constexpr const char* CMD_SET_TIME_DILATION        = "set_time_dilation";
constexpr const char* CMD_RESET_TIME_DILATION      = "reset_time_dilation";
constexpr const char* CMD_GET_TIME_STATE           = "get_time_state";

// Force-field hold (Solide) — hold a discovered reflected field at a value across
// all live instances of a class via a re-assert worker: bool ON/OFF, ObjectProperty
// → null, or numeric → absolute. "kind" string selects: "bool" | "object_null" |
// "numeric". Plus find_stealth_meter (auto-find the player's stealth/noise float).
constexpr const char* CMD_FORCE_FIELD              = "force_field";
constexpr const char* CMD_RESET_FIELD              = "reset_field";
constexpr const char* CMD_RESET_ALL_FIELDS         = "reset_all_fields";
constexpr const char* CMD_GET_FORCED_FIELDS        = "get_forced_fields";
constexpr const char* CMD_FIND_STEALTH_METER       = "find_stealth_meter";

// Fly (Dunste) — no-gravity keyboard-driven 3D flight. fly_set applies whichever
// of {enable, speed, preset} are present and returns the live status; fly_get_state
// polls it. Input is read DLL-side (GetAsyncKeyState); the UI only toggles/config.
constexpr const char* CMD_FLY_SET                  = "fly_set";
constexpr const char* CMD_FLY_GET_STATE            = "fly_get_state";

// See-through occluders (Schlacht) — hide the nearest non-Pawn actor blocking the
// camera→pawn line so the view isn't obstructed. seethrough_set applies {enable}
// and returns the live status; seethrough_get_state polls it. Trace/hide run
// DLL-side on the worker; the UI only toggles.
constexpr const char* CMD_SEE_THROUGH_SET          = "seethrough_set";
constexpr const char* CMD_SEE_THROUGH_GET_STATE    = "seethrough_get_state";

// Standalone CE-Lua trainer bake (no-DLL trainer export). One-shot: returns the
// decomposed *GWorld->Pawn offset chain + RootComponent/RelativeLocation,
// CharacterMovement + knob offsets, and the protection bits — everything a
// GWorld-anchored standalone .CT needs to re-walk and write without the DLL.
constexpr const char* CMD_GET_TRAINER_OFFSETS      = "get_trainer_offsets";

// Snapshot capture (experimental — Phase A). Stateless cursor pagination
// like get_object_list: begin_snapshot returns the total object count for
// progress; snapshot_chunk streams [offset, offset+limit) objects with their
// numeric UPROPERTY values. No end command — there is no server-side session.
constexpr const char* CMD_BEGIN_SNAPSHOT           = "begin_snapshot";
constexpr const char* CMD_SNAPSHOT_CHUNK           = "snapshot_chunk";

// Live ProcessEvent profiler (Linie via the Stark hook) — record per-UFunction*
// fire counts during a Start/Stop window, resolve names at query time. Behaviour-
// based UFunction discovery (Start → do an in-game action → Stop → see what fired).
// Pipe-only (no Mimic/CE-Lua mailbox).
constexpr const char* CMD_PE_PROFILE_START         = "pe_profile_start";
constexpr const char* CMD_PE_PROFILE_STOP          = "pe_profile_stop";
constexpr const char* CMD_PE_PROFILE_GET           = "pe_profile_get";

// Diagnostics (Sense) — self-health telemetry: how long each pipe command
// actually occupies the dispatcher (the head-of-line blocking multipipe-eval.md
// blames for UI lag but nothing measured), plus Win32 process facts. Pipe-only.
constexpr const char* CMD_GET_DIAGNOSTICS          = "get_diagnostics";
constexpr const char* CMD_RESET_DIAGNOSTICS        = "reset_diagnostics";

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
//
// ⚠ This is NOT a looser PARSER — it calls TryStrToAddr and throws the failure channel
// away. The only difference is whether the caller can tell "the address was 0" from "the
// address was garbage", so the choice is about the CONSEQUENCE of that confusion, not
// about permissiveness. The rule, applied per call site in audit #5 AD19:
//
//   * Use TryStrToAddr when a silent 0 would WRITE, EXECUTE, or change persistent state.
//     Those handlers must refuse and say why: `write_mem` reported the generic "Write
//     failed" for a malformed address, and `set_packed_consts` treats 0 as "leave
//     unchanged", so a typo'd mask was discarded while the command answered ok.
//   * StrToAddr is fine on the READ/QUERY handlers (~19 of them). There a 0 fails closed
//     — the lookup misses and the handler already returns its own specific error — so the
//     extra branch buys nothing.
//
// The malformed input that actually shows up is an unsubstituted CE placeholder such as
// "0x[ply_base]", which is why TryStrToAddr rejects trailing garbage rather than parsing
// the leading hex and stopping.
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

// Hex string to bytes, with a failure channel.
//
// The old version could not fail: `strtoul` maps any non-hex character to 0, and an
// odd trailing nibble was silently dropped. `"DE AD BE EF"` — spaces and all, the way
// a human writes a byte pattern — became `{DE,0A,0D,BE,0E}`, was WRITTEN INTO THE GAME,
// and answered `ok:true`. Nothing in the pipe layer could tell the difference between
// "wrote what you asked" and "wrote five bytes of nonsense at that address". (B46)
//
// Returns false on any non-hex character or an odd length; `out` is then untouched.
inline bool TryHexToBytes(const std::string& hex, std::vector<uint8_t>& out) {
    if (hex.empty() || (hex.size() % 2) != 0) return false;
    auto nibble = [](char c, uint8_t& v) -> bool {
        if (c >= '0' && c <= '9') { v = static_cast<uint8_t>(c - '0');        return true; }
        if (c >= 'a' && c <= 'f') { v = static_cast<uint8_t>(c - 'a' + 10);   return true; }
        if (c >= 'A' && c <= 'F') { v = static_cast<uint8_t>(c - 'A' + 10);   return true; }
        return false;
    };
    std::vector<uint8_t> bytes;
    bytes.reserve(hex.size() / 2);
    for (size_t i = 0; i < hex.size(); i += 2) {
        uint8_t hi = 0, lo = 0;
        if (!nibble(hex[i], hi) || !nibble(hex[i + 1], lo)) return false;
        bytes.push_back(static_cast<uint8_t>((hi << 4) | lo));
    }
    out = std::move(bytes);
    return true;
}

// Lenient wrapper kept for callers that have already validated their input.
// Prefer TryHexToBytes anywhere the string came from outside this process.
inline std::vector<uint8_t> HexToBytes(const std::string& hex) {
    std::vector<uint8_t> bytes;
    TryHexToBytes(hex, bytes);
    return bytes;
}

// Splice a handler payload onto an envelope with plain per-key ASSIGNMENT.
//
// NOT nlohmann's merge_patch (RFC 7386), which both builders used to call. Two of
// merge_patch's documented behaviours are wrong for an envelope, and neither is
// what any call site meant (audit #5 F5):
//
//   * a NULL value in the patch DELETES the key rather than setting it. A handler
//     that answers {"value": null} therefore ships a response with no "value" key
//     at all, and {"ok": null} would delete the envelope's own success flag. No
//     handler emits a top-level null today, so this is a latent hazard rather than
//     a live bug -- but it is one an ordinary `data["x"] = nullptr;` re-introduces
//     silently, which is exactly the shape worth removing.
//   * a NON-OBJECT patch REPLACES the whole target, so `id` / `ok` /
//     `game_thread_stalled` would vanish outright. All three payload-returning
//     call sites build objects, so the `is_object()` guard below can only ever
//     skip a payload that would have destroyed the envelope.
//
// It also MOVES each value out of an rvalue payload instead of deep-copying the
// DOM, which is the half F5 was filed for: `snapshot_chunk` (8192 objects),
// `find_instances` (50000 cap) and `list_all_functions` (100000 default) all
// paid a full second copy inside the GAME process's heap.
//
// Key ORDER is not affected: nlohmann's default object is a std::map, so the
// serialized text is key-sorted either way and the wire bytes are unchanged.
inline void ApplyPayload(nlohmann::json& res, nlohmann::json&& data) {
    if (!data.is_object()) return;
    for (auto it = data.begin(); it != data.end(); ++it)
        res[it.key()] = std::move(it.value());
}

// Build a success response.
//
// `data` is taken BY VALUE deliberately: an lvalue argument costs exactly the one
// copy merge_patch used to make, while a temporary (or an explicit std::move at a
// heavy call site) is moved straight through ApplyPayload with no copy at all.
inline nlohmann::json MakeResponse(int id, nlohmann::json data = nlohmann::json()) {
    nlohmann::json res;
    res["id"] = id;
    res["ok"] = true;
    // Cross-cutting game-thread liveness hint riding the shared success
    // envelope: a paused / suspended game stops ticking ProcessEvent, so every
    // live-camera / function-invoke feature times out. Carrying it on EVERY
    // response lets the UI raise a non-blocking "game paused" banner from any
    // command it happens to send (no dedicated heartbeat command or timer), and
    // clear it on the next response once the thread ticks again. Cost: one
    // atomic read + a steady-clock diff. Placed before the payload splice so a
    // handler that (unusually) sets its own "game_thread_stalled" still wins.
    //
    // Emitted ONLY when it is a MEASUREMENT. When Stark cannot tell — no PE hook
    // installed yet, which is the NORMAL state of a fresh connection because the
    // hook installs lazily on the first invoke — the key is OMITTED rather than
    // defaulted to false, which asserted a healthy game thread nobody had measured.
    // Absence is not a new wire state: MakeError and MakeEvent below have never
    // carried it, and the client has always tolerated its absence.
    // (STALLDEFAULT-2026-08-26)
    const Stark::GameThreadLiveness live = Stark::GetGameThreadLiveness();
    if (live != Stark::GameThreadLiveness::Unknown)
        res["game_thread_stalled"] = (live == Stark::GameThreadLiveness::Stalled);
    ApplyPayload(res, std::move(data));
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

// Build a push event (no id). Same envelope rule as MakeResponse: an "event" key
// must survive whatever the payload contains.
inline nlohmann::json MakeEvent(const std::string& eventType,
                                nlohmann::json data = nlohmann::json()) {
    nlohmann::json evt;
    evt["event"] = eventType;
    ApplyPayload(evt, std::move(data));
    return evt;
}

} // namespace Renge
