#requires -Version 5.1
# CARD-1507. Offline witnesses for scripts/qualification/stop-supervisor.ps1 and
# scripts/qualification/qualification-stop.ps1. Synthetic task and coordination roots
# under TEMP only. The Windows taskkill tree stop is not executed here.
# ASCII-only.
[CmdletBinding()]
param([string]$Case = 'Smoke')
$ErrorActionPreference = 'Stop'
$script:passed = 0
$script:failed = 0
function Assert-C1507([bool]$Condition, [string]$Name, [string]$Detail = '') {
    if ($Condition) { $script:passed++; Write-Host "PASS C1507 $Name" }
    else { $script:failed++; Write-Host "FAIL C1507 $Name :: $Detail" }
}
$lib = Join-Path $PSScriptRoot 'qualification/qualification-stop.ps1'
$script:stopScript = Join-Path $PSScriptRoot 'qualification/stop-supervisor.ps1'
if (-not [IO.File]::Exists($lib) -or -not [IO.File]::Exists($script:stopScript)) {
    Write-Host 'FAIL C1507 stop library absent'
    exit 1
}
. $lib
$script:pwsh = [Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
$script:scratch = Join-Path ([IO.Path]::GetTempPath()) ('c1507-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($script:scratch)

function Write-C1507Json([string]$Path, $Value) {
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Compress), $utf8)
}
function New-C1507Exited {
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = $script:pwsh
    $info.Arguments = '-NoProfile -NonInteractive -Command "exit 0"'
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $child = [Diagnostics.Process]::Start($info)
    $started = $child.StartTime.ToUniversalTime().ToString('o')
    $child.WaitForExit()
    $record = [ordered]@{ pid = $child.Id; processStartedAt = $started }
    $child.Dispose()
    return $record
}
function New-C1507Self {
    $self = [Diagnostics.Process]::GetCurrentProcess()
    return [ordered]@{ pid = $self.Id; processStartedAt = $self.StartTime.ToUniversalTime().ToString('o') }
}
function New-C1507Fixture($Supervisor, $Driver, $Lock, [string]$RunId = 'c1507run') {
    $name = [guid]::NewGuid().ToString('N')
    $task = Join-Path $script:scratch $name
    $coord = Join-Path $script:scratch ($name + '-coord')
    [void][IO.Directory]::CreateDirectory($task)
    [void][IO.Directory]::CreateDirectory($coord)
    $sentinel = '{"runId":"c1507run","phase":"tests"}'
    $utf8 = New-Object System.Text.UTF8Encoding $false
    if ($null -ne $Supervisor) {
        $Supervisor.runId = $RunId
        Write-C1507Json (Join-Path $task 'supervisor-identity.json') $Supervisor
    }
    if ($null -ne $Driver) {
        $Driver.runId = $RunId
        Write-C1507Json (Join-Path $task 'launch-child.json') $Driver
    }
    if ($null -ne $Lock) { Write-C1507Json (Join-Path $coord 'native-run.lock') $Lock }
    [IO.File]::WriteAllText((Join-Path $task 'last-run.json'), $sentinel, $utf8)
    [IO.File]::WriteAllText((Join-Path $coord 'last-run.json'), $sentinel, $utf8)
    return [pscustomobject]@{ Task = $task; Coord = $coord; RunId = $RunId; Sentinel = $sentinel }
}
function Get-C1507Hash([string]$Path) {
    if (-not [IO.File]::Exists($Path)) { return 'missing' }
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        try { return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '') }
        finally { $stream.Dispose() }
    } finally { $sha.Dispose() }
}
function Invoke-C1507($Fixture, [scriptblock]$Observe, [scriptblock]$StopTree, [string]$Reason = 'operator replacement') {
    $params = @{
        TaskRoot = $Fixture.Task
        CoordinationRoot = $Fixture.Coord
        RunId = $Fixture.RunId
        Reason = $Reason
        Platform = 'Windows'
        CensusMilliseconds = 0
    }
    if ($Observe) { $params.Observe = $Observe }
    if ($StopTree) { $params.StopTree = $StopTree }
    return Invoke-QualificationRecordedStop @params
}
function New-C1507World {
    return @{ map = @{}; calls = (New-Object System.Collections.Generic.List[object]) }
}
function New-C1507Observe($World) {
    return {
        param($Record)
        $key = [string][int]$Record.pid
        $status = $World.map[$key]
        if (-not $status) { $status = 'absent' }
        return [pscustomobject]@{ status = $status; reason = $status }
    }.GetNewClosure()
}
function New-C1507Stop($World) {
    return {
        param([int]$ProcessId)
        $World.calls.Add($ProcessId)
        $World.map[[string]$ProcessId] = 'absent'
        return [pscustomobject]@{ ok = $true; reason = 'stopped'; command = (Get-QualificationWindowsTreeStopCommand -ProcessId $ProcessId) }
    }.GetNewClosure()
}

