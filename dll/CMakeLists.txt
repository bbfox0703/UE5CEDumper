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

# ------------------------------------------------------------
# Keep the PE VERSIONINFO in sync with build_number.txt on incremental builds.
#
# version.rc embeds FileVersion / ProductVersion / GitCommit via
# #include "BuildInfo.h" (regenerated above on every configure — build number,
# git hash, timestamp). But Ninja does NOT scan .rc files for their #include
# dependencies the way it does .cpp (the RC compile rule emits no depfile), so a
# changed BuildInfo.h never retriggers the resource compile. With the build dir
# kept across runs (incremental Ninja), the DLL kept relinking the STALE
# version.res — its embedded version froze at the first kept-build (the .cpp TUs
# that include BuildInfo.h recompiled fine, so logs were current, but the PE
# version info lagged). OBJECT_DEPENDS adds the missing edge: the resource object
# explicitly depends on the generated header, so each build recompiles version.rc
# and the embedded version/git info stays in lockstep with the UI. Directory-
# scoped → applies to the main DLL and every proxy target listing src/version.rc.
# ------------------------------------------------------------
set_source_files_properties(src/version.rc PROPERTIES
    OBJECT_DEPENDS "${CMAKE_CURRENT_BINARY_DIR}/generated/BuildInfo.h")

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

# ============================================================
# Zydis — x64 instruction decoder (MIT, vendor submodule + nested zycore)
# Used by the Denken module to disassemble native UFunction machine code
# (Path 2 property xref). Built STATIC, decoder-only (no encoder/formatter)
# to keep the footprint small (~250KB). All variants (UE5Dumper + the two
# proxy DLLs + dll_helpers_test) link the same static lib.
#
# Cache vars must be FORCEd before add_subdirectory so Zydis's option()
# calls pick them up. Static CRT is forced on the Zydis/Zycore targets to
# match our /MT linkage (mismatched CRT = link error).
# ============================================================
set(ZYDIS_BUILD_SHARED_LIB  OFF CACHE BOOL "" FORCE)
set(ZYDIS_BUILD_TOOLS       OFF CACHE BOOL "" FORCE)
set(ZYDIS_BUILD_EXAMPLES    OFF CACHE BOOL "" FORCE)
set(ZYDIS_BUILD_TESTS       OFF CACHE BOOL "" FORCE)
set(ZYDIS_BUILD_DOXYGEN     OFF CACHE BOOL "" FORCE)
set(ZYDIS_FEATURE_DECODER   ON  CACHE BOOL "" FORCE)
set(ZYDIS_FEATURE_ENCODER   OFF CACHE BOOL "" FORCE)
set(ZYDIS_FEATURE_FORMATTER OFF CACHE BOOL "" FORCE)
set(ZYDIS_MINIMAL_MODE      OFF CACHE BOOL "" FORCE)
set(ZYAN_NO_LIBC            OFF CACHE BOOL "" FORCE)
add_subdirectory(${CMAKE_SOURCE_DIR}/vendor/zydis ${CMAKE_CURRENT_BINARY_DIR}/zydis_build EXCLUDE_FROM_ALL)

# Force static CRT + suppress third-party warnings on the Zydis/Zycore targets.
foreach(_zt Zydis Zycore)
    if(TARGET ${_zt})
        if(MSVC)
            set_property(TARGET ${_zt} PROPERTY
                MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>"
            )
            target_compile_options(${_zt} PRIVATE /wd4100 /wd4101 /wd4244 /wd4245 /wd4267)
        endif()
    endif()
endforeach()

# ============================================================
# Common build settings shared by every DLL-side C++ target
# (the shared object library, the main DLL, all three proxy DLLs).
# Defined ONCE so the four DLLs can never drift apart in flags or
# defines — that is precisely what makes their object code identical
# and therefore safe to compile once and share (see UE5DumperCommon).
# ============================================================
if(MSVC)
    # Drop the default /EHsc globally; we use /EHa for SEH (__try/__except).
    # Hoisted above all targets so every one of them inherits the cleaned flags.
    string(REPLACE "/EHsc" "" CMAKE_CXX_FLAGS "${CMAKE_CXX_FLAGS}")
