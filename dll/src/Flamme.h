#pragma once

// ============================================================
// Flamme — 弗蘭梅 (古代大魔法使 — Ancient Master)
// HintCache: per-game AOB result caching
//
// Reads/writes a JSON file with previously-winning pattern IDs
// per game (keyed by PE hash). On second scan of the same game
// version, the cached pattern is tried first for a speedup.
//
// File: %LOCALAPPDATA%\UE5CEDumper\UE5CEDumper.{COMPUTERNAME}.json
// Format: same as the C# AobUsageService writes — DLL and UI
//         share the file (last writer wins, both formats compatible).
// ============================================================

#include <string>
#include <cstdint>

// Forward declare to avoid pulling OffsetFinder.h
namespace Genau { struct EnginePointers; }

namespace Flamme {

/// Per-target hint: pattern ID to try first (empty = no hint).
/// Also carries cached UE version to skip the slow DetectVersion scan.
struct ScanHints {
    std::string gobjectsPatternId;
    std::string gnamesPatternId;
    std::string gworldPatternId;

    // Cached UE version from previous scan (0 = no cached version).
    // Only valid when hasVersionHint == true.
    uint32_t    ueVersion        = 0;
    bool        versionDetected  = false;  // true = version was reliably detected last time
    bool        hasVersionHint   = false;  // true = ueVersion/versionDetected are populated

    // User-set persistent override (highest priority — wins over auto-detect on every scan).
    // 0 = no override. Set/cleared via the set_ue_version_override pipe cmd.
    uint32_t    userOverrideVersion = 0;
    bool        hasUserOverride     = false;
};

/// Load hints for a given PE hash from the cache file.
/// Returns empty strings if the file doesn't exist, is corrupt,
/// or the PE hash is not found.  Never throws.
ScanHints LoadHints(const char* peHash);

/// Save scan results to the cache file.  Reads the existing file,
/// updates/inserts the record for peHash, writes atomically.
/// Preserves any pre-existing ueVersionUserOverride field on update.
/// Never throws — errors are logged and silently ignored.
void SaveResults(const char* peHash, const Genau::EnginePointers& ptrs,
                 const char* processName);

/// Persist (or clear) the user-set UE version override for a game.
///   ueVersion == 0 → clear the override (revert to auto-detect on next launch).
///   ueVersion != 0 → save the override; auto-detect is skipped on next launch.
/// processName is used only for the readable gameName field in the JSON record.
/// Never throws — errors are logged.
void SaveUserOverride(const char* peHash, uint32_t ueVersion,
                      const char* processName);

} // namespace Flamme
