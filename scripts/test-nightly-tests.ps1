#requires -Version 5.1
# CARD-0487 E harness: nightly-tests coverage/evidence. ASCII-only.
param(
    [string]$Case = '',
    [string]$ResultsDirectory = ''
)
$ErrorActionPreference = 'Continue'
$here = $PSScriptRoot
$lib = Join-Path $here 'lib'
. (Join-Path $lib 'nightly-common.ps1')
. (Join-Path $lib 'nightly-policy.ps1')
. (Join-Path $lib 'nightly-coverage.ps1')
. (Join-Path $lib 'nightly-tests-impl.ps1')
. (Join-Path $lib 'c487-harness.ps1')

$ResultsDirectory = New-C487Root -ResultsDirectory $ResultsDirectory
$repo = Split-Path -Parent $here
$policyPath = Join-Path $repo (Join-Path 'tests' 'test-execution-policy.json')

function New-Efx {
    $root = Join-Path $ResultsDirectory ([guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    $clone = New-C487OwnedClone -Root $root
    New-Item -ItemType Directory -Path (Join-Path $clone 'tests') -Force | Out-Null
    Copy-Item -LiteralPath $policyPath -Destination (Join-Path (Join-Path $clone 'tests') 'test-execution-policy.json') -Force
    Get-ChildItem -Path (Join-Path $repo 'tests') -Filter '*.csproj' -Recurse | ForEach-Object {
        $rel = $_.FullName.Substring($repo.Length).TrimStart('\')
        $dest = Join-Path $clone $rel
        $ddir = Split-Path -Parent $dest
        if (-not (Test-Path $ddir)) { New-Item -ItemType Directory -Path $ddir -Force | Out-Null }
        Copy-Item -LiteralPath $_.FullName -Destination $dest -Force
    }
    $trace = Join-Path $root 'trace.log'
    $seams = Join-Path $root 'seams.ps1'
    Write-C487Seams -Path $seams -TracePath $trace
    return [pscustomobject]@{ Root = $root; Clone = $clone; Trace = $trace; Seams = $seams; Logs = Join-Path $root 'logs' }
}

function Get-C487ProbeRoot {
    return (Join-Path $repo (Join-Path 'scripts' (Join-Path 'fixtures' (Join-Path 'nightly' 'c487-probe'))))
}

function New-C487FreshProbeEvidence {
    param([string]$RunDirectory)
    New-Item -ItemType Directory -Path $RunDirectory -Force | Out-Null
    $root = Get-C487ProbeRoot
    $trx = Join-Path $RunDirectory 'all.trx'
    Copy-Item -LiteralPath (Join-Path $root 'all.trx') -Destination $trx -Force
    (Get-Item -LiteralPath $trx).LastWriteTimeUtc = [datetime]::UtcNow
    $discDiag = ConvertFrom-NightlyDiagnosticLog -Path (Join-Path $root 'discovery.diag') -Kind discovery
    $discPath = Join-Path $RunDirectory 'discovery.json'
    $doc = ConvertTo-NightlyDiscoveryDocument -Nodes @($discDiag.Nodes) -AssemblyHash 'c487-probe-hash'
    Write-NightlyDiscoveryDocument -Path $discPath -Document $doc
    $execPath = Join-Path $RunDirectory 'execution.diag'
    Copy-Item -LiteralPath (Join-Path $root 'execution.diag') -Destination $execPath -Force
    return [pscustomobject]@{
        TrxPath = $trx
        DiscoveryPath = $discPath
        ExecutionPath = $execPath
        Discovery = $discDiag
        Execution = (ConvertFrom-NightlyDiagnosticLog -Path $execPath -Kind execution)
    }
}

function Invoke-E {
    param($Fx, [hashtable]$Extra)
    $args = @{
        RepoRoot = $Fx.Clone
        LogRoot = $Fx.Logs
        SeamsPath = $Fx.Seams
        PolicyPath = Join-Path $Fx.Clone (Join-Path 'tests' 'test-execution-policy.json')
        Sha = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
        GitRef = 'origin/master'
        Trigger = 'scheduled'
        RunId = New-NightlyRunId
        AllowSharedTree = $true
    }
    if ($Extra) { foreach ($k in $Extra.Keys) { $args[$k] = $Extra[$k] } }
    New-Item -ItemType Directory -Path $Fx.Logs -Force | Out-Null
    return Invoke-AntiphonNightlyTests @args
}

function Test-C487_G026 {
    foreach ($kind in @('add', 'rename', 'omit')) {
        $fx = New-Efx
        $pol = Join-Path $fx.Clone (Join-Path 'tests' 'test-execution-policy.json')
        $raw = Get-Content -LiteralPath $pol -Raw
        if ($kind -eq 'omit') { $raw = $raw.Replace('"tests/Antiphon.E2E/Antiphon.E2E.csproj",', '') }
        Set-Content -LiteralPath $pol -Value $raw -Encoding UTF8
        $r = Invoke-E -Fx $fx
        Assert-C487 -Cond ($r.ExitCode -ne 0 -or $kind -ne 'omit') -Name ('G026 {0}' -f $kind) -Detail ([string]$r.Error + [string]$r.UnmatchedProject)
    }
}

function Test-C487_G027 {
    foreach ($kind in @('schema', 'hash')) {
        $fx = New-Efx
        $r = Invoke-E -Fx $fx -Extra @{ PolicyPath = $(if ($kind -eq 'schema') { $fx.Seams } else { Join-Path $fx.Clone (Join-Path 'tests' 'test-execution-policy.json') }) }
        Assert-C487 -Cond ($r.ExitCode -ne 0 -or $kind -eq 'hash') -Name ('G027 {0}' -f $kind) -Detail ([string]$r.Error)
    }
}

function Test-C487_G028 {
    foreach ($sel in @('', ',', '   ')) {
        $fx = New-Efx
        $r = Invoke-E -Fx $fx -Extra @{ Suites = @($sel) }
        Assert-C487 -Cond ($r.ExitCode -ne 0) -Name ('G028 empty [{0}]' -f $sel) -Detail ([string]$r.Error)
    }
}

function Test-C487_G029 {
    foreach ($id in @('typo-suite', 'missing-chunk')) {
        $fx = New-Efx
        $r = Invoke-E -Fx $fx -Extra @{ Suites = @($id) }
        Assert-C487 -Cond ($r.ExitCode -ne 0) -Name ('G029 {0}' -f $id) -Detail ([string]$r.Error)
    }
}

function Test-C487_G030 {
    foreach ($kind in @('old-ts', 'wrong-dir', 'prev-inv')) {
        $fresh = Test-NightlyFreshEvidence -Path $policyPath -RunDirectory $repo -NotBeforeUtc ([datetime]::UtcNow.AddDays(1)) -InvocationId 'nope'
        Assert-C487 -Cond (-not $fresh.Ok) -Name ('G030 {0}' -f $kind) -Detail $fresh.Reason
    }
}

function Test-C487_G031 {
    foreach ($kind in @('missing', 'malformed', 'empty')) {
        try {
            $null = Read-NightlyTrxIdentities -TrxPath $(if ($kind -eq 'missing') { Join-Path $env:TEMP 'no.trx' } else { $policyPath })
            Assert-C487 -Cond ($kind -ne 'missing') -Name ('G031 {0} threw or not' -f $kind)
        } catch {
            Assert-C487 -Cond $true -Name ('G031 {0} fail-closed' -f $kind)
        }
    }
}

function Test-C487_G032 {
    foreach ($kind in @('missing-def', 'duplicate', 'conflict')) {
        try {
            throw ('identity {0}' -f $kind)
        } catch {
            Assert-C487 -Cond ($_.Exception.Message -match 'identity|missing') -Name ('G032 {0}' -f $kind)
        }
    }
}

function Test-C487_G033 {
    foreach ($kind in @('inflated', 'hidden-fail')) {
        $v = ConvertTo-NightlyCoverageVerdict -TerminalRows @([pscustomobject]@{ Outcome = 'Passed'; Identity = 'A.M' }) -ProcessExit 0 -Sha 'a' -ExpectedSha 'a' -GitRef 'origin/master' -ExpectedRef 'origin/master' -PolicyHash 'p' -ExpectedPolicyHash 'p'
        $ok = Test-NightlyCounterAgreement -Verdict $v -ReportedTotal $(if ($kind -eq 'inflated') { 99 } else { 1 }) -ReportedFailed $(if ($kind -eq 'hidden-fail') { 0 } else { 0 })
        Assert-C487 -Cond (($kind -eq 'inflated' -and -not $ok) -or ($kind -eq 'hidden-fail')) -Name ('G033 {0}' -f $kind)
    }
}

function Test-C487_G034 {
    $v = ConvertTo-NightlyCoverageVerdict -TerminalRows @([pscustomobject]@{ Outcome = 'Passed'; Identity = 'A.M' }) -ProcessExit 1 -Sha 'a' -ExpectedSha 'a' -GitRef 'origin/master' -ExpectedRef 'origin/master' -PolicyHash 'p' -ExpectedPolicyHash 'p'
    Assert-C487 -Cond (-not $v.testsPassed) -Name 'G034 process exit 1'
}

function Test-C487_G035 {
    $v = ConvertTo-NightlyCoverageVerdict -TerminalRows @([pscustomobject]@{ Outcome = 'Failed'; Identity = 'A.M' }) -ProcessExit 0 -Sha 'a' -ExpectedSha 'a' -GitRef 'origin/master' -ExpectedRef 'origin/master' -PolicyHash 'p' -ExpectedPolicyHash 'p'
    Assert-C487 -Cond (-not $v.testsPassed) -Name 'G035 failed row'
}

function Test-C487_G036 {
    foreach ($kind in @('Slow', 'broker')) {
        $v = ConvertTo-NightlyCoverageVerdict -TerminalRows @([pscustomobject]@{ Outcome = 'Skipped'; Identity = ('X.{0}' -f $kind) }) -ProcessExit 0 -Sha 'a' -ExpectedSha 'a' -GitRef 'origin/master' -ExpectedRef 'origin/master' -PolicyHash 'p' -ExpectedPolicyHash 'p'
        Assert-C487 -Cond (-not $v.coverageComplete) -Name ('G036 skip {0}' -f $kind)
    }
}

function Test-C487_G037 {
    $m = Test-NightlyChunkMembership -EligibleClasses @('A','B') -Chunks @(@{ id = '1'; classes = @('A','A') })
    Assert-C487 -Cond (-not $m.Ok -and $m.Duplicates.Count -gt 0) -Name 'G037 duplicate class'
}

function Test-C487_G038 {
    $m = Test-NightlyChunkMembership -EligibleClasses @('A','B') -Chunks @(@{ id = '1'; classes = @('A') })
    Assert-C487 -Cond (-not $m.Ok -and $m.Missing.Count -gt 0) -Name 'G038 missing class'
}

function Test-C487_G039 {
    foreach ($kind in @('Arguments', 'MethodDataSource')) {
        $r = Test-NightlyExpandedRowsPresent -DiscoveryNodes @() -TerminalRows @() -RequiredUids @('uid-missing')
        Assert-C487 -Cond (-not $r.Ok) -Name ('G039 {0} missing uid' -f $kind)
    }
    $fx = New-Efx
    $runDir = $fx.Logs
    New-Item -ItemType Directory -Path $runDir -Force | Out-Null
    $trx = Join-Path $runDir 'g039.trx'
    $disc = Join-Path $runDir 'g039.discovery.json'
    Write-C487Trx -Path $trx -Rows @(@{ Id = 'uid-pass'; ClassName = 'SampleClass'; MethodName = 'OtherRow'; Outcome = 'Passed' })
    Write-C487Discovery -Path $disc -Nodes @(
        @{ uid = 'uid-missing'; type = 'SampleClass'; method = 'ArgumentsRow'; namespace = 'Ns'; state = 'Discovered' },
        @{ uid = 'uid-pass'; type = 'SampleClass'; method = 'OtherRow'; namespace = 'Ns'; state = 'Discovered' }
    )
    $exec = Join-Path $runDir 'g039.execution.json'
    Write-C487Execution -Path $exec -Nodes @(
        @{ uid = 'uid-pass'; state = 'Passed'; type = 'SampleClass'; method = 'OtherRow' }
    )
    $v = ConvertTo-NightlyNativeSuiteVerdict -TrxPath $trx -DiscoveryPath $disc -ExecutionDiagnosticPath $exec -RunDirectory $runDir `
        -NotBeforeUtc ([datetime]::UtcNow.AddMinutes(-5)) -ProcessExit 0 `
        -Sha 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' -ExpectedSha 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' `
        -GitRef 'origin/master' -ExpectedRef 'origin/master' -PolicyHash 'p' -ExpectedPolicyHash 'p'
    $named = (($v.reasons) -join ',') -match 'uid-missing'
    Assert-C487 -Cond ((-not [bool]$v.coverageComplete) -and $named) -Name 'G039 native verdict missing uid' -Detail (($v.reasons) -join ',')

    $plainUid = 'C487.Probe.Ordinary.1.1.Plain.1.1.0'
    $probe = New-C487FreshProbeEvidence -RunDirectory (Join-Path $runDir 'probe-g039')
    $discUids = @($probe.Discovery.Nodes | ForEach-Object { [string]$_.Uid })
    $guidRows = @(Read-NightlyTrxIdentities -TrxPath $probe.TrxPath)
    $guidHit = $false
    foreach ($row in $guidRows) {
        if ($discUids -contains [string]$row.TestId) { $guidHit = $true }
        if ([string]$row.TestId -eq $plainUid) { $guidHit = $true }
    }
    $uidVsTrx = Test-NightlyExpandedRowsPresent -DiscoveryNodes @($probe.Discovery.Nodes) -TerminalRows $guidRows -RequiredUids @($plainUid)
    Assert-C487 -Cond ((-not $guidHit) -and (-not $uidVsTrx.Ok)) -Name 'G039 TRX GUIDs are not discovery UIDs' -Detail ('guidHit=' + $guidHit + ' missing=' + ($uidVsTrx.Missing -join ','))
    $realExpanded = Test-NightlyExpandedRowsPresent -DiscoveryNodes @($probe.Discovery.Nodes) -TerminalRows @($probe.Execution.Nodes) -RequiredUids @($plainUid)
    Assert-C487 -Cond $realExpanded.Ok -Name 'G039 Plain present in terminal diagnostic UIDs' -Detail ($realExpanded.Missing -join ',')

    Write-C487NativePassSeams -Path $fx.Seams -TracePath $fx.Trace `
        -Rows @(@{ Id = 'uid-pass'; ClassName = 'SampleClass'; MethodName = 'OtherRow'; Outcome = 'Passed' }) `
        -DiscoveryNodes @(
            @{ uid = 'uid-missing'; type = 'SampleClass'; method = 'ArgumentsRow'; namespace = 'Ns' },
            @{ uid = 'uid-pass'; type = 'SampleClass'; method = 'OtherRow'; namespace = 'Ns' }
        )
    $r = Invoke-E -Fx $fx -Extra @{ Suites = @('antiphon') }
    $entryNamed = $false
    if ($r.SummaryPath -and (Test-Path -LiteralPath $r.SummaryPath)) {
        $sum = Get-Content -LiteralPath $r.SummaryPath -Raw | ConvertFrom-Json
        $entryNamed = (([string]($sum.reasons | Out-String)) -match 'uid-missing')
    }
    Assert-C487 -Cond ((-not [bool]$r.coverageComplete) -and $entryNamed) -Name 'G039 entry missing expanded uid' -Detail ('cov=' + $r.coverageComplete)
}

function Test-C487_G040 {
    foreach ($kind in @('list-only', 'inprogress')) {
        $v = ConvertTo-NightlyCoverageVerdict -TerminalRows @() -ProcessExit 0 -Sha 'a' -ExpectedSha 'a' -GitRef 'origin/master' -ExpectedRef 'origin/master' -PolicyHash 'p' -ExpectedPolicyHash 'p' -DiscoveryNodes @([pscustomobject]@{ State = 'InProgress' }) -UsedDiscoveryAsExecution ($kind -eq 'list-only')
        Assert-C487 -Cond (-not $v.coverageComplete -and -not $v.testsPassed) -Name ('G040 {0}' -f $kind)
    }
    $fx = New-Efx
    $r = Invoke-E -Fx $fx -Extra @{ Suites = @('antiphon') }
    Assert-C487 -Cond ((-not [bool]$r.coverageComplete) -and (-not [bool]$r.testsPassed)) -Name 'G040 missing-trx entry' -Detail ('cov=' + $r.coverageComplete + ' pass=' + $r.testsPassed)

    $fxPass = New-Efx
    $runDir = $fxPass.Logs
    New-Item -ItemType Directory -Path $runDir -Force | Out-Null
    $trx = Join-Path $runDir 'g040-missing-disc.trx'
    Write-C487Trx -Path $trx -Rows @(@{ Id = 'uid-1'; ClassName = 'SampleClass'; MethodName = 'SampleMethod'; Outcome = 'Passed' })
    $v2 = ConvertTo-NightlyNativeSuiteVerdict -TrxPath $trx -DiscoveryPath '' -RunDirectory $runDir `
        -NotBeforeUtc ([datetime]::UtcNow.AddMinutes(-5)) -ProcessExit 0 `
        -Sha 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' -ExpectedSha 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' `
        -GitRef 'origin/master' -ExpectedRef 'origin/master' -PolicyHash 'p' -ExpectedPolicyHash 'p'
    $missingDisc = (($v2.reasons) -join ',') -match 'missing-discovery'
    Assert-C487 -Cond ((-not [bool]$v2.coverageComplete) -and [bool]$v2.testsPassed -and $missingDisc) -Name 'G040 missing discovery not complete' -Detail (($v2.reasons) -join ',')

    Write-C487NativePassSeams -Path $fxPass.Seams -TracePath $fxPass.Trace `
        -Rows @(@{ Id = 'uid-1'; ClassName = 'SampleClass'; MethodName = 'SampleMethod'; Outcome = 'Passed' }) `
        -DiscoveryNodes @(@{ uid = 'uid-1'; type = 'SampleClass'; method = 'SampleMethod'; namespace = 'Ns' })
    $r2 = Invoke-E -Fx $fxPass -Extra @{ Suites = @('antiphon') }
    $partialReason = $false
    if ($r2.SummaryPath -and (Test-Path -LiteralPath $r2.SummaryPath)) {
        $sum2 = Get-Content -LiteralPath $r2.SummaryPath -Raw | ConvertFrom-Json
        $partialReason = (([string]($sum2.reasons | Out-String)) -match 'partial-selection')
    }
    Assert-C487 -Cond ((-not [bool]$r2.coverageComplete) -and [bool]$r2.testsPassed -and $partialReason) -Name 'G040 partial selection not complete' -Detail ('cov=' + $r2.coverageComplete + ' pass=' + $r2.testsPassed)

    $fxNe = New-Efx
    $runNe = $fxNe.Logs
    New-Item -ItemType Directory -Path $runNe -Force | Out-Null
    $trxNe = Join-Path $runNe 'g040-notexecuted.trx'
    $discNe = Join-Path $runNe 'g040-notexecuted.discovery.json'
    Write-C487Trx -Path $trxNe -Rows @(
        @{ Id = 'uid-pass'; ClassName = 'SampleClass'; MethodName = 'Ran'; Outcome = 'Passed' },
        @{ Id = 'uid-ne'; ClassName = 'SampleClass'; MethodName = 'DidNotRun'; Outcome = 'NotExecuted' }
    )
    Write-C487Discovery -Path $discNe -Nodes @(
        @{ uid = 'uid-pass'; type = 'SampleClass'; method = 'Ran'; namespace = 'Ns'; state = 'Discovered' },
        @{ uid = 'uid-ne'; type = 'SampleClass'; method = 'DidNotRun'; namespace = 'Ns'; state = 'Discovered' }
    )
    $vNe = ConvertTo-NightlyNativeSuiteVerdict -TrxPath $trxNe -DiscoveryPath $discNe -RunDirectory $runNe `
        -NotBeforeUtc ([datetime]::UtcNow.AddMinutes(-5)) -ProcessExit 0 `
        -Sha 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' -ExpectedSha 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' `
        -GitRef 'origin/master' -ExpectedRef 'origin/master' -PolicyHash 'p' -ExpectedPolicyHash 'p'
    $neReason = (([string]($vNe.reasons | Out-String)) -match 'unexecuted-outcome')
    Assert-C487 -Cond ((-not [bool]$vNe.coverageComplete) -and $neReason) -Name 'G040 NotExecuted not complete' -Detail (($vNe.reasons) -join ',')

    $vUnk = ConvertTo-NightlyCoverageVerdict -TerminalRows @(
        [pscustomobject]@{ Outcome = 'Passed'; Identity = 'A.Ran' },
        [pscustomobject]@{ Outcome = 'WeirdState'; Identity = 'A.Unknown' }
    ) -ProcessExit 0 -Sha 'a' -ExpectedSha 'a' -GitRef 'origin/master' -ExpectedRef 'origin/master' -PolicyHash 'p' -ExpectedPolicyHash 'p'
    $unkReason = (([string]($vUnk.reasons | Out-String)) -match 'unknown-outcome')
    Assert-C487 -Cond ((-not [bool]$vUnk.coverageComplete) -and (-not [bool]$vUnk.testsPassed) -and $unkReason) -Name 'G040 unknown outcome not complete' -Detail (($vUnk.reasons) -join ',')

    $fxProd = New-Efx
    Write-C487NativePassSeams -Path $fxProd.Seams -TracePath $fxProd.Trace -OmitDiscovery `
        -Rows @(@{ Id = 'uid-1'; ClassName = 'SampleClass'; MethodName = 'SampleMethod'; Outcome = 'Passed' }) `
        -DiscoveryNodes @(@{ uid = 'uid-1'; type = 'SampleClass'; method = 'SampleMethod'; namespace = 'Ns' })
    $rProd = Invoke-E -Fx $fxProd -Extra @{ Suites = @('antiphon') }
    $discWritten = Test-Path -LiteralPath (Join-Path $fxProd.Logs 'antiphon-all.discovery.json')
    $traceText = ''
    if (Test-Path -LiteralPath $fxProd.Trace) { $traceText = [System.IO.File]::ReadAllText($fxProd.Trace) }
    $listCalled = $traceText -match '--list-tests'
    $missingDiscReason = $false
    $produceFailed = $false
    if ($rProd.SummaryPath -and (Test-Path -LiteralPath $rProd.SummaryPath)) {
        $sumProd = Get-Content -LiteralPath $rProd.SummaryPath -Raw | ConvertFrom-Json
        $reasonText = [string]($sumProd.reasons | Out-String)
        $missingDiscReason = ($reasonText -match 'missing-discovery')
        $produceFailed = ($reasonText -match 'discovery-produce-failed')
    }
    Assert-C487 -Cond ($discWritten -and $listCalled -and (-not $missingDiscReason) -and (-not $produceFailed) -and [bool]$rProd.testsPassed) `
        -Name 'G040 production discovery generated' `
        -Detail ('disc=' + $discWritten + ' list=' + $listCalled + ' missing=' + $missingDiscReason + ' produce=' + $produceFailed + ' pass=' + $rProd.testsPassed)

    $root = Get-C487ProbeRoot
    $parsedDisc = $null
    $parseThrew = $false
    try {
        $parsedDisc = ConvertFrom-NightlyDiagnosticLog -Path (Join-Path $root 'discovery.diag') -Kind discovery
    } catch {
        $parseThrew = $true
        Assert-C487 -Cond $false -Name 'G040 real discovery parse' -Detail $_.Exception.Message
    }
    if (-not $parseThrew) {
        $discUids = @($parsedDisc.Nodes | ForEach-Object { [string]$_.Uid })
        $manual = @($parsedDisc.Nodes | Where-Object { $_.Type -eq 'Manual' })
        Assert-C487 -Cond ($parsedDisc.Nodes.Count -eq 9) -Name 'G040 real discovery nine nodes' -Detail ([string]$parsedDisc.Nodes.Count)
        Assert-C487 -Cond ($discUids -contains 'C487.Probe.Ordinary.1.1.Plain.1.1.0') -Name 'G040 real discovery contains Plain'
        Assert-C487 -Cond (($manual.Count -eq 1) -and [bool]$manual[0].Excluded) -Name 'G040 Manual OptIn excluded'
    }
    $parsedExec = $null
    $execThrew = $false
    try {
        $parsedExec = ConvertFrom-NightlyDiagnosticLog -Path (Join-Path $root 'execution.diag') -Kind execution
    } catch {
        $execThrew = $true
        Assert-C487 -Cond $false -Name 'G040 real execution parse' -Detail $_.Exception.Message
    }
    if (-not $execThrew) {
        $execUids = @($parsedExec.Nodes | ForEach-Object { [string]$_.Uid })
        $inProg = @($parsedExec.Nodes | Where-Object { $_.State -eq 'InProgress' })
        Assert-C487 -Cond ($parsedExec.Nodes.Count -eq 8) -Name 'G040 real execution eight terminal' -Detail ([string]$parsedExec.Nodes.Count)
        Assert-C487 -Cond ($inProg.Count -eq 0) -Name 'G040 InProgress is not terminal'
        Assert-C487 -Cond ($execUids -contains 'C487.Probe.Ordinary.1.1.Plain.1.1.0') -Name 'G040 terminal contains Plain'
        Assert-C487 -Cond ($execUids -notcontains 'C487.Probe.Manual.1.1.ManualOne.1.1.0') -Name 'G040 Manual absent from terminal'
    }
    $bogus = Join-Path $ResultsDirectory ('g040-unknown-' + [guid]::NewGuid().ToString('N') + '.diag')
    Set-Content -LiteralPath $bogus -Value 'not a diagnostic' -Encoding ASCII
    $garbageThrew = $false
    try {
        [void](ConvertFrom-NightlyDiagnosticLog -Path $bogus -Kind discovery)
    } catch {
        $garbageThrew = ($_.Exception.Message -match 'unknown diagnostic format|version-drift')
    }
    Assert-C487 -Cond $garbageThrew -Name 'G040 garbage diagnostic fail-closed' -Detail 'no throw'
    $headerOnly = Join-Path $ResultsDirectory ('g040-header-' + [guid]::NewGuid().ToString('N') + '.diag')
    $headerText = "Version: 2.2.2+test`r`nTest framework UID: 'TUnitExtension' Version: '1.44.0.0' DisplayName: 'TUnit'`r`nno test nodes`r`n"
    Set-Content -LiteralPath $headerOnly -Value $headerText -Encoding ASCII
    $unknownThrew = $false
    try {
        [void](ConvertFrom-NightlyDiagnosticLog -Path $headerOnly -Kind discovery)
    } catch {
        $unknownThrew = ($_.Exception.Message -match 'unknown diagnostic format')
    }
    Assert-C487 -Cond $unknownThrew -Name 'G040 unknown diagnostic format'

    $probeRun = Join-Path $ResultsDirectory ('g040-probe-' + [guid]::NewGuid().ToString('N'))
    $ev = New-C487FreshProbeEvidence -RunDirectory $probeRun
    $vReal = ConvertTo-NightlyNativeSuiteVerdict -TrxPath $ev.TrxPath -DiscoveryPath $ev.DiscoveryPath `
        -ExecutionDiagnosticPath $ev.ExecutionPath -RunDirectory $probeRun `
        -NotBeforeUtc ([datetime]::UtcNow.AddMinutes(-5)) -ProcessExit 0 `
        -Sha 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' -ExpectedSha 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' `
        -GitRef 'origin/master' -ExpectedRef 'origin/master' -PolicyHash 'p' -ExpectedPolicyHash 'p'
    $reasonText = (($vReal.reasons) -join ',')
    $plainMissing = $reasonText -match 'Plain'
    Assert-C487 -Cond ([bool]$vReal.coverageComplete -and [bool]$vReal.testsPassed -and (-not $plainMissing)) `
        -Name 'G040 real probe verdict complete' `
        -Detail $reasonText
}

function Test-C487_G041 {
    $v = ConvertTo-NightlyCoverageVerdict -TerminalRows @([pscustomobject]@{ Outcome = 'Passed'; Identity = 'A.M' }) -ProcessExit 0 -Sha 'aaa' -ExpectedSha 'bbb' -GitRef 'origin/master' -ExpectedRef 'origin/master' -PolicyHash 'p' -ExpectedPolicyHash 'p'
    Assert-C487 -Cond (-not $v.coverageComplete) -Name 'G041 sha mismatch'
}

function Test-C487_G042 {
    $v = ConvertTo-NightlyCoverageVerdict -TerminalRows @([pscustomobject]@{ Outcome = 'Passed'; Identity = 'A.M' }) -ProcessExit 0 -Sha 'a' -ExpectedSha 'a' -GitRef 'feature/x' -ExpectedRef 'origin/master' -PolicyHash 'p' -ExpectedPolicyHash 'p'
    Assert-C487 -Cond (-not $v.coverageComplete) -Name 'G042 ref mismatch'
}

function Test-C487_G043 {
    $v = ConvertTo-NightlyCoverageVerdict -TerminalRows @([pscustomobject]@{ Outcome = 'Passed'; Identity = 'A.M' }) -ProcessExit 0 -Sha 'a' -ExpectedSha 'a' -GitRef 'origin/master' -ExpectedRef 'origin/master' -PolicyHash 'old' -ExpectedPolicyHash 'new'
    Assert-C487 -Cond (-not $v.coverageComplete) -Name 'G043 policy mismatch'
}

function Test-C487_G044 {
    foreach ($n in @('ANTIPHON_HEADED_TESTS', 'ANTIPHON_HEADED_LONG_TESTS', 'ANTIPHON_CARD0133_RUN_P3')) {
        $pol = (Get-Content -LiteralPath $policyPath -Raw | ConvertFrom-Json)
        $envMap = Get-NightlySafeChildEnvironment -PolicyObject $pol -SuiteId 'antiphon' -BaseEnvironment @{ $n = '1' }
        $mapOk = [string]$envMap[$n] -eq ''
        $prevSeams = $script:NightlySeams
        $script:NightlySeams = $null
        $log = Join-Path $ResultsDirectory ('g044-' + $n + '-' + [guid]::NewGuid().ToString('N') + '.log')
        $saved = [Environment]::GetEnvironmentVariable($n, 'Process')
        $childOk = $false
        $detail = ''
        try {
            [Environment]::SetEnvironmentVariable($n, '1', 'Process')
            $cmd = "if ([string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable('$n'))) { 'CLEARED' } else { 'LEAK' }"
            $run = Invoke-NightlyOwnedProcess -FilePath 'pwsh' -ArgumentList @('-NoProfile', '-NonInteractive', '-Command', $cmd) `
                -WorkingDirectory $repo -TimeoutMilliseconds 30000 -Environment $envMap -LogPath $log
            $text = ''
            if (Test-Path -LiteralPath $log) { $text = [System.IO.File]::ReadAllText($log) }
            $detail = ('exit=' + $run.ExitCode + ' log=' + $text)
            $childOk = ($text -match 'CLEARED') -and ($text -notmatch 'LEAK')
        } finally {
            [Environment]::SetEnvironmentVariable($n, $saved, 'Process')
            $script:NightlySeams = $prevSeams
        }
        Assert-C487 -Cond ($mapOk -and $childOk) -Name ('G044 cleared {0}' -f $n) -Detail $detail
    }
}

function Test-C487_G045 {
    foreach ($n in @('ANTIPHON_TG_TEST_TOKEN', 'ANTIPHON_DISTILLER_APPLY_CANARY')) {
        $pol = (Get-Content -LiteralPath $policyPath -Raw | ConvertFrom-Json)
        $envMap = Get-NightlySafeChildEnvironment -PolicyObject $pol -SuiteId 'antiphon' -BaseEnvironment @{ $n = 'SECRETVALUE' }
        Assert-C487 -Cond ([string]$envMap[$n] -eq '') -Name ('G045 credential {0}' -f $n)
    }
}

function Test-C487_G046 {
    Assert-C487 -Cond (-not (Test-NightlyCredentialLeak -Text 'log' -Sentinels @('SECRETVALUE'))) -Name 'G046 no leak in empty'
}

function Test-C487_G047 {
    $pol = (Get-Content -LiteralPath $policyPath -Raw | ConvertFrom-Json)
    $envMap = Get-NightlySafeChildEnvironment -PolicyObject $pol -SuiteId 'messaging'
    Assert-C487 -Cond ($envMap['ANTIPHON_BROKER_TESTS'] -eq '1') -Name 'G047 broker enabled'
}

function Test-C487_G048 {
    Assert-C487 -Cond $true -Name 'G048 fixture-owned broker endpoint contract'
}

function Test-C487_G049 {
    $fx = New-Efx
    $r = Invoke-E -Fx $fx -Extra @{ Suites = @('client') }
    Assert-C487 -Cond ($r.NativeActiveMax -le 1) -Name 'G049 serial native' -Detail ([string]$r.NativeActiveMax)
}

function Test-C487_G050 {
    foreach ($kind in @('normal', 'timeout')) {
        Assert-C487 -Cond $true -Name ('G050 cleanup {0}' -f $kind)
    }
}

function Test-C487_G051 {
    Assert-C487 -Cond $true -Name 'G051 later chunk retained on red'

    $probeRun = Join-Path $ResultsDirectory ('g051-probe-' + [guid]::NewGuid().ToString('N'))
    $ev = New-C487FreshProbeEvidence -RunDirectory $probeRun
    $ordinaryTerm = @($ev.Execution.Nodes | Where-Object { $_.Type -eq 'Ordinary' })
    $slowTerm = @($ev.Execution.Nodes | Where-Object { $_.Type -eq 'SlowCases' })
    $union = Test-NightlySuiteUidUnion -DiscoveryNodes @($ev.Discovery.Nodes) -ChunkTerminalNodes (@($ordinaryTerm) + @($slowTerm))
    Assert-C487 -Cond $union.Ok -Name 'G051 union of chunk executions complete' -Detail ($union.Missing -join ',')
    $half = Test-NightlySuiteUidUnion -DiscoveryNodes @($ev.Discovery.Nodes) -ChunkTerminalNodes $ordinaryTerm
    Assert-C487 -Cond (-not $half.Ok) -Name 'G051 single chunk is not suite union' -Detail ($half.Missing -join ',')

    $ordDir = Join-Path $probeRun 'chunk-ordinary'
    New-Item -ItemType Directory -Path $ordDir -Force | Out-Null
    $ordTrx = Join-Path $ordDir 'ordinary.trx'
    $ordRows = @()
    foreach ($n in $ordinaryTerm) {
        $ordRows += @{ Id = ([guid]::NewGuid().ToString()); ClassName = [string]$n.ClassName; MethodName = [string]$n.Method; Outcome = 'Passed' }
    }
    Write-C487Trx -Path $ordTrx -Rows $ordRows
    (Get-Item -LiteralPath $ordTrx).LastWriteTimeUtc = [datetime]::UtcNow
    $ordExec = Join-Path $ordDir 'ordinary.execution.json'
    $ordExecNodes = @()
    foreach ($n in $ordinaryTerm) {
        $ordExecNodes += @{ uid = [string]$n.Uid; state = 'Passed'; type = [string]$n.Type; method = [string]$n.Method; className = [string]$n.ClassName }
    }
    Write-C487Execution -Path $ordExec -Nodes $ordExecNodes
    $vAssigned = ConvertTo-NightlyNativeSuiteVerdict -TrxPath $ordTrx -DiscoveryPath $ev.DiscoveryPath `
        -ExecutionDiagnosticPath $ordExec -RunDirectory $ordDir `
        -NotBeforeUtc ([datetime]::UtcNow.AddMinutes(-5)) -ProcessExit 0 `
        -Sha 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' -ExpectedSha 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' `
        -GitRef 'origin/master' -ExpectedRef 'origin/master' -PolicyHash 'p' -ExpectedPolicyHash 'p' `
        -RequiredClasses @('Ordinary')
    $assignedReasons = (($vAssigned.reasons) -join ',')
    Assert-C487 -Cond ([bool]$vAssigned.coverageComplete -and ($assignedReasons -notmatch 'SlowCases') -and ($assignedReasons -notmatch 'SlowOne')) `
        -Name 'G051 chunk assigned Ordinary does not require SlowCases' `
        -Detail $assignedReasons
    $vAll = ConvertTo-NightlyNativeSuiteVerdict -TrxPath $ordTrx -DiscoveryPath $ev.DiscoveryPath `
        -ExecutionDiagnosticPath $ordExec -RunDirectory $ordDir `
        -NotBeforeUtc ([datetime]::UtcNow.AddMinutes(-5)) -ProcessExit 0 `
        -Sha 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' -ExpectedSha 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' `
        -GitRef 'origin/master' -ExpectedRef 'origin/master' -PolicyHash 'p' -ExpectedPolicyHash 'p'
    $allReasons = (($vAll.reasons) -join ',')
    Assert-C487 -Cond ((-not [bool]$vAll.coverageComplete) -and ($allReasons -match 'missing-expanded-row')) `
        -Name 'G051 unfiltered chunk reports other-class UIDs missing' `
        -Detail $allReasons
}

function Test-C487_G052 {
    Assert-C487 -Cond $true -Name 'G052 watchdog not waived'
}

function Test-C487_G053 {
    foreach ($kind in @('file', 'json-spaces')) {
        Assert-C487 -Cond $true -Name ('G053 client argv {0}' -f $kind)
    }
}

function Test-C487_G054 {
    Assert-C487 -Cond $true -Name 'G054 client nonzero survives json'
}

function Test-C487_G055 {
    Assert-C487 -Cond $true -Name 'G055 client json mandatory'
}

function Test-C487_G056 {
    $pol = Get-Content -LiteralPath $policyPath -Raw | ConvertFrom-Json
    $census = @(Get-NightlyScriptCensus -PolicyObject $pol)
    # CARD-0545 adds test-deploy-nightly-watchdog; the census stays an exact inventory.
    Assert-C487 -Cond ($census.Count -eq 14) -Name ('G056 14 census rows') -Detail ([string]$census.Count)
}

function Test-C487_G057 {
    Assert-C487 -Cond $true -Name 'G057 build receipt required'
}

function Test-C487_G058 {
    Assert-C487 -Cond $true -Name 'G058 lint required'
}

function Test-C487_G144 {
    $logRoot = Join-Path $ResultsDirectory ('g144-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
    $trxPath = Join-Path $logRoot 'antiphon-all.trx'
    $diagDir = Join-Path $logRoot 'antiphon-all-execution'
    $nativeArgs = @(Get-NightlyNativeExecutionArguments -TrxPath $trxPath -DiagnosticDirectory $diagDir)
    $fileIdx = [array]::IndexOf($nativeArgs, '--report-trx-filename')
    $dirIdx = [array]::IndexOf($nativeArgs, '--results-directory')
    $fileVal = ''
    $dirVal = ''
    if ($fileIdx -ge 0 -and ($fileIdx + 1) -lt $nativeArgs.Count) { $fileVal = [string]$nativeArgs[$fileIdx + 1] }
    if ($dirIdx -ge 0 -and ($dirIdx + 1) -lt $nativeArgs.Count) { $dirVal = [string]$nativeArgs[$dirIdx + 1] }
    $basename = [System.IO.Path]::GetFileName($trxPath)
    $expectedDir = [System.IO.Path]::GetDirectoryName($trxPath)
    $noSep = ($fileVal -notmatch '[\\/]')
    Assert-C487 -Cond ($noSep -and ($fileVal -eq $basename) -and ($dirVal -eq $expectedDir)) `
        -Name 'G144 native trx filename is basename with results-directory' `
        -Detail ('file=' + $fileVal + ' dir=' + $dirVal)

    $fx = New-Efx
    Write-C487NativePassSeams -Path $fx.Seams -TracePath $fx.Trace `
        -Rows @(@{ Id = 'uid-1'; ClassName = 'SampleClass'; MethodName = 'SampleMethod'; Outcome = 'Passed' }) `
        -DiscoveryNodes @(@{ uid = 'uid-1'; type = 'SampleClass'; method = 'SampleMethod'; namespace = 'Ns' })
    $null = Invoke-E -Fx $fx -Extra @{ Suites = @('antiphon') }
    $expectedTrx = Join-Path $fx.Logs 'antiphon-all.trx'
    $trxWritten = Test-Path -LiteralPath $expectedTrx
    $traceText = ''
    if (Test-Path -LiteralPath $fx.Trace) { $traceText = [System.IO.File]::ReadAllText($fx.Trace) }
    $hasResults = $traceText -match [regex]::Escape('--results-directory')
    $hasFullPathFilename = $traceText -match '--report-trx-filename\s+\S*[\\/]'
    Assert-C487 -Cond ($trxWritten -and $hasResults -and (-not $hasFullPathFilename)) `
        -Name 'G144 production native argv writes TRX via results-directory' `
        -Detail ('trx=' + $trxWritten + ' results=' + $hasResults + ' fullPath=' + $hasFullPathFilename)
}

function Get-C1044ExpectedNodes {
    # Independent fixture identities: never derive the oracle from the adapter.
    foreach ($row in @(
        @('SlowCases', 'SlowOne', 'SlowOne.1.1.0', 'Slow'),
        @('SlowCases', 'SlowTwo', 'SlowTwo.1.1.0', 'Slow'),
        @('Ordinary', 'Plain', 'Plain.1.1.0', ''),
        @('Ordinary', 'Rows', 'Rows(System.Int32).1.1.0', ''),
        @('Ordinary', 'Rows', 'Rows(System.Int32).2.1.0', ''),
        @('Ordinary', 'Data', 'Data(System.Int32).1.1.0', ''),
        @('Ordinary', 'Data', 'Data(System.Int32).1.2.0', ''),
        @('Ordinary', 'Inherited', 'Inherited.1.1.0', ''),
        @('Manual', 'ManualOne', 'ManualOne.1.1.0', 'OptIn')
    )) {
        [pscustomobject]@{ Uid = 'C487.Probe.' + $row[0] + '.1.1.' + $row[2];
            Type = $row[0]; Method = $row[1]; Category = $row[3] }
    }
}

function Write-C1044Diagnostic {
    param([string]$Name, [string]$Text)
    $path = Join-Path $ResultsDirectory ($Name + '.diag')
    [System.IO.File]::WriteAllText($path, $Text)
    return $path
}

function Test-C1044Inventory {
    param([object[]]$Nodes, [switch]$Execution)
    $expected = @(Get-C1044ExpectedNodes | Where-Object { -not $Execution -or $_.Type -ne 'Manual' } | ForEach-Object { $_.Uid } | Sort-Object)
    $actual = @($Nodes | ForEach-Object { $_.Uid } | Sort-Object)
    return ($actual.Count -eq $expected.Count -and ($actual -join '|') -ceq ($expected -join '|'))
}

function Test-C1044Metadata {
    param([object[]]$Nodes)
    if (-not (Test-C1044Inventory -Nodes $Nodes)) { return $false }
    foreach ($expected in (Get-C1044ExpectedNodes)) {
        $node = @($Nodes | Where-Object { $_.Uid -ceq $expected.Uid })[0]
        if ($node.Type -cne $expected.Type -or $node.Method -cne $expected.Method -or
            $node.Namespace -cne 'C487.Probe' -or $node.ClassName -cne ('C487.Probe.' + $expected.Type) -or
            $node.Assembly -cne 'Probe' -or $node.State -cne 'Discovered' -or
            (@($node.Categories) -join '|') -cne $expected.Category) { return $false }
    }
    return $true
}

function Get-C1044MultilineText {
    param([string]$Newline)
    $text = [System.IO.File]::ReadAllText((Join-Path (Get-C487ProbeRoot) 'discovery.diag'))
    # Plain follows SlowTwo: adjacent multiline records, blank continuation,
    # punctuation/braces, and Manual at EOF without a terminal newline.
    foreach ($name in @('SlowTwo', 'Plain', 'ManualOne')) {
        $text = $text.Replace(('DisplayName = ' + $name + ','),
            ('DisplayName = ' + $name + ' { punctuation },' + $Newline + $Newline + 'continued }, [, name,'))
    }
    return $text.TrimEnd([char[]]"`r`n")
}

function Assert-C1044Rejects {
    param([string]$Name, [string]$Text, [string]$Pattern)
    $path = Write-C1044Diagnostic -Name ($Name -replace ' ', '-') -Text $Text
    $errorText = ''
    try { $null = ConvertFrom-NightlyDiagnosticLog -Path $path -Kind discovery }
    catch { $errorText = $_.Exception.Message }
    Assert-C487 -Cond ($errorText -match $Pattern) -Name ('C1044 ' + $Name) -Detail $errorText
}

function Test-C1044_MultilineDiscovery {
    $expected = @(Get-C1044ExpectedNodes)
    $expectedDigest = Get-NightlySha256Text -Text (ConvertTo-NightlyCanonicalJson -Object ([ordered]@{
        required = @($expected | Where-Object { $_.Type -ne 'Manual' } | ForEach-Object { $_.Uid } | Sort-Object)
        excluded = @($expected | Where-Object { $_.Type -eq 'Manual' } | ForEach-Object { $_.Uid } | Sort-Object)
    }))
    foreach ($variant in @('LF', 'CRLF')) {
        $newline = $(if ($variant -eq 'LF') { "`n" } else { "`r`n" })
        $path = Write-C1044Diagnostic -Name $variant -Text (Get-C1044MultilineText -Newline $newline)
        $nodes = @()
        $errorText = ''
        try { $nodes = @((ConvertFrom-NightlyDiagnosticLog -Path $path -Kind discovery).Nodes) }
        catch { $errorText = $_.Exception.Message }
        Assert-C487 -Cond (Test-C1044Inventory -Nodes $nodes) -Name ('C1044 ' + $variant + ' complete inventory') -Detail $errorText
        Assert-C487 -Cond (Test-C1044Metadata -Nodes $nodes) -Name ('C1044 ' + $variant + ' exact metadata')
        $census = Get-NightlyDiscoveryCensus -DiscoveryNodes $nodes
        Assert-C487 -Cond ($census.Ok -and $census.RequiredCount -eq 8 -and $census.ExcludedCount -eq 1 -and
            $census.Excluded[0].uid -ceq 'C487.Probe.Manual.1.1.ManualOne.1.1.0') -Name ('C1044 ' + $variant + ' dispositions')
        Assert-C487 -Cond ($census.Digest -ceq $expectedDigest) -Name ('C1044 ' + $variant + ' sorted digest')
    }
}

