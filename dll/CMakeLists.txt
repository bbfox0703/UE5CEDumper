cmake_minimum_required(VERSION 3.25)

add_library(UE5Dumper SHARED
    src/dllmain.cpp
    src/Memory.cpp
    src/Logger.cpp
    src/OffsetFinder.cpp
    src/ObjectArray.cpp
    src/FNamePool.cpp
    src/UStructWalker.cpp
    src/ExportAPI.cpp
    src/PipeServer.cpp
)

target_include_directories(UE5Dumper PRIVATE
    src
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
)

# Output name without lib prefix
set_target_properties(UE5Dumper PROPERTIES
    PREFIX ""
    OUTPUT_NAME "UE5Dumper"
)