endif()

set(UE5_COMMON_DEFS
    UNICODE
    _UNICODE
    WIN32_LEAN_AND_MEAN
    NOMINMAX
    _CRT_SECURE_NO_WARNINGS
)

set(UE5_COMMON_INCLUDE_DIRS
    ${CMAKE_CURRENT_SOURCE_DIR}/src
    ${CMAKE_CURRENT_BINARY_DIR}/generated   # BuildInfo.h
    ${CMAKE_SOURCE_DIR}/vendor/nlohmann
)

# Apply the identical MSVC compile flags every DLL-side C++ target uses. Keeping
# this in one function guarantees the shared object library and the four DLLs are
# compiled with byte-for-byte identical flags (a prerequisite for sharing objects).
function(ue5_apply_common_msvc_flags _tgt)
    if(MSVC)
        set_property(TARGET ${_tgt} PROPERTY
            MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>"
        )
        target_compile_options(${_tgt} PRIVATE
            /utf-8
            /W4
            /permissive-
            /EHa            # SEH exceptions for __try/__except
            /arch:AVX2      # enable AVX2 instruction-set optimization
            /favor:blend    # tune for modern hybrid-core CPUs
        )
    endif()
endfunction()

# ============================================================
# Shared object library — proxy-agnostic sources compiled ONCE
#
# These translation units never reference UE5_PROXY_BUILD or any of the per-proxy
# sub-defines, so the compiler emits identical object code for the main DLL and
# all three proxy DLLs. A CMake OBJECT library compiles them a SINGLE time; every
# DLL then links the same objects. This is the correct, incremental-safe form of
# "compile once, reuse the .obj in the other DLLs" — CMake owns the sharing and
# dependency tracking, so a source edit recompiles that one TU once and relinks
# the four DLLs (no manual .obj copying, which would silently desync on a flag
# change). OBJECT (not STATIC) is deliberate: every object is force-linked into
# each DLL, so dllexported C-ABI symbols (Frieren) are never dropped.
#
# INVARIANT: a file may live here ONLY if it never tests a UE5_PROXY_* macro.
# If you add such a test to one of these files, move it into the per-target
# source lists below (Heiter / Lugner*) instead, or the proxy DLLs will silently
# get the main-DLL behaviour.
# ============================================================
set(UE5_COMMON_SOURCES
    src/BuildStamp.cpp
    src/Macht.cpp
    src/Sein.cpp
    src/Genau.cpp
    src/Aura.cpp
    src/Serie.cpp
    src/Ubel.cpp
    src/Radar.cpp
    src/Frieren.cpp
    src/Fern.cpp
    src/Stark.cpp
    src/Mimic.cpp
    src/Flamme.cpp
    src/Denken.cpp
    src/Wirbel.cpp
    src/Solitar.cpp
    src/Laufen.cpp
    src/Hemmung.cpp
    src/Solide.cpp
    src/Dunste.cpp
    src/Edel.cpp
    src/Grausam.cpp
    src/Schlacht.cpp
    src/Linie.cpp
    src/Sense.cpp
)

add_library(UE5DumperCommon OBJECT ${UE5_COMMON_SOURCES})
target_include_directories(UE5DumperCommon PUBLIC ${UE5_COMMON_INCLUDE_DIRS})
target_compile_definitions(UE5DumperCommon PUBLIC ${UE5_COMMON_DEFS})
ue5_apply_common_msvc_flags(UE5DumperCommon)
# Zydis (Denken.cpp) + MinHook (Stark.cpp) are needed at COMPILE time for their
# headers. PRIVATE = the headers reach this lib's TUs without propagating the
# linked statics to consumers; each DLL links the actual Zydis/minhook statics
# itself (below) so the common objects' references resolve at the final link.
target_link_libraries(UE5DumperCommon PRIVATE minhook Zydis)

