#requires -Version 5.1
# CARD-1507: stop a recorded rc qualification by supervisor pid, then launch-child pid.
# Dot-source only. The entry point is stop-supervisor.ps1. ASCII-only, Windows PowerShell 5.1 safe.
# Nothing in this file runs at load.
. (Join-Path $PSScriptRoot '../lib/qualification-supervisor.ps1')

function Get-QualificationStopPlatform {
    if (($PSVersionTable.PSEdition -eq 'Desktop') -or $IsWindows) { return 'Windows' }
    return 'Unix'
}

function Get-QualificationWindowsTreeStopCommand {
    param([Parameter(Mandatory)][int]$ProcessId)
    return 'taskkill.exe /PID ' + [string]$ProcessId + ' /T /F'
}

function Invoke-QualificationWindowsTreeStop {
    param([Parameter(Mandatory)][int]$ProcessId)
    $command = Get-QualificationWindowsTreeStopCommand -ProcessId $ProcessId
    if ((Get-QualificationStopPlatform) -cne 'Windows') {
        return [pscustomobject]@{ ok = $false; reason = 'windows-only'; command = $command }
    }
    if ($ProcessId -le 0 -or $ProcessId -eq [Diagnostics.Process]::GetCurrentProcess().Id) {
        return [pscustomobject]@{ ok = $false; reason = 'refused-pid'; command = $command }
    }
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = 'taskkill.exe'
    $info.Arguments = '/PID ' + [string]$ProcessId + ' /T /F'
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $process = $null
    try {
        $process = [Diagnostics.Process]::Start($info)
        if (-not $process.WaitForExit(30000)) {
            try { $process.Kill() } catch { }
            return [pscustomobject]@{ ok = $false; reason = 'taskkill-timeout'; command = $command }
        }
        $reason = 'taskkill-exit-' + [string]$process.ExitCode
        if ($process.ExitCode -eq 0) { $reason = 'stopped' }
        return [pscustomobject]@{ ok = ($process.ExitCode -eq 0); reason = $reason; command = $command }
    } catch {
        return [pscustomobject]@{ ok = $false; reason = 'taskkill-start-failed'; command = $command }
    } finally {
        if ($process) { $process.Dispose() }
    }
}

function Test-QualificationStopReason {
    param([string]$Reason)
    if ([string]::IsNullOrWhiteSpace($Reason)) { return $false }
    if ($Reason.Length -gt 200) { return $false }
    foreach ($ch in $Reason.ToCharArray()) {
        $code = [int]$ch
        if ($code -lt 32 -or $code -gt 126) { return $false }
    }
    return $true
}

function Read-QualificationExactBytes {
    param([string]$Path)
    $stream = $null
    try {
        $stream = Open-RcQualificationSharedRead $Path
        if ($stream.Length -gt 1048576) { return $null }
        $buffer = New-Object byte[] ([int]$stream.Length)
        $count = 0
        while ($count -lt $buffer.Length) {
            $got = $stream.Read($buffer, $count, $buffer.Length - $count)
            if ($got -le 0) { break }
            $count += $got
        }
        if ($count -ne $buffer.Length) { return $null }
        return ,$buffer
    } catch { return $null }
    finally { if ($stream) { $stream.Dispose() } }
}

function Test-QualificationSameBytes {
    param($Left, $Right)
    if ($null -eq $Left -or $null -eq $Right) { return $false }
    if ($Left.Length -ne $Right.Length) { return $false }
    for ($i = 0; $i -lt $Left.Length; $i++) {
        if ($Left[$i] -ne $Right[$i]) { return $false }
    }
    return $true
}

function Test-QualificationSameVolume {
    param([string]$Left, [string]$Right)
    try {
        $leftFull = [IO.Path]::GetFullPath($Left)
        $rightFull = [IO.Path]::GetFullPath($Right)
        if ((Get-QualificationStopPlatform) -ceq 'Windows') {
            $leftRoot = [IO.Path]::GetPathRoot($leftFull)
            $rightRoot = [IO.Path]::GetPathRoot($rightFull)
            if ([string]::IsNullOrWhiteSpace($leftRoot) -or [string]::IsNullOrWhiteSpace($rightRoot)) { return $false }
            return $leftRoot -ceq $rightRoot
        }
        $leftItem = Get-Item -LiteralPath $leftFull -Force
        $rightItem = Get-Item -LiteralPath $rightFull -Force
        if (-not $leftItem.PSObject.Properties['UnixStat'] -or -not $rightItem.PSObject.Properties['UnixStat']) { return $false }
        return ($leftItem.UnixStat.DeviceId -eq $rightItem.UnixStat.DeviceId)
    } catch { return $false }
}

