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

#include "Grimoire.h"   // MIN_SUPPORTED_UE_VERSION -- the publisher rule stops at the support floor

// Forward declare to avoid pulling OffsetFinder.h
namespace Genau { struct EnginePointers; }

namespace Flamme {

/// May a staged temp file be published over the real one?
///
/// The write-then-rename here has exactly one job: never leave the cache in a
/// worse state than it was. `Flamme.cpp` used to run `ofs << root.dump(2)`, let the
/// destructor close it, and rename — testing NOTHING. A short write (full volume,
/// quota) is swallowed by the destructor, so the truncated document got published
/// over the only good copy; `LoadHints` then parses it with allow_exceptions=false,
/// gets `discarded`, and every game's pattern IDs, ueVersion, user override and
/// invoke timeout are gone at once. (audit #5 FL1)
///
/// Two INDEPENDENT detectors, because either can miss alone:
///   * `streamOk`  — the stream reported no failure through close(). Catches the
///                   errors the CRT sees.
///   * `bytesOnDisk == bytesExpected` — what actually reached the volume. Catches
///                   a truncation the stream did not report. Requires the temp to
///                   be written in BINARY mode, or text-mode \n → \r\n expansion
///                   makes the two numbers differ for a perfectly good write.
/// `sizeKnown` is false when the size could not be read at all, which is a refusal
/// and not a pass — an unmeasurable file is not a verified one.
///
/// Refusing costs the caller one skipped cache update (the next launch re-scans);
/// publishing wrongly costs every game's cached state. The asymmetry is why this
/// fails closed.
constexpr bool ShouldPublishAtomicWrite(bool streamOk, bool sizeKnown,
                                        unsigned long long bytesOnDisk,
                                        unsigned long long bytesExpected) {
    return streamOk && sizeKnown && bytesOnDisk == bytesExpected;
}

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
    bool        lowConfidence    = false;  // cached bLowConfidence (Tier 3 / publisher bias) — preserves the override nudge
    bool        hasVersionHint   = false;  // true = ueVersion/versionDetected/lowConfidence are populated

    // Detection-logic revision that produced the cached ueVersion (see Genau::kVersionDetectLogicRev).
    // The reuse path trusts the cache only when this equals the current logic rev; a mismatch
    // (older DLL / changed logic / absent field = 0) forces a one-time re-detection + re-stamp.
    uint32_t    versionDetectRev = 0;

    // User-set persistent override (highest priority — wins over auto-detect on every scan).
    // 0 = no override. Set/cleared via the set_ue_version_override pipe cmd.
    uint32_t    userOverrideVersion = 0;
    bool        hasUserOverride     = false;

    // Per-game GameThreadDispatch invoke timeout in milliseconds.
    // 0 = use Stark's compile-time default (5000ms). Set/cleared via the
    // set_invoke_timeout pipe cmd. Useful when a game's UFunction completion
    // is gated by next-frame logic (Blueprint widgets, multicast delegates)
    // and 5s isn't enough.
    int32_t     invokeTimeoutMs        = 0;
    bool        hasInvokeTimeoutOverride = false;
};

// ============================================================
// The cached detection, as a launch reuses it [UE-OVERRIDE-HINT-AUTO]
// ============================================================
// A launch reuses the cached version instead of detecting again, and Auto chosen over an override hands one back to
// the running session. One copy of the rule, so Auto restores exactly what the next launch would start from.

/// Reused only when the current detection logic stamped it: a record from an older rev is detected again once.
inline bool CachedDetectionTrusted(const ScanHints& h, uint32_t currentLogicRev) {
    return h.hasVersionHint && h.ueVersion != 0 && h.versionDetectRev == currentLogicRev;
}

/// The cached flag, or a publisher known to ship unreliable version strings -- judged live, so a shipper added to
/// the table after this game was cached still flags it. Not below the support floor: a version there is a refusal,
/// not a guess, and the too-old refusal needs !lowConfidence to fire.
inline bool CachedLowConfidence(const ScanHints& h, bool publisherMatched) {
    return h.lowConfidence || (publisherMatched && h.ueVersion >= Grimoire::MIN_SUPPORTED_UE_VERSION);
}

