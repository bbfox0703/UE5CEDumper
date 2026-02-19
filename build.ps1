<#
.SYNOPSIS
    UE5CEDumper unified build script — C++ DLL + C# Avalonia UI

.DESCRIPTION
    Builds both the C++ injected DLL and the C# Avalonia UI application.
    Supports three build modes:
      - Debug    : Unoptimized builds with debug symbols (fast iteration)
      - Release  : Optimized builds (normal development)
      - Publish  : C++ Release + C# Native AOT single-file exe (distribution)

.PARAMETER Mode
    Build mode: Debug, Release, or Publish (default: Release)

.PARAMETER Target
    Build target: All, DLL, UI, Test (default: All)

.PARAMETER Clean
    Remove all build artifacts before building

.PARAMETER SkipRestore
    Skip NuGet package restore (faster if already restored)

.EXAMPLE
    .\build.ps1                          # Release build, all targets
    .\build.ps1 -Mode Debug             # Debug build
    .\build.ps1 -Mode Publish           # AOT single-file publish
    .\build.ps1 -Mode Publish -Clean    # Clean + publish
    .\build.ps1 -Target DLL             # Build only the C++ DLL
    .\build.ps1 -Target UI -Mode Debug  # Debug build UI only
    .\build.ps1 -Target Test            # Build + run tests
#>

[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release", "Publish")]
    [string]$Mode = "Release",

    [ValidateSet("All", "DLL", "UI", "Test")]
    [string]$Target = "All",

    [switch]$Clean,
    [switch]$SkipRestore
)

# ============================================================
# Configuration
# ============================================================

$ErrorActionPreference = "Stop"
$sw = [System.Diagnostics.Stopwatch]::StartNew()

$ROOT_DIR   = $PSScriptRoot
$vsDevShellLoaded = $false
$DLL_DIR    = Join-Path $ROOT_DIR "dll"
$UI_DIR     = Join-Path $ROOT_DIR "ui"
$UI_PROJ    = Join-Path $UI_DIR  "UE5DumpUI\UE5DumpUI.csproj"
$TEST_PROJ  = Join-Path $UI_DIR  "UE5DumpUI.Tests\UE5DumpUI.Tests.csproj"
$DIST_DIR   = Join-Path $ROOT_DIR "dist"
$BUILD_DIR  = Join-Path $ROOT_DIR "build"

# Map Mode to C++ config and C# config
$CppConfig    = if ($Mode -eq "Debug") { "Debug" } else { "Release" }
$CSharpConfig = if ($Mode -eq "Debug") { "Debug" } else { "Release" }

# ============================================================
# Helper functions
# ============================================================

function Write-Banner([string]$Text) {
    $line = "=" * 60
    Write-Host ""
    Write-Host $line -ForegroundColor Cyan
    Write-Host "  $Text" -ForegroundColor Cyan
    Write-Host $line -ForegroundColor Cyan
}

function Write-Step([string]$Text) {
    Write-Host ">> $Text" -ForegroundColor Yellow
}

function Write-Ok([string]$Text) {
    Write-Host "   [OK] $Text" -ForegroundColor Green
}

function Write-Fail([string]$Text) {
    Write-Host "   [FAIL] $Text" -ForegroundColor Red
}

function Write-Info([string]$Text) {
    Write-Host "   $Text" -ForegroundColor Gray
}

function Enter-VsDevEnvironment() {
    <#
    .SYNOPSIS
        Load MSVC x64 developer environment into the current PowerShell session.
        Only loads once per session.
    #>
    if ($script:vsDevShellLoaded) { return $true }

    $devShellDll = Join-Path $script:vsPath "Common7\Tools\Microsoft.VisualStudio.DevShell.dll"
    if (-not (Test-Path $devShellDll)) {
        Write-Fail "DevShell.dll not found: $devShellDll"
        return $false
    }

    try {
        Import-Module $devShellDll
        Enter-VsDevShell -VsInstallPath $script:vsPath -SkipAutomaticLocation -DevCmdArguments "-arch=x64 -host_arch=x64" | Out-Null
        $script:vsDevShellLoaded = $true
        Write-Ok "MSVC x64 environment loaded"
        return $true
    }
    catch {
        Write-Fail "Failed to load VS DevShell: $_"
        return $false
    }
}

