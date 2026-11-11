# ============================================================
# xref_probe.ps1 — Headless verification client for find_property_xrefs (Path 1)
#
# Connects to the INJECTED DLL's named pipe (the DLL is the pipe SERVER; this is
# a CLIENT — the opposite of test_pipe.ps1 which mocks the server for the UI).
#
# Prereq: the game is running with UE5Dumper.dll injected and its pipe server
# started. Without the UI, inject via Cheat Engine + dist\UE5CEDumper.CT
# (the CT's Lua loadLibrary + the DLL auto-start thread runs Init + StartPipeServer).
# Wait until \\.\pipe\UE5DumpBfx_<game pid> exists, then run this with -PipeName UE5DumpBfx_<game pid>.
#
# Usage:
#   # 1) validate the Script offset derivation live + run xref on a known FProperty:
#   pwsh -File scripts\xref_probe.ps1 -PropAddr 0x1F2A3B4C5D60
#
#   # 2) discover a field's FProperty addr by class name, then xref it:
#   pwsh -File scripts\xref_probe.ps1 -ClassName BP_Door_C -Field bIsOpen
#
#   # 3) just list a class's fields (to grab an addr), no xref:
#   pwsh -File scripts\xref_probe.ps1 -ClassName BP_Door_C
# ============================================================

param(
    [string]$PropAddr  = "",          # FProperty* to xref (from walk_class field 'addr')
    [string]$ClassName = "",          # resolve this class via list_classes, then walk it
    [string]$Field     = "",          # with -ClassName: pick this field's addr and xref it
    [string]$Func      = "",          # with -ClassName: reverse edge — list props this function reads/writes
    [string]$FuncAddr  = "",          # UFunction* to run the reverse edge directly
    [bool]  $GameOnly  = $true,
    [int]   $Max       = 200,
    [string]$PipeName  = "",          # UE5DumpBfx_<game pid>: each game serves its own [PIPE-PER-PROCESS-2026-11-06]
    [int]   $TimeoutMs = 5000
)

$ErrorActionPreference = "Stop"
if (-not $PipeName) { throw "Pass -PipeName UE5DumpBfx_<game pid> (each game serves its own pipe)." }
$script:reqId = 0

function Connect-Pipe {
    $pipe = New-Object System.IO.Pipes.NamedPipeClientStream(".", $PipeName,
        [System.IO.Pipes.PipeDirection]::InOut)
    Write-Host "Connecting to \\.\pipe\$PipeName ..." -ForegroundColor Yellow
    $pipe.Connect($TimeoutMs)
    $reader = New-Object System.IO.StreamReader($pipe, [System.Text.Encoding]::UTF8)
    $writer = New-Object System.IO.StreamWriter($pipe, [System.Text.Encoding]::UTF8)
    $writer.AutoFlush = $true
    Write-Host "Connected." -ForegroundColor Green
    return [pscustomobject]@{ Pipe = $pipe; Reader = $reader; Writer = $writer }
}

function Send-Cmd($conn, [string]$cmd, [hashtable]$extra) {
    $script:reqId++
    $obj = [ordered]@{ id = $script:reqId; cmd = $cmd }
    if ($extra) { foreach ($k in $extra.Keys) { $obj[$k] = $extra[$k] } }
    $line = ($obj | ConvertTo-Json -Compress)
    $conn.Writer.WriteLine($line)
    $resp = $conn.Reader.ReadLine()
    if ($null -eq $resp) { throw "Pipe closed while awaiting response to '$cmd'." }
    return ($resp | ConvertFrom-Json)
}

