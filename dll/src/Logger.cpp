// ============================================================
// Logger.cpp — Category-routed file logger
//
// Routes log messages to category-specific files under the
// per-process subfolder based on the category tag prefix.
// ============================================================

#include "Logger.h"
#include "Constants.h"
#include "BuildInfo.h"

#include <Windows.h>
#include <ShlObj.h>
#include <cstdio>
#include <cstdarg>
#include <cstring>
#include <mutex>
#include <filesystem>
#include <chrono>
#include <iomanip>
#include <sstream>
#include <vector>
#include <utility>

namespace fs = std::filesystem;

namespace Logger {

// ================================================================
// Log file categories
// ================================================================

enum LogFile : uint8_t {
    LF_Init = 0,   // init.log    — INIT, CEP, SUMMARY
    LF_Scan,        // scan.log    — SCAN:*, MEM
    LF_Offsets,     // offsets.log — DYNO:*, OARR, FNAM
    LF_Pipe,        // pipe.log    — PIPE:*
    LF_Walk,        // walk.log    — WALK:*
    LF_COUNT
};

static const wchar_t* s_fileNames[LF_COUNT] = {
    L"init", L"scan", L"offsets", L"pipe", L"walk"
};

// ================================================================
// Category → file routing (prefix-match, longest first)
// ================================================================

struct CatMapping {
    const char* prefix;
    uint8_t     prefixLen;
    LogFile     file;
};

// Sorted longest-prefix-first for correct matching
static const CatMapping s_catMap[] = {
    { "WALK:StructP",  12, LF_Walk    },
    { "WALK:ArrayP",   11, LF_Walk    },
    { "PIPE:world",    10, LF_Pipe    },
    { "PIPE:watch",    10, LF_Pipe    },
    { "DYNO:Enum",      9, LF_Offsets },
    { "SCAN:GObj",      8, LF_Scan    },
    { "SCAN:GNam",      8, LF_Scan    },
    { "SCAN:GWld",      8, LF_Scan    },
    { "SCAN:Ver",       8, LF_Scan    },
    { "PIPE:svr",       8, LF_Pipe    },
    { "PIPE:cmd",       8, LF_Pipe    },
    { "SUMMARY",        7, LF_Init    },
    { "INIT",           4, LF_Init    },
    { "SCAN",           4, LF_Scan    },
    { "DYNO",           4, LF_Offsets },
    { "OARR",           4, LF_Offsets },
    { "FNAM",           4, LF_Offsets },
    { "WALK",           4, LF_Walk    },
    { "PIPE",           4, LF_Pipe    },
    { "CEP",            3, LF_Init    },
    { "MEM",            3, LF_Scan    },
};

static LogFile ResolveFile(const char* cat) {
    if (!cat || cat[0] == '\0') return LF_Init;
    for (const auto& m : s_catMap) {
        if (strncmp(cat, m.prefix, m.prefixLen) == 0) return m.file;
    }
    return LF_Init;  // fallback: unknown categories go to init.log
}

// ================================================================
// Per-file state
// ================================================================

struct LogFileState {
    FILE*          file    = nullptr;
    size_t         written = 0;
    fs::path       currentPath;
    std::wstring   baseName;
};

static std::mutex     s_mutex;
static LogFileState   s_files[LF_COUNT];
static bool           s_filesOpen   = false;
static fs::path       s_logDir;                 // base: %LOCALAPPDATA%\UE5CEDumper\Logs
static fs::path       s_processDir;             // per-process subfolder

// Early buffering: lines logged before InitProcessMirror opens files
static std::vector<std::pair<LogFile, std::string>> s_earlyBuffer;
static bool           s_buffering   = true;
static constexpr size_t EARLY_BUFFER_MAX = 100;

// ================================================================
// Helpers
// ================================================================

static fs::path GetLogDirectory() {
    wchar_t* appdata = nullptr;
    if (SUCCEEDED(SHGetKnownFolderPath(FOLDERID_LocalAppData, 0, nullptr, &appdata))) {
        fs::path dir = fs::path(appdata) / Constants::LOG_FOLDER_NAME / Constants::LOG_SUBFOLDER;
        CoTaskMemFree(appdata);
        return dir;
    }
    return fs::path(L".");
}

static std::string GetTimestamp() {
    auto now = std::chrono::system_clock::now();
    auto t = std::chrono::system_clock::to_time_t(now);
    auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(
        now.time_since_epoch()) % 1000;

    struct tm tm_buf;
    localtime_s(&tm_buf, &t);

    std::ostringstream oss;
    oss << std::put_time(&tm_buf, "%Y-%m-%d %H:%M:%S")
        << '.' << std::setfill('0') << std::setw(3) << ms.count();
    return oss.str();
}

// ================================================================
// File rotation
// ================================================================

static void RotateLogsInDir(const fs::path& dir, const wchar_t* baseName, int maxFiles) {
    for (int i = maxFiles - 1; i >= 1; --i) {
        auto older = dir / (std::wstring(baseName) + L"-" + std::to_wstring(i) + L".log");
        auto newer = dir / (std::wstring(baseName) + L"-" + std::to_wstring(i - 1) + L".log");
        if (i == maxFiles - 1 && fs::exists(older)) fs::remove(older);
        if (fs::exists(newer)) fs::rename(newer, older);
    }
}

static void RotateIfNeeded(LogFileState& fs_state) {
    if (fs_state.written < Constants::LOG_MAX_SIZE) return;

    fflush(fs_state.file);
    fclose(fs_state.file);

    RotateLogsInDir(fs_state.currentPath.parent_path(), fs_state.baseName.c_str(),
                    Constants::LOG_ROTATE_MAX);

    fs_state.file = _wfopen(fs_state.currentPath.c_str(), L"w");
    fs_state.written = 0;

    if (fs_state.file) {
        auto ts = GetTimestamp();
        int n = fprintf(fs_state.file, "[%s] [INFO] [INIT] Log rotated | build: %s\n",
                        ts.c_str(), BUILD_VERSION_STRING);
        if (n > 0) fs_state.written += static_cast<size_t>(n);
        fflush(fs_state.file);
    }
}

// ================================================================
// Init / open / close files
// ================================================================

static bool OpenFileInDir(LogFileState& fs_state, const fs::path& dir,
                          const wchar_t* baseName, int maxRotate) {
    fs_state.baseName = baseName;
    RotateLogsInDir(dir, baseName, maxRotate);
    fs_state.currentPath = dir / (std::wstring(baseName) + L"-0.log");
    fs_state.file = _wfopen(fs_state.currentPath.c_str(), L"w");
    if (!fs_state.file) return false;
    fs_state.written = 0;

    auto ts = GetTimestamp();
    int n = fprintf(fs_state.file, "[%s] [INFO] [INIT] Logger started | build: %s\n",
                    ts.c_str(), BUILD_VERSION_STRING);
    if (n > 0) fs_state.written += static_cast<size_t>(n);
    fflush(fs_state.file);
    return true;
}

static void CloseFile(LogFileState& fs_state) {
    if (fs_state.file) {
        fflush(fs_state.file);
        fclose(fs_state.file);
        fs_state.file = nullptr;
    }
}

// Write a pre-formatted line to a file
static void WriteToFile(LogFileState& fs_state, const char* line) {
    if (!fs_state.file) return;
    RotateIfNeeded(fs_state);
    int written = fprintf(fs_state.file, "%s\n", line);
    if (written > 0) fs_state.written += static_cast<size_t>(written);
    fflush(fs_state.file);
}

// ================================================================
// Process folder cleanup
// ================================================================

static void CleanupProcessFolders(const fs::path& parentDir, int maxKeep) {
    std::vector<std::pair<fs::file_time_type, fs::path>> folders;
    std::error_code ec;
    for (auto& entry : fs::directory_iterator(parentDir, ec)) {
        if (entry.is_directory(ec)) {
            auto latestTime = entry.last_write_time(ec);
            for (auto& sub : fs::directory_iterator(entry.path(), ec)) {
                auto t = sub.last_write_time(ec);
                if (t > latestTime) latestTime = t;
            }
            folders.emplace_back(latestTime, entry.path());
        }
    }

    if (static_cast<int>(folders.size()) <= maxKeep) return;

    std::sort(folders.begin(), folders.end(), [](auto& a, auto& b) {
        return a.first > b.first;
    });

    for (size_t i = static_cast<size_t>(maxKeep); i < folders.size(); ++i) {
        fs::remove_all(folders[i].second, ec);
    }
}

// ================================================================
// Core write functions
// ================================================================

static void WriteLog(const char* level, const char* cat, const char* fmt, va_list args) {
    std::lock_guard<std::mutex> lock(s_mutex);

    auto ts = GetTimestamp();
    char msgBuf[4096];
    vsnprintf(msgBuf, sizeof(msgBuf), fmt, args);

    char lineBuf[4352];
    if (cat && cat[0] != '\0') {
        snprintf(lineBuf, sizeof(lineBuf), "[%s] [%s] [%s] %s", ts.c_str(), level, cat, msgBuf);
    } else {
        snprintf(lineBuf, sizeof(lineBuf), "[%s] [%s] %s", ts.c_str(), level, msgBuf);
    }

    LogFile target = ResolveFile(cat);

    if (s_filesOpen) {
        WriteToFile(s_files[target], lineBuf);
    } else if (s_buffering && s_earlyBuffer.size() < EARLY_BUFFER_MAX) {
        s_earlyBuffer.emplace_back(target, std::string(lineBuf));
    }
}

static void WriteSummary(const char* fmt, va_list args) {
    std::lock_guard<std::mutex> lock(s_mutex);

    auto ts = GetTimestamp();
    char msgBuf[4096];
    vsnprintf(msgBuf, sizeof(msgBuf), fmt, args);

    char lineBuf[4352];
    snprintf(lineBuf, sizeof(lineBuf), "[%s] [SUMMARY] %s", ts.c_str(), msgBuf);

    if (s_filesOpen) {
        WriteToFile(s_files[LF_Init], lineBuf);
    } else if (s_buffering && s_earlyBuffer.size() < EARLY_BUFFER_MAX) {
        s_earlyBuffer.emplace_back(LF_Init, std::string(lineBuf));
    }
}

// ================================================================
// Public API
// ================================================================

bool Init() {
    std::lock_guard<std::mutex> lock(s_mutex);

    s_logDir = GetLogDirectory();
    std::error_code ec;
    fs::create_directories(s_logDir, ec);

    // Enable early buffering — files will be opened in InitProcessMirror
    s_buffering = true;
    s_earlyBuffer.clear();
    s_earlyBuffer.reserve(EARLY_BUFFER_MAX);
    s_filesOpen = false;

    return true;
}

void InitProcessMirror(const std::wstring& processName, int maxSubfolders) {
    std::lock_guard<std::mutex> lock(s_mutex);

    if (processName.empty()) return;

    // Sanitize process name
    std::wstring safeName = processName;
    auto dotPos = safeName.rfind(L'.');
    if (dotPos != std::wstring::npos) safeName = safeName.substr(0, dotPos);
    for (wchar_t& c : safeName) {
        if (c == L'/' || c == L'\\' || c == L':' || c == L'*' ||
            c == L'?' || c == L'"' || c == L'<' || c == L'>' || c == L'|') {
            c = L'_';
        }
    }

    s_processDir = s_logDir / safeName;
    std::error_code ec;
    fs::create_directories(s_processDir, ec);
    if (ec) return;

    // Open all 5 category files
    for (int i = 0; i < LF_COUNT; ++i) {
        OpenFileInDir(s_files[i], s_processDir, s_fileNames[i], Constants::LOG_ROTATE_MAX);
    }
    s_filesOpen = true;

    // Flush early buffer to the correct files
    s_buffering = false;
    for (auto& [target, line] : s_earlyBuffer) {
        WriteToFile(s_files[target], line.c_str());
    }
    s_earlyBuffer.clear();
    s_earlyBuffer.shrink_to_fit();

    // Clean up old process folders
    CleanupProcessFolders(s_logDir, maxSubfolders);
}

void Shutdown() {
    std::lock_guard<std::mutex> lock(s_mutex);
    for (int i = 0; i < LF_COUNT; ++i) {
        CloseFile(s_files[i]);
    }
    s_filesOpen = false;
    s_buffering = false;
    s_earlyBuffer.clear();
}

void SetChannel(LogChannel /*ch*/) {
    // No-op: category routing replaces channel switching
}

LogChannel GetChannel() {
    return LogChannel::Scan;  // No-op
}

void Info(const char* cat, const char* fmt, ...) {
    va_list args;
    va_start(args, fmt);
    WriteLog("INFO", cat, fmt, args);
    va_end(args);
}

void Error(const char* cat, const char* fmt, ...) {
    va_list args;
    va_start(args, fmt);
    WriteLog("ERROR", cat, fmt, args);
    va_end(args);
}

void Warn(const char* cat, const char* fmt, ...) {
    va_list args;
    va_start(args, fmt);
    WriteLog("WARN", cat, fmt, args);
    va_end(args);
}

void Debug(const char* cat, const char* fmt, ...) {
    va_list args;
    va_start(args, fmt);
    WriteLog("DEBUG", cat, fmt, args);
    va_end(args);
}

void Summary(const char* fmt, ...) {
    va_list args;
    va_start(args, fmt);
    WriteSummary(fmt, args);
    va_end(args);
}

} // namespace Logger