# ============================================================
# Main DLL: UE5Dumper.dll (Cheat-Engine-injected build)
#   Own sources: Heiter (no UE5_PROXY_BUILD) + Methode (CE exports, main-only)
#   Shared code: the UE5DumperCommon objects
# ============================================================
add_library(UE5Dumper SHARED
    src/Heiter.cpp
    src/Methode.cpp
    src/version.rc
    $<TARGET_OBJECTS:UE5DumperCommon>
)

target_include_directories(UE5Dumper PRIVATE ${UE5_COMMON_INCLUDE_DIRS})
target_compile_definitions(UE5Dumper PRIVATE ${UE5_COMMON_DEFS})
ue5_apply_common_msvc_flags(UE5Dumper)

target_link_libraries(UE5Dumper PRIVATE
    ws2_32
    Shlwapi
    Psapi
    Version   # GetFileVersionInfoW / VerQueryValueW for PE VERSIONINFO
    # NOTE: Winmm is deliberately NOT linked. Mimic resolves timeBeginPeriod /
    # timeEndPeriod from the System32 copy at run time instead, so that a future
    # winmm.dll PROXY build cannot have our own call resolve back into its own
    # forwarding stub (whose pre-resolution return of 0 == TIMERR_NOERROR would
    # silently no-op the 1ms tick). See the comment in Mimic.cpp.
    minhook
    Zydis     # native UFunction disassembly (Denken / Path 2)
)

# Output name without lib prefix
set_target_properties(UE5Dumper PROPERTIES
    PREFIX ""
    OUTPUT_NAME "UE5Dumper"
)

# ============================================================
# Proxy DLL targets — version.dll / dinput8.dll / dxgi.dll
#
# Each proxy shares the UE5DumperCommon objects and adds only its proxy-specific
# TUs: Heiter (compiled WITH UE5_PROXY_BUILD) + the Lugner forwarder shims (each
# gated by its own UE5_PROXY_*_BUILD sub-define, so they must be compiled per
# target). Excludes Methode.cpp (no CE SDK dependency). Output renamed to the
# hijack target.
#
# Why version.dll? Almost every Windows process loads it (GetFileVersionInfo).
# dinput8.dll only works for games that use DirectInput8 (most modern UE5 games
# do not). dxgi.dll is statically imported by every D3D11/D3D12 UE game and is
# the reliable hijack for EXEs that import neither of the other two.
#
# Only one proxy DLL should be active per game folder; if the user deploys more
# than one, the runtime mutex in Heiter.cpp ensures only the first to load runs
# full init.
# ============================================================

# Proxy-specific sources (everything proxy-flavoured that is NOT in the shared
# object library). Both Lugner.cpp (version forwarders) and Lugner_Dinput8.cpp
# (dinput8 forwarders) are listed for every proxy; each is gated by its own
# #ifdef so only the active target's forwarders produce code.
set(PROXY_SPECIFIC_SOURCES
    src/Heiter.cpp
    src/Lugner.cpp
    src/Lugner_Dinput8.cpp
    src/version.rc
)

# Winmm is deliberately absent — see the note on UE5Dumper's link list above.
# It matters most HERE: one of these targets will eventually BE winmm.dll.
set(PROXY_LINK_LIBS
    ws2_32 Shlwapi Psapi Version delayimp minhook Zydis
)

option(BUILD_PROXY_DLL "Build the proxy DLL (version.dll) target" OFF)
if(BUILD_PROXY_DLL)
    add_library(UE5Dumper_Proxy SHARED
        ${PROXY_SPECIFIC_SOURCES}
        $<TARGET_OBJECTS:UE5DumperCommon>
    )

    target_include_directories(UE5Dumper_Proxy PRIVATE ${UE5_COMMON_INCLUDE_DIRS})
    target_compile_definitions(UE5Dumper_Proxy PRIVATE
        ${UE5_COMMON_DEFS}
        UE5_PROXY_BUILD
        UE5_PROXY_VERSION_BUILD
    )
    ue5_apply_common_msvc_flags(UE5Dumper_Proxy)
    target_link_libraries(UE5Dumper_Proxy PRIVATE ${PROXY_LINK_LIBS})

    # Delay-load version.dll to break the circular dependency: the proxy DLL IS
    # version.dll, so it can't be in the import table at load time. MSVC delay-load
    # resolves it at first call → goes through our proxy exports.
    target_link_options(UE5Dumper_Proxy PRIVATE
        /DELAYLOAD:version.dll
        /DEF:${CMAKE_CURRENT_SOURCE_DIR}/src/ProxyVersion.def
    )

    set_target_properties(UE5Dumper_Proxy PROPERTIES
        PREFIX ""
        OUTPUT_NAME "version"
    )
