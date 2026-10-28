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

$witness = Invoke-MonitorIdleWitness
$detail = "hangSuspected=$($witness.hangSuspected) idleMinutes=$($witness.idleMinutes) cpuSeconds=$($witness.cpuSeconds) reason=$($witness.hangReason)"
if ($witness.hangSuspected -eq $true -and $witness.idleMinutes -ge 20 -and $witness.cpuSeconds -lt 200) {
    $listed = @($witness.suspectPids | ForEach-Object { [int]$_ })
    if ($listed -contains 178996 -and -not [string]::IsNullOrWhiteSpace([string]$witness.hangReason)) {
        Write-Pass 'supervisor monitor CPU does not hold idleMinutes at zero'
    } else {
        Write-Fail 'supervisor monitor CPU does not hold idleMinutes at zero' $detail
    }
} else {
    Write-Fail 'supervisor monitor CPU does not hold idleMinutes at zero' $detail
}

Write-Host "passed=$script:passed failed=$script:failed"
if ($script:failed -gt 0) { exit 1 }
exit 0
