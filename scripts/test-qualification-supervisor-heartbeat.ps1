#requires -Version 5.1
# CARD-1606. Injected process table and clock. No live process is started or stopped.
# ASCII-only.
$ErrorActionPreference = 'Stop'
$script:passed = 0
$script:failed = 0

function Write-Pass {
    param([string]$Name)
    $script:passed++
    Write-Host "PASS $Name"
}

function Write-Fail {
    param([string]$Name, [string]$Detail)
    $script:failed++
    Write-Host "FAIL $Name - $Detail"
}

. (Join-Path $PSScriptRoot 'lib/qualification-supervisor-heartbeat.ps1')

function New-HeartbeatProcess {
    param([int]$ProcessId, [int]$ParentId, [double]$CpuSeconds)
    return [pscustomobject]@{ Pid = $ProcessId; ParentPid = $ParentId; CpuSeconds = $CpuSeconds }
}

function Invoke-MonitorIdleWitness {
    $origin = [datetime]::Parse('2026-10-28T12:00:00Z', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AdjustToUniversal)
    $cpu = @{
        100 = 1000.0
        200 = 5400.0
        300 = 5.0
        400 = 2.0
        178996 = 93.0
    }
    $previous = $null
    $beat = $null
    for ($minute = 0; $minute -le 20; $minute++) {
        $table = @(
            (New-HeartbeatProcess 100 0 $cpu[100]),
            (New-HeartbeatProcess 200 100 $cpu[200]),
            (New-HeartbeatProcess 300 100 $cpu[300]),
            (New-HeartbeatProcess 400 100 $cpu[400]),
            (New-HeartbeatProcess 178996 400 $cpu[178996])
        )
        $beat = Update-QualificationSupervisorHeartbeat -NowUtc $origin.AddMinutes($minute) -Processes $table `
            -ChunkRootPid 400 -ExcludedPids @(100, 200, 300) -Previous $previous -Trx 0 -Bytes 1000 `
            -Newest 'log.diag' -PlannedSeconds $null
        $previous = $beat
        if ($minute -lt 20) {
            $cpu[100] += 10
            $cpu[200] += 120
            $cpu[300] += 0.2
            $cpu[400] += 0.01
            $cpu[178996] += 0.05
        }
    }
    return $beat
}

function New-HeartbeatRow {
    param([int]$ProcessId, [int]$ParentId)
    return [pscustomobject]@{ Id = $ProcessId; Parent = $ParentId }
}

function Invoke-SteppedHeartbeat {
    param(
        [object[]]$Rows,
        [hashtable]$Cpu,
        [hashtable]$Gain,
        [int]$Minutes,
        [int]$ChunkRootPid,
        [int[]]$ExcludedPids,
        $PlannedSeconds,
        [double]$InitialTrx = 0,
        [int64]$InitialBytes = 1000,
        [int]$BumpMinute = -1,
        [double]$BumpTrx = 0,
        [int64]$BumpBytes = 0
    )
    $origin = [datetime]::Parse('2026-10-28T12:00:00Z', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AdjustToUniversal)
    $trx = $InitialTrx
    $bytes = $InitialBytes
    $previous = $null
    $beat = $null
    for ($minute = 0; $minute -le $Minutes; $minute++) {
        if ($minute -eq $BumpMinute) {
            $trx = $BumpTrx
            if ($BumpBytes -ne 0) { $bytes = $BumpBytes }
        }
        $table = @()
        foreach ($row in @($Rows)) {
            $table += New-HeartbeatProcess -ProcessId $row.Id -ParentId $row.Parent -CpuSeconds $Cpu[$row.Id]
        }
        $beat = Update-QualificationSupervisorHeartbeat -NowUtc $origin.AddMinutes($minute) -Processes $table `
            -ChunkRootPid $ChunkRootPid -ExcludedPids $ExcludedPids -Previous $previous -Trx $trx -Bytes $bytes `
            -Newest 'log.diag' -PlannedSeconds $PlannedSeconds
        $previous = $beat
        if ($minute -lt $Minutes) {
            foreach ($row in @($Rows)) { $Cpu[$row.Id] = [double]$Cpu[$row.Id] + [double]$Gain[$row.Id] }
        }
    }
    return $beat
}

function Assert-Hang {
    param($Beat, [bool]$Expected, [string]$Name)
    $detail = "hangSuspected=$($Beat.hangSuspected) idleMinutes=$($Beat.idleMinutes) cpuSeconds=$($Beat.cpuSeconds) budget=$($Beat.idleBudgetMinutes) source=$($Beat.idleBudgetSource) reason=$($Beat.hangReason)"
    if ([bool]$Beat.hangSuspected -eq $Expected) { Write-Pass $Name } else { Write-Fail $Name $detail }
}