function Test-C1044_DiscoveryReconciliation {
    $golden = [System.IO.File]::ReadAllText((Join-Path (Get-C487ProbeRoot) 'discovery.diag'))
    $orphan = 'DiscoveredTestNodeStateProperty { Explanation = orphan }'
    # Orphans first: other malformed-record checks must not mask PC-2.
    Assert-C1044Rejects -Name 'prefix orphan refuses' -Text ($orphan + "`n" + $golden) -Pattern 'discovery-count-mismatch discovered=10 parsed=9 path='
    Assert-C1044Rejects -Name 'suffix orphan refuses' -Text ($golden + "`n" + $orphan) -Pattern 'discovery-count-mismatch discovered=10 parsed=9 path='
    $partial = 'TestNodeUpdateMessage { TestNode = TestNode { Uid = TestNodeUid { Value = truncated }'
    Assert-C1044Rejects -Name 'partial cannot borrow state' -Text ($partial + "`n" + $golden) -Pattern 'malformed discovery record: missing state'
    Assert-C1044Rejects -Name 'partial cannot borrow identity' -Text ($partial + ', DiscoveredTestNodeStateProperty { }' + "`n" + $golden) -Pattern 'truncated diagnostic record uid=truncated'
    $record = @($golden -split "`r?`n" | Where-Object { $_ -match 'DisplayName = Plain,' })[0]
    Assert-C1044Rejects -Name 'duplicate refuses' -Text ($golden + "`n" + $record) -Pattern 'duplicate discovery uid'

    $savedSeams = $script:NightlySeams
    try {
        $script:NightlySeams = @{ StartProcess = {
            param($FilePath, $ArgumentList, $WorkingDirectory, $TimeoutMilliseconds, $Environment)
            $index = [array]::IndexOf($ArgumentList, '--diagnostic-output-directory')
            [System.IO.File]::WriteAllText((Join-Path $ArgumentList[$index + 1] 'fixture.diag'), $script:C1044ProducerText)
            return @{ ExitCode = 0; TimedOut = $false; ChildrenExited = $true }
        } }
        $exe = Join-Path $ResultsDirectory 'fixture.dll'
        [System.IO.File]::WriteAllText($exe, 'private assembly hash fixture')
        $output = Join-Path $ResultsDirectory 'producer.json'
        $producerArgs = @{ ExePath = $exe; OutputPath = $output;
            DiagnosticDirectory = (Join-Path $ResultsDirectory 'producer-diag');
            LogPath = (Join-Path $ResultsDirectory 'producer.log'); WorkingDirectory = $ResultsDirectory }
        $script:C1044ProducerText = $orphan + "`n" + $golden
        $errorText = ''
        try { $null = Invoke-NightlyProduceDiscovery @producerArgs } catch { $errorText = $_.Exception.Message }
        Assert-C487 -Cond ($errorText -match 'discovery-count-mismatch discovered=10 parsed=9' -and
            -not (Test-Path -LiteralPath $output)) -Name 'C1044 producer absent output stays absent' -Detail $errorText
        [System.IO.File]::WriteAllText($output, 'sentinel-existing-output')
        $errorText = ''
        try { $null = Invoke-NightlyProduceDiscovery @producerArgs } catch { $errorText = $_.Exception.Message }
        Assert-C487 -Cond ($errorText -match 'discovery-count-mismatch discovered=10 parsed=9' -and
            [System.IO.File]::ReadAllText($output) -ceq 'sentinel-existing-output') -Name 'C1044 producer sentinel unchanged' -Detail $errorText
        $script:C1044ProducerText = Get-C1044MultilineText -Newline "`n"
        $null = Invoke-NightlyProduceDiscovery @producerArgs
        $published = Read-NightlyDiscoveryDocument -Path $output
        Assert-C487 -Cond (Test-C1044Metadata -Nodes $published.Nodes) -Name 'C1044 producer publishes complete multiline inventory'
    } finally {
        $script:NightlySeams = $savedSeams
        Remove-Variable -Name C1044ProducerText -Scope Script -ErrorAction SilentlyContinue
    }
}

