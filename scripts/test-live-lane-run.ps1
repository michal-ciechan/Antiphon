#requires -Version 7.0
# Offline CARD-1545 admission contracts. Boundaries never contact a provider.
param([string]$Case, [string]$ResultsDirectory)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/lib/nightly-common.ps1"
. "$PSScriptRoot/lib/nightly-policy.ps1"
. "$PSScriptRoot/lib/nightly-coverage.ps1"
. "$PSScriptRoot/lib/release-gate.ps1"
. "$PSScriptRoot/lib/c487-harness.ps1"
. "$PSScriptRoot/lib/live-lane-run.ps1"
$ResultsDirectory = New-C487Root $ResultsDirectory
$repo = Split-Path $PSScriptRoot -Parent
# Commission base, never repinned to make a candidate pass.
$base = 'f1b957c7fe5bf2474ea01256891e66c611a40fc8'
$method = $Case -replace '^C1545_', ''
function Check([string]$Id, [bool]$Ok) { Assert-C487 $Ok "C1545 $method/$Id" 'contract mismatch (synthetic inputs only)' }
function New-Fixture {
    $root = Join-Path $ResultsDirectory ([guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($root)
    $script:F = @{
        Root = $root; Trace = [Collections.Generic.List[string]]::new(); Tick = 0; Waits = 0; Starts = 0
        Fault = ''; Change = ''; JoinOk = $true; DenySlot = $false; Deadline = $false; Foreign = ''
        State = @{}; Admitted = $null; StartInfo = $null
    }
    foreach ($name in @('last-run.json','last-complete-green.json','last-monitor.json',
        'interim-qualification-receipt.json','rc-gate.json','rc-authority.json')) {
        $path = Join-Path $root $name
        [IO.File]::WriteAllText($path, 'seeded-state-' + $name)
        $F.State[$path] = [Convert]::ToBase64String([IO.File]::ReadAllBytes($path))
    }
    $request = @{
        Lane = 'fixture-live'; RunId = 'run-A'; Attempt = 1; Source = ('a' * 40)
        Uids = @('fixture.A.0'); Executable = 'owned-fixture-executable'; Profile = 'owned-fixture-profile'
        Commission = 'fixture-commission'; BudgetSeconds = 120; Mode = 'Fixture'; CandidateRoot = $root
        CoordinationRoot = (Join-Path $root 'coord'); ResultsRoot = (Join-Path $root 'diagnostic')
        Inherited = @{ PATH = 'fixture-path'; ANTHROPIC_API_KEY = 'synthetic-not-a-secret'; ANTIPHON_TASK_TOKEN = 'synthetic-task' }
    }
    $boundary = @{
        Candidate = { param($r) @{ Sha = $r.Source; Clean = $true } }
        Clock = { $F.Tick }
        Trace = { param($s) $F.Trace.Add($s) }
        Slot = { param($r)
            $lock = Read-NightlyLockRecord (Get-ReleaseGateNativeLockPath $r.CoordinationRoot)
            $F.Trace.Add($(if ($lock.runId -ceq $r.RunId) { 'slot:locked' } else { 'slot:unlocked' }))
            return (-not $F.DenySlot)
        }
        WriteContext = { param($path, $json)
            $F.Trace.Add('context-write')
            if ($F.Fault -eq 'context-write') { throw 'injected' }
            $c = $json | ConvertFrom-Json -AsHashtable
            switch ($F.Change) {
                'context-missing' { return '' }
                'context-lane' { $c.lane = 'foreign' }
                'context-uid' { $c.uids = @('foreign.0') }
                'context-source' { $c.source = ('b' * 40) }
                'context-executable' { $c.executable = 'foreign' }
                'context-profile' { $c.profile = 'foreign' }
                'context-tuple-pid' { $c.owner.pid++ }
                'context-tuple-runId' { $c.owner.runId = 'foreign' }
                'context-tuple-lane' { $c.owner.lane = 'foreign' }
                'context-tuple-start' { $c.owner.processStartedAt = '2000-01-01T00:00:00Z' }
                'context-owner' {
                    $p = Join-Path (Split-Path (Split-Path $path -Parent) -Parent) 'coord/native-run.lock'
                    $owner = Get-Content -LiteralPath $p -Raw | ConvertFrom-Json
                    $owner.runId = 'foreign'
                    $F.Foreign = $owner | ConvertTo-Json -Compress
                    [IO.File]::WriteAllText($p, $F.Foreign)
                }
                'context-forged' { $path += '.unissued' }
            }
            [IO.File]::WriteAllText($path, ($c | ConvertTo-Json -Depth 12 -Compress))
            return $path
        }
        Start = { param($context, $startInfo, $manifest)
            $F.Starts++; $F.Trace.Add('start:checkpoint-A'); $F.Admitted = $context; $F.StartInfo = $startInfo
            if ($F.Fault -eq 'launch-failure') { throw 'injected before creation' }
            if (-not (Test-Path -LiteralPath $manifest)) { throw 'no materialized manifest' }
            return @{ RunId = 'checkpoint-A'; Children = @('child-A','child-B') }
        }
        Wait = { param($id)
            $F.Waits++; $F.Trace.Add('wait:' + $id)
            if ($F.Fault -eq 'wait-failure') { throw 'injected' }
            $F.Tick += $(if ($F.Deadline) { 80 } else { 1 })
            if ($F.Waits -le 2 -or $F.Deadline) { return 75 }
            return 0
        }
        ReadTerminal = { param($id)
            $F.Trace.Add('terminal:' + $id)
            if ($F.Fault -eq 'terminal-read') { throw 'injected' }
            return @{
                SchemaVersion=1; RunId=$id; ChildExit=0; Complete=$true; Evidence='Complete'
                Lane='fixture-live'; Attempt=1; Source=('a'*40); Build='Succeeded'; Platform='Fixture'; Authority='diagnostic'
                InputBefore='input-A'; InputAfter='input-A'; ExecutableBefore='exe-A'; ExecutableAfter='exe-A'; Outcome='Passed'
                Rows=@(@{ Uid='fixture.A.0'; State='Passed'; Setup='Ready' })
            }
        }
        Stop = { param($child) $F.Trace.Add('stop:' + $child) }
        Join = { param($child, $seconds) $F.Trace.Add('join:' + $child); return $F.JoinOk }
        WriteReceipt = { param($path, $receipt)
            $F.Trace.Add('receipt')
            if ($F.Fault -eq 'receipt-write') { throw 'injected' }
            [IO.File]::WriteAllText($path, ($receipt | ConvertTo-Json -Depth 10))
        }
        BeforeRelease = { param($r)
            if ($F.Change -eq 'foreign-lock') {
                $p = Get-ReleaseGateNativeLockPath $r.CoordinationRoot
                $owner = Get-Content -LiteralPath $p -Raw | ConvertFrom-Json
                $owner.runId = 'foreign'; $owner.processStartedAt = '2000-01-01T00:00:00Z'
                $F.Foreign = $owner | ConvertTo-Json -Compress
                [IO.File]::WriteAllText($p, $F.Foreign)
            }
        }
    }
    return @{ Request = $request; Boundary = $boundary }
}
function Run-Fixture($fixture) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $result = & "$PSScriptRoot/run-live-lane.ps1" -Request $fixture.Request -Boundary $fixture.Boundary
    [IO.File]::WriteAllText((Join-Path $F.Root 'trace.json'), ([ordered]@{
        method=$method; source=(& git -C $repo rev-parse HEAD); platform=[Environment]::OSVersion.Platform.ToString()
        dirty=([string]::IsNullOrEmpty((& git -C $repo status --porcelain --untracked-files=no)) -eq $false)
        hostName=[Environment]::MachineName; category='script-orchestration'; wallSeconds=$timer.Elapsed.TotalSeconds
        dbSeconds=$null; httpSeconds=$null; nativeSeconds=$null; result=$result; trace=@($F.Trace)
    } | ConvertTo-Json -Depth 12))
    foreach ($p in $F.State.Keys) {
        if ([Convert]::ToBase64String([IO.File]::ReadAllBytes($p)) -cne $F.State[$p]) { throw 'legacy state changed' }
    }
    return $result
}
function No-Starts { return ($F.Starts -eq 0 -and @($F.Trace | Where-Object { $_ -like 'slot:*' }).Count -eq 0) }
function Lock-Exists($q) { return (Test-Path -LiteralPath (Get-ReleaseGateNativeLockPath $q.Request.CoordinationRoot)) }
function Test-LockBeforeSlot {
    $q = New-Fixture; $r = Run-Fixture $q
    Check 'ordered' ($F.Starts -eq 1 -and $F.Trace.Contains('slot:locked') -and $F.Trace.IndexOf('slot:locked') -lt $F.Trace.IndexOf('start:checkpoint-A'))
    $q = New-Fixture
    $owner = Enter-ReleaseGateNativeLock -CoordinationRoot $q.Request.CoordinationRoot -RunId foreign -Lane fixture-live
    $p = $owner.Path; $bytes = [IO.File]::ReadAllText($p)
    $r = Run-Fixture $q
    Check 'busy' ($r.ExitCode -eq 21 -and (No-Starts) -and [IO.File]::ReadAllText($p) -ceq $bytes)
    Exit-ReleaseGateNativeLock -CoordinationRoot $q.Request.CoordinationRoot -RunId foreign
    $q = New-Fixture; $F.DenySlot = $true; $r = Run-Fixture $q
    Check 'slot-timeout' ($r.ExitCode -eq 4 -and $F.Trace.Contains('slot:locked') -and $F.Starts -eq 0 -and -not (Lock-Exists $q))
    foreach ($id in @('commission','budget')) {
        $q = New-Fixture
        if ($id -eq 'commission') { $q.Request.Commission = '' } else { $q.Request.BudgetSeconds = 0 }
        $r = Run-Fixture $q
        Check $id ($r.ExitCode -eq 20 -and (No-Starts))
    }
    foreach ($id in @('candidate-sha','candidate-dirty')) {
        $q = New-Fixture
        if ($id -eq 'candidate-sha') { $q.Boundary.Candidate = { param($r) @{ Sha = ('b' * 40); Clean = $true } } }
        else { $q.Boundary.Candidate = { param($r) @{ Sha = $r.Source; Clean = $false } } }
        $r = Run-Fixture $q
        Check $id ($r.ExitCode -eq 2 -and (No-Starts))
    }
}
function Test-ProtectedInputs([string]$Overlay = '') {
    # All pre-existing executable/config inputs, including definitions and old tests,
    # are protected. New executable paths are enumerated individually.
    $allowed = @('scripts/run-live-lane.ps1','scripts/lib/live-lane-run.ps1','scripts/test-live-lane-run.ps1',
        'tests/live-lane-checkpoints/live-claude-core-windows.yaml','tests/Antiphon.Tests/Scripts/LiveLaneRunnerTests.cs',
        'scripts/lib/live-lane-outcome.ps1','scripts/test-live-lane-outcome.ps1',
        'tests/Antiphon.Tests/Scripts/LiveLaneOutcomeTests.cs')
    $existing = @(& git -C $repo ls-tree -r --name-only $base)
    if ($LASTEXITCODE -ne 0 -or $existing.Count -lt 100) { throw 'base tree unavailable' }
    $changed = @(& git -C $repo diff --name-only $base --)
    if ($LASTEXITCODE -ne 0) { throw 'candidate diff unavailable' }
    $changed += @(& git -C $repo ls-files --others --exclude-standard)
    foreach ($path in $changed) {
        if ($path -cmatch '^docs/superpowers/plans/[^/]+\.md$' -or $path -cmatch '^\.antiphon/task-[^/]+\.md$') { continue }
        if ($existing -contains $path -or $allowed -cnotcontains $path) { return $false }
    }
    if ($Overlay) {
        foreach ($path in @('scripts/lib/nightly-common.ps1','scripts/lib/nightly-policy.ps1','scripts/lib/nightly-coverage.ps1',
            'tests/test-execution-policy.json','scripts/fixtures/nightly/c487-probe/discovery.diag')) {
            $expected = & git -C $repo rev-parse "${base}:$path"
            $actual = & git -C $repo hash-object -- (Join-Path $Overlay $path)
            if ($LASTEXITCODE -ne 0 -or $actual -cne $expected) { return $false }
        }
    }
    return $true
}
function Read-Census($root) {
    # A fresh runspace prevents script-level import guards from making a pinned
    # reader silently resolve functions from the candidate (or the reverse).
    $reader = {
        param($root)
        . "$root/scripts/lib/nightly-common.ps1"
        . "$root/scripts/lib/nightly-policy.ps1"
        . "$root/scripts/lib/nightly-coverage.ps1"
        $policyRead = Read-NightlyExecutionPolicy -PolicyPath "$root/tests/test-execution-policy.json"
        $policy = $policyRead.Object
        $discovery = ConvertFrom-NightlyDiagnosticLog -Path "$root/scripts/fixtures/nightly/c487-probe/discovery.diag" -Kind discovery
        $nodes = @($discovery.Nodes)
        $nodes += @(
            @{ Uid='sentinel-c680'; ClassName='Antiphon.Tests.Agents.ClaudeDialogWatchdogCanaryTests'; Method='C680_LiveNudgeRefusesPermissionAndQuestionDialogs'; Categories=@('Integration') },
            @{ Uid='sentinel-ordinary'; ClassName='Antiphon.Tests.Ordinary'; Method='Plain'; Categories=@('Unit') },
            @{ Uid='sentinel-excluded'; ClassName='Antiphon.Tests.Agents.ClaudeAdapterIntegrationTests'; Method='RoundTrip'; Categories=@('Integration') }
        )
        $exclusions = @(Get-NightlyProfileExclusions -PolicyObject $policy -Profile rc -SuiteId antiphon -Platform linux)
        $census = Get-NightlyDiscoveryCensus -DiscoveryNodes $nodes -ProfileExclusions $exclusions -ProfileAware
        return [ordered]@{ Hash=$policyRead.Hash; Required=@($census.Required.uid | Sort-Object); Excluded=@($census.Excluded.uid | Sort-Object) } | ConvertTo-Json -Compress -Depth 8
    }
    $ps = [PowerShell]::Create()
    try {
        [void]$ps.AddScript($reader.ToString()).AddArgument($root)
        $output = $ps.Invoke()
        if ($ps.HadErrors) { throw 'isolated census reader failed' }
        return [string]$output[0]
    } finally { $ps.Dispose() }
}
function Test-WrapperOnlyPaidManifest {
    $q = New-Fixture; $r = Run-Fixture $q
    Check 'valid-context' ($F.Starts -eq 1 -and $F.Admitted.lane -ceq 'fixture-live' -and $F.Admitted.source -ceq ('a' * 40) -and $F.Admitted.uids[0] -ceq 'fixture.A.0' -and $F.Admitted.owner.runId -ceq 'run-A' -and $F.Admitted.attempt -eq 1 -and $r.TerminalComplete)
    foreach ($id in @('context-missing','context-lane','context-uid','context-source','context-executable','context-profile','context-owner','context-forged','context-tuple-pid','context-tuple-runId','context-tuple-lane','context-tuple-start')) {
        $q = New-Fixture; $q.Request.Inherited.ANTIPHON_HEADED_TESTS = '1'; $F.Change = $id; $r = Run-Fixture $q
        $code = 2; $reason = 'context-mismatch'
        if ($id -eq 'context-missing') { $code = 20; $reason = 'lane-context-required' }
        if ($id -eq 'context-owner' -or $id.StartsWith('context-tuple-')) { $reason = 'context-stale' }
        if ($id -eq 'context-forged') { $reason = 'context-unissued' }
        Check $id ($r.ExitCode -eq $code -and $r.Reason -ceq $reason -and $F.Starts -eq 0)
    }
    $q = New-Fixture; $q.Request.Mode = 'Native'; $r = Run-Fixture $q
    Check 'native-disabled' ($r.ExitCode -eq 20 -and $r.Reason -ceq 'native-unqualified' -and (No-Starts))
    $beforeRoot = Join-Path $ResultsDirectory 'pinned'
    $paths = @('scripts/lib/nightly-common.ps1','scripts/lib/nightly-policy.ps1','scripts/lib/nightly-coverage.ps1',
        'tests/test-execution-policy.json','scripts/fixtures/nightly/c487-probe/discovery.diag')
    foreach ($path in $paths) {
        $dest = Join-Path $beforeRoot $path
        [void][IO.Directory]::CreateDirectory((Split-Path $dest -Parent))
        $text = (& git -C $repo show "${base}:$path") -join "`n"
        if ($LASTEXITCODE -ne 0) { throw 'pinned object unavailable' }
        [IO.File]::WriteAllText($dest, $text + "`n")
    }
    $before = Read-Census $beforeRoot; $after = Read-Census $repo
    Check 'full-inputs' ((Test-ProtectedInputs) -and $before -ceq $after -and $before.Contains('sentinel-c680') -and $before.Contains('sentinel-excluded'))
    # Real candidate policy edit, with correctly recomputed policy hash.
    $policyPath = Join-Path $beforeRoot 'tests/test-execution-policy.json'
    $original = [IO.File]::ReadAllText($policyPath)
    $policy = $original | ConvertFrom-Json -AsHashtable
    $policy.profiles.rc.exclusions += @{ suite='antiphon'; class='Antiphon.Tests.Agents.ClaudeDialogWatchdogCanaryTests'; reason='fixture'; owner='CARD-1545' }
    $policy.policyHash = Get-NightlyPolicyHash $policy
    [IO.File]::WriteAllText($policyPath, ($policy | ConvertTo-Json -Depth 50))
    Check 'policy-edit' ((Read-Census $beforeRoot) -cne $after -and -not (Test-ProtectedInputs $beforeRoot))
    [IO.File]::WriteAllText($policyPath, $original)
    $reader = Join-Path $beforeRoot 'scripts/lib/nightly-coverage.ps1'
    [IO.File]::AppendAllText($reader, "`nfunction Get-NightlyDiscoveryDisposition { param(`$Node, `$ProfileExclusions, [switch]`$ProfileAware) return @{ Disposition = 'excluded'; Reason = 'fixture'; Owner = 'CARD-1545' } }`n")
    Check 'reader-edit' ((Read-Census $beforeRoot) -cne $after -and -not (Test-ProtectedInputs $beforeRoot))
    $q = New-Fixture; $r = Run-Fixture $q
    Check 'legacy-state' ($F.Starts -eq 1 -and $F.Trace.Contains('receipt'))
}
function Test-ChildEnvironmentIsScoped {
    $inherited = @{ PATH='fixture-path'; ANTHROPIC_API_KEY='synthetic'; ANTIPHON_TASK_TOKEN='synthetic'; CLAUDE_CONFIG_DIR='foreign'; HOME='foreign' }
    $selected = @{ CLAUDE_CONFIG_DIR='owned-profile'; ANTIPHON_LIVE_LANE_CONTEXT='owned-context'; ANTIPHON_LIVE_CLAUDE_EXE='owned-executable' }
    $start = New-LiveLaneChildStartInfo $inherited $selected $ResultsDirectory
    Check 'allowlist' ($start.Environment['PATH'] -ceq 'fixture-path' -and $start.Environment['CLAUDE_CONFIG_DIR'] -ceq 'owned-profile' -and $start.Environment['ANTIPHON_LIVE_LANE_CONTEXT'] -ceq 'owned-context' -and -not $start.Environment.ContainsKey('ANTHROPIC_API_KEY') -and -not $start.Environment.ContainsKey('ANTIPHON_TASK_TOKEN') -and -not $start.Environment.ContainsKey('HOME'))
    $ok = $true
    foreach ($key in @('CLAUDE_CODE_CHILD_SESSION','CLAUDE_CODE_SESSION_ID','CLAUDE_CODE_BRIDGE_SESSION_ID','CLAUDECODE')) {
        $inherited[$key] = 'hostile-inherited-session'
        foreach ($state in @('absent','empty','value')) {
            $values = $selected.Clone()
            if ($state -ne 'absent') { $values[$key] = $(if ($state -eq 'empty') { '' } else { 'fixture-session' }) }
            $start = New-LiveLaneChildStartInfo $inherited $values $ResultsDirectory
            if ($state -eq 'absent') { $ok = $ok -and -not $start.Environment.ContainsKey($key) }
            else { $ok = $ok -and $start.Environment.ContainsKey($key) -and $start.Environment[$key] -ceq $values[$key] }
        }
    }
    Check 'empty' $ok
    # Presence-aware snapshots are compared without printing values.
    $keys = @('CLAUDE_CODE_CHILD_SESSION','CLAUDE_CODE_SESSION_ID','CLAUDE_CODE_BRIDGE_SESSION_ID','CLAUDECODE')
    $saved = [Environment]::GetEnvironmentVariables()
    try {
        foreach ($key in $keys) { [Environment]::SetEnvironmentVariable($key, '') }
        [Environment]::SetEnvironmentVariable('CLAUDECODE', 'fixture-parent')
        $before = [Environment]::GetEnvironmentVariables()
        foreach ($kind in @('success','refusal','throw')) {
            $q = New-Fixture
            if ($kind -eq 'refusal') { $q.Request.Commission = '' }
            if ($kind -eq 'throw') { $F.Fault = 'wait-failure' }
            $null = Run-Fixture $q
        }
        $after = [Environment]::GetEnvironmentVariables()
        $same = $before.Count -eq $after.Count
        foreach ($key in $before.Keys) { $same = $same -and $after.Contains($key) -and $after[$key] -ceq $before[$key] }
    } finally {
        foreach ($key in $keys) {
            if ($saved.Contains($key)) { [Environment]::SetEnvironmentVariable($key, [string]$saved[$key]) }
            else { [Environment]::SetEnvironmentVariable($key, [NullString]::Value) }
        }
    }
    Check 'parent' $same
}
function Test-DeadlineJoinsOwnedChildren {
    $q = New-Fixture; $r = Run-Fixture $q
    Check 'wait75' ($F.Starts -eq 1 -and $F.Waits -eq 3 -and @($F.Trace | Where-Object { $_ -eq 'wait:checkpoint-A' }).Count -eq 3 -and $F.Trace.IndexOf('receipt') -gt $F.Trace.IndexOf('terminal:checkpoint-A') -and $r.TerminalComplete)
    $q = New-Fixture; $F.Deadline = $true; $r = Run-Fixture $q
    $trace = $F.Trace -join ','
    Check 'deadline' ($r.ExitCode -eq 20 -and $r.Reason -ceq 'deadline' -and $trace -match 'stop:child-A,stop:child-B,join:child-A,join:child-B,(?:receipt,)?release' -and -not (Lock-Exists $q))
    $q = New-Fixture; $F.JoinOk = $false; $r = Run-Fixture $q
    Check 'cleanup-unknown' ($r.ExitCode -eq 20 -and $r.Reason -ceq 'cleanup-incomplete' -and (Lock-Exists $q) -and -not $r.TerminalComplete)
    foreach ($fault in @('context-write','launch-failure','wait-failure','terminal-read','receipt-write')) {
        $q = New-Fixture; $F.Fault = $fault; $r = Run-Fixture $q
        $ok = $r.ExitCode -ne 0 -and -not $r.TerminalComplete -and $F.Starts -le 1 -and -not (Lock-Exists $q) -and $F.Trace.Contains($(if ($fault -eq 'receipt-write') {'receipt'} elseif ($fault -eq 'terminal-read') {'terminal:checkpoint-A'} elseif ($fault -eq 'launch-failure') {'start:checkpoint-A'} elseif ($fault -eq 'wait-failure') {'wait:checkpoint-A'} else {'context-write'}))
        if ($fault -in @('wait-failure','terminal-read','receipt-write')) { $ok = $ok -and $F.Trace.Contains('join:child-A') -and $F.Trace.Contains('join:child-B') }
        Check $fault $ok
    }
    $q = New-Fixture; $F.Change = 'foreign-lock'; $r = Run-Fixture $q
    $p = Get-ReleaseGateNativeLockPath $q.Request.CoordinationRoot
    Check 'foreign-lock' ($F.Trace.Contains('join:child-B') -and (Test-Path -LiteralPath $p) -and [IO.File]::ReadAllText($p) -ceq $F.Foreign)
}
try {
    switch ($method) {
        'LockBeforeSlot' { Test-LockBeforeSlot }
        'WrapperOnlyPaidManifest' { Test-WrapperOnlyPaidManifest }
        'ChildEnvironmentIsScoped' { Test-ChildEnvironmentIsScoped }
        'DeadlineJoinsOwnedChildren' { Test-DeadlineJoinsOwnedChildren }
        default { throw 'unknown fixture case' }
    }
} catch { Write-C487Fail "C1545 $method/harness" ($_.Exception.Message + ' at ' + $_.InvocationInfo.ScriptLineNumber) }
Complete-C487Harness -ResultsDirectory $ResultsDirectory
