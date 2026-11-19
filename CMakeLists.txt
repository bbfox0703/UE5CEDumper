cmake_minimum_required(VERSION 3.25)
project(UE5CEDumper LANGUAGES CXX)

# The DLL is MSVC code (SEH, MSVC pragmas, MSVC link options), built and tested with cl.exe only. GCC would configure
# with every if(MSVC) block skipped, clang-cl (which CMake also marks MSVC) with them applied to another compiler, and
# either would fail far from the cause, so refuse both here. [MSVC-GUARD-2026-11-19]
if(NOT CMAKE_CXX_COMPILER_ID STREQUAL "MSVC")
    message(FATAL_ERROR
        "UE5CEDumper builds with MSVC (cl.exe) only, but this configure found ${CMAKE_CXX_COMPILER_ID} at "
        "${CMAKE_CXX_COMPILER}. Build through build.ps1, which loads the MSVC environment; or delete this build "
        "directory and configure from an x64 Visual Studio developer prompt.")
endif()

set(CMAKE_CXX_STANDARD 23)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_subdirectory(dll)
