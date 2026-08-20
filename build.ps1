<#
.SYNOPSIS
    UE5CEDumper unified build script — C++ DLL + C# Avalonia UI

.DESCRIPTION
    Builds both the C++ injected DLL and the C# Avalonia UI application.
    Supports three build modes:
      - Debug    : Unoptimized builds with debug symbols (fast iteration)
      - Release  : Optimized builds (normal development)
      - Publish  : C++ Release + C# optimized single-file exe (distribution)

.PARAMETER Mode
    Build mode: Debug, Release, or Publish (default: Release)

.PARAMETER Target
    Build target: All, DLL, UI, Test (default: All).
    DLL builds the main UE5Dumper.dll AND all three proxy DLLs (version/dinput8/
    dxgi) — the proxies are what actually get injected. ProxyDLL / ProxyDinput8 /
    ProxyDxgi build a single proxy in isolation.

.PARAMETER Clean
    Remove all build artifacts before building

.PARAMETER SkipRestore
    Skip NuGet package restore (faster if already restored)

.EXAMPLE
    .\build.ps1                          # Release build, all targets
    .\build.ps1 -Mode Debug             # Debug build
    .\build.ps1 -Mode Publish           # Optimized single-file publish
    .\build.ps1 -Mode Publish -Clean    # Clean + publish
    .\build.ps1 -Target DLL             # Build the C++ DLL + all 3 proxy DLLs
    .\build.ps1 -Target UI -Mode Debug  # Debug build UI only
    .\build.ps1 -Target Test            # Build + run tests (also republishes UI to dist/)
#>

[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release", "Publish")]
    [string]$Mode = "Release",

    [ValidateSet("All", "DLL", "ProxyDLL", "ProxyDinput8", "ProxyDxgi", "ProxyWinmm", "UI", "Test")]
    [string]$Target = "All",

    [switch]$Clean,
    [switch]$SkipRestore,

    # Skip the per-invocation build-number increment. Used by CI release builds
    # so the embedded build number stays pinned (e.g. to the release tag) instead
    # of drifting +1 on the runner. Local dev builds bump as usual.
    [switch]$NoBumpBuildNumber,

    [string]$LogFile = ""
)

# ============================================================
# Configuration
# ============================================================

$ErrorActionPreference = "Stop"
$sw = [System.Diagnostics.Stopwatch]::StartNew()

# Force UTF-8 for console output and .NET subprocess output (fixes CJK garbling).
# LOAD-BEARING, not cosmetic: on a localized MSVC, cl.exe /showIncludes is localized
# too, and CMake bakes the prefix it observes into build/CMakeFiles/rules.ninja as
# `msvc_deps_prefix`. Configure and build must observe the SAME code page or Ninja
# matches nothing and records ZERO header deps -- a .h edit then stops triggering a
# rebuild, silently. Pinning UTF-8 here, before any configure, is what keeps them
# equal. See Repair-NinjaHeaderDeps below, which heals a tree configured elsewhere.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
[Console]::InputEncoding  = [System.Text.Encoding]::UTF8
$env:DOTNET_CLI_UI_LANGUAGE = "en"  # dotnet CLI in English to avoid codepage issues

# Start log transcript if a log file path was provided (from build.cmd).
# This replaces the old Tee-Object pipe which caused encoding issues when
# crossing the cmd.exe <-> PowerShell boundary on CJK Windows.
$script:transcriptActive = $false
if ($LogFile) {
    try {
        Start-Transcript -Path $LogFile -Force | Out-Null
        $script:transcriptActive = $true
    }
    catch {
        Write-Warning "Could not start transcript to $LogFile : $_"
    }
}

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

