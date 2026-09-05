<#
.SYNOPSIS
    UE5CEDumper vendor sync — reference clones + pinned submodules

.DESCRIPTION
    vendor/ holds two DIFFERENT kinds of dependency and they must not be treated alike:

      1. Reference clones (Dumper-7 / RE-UE4SS / UnrealEngine) are GITIGNORED.
         Nothing we build links against them; they are read-only oracles for
         "what does upstream do at version X". Fast-forwarding them is free and
         is this script's default action.

      2. Submodules (minhook / zydis) are PINNED BY OUR GIT INDEX and are COMPILED
         INTO THE DLL. Moving one is a dependency bump: it stages a change in the
         parent repo and requires a full rebuild plus the test suite. The zydis
         v4 -> v5 bump broke dll/src/Denken.cpp (op.mem.disp.has_displacement was
         removed) and a later decoder-table regen warranted an in-game re-check.

    Therefore submodules are REPORT-ONLY by default. -UpdateSubmodules opts in.

    The report always FETCHES before counting. A behind-count taken without a
    fetch reads the last-fetched remote ref and happily reports "0 behind" against
    a remote that moved weeks ago.

.PARAMETER SkipClones
    Do not touch the reference clones; only report (or update) submodules.

.PARAMETER Init
    Run 'git submodule update --init --recursive' first. Safe and idempotent: it
    checks out the sha THIS repo pins, it does not move the pin. Use on a fresh clone.

.PARAMETER UpdateSubmodules
    Actually bump minhook/zydis to their upstream tip and stage the new shas.
    This is a code change. Rebuild and run the tests before committing:
      powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Target DLL
      powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Target Test

.EXAMPLE
    .\sync_tools.ps1                     # ff the reference clones, report submodule state
    .\sync_tools.ps1 -Init               # + restore pinned submodule checkouts (fresh clone)
    .\sync_tools.ps1 -SkipClones         # submodule report only, fast
    .\sync_tools.ps1 -UpdateSubmodules   # bump the pins (then rebuild + test)
#>

[CmdletBinding()]
param(
    [switch]$SkipClones,
    [switch]$Init,
    [switch]$UpdateSubmodules
)

$ErrorActionPreference = "Continue"
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
Set-Location $repoRoot

function Write-Section($text) {
    Write-Host ""
    Write-Host "=== $text ===" -ForegroundColor Cyan
}

# --------------------------------------------------------------------------
# 0. Damage check — run FIRST, every time.
#
#     "I emptied vendor/ before re-syncing" is a real accident that has already
#     cost a sibling repo a day. It is dangerous here because vendor/ is NOT all
#     throwaway clones: 'git ls-files vendor' returns three entries, and one of
#     them is a genuinely tracked FILE.
#
#       vendor/nlohmann/json.hpp   TRACKED CONTENT (v3.11.3, ~900 KB, a committed
#                                  header copy, not a submodule). It is on the
#                                  include path at dll/CMakeLists.txt:171,547,656
#                                  and is included by Fern.cpp / Flamme.cpp /
#                                  Renge.h / Serie.cpp / Utf8Helpers.h. Delete it
#                                  and the DLL and both test exes stop compiling.
#       vendor/minhook             gitlink; real objects live in .git/modules/,
#       vendor/zydis               which SURVIVES deleting vendor/. Restorable
#                                  offline, on the pinned sha.
#
#     The deletion itself is cheap to undo. What is not cheap is the SECOND step:
#     a 'git add -A' / 'git commit -am' after the deletion commits the removal of
#     json.hpp and of both gitlinks. So we detect and shout before anything else.
# --------------------------------------------------------------------------
$deletedTracked = @(git ls-files --deleted -- vendor 2>$null | Where-Object { $_ })
if ($deletedTracked.Count -gt 0) {
    Write-Host ""
    Write-Host "!! TRACKED CONTENT MISSING FROM vendor/ !!" -ForegroundColor Red
    $deletedTracked | ForEach-Object { Write-Host "     $_" -ForegroundColor Red }
    Write-Host "   Do NOT 'git add -A' or 'git commit -am' in this state - that commits the deletion." -ForegroundColor Red

    if ($Init) {
        # Restore ONLY paths that are actually missing. Never 'git checkout' a file
        # that is present: that would silently discard a local edit.
        Write-Host "   restoring from the object store (no network needed)..." -ForegroundColor Green
        foreach ($f in $deletedTracked) { git checkout -- $f }
    } else {
        Write-Host "   Fix: .\sync_tools.ps1 -Init      (restores tracked files + submodule checkouts)" -ForegroundColor Yellow
    }
}