function C1507_StopOrder {
    $world = New-C1507World
    $world.map['4100'] = 'alive'
    $world.map['4200'] = 'alive'
    $world.map['4300'] = 'absent'
    $fixture = New-C1507Fixture @{ pid = 4100; processStartedAt = '2026-11-09T00:00:00.0000000Z' } @{ pid = 4200; processStartedAt = '2026-11-09T00:00:01.0000000Z' } @{ pid = 4300; processStartedAt = '2026-11-09T00:00:02.0000000Z'; runId = 'c1507run' }
    $result = Invoke-C1507 $fixture (New-C1507Observe $world) (New-C1507Stop $world)
    $calls = $world.calls.ToArray()
    Assert-C1507 ($result.exitCode -eq 0 -and $calls.Count -eq 2 -and $calls[0] -eq 4100 -and $calls[1] -eq 4200 -and $result.lockAction -ceq 'moved') `
        'stop kills the supervisor tree before the driver and never the lock owner' (($calls -join ',') + ' lock=' + $result.lockAction + ' ' + $result.detail)
    Assert-C1507 ($result.kills[0].phase -ceq 'supervisor' -and $result.kills[1].phase -ceq 'driver' -and $result.kills[0].command -ceq 'taskkill.exe /PID 4100 /T /F' -and $result.kills[1].command -ceq 'taskkill.exe /PID 4200 /T /F') `
        'recorded stop command is taskkill /T /F in supervisor then driver order' (($result.kills | ForEach-Object { $_.phase + '=' + $_.command }) -join ';')

    $world2 = New-C1507World
    $world2.map['4100'] = 'reused'
    $world2.map['4200'] = 'alive'
    $reused = New-C1507Fixture @{ pid = 4100; processStartedAt = '2026-11-09T00:00:00.0000000Z' } @{ pid = 4200; processStartedAt = '2026-11-09T00:00:01.0000000Z' } @{ pid = 4300; processStartedAt = '2026-11-09T00:00:02.0000000Z'; runId = 'c1507run' }
    $lockBefore = Get-C1507Hash (Join-Path $reused.Coord 'native-run.lock')
    $refused = Invoke-C1507 $reused (New-C1507Observe $world2) (New-C1507Stop $world2)
    Assert-C1507 ($refused.exitCode -eq 2 -and $world2.calls.Count -eq 0 -and -not [IO.File]::Exists((Join-Path $reused.Task 'supervisor.log')) -and (Get-C1507Hash (Join-Path $reused.Coord 'native-run.lock')) -ceq $lockBefore) `
        'a reused supervisor pid is not killed and the lock stays' $refused.detail

    $hits = @{ n = 0 }
    $calls3 = New-Object System.Collections.Generic.List[object]
    $observe3 = {
        param($Record)
        if ([int]$Record.pid -eq 4100) {
            $hits.n++
            if ($hits.n -ge 2) { return [pscustomobject]@{ status = 'reused'; reason = 'start-mismatch' } }
        }
        return [pscustomobject]@{ status = 'alive'; reason = 'pid-start-match' }
    }.GetNewClosure()
    $stop3 = { param([int]$ProcessId) $calls3.Add($ProcessId); [pscustomobject]@{ ok = $true; reason = 'stopped'; command = 'x' } }.GetNewClosure()
    $flip = New-C1507Fixture @{ pid = 4100; processStartedAt = '2026-11-09T00:00:00.0000000Z' } @{ pid = 4200; processStartedAt = '2026-11-09T00:00:01.0000000Z' } $null
    $flipped = Invoke-C1507 $flip $observe3 $stop3
    Assert-C1507 ($flipped.exitCode -eq 2 -and $calls3.Count -eq 0) `
        'a supervisor generation change immediately before the kill aborts with no kill' $flipped.detail

    $world4 = New-C1507World
    $world4.map['4100'] = 'absent'
    $world4.map['4200'] = 'alive'
    $goneSuper = New-C1507Fixture @{ pid = 4100; processStartedAt = '2026-11-09T00:00:00.0000000Z' } @{ pid = 4200; processStartedAt = '2026-11-09T00:00:01.0000000Z' } $null
    $onlyDriver = Invoke-C1507 $goneSuper (New-C1507Observe $world4) (New-C1507Stop $world4)
    Assert-C1507 ($onlyDriver.exitCode -eq 0 -and $world4.calls.Count -eq 1 -and $world4.calls[0] -eq 4200 -and $onlyDriver.kills[0].phase -ceq 'driver') `
        'an already-dead supervisor still stops the recorded driver' (($world4.calls -join ',') + ' ' + $onlyDriver.detail)

    $foreign = New-C1507Fixture @{ pid = 4100; processStartedAt = '2026-11-09T00:00:00.0000000Z' } @{ pid = 4200; processStartedAt = '2026-11-09T00:00:01.0000000Z' } $null
    $idPath = Join-Path $foreign.Task 'supervisor-identity.json'
    [IO.File]::WriteAllText($idPath, ([IO.File]::ReadAllText($idPath).Replace('c1507run', 'other-run')))
    $calls5 = New-Object System.Collections.Generic.List[object]
    $foreignResult = Invoke-C1507 $foreign { param($Record) [pscustomobject]@{ status = 'alive'; reason = 'x' } } { param([int]$ProcessId) $calls5.Add($ProcessId); [pscustomobject]@{ ok = $true; reason = 'stopped'; command = 'x' } }
    Assert-C1507 ($foreignResult.exitCode -eq 2 -and $calls5.Count -eq 0 -and $foreignResult.detail -ceq 'supervisor-run') `
        'a supervisor identity for another run id authorizes no kill' $foreignResult.detail
}

