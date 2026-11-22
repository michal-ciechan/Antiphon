#requires -Version 5.1
# W2-nightly S17: the nightly tests run for the C487 harness drivers (W2-rc), in this process on their seams. ASCII-only.
#
# scripts/nightly-tests.ps1 and lib/nightly-tests-impl.ps1 are deleted. The run is Antiphon.Nightly.NightlyTests.Run, and
# its command (antiphon-scripts nightly-tests) takes no seams (plan 2026-11-21-w2-nightly-port-slices.md, section 2, rule
# 2). A driver that ran the entry on a seams file, and a run core whose tests hop the selection hop started as a real
# child on the forwarded seams, run NightlyTests.Run here instead, on a NightlyHost built from the same seams (rule 3,
# the in-process hop):
#   UtcNow -> the clock; Git -> git; StartProcess, WaitCleanup, NativeActiveCount -> the launcher (without StartProcess
#   the native owner, its children prepared as Set-AntiphonBuildDriverEnvironment prepares them); TracePath -> the trace;
#   HostPlatform, DockerInfo, DiskFree, ToolchainVersions -> the probe (under a StartProcess seam without DockerInfo the
#   Docker row is the one nightly-tests-impl.ps1 recorded: 'live docker skipped under seams-default'); E2EPrerequisites ->
#   the E2E observation; and the keys only these runs read: ClassCostPath -> the class-cost file, PublicationScope and
#   ExecutedContext -> the journal's publication scope (a commit fault) and the bound writer's observer.
# As Get-NightlyRcRunEvidence did, the evidence recorder is suppressed under the process or git seams unless
# ANTIPHON_RC_RUN_EVIDENCE is 'record'; the active-chunk identity is the heartbeat's (QualificationHeartbeat). Evidence
# writes are deferred for the run, as the entry script and the command defer them.
#
# Needs nightly-common.ps1 and the compiled core loaded. No load guard: the seams' hop branch dot-sources this file
# inside a run core's StartProcess call, in a child that never loaded the harness.

