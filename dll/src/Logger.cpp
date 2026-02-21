// ============================================================
// Logger.cpp — Dual-channel file logger with category tags
// ============================================================

#include "Logger.h"
#include "Constants.h"
#include "BuildInfo.h"

#include <Windows.h>
#include <ShlObj.h>
#include <cstdio>
#include <cstdarg>
#include <mutex>
#include <filesystem>
#include <chrono>
#include <iomanip>
#include <sstream>

namespace fs = std::filesystem;

namespace Logger {

// --- Per-channel state ---
struct ChannelState {
    FILE*           file    = nullptr;
    size_t          written = 0;
    fs::path        currentPath;
    const wchar_t*  prefix  = nullptr;
};

static std::mutex     s_mutex;
static ChannelState   s_scan;
static ChannelState   s_pipe;
static ChannelState   s_mirrorScan;  // per-process mirror
static ChannelState   s_mirrorPipe;
static bool           s_mirrorActive = false;
static LogChannel     s_activeChannel = LogChannel::Scan;
static fs::path       s_logDir;

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

static void RotateLogs(const wchar_t* prefix) {
    for (int i = Constants::LOG_MAX_FILES - 1; i >= 1; --i) {
        auto older = s_logDir / (std::wstring(prefix) + L"-" + std::to_wstring(i) + L".log");
        auto newer = s_logDir / (std::wstring(prefix) + L"-" + std::to_wstring(i - 1) + L".log");

        if (i == Constants::LOG_MAX_FILES - 1 && fs::exists(older)) {
            fs::remove(older);
        }
        if (fs::exists(newer)) {
            fs::rename(newer, older);
        }
    }
}

static void RotateIfNeeded(ChannelState& ch) {
    if (ch.written < Constants::LOG_MAX_SIZE) return;

    fflush(ch.file);
    fclose(ch.file);
    RotateLogs(ch.prefix);
    ch.file = _wfopen(ch.currentPath.c_str(), L"w");
    ch.written = 0;

    if (ch.file) {
        auto ts = GetTimestamp();
        int n = fprintf(ch.file, "[%s] [INFO] [INIT] Log rotated | build: %s\n",
                        ts.c_str(), BUILD_VERSION_STRING);
        if (n > 0) ch.written += static_cast<size_t>(n);
        fflush(ch.file);
    }
}

static bool InitChannel(ChannelState& ch, const wchar_t* prefix) {
    ch.prefix = prefix;
    RotateLogs(prefix);
    ch.currentPath = s_logDir / (std::wstring(prefix) + L"-0.log");
    ch.file = _wfopen(ch.currentPath.c_str(), L"w");
    if (!ch.file) return false;
    ch.written = 0;

    // Write build version header as first line
    auto ts = GetTimestamp();
    int n = fprintf(ch.file, "[%s] [INFO] [INIT] Logger started | build: %s\n",
                    ts.c_str(), BUILD_VERSION_STRING);
    if (n > 0) ch.written += static_cast<size_t>(n);
    fflush(ch.file);
    return true;
}

static void ShutdownChannel(ChannelState& ch) {
    if (ch.file) {
        fflush(ch.file);
        fclose(ch.file);
        ch.file = nullptr;
    }
}

static ChannelState& GetActiveState() {
    return (s_activeChannel == LogChannel::Scan) ? s_scan : s_pipe;
}

// Write a pre-formatted line to a channel (helper for mirror writes)
static void WriteToChannel(ChannelState& ch, const char* line) {
    if (!ch.file) return;
    RotateIfNeeded(ch);
    int written = fprintf(ch.file, "%s\n", line);
    if (written > 0) ch.written += static_cast<size_t>(written);
    fflush(ch.file);
}

static void WriteLog(const char* level, const char* cat, const char* fmt, va_list args) {
    std::lock_guard<std::mutex> lock(s_mutex);

    ChannelState& ch = GetActiveState();
    if (!ch.file && !s_mirrorActive) return;

    auto ts = GetTimestamp();
    char msgBuf[4096];
    vsnprintf(msgBuf, sizeof(msgBuf), fmt, args);

    char lineBuf[4352];
    if (cat && cat[0] != '\0') {
        snprintf(lineBuf, sizeof(lineBuf), "[%s] [%s] [%s] %s", ts.c_str(), level, cat, msgBuf);
    } else {
        snprintf(lineBuf, sizeof(lineBuf), "[%s] [%s] %s", ts.c_str(), level, msgBuf);
    }

    // Write to primary channel
    WriteToChannel(ch, lineBuf);

    // Write to mirror (same channel mapping)
    if (s_mirrorActive) {
        ChannelState& mirror = (s_activeChannel == LogChannel::Scan) ? s_mirrorScan : s_mirrorPipe;
        WriteToChannel(mirror, lineBuf);
    }
}

static void WriteSummary(const char* fmt, va_list args) {
    std::lock_guard<std::mutex> lock(s_mutex);

    auto ts = GetTimestamp();
    char msgBuf[4096];
    vsnprintf(msgBuf, sizeof(msgBuf), fmt, args);

    char lineBuf[4352];
    snprintf(lineBuf, sizeof(lineBuf), "[%s] [SUMMARY] %s", ts.c_str(), msgBuf);

    // SUMMARY always writes to the scan log
    WriteToChannel(s_scan, lineBuf);

    // Also write to mirror scan
    if (s_mirrorActive) {
        WriteToChannel(s_mirrorScan, lineBuf);
    }
}

// --- Public API ---

bool Init() {
    std::lock_guard<std::mutex> lock(s_mutex);

    s_logDir = GetLogDirectory();
    std::error_code ec;
    fs::create_directories(s_logDir, ec);

    bool ok = true;
    ok &= InitChannel(s_scan, Constants::LOG_SCAN_PREFIX);
    ok &= InitChannel(s_pipe, Constants::LOG_PIPE_PREFIX);
    s_activeChannel = LogChannel::Scan;
    return ok;
}

static void RotateLogsInDir(const fs::path& dir, const wchar_t* prefix, int maxFiles) {
    for (int i = maxFiles - 1; i >= 1; --i) {
        auto older = dir / (std::wstring(prefix) + L"-" + std::to_wstring(i) + L".log");
        auto newer = dir / (std::wstring(prefix) + L"-" + std::to_wstring(i - 1) + L".log");
        if (i == maxFiles - 1 && fs::exists(older)) fs::remove(older);
        if (fs::exists(newer)) fs::rename(newer, older);
    }
}

static bool InitChannelInDir(ChannelState& ch, const fs::path& dir, const wchar_t* prefix, int maxRotate) {
    ch.prefix = prefix;
    RotateLogsInDir(dir, prefix, maxRotate);
    ch.currentPath = dir / (std::wstring(prefix) + L"-0.log");
    ch.file = _wfopen(ch.currentPath.c_str(), L"w");
    if (!ch.file) return false;
    ch.written = 0;

    auto ts = GetTimestamp();
    int n = fprintf(ch.file, "[%s] [INFO] [INIT] Logger started | build: %s\n",
                    ts.c_str(), BUILD_VERSION_STRING);
    if (n > 0) ch.written += static_cast<size_t>(n);
    fflush(ch.file);
    return true;
}

// Cleanup old process subfolders, keeping only the most recent maxKeep
static void CleanupProcessFolders(const fs::path& parentDir, int maxKeep) {
    std::vector<std::pair<fs::file_time_type, fs::path>> folders;
    std::error_code ec;
    for (auto& entry : fs::directory_iterator(parentDir, ec)) {
        if (entry.is_directory(ec)) {
            // Get latest modification time of any file in the subfolder
            auto latestTime = entry.last_write_time(ec);
            for (auto& sub : fs::directory_iterator(entry.path(), ec)) {
                auto t = sub.last_write_time(ec);
                if (t > latestTime) latestTime = t;
            }
            folders.emplace_back(latestTime, entry.path());
        }
    }

    if (static_cast<int>(folders.size()) <= maxKeep) return;

    // Sort by time (newest first), remove the oldest
    std::sort(folders.begin(), folders.end(), [](auto& a, auto& b) {
        return a.first > b.first;
    });

    for (size_t i = static_cast<size_t>(maxKeep); i < folders.size(); ++i) {
        fs::remove_all(folders[i].second, ec);
    }
}

void InitProcessMirror(const std::wstring& processName, int maxSubfolders) {
    std::lock_guard<std::mutex> lock(s_mutex);

    if (processName.empty()) return;

    // Sanitize process name (remove .exe, replace non-filesystem chars)
    std::wstring safeName = processName;
    // Remove .exe extension if present
    auto dotPos = safeName.rfind(L'.');
    if (dotPos != std::wstring::npos) safeName = safeName.substr(0, dotPos);
    // Replace invalid filesystem characters
    for (wchar_t& c : safeName) {
        if (c == L'/' || c == L'\\' || c == L':' || c == L'*' ||
            c == L'?' || c == L'"' || c == L'<' || c == L'>' || c == L'|') {
            c = L'_';
        }
    }

    fs::path mirrorDir = s_logDir / safeName;
    std::error_code ec;
    fs::create_directories(mirrorDir, ec);
    if (ec) return;

    // 2-version rotation for per-process logs
    constexpr int PROCESS_LOG_ROTATE = 2;
    InitChannelInDir(s_mirrorScan, mirrorDir, Constants::LOG_SCAN_PREFIX, PROCESS_LOG_ROTATE);
    InitChannelInDir(s_mirrorPipe, mirrorDir, Constants::LOG_PIPE_PREFIX, PROCESS_LOG_ROTATE);
    s_mirrorActive = true;

    // Clean up old process folders (keep maxSubfolders most recent)
    CleanupProcessFolders(s_logDir, maxSubfolders);
}

void Shutdown() {
    std::lock_guard<std::mutex> lock(s_mutex);
    ShutdownChannel(s_scan);
    ShutdownChannel(s_pipe);
    if (s_mirrorActive) {
        ShutdownChannel(s_mirrorScan);
        ShutdownChannel(s_mirrorPipe);
        s_mirrorActive = false;
    }
}

void SetChannel(LogChannel ch) {
    std::lock_guard<std::mutex> lock(s_mutex);
    s_activeChannel = ch;
}

LogChannel GetChannel() {
    return s_activeChannel;
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
