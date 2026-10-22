#requires -Version 5.1
<#
.SYNOPSIS
    CARD-1407 cutover of the desktop Postgres volume to the server2 host.

    Default is -WhatIf, which prints phases 0 through 6 and their stop gates
    and touches nothing. -Execute without -ConfirmWindow refuses. The operator
    window is -Execute -ConfirmWindow. -Rollback -Execute -ConfirmWindow points
    AppHost back at the untouched desktop volume.

    The script never prints a password or a connection string, never removes a
    data directory, and never passes a worktree override to the restart script.
    Start it with pwsh -File. A same-process call cannot see -ConfirmWindow,
    and a value such as -HoldReason /ConfirmWindow never opens the window.
    -TailnetPort (default 5432) is the listener the phase 0 connect probes;
    tests point it at a loopback port. A failed keep-six -ClearHold at exit
    prints a RECOVERY line naming the agent. An enter that wrote the marker
    and then failed names the marker; exit 2 means it already existed.
    A real run that finds logs/apphost.down-on-purpose already present refuses
    before it enters maintenance. A marker that appears before the exclusive
    enter is not this run's, and this run does not clear it. After this run
    enters, every exit clears that marker and the keep-six hold. A failure
    after StopOnly and before the secret changes restarts AppHost on the old
    connection string first. If desktop postgres was stopped, it is started
    again before that restart.
    ASCII-only: it may run under Windows PowerShell 5.1.
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [switch]$WhatIf,
    [switch]$Execute,
    [switch]$ConfirmWindow,
    [switch]$Rollback,
    [switch]$AcceptDataLossRollback,
    [switch]$AcceptDrillTime,
    [switch]$TailnetOnlyConfirmed,
    [string]$SnapshotReference,
    [string]$DrillRecord,
    [string]$ConnectionSecretFile,
    [string]$RcTip,
    [string]$Repository,
    [string]$ProjectId,
    [string]$LandPendingFixture,
    [string]$DumpDirectory,
    [string]$AgeRecipientFile,
    [string]$AgeIdentityFile,
    [string]$Server2SshHost = 'server2',
    [string]$Server2ComposeDir,
    [string]$TailnetAddress,
    [ValidateRange(1, 65535)]
    [int]$TailnetPort = 5432,
    [string]$HoldAgent,
    [string]$HoldReason,
    [string]$HoldUntil,
    [string]$ApiBase = 'http://localhost:17202',
    [string]$CommandHost = 'pwsh'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
if ([string]::IsNullOrWhiteSpace($CommandHost)) { $CommandHost = 'pwsh' }

$script:KeyTables = @(
    'TranscriptEntries',
    'AgentTasks',
    'SessionQueuedMessages',
    'AgentTaskLandNotifications',
    'AgentTaskEvents'
)

function Fail-Cutover {
    param([string]$Message, [int]$Code = 3)
    Write-Host $Message
    exit $Code
}

# CARD-1417 A-1: the names this script binds, split by whether they consume the next token.
$script:CutoverSwitchParameters = @()
$script:CutoverValueParameters = @()
foreach ($entry in $MyInvocation.MyCommand.Parameters.GetEnumerator()) {
    if ($entry.Value.SwitchParameter) { $script:CutoverSwitchParameters += [string]$entry.Key }
    else { $script:CutoverValueParameters += [string]$entry.Key }
}

function Test-CutoverValueParameterName {
    # True when the dash token names (exactly, or as a unique prefix the binder accepts)
    # a parameter that takes the following token as its value.
    param([string]$Token)
    if ([string]::IsNullOrEmpty($Token)) { return $false }
    $all = @($script:CutoverValueParameters) + @($script:CutoverSwitchParameters)
    foreach ($name in $all) {
        if ($name.Equals($Token, [System.StringComparison]::OrdinalIgnoreCase)) {
            return (@($script:CutoverValueParameters) -contains $name)
        }
    }
    $candidates = @($all | Where-Object { $_.StartsWith($Token, [System.StringComparison]::OrdinalIgnoreCase) })
    if ($candidates.Count -ne 1) { return $false }
    return (@($script:CutoverValueParameters) -contains $candidates[0])
}

function Test-ExactNamedSwitch {
    # Only a dash token counts. A token that is the value of the previous value-taking
    # parameter is skipped, so -HoldReason /ConfirmWindow cannot open the window.
    param([string]$Name)
    $skipValue = $false
    foreach ($token in @([Environment]::GetCommandLineArgs())) {
        $text = [string]$token
        if ($skipValue) { $skipValue = $false; continue }
        if (-not $text.StartsWith('-')) { continue }
        if ($text.StartsWith('--')) { $text = $text.Substring(1) }
        $text = $text.Substring(1)
        $colon = $text.IndexOf(':')
        $carriesValue = ($colon -ge 0)
        if ($carriesValue) { $text = $text.Substring(0, $colon) }
        if ($text.Equals($Name, [System.StringComparison]::OrdinalIgnoreCase)) { return $true }
        if (-not $carriesValue -and (Test-CutoverValueParameterName $text)) { $skipValue = $true }
    }
    return $false
}

function Test-CutoverSha {
    param([string]$Value)
    return ($Value -match '^[0-9a-fA-F]{40}$' -or $Value -match '^[0-9a-fA-F]{64}$')
}