# The host of a run on these seams (a hashtable of the seams file's entries; $null: none). OutWriter and ErrorWriter default to this
# process's streams.
function New-C487NightlyTestsHost {
    param($Seams, [IO.TextWriter]$OutWriter = $null, [IO.TextWriter]$ErrorWriter = $null)
    if ($null -eq $OutWriter) { $OutWriter = [Console]::Out }
    if ($null -eq $ErrorWriter) { $ErrorWriter = [Console]::Error }
    $clock = $null
    $trace = $null
    $git = $null
    $launcher = $null
    $platform = $null; $docker = $null; $disk = $null; $tool = $null
    if ($Seams) {
        if ($Seams.UtcNow) {
            $now = $Seams.UtcNow
            $clock = [Antiphon.Nightly.DelegateTimeProvider]::new([Func[datetime]]({
                $value = @($now.Invoke())
                if ($value.Count -gt 0) { return [datetime]$value[0] }
                return [datetime]::UtcNow
            }.GetNewClosure()))
        }
        if ($Seams.TracePath) {
            $trace = [Antiphon.Nightly.AppendingTraceWriter]::new($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath([string]$Seams.TracePath))
        }
        if ($Seams.Git) {
            $gitSeam = $Seams.Git
            # Invoke-NightlyGit over the Git seam: a record's ExitCode and Output, else the answer as the output of exit 0.
            $git = [Antiphon.Nightly.NightlyGit]({
                param($WorkingDirectory, $Arguments)
                $answer = @($gitSeam.Invoke($WorkingDirectory, [string[]]@($Arguments)))
                $record = $null
                if ($answer.Count -gt 0) { $record = $answer[0] }
                if ($record -is [Collections.IDictionary] -or ($null -ne $record -and $null -ne $record.PSObject.Properties['ExitCode'])) {
                    return [Antiphon.Nightly.NightlyGitResult]::new([int]$record.ExitCode, [string]$record.Output)
                }
                return [Antiphon.Nightly.NightlyGitResult]::new(0, [string]$record)
            }.GetNewClosure())
        }
        if ($Seams.StartProcess) {
            $startSeam = $Seams.StartProcess
            $start = [Func[Antiphon.Nightly.NightlyLaunch, object]]({
                param($Launch)
                # A child reads the disk: queued evidence writes land first.
                if ([Antiphon.Nightly.EvidenceFiles]::Buffered) {
                    foreach ($failure in @([Antiphon.Nightly.EvidenceFiles]::Flush())) { [Console]::Error.WriteLine('WARNING: deferred-evidence-write-failed: ' + $failure) }
                }
                $seamEnvironment = $null
                if ($null -ne $Launch.Environment) {
                    $seamEnvironment = @{}
                    foreach ($pair in $Launch.Environment.GetEnumerator()) { $seamEnvironment[$pair.Key] = $pair.Value }
                }
                return $startSeam.Invoke($Launch.FileName, [string[]]@($Launch.Arguments), $Launch.WorkingDirectory, $Launch.TimeoutMilliseconds, $seamEnvironment)
            }.GetNewClosure())
            $wait = $null
            if ($Seams.WaitCleanup) {
                $waitSeam = $Seams.WaitCleanup
                $wait = [Func[object, bool]]({ param($Record) [bool](@($waitSeam.Invoke($Record))[0]) }.GetNewClosure())
            }
            $active = $null
            if ($Seams.NativeActiveCount) {
                $activeSeam = $Seams.NativeActiveCount
                $active = [Func[int]]({ [int](@($activeSeam.Invoke())[0]) }.GetNewClosure())
            }
            $launcher = [Antiphon.Nightly.DelegateLauncher]::new($start, $wait, $active, [Console]::Error)
            # nightly-tests-impl.ps1's Docker row under the process seam when no DockerInfo seam answers.
            $docker = [Func[Antiphon.Nightly.NightlyProbeRow]]{
                $row = [Antiphon.Nightly.NightlyProbeRow]::new()
                $row.ExitCode = 0; $row.Version = 'test'; $row.Detail = 'live docker skipped under seams-default'
                return $row
            }
        }
        if ($Seams.HostPlatform) {
            $platformSeam = $Seams.HostPlatform
            $platform = [Func[string]]({ [string](@($platformSeam.Invoke())[0]) }.GetNewClosure())
        }
        if ($Seams.DockerInfo) {
            $dockerSeam = $Seams.DockerInfo
            $docker = [Func[Antiphon.Nightly.NightlyProbeRow]]({
                $answer = @($dockerSeam.Invoke())
                $row = [Antiphon.Nightly.NightlyProbeRow]::new()
                if ($answer.Count -eq 0 -or $null -eq $answer[0]) { $row.ExitCode = 1; return $row }
                $row.ExitCode = [int]$answer[0].exitCode; $row.Version = [string]$answer[0].version; $row.Detail = [string]$answer[0].detail
                return $row
            }.GetNewClosure())
        }
        if ($Seams.DiskFree) {
            $diskSeam = $Seams.DiskFree
            $disk = [Func[Antiphon.Nightly.NightlyProbeRow]]({
                $answer = @($diskSeam.Invoke())
                $row = [Antiphon.Nightly.NightlyProbeRow]::new()
                if ($answer.Count -eq 0 -or $null -eq $answer[0]) { $row.ExitCode = 1; return $row }
                $row.ExitCode = [int]$answer[0].exitCode
                if ($null -ne $answer[0].freeBytes) { $row.FreeBytes = [long]$answer[0].freeBytes }
                if ($null -ne $answer[0].minimumFreeBytes) { $row.MinimumFreeBytes = [long]$answer[0].minimumFreeBytes }
                return $row
            }.GetNewClosure())
        }
        if ($Seams.ToolchainVersions) {
            $toolSeam = $Seams.ToolchainVersions
            $tool = [Func[string, string]]({ param([string]$Tool) [string](& $toolSeam $Tool) }.GetNewClosure())
        }
    }
    if ($null -eq $launcher) {
        $driverScript = Join-Path $PSScriptRoot 'build-driver-environment.ps1'
        $prepare = [Action[Diagnostics.ProcessStartInfo]]({ param($StartInfo) . $driverScript; Set-AntiphonBuildDriverEnvironment -StartInfo $StartInfo }.GetNewClosure())
        $sync = [Action]{
            if ([Antiphon.Nightly.EvidenceFiles]::Buffered) {
                foreach ($failure in @([Antiphon.Nightly.EvidenceFiles]::Flush())) { [Console]::Error.WriteLine('WARNING: deferred-evidence-write-failed: ' + $failure) }
            }
        }
        $launcher = [Antiphon.Nightly.OwnedLauncher]::new($sync, $prepare)
    }
    $probe = [Antiphon.Nightly.DelegateHostProbe]::new($platform, $docker, $disk, $tool, $null)
    return [Antiphon.Nightly.NightlyHost]::Create($clock, $launcher, $OutWriter, $ErrorWriter, $trace, $git, $null, $probe)
}

