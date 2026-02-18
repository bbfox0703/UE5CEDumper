@echo off
setlocal EnableDelayedExpansion

:: ============================================================
:: build.cmd — UE5CEDumper build wrapper
::
:: Usage:
::   build              Build Release (all targets)
::   build debug        Build Debug
::   build release      Build Release
::   build publish      Build Publish (Native AOT single-file)
::   build clean        Clean + Release build
::   build dll          Build DLL only
::   build ui           Build UI only
::   build test         Build + run tests
::   build publish clean   Publish with clean
:: ============================================================

set "MODE=Release"
set "TARGET=All"
set "CLEAN="
set "EXTRA_ARGS="

:: Parse arguments
:parse_args
if "%~1"=="" goto :run

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

:: Unknown arg — pass through
set "EXTRA_ARGS=!EXTRA_ARGS! %~1"
shift
goto :parse_args

:run
echo.
echo  UE5CEDumper Build
echo  Mode: %MODE%  Target: %TARGET%  Clean: %CLEAN%
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -Mode %MODE% -Target %TARGET% %CLEAN% %EXTRA_ARGS%
set "EC=%ERRORLEVEL%"

if %EC% neq 0 (
    echo.
    echo  BUILD FAILED [exit code %EC%]
    echo.
) else (
    echo.
    echo  BUILD SUCCEEDED
    echo.
)

exit /b %EC%

:usage
echo.
echo  Usage: build [mode] [target] [options]
echo.
echo  Modes:
echo    debug       Unoptimized, debug symbols (fast iteration)
echo    release     Optimized build (default)
echo    publish     Native AOT single-file exe (distribution)
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
echo    build publish clean     Clean + AOT publish
echo    build dll               Build DLL only
echo    build test              Run tests
echo.
exit /b 0

:to_upper
:: Convert variable to uppercase
for %%a in (A B C D E F G H I J K L M N O P Q R S T U V W X Y Z) do (
    set "%1=!%1:%%a=%%a!"
)
goto :eof