function Repair-NinjaHeaderDeps() {
    <#
    .SYNOPSIS
        Drop the build dir if its msvc_deps_prefix cannot match what cl.exe prints,
        which silently disables Ninja's header dependency tracking.

    .DESCRIPTION
        Ninja learns which headers a .cpp includes by parsing cl.exe's /showIncludes
        output: a line naming an included file is recognised by the literal string in
        `msvc_deps_prefix` (build/CMakeFiles/rules.ninja). CMake detects that prefix
        once, at CONFIGURE time, by running the compiler and reading its output.

        On a localized MSVC the prefix is localized too -- on this project's zh-TW
        machines it is "注意: 包含檔案: ", never the English "Note: including file: "
        (VSLANG=1033 does NOT change it; only the installed language pack decides).
        Its BYTES therefore depend on the console code page CMake probed under. This
        script pins the console to UTF-8 before doing anything (see
        [Console]::OutputEncoding at the top), so configure and build always agree.

        A tree configured under a DIFFERENT code page does not agree. The bare
        `cmake -S . -B build -G Ninja` shown in CLAUDE.md's "manual commands" section,
        run from a stock cmd/Git Bash shell on CJK Windows, bakes a cp950 prefix; under
        this script cl.exe then prints UTF-8 and NO line ever matches. Ninja records
        ZERO header deps and reports nothing. The symptom is the dangerous one: editing
        a .h stops triggering a rebuild, so the test executables link stale objects and
        a header-pinned unit test goes green against code that was never compiled.

        Detect that state -- a prefix that is not valid UTF-8 -- and remove the build
        dir so the caller's configure runs fresh and every object recompiles with
        working deps. A pure-ASCII prefix (English MSVC) is valid UTF-8 and is left
        alone, so this is a no-op on non-localized toolchains.
    #>
    $rulesPath = Join-Path $BUILD_DIR "CMakeFiles\rules.ninja"
    if (-not (Test-Path $rulesPath)) { return }   # nothing configured yet

    # latin-1 maps 0x00-0xFF one-to-one, so the raw prefix bytes survive the round trip.
    $latin1 = [System.Text.Encoding]::GetEncoding(28591)
    $text   = $latin1.GetString([System.IO.File]::ReadAllBytes($rulesPath))
    $match  = [regex]::Match($text, '(?m)^msvc_deps_prefix = (.*)$')
    if (-not $match.Success) { return }           # not an MSVC deps=msvc tree

    $prefixBytes = $latin1.GetBytes($match.Groups[1].Value.TrimEnd("`r"))
    if ($prefixBytes.Length -eq 0) { return }

    $strictUtf8 = [System.Text.UTF8Encoding]::new($false, $true)
    try {
        $null = $strictUtf8.GetString($prefixBytes)
        return                                    # healthy: prefix will match
    } catch {
        # not valid UTF-8 => configured under another code page => deps are dead
    }

    Write-Step "Stale msvc_deps_prefix in $rulesPath (configured under another code page)."
    Write-Info "Ninja cannot match cl.exe /showIncludes output, so it is recording NO header"
    Write-Info "dependencies: a .h edit would not trigger a rebuild and the tests would link"
    Write-Info "stale objects. Removing the build dir to force a clean re-configure."
    Remove-Item -Recurse -Force $BUILD_DIR -ErrorAction SilentlyContinue
}

function Enter-VsDevEnvironment() {
    <#
    .SYNOPSIS
        Load MSVC x64 developer environment into the current PowerShell session.
        Only loads once per session.
    #>
    if ($script:vsDevShellLoaded) { return $true }

    # If this shell already has an x64 MSVC dev environment (e.g. inherited from a
    # parent "Developer PowerShell", or a sibling repo's publish script ran vcvars in
    # this same session), DON'T enter it again. Enter-VsDevShell PREPENDS the VC paths
    # to PATH/LIB/INCLUDE every time, so re-entering stacks them until the linker's
    # command line blows past the 8191-char Windows limit (-> AOT link exits 123).
    if ($env:VSCMD_ARG_TGT_ARCH -eq 'x64') {
        $script:vsDevShellLoaded = $true
        Write-Ok "MSVC x64 environment already loaded (reusing inherited env)"
        return $true
    }

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
        # Temporarily relax error preference to prevent native command stderr
        # (e.g., git warnings) from becoming terminating PowerShell errors.
        $prevEAP = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        try { Invoke-Expression $line 2>&1 | Write-Host }
        finally { $ErrorActionPreference = $prevEAP }
        if ($LASTEXITCODE -ne 0) { return $false }
    }
    return $true
}