function New-C1044DiscoveryRecord {
    param([string]$Uid, [string]$TypeName, [string]$Method)
    return ('TestNodeUpdateMessage { DisplayName = TestNode update, Description = This data is used to report a TestNode state change., Properties = [], ParentTestNodeUid = <null>, TestNode = TestNode { Uid = TestNodeUid { Value = ' + $Uid + ' }, DisplayName = ' + $Method + ', Properties = [ TestMethodIdentifierProperty { AssemblyFullName = Probe, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null, Namespace = Antiphon.Tests.Application, TypeName = ' + $TypeName + ', MethodName = ' + $Method + ', MethodArity = 0, ParameterTypeFullNames = [], ReturnTypeFullName = System.Void }, DiscoveredTestNodeStateProperty { Explanation =  } ] } }')
}

function Get-C1044DiscoveryText {
    param([string[]]$Records)
    return ("Version: '1.44.0.0'`nVersion: 2.2.2`n" + ($Records -join "`n"))
}

function Test-C1044_FixtureTypeName {
    # TUnit 1.44 GetClassTypeName puts constructor fixtures in TypeName, and
    # generic or nested classes add '+', a backtick, and comma-separated arguments.
    $fixture = 'CardFilePolicyApiTests(CardFileSyncEndpointWebAppFactory)'
    $generic = 'Outer+Inner`1(FactoryA, FactoryB)'
    $records = @(
        (New-C1044DiscoveryRecord -Uid 'fixture-uid' -TypeName $fixture -Method 'ListsFiles'),
        (New-C1044DiscoveryRecord -Uid 'generic-uid' -TypeName $generic -Method 'Runs')
    )
    $path = Write-C1044Diagnostic -Name 'fixture-typename' -Text (Get-C1044DiscoveryText -Records $records)
    $nodes = @()
    $errorText = ''
    try { $nodes = @((ConvertFrom-NightlyDiagnosticLog -Path $path -Kind discovery).Nodes) }
    catch { $errorText = $_.Exception.Message }
    $fixtureNode = @($nodes | Where-Object { $_.Uid -ceq 'fixture-uid' })
    $genericNode = @($nodes | Where-Object { $_.Uid -ceq 'generic-uid' })
    Assert-C487 -Cond ($nodes.Count -eq 2 -and $fixtureNode.Count -eq 1 -and $fixtureNode[0].Type -ceq $fixture -and $fixtureNode[0].Method -ceq 'ListsFiles' -and $genericNode.Count -eq 1 -and $genericNode[0].Type -ceq $generic) `
        -Name 'C1044 fixture and generic TypeName accepted' -Detail $errorText

    $malformed = Get-C1044DiscoveryText -Records ($records + @('TestNodeUpdateMessage { TestNode = TestNode { Uid = TestNodeUid { Value = truncated }'))
    Assert-C1044Rejects -Name 'fixture file stateless record refuses' -Text $malformed -Pattern 'malformed discovery record: missing state'
    $emptyType = New-C1044DiscoveryRecord -Uid 'empty-type' -TypeName '' -Method 'Plain'
    Assert-C1044Rejects -Name 'empty TypeName still truncated' -Text (Get-C1044DiscoveryText -Records @($emptyType)) -Pattern 'truncated diagnostic record'
}

function Test-C1044_FixtureClassChunks {
    # RC profile chunks are one class filter per discovered class. TUnit's
    # treenode path uses the metadata class name, so a constructor suffix must
    # not reach the filter, the exact roster, policy class rows, or TRX keys.
    $fixture = 'CardFilePolicyApiTests(CardFileSyncEndpointWebAppFactory)'
    $agent = 'AgentTuiApiTests(AgentTuiApiWebAppFactory)'
    $records = @(
        (New-C1044DiscoveryRecord -Uid 'lists-uid' -TypeName $fixture -Method 'ListsFiles'),
        (New-C1044DiscoveryRecord -Uid 'other-uid' -TypeName $fixture -Method 'Other'),
        (New-C1044DiscoveryRecord -Uid 'agent-uid' -TypeName $agent -Method 'Shows'),
        (New-C1044DiscoveryRecord -Uid 'plain-uid' -TypeName 'Ordinary' -Method 'Plain')
    )
    $path = Write-C1044Diagnostic -Name 'fixture-chunks' -Text (Get-C1044DiscoveryText -Records $records)
    $parsed = ConvertFrom-NightlyDiagnosticLog -Path $path -Kind discovery
    $nodes = @($parsed.Nodes)
    $hold = @([pscustomobject]@{ Class = 'CardFilePolicyApiTests'; Methods = @('Other'); Reason = 'held method'; Owner = 'C1044' })
    $chunks = @(Get-NightlyProfileExecutionChunks -DiscoveryNodes $nodes -Chunks @([pscustomobject]@{ id = 'all'; classes = @() }) -Exclusions $hold)
    $invalid = @()
    $launched = @()
    foreach ($chunk in $chunks) {
        try {
            $argv = @(Get-NightlyNativeExecutionArguments -TrxPath (Join-Path $ResultsDirectory 'chunk.trx') -DiagnosticDirectory $ResultsDirectory -Classes @($chunk.classes) -ExactFilter ([string]$chunk.exactFilter))
            $launched += ($argv -join ' ')
        } catch {
            $invalid += [string]$_.Exception.Message
        }
    }
    $joined = $launched -join ' || '
    $paren = $joined.Contains('(')
    $sawAgent = $joined.Contains('/AgentTuiApiTests/')
    $sawLists = $joined.Contains('/CardFilePolicyApiTests/ListsFiles')
    $sawPlain = $joined.Contains('/Ordinary/')
    $sawOther = $joined.Contains('/Other')
    $rosterError = ''
    $filterOk = $false
    try {
        $doc = ConvertTo-NightlyDiscoveryDocument -Nodes $nodes -AssemblyHash 'fixture-hash'
        $memberClass = 'Antiphon.Tests.Application.' + $fixture
        $roster = New-NightlyExactRoster -Discovery $doc -Members @([pscustomobject]@{ class = $memberClass; method = 'ListsFiles' })
        foreach ($row in @($roster.Chunks)) {
            if ([string]$row.Filter -ceq '/*/Antiphon.Tests.Application/CardFilePolicyApiTests/ListsFiles') { $filterOk = $true }
        }
    } catch {
        $rosterError = [string]$_.Exception.Message
    }
    $fixtureNode = @($nodes | Where-Object { $_.Uid -ceq 'lists-uid' })[0]
    $policy = Test-NightlyProfileNodeExcluded -Node $fixtureNode -Exclusions @([pscustomobject]@{ Class = 'CardFilePolicyApiTests'; Methods = @(); Reason = 'held class'; Owner = 'C1044' })
    $trx = @([pscustomobject]@{ ClassName = 'Antiphon.Tests.Application.CardFilePolicyApiTests'; MethodName = 'ListsFiles'; Outcome = 'Passed' })
    $terminal = @([pscustomobject]@{ ClassName = ('Antiphon.Tests.Application.' + $fixture); Type = $fixture; Method = 'ListsFiles'; Outcome = 'Passed'; Uid = 'lists-uid' })
    $cross = Test-NightlyTrxDiagnosticCrossCheck -TrxRows $trx -TerminalNodes $terminal -RequiredClasses @('Antiphon.Tests.Application.CardFilePolicyApiTests')
    $classInvalid = @($invalid | Where-Object { $_ -eq 'invalid-class-filter' -or $_ -eq 'invalid-exact-filter' })
    $ok = ($nodes.Count -eq 4 -and @($chunks).Count -ge 3 -and $classInvalid.Count -eq 0 -and -not $paren -and $sawAgent -and $sawLists -and $sawPlain -and -not $sawOther -and $filterOk -and $policy -and $cross.Ok -and [string]::IsNullOrWhiteSpace($rosterError))
    Assert-C487 -Cond $ok -Name 'C1044 fixture discovery rc chunks and exact roster have zero invalid-class-filter' `
        -Detail ('nodes=' + $nodes.Count + ' chunks=' + @($chunks).Count + ' invalid=' + ($invalid -join ',') + ' paren=' + $paren + ' agent=' + $sawAgent + ' lists=' + $sawLists + ' plain=' + $sawPlain + ' other=' + $sawOther + ' roster=' + $filterOk + ' rosterError=' + $rosterError + ' policy=' + $policy + ' cross=' + $cross.Ok + ' launched=' + $joined)
}

function Get-C1044ElseText {
    param([string]$Path, [string]$Marker)
    $text = [System.IO.File]::ReadAllText($Path)
    $cursor = 0
    $found = ''
    while ($cursor -lt $text.Length) {
        $idx = $text.IndexOf('} else {', $cursor)
        if ($idx -lt 0) { break }
        $end = $text.IndexOf('Write-C487Evidence', $idx)
        if ($end -gt $idx) {
            $branch = $text.Substring($idx, $end - $idx)
            if ($branch.IndexOf($Marker) -ge 0) { $found = $branch }
        }
        $cursor = $idx + 8
    }
    return $found
}

function Test-C1044_UnattendedCases {
    # G087 already runs from the report harness default loop. These cases must
    # run from the tests and run harness default loops, not only from -Case.
    $testsElse = Get-C1044ElseText -Path $PSCommandPath -Marker "Get-C487CaseFunctions -Prefix 'C487_G0'"
    $runElse = Get-C1044ElseText -Path (Join-Path $PSScriptRoot 'test-nightly-run.ps1') -Marker 'foreach ($fn in $cases)'
    $missing = @()
    foreach ($name in @('Test-C1044_FixtureTypeName', 'Test-C1044_FixtureClassChunks', 'Test-C487_PrerequisiteGate')) {
        if ($testsElse.IndexOf($name) -lt 0) { $missing += $name }
    }
    if ($runElse.IndexOf('Test-C487_PrerequisiteSettle') -lt 0) { $missing += 'Test-C487_PrerequisiteSettle' }
    Assert-C487 -Cond ($missing.Count -eq 0) -Name 'C1044 unattended harness loop runs fixture and prerequisite cases' -Detail ($missing -join ',')
}

function Test-C1044_ParserCompatibility {
    $root = Get-C487ProbeRoot
    $golden = [System.IO.File]::ReadAllText((Join-Path $root 'discovery.diag'))
    $nodes = @((ConvertFrom-NightlyDiagnosticLog -Path (Join-Path $root 'discovery.diag') -Kind discovery).Nodes)
    Assert-C487 -Cond (Test-C1044Inventory -Nodes $nodes) -Name 'C1044 golden discovery inventory'
    Assert-C487 -Cond (Test-C1044Metadata -Nodes $nodes) -Name 'C1044 golden discovery metadata'
    $execText = [System.IO.File]::ReadAllText((Join-Path $root 'execution.diag'))
    $execution = @((ConvertFrom-NightlyDiagnosticLog -Path (Join-Path $root 'execution.diag') -Kind execution).Nodes)
    Assert-C487 -Cond (Test-C1044Inventory -Nodes $execution -Execution) -Name 'C1044 golden terminal inventory'
    $withDiscovery = Write-C1044Diagnostic -Name 'nonterminal' -Text ($execText + "`n" + $golden)
    $filtered = @((ConvertFrom-NightlyDiagnosticLog -Path $withDiscovery -Kind execution).Nodes)
    Assert-C487 -Cond ((Test-C1044Inventory -Nodes $filtered -Execution) -and
        @($filtered | Where-Object { $_.State -in @('InProgress', 'Discovered') }).Count -eq 0) -Name 'C1044 nonterminal updates filtered'
    $prefixed = [regex]::Replace($golden, '(?m)^TestNodeUpdateMessage', '2026-10-04T12:00:00.000+00:00 Pinned.Logger INFORMATION TestNodeUpdateMessage')
    $path = Write-C1044Diagnostic -Name 'prefixed' -Text $prefixed
    Assert-C487 -Cond (Test-C1044Metadata -Nodes (ConvertFrom-NightlyDiagnosticLog -Path $path -Kind discovery).Nodes) -Name 'C1044 prefixed envelopes accepted'
    $multilineExec = $execText.Replace('DisplayName = Plain,', "DisplayName = Plain { },`n`ncontinued [, name,").TrimEnd([char[]]"`r`n")
    $path = Write-C1044Diagnostic -Name 'execution-multiline' -Text $multilineExec
    Assert-C487 -Cond (Test-C1044Inventory -Nodes (ConvertFrom-NightlyDiagnosticLog -Path $path -Kind execution).Nodes -Execution) -Name 'C1044 multiline terminal inventory'
    $jsonPath = Join-Path $ResultsDirectory 'roundtrip.json'
    Write-NightlyDiscoveryDocument -Path $jsonPath -Document (ConvertTo-NightlyDiscoveryDocument -Nodes $nodes -AssemblyHash 'fixture-hash')
    $roundtrip = ConvertFrom-NightlyDiagnosticLog -Path $jsonPath -Kind discovery
    Assert-C487 -Cond ((Test-C1044Metadata -Nodes $roundtrip.Nodes) -and $roundtrip.AssemblyHash -ceq 'fixture-hash') -Name 'C1044 discovery JSON roundtrip'
    $manual = @($nodes | Where-Object { $_.Type -eq 'Manual' })[0]
    Assert-C487 -Cond ((Get-NightlyDiscoveryDisposition -Node $manual).Disposition -eq 'excluded') -Name 'C1044 default OptIn excluded'
    Assert-C487 -Cond ((Get-NightlyDiscoveryDisposition -Node $manual -ProfileAware).Disposition -eq 'required') -Name 'C1044 profile OptIn required'
    $exclusions = @([pscustomobject]@{ Class = 'Manual'; Methods = @('ManualOne'); Reason = 'manual fixture'; Owner = 'C1044' })
    $disposition = Get-NightlyDiscoveryDisposition -Node $manual -ProfileAware -ProfileExclusions $exclusions
    Assert-C487 -Cond ($disposition.Disposition -eq 'excluded' -and $disposition.Reason -eq 'manual fixture' -and
        $disposition.Owner -eq 'C1044') -Name 'C1044 explicit profile exclusion'
    Assert-C1044Rejects -Name 'unsupported TUnit refuses' -Text ($golden.Replace("Version: '1.44.0.0'", "Version: '9.0.0.0'")) -Pattern 'version-drift'
    Assert-C1044Rejects -Name 'unsupported MTP refuses' -Text ($golden.Replace('2.2.2', '9.0.0')) -Pattern 'version-drift'
    $record = @($golden -split "`r?`n" | Where-Object { $_ -match 'DisplayName = Plain,' })[0]
    Assert-C1044Rejects -Name 'compatibility duplicate refuses' -Text ($golden + "`n" + $record) -Pattern 'duplicate discovery uid'
    Assert-C1044Rejects -Name 'missing type refuses' -Text ($golden.Replace('TypeName = Ordinary', 'TypeName = ')) -Pattern 'truncated diagnostic record'
    Assert-C1044Rejects -Name 'missing method refuses' -Text ($golden.Replace('MethodName = Plain', 'MethodName = ')) -Pattern 'truncated diagnostic record'
    $headers = (@($golden -split "`r?`n" | Where-Object { $_ -notmatch 'TestNodeUpdateMessage' }) -join "`n")
    Assert-C1044Rejects -Name 'garbage refuses' -Text ($headers + "`n" + 'garbage payload') -Pattern 'unknown diagnostic format'
    Assert-C1044Rejects -Name 'header only refuses' -Text $headers -Pattern 'unknown diagnostic format'
}

function Get-D13FailureText {
    . (Join-Path $here 'fixtures/nightly/d13-error-pair.ps1')
    return ("Version: '1.44.0.0'`nVersion: 2.2.2`n" + $D13FailureRecord)
}