function C1507_LockClear {
    $goneSupervisor = New-C1507Exited
    $goneDriver = New-C1507Exited
    $goneOwner = New-C1507Exited
    $deadLock = [ordered]@{ pid = $goneOwner.pid; processStartedAt = $goneOwner.processStartedAt; runId = 'c1507run'; lane = 'rc' }
    $fixture = New-C1507Fixture $goneSupervisor $goneDriver $deadLock
    $before = Get-C1507Hash (Join-Path $fixture.Coord 'native-run.lock')
    $unexpected = { param([int]$ProcessId) throw ('unexpected stop ' + $ProcessId) }
    $cleared = Invoke-C1507 $fixture $null $unexpected
    $moved = @(Get-ChildItem -LiteralPath (Join-Path $fixture.Task 'stop-evidence') -File -ErrorAction SilentlyContinue)
    $movedHash = 'missing'
    if ($moved.Count -eq 1) { $movedHash = Get-C1507Hash $moved[0].FullName }
    Assert-C1507 ($cleared.exitCode -eq 0 -and $cleared.lockAction -ceq 'moved' -and $moved.Count -eq 1 -and $movedHash -ceq $before -and -not [IO.File]::Exists((Join-Path $fixture.Coord 'native-run.lock'))) `
        'a dead native-lock owner is moved aside and the original path is absent' ($cleared.detail + ' moved=' + $moved.Count + ' hash=' + $movedHash)

    $aliveOwner = New-C1507Self
    $aliveLock = [ordered]@{ pid = $aliveOwner.pid; processStartedAt = $aliveOwner.processStartedAt; runId = 'c1507run'; lane = 'rc' }
    $aliveFixture = New-C1507Fixture (New-C1507Exited) (New-C1507Exited) $aliveLock
    $aliveBefore = Get-C1507Hash (Join-Path $aliveFixture.Coord 'native-run.lock')
    $taskSentinel = Get-C1507Hash (Join-Path $aliveFixture.Task 'last-run.json')
    $alive = Invoke-C1507 $aliveFixture $null $unexpected
    Assert-C1507 ($alive.exitCode -eq 4 -and $alive.lockAction -ceq 'retained-alive' -and (Get-C1507Hash (Join-Path $aliveFixture.Coord 'native-run.lock')) -ceq $aliveBefore -and (Get-C1507Hash (Join-Path $aliveFixture.Task 'last-run.json')) -ceq $taskSentinel) `
        'a live native-lock owner keeps the lock and last-run.json' ($alive.lockAction + ' ' + $alive.detail)

    $world = New-C1507World
    $world.map['4100'] = 'absent'
    $world.map['4200'] = 'absent'
    $world.map['4300'] = 'unknown'
    $unknownFixture = New-C1507Fixture @{ pid = 4100; processStartedAt = '2026-11-09T00:00:00.0000000Z' } @{ pid = 4200; processStartedAt = '2026-11-09T00:00:01.0000000Z' } @{ pid = 4300; processStartedAt = '2026-11-09T00:00:02.0000000Z'; runId = 'c1507run' }
    $unknownBefore = Get-C1507Hash (Join-Path $unknownFixture.Coord 'native-run.lock')
    $unknown = Invoke-C1507 $unknownFixture (New-C1507Observe $world) (New-C1507Stop $world)
    Assert-C1507 ($unknown.exitCode -eq 4 -and $unknown.lockAction -ceq 'retained-unknown' -and (Get-C1507Hash (Join-Path $unknownFixture.Coord 'native-run.lock')) -ceq $unknownBefore) `
        'an unknown native-lock owner keeps the lock' $unknown.detail

    $worldR = New-C1507World
    $worldR.map['4100'] = 'absent'
    $worldR.map['4200'] = 'absent'
    $worldR.map['4300'] = 'reused'
    $reusedFixture = New-C1507Fixture @{ pid = 4100; processStartedAt = '2026-11-09T00:00:00.0000000Z' } @{ pid = 4200; processStartedAt = '2026-11-09T00:00:01.0000000Z' } @{ pid = 4300; processStartedAt = '2026-11-09T00:00:02.0000000Z'; runId = 'c1507run' }
    $reusedBefore = Get-C1507Hash (Join-Path $reusedFixture.Coord 'native-run.lock')
    $reused = Invoke-C1507 $reusedFixture (New-C1507Observe $worldR) (New-C1507Stop $worldR)
    Assert-C1507 ($reused.exitCode -eq 4 -and $reused.lockAction -ceq 'retained-reused' -and (Get-C1507Hash (Join-Path $reusedFixture.Coord 'native-run.lock')) -ceq $reusedBefore) `
        'a reused native-lock pid keeps the lock' $reused.detail

    $foreignLock = New-C1507Fixture @{ pid = 4100; processStartedAt = '2026-11-09T00:00:00.0000000Z' } @{ pid = 4200; processStartedAt = '2026-11-09T00:00:01.0000000Z' } @{ pid = 4300; processStartedAt = '2026-11-09T00:00:02.0000000Z'; runId = 'other-run' }
    $foreignBefore = Get-C1507Hash (Join-Path $foreignLock.Coord 'native-run.lock')
    $worldF = New-C1507World
    $worldF.map['4100'] = 'absent'
    $worldF.map['4200'] = 'absent'
    $worldF.map['4300'] = 'absent'
    $foreign = Invoke-C1507 $foreignLock (New-C1507Observe $worldF) (New-C1507Stop $worldF)
    Assert-C1507 ($foreign.exitCode -eq 4 -and $foreign.lockAction -ceq 'retained-foreign' -and (Get-C1507Hash (Join-Path $foreignLock.Coord 'native-run.lock')) -ceq $foreignBefore) `
        'a native lock for another run is not cleared' $foreign.detail

    $changed = New-C1507Fixture @{ pid = 4100; processStartedAt = '2026-11-09T00:00:00.0000000Z' } @{ pid = 4200; processStartedAt = '2026-11-09T00:00:01.0000000Z' } @{ pid = 4300; processStartedAt = '2026-11-09T00:00:02.0000000Z'; runId = 'c1507run' }
    $worldC = New-C1507World
    $worldC.map['4100'] = 'alive'
    $worldC.map['4200'] = 'absent'
    $worldC.map['4300'] = 'absent'
    $stopChange = {
        param([int]$ProcessId)
        $lockPath = Join-Path $changed.Coord 'native-run.lock'
        [IO.File]::WriteAllText($lockPath, '{"pid":4300,"processStartedAt":"2026-11-09T00:00:02.0000000Z","runId":"c1507run","lane":"replaced"}')
        $worldC.map[[string]$ProcessId] = 'absent'
        [pscustomobject]@{ ok = $true; reason = 'stopped'; command = (Get-QualificationWindowsTreeStopCommand -ProcessId $ProcessId) }
    }.GetNewClosure()
    $changedResult = Invoke-C1507 $changed (New-C1507Observe $worldC) $stopChange
    Assert-C1507 ($changedResult.exitCode -eq 4 -and $changedResult.lockAction -ceq 'retained-changed' -and [IO.File]::Exists((Join-Path $changed.Coord 'native-run.lock'))) `
        'a native lock whose bytes change during the stop is not moved' $changedResult.detail

    $worldS = New-C1507World
    $worldS.map['4100'] = 'alive'
    $worldS.map['4200'] = 'alive'
    $worldS.map['4300'] = 'absent'
    $survivor = New-C1507Fixture @{ pid = 4100; processStartedAt = '2026-11-09T00:00:00.0000000Z' } @{ pid = 4200; processStartedAt = '2026-11-09T00:00:01.0000000Z' } @{ pid = 4300; processStartedAt = '2026-11-09T00:00:02.0000000Z'; runId = 'c1507run' }
    $stopSurvivor = {
        param([int]$ProcessId)
        if ($ProcessId -eq 4100) { $worldS.map['4100'] = 'absent' }
        [pscustomobject]@{ ok = $true; reason = 'stopped'; command = (Get-QualificationWindowsTreeStopCommand -ProcessId $ProcessId) }
    }.GetNewClosure()
    $survivorBefore = Get-C1507Hash (Join-Path $survivor.Coord 'native-run.lock')
    $survivorResult = Invoke-C1507 $survivor (New-C1507Observe $worldS) $stopSurvivor
    Assert-C1507 ($survivorResult.exitCode -eq 3 -and $survivorResult.outcome -ceq 'incomplete' -and (Get-C1507Hash (Join-Path $survivor.Coord 'native-run.lock')) -ceq $survivorBefore) `
        'a driver that survives the stop keeps the native lock' $survivorResult.detail

    $missing = New-C1507Fixture (New-C1507Exited) (New-C1507Exited) $null
    $missingResult = Invoke-C1507 $missing $null $unexpected
    Assert-C1507 ($missingResult.exitCode -eq 0 -and $missingResult.lockAction -ceq 'absent' -and -not [IO.Directory]::Exists((Join-Path $missing.Task 'stop-evidence'))) `
        'a missing native lock is already clear and creates no evidence directory' $missingResult.detail
}

