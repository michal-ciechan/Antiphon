#requires -Version 5.1
<#
.SYNOPSIS
    CARD-0543: Get-AppHostGitIndexLock reports absent/fresh/stale on a scratch repo
    and never deletes. ASCII-only (pwsh 7 and Windows PowerShell 5.1).
    Never touches C:\src\Antiphon or logs/apphost.*.lock.
#>
$ErrorActionPreference = 'Continue'

. (Join-Path $PSScriptRoot 'apphost-common.ps1')

function Invoke-AppHostGracefulStop {
    param([int]$TimeoutSec = 5, [string]$Reason)
    $trace = Join-Path ([System.IO.Path]::GetTempPath()) 'apphost-graceful-stop-seam.jsonl'
    @{ kind = 'graceful'; class = 'unreachable' } | ConvertTo-Json -Compress | Add-Content -LiteralPath $trace -Encoding ASCII
    return [pscustomobject]@{ Class = 'unreachable'; Pid = $null; Detail = 'seamed' }
}

function Wait-AppHostProcessExit {
    param([int]$ProcessId, [int]$TimeoutSec)
    $trace = Join-Path ([System.IO.Path]::GetTempPath()) 'apphost-graceful-stop-seam.jsonl'
    @{ kind = 'wait-exit'; pid = $ProcessId } | ConvertTo-Json -Compress | Add-Content -LiteralPath $trace -Encoding ASCII
    return $null
}

$script:passed = 0
$script:failed = 0
$script:failures = @()

function Write-Pass {
    param([string]$Name)
    $script:passed++
    Write-Host "PASS $Name"
}

function Write-Fail {
    param([string]$Name, [string]$Detail)
    $script:failed++
    $script:failures += "$Name : $Detail"
    Write-Host "FAIL $Name - $Detail"
}

function Assert-True {
    param([bool]$Cond, [string]$Name, [string]$Detail = '')
    if ($Cond) { Write-Pass $Name }
    else { Write-Fail $Name $Detail }
}

$root = Split-Path $PSScriptRoot -Parent
$scratch = Join-Path $env:TEMP ('apphost-git-index-lock-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch -Force | Out-Null
try {
    & git -C $scratch init -b master --quiet
    if ($LASTEXITCODE -ne 0) { throw "git init failed in $scratch" }
    & git -C $scratch config user.email 'test@antiphon.local'
    & git -C $scratch config user.name 'Index Lock Tests'

    # T1 absent
    $absent = Get-AppHostGitIndexLock -SourceRoot $scratch
    Assert-True ($null -eq $absent) 'T1 absent returns $null' ("actual=$absent")

    $lockPath = (& git -C $scratch rev-parse --path-format=absolute --git-path index.lock).Trim()
    Assert-True (-not [string]::IsNullOrWhiteSpace($lockPath)) 'T1 resolved index.lock path' $lockPath

    # T2 fresh lock
    [IO.File]::WriteAllBytes($lockPath, [byte[]]@())
    $fresh = Get-AppHostGitIndexLock -SourceRoot $scratch
    Assert-True ($null -ne $fresh -and $fresh.Present -and -not $fresh.Stale) 'T2 Present and not Stale' ("stale=$($fresh.Stale)")

    # T3 lock aged 60 min
    $agedUtc = [datetime]::UtcNow.AddMinutes(-60)
    $item = Get-Item -LiteralPath $lockPath
    $item.LastWriteTimeUtc = $agedUtc
    $stale = Get-AppHostGitIndexLock -SourceRoot $scratch
    Assert-True ($null -ne $stale -and $stale.Stale) 'T3 Stale after 60 min with no older git holder' ("stale=$($stale.Stale) age=$($stale.AgeMinutes)")

    # T4 note text
    $note = Format-AppHostGitIndexLockNote $stale
    Assert-True ($note -match [regex]::Escape($lockPath)) 'T4 note contains path' $note
    Assert-True ($note -match 'Remove-Item') 'T4 note contains Remove-Item' $note

    # T5 wiring guard
    $hits = @(
        Select-String -Path (Join-Path $PSScriptRoot 'restart-apphost.ps1') -Pattern 'Get-AppHostGitIndexLock' -SimpleMatch
        Select-String -Path (Join-Path $root 'verify-dev-stack.ps1') -Pattern 'Get-AppHostGitIndexLock' -SimpleMatch
        Select-String -Path (Join-Path $PSScriptRoot 'watchdog-apphost.ps1') -Pattern 'Get-AppHostGitIndexLock' -SimpleMatch
    )
    Assert-True ($hits.Count -ge 3) 'T5 Get-AppHostGitIndexLock wired in restart, verify, watchdog' ("hits=$($hits.Count)")

    # T6 helper never deletes
    Assert-True (Test-Path -LiteralPath $lockPath) 'T6 helper left the lock file in place' $lockPath
}
catch {
    Write-Fail 'setup' $_.Exception.Message
}
finally {
    Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host ('CARD-0543 git index lock: {0} passed, {1} failed' -f $script:passed, $script:failed)
if ($script:failed -gt 0) {
    foreach ($line in $script:failures) { Write-Host ("  " + $line) }
    Write-Host 'APPHOST GIT INDEX LOCK TESTS EXIT CODE: 1  (FAIL - do not report this run as green)'
    exit 1
}
Write-Host 'APPHOST GIT INDEX LOCK TESTS EXIT CODE: 0  (PASS)'
exit 0
