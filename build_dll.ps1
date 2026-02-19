$ErrorActionPreference = 'Stop'

$vswhere = "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vswhere) {
    $vsPath = & $vswhere -latest -property installationPath
} else {
    $vsPath = "C:\Program Files\Microsoft Visual Studio\18\Community"
}

$vcvars = Join-Path $vsPath "VC\Auxiliary\Build\vcvars64.bat"
Write-Host "[BUILD] VS: $vsPath"
Write-Host "[BUILD] vcvars: $vcvars"

$cmds = @(
    "`"$vcvars`"",
    "cmake -S D:/Github/UE5CEDumper -B D:/Github/UE5CEDumper/build -G Ninja -DCMAKE_BUILD_TYPE=Release",
    "cmake --build D:/Github/UE5CEDumper/build --config Release"
)

$script = $cmds -join " && "
Write-Host "[BUILD] Running: $script"
cmd /c $script
exit $LASTEXITCODE
