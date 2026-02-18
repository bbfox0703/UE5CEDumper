#pragma once

// ============================================================
// Logger.h — File logger with rotation
// ============================================================

#include <cstdint>
#include <string>

namespace Logger {

// Initialize logging, rotate old log files
bool Init();

// Shutdown, flush and close
void Shutdown();

// Log message at various levels
void Info(const char* fmt, ...);
void Error(const char* fmt, ...);
void Warn(const char* fmt, ...);
void Debug(const char* fmt, ...);

} // namespace Logger

// Convenience macros
#define LOG_INFO(fmt, ...)  Logger::Info(fmt, ##__VA_ARGS__)
#define LOG_ERROR(fmt, ...) Logger::Error(fmt, ##__VA_ARGS__)
#define LOG_WARN(fmt, ...)  Logger::Warn(fmt, ##__VA_ARGS__)
#define LOG_DEBUG(fmt, ...) Logger::Debug(fmt, ##__VA_ARGS__)
