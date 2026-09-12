<#
.SYNOPSIS
    Kill and restart the whole Antiphon Aspire stack (AppHost + server + client +
    dashboard), then wait for it to come back healthy.

    dev-aspire.ps1 launches a NEW AppHost but does not stop an old one, so a plain
    re-run collides with the still-running AppHost/server DLLs. This script does the
    teardown first, then calls dev-aspire.ps1.

    The always-on session-runner (port 17204) is PRESERVED - it is designed to
    survive AppHost restarts, and the new AppHost re-adopts it. (Server/client have
    no standalone restart; they live and die with the AppHost. To bounce just the
    session-runner, use restart-session-runner.ps1.)

    CARD-0075: this script is the only thing that KILLS anything, and until now it
    was the only actor blind to CARD-0011's launch lock. It now takes
    logs/apphost.restart.lock for the whole run and refuses (exit 3) if either that
    lock or logs/apphost.launch.lock is held. It also returns non-zero while the
    dev-aspire.ps1 it spawned may still be legitimately launching, so a refusal has
    to be distinguishable from a failure - an indistinguishable failure is what
    produces the retry that kills a DCP mid-startup.

    CARD-0310: TimeoutSec (exit 1, child may still be launching) and DCP timeout
    (exit 4) leave apphost.restart.lock on disk for LockMaxAgeMinutes. A dead
    holder with a fresh stamp is still in-flight. Exit 0 and BuildFailed remove
    the lock; BuildFailed also Stop-Process the spawned dev-aspire.ps1.

