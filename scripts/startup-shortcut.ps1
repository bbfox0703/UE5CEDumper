<#
.SYNOPSIS
    Create, remove or inspect a Start Menu **Startup** shortcut for UE5DumpUI.exe,
    so the UI launches automatically when the current user signs in.

.DESCRIPTION
    One combined CLI (status + install + remove):

      .\startup-shortcut.ps1              # STATUS (default) — read-only, changes nothing
      .\startup-shortcut.ps1 install      # create the shortcut
      .\startup-shortcut.ps1 remove       # delete it
      .\startup-shortcut.ps1 install -Minimized

    A stdlib-Python twin lives beside this file as startup_shortcut.py — same
    verbs, same exit codes, same refusals. It exists because Bitdefender's
    Advanced Threat Defense quarantined THIS script the first time it ran: the
    detection was behavioural, not a signature — an unsigned parent process
    spawning pwsh spawning powershell, which then wrote a .lnk into the Startup
    folder. That is a textbook persistence shape and an AV is right to look at
    it. Neither script tries to look like anything else; if yours trips too, the
    honest fix is a folder exclusion, not a workaround.

    CURRENT USER ONLY, by design. The shortcut goes in the per-user Startup
    folder resolved from the shell itself
    ([Environment]::GetFolderPath('Startup')) rather than a hardcoded
    %APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup — that path is
    localised on non-English Windows and relocatable by policy. The all-users
    Startup folder is deliberately NOT supported: writing there needs
    elevation, and a debugging tool that silently starts for every account on
    the machine is not a default anyone asked for.

    The target executable is resolved (unless -ExePath is given) from, in order:
      <script dir>\UE5DumpUI.exe, <script dir>\..\dist\UE5DumpUI.exe,
      <script dir>\dist\UE5DumpUI.exe, <cwd>\UE5DumpUI.exe.
    The first entry is the shipped case: build.ps1 copies this script into
    dist\ next to the exe, so a user who unzips the release and runs it here
    needs no arguments. The rest let it work straight out of the repo, and
    match how inject-ue.ps1 finds UE5Dumper.dll.

    NO PIPE, NO NETWORK. Install is exactly: resolve the path, confirm the file
    exists, write the .lnk, read it back. The UI and the DLL do not need to be
    running. (xref_probe.ps1 is the script in this folder that does connect.)

    The resolved target is always printed before anything is written, and the
    shortcut is read back after Save() to confirm what actually landed —
    CreateShortcut().Save() reports success by not throwing, which is not the
    same as having written what you asked for.

    Safety: install refuses to overwrite a shortcut of the same name that
    points somewhere else, and remove refuses to delete one whose target is
    not a UE5DumpUI.exe. Both say what they found and both are overridable
    with -Force. Re-running install against the same target just rewrites it.

    Windows PowerShell 5.1 compatible (no ternary, no null-coalescing) — an
    end user will most likely reach this via
    `powershell -ExecutionPolicy Bypass -File startup-shortcut.ps1 install`,
    which is 5.1, not pwsh 7.

.PARAMETER Action
    Status (default), Install, or Remove. Positional, case-insensitive.

.PARAMETER ExePath
    Explicit path to UE5DumpUI.exe. Defaults to the search order above.

.PARAMETER Name
    Shortcut base name. Default 'UE5CEDumper'. A trailing .lnk is optional.

.PARAMETER Arguments
    Command-line arguments baked into the shortcut. Default none.

.PARAMETER Minimized
    Start minimized (shortcut WindowStyle 7) instead of normal (1).

.PARAMETER Force
    Overwrite / delete even when the existing shortcut points elsewhere.

.EXAMPLE
    .\startup-shortcut.ps1
    .\startup-shortcut.ps1 install
    .\startup-shortcut.ps1 install -Minimized -Name "UE5 Dumper"
    .\startup-shortcut.ps1 remove

.NOTES
    Exit codes: 0 = success / installed, 1 = error, 2 = not installed (status
    or remove), 3 = installed but the target no longer exists (status).
    2 and 3 are distinct so a wrapper can tell "never set up" from "set up and
    since broken" — the second is what a moved or re-extracted dist\ looks like.
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('Status', 'Install', 'Remove')]
    [string]$Action = 'Status',

    [string]$ExePath,
    [string]$Name = 'UE5CEDumper',
    [string]$Arguments = '',
    [switch]$Minimized,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$EXE_LEAF = 'UE5DumpUI.exe'