function New-QualificationStopResult {
    param([int]$ExitCode, [string]$Outcome, [string]$Detail, $Kills, [string]$LockAction, $Retained, $LogPath)
    $killList = @()
    if ($Kills) { $killList = @($Kills) }
    return [pscustomobject]@{
        exitCode = $ExitCode
        outcome = $Outcome
        detail = $Detail
        kills = $killList
        lockAction = $LockAction
        retainedLockPath = $Retained
        logPath = $LogPath
    }
}

function Invoke-QualificationRecordedStop {
    param(
        [Parameter(Mandatory)][string]$TaskRoot,
        [Parameter(Mandatory)][string]$CoordinationRoot,
        [Parameter(Mandatory)][string]$RunId,
        [Parameter(Mandatory)][string]$Reason,
        [string]$EvidenceRoot,
        [string]$Platform,
        [scriptblock]$Observe,
        [scriptblock]$StopTree,
        [int]$CensusMilliseconds = 0
    )
    if ($RunId -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$') {
        return (New-QualificationStopResult 2 'refused' 'run-id' $null 'not-attempted' $null $null)
    }
    if (-not (Test-QualificationStopReason $Reason)) {
        return (New-QualificationStopResult 2 'refused' 'reason' $null 'not-attempted' $null $null)
    }
    if ([string]::IsNullOrWhiteSpace($Platform)) { $Platform = Get-QualificationStopPlatform }
    if ($Platform -cne 'Windows') {
        return (New-QualificationStopResult 5 'refused' 'windows-only' $null 'not-attempted' $null $null)
    }
    if (-not $Observe) { $Observe = { param($Record) Get-QualificationProcessObservation -Record $Record } }
    if (-not $StopTree) { $StopTree = { param([int]$ProcessId) Invoke-QualificationWindowsTreeStop -ProcessId $ProcessId } }

    $taskFull = [IO.Path]::GetFullPath($TaskRoot)
    $coordFull = [IO.Path]::GetFullPath($CoordinationRoot)
    if (-not [IO.Directory]::Exists($taskFull)) {
        return (New-QualificationStopResult 2 'refused' 'task-root' $null 'not-attempted' $null $null)
    }
    if (-not [IO.Directory]::Exists($coordFull)) {
        return (New-QualificationStopResult 2 'refused' 'coordination-root' $null 'not-attempted' $null $null)
    }

    $identityPath = [IO.Path]::Combine($taskFull, 'supervisor-identity.json')
    $driverPath = [IO.Path]::Combine($taskFull, 'launch-child.json')
    $logPath = [IO.Path]::Combine($taskFull, 'supervisor.log')
    $lockPath = [IO.Path]::Combine($coordFull, 'native-run.lock')

    $identity = Read-RcQualificationRecord -Path $identityPath
    if ($identity.status -cne 'ok') {
        return (New-QualificationStopResult 2 'refused' ('supervisor-' + $identity.status) $null 'not-attempted' $null $null)
    }
    if ([string]$identity.record.runId -cne $RunId) {
        return (New-QualificationStopResult 2 'refused' 'supervisor-run' $null 'not-attempted' $null $null)
    }

    $driverRead = Read-RcQualificationRecord -Path $driverPath
    $driverRecord = $null
    if ($driverRead.status -ceq 'missing') { }
    elseif ($driverRead.status -cne 'ok') {
        return (New-QualificationStopResult 2 'refused' ('driver-' + $driverRead.status) $null 'not-attempted' $null $null)
    } elseif ([string]$driverRead.record.runId -cne $RunId) {
        return (New-QualificationStopResult 2 'refused' 'driver-run' $null 'not-attempted' $null $null)
    } else { $driverRecord = $driverRead.record }

    $lock = Read-RcQualificationRecord -Path $lockPath
    $lockState = 'missing'
    $preserved = $null
    if ($lock.status -ceq 'ok') {
        $lockState = 'ok'
        $preserved = Read-QualificationExactBytes $lockPath
    } elseif ($lock.status -cne 'missing') { $lockState = 'unreadable' }

    $supervisorObs = & $Observe $identity.record
    if ($supervisorObs.status -cin @('reused', 'unknown')) {
        return (New-QualificationStopResult 2 'refused' ('supervisor-' + $supervisorObs.status) $null 'not-attempted' $null $null)
    }
    $driverObs = [pscustomobject]@{ status = 'absent'; reason = 'not-recorded' }
    if ($driverRecord) {
        $driverObs = & $Observe $driverRecord
        if ($driverObs.status -cin @('reused', 'unknown')) {
            return (New-QualificationStopResult 2 'refused' ('driver-' + $driverObs.status) $null 'not-attempted' $null $null)
        }
    }

    # Sanctioned replacement order: the supervisor tree, then the recorded launch-child driver.
    $supervisorStep = [pscustomobject]@{ phase = 'supervisor'; record = $identity.record; status = [string]$supervisorObs.status }
    $driverStep = [pscustomobject]@{ phase = 'driver'; record = $driverRecord; status = [string]$driverObs.status }
    $kills = New-Object System.Collections.Generic.List[object]
    $failed = $false
    foreach ($step in @($supervisorStep, $driverStep)) {
        if ($failed) { break }
        if (-not $step.record -or $step.status -cne 'alive') { continue }
        $again = & $Observe $step.record
        if ($again.status -cne 'alive') {
            if ($kills.Count -eq 0) {
                return (New-QualificationStopResult 2 'refused' ($step.phase + '-generation') $null 'not-attempted' $null $null)
            }
            $failed = $true
            break
        }
        $processId = [int]$step.record.pid
        $stopped = & $StopTree $processId
        $kills.Add([pscustomobject]@{
            phase = $step.phase
            pid = $processId
            ok = [bool]$stopped.ok
            reason = [string]$stopped.reason
            command = [string]$stopped.command
        })
        if (-not $stopped.ok) { $failed = $true }
    }

    $censusOk = -not $failed
    if ($censusOk) {
        $deadline = [datetime]::UtcNow.AddMilliseconds([Math]::Max(0, $CensusMilliseconds))
        do {
            $supervisorNow = & $Observe $identity.record
            $driverNow = [pscustomobject]@{ status = 'absent' }
            if ($driverRecord) { $driverNow = & $Observe $driverRecord }
            $pending = ($supervisorNow.status -cne 'absent') -or ($driverRecord -and $driverNow.status -cne 'absent')
            if (-not $pending) { break }
            if ($CensusMilliseconds -le 0 -or [datetime]::UtcNow -ge $deadline) { $censusOk = $false; break }
            Start-Sleep -Milliseconds 200
        } while ($true)
    }

    $exit = 3
    $outcome = 'incomplete'
    $lockAction = 'not-attempted'
    $retained = $null
    if ($censusOk) {
        $exit = 0
        $outcome = 'stopped'
        $lockAction = 'absent'
        if ($lockState -ceq 'missing') { }
        elseif ($lockState -cne 'ok') {
            $lockAction = 'retained-unreadable'
            $exit = 4
            $outcome = 'lock-retained'
        } elseif ([string]$lock.record.runId -cne $RunId) {
            $lockAction = 'retained-foreign'
            $exit = 4
            $outcome = 'lock-retained'
        } else {
            $ownerObs = & $Observe $lock.record
            if ($ownerObs.status -cne 'absent') {
                $lockAction = 'retained-' + [string]$ownerObs.status
                $exit = 4
                $outcome = 'lock-retained'
            } else {
                $current = Read-QualificationExactBytes $lockPath
                $reread = Read-RcQualificationRecord -Path $lockPath
                if ($reread.status -cne 'ok' -or [string]$reread.record.runId -cne $RunId -or -not (Test-QualificationSameBytes $preserved $current)) {
                    $lockAction = 'retained-changed'
                    $exit = 4
                    $outcome = 'lock-retained'
                } else {
                    if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) { $EvidenceRoot = [IO.Path]::Combine($taskFull, 'stop-evidence') }
                    $evidenceFull = [IO.Path]::GetFullPath($EvidenceRoot)
                    if (-not [IO.Directory]::Exists($evidenceFull)) {
                        $parent = [IO.Path]::GetDirectoryName($evidenceFull)
                        if ($parent -and [IO.Directory]::Exists($parent)) { [void][IO.Directory]::CreateDirectory($evidenceFull) }
                    }
                    $stamp = [datetime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ', [Globalization.CultureInfo]::InvariantCulture)
                    $dest = [IO.Path]::Combine($evidenceFull, 'native-run.lock.moved-' + $stamp)
                    if ([IO.File]::Exists($dest) -or -not (Test-QualificationSameVolume $coordFull $evidenceFull)) {
                        $lockAction = 'retained-volume'
                        $exit = 4
                        $outcome = 'lock-retained'
                    } else {
                        try {
                            [IO.File]::Move($lockPath, $dest)
                            $movedBytes = Read-QualificationExactBytes $dest
                            if ((Test-QualificationSameBytes $preserved $movedBytes) -and -not [IO.File]::Exists($lockPath)) {
                                $lockAction = 'moved'
                                $retained = $dest
                            } else {
                                $lockAction = 'retained-move-mismatch'
                                $exit = 4
                                $outcome = 'lock-retained'
                            }
                        } catch {
                            $lockAction = 'retained-move-failed'
                            $exit = 4
                            $outcome = 'lock-retained'
                        }
                    }
                }
            }
        }
    }

    $phases = @()
    foreach ($kill in $kills) { $phases += ($kill.phase + ':' + [string]$kill.pid) }
    $line = 'STOP reason="' + $Reason + '" outcome=' + $outcome + ' lock=' + $lockAction
    if ($phases.Count -gt 0) { $line = $line + ' killed=' + ($phases -join ',') }
    Write-QualificationSupervisorLine -Path $logPath -Text $line
    return (New-QualificationStopResult $exit $outcome $line $kills.ToArray() $lockAction $retained $logPath)
}
