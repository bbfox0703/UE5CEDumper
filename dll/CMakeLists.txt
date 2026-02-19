cmake_minimum_required(VERSION 3.25)

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