function Invoke-CmdInVsEnv([string]$Commands) {
    <#
    .SYNOPSIS
        Execute commands inside the MSVC x64 developer environment.
        Uses Enter-VsDevShell (in-process) — works from any shell.
        Returns $true on success.
    #>

    if (-not (Enter-VsDevEnvironment)) { return $false }

    # Split commands by newline and execute each
    $lines = $Commands -split "`n" | Where-Object { $_.Trim() }
    foreach ($line in $lines) {
        $line = $line.Trim()
        if (-not $line) { continue }

        Write-Info "  > $line"
        Invoke-Expression $line 2>&1 | Write-Host
        if ($LASTEXITCODE -ne 0) { return $false }
    }
    return $true
}

function Get-FileSize([string]$Path) {
    if (Test-Path $Path) {
        $size = (Get-Item $Path).Length
        if ($size -gt 1MB) { return "{0:N1} MB" -f ($size / 1MB) }
        if ($size -gt 1KB) { return "{0:N1} KB" -f ($size / 1KB) }
        return "$size B"
    }
    return "N/A"
}

# ============================================================
# Preamble
# ============================================================

Write-Banner "UE5CEDumper Build  |  Mode: $Mode  |  Target: $Target"

Write-Info "Root:   $ROOT_DIR"
Write-Info "C++:    $CppConfig"
Write-Info "C#:     $CSharpConfig"
Write-Info "Clean:  $Clean"

# Locate vswhere.exe — search multiple known locations
$vswhere = $null
$vswhereCandidates = [System.Collections.Generic.List[string]]::new()

# 1. Already in PATH?
$inPath = Get-Command vswhere -ErrorAction SilentlyContinue
if ($inPath) { $vswhereCandidates.Add($inPath.Source) }

# 2. Standard VS Installer location (x86 Program Files)
$pf86 = ${env:ProgramFiles(x86)}
if ($pf86) { $vswhereCandidates.Add((Join-Path $pf86 "Microsoft Visual Studio\Installer\vswhere.exe")) }

# 3. Program Files (non-x86)
if ($env:ProgramFiles) { $vswhereCandidates.Add((Join-Path $env:ProgramFiles "Microsoft Visual Studio\Installer\vswhere.exe")) }

# 4. Chocolatey
if ($env:ChocolateyInstall) { $vswhereCandidates.Add((Join-Path $env:ChocolateyInstall "bin\vswhere.exe")) }

# 5. User-level
if ($env:LOCALAPPDATA) { $vswhereCandidates.Add((Join-Path $env:LOCALAPPDATA "Microsoft\VisualStudio\Installer\vswhere.exe")) }

foreach ($candidate in $vswhereCandidates) {
    if ($candidate -and (Test-Path $candidate -ErrorAction SilentlyContinue)) {
        $vswhere = $candidate
        break
    }
}

if (-not $vswhere) {
    Write-Fail "vswhere.exe not found. Searched:"
    foreach ($c in $vswhereCandidates) {
        if ($c) { Write-Info "  - $c" }
    }
    Write-Info ""
    Write-Info "Install Visual Studio or run: winget install Microsoft.VisualStudio.Locator"
    exit 1
}

Write-Info "vswhere: $vswhere"

# Find Visual Studio installation path
$vsPath = & $vswhere -latest -property installationPath
if (-not $vsPath) {
    # Try finding any VS with C++ workload
    $vsPath = & $vswhere -latest -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
}
if (-not $vsPath) {
    Write-Fail "No Visual Studio installation found (need C++ Desktop workload)"
    exit 1
}
Write-Info "VS:     $vsPath"

# Verify dotnet SDK is available (cmake/ninja are bundled with VS, accessed via vcvars)
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Fail "dotnet SDK not found in PATH"
    exit 1
}
Write-Info "dotnet: $(dotnet --version)"

# ============================================================
# Clean
# ============================================================

if ($Clean) {
    Write-Step "Cleaning build artifacts..."

    if (Test-Path $BUILD_DIR)  { Remove-Item $BUILD_DIR -Recurse -Force }
    if (Test-Path $DIST_DIR)   { Remove-Item $DIST_DIR  -Recurse -Force }

    # Clean dotnet outputs
    if ($Target -in "All", "UI", "Test") {
        & dotnet clean $UI_PROJ -c $CSharpConfig --nologo -v q 2>$null
        if (Test-Path $TEST_PROJ) {
            & dotnet clean $TEST_PROJ -c $CSharpConfig --nologo -v q 2>$null
        }
    }

    Write-Ok "Clean complete"
}

# Create output directories
New-Item -ItemType Directory -Path $DIST_DIR  -Force | Out-Null
New-Item -ItemType Directory -Path $BUILD_DIR -Force | Out-Null

$exitCode = 0

# ============================================================
# Build C++ DLL
# ============================================================