# ── Output helpers ──────────────────────────────────────────────────────
function Write-Ok   ([string]$m) { Write-Host "  [ok]   $m"   -ForegroundColor Green }
function Write-Info ([string]$m) { Write-Host "  [info] $m" }
function Write-Warn ([string]$m) { Write-Host "  [warn] $m"   -ForegroundColor Yellow }
function Write-Fail ([string]$m) { Write-Host "  [fail] $m"   -ForegroundColor Red }

# ── Path resolution ─────────────────────────────────────────────────────
function Resolve-Exe {
    if ($ExePath) {
        if (Test-Path -LiteralPath $ExePath -PathType Leaf) {
            return (Resolve-Path -LiteralPath $ExePath).Path
        }
        throw "Executable not found: $ExePath"
    }
    $root = $PSScriptRoot
    if (-not $root) { $root = (Get-Location).Path }
    $candidates = @(
        (Join-Path $root $EXE_LEAF),
        (Join-Path $root "..\dist\$EXE_LEAF"),
        (Join-Path $root "dist\$EXE_LEAF"),
        (Join-Path (Get-Location).Path $EXE_LEAF)
    )
    foreach ($c in $candidates) {
        if (Test-Path -LiteralPath $c -PathType Leaf) { return (Resolve-Path -LiteralPath $c).Path }
    }
    throw ("$EXE_LEAF not found near the script. Looked in:`n    " +
           (($candidates | ForEach-Object { $_ }) -join "`n    ") +
           "`n  Pass -ExePath <path>, or run this from the folder the release was unzipped into.")
}

function Get-StartupDir {
    # Ask the shell, never build the path from %APPDATA% — it is localised on
    # non-English Windows and can be redirected by Group Policy / folder redirection.
    $dir = [Environment]::GetFolderPath('Startup')
    if (-not $dir) {
        throw "Windows did not report a Startup folder for this account (GetFolderPath('Startup') was empty)."
    }
    if (-not (Test-Path -LiteralPath $dir -PathType Container)) {
        throw "Startup folder does not exist: $dir"
    }
    return $dir
}

function Get-ShortcutPath {
    $leaf = $Name
    if ($leaf -notmatch '\.lnk$') { $leaf = "$leaf.lnk" }
    foreach ($bad in [System.IO.Path]::GetInvalidFileNameChars()) {
        # IndexOf(char), not Contains(char) — the char overload of String.Contains
        # does not exist on .NET Framework, which is what Windows PowerShell 5.1 runs on.
        if ($leaf.IndexOf($bad) -ge 0) { throw "Invalid character in -Name: '$Name'" }
    }
    return (Join-Path (Get-StartupDir) $leaf)
}

# ── Shortcut I/O (WScript.Shell COM — the only supported way to author a .lnk) ──
function Read-Shortcut ([string]$Path) {
    # CreateShortcut() on an existing file LOADS it; nothing is written until Save().
    # So this is a safe read, and it is how both install and remove look before they leap.
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    $shell = New-Object -ComObject WScript.Shell
    try {
        $sc = $shell.CreateShortcut($Path)
        return [pscustomobject]@{
            Target      = $sc.TargetPath
            Arguments   = $sc.Arguments
            WorkingDir  = $sc.WorkingDirectory
            WindowStyle = $sc.WindowStyle
        }
    }
    finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell) }
}

function Write-Shortcut ([string]$Path, [string]$Target, [string]$Args,
                         [string]$WorkDir, [int]$Style) {
    $shell = New-Object -ComObject WScript.Shell
    try {
        $sc = $shell.CreateShortcut($Path)
        $sc.TargetPath       = $Target
        $sc.Arguments        = $Args
        # Without this the shortcut inherits the Startup folder as its working
        # directory, so anything the app resolves relative to itself (the .CT,
        # UE5Dumper.dll, the bundled native libs) is looked for in the wrong place.
        $sc.WorkingDirectory = $WorkDir
        $sc.IconLocation     = "$Target,0"
        $sc.Description      = 'UE5CEDumper - Unreal Engine object/offset dumper UI'
        $sc.WindowStyle      = $Style
        $sc.Save()
    }
    finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell) }
}

function Test-IsOurTarget ([string]$Target) {
    if (-not $Target) { return $false }
    return ([System.IO.Path]::GetFileName($Target) -ieq $EXE_LEAF)
}

