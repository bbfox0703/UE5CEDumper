@echo off
:: 1. 先同步 URL 設定，避免以後 remote 又更名導致路徑對不上
git submodule sync --recursive

:: 2. 更新子模組並合併遠端最新的 commit
git submodule update --remote --merge --recursive

:: 3. 檢查是否有變動，避免 "nothing to commit" 的報錯
git diff --quiet --exit-code
if %errorlevel% neq 0 (
    git add .
    git commit -m "chore: sync all submodules to latest remote commits"
    
    :: 4. 推送到遠端
    git push origin HEAD
    echo [SUCCESS] Submodules updated and pushed.
) else (
    echo [INFO] No submodule updates found. Everything is up-to-date.
)