function Show-CutoverWhatIf {
    Write-Host 'PHASE 0 Pre-flight'
    Write-Host '  EXIT CHECK 0: verified snapshot reference exists, passed drill record, latency median at or under 20 ms, and the drill time gate.'
    Write-Host '  STOP GATE 0: refuse without a verified snapshot reference and a passed drill record, when latency is above 20 ms, when the drill misses the time gate, or when the new listener is reachable off the tailnet. No production writer has moved.'
    Write-Host 'PHASE 1 Quiesce'
    Write-Host '  EXIT CHECK 1: maintenance mode is entered before the hold and StopOnly, dispatch hold is recorded, landPending is empty, repository preview has no uncertain child, and StopOnly leaves AppHost ports free while 17204 and antiphon-postgres stay up.'
    Write-Host '  STOP GATE 1: landPending is not empty, the repository preview shows an uncertain child, StopOnly would relaunch, or port 17204 or 17280 was touched. A pre-existing logs/apphost.down-on-purpose marker refuses before this run enters maintenance. Leave the old database in place.'
    Write-Host 'PHASE 2 Final dump and restore'
    Write-Host '  EXIT CHECK 2: pg_dump -Fc sees no other session, and pg_restore exits 0.'
    Write-Host '  STOP GATE 2: another session appears, or pg_restore exits non-zero. Do not dump under writers. Leave the desktop container up.'
    Write-Host 'PHASE 3 Verify'
    Write-Host '  EXIT CHECK 3: count(*) per user table, sequences, extensions, migration ids, and key-table minimum and maximum primary keys match.'
    Write-Host '  STOP GATE 3: any mismatch. Do not repoint.'
    Write-Host 'PHASE 4 Repoint and restart'
    Write-Host '  EXIT CHECK 4: desktop postgres is stopped, antiphon_pgdata remains listed, the user-secrets key is set from stdin with its value not displayed, maintenance is lifted, and restart-apphost.ps1 -ExpectedServerSha exits 0.'
    Write-Host '  STOP GATE 4: compose offers to remove the data directory, restart exit is 3, or the SHA mismatches. Exit 5 keeps the restart lock. Go to phase 6 if the secret was already changed.'
    Write-Host 'PHASE 5 Post-checks'
    Write-Host '  EXIT CHECK 5: health is 200, the version SHA equals the rc tip and advertises land-v2, pool flags match, and server2 shows a desktop client.'
    Write-Host '  STOP GATE 5: a post-check fails. The operator runs phase 6. The script does not restart itself again.'
    Write-Host 'PHASE 6 Rollback'
    Write-Host '  EXIT CHECK 6: desktop postgres is started again, row counts are compared, and AppHost is restarted with -ExpectedServerSha against the untouched desktop volume. Maintenance is lifted after that restart.'
    Write-Host '  STOP GATE 6: if the new database has rows the old desktop volume lacks, the script stops unless -AcceptDataLossRollback was passed. Neither data directory is removed.'
    Write-Host 'WHATIF: does not POST, does not call docker, and does not call dotnet user-secrets set.'
    Write-Host 'REFUSE: without a verified snapshot reference and a passed drill record the window does not start.'
}

function Read-CutoverDrill {
    param([string]$Path)
    try {
        return (Get-Content -LiteralPath $Path -Raw -ErrorAction Stop | ConvertFrom-Json)
    } catch {
        Fail-Cutover 'REFUSED: passed drill record could not be read. Nothing was changed.'
    }
}

function Assert-CutoverPhase0Files {
    if ([string]::IsNullOrWhiteSpace($SnapshotReference) -or -not (Test-Path -LiteralPath $SnapshotReference -PathType Leaf)) {
        Fail-Cutover 'REFUSED: verified snapshot reference is missing. Nothing was changed.'
    }
    if ([string]::IsNullOrWhiteSpace($DrillRecord) -or -not (Test-Path -LiteralPath $DrillRecord -PathType Leaf)) {
        Fail-Cutover 'REFUSED: passed drill record is missing. Nothing was changed.'
    }
    $drill = Read-CutoverDrill $DrillRecord
    if ($drill.passed -ne $true) {
        Fail-Cutover 'REFUSED: drill record did not pass. Nothing was changed.'
    }
    $latency = $null
    if ($null -ne $drill.latencyMedianMs) { $latency = [double]$drill.latencyMedianMs }
    if ($null -eq $latency -or $latency -gt 20) {
        Fail-Cutover 'REFUSED: latency baseline gate is above 20 ms or empty. Nothing was changed.'
    }
    $dumpSec = $null
    $copySec = $null
    if ($null -ne $drill.dumpRestoreSec) { $dumpSec = [int]$drill.dumpRestoreSec }
    if ($null -ne $drill.copySec) { $copySec = [int]$drill.copySec }
    if ($null -eq $dumpSec -or $null -eq $copySec) {
        Fail-Cutover 'REFUSED: drill time gate is empty. Nothing was changed.'
    }
    $sum = $dumpSec + $copySec
    if ($sum -gt 1800) {
        Fail-Cutover 'REFUSED: drill dump plus copy exceeds the 30 minute envelope. Nothing was changed.'
    }
    if ($sum -gt 600 -and -not $AcceptDrillTime) {
        Fail-Cutover 'REFUSED: drill dump plus copy is above 600 seconds. Nothing was changed.'
    }
}

function Get-CutoverItemsCount {
    param($Doc)
    if ($null -eq $Doc -or $Doc -isnot [System.Management.Automation.PSCustomObject]) { return -3 }
    $prop = $Doc.PSObject.Properties['items']
    if ($null -eq $prop) { return -3 }
    if ($null -eq $Doc.items) { return 0 }
    return @($Doc.items).Count
}

function Get-CutoverLandPendingCount {
    if (-not [string]::IsNullOrWhiteSpace($LandPendingFixture)) {
        if (-not (Test-Path -LiteralPath $LandPendingFixture -PathType Leaf)) { return -1 }
        try {
            $doc = Get-Content -LiteralPath $LandPendingFixture -Raw -ErrorAction Stop | ConvertFrom-Json
        } catch {
            return -2
        }
        $count = Get-CutoverItemsCount $doc
        if ($count -lt 0) { return -2 }
        return $count
    }
    $pendingUri = '/api/agent-tasks?projectId=' + [uri]::EscapeDataString($ProjectId) + '&landPending=true'
    $uri = $ApiBase.TrimEnd('/') + '/' + $pendingUri.TrimStart('/')
    try {
        $response = Invoke-WebRequest -Method Get -Uri $uri -TimeoutSec 15 -UseBasicParsing
    } catch {
        Fail-Cutover ('REFUSED: GET ' + $pendingUri + ' failed. Body not printed. StopOnly was not run.')
    }
    try {
        $doc = $response.Content | ConvertFrom-Json
    } catch {
        Fail-Cutover 'REFUSED: landPending response was not JSON. Body not printed. StopOnly was not run.'
    }
    $count = Get-CutoverItemsCount $doc
    if ($count -lt 0) {
        Fail-Cutover 'REFUSED: landPending response has no items array. Body not printed. StopOnly was not run.'
    }
    return $count
}

