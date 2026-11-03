#requires -Version 5.1
<#
.SYNOPSIS
CARD-1667: observe one rc qualification launch; start it only through start-supervisor.ps1.
.DESCRIPTION
Records its own identity, starts the launch script (launch-rc.ps1) with its own console
(Windows) and its own output files, then writes a heartbeat every PollSeconds, a PROGRESS line
every ProgressMinutes, an observation-only HANG_SUSPECTED flag, and finally EXIT <code> and
commission-exit.txt. Every log line is flushed to TaskRoot\supervisor.log as it is written.
The supervisor never stops, signals, or restarts a process and never clears a lock. Its death
does not end the run: observe.ps1 reads last-run.json completedAt as the end signal.
ASCII-only, Windows PowerShell 5.1 safe.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$TaskRoot,
    [Parameter(Mandatory = $true)][string]$LaunchScript,
    [Parameter(Mandatory = $true)][string]$LogRoot,
    [Parameter(Mandatory = $true)][string]$StateRoot,
    [ValidateRange(1, 300)][int]$PollSeconds = 30,
    [ValidateRange(1, 120)][int]$ProgressMinutes = 10,
    [ValidateRange(1, 600)][double]$HangMinutes = 20,
    [ValidateSet('new-console', 'setsid', 'none')][string]$Detach = 'none',
    [string]$PowerShellPath
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../lib/qualification-supervisor.ps1')
. (Join-Path $PSScriptRoot '../lib/qualification-supervisor-heartbeat.ps1')
$log = Join-Path $TaskRoot 'supervisor.log'
$heartbeat = Join-Path $TaskRoot 'supervisor-heartbeat.json'
$platform = Get-QualificationHostPlatform
if ([string]::IsNullOrWhiteSpace($PowerShellPath)) { $PowerShellPath = [Diagnostics.Process]::GetCurrentProcess().MainModule.FileName }

$self = [Diagnostics.Process]::GetCurrentProcess()
$identity = [ordered]@{
    pid = $self.Id
    processStartedAt = $self.StartTime.ToUniversalTime().ToString('o')
    script = $PSCommandPath
    launchScript = $LaunchScript
    platform = $platform
    detach = $Detach
    sessionId = (Get-QualificationSessionId)
    consoleProcessIds = @(Get-QualificationConsoleProcessIds)
}
Write-QualificationSupervisorRecord -Path (Join-Path $TaskRoot 'supervisor-identity.json') -Text ($identity | ConvertTo-Json -Compress)
Write-QualificationSupervisorLine -Path $log -Text ("START supervisor pid={0} platform={1} detach={2} session={3} console=[{4}]" -f $self.Id, $platform, $Detach, $identity.sessionId, (@($identity.consoleProcessIds) -join ','))

$exitCode = $null
try {
    $start = New-QualificationDetachedStartParameters -FilePath $PowerShellPath -Platform $platform -SameSession `
        -ArgumentList @('-NoProfile', '-NonInteractive', '-File', $LaunchScript) `
        -StandardOutput (Join-Path $TaskRoot 'commission-console.log') -StandardError (Join-Path $TaskRoot 'commission-stderr.log')
    $child = Start-Process @start
    # Touch the handle now so ExitCode stays readable after the child exits.
    $null = $child.Handle
    $childStart = $child.StartTime.ToUniversalTime().ToString('o')
    Write-QualificationSupervisorRecord -Path (Join-Path $TaskRoot 'launch-child.json') -Text (([ordered]@{ pid = $child.Id; processStartedAt = $childStart }) | ConvertTo-Json -Compress)
    Write-QualificationSupervisorLine -Path $log -Text ("LAUNCHED pid={0} started={1} script={2}" -f $child.Id, $childStart, $LaunchScript)

    $previous = $null
    $hangReported = $false
    $nextProgress = [datetime]::UtcNow.AddMinutes($ProgressMinutes)
    while (-not $child.WaitForExit($PollSeconds * 1000)) {
        try {
            $files = @()
            foreach ($rootPath in @($LogRoot, $StateRoot)) {
                if ([IO.Directory]::Exists($rootPath)) { $files += @(Get-ChildItem -LiteralPath $rootPath -Recurse -File -Force -ErrorAction SilentlyContinue) }
            }
            $bytes = [int64]0
            foreach ($file in $files) { $bytes += $file.Length }
            $trx = @($files | Where-Object { $_.Extension -eq '.trx' }).Count
            $newest = $files | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
            $newestName = ''
            if ($newest) { $newestName = $newest.Name }
            $beat = Update-QualificationSupervisorHeartbeat -NowUtc ([datetime]::UtcNow) -Processes (Get-QualificationProcessTable) `
                -ChunkRootPid $child.Id -ExcludedPids @($self.Id) -Previous $previous -Trx $trx -Bytes $bytes -Newest $newestName `
                -PlannedSeconds $null -DefaultIdleMinutes $HangMinutes
            Write-QualificationSupervisorHeartbeat -Record $beat -Path $heartbeat
            $previous = $beat
            if ($beat.hangSuspected -and -not $hangReported) {
                $hangReported = $true
                $line = "HANG_SUSPECTED idleMinutes={0} reason={1} (observe only; nothing is stopped)" -f $beat.idleMinutes, $beat.hangReason
                Write-QualificationSupervisorRecord -Path (Join-Path $TaskRoot 'hang-suspected.txt') -Text $line
                Write-QualificationSupervisorLine -Path $log -Text $line
            }
            if (-not $beat.hangSuspected) { $hangReported = $false }
            if ([datetime]::UtcNow -ge $nextProgress) {
                $nextProgress = [datetime]::UtcNow.AddMinutes($ProgressMinutes)
                Write-QualificationSupervisorLine -Path $log -Text ("PROGRESS trx={0} bytes={1} idleMinutes={2} cpuSeconds={3} newest={4}" -f $trx, $bytes, $beat.idleMinutes, $beat.cpuSeconds, $newestName)
            }
        } catch {
            # A failed observation must never end the supervisor; the next poll retries.
            try { Write-QualificationSupervisorLine -Path $log -Text ("HEARTBEAT-ERROR " + $_.Exception.Message) } catch { }
        }
    }
    $child.WaitForExit()
    $exitCode = $child.ExitCode
    Write-QualificationSupervisorLine -Path $log -Text ("EXIT {0}" -f $exitCode)
    Write-QualificationSupervisorRecord -Path (Join-Path $TaskRoot 'commission-exit.txt') -Text ([string]$exitCode)
} catch {
    try { Write-QualificationSupervisorLine -Path $log -Text ("SUPERVISOR-ERROR " + $_.Exception.Message) } catch { }
    throw
} finally {
    try { Write-QualificationSupervisorLine -Path $log -Text 'SUPERVISOR-END' } catch { }
}
exit 0