function Run-Xref($conn, [string]$addr) {
    Write-Host ""
    Write-Host "find_property_xrefs prop_addr=$addr game_only=$GameOnly max=$Max" -ForegroundColor Cyan
    $r = Send-Cmd $conn "find_property_xrefs" @{ prop_addr = $addr; game_only = $GameOnly; max_results = $Max }
    if (-not $r.ok) { Write-Host "ERROR: $($r.error)" -ForegroundColor Red; return }
    $s = $r.scan
    Write-Host ("scan: functions_scanned={0} with_script={1} objects_total={2} {3}ms deadline_hit={4}" -f `
        $s.functions_scanned, $s.functions_with_script, $s.objects_total, $s.duration_ms, $s.deadline_hit) `
        -ForegroundColor DarkGray
    $xrefs = @($r.xrefs)
    Write-Host ("xrefs: {0}" -f $xrefs.Count) -ForegroundColor Green
    if ($xrefs.Count -gt 0) {
        $xrefs | ForEach-Object {
            [pscustomobject]@{
                kind  = $_.kind
                occ   = $_.occurrences
                wr    = $_.write_count  # how many of occ are writes (EX_Let* LHS)
                owner = $_.owner_class
                event = $_.event        # v2a: BP event (ubergraph hits only)
                func  = $_.func_full
            }
        } | Format-Table -AutoSize
    }
}

function Run-FuncProps($conn, [string]$addr) {
    Write-Host ""
    Write-Host "walk_function_props func_addr=$addr" -ForegroundColor Cyan
    $r = Send-Cmd $conn "walk_function_props" @{ func_addr = $addr }
    if (-not $r.ok) { Write-Host "ERROR: $($r.error)" -ForegroundColor Red; return }
    $props = @($r.props)
    Write-Host ("script_bytes={0}  props={1}" -f $r.script_bytes, $props.Count) -ForegroundColor DarkGray
    if ($props.Count -gt 0) {
        $props | ForEach-Object {
            $acc = if ($_.write_count -gt 0) { "{0}W/{1}R" -f $_.write_count, ($_.occurrences - $_.write_count) } else { "read" }
            [pscustomobject]@{ scope = $_.scope; access = $acc; occ = $_.occurrences; type = $_.type; name = $_.name }
        } | Format-Table -AutoSize
    }
}

$conn = Connect-Pipe
try {
    # --- Step 1: validate the Script offset derivation LIVE (step-1 deliverable) ---
    $off = Send-Cmd $conn "get_offsets" $null
    if ($off.ok) {
        $ps = $off.ustruct_propssize
        $sc = $off.ustruct_script
        $ok = ($sc -eq ($ps + 8))
        $col = if ($ok) { "Green" } else { "Red" }
        Write-Host ("get_offsets: PropsSize=0x{0:X}  Script=0x{1:X}  (Script == PropsSize+8 : {2})" -f `
            $ps, $sc, $ok) -ForegroundColor $col
        Write-Host ("  use_fproperty={0}  validated={1}  build={2}" -f `
            $off.use_fproperty, $off.validated, $off.build_info) -ForegroundColor DarkGray
    } else {
        Write-Host "get_offsets failed: $($off.error)" -ForegroundColor Red
    }

    # --- Reverse edge: direct func_addr path ---
    if ($FuncAddr) { Run-FuncProps $conn $FuncAddr; return }

    # --- Step 2: direct prop_addr path ---
    if ($PropAddr) { Run-Xref $conn $PropAddr; return }

    # --- Step 2b: resolve via class name ---
    if ($ClassName) {
        Write-Host ""
        Write-Host "list_classes game_only=$GameOnly (filtering for '$ClassName') ..." -ForegroundColor Cyan
        $lc = Send-Cmd $conn "list_classes" @{ game_only = $GameOnly; limit = 5000 }
        if (-not $lc.ok) { throw "list_classes failed: $($lc.error)" }
        $match = @($lc.classes | Where-Object { $_.class_name -eq $ClassName })
        if ($match.Count -eq 0) {
            $match = @($lc.classes | Where-Object { $_.class_name -like "*$ClassName*" })
        }
        if ($match.Count -eq 0) { throw "No class matching '$ClassName' (scanned $($lc.total_classes))." }
        $cls = $match[0]
        Write-Host ("class: {0}  addr={1}  path={2}" -f $cls.class_name, $cls.class_addr, $cls.class_path) -ForegroundColor Green

        # Reverse edge: resolve a function by name via walk_functions, then list its props.
        if ($Func) {
            $wf = Send-Cmd $conn "walk_functions" @{ addr = $cls.class_addr }
            if (-not $wf.ok) { throw "walk_functions failed: $($wf.error)" }
            $fns = @($wf.functions)
            $fm = @($fns | Where-Object { $_.name -eq $Func })
            if ($fm.Count -eq 0) { $fm = @($fns | Where-Object { $_.name -like "*$Func*" }) }
            if ($fm.Count -eq 0) { throw "No function matching '$Func' on $($cls.class_name) ($($fns.Count) funcs)." }
            $fn = $fm[0]
            Write-Host ("func: {0} addr={1}" -f $fn.name, $fn.addr) -ForegroundColor Green
            Run-FuncProps $conn $fn.addr
            return
        }

        $wc = Send-Cmd $conn "walk_class" @{ addr = $cls.class_addr }
        if (-not $wc.ok) { throw "walk_class failed: $($wc.error)" }
        $fields = @($wc.class.fields)
        Write-Host ("fields: {0}" -f $fields.Count) -ForegroundColor DarkGray

        if ($Field) {
            $fm = @($fields | Where-Object { $_.name -eq $Field })
            if ($fm.Count -eq 0) { $fm = @($fields | Where-Object { $_.name -like "*$Field*" }) }
            if ($fm.Count -eq 0) { throw "No field matching '$Field' on $($cls.class_name)." }
            $f = $fm[0]
            Write-Host ("field: {0} ({1}) addr={2} off={3}" -f $f.name, $f.type, $f.addr, $f.offset) -ForegroundColor Green
            Run-Xref $conn $f.addr
        } else {
            # No -Field: just print the table so the user can pick an addr.
            $fields | ForEach-Object {
                [pscustomobject]@{ name = $_.name; type = $_.type; offset = $_.offset; addr = $_.addr }
            } | Format-Table -AutoSize
            Write-Host "Re-run with -Field <name> or -PropAddr <addr> to xref." -ForegroundColor Yellow
        }
        return
    }

    Write-Host ""
    Write-Host "No -PropAddr or -ClassName given. get_offsets validated above; pass one to run an xref." -ForegroundColor Yellow
}
finally {
    # Reader/Writer wrap the SAME pipe stream; disposing one closes it, so the
    # others throw "closed pipe". Guard each independently.
    foreach ($d in @($conn.Writer, $conn.Reader, $conn.Pipe)) {
        try { if ($d) { $d.Dispose() } } catch { }
    }
}