function Invoke-CutoverCommand {
    param(
        [Parameter(Mandatory = $true)][string[]]$ArgumentList,
        [switch]$Quiet
    )
    if ($ArgumentList.Count -lt 1) { return 1 }
    $exe = [string]$ArgumentList[0]
    $rest = @()
    if ($ArgumentList.Count -gt 1) {
        $rest = [string[]]$ArgumentList[1..($ArgumentList.Count - 1)]
    }
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        if ($Quiet) {
            & $exe @rest 2>&1 | Out-Null
        } else {
            & $exe @rest 2>&1 | Out-Host
        }
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previous
    }
    if ($null -eq $code) { return 0 }
    return [int]$code
}

function Invoke-CutoverMaintenance {
    param([switch]$Clear)
    $path = Join-Path $Repository 'scripts/set-apphost-maintenance.ps1'
    $marker = Get-CutoverMaintenanceMarkerPath
    if (-not $Clear) {
        if (Test-Path -LiteralPath $marker) {
            Fail-Cutover 'REFUSED: maintenance marker already exists at logs/apphost.down-on-purpose. This run did not enter it. The operator must clear the marker before the window can start.'
        }
    }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        Fail-Cutover 'REFUSED: maintenance script is missing.'
    }
    $maintArgs = @($script:CommandHost, '-NoProfile', '-File', $path)
    if ($Clear) { $maintArgs += '-Clear' }
    else { $maintArgs += '-EnterIfAbsent' }
    $code = Invoke-CutoverCommand -ArgumentList $maintArgs
    if ($code -ne 0) {
        if ($Clear) {
            Fail-Cutover 'REFUSED: AppHost maintenance clear exit check failed.'
        }
        # CARD-1417 A-3: -EnterIfAbsent exits 2 only when the marker already existed (create-new
        # semantics). Any other failure happened after this run wrote the marker, or before it.
        if ($code -eq 2) {
            Fail-Cutover 'REFUSED: AppHost maintenance enter exit check failed. Exit 2: the marker logs/apphost.down-on-purpose already existed. This run did not enter it and does not clear it. The operator must clear the marker before the window can start.'
        }
        if (Test-Path -LiteralPath $marker) {
            Fail-Cutover ('REFUSED: AppHost maintenance enter exit check failed. Exit ' + $code + ': the marker logs/apphost.down-on-purpose was written by this run before the failure and is left in place. Check the watchdog task, then clear it with scripts/set-apphost-maintenance.ps1 -Clear before retrying.')
        }
        Fail-Cutover ('REFUSED: AppHost maintenance enter exit check failed. Exit ' + $code + ': the marker logs/apphost.down-on-purpose is absent. Nothing was entered.')
    }
    if ($Clear) { Write-Host 'Maintenance lifted. Watchdog may run again.' }
    else { Write-Host 'Maintenance entered before quiesce. Watchdog will not restart AppHost.' }
}

function Clear-CutoverMaintenanceQuiet {
    $path = Join-Path $Repository 'scripts/set-apphost-maintenance.ps1'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return 1 }
    return (Invoke-CutoverCommand -ArgumentList @($script:CommandHost, '-NoProfile', '-File', $path, '-Clear'))
}

function Clear-CutoverHold {
    $hold = Join-Path $Repository 'scripts/keep-six.ps1'
    return (Invoke-CutoverCommand -ArgumentList @($script:CommandHost, '-NoProfile', '-File', $hold, '-ClearHold', '-Agent', $HoldAgent))
}

function Reset-CutoverWindowState {
    $script:CutoverEnteredMaintenance = $false
    $script:CutoverHoldRecorded = $false
    $script:CutoverStopOnlyCompleted = $false
    $script:CutoverSecretChanged = $false
    $script:CutoverOldStringRestartAttempted = $false
    $script:CutoverOldStringRestartSucceeded = $false
    $script:CutoverDesktopPostgresStopped = $false
    $script:CutoverWindowSucceeded = $false
}

function Get-CutoverMaintenanceMarkerPath {
    return (Join-Path (Join-Path $Repository 'logs') 'apphost.down-on-purpose')
}

function Assert-CutoverMaintenanceFree {
    $marker = Get-CutoverMaintenanceMarkerPath
    if (Test-Path -LiteralPath $marker) {
        Fail-Cutover 'REFUSED: maintenance marker already exists at logs/apphost.down-on-purpose. This run did not enter it. The operator must clear the marker before the window can start.'
    }
}

function Clear-CutoverRecordedHold {
    if (-not $script:CutoverHoldRecorded) { return 0 }
    $code = 1
    try { $code = Clear-CutoverHold } catch { $code = 1 }
    if ($code -eq 0) { $script:CutoverHoldRecorded = $false }
    return $code
}

function Clear-CutoverRecordedMaintenance {
    if (-not $script:CutoverEnteredMaintenance) { return 0 }
    $code = 1
    try { $code = Clear-CutoverMaintenanceQuiet } catch { $code = 1 }
    if ($code -eq 0) { $script:CutoverEnteredMaintenance = $false }
    return $code
}