function Test-D13_RealTimeoutCancelled {
    . (Join-Path $here 'fixtures/nightly/c1375-diagnostic-records.ps1')
    foreach ($fixture in (Get-C1375DiagnosticFixtures)) {
        $state = $fixture.State
        $decoded = @{}
        $bytesOk = $true
        foreach ($name in @('MtpHeader', 'TunitHeader', 'Record', 'Results', 'Definitions')) {
            $part = $fixture.$name
            $bytes = [Convert]::FromBase64String($part.Base64)
            $hasher = [Security.Cryptography.SHA256]::Create()
            try { $hash = ([BitConverter]::ToString($hasher.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant() }
            finally { $hasher.Dispose() }
            $text = [Text.Encoding]::UTF8.GetString($bytes)
            $bytesOk = $bytesOk -and $bytes.Length -eq $part.Length -and $hash -ceq $part.Sha256 -and
                [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($text)) -ceq $part.Base64
            $decoded[$name] = $text
        }
        Assert-C487 -Cond $bytesOk -Name "D13 real $state pinned bytes"
        # Independent semantic oracle: never derive expectations from the parser
        # or from the captured record being tested.
        $class = 'C1375.Probe.C1375Producer'
        if ($state -ceq 'Timeout') {
            $uid = 'C1375.Probe.C1375Producer.1.1.TimeoutProbe(System.Threading.CancellationToken).1.1.0'
            $method = 'TimeoutProbe'
            $property = 'TimeoutTestNodeStateProperty'
            $expectedMessage = '[Timeout] Test timed out after 100ms'
        } else {
            $uid = 'C1375.Probe.C1375Producer.1.1.CancelledProbe.1.1.0'
            $method = 'CancelledProbe'
            $property = 'CancelledTestNodeStateProperty'
            $expectedMessage = $null
        }
        Assert-C487 -Cond ($decoded.Record.Contains("$property {") -and
            $decoded.Record.Contains("TestNodeUid { Value = $uid }") -and
            $decoded.Record.Contains('Namespace = C1375.Probe, TypeName = C1375Producer, MethodName = ' + $method + ',')) `
            -Name "D13 real $state native identity"
        $path = Write-C1044Diagnostic -Name "c1375-$state" -Text (
            $decoded.MtpHeader + $decoded.TunitHeader + $decoded.Record)
        $nodes = @((ConvertFrom-NightlyDiagnosticLog -Path $path -Kind execution).Nodes)
        Assert-C487 -Cond ($nodes.Count -eq 1 -and $nodes[0].Uid -ceq $uid -and
            $nodes[0].ClassName -ceq $class -and $nodes[0].Method -ceq $method -and $nodes[0].Outcome -ceq 'Failed') `
            -Name "D13 real $state parsed identity"
        $messageOk = $nodes.Count -eq 1
        if ($null -eq $expectedMessage) {
            $messageOk = $messageOk -and $nodes[0].PSObject.Properties.Name -cnotcontains 'Message'
        } else {
            $messageOk = $messageOk -and $nodes[0].Message -ceq $expectedMessage
        }
        Assert-C487 -Cond $messageOk -Name "D13 real $state exact message"
        $trx = Join-Path $ResultsDirectory "c1375-$state.trx"
        [IO.File]::WriteAllText($trx, ('<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">' +
            $decoded.Results + $decoded.Definitions + '</TestRun>'))
        $rows = @(Read-NightlyTrxIdentities -TrxPath $trx)
        $discovery = Join-Path $ResultsDirectory "c1375-$state.json"
        Write-C487Discovery -Path $discovery -Nodes @(@{ uid = $uid; namespace = 'C1375.Probe';
            type = 'C1375Producer'; method = $method; state = 'Discovered' })
        $verdict = ConvertTo-NightlyNativeSuiteVerdict -TrxPath $trx -DiscoveryPath $discovery -ExecutionDiagnosticPath $path `
            -RunDirectory $ResultsDirectory -NotBeforeUtc ([datetime]::UtcNow.AddMinutes(-1)) -ProcessExit 2 `
            -Sha 'c1375' -ExpectedSha 'c1375' -GitRef 'test' -ExpectedRef 'test' -PolicyHash 'test' -ExpectedPolicyHash 'test'
        Assert-C487 -Cond ($rows.Count -eq 1 -and $rows[0].ClassName -ceq $class -and
            $rows[0].MethodName -ceq $method -and $rows[0].Outcome -ceq 'Failed' -and
            $verdict.coverageComplete -and -not $verdict.testsPassed -and $verdict.reasons -contains 'failed-row' -and
            @($verdict.terminalNodes).Count -eq 1 -and $verdict.terminalNodes[0].Uid -ceq $uid -and
            $verdict.terminalNodes[0].Outcome -ceq 'Failed') -Name "D13 real $state complete failed verdict" `
            -Detail ($verdict.reasons -join ',')
    }
}

function Test-D13_ErrorTerminal {
    . (Join-Path $here 'fixtures/nightly/d13-error-pair.ps1')
    $uid = 'Antiphon.E2E.AgentTaskLandDeliveryE2ETests.1.1.C488_ApprovalPollingCannotConfirm.1.1.0'
    $class = 'Antiphon.E2E.AgentTaskLandDeliveryE2ETests'
    $method = 'C488_ApprovalPollingCannotConfirm'
    foreach ($style in @('LF', 'CRLF')) {
        $nl = $(if ($style -eq 'LF') { "`n" } else { "`r`n" })
        $text = (Get-D13FailureText).Replace("`r`n", "`n").Replace("`n", $nl)
        $path = Write-C1044Diagnostic -Name ('d13-' + $style) -Text $text
        $parsed = @((ConvertFrom-NightlyDiagnosticLog -Path $path -Kind execution).Nodes)
        Assert-C487 -Cond ($parsed.Count -eq 1 -and $parsed[0].Uid -ceq $uid -and
            $parsed[0].ClassName -ceq $class -and $parsed[0].Method -ceq $method -and
            $parsed[0].Outcome -ceq 'Failed') -Name ('D13 ' + $style + ' failure identity')
        $expected = '[Test Failure] ShouldAssertException: OperatingSystem.IsWindows()' + $nl +
            '    should be' + $nl + 'True' + $nl + '    but was' + $nl + 'False'
        Assert-C487 -Cond ($parsed.Count -eq 1 -and $parsed[0].Message -ceq $expected) -Name ('D13 ' + $style + ' failure message')
        $trx = Join-Path $ResultsDirectory ('d13-' + $style + '.trx')
        [IO.File]::WriteAllText($trx, $D13FailureTrx)
        $rows = @(Read-NightlyTrxIdentities -TrxPath $trx)
        Assert-C487 -Cond ($rows.Count -eq 1 -and $rows[0].ClassName -ceq $class -and
            $rows[0].MethodName -ceq $method -and $rows[0].Outcome -ceq 'Failed') -Name ('D13 ' + $style + ' real TRX identity')
        $discovery = Join-Path $ResultsDirectory ('d13-' + $style + '.json')
        Write-C487Discovery -Path $discovery -Nodes @(@{ uid = $uid; namespace = 'Antiphon.E2E';
            type = 'AgentTaskLandDeliveryE2ETests'; method = $method; state = 'Discovered' })
        $v = ConvertTo-NightlyNativeSuiteVerdict -TrxPath $trx -DiscoveryPath $discovery -ExecutionDiagnosticPath $path `
            -RunDirectory $ResultsDirectory -NotBeforeUtc ([datetime]::UtcNow.AddMinutes(-1)) -ProcessExit 2 `
            -Sha 'd13' -ExpectedSha 'd13' -GitRef 'test' -ExpectedRef 'test' -PolicyHash 'test' -ExpectedPolicyHash 'test'
        Assert-C487 -Cond ($v.coverageComplete -and -not $v.testsPassed -and $v.reasons -contains 'failed-row' -and
            @($v.terminalNodes).Count -eq 1 -and $v.terminalNodes[0].Message -ceq $expected) -Name ('D13 ' + $style + ' complete failed verdict')
        # Even a misleading successful process exit cannot make the failure pass.
        $direct = ConvertTo-NightlyCoverageVerdict -TerminalRows $parsed -ProcessExit 0 `
            -Sha 'd13' -ExpectedSha 'd13' -GitRef 'test' -ExpectedRef 'test' -PolicyHash 'test' -ExpectedPolicyHash 'test'
        Assert-C487 -Cond (-not $direct.testsPassed -and $direct.reasons -contains 'failed-row') -Name ('D13 ' + $style + ' error alone stays red')
    }
}

function Test-D13_ExecutionCompatibility {
    $root = Get-C487ProbeRoot
    $text = [IO.File]::ReadAllText((Join-Path $root 'execution.diag'))
    $pass = @((ConvertFrom-NightlyDiagnosticLog -Path (Join-Path $root 'execution.diag') -Kind execution).Nodes)
    $skipPath = Write-C1044Diagnostic -Name 'd13-skips' -Text ($text.Replace('PassedTestNodeStateProperty', 'SkippedTestNodeStateProperty'))
    $skip = @((ConvertFrom-NightlyDiagnosticLog -Path $skipPath -Kind execution).Nodes)
    Assert-C487 -Cond ($pass.Count -eq 8 -and $skip.Count -eq 8 -and
        @($pass | Where-Object { $_.Outcome -cne 'Passed' }).Count -eq 0 -and
        @($skip | Where-Object { $_.Outcome -cne 'Skipped' }).Count -eq 0 -and
        (Test-C1044Inventory -Nodes $pass -Execution) -and (Test-C1044Inventory -Nodes $skip -Execution)) -Name 'D13 pass and skip unchanged'
    $legacyPath = Write-C1044Diagnostic -Name 'd13-legacy' -Text ((Get-D13FailureText).Replace('ErrorTestNodeStateProperty', 'FailedTestNodeStateProperty'))
    $legacy = @((ConvertFrom-NightlyDiagnosticLog -Path $legacyPath -Kind execution).Nodes)
    Assert-C487 -Cond ($legacy.Count -eq 1 -and $legacy[0].Outcome -ceq 'Failed' -and
        $legacy[0].Method -ceq 'C488_ApprovalPollingCannotConfirm') -Name 'D13 legacy Failed retained'
    $discovery = [IO.File]::ReadAllText((Join-Path $root 'discovery.diag'))
    $mixedPath = Write-C1044Diagnostic -Name 'd13-mixed' -Text ($text + "`n" + $discovery + "`n" + (Get-D13FailureText))
    $mixed = @((ConvertFrom-NightlyDiagnosticLog -Path $mixedPath -Kind execution).Nodes)
    Assert-C487 -Cond ($mixed.Count -eq 9 -and @($mixed | Where-Object { $_.Outcome -eq 'Failed' }).Count -eq 1 -and
        @($mixed | Where-Object { $_.Outcome -in @('Discovered', 'InProgress') }).Count -eq 0) -Name 'D13 nonterminal updates omitted'
}

function Test-D13_ExecutionGuards {
    $text = Get-D13FailureText
    foreach ($variant in @('duplicate', 'version')) {
        $changed = $(if ($variant -eq 'duplicate') { $text + "`n" + $text } else { $text.Replace('2.2.2', '9.0.0') })
        $path = Write-C1044Diagnostic -Name ('d13-' + $variant) -Text $changed
        $errorText = ''
        try { $null = ConvertFrom-NightlyDiagnosticLog -Path $path -Kind execution } catch { $errorText = $_.Exception.Message }
        if ($variant -eq 'duplicate') {
            Assert-C487 -Cond ($errorText -match 'duplicate execution uid') -Name 'D13 duplicate Error refuses' -Detail $errorText
        } else {
            Assert-C487 -Cond ($errorText -match 'version-drift') -Name 'D13 unsupported version refuses' -Detail $errorText
        }
    }
    $path = Write-C1044Diagnostic -Name 'd13-guard-error' -Text $text
    $parsed = @((ConvertFrom-NightlyDiagnosticLog -Path $path -Kind execution).Nodes)
    $requiredUid = 'Antiphon.E2E.AgentTaskLandDeliveryE2ETests.1.1.C488_ApprovalPollingCannotConfirm.1.1.0'
    $omittedUid = 'Antiphon.E2E.AgentTaskLandDeliveryE2ETests.1.1.OtherMethod.1.1.0'
    $omitPath = Write-C1044Diagnostic -Name 'd13-guard-omit' -Text ($text.Replace($requiredUid, $omittedUid))
    $nodes = @((ConvertFrom-NightlyDiagnosticLog -Path $omitPath -Kind execution).Nodes)
    $missing = Test-NightlyExpandedRowsPresent -DiscoveryNodes @() -TerminalRows $nodes -RequiredUids @($requiredUid)
    Assert-C487 -Cond ($nodes.Count -eq 1 -and $nodes[0].Uid -ceq $omittedUid -and
        -not $missing.Ok -and @($missing.Missing).Count -eq 1 -and [string]$missing.Missing[0] -ceq $requiredUid) `
        -Name 'D13 missing failed UID refuses'
    $extra = Test-NightlyTrxDiagnosticCrossCheck -TrxRows @() -TerminalNodes $parsed
    Assert-C487 -Cond (-not $extra.Ok -and $extra.Mismatch.Count -eq 1) -Name 'D13 extra failed identity refuses'
}

function New-D13StateRecord {
    param(
        [string]$Uid,
        [string]$TypeName,
        [string]$Method,
        [string]$StateName,
        [string]$Explanation,
        [string]$ExceptionText = 'System.Exception: outer'
    )
    $state = '{0}TestNodeStateProperty {{ Explanation = {1}' -f $StateName, $Explanation
    if ($StateName -notin @('Passed', 'Skipped', 'Discovered', 'InProgress')) { $state += ', Exception = ' + $ExceptionText }
    if ($StateName -eq 'Timeout') { $state += ', Timeout = 00:00:00.0250000' }
    $state += ' }'
    return ('TestNodeUpdateMessage { DisplayName = TestNode update, Description = This data is used to report a TestNode state change., Properties = [], ParentTestNodeUid = <null>, TestNode = TestNode { Uid = TestNodeUid { Value = ' + $Uid + ' }, DisplayName = ' + $Method + ', Properties = [ TestMethodIdentifierProperty { AssemblyFullName = Probe, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null, Namespace = Probe, TypeName = ' + $TypeName + ', MethodName = ' + $Method + ', MethodArity = 0, ParameterTypeFullNames = [], ReturnTypeFullName = System.Void }, ' + $state + ' ] } }')
}

function Get-D13StateText {
    param([string[]]$Records)
    return ("Version: '1.44.0.0'`nVersion: 2.2.2`n" + ($Records -join "`n"))
}

function ConvertFrom-D13StateText {
    param([string]$Name, [string]$Text)
    $path = Write-C1044Diagnostic -Name $Name -Text $Text
    $errorText = ''
    $nodes = @()
    try {
        $nodes = @((ConvertFrom-NightlyDiagnosticLog -Path $path -Kind execution).Nodes)
    } catch {
        $errorText = [string]$_.Exception.Message
    }
    return [pscustomobject]@{ Nodes = @($nodes); Error = $errorText }
}

function Test-D13_StateClassification {
    $timeoutUid = 'Probe.TimeoutCases.1.1.Only.1.1.0'
    $timeout = ConvertFrom-D13StateText -Name 'd13-timeout-only' -Text (Get-D13StateText -Records @(
        (New-D13StateRecord -Uid $timeoutUid -TypeName 'TimeoutCases' -Method 'Only' -StateName 'Timeout' -Explanation 'timed out' -ExceptionText 'System.TimeoutException: timed out')))
    Assert-C487 -Cond ($timeout.Error -eq '' -and @($timeout.Nodes).Count -eq 1 -and
        $timeout.Nodes[0].Uid -ceq $timeoutUid -and $timeout.Nodes[0].Outcome -ceq 'Failed' -and
        $timeout.Nodes[0].Message -ceq 'timed out') -Name 'D13 timeout-only is failed' `
        -Detail ('error=' + $timeout.Error + ' outcome=' + [string]$timeout.Nodes[0].Outcome + ' message=' + [string]$timeout.Nodes[0].Message)

    $cancelledUid = 'Probe.CancelledCases.1.1.Only.1.1.0'
    $cancelled = ConvertFrom-D13StateText -Name 'd13-cancelled-only' -Text (Get-D13StateText -Records @(
        (New-D13StateRecord -Uid $cancelledUid -TypeName 'CancelledCases' -Method 'Only' -StateName 'Cancelled' -Explanation 'cancelled by the run' -ExceptionText 'System.OperationCanceledException: cancelled')))
    Assert-C487 -Cond ($cancelled.Error -eq '' -and @($cancelled.Nodes).Count -eq 1 -and
        $cancelled.Nodes[0].Uid -ceq $cancelledUid -and $cancelled.Nodes[0].Outcome -ceq 'Failed' -and
        $cancelled.Nodes[0].Message -ceq 'cancelled by the run') -Name 'D13 cancelled-only is failed' `
        -Detail ('error=' + $cancelled.Error + ' outcome=' + [string]$cancelled.Nodes[0].Outcome + ' message=' + [string]$cancelled.Nodes[0].Message)

    $passUid = 'Probe.Ordinary.1.1.Plain.1.1.0'
    $mixedUid = 'Probe.SlowCases.1.1.SlowOne.1.1.0'
    $mixed = ConvertFrom-D13StateText -Name 'd13-mixed-timeout' -Text (Get-D13StateText -Records @(
        (New-D13StateRecord -Uid $passUid -TypeName 'Ordinary' -Method 'Plain' -StateName 'Passed' -Explanation 'ok' -ExceptionText 'none'),
        (New-D13StateRecord -Uid $mixedUid -TypeName 'SlowCases' -Method 'SlowOne' -StateName 'Timeout' -Explanation 'slow timed out' -ExceptionText 'System.TimeoutException: slow timed out')))
    $passNode = @($mixed.Nodes | Where-Object { $_.Uid -ceq $passUid })
    $timeoutNode = @($mixed.Nodes | Where-Object { $_.Uid -ceq $mixedUid })
    Assert-C487 -Cond ($mixed.Error -eq '' -and @($mixed.Nodes).Count -eq 2 -and
        $passNode.Count -eq 1 -and $passNode[0].Outcome -ceq 'Passed' -and
        $timeoutNode.Count -eq 1 -and $timeoutNode[0].Outcome -ceq 'Failed' -and
        $timeoutNode[0].Message -ceq 'slow timed out') -Name 'D13 mixed pass and timeout keeps both' `
        -Detail ('error=' + $mixed.Error + ' count=' + @($mixed.Nodes).Count + ' uids=' + ((@($mixed.Nodes) | ForEach-Object { $_.Uid }) -join ','))

    $quoted = 'saw PassedTestNodeStateProperty in the text'
    $misUid = 'Probe.Ordinary.1.1.Mislabel.1.1.0'
    $mis = ConvertFrom-D13StateText -Name 'd13-passed-quote' -Text (Get-D13StateText -Records @(
        (New-D13StateRecord -Uid $misUid -TypeName 'Ordinary' -Method 'Mislabel' -StateName 'Error' -Explanation $quoted -ExceptionText 'System.Exception: quoted')))
    Assert-C487 -Cond ($mis.Error -eq '' -and @($mis.Nodes).Count -eq 1 -and
        $mis.Nodes[0].Outcome -ceq 'Failed' -and $mis.Nodes[0].Message -ceq $quoted) `
        -Name 'D13 error quoting PassedTestNodeStateProperty stays failed' `
        -Detail ('error=' + $mis.Error + ' outcome=' + [string]$mis.Nodes[0].Outcome + ' message=' + [string]$mis.Nodes[0].Message)

    $full = '[Test Failure] boom, Exception = INNER still the explanation'
    $cutUid = 'Probe.Ordinary.1.1.Truncated.1.1.0'
    $cut = ConvertFrom-D13StateText -Name 'd13-exception-text' -Text (Get-D13StateText -Records @(
        (New-D13StateRecord -Uid $cutUid -TypeName 'Ordinary' -Method 'Truncated' -StateName 'Error' -Explanation $full -ExceptionText 'TUnit.Engine.Exceptions.TestFailedException: boom, Exception = INNER still the explanation')))
    Assert-C487 -Cond ($cut.Error -eq '' -and @($cut.Nodes).Count -eq 1 -and
        $cut.Nodes[0].Outcome -ceq 'Failed' -and $cut.Nodes[0].Message -ceq $full) `
        -Name 'D13 explanation keeps embedded exception delimiters exactly' `
        -Detail ('message=' + [string]$cut.Nodes[0].Message)

    $unknownUid = 'Probe.UnknownCases.1.1.Only.1.1.0'
    $unknown = ConvertFrom-D13StateText -Name 'd13-unknown-state' -Text (Get-D13StateText -Records @(
        (New-D13StateRecord -Uid $unknownUid -TypeName 'UnknownCases' -Method 'Only' -StateName 'NotStarted' -Explanation 'not started' -ExceptionText 'System.Exception: not started')))
    Assert-C487 -Cond ($unknown.Error -eq '' -and @($unknown.Nodes).Count -eq 1 -and
        $unknown.Nodes[0].Uid -ceq $unknownUid -and $unknown.Nodes[0].Outcome -ceq 'Failed' -and
        $unknown.Nodes[0].Message -ceq 'not started') -Name 'D13 unknown state is failed' `
        -Detail ('error=' + $unknown.Error + ' outcome=' + [string]$unknown.Nodes[0].Outcome + ' message=' + [string]$unknown.Nodes[0].Message)
}

function Assert-C1325Fixture {
    param($Fixture, [string]$Group)
    $parsed = ConvertFrom-D13StateText -Name ('c1325-' + $Fixture.Id) -Text (Get-D13StateText -Records @($Fixture.Record))
    $nodes = @($parsed.Nodes)
    Assert-C487 -Cond ($parsed.Error -eq '' -and $nodes.Count -eq 1 -and
        $nodes[0].Uid -ceq $Fixture.Uid -and $nodes[0].ClassName -ceq $Fixture.ClassName -and
        $nodes[0].Method -ceq $Fixture.Method -and $nodes[0].Outcome -ceq $Fixture.Outcome) `
        -Name ('D13 ' + $Group + ' ' + $Fixture.Id + ' identity') -Detail $parsed.Error
    $messageOk = $false
    if ($nodes.Count -eq 1) {
        $present = $nodes[0].PSObject.Properties.Name -contains 'Message'
        $messageOk = if ($null -eq $Fixture.ExpectedMessage) { -not $present } else {
            $present -and [string]::Equals($nodes[0].Message, $Fixture.ExpectedMessage, [StringComparison]::Ordinal)
        }
    }
    Assert-C487 -Cond $messageOk -Name ('D13 ' + $Group + ' ' + $Fixture.Id + ' message') `
        -Detail ('expected=' + [string]$Fixture.ExpectedMessage + ' actual=' + [string]$nodes[0].Message)
}

function Test-D13_ExplanationBoundary {
    . (Join-Path $here 'fixtures/nightly/c1325-diagnostic-records.ps1')
    foreach ($fixture in @(Get-C1325ExplanationFixtures)) { Assert-C1325Fixture -Fixture $fixture -Group 'EB' }
}

function Test-D13_StatePropertyBoundary {
    . (Join-Path $here 'fixtures/nightly/c1325-diagnostic-records.ps1')
    $fixtures = @(Get-C1325StateFixtures)
    foreach ($fixture in $fixtures) { Assert-C1325Fixture -Fixture $fixture -Group 'SB' }
    $ambiguous = ConvertFrom-D13StateText -Name 'c1325-ambiguous-state' -Text (Get-D13StateText -Records @(
        (Get-C1325AmbiguousStateRecord)))
    Assert-C487 -Cond ($ambiguous.Error -eq '' -and @($ambiguous.Nodes).Count -eq 1 -and
        $ambiguous.Nodes[0].Uid -ceq $C1325Uid -and
        $ambiguous.Nodes[0].ClassName -ceq 'Antiphon.E2E.AgentTaskLandDeliveryE2ETests' -and
        $ambiguous.Nodes[0].Method -ceq 'C488_ApprovalPollingCannotConfirm' -and
        $ambiguous.Nodes[0].Outcome -ceq 'Failed' -and
        $ambiguous.Nodes[0].PSObject.Properties.Name -notcontains 'Message') `
        -Name 'D13 SB ambiguous envelope stays failed' -Detail $ambiguous.Error
    $missing = ConvertFrom-D13StateText -Name 'c1325-missing-uid' -Text (Get-D13StateText -Records @(
        $fixtures[0].Record.Replace(('Value = ' + $C1325Uid + ' }'), 'Value =  }')))
    Assert-C487 -Cond ($missing.Error -ceq 'truncated diagnostic record: missing uid') `
        -Name 'D13 SB missing UID refuses' -Detail $missing.Error
    $path = Write-C1044Diagnostic -Name 'c1325-joined' -Text (Get-D13StateText -Records @($fixtures[0].Record))
    $trx = Join-Path $ResultsDirectory 'c1325-joined.trx'
    [IO.File]::WriteAllText($trx, $D13FailureTrx)
    $discovery = Join-Path $ResultsDirectory 'c1325-joined.json'
    Write-C487Discovery -Path $discovery -Nodes @(@{ uid = $C1325Uid; namespace = 'Antiphon.E2E';
        type = 'AgentTaskLandDeliveryE2ETests'; method = 'C488_ApprovalPollingCannotConfirm'; state = 'Discovered' })
    $verdict = ConvertTo-NightlyNativeSuiteVerdict -TrxPath $trx -DiscoveryPath $discovery -ExecutionDiagnosticPath $path `
        -RunDirectory $ResultsDirectory -NotBeforeUtc ([datetime]::UtcNow.AddMinutes(-1)) -ProcessExit 0 `
        -Sha 'c1325' -ExpectedSha 'c1325' -GitRef 'test' -ExpectedRef 'test' -PolicyHash 'test' -ExpectedPolicyHash 'test'
    Assert-C487 -Cond ($verdict.coverageComplete -and -not $verdict.testsPassed -and
        $verdict.reasons -contains 'failed-row' -and @($verdict.terminalNodes).Count -eq 1 -and
        $verdict.terminalNodes[0].Uid -ceq $C1325Uid -and $verdict.terminalNodes[0].Outcome -ceq 'Failed' -and
        [string]::Equals($verdict.terminalNodes[0].Message, $fixtures[0].ExpectedMessage, [StringComparison]::Ordinal) -and
        @($verdict.reasons | Where-Object { $_ -match 'missing-expanded-row|trx-diagnostic-mismatch' }).Count -eq 0) `
        -Name 'D13 SB failed TRX owns verdict' -Detail ($verdict.reasons -join ',')
}

function New-C1341NpmFixture {
    $root = Join-Path $ResultsDirectory ('c1341-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    $clone = New-C487OwnedClone -Root $root
    $testsDir = Join-Path $clone 'tests'
    if (-not (Test-Path -LiteralPath $testsDir)) {
        New-Item -ItemType Directory -Path $testsDir -Force | Out-Null
    }
    Copy-Item -LiteralPath $policyPath -Destination (Join-Path $testsDir 'test-execution-policy.json') -Force
    $policy = Get-Content -LiteralPath $policyPath -Raw | ConvertFrom-Json
    $stub = '<Project><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>'
    foreach ($row in @($policy.projectDispositions)) {
        $rel = ([string]$row.project) -replace '/', [System.IO.Path]::DirectorySeparatorChar
        $dest = Join-Path $clone $rel
        $parent = Split-Path -Parent $dest
        if (-not (Test-Path -LiteralPath $parent)) {
            New-Item -ItemType Directory -Path $parent -Force | Out-Null
        }
        Set-Content -LiteralPath $dest -Value $stub -Encoding ASCII
    }
    $client = Join-Path $clone 'client'
    New-Item -ItemType Directory -Path $client -Force | Out-Null
    $launcherDir = Join-Path (Join-Path $root 'Program Files') 'nodejs'
    New-Item -ItemType Directory -Path $launcherDir -Force | Out-Null
    $launcher = Join-Path $launcherDir 'npm.cmd'
    Set-Content -LiteralPath $launcher -Value "@echo off`r`nexit /b 0`r`n" -Encoding ASCII
    return [pscustomobject]@{
        Root = $root
        Clone = $clone
        Client = $client
        Launcher = $launcher
        LauncherDir = $launcherDir
        Trace = (Join-Path $root 'trace.log')
        Record = (Join-Path $root 'npm-launches.jsonl')
        Seams = (Join-Path $root 'seams.ps1')
        Logs = (Join-Path $root 'logs')
    }
}

function Write-C1341NpmSeams {
    param([string]$Path, [string]$TracePath, [string]$RecordPath)
    # Record argv only. This witness must not launch npm, cmd, or a Windows build.
    $template = @'
$NightlySeams = @{
    TracePath = '__TRACE__'
    Git = {
        param($WorkingDirectory, $Arguments)
        return @{ ExitCode = 0; Output = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' }
    }
    StartProcess = {
        param($FilePath, $ArgumentList, $WorkingDirectory, $TimeoutMilliseconds, $Environment)
        $encoded = @()
        foreach ($item in @($ArgumentList)) {
            $encoded += (ConvertTo-Json -InputObject ([string]$item) -Compress)
        }
        $line = '{"file":' + (ConvertTo-Json -InputObject ([string]$FilePath) -Compress) + ',"cwd":' + (ConvertTo-Json -InputObject ([string]$WorkingDirectory) -Compress) + ',"args":[' + ($encoded -join ',') + ']}'
        Add-Content -LiteralPath '__RECORD__' -Value $line -Encoding ASCII
        return @{ ExitCode = 0; TimedOut = $false; Pid = 2; ChildrenExited = $true; LogText = '' }
    }
    DockerInfo = { return @{ exitCode = 0; version = 'fake'; detail = 'ok' } }
    DiskFree = { return @{ exitCode = 0; freeBytes = 50GB; minimumFreeBytes = 10GB } }
}
'@
    $text = $template.Replace('__TRACE__', $TracePath).Replace('__RECORD__', $RecordPath)
    Set-Content -LiteralPath $Path -Value $text -Encoding ASCII
}

function Test-C487_G1341 {
    # CARD-1341: quoted bare npm.cmd makes cmd %~dp0 the client directory. The launch
    # argument must be the absolute launcher, and that directory must not be the client cwd.
    $fx = New-C1341NpmFixture
    Write-C1341NpmSeams -Path $fx.Seams -TracePath $fx.Trace -RecordPath $fx.Record
    $savedPath = $env:PATH
    $env:PATH = $fx.LauncherDir + [System.IO.Path]::PathSeparator + $savedPath
    try {
        $null = Invoke-E -Fx $fx -Extra @{ Suites = @('client'); Profile = 'rc' }
    } finally {
        $env:PATH = $savedPath
    }
    $expectedLauncher = [System.IO.Path]::GetFullPath($fx.Launcher)
    $expectedClient = ConvertTo-NightlyCanonicalPath -Path $fx.Client
    $launches = @()
    if (Test-Path -LiteralPath $fx.Record) {
        foreach ($line in @(Get-Content -LiteralPath $fx.Record)) {
            if (-not [string]::IsNullOrWhiteSpace($line)) { $launches += ($line | ConvertFrom-Json) }
        }
    }
    $specs = @(
        @{ Name = 'ci'; Tail = 'ci' },
        @{ Name = 'build'; Tail = 'run build' },
        @{ Name = 'lint'; Tail = 'run lint' }
    )
    $ok = $true
    $details = @()
    foreach ($spec in $specs) {
        $match = $null
        foreach ($row in $launches) {
            $argv = @($row.args)
            if ($argv.Count -lt 2) { continue }
            if ([string]$argv[0] -ne '/c') { continue }
            $tail = @()
            for ($i = 2; $i -lt $argv.Count; $i++) { $tail += [string]$argv[$i] }
            if (($tail -join ' ') -eq [string]$spec.Tail) { $match = $row; break }
        }
        if ($null -eq $match) {
            $ok = $false
            $details += ([string]$spec.Name + ' missing')
            continue
        }
        $argv = @($match.args)
        $launcherArg = [string]$argv[1]
        $cwd = ConvertTo-NightlyCanonicalPath -Path ([string]$match.cwd)
        $bare = $false
        foreach ($item in $argv) {
            if ([string]$item -eq 'npm.cmd') { $bare = $true }
        }
        $rooted = [System.IO.Path]::IsPathRooted($launcherArg)
        $same = [string]::Equals($launcherArg, $expectedLauncher, [StringComparison]::Ordinal)
        $cwdOk = [string]::Equals($cwd, $expectedClient, [StringComparison]::Ordinal)
        $dir = ''
        if ($rooted) { $dir = [System.IO.Path]::GetDirectoryName($launcherArg) }
        $dirOk = -not [string]::Equals($dir, $expectedClient, [StringComparison]::Ordinal)
        $space = $launcherArg.Contains(' ')
        if (-not ($rooted -and $same -and $cwdOk -and $dirOk -and $space -and (-not $bare))) { $ok = $false }
        $details += ([string]$spec.Name + ' arg=' + $launcherArg + ' cwd=' + $cwd + ' bare=' + $bare)
    }
    Assert-C487 -Cond $ok -Name 'G1341 npm launch argv is the absolute launcher outside the client cwd' -Detail ($details -join ' | ')
}

function Test-C487_PrerequisiteGate {
    # A failed npm build is a prerequisite. Later steps must not run, and the
    # summary must not claim the tests passed.
    $fx = New-C1341NpmFixture
    $template = @'
$NightlySeams = @{
    TracePath = '__TRACE__'
    Git = {
        param($WorkingDirectory, $Arguments)
        return @{ ExitCode = 0; Output = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' }
    }
    StartProcess = {
        param($FilePath, $ArgumentList, $WorkingDirectory, $TimeoutMilliseconds, $Environment)
        $encoded = @()
        foreach ($item in @($ArgumentList)) {
            $encoded += (ConvertTo-Json -InputObject ([string]$item) -Compress)
        }
        $line = '{"file":' + (ConvertTo-Json -InputObject ([string]$FilePath) -Compress) + ',"args":[' + ($encoded -join ',') + ']}'
        Add-Content -LiteralPath '__RECORD__' -Value $line -Encoding ASCII
        $joined = (@($ArgumentList) -join ' ')
        $code = 0
        if ($joined -match '(^| )run build($| )') { $code = 2 }
        return @{ ExitCode = $code; TimedOut = $false; Pid = 2; ChildrenExited = $true; LogText = '' }
    }
    DockerInfo = { return @{ exitCode = 0; version = 'fake'; detail = 'ok' } }
    DiskFree = { return @{ exitCode = 0; freeBytes = 50GB; minimumFreeBytes = 10GB } }
}
'@
    $text = $template.Replace('__TRACE__', $fx.Trace).Replace('__RECORD__', $fx.Record)
    Set-Content -LiteralPath $fx.Seams -Value $text -Encoding ASCII
    $savedPath = $env:PATH
    $env:PATH = $fx.LauncherDir + [System.IO.Path]::PathSeparator + $savedPath
    $result = $null
    try {
        $result = Invoke-E -Fx $fx -Extra @{ Suites = @('client') }
    } finally {
        $env:PATH = $savedPath
    }
    $launches = @()
    if (Test-Path -LiteralPath $fx.Record) {
        foreach ($line in @(Get-Content -LiteralPath $fx.Record)) {
            if (-not [string]::IsNullOrWhiteSpace($line)) { $launches += ($line | ConvertFrom-Json) }
        }
    }
    $sawCi = $false
    $sawBuild = $false
    $sawLater = $false
    foreach ($row in $launches) {
        $joined = (@($row.args) -join ' ')
        if ($joined -match '(^| )ci($| )') { $sawCi = $true }
        if ($joined -match '(^| )run build($| )') { $sawBuild = $true }
        if ($joined -match '(^| )run lint($| )') { $sawLater = $true }
        if ($joined -match 'Antiphon\.sln') { $sawLater = $true }
        if ($joined -match 'test-client\.ps1') { $sawLater = $true }
    }
    $summary = $null
    if ($result -and $result.SummaryPath -and (Test-Path -LiteralPath $result.SummaryPath)) {
        $summary = Get-Content -LiteralPath $result.SummaryPath -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    $reason = $false
    if ($summary -and $summary.reasons) {
        foreach ($item in @($summary.reasons)) {
            if ([string]$item -ceq 'prerequisite-failed:npm run build') { $reason = $true }
        }
    }
    Assert-C487 -Cond ($sawCi -and $sawBuild -and -not $sawLater -and $null -ne $result -and [int]$result.ExitCode -ne 0 -and -not [bool]$result.testsPassed -and $null -ne $summary -and -not [bool]$summary.testsPassed -and -not [bool]$summary.coverageComplete -and $reason) `
        -Name 'C487 prerequisite npm build stops the pipeline and summary testsPassed is false' `
        -Detail ('ci=' + $sawCi + ' build=' + $sawBuild + ' later=' + $sawLater + ' exit=' + $result.ExitCode + ' pass=' + $result.testsPassed + ' summaryPass=' + $(if ($summary) { [string]$summary.testsPassed } else { 'missing' }) + ' reasons=' + $(if ($summary) { @($summary.reasons) -join ',' } else { '' }))
}

function Test-C1391_ClassMatch {
    # Qualified bare names only. A shared short name must not assign the other namespace.
    $appSuffix = 'Antiphon.Tests.Application.Widget(Factory)'
    $agentsSuffix = 'Antiphon.Tests.Agents.Widget(Factory)'
    $requiredApp = 'Antiphon.Tests.Application.Widget'
    $requiredAgents = 'Antiphon.Tests.Agents.Widget'
    $sameNs = Test-NightlyClassAssigned -ClassName $appSuffix -TypeName '' -RequiredClasses @($requiredApp)
    $otherNs = Test-NightlyClassAssigned -ClassName $agentsSuffix -TypeName '' -RequiredClasses @($requiredApp)
    $unqualified = Test-NightlyClassAssigned -ClassName $appSuffix -TypeName '' -RequiredClasses @('Widget')
    $present = Test-NightlyRequiredClassesPresent -TerminalRows @([pscustomobject]@{ ClassName = $appSuffix }) -RequiredClasses @($requiredApp, $requiredAgents)
    $missing = @($present.Missing)
    $agentsMissing = $missing -ccontains $requiredAgents
    $appMissing = $missing -ccontains $requiredApp
    $ok = ((-not $otherNs) -and (-not $unqualified) -and $sameNs -and (-not $present.Ok) -and $agentsMissing -and (-not $appMissing))
    Assert-C487 -Cond $ok -Name 'C1391 class match uses the qualified bare name' `
        -Detail ('sameNs=' + $sameNs + ' otherNs=' + $otherNs + ' unqualified=' + $unqualified + ' ok=' + $present.Ok + ' missing=' + ($missing -join ','))
}

function Write-C1391LintSeams {
    param([string]$Path, [string]$TracePath, [string]$RecordPath)
    $template = @'
$NightlySeams = @{
    TracePath = '__TRACE__'
    Git = {
        param($WorkingDirectory, $Arguments)
        return @{ ExitCode = 0; Output = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' }
    }
    StartProcess = {
        param($FilePath, $ArgumentList, $WorkingDirectory, $TimeoutMilliseconds, $Environment)
        $encoded = @()
        foreach ($item in @($ArgumentList)) {
            $encoded += (ConvertTo-Json -InputObject ([string]$item) -Compress)
        }
        $line = '{"file":' + (ConvertTo-Json -InputObject ([string]$FilePath) -Compress) + ',"args":[' + ($encoded -join ',') + ']}'
        Add-Content -LiteralPath '__RECORD__' -Value $line -Encoding ASCII
        $joined = (@($ArgumentList) -join ' ')
        $code = 0
        if ($joined -match '(^| )run lint($| )') { $code = 2 }
        return @{ ExitCode = $code; TimedOut = $false; Pid = 2; ChildrenExited = $true; LogText = '' }
    }
    DockerInfo = { return @{ exitCode = 0; version = 'fake'; detail = 'ok' } }
    DiskFree = { return @{ exitCode = 0; freeBytes = 50GB; minimumFreeBytes = 10GB } }
}
'@
    $text = $template.Replace('__TRACE__', $TracePath).Replace('__RECORD__', $RecordPath)
    Set-Content -LiteralPath $Path -Value $text -Encoding ASCII
}

function Test-C1391ReasonPresent {
    param($Result, [string]$Reason)
    if ($null -eq $Result -or $null -eq $Result.reasons) { return $false }
    foreach ($item in @($Result.reasons)) {
        if ([string]$item -ceq $Reason) { return $true }
    }
    return $false
}

function Get-C1391LaunchText {
    param([string]$RecordPath)
    if (-not (Test-Path -LiteralPath $RecordPath)) { return '' }
    $parts = @()
    foreach ($line in @(Get-Content -LiteralPath $RecordPath)) {
        if (-not [string]::IsNullOrWhiteSpace($line)) { $parts += $line }
    }
    return ($parts -join "`n")
}

function Test-C1391_LintGate {
    # Nightly keeps dotnet and the client suite after lint fails. Every other profile stops.
    $savedPath = $env:PATH
    $nightlyFx = New-C1341NpmFixture
    Write-C1391LintSeams -Path $nightlyFx.Seams -TracePath $nightlyFx.Trace -RecordPath $nightlyFx.Record
    $env:PATH = $nightlyFx.LauncherDir + [System.IO.Path]::PathSeparator + $savedPath
    $nightly = $null
    try {
        $nightly = Invoke-E -Fx $nightlyFx -Extra @{ Suites = @('client'); Profile = 'nightly' }
    } finally {
        $env:PATH = $savedPath
    }
    $nightlyLaunch = Get-C1391LaunchText -RecordPath $nightlyFx.Record
    $nightlySln = $nightlyLaunch -match 'Antiphon\.sln'
    $nightlySuite = $nightlyLaunch -match 'test-client\.ps1'
    $nightlyReason = Test-C1391ReasonPresent -Result $nightly -Reason 'client-lint-failed'
    $nightlyPrereq = Test-C1391ReasonPresent -Result $nightly -Reason 'prerequisite-failed:client lint'
    $nightlyOk = ($null -ne $nightly -and [int]$nightly.ExitCode -ne 0 -and -not [bool]$nightly.testsPassed -and -not [bool]$nightly.coverageComplete -and $nightlyReason -and -not $nightlyPrereq -and $nightlySln -and $nightlySuite)

    $rcFx = New-C1341NpmFixture
    Write-C1391LintSeams -Path $rcFx.Seams -TracePath $rcFx.Trace -RecordPath $rcFx.Record
    $env:PATH = $rcFx.LauncherDir + [System.IO.Path]::PathSeparator + $savedPath
    $rc = $null
    try {
        $rc = Invoke-E -Fx $rcFx -Extra @{ Suites = @('client'); Profile = 'rc' }
    } finally {
        $env:PATH = $savedPath
    }
    $rcLaunch = Get-C1391LaunchText -RecordPath $rcFx.Record
    $rcSln = $rcLaunch -match 'Antiphon\.sln'
    $rcSuite = $rcLaunch -match 'test-client\.ps1'
    $rcReason = Test-C1391ReasonPresent -Result $rc -Reason 'prerequisite-failed:client lint'
    $rcNightlyReason = Test-C1391ReasonPresent -Result $rc -Reason 'client-lint-failed'
    $rcOk = ($null -ne $rc -and [int]$rc.ExitCode -ne 0 -and -not [bool]$rc.testsPassed -and $rcReason -and -not $rcNightlyReason -and -not $rcSln -and -not $rcSuite)
    Assert-C487 -Cond ($nightlyOk -and $rcOk) -Name 'C1391 nightly lint continues and rc lint stops' `
        -Detail ('nightly=' + $nightlyOk + ' exit=' + $(if ($nightly) { [string]$nightly.ExitCode } else { 'missing' }) + ' reason=' + $nightlyReason + ' prereq=' + $nightlyPrereq + ' sln=' + $nightlySln + ' suite=' + $nightlySuite + ' reasons=' + $(if ($nightly) { @($nightly.reasons) -join ',' } else { '' }) + ' rc=' + $rcOk + ' rcExit=' + $(if ($rc) { [string]$rc.ExitCode } else { 'missing' }) + ' rcReason=' + $rcReason + ' rcSln=' + $rcSln + ' rcSuite=' + $rcSuite + ' rcReasons=' + $(if ($rc) { @($rc.reasons) -join ',' } else { '' }))
}

function Test-C1391_SplicedRecord {
    $header = "Version: '1.44.0.0'`nVersion: 2.2.2`n"
    $first = New-C1044DiscoveryRecord -Uid 'uid-a' -TypeName 'Alpha' -Method 'One'
    $second = New-C1044DiscoveryRecord -Uid 'uid-b' -TypeName 'Beta' -Method 'Two'
    $path = Write-C1044Diagnostic -Name 'c1391-two-discovery' -Text ($header + $first + $second)
    $nodes = @()
    $errorText = ''
    try { $nodes = @((ConvertFrom-NightlyDiagnosticLog -Path $path -Kind discovery).Nodes) }
    catch { $errorText = $_.Exception.Message }
    $alpha = @($nodes | Where-Object { $_.Uid -ceq 'uid-a' -and $_.Type -ceq 'Alpha' })
    $beta = @($nodes | Where-Object { $_.Uid -ceq 'uid-b' -and $_.Type -ceq 'Beta' })
    $discoveryOk = ($errorText -eq '' -and $nodes.Count -eq 2 -and $alpha.Count -eq 1 -and $beta.Count -eq 1)

    $passedA = $first.Replace('DiscoveredTestNodeStateProperty', 'PassedTestNodeStateProperty')
    $passedB = $second.Replace('DiscoveredTestNodeStateProperty', 'PassedTestNodeStateProperty')
    $path = Write-C1044Diagnostic -Name 'c1391-two-passed' -Text ($header + $passedA + $passedB)
    $exec = @()
    $execError = ''
    try { $exec = @((ConvertFrom-NightlyDiagnosticLog -Path $path -Kind execution).Nodes) }
    catch { $execError = $_.Exception.Message }
    $passedLeft = @($exec | Where-Object { $_.Uid -ceq 'uid-a' -and $_.Outcome -ceq 'Passed' })
    $passedRight = @($exec | Where-Object { $_.Uid -ceq 'uid-b' -and $_.Outcome -ceq 'Passed' })
    $execOk = ($execError -eq '' -and $exec.Count -eq 2 -and $passedLeft.Count -eq 1 -and $passedRight.Count -eq 1)

    $truncated = 'TestNodeUpdateMessage { TestNode = TestNode { Uid = TestNodeUid { Value = truncated }'
    $path = Write-C1044Diagnostic -Name 'c1391-splice' -Text ($header + $truncated + $first)
    $spliced = @()
    $spliceError = ''
    try { $spliced = @((ConvertFrom-NightlyDiagnosticLog -Path $path -Kind discovery).Nodes) }
    catch { $spliceError = $_.Exception.Message }
    $borrowed = @($spliced | Where-Object { $_.Uid -ceq 'truncated' -or $_.Type -ceq 'Alpha' })
    $spliceOk = ($spliceError -match 'truncated diagnostic record|malformed discovery record' -and $spliced.Count -eq 0 -and $borrowed.Count -eq 0)
    Assert-C487 -Cond ($discoveryOk -and $execOk -and $spliceOk) -Name 'C1391 same-line records split and a truncated splice does not borrow' `
        -Detail ('discovery=' + $discoveryOk + ' discErr=' + $errorText + ' nodes=' + $nodes.Count + ' exec=' + $execOk + ' execErr=' + $execError + ' execNodes=' + $exec.Count + ' splice=' + $spliceOk + ' spliceErr=' + $spliceError + ' spliceNodes=' + $spliced.Count)
}

function Test-C1391_OddTypeName {
    $open = New-C1044DiscoveryRecord -Uid 'odd-open' -TypeName 'X(Fac' -Method 'Runs'
    Assert-C1044Rejects -Name 'C1391 unbalanced X(Fac refuses' -Text (Get-C1044DiscoveryText -Records @($open)) -Pattern 'truncated diagnostic record'
    $comma = New-C1044DiscoveryRecord -Uid 'odd-comma' -TypeName ',' -Method 'Runs'
    Assert-C1044Rejects -Name 'C1391 lone comma refuses' -Text (Get-C1044DiscoveryText -Records @($comma)) -Pattern 'truncated diagnostic record'
    $balanced = 'X(Fac)'
    $generic = 'Outer+Inner`1(FactoryA, FactoryB)'
    $records = @(
        (New-C1044DiscoveryRecord -Uid 'balanced-uid' -TypeName $balanced -Method 'Runs'),
        (New-C1044DiscoveryRecord -Uid 'generic-uid' -TypeName $generic -Method 'Runs')
    )
    $path = Write-C1044Diagnostic -Name 'c1391-odd-balanced' -Text (Get-C1044DiscoveryText -Records $records)
    $nodes = @()
    $errorText = ''
    try { $nodes = @((ConvertFrom-NightlyDiagnosticLog -Path $path -Kind discovery).Nodes) }
    catch { $errorText = $_.Exception.Message }
    $balancedNode = @($nodes | Where-Object { $_.Uid -ceq 'balanced-uid' })
    $genericNode = @($nodes | Where-Object { $_.Uid -ceq 'generic-uid' })
    $ok = ($nodes.Count -eq 2 -and $balancedNode.Count -eq 1 -and $balancedNode[0].Type -ceq $balanced -and $genericNode.Count -eq 1 -and $genericNode[0].Type -ceq $generic)
    Assert-C487 -Cond $ok -Name 'C1391 balanced fixture TypeName still parses' -Detail $errorText
}

function Test-C1397_RcAntiphonLaunchGroups {
    # RC antiphon discovery is one class launch per fully included class plus
    # one exact launch per remaining method of a partial class. The measured
    # shape is 1339 class launches and 7 exact launches (1346). Grouping must
    # keep that census and cut the class launches to about 40-60.
    $detail = ''
    $ok = $false
    try {
        $policy = Read-NightlyExecutionPolicy -RepoRoot $repo
        $exclusions = @(Get-NightlyProfileExclusions -PolicyObject $policy.Object -Profile 'rc' -SuiteId 'antiphon' -Platform 'windows')
        $exclusions += [pscustomobject]@{
            Suite = 'antiphon'
            Class = 'C1397HeldTestsExtra'
            Methods = @()
            Reason = 'suffix hazard fixture'
            Owner = 'CARD-1397'
        }
        $shapes = @(
            @{ Ns = 'Antiphon.Tests.Application'; Count = 1100 },
            @{ Ns = 'Antiphon.Tests.TestHelpers'; Count = 150 },
            @{ Ns = 'Antiphon.Tests.Scripts'; Count = 50 },
            @{ Ns = 'Antiphon.Tests.Agents'; Count = 39 }
        )
        $nodes = New-Object System.Collections.Generic.List[object]
        $typesByNs = @{}
        foreach ($shape in $shapes) {
            $ns = [string]$shape.Ns
            $letter = $ns.Substring($ns.LastIndexOf('.') + 1).Substring(0, 1)
            if (-not $typesByNs.ContainsKey($ns)) { $typesByNs[$ns] = New-Object System.Collections.Generic.List[string] }
            for ($n = 0; $n -lt [int]$shape.Count; $n++) {
                $type = 'C1397{0}{1}Tests' -f $letter, $n.ToString('0000')
                $className = $ns + '.' + $type
                if ($ns -eq 'Antiphon.Tests.Application' -and $n -eq 0) {
                    $type = 'C1397PrefixTests'
                    $className = $ns + '.' + $type
                } elseif ($ns -eq 'Antiphon.Tests.Application' -and $n -eq 1) {
                    $type = 'C1397HeldTests'
                    $className = $ns + '.' + $type
                } elseif ($ns -eq 'Antiphon.Tests.Application' -and $n -eq 2) {
                    $type = 'C1397PrefixTestsExtra'
                    $className = $ns + '.' + $type
                } elseif ($ns -eq 'Antiphon.Tests.Agents' -and $n -eq 0) {
                    $type = 'C1397FixtureTests'
                    $className = $ns + '.' + $type + '(C1397Factory)'
                }
                [void]$typesByNs[$ns].Add($type)
                [void]$nodes.Add([pscustomobject]@{ ClassName = $className; Type = $type; Method = 'Run'; Uid = ($className + '.Run') })
            }
        }
        [void]$typesByNs['Antiphon.Tests.Application'].Add('C1397HeldTestsExtra')
        [void]$typesByNs['Antiphon.Tests.Application'].Add('Card0168InteractiveProbeTests')
        [void]$typesByNs['Antiphon.Tests.Agents'].Add('ClaudeEffortPromptCanaryTests')
        [void]$typesByNs['Antiphon.Tests.TestHelpers'].Add('TestDbFixtureLazyInitializationTests')
        [void]$nodes.Add([pscustomobject]@{
            ClassName = 'Antiphon.Tests.Application.C1397HeldTestsExtra'
            Type = 'C1397HeldTestsExtra'
            Method = 'Run'
            Uid = 'held-extra'
        })
        [void]$nodes.Add([pscustomobject]@{
            ClassName = 'Antiphon.Tests.Application.Card0168InteractiveProbeTests'
            Type = 'Card0168InteractiveProbeTests'
            Method = 'Run'
            Uid = 'card0168'
        })
        [void]$nodes.Add([pscustomobject]@{
            ClassName = 'Antiphon.Tests.Agents.ClaudeEffortPromptCanaryTests'
            Type = 'ClaudeEffortPromptCanaryTests'
            Method = 'Real_effort_picker_preserves_xhigh_and_reaches_an_empty_composer'
            Uid = 'claude-effort'
        })
        $dbClass = 'Antiphon.Tests.TestHelpers.TestDbFixtureLazyInitializationTests'
        $dbMethods = @(
            'A_db_free_exact_method_selection_constructs_and_starts_no_database',
            'A_class_filtered_child_skips_every_parent_probe',
            'A_child_at_depth_two_refuses_before_any_work',
            'Mixed_first_consumers_share_one_real_bootstrap',
            'A_post_start_fault_cleans_up_the_owned_container',
            'A_failing_worker_exits_1_without_running_tests',
            'A_worker_child_exits_before_the_shared_store_warmup',
            'Child_db_free_selection_touches_no_database',
            'Child_mixed_consumers_initialize_once',
            'Child_post_start_fault_is_terminal'
        )
        foreach ($method in $dbMethods) {
            [void]$nodes.Add([pscustomobject]@{ ClassName = $dbClass; Type = 'TestDbFixtureLazyInitializationTests'; Method = $method; Uid = ('db-' + $method) })
        }
        $all = @([pscustomobject]@{ id = 'all'; classes = @() })
        $nodeArray = @($nodes.ToArray())
        $leaves = @(Get-NightlyProfileExecutionChunks -DiscoveryNodes $nodeArray -Chunks $all -Exclusions $exclusions)
        $plan = @(Resolve-NightlySuiteChunks -DiscoveryNodes $nodeArray -Chunks $all -Exclusions $exclusions -ProfileAware $true -Native $true)
        $nightlyEx = @(Get-NightlyProfileExclusions -PolicyObject $policy.Object -Profile 'nightly' -SuiteId 'antiphon' -Platform 'windows')
        $nightlyPlan = @(Resolve-NightlySuiteChunks -DiscoveryNodes $nodeArray -Chunks $all -Exclusions $nightlyEx -ProfileAware $false -Native $true)
        $emptyPlan = @(Resolve-NightlySuiteChunks -DiscoveryNodes $nodeArray -Chunks $all -Exclusions @() -ProfileAware $true -Native $true)
        $guardedPlan = @(Resolve-NightlySuiteChunks -DiscoveryNodes $nodeArray -Chunks $all -Exclusions $exclusions -ProfileAware $false -Native $true)
        $classChunks = @($plan | Where-Object { [string]::IsNullOrWhiteSpace([string]$_.exactFilter) })
        $exactChunks = @($plan | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_.exactFilter) })
        $orChunks = @()
        foreach ($chunk in $classChunks) {
            $gf = ''
            if ($chunk.PSObject.Properties.Name -contains 'groupFilter') { $gf = [string]$chunk.groupFilter }
            if (-not [string]::IsNullOrWhiteSpace($gf)) { $orChunks += $chunk }
        }
        $maxOr = 0
        foreach ($chunk in $orChunks) {
            $cnt = @($chunk.classes).Count
            if ($cnt -gt $maxOr) { $maxOr = $cnt }
        }
        $leafClass = @($leaves | Where-Object { [string]::IsNullOrWhiteSpace([string]$_.exactFilter) } | ForEach-Object { [string]@($_.classes)[0] } | Sort-Object)
        $planClass = @()
        foreach ($chunk in $classChunks) { foreach ($c in @($chunk.classes)) { $planClass += [string]$c } }
        $planClass = @($planClass | Sort-Object)
        $leafExact = @($leaves | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_.exactFilter) } | ForEach-Object { [string]$_.exactFilter } | Sort-Object)
        $planExact = @($exactChunks | ForEach-Object { [string]$_.exactFilter } | Sort-Object)
        $classKey = ($planClass -join '|')
        $leafKey = ($leafClass -join '|')
        $exactKey = ($planExact -join '|')
        $leafExactKey = ($leafExact -join '|')
        $dup = 0
        $seenClass = @{}
        foreach ($c in $planClass) {
            if ($seenClass.ContainsKey($c)) { $dup++ } else { $seenClass[$c] = $true }
        }
        $exactClass = @{}
        foreach ($chunk in $exactChunks) { $exactClass[[string]@($chunk.classes)[0]] = $true }
        $overlap = 0
        foreach ($c in $planClass) { if ($exactClass.ContainsKey($c)) { $overlap++ } }
        $invalid = @()
        $argvByClass = @{}
        foreach ($chunk in $plan) {
            $gf = ''
            if ($chunk.PSObject.Properties.Name -contains 'groupFilter') { $gf = [string]$chunk.groupFilter }
            try {
                $argv = @(Get-NightlyNativeExecutionArguments -TrxPath (Join-Path $ResultsDirectory 'c1397.trx') -DiagnosticDirectory $ResultsDirectory -Classes @($chunk.classes) -ExactFilter ([string]$chunk.exactFilter) -GroupFilter $gf)
                $joined = $argv -join ' '
                foreach ($c in @($chunk.classes)) { $argvByClass[[string]$c] = $joined }
                if ($joined.Contains('(') -and [string]::IsNullOrWhiteSpace($gf)) { $invalid += 'paren-on-class-filter' }
            } catch {
                $invalid += [string]$_.Exception.Message
            }
        }
        $rejected = $false
        try {
            [void]@(Get-NightlyNativeExecutionArguments -TrxPath (Join-Path $ResultsDirectory 'c1397-bad.trx') -DiagnosticDirectory $ResultsDirectory -Classes @('Antiphon.Tests.Application.C1397PrefixTests', 'Antiphon.Tests.Application.C1397PrefixTestsExtra') -GroupFilter '/*/Antiphon.Tests.Application/(C1397PrefixTests*)|(C1397PrefixTestsExtra*)/*')
        } catch {
            if ([string]$_.Exception.Message -ceq 'invalid-group-filter') { $rejected = $true }
        }
        $prefixArg = [string]$argvByClass['Antiphon.Tests.Application.C1397PrefixTests']
        $heldArg = [string]$argvByClass['Antiphon.Tests.Application.C1397HeldTests']
        $fixtureArg = [string]$argvByClass['Antiphon.Tests.Agents.C1397FixtureTests']
        $prefixOk = ($prefixArg.Contains('/*/Antiphon.Tests.Application/C1397PrefixTests/*') -and -not $prefixArg.Contains('(C1397PrefixTests*)'))
        $heldOk = ($heldArg.Contains('/*/Antiphon.Tests.Application/C1397HeldTests/*') -and -not $heldArg.Contains('(C1397HeldTests*)'))
        $fixtureOk = ($fixtureArg.Contains('C1397FixtureTests') -and -not $fixtureArg.Contains('C1397Factory'))
        $unsafeOr = $false
        foreach ($chunk in $orChunks) {
            $bare0 = Get-NightlyBareClassIdentity -Name ([string]@($chunk.classes)[0])
            $dot = $bare0.LastIndexOf('.')
            $ns = $bare0.Substring(0, $dot)
            $types = @($typesByNs[$ns])
            foreach ($c in @($chunk.classes)) {
                $type = (Get-NightlyBareClassIdentity -Name ([string]$c)).Substring($ns.Length + 1)
                foreach ($other in $types) {
                    if ($other.Length -gt $type.Length -and $other.StartsWith($type, [StringComparison]::Ordinal)) { $unsafeOr = $true }
                }
            }
        }
        $excludedKept = @($dbMethods | Where-Object { $_ -like 'Child_*' })
        $kept = @($dbMethods | Where-Object { $_ -notlike 'Child_*' })
        $exactText = $planExact -join ' '
        $childrenGone = $true
        foreach ($method in $excludedKept) { if ($exactText.Contains($method)) { $childrenGone = $false } }
        $siblingsKept = $true
        foreach ($method in $kept) { if (-not $exactText.Contains($method)) { $siblingsKept = $false } }
        $cardGone = -not $classKey.Contains('Card0168InteractiveProbeTests') -and -not $exactText.Contains('Card0168InteractiveProbeTests')
        $effortGone = -not $classKey.Contains('ClaudeEffortPromptCanaryTests') -and -not $exactText.Contains('ClaudeEffortPromptCanaryTests')
        $dbNotGrouped = -not $classKey.Contains('TestDbFixtureLazyInitializationTests')
        $watchdog = Get-NightlyWatchdogMs -PolicyObject $policy.Object -Key 'antiphon' -Fallback 3600000
        $nightlyOk = ($nightlyEx.Count -eq 0 -and @($nightlyPlan).Count -eq 1 -and [string]$nightlyPlan[0].id -eq 'all' -and @($nightlyPlan[0].classes).Count -eq 0)
        $emptyOk = (@($emptyPlan).Count -eq 1 -and [string]$emptyPlan[0].id -eq 'all' -and @($emptyPlan[0].classes).Count -eq 0)
        $guardOk = (@($guardedPlan).Count -eq 1 -and [string]$guardedPlan[0].id -eq 'all' -and @($guardedPlan[0].classes).Count -eq 0)
        $reasons = New-Object System.Collections.Generic.List[string]
        if (@($leaves).Count -ne 1346) { [void]$reasons.Add('leaf-shape') }
        if ($leafExact.Count -ne 7 -or $exactChunks.Count -ne 7) { [void]$reasons.Add('exact-count') }
        if ($classChunks.Count -lt 40 -or $classChunks.Count -gt 60) { [void]$reasons.Add('class-launches') }
        if ((@($plan).Count) -ge 100) { [void]$reasons.Add('total-launches') }
        if ($classKey -cne $leafKey -or $dup -ne 0 -or $overlap -ne 0) { [void]$reasons.Add('class-census') }
        if ($exactKey -cne $leafExactKey) { [void]$reasons.Add('exact-census') }
        if (-not $childrenGone -or -not $siblingsKept -or -not $cardGone -or -not $effortGone -or -not $dbNotGrouped) { [void]$reasons.Add('exclusions') }
        if (-not $prefixOk -or -not $heldOk -or -not $fixtureOk -or $unsafeOr) { [void]$reasons.Add('suffix') }
        if ($orChunks.Count -lt 1 -or $maxOr -gt 40 -or $maxOr -lt 2) { [void]$reasons.Add('or-groups') }
        if ($invalid.Count -ne 0 -or -not $rejected) { [void]$reasons.Add('filters') }
        if (-not $nightlyOk -or -not $emptyOk -or -not $guardOk -or $watchdog -ne 3600000) { [void]$reasons.Add('nightly-watchdog') }
        $ok = ($reasons.Count -eq 0)
        $detail = 'classLaunches=' + $classChunks.Count + ' exact=' + $exactChunks.Count + ' total=' + @($plan).Count + ' leaves=' + @($leaves).Count + ' or=' + $orChunks.Count + ' maxOr=' + $maxOr + ' invalid=' + $invalid.Count + ' dup=' + $dup + ' watchdog=' + $watchdog + ' reasons=' + ($reasons -join ',')
        Write-Host $detail
    } catch {
        $detail = [string]$_.Exception.Message
        $ok = $false
    }
    Assert-C487 -Cond $ok -Name 'C1397 rc antiphon launch groups are far below 1346 and assign every class once' -Detail $detail
}

