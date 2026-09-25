<#
.SYNOPSIS
    UE5CEDumper command-line injector — list running UE4/UE5 games and inject
    UE5Dumper.dll via classic CreateRemoteThread + LoadLibraryW.

.DESCRIPTION
    One combined CLI (list + inject + auto):

      .\inject-ue.ps1                 # AUTO: find UE processes. 1 -> inject it;
                                      #       0 -> abort; 2+ -> list them + abort.
      .\inject-ue.ps1 -List           # list UE processes and exit (no inject)
      .\inject-ue.ps1 -ProcessId 1234 # inject into a specific PID
      .\inject-ue.ps1 -All            # with -List, show ALL x64 processes (not just UE)

    UE detection is by the process's executable path (the same heuristics the UI's
    drive scan uses): a `*-Shipping.exe` name, or an exe under `\Binaries\Win64\`
    with an `Engine` folder / `Content\Paks` up the tree. x64 targets only
    (UE5Dumper.dll is x64) — 32-bit targets are reported and skipped.

    The DLL to inject is resolved (unless -Dll is given) from, in order:
      <script dir>\UE5Dumper.dll, <script dir>\..\dist\UE5Dumper.dll,
      <script dir>\dist\UE5Dumper.dll, <cwd>\UE5Dumper.dll.

.PARAMETER ProcessId
    Target process id. 0 (default) = auto-pick the single running UE process.

.PARAMETER List
    List detected UE processes and exit without injecting.

.PARAMETER All
    With -List, list every accessible x64 process (not only UE ones).

.PARAMETER Dll
    Path to the DLL to inject. Defaults to UE5Dumper.dll near the script / dist.

.PARAMETER Force
    Inject even if UE5Dumper.dll already appears loaded in the target.

.EXAMPLE
    .\inject-ue.ps1
    .\inject-ue.ps1 -List
    .\inject-ue.ps1 -ProcessId 12588
#>
[CmdletBinding()]
param(
    [int]$ProcessId = 0,
    [switch]$List,
    [switch]$All,
    [string]$Dll,
    [switch]$Force,
    [switch]$Elevated   # internal: set on the self-relaunched elevated run (prevents a re-loop)
)

$ErrorActionPreference = 'Stop'

# ── Injection P/Invoke (classic DllImport; blittable — no LibraryImport needed) ──
if (-not ('UeInject.Injector' -as [type])) {
    Add-Type -Namespace 'UeInject' -Name 'Injector' -Language CSharp -MemberDefinition @'
    const uint PROCESS_ALL_ACCESS = 0x1F0FFF;
    const uint MEM_COMMIT_RESERVE = 0x3000;
    const uint MEM_RELEASE        = 0x8000;
    const uint PAGE_READWRITE     = 0x04;
    const uint WAIT_OBJECT_0      = 0x0;
    const uint TIMEOUT_MS         = 10000;

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError=true)]
    static extern System.IntPtr OpenProcess(uint access, [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] bool inherit, int pid);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError=true)]
    static extern System.IntPtr VirtualAllocEx(System.IntPtr h, System.IntPtr addr, System.UIntPtr size, uint type, uint protect);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError=true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    static extern bool VirtualFreeEx(System.IntPtr h, System.IntPtr addr, System.UIntPtr size, uint type);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError=true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    static extern bool WriteProcessMemory(System.IntPtr h, System.IntPtr addr, byte[] buf, System.UIntPtr size, out System.UIntPtr written);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet=System.Runtime.InteropServices.CharSet.Unicode, SetLastError=true, EntryPoint="GetModuleHandleW")]
    static extern System.IntPtr GetModuleHandle(string name);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet=System.Runtime.InteropServices.CharSet.Ansi, SetLastError=true, EntryPoint="GetProcAddress")]
    static extern System.IntPtr GetProcAddress(System.IntPtr h, string name);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError=true)]
    static extern System.IntPtr CreateRemoteThread(System.IntPtr h, System.IntPtr sa, System.UIntPtr stack, System.IntPtr start, System.IntPtr param, uint flags, System.IntPtr tid);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError=true)]
    static extern uint WaitForSingleObject(System.IntPtr h, uint ms);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError=true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    static extern bool GetExitCodeThread(System.IntPtr h, out uint code);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError=true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    static extern bool CloseHandle(System.IntPtr h);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError=true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    static extern bool IsWow64Process(System.IntPtr h, [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] out bool wow64);

    // Returns "" (empty) + hmod on success; a non-empty error string on failure.
    public static string Inject(int pid, string dllPath, out uint hmod) {
        hmod = 0;
        if (!System.IO.File.Exists(dllPath)) return "DLL not found: " + dllPath;
        System.IntPtr hProc = OpenProcess(PROCESS_ALL_ACCESS, false, pid);
        if (hProc == System.IntPtr.Zero)
            return "OpenProcess failed (Win32 " + System.Runtime.InteropServices.Marshal.GetLastWin32Error() + "). If the game runs elevated, run this as Administrator.";
        try {
            bool wow64;
            if (IsWow64Process(hProc, out wow64) && wow64)
                return "Target PID " + pid + " is a 32-bit process; UE5Dumper.dll is x64-only.";

            byte[] pathBytes = System.Text.Encoding.Unicode.GetBytes(dllPath + "\0");
            System.UIntPtr size = (System.UIntPtr)pathBytes.Length;
            System.IntPtr remote = VirtualAllocEx(hProc, System.IntPtr.Zero, size, MEM_COMMIT_RESERVE, PAGE_READWRITE);
            if (remote == System.IntPtr.Zero)
                return "VirtualAllocEx failed (Win32 " + System.Runtime.InteropServices.Marshal.GetLastWin32Error() + ").";
            try {
                System.UIntPtr written;
                if (!WriteProcessMemory(hProc, remote, pathBytes, size, out written))
                    return "WriteProcessMemory failed (Win32 " + System.Runtime.InteropServices.Marshal.GetLastWin32Error() + ").";

                System.IntPtr k32 = GetModuleHandle("kernel32.dll");
                System.IntPtr loadLib = (k32 == System.IntPtr.Zero) ? System.IntPtr.Zero : GetProcAddress(k32, "LoadLibraryW");
                if (loadLib == System.IntPtr.Zero) return "Could not resolve LoadLibraryW.";

                System.IntPtr hThread = CreateRemoteThread(hProc, System.IntPtr.Zero, System.UIntPtr.Zero, loadLib, remote, 0, System.IntPtr.Zero);
                if (hThread == System.IntPtr.Zero)
                    return "CreateRemoteThread failed (Win32 " + System.Runtime.InteropServices.Marshal.GetLastWin32Error() + ").";
                try {
                    uint w = WaitForSingleObject(hThread, TIMEOUT_MS);
                    if (w != WAIT_OBJECT_0) return "Remote thread did not finish in 10s (wait 0x" + w.ToString("X8") + ").";
                    uint code;
                    if (!GetExitCodeThread(hThread, out code)) return "GetExitCodeThread failed.";
                    if (code == 0) return "LoadLibraryW returned NULL — DLL failed to load. Check %LOCALAPPDATA%\\UE5CEDumper\\Logs.";
                    hmod = code;
                    return "";
                } finally { CloseHandle(hThread); }
            } finally { VirtualFreeEx(hProc, remote, System.UIntPtr.Zero, MEM_RELEASE); }
        } finally { CloseHandle(hProc); }
    }
'@
}

# ── UE detection (mirrors the UI drive-scan LooksLikeUeGameRoot heuristics) ──
$script:SkipExe = @('CrashReportClient.exe','UnrealEditor.exe','UE4Editor.exe',
                    'UnrealFrontend.exe','UnrealCEFSubProcess.exe','EpicWebHelper.exe',
                    'UE5DumpUI.exe')

function Test-UeProcess([string]$exePath, [string]$name) {
    if ([string]::IsNullOrEmpty($exePath)) { return $false }
    if ($script:SkipExe -contains $name) { return $false }

    # High-confidence: cooked shipping executable naming.
    if ($name -match '(?i)-Shipping\.exe$') { return $true }

    # Structural: exe under \Binaries\Win64\ with an Engine folder or Content\Paks up the tree.
    if ($exePath -match '(?i)\\Binaries\\Win64\\') {
        $bin  = Split-Path $exePath -Parent              # ...\<Project>\Binaries\Win64
        $proj = Split-Path (Split-Path $bin -Parent) -Parent   # ...\<Project>
        $root = Split-Path $proj -Parent                 # ...\<GameRoot>
        try {
            if ($proj -and (Test-Path (Join-Path $proj 'Content\Paks'))) { return $true }
            if ($root -and (Test-Path (Join-Path $root 'Engine')))       { return $true }
        } catch { }
        return $true   # under Binaries\Win64 is itself a strong UE signal
    }
    return $false
}

# Enumerate accessible processes with their exe path (best-effort; skip protected).
function Get-CandidateProcesses {
    $out = @()
    foreach ($p in Get-Process) {
        $path = $null
        try { $path = $p.Path } catch { $path = $null }   # protected/system -> no path
        if (-not $path) { continue }
        $leaf = Split-Path $path -Leaf                     # real exe name (with .exe)
        $out += [pscustomobject]@{
            PID  = $p.Id
            Name = $leaf
            IsUe = (Test-UeProcess $path $leaf)
            Path = $path
            Proc = $p
        }
    }
    return $out
}

function Test-AlreadyInjected($proc, [string]$dllLeaf) {
    try {
        foreach ($m in $proc.Modules) {
            if ([string]::Equals($m.ModuleName, $dllLeaf, [System.StringComparison]::OrdinalIgnoreCase)) { return $true }
        }
    } catch { }   # can't read modules -> assume not injected
    return $false
}

# [PATH-PS1-LITERALPATH] -LiteralPath throughout: a positional path binds to -Path, which expands wildcards, so a
# folder like 'UE5CEDumper [3554]' (a character class) never matched the DLL sitting in it.
function Resolve-Dll {
    if ($Dll) {
        if (Test-Path -LiteralPath $Dll) { return (Resolve-Path -LiteralPath $Dll).Path }
        throw "DLL not found: $Dll"
    }
    $root = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
    $candidates = @(
        (Join-Path $root 'UE5Dumper.dll'),
        (Join-Path $root '..\dist\UE5Dumper.dll'),
        (Join-Path $root 'dist\UE5Dumper.dll'),
        (Join-Path (Get-Location).Path 'UE5Dumper.dll')
    )
    foreach ($c in $candidates) { if (Test-Path -LiteralPath $c) { return (Resolve-Path -LiteralPath $c).Path } }
    throw "UE5Dumper.dll not found near the script. Pass -Dll <path> (e.g. dist\UE5Dumper.dll)."
}

# ── Main ────────────────────────────────────────────────────────────────
$procs = Get-CandidateProcesses
$ue    = $procs | Where-Object { $_.IsUe }

if ($List) {
    $show = if ($All) { $procs } else { $ue }
    if (-not $show -or $show.Count -eq 0) {
        Write-Host ("No {0}processes found." -f ($(if ($All) { '' } else { 'UE ' })))
        exit 0
    }
    $show | Sort-Object Name | Format-Table PID, Name, @{L='UE';E={ if ($_.IsUe) {'yes'} else {'no'} }}, Path -AutoSize
    exit 0
}

# Resolve DLL (needed for injection).
try { $dllPath = Resolve-Dll } catch { Write-Host $_.Exception.Message -ForegroundColor Red; exit 1 }
$dllLeaf = Split-Path $dllPath -Leaf

# Choose the target.
$target = $null
if ($ProcessId -ne 0) {
    $target = $procs | Where-Object { $_.PID -eq $ProcessId } | Select-Object -First 1
    if (-not $target) {
        # Not in our accessible list — still try (the user was explicit); build a stub.
        try {
            $p = Get-Process -Id $ProcessId -ErrorAction Stop
            $path = $null; try { $path = $p.Path } catch { }
            $nm = if ($path) { Split-Path $path -Leaf } else { $p.ProcessName }
            $target = [pscustomobject]@{ PID=$p.Id; Name=$nm; IsUe=$false; Path=$path; Proc=$p }
        } catch {
            Write-Host "No process with PID $ProcessId is running." -ForegroundColor Red; exit 1
        }
    }
    if (-not $target.IsUe) {
        Write-Host "[warn] PID $($target.PID) ($($target.Name)) does not look like a UE game — injecting anyway (you asked for this PID)." -ForegroundColor Yellow
    }
}
else {
    # AUTO: exactly one UE process -> inject; 0 -> abort; 2+ -> list + abort.
    if ($ue.Count -eq 0) {
        Write-Host "No running UE4/UE5 process found. Start the game first, or pass -ProcessId <pid>." -ForegroundColor Red
        exit 1
    }
    if ($ue.Count -gt 1) {
        Write-Host "Multiple UE processes found — specify one with -ProcessId:" -ForegroundColor Yellow
        $ue | Sort-Object Name | Format-Table PID, Name, Path -AutoSize
        exit 2
    }
    $target = $ue[0]
}

Write-Host ("Target : PID {0}  {1}" -f $target.PID, $target.Name)
Write-Host ("DLL    : {0}" -f $dllPath)

if (-not $Force -and (Test-AlreadyInjected $target.Proc $dllLeaf)) {
    Write-Host "[info] $dllLeaf already loaded in PID $($target.PID) — nothing to do (use -Force to re-inject)." -ForegroundColor Cyan
    exit 0
}

[uint32]$hmod = 0
$err = [UeInject.Injector]::Inject($target.PID, $dllPath, [ref]$hmod)

# Auto-elevate on Access Denied (game runs as Administrator) — relaunch this script
# elevated (one UAC prompt). Guarded so it never re-loops and never fires when we
# already are admin.
if (-not [string]::IsNullOrEmpty($err) -and $err -match 'Win32 5\)' -and -not $Elevated) {
    $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $isAdmin) {
        Write-Host "[info] Access denied — the game may be running as Administrator. Relaunching elevated (accept the UAC prompt)..." -ForegroundColor Yellow
        $psExe = (Get-Process -Id $PID).Path
        # [PATH-PS1-ELEVATE-QUOTE] Start-Process joins -ArgumentList with single spaces and quotes nothing (its docs:
        # a value containing a space needs escaped double quotes), so a path with a space -- C:\Program Files, a
        # profile, 'DragonSword  Awakening' -- split in the elevated child. A Windows path cannot hold a double quote.
        $argList = @('-NoProfile','-ExecutionPolicy','Bypass','-File', ('"{0}"' -f $PSCommandPath),
                     '-ProcessId', "$($target.PID)", '-Dll', ('"{0}"' -f $dllPath), '-Elevated')
        try {
            $p = Start-Process -FilePath $psExe -Verb RunAs -ArgumentList $argList -PassThru -Wait -ErrorAction Stop
            if ($p.ExitCode -eq 0) {
                Write-Host ("[ok] Injected {0} into PID {1} (elevated). Launch UE5DumpUI.exe and Connect." -f $dllLeaf, $target.PID) -ForegroundColor Green
            } else {
                Write-Host ("[fail] Elevated inject failed (exit {0})." -f $p.ExitCode) -ForegroundColor Red
            }
            exit $p.ExitCode
        } catch {
            Write-Host ("[fail] Elevation cancelled or failed: {0}" -f $_.Exception.Message) -ForegroundColor Red
            exit 1
        }
    }
}

if ([string]::IsNullOrEmpty($err)) {
    Write-Host ("[ok] Injected {0} into PID {1} (HMODULE=0x{2:X})." -f $dllLeaf, $target.PID, $hmod) -ForegroundColor Green
    Write-Host "The DLL starts its pipe server automatically. Launch UE5DumpUI.exe and Connect."
    exit 0
}
else {
    Write-Host ("[fail] {0}" -f $err) -ForegroundColor Red
    exit 1
}
