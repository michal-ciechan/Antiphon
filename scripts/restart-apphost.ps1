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
    the lock; BuildFailed also Stop-Process the spawned dev-aspire.ps1 and removes
    the launch lock when its recorded holder is that killed child (CARD-0644 R4).
    A lock stamped before the last OS boot is stale whatever its age or PID.

.PARAMETER NoBuild            Pass -NoBuild through to dev-aspire.ps1 (skip restore/npm).
.PARAMETER TimeoutSec         Seconds to wait for the dashboard + backend health (default 150).
.PARAMETER LockMaxAgeMinutes  Ignore a lock older than this (default 15, the watchdog's number).
.PARAMETER AllowWorktree      Admit a linked worktree at its current HEAD when it is not origin/master (warning printed).
                              Never overrides -ExpectedSha, an unverifiable root, or the locks.

.OUTPUTS
    Exit codes:
      0  restarted, dashboard up, /health 200, and /api/version SHA matches the admitted SHA
      1  failed (build failed, or did not come up within TimeoutSec)
      3  REFUSED - unverifiable root, SHA admission failure, or another restart or launch is in flight; nothing was killed
      4  Aspire's DCP dependency check timed out (see the printed verdict)
      5  server build unverified - health succeeded but /api/version SHA did not match; lock retained, child left running
.PARAMETER ExpectedSha
    Full 40- or 64-character commit SHA that must equal source-root HEAD (alias
    -ExpectedServerSha). Default expectation: HEAD must equal the local
    refs/remotes/origin/master (never fetched here). Main and linked worktrees are
    admitted alike (CARD-0644); shared-stack locks and state live in the main
    worktree's logs/ either way.
.EXAMPLE
    pwsh -File scripts/restart-apphost.ps1
    pwsh -File scripts/restart-apphost.ps1 -ExpectedSha <full-sha>
    pwsh -File scripts/restart-apphost.ps1 -AllowWorktree
.NOTES
    Keep this file ASCII-only: it may run under Windows PowerShell 5.1, which reads
    no-BOM .ps1 as CP1252 and mangles non-ASCII characters into parse errors.
#>
param(
    [switch]$NoBuild,
    [int]$TimeoutSec = 150,
    [int]$LockMaxAgeMinutes,
    [switch]$AllowWorktree,
    [Alias('ExpectedServerSha')][string]$ExpectedSha
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
$restartBeganUtc = [datetime]::UtcNow
# CARD-0644: admit the source root by exact commit before any lock, kill or launch.
$admission = Get-AppHostSourceAdmission -SourceRoot $root -ExpectedSha $ExpectedSha -AllowWorktree:$AllowWorktree
if (-not $admission.Admitted) {
    $admission.Lines | ForEach-Object { Write-Host $_ -ForegroundColor Yellow }
    exit 3
}
$admissionColor = if ($admission.Override) { 'Yellow' } else { 'DarkGray' }
$admission.Lines | ForEach-Object { Write-Host $_ -ForegroundColor $admissionColor }
$frozenSha = $admission.AdmittedSha

# Shared-stack coordination state lives in the MAIN worktree whichever checkout this
# runs from, so every restart/launch/watchdog on the machine contends on one set of
# locks. Build inputs (dev-aspire.ps1 and the code it builds) come from $root.
$logDir  = Join-Path $admission.StateRoot 'logs'
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

    if (Test-AppHostTrackedEdits -SourceRoot $root) {
        Write-Host "NOTE: source checkout has tracked edits; SHA equality does not prove uncommitted behavior is loaded. Probe the changed feature directly." -ForegroundColor Yellow
    }
    $gitIndexLock = Get-AppHostGitIndexLock -SourceRoot $root
    $gitIndexLockNote = Format-AppHostGitIndexLockNote $gitIndexLock
    if ($gitIndexLockNote) {
        Write-Host $gitIndexLockNote -ForegroundColor Yellow
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

    $gitIndexLockAfterKill = Get-AppHostGitIndexLock -SourceRoot $root
    if ($gitIndexLockAfterKill -and $gitIndexLockAfterKill.LastWriteTimeUtc -gt $restartBeganUtc) {
        Write-Host "NOTE: git index lock appeared during this restart's kill (mtime after script start); probably orphaned by this restart's kill. $($gitIndexLockAfterKill.Path). Remove it with: Remove-Item '$($gitIndexLockAfterKill.Path)'. Never remove a lock while a git process older than it is running." -ForegroundColor Yellow
    }

    # 4) Reset launch signals so the wait below is not fooled by stale state.
    Remove-Item $urlFile -ErrorAction SilentlyContinue
    '' | Set-Content -LiteralPath $logFile -ErrorAction SilentlyContinue

    # 5) Relaunch in a normal window (detached) - dev-aspire.ps1 backgrounds the AppHost.
    Write-Host "  launching dev-aspire.ps1$(if ($NoBuild) { ' -NoBuild' })..." -ForegroundColor DarkGray
    # CARD-0644 D-7: the child rechecks HEAD against the frozen SHA, and RestartOwnerPid
    # lets it launch under this run's restart lock without admitting any other restart.
    $devArgs = @('-NoLogo', '-File', $devScript)
    if ($NoBuild) { $devArgs += '-NoBuild' }
    $devArgs += @('-ExpectedSha', $frozenSha, '-RestartOwnerPid', $PID)
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
            # CARD-0644 R4: a forced kill skips the child's finally, so its launch lock
            # would block the next restart and the watchdog for 15 minutes. Remove it
            # only when the recorded holder is the child this run just killed.
            if ($devProcess -and $devProcess.Id -and (Remove-AppHostOwnedLock -Path $launchLock -OwnerPid $devProcess.Id)) {
                Write-Host "  removed the killed child's launch lock (PID $($devProcess.Id); $launchLock)" -ForegroundColor DarkGray
            }
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
        $moved = (-not $freshHeadNow) -or ($freshHeadNow -ne $frozenSha)
        $shaOk = $ident.Ok -and ($ident.Sha.ToLowerInvariant() -eq $frozenSha)
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
        Write-Host "  expected SHA: $frozenSha" -ForegroundColor DarkGray
        Write-Host "  observed SHA: $observedText" -ForegroundColor DarkGray
        Write-Host "  reason: $identityReason" -ForegroundColor DarkGray
        if ($identityReason -eq 'checkout moved') {
            Write-Host "  checkout moved: expected $frozenSha, HEAD now $freshHeadNow" -ForegroundColor DarkGray
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
