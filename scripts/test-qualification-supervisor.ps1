#requires -Version 5.1
# CARD-1667. Offline witnesses for the rc qualification supervisor set:
# scripts/qualification/{start-supervisor,supervise-rc,observe,launch-rc}.ps1 and
# scripts/lib/qualification-supervisor.ps1. Synthetic task/state roots under TEMP and
# a fake launch script only; no qualification run, lock, or live process is touched.
# ASCII-only.
[CmdletBinding()]
param([string]$Case = 'Smoke', [string]$ResultsDirectory)
$ErrorActionPreference = 'Stop'
$script:passed = 0
$script:failed = 0
function Assert-C1667([bool]$Condition, [string]$Name, [string]$Detail = '') {
    if ($Condition) { $script:passed++; Write-Host "PASS C1667 $Name" }
    else { $script:failed++; Write-Host "FAIL C1667 $Name :: $Detail" }
}
$root = Split-Path -Parent $PSScriptRoot
$qualification = Join-Path $PSScriptRoot 'qualification'
$observeScript = Join-Path $qualification 'observe.ps1'
$startScript = Join-Path $qualification 'start-supervisor.ps1'
$launchScript = Join-Path $qualification 'launch-rc.ps1'
$isWindowsHost = ($PSVersionTable.PSEdition -eq 'Desktop') -or $IsWindows
$pwshPath = [Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('c1667-' + [guid]::NewGuid().ToString('N'))
$owned = New-Object System.Collections.Generic.List[string]
$ownedDirs = New-Object System.Collections.Generic.List[string]

function New-OwnedDirectory([string]$Name) {
    $path = Join-Path $scratch $Name
    [void][IO.Directory]::CreateDirectory($path)
    $script:ownedDirs.Insert(0, $path)
    return $path
}
function Write-OwnedJson([string]$Path, $Value) {
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 6 -Compress), $utf8)
    if (-not $script:owned.Contains($Path)) { $script:owned.Add($Path) }
}
function Get-SelfIdentity {
    $self = [Diagnostics.Process]::GetCurrentProcess()
    return [ordered]@{ pid = $self.Id; processStartedAt = $self.StartTime.ToUniversalTime().ToString('o') }
}
function Get-ExitedIdentity {
    # A real process that has exited: its pid/start pair can no longer match a live process.
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = $pwshPath
    $info.Arguments = '-NoProfile -NonInteractive -Command exit 0'
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $child = [Diagnostics.Process]::Start($info)
    $started = $child.StartTime.ToUniversalTime().ToString('o')
    $child.WaitForExit()
    $identity = [ordered]@{ pid = $child.Id; processStartedAt = $started }
    $child.Dispose()
    return $identity
}
# The supervisor identity carries the launch's bound run id (c1667run unless BoundRunId says otherwise).
function New-ObservedRun([string]$Name, $RunOwner, $Supervisor, $HeartbeatUtc, $State, [string]$LockRunId = 'c1667run',
    [string]$CommissionExit, [string]$BoundRunId = 'c1667run', [string]$LaunchStartedAt, $OwnCopy) {
    $task = New-OwnedDirectory ($Name + '-task')
    if ($LaunchStartedAt) { Write-OwnedJson (Join-Path $task 'launch-child.json') ([ordered]@{ pid = 1; processStartedAt = $LaunchStartedAt; runId = $BoundRunId }) }
    if ($null -ne $OwnCopy) { Write-OwnedJson (Join-Path $task 'own-last-run.json') $OwnCopy }
    $stateRoot = New-OwnedDirectory ($Name + '-state')
    if ($null -ne $RunOwner) { Write-OwnedJson (Join-Path $stateRoot 'run.lock') ([ordered]@{ pid = $RunOwner.pid; processStartedAt = $RunOwner.processStartedAt; runId = $LockRunId }) }
    if ($null -ne $State) { Write-OwnedJson (Join-Path $stateRoot 'last-run.json') $State }
    if ($null -ne $Supervisor) {
        $identity = [ordered]@{ pid = $Supervisor.pid; processStartedAt = $Supervisor.processStartedAt }
        if ($BoundRunId) { $identity.runId = $BoundRunId }
        Write-OwnedJson (Join-Path $task 'supervisor-identity.json') $identity
    }
    if ($null -ne $HeartbeatUtc) { Write-OwnedJson (Join-Path $task 'supervisor-heartbeat.json') ([ordered]@{ observedAtUtc = $HeartbeatUtc; trx = 3; hangSuspected = $false }) }
    if ($CommissionExit) {
        $exitPath = Join-Path $task 'commission-exit.txt'
        [IO.File]::WriteAllText($exitPath, $CommissionExit)
        $script:owned.Add($exitPath)
    }
    return [pscustomobject]@{ Task = $task; State = $stateRoot }
}
function Invoke-Launch([string]$Name, $Record, [string]$BoundRunId) {
    $dir = New-OwnedDirectory ($Name + '-launch')
    $config = Join-Path $dir 'launch-record.json'
    Write-OwnedJson $config $Record
    $previous = $env:ANTIPHON_QUALIFICATION_RUN_ID
    try {
        $env:ANTIPHON_QUALIFICATION_RUN_ID = $BoundRunId
        $text = @(& $pwshPath -NoProfile -NonInteractive -File $launchScript -Config $config 2>&1) -join "`n"
        $code = $LASTEXITCODE
    } finally { $env:ANTIPHON_QUALIFICATION_RUN_ID = $previous }
    return [pscustomobject]@{ Code = $code; Text = $text }
}
function Invoke-Observe($Run) {
    $text = @(& $pwshPath -NoProfile -NonInteractive -File $observeScript -TaskRoot $Run.Task -StateRoot $Run.State 2>&1) -join "`n"
    $code = $LASTEXITCODE
    $parsed = $null
    try { $parsed = ($text.Trim() -split "`n")[-1] | ConvertFrom-Json } catch { }
    return [pscustomobject]@{ Code = $code; Text = $text; Json = $parsed }
}

