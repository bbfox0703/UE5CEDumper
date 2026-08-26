@echo off
:: Count lines of code in THIS repo, wherever it is checked out.
::
:: Nothing here is machine-specific any more. It used to hardcode both
:: D:\tools\cloc-2.08.exe and D:\Github\UE5CEDumper, which meant the script only ran on
:: one machine and pinned one cloc version -- and check_no_local_paths.py did not catch
:: it, because that gate hunts USER-HOME paths and D:\ is not one.
::
::   count_cloc.bat                 count the repo this file lives in
::   count_cloc.bat <dir>           count somewhere else
::   set CLOC_EXE=... ^& count_cloc.bat   force a specific cloc
::
:: cloc resolution order: %CLOC_EXE% -> cloc on PATH -> a few known local copies.
:: Install it with:  winget install --id AlDanial.Cloc --exact
:: See docs/toolchain.md (tier C).
chcp 65001 >nul
setlocal EnableDelayedExpansion

set "TARGET_DIR=%~1"
if "%TARGET_DIR%"=="" set "TARGET_DIR=%~dp0."

:: 1. explicit override
if defined CLOC_EXE if exist "%CLOC_EXE%" goto :found

:: 2. on PATH (winget AlDanial.Cloc puts it there)
for /f "delims=" %%I in ('where cloc 2^>nul') do (
    set "CLOC_EXE=%%I"
    goto :found
)

:: 3. known local copies, newest name last so the loop keeps the highest version
for %%D in ("D:\tools" "D:\Tools" "C:\tools" "%USERPROFILE%\tools") do (
    for %%F in ("%%~D\cloc*.exe") do set "CLOC_EXE=%%~fF"
)
if defined CLOC_EXE if exist "%CLOC_EXE%" goto :found

echo(
echo   [ERROR] cloc was not found.
echo(
echo   Install it:   winget install --id AlDanial.Cloc --exact
echo   Or point at an existing copy:   set "CLOC_EXE=X:\path\to\cloc.exe"
echo(
exit /b 1

:found
echo [INFO] cloc:   %CLOC_EXE%
echo [INFO] target: %TARGET_DIR%
echo(

:: --fullpath so --not-match-d matches the path, not just the leaf directory name.
"%CLOC_EXE%" "%TARGET_DIR%" ^
    --fullpath ^
    --not-match-d="(\.claude|\.git|\.vs|build|bin|obj|ui/UE5DumpUI/bin|ui/UE5DumpUI/obj|ui/UE5DumpUI.Tests/bin|ui/UE5DumpUI.Tests/obj|vendor)" ^
    --exclude-lang="JSON,XML"

exit /b %ERRORLEVEL%