$witness = Invoke-MonitorIdleWitness
$detail = "hangSuspected=$($witness.hangSuspected) idleMinutes=$($witness.idleMinutes) cpuSeconds=$($witness.cpuSeconds) reason=$($witness.hangReason)"
if ($witness.hangSuspected -eq $true -and $witness.idleMinutes -ge 20 -and $witness.cpuSeconds -lt 200 -and [string]$witness.idleBudgetSource -eq 'default-unknown-group' -and [double]$witness.idleBudgetMinutes -eq 20) {
    $listed = @($witness.suspectPids | ForEach-Object { [int]$_ })
    if ($listed -contains 178996 -and -not [string]::IsNullOrWhiteSpace([string]$witness.hangReason)) {
        Write-Pass 'supervisor monitor CPU does not hold idleMinutes at zero'
    } else {
        Write-Fail 'supervisor monitor CPU does not hold idleMinutes at zero' $detail
    }
} else {
    Write-Fail 'supervisor monitor CPU does not hold idleMinutes at zero' $detail
}

$nestedRows = @(
    (New-HeartbeatRow 400 100),
    (New-HeartbeatRow 200 400),
    (New-HeartbeatRow 300 400),
    (New-HeartbeatRow 178996 400)
)
$nested = Invoke-SteppedHeartbeat -Rows $nestedRows -Cpu @{ 400 = 2.0; 200 = 5400.0; 300 = 5.0; 178996 = 93.0 } `
    -Gain @{ 400 = 0.01; 200 = 120.0; 300 = 50.0; 178996 = 0.05 } -Minutes 20 -ChunkRootPid 400 `
    -ExcludedPids @(200, 300) -PlannedSeconds $null
$nestedIds = @($nested.suspectPids | ForEach-Object { [int]$_ })
if ($nested.hangSuspected -eq $true -and $nested.cpuSeconds -lt 200 -and $nestedIds -contains 178996 -and -not ($nestedIds -contains 200) -and -not ($nestedIds -contains 300)) {
    Write-Pass 'monitor and heartbeat writer inside the chunk tree are excluded'
} else {
    Write-Fail 'monitor and heartbeat writer inside the chunk tree are excluded' "hang=$($nested.hangSuspected) cpu=$($nested.cpuSeconds) pids=$($nestedIds -join ',')"
}

$busyRows = @((New-HeartbeatRow 400 100), (New-HeartbeatRow 200 100), (New-HeartbeatRow 178996 400))
$busy = Invoke-SteppedHeartbeat -Rows $busyRows -Cpu @{ 400 = 2.0; 200 = 5400.0; 178996 = 93.0 } `
    -Gain @{ 400 = 0.01; 200 = 120.0; 178996 = 30.0 } -Minutes 41 -ChunkRootPid 400 `
    -ExcludedPids @(100, 200) -PlannedSeconds $null
Assert-Hang $busy $false 'a busy test host is not a hang while the monitor burns CPU'
if ([double]$busy.idleMinutes -eq 0) { Write-Pass 'busy test host keeps idleMinutes at zero' } else { Write-Fail 'busy test host keeps idleMinutes at zero' "idleMinutes=$($busy.idleMinutes)" }

$trxRows = @((New-HeartbeatRow 178996 0))
$trxBeat = Invoke-SteppedHeartbeat -Rows $trxRows -Cpu @{ 178996 = 93.0 } -Gain @{ 178996 = 0.01 } -Minutes 19 `
    -ChunkRootPid 178996 -ExcludedPids @() -PlannedSeconds 120 -BumpMinute 19 -BumpTrx 4
Assert-Hang $trxBeat $false 'trx growth clears a would-be hang'
if ([double]$trxBeat.idleMinutes -lt 0.001 -and [double]$trxBeat.trx -eq 4) { Write-Pass 'trx growth resets idleMinutes' } else { Write-Fail 'trx growth resets idleMinutes' "idle=$($trxBeat.idleMinutes) trx=$($trxBeat.trx)" }

$byteBeat = Invoke-SteppedHeartbeat -Rows $trxRows -Cpu @{ 178996 = 93.0 } -Gain @{ 178996 = 0.01 } -Minutes 19 `
    -ChunkRootPid 178996 -ExcludedPids @() -PlannedSeconds 120 -BumpMinute 19 -BumpTrx 0 -BumpBytes 2500
Assert-Hang $byteBeat $false 'chunk-log byte growth clears a would-be hang'
if ([double]$byteBeat.idleMinutes -lt 0.001 -and [int64]$byteBeat.bytes -eq 2500) { Write-Pass 'chunk-log byte growth resets idleMinutes' } else { Write-Fail 'chunk-log byte growth resets idleMinutes' "idle=$($byteBeat.idleMinutes) bytes=$($byteBeat.bytes)" }