// ============================================================
// Auto, chosen over an override [UE-OVERRIDE-HINT-AUTO]
// ============================================================
// What set_ue_version_override(0) hands back to the running session. Only an override in force has anything to undo,
// and the detection it returns to is the one the next launch would start from (CachedDetectionTrusted) -- unless that
// launch would refuse it as too old, because a session scanned under the override has no business being read by a
// version the dumper cannot read. Anything else waits for the next launch, which detects afresh.
enum class AutoRestore {
    Restored,         // the detection on record is in force again
    NotOverridden,    // no override was in force: nothing to undo
    NoDetection,      // the record holds no detection: every launch so far ran under the override
    StaleDetection,   // the record's detection predates the current detection logic, which the next launch re-runs
    TooOld,           // the record's detection is one the next launch refuses
};

/// The reply's wire name for each outcome; the UI's DumpService reads them.
constexpr const char* AutoRestoreName(AutoRestore a) {
    switch (a) {
        case AutoRestore::Restored:       return "restored";
        case AutoRestore::NotOverridden:  return "not_overridden";
        case AutoRestore::NoDetection:    return "no_detection";
        case AutoRestore::StaleDetection: return "stale_detection";
        case AutoRestore::TooOld:         return "too_old";
    }
    return "unknown";
}

struct AutoRestorePlan {
    AutoRestore outcome       = AutoRestore::NotOverridden;
    uint32_t    version       = 0;       // the detection to hand back, before init's structural ladder climbs it
    bool        detected      = false;
    bool        lowConfidence = false;
};

/// `overrideInForce` is the session's own flag (an override applied at launch or set since); `publisherMatched`
/// whether a publisher thumbprint matched this exe, which a launch re-applies to a reused version.
inline AutoRestorePlan PlanAutoRestore(bool overrideInForce, const ScanHints& h, uint32_t currentLogicRev,
                                       bool publisherMatched) {
    (void)overrideInForce; (void)h; (void)currentLogicRev; (void)publisherMatched;
    return {};
}

/// Whether the override's version holds until the next launch: Auto found nothing to hand back, and the clear was
/// persisted. Unpersisted, the override is still on disk and the next launch applies it again, so nothing is pending.
constexpr bool AutoPendsUntilNextLaunch(AutoRestore outcome, bool persisted) {
    (void)outcome; (void)persisted;
    return false;
}

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

/// Correct the cached gObjects resolution method AFTER post-init decoy recovery
/// (UE5_Init). SaveResults runs inside FindAll — before recovery — so it records
/// the decoy's "aob" method + winning patternId. Once recovery re-resolves GObjects
/// via the data scan, call this to rewrite method (e.g. "data_scan_recovery") and
/// CLEAR the patternId, so the next launch's LoadHints does not prioritise an AOB
/// pattern that only matched a decoy (ExtractHint returns a hint only when method
/// == "aob"). No-op if the cache file or record doesn't exist. Never throws.
void UpdateGObjectsMethod(const char* peHash, const char* method);

/// Persist (or clear) the user-set UE version override for a game.
///   ueVersion == 0 → clear the override (revert to auto-detect on next launch).
///   ueVersion != 0 → save the override; auto-detect is skipped on next launch.
/// processName is used only for the readable gameName field in the JSON record.
/// Never throws — errors are logged.
void SaveUserOverride(const char* peHash, uint32_t ueVersion,
                      const char* processName);

/// Persist (or clear) the user-set GameThreadDispatch invoke timeout for a game.
///   timeoutMs == 0 → clear the override (revert to Stark's default 5000ms).
///   timeoutMs != 0 → save the override; applied at next scan + immediately to Stark.
/// processName is used only for the readable gameName field in the JSON record.
/// Never throws — errors are logged.
void SaveInvokeTimeout(const char* peHash, int32_t timeoutMs,
                       const char* processName);

/// Is the UI's "experimental features" opt-in switched on?
/// Reads %LOCALAPPDATA%\UE5CEDumper\experimental.json — the SAME file the UI's
/// ExperimentalGate writes — so the DLL honours the toggle on every entry path
/// (UI pipe scan, CE Lua UE5_Init, proxy auto-start) with no protocol change.
/// Result is cached after the first call. Missing/malformed file ⇒ false, i.e.
/// experimental behaviour stays OFF unless the user explicitly opted in.
bool IsExperimentalEnabled();

} // namespace Flamme
