@echo off
:: 1. 萬一有殘留的 lock 檔，先嘗試清掉
if exist ".git\index.lock" del /f /q ".git\index.lock"

:: 2. 強制同步 URL 並更新所有層級
git submodule sync --recursive
git submodule update --init --recursive --remote --merge --force

:: 3. 關鍵一步：進去 RE-UE4SS 把它的子模組指針也 commit 起來
cd vendor\RE-UE4SS
git add .
git commit -m "chore: update nested Unreal submodule" 2>nul
cd ..\..

:: 4. 提交主專案的變動
git add .
git commit -m "chore: sync all submodules to latest remote commits"
git push origin HEAD

echo [FINISH] BBFox, everything is clean now.