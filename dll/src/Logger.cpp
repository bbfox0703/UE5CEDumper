// ============================================================
// Logger.cpp — File logger with rotation (5 files, 5MB max)
// ============================================================

#include "Logger.h"
#include "Constants.h"

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

static std::mutex  s_mutex;
static FILE*       s_file = nullptr;
static size_t      s_written = 0;
static fs::path    s_logDir;
static fs::path    s_currentPath;

static fs::path GetLogDirectory() {
    wchar_t* appdata = nullptr;
    if (SUCCEEDED(SHGetKnownFolderPath(FOLDERID_RoamingAppData, 0, nullptr, &appdata))) {
        fs::path dir = fs::path(appdata) / Constants::LOG_FOLDER_NAME / Constants::LOG_SUBFOLDER;
        CoTaskMemFree(appdata);
        return dir;
    }
    return fs::path(L".");
}

static void RotateLogs() {
    // Delete oldest if at max
    for (int i = Constants::LOG_MAX_FILES - 1; i >= 1; --i) {
        auto older = s_logDir / (std::wstring(Constants::LOG_FILE_PREFIX) + L"-" + std::to_wstring(i) + L".log");
        auto newer = s_logDir / (std::wstring(Constants::LOG_FILE_PREFIX) + L"-" + std::to_wstring(i - 1) + L".log");

        if (i == Constants::LOG_MAX_FILES - 1 && fs::exists(older)) {
            fs::remove(older);
        }
        if (fs::exists(newer)) {
            fs::rename(newer, older);
        }
    }

    // Current log is always -0
    s_currentPath = s_logDir / (std::wstring(Constants::LOG_FILE_PREFIX) + L"-0.log");
}

bool Init() {
    std::lock_guard<std::mutex> lock(s_mutex);

    s_logDir = GetLogDirectory();
    std::error_code ec;
    fs::create_directories(s_logDir, ec);

    RotateLogs();

    s_file = _wfopen(s_currentPath.c_str(), L"w");
    if (!s_file) return false;

    s_written = 0;
    return true;
}

void Shutdown() {
    std::lock_guard<std::mutex> lock(s_mutex);
    if (s_file) {
        fflush(s_file);
        fclose(s_file);
        s_file = nullptr;
    }
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

static void WriteLog(const char* level, const char* fmt, va_list args) {
    std::lock_guard<std::mutex> lock(s_mutex);
    if (!s_file) return;

    // Check if rotation needed (size limit)
    if (s_written >= Constants::LOG_MAX_SIZE) {
        fflush(s_file);
        fclose(s_file);
        RotateLogs();
        s_file = _wfopen(s_currentPath.c_str(), L"w");
        if (!s_file) return;
        s_written = 0;
    }

    auto ts = GetTimestamp();
    char msgBuf[4096];
    vsnprintf(msgBuf, sizeof(msgBuf), fmt, args);

    int written = fprintf(s_file, "[%s] [%s] %s\n", ts.c_str(), level, msgBuf);
    if (written > 0) s_written += static_cast<size_t>(written);
    fflush(s_file);
}

void Info(const char* fmt, ...) {
    va_list args;
    va_start(args, fmt);
    WriteLog("INFO", fmt, args);
    va_end(args);
}

void Error(const char* fmt, ...) {
    va_list args;
    va_start(args, fmt);
    WriteLog("ERROR", fmt, args);
    va_end(args);
}

void Warn(const char* fmt, ...) {
    va_list args;
    va_start(args, fmt);
    WriteLog("WARN", fmt, args);
    va_end(args);
}

void Debug(const char* fmt, ...) {
    va_list args;
    va_start(args, fmt);
    WriteLog("DEBUG", fmt, args);
    va_end(args);
}

} // namespace Logger
