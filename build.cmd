@echo off
setlocal EnableDelayedExpansion

:: ============================================================
:: build.cmd -- UE5CEDumper build wrapper
::
:: NOTE: All builds are ALWAYS clean (no cache).
::   CMake build/ dir and dotnet bin/obj are removed before each build.
::
:: Usage:
::   build              Build Release (all targets)
::   build debug        Build Debug
::   build release      Build Release
::   build publish      Build Publish (optimized single-file)
::   build clean        Clean dist/ folder too
::   build dll          Build DLL only
::   build ui           Build UI only
::   build test         Build + run tests
::   build publish clean   Publish with clean
:: ============================================================

set "MODE=Release"
set "TARGET=All"
set "CLEAN="
set "HAS_ARGS=0"

:: Parse arguments
:parse_args
if "%~1"=="" goto :run
set "HAS_ARGS=1"

set "ARG=%~1"

:: Case-insensitive matching
for %%A in ("%ARG%") do set "UPPER=%%~A"
call :to_upper UPPER

if "!UPPER!"=="DEBUG"   ( set "MODE=Debug"   & shift & goto :parse_args )
if "!UPPER!"=="RELEASE" ( set "MODE=Release" & shift & goto :parse_args )
if "!UPPER!"=="PUBLISH" ( set "MODE=Publish" & shift & goto :parse_args )
if "!UPPER!"=="CLEAN"   ( set "CLEAN=-Clean" & shift & goto :parse_args )
if "!UPPER!"=="DLL"     ( set "TARGET=DLL"   & shift & goto :parse_args )
if "!UPPER!"=="UI"      ( set "TARGET=UI"    & shift & goto :parse_args )
if "!UPPER!"=="TEST"    ( set "TARGET=Test"  & shift & goto :parse_args )
if "!UPPER!"=="ALL"     ( set "TARGET=All"   & shift & goto :parse_args )
if "!UPPER!"=="/?"      goto :usage
if "!UPPER!"=="-H"      goto :usage
if "!UPPER!"=="--HELP"  goto :usage

:: Unknown arg -- show error and usage
echo.
echo  ERROR: Unknown argument '%~1'
goto :usage_error

:run
set "LOG=%~dp0build_log.txt"

echo.
echo  UE5CEDumper Build
echo  Mode: %MODE%  Target: %TARGET%  Clean: %CLEAN%
echo  Log:  %LOG%

:: Show hint when no arguments provided (default Release build)
if "!HAS_ARGS!"=="0" (
    echo  Hint: No arguments -- using defaults. Available options:
    echo    build debug          Debug build
    echo    build dll            DLL only
    echo    build ui             UI only
    echo    build test           Run tests
    echo    build publish        Optimized single-file
    echo    build clean          Clean first
    echo    build --help         Full usage
)
echo.

:: Use -File instead of -Command to avoid encoding issues at the
:: cmd.exe -> PowerShell -> Tee-Object -> cmd.exe pipe boundary.
:: Logging is handled inside build.ps1 via Start-Transcript.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -Mode %MODE% -Target %TARGET% %CLEAN% -LogFile "%LOG%"
set "EC=%ERRORLEVEL%"

if %EC% neq 0 (
    echo.
    echo  BUILD FAILED [exit code %EC%] -- see %LOG%
    echo.
) else (
    echo.
    echo  BUILD SUCCEEDED -- log: %LOG%
    echo.
)

exit /b %EC%

:usage_error
call :print_usage
exit /b 1

:usage
call :print_usage
exit /b 0

:print_usage
echo.
echo  Usage: build [mode] [target] [options]
echo.
echo  Modes:
echo    debug       Unoptimized, debug symbols (fast iteration)
echo    release     Optimized build (default)
echo    publish     Optimized single-file exe (distribution)
echo.
echo  Targets:
echo    all         Build everything (default)
echo    dll         C++ DLL only
echo    ui          C# Avalonia UI only
echo    test        Build + run unit tests
echo.
echo  Options:
echo    clean       Remove all build artifacts first
echo.
echo  Examples:
echo    build                   Release build, all targets
echo    build debug             Debug build
echo    build publish clean     Clean + publish
echo    build dll               Build DLL only
echo    build test              Run tests
echo.
goto :eof

:to_upper
:: Convert variable to uppercase
for %%a in (A B C D E F G H I J K L M N O P Q R S T U V W X Y Z) do (
    set "%1=!%1:%%a=%%a!"
)
goto :eof