if ($Target -in "All", "DLL") {
    Write-Banner "C++ DLL  |  $CppConfig"

    Write-Step "Configuring CMake (Ninja + MSVC)..."
    $configOk = Invoke-CmdInVsEnv "cmake -S `"$ROOT_DIR`" -B `"$BUILD_DIR`" -G Ninja -DCMAKE_BUILD_TYPE=$CppConfig"

    if (-not $configOk) {
        Write-Fail "CMake configure failed"
        $exitCode = 1
    }
    else {
        Write-Ok "CMake configured"

        Write-Step "Building UE5Dumper.dll ($CppConfig)..."
        $buildOk = Invoke-CmdInVsEnv "cmake --build `"$BUILD_DIR`" --config $CppConfig"

        if (-not $buildOk) {
            Write-Fail "DLL build failed"
            $exitCode = 1
        }
        else {
            # Find and copy the DLL to dist/
            $dllFile = Get-ChildItem -Path $BUILD_DIR -Filter "UE5Dumper.dll" -Recurse |
                       Select-Object -First 1

            if ($dllFile) {
                Copy-Item $dllFile.FullName -Destination $DIST_DIR -Force

                # Always copy PDB (useful for crash diagnostics even in Release)
                $pdbFile = Get-ChildItem -Path $BUILD_DIR -Filter "UE5Dumper.pdb" -Recurse |
                           Select-Object -First 1
                if ($pdbFile) {
                    Copy-Item $pdbFile.FullName -Destination $DIST_DIR -Force
                }

                $dllSize = Get-FileSize (Join-Path $DIST_DIR "UE5Dumper.dll")
                Write-Ok "UE5Dumper.dll ($dllSize)"

                # Verify exports with dumpbin (best-effort)
                Write-Step "Verifying DLL exports..."
                Invoke-CmdInVsEnv "dumpbin /exports `"$($dllFile.FullName)`" | findstr /C:`"UE5_`"" | Out-Null
            }
            else {
                Write-Fail "UE5Dumper.dll not found in build output"
                $exitCode = 1
            }
        }
    }
}

# ============================================================
# Build C# Avalonia UI
# ============================================================

