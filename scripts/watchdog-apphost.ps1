<#
.SYNOPSIS
    Health-triggered AppHost watchdog. Registered as the per-user Scheduled Task
    "Antiphon AppHost Watchdog" by scripts/install-autostart.ps1 and fired every
    two minutes. Probes the published HTTP endpoints (NOT TCP listen - dcpctrl
    owns 17202 and 17203) and, after three failed rounds, restarts the stack via
    scripts/restart-apphost.ps1 so the session-runner on 17204 is preserved.

    One fire does, in order:
      1. Observe API health and outage duration before recovery guards. Emit
         independent Event Log, marker and optional webhook signals at 3 min.
         Then bail if a launch or restart is in flight (logs/apphost.launch.lock or
         logs/apphost.restart.lock with a stamp younger than 15 min, even if
         the holder PID has exited, or the "Antiphon AppHost" logon task
         currently Running). A proven-dead restart generation may be reclaimed
         after two distinct fires, with shared admission revalidation.
      2. GET /health on 17202 (expect 200) and GET / on 17203 (expect 2xx/3xx).
         On a restart-worthy failure (connection-refused / stack down), re-probe
         twice more at IntervalSeconds (15) spacing. Restart only if all three
         rounds look down. HttpClient timeout on /health while client=200 is
         NOT a failed round (CARD-0310: loaded or coming-up, not a corpse).
      3. If docker info fails, log and exit without counting against the flap budget.
      4. Cooldown: skip if a restart was stamped under CooldownMinutes (15,
         same as LockMaxAgeMinutes) ago.
      5. Flap cap: skip and ERROR if MaxRestartsPerWindow (3) restarts sit inside
         the last 60 minutes.
      6. Re-check the locks (CARD-0075: step 1 ran up to ~60s of probing ago, and
         a restart that started inside that window must not be raced).
      7. Invoke restart-apphost.ps1, log its last ~40 stdout/stderr lines and
         named exit (0=healthy, 1=timeout/build, 3=refused, 4=DCP timeout),
         then stamp logs/apphost-watchdog.state - UNLESS it exits 3 (refused,
         nothing killed), which spends no flap budget and so must not be stamped.

    -ProbeOnly probes and logs, never restarts (the safe acceptance check).
    -RestartScript overrides which script step 7 invokes; it exists so the refusal
    path can be exercised against a stub without tearing down a live stack.
    Leave the stack down on purpose through the supported helper (creates
    logs/apphost.down-on-purpose, then disables this task):
      pwsh -File scripts/set-apphost-maintenance.ps1
    Direct Disable-ScheduledTask still works and is what the independent
    watchdog-state observer detects as an unintentional disable.
.NOTES
    Requires scripts/apphost-common.ps1 (dot-sourced) for the shared lock parser.
    Keep this file ASCII-only: it may run under Windows PowerShell 5.1, which reads
    no-BOM .ps1 as CP1252 and mangles non-ASCII characters into parse errors.
#>
[CmdletBinding()]
param(
    [switch]$ProbeOnly,
    [int]$CooldownMinutes,
    [int]$MaxRestartsPerWindow = 3,
    [int]$IntervalSeconds = 15,
    [int]$LockMaxAgeMinutes,
    [int]$ProbeTimeoutSec = 5,
    [string]$HealthUrl = 'http://localhost:17202/health',
    [string]$ClientUrl = 'http://localhost:17203/',
    [string]$RestartScript
)

$ErrorActionPreference = 'Continue'

. (Join-Path $PSScriptRoot 'apphost-common.ps1')
if (-not $PSBoundParameters.ContainsKey('CooldownMinutes')) {
    $CooldownMinutes = $AppHostLockMaxAgeMinutes
}
if (-not $PSBoundParameters.ContainsKey('LockMaxAgeMinutes')) {
    $LockMaxAgeMinutes = $AppHostLockMaxAgeMinutes
}

$root     = Split-Path $PSScriptRoot -Parent
$logDir   = Join-Path $root 'logs'
$logFile  = Join-Path $logDir 'watchdog-apphost.log'
$lockFile = Join-Path $logDir 'apphost.launch.lock'
$restartLockFile = Join-Path $logDir 'apphost.restart.lock'
$stateFile = Join-Path $logDir 'apphost-watchdog.state'
if (-not $RestartScript) { $RestartScript = Join-Path $PSScriptRoot 'restart-apphost.ps1' }
$appHostTaskName = 'Antiphon AppHost'
$flapWindowMinutes = 60