function Invoke-CppSelfTest {
    <#
    .SYNOPSIS
        Build and run one C++ self-test target. Returns $true only when the
        target compiled, produced an .exe, ran, and passed.

    .DESCRIPTION
        A C++ test target that FAILS TO COMPILE used to be reported as a skip
        and left the exit code at 0, so the entire C++ suite could go silent —
        in CI too, because ci.yml / release.yml assert nothing beyond
        build.ps1's exit code. Three different outcomes collapsed into one
        "not available (skip)" line: the target failed to compile, the build
        succeeded but no .exe was found, and the build dir was never
        configured. None of them is a skip; under -Target Test / All the C++
        suite is expected to build and run, so all three are failures now.
        (Audit #5 AD1/AD2 — docs/audit-2026-08-13-early-code-findings.md §3c.)

        This lives in ONE function on purpose. It was two hand-copied blocks,
        which is the exact shape of that audit's root cause #4 — a fix applied
        at only some of its sites. Adding a third test target must not add a
        third copy of this logic.

        PowerShell note: the test's own stdout is piped to Out-Host rather than
        left on the pipeline, or it would be captured into this function's
        return value. $LASTEXITCODE is still set.
    #>
    param(
        [Parameter(Mandatory)][string]$TargetName,
        [Parameter(Mandatory)][string]$BuildDir,
        [Parameter(Mandatory)][string]$Config
    )

    if (-not (Test-Path $BuildDir)) {
        Write-Fail "$TargetName did NOT run - no C++ build dir ($BuildDir); the CMake configure above must have failed"
        return $false
    }

    Write-Step "Building $TargetName (C++ self-test)..."
    if (-not (Invoke-CmdInVsEnv "cmake --build `"$BuildDir`" --config $Config --target $TargetName")) {
        Write-Fail "$TargetName FAILED TO COMPILE - the C++ suite did not run (this is a build failure, not a skip)"
        return $false
    }

    $exe = Get-ChildItem -Path $BuildDir -Filter "$TargetName.exe" -Recurse -ErrorAction SilentlyContinue |
           Select-Object -First 1
    if (-not $exe) {
        Write-Fail "$TargetName.exe not found after a successful build - check the CMake target name / output dir"
        return $false
    }

    Write-Step "Running $TargetName..."
    # Under CI, ask the harness to name each test as it starts. This exists because a
    # CI-only crash (0xC0000409 in dll_helpers_test) produced ZERO output — the exe
    # passed locally, and the log had nothing to locate it with. Local runs stay quiet.
    if ($env:CI) { $env:DLL_TEST_TRACE = "1" }
    & $exe.FullName | Out-Host
    if ($env:CI) { Remove-Item Env:\DLL_TEST_TRACE -ErrorAction SilentlyContinue }
    if ($LASTEXITCODE -ne 0) {
        Write-Fail "$TargetName failed ($LASTEXITCODE assertion(s))"
        return $false
    }

    Write-Ok "$TargetName passed"
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

# Remove legacy per-proxy build directories from the old (pre-unified) layout.
# The proxy DLLs now build inside $BUILD_DIR alongside the main DLL, so these are
# dead weight — drop them once if a previous checkout left them behind.
foreach ($legacy in @("build_proxy", "build_proxy_dinput8", "build_proxy_dxgi")) {
    $legacyPath = Join-Path $ROOT_DIR $legacy
    if (Test-Path $legacyPath) {
        Remove-Item $legacyPath -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# ============================================================
# Increment build number (once per build invocation)
# CMake and MSBuild both READ this file; only build.ps1 increments.
# ============================================================
$buildNumFile = Join-Path $ROOT_DIR "build_number.txt"
if (Test-Path $buildNumFile) {
    $buildNum = [int](Get-Content $buildNumFile -Raw).Trim()
}
else {
    $buildNum = 0
}
if (-not $NoBumpBuildNumber) {
    $buildNum++
    # Write with LF line ending to match CMake's original format (avoids git CRLF warning)
    [System.IO.File]::WriteAllText($buildNumFile, "$buildNum`n")
    Write-Info "Build#: $buildNum"
}
else {
    Write-Info "Build#: $buildNum (no bump)"
}

$exitCode = 0

# ============================================================
# Build C++ DLLs (main + proxies) — single unified build dir
#
# All four DLLs share ONE CMake configure + ONE Ninja build so that:
#   - Zydis / minhook compile ONCE (not once per DLL)
#   - the proxy-agnostic sources compile ONCE (UE5DumperCommon object library)
#   - Ninja does true incremental rebuilds between runs (a source edit recompiles
#     only that TU, then relinks the affected DLLs)
#
# A full wipe of $BUILD_DIR happens ONLY with -Clean (done in the Clean phase
# above; CI always passes -Clean). Without -Clean we KEEP the build dir and let
# Ninja rebuild just what changed. We STILL re-run `cmake` configure on every
# build so BuildInfo.h (build number / git hash / timestamp) is regenerated —
# that keeps the embedded version fresh without forcing a full recompile (only
# the few TUs that include BuildInfo.h rebuild).
# ============================================================

# Map the requested -Target to the concrete Ninja targets to build. The configure
# always enables every proxy target, so any subset can be built without a
# reconfigure; we only compile the targets requested here.
# -Target DLL builds the main DLL AND all three proxy DLLs (version/dinput8/dxgi)
# — the proxies are the actually-injected artifacts, so "the DLL" means all of
# them. The per-proxy targets (ProxyDLL/ProxyDinput8/ProxyDxgi) remain for
# building a single proxy in isolation.
$cppTargets = @()
if ($Target -in "All", "DLL")               { $cppTargets += "UE5Dumper" }
if ($Target -in "All", "DLL", "ProxyDLL")     { $cppTargets += "UE5Dumper_Proxy" }
if ($Target -in "All", "DLL", "ProxyDinput8") { $cppTargets += "UE5Dumper_ProxyDinput8" }
if ($Target -in "All", "DLL", "ProxyDxgi")    { $cppTargets += "UE5Dumper_ProxyDxgi" }
if ($Target -in "All", "DLL", "ProxyWinmm")   { $cppTargets += "UE5Dumper_ProxyWinmm" }

if ($cppTargets.Count -gt 0) {
    Write-Banner "C++ DLLs  |  $CppConfig"

    # Submodules, checked HERE because this is where they are consumed.
    #
    # `git worktree add` does NOT populate submodules, and the task/chip runner creates
    # its worktrees with recursive submodule init DISABLED (turning it on makes the chip
    # fail to start). So a worktree-isolated task gets vendor/minhook and vendor/zydis as
    # EMPTY directories, and CMake then fails deep inside add_subdirectory with a message
    # that reads like a broken toolchain rather than "you have no submodules".
    #
    # Self-heal instead of documenting it: a note only works if every future task
    # remembers, and this repo already learned that a rule without a check is a rule that
    # breaks (tools/check_derived_counts.py exists for the same reason). C#-only work is
    # unaffected, which is precisely why this stayed invisible until a C++ task landed in
    # a worktree.
    foreach ($sm in @("vendor/minhook", "vendor/zydis")) {
        $smPath = Join-Path $ROOT_DIR $sm
        if (-not (Test-Path $smPath) -or -not (Get-ChildItem -Force $smPath -ErrorAction SilentlyContinue)) {
            Write-Step "Submodule '$sm' is empty (worktree?) — running git submodule update --init --recursive..."
            & git -C $ROOT_DIR submodule update --init --recursive
            if ($LASTEXITCODE -ne 0) {
                Write-Fail "git submodule update failed. Run it by hand in $ROOT_DIR, then rebuild."
                exit 1
            }
            break   # one call restores every submodule; no need to test the rest
        }
    }

    # NOTE: no clean here. -Clean already removed $BUILD_DIR in the Clean phase;
    # otherwise we keep it for an incremental Ninja build (the whole point).
    # A tree configured under a different console code page has dead header-dep
    # tracking; drop it first so the configure below is fresh. No-op when healthy.
    Repair-NinjaHeaderDeps

    Write-Step "Configuring CMake (Ninja + MSVC, all DLL targets)..."
    $configOk = Invoke-CmdInVsEnv "cmake -S `"$ROOT_DIR`" -B `"$BUILD_DIR`" -G Ninja -DCMAKE_BUILD_TYPE=$CppConfig -DBUILD_PROXY_DLL=ON -DBUILD_PROXY_DINPUT8=ON -DBUILD_PROXY_DXGI=ON -DBUILD_PROXY_WINMM=ON"

    if (-not $configOk) {
        Write-Fail "CMake configure failed"
        $exitCode = 1
    }
    else {
        Write-Ok "CMake configured"

        $targetArgs = "--target " + ($cppTargets -join " ")
        Write-Step "Building $($cppTargets -join ', ') ($CppConfig)..."
        $buildOk = Invoke-CmdInVsEnv "cmake --build `"$BUILD_DIR`" --config $CppConfig $targetArgs"

        if (-not $buildOk) {
            Write-Fail "DLL build failed"
            $exitCode = 1
        }
        else {
            # ---- Copy the main DLL to dist\ ----
            if ($Target -in "All", "DLL") {
                $dllFile = Get-ChildItem -Path $BUILD_DIR -Filter "UE5Dumper.dll" -Recurse |
                           Select-Object -First 1
                if ($dllFile) {
                    Copy-Item $dllFile.FullName -Destination $DIST_DIR -Force

                    # Copy PDB for Debug only — Release/Publish dists ship without
                    # symbols (a final sweep also strips any stale *.pdb).
                    if ($Mode -eq "Debug") {
                        $pdbFile = Get-ChildItem -Path $BUILD_DIR -Filter "UE5Dumper.pdb" -Recurse |
                                   Select-Object -First 1
                        if ($pdbFile) { Copy-Item $pdbFile.FullName -Destination $DIST_DIR -Force }
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

            # ---- Copy proxy DLLs to dist\proxy\ ----
            # Each proxy goes in a subdirectory to prevent UE5DumpUI.exe from
            # accidentally loading it (Windows DLL search order).
            $proxyOutputs = @(
                @{ When = "ProxyDLL";     File = "version.dll"; Pdb = "version.pdb" },
                @{ When = "ProxyDinput8"; File = "dinput8.dll"; Pdb = "dinput8.pdb" },
                @{ When = "ProxyDxgi";    File = "dxgi.dll";    Pdb = "dxgi.pdb"    },
                @{ When = "ProxyWinmm";   File = "winmm.dll";   Pdb = "winmm.pdb"   }
            )
            $proxyOutDir = Join-Path $DIST_DIR "proxy"
            foreach ($p in $proxyOutputs) {
                # -Target DLL copies ALL proxies; a per-proxy target copies only its own.
                if ($Target -notin "All", "DLL", $p.When) { continue }

                $proxyDll = Get-ChildItem -Path $BUILD_DIR -Filter $p.File -Recurse |
                            Select-Object -First 1
                if ($proxyDll) {
                    if (-not (Test-Path $proxyOutDir)) { New-Item -Path $proxyOutDir -ItemType Directory | Out-Null }
                    Copy-Item $proxyDll.FullName -Destination $proxyOutDir -Force

                    if ($Mode -eq "Debug") {
                        $pdbFile = Get-ChildItem -Path $BUILD_DIR -Filter $p.Pdb -Recurse |
                                   Select-Object -First 1
                        if ($pdbFile) { Copy-Item $pdbFile.FullName -Destination $proxyOutDir -Force }
                    }

                    $dllSize = Get-FileSize (Join-Path $proxyOutDir $p.File)
                    Write-Ok "$($p.File) ($dllSize) -> dist\proxy\"
                }
                else {
                    Write-Fail "$($p.File) not found in build output"
                    $exitCode = 1
                }
            }
        }
    }
}

# ============================================================
# Build C# Avalonia UI
# ============================================================

if ($Target -in "All", "UI", "Test") {
    Write-Banner "C# Avalonia UI  |  $CSharpConfig ($Mode)"

    # Native AOT links with MSVC's link.exe. `-Mode Publish` on its own (or with
    # -Target UI) never touched the C++ path, so the DevShell was never entered and
    # ILCompiler died with `link ... exited with code 9009` -- command not found, which
    # reads like a broken toolchain rather than a missing environment. Entering it here
    # is idempotent (Enter-VsDevEnvironment early-outs on $script:vsDevShellLoaded and on
    # an already-active VCINSTALLDIR) and costs nothing for a non-AOT build, so it is
    # unconditional rather than gated on yet another combination of flags.
    if ($Mode -eq "Publish") {
        if (-not (Enter-VsDevEnvironment)) {
            Write-Fail "Native AOT needs the MSVC linker; VS DevShell could not be loaded"
            $exitCode = 1
        }
    }

    # Always do a clean build — remove bin/obj to force full recompile
    $uiProjDir = Split-Path $UI_PROJ -Parent
    foreach ($subdir in @("bin", "obj")) {
        $p = Join-Path $uiProjDir $subdir
        if (Test-Path $p) {
            Write-Step "Removing $subdir for clean build..."
            Remove-Item $p -Recurse -Force
        }
    }

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

    # Guard: the native-renderer packages must stay on the versions Avalonia was
    # BUILT against. NuGet structurally cannot enforce this — Avalonia declares an
    # open-ended minimum ("SkiaSharp >= 3.119.4"), so a jump to a different MAJOR
    # *satisfies* the constraint and raises no NU1608/NU1605 for
    # TreatWarningsAsErrors to catch. Three routine "Update all packages" passes
    # duly walked SkiaSharp to 4.151.1 and HarfBuzzSharp to 14.2.1.2, and the UI
    # died with STATUS_HEAP_CORRUPTION inside libSkiaSharp (build 3122; see
    # docs/dev-log.md and docs/todo.md "SkiaSharp/HarfBuzzSharp ABI alignment").
    #
    # A comment saying "do not bump these" had already failed three times, which is
    # why this is a build step. Same reasoning as the Microsoft.Testing.* guard below.
    #
    # It compares what Avalonia ASKS FOR against what actually RESOLVED, read out of
    # the restore graph — deliberately NOT against a hardcoded constant, so upgrading
    # Avalonia itself moves the target automatically instead of tripping a stale check.
    if ($exitCode -eq 0) {
        $assetsPath = Join-Path (Split-Path $UI_PROJ -Parent) "obj\project.assets.json"
        if (Test-Path $assetsPath) {
            try {
                $assets = Get-Content $assetsPath -Raw | ConvertFrom-Json
                # One target framework in this project; take the first.
                # NOTE: PSObject.Properties is a PSMemberInfoCollection and indexes by
                # NAME, not position — `.Properties[0]` returns $null, and `foreach`
                # over $null iterates zero times, so the whole guard silently PASSED.
                # That is exactly how it was written first, and the negative control
                # is the only reason it was caught. Hence the $checked assertion below.
                $graph = ($assets.targets.PSObject.Properties | Select-Object -First 1).Value
                # Consumer package -> the dependency whose version it dictates.
                $watch = @{ 'Avalonia.Skia' = 'SkiaSharp'; 'Avalonia.HarfBuzz' = 'HarfBuzzSharp' }
                $drift = @()
                $checked = 0
                foreach ($entry in $graph.PSObject.Properties) {
                    $name = $entry.Name.Split('/')[0]
                    if (-not $watch.ContainsKey($name)) { continue }
                    $dep = $watch[$name]
                    $wantedProp = $entry.Value.dependencies.PSObject.Properties[$dep]
                    if (-not $wantedProp) { continue }
                    $wanted = $wantedProp.Value
                    # What actually resolved for that dependency.
                    $resolved = ($graph.PSObject.Properties |
                        Where-Object { $_.Name.Split('/')[0] -eq $dep } |
                        ForEach-Object { $_.Name.Split('/')[1] } | Select-Object -First 1)
                    $checked++
                    if ($resolved -and $wanted -and $resolved -ne $wanted) {
                        $drift += [pscustomobject]@{
                            Consumer = $entry.Name; Dep = $dep; Wanted = $wanted; Resolved = $resolved
                        }
                    }
                }
                if ($drift.Count -gt 0) {
                    Write-Fail "Native renderer package drift — this is what crashed the UI at build 3122:"
                    $drift | ForEach-Object {
                        Write-Host ("      {0} was built against {1} {2}, but {3} resolved" `
                            -f $_.Consumer, $_.Dep, $_.Wanted, $_.Resolved) -ForegroundColor Red
                    }
                    Write-Host "      => Set <SkiaSharpVersion> / <HarfBuzzSharpVersion> in" -ForegroundColor Yellow
                    Write-Host "         ui/UE5DumpUI/UE5DumpUI.csproj to the 'built against' version." -ForegroundColor Yellow
                    Write-Host "         NuGet cannot warn about this: Avalonia's constraint is an" -ForegroundColor Yellow
                    Write-Host "         open-ended minimum, so a different MAJOR satisfies it." -ForegroundColor Yellow
                    $exitCode = 1
                }
                elseif ($checked -lt $watch.Count) {
                    # A check that examined nothing must FAIL, not pass. The first
                    # version of this guard compared zero pairs and cheerfully
                    # reported success; only reverting the fix and watching it stay
                    # green exposed it. If Avalonia is ever split or renamed, this
                    # says so instead of quietly protecting nothing.
                    Write-Fail ("Renderer drift guard checked only $checked of $($watch.Count) " +
                                "package pairs — it is not actually guarding anything.")
                    Write-Host "      => The consumer package names in `$watch no longer match the" -ForegroundColor Yellow
                    Write-Host "         restore graph (Avalonia.Skia / Avalonia.HarfBuzz). Fix them." -ForegroundColor Yellow
                    $exitCode = 1
                }
                else {
                    Write-Ok "Native renderer packages match what Avalonia was built against ($checked pairs)"
                }
            }
            catch {
                # A parse failure must not silently pass the guard — an unchecked
                # invariant that reports success is worse than no invariant.
                Write-Fail "Could not read $assetsPath to check renderer package drift: $_"
                $exitCode = 1
            }
        }
    }

    if ($exitCode -eq 0) {
        $publishDir = Join-Path $DIST_DIR "publish"

        if ($Mode -eq "Publish") {
            # ===== Publish mode: Native AOT =====
            # Produces a lean native EXE + a few native DLLs (SkiaSharp, HarfBuzz, ANGLE).
            # No .NET runtime bundled — much smaller than self-contained single-file.
            Write-Step "Publishing UE5DumpUI (Native AOT, Release)..."

            # IlcUseEnvironmentalTools: use the MSVC linker already on PATH (loaded by
            # Enter-VsDevEnvironment above) instead of letting ILCompiler run its own
            # findvcvarsall.bat discovery. That discovery captures cmd.exe stdout to
            # locate link.exe, so when the inherited environment is bloated (e.g. this
            # shell already ran another repo's vcvars/DevShell publish, stacking
            # PATH/LIB/INCLUDE), cmd.exe emits "The input line is too long." and that
            # text leaks into the linker path -> link.exe exits 123. Using the
            # environmental tools skips that fragile step entirely.
            & dotnet publish $UI_PROJ `
                -c Release `
                -r win-x64 `
                -p:PublishAot=true `
                -p:IlcUseEnvironmentalTools=true `
                -o $publishDir `
                --nologo

            if ($LASTEXITCODE -ne 0) {
                Write-Fail "UI AOT publish failed"
                $exitCode = 1
            }
            else {
                $exeFile = Get-ChildItem -Path $publishDir -Filter "UE5DumpUI.exe" -ErrorAction SilentlyContinue |
                           Select-Object -First 1

                if ($exeFile) {
                    # Copy EXE + native DLLs only — Publish dists ship without
                    # *.pdb symbols (keeps the distribution lean).
                    Get-ChildItem -Path $publishDir -File |
                        Where-Object { $_.Extension -in ".exe", ".dll" } |
                        ForEach-Object { Copy-Item $_.FullName -Destination $DIST_DIR -Force }

                    Remove-Item $publishDir -Recurse -Force -ErrorAction SilentlyContinue

                    $exeSize = Get-FileSize (Join-Path $DIST_DIR "UE5DumpUI.exe")
                    Write-Ok "UE5DumpUI.exe ($exeSize)"
                }
                else {
                    Write-Fail "No build output found"
                    $exitCode = 1
                }
            }
        }
        else {
            # ===== Debug / Release mode: self-contained single-file =====
            # Bundles .NET runtime + all managed/native DLLs into one EXE (~96 MB).
            # Convenient for dev iteration — no AOT compile step.
            $publishConfig = if ($Mode -eq "Debug") { "Debug" } else { "Release" }
            Write-Step "Publishing UE5DumpUI ($publishConfig, self-contained single-file)..."

            & dotnet publish $UI_PROJ `
                -c $publishConfig `
                -r win-x64 `
                --self-contained `
                -p:PublishSingleFile=true `
                -p:PublishAot=false `
                -p:IncludeNativeLibrariesForSelfExtract=true `
                -p:IncludeAllContentForSelfExtract=true `
                -o $publishDir `
                --nologo

            if ($LASTEXITCODE -ne 0) {
                Write-Fail "UI publish failed"
                $exitCode = 1
            }
            else {
                $exeFile = Get-ChildItem -Path $publishDir -Filter "UE5DumpUI.exe" -ErrorAction SilentlyContinue |
                           Select-Object -First 1

                if ($exeFile) {
                    Copy-Item $exeFile.FullName -Destination $DIST_DIR -Force

                    if ($Mode -eq "Debug") {
                        $pdb = Join-Path $exeFile.DirectoryName "UE5DumpUI.pdb"
                        if (Test-Path $pdb) { Copy-Item $pdb -Destination $DIST_DIR -Force }
                    }

                    Remove-Item $publishDir -Recurse -Force -ErrorAction SilentlyContinue

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

    # Ensure the C++ build dir is configured so the test targets exist. For
    # `build test` run on its own (no DLL build this invocation) the dir may be
    # empty/unconfigured — configure it now (cheap; only the requested test
    # targets are compiled below, not the DLLs).
    # Removes the build dir when its header-dep tracking is dead, which also clears
    # CMakeCache.txt and so makes the configure below run. No-op when healthy.
    Repair-NinjaHeaderDeps

    if (-not (Test-Path (Join-Path $BUILD_DIR "CMakeCache.txt"))) {
        Write-Step "Configuring CMake for tests (Ninja + MSVC)..."
        $testCfgOk = Invoke-CmdInVsEnv "cmake -S `"$ROOT_DIR`" -B `"$BUILD_DIR`" -G Ninja -DCMAKE_BUILD_TYPE=$CppConfig -DBUILD_PROXY_DLL=ON -DBUILD_PROXY_DINPUT8=ON -DBUILD_PROXY_DXGI=ON -DBUILD_PROXY_WINMM=ON"
        if (-not $testCfgOk) {
            Write-Fail "CMake configure (tests) failed"
            $exitCode = 1
        }
    }

    # ----- C++ self-tests -----
    # Build + run before the C# tests so a regression in the surrogate /
    # UTF-8 logic surfaces immediately rather than being masked by a
    # downstream pipe / serialization failure. Always trigger a cmake
    # build of the test target — Ninja is a no-op when the source is
    # unchanged, and re-runs the compilation when a header or the test
    # cpp is touched.
    #
    # Both targets go through the SAME Invoke-CppSelfTest helper on purpose;
    # see its comment for why this used to be two hand-copied blocks.
    if (-not (Invoke-CppSelfTest -TargetName "utf8_helpers_test" -BuildDir $BUILD_DIR -Config $CppConfig)) {
        $exitCode = 1
    }

    # dll_helpers_test covers the pure helpers (Renge::TryStrToAddr, Scharf)
    # and compiles Radar.cpp / Denken.cpp as sources.
    if (-not (Invoke-CppSelfTest -TargetName "dll_helpers_test" -BuildDir $BUILD_DIR -Config $CppConfig)) {
        $exitCode = 1
    }

    # ----- C# tests -----
    # A missing test csproj is NOT a skip: it is checked into the repo, so its
    # absence means a broken tree or a wrong path, and reporting it as a skip
    # leaves a green exit code with zero C# tests run. Same defect class as
    # AD1/AD2 above, found by grepping this file for siblings at fix time.
    if (-not (Test-Path $TEST_PROJ)) {
        Write-Fail "C# test project not found at $TEST_PROJ - no C# tests ran (this is a failure, not a skip)"
        $exitCode = 1
    }
    else {
        Write-Step "Building + running tests..."

        # Guard: a NuGet "Update all packages" pass keeps re-adding explicit
        # Microsoft.Testing.Platform / Microsoft.Testing.Extensions.* pins to the
        # test csproj. Those override xunit.v3's bundled MTP bridge (compiled
        # against a specific MTP API surface) and crash the runner at startup with
        # 'Zero tests ran', exit -532462766, MissingMethodException on
        # IOutputDevice.DisplayAsync. Catch it HERE — before dotnet test — so the
        # failure is a clear message instead of a cryptic exit code. xunit.v3
        # resolves the whole Microsoft.Testing.* stack transitively; it must NOT
        # be pinned (see the comment in UE5DumpUI.Tests.csproj).
        $forbiddenMtp = Select-String -Path $TEST_PROJ -Pattern 'PackageReference\s+Include="Microsoft\.Testing\.(Platform|Extensions)' -ErrorAction SilentlyContinue
        # Same sweep, same file, different casualty: it also re-adds SkiaSharp.* /
        # HarfBuzzSharp.* pins here. They are Linux/WASM native assets that never
        # load on Windows, so they break nothing TODAY — but they sat at 4.151.1 /
        # 14.2.1.2 while the app moved to 3.119.4 / 8.3.1.3, i.e. they are a second
        # copy of a version that is now wrong, in a file whose own comment says not
        # to pin them. The next person to read them as authoritative is the problem.
        $forbiddenNative = Select-String -Path $TEST_PROJ -Pattern 'PackageReference\s+Include="(SkiaSharp|HarfBuzzSharp)' -ErrorAction SilentlyContinue
        if ($forbiddenMtp -or $forbiddenNative) {
            if ($forbiddenMtp) {
                Write-Fail "Forbidden explicit Microsoft.Testing.* PackageReference(s) in the test csproj:"
                $forbiddenMtp | ForEach-Object { Write-Host "      line $($_.LineNumber): $($_.Line.Trim())" -ForegroundColor Red }
                Write-Host "      => Remove them. xunit.v3 resolves Microsoft.Testing.* transitively;" -ForegroundColor Yellow
                Write-Host "         explicit pins crash the runner (MissingMethodException" -ForegroundColor Yellow
                Write-Host "         IOutputDevice.DisplayAsync). See the comment in the csproj." -ForegroundColor Yellow
            }
            if ($forbiddenNative) {
                Write-Fail "Forbidden explicit SkiaSharp/HarfBuzzSharp PackageReference(s) in the test csproj:"
                $forbiddenNative | ForEach-Object { Write-Host "      line $($_.LineNumber): $($_.Line.Trim())" -ForegroundColor Red }
                Write-Host "      => Remove them. They flow in from the UE5DumpUI ProjectReference," -ForegroundColor Yellow
                Write-Host "         which owns the version via <SkiaSharpVersion>/<HarfBuzzSharpVersion>." -ForegroundColor Yellow
                Write-Host "         A second copy here silently disagrees with the app's." -ForegroundColor Yellow
            }
            $exitCode = 1
        }
        else {
            # .NET 10 SDK + Microsoft.Testing.Platform 2.x: global.json's
            # `test.runner = Microsoft.Testing.Platform` switches dotnet test
            # into MTP mode. Differences from the legacy VSTest mode:
            #   - Project passed via --project (positional no longer works).
            #   - `--nologo` / `-v` aren't in MTP's allowed flag list;
            #     unknown flags get forwarded to the test runner (xunit.v3),
            #     which doesn't recognize them and prints help + exits 5.
            #   - VSTest bridge target was dropped in MTP 2.x on .NET 10.
            # Build args via splatting so an absent --no-restore is OMITTED, not
            # passed as an empty string — MTP's CommandLineParser throws
            # IndexOutOfRangeException on a stray empty arg ("Zero tests ran").
            $testArgs = @('test', '--project', $TEST_PROJ, '-c', $CSharpConfig)
            if ($SkipRestore) { $testArgs += '--no-restore' }
            & dotnet @testArgs

            if ($LASTEXITCODE -ne 0) {
                Write-Fail "Tests failed"
                $exitCode = 1
            }
            else {
                Write-Ok "All tests passed"
            }
        }
    }
}