if ($Target -in "All", "UI") {
    Write-Banner "C# Avalonia UI  |  $CSharpConfig ($Mode)"

    # Restore packages
    if (-not $SkipRestore) {
        Write-Step "Restoring NuGet packages..."
        & dotnet restore $UI_PROJ --nologo -v q
        if ($LASTEXITCODE -ne 0) {
            Write-Fail "Package restore failed"
            $exitCode = 1
        }
        else {
            Write-Ok "Packages restored"
        }
    }

    if ($exitCode -eq 0) {
        if ($Mode -eq "Publish") {
            # ===== Publish mode: Native AOT single-file =====
            # Native AOT requires MSVC linker — run inside VS developer environment
            # (the ILCompiler internally invokes vswhere + link.exe)
            Write-Step "Publishing with Native AOT (this may take several minutes)..."

            $publishDir = Join-Path $DIST_DIR "publish"

            # ILCompiler invokes vswhere.exe internally — ensure it's on PATH
            $vswhereBin = Split-Path $vswhere -Parent
            $publishCmd = "set `"PATH=%PATH%;$vswhereBin`"`nset `"Platform=x64`"`ndotnet publish `"$UI_PROJ`" -c Release -r win-x64 --self-contained -p:PublishAot=true -p:PublishSingleFile=true -o `"$publishDir`" --nologo"
            $publishOk = Invoke-CmdInVsEnv $publishCmd

            if (-not $publishOk) {
                Write-Fail "AOT publish failed"
                $exitCode = 1
            }
            else {
                $exeFile = Get-ChildItem -Path $publishDir -Filter "UE5DumpUI.exe" |
                           Select-Object -First 1
                if ($exeFile) {
                    Copy-Item $exeFile.FullName -Destination $DIST_DIR -Force
                    # Clean up the temp publish subfolder — keep dist\ tidy
                    Remove-Item $publishDir -Recurse -Force -ErrorAction SilentlyContinue
                    $exeSize = Get-FileSize (Join-Path $DIST_DIR "UE5DumpUI.exe")
                    Write-Ok "UE5DumpUI.exe  |  Native AOT single-file ($exeSize)"
                }
                else {
                    Write-Fail "UE5DumpUI.exe not found in publish output"
                    $exitCode = 1
                }
            }
        }
        else {
            # ===== Debug mode: dotnet build (fast, requires .NET runtime) =====
            # ===== Release mode: dotnet publish --self-contained single-file (no AOT) =====
            if ($Mode -eq "Debug") {
                Write-Step "Building UE5DumpUI (Debug, framework-dependent)..."
                & dotnet build $UI_PROJ -c Debug --nologo
            }
            else {
                Write-Step "Publishing UE5DumpUI (Release, self-contained single-file)..."
                $releasePublishDir = Join-Path $DIST_DIR "publish"
                & dotnet publish $UI_PROJ `
                    -c Release `
                    -r win-x64 `
                    --self-contained `
                    -p:PublishSingleFile=true `
                    -p:PublishAot=false `
                    -o $releasePublishDir `
                    --nologo
            }

            if ($LASTEXITCODE -ne 0) {
                Write-Fail "UI build failed"
                $exitCode = 1
            }
            else {
                # Locate output exe
                if ($Mode -eq "Debug") {
                    $searchBase = Join-Path $UI_DIR "UE5DumpUI\bin\Debug"
                    $exeFile = Get-ChildItem -Path $searchBase -Filter "UE5DumpUI.exe" -Recurse -ErrorAction SilentlyContinue |
                               Select-Object -First 1
                }
                else {
                    $releasePublishDir = Join-Path $DIST_DIR "publish"
                    $exeFile = Get-ChildItem -Path $releasePublishDir -Filter "UE5DumpUI.exe" -ErrorAction SilentlyContinue |
                               Select-Object -First 1
                }

                if ($exeFile) {
                    Copy-Item $exeFile.FullName -Destination $DIST_DIR -Force

                    if ($Mode -eq "Debug") {
                        $pdb = Join-Path $exeFile.DirectoryName "UE5DumpUI.pdb"
                        if (Test-Path $pdb) { Copy-Item $pdb -Destination $DIST_DIR -Force }
                    }
                    else {
                        # Clean up publish temp folder
                        Remove-Item $releasePublishDir -Recurse -Force -ErrorAction SilentlyContinue
                    }

                    $exeSize = Get-FileSize (Join-Path $DIST_DIR "UE5DumpUI.exe")
                    Write-Ok "UE5DumpUI.exe ($exeSize)"
                }
                else {
                    Write-Fail "No build output found"
                    $exitCode = 1
                }
            }
        }
    }
}

# ============================================================
# Run Tests
# ============================================================

if ($Target -in "All", "Test") {
    Write-Banner "Unit Tests"

    if (-not (Test-Path $TEST_PROJ)) {
        Write-Info "Test project not found, skipping"
    }
    else {
        Write-Step "Building + running tests..."

        $restoreFlag = if ($SkipRestore) { "--no-restore" } else { "" }
        & dotnet test $TEST_PROJ -c $CSharpConfig --nologo $restoreFlag -v minimal

        if ($LASTEXITCODE -ne 0) {
            Write-Fail "Tests failed"
            $exitCode = 1
        }
        else {
            Write-Ok "All tests passed"
        }
    }
}

# ============================================================
# Copy CE Lua scripts
# ============================================================

if ($Target -eq "All") {
    Write-Step "Copying CT table to dist\..."
    # CT goes in the same folder as DLL + EXE so it can auto-detect the DLL path
    $src = Join-Path $ROOT_DIR "scripts\UE5CEDumper.CT"
    if (Test-Path $src) {
        Copy-Item $src -Destination $DIST_DIR -Force
        Write-Ok "UE5CEDumper.CT copied to dist\"
    }
}

# ============================================================
# Summary
# ============================================================

$sw.Stop()

Write-Banner "Build Summary"

Write-Host ""
Write-Host "  Mode:     $Mode" -ForegroundColor White
Write-Host "  Target:   $Target" -ForegroundColor White
Write-Host "  Time:     $($sw.Elapsed.ToString('mm\:ss\.ff'))" -ForegroundColor White

$statusColor = if ($exitCode -eq 0) { "Green" } else { "Red" }
$statusText  = if ($exitCode -eq 0) { "SUCCESS" } else { "FAILED" }
Write-Host "  Status:   $statusText" -ForegroundColor $statusColor
Write-Host ""

if (Test-Path $DIST_DIR) {
    Write-Host "  Output: $DIST_DIR" -ForegroundColor White

    Get-ChildItem $DIST_DIR -File -ErrorAction SilentlyContinue | ForEach-Object {
        $sz = Get-FileSize $_.FullName
        Write-Host "    $($_.Name)  ($sz)" -ForegroundColor Gray
    }
}

Write-Host ""
exit $exitCode