New-Item -ItemType Directory -Force $logDir | Out-Null

function Write-Log([string]$level, [string]$msg) {
    $line = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [watchdog-apphost] [$level] $msg"
    Write-Host $line
    Add-Content -LiteralPath $logFile -Value $line -Encoding UTF8 -ErrorAction SilentlyContinue
}

function Read-WatchdogState {
    if (Test-Path -LiteralPath $stateFile) {
        try { $state = Get-Content -LiteralPath $stateFile -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop }
        catch { throw 'Watchdog state unreadable; preserving outage and restart history for repair.' }
    } else { $state = [pscustomobject]@{} }
    foreach ($name in @('lastOkUtc','restartsUtc','deadLaunch','downSinceUtc','outageId','outageAlerted','pendingAlerts')) {
        if (-not $state.PSObject.Properties[$name]) {
            $default=$null
            if ($name -in @('restartsUtc','pendingAlerts')) { $default=@() }
            $state | Add-Member NoteProperty $name $default
        }
    }
    return $state
}

function Write-WatchdogState($state) {
    $temp = $stateFile + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    try {
        [IO.File]::WriteAllText($temp, ($state | ConvertTo-Json -Depth 8), [Text.Encoding]::UTF8)
        if (Test-Path -LiteralPath $stateFile) { [IO.File]::Replace($temp,$stateFile,[NullString]::Value) }
        else { [IO.File]::Move($temp,$stateFile) }
    } finally { if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force } }
}

function Observe-WatchdogOutage($round) {
    $script:state = Update-AppHostOutage -State $script:state -HealthOk $round.HealthOk -Maintenance $script:maintenance -NowUtc (Get-AppHostWatchdogNow)
    # Intent precedes transport. Sent events stay in a local append-only journal
    # for post-recovery attention import; pending events retain the same Id.
    Write-WatchdogState $script:state
    if ($ProbeOnly) { return }
    if (-not (Send-AppHostOutageEvent -SourceRoot $root -State $script:state -NowUtc (Get-AppHostWatchdogNow))) {
        Write-Log 'WARN' 'outage marker could not be updated; other alert sinks remain independent'
    }
    if ($script:maintenance) { return }
    $webhookBlocked = $false
    foreach ($event in @($script:state.pendingAlerts)) {
        $complete = Send-AppHostOutageEvent -Event $event -SourceRoot $root -State $script:state -NowUtc (Get-AppHostWatchdogNow) -DeferWebhook:$webhookBlocked -PersistEventState { Write-WatchdogState $script:state }
        Write-WatchdogState $script:state
        if (-not $complete) {
            Write-Log 'WARN' "optional outage webhook pending ($($event.Id)); local sinks were attempted independently"
            $webhookBlocked = $true
            continue
        }
        $event | ConvertTo-Json -Compress | Add-Content -LiteralPath (Join-Path $logDir 'apphost-outage-events.jsonl') -Encoding UTF8 -ErrorAction Stop
        $script:state.pendingAlerts = @($script:state.pendingAlerts | Where-Object { $_.Id -ne $event.Id })
        Write-WatchdogState $script:state
    }
}

function Test-HttpOk([string]$url, [int[]]$okCodes) {
    try {
        $resp = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec $ProbeTimeoutSec
        $code = [int]$resp.StatusCode
        $ok = $okCodes -contains $code
        return [pscustomobject]@{ Ok = $ok; Code = $code; Error = $null }
    } catch {
        $code = $null
        if ($_.Exception.Response -and $_.Exception.Response.StatusCode) {
            $code = [int]$_.Exception.Response.StatusCode
            if ($okCodes -contains $code) {
                return [pscustomobject]@{ Ok = $true; Code = $code; Error = $null }
            }
        }
        return [pscustomobject]@{ Ok = $false; Code = $code; Error = $_.Exception.Message }
    }
}

function Test-Round {
    $health = Test-HttpOk $HealthUrl @(200)
    $clientCodes = 200, 201, 202, 203, 204, 301, 302, 303, 307, 308
    $client = Test-HttpOk $ClientUrl $clientCodes
    $class = Get-AppHostProbeClassification `
        -HealthOk $health.Ok -HealthError $health.Error -HealthCode $health.Code `
        -ClientOk $client.Ok -ClientError $client.Error -ClientCode $client.Code
    return [pscustomobject]@{
        HealthOk      = $health.Ok
        HealthDead    = (-not $health.Ok -and (Test-AppHostProbeErrorIsConnectionRefused $health.Error))
        Ok            = ($class.Kind -eq 'Up')
        RestartWorthy = $class.RestartWorthy
        Kind          = $class.Kind
        Summary       = $class.Summary
    }
}