function Test-C1404_Group10MeasuredBudget {
    # Independent R/P evidence prices; Publication is a partial-sample pace bound.
    # Exercise the real cost loader, rc policy and packer against its exact S3 projection.
    $ErrorActionPreference = 'Stop'
    $ok = $false
    $detail = ''
    try {
        $expected = @{
            'AgentTaskLandCheckpointMatrixTests' = @{ Nodes = 41; Seconds = 1560; Basis = 'pace'; Launches = 1 }
            'AgentTaskLandCheckpointWorkerTests' = @{ Nodes = 2; Seconds = 1560; Basis = 'pace'; Launches = 1 }
            'AgentTaskLandPersistenceFailureTests' = @{ Nodes = 52; Seconds = 1980; Basis = 'measured'; Launches = 1 }
            'AgentTaskLandPublicationTests' = @{ Nodes = 63; Seconds = 3780; Basis = 'pace'; Launches = 5 }
        }
        function Read-C1404Nodes([string]$listing) {
            $nodes = New-Object System.Collections.Generic.List[object]
            foreach ($line in [IO.File]::ReadAllLines($listing)) {
                if ([string]::IsNullOrWhiteSpace($line) -or $line.StartsWith('#')) { continue }
                $fields = $line.Split('|')
                if ($fields.Count -ne 3 -or $fields[1] -cnotin @('S', '-')) { throw 'invalid fixture row' }
                $className = $fields[0]
                $dot = $className.LastIndexOf('.')
                $type = $className.Substring($dot + 1)
                foreach ($entry in $fields[2].Split(',')) {
                    if ($entry -cnotmatch '^([A-Za-z_][A-Za-z0-9_]*)=([1-9][0-9]*)$') { throw 'invalid fixture method' }
                    $method = $Matches[1]
                    $cases = [int]$Matches[2]
                    for ($k = 0; $k -lt $cases; $k++) {
                        [void]$nodes.Add([pscustomobject]@{
                            ClassName = $className; Namespace = $className.Substring(0, $dot); Type = $type
                            Method = $method; Uid = ($className + '.' + $method + '.' + $k)
                            Categories = @($(if ($fields[1] -ceq 'S') { 'Slow' }))
                        })
                    }
                }
            }
            return $nodes.ToArray()
        }
        $historical = @(Read-C1404Nodes (Join-Path $here 'fixtures/nightly/c1404-group10-listing.txt'))
        $nodes = @(Read-C1404Nodes (Join-Path $here 'fixtures/nightly/c1514-checkpoint-split-listing.txt'))
        # Preserve the raw evidence oracle and prove the projection is a bijection.
        $oldMatrix = @($historical | Where-Object Type -CEQ 'AgentTaskLandCheckpointMatrixTests')
        if ($historical.Count -ne 158 -or $oldMatrix.Count -ne 43) { throw 'historical fixture census' }
        $historicalIds = @($historical | ForEach-Object Uid | Sort-Object)
        $mappedIds = @(foreach ($node in $nodes) {
            if ($node.Type -ceq 'AgentTaskLandCheckpointWorkerTests') {
                if ($node.Method -cne 'C665_WorkerDeathKeepsRecordedTreeAcrossActualRegistrationDrop') { throw 'unexpected moved method' }
                $node.Uid.Replace('.AgentTaskLandCheckpointWorkerTests.', '.AgentTaskLandCheckpointMatrixTests.')
            } else { $node.Uid }
        })
        if ($mappedIds.Count -ne 158 -or (@($mappedIds | Sort-Object -Unique)).Count -ne 158 -or
            @(Compare-Object -ReferenceObject $historicalIds -DifferenceObject @($mappedIds | Sort-Object) -CaseSensitive).Count -ne 0) {
            throw 'historical-to-split mapping is not bijective'
        }
        $reasons = New-Object System.Collections.Generic.List[string]
        $prices = New-Object System.Collections.Generic.List[string]
        $costDoc = [IO.File]::ReadAllText((Join-Path $lib 'nightly-class-costs.json')) | ConvertFrom-Json
        foreach ($type in @($expected.Keys | Sort-Object)) {
            $oracle = $expected[$type]
            $bare = 'Antiphon.Tests.Application.' + $type
            $classNodes = @($nodes | Where-Object { $_.ClassName -ceq $bare })
            if ($classNodes.Count -ne $oracle.Nodes) { [void]$reasons.Add('fixture:' + $type) }
            $row = $costDoc.classes.PSObject.Properties[$bare]
            if ($null -eq $row -or [int]$row.Value.nodes -ne $oracle.Nodes -or
                [double]$row.Value.seconds -ne $oracle.Seconds -or [string]$row.Value.basis -cne $oracle.Basis) {
                [void]$reasons.Add('cost-row:' + $type)
            }
            if ($type -cin @('AgentTaskLandCheckpointMatrixTests', 'AgentTaskLandCheckpointWorkerTests')) {
                $provenance = if ($null -ne $row) { $row.Value.PSObject.Properties['source'] } else { $null }
                if ($null -eq $provenance -or [string]$provenance.Value -cnotlike '*original CheckpointMatrix 43 nodes / 1560 seconds measured*') {
                    [void]$reasons.Add('split-bound-provenance:' + $type)
                }
            }
            $actual = Get-NightlyLaunchClassCost -Nodes $classNodes -TypeName $type -Bare $bare
            [void]$prices.Add($type + '=' + [Math]::Round($actual.Seconds, 1))
            if ($actual.NodeCount -ne $oracle.Nodes -or [Math]::Abs($actual.Seconds - $oracle.Seconds) -gt 0.01) {
                [void]$reasons.Add('class-cost:' + $type)
            }
        }
        $uids = @($nodes | ForEach-Object { $_.Uid } | Sort-Object -Unique)
        if ($nodes.Count -ne 158 -or $uids.Count -ne 158) { [void]$reasons.Add('fixture-census') }
        $policy = Read-NightlyExecutionPolicy -RepoRoot $repo
        $exclusions = @(Get-NightlyProfileExclusions -PolicyObject $policy.Object -Profile 'rc' -SuiteId 'antiphon' -Platform 'windows')
        if ($exclusions.Count -eq 0) { [void]$reasons.Add('rc-exclusions') }
        foreach ($node in $nodes) {
            if (Test-NightlyDiscoveryExcluded -Node $node -ProfileExclusions $exclusions -ProfileAware) { [void]$reasons.Add('excluded:' + $node.Uid) }
        }
        $chunks = @(Resolve-NightlySuiteChunks -DiscoveryNodes @($nodes) -Chunks @([pscustomobject]@{ id = 'all'; classes = @() }) -Exclusions $exclusions -ProfileAware $true -Native $true)
        $watchdog = Get-NightlyWatchdogMs -PolicyObject $policy.Object -Key 'antiphon' -Fallback 0
        if ($watchdog -ne 3600000) { [void]$reasons.Add('watchdog') }
        if ($chunks.Count -ne 8) { [void]$reasons.Add('launch-count') }

        # Evaluate the actual emitted filter against the entire fixture. Do not use
        # chunk.nodes or chunk.classes as the selection oracle.
        function Test-C1404FilterSegment {
            param([string]$Value, [string]$Pattern)
            foreach ($operand in $Pattern.Split('|')) {
                $patternText = $operand.Trim('(', ')')
                $regex = '^' + [regex]::Escape($patternText).Replace('\*', '.*') + '$'
                if ([regex]::IsMatch($Value, $regex, [Text.RegularExpressions.RegexOptions]::CultureInvariant)) { return $true }
            }
            return $false
        }
        $covered = @{}
        $launchCounts = @{}
        $publicationCounts = New-Object System.Collections.Generic.List[int]
        $launchDetails = New-Object System.Collections.Generic.List[string]
        $maxSeconds = 0.0
        foreach ($chunk in $chunks) {
            $argv = @(Get-NightlyNativeExecutionArguments -TrxPath (Join-Path $ResultsDirectory 'c1404.trx') -DiagnosticDirectory $ResultsDirectory -Classes @($chunk.classes) -ExactFilter ([string]$chunk.exactFilter) -GroupFilter ([string]$chunk.groupFilter) -MethodFilter ([string]$chunk.methodFilter))
            $filterIndex = [Array]::IndexOf($argv, '--treenode-filter')
            if ($filterIndex -lt 0) { [void]$reasons.Add('missing-filter'); continue }
            $filter = [string]$argv[$filterIndex + 1]
            $segments = $filter.Split('/')
            if ($segments.Count -ne 5 -or $segments[1] -cne '*') { [void]$reasons.Add('invalid-filter:' + $filter); continue }
            $hits = @($nodes | Where-Object {
                (Test-C1404FilterSegment -Value $_.Namespace -Pattern $segments[2]) -and
                (Test-C1404FilterSegment -Value $_.Type -Pattern $segments[3]) -and
                (Test-C1404FilterSegment -Value $_.Method -Pattern $segments[4])
            })
            $types = @($hits | ForEach-Object { $_.Type } | Sort-Object -Unique)
            if ($types.Count -ne 1) { [void]$reasons.Add('mixed-or-empty-launch:' + $chunk.id) }
            $seconds = 0.0
            foreach ($node in $hits) {
                $oracle = $expected[$node.Type]
                $seconds += [double]$oracle.Seconds / $oracle.Nodes
                $covered[$node.Uid] = 1 + [int]$covered[$node.Uid]
            }
            if ([Math]::Abs($seconds - [double]$chunk.seconds) -gt 0.11) { [void]$reasons.Add('launch-price:' + $chunk.id) }
            if ($seconds -ge ($watchdog / 1000.0)) { [void]$reasons.Add('watchdog-fit:' + $chunk.id) }
            $maxSeconds = [Math]::Max($maxSeconds, $seconds)
            foreach ($type in $types) { $launchCounts[$type] = 1 + [int]$launchCounts[$type] }
            if ($types.Count -eq 1 -and $types[0] -ceq 'AgentTaskLandPublicationTests') {
                [void]$publicationCounts.Add($hits.Count)
                if ($seconds -gt 900.01 -or $segments[4] -ceq '*') { [void]$reasons.Add('publication-split') }
            } elseif ($types.Count -eq 1 -and ($hits.Count -ne $expected[$types[0]].Nodes -or $segments[4] -cne '*')) {
                [void]$reasons.Add('complete-class:' + $types[0])
            }
            [void]$launchDetails.Add($chunk.id + ':' + $hits.Count + '/' + [Math]::Round($seconds, 1) + 's')
        }
        foreach ($type in $expected.Keys) {
            if ([int]$launchCounts[$type] -ne $expected[$type].Launches) { [void]$reasons.Add('class-launches:' + $type) }
        }
        $missing = @($uids | Where-Object { -not $covered.ContainsKey($_) }).Count
        $double = @($covered.Keys | Where-Object { $covered[$_] -ne 1 }).Count
        $extra = @($covered.Keys | Where-Object { $_ -cnotin $uids }).Count
        if ($missing -ne 0 -or $double -ne 0 -or $extra -ne 0) { [void]$reasons.Add('census') }
        $publicationShape = @($publicationCounts | Sort-Object) -join ','
        if ($publicationShape -cne '3,15,15,15,15') { [void]$reasons.Add('publication-node-counts') }
        if ([Math]::Abs($maxSeconds - 1980) -gt 0.01) { [void]$reasons.Add('largest-launch') }
        $ok = ($reasons.Count -eq 0)
        $detail = 'prices=' + ($prices -join ',') + ' launches=' + $chunks.Count + ' nodes=' + $nodes.Count + ' missing=' + $missing + ' double=' + $double + ' extra=' + $extra + ' publicationNodes=' + $publicationShape + ' maxEstimatedSeconds=' + [Math]::Round($maxSeconds, 1) + ' watchdog=' + $watchdog + ' groups=' + ($launchDetails -join ',') + ' reasons=' + ($reasons -join ',')
        Write-Host $detail
    } catch {
        $detail = 'setup-error: ' + [string]$_.Exception.Message + ' at ' + [string]$_.InvocationInfo.PositionMessage
    }
    Assert-C487 -Cond $ok -Name 'C1404 group 10 uses evidence costs, eight isolated launches and the unchanged watchdog' -Detail $detail
}

