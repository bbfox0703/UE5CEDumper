# ============================================================
# test_pipe.ps1 — Mock DLL pipe server for UI testing
# Creates a NamedPipeServerStream that mimics the DLL responses.
# Usage: Run this script, then launch UE5DumpUI.exe and click Connect.
# ============================================================

param(
    [int]$ObjectCount = 500
)

Write-Host "=== UE5Dump Mock Pipe Server ===" -ForegroundColor Cyan
Write-Host "Pipe: \\.\pipe\UE5DumpBfx"
Write-Host "Mock objects: $ObjectCount"
Write-Host ""

# Generate mock object list
$mockObjects = @()
$classNames = @("Actor", "PlayerController", "GameMode", "Widget", "AnimInstance",
                "BlueprintGeneratedClass", "SceneComponent", "StaticMeshComponent",
                "SkeletalMeshComponent", "CharacterMovementComponent")
$objectNames = @("BP_Player", "BP_Enemy", "BP_Weapon", "BP_Projectile", "BP_GameMode",
                 "BP_HUD", "BP_PickupItem", "BP_Door", "BP_Trigger", "BP_Light")

for ($i = 0; $i -lt $ObjectCount; $i++) {
    $cls = $classNames[$i % $classNames.Count]
    $name = "$($objectNames[$i % $objectNames.Count])_$i"
    $addr = "0x{0:X}" -f (0x7FF000000000 + $i * 0x100)
    $mockObjects += @{
        addr = $addr
        name = $name
        class = $cls
        outer = "0x0"
    }
}

function Handle-Command($json) {
    $req = $json | ConvertFrom-Json
    $id = $req.id
    $cmd = $req.cmd

    switch ($cmd) {
        "init" {
            return (@{ id = $id; ok = $true; ue_version = 504 } | ConvertTo-Json -Compress)
        }
        "get_pointers" {
            return (@{
                id = $id; ok = $true
                gobjects = "0x7FF600A12340"
                gnames = "0x7FF600B56780"
                object_count = $ObjectCount
            } | ConvertTo-Json -Compress)
        }
        "get_object_count" {
            return (@{ id = $id; ok = $true; count = $ObjectCount } | ConvertTo-Json -Compress)
        }
        "get_object_list" {
            $offset = if ($req.offset) { $req.offset } else { 0 }
            $limit = if ($req.limit) { $req.limit } else { 200 }
            $end = [Math]::Min($offset + $limit, $ObjectCount)
            $slice = $mockObjects[$offset..($end - 1)]
            return (@{
                id = $id; ok = $true
                total = $ObjectCount
                objects = $slice
            } | ConvertTo-Json -Compress -Depth 5)
        }
        "get_object" {
            return (@{
                id = $id; ok = $true
                addr = $req.addr; name = "MockObject"
                full_name = "/Game/MockObject"; class = "Actor"
                class_addr = "0x100"; outer = ""; outer_addr = "0x0"
            } | ConvertTo-Json -Compress)
        }
        "find_object" {
            return (@{
                id = $id; ok = $true
                addr = "0x7FF000000100"; name = $req.path
            } | ConvertTo-Json -Compress)
        }
        "walk_class" {
            $fields = @(
                @{ addr = "0x300"; name = "Health"; type = "FloatProperty"; offset = 720; size = 4 },
                @{ addr = "0x310"; name = "MaxHealth"; type = "FloatProperty"; offset = 724; size = 4 },
                @{ addr = "0x320"; name = "Mana"; type = "FloatProperty"; offset = 728; size = 4 },
                @{ addr = "0x330"; name = "Speed"; type = "FloatProperty"; offset = 732; size = 4 },
                @{ addr = "0x340"; name = "bIsAlive"; type = "BoolProperty"; offset = 736; size = 1 },
                @{ addr = "0x350"; name = "PlayerName"; type = "StrProperty"; offset = 744; size = 16 },
                @{ addr = "0x360"; name = "Inventory"; type = "ArrayProperty"; offset = 760; size = 16 }
            )
            return (@{
                id = $id; ok = $true
                class = @{
                    name = "BP_Player_C"
                    full_path = "/Game/BP_Player.BP_Player_C"
                    super_addr = "0x7FF000000000"
                    super_name = "Character"
                    props_size = 1024
                    fields = $fields
                }
            } | ConvertTo-Json -Compress -Depth 5)
        }
        "read_mem" {
            $size = if ($req.size) { $req.size } else { 256 }
            $bytes = -join (1..$size | ForEach-Object { "{0:X2}" -f (Get-Random -Minimum 0 -Maximum 256) })
            return (@{ id = $id; ok = $true; bytes = $bytes } | ConvertTo-Json -Compress)
        }
        "watch" {
            return (@{ id = $id; ok = $true } | ConvertTo-Json -Compress)
        }
        "unwatch" {
            return (@{ id = $id; ok = $true } | ConvertTo-Json -Compress)
        }
        default {
            return (@{ id = $id; ok = $false; error = "Unknown command: $cmd" } | ConvertTo-Json -Compress)
        }
    }
}

# Main server loop
while ($true) {
    Write-Host "Waiting for client connection..." -ForegroundColor Yellow

    $pipe = New-Object System.IO.Pipes.NamedPipeServerStream(
        "UE5DumpBfx",
        [System.IO.Pipes.PipeDirection]::InOut,
        1,
        [System.IO.Pipes.PipeTransmissionMode]::Byte,
        [System.IO.Pipes.PipeOptions]::Asynchronous)

    $pipe.WaitForConnection()
    Write-Host "Client connected!" -ForegroundColor Green

    $reader = New-Object System.IO.StreamReader($pipe, [System.Text.Encoding]::UTF8)
    $writer = New-Object System.IO.StreamWriter($pipe, [System.Text.Encoding]::UTF8)
    $writer.AutoFlush = $true

    try {
        while ($pipe.IsConnected) {
            $line = $reader.ReadLine()
            if ($null -eq $line) { break }

            Write-Host "RX: $line" -ForegroundColor DarkGray
            $response = Handle-Command $line
            Write-Host "TX: $response" -ForegroundColor DarkGray

            $writer.WriteLine($response)
        }
    }
    catch {
        Write-Host "Client error: $_" -ForegroundColor Red
    }
    finally {
        $reader.Dispose()
        $writer.Dispose()
        $pipe.Dispose()
        Write-Host "Client disconnected." -ForegroundColor Yellow
    }
}