function C1507_RunLog {
    $gone = New-C1507Exited
    $fixture = New-C1507Fixture $gone $gone $null
    $taskHash = Get-C1507Hash (Join-Path $fixture.Task 'last-run.json')
    $coordHash = Get-C1507Hash (Join-Path $fixture.Coord 'last-run.json')
    $unexpected = { param([int]$ProcessId) throw ('unexpected stop ' + $ProcessId) }
    $result = Invoke-C1507 $fixture $null $unexpected
    $logPath = Join-Path $fixture.Task 'supervisor.log'
    $log = ''
    if ([IO.File]::Exists($logPath)) { $log = [IO.File]::ReadAllText($logPath) }
    Assert-C1507 ($result.exitCode -eq 0 -and $log -match 'STOP reason="operator replacement"' -and $log -match 'outcome=stopped' -and $log -match "`r`n$") `
        'the stop reason is appended to the supervisor run log' $log
    Assert-C1507 ((Get-C1507Hash (Join-Path $fixture.Task 'last-run.json')) -ceq $taskHash -and (Get-C1507Hash (Join-Path $fixture.Coord 'last-run.json')) -ceq $coordHash) `
        'the stop does not rewrite last-run.json' 'task/coord sentinel changed'

    $blank = New-C1507Fixture (New-C1507Exited) (New-C1507Exited) $null
    $blankResult = Invoke-C1507 $blank $null $unexpected '  '
    Assert-C1507 ($blankResult.exitCode -eq 2 -and $blankResult.detail -ceq 'reason' -and -not [IO.File]::Exists((Join-Path $blank.Task 'supervisor.log'))) `
        'a blank stop reason is refused before any log write' $blankResult.detail
    $broken = New-C1507Fixture (New-C1507Exited) (New-C1507Exited) $null
    $brokenResult = Invoke-C1507 $broken $null $unexpected "operator`nreplacement"
    Assert-C1507 ($brokenResult.exitCode -eq 2 -and -not [IO.File]::Exists((Join-Path $broken.Task 'supervisor.log'))) `
        'a multi-line stop reason is refused before any log write' $brokenResult.detail
}

function C1507_WindowsTreeStopRefusesOffWindows {
    $platform = Get-QualificationStopPlatform
    if ($platform -cne 'Unix') {
        Assert-C1507 $false 'windows tree stop refusal is a Unix witness; native taskkill is owed and was not run' $platform
        return
    }
    $processId = 12345
    $result = Invoke-QualificationWindowsTreeStop -ProcessId $processId
    $self = [Diagnostics.Process]::GetCurrentProcess()
    $stillAlive = $false
    try { $probe = [Diagnostics.Process]::GetProcessById($self.Id); $stillAlive = -not $probe.HasExited; $probe.Dispose() } catch { $stillAlive = $false }
    Assert-C1507 ($platform -ceq 'Unix' -and $result.ok -eq $false -and $result.reason -ceq 'windows-only' -and $result.command -ceq 'taskkill.exe /PID 12345 /T /F' -and $stillAlive) `
        'Invoke-QualificationWindowsTreeStop refuses off Windows and records taskkill /T /F' ($result.reason + ' ' + $result.command)
}

