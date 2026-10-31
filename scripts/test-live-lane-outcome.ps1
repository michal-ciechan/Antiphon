#requires -Version 7.0
# Synthetic, offline outcome contracts. No provider executable or network operation.
param([string]$Case, [string]$ResultsDirectory)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/lib/live-lane-run.ps1"
. "$PSScriptRoot/lib/c487-harness.ps1"
$ResultsDirectory = New-C487Root $ResultsDirectory
$method = $Case -replace '^C1545_', ''
function Check([string]$Id, [bool]$Ok) { Assert-C487 $Ok "C1545 $method/$Id" 'synthetic outcome contract mismatch' }
function Row([string]$Uid = 'fixture.A.0', [string]$State = 'Passed') {
    return @{
        Uid=$Uid; State=$State; Setup='Ready'; Healthy=$true; Deterministic=$true
        Account='account-A'; Endpoint='endpoint-A'; Dependency='provider-A'
        Session='session-A'; Request='request-A'; Prompt="fixture whole`nprompt body"; Transcript=@()
    }
}
function Outage($row) {
    $row.State='Failed'
    $row.Transcript=@(
        @{ Type='UserPrompt'; Body=$row.Prompt; Session=$row.Session; Request=$row.Request; Sequence=1 },
        @{ Type='ApiError'; Error='ServiceUnavailable'; Session=$row.Session; Request=$row.Request; Sequence=2 })
}
function New-Fixture {
    $root = Join-Path $ResultsDirectory ([guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($root)
    $script:F=@{
        Root=$root; Tick=0; Starts=0; Prompts=0; Slots=0; Waits=0; Pre=0; Post=0; ProbePrompts=0
        PreStatus='Ready'; PreDelay=0; PostStatus='Unknown'; PostDelay=0; Foreign=''; Raw=0
        Trace=[Collections.Generic.List[string]]::new(); ProbeRequests=[Collections.Generic.List[object]]::new()
    }
    $request=@{
        Mode='Fixture'; Lane='fixture-live'; RunId='run-A'; Attempt=1; Source=('a'*40)
        Uids=@('fixture.A.0'); Executable='owned-fixture-executable'; Profile='owned-fixture-profile'
        Commission='fixture-commission'; BudgetSeconds=120; Inherited=@{}
        CoordinationRoot=(Join-Path $root 'coord'); ResultsRoot=(Join-Path $root 'diagnostic')
        Account='account-A'; Endpoint='endpoint-A'; Dependency='provider-A'
    }
    $F.Terminal=@{
        SchemaVersion=1; RunId='checkpoint-A'; Lane='fixture-live'; Attempt=1; Source=('a'*40)
        Build='Succeeded'; Platform='Fixture'; Authority='diagnostic'; Complete=$true; Evidence='Complete'
        InputBefore='input-A'; InputAfter='input-A'; ExecutableBefore='exe-A'; ExecutableAfter='exe-A'
        Outcome='Passed'; Rows=@((Row))
    }
    $boundary=@{
        Candidate={ param($r) @{ Sha=$r.Source; Clean=$true } }; Clock={ $F.Tick }
        Slot={ param($r) $F.Slots++; return $true }
        WriteContext={ param($p,$j) [IO.File]::WriteAllText($p,$j); return $p }
        Start={ param($c,$s,$m) $F.Starts++; $F.Prompts++; $F.Trace.Add('start'); return @{ RunId='checkpoint-A'; Children=@('owned-child') } }
        Wait={ param($id) $F.Waits++; $F.Trace.Add('wait:'+ $id); if ($F.Waits -eq 1) { return 75 }; return $F.Raw }
        ReadTerminal={ param($id) $F.Trace.Add('terminal'); return $F.Terminal }
        Stop={ param($id) $F.Trace.Add('stop') }; Join={ param($id,$budget) $F.Trace.Add('join'); return $true }
        WriteReceipt={ param($p,$r) $F.Trace.Add('receipt'); [IO.File]::WriteAllText($p,($r | ConvertTo-Json -Depth 20)) }
        Probe={ param($purpose,$spec)
            $F.ProbeRequests.Add($spec); $F.ProbePrompts += [int]$spec.PromptCount
            if ($spec.ContainsKey('Prompt') -or $spec.Operation -cne 'Availability') { throw 'forbidden model call' }
            $F.Trace.Add('probe:'+ $purpose)
            $status=$F.PostStatus; $delay=$F.PostDelay
            if ($purpose -eq 'Preflight') { $F.Pre++; $status=$F.PreStatus; $delay=$F.PreDelay }
            else { $F.Post++; if ($F.Post -gt 1) { $status='Unavailable' } }
            # The adapter honours the supplied absolute deadline, with virtual time.
            $available=[Math]::Max(0,[double]$spec.Deadline-$F.Tick)
            $F.Tick += [Math]::Min($delay,$available)
            if ($delay -gt $available) { $status='Timeout' }
            $p=@{ Status=$status; Error='ServiceUnavailable'; Account='account-A'; Endpoint='endpoint-A'; Dependency='provider-A' }
            if ($F.Foreign) { $p[$F.Foreign]='foreign' }
            return $p
        }
    }
    return @{ Request=$request; Boundary=$boundary }
}
function Run($q) {
    $clock=[Diagnostics.Stopwatch]::StartNew()
    $r=& "$PSScriptRoot/run-live-lane.ps1" -Request $q.Request -Boundary $q.Boundary
    [IO.File]::WriteAllText((Join-Path $F.Root 'trace.json'),(@{
        method=$method; source=(& git -C (Split-Path $PSScriptRoot) rev-parse HEAD)
        hostName=[Environment]::MachineName; platform=[Environment]::OSVersion.Platform.ToString(); category='script-orchestration'
        wallSeconds=$clock.Elapsed.TotalSeconds; dbSeconds=$null; httpSeconds=$null; nativeSeconds=$null
        result=$r; trace=@($F.Trace); probeRequests=@($F.ProbeRequests)
    } | ConvertTo-Json -Depth 20))
    return $r
}
function Is($r,[int]$code,[string]$reason='') {
    return ($r.ExitCode -eq $code -and (-not $reason -or $r.Reason -ceq $reason) -and $r.DiagnosticOnly -and -not $r.GateCredit)
}
function Test-ProviderOutcomeIsDistinct {
    $q=New-Fixture; $r=Run $q
    $receipt=Join-Path $q.Request.ResultsRoot 'diagnostic.json'
    $saved=Get-Content -LiteralPath $receipt -Raw | ConvertFrom-Json
    Check 'green' ((Is $r 0 'all-passed') -and $r.Classification -ceq 'GREEN' -and $r.TerminalComplete -and $saved.ExitCode -eq 0 -and $saved.DiagnosticOnly -and -not $saved.GateCredit -and $F.Trace.IndexOf('join') -lt $F.Trace.IndexOf('receipt'))
    $ok=$true
    foreach ($raw in @(2,3,8)) {
        $q=New-Fixture; $F.Raw=$raw; $F.Terminal.Build='Failed'; $F.Terminal.Outcome='Failed'; $r=Run $q
        $ok=$ok -and (Is $r 2) -and $r.RawChildExit -eq $raw -and $r.RawChildOutcome -ceq 'Failed' -and $F.Waits -eq 2
    }
    Check 'raw-child' $ok
    $ok=$true
    foreach ($variant in @('missing','unreadable','incomplete','identity','schema')) {
        $q=New-Fixture
        switch ($variant) {
            'missing' { $F.Terminal=$null }; 'unreadable' { $F.Terminal.Evidence='Unreadable' }
            'incomplete' { $F.Terminal.Complete=$false }; 'identity' { $F.Terminal.RunId='foreign' }
            'schema' { $F.Terminal.SchemaVersion=42 }
        }
        $r=Run $q; $expected=20; if ($variant -in @('identity','schema')) { $expected=2 }
        $ok=$ok -and (Is $r $expected) -and $F.Starts -eq 1
    }
    Check 'evidence' $ok
    $ok=$true
    foreach ($kind in @('InputAfter','ExecutableAfter')) { $q=New-Fixture; $F.Terminal[$kind]='drift'; $r=Run $q; $ok=$ok -and (Is $r 20 'input-drift') }
    Check 'drift' $ok
    $q=New-Fixture; $F.PreStatus='Unavailable'; $F.PreDelay=20; $r=Run $q
    Check 'pre-retry' ((Is $r 20 'provider-outage') -and $F.Pre -eq 2 -and $F.Tick -le 60 -and $F.ProbeRequests[1].Deadline - $F.ProbeRequests[0].Deadline -le 30 -and $F.Starts -eq 0)
    $q=New-Fixture; $F.PreStatus='QuotaExceeded'; $r=Run $q
    Check 'quota-retry' ((Is $r 20 'quota-exhausted') -and $F.Pre -eq 1 -and $F.Starts -eq 0 -and $F.Prompts -eq 0)
    $q=New-Fixture; Outage $F.Terminal.Rows[0]; $F.Raw=3; $r=Run $q
    Check 'post-submit' ((Is $r 20 'provider-outage') -and $F.Pre -eq 1 -and $F.Starts -eq 1 -and $F.Prompts -eq 1 -and $r.RawChildExit -eq 3)
}
function Test-MidRunOutageAfterReceiptIsInconclusive {
    $ok=$true
    foreach ($type in @('Overloaded','ServiceUnavailable','RateLimited')) {
        $q=New-Fixture; Outage $F.Terminal.Rows[0]; $F.Terminal.Rows[0].Transcript[1].Error=$type; $r=Run $q
        $ok=$ok -and (Is $r 20 'provider-outage') -and $F.Pre -eq 1 -and $F.Starts -eq 1 -and $r.Rows[0].Reason -ceq 'provider-outage'
    }
    Check 'transcript-positive' $ok
    foreach ($variant in @('prompt-partial','session-foreign','request-foreign','error-before-prompt','untyped-error')) {
        $q=New-Fixture; Outage $F.Terminal.Rows[0]; $t=$F.Terminal.Rows[0].Transcript
        switch ($variant) {
            'prompt-partial' { $t[0].Body='fixture whole' }; 'session-foreign' { $t[0].Session='foreign' }
            'request-foreign' { $t[1].Request='foreign' }; 'error-before-prompt' { $t[1].Sequence=0 }
            'untyped-error' { $t[1].Error='overload text only' }
        }
        $r=Run $q; Check $variant ((Is $r 1 'assertion-failed') -and $F.Starts -eq 1)
    }
    $q=New-Fixture; $F.Terminal.Rows[0].State='Failed'; $F.PostStatus='Unavailable'; $r=Run $q
    Check 'probe-positive' ((Is $r 20 'provider-outage') -and $F.Post -eq 1)
    foreach ($field in @('Account','Endpoint','Dependency')) {
        $q=New-Fixture; $F.Terminal.Rows[0].State='Failed'; $F.PostStatus='Unavailable'; $F.Foreign=$field; $r=Run $q
        Check ($field.ToLowerInvariant()+'-foreign') ((Is $r 1 'assertion-failed') -and $F.Post -eq 1)
    }
    $q=New-Fixture; $F.Terminal.Rows[0].State='Failed'; $F.PostStatus='Unavailable'; $F.PostDelay=16; $r=Run $q
    Check 'post-probe-bound' ((Is $r 1) -and $F.Tick -le 15 -and $F.Post -eq 1)
    $q=New-Fixture; $F.Terminal.Rows[0].State='Failed'; $r=Run $q
    Check 'post-probe-count' ((Is $r 1) -and $F.Post -eq 1)
    Check 'post-probe-no-prompt' ($F.ProbePrompts -eq 0 -and $F.Prompts -eq 1 -and @($F.ProbeRequests | Where-Object { $_.Operation -cne 'Availability' -or $_.ContainsKey('Prompt') }).Count -eq 0)
}
function Test-HealthyProviderAssertionStaysRed {
    $ok=$true
    foreach ($assertion in @('EnterLost','BridgeDropped')) {
        $q=New-Fixture; $F.Terminal.Rows[0].State='Failed'; $F.Terminal.Rows[0].Assertion=$assertion; $F.PostStatus='Ready'; $r=Run $q
        $ok=$ok -and (Is $r 1 'assertion-failed') -and $r.Classification -ceq 'RED'
    }
    Check 'healthy-red' $ok
    $ok=$true
    foreach ($unproven in @('Unknown','Timeout','InternetFailure','Screenshot','ModelRefusal')) {
        $q=New-Fixture; $F.Terminal.Rows[0].State='Failed'; $F.PostStatus=$unproven; $r=Run $q
        $ok=$ok -and (Is $r 1)
    }
    Check 'unproven-red' $ok
    $ok=$true; $invalid=$true
    foreach ($reverse in @($false,$true)) {
        $q=New-Fixture; $q.Request.Uids=@('fixture.A.0','fixture.B.0')
        $a=Row 'fixture.A.0' 'Failed'; $b=Row 'fixture.B.0'; Outage $b
        $F.Terminal.Rows=$(if ($reverse) { @($b,$a) } else { @($a,$b) }); $r=Run $q
        $ok=$ok -and (Is $r 1) -and @($r.Rows | Where-Object ExitCode -eq 1).Count -eq 1 -and @($r.Rows | Where-Object ExitCode -eq 20).Count -eq 1
        foreach ($field in @('SchemaVersion','Source','Build','Authority')) {
            $F.Terminal[$field]='invalid'; $q.Request.ResultsRoot += '-invalid-'+$field; $r=Run $q
            $invalid=$invalid -and (Is $r 2) -and @($r.Rows).Count -eq 2
            $F.Terminal[$field]=$(switch ($field) { 'SchemaVersion' { 1 }; 'Source' { 'a'*40 }; 'Build' { 'Succeeded' }; 'Authority' { 'diagnostic' } })
        }
    }
    Check 'mixed-red' $ok
    Check 'mixed-invalid' $invalid
}
function Test-TypedSetupOutcomes {
    $ok=$true
    $mapping=@{ AuthMissing='auth-required'; AuthExpired='auth-required'; QuotaExceeded='quota-exhausted'; DependencyUnavailable='provider-outage'; StartupDefaultAbsent='premise-absent' }
    foreach ($status in $mapping.Keys) {
        $q=New-Fixture; $F.PreStatus=$status; $r=Run $q
        $ok=$ok -and (Is $r 20 $mapping[$status]) -and $F.Starts -eq 0 -and $F.Prompts -eq 0
    }
    Check 'external-setup' $ok
    $ok=$true
    foreach ($status in @('ReadyTimeout','TrustNotCleared','BridgeNotEstablished')) {
        $q=New-Fixture; $F.PreStatus=$status; $r=Run $q
        $ok=$ok -and (Is $r 20 'setup-unclassified') -and $F.Prompts -eq 0
    }
    Check 'unclassified-setup' $ok
    $ok=$true
    foreach ($healthy in @($true,$false)) {
        $q=New-Fixture; $F.Terminal.Rows[0].Setup='SetupContractViolation'; $F.Terminal.Rows[0].State='Skipped'; $F.Terminal.Rows[0].Healthy=$healthy; $r=Run $q
        $expected=20; if ($healthy) { $expected=1 }; $ok=$ok -and (Is $r $expected)
    }
    Check 'setup-contract' $ok
    $ok=$true
    foreach ($field in @('Executable','Profile','Context')) {
        $q=New-Fixture
        if ($field -eq 'Context') { $q.Boundary.WriteContext={ return '' } } else { $q.Request[$field]='' }
        $r=Run $q; $reason=@{ Executable='executable-missing'; Profile='profile-required'; Context='lane-context-required' }[$field]
        $ok=$ok -and (Is $r 20 $reason) -and $F.Prompts -eq 0
    }
    Check 'missing-prerequisite' $ok
    $ok=$true
    foreach ($status in @('LegacySkip','arbitrary','')) { $q=New-Fixture; $F.Terminal.Rows[0].State='Skipped'; $F.Terminal.Rows[0].Setup=$status; $r=Run $q; $ok=$ok -and (Is $r 20 'unclassified-skip') }
    Check 'unknown-skip' $ok
    $q=New-Fixture; $F.PreStatus='AuthMissing'; $r=Run $q
    Check 'setup-no-prompt' ((Is $r 20 'auth-required') -and $F.Prompts -eq 0 -and $F.Starts -eq 0)
}
function Test-SkipAndZeroNeverGreen {
    $q=New-Fixture; $q.Request.Uids=@('fixture.A.0','fixture.B.0'); $F.Terminal.Rows=@((Row),(Row 'fixture.B.0')); $r=Run $q
    $ok=(Is $r 0)
    foreach ($variant in @('zero','missing','duplicate','unexpected','wrong','summary')) {
        $q=New-Fixture; $q.Request.Uids=@('fixture.A.0','fixture.B.0')
        $F.Terminal.Rows=$(switch ($variant) {
            'zero' { @() }; 'missing' { @((Row)) }; 'duplicate' { @((Row),(Row)) }
            'unexpected' { @((Row),(Row 'fixture.B.0'),(Row 'unexpected')) }; 'wrong' { @((Row),(Row 'wrong')) }; 'summary' { @() }
        })
        $F.Terminal.Passed=2; $r=Run $q; $ok=$ok -and (Is $r 2 'roster-incomplete')
    }
    Check 'exact-roster' $ok
    $ok=$true
    foreach ($setup in @('Ready','AuthMissing','LegacySkip')) {
        $q=New-Fixture; $q.Request.Uids=@('fixture.A.0','fixture.B.0'); $skip=Row 'fixture.B.0' 'Skipped'; $skip.Setup=$setup; $F.Terminal.Rows=@((Row),$skip)
        $r=Run $q; $ok=$ok -and (Is $r 20)
    }
    # A typed preflight refusal explains why no UID executed.
    $q=New-Fixture; $F.PreStatus='AuthExpired'; $F.Terminal.Rows=@(); $r=Run $q; $ok=$ok -and (Is $r 20 'auth-required')
    Check 'skipped' $ok
    $ok=$true
    foreach ($field in @('SchemaVersion','Build','Platform','Authority','Source','Uid','State')) {
        $q=New-Fixture; Outage $F.Terminal.Rows[0]
        if ($field -in @('Uid','State')) { $F.Terminal.Rows[0][$field]=$null } else { $F.Terminal[$field]=$null }
        $r=Run $q; $ok=$ok -and (Is $r 2)
    }
    Check 'invalid-schema' $ok
}
function Test-StaleSharedLockRefuses {
    foreach ($age in @('2000-01-01T00:00:00Z','2099-01-01T00:00:00Z')) {
        $q=New-Fixture; $p=Get-ReleaseGateNativeLockPath $q.Request.CoordinationRoot
        [void][IO.Directory]::CreateDirectory((Split-Path $p))
        $bytes=@{ pid=2147483647; runId='dead'; lane='fixture-live'; processStartedAt='2000-01-01T00:00:00Z'; acquiredAt=$age } | ConvertTo-Json -Compress
        [IO.File]::WriteAllText($p,$bytes); $r=Run $q
        $ok=(Is $r 2 'stale-shared-lock') -and $r.Classification -ceq 'INVALID' -and $F.Starts -eq 0 -and $F.Slots -eq 0 -and [IO.File]::ReadAllText($p) -ceq $bytes
        Check $(if ($age.StartsWith('2000')) { 'stale' } else { 'age' }) $ok
    }
    $q=New-Fixture; $owner=Enter-ReleaseGateNativeLock -CoordinationRoot $q.Request.CoordinationRoot -RunId foreign -Lane fixture-live
    $bytes=[IO.File]::ReadAllText($owner.Path); $r=Run $q
    Check 'busy' ((Is $r 21 'deferred-busy') -and $F.Starts -eq 0 -and $F.Slots -eq 0 -and [IO.File]::ReadAllText($owner.Path) -ceq $bytes)
    Exit-ReleaseGateNativeLock -CoordinationRoot $q.Request.CoordinationRoot -RunId foreign
}
try {
    if (-not (Get-Command Get-LiveLaneOutcome -ErrorAction SilentlyContinue)) {
        Check 'entry' $false
    } else {
        Check 'entry' $true
        switch ($method) {
            'ProviderOutcomeIsDistinct' { Test-ProviderOutcomeIsDistinct }
            'MidRunOutageAfterReceiptIsInconclusive' { Test-MidRunOutageAfterReceiptIsInconclusive }
            'HealthyProviderAssertionStaysRed' { Test-HealthyProviderAssertionStaysRed }
            'TypedSetupOutcomes' { Test-TypedSetupOutcomes }
            'SkipAndZeroNeverGreen' { Test-SkipAndZeroNeverGreen }
            'StaleSharedLockRefuses' { Test-StaleSharedLockRefuses }
            default { throw 'unknown synthetic case' }
        }
    }
} catch { Write-C487Fail "C1545 $method/harness" ($_.Exception.Message+' line '+$_.InvocationInfo.ScriptLineNumber) }
Complete-C487Harness -ResultsDirectory $ResultsDirectory