try {
    [void][IO.Directory]::CreateDirectory($scratch)
    . (Join-Path $PSScriptRoot 'lib/qualification-supervisor.ps1')
    $self = Get-SelfIdentity
    $gone = Get-ExitedIdentity
    $now = [datetime]::UtcNow
    $fresh = $now.ToString('o')
    $stale = $now.AddMinutes(-40).ToString('o')
    $running = [ordered]@{ runId = 'c1667run'; phase = 'tests'; startedAt = $now.AddHours(-9).ToString('o') }

    # The CARD-1667 witness: supervisor and its heartbeat are gone, nightly-run still owns run.lock.
    $run = New-ObservedRun 'lost' $self $gone $stale $running
    $o = Invoke-Observe $run
    Assert-C1667 ($o.Code -eq 0 -and $o.Json.classification -ceq 'supervisor-lost-run-alive' -and $o.Json.runOwner -ceq 'alive' -and $o.Json.supervisor -ceq 'absent') `
        'observe classifies a gone supervisor with live nightly-run as supervisor-lost-run-alive' $o.Text

    $run = New-ObservedRun 'hung' $self $self $stale $running
    $o = Invoke-Observe $run
    Assert-C1667 ($o.Code -eq 0 -and $o.Json.classification -ceq 'supervisor-lost-run-alive' -and $o.Json.reason -ceq 'heartbeat-stale' -and $o.Json.supervisor -ceq 'alive') `
        'observe treats a stale heartbeat as supervisor lost while the run is alive' $o.Text

    $run = New-ObservedRun 'missingbeat' $self $gone $null $running
    $o = Invoke-Observe $run
    Assert-C1667 ($o.Code -eq 0 -and $o.Json.classification -ceq 'supervisor-lost-run-alive' -and $o.Json.heartbeat -ceq 'missing') `
        'observe treats a missing heartbeat as supervisor lost while the run is alive' $o.Text

    $run = New-ObservedRun 'healthy' $self $self $fresh $running
    $o = Invoke-Observe $run
    Assert-C1667 ($o.Code -eq 0 -and $o.Json.classification -ceq 'running' -and $o.Json.reason -ceq 'heartbeat-fresh') `
        'observe reports running for a live supervisor with a fresh heartbeat' $o.Text

    $completed = [ordered]@{ runId = 'c1667run'; phase = 'final'; completedAt = $now.AddMinutes(-2).ToString('o'); exitCode = 1; testsPassed = $false }
    $run = New-ObservedRun 'done' $gone $gone $stale $completed
    $o = Invoke-Observe $run
    Assert-C1667 ($o.Code -eq 0 -and $o.Json.classification -ceq 'completed' -and $o.Json.exitCode -eq 1 -and $null -eq $o.Json.commissionExit -and $o.Json.runOwner -ceq 'absent') `
        'observe treats last-run completedAt as the end signal without commission-exit' $o.Text

    $run = New-ObservedRun 'dead' $gone $gone $stale $running
    $o = Invoke-Observe $run
    Assert-C1667 ($o.Code -eq 3 -and $o.Json.classification -ceq 'run-lost' -and $o.Json.runOwner -ceq 'absent') `
        'observe reports run-lost when nightly-run is gone without completedAt' $o.Text

    $run = New-ObservedRun 'noowner' $null $self $fresh $running
    $o = Invoke-Observe $run
    Assert-C1667 ($o.Code -eq 2 -and $o.Json.classification -ceq 'unknown') `
        'observe reports unknown without a run owner record' $o.Text

    # Completion races the probe: nightly-run writes completedAt before releasing run.lock and
    # exiting, so the owner must be probed before last-run.json is read.
    $run = New-ObservedRun 'race' $self $gone $stale $running
    $statePath = Join-Path $run.State 'last-run.json'
    $probe = {
        param($Record)
        Write-OwnedJson $statePath ([ordered]@{ runId = 'c1667run'; phase = 'final'; completedAt = [datetime]::UtcNow.ToString('o'); exitCode = 0 })
        [pscustomobject]@{ status = 'absent'; reason = 'pid-not-found' }
    }
    $r = Get-QualificationSupervision -TaskRoot $run.Task -StateRoot $run.State -ObserveProcess $probe
    Assert-C1667 ($r.classification -ceq 'completed' -and $r.exitCode -eq 0) `
        'observe probes the run owner before reading last-run.json' ($r | ConvertTo-Json -Compress)

    # D-1 (Review b26811ff): the shared state root keeps the PREVIOUS run's last-run.json, so
    # completed/running/supervisor-lost-run-alive are credited only for the launch's own run id.
    $previousRun = [ordered]@{ runId = 'c1667prev'; phase = 'final'; completedAt = $now.AddHours(-3).ToString('o'); exitCode = 0; testsPassed = $true }
    $run = New-ObservedRun 'refused' $null $gone $stale $previousRun -CommissionExit '2'
    $o = Invoke-Observe $run
    Assert-C1667 ($o.Code -eq 2 -and $o.Json.classification -ceq 'launch-failed' -and $o.Json.reason -ceq 'commission-exit-2' -and
        $o.Json.runId -ceq 'c1667run' -and $o.Json.stateRunId -ceq 'c1667prev' -and $null -eq $o.Json.exitCode -and $null -eq $o.Json.testsPassed) `
        'observe does not credit a previous run completedAt to a refused launch (commission-exit 2)' $o.Text

    $otherRun = [ordered]@{ runId = 'c1667other'; phase = 'tests'; startedAt = $now.AddHours(-1).ToString('o') }
    $run = New-ObservedRun 'foreignlock' $self $gone $stale $otherRun -LockRunId 'c1667other'
    $o = Invoke-Observe $run
    Assert-C1667 ($o.Code -eq 2 -and $o.Json.classification -ceq 'not-started' -and $o.Json.runLock -ceq 'foreign' -and
        $o.Json.lockRunId -ceq 'c1667other' -and $o.Json.runOwner -ceq 'alive') `
        'observe does not report supervisor-lost-run-alive for another run holding run.lock' $o.Text

    $run = New-ObservedRun 'replacedlock' $self $gone $stale $running -LockRunId 'c1667other'
    $o = Invoke-Observe $run
    Assert-C1667 ($o.Code -eq 2 -and $o.Json.classification -ceq 'unknown' -and $o.Json.reason -ceq 'run-lock-foreign' -and $o.Json.stateRunId -ceq 'c1667run') `
        'observe needs the run.lock run id as well as the last-run.json run id before crediting supervisor-lost-run-alive' $o.Text

    $run = New-ObservedRun 'slotwait' $null $self $fresh $previousRun
    $o = Invoke-Observe $run
    Assert-C1667 ($o.Code -eq 2 -and $o.Json.classification -ceq 'not-started' -and $o.Json.reason -ceq 'run-id-not-seen' -and
        $o.Json.runLock -ceq 'missing' -and $null -eq $o.Json.completedAt) `
        'observe reports not-started while the launch waits for a build slot (no Started yet)' $o.Text

    $run = New-ObservedRun 'prestart' $self $self $fresh $previousRun
    $o = Invoke-Observe $run
    Assert-C1667 ($o.Code -eq 2 -and $o.Json.classification -ceq 'not-started' -and $o.Json.reason -ceq 'state-not-written' -and $o.Json.runLock -ceq 'ours') `
        'observe needs the last-run.json run id as well as the run.lock run id before crediting running' $o.Text

    $run = New-ObservedRun 'unbound' $self $self $fresh $running -BoundRunId ''
    $o = Invoke-Observe $run
    Assert-C1667 ($o.Code -eq 2 -and $o.Json.classification -ceq 'unknown' -and $o.Json.reason -ceq 'run-id-unbound') `
        'observe refuses to classify without a bound launch run id' $o.Text

    # D-1 (Review 9f29e134): last-run.json holds only the latest run. Once the next run writes it,
    # our run is credited from the TaskRoot copy supervise-rc kept, or is unknown/state-superseded;
    # never launch-failed or not-started, which would invite relaunching a finished qualification.
    $launchedAt = $now.AddHours(-9).ToString('o')
    $nextRun = [ordered]@{ runId = 'c1667next'; phase = 'tests'; startedAt = $now.AddMinutes(-10).ToString('o') }
    $run = New-ObservedRun 'superseded' $self $gone $stale $nextRun -LockRunId 'c1667next' -CommissionExit '0' -LaunchStartedAt $launchedAt
    $o = Invoke-Observe $run
    Assert-C1667 ($o.Code -eq 2 -and $o.Json.classification -ceq 'unknown' -and $o.Json.reason -ceq 'state-superseded' -and
        $o.Json.stateRunId -ceq 'c1667next' -and $null -eq $o.Json.exitCode -and $null -eq $o.Json.completedAt) `
        'observe reports a completed run superseded by a later run without a copy as unknown, not launch-failed' $o.Text

    $midRun = [ordered]@{ runId = 'c1667run'; phase = 'tests'; startedAt = $launchedAt }
    $run = New-ObservedRun 'lostsuperseded' $self $gone $stale $nextRun -LockRunId 'c1667next' -LaunchStartedAt $launchedAt
    $o = Invoke-Observe $run
    $run2 = New-ObservedRun 'lostsupercopy' $null $gone $stale $nextRun -LaunchStartedAt $launchedAt -OwnCopy $midRun
    $o2 = Invoke-Observe $run2
    Assert-C1667 ($o.Code -eq 2 -and $o.Json.classification -ceq 'unknown' -and $o.Json.reason -ceq 'state-superseded' -and
        $o2.Code -eq 2 -and $o2.Json.classification -ceq 'unknown' -and $o2.Json.reason -ceq 'state-superseded' -and $o2.Json.stateCopy -ceq 'ours') `
        'observe reports a lost supervisor whose run was superseded as unknown, not not-started' ($o.Text + ' | ' + $o2.Text)

    $ownDone = [ordered]@{ runId = 'c1667run'; phase = 'final'; startedAt = $launchedAt; completedAt = $now.AddMinutes(-30).ToString('o'); exitCode = 1; testsPassed = $false }
    $run = New-ObservedRun 'copydone' $self $gone $stale $nextRun -LockRunId 'c1667next' -CommissionExit '0' -LaunchStartedAt $launchedAt -OwnCopy $ownDone
    $o = Invoke-Observe $run
    Assert-C1667 ($o.Code -eq 0 -and $o.Json.classification -ceq 'completed' -and $o.Json.reason -ceq 'state-copy-completedAt' -and
        $o.Json.exitCode -eq 1 -and $o.Json.testsPassed -eq $false -and $o.Json.stateRunId -ceq 'c1667next') `
        'observe credits completed from the TaskRoot copy of our last-run.json after it is superseded' $o.Text

    # A refused run writes deferred-busy to last-run.json while ours still holds run.lock.
    $busyRun = [ordered]@{ runId = 'c1667busy'; phase = 'deferred-busy'; startedAt = $now.AddMinutes(-5).ToString('o'); exitCode = 3 }
    $run = New-ObservedRun 'busyover' $self $self $fresh $busyRun -LaunchStartedAt $launchedAt -OwnCopy $midRun
    $o = Invoke-Observe $run
    Assert-C1667 ($o.Code -eq 0 -and $o.Json.classification -ceq 'running' -and $o.Json.stateRunId -ceq 'c1667busy' -and $o.Json.phase -ceq 'tests') `
        'observe credits running from run.lock and the copy when another run overwrote last-run.json' $o.Text

    # A previous run that started before the launch child is not a supersession: still launch-failed.
    $earlierRun = [ordered]@{ runId = 'c1667prev'; phase = 'final'; startedAt = $now.AddHours(-12).ToString('o'); completedAt = $now.AddHours(-10).ToString('o'); exitCode = 0 }
    $run = New-ObservedRun 'refusedlaunched' $null $gone $stale $earlierRun -CommissionExit '2' -LaunchStartedAt $launchedAt
    $o = Invoke-Observe $run
    Assert-C1667 ($o.Code -eq 2 -and $o.Json.classification -ceq 'launch-failed' -and $o.Json.reason -ceq 'commission-exit-2') `
        'observe keeps launch-failed when last-run.json names a run that started before the launch child' $o.Text

    # A-2 (Review 230d1f5d): a copy left in the TaskRoot by an earlier launch names another run id.
    # It is neither credited (completion, state or result fields) nor counted as a supersession.
    $foreignDone = [ordered]@{ runId = 'c1667prev'; phase = 'final'; startedAt = $now.AddHours(-12).ToString('o'); completedAt = $now.AddHours(-10).ToString('o'); exitCode = 0; testsPassed = $true }
    $run = New-ObservedRun 'foreigncopy' $null $gone $stale $earlierRun -CommissionExit '2' -LaunchStartedAt $launchedAt -OwnCopy $foreignDone
    $o = Invoke-Observe $run
    $run2 = New-ObservedRun 'foreigncopywait' $null $self $fresh $null -LaunchStartedAt $launchedAt -OwnCopy $foreignDone
    $o2 = Invoke-Observe $run2
    $run3 = New-ObservedRun 'foreigncopylock' $self $self $fresh $null -LaunchStartedAt $launchedAt -OwnCopy $foreignDone
    $o3 = Invoke-Observe $run3
    Assert-C1667 ($o.Code -eq 2 -and $o.Json.classification -ceq 'launch-failed' -and $o.Json.reason -ceq 'commission-exit-2' -and $o.Json.stateCopy -ceq 'foreign' -and
        $null -eq $o.Json.completedAt -and $null -eq $o.Json.exitCode -and
        $o2.Code -eq 2 -and $o2.Json.classification -ceq 'not-started' -and $o2.Json.reason -ceq 'run-id-not-seen' -and $o2.Json.stateCopy -ceq 'foreign' -and
        $o3.Code -eq 2 -and $o3.Json.classification -ceq 'not-started' -and $o3.Json.reason -ceq 'state-not-written' -and $o3.Json.stateCopy -ceq 'foreign' -and $null -eq $o3.Json.phase) `
        'observe credits neither completion nor supersession to a TaskRoot copy naming another run id' ($o.Text + ' | ' + $o2.Text + ' | ' + $o3.Text)

    # supervise-rc keeps the copy: while polling (and does not overwrite it with the next run), and
    # once more at child exit (a poll that never ran).
    $keepState = New-OwnedDirectory 'keep-state'
    $keepLogs = New-OwnedDirectory 'keep-logs'
    $keepPoll = New-OwnedDirectory 'keep-poll-task'
    $keepExit = New-OwnedDirectory 'keep-exit-task'
    $keepStatePath = Join-Path $keepState 'last-run.json'
    $owned.Add($keepStatePath)
    $doneJson = '{"runId":"c1667keep","phase":"final","startedAt":"' + $launchedAt + '","completedAt":"' + $now.AddMinutes(-1).ToString('o') + '","exitCode":0,"testsPassed":true}'
    $nextJson = '{"runId":"c1667after","phase":"Started","startedAt":"' + $now.ToString('o') + '"}'
    $pollFake = Join-Path $keepPoll 'fake-launch.ps1'
    [IO.File]::WriteAllText($pollFake, "[IO.File]::WriteAllText('$keepStatePath', '$doneJson')`r`nStart-Sleep -Seconds 4`r`n[IO.File]::WriteAllText('$keepStatePath', '$nextJson')`r`nexit 0`r`n")
    $exitFake = Join-Path $keepExit 'fake-launch.ps1'
    [IO.File]::WriteAllText($exitFake, "[IO.File]::WriteAllText('$keepStatePath', '$doneJson')`r`nexit 0`r`n")
    foreach ($dir in @($keepPoll, $keepExit)) {
        foreach ($name in @('fake-launch.ps1', 'supervisor-identity.json', 'supervisor-identity.json.tmp', 'supervisor.log', 'supervisor-heartbeat.json',
            'supervisor-heartbeat.json.tmp', 'commission-console.log', 'commission-stderr.log', 'commission-exit.txt', 'commission-exit.txt.tmp',
            'launch-child.json', 'launch-child.json.tmp', 'hang-suspected.txt', 'own-last-run.json', 'own-last-run.json.tmp')) { $owned.Add((Join-Path $dir $name)) }
    }
    $superviseScript = Join-Path $qualification 'supervise-rc.ps1'
    $pollText = @(& $pwshPath -NoProfile -NonInteractive -File $superviseScript -TaskRoot $keepPoll -LaunchScript $pollFake -LogRoot $keepLogs `
        -StateRoot $keepState -RunId 'c1667keep' -PollSeconds 1 2>&1) -join "`n"
    $pollObserved = Invoke-Observe ([pscustomobject]@{ Task = $keepPoll; State = $keepState })
    $exitText = @(& $pwshPath -NoProfile -NonInteractive -File $superviseScript -TaskRoot $keepExit -LaunchScript $exitFake -LogRoot $keepLogs `
        -StateRoot $keepState -RunId 'c1667keep' -PollSeconds 300 2>&1) -join "`n"
    [IO.File]::WriteAllText($keepStatePath, $nextJson)
    $exitObserved = Invoke-Observe ([pscustomobject]@{ Task = $keepExit; State = $keepState })
    $pollCopy = $null
    try { $pollCopy = Get-Content -LiteralPath (Join-Path $keepPoll 'own-last-run.json') -Raw | ConvertFrom-Json } catch { }
    Assert-C1667 ($pollCopy -and $pollCopy.runId -ceq 'c1667keep' -and $null -ne $pollCopy.completedAt -and
        $pollObserved.Json.classification -ceq 'completed' -and $pollObserved.Json.stateRunId -ceq 'c1667after' -and $pollObserved.Json.testsPassed -eq $true) `
        'supervise-rc copies last-run.json for its run id while polling and keeps it after the next run writes' ($pollText + ' | ' + $pollObserved.Text)
    Assert-C1667 ($exitObserved.Code -eq 0 -and $exitObserved.Json.classification -ceq 'completed' -and $exitObserved.Json.reason -ceq 'state-copy-completedAt' -and $exitObserved.Json.stateCopy -ceq 'ours' -and
        (Test-Path -LiteralPath (Join-Path $keepExit 'own-last-run.json'))) `
        'supervise-rc copies last-run.json for its run id at child exit' ($exitText + ' | ' + $exitObserved.Text)

    # start-supervisor binds a fresh run id: one the state root already names is refused.
    $reuseTask = New-OwnedDirectory 'reuse-task'
    $reuseState = New-OwnedDirectory 'reuse-state'
    Write-OwnedJson (Join-Path $reuseState 'last-run.json') ([ordered]@{ runId = 'c1667prev'; phase = 'final'; completedAt = $stale })
    $reuseText = @(& $pwshPath -NoProfile -NonInteractive -File $startScript -TaskRoot $reuseTask -LaunchScript $observeScript -LogRoot $reuseTask `
        -StateRoot $reuseState -RunId 'c1667prev' 2>&1) -join "`n"
    $reuseCode = $LASTEXITCODE
    Assert-C1667 ($reuseCode -eq 1 -and $reuseText -match 'already names run id c1667prev' -and -not (Test-Path -LiteralPath (Join-Path $reuseTask 'supervisor-identity.json'))) `
        'start-supervisor refuses a run id the state root already records' "code=$reuseCode $reuseText"

    # A-2: a run id the TaskRoot copy (own-last-run.json) already names is refused too.
    $copyTask = New-OwnedDirectory 'reusecopy-task'
    $copyState = New-OwnedDirectory 'reusecopy-state'
    $copyFake = Join-Path $copyTask 'fake-launch.ps1'
    [IO.File]::WriteAllText($copyFake, "exit 0`r`n")
    foreach ($name in @('fake-launch.ps1', 'supervisor-identity.json', 'supervisor-identity.json.tmp', 'supervisor.log', 'supervisor-heartbeat.json',
        'supervisor-heartbeat.json.tmp', 'commission-console.log', 'commission-stderr.log', 'commission-exit.txt', 'commission-exit.txt.tmp',
        'launch-child.json', 'launch-child.json.tmp', 'hang-suspected.txt', 'own-last-run.json.tmp', 'start-supervisor.json', 'start-supervisor.json.tmp',
        'supervisor-stdout.log', 'supervisor-stderr.log')) { $owned.Add((Join-Path $copyTask $name)) }
    Write-OwnedJson (Join-Path $copyTask 'own-last-run.json') ([ordered]@{ runId = 'c1667copy'; phase = 'final'; completedAt = $stale })
    $copyText = @(& $pwshPath -NoProfile -NonInteractive -File $startScript -TaskRoot $copyTask -LaunchScript $copyFake -LogRoot $copyTask `
        -StateRoot $copyState -RunId 'c1667copy' -IdentityTimeoutSeconds 20 2>&1) -join "`n"
    $copyCode = $LASTEXITCODE
    Assert-C1667 ($copyCode -eq 1 -and $copyText -match 'already names run id c1667copy' -and -not (Test-Path -LiteralPath (Join-Path $copyTask 'supervisor-identity.json'))) `
        'start-supervisor refuses a run id the TaskRoot copy own-last-run.json already records' "code=$copyCode $copyText"

    # A-1 (Review 230d1f5d): nightly-run writes last-run.json with File.Replace, which Windows refuses
    # while a reader's handle lacks FileShare.Delete; a failed state write exits nightly-run 1.
    # Portable part: every read open in the supervisor set carries FileShare.Delete, and both
    # last-run.json readers go through Open-RcQualificationSharedRead. Windows only: a File.Replace
    # of the record succeeds while each reader holds it open (Linux does not enforce share modes).
    $shareProblems = New-Object System.Collections.Generic.List[string]
    $readOpens = 0
    $shareFiles = @((Join-Path $PSScriptRoot 'lib/rc-qualification-status.ps1'), (Join-Path $PSScriptRoot 'lib/qualification-supervisor.ps1'),
        $observeScript, $startScript, (Join-Path $qualification 'supervise-rc.ps1'), $launchScript)
    $readers = @{}
    foreach ($path in $shareFiles) {
        $tokens = $null; $errors = $null
        $ast = [Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors)
        $opens = @($ast.FindAll({ param($node)
            ($node -is [Management.Automation.Language.InvokeMemberExpressionAst] -and $node.Member.Extent.Text -match '^(Open|new)$' -and
                $node.Expression.Extent.Text -match 'File(Stream)?\]$') -or
            ($node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -ieq 'New-Object' -and $node.Extent.Text -match 'FileStream') }, $true))
        foreach ($open in $opens) {
            if ($open.Extent.Text -notmatch 'FileAccess\]::Read\b') { continue }
            $readOpens++
            if ($open.Extent.Text -notmatch 'FileShare\]::Delete') { $shareProblems.Add(('{0}:{1} {2}' -f (Split-Path -Leaf $path), $open.Extent.StartLineNumber, $open.Extent.Text)) }
        }
        foreach ($fn in @($ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] }, $true))) {
            $calls = @($fn.Body.FindAll({ param($node) $node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -ceq 'Open-RcQualificationSharedRead' }, $true))
            $readers[$fn.Name] = $calls.Count
        }
    }
    foreach ($name in @('Read-RcQualificationRecord', 'Save-QualificationOwnRunState')) {
        if (-not $readers.ContainsKey($name) -or $readers[$name] -lt 1) { $shareProblems.Add("$name does not open through Open-RcQualificationSharedRead") }
    }
    if (-not $readers.ContainsKey('Open-RcQualificationSharedRead') -or $readOpens -lt 1) { $shareProblems.Add("no shared read opener (read opens=$readOpens)") }
    # The Replace runs on every host: Linux never blocks a rename under an open reader, so only Windows
    # proves the FileShare.Delete sharing, but every host proves the call itself (group-128: a $null
    # backup reached File.Replace as an empty path, which throws on Linux too).
    $shareDir = New-OwnedDirectory 'share'
    $sharePath = Join-Path $shareDir 'last-run.json'
    $shareTemp = Join-Path $shareDir 'last-run.json.next'
    $owned.Add($shareTemp)
    Write-OwnedJson $sharePath ([ordered]@{ runId = 'c1667share' })
    $script:shareReplace = 'not-run'
    $replaceUnder = { param($p) try { [IO.File]::WriteAllText($shareTemp, '{"runId":"c1667next"}'); [IO.File]::Replace($shareTemp, $p, [NullString]::Value); $script:shareReplace = 'replaced' } catch { $script:shareReplace = $_.Exception.Message } }
    [void](Read-RcQualificationRecord -Path $sharePath -AfterRead $replaceUnder)
    if ($script:shareReplace -cne 'replaced') { $shareProblems.Add("Read-RcQualificationRecord blocked File.Replace: $($script:shareReplace)") }
    $script:shareReplace = 'not-run'
    try {
        $held = Open-RcQualificationSharedRead $sharePath
        try { & $replaceUnder $sharePath } finally { $held.Dispose() }
    } catch { $script:shareReplace = $_.Exception.Message }
    if ($script:shareReplace -cne 'replaced') { $shareProblems.Add("Open-RcQualificationSharedRead blocked File.Replace: $($script:shareReplace)") }
    if (-not $isWindowsHost) { Write-Host "NOTE C1667 FileShare.Delete sharing is Windows-only; the source pin and the File.Replace call ran" }
    Assert-C1667 ($shareProblems.Count -eq 0) 'last-run.json readers open with FileShare.Delete so the nightly-run File.Replace is never blocked' ($shareProblems -join '; ')

    # launch-rc refuses before git or build-slot when its run id is not the supervisor's bound id.
    $sha = '0123456789abcdef0123456789abcdef01234567'
    $l = Invoke-Launch 'norunid' ([ordered]@{ checkoutRoot = $scratch; expectedSha = $sha; nightlyArguments = @('-Profile', 'rc') }) 'c1667run'
    $m = Invoke-Launch 'otherrunid' ([ordered]@{ checkoutRoot = $scratch; expectedSha = $sha; nightlyArguments = @('-RunId', 'c1667other') }) 'c1667run'
    Assert-C1667 ($l.Code -eq 2 -and $l.Text -match 'requires -RunId' -and $m.Code -eq 2 -and $m.Text -match 'is not the supervisor run id c1667run') `
        'launch-rc refuses a launch whose -RunId is missing or differs from the bound run id' ($l.Text + ' | ' + $m.Text)

    # A-3: a live run.lock refuses the launch instead of queueing behind it.
    $lockState = New-OwnedDirectory 'livelock-state'
    Write-OwnedJson (Join-Path $lockState 'run.lock') ([ordered]@{ pid = $self.pid; processStartedAt = $self.processStartedAt; runId = 'c1667other' })
    $l = Invoke-Launch 'livelock' ([ordered]@{ checkoutRoot = $scratch; expectedSha = $sha; nightlyArguments = @('-RunId', 'c1667run', '-StateRoot', $lockState) }) 'c1667run'
    $gonelock = New-OwnedDirectory 'stalelock-state'
    Write-OwnedJson (Join-Path $gonelock 'run.lock') ([ordered]@{ pid = $gone.pid; processStartedAt = $gone.processStartedAt; runId = 'c1667other' })
    $g = Invoke-Launch 'stalelock' ([ordered]@{ checkoutRoot = $scratch; expectedSha = $sha; nightlyArguments = @('-RunId:c1667run', ('-StateRoot:' + $gonelock)) }) 'c1667run'
    Assert-C1667 ($l.Code -eq 3 -and $l.Text -match 'held by live pid' -and $l.Text -match 'c1667other' -and $g.Code -eq 2 -and $g.Text -match 'checkout HEAD') `
        'launch-rc refuses a live run.lock and leaves a dead owner to nightly-run' ($l.Text + ' | ' + $g.Text)

    # Run ae84478d g126: a reused checkout moved to the expected SHA kept a CRLF working copy of an
    # eol=lf script (git checkout never rewrites a file whose blob is unchanged). The launch must refuse
    # that tree before build-slot, and must launch once the working file honours its attribute.
    $eolRepo = New-OwnedDirectory 'eol-checkout'
    $lfUtf8 = New-Object System.Text.UTF8Encoding $false
    [void][IO.Directory]::CreateDirectory((Join-Path $eolRepo 'scripts'))
    [IO.File]::WriteAllText((Join-Path $eolRepo '.gitattributes'), "*.ps1 text eol=lf`n", $lfUtf8)
    [IO.File]::WriteAllText((Join-Path $eolRepo 'scripts/selector.ps1'), "param()`nWrite-Output 'selector'`n", $lfUtf8)
    [IO.File]::WriteAllText((Join-Path $eolRepo 'scripts/build-slot.ps1'), "Write-Output 'C1667 fake build-slot'`nexit 7`n", $lfUtf8)
    $eolGit = @('-C', $eolRepo, '-c', 'core.autocrlf=false', '-c', 'user.name=c1667', '-c', 'user.email=c1667@example.invalid')
    [void](& git @eolGit init -q 2>&1)
    [void](& git @eolGit add -A 2>&1)
    [void](& git @eolGit commit -q -m 'eol fixture' 2>&1)
    $eolSha = ('' + (& git -C $eolRepo rev-parse HEAD)).Trim()
    $eolState = New-OwnedDirectory 'eol-state'
    $eolRecord = [ordered]@{ checkoutRoot = $eolRepo; expectedSha = $eolSha; nightlyArguments = @('-RunId', 'c1667run', '-StateRoot', $eolState) }
    [IO.File]::WriteAllText((Join-Path $eolRepo 'scripts/selector.ps1'), "param()`r`nWrite-Output 'selector'`r`n", $lfUtf8)
    $crlf = Invoke-Launch 'eolcrlf' $eolRecord 'c1667run'
    [IO.File]::WriteAllText((Join-Path $eolRepo 'scripts/selector.ps1'), "param()`nWrite-Output 'selector'`n", $lfUtf8)
    $lf = Invoke-Launch 'eollf' $eolRecord 'c1667run'
    Assert-C1667 ($eolSha -match '^[0-9a-f]{40}$' -and $crlf.Code -eq 2 -and $crlf.Text -match 'line endings' -and $crlf.Text -match 'scripts/selector\.ps1' -and
        $crlf.Text -notmatch 'fake build-slot' -and $lf.Code -eq 7 -and $lf.Text -match 'C1667 fake build-slot') `
        'launch-rc refuses a checkout whose working files break their eol attribute before build-slot' ($crlf.Text + ' | ' + $lf.Text)

    # Line-flushed log: every line is on disk when the call returns, and no handle stays open.
    $logDir = New-OwnedDirectory 'log'
    $log = Join-Path $logDir 'supervisor.log'
    $owned.Add($log)
    Write-QualificationSupervisorLine -Path $log -Text 'PROGRESS one'
    Write-QualificationSupervisorLine -Path $log -Text 'PROGRESS two'
    $bytes = [IO.File]::ReadAllBytes($log)
    $text = [Text.Encoding]::UTF8.GetString($bytes)
    $exclusive = $false
    try { $h = [IO.File]::Open($log, 'Open', 'ReadWrite', 'None'); $h.Dispose(); $exclusive = $true } catch { }
    Assert-C1667 ($text -match '^\S+Z PROGRESS one\r?\n\S+Z PROGRESS two\r?\n$' -and $exclusive) `
        'supervisor log lines are on disk at return and the file is not held open' $text

    $win = New-QualificationDetachedStartParameters -FilePath 'C:\pwsh\pwsh.exe' -ArgumentList @('-File', 'C:\a b\supervise-rc.ps1') `
        -Platform Windows -StandardOutput 'C:\t\out.log' -StandardError 'C:\t\err.log'
    Assert-C1667 ($win.WindowStyle -ceq 'Hidden' -and -not $win.ContainsKey('NoNewWindow') -and $win.FilePath -ceq 'C:\pwsh\pwsh.exe' -and
        $win.ArgumentList -ceq '-File "C:\a b\supervise-rc.ps1"' -and $win.RedirectStandardOutput -ceq 'C:\t\out.log' -and $win.PassThru) `
        'Windows detached start gets its own hidden console and file handles, never NoNewWindow' ($win | ConvertTo-Json -Compress)
    $unix = New-QualificationDetachedStartParameters -FilePath '/usr/bin/pwsh' -ArgumentList @('-File', '/t/supervise-rc.ps1') `
        -Platform Unix -StandardOutput '/t/out.log' -StandardError '/t/err.log'
    Assert-C1667 ($unix.FilePath -ceq 'setsid' -and $unix.ArgumentList -ceq '/usr/bin/pwsh -File /t/supervise-rc.ps1' -and -not $unix.ContainsKey('WindowStyle') -and
        $unix.RedirectStandardError -ceq '/t/err.log') `
        'Unix detached start uses setsid and file redirects' ($unix | ConvertTo-Json -Compress)

    # End to end: wrapper -> detached supervisor -> fake launch-rc (exit 7). The wrapper runs as the
    # leader of its own process group; after it returns that group is killed, which is the Unix
    # analogue of the shared console teardown that killed the 7f8e9fd1 supervisor.
    $e2eTask = New-OwnedDirectory 'e2e-task'
    $e2eLogs = New-OwnedDirectory 'e2e-logs'
    $e2eState = New-OwnedDirectory 'e2e-state'
    $fake = Join-Path $e2eTask 'fake-launch.ps1'
    $owned.Add($fake)
    [IO.File]::WriteAllText($fake, "Start-Sleep -Seconds 6`r`n[IO.File]::WriteAllText((Join-Path '$e2eLogs' 'a.trx'), 'x')`r`nWrite-Output ('bound run=' + `$env:ANTIPHON_QUALIFICATION_RUN_ID)`r`nWrite-Output 'fake launch done'`r`nexit 7`r`n")
    foreach ($name in @('supervisor-identity.json', 'start-supervisor.json', 'supervisor.log', 'supervisor-stdout.log', 'supervisor-stderr.log',
        'supervisor-heartbeat.json', 'supervisor-heartbeat.json.tmp', 'commission-console.log', 'commission-stderr.log', 'commission-exit.txt',
        'commission-exit.txt.tmp', 'launch-child.json', 'hang-suspected.txt', 'wrapper-out.log', 'wrapper-err.log')) { $owned.Add((Join-Path $e2eTask $name)) }
    $owned.Add((Join-Path $e2eLogs 'a.trx'))
    $wrapperArgs = @('-NoProfile', '-NonInteractive', '-File', $startScript, '-TaskRoot', $e2eTask, '-LaunchScript', $fake,
        '-LogRoot', $e2eLogs, '-StateRoot', $e2eState, '-RunId', 'c1667e2e', '-PollSeconds', '1', '-ProgressMinutes', '1')
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    if ($isWindowsHost) { $info.FileName = $pwshPath; $info.Arguments = (ConvertTo-QualificationCommandLine $wrapperArgs) }
    else { $info.FileName = 'setsid'; $info.Arguments = (ConvertTo-QualificationCommandLine (@($pwshPath) + $wrapperArgs)) }
    $wrapper = [Diagnostics.Process]::Start($info)
    $wrapperOut = $wrapper.StandardOutput.ReadToEndAsync()
    $wrapperErr = $wrapper.StandardError.ReadToEndAsync()
    $wrapperExited = $wrapper.WaitForExit(60000)
    $wrapperPid = $wrapper.Id
    $wrapperCode = -1
    if ($wrapperExited) { $wrapperCode = $wrapper.ExitCode }
    $wrapperText = $wrapperOut.GetAwaiter().GetResult() + $wrapperErr.GetAwaiter().GetResult()
    $identity = $null
    try { $identity = Get-Content -LiteralPath (Join-Path $e2eTask 'supervisor-identity.json') -Raw | ConvertFrom-Json } catch { }
    $teardown = 'not-attempted'
    if ($wrapperExited -and -not $isWindowsHost) {
        & kill -TERM -- ('-' + $wrapperPid) 2>$null
        $teardown = 'group-' + $wrapperPid + '-killed'
    }
    $exitFile = Join-Path $e2eTask 'commission-exit.txt'
    $deadline = [datetime]::UtcNow.AddSeconds(60)
    while (-not (Test-Path -LiteralPath $exitFile) -and [datetime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 250 }
    $supervisorGone = $false
    if ($identity) {
        while ([datetime]::UtcNow -lt $deadline) {
            $p = Get-Process -Id ([int]$identity.pid) -ErrorAction SilentlyContinue
            if (-not $p) { $supervisorGone = $true; break }
            $p.Dispose(); Start-Sleep -Milliseconds 250
        }
    }
    $exitText = $null
    if (Test-Path -LiteralPath $exitFile) { $exitText = (Get-Content -LiteralPath $exitFile -Raw).Trim() }
    $logText = ''
    if (Test-Path -LiteralPath (Join-Path $e2eTask 'supervisor.log')) { $logText = [string](Get-Content -LiteralPath (Join-Path $e2eTask 'supervisor.log') -Raw) }
    $detail = "wrapperCode=$wrapperCode teardown=$teardown exit=$exitText gone=$supervisorGone wrapper=[$wrapperText] log=[$logText]"
    Assert-C1667 ($wrapperExited -and $wrapperCode -eq 0 -and $identity -and [int]$identity.pid -ne $wrapperPid) `
        'start-supervisor returns after the supervisor records its own identity' $detail
    $detached = $false
    if ($identity) {
        if ($isWindowsHost) { $detached = ($identity.detach -ceq 'new-console') -and -not (@($identity.consoleProcessIds) -contains $wrapperPid) }
        else { $detached = ($identity.detach -ceq 'setsid') -and [int]$identity.sessionId -eq [int]$identity.pid -and [int]$identity.sessionId -ne $wrapperPid }
    }
    Assert-C1667 $detached 'supervisor runs in its own session or console, not the wrapper''s' (($identity | ConvertTo-Json -Compress) + ' ' + $detail)
    Assert-C1667 ($exitText -ceq '7' -and $supervisorGone) 'supervisor survives teardown of the starting group and records the launch exit code' $detail
    $beat = $null
    try { $beat = Get-Content -LiteralPath (Join-Path $e2eTask 'supervisor-heartbeat.json') -Raw | ConvertFrom-Json } catch { }
    Assert-C1667 ($logText -match '(?m)^\S+Z START supervisor pid=' -and $logText -match '(?m)^\S+Z LAUNCHED pid=' -and $logText -match '(?m)^\S+Z EXIT 7\r?$' -and
        $logText.EndsWith("`n") -and $beat -and $null -ne $beat.observedAtUtc) `
        'supervisor writes START, LAUNCHED and EXIT lines and a heartbeat' $detail
    $console = ''
    if (Test-Path -LiteralPath (Join-Path $e2eTask 'commission-console.log')) { $console = [string](Get-Content -LiteralPath (Join-Path $e2eTask 'commission-console.log') -Raw) }
    Assert-C1667 ($console.Contains('fake launch done')) 'launch child output goes to its own commission-console.log' $console
    $o = Invoke-Observe ([pscustomobject]@{ Task = $e2eTask; State = $e2eState })
    Assert-C1667 ($o.Code -eq 2 -and $o.Json.supervisor -ceq 'absent' -and $o.Json.commissionExit -ceq '7') `
        'observe reads the real supervisor identity and commission-exit of the finished run' $o.Text
    $child = $null
    try { $child = Get-Content -LiteralPath (Join-Path $e2eTask 'launch-child.json') -Raw | ConvertFrom-Json } catch { }
    Assert-C1667 ($identity -and $identity.runId -ceq 'c1667e2e' -and $child -and $child.runId -ceq 'c1667e2e' -and $console.Contains('bound run=c1667e2e') -and
        $o.Json.classification -ceq 'launch-failed' -and $o.Json.runId -ceq 'c1667e2e') `
        'supervisor binds the launch run id in its identity, launch-child record and the launch environment' ($o.Text + ' ' + $console)

    $valid = $true
    $problems = @()
    foreach ($path in @((Join-Path $PSScriptRoot 'lib/qualification-supervisor.ps1'), $observeScript, $startScript,
        (Join-Path $qualification 'supervise-rc.ps1'), (Join-Path $qualification 'launch-rc.ps1'), $PSCommandPath)) {
        $tokens = $null; $errors = $null
        $ast = [Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors)
        if (@($errors).Count -gt 0) { $valid = $false; $problems += "parse $path" }
        if (@([IO.File]::ReadAllBytes($path) | Where-Object { $_ -gt 127 }).Count -gt 0) { $valid = $false; $problems += "non-ascii $path" }
        if ($ast.ParamBlock) {
            foreach ($parameter in @($ast.ParamBlock.Parameters)) {
                if ($parameter.DefaultValue -and $parameter.DefaultValue.Extent.Text -match 'PSScriptRoot') { $valid = $false; $problems += "PSScriptRoot default $path" }
            }
        }
    }
    Assert-C1667 $valid 'supervisor scripts parse, are ASCII and keep PSScriptRoot out of param defaults' ($problems -join '; ')
} catch {
    $script:failed++
    Write-Host "FAIL C1667 harness error: $($_.Exception.Message) $($_.ScriptStackTrace)"
} finally {
    # Exact owned paths only; never recursive removal.
    foreach ($path in $owned) { if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force } }
    foreach ($dir in $ownedDirs) {
        foreach ($leaf in @('run.lock', 'last-run.json', 'supervisor-identity.json', 'supervisor-heartbeat.json', 'launch-child.json', 'own-last-run.json')) {
            $p = Join-Path $dir $leaf
            if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Force }
        }
        if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir }
    }
    if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch }
}
Write-Host "C487: $($script:passed) passed, $($script:failed) failed, $($script:passed + $script:failed) rows"
if ($script:failed) { Write-Host 'C487 HARNESS EXIT CODE: 1'; exit 1 }
Write-Host 'C487 HARNESS EXIT CODE: 0'
exit 0