function Complete-CutoverWindowExit {
    param([switch]$AllowOldStringRestart)
    # Never throw here. The caller's exit code and message must survive.
    try {
        $entered = [bool]$script:CutoverEnteredMaintenance
        $held = [bool]$script:CutoverHoldRecorded
        $stopped = [bool]$script:CutoverStopOnlyCompleted
        $secret = [bool]$script:CutoverSecretChanged
        $restarted = [bool]$script:CutoverOldStringRestartAttempted
        $restartOk = [bool]$script:CutoverOldStringRestartSucceeded
        $success = [bool]$script:CutoverWindowSucceeded
        $skippedRestart = $false
        if ($AllowOldStringRestart -and $stopped -and -not $secret -and -not $restarted) {
            $script:CutoverOldStringRestartAttempted = $true
            $postgresReady = $true
            if ($script:CutoverDesktopPostgresStopped) {
                $postgresReady = $false
                $startCode = 1
                try {
                    $compose = Join-Path $Repository 'docker-compose.dev.yml'
                    $startCode = Invoke-CutoverCommand -ArgumentList @('docker', 'compose', '-f', $compose, 'start', 'postgres')
                } catch {
                    $startCode = 1
                }
                if ($startCode -eq 0) { $postgresReady = $true }
            }
            if (-not $postgresReady) {
                $skippedRestart = $true
                Write-Host 'RECOVERY: desktop postgres did not start. Run docker compose -f docker-compose.dev.yml start postgres, then restart-apphost.ps1 -ExpectedServerSha. The secret was not changed.'
            } else {
                $code = 1
                try {
                    $code = Invoke-CutoverRestart -ExpectedSha $RcTip
                } catch {
                    $code = 1
                }
                if ($code -eq 0) {
                    $script:CutoverOldStringRestartSucceeded = $true
                    Write-Host 'RECOVERY: restarted AppHost on the old connection string before lifting maintenance. The secret was not changed.'
                } else {
                    Write-Host ('RECOVERY: AppHost is still down. The secret was not changed. Start AppHost on the old connection string with restart-apphost.ps1 -ExpectedServerSha. Restart exit ' + $code + '.')
                }
            }
        } elseif (-not $success -and $secret) {
            Write-Host 'RECOVERY: the connection secret was already changed. This run does not restart on the old string. Operator runs phase 6.'
        } elseif ($AllowOldStringRestart -and -not $success -and $stopped -and -not $secret -and $restarted -and -not $restartOk -and -not $skippedRestart) {
            Write-Host 'RECOVERY: AppHost is still down. The secret was not changed. Start AppHost on the old connection string with restart-apphost.ps1 -ExpectedServerSha.'
        } elseif ($AllowOldStringRestart -and -not $success -and $entered -and -not $stopped) {
            Write-Host 'RECOVERY: StopOnly did not complete. This run does not restart AppHost. Maintenance entered by this run will be lifted.'
        } elseif (-not $AllowOldStringRestart -and -not $success -and $entered) {
            Write-Host 'RECOVERY: rollback did not finish. Do not start a second copy if the restart lock is held. Maintenance entered by this run will be lifted.'
        }
        if ($held) {
            try {
                # CARD-1417 A-2: a non-zero clear is reported, never swallowed.
                $holdCode = Clear-CutoverRecordedHold
                if ($holdCode -ne 0) {
                    Write-Host ('RECOVERY: keep-six -ClearHold exited ' + $holdCode + '. The dispatch hold for agent ' + $HoldAgent + ' is still recorded. Clear it with scripts/keep-six.ps1 -ClearHold -Agent ' + $HoldAgent + '.')
                }
            } catch {
                Write-Host 'RECOVERY: keep-six -ClearHold failed. The original error still stands.'
            }
        }
        if ($script:CutoverEnteredMaintenance) {
            try {
                $clearCode = Clear-CutoverRecordedMaintenance
                if ($clearCode -ne 0) {
                    Write-Host 'RECOVERY: maintenance clear failed. The original error still stands. The operator clears it with set-apphost-maintenance.ps1 -Clear only if this run entered maintenance.'
                }
            } catch {
                Write-Host 'RECOVERY: maintenance clear failed. The original error still stands.'
            }
        }
    } catch {
        Write-Host 'RECOVERY: cleanup failed. The original error still stands.'
    }
}

function Assert-CutoverOperatorInputs {
    if (-not (Test-CutoverSha $RcTip)) {
        Fail-Cutover 'REFUSED: rc tip must be the full SHA passed to -ExpectedServerSha. StopOnly was not run.'
    }
    $restart = Join-Path $Repository 'scripts/restart-apphost.ps1'
    $compose = Join-Path $Repository 'docker-compose.dev.yml'
    if ([string]::IsNullOrWhiteSpace($Repository) -or -not (Test-Path -LiteralPath $restart -PathType Leaf) -or -not (Test-Path -LiteralPath $compose -PathType Leaf)) {
        Fail-Cutover 'REFUSED: -Repository must be the main checkout. StopOnly was not run.'
    }
    if ([string]::IsNullOrWhiteSpace($ConnectionSecretFile) -or -not (Test-Path -LiteralPath $ConnectionSecretFile -PathType Leaf)) {
        Fail-Cutover 'REFUSED: connection secret file is missing. Value not displayed. StopOnly was not run.'
    }
    if ([string]::IsNullOrWhiteSpace($ProjectId)) {
        Fail-Cutover 'REFUSED: project id is missing. StopOnly was not run.'
    }
    if ([string]::IsNullOrWhiteSpace($DumpDirectory) -or -not (Test-Path -LiteralPath $DumpDirectory -PathType Container)) {
        Fail-Cutover 'REFUSED: dump directory is missing. StopOnly was not run.'
    }
    if ([string]::IsNullOrWhiteSpace($AgeRecipientFile) -or -not (Test-Path -LiteralPath $AgeRecipientFile -PathType Leaf)) {
        Fail-Cutover 'REFUSED: age recipient file is missing. StopOnly was not run.'
    }
    if ([string]::IsNullOrWhiteSpace($AgeIdentityFile) -or -not (Test-Path -LiteralPath $AgeIdentityFile -PathType Leaf)) {
        Fail-Cutover 'REFUSED: age identity file is missing. StopOnly was not run.'
    }
    if ([string]::IsNullOrWhiteSpace($Server2ComposeDir)) {
        Fail-Cutover 'REFUSED: server2 compose directory is missing. StopOnly was not run.'
    }
    if ([string]::IsNullOrWhiteSpace($TailnetAddress) -or -not $TailnetOnlyConfirmed) {
        Fail-Cutover 'REFUSED: tailnet address is missing or the listener was not confirmed tailnet-only. StopOnly was not run.'
    }
    if ([string]::IsNullOrWhiteSpace($HoldAgent) -or [string]::IsNullOrWhiteSpace($HoldReason) -or [string]::IsNullOrWhiteSpace($HoldUntil)) {
        Fail-Cutover 'REFUSED: keep-six hold agent, reason, and until are required. StopOnly was not run.'
    }
}

function Invoke-CutoverGet {
    param([string]$Relative)
    $uri = $ApiBase.TrimEnd('/') + '/' + $Relative.TrimStart('/')
    try {
        return Invoke-RestMethod -Method Get -Uri $uri -TimeoutSec 15
    } catch {
        Fail-Cutover ('REFUSED: GET ' + $Relative + ' failed. Body not printed. StopOnly was not run.')
    }
}

function Invoke-CutoverRestart {
    param([switch]$StopOnly, [string]$ExpectedSha)
    $restart = Join-Path $Repository 'scripts/restart-apphost.ps1'
    $restartArgs = @($script:CommandHost, '-NoProfile', '-File', $restart)
    if ($StopOnly) { $restartArgs += '-StopOnly' }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedSha)) {
        $restartArgs += @('-ExpectedServerSha', $ExpectedSha)
    }
    return (Invoke-CutoverCommand -ArgumentList $restartArgs)
}

