#pragma once

// ============================================================
// BuildStamp.h — stable accessors for the auto-generated build / version
// metadata (BuildInfo.h).
//
// Why this exists: BuildInfo.h is regenerated on every CMake configure (the
// build number bumps and the timestamp changes each build), so any TU that
// includes it recompiles on EVERY build. Routing consumers through these
// out-of-line accessors keeps the volatile macros confined to a single tiny TU
// (BuildStamp.cpp) plus the resource script (version.rc). A build-number bump
// then recompiles only that small file — not the heavy consumers.
// ============================================================
namespace BuildStamp {
    // "1.0.0.1812 abc1234 (Release, 2026-...)" — BUILD_VERSION_STRING
    const char* VersionString();
    // Short git hash (may carry a "-dirty" suffix) — BUILD_GIT_SHORT
    const char* GitShort();
    // Full 40-char git commit hash — BUILD_GIT_HASH
    const char* GitHash();
    // ISO-8601 build timestamp — BUILD_TIMESTAMP
    const char* Timestamp();
    // Build configuration ("Release" / "Debug") — BUILD_CONFIG
    const char* Config();
    // Numeric build number (4th version digit) — VER_BUILD
    int         BuildNumber();
}