.PARAMETER NoBuild            Pass -NoBuild through to dev-aspire.ps1 (skip restore/npm).
.PARAMETER TimeoutSec         Seconds to wait for the dashboard + backend health (default 150).
.PARAMETER LockMaxAgeMinutes  Ignore a lock older than this (default 15, the watchdog's number).
.PARAMETER AllowWorktree      Intentionally allow a linked worktree to control the shared local stack.

.OUTPUTS
    Exit codes:
      0  restarted, dashboard up, /health 200, and /api/version SHA matches the intended HEAD
      1  failed (build failed, or did not come up within TimeoutSec)
      3  REFUSED - a linked/unverifiable worktree root, SHA admission failure, or another restart or launch is in flight; nothing was killed
      4  Aspire's DCP dependency check timed out (see the printed verdict)
      5  server build unverified - health succeeded but /api/version SHA did not match; lock retained, child left running
.PARAMETER ExpectedServerSha
    Full 40- or 64-character SHA that must equal source-root HEAD. Default is that HEAD.
.EXAMPLE
    pwsh -File scripts/restart-apphost.ps1
    pwsh -File scripts/restart-apphost.ps1 -AllowWorktree
    pwsh -File scripts/restart-apphost.ps1 -ExpectedServerSha <full-sha>
.NOTES
    Keep this file ASCII-only: it may run under Windows PowerShell 5.1, which reads
    no-BOM .ps1 as CP1252 and mangles non-ASCII characters into parse errors.
#>
param(
    [switch]$NoBuild,
    [int]$TimeoutSec = 150,
    [int]$LockMaxAgeMinutes,
    [switch]$AllowWorktree,
    [string]$ExpectedServerSha
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'apphost-common.ps1')
$appHostTestSeams = $env:ANTIPHON_APPHOST_TEST_SEAMS
if (-not [string]::IsNullOrWhiteSpace($appHostTestSeams) -and (Test-Path -LiteralPath $appHostTestSeams)) {
    . $appHostTestSeams
    Write-Host 'TEST SEAMS ACTIVE'
}
if (-not $PSBoundParameters.ContainsKey('LockMaxAgeMinutes')) {
    $LockMaxAgeMinutes = $AppHostLockMaxAgeMinutes
}

$root    = Split-Path $PSScriptRoot -Parent      # scripts/ -> repo root
$worktree = Get-AppHostWorktreeClassification -SourceRoot $root
if (-not $worktree.Verified -or (-not $worktree.IsMainWorktree -and -not $AllowWorktree)) {
    Format-AppHostWorktreeGuardMessage -Classification $worktree | ForEach-Object { Write-Host $_ -ForegroundColor Yellow }
    exit 3
}
if (-not $worktree.IsMainWorktree -and $AllowWorktree) {
    Format-AppHostWorktreeGuardMessage -Classification $worktree -AllowWorktree | ForEach-Object { Write-Host $_ -ForegroundColor Yellow }
}

$logDir  = Join-Path $root 'logs'
$pidFile = Join-Path $logDir 'apphost.pid'
$urlFile = Join-Path $logDir 'apphost-dashboard-url.txt'
$logFile = Join-Path $logDir 'apphost.log'
$devScript = Join-Path $root 'dev-aspire.ps1'
$launchLock  = Join-Path $logDir 'apphost.launch.lock'
$restartLock = Join-Path $logDir 'apphost.restart.lock'
$watchdogLog = Join-Path $logDir 'watchdog-apphost.log'

# Everything the AppHost owns. 17204 (session-runner) is deliberately EXCLUDED.
# 17209 = Storybook (AppHost-managed npm app; escapes the wrapper tree like the Vite client).
$appHostPorts = 17200, 17202, 17203, 17205, 17206, 17207, 17209
$sessionRunnerPort = 17204

function Read-PidFile([string]$file) {
    if (Test-Path $file) {
        $v = (Get-Content -LiteralPath $file -Raw -ErrorAction SilentlyContinue).Trim()
        if ($v -match '^\d+$') { return [int]$v }
    }
    return $null
}

Write-Host "Restarting Antiphon AppHost..." -ForegroundColor Cyan

# 0) Refuse if anything else is already launching or restarting. This runs BEFORE
#    any kill: a refusal must leave the stack exactly as it found it.
New-Item -ItemType Directory -Force $logDir | Out-Null

$launchInFlight = Test-AppHostLockActive -Path $launchLock -MaxAgeMinutes $LockMaxAgeMinutes -Label 'launch lock'
if ($launchInFlight) {
    Write-Host "REFUSED: a launch is in flight - $launchInFlight" -ForegroundColor Yellow
    Write-Host "  dev-aspire.ps1 is still bringing the stack up. Killing it now would time out its" -ForegroundColor DarkGray
    Write-Host "  DCP dependency check (CARD-0075). Wait for it, then re-run if it did not come up." -ForegroundColor DarkGray
    Write-Host "  Nothing was killed." -ForegroundColor DarkGray
    exit 3
}

$lock = New-AppHostLock -Path $restartLock -MaxAgeMinutes $LockMaxAgeMinutes -Label 'restart lock'
if (-not $lock.Acquired) {
    Write-Host "REFUSED: another restart is in flight - $($lock.Reason)" -ForegroundColor Yellow
    Write-Host "  A second teardown over the top of the first is exactly what times out the DCP" -ForegroundColor DarkGray
    Write-Host "  dependency check (CARD-0075). Wait for it to finish, then check http://localhost:17202/health." -ForegroundColor DarkGray
    Write-Host "  Nothing was killed. If the holder is genuinely gone, delete $restartLock." -ForegroundColor DarkGray
    exit 3
}

# CARD-0310: keep the stamp on disk after this process exits when the spawned
# child may still be launching. Set true after Start-Process; cleared on a
# healthy exit 0 and on BuildFailed (which also kills the child).
$keepRestartLock = $false
$devProcess = $null

try {

    $sourceHead = Get-AppHostSourceHead -SourceRoot $root
    if (-not $sourceHead) {
        Write-Host "REFUSED: could not read a full HEAD SHA from source root $root" -ForegroundColor Yellow
        if ($PSBoundParameters.ContainsKey('ExpectedServerSha') -and -not [string]::IsNullOrWhiteSpace($ExpectedServerSha)) {
            Write-Host "  supplied -ExpectedServerSha: $ExpectedServerSha" -ForegroundColor DarkGray
        }
        Write-Host "  Update the canonical checkout, then re-run." -ForegroundColor DarkGray
        Write-Host "  Nothing was killed." -ForegroundColor DarkGray
        exit 3
    }
    $expectedSha = $sourceHead
    if ($PSBoundParameters.ContainsKey('ExpectedServerSha') -and -not [string]::IsNullOrWhiteSpace($ExpectedServerSha)) {
        if (-not (Test-AppHostBuildShaFormat $ExpectedServerSha)) {
            Write-Host "REFUSED: -ExpectedServerSha is not a full 40- or 64-character SHA: $ExpectedServerSha" -ForegroundColor Yellow
            Write-Host "  source root: $root" -ForegroundColor DarkGray
            Write-Host "  HEAD: $sourceHead" -ForegroundColor DarkGray
            Write-Host "  Update the canonical checkout, then re-run." -ForegroundColor DarkGray
            Write-Host "  Nothing was killed." -ForegroundColor DarkGray
            exit 3
        }
        if ($ExpectedServerSha.ToLowerInvariant() -ne $sourceHead) {
            Write-Host "REFUSED: -ExpectedServerSha $ExpectedServerSha does not match source-root HEAD $sourceHead" -ForegroundColor Yellow
            Write-Host "  source root: $root" -ForegroundColor DarkGray
            Write-Host "  Update the canonical checkout, then re-run." -ForegroundColor DarkGray
            Write-Host "  Nothing was killed." -ForegroundColor DarkGray
            exit 3
        }
        $expectedSha = $ExpectedServerSha.ToLowerInvariant()
    }
    if (Test-AppHostTrackedEdits -SourceRoot $root) {
        Write-Host "NOTE: source checkout has tracked edits; SHA equality does not prove uncommitted behavior is loaded. Probe the changed feature directly." -ForegroundColor Yellow
    }

    # Guard: which PID owns the session-runner port, so we never kill it.
    $srPid = Get-AppHostPortOwners $sessionRunnerPort | Select-Object -First 1
    if ($srPid) { Write-Host "  preserving session-runner (PID $srPid on $sessionRunnerPort)" -ForegroundColor DarkGray }
    & (Join-Path $PSScriptRoot 'check-daemon-build.ps1')

    # 1) Kill the AppHost wrapper + dotnet AppHost tree.
    $appHostPid = Read-PidFile $pidFile
    if ($appHostPid) { Stop-AppHostProcessTree -ProcessId $appHostPid; Write-Host "  killed AppHost wrapper tree (PID $appHostPid)" }

    # 2) Free every AppHost-owned port (DCP children often escape the wrapper tree).
    #    Never touch the session-runner PID.
    Wait-AppHostPollInterval -Seconds 1
    foreach ($port in $appHostPorts) {
        foreach ($owner in Get-AppHostPortOwners $port) {
            if ($owner -ne $srPid) {
                Stop-AppHostProcessId -ProcessId $owner
                Write-Host "  freed port $port (PID $owner)"
            }
        }
    }

    # 3) Clean stale DCP / dashboard processes left orphaned by an unclean exit.
    Get-AppHostStrayProcesses |
        Where-Object { $_.Id -ne $srPid } |
        ForEach-Object { Stop-AppHostProcessId -ProcessId $_.Id; Write-Host "  killed stale $($_.ProcessName) (PID $($_.Id))" }

    # 4) Reset launch signals so the wait below is not fooled by stale state.
    Remove-Item $urlFile -ErrorAction SilentlyContinue
    '' | Set-Content -LiteralPath $logFile -ErrorAction SilentlyContinue

    # 5) Relaunch in a normal window (detached) - dev-aspire.ps1 backgrounds the AppHost.
    Write-Host "  launching dev-aspire.ps1$(if ($NoBuild) { ' -NoBuild' })..." -ForegroundColor DarkGray
    $devArgs = @('-NoLogo', '-File', $devScript)
    if ($NoBuild) { $devArgs += '-NoBuild' }
    if ($AllowWorktree) { $devArgs += '-AllowWorktree' }
    $devProcess = Start-AppHostDevLaunch -ArgumentList $devArgs
    $keepRestartLock = $true

    # 6) Wait for the dashboard URL + backend health + intended loaded SHA.
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    $dashUrl = $null
    $backendOk = $false
    $identityVerified = $false
    $identityObserved = $null
    $identityReason = 'unavailable'
    $freshHeadNow = $null
    while ((Get-Date) -lt $deadline) {
        Wait-AppHostPollInterval -Seconds 3
        if (-not $dashUrl -and (Test-Path $urlFile)) {
            $u = (Get-Content -LiteralPath $urlFile -Raw -ErrorAction SilentlyContinue).Trim()
            if ($u -match '^https?://') { $dashUrl = $u }
        }
        $verdict = Get-AppHostLogVerdict -LogPath $logFile
        if ($verdict.Kind -eq 'BuildFailed') {
            $keepRestartLock = $false
            Stop-AppHostLaunchChild -Process $devProcess
            Write-Host "AppHost build FAILED - check: Get-Content '$logFile' -Tail 40" -ForegroundColor Red
            exit 1
        }
        if ($verdict.Kind -eq 'DcpDependencyTimeout') {
            # The exception names podman because podman is the only probe that had
            # returned when the deadline hit. Say what docker actually reported.
            # Keep the restart lock: the spawned child may still be in flight.
            Show-DcpTimeoutVerdict -LogPath $logFile -Evidence $verdict.Evidence -PodmanNoise $verdict.PodmanNoise `
                -LaunchLock $launchLock -RestartLock $restartLock -WatchdogLog $watchdogLog
            exit 4
        }
        if (-not $dashUrl) { continue }
        $health = Invoke-AppHostHealthProbe
        if ([string]$health.StatusCode -ne '200') { continue }
        $backendOk = $true
        $versionProbe = Invoke-AppHostVersionProbe
        $ident = Resolve-AppHostLoadedIdentity -Probe $versionProbe
        $freshHeadNow = Get-AppHostSourceHead -SourceRoot $root
        $moved = (-not $freshHeadNow) -or ($freshHeadNow -ne $expectedSha)
        $shaOk = $ident.Ok -and ($ident.Sha.ToLowerInvariant() -eq $expectedSha)
        if ($shaOk -and -not $moved) {
            $identityVerified = $true
            $identityObserved = $ident.Sha
            break
        }
        if ($moved) {
            $identityReason = 'checkout moved'
            $identityObserved = $freshHeadNow
        } elseif ($ident.Ok) {
            $identityReason = $ident.Sha
            $identityObserved = $ident.Sha
        } else {
            $identityReason = $ident.Reason
            $identityObserved = $ident.Sha
        }
    }

    if ($dashUrl -and $backendOk -and $identityVerified) {
        $keepRestartLock = $false
        Write-Host "AppHost restarted - dashboard $dashUrl, backend healthy, server $identityObserved." -ForegroundColor Green
        exit 0
    } elseif ($dashUrl -and $backendOk) {
        $observedText = $identityObserved
        if ([string]::IsNullOrWhiteSpace($observedText)) { $observedText = $identityReason }
        Write-Host "REFUSED: server build unverified (exit 5)." -ForegroundColor Yellow
        Write-Host "  expected SHA: $expectedSha" -ForegroundColor DarkGray
        Write-Host "  observed SHA: $observedText" -ForegroundColor DarkGray
        Write-Host "  reason: $identityReason" -ForegroundColor DarkGray
        if ($identityReason -eq 'checkout moved') {
            Write-Host "  checkout moved: expected $expectedSha, HEAD now $freshHeadNow" -ForegroundColor DarkGray
        }
        Write-Host "  source root: $root" -ForegroundColor DarkGray
        Write-Host "  lock: $restartLock" -ForegroundColor DarkGray
        Write-Host "  inspect: Get-Content '$logFile' -Tail 40; Get-Content '$restartLock'" -ForegroundColor DarkGray
        Write-Host "  The launched child was left running; the restart lock is retained." -ForegroundColor DarkGray
        exit 5
    } elseif ($dashUrl) {
        Write-Host "Dashboard up ($dashUrl) but backend health not confirmed in ${TimeoutSec}s." -ForegroundColor Yellow
        Write-Host "  Leaving $restartLock so a watchdog fire treats this as still in flight." -ForegroundColor DarkGray
        exit 1
    } else {
        Write-Host "AppHost did not come up within ${TimeoutSec}s - check '$logFile'." -ForegroundColor Red
        Write-Host "  NOTE: the dev-aspire.ps1 this script spawned may STILL be launching (its own" -ForegroundColor DarkGray
        Write-Host "  budget is longer than ${TimeoutSec}s). Do not re-run blind - re-running kills a" -ForegroundColor DarkGray
        Write-Host "  DCP that is still coming up (CARD-0075). Check $launchLock first." -ForegroundColor DarkGray
        Write-Host "  Leaving $restartLock for LockMaxAgeMinutes; delete it to force a retry." -ForegroundColor DarkGray
        exit 1
    }

} finally {
    # Released even on a terminating error: $ErrorActionPreference is Stop, so a
    # trailing Remove-Item would be skipped (the same reasoning dev-aspire.ps1:21
    # already records for the launch lock). TimeoutSec / exit 4 keep the file
    # (CARD-0310); the stream is always closed so the stamp is readable.
    Remove-AppHostLock $lock -KeepFile:$keepRestartLock
}
