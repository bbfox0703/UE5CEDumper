@echo off
:: 設定編碼為 UTF-8
chcp 65001 >nul
setlocal

REM 使用絕對路徑指向工具
set "CLOC_EXE=D:\tools\cloc-2.08.exe"
set "TARGET_DIR=D:\Github\UE5CEDumper"

echo [INFO] 正在統計 %TARGET_DIR% 的程式碼行數...

REM 統計指令：使用正斜線 / 以避免反斜線 \ 的轉義問題
"%CLOC_EXE%" "%TARGET_DIR%" ^
    --fullpath ^
    --not-match-d="(\.claude|\.git|\.vs|build|bin|obj|ui/UE5DumpUI/bin|ui/UE5DumpUI/obj|ui/UE5DumpUI.Tests/bin|ui/UE5DumpUI.Tests/obj|vendor|docs/private)" ^
    --exclude-lang="JSON,XML"

::pause