# The bindings of a run on these seams. ClassCostPath blank: the seams' ClassCostPath, else DefaultClassCostPath.
function New-C487NightlyTestsBindings {
    param($Seams, $NightlyHost, [string]$ClassCostPath = '', [string]$DefaultClassCostPath = '')
    $seamed = [bool]($Seams -and ($null -ne $Seams.StartProcess -or $null -ne $Seams.Git))
    $output = $NightlyHost.Out
    $clockHost = $NightlyHost
    $line = [Action[string]]({ param([string]$Text) $output.WriteLine('[' + $clockHost.UtcNow.ToString('o') + '] ' + $Text) }.GetNewClosure())
    $bindings = [Antiphon.Nightly.NightlyTestsBindings]::new()
    $bindings.RcEvidence = [Antiphon.Nightly.RcRunEvidenceRecorder]::new([string]$env:ANTIPHON_RC_RUN_EVIDENCE, $seamed, $false, $line)
    if ($Seams -and $Seams.E2EPrerequisites) {
        $e2eSeam = $Seams.E2EPrerequisites
        $bindings.E2EPrerequisites = [Func[string, string, Collections.IDictionary, string, object]]({ param($Root, $Log, $Environment, $Bundle)
            @($e2eSeam.Invoke($Root, $Log, $Environment, $Bundle))[0] }.GetNewClosure())
    }
    $errorWriter = $NightlyHost.Error
    $bindings.ActiveChunk = [Antiphon.Nightly.QualificationHeartbeat]::ActiveChunkWriter([Action[string]]({ param([string]$Text) $errorWriter.WriteLine('WARNING: ' + $Text) }.GetNewClosure()))
    # A driver's journal seams (only these runs read them): PublicationScope answers the scope of a timing root at each use
    # (its commit barrier and fault), ExecutedContext observes the bound writer context once it is open.
    if ($Seams -and $Seams.PublicationScope) {
        $scopeSeam = $Seams.PublicationScope
        $bindings.PublicationScope = [Func[string, Antiphon.Nightly.TestPhasePublicationScope]]({ param([string]$TimingRoot) @($scopeSeam.Invoke($TimingRoot))[0] }.GetNewClosure())
    }
    if ($Seams -and $Seams.ExecutedContext) {
        $contextSeam = $Seams.ExecutedContext
        $bindings.ExecutedContext = [Action[Antiphon.Nightly.TestPhaseJournalContext]]({ param($Context) $null = $contextSeam.Invoke($Context) }.GetNewClosure())
    }
    $cost = $ClassCostPath
    if ([string]::IsNullOrWhiteSpace($cost) -and $Seams -and $Seams.ClassCostPath) { $cost = [string]$Seams.ClassCostPath }
    if ([string]::IsNullOrWhiteSpace($cost)) { $cost = $DefaultClassCostPath }
    if ([string]::IsNullOrWhiteSpace($cost)) { $cost = Join-Path $PSScriptRoot 'nightly-class-costs.json' }
    $bindings.ClassCostPath = $cost
    return $bindings
}

