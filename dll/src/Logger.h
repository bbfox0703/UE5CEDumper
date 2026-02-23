#pragma once

// ============================================================
// Logger.h — Category-routed file logger
//
// Each log message is routed to a category-specific file under
// the per-process folder based on its category tag prefix:
//
//   init.log    — INIT, CEP, SUMMARY (lifecycle)
//   scan.log    — SCAN:*, MEM        (AOB scanning, pointers)
//   offsets.log — DYNO:*, OARR, FNAM (dynamic offsets, stride)
//   pipe.log    — PIPE:*             (pipe server, commands)
//   walk.log    — WALK:*             (struct walking, props)
//
// Format: [timestamp] [LEVEL] [CAT] message
// SUMMARY: [timestamp] [SUMMARY] message (routed to init.log)
// ============================================================

#include <cstdint>
#include <string>

// Kept for backward compatibility (SetChannel/GetChannel are no-ops).
enum class LogChannel { Scan, Pipe };

namespace Logger {

// Initialize the logger: creates log directory, enables early buffering.
// Actual log files are opened in InitProcessMirror().
bool Init();

// Open category log files in a per-process subfolder.
// Call AFTER Init() and after DLL determines the host process name.
// Creates <logDir>/<processName>/ with 5 category files, 2-file rotation.
// Flushes any early-buffered lines to the correct files.
// Cleans up old subfolders if more than maxSubfolders exist.
void InitProcessMirror(const std::wstring& processName, int maxSubfolders = 20);

// Shutdown: flush and close all category files
void Shutdown();

// No-ops, kept for backward API compatibility.
// Category routing replaces channel switching.
void SetChannel(LogChannel ch);
LogChannel GetChannel();

// Category-aware log functions
// cat: category tag string (e.g. "SCAN:GObj", "PIPE:cmd", "MEM")
//      empty string "" is allowed (routed to init.log)
void Info(const char* cat, const char* fmt, ...);
void Error(const char* cat, const char* fmt, ...);
void Warn(const char* cat, const char* fmt, ...);
void Debug(const char* cat, const char* fmt, ...);

// Summary level — routed to init.log.
// No category tag; format: [timestamp] [SUMMARY] message
void Summary(const char* fmt, ...);

} // namespace Logger

// Convenience macros — each source file should #define LOG_CAT before
// including this header (or before using the macros).
// Example:  #define LOG_CAT "SCAN:GObj"
//
// If LOG_CAT is not defined, defaults to "" (routed to init.log).
#ifndef LOG_CAT
#define LOG_CAT ""
#endif

#define LOG_INFO(fmt, ...)    Logger::Info(LOG_CAT, fmt, ##__VA_ARGS__)
#define LOG_ERROR(fmt, ...)   Logger::Error(LOG_CAT, fmt, ##__VA_ARGS__)
#define LOG_WARN(fmt, ...)    Logger::Warn(LOG_CAT, fmt, ##__VA_ARGS__)
#define LOG_DEBUG(fmt, ...)   Logger::Debug(LOG_CAT, fmt, ##__VA_ARGS__)
#define LOG_SUMMARY(fmt, ...) Logger::Summary(fmt, ##__VA_ARGS__)
