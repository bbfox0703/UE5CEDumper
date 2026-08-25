#pragma once

// ============================================================
// Sein — 贊恩 (僧侶・記錄者 — Priest, Chronicler)
// Logger: 5-category per-process file logging with rotation
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

namespace Sein {

// Initialize the logger: creates log directory, enables early buffering.
// Actual log files are opened in InitProcessMirror().
bool Init();

// Open category log files in a per-process subfolder.
// Call AFTER Init() and after DLL determines the host process name.
// Creates <logDir>/<processName>/ with 5 category files; the previous run's
// -0.log of each is archived to <base>-YYYYMMDD-HHMMSS.log.
// Flushes any early-buffered lines to the correct files.
// Does NOT run the retention sweep — call RunRetentionSweep() for that.
void InitProcessMirror(const std::wstring& processName);

// Delete archived logs, and whole per-process folders, untouched for
// Grimoire::LOG_RETENTION_DAYS. Retention is by AGE, not by file or folder count
// — see the retention block in Sein.cpp for why a count could not express it.
//
// SPLIT OUT of InitProcessMirror for AB9: it ran inline under the LOADER LOCK, and
// it is unbounded work — two directory sweeps plus a recursive remove_all over a
// tree that grows with every game ever run. Measured over 407 real sessions in the
// 3,023-file / 32-folder log corpus on this machine, the DllMain window that
// CONTAINS it (`UE5Dumper DLL loaded` -> `auto-start thread created OK`) has a
// median of 138 ms and a max of 475 ms, against a floor of 15 ms on a small tree.
// Every DLL_PROCESS_ATTACH in the process is serialized behind that.
//
// CONTRACT:
//   * Idempotent, and latched: only the FIRST call does work.
//   * Safe from any thread, but only AFTER InitProcessMirror has created the
//     per-process folder; a call before that logs and returns without touching
//     anything.
//   * Takes the log mutex only to COPY the two paths, then releases it and sweeps
//     unlocked — holding it across the sweep would merely move the stall from the
//     loader lock onto every logging thread.
//
// ⚠ CALLER OWNS THE THREADING DECISION, and it is not free: AB1 forbids creating a
// thread in a CE plugin host, where the module is deliberately NOT pinned
// (Heiter.cpp:399 — CE FreeLibrary's its plugins, and a thread in an unmapped image
// takes CE down). So DllMain calls this INLINE there and on a thread elsewhere.
void RunRetentionSweep();

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

} // namespace Sein

// Convenience macros — each source file should #define LOG_CAT before
// including this header (or before using the macros).
// Example:  #define LOG_CAT "SCAN:GObj"
//
// If LOG_CAT is not defined, defaults to "" (routed to init.log).
#ifndef LOG_CAT
#define LOG_CAT ""
#endif

#define LOG_INFO(fmt, ...)    Sein::Info(LOG_CAT, fmt, ##__VA_ARGS__)
#define LOG_ERROR(fmt, ...)   Sein::Error(LOG_CAT, fmt, ##__VA_ARGS__)
#define LOG_WARN(fmt, ...)    Sein::Warn(LOG_CAT, fmt, ##__VA_ARGS__)
#define LOG_DEBUG(fmt, ...)   Sein::Debug(LOG_CAT, fmt, ##__VA_ARGS__)
#define LOG_SUMMARY(fmt, ...) Sein::Summary(fmt, ##__VA_ARGS__)
