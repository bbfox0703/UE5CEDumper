git submodule update --remote --merge --recursive
git add .
git commit -m "chore: sync all submodules to latest remote commits"

:: 使用此指令會自動推送到目前分支的對應遠端
git push origin HEAD