# NightlyTests.Run with write-behind evidence for the run (unless ANTIPHON_NIGHTLY_DEFERRED_WRITES is off or this process
# already defers its writes).
function Invoke-C487NightlyTestsRun {
    param($Options, $NightlyHost, $Bindings)
    $defer = (-not [Antiphon.Nightly.EvidenceFiles]::Buffered) -and ([string]$env:ANTIPHON_NIGHTLY_DEFERRED_WRITES -ine 'off')
    if ($defer) { [Antiphon.Nightly.EvidenceFiles]::EnableWriteBehind() }
    try { return [Antiphon.Nightly.NightlyTests]::Run($Options, $NightlyHost, $Bindings) }
    finally {
        if ($defer) {
            foreach ($failure in @([Antiphon.Nightly.EvidenceFiles]::DisableWriteBehind())) { $NightlyHost.Error.WriteLine('WARNING: deferred-evidence-write-failed: ' + $failure) }
        }
    }
}

# The nightly tests run of a hashtable of nightly-tests.ps1's parameters (Invoke-AntiphonNightlyTests' splat), on the
# seams file in its SeamsPath (imported as Invoke-AntiphonNightlyTests imported it). Returns the NightlyTestsResult; its
# ProcessExitCode is the exit the entry script had. The class-cost file is this checkout's unless the seams name one.
function Invoke-C487NightlyTests {
    param([hashtable]$Parameters, [string]$ClassCostPath = '')
    $seamsPath = ''
    if ($Parameters.ContainsKey('SeamsPath')) { $seamsPath = [string]$Parameters['SeamsPath'] }
    Import-NightlySeams -SeamsPath $seamsPath
    $seams = $script:NightlySeams
    $options = [Antiphon.Nightly.NightlyTestsOptions]::new()
    foreach ($key in @($Parameters.Keys)) {
        if ($key -in @('SeamsPath', 'PassThru')) { continue }
        $value = $Parameters[$key]
        if ($key -eq 'Suites') { $options.Suites = [string[]]@($value | ForEach-Object { [string]$_ }); continue }
        if ($key -in @('AcceptCrossCandidateSeed', 'AllowSharedTree', 'WhatIf')) { $options.$key = [bool]$value; continue }
        if ($key -eq 'Attempt') { $options.Attempt = [int]$value; continue }
        $options.$key = [string]$value
    }
    $nightlyHost = New-C487NightlyTestsHost -Seams $seams
    $bindings = New-C487NightlyTestsBindings -Seams $seams -NightlyHost $nightlyHost -ClassCostPath $ClassCostPath
    return (Invoke-C487NightlyTestsRun $options $nightlyHost $bindings)
}