function Test-LaunchInFlight([switch]$Revalidate) {
    $restart = Get-AppHostLock -Path $restartLockFile
    $launchHeld = Test-AppHostLockActive -Path $lockFile -MaxAgeMinutes $LockMaxAgeMinutes -Label 'launch lock'
    $activity = Get-AppHostLaunchActivity -SourceRoot $root -Lock $restart
    $observedRound = $script:firstRound
    if ($Revalidate) { $observedRound = Test-Round }
    $decision = Resolve-AppHostDeadLaunch -Lock $restart -Activity $activity -HealthDead $observedRound.HealthDead `
        -LaunchBlocked ([bool]$launchHeld) -Previous $script:state.deadLaunch -FireId $script:fireId `
        -NowUtc (Get-AppHostWatchdogNow) -Revalidate:($Revalidate -or $ProbeOnly)
    $script:state.deadLaunch = $decision.Proof
    Write-WatchdogState $script:state
    if ($launchHeld) { return $launchHeld }
    $restartHeld = Test-AppHostLockActive -Path $restartLockFile -MaxAgeMinutes $LockMaxAgeMinutes -Label 'restart lock'
    if ($restartHeld -and -not $decision.Reclaim) { return $restartHeld }
    if ($decision.Reclaim) { Write-Log 'WARN' 'dead launch proven on two fires; restart admission must revalidate' }
    # Existing task protection also applies when no restart stamp exists.
    try {
        $task = @(Get-ScheduledTask -ErrorAction Stop | Where-Object { $_.TaskName -eq $appHostTaskName })
        if (@($task | Where-Object { $_.State -eq 'Running' }).Count) { return 'AppHost logon task is Running' }
    } catch { return 'AppHost logon task inventory unknown' }
    return $null
}

