#requires -Version 7.0
# Local Linux diagnostic probes only. Not a session/process custodian.

function Get-NightlyProbeProcIdentity {
    param([int]$ProcessId)
    try {
        $stat = [IO.File]::ReadAllText("/proc/$ProcessId/stat")
        $fields = $stat.Substring($stat.LastIndexOf(')') + 2).Split(' ')
        return [pscustomobject]@{ Pid = $ProcessId; ParentPid = [int]$fields[1]; Start = $fields[19] }
    } catch [IO.IOException] { return $null }
}

function Test-NightlyProbeIdentityAlive {
    param($Identity)
    $current = Get-NightlyProbeProcIdentity -ProcessId $Identity.Pid
    return ($null -ne $current -and $current.Start -ceq $Identity.Start -and -not $Identity.Process.HasExited)
}

function Update-NightlyProbeDescendants {
    param($Root, [System.Collections.Generic.List[object]]$Descendants)
    $known = @{}
    foreach ($identity in @($Root) + $Descendants.ToArray()) {
        if (Test-NightlyProbeIdentityAlive $identity) { $known[$identity.Pid] = $identity.Start }
    }
    $inventory = @(foreach ($directory in [IO.Directory]::EnumerateDirectories('/proc')) {
        $number = 0
        if ([int]::TryParse([IO.Path]::GetFileName($directory), [ref]$number)) {
            $identity = Get-NightlyProbeProcIdentity -ProcessId $number
            if ($null -ne $identity) { $identity }
        }
    })
    do {
        $added = $false
        foreach ($identity in $inventory) {
            if ($known.ContainsKey($identity.Pid) -or -not $known.ContainsKey($identity.ParentPid)) { continue }
            $parent = Get-NightlyProbeProcIdentity -ProcessId $identity.ParentPid
            if ($null -eq $parent -or $parent.Start -cne $known[$identity.ParentPid]) { continue }
            $process = $null
            try {
                $process = [Diagnostics.Process]::GetProcessById($identity.Pid)
                $startTicks = $process.StartTime.ToUniversalTime().Ticks
                $current = Get-NightlyProbeProcIdentity -ProcessId $identity.Pid
                if ($null -eq $current -or $current.Start -cne $identity.Start) { $process.Dispose(); continue }
                $identity | Add-Member Process $process
                $identity | Add-Member StartTimeUtcTicks $startTicks
                $Descendants.Add($identity)
                $known[$identity.Pid] = $identity.Start
                $added = $true
            } catch {
                if ($null -ne $process) { $process.Dispose() }
            }
        }
    } while ($added)
}

function Invoke-NightlyProbeSupervisor {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$ArgumentList = @(),
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [scriptblock]$ElapsedMillisecondsProvider
    )
    if (-not $IsLinux) { throw 'Nightly probe supervision requires Linux /proc.' }
    $info = [Diagnostics.ProcessStartInfo]::new($FilePath)
    $info.WorkingDirectory = $WorkingDirectory
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($argument in $ArgumentList) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    $descendants = [System.Collections.Generic.List[object]]::new()
    $errors = [System.Collections.Generic.List[string]]::new()
    $root = $null
    $stdout = $null
    $stderr = $null
    $expired = $false
    $rootJoined = $false
    $joined = @()
    $started = $false
    $execution = [Diagnostics.Stopwatch]::new()
    $cleanup = [Diagnostics.Stopwatch]::new()
    try {
        if (-not $process.Start()) { throw 'Probe process did not start.' }
        $started = $true
        $execution.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $root = Get-NightlyProbeProcIdentity -ProcessId $process.Id
        if ($null -eq $root) { throw 'Probe root identity was unavailable.' }
        $root | Add-Member Process $process
        $root | Add-Member StartTimeUtcTicks $process.StartTime.ToUniversalTime().Ticks
        while ($true) {
            Update-NightlyProbeDescendants -Root $root -Descendants $descendants
            if ($process.HasExited -and $stdout.IsCompleted -and $stderr.IsCompleted) { break }
            $elapsed = if ($ElapsedMillisecondsProvider) {
                & $ElapsedMillisecondsProvider $root $descendants.ToArray()
            } else { $execution.ElapsedMilliseconds }
            if ($elapsed -ge 30000) { $expired = $true; break }
            Start-Sleep -Milliseconds 10
        }
    } catch {
        $errors.Add($_.Exception.Message)
    } finally {
        # One real allowance includes all kill calls, joins and both EOF drains.
        $cleanup.Start()
        if ($started) {
            # CARD-1375 CP-3 intentional negative control: suppress both kill paths.
            try { if (-not $process.HasExited) { } }
            catch { $errors.Add("root kill: $($_.Exception.Message)") }
            foreach ($identity in $descendants) {
                try { if (Test-NightlyProbeIdentityAlive $identity) { } }
                catch { $errors.Add("descendant kill: $($_.Exception.Message)") }
            }
            try { $rootJoined = $process.WaitForExit([int][Math]::Max(0, 5000 - $cleanup.ElapsedMilliseconds)) }
            catch { $errors.Add("root join: $($_.Exception.Message)") }
            foreach ($identity in $descendants) {
                $didJoin = $false
                try { $didJoin = $identity.Process.WaitForExit([int][Math]::Max(0, 5000 - $cleanup.ElapsedMilliseconds)) }
                catch { $errors.Add("descendant join: $($_.Exception.Message)") }
                $joined += [pscustomobject]@{
                    pid = $identity.Pid; start = $identity.Start
                    startTimeUtcTicks = $identity.StartTimeUtcTicks; joined = $didJoin
                }
            }
            foreach ($stream in @($stdout, $stderr)) {
                if ($null -ne $stream) {
                    try { $null = $stream.Wait([int][Math]::Max(0, 5000 - $cleanup.ElapsedMilliseconds)) }
                    catch { $errors.Add("stream drain: $($_.Exception.Message)") }
                }
            }
        }
        $cleanup.Stop()
    }
    $stdoutDrained = $null -ne $stdout -and $stdout.IsCompletedSuccessfully
    $stderrDrained = $null -ne $stderr -and $stderr.IsCompletedSuccessfully
    $receipt = [pscustomobject]@{
        filePath = $FilePath; arguments = $ArgumentList
        pid = $(if ($started) { $process.Id } else { $null })
        start = $(if ($root) { $root.Start } else { $null })
        startTimeUtcTicks = $(if ($root) { $root.StartTimeUtcTicks } else { $null })
        expired = $expired; executionElapsedMs = $execution.ElapsedMilliseconds
        cleanupElapsedMs = $cleanup.ElapsedMilliseconds; rootJoined = $rootJoined
        descendants = $joined; stdoutDrained = $stdoutDrained; stderrDrained = $stderrDrained
        cleanupSuccess = ($rootJoined -and @($joined | Where-Object { -not $_.joined }).Count -eq 0 -and
            $stdoutDrained -and $stderrDrained -and $cleanup.ElapsedMilliseconds -le 5000 -and $errors.Count -eq 0)
        exitCode = $(if ($rootJoined) { $process.ExitCode } else { $null })
        stdout = $(if ($stdoutDrained) { $stdout.Result } else { $null })
        stderr = $(if ($stderrDrained) { $stderr.Result } else { $null })
        errors = $errors.ToArray()
    }
    foreach ($identity in $descendants) { $identity.Process.Dispose() }
    $process.Dispose()
    return $receipt
}