function Set-CutoverConnectionSecret {
    param([switch]$Remove)
    $project = Join-Path $Repository 'Antiphon.AppHost/Antiphon.AppHost.csproj'
    if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
        Fail-Cutover 'REFUSED: AppHost project is missing. Value not displayed.'
    }
    if ($Remove) {
        Write-Host 'Removing ConnectionStrings:DefaultConnection. Value not displayed.'
        $removeCode = Invoke-CutoverCommand -Quiet -ArgumentList @('dotnet', 'user-secrets', 'remove', 'ConnectionStrings:DefaultConnection', '--id', 'aspire-antiphon-apphost', '--project', $project)
        if ($removeCode -ne 0) {
            Write-Host 'ConnectionStrings:DefaultConnection was already absent or could not be removed. Value not displayed.'
        } else {
            Write-Host 'ConnectionStrings:DefaultConnection removed. Value not displayed.'
        }
        return
    }
    $raw = [System.IO.File]::ReadAllText($ConnectionSecretFile).Trim()
    if ([string]::IsNullOrWhiteSpace($raw) -or $raw.Contains("`n") -or $raw.Contains("`r")) {
        Fail-Cutover 'REFUSED: connection secret file must be one non-empty line. Value not displayed.'
    }
    Write-Host 'Setting ConnectionStrings:DefaultConnection from the secret file. Value not displayed.'
    # stdin JSON merges keys into the existing user-secrets store. The value is not an argument.
    $json = ConvertTo-Json -InputObject @{ 'ConnectionStrings:DefaultConnection' = $raw } -Compress
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $json | & dotnet user-secrets set --id 'aspire-antiphon-apphost' --project $project 2>&1 | Out-Null
        $code = $LASTEXITCODE
        if ($null -eq $code) { $code = 1 } else { $code = [int]$code }
    } finally {
        $ErrorActionPreference = $previous
        $raw = $null
        $json = $null
    }
    if ($code -ne 0) {
        Fail-Cutover 'REFUSED: user-secrets set failed. Value not displayed.'
    }
    Write-Host 'ConnectionStrings:DefaultConnection is present. Value not displayed.'
}

function Invoke-CutoverPsql {
    param([string]$Target, [string]$Sql)
    $file = Join-Path $DumpDirectory ('cutover-query-' + [guid]::NewGuid().ToString('N') + '.sql')
    Set-Content -LiteralPath $file -Value $Sql -Encoding ascii
    try {
        if ($Target -eq 'source') {
            $raw = Get-Content -LiteralPath $file -Raw | & docker exec -i antiphon-postgres psql -U antiphon -d antiphon -At -v ON_ERROR_STOP=1 2>&1 | Out-String
        } else {
            $remote = "cd '$Server2ComposeDir' && docker compose -p antiphon-server2-postgres exec -T postgres psql -U antiphon -d antiphon -At -v ON_ERROR_STOP=1"
            $raw = Get-Content -LiteralPath $file -Raw | & ssh $Server2SshHost $remote 2>&1 | Out-String
        }
        if ($LASTEXITCODE -ne 0) { return $null }
        return $raw.Trim()
    } finally {
        Remove-Item -LiteralPath $file -Force -ErrorAction SilentlyContinue
    }
}

function Assert-CutoverEqual {
    param([string]$Label, [string]$SourceText, [string]$TargetText)
    if ($null -eq $SourceText -or $null -eq $TargetText) {
        Fail-Cutover ('REFUSED: phase 3 query failed for ' + $Label + '. Row text was not printed.')
    }
    if ($SourceText -ne $TargetText) {
        Fail-Cutover ('REFUSED: phase 3 mismatch for ' + $Label + '. Row text was not printed. Do not repoint.')
    }
    Write-Host ('PHASE 3 match: ' + $Label)
}

function Invoke-CutoverVerify {
    $tables = Invoke-CutoverPsql -Target source -Sql "SELECT format('%I.%I', n.nspname, c.relname) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'public' AND c.relkind = 'r' ORDER BY 1"
    $targetTables = Invoke-CutoverPsql -Target target -Sql "SELECT format('%I.%I', n.nspname, c.relname) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'public' AND c.relkind = 'r' ORDER BY 1"
    Assert-CutoverEqual -Label 'user-table-names' -SourceText $tables -TargetText $targetTables
    foreach ($name in @($tables -split "`n")) {
        $ident = $name.Trim()
        if ([string]::IsNullOrWhiteSpace($ident)) { continue }
        if ($ident -notmatch '^[A-Za-z0-9_."]+$') {
            Fail-Cutover 'REFUSED: phase 3 table name was not a catalog identifier. Row text was not printed.'
        }
        $countSql = 'SELECT count(*) FROM ' + $ident
        Assert-CutoverEqual -Label ('count ' + $ident) -SourceText (Invoke-CutoverPsql -Target source -Sql $countSql) -TargetText (Invoke-CutoverPsql -Target target -Sql $countSql)
    }
    $seq = "SELECT schemaname || '.' || sequencename || ' ' || COALESCE(last_value::text, '') || ' ' || is_called::text FROM pg_sequences ORDER BY 1"
    Assert-CutoverEqual -Label 'sequences' -SourceText (Invoke-CutoverPsql -Target source -Sql $seq) -TargetText (Invoke-CutoverPsql -Target target -Sql $seq)
    $ext = "SELECT extname || ' ' || extversion FROM pg_extension ORDER BY 1"
    Assert-CutoverEqual -Label 'extensions' -SourceText (Invoke-CutoverPsql -Target source -Sql $ext) -TargetText (Invoke-CutoverPsql -Target target -Sql $ext)
    $mig = 'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1'
    Assert-CutoverEqual -Label 'migrations' -SourceText (Invoke-CutoverPsql -Target source -Sql $mig) -TargetText (Invoke-CutoverPsql -Target target -Sql $mig)
    foreach ($table in $script:KeyTables) {
        $keySql = "SELECT a.attname FROM pg_index i JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY (i.indkey) WHERE i.indrelid = 'public.`"$table`"'::regclass AND i.indisprimary ORDER BY array_position(i.indkey, a.attnum) LIMIT 1"
        $key = Invoke-CutoverPsql -Target source -Sql $keySql
        if ([string]::IsNullOrWhiteSpace($key) -or $key -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') {
            Fail-Cutover ('REFUSED: phase 3 primary key missing for ' + $table + '. Row text was not printed.')
        }
        $probe = 'SELECT count(*)::text || '' '' || min("' + $key + '")::text || '' '' || max("' + $key + '")::text FROM "' + $table + '"'
        Assert-CutoverEqual -Label ('key ' + $table) -SourceText (Invoke-CutoverPsql -Target source -Sql $probe) -TargetText (Invoke-CutoverPsql -Target target -Sql $probe)
    }
}

function Get-CutoverCountMap {
    param([string]$Target)
    $tables = Invoke-CutoverPsql -Target $Target -Sql "SELECT format('%I.%I', n.nspname, c.relname) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'public' AND c.relkind = 'r' ORDER BY 1"
    if ($null -eq $tables) { return $null }
    $map = @{}
    foreach ($name in @($tables -split "`n")) {
        $ident = $name.Trim()
        if ([string]::IsNullOrWhiteSpace($ident)) { continue }
        if ($ident -notmatch '^[A-Za-z0-9_."]+$') { return $null }
        $countText = Invoke-CutoverPsql -Target $Target -Sql ('SELECT count(*) FROM ' + $ident)
        if ($null -eq $countText) { return $null }
        $parsed = 0
        if (-not [int]::TryParse($countText.Trim(), [ref]$parsed)) { return $null }
        $map[$ident] = $parsed
    }
    return $map
}