$floorEarly = Invoke-SteppedHeartbeat -Rows $trxRows -Cpu @{ 178996 = 1.0 } -Gain @{ 178996 = 0.01 } -Minutes 9 `
    -ChunkRootPid 178996 -ExcludedPids @() -PlannedSeconds 120
$floorLate = Invoke-SteppedHeartbeat -Rows $trxRows -Cpu @{ 178996 = 1.0 } -Gain @{ 178996 = 0.01 } -Minutes 10 `
    -ChunkRootPid 178996 -ExcludedPids @() -PlannedSeconds 120
if ($floorEarly.hangSuspected -eq $false -and [double]$floorEarly.idleBudgetMinutes -eq 10 -and [string]$floorEarly.idleBudgetSource -eq 'plannedSeconds') {
    Write-Pass 'plannedSeconds below 10 minutes uses the 10 minute floor'
} else {
    Write-Fail 'plannedSeconds below 10 minutes uses the 10 minute floor' "hang=$($floorEarly.hangSuspected) budget=$($floorEarly.idleBudgetMinutes) source=$($floorEarly.idleBudgetSource)"
}
Assert-Hang $floorLate $true 'idle for the 10 minute floor is a hang'

$longEarly = Invoke-SteppedHeartbeat -Rows $trxRows -Cpu @{ 178996 = 1.0 } -Gain @{ 178996 = 0.01 } -Minutes 20 `
    -ChunkRootPid 178996 -ExcludedPids @() -PlannedSeconds 1800
$longLate = Invoke-SteppedHeartbeat -Rows $trxRows -Cpu @{ 178996 = 1.0 } -Gain @{ 178996 = 0.01 } -Minutes 30 `
    -ChunkRootPid 178996 -ExcludedPids @() -PlannedSeconds 1800
if ($longEarly.hangSuspected -eq $false -and [double]$longEarly.idleBudgetMinutes -eq 30 -and [string]$longEarly.idleBudgetSource -eq 'plannedSeconds') {
    Write-Pass 'a 30 minute plan does not hang at the 20 minute default'
} else {
    Write-Fail 'a 30 minute plan does not hang at the 20 minute default' "hang=$($longEarly.hangSuspected) budget=$($longEarly.idleBudgetMinutes) idle=$($longEarly.idleMinutes)"
}
Assert-Hang $longLate $true 'idle for the planned 30 minutes is a hang'

