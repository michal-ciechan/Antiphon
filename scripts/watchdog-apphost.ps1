<#
.SYNOPSIS
    Health-triggered AppHost watchdog. Registered as the per-user Scheduled Task
    "Antiphon AppHost Watchdog" by scripts/install-autostart.ps1 and fired every
    two minutes. Probes the published HTTP endpoints (NOT TCP listen - dcpctrl
    owns 17202 and 17203) and, after three failed rounds, restarts the stack via
    scripts/restart-apphost.ps1 so the session-runner on 17204 is preserved.

    One fire does, in order:
      1. Bail if a launch or restart is in flight (logs/apphost.launch.lock or
         logs/apphost.restart.lock under 15 min old with a live holder, or the
         "Antiphon AppHost" logon task currently Running).
      2. GET /health on 17202 (expect 200) and GET / on 17203 (expect 2xx/3xx).
         On a restart-worthy failure (connection-refused / stack down), re-probe
         twice more at IntervalSeconds (15) spacing. Restart only if all three
         rounds look down. HttpClient timeout on /health while client=200 is
         NOT a failed round (CARD-0310: loaded or coming-up, not a corpse).
      3. If docker info fails, log and exit without counting against the flap budget.
      4. Cooldown: skip if a restart was stamped under CooldownMinutes (10) ago.
      5. Flap cap: skip and ERROR if MaxRestartsPerWindow (3) restarts sit inside
         the last 60 minutes.
      6. Re-check the locks (CARD-0075: step 1 ran up to ~60s of probing ago, and
         a restart that started inside that window must not be raced).
      7. Invoke restart-apphost.ps1, then stamp logs/apphost-watchdog.state -
         UNLESS it exits 3 (refused, nothing killed), which spends no flap budget
         and so must not be stamped.

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
    [int]$CooldownMinutes = 10,
    [int]$MaxRestartsPerWindow = 3,
    [int]$IntervalSeconds = 15,
    [int]$LockMaxAgeMinutes = 15,
    [int]$ProbeTimeoutSec = 5,
    [string]$HealthUrl = 'http://localhost:17202/health',
    [string]$ClientUrl = 'http://localhost:17203/',
    [string]$RestartScript
)

$ErrorActionPreference = 'Continue'

. (Join-Path $PSScriptRoot 'apphost-common.ps1')

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
    $state = [pscustomobject]@{
        lastOkUtc    = $null
        restartsUtc  = @()
    }
    if (-not (Test-Path $stateFile)) { return $state }
    try {
        $raw = Get-Content -LiteralPath $stateFile -Raw -ErrorAction Stop
        if ([string]::IsNullOrWhiteSpace($raw)) { return $state }
        $parsed = $raw | ConvertFrom-Json
        if ($parsed.lastOkUtc) {
            if ($parsed.lastOkUtc -is [datetime]) {
                $state.lastOkUtc = $parsed.lastOkUtc.ToUniversalTime().ToString('o')
            } else {
                $state.lastOkUtc = [string]$parsed.lastOkUtc
            }
        }
        if ($null -ne $parsed.restartsUtc) {
            $state.restartsUtc = @($parsed.restartsUtc | ForEach-Object {
                if ($_ -is [datetime]) { $_.ToUniversalTime().ToString('o') } else { [string]$_ }
            })
        }
    } catch {
        Write-Log 'WARN' "state file unreadable - treating as empty ($($_.Exception.Message))"
    }
    return $state
}

function Write-WatchdogState($state) {
    $payload = [pscustomobject]@{
        lastOkUtc   = $state.lastOkUtc
        restartsUtc = @($state.restartsUtc)
    }
    $payload | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $stateFile -Encoding UTF8
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
        Ok            = ($class.Kind -eq 'Up')
        RestartWorthy = $class.RestartWorthy
        Kind          = $class.Kind
        Summary       = $class.Summary
    }
}

function Test-LaunchInFlight {
    # CARD-0075: BOTH locks, not just the launch one. restart-apphost.ps1 tears the
    # stack down before dev-aspire.ps1 (and its launch lock) exists, so a fire that
    # begins inside that window sees no lock, three failed rounds, and calls a
    # second restart over the top of the first.
    $ah = Get-ScheduledTask -TaskName $appHostTaskName -ErrorAction SilentlyContinue
    if ($ah -and $ah.State -eq 'Running') {
        return "AppHost logon task '$appHostTaskName' is Running"
    }
    foreach ($pair in @(
        @{ Path = $restartLockFile; Label = 'restart lock' },
        @{ Path = $lockFile;        Label = 'launch lock'  })) {
        $lock = Get-AppHostLock -Path $pair.Path
        $held = Test-AppHostLockActive -Path $pair.Path -MaxAgeMinutes $LockMaxAgeMinutes -Label $pair.Label
        if ($held) { return $held }
        if ($lock.Exists) {
            Write-Log 'INFO' ("stale {0} - ignoring (stamp {1}, {2}; holder PID {3} alive={4}; {5})" -f `
                $pair.Label, (Format-AppHostLockStamp $lock), (Format-AppHostLockAge $lock), `
                $lock.ProcessId, $lock.HolderAlive, $pair.Path)
        }
    }
    return $null
}

Write-Log 'INFO' "fire beginning (PID $PID$(if ($ProbeOnly) { '; ProbeOnly' }))"

$inFlight = Test-LaunchInFlight
if ($inFlight) {
    Write-Log 'INFO' "skip: launch in flight - $inFlight"
    exit 0
}


$roundsNeeded = 3
$downRounds = 0
$lastSummary = ''
for ($i = 1; $i -le $roundsNeeded; $i++) {
    $round = Test-Round
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
$inFlight = Test-LaunchInFlight
if ($inFlight) {
    Write-Log 'INFO' "skip: launch in flight at restart time - $inFlight"
    exit 0
}

Write-Log 'WARN' "restarting AppHost via $restartScript"
$psExe = $null
try { $psExe = (Get-Process -Id $PID).Path } catch { }
if (-not $psExe) { $psExe = 'pwsh' }
& $psExe -NoLogo -NonInteractive -File $restartScript
$restartExit = $LASTEXITCODE

# Exit 3 = refused, nothing was killed. Stamping it would burn flap-cooldown
# budget on a restart that never happened, so the next real failure would be
# skipped by the cooldown.
if ($restartExit -eq 3) {
    Write-Log 'INFO' "restart-apphost.ps1 REFUSED (exit 3, nothing killed) - not stamping a restart"
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
Write-WatchdogState $state
Write-Log 'INFO' "restart-apphost.ps1 exited $restartExit; stamped restart in $stateFile"

exit 0