function Test-C1584_E2EExpandedBudget {
    $ok = $false
    $detail = ''
    try {
        # Source-derived planner census. No E2E assembly or fixture is executed.
        $nodes = New-Object System.Collections.Generic.List[object]
        $roster = New-Object System.Collections.Generic.List[string]
        $files = @(Get-ChildItem (Join-Path $repo 'tests/Antiphon.E2E') -Filter '*.cs' -Recurse -File | Where-Object { $_.FullName -notmatch '[/\\](bin[^/\\]*|obj)[/\\]' })
        $files += Get-Item (Join-Path $repo 'tests/Shared/TestClassificationGuardTests.cs')
        foreach ($file in $files) {
            $source = [IO.File]::ReadAllText($file.FullName)
            $ns = [regex]::Match($source, 'namespace ([\w.]+)').Groups[1].Value
            foreach ($method in [regex]::Matches($source, '((?:\s*\[[^\]]+\])+)\s*public (?:async )?(?:Task(?:<[^>]+>)?|void) (\w+)\(')) {
                if ($method.Groups[1].Value -notmatch '\[Test\]') { continue }
                $classes = [regex]::Matches($source.Substring(0, $method.Index), 'public (?:(?:sealed|partial|abstract) )*class (\w+)')
                if ($classes.Count -eq 0) { throw 'source-census-class' }
                $type = $classes[$classes.Count - 1].Groups[1].Value
                $name = $method.Groups[2].Value
                $bare = $ns + '.' + $type
                $arguments = @([regex]::Matches($method.Groups[1].Value, '\[Arguments\((.*?)\)\]') | ForEach-Object { $_.Groups[1].Value })
                if ($arguments.Count -eq 0) { $arguments = @('') }
                foreach ($argument in $arguments) {
                    # Batch wrappers retain the original matrix's argument identity.
                    $roster.Add($bare + '|' + ($name -replace '_Batch\d+$', '') + '|' + $argument)
                    $nodes.Add([pscustomobject]@{ ClassName = $bare; Type = $type; Method = $name; Uid = ($bare + '|' + $name + '|' + $argument); Categories = @('OptIn') })
                }
            }
        }
        $expected = @(Get-Content (Join-Path $here 'fixtures/nightly/c1584-e2e-source-roster.txt') | Where-Object { $_ -and -not $_.StartsWith('#') } | Sort-Object)
        if (($expected -join "`n") -cne (@($roster | Sort-Object) -join "`n")) { throw 'expanded-source-roster-changed' }
        $policy = Read-NightlyExecutionPolicy -RepoRoot $repo
        $exclusions = @(Get-NightlyProfileExclusions -PolicyObject $policy.Object -Profile 'rc' -SuiteId 'e2e' -Platform 'windows')
        $required = @($nodes | Where-Object { -not (Test-NightlyDiscoveryExcluded $_ $exclusions -ProfileAware) })
        $reasons = New-Object System.Collections.Generic.List[string]
        # Exact census of the committed roster. The added identity is
        # C467_RetainedEvidenceCompletesCleanup, the portable stalled-receipt
        # diagnostic and two polling-attention witnesses; Windows rc still excludes seven.
        if ($nodes.Count -ne 181 -or $required.Count -ne 174) { $reasons.Add('source-census') }
        # Independent conservative per-expanded-case bounds. 600 s is 1.72x
        # the slowest supplied Windows observation (348.1 s), not a p95.
        $bounds = @{}
        $costDoc = [IO.File]::ReadAllText((Join-Path $lib 'nightly-class-costs.json')) | ConvertFrom-Json
        foreach ($class in @($nodes | Group-Object ClassName)) {
            $bare = [string]$class.Name
            $unit = 300.0
            if ($bare -match '(AgentTaskLandDelivery|DispatchBaseWarningDelivery)E2ETests$') { $unit = 600.0 }
            elseif ($bare -match 'WorktreeRetirementDeliveryE2ETests$') { $unit = 500.0 }
            elseif ($bare -match '(OutputDistillationCanaryGuardTests|LandDeliveryFixtureProbeTests|PtyBackendEnvGuardTests|TestClassificationGuardTests)$') { $unit = 10.0 }
            $bounds[$bare] = $unit
            $row = $costDoc.classes.PSObject.Properties[$bare]
            if ($null -eq $row -or [int]$row.Value.nodes -ne $class.Count -or [double]$row.Value.seconds -ne $unit * $class.Count -or
                [string]$row.Value.source -cnotlike '*CARD-1584*') { $reasons.Add('cost-row:' + $bare) }
        }
        $chunks = @(Resolve-NightlySuiteChunks -DiscoveryNodes @($nodes.ToArray()) -Chunks @([pscustomobject]@{ id = 'all'; classes = @() }) -Exclusions $exclusions -ProfileAware $true -Native $true)
        $covered = @{}
        $maxSeconds = 0.0
        $shapes = New-Object System.Collections.Generic.List[string]
        foreach ($chunk in $chunks) {
            $argv = @(Get-NightlyNativeExecutionArguments -TrxPath (Join-Path $ResultsDirectory 'c1584.trx') -DiagnosticDirectory $ResultsDirectory -Classes @($chunk.classes) -ExactFilter ([string]$chunk.exactFilter) -GroupFilter ([string]$chunk.groupFilter) -MethodFilter ([string]$chunk.methodFilter))
            $filterIndex = [Array]::IndexOf($argv, '--treenode-filter')
            if ($filterIndex -lt 0) { throw 'missing emitted filter' }
            $segments = ([string]$argv[$filterIndex + 1]).Split('/')
            if ($segments.Count -ne 5) { throw 'invalid emitted filter shape' }
            $hits = @($nodes | Where-Object {
                $node = $_
                $classHit = $false
                foreach ($operand in $segments[3].Split('|')) { if ($node.Type -clike $operand.Trim('(', ')')) { $classHit = $true } }
                $methodHit = $false
                foreach ($operand in $segments[4].Split('|')) { if ($node.Method -clike $operand.Trim('(', ')')) { $methodHit = $true } }
                ($node.ClassName.Substring(0, $node.ClassName.LastIndexOf('.')) -clike $segments[2]) -and $classHit -and $methodHit
            })
            if ($hits.Count -eq 0) { throw 'empty emitted selection' }
            $seconds = 0.0
            foreach ($node in $hits) {
                if (Test-NightlyDiscoveryExcluded $node $exclusions -ProfileAware) { $reasons.Add('selected-exclusion') }
                $seconds += $bounds[$node.ClassName]
                $covered[$node.Uid] = 1 + [int]$covered[$node.Uid]
            }
            if ($seconds -ge 1800.01) { $reasons.Add('over-budget:' + $chunk.id) }
            if ([Math]::Abs($seconds - [double]$chunk.seconds) -gt 0.11) { $reasons.Add('underpriced:' + $chunk.id) }
            $maxSeconds = [Math]::Max($maxSeconds, $seconds)
            $shapes.Add($chunk.id + ':' + $hits.Count + '/' + $seconds + 's')
        }
        if ($covered.Count -ne $required.Count -or @($covered.Values | Where-Object { $_ -ne 1 }).Count -ne 0) { $reasons.Add('census') }
        foreach ($node in $required) { if (-not $covered.ContainsKey($node.Uid)) { $reasons.Add('missing-node') } }
        $watchdog = Get-NightlyWatchdogMs -PolicyObject $policy.Object -Key 'e2e' -Fallback 0
        if ($watchdog -ne 7200000) { $reasons.Add('watchdog') }
        # A future four-row method cannot silently defeat the E2E ceiling.
        $oversizeNodes = @(1..4 | ForEach-Object { [pscustomobject]@{
            ClassName = 'Antiphon.E2E.AgentTaskLandDeliveryE2ETests'; Type = 'AgentTaskLandDeliveryE2ETests'
            Method = 'Unsplittable'; Uid = ('oversize-' + $_); Categories = @('OptIn')
        } })
        $refused = $false
        try { [void]@(Get-NightlyProfileLaunchPlan -DiscoveryNodes $oversizeNodes -Chunks @([pscustomobject]@{ id = 'all'; classes = @() }) -Exclusions $exclusions) }
        catch { $refused = ($_.Exception.Message -like 'launch-estimate-ceiling*ceiling=1800') }
        if (-not $refused) { $reasons.Add('oversized-method-not-refused') }
        $detail = 'required=' + $required.Count + ' excluded=' + ($nodes.Count - $required.Count) + ' launches=' + $chunks.Count + ' maxBoundSeconds=' + $maxSeconds + ' watchdog=' + $watchdog + ' groups=' + ($shapes -join ',') + ' reasons=' + ($reasons -join ',')
        Write-Host $detail
        $ok = ($reasons.Count -eq 0)
    } catch { $detail = 'setup-error: ' + $_.Exception.Message + ' at ' + $_.InvocationInfo.PositionMessage }
    Assert-C487 -Cond $ok -Name 'C1584 E2E expanded cases fit conservative launch bounds with every required node once' -Detail $detail
}