function C1507_EntryRefusesOffWindows {
    $platform = Get-QualificationStopPlatform
    if ($platform -cne 'Unix') {
        Assert-C1507 $false 'entry refusal is a Unix witness; the native entry was not run' $platform
        return
    }
    $fixture = New-C1507Fixture @{ pid = 4100; processStartedAt = '2026-11-09T00:00:00.0000000Z' } @{ pid = 4200; processStartedAt = '2026-11-09T00:00:01.0000000Z' } @{ pid = 4300; processStartedAt = '2026-11-09T00:00:02.0000000Z'; runId = 'c1507run' }
    $lockBefore = Get-C1507Hash (Join-Path $fixture.Coord 'native-run.lock')
    $text = @(& $script:pwsh -NoProfile -NonInteractive -File $script:stopScript -TaskRoot $fixture.Task -CoordinationRoot $fixture.Coord -RunId $fixture.RunId -Reason 'operator replacement' -Confirm 2>&1) -join "`n"
    $code = $LASTEXITCODE
    Assert-C1507 ($code -eq 5 -and $text -match 'windows-only' -and -not [IO.File]::Exists((Join-Path $fixture.Task 'supervisor.log')) -and (Get-C1507Hash (Join-Path $fixture.Coord 'native-run.lock')) -ceq $lockBefore) `
        'stop-supervisor.ps1 refuses on this host before any kill or lock change' ($code.ToString() + ' ' + $text)
    $noConfirm = @(& $script:pwsh -NoProfile -NonInteractive -File $script:stopScript -TaskRoot $fixture.Task -CoordinationRoot $fixture.Coord -RunId $fixture.RunId -Reason 'operator replacement' 2>&1) -join "`n"
    Assert-C1507 ($LASTEXITCODE -eq 2 -and $noConfirm -match 'Confirm') `
        'stop-supervisor.ps1 without -Confirm does not stop anything' ($LASTEXITCODE.ToString() + ' ' + $noConfirm)
    $bytes = [IO.File]::ReadAllBytes($script:stopScript) + [IO.File]::ReadAllBytes($lib)
    $ascii = $true
    foreach ($b in $bytes) { if ($b -gt 127) { $ascii = $false; break } }
    Assert-C1507 $ascii 'stop scripts are ASCII' 'non-ascii byte'
}

try {
    $selected = @(
        'C1507_StopOrder',
        'C1507_LockClear',
        'C1507_RunLog',
        'C1507_WindowsTreeStopRefusesOffWindows',
        'C1507_EntryRefusesOffWindows'
    )
    if ($Case -ne 'Smoke') {
        if ($selected -notcontains $Case) { throw ('unknown case ' + $Case) }
        $selected = @($Case)
    }
    foreach ($name in $selected) { & $name }
} catch {
    Write-Host ('FAIL C1507 harness error: ' + $_.Exception.Message)
    $script:failed++
} finally {
    if ([IO.Directory]::Exists($script:scratch)) { [IO.Directory]::Delete($script:scratch, $true) }
}
Write-Host "C1507 passed=$script:passed failed=$script:failed"
if ($script:failed -gt 0) { exit 1 }
exit 0