# ============================================================
# Copy CE Lua scripts
# ============================================================

if ($Target -in "All", "DLL") {
    Write-Step "Copying CE scripts to dist\..."
    # CT goes in the same folder as DLL + EXE so it can auto-detect the DLL path
    $src = Join-Path $ROOT_DIR "scripts\UE5CEDumper.CT"
    if (Test-Path $src) {
        Copy-Item $src -Destination $DIST_DIR -Force
        Write-Ok "UE5CEDumper.CT copied to dist\"
    }
    # ue5_dissect.lua — standalone CE Structure Dissect builder (optional, for advanced users)
    $dissectSrc = Join-Path $ROOT_DIR "scripts\ue5_dissect.lua"
    if (Test-Path $dissectSrc) {
        Copy-Item $dissectSrc -Destination $DIST_DIR -Force
        Write-Ok "ue5_dissect.lua copied to dist\"
    }
    # inject-ue.ps1 — standalone command-line injector. Goes next to UE5Dumper.dll
    # so its default DLL resolution ("<script dir>\UE5Dumper.dll") just works.
    $injectSrc = Join-Path $ROOT_DIR "scripts\inject-ue.ps1"
    if (Test-Path $injectSrc) {
        Copy-Item $injectSrc -Destination $DIST_DIR -Force
        Write-Ok "inject-ue.ps1 copied to dist\"
    }
    # README.html — deployment guide for end users (covers CE inject + Proxy DLL)
    $readmeSrc = Join-Path $ROOT_DIR "scripts\DEPLOY_README.html"
    if (Test-Path $readmeSrc) {
        Copy-Item $readmeSrc -Destination (Join-Path $DIST_DIR "README.html") -Force
        # Remove any stale markdown README from an earlier non-clean build.
        $staleMd = Join-Path $DIST_DIR "README.md"
        if (Test-Path $staleMd) { Remove-Item $staleMd -Force -ErrorAction SilentlyContinue }
        Write-Ok "deployment README.html copied to dist\"
    }
    # Feature-Guide.html — browsable stable-feature walkthrough (companion to
    # README.html's Quick Start; experimental tabs intentionally omitted).
    $guideSrc = Join-Path $ROOT_DIR "scripts\Feature-Guide.html"
    if (Test-Path $guideSrc) {
        Copy-Item $guideSrc -Destination (Join-Path $DIST_DIR "Feature-Guide.html") -Force
        Write-Ok "Feature-Guide.html copied to dist\"
    }
    # build_number.txt — build version tracking
    $buildNumSrc = Join-Path $ROOT_DIR "build_number.txt"
    if (Test-Path $buildNumSrc) {
        Copy-Item $buildNumSrc -Destination $DIST_DIR -Force
        Write-Ok "build_number.txt copied to dist\"
    }
}

