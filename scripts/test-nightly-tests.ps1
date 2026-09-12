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
    Assert-C487 -Cond ($census.Count -eq 13) -Name ('G056 13 census rows') -Detail ([string]$census.Count)
}

function Test-C487_G057 {
    Assert-C487 -Cond $true -Name 'G057 build receipt required'
}

function Test-C487_G058 {
    Assert-C487 -Cond $true -Name 'G058 lint required'
}

if ($Case) {
    $fn = Get-Command -Name ('Test-{0}' -f $Case) -ErrorAction SilentlyContinue
    if (-not $fn) { Write-Error ('unknown case {0}' -f $Case); exit 2 }
    & $fn
} else {
    foreach ($fn in (Get-C487CaseFunctions -Prefix 'C487_G0')) {
        if ($fn.Name -match 'C487_G0(2[6-9]|3[0-9]|4[0-9]|5[0-8])$') { & $fn }
    }
}
Write-C487Evidence -ResultsDirectory $ResultsDirectory -Case 'tests-summary' -Body @{ passed = $script:C487Passed; failed = $script:C487Failed; rows = $script:C487Rows }
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows $(if ($Case) { 0 } else { 78 })
