cmake_minimum_required(VERSION 3.25)

# ============================================================
# Product version (major.minor.patch) — shared with UI project
# ============================================================
set(VER_MAJOR 1)
set(VER_MINOR 0)
set(VER_PATCH 0)

# ============================================================
# Build number (4th digit) — read from shared file
# Incremented by build.ps1 once per build, NOT by CMake.
# Both DLL and ProxyDLL configure share this file, so incrementing
# here would cause the DLL to get N and ProxyDLL to get N+1.
# ============================================================
set(BUILD_NUMBER_FILE "${CMAKE_SOURCE_DIR}/build_number.txt")
if(EXISTS "${BUILD_NUMBER_FILE}")
    file(READ "${BUILD_NUMBER_FILE}" VER_BUILD)
    string(STRIP "${VER_BUILD}" VER_BUILD)
else()
    set(VER_BUILD 0)
endif()
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
    ERROR_QUIET
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

# ============================================================
# MinHook — inline hooking library (MIT, vendor submodule)
# Built as a separate static C library to avoid C/C++ linkage
# issues (MSVC /TP forces C++ on .c files in C++ targets).
# ============================================================
enable_language(C)

add_library(minhook STATIC
    ${CMAKE_SOURCE_DIR}/vendor/minhook/src/buffer.c
    ${CMAKE_SOURCE_DIR}/vendor/minhook/src/hook.c
    ${CMAKE_SOURCE_DIR}/vendor/minhook/src/trampoline.c
    ${CMAKE_SOURCE_DIR}/vendor/minhook/src/hde/hde64.c
)

target_include_directories(minhook PUBLIC
    ${CMAKE_SOURCE_DIR}/vendor/minhook/include
)

if(MSVC)
    set_property(TARGET minhook PROPERTY
        MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>"
    )
    target_compile_options(minhook PRIVATE /W3 /utf-8)
endif()

add_library(UE5Dumper SHARED
    src/Heiter.cpp
    src/Methode.cpp
    src/Macht.cpp
    src/Sein.cpp
    src/Genau.cpp
    src/Aura.cpp
    src/Serie.cpp
    src/Ubel.cpp
    src/Frieren.cpp
    src/Fern.cpp
    src/Stark.cpp
    src/Mimic.cpp
    src/Flamme.cpp
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
    minhook
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

# Shared source list for both proxy DLL targets.
# Includes BOTH Lugner.cpp (version.dll forwarders) and Lugner_Dinput8.cpp
# (dinput8.dll forwarders); each is gated by its own #ifdef so only the
# active target's forwarders compile.
set(PROXY_SOURCES
    src/Heiter.cpp
    src/Lugner.cpp
    src/Lugner_Dinput8.cpp
    src/Macht.cpp
    src/Sein.cpp
    src/Genau.cpp
    src/Aura.cpp
    src/Serie.cpp
    src/Ubel.cpp
    src/Frieren.cpp
    src/Fern.cpp
    src/Stark.cpp
    src/Mimic.cpp
    src/Flamme.cpp
    src/version.rc
)

set(PROXY_COMMON_DEFS
    UNICODE
    _UNICODE
    WIN32_LEAN_AND_MEAN
    NOMINMAX
    _CRT_SECURE_NO_WARNINGS
    UE5_PROXY_BUILD
)

set(PROXY_INCLUDE_DIRS
    src
    ${CMAKE_CURRENT_BINARY_DIR}/generated
    ${CMAKE_SOURCE_DIR}/vendor/nlohmann
)

set(PROXY_LINK_LIBS
    ws2_32 Shlwapi Psapi Version delayimp minhook
)

if(BUILD_PROXY_DLL)
    add_library(UE5Dumper_Proxy SHARED ${PROXY_SOURCES})

    target_include_directories(UE5Dumper_Proxy PRIVATE ${PROXY_INCLUDE_DIRS})

    target_compile_definitions(UE5Dumper_Proxy PRIVATE
        ${PROXY_COMMON_DEFS}
        UE5_PROXY_VERSION_BUILD
    )

    if(MSVC)
        set_property(TARGET UE5Dumper_Proxy PROPERTY
            MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>"
        )
        target_compile_options(UE5Dumper_Proxy PRIVATE
            /utf-8 /W4 /permissive- /EHa /arch:AVX2 /favor:blend
        )
    endif()

    target_link_libraries(UE5Dumper_Proxy PRIVATE ${PROXY_LINK_LIBS})

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

# ============================================================
# Proxy DLL target (alternative): dinput8.dll
# Same architecture as version.dll proxy, but hijacks dinput8.dll
# instead. Useful when version.dll hijacking conflicts with the
# game's installer / anti-tamper shim.
#
# Only one proxy DLL should be active per game folder; if the user
# accidentally deploys both, the runtime mutex in Heiter.cpp ensures
# only the first to load runs full init.
# ============================================================
option(BUILD_PROXY_DINPUT8 "Build the dinput8.dll proxy DLL target" OFF)

if(BUILD_PROXY_DINPUT8)
    add_library(UE5Dumper_ProxyDinput8 SHARED ${PROXY_SOURCES})

    target_include_directories(UE5Dumper_ProxyDinput8 PRIVATE ${PROXY_INCLUDE_DIRS})

    target_compile_definitions(UE5Dumper_ProxyDinput8 PRIVATE
        ${PROXY_COMMON_DEFS}
        UE5_PROXY_DINPUT8_BUILD
    )

    if(MSVC)
        set_property(TARGET UE5Dumper_ProxyDinput8 PROPERTY
            MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>"
        )
        target_compile_options(UE5Dumper_ProxyDinput8 PRIVATE
            /utf-8 /W4 /permissive- /EHa /arch:AVX2 /favor:blend
        )
    endif()

    target_link_libraries(UE5Dumper_ProxyDinput8 PRIVATE ${PROXY_LINK_LIBS})

    # Delay-load dinput8.dll for the same reason as the version.dll proxy:
    # we ARE dinput8.dll, so it can't be in the import table at load time.
    target_link_options(UE5Dumper_ProxyDinput8 PRIVATE
        /DELAYLOAD:dinput8.dll
        /DEF:${CMAKE_CURRENT_SOURCE_DIR}/src/ProxyDinput8.def
    )

    set_target_properties(UE5Dumper_ProxyDinput8 PROPERTIES
        PREFIX ""
        OUTPUT_NAME "dinput8"
    )
endif()
