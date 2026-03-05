# 定義工具與 URL
$tools = @{
    "Dumper-7"     = "https://github.com/Encryqed/Dumper-7.git"
    "RE-UE4SS"      = "https://github.com/UE4SS-RE/RE-UE4SS.git"
    "UnrealEngine"  = "https://github.com/EpicGames/UnrealEngine.git"
}
##UE
## git clone --filter=blob:none https://github.com/EpicGames/UnrealEngine.git vendor/UnrealEngine
##
# 確保 vendor 目錄存在
if (!(Test-Path "vendor")) { New-Item -ItemType Directory -Path "vendor" }

foreach ($entry in $tools.GetEnumerator()) {
    $name = $entry.Key
    $url = $entry.Value
    $targetDir = "vendor\$name"

    if (Test-Path "$targetDir\.git") {
        Write-Host "[更新] $name..." -ForegroundColor Cyan
        Push-Location $targetDir
        git pull
        Pop-Location
    } else {
        Write-Host "[下載] $name..." -ForegroundColor Green
        if ($name -eq "UnrealEngine") {
            Write-Host "偵測到 UnrealEngine，執行 Blobless Clone (保留歷史但加速)..." -ForegroundColor Yellow
            # --filter=blob:none 只抓歷史紀錄，不預抓所有檔案內容
            git clone --filter=blob:none $url $targetDir
        } else {
            git clone $url $targetDir
        }
    }
    Write-Host "---------------------------------------"
}