# Serialize overlapping scheduler fires for this root. Keep the mutex pathname.
$fireLease = $null
try { $fireLease = [IO.File]::Open((Join-Path $logDir 'apphost-watchdog.fire.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None) }
catch { Write-Log 'INFO' 'skip: another watchdog fire owns observation'; exit 0 }
try {
$ErrorActionPreference = 'Stop'
$script:fireId = [guid]::NewGuid().ToString('N')
$script:state = Read-WatchdogState
$script:maintenance = Test-Path -LiteralPath (Join-Path $logDir 'apphost.down-on-purpose')
$script:firstRound = Test-Round
Observe-WatchdogOutage $script:firstRound
if ($script:maintenance) {
    $script:state.deadLaunch = $null
    Write-WatchdogState $script:state
    Write-Log 'INFO' 'skip: intentional AppHost maintenance'
    exit 0
}

Write-Log 'INFO' "fire beginning (PID $PID$(if ($ProbeOnly) { '; ProbeOnly' }))"

$gitIndexLock = Get-AppHostGitIndexLock -SourceRoot $root
if ($gitIndexLock -and $gitIndexLock.Stale) {
    Write-Log 'WARN' (Format-AppHostGitIndexLockNote $gitIndexLock)
}

$inFlight = Test-LaunchInFlight
if ($inFlight) {
    Write-Log 'INFO' "skip: launch in flight - $inFlight"
    exit 0
}


$roundsNeeded = 3
$downRounds = 0
$lastSummary = ''
for ($i = 1; $i -le $roundsNeeded; $i++) {
    if ($i -eq 1) { $round = $script:firstRound } else { $round = Test-Round; Observe-WatchdogOutage $round }
    $lastSummary = $round.Summary
    if ($round.Ok) {
        if ($i -gt 1) {
            Write-Log 'INFO' "probe round $i/$roundsNeeded recovered: $lastSummary"
        }
        $state = Read-WatchdogState
        $lastOk = ConvertTo-UtcStamp $state.lastOkUtc
        $now = [datetime]::UtcNow
        $logHeartbeat = $ProbeOnly -or (-not $lastOk) -or (($now - $lastOk).TotalHours -ge 1)
        if ($logHeartbeat) {
            Write-Log 'INFO' "OK $lastSummary"
            $state.lastOkUtc = $now.ToString('o')
            Write-WatchdogState $state
        }
        exit 0
    }
    if (-not $round.RestartWorthy) {
        Write-Log 'WARN' "health slow, client up - not counting as down ($lastSummary)"
        if ($i -lt $roundsNeeded) {
            Start-Sleep -Seconds $IntervalSeconds
        }
        continue
    }
    $downRounds++
    Write-Log 'WARN' "probe round $i/$roundsNeeded failed: $lastSummary"
    if ($i -lt $roundsNeeded) {
        Start-Sleep -Seconds $IntervalSeconds
    }
}

if ($downRounds -lt $roundsNeeded) {
    Write-Log 'WARN' "health slow, client up - not counting as down ($lastSummary)"
    exit 0
}

Write-Log 'WARN' "all $roundsNeeded probe rounds failed ($lastSummary)"

# Docker down is not a reason to bounce the AppHost - the launch would fail anyway.
docker info 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Log 'WARN' "skip: Docker is not responsive (not counting against flap budget)"
    exit 0
}

$state = Read-WatchdogState
$now = [datetime]::UtcNow
$restartTimes = @()
foreach ($stampText in @($state.restartsUtc)) {
    $t = ConvertTo-UtcStamp $stampText
    if ($t) { $restartTimes += $t }
}
$recent = @($restartTimes | Where-Object { ($now - $_).TotalMinutes -lt $flapWindowMinutes } | Sort-Object)

if ($recent.Count -gt 0) {
    $newest = $recent[-1]
    $minutesAgo = ($now - $newest).TotalMinutes
    if ($minutesAgo -lt $CooldownMinutes) {
        Write-Log 'INFO' ("skip: cooldown ({0:N1} min since last restart, need {1})" -f $minutesAgo, $CooldownMinutes)
        exit 0
    }
}

if ($recent.Count -ge $MaxRestartsPerWindow) {
    Write-Log 'ERROR' ("flap cap: {0} restarts in the last {1} min - not restarting until the window clears" -f $recent.Count, $flapWindowMinutes)
    exit 0
}

if ($ProbeOnly) {
    Write-Log 'INFO' "ProbeOnly: would restart via $restartScript"
    exit 0
}

if (-not (Test-Path $restartScript)) {
    Write-Log 'ERROR' "restart script missing: $restartScript"
    exit 1
}

# CARD-0075: the check at the top of this fire is up to ~60s old by now (three
# probe rounds at IntervalSeconds, plus docker info). Re-check immediately before
# doing anything destructive - one call closes the whole TOCTOU window.
$inFlight = Test-LaunchInFlight -Revalidate
if ($inFlight) {
    Write-Log 'INFO' "skip: launch in flight at restart time - $inFlight"
    exit 0
}

Write-Log 'WARN' "restarting AppHost via $restartScript"
$psExe = $null
try { $psExe = (Get-Process -Id $PID).Path } catch { }
if (-not $psExe) { $psExe = 'pwsh' }
$captured = Invoke-AppHostRestartCaptured -PowerShellExe $psExe -RestartScript $RestartScript
$restartExit = $captured.ExitCode
foreach ($line in @($captured.Tail)) {
    Write-Log 'INFO' ("restart-apphost: {0}" -f $line)
}

# Exit 3 = refused, nothing was killed. Stamping it would burn flap-cooldown
# budget on a restart that never happened, so the next real failure would be
# skipped by the cooldown.
if ($restartExit -eq 3) {
    Write-Log 'INFO' ("restart-apphost.ps1 REFUSED ({0}, nothing killed) - not stamping a restart" -f $captured.ExitName)
    exit 0
}

$state = Read-WatchdogState
$restartTimes = @()
foreach ($stampText in @($state.restartsUtc)) {
    $t = ConvertTo-UtcStamp $stampText
    if ($t -and ($now - $t).TotalMinutes -lt $flapWindowMinutes) { $restartTimes += $t.ToString('o') }
}
$restartTimes += [datetime]::UtcNow.ToString('o')
$state.restartsUtc = @($restartTimes)
$null = Send-AppHostOutageEvent -SourceRoot $root -State $state -NowUtc (Get-AppHostWatchdogNow)
Write-WatchdogState $state
Write-Log 'INFO' ("restart-apphost.ps1 exited {0}; stamped restart in {1}" -f $captured.ExitName, $stateFile)

exit 0

} finally { if ($fireLease) { $fireLease.Dispose() } }