# ── Actions ─────────────────────────────────────────────────────────────
function Invoke-Status {
    $link = Get-ShortcutPath
    Write-Info "Startup folder: $(Get-StartupDir)"
    Write-Info "Shortcut:       $link"

    $existing = Read-Shortcut $link
    if (-not $existing) {
        Write-Info "Not installed."
        Write-Host ""
        Write-Host "  Install with:  .\startup-shortcut.ps1 install"
        return 2
    }

    Write-Ok   "Installed."
    Write-Info "Target:         $($existing.Target)"
    if ($existing.Arguments)  { Write-Info "Arguments:      $($existing.Arguments)" }
    if ($existing.WorkingDir) { Write-Info "Working dir:    $($existing.WorkingDir)" }
    Write-Info ("Window:         " + $(if ($existing.WindowStyle -eq 7) { 'minimized' } else { 'normal' }))

    if (-not (Test-IsOurTarget $existing.Target)) {
        Write-Warn "That target is not $EXE_LEAF — this .lnk was not created by this script."
        return 3
    }
    if (-not (Test-Path -LiteralPath $existing.Target -PathType Leaf)) {
        # The common real-world break: the release folder was moved, renamed or
        # re-extracted elsewhere. Windows silently keeps the dead shortcut and the
        # UI just stops appearing at sign-in, with nothing to say why.
        Write-Warn "Target no longer exists — the shortcut is dead. Re-run 'install' from the current folder."
        return 3
    }
    Write-Ok "Target exists."
    return 0
}

function Invoke-Install {
    $exe  = Resolve-Exe
    $link = Get-ShortcutPath
    $dir  = Split-Path -Parent $exe

    Write-Info "Startup folder: $(Get-StartupDir)"
    Write-Info "Shortcut:       $link"
    Write-Info "Target:         $exe"

    $existing = Read-Shortcut $link
    if ($existing) {
        if ($existing.Target -ieq $exe) {
            Write-Info "A shortcut to this exact target already exists — rewriting it."
        }
        elseif ($Force) {
            Write-Warn "Overwriting a shortcut that pointed at: $($existing.Target)"
        }
        else {
            Write-Fail "'$([System.IO.Path]::GetFileName($link))' already exists and points at:"
            Write-Fail "    $($existing.Target)"
            Write-Fail "Refusing to overwrite it. Re-run with -Force, or pick another -Name."
            return 1
        }
    }

    $style = 1
    if ($Minimized) { $style = 7 }

    Write-Shortcut -Path $link -Target $exe -Args $Arguments -WorkDir $dir -Style $style

    # Read it back. Save() signals success by not throwing, which is not the same
    # as having written what was asked for — and a Startup shortcut that is wrong
    # gives no feedback until the next sign-in.
    $written = Read-Shortcut $link
    if (-not $written) {
        Write-Fail "Save() reported success but no shortcut is there: $link"
        return 1
    }
    if ($written.Target -ine $exe) {
        Write-Fail "Shortcut was written with the wrong target: $($written.Target)"
        return 1
    }
    Write-Ok "Installed — UE5DumpUI will start when $env:USERNAME signs in."
    if ($Minimized) { Write-Info "It will start minimized." }
    Write-Host ""
    Write-Host "  Remove with:  .\startup-shortcut.ps1 remove"
    return 0
}

function Invoke-Remove {
    $link = Get-ShortcutPath
    Write-Info "Startup folder: $(Get-StartupDir)"
    Write-Info "Shortcut:       $link"

    $existing = Read-Shortcut $link
    if (-not $existing) {
        Write-Info "Not installed — nothing to remove."
        return 2
    }

    # Look before deleting. -Name is user-supplied and the Startup folder holds
    # other people's shortcuts; deleting one we did not create would be silent
    # and, for anything else that lives there, not obviously recoverable.
    if (-not (Test-IsOurTarget $existing.Target)) {
        if (-not $Force) {
            Write-Fail "That shortcut points at:"
            Write-Fail "    $($existing.Target)"
            Write-Fail "which is not $EXE_LEAF, so this script did not create it. Refusing to delete."
            Write-Fail "Re-run with -Force if you are sure."
            return 1
        }
        Write-Warn "Deleting a shortcut this script did not create: $($existing.Target)"
    }

    Remove-Item -LiteralPath $link -Force
    if (Test-Path -LiteralPath $link) {
        Write-Fail "Delete reported success but the shortcut is still there: $link"
        return 1
    }
    Write-Ok "Removed — UE5DumpUI will no longer start at sign-in."
    return 0
}

# ── Main ────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "UE5CEDumper - Startup shortcut ($($Action.ToLower()))" -ForegroundColor Cyan
Write-Host ""

try {
    switch ($Action) {
        'Install' { $code = Invoke-Install }
        'Remove'  { $code = Invoke-Remove }
        default   { $code = Invoke-Status }
    }
}
catch {
    Write-Fail $_.Exception.Message
    $code = 1
}

Write-Host ""
exit $code
