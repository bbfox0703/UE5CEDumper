// ============================================================
// Flamme — 弗蘭梅 (古代大魔法使 — Ancient Master)
// HintCache: per-game AOB result caching
// ============================================================

#include "Flamme.h"
#include "Genau.h"
#include "Grimoire.h"
#define LOG_CAT "SCAN"
#include "Sein.h"

#include <Windows.h>
#include <ShlObj.h>
#include <json.hpp>
#include <filesystem>
#include <fstream>
#include <chrono>
#include <iomanip>
#include <sstream>
#include <atomic>
#include <functional>

namespace fs = std::filesystem;
using json = nlohmann::json;

namespace Flamme {

// ============================================================
// Helpers
// ============================================================

bool IsExperimentalEnabled() {
    // Cached: the scan queries this per GNames candidate, and the answer cannot
    // meaningfully change mid-scan. -1 = not yet read.
    static std::atomic<int> s_cached{-1};
    const int known = s_cached.load(std::memory_order_relaxed);
    if (known >= 0) return known != 0;

    int enabled = 0;
    wchar_t* appdata = nullptr;
    if (SUCCEEDED(SHGetKnownFolderPath(FOLDERID_LocalAppData, 0, nullptr, &appdata))) {
        fs::path p = fs::path(appdata) / Grimoire::LOG_FOLDER_NAME / L"experimental.json";
        CoTaskMemFree(appdata);
        try {
            std::ifstream f(p);
            if (f.is_open()) {
                json j;
                f >> j;
                if (j.contains("enabled") && j["enabled"].is_boolean() && j["enabled"].get<bool>())
                    enabled = 1;
            }
        } catch (...) {
            // Malformed or unreadable — stay OFF. Never throws to the caller.
        }
    }
    s_cached.store(enabled, std::memory_order_relaxed);
    LOG_INFO("Experimental features: %s", enabled ? "ENABLED" : "disabled");
    return enabled != 0;
}

/// Build the cache file path: %LOCALAPPDATA%\UE5CEDumper\UE5CEDumper.{COMPUTERNAME}.json
static fs::path GetCacheFilePath() {
    wchar_t* appdata = nullptr;
    if (!SUCCEEDED(SHGetKnownFolderPath(FOLDERID_LocalAppData, 0, nullptr, &appdata)))
        return {};

    fs::path dir = fs::path(appdata) / Grimoire::LOG_FOLDER_NAME;
    CoTaskMemFree(appdata);

    // Get machine name
    wchar_t compName[MAX_COMPUTERNAME_LENGTH + 1] = {};
    DWORD compLen = MAX_COMPUTERNAME_LENGTH + 1;
    if (!GetComputerNameW(compName, &compLen))
        wcscpy_s(compName, L"UNKNOWN");

    // UE5CEDumper.{COMPUTERNAME}.json
    std::wstring filename = std::wstring(Grimoire::HINT_CACHE_PREFIX) + L"." +
                            compName + L".json";
    return dir / filename;
}

/// Get current UTC timestamp in ISO 8601 format.
// Temp path for the write-then-rename. PER-PROCESS, and that is the whole point:
// the four writers here and AobUsageService.SaveFileAsync on the C# side all target the
// SAME cache file, from DIFFERENT processes, and used to build the byte-identical
// "<file>.tmp". Each opens it with truncate. So the game's DLL could truncate the temp
// file while the UI was mid-write, and whichever renamed last published a half-written
// document over the real cache — losing, among other things, the user's
// ueVersionUserOverride. The final rename is still last-writer-wins (that is the
// existing, accepted semantics); what must not be shared is the staging file. (B39)
static fs::path MakeTempPath(const fs::path& path) {
    auto tmp = path;
    tmp += L".tmp." + std::to_wstring(GetCurrentProcessId());
    return tmp;
}

// ============================================================
// Atomic publish — ONE implementation, four call sites
// ============================================================
//
// The block this replaces was repeated verbatim four times:
//     { std::ofstream ofs(temp, trunc); if (!ofs) return; ofs << root.dump(2); }
//     fs::rename(temp, path);
// and carried two defects that only surface when the disk or the UI does.
//
// FL1 — the stream's error state was NEVER tested. No exceptions() mask, no good(),
// no explicit close(). A short write is swallowed by the destructor and the rename
// publishes the TRUNCATED document over the only good copy. The C# twin
// structurally cannot do this: `await File.WriteAllTextAsync` throws BEFORE
// `File.Move` (AobUsageService.cs `SaveFileAsync`).
//
// FL2 — `fs::rename(a, b)` is the THROWING overload, and each caller's catch-all
// logged it and returned, leaking `<file>.tmp.<pid>`. The suffix is the PID, so
// every affected launch left a DISTINCTLY NAMED full copy of the cache in
// %LOCALAPPDATA%\UE5CEDumper\ forever — against the app-data rule that the root
// holds only files "app-wide and fixed in number". It needs no disk fault either:
// the UI holds this file with FileShare.Read and NOT FileShare.Delete, so a
// replace-rename during a UI read simply fails.
//
// The decision itself lives in Flamme.h (ShouldPublishAtomicWrite) so the light
// `dll_helpers_test` can pin it; this file reaches only `dll_core_test`.

// Remove staging files abandoned by earlier failures — ours AND the UI's; both
// build "<cache>.tmp.<pid>" (AobUsageService.cs `SaveFileAsync`). Age-guarded rather than
// PID-guarded: writing this file is a few KB and lasts milliseconds, so anything an
// hour old is provably abandoned, whereas a liveness test would race a process
// that is mid-write right now.
//
// Scoped to this cache file's own "<name>.tmp." prefix, never a folder wildcard.
// The root holds the app-wide fixed files; the per-game families that must move and
// expire as a GROUP (Snapshots\ with its -wal/-shm, Bookmarks\) live in their own
// subfolders, which this never enters — so nothing here can orphan a sibling.
// Runs at most once per process. (audit #5 FL2)
static void SweepOrphanTemps(const fs::path& path) {
    // Atomic, not a plain bool: SaveResults runs on the init/scan path while
    // SaveUserOverride / SaveInvokeTimeout run on pipe threads. They serialise on the
    // cache lock now, except when it could not be opened (CacheLock writes without it),
    // so two writers can still reach here at once. Worst case would only be a duplicate
    // (idempotent) sweep, but a torn plain-bool write is still a data race.
    static std::atomic<bool> s_swept{false};
    if (s_swept.exchange(true)) return;

    const std::wstring prefix = path.filename().wstring() + L".tmp.";
    const auto cutoff = fs::file_time_type::clock::now() - std::chrono::hours(1);

    std::error_code ec;
    fs::directory_iterator it(path.parent_path(), ec);
    if (ec) return;

    // Explicit increment(ec): a range-for advances with the THROWING operator++,
    // and the ec handed to the constructor only ever reports construction. Same
    // trap as Sein's retention sweeps (audit #5 SE2).
    const fs::directory_iterator last{};
    int removed = 0;
    while (it != last) {
        const fs::path p = it->path();
        const std::wstring name = p.filename().wstring();
        if (name.size() > prefix.size() &&
            name.compare(0, prefix.size(), prefix) == 0) {
            std::error_code fe;
            const auto mtime = fs::last_write_time(p, fe);
            if (!fe && mtime < cutoff) {
                std::error_code re;
                if (fs::remove(p, re)) ++removed;
            }
        }
        it.increment(ec);
        if (ec) break;
    }

    if (removed > 0)
        LOG_INFO("HintCache: removed %d abandoned staging file(s) older than 1h", removed);
}

/// Write `text` to a temp file, VERIFY it, then rename it over `path`.
/// Returns false without touching `path` when anything went wrong, and never
/// leaves the staging file behind. `who` names the caller for the log line.
static bool WriteJsonAtomic(const fs::path& path, const std::string& text,
                            const char* who) {
    const fs::path tempPath = MakeTempPath(path);

    bool streamOk = false;
    {
        // BINARY: what lands on disk is then byte-for-byte `text`, which is what
        // makes the size check below an exact test rather than an approximation.
        std::ofstream ofs(tempPath, std::ios::binary | std::ios::trunc);
        if (!ofs.is_open()) {
            LOG_WARN("HintCache: %s: failed to open temp file for writing", who);
            return false;
        }
        ofs << text;
        ofs.close();              // flushes; a short write shows up here
        streamOk = !ofs.fail();
    }

    std::error_code ec;
    const auto onDisk = fs::file_size(tempPath, ec);
    const bool sizeKnown = !ec;

    if (!ShouldPublishAtomicWrite(streamOk, sizeKnown,
                                  sizeKnown ? static_cast<unsigned long long>(onDisk) : 0ull,
                                  static_cast<unsigned long long>(text.size()))) {
        LOG_WARN("HintCache: %s: staged write is incomplete (stream=%s, %s of %llu bytes) "
                 "— NOT publishing; the previous cache is kept intact",
                 who, streamOk ? "ok" : "FAILED",
                 sizeKnown ? std::to_string(onDisk).c_str() : "unknown",
                 static_cast<unsigned long long>(text.size()));
        std::error_code rmEc;
        fs::remove(tempPath, rmEc);
        return false;
    }

    fs::rename(tempPath, path, ec);      // non-throwing overload
    if (ec) {
        LOG_WARN("HintCache: %s: publish failed (%s) — previous cache intact, "
                 "removing the staging file", who, ec.message().c_str());
        std::error_code rmEc;
        fs::remove(tempPath, rmEc);
        return false;
    }

    SweepOrphanTemps(path);
    return true;
}

// ============================================================
// The cache's lock ([PIPE-PER-PROCESS-2026-11-06] Stage C)
// ============================================================
// Every writer reads the file, changes one game's record and publishes the whole file. Two at once -- two pipe
// clients setting an override and an invoke timeout, or one of them beside a scan -- each published its own read,
// so the later rename dropped the earlier change while its reply said persisted; both also staged through the one
// .tmp.<pid>, so one could delete the other's staged file. A NAMED mutex rather than a std::mutex: a second copy of
// the DLL in this process shares the staging name, and a second injected game shares the file. A crashed holder
// (WAIT_ABANDONED) left a whole file -- the publish is a rename -- so its lock is taken as held.
class CacheLock {
public:
    explicit CacheLock(const fs::path& path, unsigned long waitMs = Grimoire::HINTCACHE_LOCK_WAIT_MS) {
        const std::wstring name = std::wstring(Grimoire::HINTCACHE_LOCK_PREFIX) + path.filename().wstring();
        m_mutex = CreateMutexW(nullptr, FALSE, name.c_str());
        if (!m_mutex) {
            // No lock to take (the name is held under another security context, say): write as before the lock existed
            // rather than never again.
            LOG_WARN("HintCache: could not open the cache lock (err=%lu) -- writing without it", GetLastError());
            m_held = true;
            return;
        }
        const DWORD w = WaitForSingleObject(m_mutex, waitMs);
        m_owned = m_held = (w == WAIT_OBJECT_0 || w == WAIT_ABANDONED);
    }
    ~CacheLock() {
        if (m_owned) ReleaseMutex(m_mutex);
        if (m_mutex) CloseHandle(m_mutex);
    }
    CacheLock(const CacheLock&) = delete;
    CacheLock& operator=(const CacheLock&) = delete;
    // Go ahead: the lock is ours, or there was no lock to take.
    bool Held() const { return m_held; }

private:
    HANDLE m_mutex = nullptr;
    bool   m_owned = false;   // this object holds the mutex, and releases it
    bool   m_held  = false;
};

/// Read the cache, let `mutate` change the root and this game's record, publish -- all under CacheLock, so no other
/// writer's change is lost between the read and the rename. `createIfMissing` false: no file, or no record for this
/// game, means nothing to change. Returns whether the file was published; every failure is logged.
static bool UpdateCacheRecord(const fs::path& path, const char* peHash, bool createIfMissing,
                              const std::function<void(json& root, json& rec)>& mutate, const char* who) {
    CacheLock lock(path);
    if (!lock.Held()) {
        LOG_WARN("HintCache: %s: another writer held the cache for %lu ms -- not written, the previous cache kept",
                 who, Grimoire::HINTCACHE_LOCK_WAIT_MS);
        return false;
    }
    json root;
    if (fs::exists(path)) {
        std::ifstream ifs(path);
        if (ifs.is_open()) {
            root = json::parse(ifs, nullptr, /*allow_exceptions=*/false);
            ifs.close();
        }
    } else if (!createIfMissing) {
        return false;   // nothing written yet -> nothing to fix
    }
    if (!root.is_object()) {
        if (!createIfMissing) return false;
        root = json::object();
    }
    if (createIfMissing) {
        fs::create_directories(path.parent_path());
        if (!root.contains("version")) root["version"] = 1;
        if (!root.contains("games") || !root["games"].is_object()) root["games"] = json::object();
    }
    auto gamesIt = root.find("games");
    if (gamesIt == root.end() || !gamesIt->is_object()) return false;
    json rec = gamesIt->contains(peHash) ? (*gamesIt)[peHash] : json::object();
    if (!rec.is_object()) {
        if (!createIfMissing) return false;
        rec = json::object();
    } else if (!createIfMissing && !gamesIt->contains(peHash)) {
        return false;
    }
    mutate(root, rec);
    root["games"][peHash] = rec;
    // Atomic write: staged temp, verified, then renamed. (FL1/FL2)
    return WriteJsonAtomic(path, root.dump(2), who);
}

static std::string GetUtcTimestamp() {
    auto now = std::chrono::system_clock::now();
    auto t = std::chrono::system_clock::to_time_t(now);
    struct tm tm_buf;
    gmtime_s(&tm_buf, &t);

    std::ostringstream oss;
    oss << std::put_time(&tm_buf, "%Y-%m-%dT%H:%M:%SZ");
    return oss.str();
}

/// Extract the pattern ID from a scan entry, but only if method == "aob".
static std::string ExtractHint(const json& entry) {
    if (!entry.is_object()) return {};
    auto mIt = entry.find("method");
    auto pIt = entry.find("patternId");
    if (mIt == entry.end() || pIt == entry.end()) return {};
    if (!mIt->is_string() || !pIt->is_string()) return {};
    if (mIt->get<std::string>() != "aob") return {};
    return pIt->get<std::string>();
}

// ============================================================
// LoadHints
// ============================================================

ScanHints LoadHints(const char* peHash) {
    ScanHints hints;
    if (!peHash || !peHash[0]) return hints;

    try {
        auto path = GetCacheFilePath();
        if (path.empty() || !fs::exists(path)) return hints;

        // Under the writers' lock. The read would be whole without it -- a publish is a rename -- but this open
        // handle would make a writer's rename fail and drop that change. Past the wait, read anyway: the hints only
        // speed a scan up.
        CacheLock lock(path);

        // Open with shared read access (UI may hold the file)
        std::ifstream ifs(path);
        if (!ifs.is_open()) return hints;

        json root = json::parse(ifs, nullptr, /*allow_exceptions=*/false);
        ifs.close();

        if (!root.is_object()) return hints;

        auto gamesIt = root.find("games");
        if (gamesIt == root.end() || !gamesIt->is_object()) return hints;

        auto recIt = gamesIt->find(peHash);
        if (recIt == gamesIt->end() || !recIt->is_object()) return hints;

        const auto& rec = *recIt;

        // Extract pattern IDs only when method was "aob"
        auto goIt = rec.find("gObjects");
        auto gnIt = rec.find("gNames");
        auto gwIt = rec.find("gWorld");

        if (goIt != rec.end()) hints.gobjectsPatternId = ExtractHint(*goIt);
        if (gnIt != rec.end()) hints.gnamesPatternId   = ExtractHint(*gnIt);
        if (gwIt != rec.end()) hints.gworldPatternId    = ExtractHint(*gwIt);

        // Extract cached UE version (skip expensive DetectVersion on repeat scans)
        auto uvIt = rec.find("ueVersion");
        auto vdIt = rec.find("versionDetected");
        auto lcIt = rec.find("lowConfidence");
        auto rvIt = rec.find("versionDetectRev");
        if (uvIt != rec.end() && uvIt->is_number_integer()) {
            hints.ueVersion = uvIt->get<uint32_t>();
            hints.versionDetected = (vdIt != rec.end() && vdIt->is_boolean())
                                    ? vdIt->get<bool>() : false;
            // lowConfidence/versionDetected are written by both the DLL (always) and the C# UI
            // (DefaultIgnoreCondition omits a false bool) — an absent field reads as false.
            hints.lowConfidence = (lcIt != rec.end() && lcIt->is_boolean())
                                    ? lcIt->get<bool>() : false;
            hints.versionDetectRev = (rvIt != rec.end() && rvIt->is_number_integer())
                                    ? rvIt->get<uint32_t>() : 0;
            hints.hasVersionHint = true;
        }

        // User-set persistent override (per-game, highest priority on next scan).
        auto uoIt = rec.find("ueVersionUserOverride");
        if (uoIt != rec.end() && uoIt->is_number_integer()) {
            uint32_t v = uoIt->get<uint32_t>();
            if (v != 0) {
                hints.userOverrideVersion = v;
                hints.hasUserOverride = true;
            }
        }

        // Per-game GameThreadDispatch invoke timeout override.
        auto itIt = rec.find("invokeTimeoutMs");
        if (itIt != rec.end() && itIt->is_number_integer()) {
            int32_t v = itIt->get<int32_t>();
            if (v > 0) {
                hints.invokeTimeoutMs = v;
                hints.hasInvokeTimeoutOverride = true;
            }
        }

        LOG_INFO("HintCache: Loaded hints for PE=%s (GObj=%s, GNam=%s, GWld=%s, UE=%u%s%s%s)",
                 peHash,
                 hints.gobjectsPatternId.empty() ? "-" : hints.gobjectsPatternId.c_str(),
                 hints.gnamesPatternId.empty()   ? "-" : hints.gnamesPatternId.c_str(),
                 hints.gworldPatternId.empty()    ? "-" : hints.gworldPatternId.c_str(),
                 hints.ueVersion,
                 hints.hasVersionHint ? (hints.versionDetected ? " detected" : " inferred") : " none",
                 hints.hasUserOverride ? " [USER-OVERRIDE active]" : "",
                 hints.hasInvokeTimeoutOverride ? " [INVOKE-TIMEOUT override]" : "");

    } catch (const std::exception& ex) {
        LOG_WARN("HintCache: Failed to load hints: %s", ex.what());
    } catch (...) {
        LOG_WARN("HintCache: Failed to load hints (unknown error)");
    }

    return hints;
}

// ============================================================
// SaveResults
// ============================================================

/// Build a scan entry JSON object matching the C# AobScanEntry format.
static json MakeScanEntry(const char* method, const char* patternId, int tried, int hit) {
    json entry;
    entry["method"]       = method ? method : "not_found";
    entry["patternId"]    = patternId ? patternId : "";
    entry["patternsTried"] = tried;
    entry["patternsHit"]   = hit;
    return entry;
}

void SaveResults(const char* peHash, const Genau::EnginePointers& ptrs,
                 const char* processName) {
    if (!peHash || !peHash[0]) return;

    try {
        auto path = GetCacheFilePath();
        if (path.empty()) return;

        int scanCount = 0;
        const bool ok = UpdateCacheRecord(path, peHash, /*createIfMissing=*/true, [&](json& root, json& rec) {
            // Update machine name
            wchar_t compName[MAX_COMPUTERNAME_LENGTH + 1] = {};
            DWORD compLen = MAX_COMPUTERNAME_LENGTH + 1;
            if (GetComputerNameW(compName, &compLen)) {
                // Convert wchar to UTF-8
                int sz = WideCharToMultiByte(CP_UTF8, 0, compName, -1, nullptr, 0, nullptr, nullptr);
                std::string nameUtf8(sz - 1, '\0');
                WideCharToMultiByte(CP_UTF8, 0, compName, -1, nameUtf8.data(), sz, nullptr, nullptr);
                root["machineName"] = nameUtf8;
            }

            rec["peHash"]   = peHash;
            rec["gameName"] = processName ? processName : "";
            // Cache the version-detection result ONLY when it's a genuine detection. When the
            // version came from a user override (bUserOverride), FindAll skipped detection entirely
            // and ptrs.UEVersion is just the override value — caching it (with versionDetectRev) would
            // let it masquerade as a confident detection once the override is later cleared. So we
            // leave the prior real-detection fields untouched; the override itself lives in
            // ueVersionUserOverride, and clearing it correctly reverts to the last real detection
            // (or re-detects if none was ever cached).
            if (!ptrs.bUserOverride) {
                rec["ueVersion"]        = static_cast<int>(ptrs.UEVersion);
                rec["versionDetected"]  = ptrs.bVersionDetected;
                rec["lowConfidence"]    = ptrs.bLowConfidence;
                // Stamp the detection-logic revision so the next launch can trust this cached
                // version and skip the slow DetectVersion scan (until the logic rev changes).
                rec["versionDetectRev"] = static_cast<int>(Genau::kVersionDetectLogicRev);
            }
            // Preserve any pre-existing ueVersionUserOverride from the previous record —
            // a fresh scan must not silently clobber a user-set persistent override.
            // If the field was absent before, leave it absent (no zero-stamp).
            // The override is written/cleared exclusively via SaveUserOverride.

            rec["gObjects"] = MakeScanEntry(ptrs.gobjectsMethod, ptrs.gobjectsPatternId,
                                            ptrs.gobjectsPatternsTried, ptrs.gobjectsPatternsHit);
            rec["gNames"]   = MakeScanEntry(ptrs.gnamesMethod, ptrs.gnamesPatternId,
                                            ptrs.gnamesPatternsTried, ptrs.gnamesPatternsHit);
            rec["gWorld"]   = MakeScanEntry(ptrs.gworldMethod, ptrs.gworldPatternId,
                                            ptrs.gworldPatternsTried, ptrs.gworldPatternsHit);

            rec["lastScanUtc"] = GetUtcTimestamp();

            // A new record starts at 1, an existing one counts on.
            int count = 1;
            auto cntIt = rec.find("scanCount");
            if (cntIt != rec.end() && cntIt->is_number_integer())
                count = cntIt->get<int>() + 1;
            rec["scanCount"] = count;
            scanCount = count;
        }, "SaveResults");
        if (!ok) return;

        LOG_INFO("HintCache: Saved results for PE=%s (%s, scan #%d)",
                 peHash, processName ? processName : "?", scanCount);

    } catch (const std::exception& ex) {
        LOG_WARN("HintCache: Failed to save results: %s", ex.what());
    } catch (...) {
        LOG_WARN("HintCache: Failed to save results (unknown error)");
    }
}

// ============================================================
// UpdateGObjectsMethod
// ============================================================

void UpdateGObjectsMethod(const char* peHash, const char* method) {
    if (!peHash || !peHash[0] || !method) return;

    try {
        auto path = GetCacheFilePath();
        if (path.empty()) return;

        // Not creating: nothing written yet, or no record for this game, means nothing to fix.
        const bool ok = UpdateCacheRecord(path, peHash, /*createIfMissing=*/false, [&](json&, json& rec) {
            json& go = rec["gObjects"];
            if (!go.is_object()) go = json::object();
            go["method"]    = method;
            go["patternId"] = "";   // the AOB pattern only matched a decoy — do not hint it next launch
        }, "UpdateGObjectsMethod");
        if (!ok) return;

        LOG_INFO("HintCache: Updated gObjects method for PE=%s -> %s (patternId cleared)", peHash, method);

    } catch (const std::exception& ex) {
        LOG_WARN("HintCache: UpdateGObjectsMethod failed: %s", ex.what());
    } catch (...) {
        LOG_WARN("HintCache: UpdateGObjectsMethod failed (unknown error)");
    }
}

// ============================================================
// SaveUserOverride
// ============================================================

bool SaveUserOverride(const char* peHash, uint32_t ueVersion,
                      const char* processName) {
    if (!peHash || !peHash[0]) return false;

    try {
        auto path = GetCacheFilePath();
        if (path.empty()) return false;

        const bool ok = UpdateCacheRecord(path, peHash, /*createIfMissing=*/true, [&](json&, json& rec) {
            rec["peHash"]   = peHash;
            if (processName && processName[0])
                rec["gameName"] = processName;

            if (ueVersion == 0) {
                // Clear override
                rec.erase("ueVersionUserOverride");
                rec.erase("ueVersionUserOverrideAt");
            } else {
                rec["ueVersionUserOverride"]   = static_cast<int>(ueVersion);
                rec["ueVersionUserOverrideAt"] = GetUtcTimestamp();
            }
        }, "SaveUserOverride");
        if (!ok) return false;

        if (ueVersion == 0)
            LOG_INFO("HintCache: Cleared user UE-version override for PE=%s", peHash);
        else
            LOG_INFO("HintCache: Saved user UE-version override for PE=%s -> %u",
                     peHash, ueVersion);
        return true;

    } catch (const std::exception& ex) {
        LOG_WARN("HintCache: SaveUserOverride failed: %s", ex.what());
    } catch (...) {
        LOG_WARN("HintCache: SaveUserOverride failed (unknown error)");
    }
    return false;
}

// ============================================================
// SaveInvokeTimeout — mirrors SaveUserOverride for the GameThreadDispatch
// timeout. Same locked read-modify-write and record preservation.
// ============================================================

bool SaveInvokeTimeout(const char* peHash, int32_t timeoutMs,
                       const char* processName) {
    if (!peHash || !peHash[0]) return false;

    try {
        auto path = GetCacheFilePath();
        if (path.empty()) return false;

        const bool ok = UpdateCacheRecord(path, peHash, /*createIfMissing=*/true, [&](json&, json& rec) {
            rec["peHash"] = peHash;
            if (processName && processName[0])
                rec["gameName"] = processName;

            if (timeoutMs <= 0) {
                // Clear override
                rec.erase("invokeTimeoutMs");
                rec.erase("invokeTimeoutMsAt");
            } else {
                rec["invokeTimeoutMs"]   = timeoutMs;
                rec["invokeTimeoutMsAt"] = GetUtcTimestamp();
            }
        }, "SaveInvokeTimeout");
        if (!ok) return false;

        if (timeoutMs <= 0)
            LOG_INFO("HintCache: Cleared invoke timeout override for PE=%s", peHash);
        else
            LOG_INFO("HintCache: Saved invoke timeout override for PE=%s -> %dms",
                     peHash, timeoutMs);
        return true;

    } catch (const std::exception& ex) {
        LOG_WARN("HintCache: SaveInvokeTimeout failed: %s", ex.what());
    } catch (...) {
        LOG_WARN("HintCache: SaveInvokeTimeout failed (unknown error)");
    }
    return false;
}

} // namespace Flamme