# --------------------------------------------------------------------------
# 1. Reference clones (gitignored, safe to fast-forward)
# --------------------------------------------------------------------------
$clones = [ordered]@{
    "Dumper-7"     = "https://github.com/Encryqed/Dumper-7.git"
    "RE-UE4SS"     = "https://github.com/UE4SS-RE/RE-UE4SS.git"
    "UnrealEngine" = "https://github.com/EpicGames/UnrealEngine.git"
}

if (-not $SkipClones) {
    if (!(Test-Path "vendor")) { New-Item -ItemType Directory -Path "vendor" | Out-Null }

    foreach ($entry in $clones.GetEnumerator()) {
        $name = $entry.Key
        $url  = $entry.Value
        $dir  = "vendor\$name"

        Write-Section "reference clone: $name"

        if (Test-Path "$dir\.git") {
            # fetch + ff-only, never a merge. A merge commit in a read-only oracle
            # is pure confusion, and 'git pull' on a detached HEAD just errors out.
            git -C $dir fetch --prune origin
            $branch = (git -C $dir rev-parse --abbrev-ref HEAD 2>$null)
            if ($branch -and $branch -ne "HEAD") {
                git -C $dir merge --ff-only "@{u}" 2>&1 | Write-Host
            } else {
                Write-Host "  detached HEAD - fetched only, not moved" -ForegroundColor Yellow
            }
        } else {
            Write-Host "  cloning..." -ForegroundColor Green
            if ($name -eq "UnrealEngine") {
                # blob:none keeps full history without pre-fetching every blob, but the
                # working tree still lands at ~5.6 GB and the initial fetch is long.
                # Say so out loud: a re-clone here is what makes "I emptied vendor/"
                # expensive, and it is easy to kick off without meaning to.
                Write-Host "  NOTE: this is a ~5.6 GB download and will take a while." -ForegroundColor Yellow
                Write-Host "        Ctrl+C now if you only meant to sync the others (-SkipClones)." -ForegroundColor Yellow
                git clone --filter=blob:none $url $dir
            } else {
                git clone $url $dir
            }
        }

        if (Test-Path "$dir\.git") {
            $head = (git -C $dir log -1 --format='%h %ad %s' --date=short 2>$null)
            $desc = (git -C $dir describe --tags --abbrev=0 2>$null)
            Write-Host "  HEAD: $head"
            if ($desc) { Write-Host "  tag:  $desc" }
        }
    }
}

# --------------------------------------------------------------------------
# 2. Submodules (pinned + compiled in)
# --------------------------------------------------------------------------
Write-Section "submodules (pinned by our index, compiled into the DLL)"

if ($Init) {
    Write-Host "  restoring pinned checkouts (does NOT move the pin)..." -ForegroundColor Green
    git submodule update --init --recursive
}

if ($UpdateSubmodules) {
    Write-Host "  BUMPING submodules to upstream tip - this stages a code change" -ForegroundColor Yellow

    # NOT '--remote --recursive'. Measured 2026-09-05: that combination walks into
    # NESTED submodules and moves them to THEIR upstream tip too, which de-syncs
    # them from what their own parent pins. It took zydis's dependencies/zycore
    # from 75a36c45 (what zydis@a95bb71 records) to c1fa01ce (zycore master),
    # leaving `git status` dirty at ' M vendor/zydis' for a bump nobody asked for
    # and that zydis has never been built against.
    #
    # Correct shape is two steps: bump only OUR direct pins, then let each new
    # parent decide what its own children should be.
    git submodule update --remote
    git submodule update --init --recursive
}