$absentRows = @((New-HeartbeatRow 100 0))
$absent = Invoke-SteppedHeartbeat -Rows $absentRows -Cpu @{ 100 = 10.0 } -Gain @{ 100 = 5.0 } -Minutes 20 `
    -ChunkRootPid 400 -ExcludedPids @(100) -PlannedSeconds $null
if ($absent.hangSuspected -eq $true -and [string]$absent.hangReason -match 'no test-host' -and @($absent.suspectPids) -contains 400) {
    Write-Pass 'a chunk with no test host is the suspect'
} else {
    Write-Fail 'a chunk with no test host is the suspect' "hang=$($absent.hangSuspected) reason=$($absent.hangReason) pids=$(@($absent.suspectPids) -join ',')"
}

$temp = Join-Path ([IO.Path]::GetTempPath()) ('qhb-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try {
    $plannedPath = Join-Path $temp 'groups-planned.json'
    $plan=[ordered]@{version=1;suite='antiphon';runId='standalone-heartbeat';attempt='1';sourceDigest=('a'*40);policyDigest=('b'*64);costFileDigest=('c'*64);assemblyDigest=('d'*64);groups=@([ordered]@{chunk='all-group-105';plannedSeconds=600},[ordered]@{chunk='other';plannedSeconds=1800})}
    $plannedJson=ConvertTo-Json -InputObject $plan -Depth 8 -Compress
    [IO.File]::WriteAllText($plannedPath, $plannedJson)
    [IO.File]::WriteAllText((Join-Path $temp 'groups-planned-antiphon.json'),$plannedJson)
    $index=[ordered]@{version=1;runId=$plan.runId;attempt='1';suites=@([ordered]@{suite='antiphon';file='groups-planned-antiphon.json';runId=$plan.runId;attempt='1';sourceDigest=$plan.sourceDigest;policyDigest=$plan.policyDigest;costFileDigest=$plan.costFileDigest;assemblyDigest=$plan.assemblyDigest})}
    [IO.File]::WriteAllText((Join-Path $temp 'groups-planned-index.json'),(ConvertTo-Json -InputObject $index -Depth 8 -Compress))
    [void](Write-QualificationActiveChunkIdentity $temp 'antiphon' 'all-group-105' $plan.runId '1')
    $found = Get-QualificationGroupPlannedSeconds -Path $plannedPath -ChunkId 'all-group-105'
    $missingChunk = Get-QualificationGroupPlannedSeconds -Path $plannedPath -ChunkId 'missing-group'
    $wrongCase = Get-QualificationGroupPlannedSeconds -Path $plannedPath -ChunkId 'All-Group-105'
    $missingFile = Get-QualificationGroupPlannedSeconds -Path (Join-Path $temp 'absent.json') -ChunkId 'all-group-105'
    [IO.File]::WriteAllText((Join-Path $temp 'bad.json'), '{')
    $malformed = Get-QualificationGroupPlannedSeconds -Path (Join-Path $temp 'bad.json') -ChunkId 'all-group-105'
    if ($found -eq 600 -and $null -eq $missingChunk -and $null -eq $wrongCase -and $null -eq $missingFile -and $null -eq $malformed) {
        Write-Pass 'groups-planned.json exposes plannedSeconds for the current chunk'
    } else {
        Write-Fail 'groups-planned.json exposes plannedSeconds for the current chunk' "found=$found missing=$missingChunk case=$wrongCase file=$missingFile bad=$malformed"
    }

    $heartbeatPath = Join-Path $temp 'supervisor-heartbeat.json'
    Write-QualificationSupervisorHeartbeat -Record $witness -Path $heartbeatPath
    $raw = [IO.File]::ReadAllText($heartbeatPath)
    $names = @('cpuSeconds', 'bytes', 'idleMinutes', 'trx', 'newest', 'hangSuspected', 'hangReason', 'suspectPids')
    $missingNames = @()
    foreach ($name in $names) {
        if ($raw.IndexOf('"' + $name + '"') -lt 0) { $missingNames += $name }
    }
    $round = Read-QualificationSupervisorHeartbeat -Path $heartbeatPath
    $roundIds = @($round.suspectPids | ForEach-Object { [int]$_ })
    if ($missingNames.Count -eq 0 -and $round.hangSuspected -eq $true -and [string]$round.newest -eq 'log.diag' -and $roundIds -contains 178996) {
        Write-Pass 'heartbeat JSON keeps existing names and adds hangReason plus suspectPids'
    } else {
        Write-Fail 'heartbeat JSON keeps existing names and adds hangReason plus suspectPids' "missing=$($missingNames -join ',') hang=$($round.hangSuspected) newest=$($round.newest)"
    }

    $subjectCpu = @{}
    foreach ($sample in @($round.hostCpu)) { $subjectCpu[[int]$sample.pid] = [double]$sample.cpuSeconds }
    $continuedTable = @(
        (New-HeartbeatProcess 100 0 1200),
        (New-HeartbeatProcess 200 100 7800),
        (New-HeartbeatProcess 300 100 9),
        (New-HeartbeatProcess 400 100 $subjectCpu[400]),
        (New-HeartbeatProcess 178996 400 $subjectCpu[178996])
    )
    $continuedAt = ([datetime]::Parse([string]$round.observedAtUtc, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AdjustToUniversal)).AddMinutes(1)
    $continued = Update-QualificationSupervisorHeartbeat -NowUtc $continuedAt -Processes $continuedTable -ChunkRootPid 400 `
        -ExcludedPids @(100, 200, 300) -Previous $round -Trx 0 -Bytes 1000 -Newest 'log.diag' -PlannedSeconds $null
    if ($continued.hangSuspected -eq $true -and [double]$continued.idleMinutes -ge 21) {
        Write-Pass 'a reread heartbeat continues the idle clock'
    } else {
        Write-Fail 'a reread heartbeat continues the idle clock' "hang=$($continued.hangSuspected) idle=$($continued.idleMinutes)"
    }
} finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}

$libraryPath = Join-Path $PSScriptRoot 'lib/qualification-supervisor-heartbeat.ps1'
$libraryText = [IO.File]::ReadAllText($libraryPath)
$libraryBytes = [IO.File]::ReadAllBytes($libraryPath)
$nonAscii = @($libraryBytes | Where-Object { $_ -gt 127 }).Count
$forbidden = @('Stop-Process', 'taskkill', 'TerminateProcess', '.Kill(')
$foundForbidden = @()
foreach ($token in $forbidden) {
    if ($libraryText.IndexOf($token) -ge 0) { $foundForbidden += $token }
}
if ($nonAscii -eq 0 -and $foundForbidden.Count -eq 0) {
    Write-Pass 'heartbeat library is ASCII and does not kill'
} else {
    Write-Fail 'heartbeat library is ASCII and does not kill' "nonascii=$nonAscii forbidden=$($foundForbidden -join ',')"
}

Write-Host "passed=$script:passed failed=$script:failed"
if ($script:failed -gt 0) { exit 1 }
exit 0