endif()

option(BUILD_PROXY_DINPUT8 "Build the dinput8.dll proxy DLL target" OFF)
if(BUILD_PROXY_DINPUT8)
    add_library(UE5Dumper_ProxyDinput8 SHARED
        ${PROXY_SPECIFIC_SOURCES}
        $<TARGET_OBJECTS:UE5DumperCommon>
    )

    target_include_directories(UE5Dumper_ProxyDinput8 PRIVATE ${UE5_COMMON_INCLUDE_DIRS})
    target_compile_definitions(UE5Dumper_ProxyDinput8 PRIVATE
        ${UE5_COMMON_DEFS}
        UE5_PROXY_BUILD
        UE5_PROXY_DINPUT8_BUILD
    )
    ue5_apply_common_msvc_flags(UE5Dumper_ProxyDinput8)
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

option(BUILD_PROXY_DXGI "Build the dxgi.dll proxy DLL target" OFF)
if(BUILD_PROXY_DXGI)
    # MASM is only needed for this target's jmp-thunks; enable it lazily so the
    # version/dinput8 proxy configures don't pay for it.
    enable_language(ASM_MASM)

    # Assemble the jmp-thunks in an ISOLATED object library that links nothing.
    # The main proxy target links Zydis, which exports a PUBLIC /wd4201 compile
    # option; target compile options apply to ALL languages, so that CL-only flag
    # would reach ml64 and trigger "MASM warning A4018: invalid command-line
    # option". Keeping the .asm in a dependency-free object lib (then folding its
    # objects in via $<TARGET_OBJECTS>) keeps ml64's command line clean. f0..f19
    # resolve against mProcs (in Lugner_Dxgi.cpp) at the final shared-lib link.
    add_library(UE5Dumper_ProxyDxgi_Thunks OBJECT src/Lugner_Dxgi.asm)

    add_library(UE5Dumper_ProxyDxgi SHARED
        ${PROXY_SPECIFIC_SOURCES}
        src/Lugner_Dxgi.cpp
        $<TARGET_OBJECTS:UE5DumperCommon>
        $<TARGET_OBJECTS:UE5Dumper_ProxyDxgi_Thunks>
    )

    target_include_directories(UE5Dumper_ProxyDxgi PRIVATE ${UE5_COMMON_INCLUDE_DIRS})
    target_compile_definitions(UE5Dumper_ProxyDxgi PRIVATE
        ${UE5_COMMON_DEFS}
        UE5_PROXY_BUILD
        UE5_PROXY_DXGI_BUILD
    )

    if(MSVC)
        set_property(TARGET UE5Dumper_ProxyDxgi PROPERTY
            MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>"
        )
        # /utf-8 /W4 etc. apply to C/C++ only; MASM (.asm) ignores them. The
        # COMPILE_LANGUAGE guard keeps these CL-only flags off ml64's command line.
        target_compile_options(UE5Dumper_ProxyDxgi PRIVATE
            $<$<COMPILE_LANGUAGE:CXX>:/utf-8>
            $<$<COMPILE_LANGUAGE:CXX>:/W4>
            $<$<COMPILE_LANGUAGE:CXX>:/permissive->
            $<$<COMPILE_LANGUAGE:CXX>:/EHa>
            $<$<COMPILE_LANGUAGE:CXX>:/arch:AVX2>
            $<$<COMPILE_LANGUAGE:CXX>:/favor:blend>
        )
    endif()

    target_link_libraries(UE5Dumper_ProxyDxgi PRIVATE ${PROXY_LINK_LIBS})

    # No /DELAYLOAD here: unlike the version/dinput8 proxies, this DLL does NOT
    # import dxgi.dll (PROXY_LINK_LIBS has no dxgi), so there is no circular
    # import to break — we load the real dxgi manually by full path.
    target_link_options(UE5Dumper_ProxyDxgi PRIVATE
        /DEF:${CMAKE_CURRENT_SOURCE_DIR}/src/ProxyDxgi.def
    )

    set_target_properties(UE5Dumper_ProxyDxgi PROPERTIES
        PREFIX ""
        OUTPUT_NAME "dxgi"
    )
