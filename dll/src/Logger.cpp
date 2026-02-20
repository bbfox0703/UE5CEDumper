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

static void WriteLog(const char* level, const char* cat, const char* fmt, va_list args) {
    std::lock_guard<std::mutex> lock(s_mutex);

    ChannelState& ch = GetActiveState();
    if (!ch.file) return;

    RotateIfNeeded(ch);

    auto ts = GetTimestamp();
    char msgBuf[4096];
    vsnprintf(msgBuf, sizeof(msgBuf), fmt, args);

    int written;
    if (cat && cat[0] != '\0') {
        written = fprintf(ch.file, "[%s] [%s] [%s] %s\n", ts.c_str(), level, cat, msgBuf);
    } else {
        written = fprintf(ch.file, "[%s] [%s] %s\n", ts.c_str(), level, msgBuf);
    }
    if (written > 0) ch.written += static_cast<size_t>(written);
    fflush(ch.file);
}

static void WriteSummary(const char* fmt, va_list args) {
    std::lock_guard<std::mutex> lock(s_mutex);

    // SUMMARY always writes to the scan log
    if (!s_scan.file) return;
    RotateIfNeeded(s_scan);

    auto ts = GetTimestamp();
    char msgBuf[4096];
    vsnprintf(msgBuf, sizeof(msgBuf), fmt, args);

    int written = fprintf(s_scan.file, "[%s] [SUMMARY] %s\n", ts.c_str(), msgBuf);
    if (written > 0) s_scan.written += static_cast<size_t>(written);
    fflush(s_scan.file);
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

void Shutdown() {
    std::lock_guard<std::mutex> lock(s_mutex);
    ShutdownChannel(s_scan);
    ShutdownChannel(s_pipe);
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