function Test-CutoverNewHasExtraRows {
    $source = Get-CutoverCountMap -Target 'source'
    $target = Get-CutoverCountMap -Target 'target'
    if ($null -eq $source -or $null -eq $target) { return $null }
    foreach ($key in @($target.Keys)) {
        $old = 0
        if ($source.ContainsKey($key)) { $old = [int]$source[$key] }
        if ([int]$target[$key] -gt $old) { return $true }
    }
    return $false
}

function Invoke-CutoverPipeDumpToAge {
    param([string]$Encrypted)
    $dump = New-Object System.Diagnostics.Process
    $age = New-Object System.Diagnostics.Process
    $dumpStarted = $false
    $ageStarted = $false
    try {
        $dump.StartInfo = New-Object System.Diagnostics.ProcessStartInfo
        $dump.StartInfo.FileName = 'docker'
        $dump.StartInfo.Arguments = 'exec antiphon-postgres pg_dump -U antiphon -d antiphon -Fc --no-owner --no-acl'
        $dump.StartInfo.UseShellExecute = $false
        $dump.StartInfo.RedirectStandardOutput = $true
        $dump.StartInfo.RedirectStandardError = $true
        $age.StartInfo = New-Object System.Diagnostics.ProcessStartInfo
        $age.StartInfo.FileName = 'age'
        $age.StartInfo.Arguments = '-R "' + $AgeRecipientFile + '" -o "' + $Encrypted + '"'
        $age.StartInfo.UseShellExecute = $false
        $age.StartInfo.RedirectStandardInput = $true
        $age.StartInfo.RedirectStandardOutput = $true
        $age.StartInfo.RedirectStandardError = $true
        if (-not $dump.Start()) { return $false }
        $dumpStarted = $true
        if (-not $age.Start()) { return $false }
        $ageStarted = $true
        $copy = $dump.StandardOutput.BaseStream.CopyToAsync($age.StandardInput.BaseStream)
        $dumpErr = $dump.StandardError.ReadToEndAsync()
        $ageErr = $age.StandardError.ReadToEndAsync()
        $ageOut = $age.StandardOutput.ReadToEndAsync()
        $dump.WaitForExit()
        $copy.Wait()
        $age.StandardInput.Close()
        $age.WaitForExit()
        $null = $dumpErr.Result
        $null = $ageErr.Result
        $null = $ageOut.Result
        if ($dump.ExitCode -ne 0 -or $age.ExitCode -ne 0) { return $false }
        return $true
    } catch {
        return $false
    } finally {
        if ($dumpStarted) {
            try { if (-not $dump.HasExited) { $dump.Kill() } } catch {}
        }
        if ($ageStarted) {
            try { if (-not $age.HasExited) { $age.Kill() } } catch {}
        }
    }
}

function Invoke-CutoverCopyAndRestore {
    param([string]$Encrypted)
    $remoteName = Split-Path -Leaf $Encrypted
    $remoteAge = $Server2ComposeDir.TrimEnd('/') + '/' + $remoteName
    $copyCode = Invoke-CutoverCommand -ArgumentList @('scp', $Encrypted, ($Server2SshHost + ':' + $remoteAge))
    if ($copyCode -ne 0) { return 7 }
    $remote = "cd '$Server2ComposeDir' && age -d -i - './$remoteName' | docker compose -p antiphon-server2-postgres exec -T postgres pg_restore -U antiphon -d antiphon --no-owner --no-acl; rc=`$?; rm -f './$remoteName'; exit `$rc"
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        Get-Content -LiteralPath $AgeIdentityFile -Raw -ErrorAction Stop | & ssh $Server2SshHost $remote 2>&1 | Out-Null
        $code = $LASTEXITCODE
        if ($null -eq $code) { $code = 1 } else { $code = [int]$code }
    } catch {
        $code = 1
    } finally {
        $ErrorActionPreference = $previous
    }
    $null = Invoke-CutoverCommand -ArgumentList @('ssh', $Server2SshHost, ("rm -f '" + $remoteAge + "'"))
    return [int]$code
}

