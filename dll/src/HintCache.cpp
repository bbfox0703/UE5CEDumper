// ============================================================
// HintCache.cpp — JSON-based scan hint cache
//
// Reads/writes %LOCALAPPDATA%\UE5CEDumper\UE5CEDumper.{COMPUTERNAME}.json
// to accelerate repeat scans by trying the previously-winning
// AOB pattern first.
// ============================================================

#include "HintCache.h"
#include "OffsetFinder.h"
#include "Constants.h"
#define LOG_CAT "SCAN"
#include "Logger.h"

#include <Windows.h>
#include <ShlObj.h>
#include <json.hpp>
#include <filesystem>
#include <fstream>
#include <chrono>
#include <iomanip>
#include <sstream>

namespace fs = std::filesystem;
using json = nlohmann::json;

namespace HintCache {

// ============================================================
// Helpers
// ============================================================

/// Build the cache file path: %LOCALAPPDATA%\UE5CEDumper\UE5CEDumper.{COMPUTERNAME}.json
static fs::path GetCacheFilePath() {
    wchar_t* appdata = nullptr;
    if (!SUCCEEDED(SHGetKnownFolderPath(FOLDERID_LocalAppData, 0, nullptr, &appdata)))
        return {};

    fs::path dir = fs::path(appdata) / Constants::LOG_FOLDER_NAME;
    CoTaskMemFree(appdata);

    // Get machine name
    wchar_t compName[MAX_COMPUTERNAME_LENGTH + 1] = {};
    DWORD compLen = MAX_COMPUTERNAME_LENGTH + 1;
    if (!GetComputerNameW(compName, &compLen))
        wcscpy_s(compName, L"UNKNOWN");

    // UE5CEDumper.{COMPUTERNAME}.json
    std::wstring filename = std::wstring(Constants::HINT_CACHE_PREFIX) + L"." +
                            compName + L".json";
    return dir / filename;
}

/// Get current UTC timestamp in ISO 8601 format.
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
        if (uvIt != rec.end() && uvIt->is_number_integer()) {
            hints.ueVersion = uvIt->get<uint32_t>();
            hints.versionDetected = (vdIt != rec.end() && vdIt->is_boolean())
                                    ? vdIt->get<bool>() : false;
            hints.hasVersionHint = true;
        }

        LOG_INFO("HintCache: Loaded hints for PE=%s (GObj=%s, GNam=%s, GWld=%s, UE=%u%s)",
                 peHash,
                 hints.gobjectsPatternId.empty() ? "-" : hints.gobjectsPatternId.c_str(),
                 hints.gnamesPatternId.empty()   ? "-" : hints.gnamesPatternId.c_str(),
                 hints.gworldPatternId.empty()    ? "-" : hints.gworldPatternId.c_str(),
                 hints.ueVersion,
                 hints.hasVersionHint ? (hints.versionDetected ? " detected" : " inferred") : " none");

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

void SaveResults(const char* peHash, const OffsetFinder::EnginePointers& ptrs,
                 const char* processName) {
    if (!peHash || !peHash[0]) return;

    try {
        auto path = GetCacheFilePath();
        if (path.empty()) return;

        // Ensure directory exists
        fs::create_directories(path.parent_path());

        // Load existing file (or start fresh)
        json root;
        if (fs::exists(path)) {
            std::ifstream ifs(path);
            if (ifs.is_open()) {
                root = json::parse(ifs, nullptr, /*allow_exceptions=*/false);
                ifs.close();
                if (!root.is_object()) root = json::object();
            }
        }

        // Ensure structure
        if (!root.contains("version")) root["version"] = 1;

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

        if (!root.contains("games") || !root["games"].is_object())
            root["games"] = json::object();

        // Build or update the record
        json& games = root["games"];
        bool isUpdate = games.contains(peHash);

        json rec;
        if (isUpdate) {
            rec = games[peHash];
        }

        rec["peHash"]   = peHash;
        rec["gameName"] = processName ? processName : "";
        rec["ueVersion"] = static_cast<int>(ptrs.UEVersion);
        rec["versionDetected"] = ptrs.bVersionDetected;

        rec["gObjects"] = MakeScanEntry(ptrs.gobjectsMethod, ptrs.gobjectsPatternId,
                                        ptrs.gobjectsPatternsTried, ptrs.gobjectsPatternsHit);
        rec["gNames"]   = MakeScanEntry(ptrs.gnamesMethod, ptrs.gnamesPatternId,
                                        ptrs.gnamesPatternsTried, ptrs.gnamesPatternsHit);
        rec["gWorld"]   = MakeScanEntry(ptrs.gworldMethod, ptrs.gworldPatternId,
                                        ptrs.gworldPatternsTried, ptrs.gworldPatternsHit);

        rec["lastScanUtc"] = GetUtcTimestamp();

        if (isUpdate) {
            int count = 1;
            auto cntIt = rec.find("scanCount");
            if (cntIt != rec.end() && cntIt->is_number_integer())
                count = cntIt->get<int>() + 1;
            rec["scanCount"] = count;
        } else {
            rec["scanCount"] = 1;
        }

        games[peHash] = rec;

        // Write atomically: temp file + rename
        auto tempPath = path;
        tempPath += L".tmp";
        {
            std::ofstream ofs(tempPath, std::ios::trunc);
            if (!ofs.is_open()) {
                LOG_WARN("HintCache: Failed to open temp file for writing");
                return;
            }
            ofs << root.dump(2);  // Pretty-print with 2-space indent
        }
        fs::rename(tempPath, path);

        LOG_INFO("HintCache: Saved results for PE=%s (%s, scan #%d)",
                 peHash, processName ? processName : "?",
                 games[peHash]["scanCount"].get<int>());

    } catch (const std::exception& ex) {
        LOG_WARN("HintCache: Failed to save results: %s", ex.what());
    } catch (...) {
        LOG_WARN("HintCache: Failed to save results (unknown error)");
    }
}

} // namespace HintCache