endif()

option(BUILD_PROXY_WINMM "Build the winmm.dll proxy DLL target" OFF)
if(BUILD_PROXY_WINMM)
    # Structurally identical to the dxgi target above — see its comments for why
    # the thunks live in an isolated object library and why there is no
    # /DELAYLOAD. The three winmm artefacts (.cpp/.asm/.def) are GENERATED by
    # scripts/gen_proxy_forwarders.py from the real System32 winmm.dll; with 180
    # exports they must never be hand-edited. Re-run the generator instead
    # (`--check` verifies they are current).
    enable_language(ASM_MASM)

    add_library(UE5Dumper_ProxyWinmm_Thunks OBJECT src/Lugner_Winmm.asm)

    add_library(UE5Dumper_ProxyWinmm SHARED
        ${PROXY_SPECIFIC_SOURCES}
        src/Lugner_Winmm.cpp
        $<TARGET_OBJECTS:UE5DumperCommon>
        $<TARGET_OBJECTS:UE5Dumper_ProxyWinmm_Thunks>
    )

    target_include_directories(UE5Dumper_ProxyWinmm PRIVATE ${UE5_COMMON_INCLUDE_DIRS})
    target_compile_definitions(UE5Dumper_ProxyWinmm PRIVATE
        ${UE5_COMMON_DEFS}
        UE5_PROXY_BUILD
        UE5_PROXY_WINMM_BUILD
    )

    if(MSVC)
        set_property(TARGET UE5Dumper_ProxyWinmm PROPERTY
            MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>"
        )
        target_compile_options(UE5Dumper_ProxyWinmm PRIVATE
            $<$<COMPILE_LANGUAGE:CXX>:/utf-8>
            $<$<COMPILE_LANGUAGE:CXX>:/W4>
            $<$<COMPILE_LANGUAGE:CXX>:/permissive->
            $<$<COMPILE_LANGUAGE:CXX>:/EHa>
            $<$<COMPILE_LANGUAGE:CXX>:/arch:AVX2>
            $<$<COMPILE_LANGUAGE:CXX>:/favor:blend>
        )
    endif()

    # PROXY_LINK_LIBS deliberately has no Winmm — this DLL IS winmm.dll. Mimic
    # resolves timeBeginPeriod/timeEndPeriod from System32 by explicit path for
    # exactly this reason (build 2301); a static import would resolve back into
    # our own forwarding stub, whose pre-resolution return of 0 == TIMERR_NOERROR
    # would silently no-op the 1 ms tick.
    target_link_libraries(UE5Dumper_ProxyWinmm PRIVATE ${PROXY_LINK_LIBS})

    target_link_options(UE5Dumper_ProxyWinmm PRIVATE
        /DEF:${CMAKE_CURRENT_SOURCE_DIR}/src/ProxyWinmm.def
    )

    set_target_properties(UE5Dumper_ProxyWinmm PROPERTIES
        PREFIX ""
        OUTPUT_NAME "winmm"
    )
endif()

