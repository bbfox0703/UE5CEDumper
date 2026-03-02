cmake_minimum_required(VERSION 3.25)

# ============================================================
# Product version (major.minor.patch) — shared with UI project
# ============================================================
set(VER_MAJOR 1)
set(VER_MINOR 0)
set(VER_PATCH 0)

# ============================================================
# Auto-increment build number (4th digit) — persisted in file
# Incremented on every cmake configure (i.e. every build)
# ============================================================
set(BUILD_NUMBER_FILE "${CMAKE_SOURCE_DIR}/build_number.txt")
if(EXISTS "${BUILD_NUMBER_FILE}")
    file(READ "${BUILD_NUMBER_FILE}" VER_BUILD)
    string(STRIP "${VER_BUILD}" VER_BUILD)
else()
    set(VER_BUILD 0)
endif()
math(EXPR VER_BUILD "${VER_BUILD} + 1")
file(WRITE "${BUILD_NUMBER_FILE}" "${VER_BUILD}\n")
message(STATUS "Version: ${VER_MAJOR}.${VER_MINOR}.${VER_PATCH}.${VER_BUILD}")

# ============================================================
# Git commit hash extraction — embedded into DLL at build time
# ============================================================
execute_process(
    COMMAND git rev-parse HEAD
    WORKING_DIRECTORY ${CMAKE_SOURCE_DIR}
    OUTPUT_VARIABLE GIT_COMMIT_HASH
    OUTPUT_STRIP_TRAILING_WHITESPACE
    ERROR_QUIET
)
execute_process(
    COMMAND git rev-parse --short HEAD
    WORKING_DIRECTORY ${CMAKE_SOURCE_DIR}
    OUTPUT_VARIABLE GIT_COMMIT_SHORT
    OUTPUT_STRIP_TRAILING_WHITESPACE
    ERROR_QUIET
)
execute_process(
    COMMAND git diff --quiet HEAD
    WORKING_DIRECTORY ${CMAKE_SOURCE_DIR}
    RESULT_VARIABLE GIT_DIRTY_RESULT
)
if(GIT_DIRTY_RESULT EQUAL 0)
    set(GIT_IS_DIRTY 0)
else()
    set(GIT_IS_DIRTY 1)
    string(APPEND GIT_COMMIT_SHORT "-dirty")
endif()

# Build timestamp (ISO 8601)
string(TIMESTAMP BUILD_TIMESTAMP "%Y-%m-%dT%H:%M:%S" UTC)

message(STATUS "Git: ${GIT_COMMIT_SHORT} (${GIT_COMMIT_HASH})")
message(STATUS "Build: ${CMAKE_BUILD_TYPE} @ ${BUILD_TIMESTAMP}")

# Generate BuildInfo.h from template
configure_file(
    ${CMAKE_CURRENT_SOURCE_DIR}/src/BuildInfo.h.in
    ${CMAKE_CURRENT_BINARY_DIR}/generated/BuildInfo.h
    @ONLY
)

add_library(UE5Dumper SHARED
    src/dllmain.cpp
    src/CEPlugin.cpp
    src/Memory.cpp
    src/Logger.cpp
    src/OffsetFinder.cpp
    src/ObjectArray.cpp
    src/FNamePool.cpp
    src/UStructWalker.cpp
    src/ExportAPI.cpp
    src/PipeServer.cpp
    src/version.rc
)

target_include_directories(UE5Dumper PRIVATE
    src
    ${CMAKE_CURRENT_BINARY_DIR}/generated   # BuildInfo.h
    ${CMAKE_SOURCE_DIR}/vendor/nlohmann
)

target_compile_definitions(UE5Dumper PRIVATE
    UNICODE
    _UNICODE
    WIN32_LEAN_AND_MEAN
    NOMINMAX
    _CRT_SECURE_NO_WARNINGS
)

# MSVC-specific flags
if(MSVC)
    # Static CRT linkage
    set_property(TARGET UE5Dumper PROPERTY
        MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>"
    )

    # Remove default /EHsc to avoid conflict with /EHa
    string(REPLACE "/EHsc" "" CMAKE_CXX_FLAGS "${CMAKE_CXX_FLAGS}")

    target_compile_options(UE5Dumper PRIVATE
        /utf-8
        /W4
        /permissive-
        /EHa        # Enable SEH exceptions for __try/__except
		/arch:AVX2          # 啟用 AVX2 指令集最佳化
        /favor:blend        # 針對現代 CPU 混合架構最佳化		
    )
endif()

target_link_libraries(UE5Dumper PRIVATE
    ws2_32
    Shlwapi
    Psapi
    Version   # GetFileVersionInfoW / VerQueryValueW for PE VERSIONINFO
)

# Output name without lib prefix
set_target_properties(UE5Dumper PROPERTIES
    PREFIX ""
    OUTPUT_NAME "UE5Dumper"
)

# ============================================================
# Proxy DLL target — builds as version.dll for direct game-folder injection
# Same source files as UE5Dumper, but:
#   - Excludes CEPlugin.cpp (no CE SDK dependency)
#   - Includes ProxyVersion.cpp (version.dll export forwarding)
#   - Defines UE5_PROXY_BUILD (changes auto-start behavior)
#   - Output: version.dll
#
# Why version.dll? Almost every Windows process loads it (via
# GetFileVersionInfo calls). dinput8.dll only works for games
# that use DirectInput8 — most modern UE5 games do not.
# ============================================================
option(BUILD_PROXY_DLL "Build the proxy DLL (version.dll) target" OFF)

if(BUILD_PROXY_DLL)
    add_library(UE5Dumper_Proxy SHARED
        src/dllmain.cpp
        src/ProxyVersion.cpp
        src/Memory.cpp
        src/Logger.cpp
        src/OffsetFinder.cpp
        src/ObjectArray.cpp
        src/FNamePool.cpp
        src/UStructWalker.cpp
        src/ExportAPI.cpp
        src/PipeServer.cpp
        src/version.rc
    )

    target_include_directories(UE5Dumper_Proxy PRIVATE
        src
        ${CMAKE_CURRENT_BINARY_DIR}/generated
        ${CMAKE_SOURCE_DIR}/vendor/nlohmann
    )

    target_compile_definitions(UE5Dumper_Proxy PRIVATE
        UNICODE
        _UNICODE
        WIN32_LEAN_AND_MEAN
        NOMINMAX
        _CRT_SECURE_NO_WARNINGS
        UE5_PROXY_BUILD
    )

    if(MSVC)
        set_property(TARGET UE5Dumper_Proxy PROPERTY
            MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>"
        )
        target_compile_options(UE5Dumper_Proxy PRIVATE
            /utf-8 /W4 /permissive- /EHa /arch:AVX2 /favor:blend
        )
    endif()

    target_link_libraries(UE5Dumper_Proxy PRIVATE
        ws2_32 Shlwapi Psapi Version delayimp
    )

    # Delay-load version.dll to avoid circular dependency: the proxy DLL
    # IS version.dll, so we can't have it in the import table at load time.
    # MSVC delay-load resolves it at first call → goes through our proxy exports.
    target_link_options(UE5Dumper_Proxy PRIVATE
        /DELAYLOAD:version.dll
        /DEF:${CMAKE_CURRENT_SOURCE_DIR}/src/ProxyVersion.def
    )

    set_target_properties(UE5Dumper_Proxy PROPERTIES
        PREFIX ""
        OUTPUT_NAME "version"
    )
endif()