function Invoke-CutoverRollback {
    Assert-CutoverOperatorInputs
    Reset-CutoverWindowState
    Assert-CutoverMaintenanceFree
    $compose = Join-Path $Repository 'docker-compose.dev.yml'
    Write-Host 'PHASE 6: starting desktop postgres. The data directory is not removed.'
    $startCode = Invoke-CutoverCommand -ArgumentList @('docker', 'compose', '-f', $compose, 'start', 'postgres')
    if ($startCode -ne 0) { Fail-Cutover 'REFUSED: desktop postgres did not start.' }
    $extra = Test-CutoverNewHasExtraRows
    if ($null -eq $extra) {
        Fail-Cutover 'REFUSED: stop gate 6: row comparison failed. Neither data directory is removed.'
    }
    if ($extra -and -not $AcceptDataLossRollback) {
        Fail-Cutover 'REFUSED: stop gate 6: the new database has rows the old desktop volume lacks. Pass -AcceptDataLossRollback to roll back. Neither data directory is removed.'
    }
    if ($extra) {
        Write-Host 'PHASE 6: -AcceptDataLossRollback accepted. Rows that exist only on the new database will be left behind.'
    }
    try {
        Invoke-CutoverMaintenance
        $script:CutoverEnteredMaintenance = $true
        Set-CutoverConnectionSecret -Remove
        $code = Invoke-CutoverRestart -ExpectedSha $RcTip
        if ($code -ne 0) { Fail-Cutover ('REFUSED: rollback restart exited ' + $code + '. Do not start a second copy if the lock is held.') }
        Invoke-CutoverMaintenance -Clear
        $script:CutoverEnteredMaintenance = $false
        Write-Host 'PHASE 6 exit check: AppHost restarted against the untouched desktop volume. Maintenance lifted.'
        $script:CutoverWindowSucceeded = $true
        exit 0
    } finally {
        Complete-CutoverWindowExit
    }
}