# ============================================================
# (removed 2026-08-20) The Startup-shortcut helpers are no longer published.
# ============================================================
# `scripts/startup-shortcut.ps1` and `scripts/startup_shortcut.py` used to be
# copied into dist\ beside UE5DumpUI.exe. They are no longer part of the
# distribution. Both still live in scripts\ and still work when run from there —
# only the publish step is gone, so nothing about the tool itself changed.

# ============================================================
# Strip debug symbols from distribution builds
# ============================================================
# Release/Publish dists ship WITHOUT *.pdb. Sweep the whole dist tree (incl.
# dist\proxy) so any stale symbols from a prior non-clean build are removed too.
# Debug keeps them for local crash diagnostics.
if ($Mode -ne "Debug" -and (Test-Path $DIST_DIR)) {
    $pdbs = @(Get-ChildItem -Path $DIST_DIR -Recurse -Filter "*.pdb" -File -ErrorAction SilentlyContinue)
    if ($pdbs.Count -gt 0) {
        $pdbs | Remove-Item -Force -ErrorAction SilentlyContinue
        Write-Ok "stripped $($pdbs.Count) *.pdb from dist\ (Release/Publish ship without symbols)"
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

if ($script:transcriptActive) {
    try { Stop-Transcript | Out-Null } catch { }
}
exit $exitCode
