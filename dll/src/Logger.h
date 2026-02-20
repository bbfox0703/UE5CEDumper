#pragma once

// ============================================================
// Logger.h — Dual-channel file logger with category tags
//
// Channels:
//   Scan — init/scan phase (OffsetFinder, DynOff, version detect)
//   Pipe — runtime phase (pipe commands, walker, watch)
//
// Format: [timestamp] [LEVEL] [CAT] message
// SUMMARY: [timestamp] [SUMMARY] message (always scan log)
// ============================================================

#include <cstdint>
#include <string>

// Log channels — determines which file receives output
enum class LogChannel { Scan, Pipe };

namespace Logger {

// Initialize both log files (scan + pipe), rotate old logs, write build header
bool Init();

// Shutdown: flush and close both files
void Shutdown();

// Switch the active channel (default: Scan at startup)
void SetChannel(LogChannel ch);
LogChannel GetChannel();

// Category-aware log functions
// cat: category tag string (e.g. "SCAN:GObj", "PIPE:cmd", "MEM")
//      empty string "" is allowed (no tag bracket shown)
void Info(const char* cat, const char* fmt, ...);
void Error(const char* cat, const char* fmt, ...);
void Warn(const char* cat, const char* fmt, ...);
void Debug(const char* cat, const char* fmt, ...);

// Summary level — always writes to the Scan log file regardless of active channel.
// No category tag; format: [timestamp] [SUMMARY] message
void Summary(const char* fmt, ...);

} // namespace Logger

// Convenience macros — each source file should #define LOG_CAT before
// including this header (or before using the macros).
// Example:  #define LOG_CAT "SCAN:GObj"
//
// If LOG_CAT is not defined, defaults to "" (no tag shown).
#ifndef LOG_CAT
#define LOG_CAT ""
#endif

#define LOG_INFO(fmt, ...)    Logger::Info(LOG_CAT, fmt, ##__VA_ARGS__)
#define LOG_ERROR(fmt, ...)   Logger::Error(LOG_CAT, fmt, ##__VA_ARGS__)
#define LOG_WARN(fmt, ...)    Logger::Warn(LOG_CAT, fmt, ##__VA_ARGS__)
#define LOG_DEBUG(fmt, ...)   Logger::Debug(LOG_CAT, fmt, ##__VA_ARGS__)
#define LOG_SUMMARY(fmt, ...) Logger::Summary(fmt, ##__VA_ARGS__)