function Invoke-CutoverWindow {
    Assert-CutoverPhase0Files
    Assert-CutoverOperatorInputs
    Reset-CutoverWindowState
    Assert-CutoverMaintenanceFree

    try {
    $version = Invoke-CutoverGet '/api/version'
    $sha = [string]$version.version
    if ($sha.ToLowerInvariant() -ne $RcTip.ToLowerInvariant()) {
        Fail-Cutover 'REFUSED: GET /api/version SHA does not equal the rc tip. StopOnly was not run.'
    }
    $caps = @($version.capabilities)
    if ($caps -notcontains 'land-v2') {
        Fail-Cutover 'REFUSED: GET /api/version does not advertise land-v2. StopOnly was not run.'
    }
    $state = Invoke-CutoverGet '/api/diagnostics/session-state'
    $pool = $state.pool
    $pooling = $pool.Pooling
    if ($null -eq $pooling) { $pooling = $pool.pooling }
    $noReset = $pool.NoResetOnClose
    if ($null -eq $noReset) { $noReset = $pool.noResetOnClose }
    $maxAuto = $pool.MaxAutoPrepare
    if ($null -eq $maxAuto) { $maxAuto = $pool.maxAutoPrepare }
    $multi = $pool.Multiplexing
    if ($null -eq $multi) { $multi = $pool.multiplexing }
    if ($pooling -ne $true -or $noReset -ne $true -or [int]$maxAuto -ne 0 -or $multi -ne $false) {
        Fail-Cutover 'REFUSED: pool flags differ from the expected window. Connection text was not printed. StopOnly was not run.'
    }
    Write-Host 'PHASE 0 pool flags match. Connection text was not printed.'

    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $wait = $client.BeginConnect($TailnetAddress, $TailnetPort, $null, $null)
        $opened = $wait.AsyncWaitHandle.WaitOne(3000, $false) -and $client.Connected
    } finally {
        $client.Close()
    }
    if (-not $opened) {
        Fail-Cutover ('REFUSED: tailnet ' + $TailnetPort + ' did not accept a desktop connect. StopOnly was not run.')
    }
    Write-Host ('PHASE 0 tailnet ' + $TailnetPort + ' accepted a desktop connect.')

    Invoke-CutoverMaintenance
    $script:CutoverEnteredMaintenance = $true
    $hold = Join-Path $Repository 'scripts/keep-six.ps1'
    $holdCode = Invoke-CutoverCommand -ArgumentList @($script:CommandHost, '-NoProfile', '-File', $hold, '-Hold', '-Agent', $HoldAgent, '-Reason', $HoldReason, '-Until', $HoldUntil)
    if ($holdCode -ne 0) {
        Clear-CutoverRecordedMaintenance | Out-Null
        Fail-Cutover 'REFUSED: dispatch hold was not recorded. StopOnly was not run.'
    }
    $script:CutoverHoldRecorded = $true
    $pendingCount = Get-CutoverLandPendingCount
    if ($pendingCount -lt 0) {
        Clear-CutoverRecordedHold | Out-Null
        Clear-CutoverRecordedMaintenance | Out-Null
        if ($pendingCount -eq -1) {
            Fail-Cutover 'REFUSED: landPending fixture is missing. StopOnly was not run.'
        }
        Fail-Cutover 'REFUSED: landPending fixture could not be read. StopOnly was not run.'
    }
    if ($pendingCount -ne 0) {
        $released = Clear-CutoverRecordedHold
        Clear-CutoverRecordedMaintenance | Out-Null
        if ($released -ne 0) {
            Fail-Cutover 'REFUSED: landPending is not empty, and the keep-six hold could not be released. StopOnly was not run.'
        }
        Fail-Cutover 'REFUSED: landPending is not empty. StopOnly was not run.'
    }
    Write-Host 'PHASE 1 landPending is empty.'

    $preview = Join-Path $Repository 'scripts/recover-repository-children.ps1'
    $previewCode = Invoke-CutoverCommand -ArgumentList @($script:CommandHost, '-NoProfile', '-File', $preview, '-Repository', $Repository)
    if ($previewCode -ne 0) {
        Clear-CutoverRecordedHold | Out-Null
        Clear-CutoverRecordedMaintenance | Out-Null
        Fail-Cutover 'REFUSED: repository preview is not clear. StopOnly was not run.'
    }

    $stopCode = Invoke-CutoverRestart -StopOnly
    if ($stopCode -ne 0) {
        Clear-CutoverRecordedHold | Out-Null
        Clear-CutoverRecordedMaintenance | Out-Null
        Fail-Cutover ('REFUSED: StopOnly exited ' + $stopCode + '. The old database stays in place.')
    }
    $script:CutoverStopOnlyCompleted = $true
    Write-Host 'PHASE 1 StopOnly exited 0. Relaunch was not requested by this script.'

    $sessions = Invoke-CutoverPsql -Target source -Sql "SELECT count(*) FROM pg_stat_activity WHERE datname = 'antiphon' AND pid <> pg_backend_pid()"
    if ($sessions -ne '0') {
        Write-Host 'STOP GATE 2: another session is connected. Start AppHost again on the old string. The secret was not changed.'
        $script:CutoverOldStringRestartAttempted = $true
        $abandon = Invoke-CutoverRestart -ExpectedSha $RcTip
        if ($abandon -eq 0) {
            $script:CutoverOldStringRestartSucceeded = $true
            Clear-CutoverRecordedMaintenance | Out-Null
        }
        Fail-Cutover ('REFUSED: dump refused because another session is connected. Restart exit ' + $abandon + '.')
    }
    $stamp = [datetime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
    $plain = Join-Path $DumpDirectory ('antiphon-cutover-' + $stamp + '.dump')
    $encrypted = Join-Path $DumpDirectory ('antiphon-cutover-' + $stamp + '.dump.age')
    try {
        $piped = $false
        try { $piped = Invoke-CutoverPipeDumpToAge -Encrypted $encrypted } catch { $piped = $false }
        if (-not $piped) {
            Fail-Cutover 'REFUSED: pg_dump -Fc or age encrypt failed. The secret was not changed.'
        }
        $restoreCode = Invoke-CutoverCopyAndRestore -Encrypted $encrypted
        if ($restoreCode -eq 7) { Fail-Cutover 'REFUSED: copy to server2 failed. The secret was not changed.' }
        if ($restoreCode -ne 0) {
            Fail-Cutover 'REFUSED: pg_restore exited non-zero. AppHost stays down. The secret was not changed. Start AppHost on the old string after confirming the secret file was not applied.'
        }
        Write-Host 'PHASE 2 pg_restore exited 0. Encrypted witness kept beside the dump.'
    } finally {
        if (Test-Path -LiteralPath $plain) {
            Remove-Item -LiteralPath $plain -Force -ErrorAction SilentlyContinue
        }
    }

    Invoke-CutoverVerify
    Write-Host 'PHASE 3 exit check passed. Row text was not printed.'

    $compose = Join-Path $Repository 'docker-compose.dev.yml'
    $stopOut = & docker compose -f $compose stop postgres 2>&1 | Out-String
    $stopCode = $LASTEXITCODE
    if ($null -eq $stopCode) { $stopCode = 1 } else { $stopCode = [int]$stopCode }
    if ($stopCode -ne 0) { Fail-Cutover 'REFUSED: desktop postgres did not stop. The secret was not changed.' }
    $script:CutoverDesktopPostgresStopped = $true
    if ($stopOut -match '(?i)antiphon_pgdata' -and $stopOut -match '(?i)remove') {
        Fail-Cutover 'REFUSED: compose output named the data directory with a removal. The secret was not changed.'
    }
    $names = @(& docker volume ls --format '{{.Name}}')
    $volumeCode = $LASTEXITCODE
    if ($null -eq $volumeCode) { $volumeCode = 1 } else { $volumeCode = [int]$volumeCode }
    if ($volumeCode -ne 0) { Fail-Cutover 'REFUSED: docker volume ls failed. The secret was not changed.' }
    if ($names -notcontains 'antiphon_pgdata') {
        Fail-Cutover 'REFUSED: antiphon_pgdata is not listed after the stop. The secret was not changed.'
    }
    $status = (& docker inspect -f '{{.State.Status}}' antiphon-postgres | Out-String).Trim()
    $inspectCode = $LASTEXITCODE
    if ($null -eq $inspectCode) { $inspectCode = 1 } else { $inspectCode = [int]$inspectCode }
    if ($inspectCode -ne 0) { Fail-Cutover 'REFUSED: desktop postgres inspect failed. The secret was not changed.' }
    if ($status -ne 'exited') { Fail-Cutover 'REFUSED: desktop postgres is not exited. The secret was not changed.' }
    Set-CutoverConnectionSecret
    $script:CutoverSecretChanged = $true
    $up = Invoke-CutoverRestart -ExpectedSha $RcTip
    if ($up -ne 0) {
        Write-Host 'STOP GATE 4: restart did not exit 0 after the secret changed. Exit 5 keeps the restart lock. Operator runs phase 6.'
        Fail-Cutover ('REFUSED: restart exited ' + $up + '. Do not start a second copy.')
    }
    Invoke-CutoverMaintenance -Clear
    $script:CutoverEnteredMaintenance = $false
    Write-Host 'PHASE 4 restart exited 0.'

    try {
        $health = Invoke-WebRequest -Uri ($ApiBase.TrimEnd('/') + '/health') -TimeoutSec 15 -UseBasicParsing
        $healthCode = [int]$health.StatusCode
    } catch {
        $healthCode = 0
    }
    if ($healthCode -ne 200) { Fail-Cutover 'PHASE 5 failed: health was not 200. Operator runs phase 6.' 2 }
    $after = Invoke-CutoverGet '/api/version'
    if ([string]$after.version -ne $sha -or @($after.capabilities) -notcontains 'land-v2') {
        Fail-Cutover 'PHASE 5 failed: version SHA or land-v2. Operator runs phase 6.' 2
    }
    $afterState = Invoke-CutoverGet '/api/diagnostics/session-state'
    $afterPool = $afterState.pool
    $afterReset = $afterPool.NoResetOnClose
    if ($null -eq $afterReset) { $afterReset = $afterPool.noResetOnClose }
    if ($afterReset -ne $true) { Fail-Cutover 'PHASE 5 failed: pool flag. Operator runs phase 6.' 2 }
    $activity = Invoke-CutoverPsql -Target target -Sql "SELECT count(*) FROM pg_stat_activity WHERE datname = 'antiphon' AND client_addr IS NOT NULL"
    if ($activity -eq '0' -or [string]::IsNullOrWhiteSpace($activity)) {
        Fail-Cutover 'PHASE 5 failed: server2 shows no desktop client. Operator runs phase 6.' 2
    }
    Write-Host 'PHASE 5 mechanical checks passed.'
    Write-Host 'OPERATOR: confirm one dispatch, one land, Hangfire, and the 30 minute monitor. This script does not create that task.'
    $script:CutoverWindowSucceeded = $true
    exit 0
    } finally {
        Complete-CutoverWindowExit -AllowOldStringRestart
    }
}

if (-not $Execute) {
    Show-CutoverWhatIf
    exit 0
}
if ($WhatIf) {
    Fail-Cutover 'REFUSED: -Execute and -WhatIf together are ambiguous. Nothing was changed.' 2
}
if (-not $ConfirmWindow -or -not (Test-ExactNamedSwitch 'ConfirmWindow')) {
    Fail-Cutover 'REFUSED: -Execute requires -ConfirmWindow. Nothing was changed.' 2
}
if ($Rollback) {
    Invoke-CutoverRollback
}
Invoke-CutoverWindow