# The in-process tests hop for a StartProcess seam answering FilePath antiphon-scripts with ArgumentList 'nightly-tests'
# and its --kebab flags: the command's run on the same seams in this process. The class-cost file is the checkout's
# (WorkingDirectory) scripts/lib/nightly-class-costs.json, as the hop ran the checkout's entry, unless the seams name one.
# LogPath: the run's output and error lines go to that file instead of this process's streams. Returns the StartProcess
# record (ExitCode is the command's exit: 2 on a usage error, 1 when the run threw) with the run's Result.
function Invoke-C487NightlyTestsHop {
    param($Seams, [string[]]$ArgumentList, [string]$WorkingDirectory, [string]$LogPath = '')
    $writer = $null
    $out = $null
    $err = $null
    if (-not [string]::IsNullOrWhiteSpace($LogPath)) {
        $writer = [IO.StreamWriter]::new($LogPath, $true, [Text.UTF8Encoding]::new($false))
        $writer.AutoFlush = $true
        $out = [IO.TextWriter]::Synchronized($writer)
        $err = $out
    }
    try {
        $flags = @($ArgumentList)
        if ($flags.Count -gt 0 -and [string]$flags[0] -ceq 'nightly-tests') { $flags = @($flags | Select-Object -Skip 1) }
        try { $options = [Antiphon.Nightly.NightlyTestsOptions]::Parse([string[]]$flags) }
        catch {
            [Console]::Error.WriteLine('usage: antiphon-scripts ' + (Get-NightlyRefusalText $_))
            return @{ ExitCode = 2; TimedOut = $false; Pid = $PID; ChildrenExited = $true; Result = $null }
        }
        $default = ''
        if (-not [string]::IsNullOrWhiteSpace($WorkingDirectory)) {
            $candidate = Join-Path $WorkingDirectory (Join-Path 'scripts' (Join-Path 'lib' 'nightly-class-costs.json'))
            if (Test-Path -LiteralPath $candidate -PathType Leaf) { $default = $candidate }
        }
        $nightlyHost = New-C487NightlyTestsHost -Seams $Seams -OutWriter $out -ErrorWriter $err
        $bindings = New-C487NightlyTestsBindings -Seams $Seams -NightlyHost $nightlyHost -DefaultClassCostPath $default
        try { $result = Invoke-C487NightlyTestsRun $options $nightlyHost $bindings }
        catch {
            $nightlyHost.Error.WriteLine('nightly-tests: ' + (Get-NightlyRefusalText $_))
            return @{ ExitCode = 1; TimedOut = $false; Pid = $PID; ChildrenExited = $true; Result = $null }
        }
        return @{ ExitCode = [int]$result.ProcessExitCode; TimedOut = $false; Pid = $PID; ChildrenExited = $true; Result = $result }
    } finally {
        if ($null -ne $writer) { $writer.Dispose() }
    }
}

# Invoke-NightlyExactRoster's parameters over NightlyTests.ExactRoster: each roster chunk starts the test host as the
# entry did (CheckpointProcessRunner, 600 s, its children prepared by Set-AntiphonBuildDriverEnvironment), its console in
# the chunk's console.log. Returns the receipt; a refusal throws its reason.
function Invoke-C487NightlyExactRoster {
    param([string]$AssemblyPath, $Discovery, [object[]]$Members, [string]$ResultsDirectory,
          [string[]]$ClassifiedUids = $null, [hashtable]$Environment = $null)
    . (Join-Path $PSScriptRoot 'checkpoint-process-runner.ps1')
    $driverScript = Join-Path $PSScriptRoot 'build-driver-environment.ps1'
    $start = [Func[Antiphon.Nightly.NightlyLaunch, object]]({
        param($Launch)
        . $driverScript
        $info = [Diagnostics.ProcessStartInfo]::new([string]$Launch.FileName)
        if (-not [string]::IsNullOrWhiteSpace([string]$Launch.WorkingDirectory)) { $info.WorkingDirectory = [string]$Launch.WorkingDirectory }
        foreach ($argument in @($Launch.Arguments)) { $info.ArgumentList.Add([string]$argument) }
        if ($null -ne $Launch.Environment) {
            foreach ($pair in $Launch.Environment.GetEnumerator()) { $info.Environment[[string]$pair.Key] = [string]$pair.Value }
        }
        Set-AntiphonBuildDriverEnvironment -StartInfo $info
        $run = [Antiphon.Checkpoints.CheckpointProcessRunner]::Run($info, [int]$Launch.TimeoutMilliseconds, $false)
        return @{ ExitCode = [int]$run.Exit; TimedOut = [bool]$run.TimedOut; Pid = 0; ChildrenExited = $true; LogText = ([string]$run.Stdout + [string]$run.Stderr) }
    }.GetNewClosure())
    $launcher = [Antiphon.Nightly.DelegateLauncher]::new($start, $null, $null, [Console]::Error)
    $nightlyHost = [Antiphon.Nightly.NightlyHost]::Create($null, $launcher, [Console]::Out, [Console]::Error, $null, $null, $null, $null)
    try { return [Antiphon.Nightly.NightlyTests]::ExactRoster($nightlyHost, $AssemblyPath, $Discovery, $Members, $ResultsDirectory, $ClassifiedUids, $Environment) }
    catch { throw (Get-NightlyRefusalText $_) }
}