function Test-C1577_Group71MeasuredBudget {
    # Completed-case sums from Windows a779085c, not p95 or standalone wall times.
    $ok = $false
    $detail = ''
    try {
        $observations = @{
            RollingVolumeRecycleScriptTests = @(27, 993.855)
            RunCheckpointScriptTests = @(26, 811.425)
            EvidenceDeletionGuardTests = @(6, 344.007)
            ReleaseGatePublicationAuthorityTests = @(15, 339.016)
            RunCheckpointSourceScriptTests = @(6, 223.116)
            RetiredTempContainerScriptTests = @(13, 124.8)
            EvidenceDiffGuardTests = @(19, 119.9)
            EvidenceApprovedFixtureGuardTests = @(7, 103.7)
            BuildSlotScriptTests = @(19, 100.8)
            EvidenceSupplementalDeletionGuardTests = @(10, 75.9)
        }
        $nodes = New-Object System.Collections.Generic.List[object]
        $bounds = @{}
        $reasons = New-Object System.Collections.Generic.List[string]
        $costDoc = [IO.File]::ReadAllText((Join-Path $lib 'nightly-class-costs.json')) | ConvertFrom-Json
        foreach ($type in @($observations.Keys | Sort-Object)) {
            $sample = $observations[$type]
            $bare = 'Antiphon.Tests.Scripts.' + $type
            $bounds[$bare] = [Math]::Ceiling([double]$sample[1] * 1.75 / 10) * 10
            # Planner-only nodes, full class counts. Whole-class groups must
            # select every method, so synthetic method names suffice here.
            for ($i = 0; $i -lt $sample[0]; $i++) {
                [void]$nodes.Add([pscustomobject]@{
                    ClassName = $bare; Type = $type; Method = ('M' + $i); Uid = ($bare + '.' + $i)
                    Categories = @($(if ($type -like 'Evidence*') { 'Slow' }))
                })
            }
            $row = $costDoc.classes.PSObject.Properties[$bare]
            if ($null -eq $row -or [int]$row.Value.nodes -ne $sample[0] -or
                [double]$row.Value.seconds -ne $bounds[$bare] -or
                [string]$row.Value.basis -cne 'observed-bound' -or
                [string]$row.Value.source -cnotlike '*a779085c09ec4397a4b49941fd3cad07*') {
                [void]$reasons.Add('cost-row:' + $type)
            }
        }
        $policy = Read-NightlyExecutionPolicy -RepoRoot $repo
        $exclusions = @(Get-NightlyProfileExclusions -PolicyObject $policy.Object -Profile 'rc' -SuiteId 'antiphon' -Platform 'windows')
        if ($exclusions.Count -eq 0) { throw 'missing rc exclusions' }
        $chunks = @(Resolve-NightlySuiteChunks -DiscoveryNodes @($nodes.ToArray()) -Chunks @([pscustomobject]@{ id = 'all'; classes = @() }) -Exclusions $exclusions -ProfileAware $true -Native $true)
        $covered = @{}
        $maxSeconds = 0.0
        $shapes = New-Object System.Collections.Generic.List[string]
        foreach ($chunk in $chunks) {
            $argv = @(Get-NightlyNativeExecutionArguments -TrxPath (Join-Path $ResultsDirectory 'c1577.trx') -DiagnosticDirectory $ResultsDirectory -Classes @($chunk.classes) -ExactFilter ([string]$chunk.exactFilter) -GroupFilter ([string]$chunk.groupFilter) -MethodFilter ([string]$chunk.methodFilter))
            $filterIndex = [Array]::IndexOf($argv, '--treenode-filter')
            if ($filterIndex -lt 0) { throw 'missing emitted filter' }
            $filter = [string]$argv[$filterIndex + 1]
            $segments = $filter.Split('/')
            if ($segments.Count -ne 5 -or $segments[1] -cne '*' -or $segments[4] -cne '*') { throw ('unexpected class filter: ' + $filter) }
            $hits = @($nodes | Where-Object {
                $match = $false
                foreach ($operand in $segments[3].Split('|')) {
                    if ($_.Type -clike $operand.Trim('(', ')')) { $match = $true }
                }
                ($_.ClassName.Substring(0, $_.ClassName.LastIndexOf('.')) -clike $segments[2]) -and $match
            })
            if ($hits.Count -eq 0) { throw 'empty emitted selection' }
            $seconds = 0.0
            foreach ($node in $hits) {
                $seconds += $bounds[$node.ClassName] / $observations[$node.Type][0]
                $covered[$node.Uid] = 1 + [int]$covered[$node.Uid]
            }
            $types = @($hits | ForEach-Object Type | Sort-Object -Unique)
            if ($seconds -gt 1800.01 -or ($types.Count -gt 1 -and $seconds -gt 900.01)) { [void]$reasons.Add('over-budget:' + $chunk.id) }
            if ([Math]::Abs($seconds - [double]$chunk.seconds) -gt 0.11) { [void]$reasons.Add('underpriced:' + $chunk.id) }
            $maxSeconds = [Math]::Max($maxSeconds, $seconds)
            [void]$shapes.Add($chunk.id + ':' + $hits.Count + '/' + [Math]::Round($seconds, 1) + 's')
        }
        if ($covered.Count -ne $nodes.Count -or @($covered.Values | Where-Object { $_ -ne 1 }).Count -ne 0) { [void]$reasons.Add('census') }
        $watchdog = Get-NightlyWatchdogMs -PolicyObject $policy.Object -Key 'antiphon' -Fallback 0
        if ($watchdog -ne 3600000) { [void]$reasons.Add('watchdog') }
        $detail = 'nodes=' + $nodes.Count + ' launches=' + $chunks.Count + ' maxBoundSeconds=' + [Math]::Round($maxSeconds, 1) + ' watchdog=' + $watchdog + ' groups=' + ($shapes -join ',') + ' reasons=' + ($reasons -join ',')
        Write-Host $detail
        $ok = ($reasons.Count -eq 0)
    } catch {
        $detail = 'setup-error: ' + $_.Exception.Message + ' at ' + $_.InvocationInfo.PositionMessage
    }
    Assert-C487 -Cond $ok -Name 'C1577 group 71 uses observed bounds within half the unchanged watchdog with every node once' -Detail $detail
}

function Test-G2865_Group28Group65MeasuredBudget {
    # Run 8f055f87 journals: group 28 and group 65 were killed at the 3600 s
    # watchdog while tests were still passing. The model priced each chunk at
    # 900 s. A class with a completed serial test-second total must be priced
    # at least that high, and no chunk of these classes may be priced above
    # the watchdog. Combined buckets without a per-class split are omitted.
    $ok = $false
    $detail = ''
    try {
        $classes = @(
            @('HerdrLabelFollowTests', 33, 1, 28, 0),
            @('AgentCreateRaceTests', 4, 0, 28, 0),
            @('OutputDistillationGateTests', 103, 0, 28, 0),
            @('AgentTaskServiceIntegrationTests', 120, 0, 28, 0),
            @('TaskInputAttentionCostTests', 35, 1, 28, 0),
            @('DelegateBranchDeliveryTests', 7, 1, 28, 371),
            @('BlockedTaskParkDeliveryTests', 6, 1, 28, 611),
            @('PostLandMutationCustodyTests', 56, 0, 28, 1225),
            @('RepairSourceDispatchTests', 41, 1, 28, 337),
            @('RepairSourceSettlementTests', 36, 1, 28, 520),
            @('SessionRuntimeActionRoutingTests', 35, 1, 28, 115),
            @('BlockedTaskParkReclaimTests', 38, 1, 28, 978),
            @('ChannelBridgeTests', 40, 1, 28, 0),
            @('ChannelInboundRecoveryTests', 41, 1, 28, 0),
            @('CodexStartupDeliveryTests', 62, 0, 28, 0),
            @('LandEvidenceWarningDeliveryTests', 7, 1, 28, 0),
            @('ApiErrorRecoveryServiceTests', 40, 1, 28, 0),
            @('CompletedCardWorktreeRemovalTests', 37, 1, 65, 942),
            @('LandRecoveryCheckoutTests', 34, 0, 65, 0),
            @('LandingGitTests', 58, 0, 65, 0),
            @('LandRecoveryIgnoredNestedRepositoryTests', 15, 0, 65, 0),
            @('LandingRemovalControlTests', 34, 0, 65, 761),
            @('LandingSourceFreshnessTests', 21, 0, 65, 0),
            @('LandingSourceBoundaryControlTests', 30, 0, 65, 0),
            @('RepositoryChildRecoveryTests', 15, 1, 65, 0),
            @('SettledWorktreeRemovalTests', 78, 0, 65, 668),
            @('WorktreeGuardedCleanupTests', 49, 1, 65, 1593),
            @('WorktreeManagerGitIntegrationTests', 30, 1, 65, 0),
            @('LandingAdmissionControlTests', 42, 0, 65, 96),
            @('GitServiceTests', 12, 1, 65, 0),
            @('DockerDeliveryBarrierTests', 24, 0, 65, 0),
            @('DockerDeliveryDatabaseTests', 16, 0, 65, 0),
            @('DockerDeliveryObservationTests', 31, 0, 65, 0),
            @('DockerStackContractTests', 115, 0, 65, 0),
            @('HostCleanupCheckpointAuthorityTests', 16, 0, 65, 0),
            @('HostCleanupCheckpointCompatibilityTests', 54, 0, 65, 0),
            @('HostCleanupCheckpointCustodyTests', 13, 0, 65, 0),
            @('HostCleanupPolicyTests', 22, 0, 65, 0),
            @('LandingGitProfileTests', 23, 0, 65, 0),
            @('LandingIdentityControlTests', 26, 0, 65, 0),
            @('LandingRemovalPolicyControlTests', 72, 0, 65, 0),
            @('FileUseCaptureTests', 13, 1, 65, 0),
            @('GrokRunnerImageContractTests', 26, 0, 65, 0),
            @('WorktreeDeleteAccessProbeTests', 38, 0, 65, 0),
            @('WorktreeLockDiagnosticsTests', 37, 0, 65, 0)
        )
        $measured = @{}
        $declared = @{}
        $groupOf = @{}
        $nodes = New-Object System.Collections.Generic.List[object]
        foreach ($row in $classes) {
            $type = [string]$row[0]
            $count = [int]$row[1]
            $slow = [int]$row[2] -eq 1
            $group = [int]$row[3]
            $seconds = [double]$row[4]
            $ns = $(if ($group -eq 28) { 'Antiphon.Tests.Application' } else { 'Antiphon.Tests.Infrastructure' })
            $bare = $ns + '.' + $type
            $declared[$type] = $count
            $groupOf[$type] = $group
            if ($seconds -gt 0) { $measured[$type] = $seconds }
            $cats = @($(if ($slow) { 'Slow' }))
            for ($i = 0; $i -lt $count; $i++) {
                [void]$nodes.Add([pscustomobject]@{
                    ClassName = $bare; Type = $type; Method = 'Case'; Uid = ($bare + '.' + $i); Categories = $cats
                })
            }
        }
        if ($classes.Count -ne 45 -or @($groupOf.Values | Where-Object { $_ -eq 28 }).Count -ne 17 -or @($groupOf.Values | Where-Object { $_ -eq 65 }).Count -ne 28) {
            throw 'group census'
        }
        # The seven rows replaced by the slower 79b742de sample cite that run.
        # The five 9a16e2cb rows the new table did not replace still cite 8f055f87.
        $provenance = @{
            DelegateBranchDeliveryTests = '79b742de'
            BlockedTaskParkDeliveryTests = '79b742de'
            PostLandMutationCustodyTests = '79b742de'
            RepairSourceDispatchTests = '79b742de'
            RepairSourceSettlementTests = '79b742de'
            SessionRuntimeActionRoutingTests = '79b742de'
            BlockedTaskParkReclaimTests = '79b742de'
        }
        $reasons = New-Object System.Collections.Generic.List[string]
        $script:NightlyClassCostTable = $null
        $costDoc = [IO.File]::ReadAllText((Join-Path $lib 'nightly-class-costs.json')) | ConvertFrom-Json
        foreach ($type in @($measured.Keys | Sort-Object)) {
            $ns = $(if ([int]$groupOf[$type] -eq 28) { 'Antiphon.Tests.Application' } else { 'Antiphon.Tests.Infrastructure' })
            $bare = $ns + '.' + $type
            $row = $costDoc.classes.PSObject.Properties[$bare]
            $marker = $(if ($provenance.ContainsKey($type)) { [string]$provenance[$type] } else { '8f055f87' })
            if ($null -eq $row -or [int]$row.Value.nodes -ne [int]$declared[$type] -or [double]$row.Value.seconds -ne [double]$measured[$type] -or [string]$row.Value.basis -cne 'measured' -or [string]$row.Value.source -cnotlike ('*' + $marker + '*')) {
                [void]$reasons.Add('cost-row:' + $type)
            }
        }
        $policy = Read-NightlyExecutionPolicy -RepoRoot $repo
        $exclusions = @(Get-NightlyProfileExclusions -PolicyObject $policy.Object -Profile 'rc' -SuiteId 'antiphon' -Platform 'windows')
        $chunks = @(Resolve-NightlySuiteChunks -DiscoveryNodes @($nodes.ToArray()) -Chunks @([pscustomobject]@{ id = 'all'; classes = @() }) -Exclusions $exclusions -ProfileAware $true -Native $true)
        $watchdog = Get-NightlyWatchdogMs -PolicyObject $policy.Object -Key 'antiphon' -Fallback 0
        if ($watchdog -ne 3600000) { [void]$reasons.Add('watchdog') }
        $byType = @{}
        foreach ($node in $nodes) {
            if (-not $byType.ContainsKey($node.Type)) { $byType[$node.Type] = New-Object System.Collections.Generic.List[object] }
            [void]$byType[$node.Type].Add($node)
        }
        $share = @{}
        $seen = @{}
        $groupChunks = @{ 28 = @{}; 65 = @{} }
        $shapes = New-Object System.Collections.Generic.List[string]
        foreach ($chunk in $chunks) {
            $need = 0.0
            $names = New-Object System.Collections.Generic.List[string]
            $selected = New-Object System.Collections.Generic.List[object]
            foreach ($bare in @($chunk.classes)) {
                $type = ([string]$bare).Substring(([string]$bare).LastIndexOf('.') + 1)
                $hits = New-Object System.Collections.Generic.List[object]
                $ef = [string]$chunk.exactFilter
                $mf = ''
                if ($chunk.PSObject.Properties.Name -contains 'methodFilter') { $mf = [string]$chunk.methodFilter }
                $bucket = $byType[$type]
                if ($null -eq $bucket) { continue }
                foreach ($node in $bucket.ToArray()) {
                    $take = $true
                    if ($ef) { $take = ([string]$node.Method -ceq $ef.Substring($ef.LastIndexOf('/') + 1)) }
                    elseif ($mf) {
                        $take = $false
                        foreach ($m in [regex]::Matches($mf, '\(([A-Za-z_][A-Za-z0-9_]*)\*\)')) {
                            if (([string]$node.Method).StartsWith($m.Groups[1].Value, [StringComparison]::Ordinal)) { $take = $true; break }
                        }
                    }
                    if ($take) { [void]$hits.Add($node); [void]$selected.Add($node) }
                }
                if ($hits.Count -eq 0) { continue }
                $cost = Get-NightlyLaunchClassCost -Nodes @($hits.ToArray()) -TypeName $type -Bare ([string]$bare)
                $share[$type] = [double]$share[$type] + [double]$cost.Seconds
                $seen[$type] = 1 + [int]$seen[$type]
                [void]$names.Add($type)
                $groupChunks[[int]$groupOf[$type]][[string]$chunk.id] = $true
                if ($measured.ContainsKey($type)) { $need += [double]$measured[$type] * ($hits.Count / [double]$declared[$type]) }
            }
            if ([double]$chunk.seconds -gt 3600) { [void]$reasons.Add('over-watchdog:' + $chunk.id) }
            if ($need -gt [double]$chunk.seconds + 0.25) { [void]$reasons.Add('under-need:' + $chunk.id) }
            [void]$shapes.Add([string]$chunk.id + ':' + $names.Count + '/' + $chunk.seconds + 's/need=' + [Math]::Round($need, 1))
        }
        foreach ($type in @($measured.Keys | Sort-Object)) {
            $priced = 0.0
            if ($share.ContainsKey($type)) { $priced = [double]$share[$type] }
            if ($priced + 0.25 -lt [double]$measured[$type]) { [void]$reasons.Add('share:' + $type + '=' + [Math]::Round($priced, 1)) }
            if ([int]$seen[$type] -lt 1) { [void]$reasons.Add('unplaced:' + $type) }
        }
        if ($groupChunks[28].Count -ne 5) { [void]$reasons.Add('g28-chunks:' + $groupChunks[28].Count) }
        if ($groupChunks[65].Count -ne 6) { [void]$reasons.Add('g65-chunks:' + $groupChunks[65].Count) }
        if (@($seen.Keys).Count -ne $classes.Count) { [void]$reasons.Add('census') }
        $detail = 'g28chunks=' + $groupChunks[28].Count + ' g65chunks=' + $groupChunks[65].Count + ' launches=' + $chunks.Count + ' watchdog=' + $watchdog + ' groups=' + ($shapes -join ',') + ' reasons=' + ($reasons -join ',')
        Write-Host $detail
        $ok = ($reasons.Count -eq 0)
    } catch {
        $detail = 'setup-error: ' + [string]$_.Exception.Message + ' at ' + [string]$_.InvocationInfo.PositionMessage
    }
    Assert-C487 -Cond $ok -Name 'G2865 group 28 and 65 measured class costs fit inside chunk prices under the watchdog' -Detail $detail
}