$subs = @(
    @{ Name = "minhook"; Path = "vendor/minhook" }
    @{ Name = "zydis";   Path = "vendor/zydis"   }
)

foreach ($s in $subs) {
    $p = $s.Path
    if (!(Test-Path "$p/.git")) {
        Write-Host "  $($s.Name): NOT CHECKED OUT - run '.\sync_tools.ps1 -Init'" -ForegroundColor Red
        continue
    }

    # Fetch FIRST. Counting against an unfetched origin reports a stale zero.
    git -C $p fetch --quiet --prune origin 2>&1 | Out-Null

    $upstream = $null
    foreach ($cand in @("origin/master", "origin/main")) {
        git -C $p rev-parse --verify --quiet $cand > $null 2>&1
        if ($LASTEXITCODE -eq 0) { $upstream = $cand; break }
    }

    $head   = (git -C $p rev-parse --short HEAD 2>$null)
    $behind = if ($upstream) { (git -C $p rev-list --count "HEAD..$upstream" 2>$null) } else { "?" }

    $colour = if ($behind -eq "0") { "Green" } else { "Yellow" }
    Write-Host "  $($s.Name): HEAD $head, $behind behind $upstream" -ForegroundColor $colour

    if ($behind -ne "0" -and $behind -ne "?") {
        git -C $p log --oneline "HEAD..$upstream" 2>$null | Select-Object -First 10 | ForEach-Object {
            Write-Host "      $_"
        }
    }
}

# zydis carries a trap worth printing every time: 'git describe' reports
# v4.0.0-121-g... while the header declares 5.0.0. The reason is NOT an
# incomplete tag fetch -- upstream has published no v5 tag at all (highest is
# v4.1.1, and the v4.1.x line lives on maintenance/v4, which is not an ancestor
# of master). So describe walks master's ancestry back to v4.0.0 and is doing
# the only thing it can. Fetching more tags cannot help. The header is the fact.
$zydisHeader = "vendor/zydis/include/Zydis/Zydis.h"
if (Test-Path $zydisHeader) {
    $line = Select-String -Path $zydisHeader -Pattern 'define\s+ZYDIS_VERSION\s+(0x[0-9A-Fa-f]+)' | Select-Object -First 1
    if ($line) {
        $raw   = $line.Matches[0].Groups[1].Value
        $val   = [uint64]$raw
        $major = ($val -shr 48) -band 0xFFFF
        $minor = ($val -shr 32) -band 0xFFFF
        $patch = ($val -shr 16) -band 0xFFFF
        Write-Host "  zydis real version: $major.$minor.$patch (from Zydis.h; 'git describe' lies here)" -ForegroundColor DarkGray
    }
}

# Pin drift: a leading +/- from 'git submodule status' means the working tree is
# not on the sha this repo records. That is a staged (or about-to-be) bump.
$status = git submodule status
$drift  = $status | Where-Object { $_ -match '^[+\-U]' }
if ($drift) {
    Write-Host ""
    Write-Host "  PIN DRIFT - working tree differs from the recorded sha:" -ForegroundColor Yellow
    $drift | ForEach-Object { Write-Host "      $_" }
    Write-Host "      A '+' is a pending bump: rebuild and run the tests before committing." -ForegroundColor Yellow
    Write-Host "      A '-' means not initialised: run '.\sync_tools.ps1 -Init'." -ForegroundColor Yellow
}

if ($UpdateSubmodules -and $drift) {
    Write-Host ""
    Write-Host "  A submodule bump is a DEPENDENCY CHANGE. Before committing:" -ForegroundColor Yellow
    Write-Host "    powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Target DLL"
    Write-Host "    powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Target Test"
}

Write-Host ""
Write-Host "done." -ForegroundColor Cyan
