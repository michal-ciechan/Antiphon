#requires -Version 7.0
# CARD-1545 S1b admission. All results are diagnostic; native/paid execution stays disabled.
. (Join-Path $PSScriptRoot 'nightly-common.ps1')
. (Join-Path $PSScriptRoot 'release-gate.ps1')
. (Join-Path $PSScriptRoot 'nightly-owned-process.ps1')
. (Join-Path $PSScriptRoot 'live-lane-outcome.ps1')

function New-LiveLaneChildStartInfo {
    param([hashtable]$Inherited, [hashtable]$Selected, [string]$WorkingDirectory)
    # Never merge an ambient task/provider environment into this child. Absence,
    # present-empty and present-valued are preserved by the real StartInfo builder.
    $environment = @{}
    foreach ($key in @('PATH','SystemRoot','WINDIR','TEMP','TMP','TMPDIR','DOTNET_ROOT')) {
        if ($Inherited.ContainsKey($key)) { $environment[$key] = $Inherited[$key] }
    }
    foreach ($key in @('CLAUDE_CONFIG_DIR','ANTIPHON_LIVE_LANE_CONTEXT','ANTIPHON_LIVE_CLAUDE_EXE',
        'CLAUDE_CODE_CHILD_SESSION','CLAUDE_CODE_SESSION_ID','CLAUDE_CODE_BRIDGE_SESSION_ID','CLAUDECODE')) {
        if ($Selected.ContainsKey($key)) { $environment[$key] = $Selected[$key] }
    }
    return New-NightlyChildStartInfo -FilePath 'fixture-only-no-executable' -ArgumentList @() `
        -WorkingDirectory $WorkingDirectory -Environment $environment
}

function Test-LiveLaneOwner {
    param($Saved, [string]$CoordinationRoot)
    $current = Read-NightlyLockRecord -LockPath (Get-ReleaseGateNativeLockPath $CoordinationRoot)
    if ($null -eq $current -or $null -eq $Saved) { return $false }
    return ([int]$current.pid -eq [int]$Saved.pid -and
        [string]$current.runId -ceq [string]$Saved.runId -and
        [string]$current.lane -ceq [string]$Saved.lane -and
        (Test-ReleaseGateSameInstant $current.processStartedAt $Saved.processStartedAt) -and
        (Test-NightlyProcessAlive -ProcessId ([int]$Saved.pid)))
}

function Test-LiveLaneContext {
    param($Context, $Expected, [string]$IssuedPath, [string]$ActualPath, [string]$CoordinationRoot)
    if ($null -eq $Context) { return 'lane-context-required' }
    # Each bound field has a separate comparison so a changed component is invalid.
    foreach ($field in @('schemaVersion','lane','runId','attempt','source','executable','profile','issuance')) {
        if ([string]$Context[$field] -cne [string]$Expected[$field]) { return 'context-mismatch' }
    }
    if (@($Context.uids).Count -ne @($Expected.uids).Count -or
        (ConvertTo-Json -InputObject @($Context.uids) -Compress) -cne
        (ConvertTo-Json -InputObject @($Expected.uids) -Compress)) { return 'context-mismatch' }
    if ($ActualPath -cne $IssuedPath) { return 'context-unissued' }
    foreach ($field in @('pid','runId','lane')) {
        if ([string]$Context.owner[$field] -cne [string]$Expected.owner[$field]) { return 'context-stale' }
    }
    if (-not (Test-ReleaseGateSameInstant $Context.owner.processStartedAt $Expected.owner.processStartedAt) -or
        -not (Test-LiveLaneOwner $Expected.owner $CoordinationRoot)) { return 'context-stale' }
    return ''
}

function Join-LiveLaneChildren {
    param($Child, [hashtable]$Boundary, [bool]$Stop)
    $ok = $true
    if ($Stop) {
        foreach ($id in $Child.Children) { try { & $Boundary.Stop $id } catch { $ok = $false } }
    }
    foreach ($id in $Child.Children) {
        try { if (-not (& $Boundary.Join $id 30)) { $ok = $false } } catch { $ok = $false }
    }
    return $ok
}

function Invoke-LiveLaneRun {
    [CmdletBinding()]
    param([hashtable]$Request, [hashtable]$Boundary = @{})
    $result = [pscustomobject]@{
        ExitCode = 20; Reason = 'admission-incomplete'; DiagnosticOnly = $true; GateCredit = $false
        TerminalComplete = $false; RawChildExit = $null; CheckpointRunId = $null
    }
    # This slice has no production native executor. Fake opt-in alone is insufficient:
    # only this inert roster/opaque executable/profile and explicit I/O adapters exist.
    if ($Request.Mode -cne 'Fixture') { $result.Reason = 'native-unqualified'; return $result }
    if ($Request.Lane -cne 'fixture-live' -or $Request.Executable -cne 'owned-fixture-executable' -or
        $Request.Profile -cne 'owned-fixture-profile' -or @($Request.Uids).Count -eq 0 -or
        @($Request.Uids | Where-Object { $_ -cnotin @('fixture.A.0','fixture.B.0') }).Count -gt 0) {
        $result.Reason = 'native-unqualified'; return $result
    }
    foreach ($name in @('Candidate','Clock','Slot','WriteContext','Start','Wait','ReadTerminal','Stop','Join','WriteReceipt')) {
        if ($Boundary[$name] -isnot [scriptblock]) { $result.Reason = 'fixture-boundary-required'; return $result }
    }
    if ([string]::IsNullOrWhiteSpace($Request.Commission)) { $result.Reason = 'commission-required'; return $result }
    if ($Request.BudgetSeconds -le 0) { $result.Reason = 'budget-exhausted'; return $result }
    if ($Request.Source -cnotmatch '^[0-9a-f]{40}$' -or [string]::IsNullOrWhiteSpace($Request.RunId) -or
        $Request.Attempt -lt 1 -or [string]::IsNullOrWhiteSpace($Request.CoordinationRoot) -or
        [string]::IsNullOrWhiteSpace($Request.ResultsRoot)) {
        $result.ExitCode = 2; $result.Reason = 'request-invalid'; return $result
    }
    $owned = $false; $child = $null; $terminal = $false; $joined = $false; $context = $null; $cleanupAttempted = $false
    try {
        $candidate = & $Boundary.Candidate $Request
        if ($candidate.Sha -cne $Request.Source -or $candidate.Clean -isnot [bool] -or -not $candidate.Clean) {
            $result.ExitCode = 2; $result.Reason = 'candidate-invalid'; return $result
        }
        # Atomic shared native lock always precedes slot or checkpoint admission.
        $lock = Enter-ReleaseGateNativeLock -CoordinationRoot $Request.CoordinationRoot -RunId $Request.RunId -Lane $Request.Lane
        if (-not $lock.Ok) {
            $result.ExitCode = $(if ($lock.Reason -eq 'deferred-busy') { 21 } else { 2 })
            $result.Reason = $lock.Reason; return $result
        }
        $owned = $true
        if ($Boundary.Trace) { & $Boundary.Trace 'lock-acquired' }
        $started = [double](& $Boundary.Clock)
        $deadline = $started + [double]$Request.BudgetSeconds
        if (-not (& $Boundary.Slot $Request)) { $result.ExitCode = 4; $result.Reason = 'slot-timeout'; return $result }
        # Results root is created exclusively, never reused to resume an old run.
        if (Test-Path -LiteralPath $Request.ResultsRoot) { $result.ExitCode = 2; $result.Reason = 'results-exist'; return $result }
        [void][IO.Directory]::CreateDirectory($Request.ResultsRoot)
        if (-not $IsWindows) { [IO.File]::SetUnixFileMode($Request.ResultsRoot, [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite -bor [IO.UnixFileMode]::UserExecute) }
        $issuedPath = Join-Path $Request.ResultsRoot 'lane-context.json'
        $expected = [ordered]@{
            schemaVersion = 1; lane = $Request.Lane; runId = $Request.RunId; attempt = $Request.Attempt
            source = $Request.Source; uids = @($Request.Uids); executable = $Request.Executable; profile = $Request.Profile
            owner = [ordered]@{ pid = $lock.Record.pid; processStartedAt = $lock.Record.processStartedAt; runId = $lock.Record.runId; lane = $lock.Record.lane }
            issuance = [guid]::NewGuid().ToString('N')
        }
        $actualPath = & $Boundary.WriteContext $issuedPath ($expected | ConvertTo-Json -Depth 12 -Compress)
        if ([string]::IsNullOrWhiteSpace($actualPath) -or -not (Test-Path -LiteralPath $actualPath -PathType Leaf)) {
            $result.Reason = 'lane-context-required'; return $result
        }
        $context = [IO.File]::ReadAllText($actualPath) | ConvertFrom-Json -AsHashtable
        $invalid = Test-LiveLaneContext $context $expected $issuedPath $actualPath $Request.CoordinationRoot
        if ($invalid) { $result.ExitCode = 2; $result.Reason = $invalid; return $result }
        $manifest = Join-Path $Request.ResultsRoot 'fixture-checkpoints.yaml'
        # An inert, deliberately non-executable synthetic spec. Only the injected
        # fixture adapter sees it. Native manifest generation belongs to S1c.
        [IO.File]::WriteAllText($manifest, "schemaVersion: -1545`nformat: admitted-fixture-only`n")
        $selected = @{
            CLAUDE_CONFIG_DIR = $Request.Profile; ANTIPHON_LIVE_CLAUDE_EXE = $Request.Executable
            ANTIPHON_LIVE_LANE_CONTEXT = $issuedPath
        }
        $startInfo = New-LiveLaneChildStartInfo -Inherited $Request.Inherited -Selected $selected -WorkingDirectory $Request.ResultsRoot
        # Recheck current ownership immediately before the adapter handoff.
        if (-not (Test-LiveLaneOwner $expected.owner $Request.CoordinationRoot)) {
            $result.ExitCode = 2; $result.Reason = 'context-stale'; return $result
        }
        if ([double](& $Boundary.Clock) -ge $deadline) { $result.Reason = 'deadline'; return $result }
        $child = & $Boundary.Start $context $startInfo $manifest
        if ([string]::IsNullOrWhiteSpace($child.RunId) -or @($child.Children).Count -eq 0) { throw 'invalid owned adapter handle' }
        $result.CheckpointRunId = $child.RunId
        while ($true) {
            if ([double](& $Boundary.Clock) -ge $deadline) { $result.Reason = 'deadline'; break }
            $code = [int](& $Boundary.Wait $child.RunId)
            $result.RawChildExit = $code
            if ($code -eq 75) { continue }
            $terminal = $true
            $evidence = & $Boundary.ReadTerminal $child.RunId
            if ($evidence.RunId -cne $child.RunId -or $evidence.Complete -ne $true) { throw 'incomplete terminal evidence' }
            $result.Reason = 'outcomes-unqualified'
            break
        }
        # Stop every still-running owned child before joining any. No foreign PID
        # is ever inferred from a lock file or ambient process listing.
        $cleanupAttempted = $true
        $joined = Join-LiveLaneChildren $child $Boundary (-not $terminal)
        if (-not $joined) { $result.Reason = 'cleanup-incomplete'; return $result }
        $result.TerminalComplete = $terminal
        & $Boundary.WriteReceipt (Join-Path $Request.ResultsRoot 'diagnostic.json') $result
    } catch {
        # Never serialize arbitrary exceptions from a provider, path or environment.
        $result.ExitCode = 20; $result.Reason = 'handoff-incomplete'; $result.TerminalComplete = $false
    } finally {
        if ($null -ne $child -and -not $cleanupAttempted) {
            $joined = Join-LiveLaneChildren $child $Boundary $true
            if (-not $joined) { $result.ExitCode = 20; $result.Reason = 'cleanup-incomplete'; $result.TerminalComplete = $false }
        }
        if ($owned -and ($null -eq $child -or $joined)) {
            if ($Boundary.BeforeRelease) { & $Boundary.BeforeRelease $Request }
            # Existing release helper is additionally guarded by PID/start/run/lane.
            # It cannot remove a lock replaced during teardown.
            if (Test-LiveLaneOwner $lock.Record $Request.CoordinationRoot) {
                Exit-ReleaseGateNativeLock -CoordinationRoot $Request.CoordinationRoot -RunId $Request.RunId
                if ($Boundary.Trace) { & $Boundary.Trace 'release' }
            }
        }
    }
    return $result
}