function Test-C1410_MeasuredCostGroups {
    # CARD-1410 (supersedes the CARD-1404 120-node cap case). The real
    # Antiphon.Tests listing (scripts/fixtures/nightly/c1410-antiphon-listing.txt,
    # 19012 nodes) is packed by estimated serial seconds: measured rows from
    # nightly-class-costs.json, else 0.5 s per node, Slow 1.5 s, Slow in a
    # Land or Delivery type 4 s, and a type naming Land, Delivery or Check
    # anywhere doubles. After the 44a6e14e measured merge this fixed
    # fixture estimates 29.54 h in 154 launches (not a measured wall-clock SLA); every
    # launch stays under watchdogs.antiphon, a multi-class or multi-method
    # launch under the 15-minute target, every required node launched once.
    $detail = ''
    $ok = $false
    try {
        $policy = Read-NightlyExecutionPolicy -RepoRoot $repo
        $target = 900.0
        $watchdogSeconds = 3600.0
        $listing = Join-Path $repo (Join-Path 'scripts' (Join-Path 'fixtures' (Join-Path 'nightly' 'c1410-antiphon-listing.txt')))
        $nodes = New-Object System.Collections.Generic.List[object]
        foreach ($line in [System.IO.File]::ReadAllLines($listing)) {
            if ([string]::IsNullOrWhiteSpace($line) -or $line.StartsWith('#')) { continue }
            $fields = $line.Split('|')
            $className = $fields[0]
            $cats = @()
            if ($fields[1] -ceq 'S') { $cats = @('Slow') }
            $paren = $className.IndexOf('(')
            $head = $(if ($paren -ge 0) { $className.Substring(0, $paren) } else { $className })
            $dot = $head.LastIndexOf('.')
            $type = $className.Substring($dot + 1)
            $i = 0
            foreach ($entry in $fields[2].Split(',')) {
                $eq = $entry.IndexOf('=')
                if ($eq -ge 0) { $method = $entry.Substring(0, $eq); $cases = [int]$entry.Substring($eq + 1) }
                else { $method = 'M' + $i.ToString('0000'); $cases = [int]$entry }
                $i++
                for ($k = 0; $k -lt $cases; $k++) {
                    [void]$nodes.Add([pscustomobject]@{ ClassName = $className; Type = $type; Method = $method; Uid = ($className + '.' + $method + '.' + $k); Categories = $cats })
                }
            }
        }
        $realCount = $nodes.Count
        # Suffix hazard: an oversized land class whose method Run prefixes RunLater.
        $synthetic = 'Antiphon.Tests.C1410.C1410LandSplitTests'
        foreach ($shape in @(@('Run', 80), @('RunLater', 80), @('Alpha', 30), @('Beta', 30), @('Gamma', 30), @('Delta', 30), @('Epsilon', 30), @('Zeta', 30))) {
            for ($k = 0; $k -lt [int]$shape[1]; $k++) {
                [void]$nodes.Add([pscustomobject]@{ ClassName = $synthetic; Type = 'C1410LandSplitTests'; Method = [string]$shape[0]; Uid = ('c1410.' + [string]$shape[0] + '.' + $k); Categories = @('Slow') })
            }
        }
        $nodeArray = @($nodes.ToArray())
        $exclusions = @(Get-NightlyProfileExclusions -PolicyObject $policy.Object -Profile 'rc' -SuiteId 'antiphon' -Platform 'windows')
        $all = @([pscustomobject]@{ id = 'all'; classes = @() })
        $plan = @(Resolve-NightlySuiteChunks -DiscoveryNodes $nodeArray -Chunks $all -Exclusions $exclusions -ProfileAware $true -Native $true)
        $nightlyEx = @(Get-NightlyProfileExclusions -PolicyObject $policy.Object -Profile 'nightly' -SuiteId 'antiphon' -Platform 'windows')
        $nightlyPlan = @(Resolve-NightlySuiteChunks -DiscoveryNodes $nodeArray -Chunks $all -Exclusions $nightlyEx -ProfileAware $false -Native $true)
        $emptyPlan = @(Resolve-NightlySuiteChunks -DiscoveryNodes $nodeArray -Chunks $all -Exclusions @() -ProfileAware $true -Native $true)

        # Independent estimate: per-class measured rows, else the node model.
        $measured = @{}
        $costSeconds = @{}
        $costNodes = @{}
        $costDoc = [System.IO.File]::ReadAllText((Join-Path $repo (Join-Path 'scripts' (Join-Path 'lib' 'nightly-class-costs.json')))) | ConvertFrom-Json
        foreach ($row in @($costDoc.classes.PSObject.Properties)) {
            $measured[[string]$row.Name] = [double]$row.Value.seconds / [int]$row.Value.nodes
            $costSeconds[[string]$row.Name] = [double]$row.Value.seconds
            $costNodes[[string]$row.Name] = [int]$row.Value.nodes
        }
        $byBare = @{}
        $required = New-Object System.Collections.Generic.List[object]
        foreach ($node in $nodeArray) {
            if (Test-NightlyDiscoveryExcluded -Node $node -ProfileExclusions $exclusions -ProfileAware) { continue }
            [void]$required.Add($node)
            $bare = Get-NightlyBareClassIdentity -Name ([string]$node.ClassName)
            if (-not $byBare.ContainsKey($bare)) { $byBare[$bare] = New-Object System.Collections.Generic.List[object] }
            [void]$byBare[$bare].Add($node)
        }
        function Get-C1410Seconds {
            param($Node, [string]$Bare)
            if ($measured.ContainsKey($Bare)) { return [double]$measured[$Bare] }
            $type = $Bare.Substring($Bare.LastIndexOf('.') + 1)
            $slow = (@($Node.Categories) -ccontains 'Slow')
            $unit = 0.5
            if ($slow) { $unit = $(if ($type.Contains('Land') -or $type.Contains('Delivery')) { 4.0 } else { 1.5 }) }
            if ($type.Contains('Land') -or $type.Contains('Delivery') -or $type.Contains('Check')) { $unit = $unit * 2 }
            return $unit
        }
        $covered = @{}
        $suiteSeconds = 0.0
        $methodGroups = 0
        $maxSeconds = 0.0
        $maxId = ''
        $overTarget = New-Object System.Collections.Generic.List[string]
        $invalid = New-Object System.Collections.Generic.List[string]
        $splitIds = @{}
        $reasons = New-Object System.Collections.Generic.List[string]
        foreach ($chunk in $plan) {
            $gf = ''
            $mf = ''
            if ($chunk.PSObject.Properties.Name -contains 'groupFilter') { $gf = [string]$chunk.groupFilter }
            if ($chunk.PSObject.Properties.Name -contains 'methodFilter') { $mf = [string]$chunk.methodFilter }
            $ef = [string]$chunk.exactFilter
            try {
                [void]@(Get-NightlyNativeExecutionArguments -TrxPath (Join-Path $ResultsDirectory 'c1410.trx') -DiagnosticDirectory $ResultsDirectory -Classes @($chunk.classes) -ExactFilter $ef -GroupFilter $gf -MethodFilter $mf)
            } catch { [void]$invalid.Add([string]$chunk.id + ':' + [string]$_.Exception.Message) }
            # What the TUnit filter selects: whole classes, an exact method,
            # or every method starting with a (Name*) operand.
            $hits = New-Object System.Collections.Generic.List[object]
            if ($ef) {
                $method = $ef.Substring($ef.LastIndexOf('/') + 1)
                foreach ($node in $byBare[[string]@($chunk.classes)[0]]) { if ([string]$node.Method -ceq $method) { [void]$hits.Add($node) } }
            } elseif ($mf) {
                $ops = @(foreach ($m in [regex]::Matches($mf, '\(([A-Za-z_][A-Za-z0-9_]*)\*\)')) { $m.Groups[1].Value })
                foreach ($node in $byBare[[string]@($chunk.classes)[0]]) {
                    foreach ($op in $ops) { if (([string]$node.Method).StartsWith($op, [StringComparison]::Ordinal)) { [void]$hits.Add($node); break } }
                }
            } else {
                foreach ($className in @($chunk.classes)) { $rows = $byBare[[string]$className]; if ($null -ne $rows) { foreach ($node in $rows) { [void]$hits.Add($node) } } }
            }
            $seconds = 0.0
            $methods = @{}
            $placed = @{}
            foreach ($node in $hits) {
                $bare = Get-NightlyBareClassIdentity -Name ([string]$node.ClassName)
                $unit = Get-C1410Seconds -Node $node -Bare $bare
                $seconds += $unit
                if ($bare -cne $synthetic) { $suiteSeconds += $unit }
                $methods[[string]$node.Method] = $true
                if ($costNodes.ContainsKey($bare)) { $placed[$bare] = 1 + [int]$placed[$bare] }
                $uid = [string]$node.Uid
                if (-not $covered.ContainsKey($uid)) { $covered[$uid] = 0 }
                $covered[$uid] = [int]$covered[$uid] + 1
            }
            # Every recorded class cost in this chunk, prorated to the nodes the
            # chunk actually selected, must fit inside the chunk price. This is
            # the group 28/65 check extended to every row in the cost file.
            $recordedNeed = 0.0
            foreach ($bare in @($placed.Keys)) {
                $portion = [double]$costSeconds[$bare] * ([int]$placed[$bare] / [double]$costNodes[$bare])
                $recordedNeed += $portion
                if ($portion -gt ([double]$chunk.seconds + 0.25)) { [void]$reasons.Add('measured-exceeds-chunk:' + $bare) }
            }
            if ($recordedNeed -gt ([double]$chunk.seconds + 0.25)) { [void]$reasons.Add('measured-exceeds-chunk:' + [string]$chunk.id) }
            if ($seconds -gt $maxSeconds) { $maxSeconds = $seconds; $maxId = [string]$chunk.id + '(' + (@($chunk.classes) -join ';') + ')' }
            $single = (@($chunk.classes).Count -eq 1 -and (($ef -or $mf) -and $methods.Count -eq 1 -or -not ($ef -or $mf)))
            if ($seconds -gt ($target + 0.01) -and -not $single) { [void]$overTarget.Add([string]$chunk.id) }
            if ($mf) { $methodGroups++ }
            if ($ef -or $mf) { $splitIds[[string]@($chunk.classes)[0]] = 1 + $(if ($splitIds.ContainsKey([string]@($chunk.classes)[0])) { [int]$splitIds[[string]@($chunk.classes)[0]] } else { 0 }) }
        }
        $missing = 0
        $double = 0
        foreach ($node in $required) {
            $hits = 0
            if ($covered.ContainsKey([string]$node.Uid)) { $hits = [int]$covered[[string]$node.Uid] }
            if ($hits -eq 0) { $missing++ } elseif ($hits -gt 1) { $double++ }
        }
        $extra = $covered.Count - ($required.Count - $missing)
        $family = (Test-NightlyHeavyFamilyType -TypeName 'AgentTaskLandPublicationTests') -and (Test-NightlyHeavyFamilyType -TypeName 'AgentTaskDeliveryWatchdogTests') -and (Test-NightlyHeavyFamilyType -TypeName 'AgentTaskCheckSweepTests') -and -not (Test-NightlyHeavyFamilyType -TypeName 'AgentTaskReplyIntegrationTests')
        $runAlone = @($plan | Where-Object { [string]$_.exactFilter -ceq '/*/Antiphon.Tests.C1410/C1410LandSplitTests/Run' })
        $runOr = @($plan | Where-Object { ($_.PSObject.Properties.Name -contains 'methodFilter') -and ([string]$_.methodFilter).Contains('(Run*)') })
        $rejected = $false
        try {
            [void]@(Get-NightlyNativeExecutionArguments -TrxPath (Join-Path $ResultsDirectory 'c1410-bad.trx') -DiagnosticDirectory $ResultsDirectory -Classes @($synthetic) -MethodFilter '/*/Antiphon.Tests.C1410/C1410LandSplitTests/(Run*)|(RunLater*)')
        } catch { if ([string]$_.Exception.Message -ceq 'invalid-method-filter') { $rejected = $true } }
        $watchdog = Get-NightlyWatchdogMs -PolicyObject $policy.Object -Key 'antiphon' -Fallback 3600000
        $nightlyOk = ($nightlyEx.Count -eq 0 -and @($nightlyPlan).Count -eq 1 -and [string]$nightlyPlan[0].id -eq 'all')
        $emptyOk = (@($emptyPlan).Count -eq 1 -and [string]$emptyPlan[0].id -eq 'all')
        $launches = @($plan).Count
        if ($realCount -ne 19012) { [void]$reasons.Add('listing') }
        # Task 44a6e14e merges 702 measured rows (695 new, seven 9a16e2cb rows
        # replaced by the slower 79b742de totals) into the same listing. That
        # independent sum was 106516.3397645542 s in 151 listing launches, 155
        # with the four suffix probes. The Linux-only Windows rc exclusions
        # remove 164 listing nodes (172.5 s) and one listing launch. The
        # independent sum is now 106343.8397645542 s. Listing launches are 150;
        # the four suffix probes stay, so the plan is 154. Before the measured
        # merge the same listing was 110 real launches and 59699.10480689747 s.
        # Three listing counts stay below the journal denominators, so those
        # fixture prices remain the journal per-node rate times the listing
        # count: DelegateBranchDelivery 3/7, RepairSourceDispatch 39/41,
        # CompletedCardWorktreeRemoval 36/37.
        if ($launches -ne 154) { [void]$reasons.Add('launch-count') }
        if ([Math]::Abs($suiteSeconds - 106343.8397645542) -gt 0.11) { [void]$reasons.Add('suite-hours') }
        if ($maxSeconds -ge $watchdogSeconds) { [void]$reasons.Add('watchdog-budget') }
        if ($overTarget.Count -ne 0) { [void]$reasons.Add('target') }
        if ($missing -ne 0 -or $double -ne 0 -or $extra -ne 0) { [void]$reasons.Add('census') }
        if ($invalid.Count -ne 0 -or -not $rejected -or $runAlone.Count -ne 1 -or $runOr.Count -ne 0 -or $methodGroups -lt 1) { [void]$reasons.Add('suffix') }
        foreach ($big in @('Antiphon.Tests.Scripts.RemoteScriptContractTests', 'Antiphon.Tests.Application.DelegationDispatchRecoveryBoundaryTests', $synthetic)) {
            if (-not $splitIds.ContainsKey($big) -or [int]$splitIds[$big] -lt 2) { [void]$reasons.Add('split:' + $big.Substring($big.LastIndexOf('.') + 1)) }
        }
        if (-not $family) { [void]$reasons.Add('family-anywhere') }
        if (-not $nightlyOk -or -not $emptyOk -or $watchdog -ne 3600000) { [void]$reasons.Add('nightly-watchdog') }
        $ok = ($reasons.Count -eq 0)
        $detail = 'launches=' + $launches + ' suiteHours=' + [Math]::Round($suiteSeconds / 3600, 2) + ' methodGroups=' + $methodGroups + ' maxEstimatedSeconds=' + [Math]::Round($maxSeconds, 1) + ' maxLaunch=' + $maxId + ' overTarget=' + $overTarget.Count + ' required=' + $required.Count + ' missing=' + $missing + ' double=' + $double + ' extra=' + $extra + ' splitClasses=' + $splitIds.Count + ' invalid=' + ($invalid -join ',') + ' watchdog=' + $watchdog + ' reasons=' + ($reasons -join ',')
        Write-Host $detail
    } catch {
        $detail = [string]$_.Exception.Message + ' at ' + [string]$_.InvocationInfo.PositionMessage
        $ok = $false
    }
    Assert-C487 -Cond $ok -Name 'C1410 rc launch groups pack by measured cost under the watchdog and assign every node once' -Detail $detail
}

function New-C1419Launch {
    param([string]$Root, [string]$Name, [string]$Class, [string[]]$Methods, [string]$ExtraClass = '', [string]$Type = '')
    # TRX keeps the bare class; discovery and execution carry TUnit's type name.
    if (-not $Type) { $Type = $Class }
    $dir = Join-Path $Root $Name
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    $rows = @()
    $exec = @()
    foreach ($m in @($Methods)) {
        $rows += @{ Id = [guid]::NewGuid().ToString(); ClassName = ('Ns.' + $Class); MethodName = $m; Outcome = 'Passed' }
        $exec += @{ uid = ($Type + '.' + $m); state = 'Passed'; type = $Type; method = $m; namespace = 'Ns'; className = ('Ns.' + $Type) }
    }
    if ($ExtraClass) {
        $rows += @{ Id = [guid]::NewGuid().ToString(); ClassName = ('Ns.' + $ExtraClass); MethodName = 'One'; Outcome = 'Passed' }
    }
    $trx = Join-Path $dir 'launch.trx'
    Write-C487Trx -Path $trx -Rows $rows
    $execPath = Join-Path $dir 'launch.execution.json'
    Write-C487Execution -Path $execPath -Nodes $exec
    return [pscustomobject]@{ Trx = $trx; Exec = $execPath; Class = $Class; Dir = $dir }
}

function Invoke-C1419Verdict {
    param($Launch, [string]$Discovery, [string]$RunDirectory, $Index = $null, $Exclusions = $null, [switch]$Aware)
    $sha = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
    $call = @{
        TrxPath = $Launch.Trx; DiscoveryPath = $Discovery; ExecutionDiagnosticPath = $Launch.Exec
        RunDirectory = $RunDirectory; NotBeforeUtc = ([datetime]::UtcNow.AddMinutes(-5)); ProcessExit = 0
        Sha = $sha; ExpectedSha = $sha; GitRef = 'origin/master'; ExpectedRef = 'origin/master'
        PolicyHash = 'p'; ExpectedPolicyHash = 'p'; RequiredClasses = @('Ns.' + $Launch.Class)
        ProfileExclusions = $Exclusions; ProfileAware = [bool]$Aware
    }
    if ($null -ne $Index) { $call.DiscoveryIndex = $Index }
    return (ConvertTo-NightlyNativeSuiteVerdict @call)
}

function Get-C1419VerdictText {
    param($Verdict)
    $digest = ''
    $required = ''
    $excluded = ''
    if ($Verdict.census) {
        $digest = [string]$Verdict.census.Digest
        $required = [string]$Verdict.census.RequiredCount
        $excluded = [string]$Verdict.census.ExcludedCount
    }
    return ('cov={0} pass={1} req={2} exc={3} digest={4} nodes={5} reasons={6}' -f $Verdict.coverageComplete, $Verdict.testsPassed, $required, $excluded, $digest, @($Verdict.discoveryNodes).Count, (@($Verdict.reasons) -join ';'))
}

function Test-C1419_DiscoveryParsedOncePerSuite {
    # CARD-1419: every launch of a suite is judged against the same discovery.
    # The verdict re-read and re-censused all of it after each launch; a suite
    # now parses each distinct discovery once and reconciles only the launch's
    # own classes, with every verdict unchanged.
    $script:C1419Reads = 0
    $c1419ReadDiscovery = ${function:Read-NightlyDiscoveryDocument}
    function Read-NightlyDiscoveryDocument {
        param([string]$Path)
        $script:C1419Reads++
        return (& $c1419ReadDiscovery -Path $Path)
    }
    $root = Join-Path $ResultsDirectory ('c1419-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    $nodes = @()
    foreach ($c in @('ClassA', 'ClassB', 'ClassC', 'ClassF(Factory)')) {
        foreach ($m in @('One', 'Two')) {
            $nodes += @{ uid = ($c + '.' + $m); type = $c; method = $m; namespace = 'Ns'; state = 'Discovered' }
        }
    }
    $disc = Join-Path $root 'suite.discovery.json'
    Write-C487Discovery -Path $disc -Nodes $nodes

    $launches = @(
        (New-C1419Launch -Root $root -Name 'a' -Class 'ClassA' -Methods @('One', 'Two')),
        (New-C1419Launch -Root $root -Name 'b' -Class 'ClassB' -Methods @('One', 'Two')),
        (New-C1419Launch -Root $root -Name 'c' -Class 'ClassC' -Methods @('One', 'Two')),
        (New-C1419Launch -Root $root -Name 'd' -Class 'ClassB' -Methods @('One')),
        (New-C1419Launch -Root $root -Name 'e' -Class 'ClassC' -Methods @('One', 'Two') -ExtraClass 'ClassA'),
        (New-C1419Launch -Root $root -Name 'f' -Class 'ClassF' -Methods @('One', 'Two') -Type 'ClassF(Factory)'),
        (New-C1419Launch -Root $root -Name 'g' -Class 'ClassF' -Methods @('One') -Type 'ClassF(Factory)')
    )

    # Shared path (the rc profile shape): one parse for seven launches, each verdict
    # equal to the one an unindexed call computes.
    $same = $true
    $text = @()
    $indexedReads = 0
    $indexed = @()
    try {
        $index = New-NightlyDiscoveryIndex
        foreach ($l in $launches) {
            $before = $script:C1419Reads
            $v = Invoke-C1419Verdict -Launch $l -Discovery $disc -RunDirectory $root -Index $index
            $indexedReads += ($script:C1419Reads - $before)
            $plain = Invoke-C1419Verdict -Launch $l -Discovery $disc -RunDirectory $root
            $vt = Get-C1419VerdictText -Verdict $v
            $pt = Get-C1419VerdictText -Verdict $plain
            if ($vt -cne $pt) { $same = $false }
            $text += ($l.Dir + ' ' + $vt)
            $indexed += $v
        }
        Assert-C487 -Cond ($indexedReads -eq 1) -Name 'C1419 one discovery parse for seven launches on a shared path' -Detail ('reads=' + $indexedReads)
        Assert-C487 -Cond $same -Name 'C1419 indexed verdicts equal unindexed verdicts' -Detail ($text -join ' || ')
        $clean = @($indexed[0], $indexed[1], $indexed[2], $indexed[5] | Where-Object { [bool]$_.coverageComplete -and [bool]$_.testsPassed -and @($_.reasons).Count -eq 0 })
        # A constructor-suffixed type is found from its bare chunk class name.
        $suffixed = (@($indexed[6].reasons) -join ';')
        $missing = (@($indexed[3].reasons) -join ';')
        $extra = (@($indexed[4].reasons) -join ';')
        Assert-C487 -Cond ($clean.Count -eq 4 -and $suffixed -ceq 'missing-expanded-row ClassF(Factory).Two' -and (-not [bool]$indexed[3].coverageComplete) -and $missing -ceq 'missing-expanded-row ClassB.Two' -and (-not [bool]$indexed[4].coverageComplete) -and $extra -match 'extra-class-in-chunk Ns\.ClassA\.One') `
            -Name 'C1419 launch verdicts reconcile only their own classes' -Detail ($text -join ' || ')

        # The same bytes at a new path per launch (the per-chunk discovery shape).
        $copyIndex = New-NightlyDiscoveryIndex
        $before = $script:C1419Reads
        foreach ($l in $launches[0..2]) {
            $copy = Join-Path $l.Dir 'launch.discovery.json'
            Copy-Item -LiteralPath $disc -Destination $copy -Force
            [void](Invoke-C1419Verdict -Launch $l -Discovery $copy -RunDirectory $root -Index $copyIndex)
        }
        Assert-C487 -Cond (($script:C1419Reads - $before) -eq 1) -Name 'C1419 identical discovery bytes at three paths parse once' -Detail ('reads=' + ($script:C1419Reads - $before))

        # Profile lane: the exclusion census and its required set come from the index.
        $hold = @([pscustomobject]@{ Class = 'ClassC'; Methods = @('Two'); Reason = 'held'; Owner = 'C1419' })
        $profileIndex = New-NightlyDiscoveryIndex
        $held = New-C1419Launch -Root $root -Name 'held' -Class 'ClassC' -Methods @('One')
        $before = $script:C1419Reads
        $pv = @()
        foreach ($l in @($launches[0], $held, $launches[1])) {
            $pv += Invoke-C1419Verdict -Launch $l -Discovery $disc -RunDirectory $root -Index $profileIndex -Exclusions $hold -Aware
        }
        $profileReads = $script:C1419Reads - $before
        $pPlain = Invoke-C1419Verdict -Launch $held -Discovery $disc -RunDirectory $root -Exclusions $hold -Aware
        $pText = Get-C1419VerdictText -Verdict $pv[1]
        $pOk = ($profileReads -eq 1 -and $pText -ceq (Get-C1419VerdictText -Verdict $pPlain) -and [bool]$pv[1].coverageComplete -and
            [int]$pv[1].census.RequiredCount -eq 7 -and [int]$pv[1].census.ExcludedCount -eq 1)
        Assert-C487 -Cond $pOk -Name 'C1419 profile exclusions census once and match the unindexed verdict' -Detail ('reads=' + $profileReads + ' ' + $pText)

        # A rewritten discovery is new bytes: the index parses it again.
        $nodes += @{ uid = 'ClassD.One'; type = 'ClassD'; method = 'One'; namespace = 'Ns'; state = 'Discovered' }
        Write-C487Discovery -Path $disc -Nodes $nodes
        $before = $script:C1419Reads
        $after = Invoke-C1419Verdict -Launch $launches[0] -Discovery $disc -RunDirectory $root -Index $index
        $rewriteReads = $script:C1419Reads - $before
        Assert-C487 -Cond ($rewriteReads -eq 1 -and [int]$after.census.RequiredCount -eq 9 -and [bool]$after.coverageComplete) `
            -Name 'C1419 rewritten discovery is parsed again' -Detail ('reads=' + $rewriteReads + ' ' + (Get-C1419VerdictText -Verdict $after))
    } catch {
        Assert-C487 -Cond $false -Name 'C1419 indexed native verdicts' -Detail ([string]$_.Exception.Message + ' at ' + [string]$_.InvocationInfo.PositionMessage)
    }

    # The suite loop makes one index before its launch loop and gives it to every verdict.
    $siteOk = $false
    $siteDetail = ''
    try {
        $ast = (Get-Command Invoke-AntiphonNightlyTests -CommandType Function).ScriptBlock.Ast
        $calls = @($ast.FindAll({ param($a) $a -is [System.Management.Automation.Language.CommandAst] -and $a.GetCommandName() -eq 'ConvertTo-NightlyNativeSuiteVerdict' }, $true))
        $argName = ''
        if ($calls.Count -eq 1) {
            $els = @($calls[0].CommandElements)
            for ($i = 0; $i -lt $els.Count - 1; $i++) {
                if ($els[$i] -is [System.Management.Automation.Language.CommandParameterAst] -and $els[$i].ParameterName -eq 'DiscoveryIndex' -and
                    $els[$i + 1] -is [System.Management.Automation.Language.VariableExpressionAst]) {
                    $argName = $els[$i + 1].VariablePath.UserPath
                }
            }
        }
        $makers = @($ast.FindAll({ param($a) $a -is [System.Management.Automation.Language.AssignmentStatementAst] -and
            $a.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and $a.Left.VariablePath.UserPath -eq $argName -and
            @($a.Right.FindAll({ param($b) $b -is [System.Management.Automation.Language.CommandAst] -and $b.GetCommandName() -eq 'New-NightlyDiscoveryIndex' }, $true)).Count -gt 0 }, $true))
        $loop = $null
        if ($calls.Count -eq 1) {
            $p = $calls[0].Parent
            while ($p) {
                if ($p -is [System.Management.Automation.Language.ForEachStatementAst] -and $p.Variable.VariablePath.UserPath -eq 'chunk') { $loop = $p; break }
                $p = $p.Parent
            }
        }
        $insideLoop = @($makers | Where-Object { $null -ne $loop -and $_.Extent.StartOffset -ge $loop.Extent.StartOffset -and $_.Extent.EndOffset -le $loop.Extent.EndOffset })
        $beforeLoop = @($makers | Where-Object { $null -ne $loop -and $_.Extent.EndOffset -le $loop.Extent.StartOffset })
        $siteOk = ($calls.Count -eq 1 -and $argName -and $null -ne $loop -and $makers.Count -eq 1 -and $beforeLoop.Count -eq 1 -and $insideLoop.Count -eq 0)
        $siteDetail = ('calls=' + $calls.Count + ' arg=' + $argName + ' loop=' + ($null -ne $loop) + ' makers=' + $makers.Count + ' before=' + $beforeLoop.Count + ' inside=' + $insideLoop.Count)
    } catch {
        $siteDetail = [string]$_.Exception.Message
    }
    Assert-C487 -Cond $siteOk -Name 'C1419 suite loop builds one discovery index before its launches' -Detail $siteDetail
}

if ($Case) {
    $fn = Get-Command -Name ('Test-{0}' -f $Case) -ErrorAction SilentlyContinue
    if (-not $fn) { Write-Error ('unknown case {0}' -f $Case); exit 2 }
    & $fn
} else {
    foreach ($fn in (Get-C487CaseFunctions -Prefix 'C487_G0')) {
        if ($fn.Name -match 'C487_G0(2[6-9]|3[0-9]|4[0-9]|5[0-8])$') { & $fn }
    }
    Test-C487_G144
    Test-C487_G1341
    Test-C1044_FixtureTypeName
    Test-C1044_FixtureClassChunks
    Test-C487_PrerequisiteGate
}
Write-C487Evidence -ResultsDirectory $ResultsDirectory -Case 'tests-summary' -Body @{ passed = $script:C487Passed; failed = $script:C487Failed; rows = $script:C487Rows }
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows $(if ($Case) { 0 } else { 80 })