# ============================================================
# Unit test targets — Utf8Helpers + pure-helper self-tests
#
# Stand-alone executables, no GoogleTest dependency. Built in the SAME (unified)
# binary directory as the DLLs. Gated by an explicit option (default ON) instead
# of the old "proxy targets are off" heuristic, because the unified configure now
# enables the proxy targets too. build.ps1 -Target Test runs the executables
# before the C# tests; a non-zero exit fails the test phase.
#
# Why separate targets instead of folding into UE5Dumper? The DLL requires Win32 /
# MinHook / nlohmann linkage that the test logic doesn't need. Keeping the tests
# minimal means a regression in the DLL build doesn't block the tests from running.
# They compile their own copies of Radar.cpp / Denken.cpp (a couple of extra TUs)
# rather than linking the object library, to keep the leaf test self-contained.
# ============================================================
option(BUILD_DLL_TESTS "Build the C++ self-test executables" ON)
if(BUILD_DLL_TESTS)
    add_executable(utf8_helpers_test
        ${CMAKE_CURRENT_SOURCE_DIR}/tests/utf8_helpers_test.cpp
    )

    target_include_directories(utf8_helpers_test PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/src
    )

    if(MSVC)
        set_property(TARGET utf8_helpers_test PROPERTY
            MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>"
        )
        target_compile_options(utf8_helpers_test PRIVATE
            /utf-8 /W4 /permissive- /EHa
        )
    endif()

    # Pure-helper tests (Renge::TryStrToAddr, Scharf::IsAlignmentSuspicious,
    # Radar typed-read + predicates + session lifecycle).
    # Same harness style as utf8_helpers_test — exit code = failure count.
    # Pulls in nlohmann/json since Renge.h needs it (header-only, no link).
    # Radar.cpp is std-only so it compiles cleanly into the leaf test.
    # Denken.cpp is a pure Zydis-only decoder core (memory access goes through
    # a reader callback supplied by the caller), so it links into the leaf test
    # without pulling in Macht / Win32 / the rest of the DLL.
    add_executable(dll_helpers_test
        ${CMAKE_CURRENT_SOURCE_DIR}/tests/dll_helpers_test.cpp
        ${CMAKE_CURRENT_SOURCE_DIR}/src/Radar.cpp
        ${CMAKE_CURRENT_SOURCE_DIR}/src/Denken.cpp
    )

    target_include_directories(dll_helpers_test PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/src
        ${CMAKE_SOURCE_DIR}/vendor/nlohmann
    )

    if(MSVC)
        set_property(TARGET dll_helpers_test PROPERTY
            MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>"
        )
        target_compile_options(dll_helpers_test PRIVATE
            /utf-8 /W4 /permissive- /EHa
        )
    endif()

    # Winmm deliberately absent: nothing in this target needs it. Mimic resolves
    # timeBeginPeriod / timeEndPeriod from the System32 copy by explicit path so a
    # future winmm.dll PROXY build cannot have the call resolve into its own
    # forwarding stub, and nothing in the tree statically imports winmm.
    #
    # This comment used to claim the test target "covers the real mechanism" of
    # that resolution. It never did (audit #5 AD6): the test re-implemented the
    # resolution inside itself and measured the host's timer tick, which is
    # invariant to every line in dll/src. Mimic.cpp is still not compiled here —
    # what the target now covers is Mimic.h's DATA surface, the mailbox layout and
    # command numbering that CE Lua bakes as literals.
    target_link_libraries(dll_helpers_test PRIVATE Zydis)

    # ---- grausam_window_test: DLL LOW L10, the window re-subclass half -----------------
    #
    # A SEPARATE target, not another source on dll_helpers_test, and deliberately so: this
    # one #includes Grausam.cpp to reach its anonymous namespace, and it CREATES REAL
    # WINDOWS. Folding it in would give the leaf helper test a user32 message queue and a
    # MinHook dependency it has no business carrying.
    #
    # minhook is MANDATORY, not incidental: Grausam.cpp's SetForegroundLock has external
    # linkage and references MH_Initialize / MH_StatusToString / MH_CreateHook /
    # MH_EnableHook / MH_RemoveHook, so the TU does not link without it -- even though the
    # test never installs a hook. Sein is satisfied by two stub definitions inside the test
    # TU rather than by compiling Sein.cpp, which would drag in the whole logging stack.
    add_executable(grausam_window_test
        ${CMAKE_CURRENT_SOURCE_DIR}/tests/grausam_window_test.cpp
    )

    target_include_directories(grausam_window_test PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/src
    )

    if(MSVC)
        set_property(TARGET grausam_window_test PROPERTY
            MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>"
        )
        # /W4 without /permissive- : Grausam.cpp is compiled here as an INCLUDED source and
        # its warning posture is the DLL's, not this target's, to argue with.
        target_compile_options(grausam_window_test PRIVATE /utf-8 /W4 /EHsc)
    endif()

    target_link_libraries(grausam_window_test PRIVATE minhook user32)

    # ---- sein_retention_test: AB9, the log retention sweep -----------------------------
    #
    # #includes Sein.cpp to reach its file statics (s_logDir / s_processDir /
    # s_processDirReady / s_sweepDone), which is the only way to point the sweep at a
    # fixture without adding a test-only hook to shipping code.
    #
    # WARNING: this target drives the DLL's only recursive delete. The test carries a
    # safety gate that refuses to run unless its root is under %TEMP%, pid-named, and
    # carrying a sentinel it wrote -- a fixture bug aimed at the real log tree under
    # LOCALAPPDATA would destroy the log corpus. Do not weaken it.
    #
    # shell32/ole32 (SHGetKnownFolderPath) come from MSVC's default
    # CMAKE_CXX_STANDARD_LIBRARIES, so no extra link config is needed.
    add_executable(sein_retention_test
        ${CMAKE_CURRENT_SOURCE_DIR}/tests/sein_retention_test.cpp
    )

    target_include_directories(sein_retention_test PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/src
    )

    if(MSVC)
        set_property(TARGET sein_retention_test PROPERTY
            MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>"
        )
        target_compile_options(sein_retention_test PRIVATE /utf-8 /W4 /EHsc)
    endif()

    # ---- dll_core_test: the FIRST test target that compiles the DLL's core --------------
    #
    # ⭐ WHY THIS EXISTS. Until now NO test target compiled Aura.cpp, Ubel.cpp, Genau.cpp,
    # Macht.cpp or Serie.cpp -- 23,000 lines including every GObjects walk, the class
    # walker and the offset finder. Several audit findings were shipped unverifiable for
    # exactly that reason, and A10 was declined partly on it.
    #
    # It builds by INCLUDING the .cpp files into one TU, so a test can reach file statics
    # and drive them against a fake object pool built in the TEST PROCESS's own memory --
    # Macht reads the current process, so no game is needed.
    #
    # THE EXTERNAL SURFACE, measured rather than guessed (an incremental link probe):
    #   * Sein  -- 5 varargs log functions, stubbed in the test TU. Compiling Sein.cpp
    #              would drag in the log tree and fight sein_retention_test over it.
    #   * Stark::SetInvokeTimeoutMs -- one setter, stubbed; Stark.cpp is the MinHook
    #              ProcessEvent hook and has no place in a headless test.
    #   * g_cachedUEVersion -- defined in Frieren.cpp (the C ABI layer). Defined in the
    #              test instead, which is a FEATURE: the test picks the UE version.
    #   * Zydis (Denken) and version.lib (Genau's PE version-info reads).
    add_executable(dll_core_test
        ${CMAKE_CURRENT_SOURCE_DIR}/tests/dll_core_test.cpp
    )

    target_include_directories(dll_core_test PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/src
        ${CMAKE_SOURCE_DIR}/vendor/nlohmann
    )

    target_compile_definitions(dll_core_test PRIVATE ${UE5_COMMON_DEFS})

    if(MSVC)
        set_property(TARGET dll_core_test PROPERTY
            MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>"
        )
        # /W3, not /W4: these are PRODUCTION sources compiled as an included TU, and their
        # warning posture is the DLL's to argue with, not this target's.
        target_compile_options(dll_core_test PRIVATE /utf-8 /W3 /EHsc /bigobj)
    endif()

    target_link_libraries(dll_core_test PRIVATE Zydis version)
endif()
