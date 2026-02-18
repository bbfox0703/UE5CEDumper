# ============================================================
# build_all.ps1 — Build DLL + UI, output to dist/
# ============================================================

param(
    [switch]$Clean,
    [string]$Config = "Release"
)

$ErrorActionPreference = "Stop"
$rootDir = Split-Path -Parent $PSScriptRoot

Write-Host "=== UE5CEDumper Build Script ===" -ForegroundColor Cyan
Write-Host "Root: $rootDir"
Write-Host "Config: $Config"

# Find Visual Studio
$vswhere = "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) {
    Write-Error "vswhere not found at $vswhere"
    exit 1
}

$vsPath = & $vswhere -latest -property installationPath
if (-not $vsPath) {
    Write-Error "Visual Studio not found"
    exit 1
}
Write-Host "VS: $vsPath" -ForegroundColor Green

# Prepare dist directory
$distDir = Join-Path $rootDir "dist"
if ($Clean -and (Test-Path $distDir)) {
    Remove-Item $distDir -Recurse -Force
}
New-Item -ItemType Directory -Path $distDir -Force | Out-Null

# ---- Build DLL ----
Write-Host "`n=== Building C++ DLL ===" -ForegroundColor Yellow

$buildDir = Join-Path $rootDir "build"
if ($Clean -and (Test-Path $buildDir)) {
    Remove-Item $buildDir -Recurse -Force
}

# Use vcvars64 environment
$vcvars = Join-Path $vsPath "VC\Auxiliary\Build\vcvars64.bat"
if (-not (Test-Path $vcvars)) {
    Write-Error "vcvars64.bat not found at $vcvars"
    exit 1
}

# Run cmake in vcvars environment
$cmakeArgs = @(
    "/c",
    "`"call `"$vcvars`" >nul 2>&1 && " +
    "cmake -S `"$rootDir`" -B `"$buildDir`" -G Ninja -DCMAKE_BUILD_TYPE=$Config && " +
    "cmake --build `"$buildDir`" --config $Config`""
)

$proc = Start-Process -FilePath "cmd.exe" -ArgumentList $cmakeArgs -Wait -PassThru -NoNewWindow
if ($proc.ExitCode -ne 0) {
    Write-Error "DLL build failed"
    exit 1
}

# Copy DLL to dist
$dllPath = Get-ChildItem -Path $buildDir -Filter "UE5Dumper.dll" -Recurse | Select-Object -First 1
if ($dllPath) {
    Copy-Item $dllPath.FullName -Destination $distDir
    Write-Host "DLL copied to dist/" -ForegroundColor Green
} else {
    Write-Warning "UE5Dumper.dll not found in build output"
}

# ---- Build UI ----
Write-Host "`n=== Building Avalonia UI ===" -ForegroundColor Yellow

$uiProj = Join-Path $rootDir "ui\UE5DumpUI\UE5DumpUI.csproj"
$publishDir = Join-Path $distDir "ui-publish"

dotnet publish $uiProj -c $Config -r win-x64 --self-contained -o $publishDir
if ($LASTEXITCODE -ne 0) {
    Write-Error "UI build failed"
    exit 1
}

# Copy exe to dist root
$exePath = Get-ChildItem -Path $publishDir -Filter "UE5DumpUI.exe" | Select-Object -First 1
if ($exePath) {
    Copy-Item $exePath.FullName -Destination $distDir
    Write-Host "UI exe copied to dist/" -ForegroundColor Green
}

# ---- Copy scripts ----
Write-Host "`n=== Copying scripts ===" -ForegroundColor Yellow
$scriptsDir = Join-Path $distDir "scripts"
New-Item -ItemType Directory -Path $scriptsDir -Force | Out-Null
Copy-Item (Join-Path $rootDir "scripts\ue5dump.lua") -Destination $scriptsDir
Copy-Item (Join-Path $rootDir "scripts\utils.lua") -Destination $scriptsDir

# ---- Summary ----
Write-Host "`n=== Build Complete ===" -ForegroundColor Green
Write-Host "Output directory: $distDir"
Get-ChildItem $distDir -File | ForEach-Object { Write-Host "  $($_.Name) ($([math]::Round($_.Length/1KB, 1)) KB)" }
