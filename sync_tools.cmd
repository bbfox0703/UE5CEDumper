@echo off
setlocal enabledelayedexpansion

:: 定義工具清單與對應的遠端 URL (請根據你實際使用的 Repo 修改)
set "TOOLS=Dumper-7 RE-UE4SS"
set "URL_Dumper-7=https://github.com/Encryqed/Dumper-7.git"
set "URL_RE-UE4SS=https://github.com/UE4SS-RE/RE-UE4SS.git"

:: 檢查 vendor 資料夾是否存在
if not exist "vendor" mkdir "vendor"

for %%i in (%TOOLS%) do (
    set "FOLDER=vendor\%%i"
    set "REPO_URL=!URL_%%i!"

    if exist "!FOLDER!\.git" (
        echo [更新] !FOLDER! 正在抓取最新版本...
        pushd "!FOLDER!"
        git pull
        popd
    ) else (
        echo [下載] !FOLDER! 正在進行首次 Clone...
        git clone "!REPO_URL!" "!FOLDER!"
    )
    echo ---------------------------------------
)

echo 所有工具同步完成！
pause