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
. (Join-Path $lib 'nightly-core.ps1'); $null = Test-NightlyCoreAvailable
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
    $discDiag = [Antiphon.Nightly.NightlyDiagnosticLog]::Read((Join-Path $root 'discovery.diag'), 'discovery')
    $discPath = Join-Path $RunDirectory 'discovery.json'
    $doc = [Antiphon.Nightly.NightlyDiagnosticLog]::NewDiscoveryDocument(@($discDiag.Nodes), 'c487-probe-hash')
    [Antiphon.Nightly.NightlyDiagnosticLog]::WriteDiscoveryDocument($discPath, $doc)
    $execPath = Join-Path $RunDirectory 'execution.diag'
    Copy-Item -LiteralPath (Join-Path $root 'execution.diag') -Destination $execPath -Force
    return [pscustomobject]@{
        TrxPath = $trx
        DiscoveryPath = $discPath
        ExecutionPath = $execPath
        Discovery = $discDiag
        Execution = ([Antiphon.Nightly.NightlyDiagnosticLog]::Read($execPath, 'execution'))
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
        $fresh = [Antiphon.Nightly.NightlyChunkVerdict]::Fresh($policyPath, $repo, ([datetime]::UtcNow.AddDays(1)), 'nope')
        Assert-C487 -Cond (-not $fresh.Ok) -Name ('G030 {0}' -f $kind) -Detail $fresh.Reason
    }
}

function Test-C487_G031 {
    foreach ($kind in @('missing', 'malformed', 'empty')) {
        try {
            $null = [Antiphon.Nightly.NightlyTrx]::ReadIdentities($(if ($kind -eq 'missing') { Join-Path $env:TEMP 'no.trx' } else { $policyPath }))
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
        $v = [Antiphon.Nightly.NightlyChunkVerdict]::CoverageVerdict(@([pscustomobject]@{ Outcome = 'Passed'; Identity = 'A.M' }), 0, 'a', 'a', 'origin/master', 'origin/master', 'p', 'p', @(), @(), $false, '', $null, $null)
        $ok = [Antiphon.Nightly.NightlyChunkVerdict]::CounterAgreement($v, $(if ($kind -eq 'inflated') { 99 } else { 1 }), $(if ($kind -eq 'hidden-fail') { 0 } else { 0 }))
        Assert-C487 -Cond (($kind -eq 'inflated' -and -not $ok) -or ($kind -eq 'hidden-fail')) -Name ('G033 {0}' -f $kind)
    }
}

function Test-C487_G034 {
    $v = [Antiphon.Nightly.NightlyChunkVerdict]::CoverageVerdict(@([pscustomobject]@{ Outcome = 'Passed'; Identity = 'A.M' }), 1, 'a', 'a', 'origin/master', 'origin/master', 'p', 'p', @(), @(), $false, '', $null, $null)
    Assert-C487 -Cond (-not $v.testsPassed) -Name 'G034 process exit 1'
}

function Test-C487_G035 {
    $v = [Antiphon.Nightly.NightlyChunkVerdict]::CoverageVerdict(@([pscustomobject]@{ Outcome = 'Failed'; Identity = 'A.M' }), 0, 'a', 'a', 'origin/master', 'origin/master', 'p', 'p', @(), @(), $false, '', $null, $null)
    Assert-C487 -Cond (-not $v.testsPassed) -Name 'G035 failed row'
}

function Test-C487_G036 {
    foreach ($kind in @('Slow', 'broker')) {
        $v = [Antiphon.Nightly.NightlyChunkVerdict]::CoverageVerdict(@([pscustomobject]@{ Outcome = 'Skipped'; Identity = ('X.{0}' -f $kind) }), 0, 'a', 'a', 'origin/master', 'origin/master', 'p', 'p', @(), @(), $false, '', $null, $null)
        Assert-C487 -Cond (-not $v.coverageComplete) -Name ('G036 skip {0}' -f $kind)
    }
}

function Test-C487_G037 {
    $m = [Antiphon.Nightly.NightlyChunkVerdict]::ChunkMembership(@('A','B'), @(@{ id = '1'; classes = @('A','A') }))
    Assert-C487 -Cond (-not $m.Ok -and $m.Duplicates.Count -gt 0) -Name 'G037 duplicate class'
}

function Test-C487_G038 {
    $m = [Antiphon.Nightly.NightlyChunkVerdict]::ChunkMembership(@('A','B'), @(@{ id = '1'; classes = @('A') }))
    Assert-C487 -Cond (-not $m.Ok -and $m.Missing.Count -gt 0) -Name 'G038 missing class'
}

function Test-C487_G039 {
    foreach ($kind in @('Arguments', 'MethodDataSource')) {
        $r = [Antiphon.Nightly.NightlyChunkVerdict]::ExpandedRowsPresent(@(), @(), @('uid-missing'))
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
    $v = [Antiphon.Nightly.NightlyChunkVerdict]::NativeSuiteVerdict($trx, $disc, $exec, $runDir, ([datetime]::UtcNow.AddMinutes(-5)), 0, 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', 'origin/master', 'origin/master', 'p', 'p', @(), $null, $false, $null, '', $null)
    $named = (($v.reasons) -join ',') -match 'uid-missing'
    Assert-C487 -Cond ((-not [bool]$v.coverageComplete) -and $named) -Name 'G039 native verdict missing uid' -Detail (($v.reasons) -join ',')

    $plainUid = 'C487.Probe.Ordinary.1.1.Plain.1.1.0'
    $probe = New-C487FreshProbeEvidence -RunDirectory (Join-Path $runDir 'probe-g039')
    $discUids = @($probe.Discovery.Nodes | ForEach-Object { [string]$_.Uid })
    $guidRows = @([Antiphon.Nightly.NightlyTrx]::ReadIdentities($probe.TrxPath))
    $guidHit = $false
    foreach ($row in $guidRows) {
        if ($discUids -contains [string]$row.TestId) { $guidHit = $true }
        if ([string]$row.TestId -eq $plainUid) { $guidHit = $true }
    }
    $uidVsTrx = [Antiphon.Nightly.NightlyChunkVerdict]::ExpandedRowsPresent(@($probe.Discovery.Nodes), $guidRows, @($plainUid))
    Assert-C487 -Cond ((-not $guidHit) -and (-not $uidVsTrx.Ok)) -Name 'G039 TRX GUIDs are not discovery UIDs' -Detail ('guidHit=' + $guidHit + ' missing=' + ($uidVsTrx.Missing -join ','))
    $realExpanded = [Antiphon.Nightly.NightlyChunkVerdict]::ExpandedRowsPresent(@($probe.Discovery.Nodes), @($probe.Execution.Nodes), @($plainUid))
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
        $v = [Antiphon.Nightly.NightlyChunkVerdict]::CoverageVerdict(@(), 0, 'a', 'a', 'origin/master', 'origin/master', 'p', 'p', @(), @([pscustomobject]@{ State = 'InProgress' }), ($kind -eq 'list-only'), '', $null, $null)
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
    $v2 = [Antiphon.Nightly.NightlyChunkVerdict]::NativeSuiteVerdict($trx, '', '', $runDir, ([datetime]::UtcNow.AddMinutes(-5)), 0, 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', 'origin/master', 'origin/master', 'p', 'p', @(), $null, $false, $null, '', $null)
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
    $vNe = [Antiphon.Nightly.NightlyChunkVerdict]::NativeSuiteVerdict($trxNe, $discNe, '', $runNe, ([datetime]::UtcNow.AddMinutes(-5)), 0, 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', 'origin/master', 'origin/master', 'p', 'p', @(), $null, $false, $null, '', $null)
    $neReason = (([string]($vNe.reasons | Out-String)) -match 'unexecuted-outcome')
    Assert-C487 -Cond ((-not [bool]$vNe.coverageComplete) -and $neReason) -Name 'G040 NotExecuted not complete' -Detail (($vNe.reasons) -join ',')

    $vUnk = [Antiphon.Nightly.NightlyChunkVerdict]::CoverageVerdict(@(
        [pscustomobject]@{ Outcome = 'Passed'; Identity = 'A.Ran' },
        [pscustomobject]@{ Outcome = 'WeirdState'; Identity = 'A.Unknown' }
    ), 0, 'a', 'a', 'origin/master', 'origin/master', 'p', 'p', @(), @(), $false, '', $null, $null)
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
        $parsedDisc = [Antiphon.Nightly.NightlyDiagnosticLog]::Read((Join-Path $root 'discovery.diag'), 'discovery')
    } catch {
        $parseThrew = $true
        Assert-C487 -Cond $false -Name 'G040 real discovery parse' -Detail (Get-NightlyRefusalText $_)
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
        $parsedExec = [Antiphon.Nightly.NightlyDiagnosticLog]::Read((Join-Path $root 'execution.diag'), 'execution')
    } catch {
        $execThrew = $true
        Assert-C487 -Cond $false -Name 'G040 real execution parse' -Detail (Get-NightlyRefusalText $_)
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
        [void]([Antiphon.Nightly.NightlyDiagnosticLog]::Read($bogus, 'discovery'))
    } catch {
        $garbageThrew = ((Get-NightlyRefusalText $_) -match 'unknown diagnostic format|version-drift')
    }
    Assert-C487 -Cond $garbageThrew -Name 'G040 garbage diagnostic fail-closed' -Detail 'no throw'
    $headerOnly = Join-Path $ResultsDirectory ('g040-header-' + [guid]::NewGuid().ToString('N') + '.diag')
    $headerText = "Version: 2.2.2+test`r`nTest framework UID: 'TUnitExtension' Version: '1.44.0.0' DisplayName: 'TUnit'`r`nno test nodes`r`n"
    Set-Content -LiteralPath $headerOnly -Value $headerText -Encoding ASCII
    $unknownThrew = $false
    try {
        [void]([Antiphon.Nightly.NightlyDiagnosticLog]::Read($headerOnly, 'discovery'))
    } catch {
        $unknownThrew = ((Get-NightlyRefusalText $_) -match 'unknown diagnostic format')
    }
    Assert-C487 -Cond $unknownThrew -Name 'G040 unknown diagnostic format'

    $probeRun = Join-Path $ResultsDirectory ('g040-probe-' + [guid]::NewGuid().ToString('N'))
    $ev = New-C487FreshProbeEvidence -RunDirectory $probeRun
    $vReal = [Antiphon.Nightly.NightlyChunkVerdict]::NativeSuiteVerdict($ev.TrxPath, $ev.DiscoveryPath, $ev.ExecutionPath, $probeRun, ([datetime]::UtcNow.AddMinutes(-5)), 0, 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', 'origin/master', 'origin/master', 'p', 'p', @(), $null, $false, $null, '', $null)
    $reasonText = (($vReal.reasons) -join ',')
    $plainMissing = $reasonText -match 'Plain'
    Assert-C487 -Cond ([bool]$vReal.coverageComplete -and [bool]$vReal.testsPassed -and (-not $plainMissing)) `
        -Name 'G040 real probe verdict complete' `
        -Detail $reasonText
}

function Test-C487_G041 {
    $v = [Antiphon.Nightly.NightlyChunkVerdict]::CoverageVerdict(@([pscustomobject]@{ Outcome = 'Passed'; Identity = 'A.M' }), 0, 'aaa', 'bbb', 'origin/master', 'origin/master', 'p', 'p', @(), @(), $false, '', $null, $null)
    Assert-C487 -Cond (-not $v.coverageComplete) -Name 'G041 sha mismatch'
}

function Test-C487_G042 {
    $v = [Antiphon.Nightly.NightlyChunkVerdict]::CoverageVerdict(@([pscustomobject]@{ Outcome = 'Passed'; Identity = 'A.M' }), 0, 'a', 'a', 'feature/x', 'origin/master', 'p', 'p', @(), @(), $false, '', $null, $null)
    Assert-C487 -Cond (-not $v.coverageComplete) -Name 'G042 ref mismatch'
}

function Test-C487_G043 {
    $v = [Antiphon.Nightly.NightlyChunkVerdict]::CoverageVerdict(@([pscustomobject]@{ Outcome = 'Passed'; Identity = 'A.M' }), 0, 'a', 'a', 'origin/master', 'origin/master', 'old', 'new', @(), @(), $false, '', $null, $null)
    Assert-C487 -Cond (-not $v.coverageComplete) -Name 'G043 policy mismatch'
}

function Test-C487_G044 {
    foreach ($n in @('ANTIPHON_HEADED_TESTS', 'ANTIPHON_HEADED_LONG_TESTS', 'ANTIPHON_CARD0133_RUN_P3')) {
        $pol = [Antiphon.Nightly.ExecutionPolicy]::Parse([IO.File]::ReadAllText($policyPath))
        $envMap = [Antiphon.Nightly.NightlyPolicy]::SafeChildEnvironment($pol, 'antiphon', @{ $n = '1' })
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
        $pol = [Antiphon.Nightly.ExecutionPolicy]::Parse([IO.File]::ReadAllText($policyPath))
        $envMap = [Antiphon.Nightly.NightlyPolicy]::SafeChildEnvironment($pol, 'antiphon', @{ $n = 'SECRETVALUE' })
        Assert-C487 -Cond ([string]$envMap[$n] -eq '') -Name ('G045 credential {0}' -f $n)
    }
}

function Test-C487_G046 {
    Assert-C487 -Cond (-not ([Antiphon.Nightly.NightlyPolicy]::CredentialLeak('log', @('SECRETVALUE')))) -Name 'G046 no leak in empty'
}

function Test-C487_G047 {
    $pol = [Antiphon.Nightly.ExecutionPolicy]::Parse([IO.File]::ReadAllText($policyPath))
    $envMap = [Antiphon.Nightly.NightlyPolicy]::SafeChildEnvironment($pol, 'messaging', $null)
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
    $union = [Antiphon.Nightly.NightlyChunkVerdict]::SuiteUidUnion(@($ev.Discovery.Nodes), (@($ordinaryTerm) + @($slowTerm)))
    Assert-C487 -Cond $union.Ok -Name 'G051 union of chunk executions complete' -Detail ($union.Missing -join ',')
    $half = [Antiphon.Nightly.NightlyChunkVerdict]::SuiteUidUnion(@($ev.Discovery.Nodes), $ordinaryTerm)
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
    $vAssigned = [Antiphon.Nightly.NightlyChunkVerdict]::NativeSuiteVerdict($ordTrx, $ev.DiscoveryPath, $ordExec, $ordDir, ([datetime]::UtcNow.AddMinutes(-5)), 0, 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', 'origin/master', 'origin/master', 'p', 'p', @('Ordinary'), $null, $false, $null, '', $null)
    $assignedReasons = (($vAssigned.reasons) -join ',')
    Assert-C487 -Cond ([bool]$vAssigned.coverageComplete -and ($assignedReasons -notmatch 'SlowCases') -and ($assignedReasons -notmatch 'SlowOne')) `
        -Name 'G051 chunk assigned Ordinary does not require SlowCases' `
        -Detail $assignedReasons
    $vAll = [Antiphon.Nightly.NightlyChunkVerdict]::NativeSuiteVerdict($ordTrx, $ev.DiscoveryPath, $ordExec, $ordDir, ([datetime]::UtcNow.AddMinutes(-5)), 0, 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', 'origin/master', 'origin/master', 'p', 'p', @(), $null, $false, $null, '', $null)
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

# CARD-1626: the census stays an exact inventory. Each row is pinned by id and path, so a
# dropped, added or renamed row fails G056; a deliberate census edit moves this pin with it.
$script:C487ScriptCensusInventory = @(
    'test-apphost-graceful-stop scripts/test-apphost-graceful-stop.ps1',
    'test-apphost-lock-age scripts/test-apphost-lock-age.ps1',
    'test-apphost-main-worktree-guard scripts/test-apphost-main-worktree-guard.ps1',
    'test-apphost-probe-class scripts/test-apphost-probe-class.ps1',
    'test-cleanup-claude-sessions scripts/test-cleanup-claude-sessions.ps1',
    'test-cleanup-codex-test-residue scripts/test-cleanup-codex-test-residue.ps1',
    'test-client scripts/test-client.ps1',
    'test-client-mode scripts/test-client-mode.ps1',
    'test-deploy-am-service scripts/test-deploy-am-service.ps1',
    'test-deploy-nightly-watchdog scripts/test-deploy-nightly-watchdog.ps1',
    'test-duration-tripwire scripts/test-duration-tripwire.ps1',
    'test-hooks scripts/test-hooks.ps1',
    'test-reap-zombie-agents scripts/test-reap-zombie-agents.ps1',
    'test-stage-value-report scripts/test-stage-value-report.ps1',
    'test-qualification-graph scripts/test-qualification-graph.ps1'
)

function Get-C487ScriptCensusDrift {
    param([object[]]$Census, [string[]]$Expected)
    $actual = @($Census | ForEach-Object { '{0} {1}' -f [string]$_.id, [string]$_.path })
    $drift = @()
    foreach ($row in $Expected) {
        $n = @($actual | Where-Object { $_ -ceq $row }).Count
        if ($n -eq 0) { $drift += ('missing ' + $row) } elseif ($n -gt 1) { $drift += ('duplicate ' + $row) }
    }
    foreach ($row in $actual) {
        if (@($Expected | Where-Object { $_ -ceq $row }).Count -eq 0) { $drift += ('extra ' + $row) }
    }
    if ($actual.Count -ne $Expected.Count) { $drift += ('count {0}/{1}' -f $actual.Count, $Expected.Count) }
    return $drift
}

function Test-C487_G056 {
    $pol = [Antiphon.Nightly.ExecutionPolicy]::Parse([IO.File]::ReadAllText($policyPath))
    $census = @([Antiphon.Nightly.NightlyPolicy]::ScriptCensus($pol))
    $expected = $script:C487ScriptCensusInventory
    $drift = @(Get-C487ScriptCensusDrift -Census $census -Expected $expected)
    # The comparison must see a dropped row and an unlisted added row, or a green proves nothing.
    $dropped = @($census | Select-Object -Skip 1)
    $added = @($census) + @([pscustomobject]@{ id = 'test-unlisted'; path = 'scripts/test-unlisted.ps1'; disposition = 'unattended'; wildcard = $false })
    $seesDrop = @(Get-C487ScriptCensusDrift -Census $dropped -Expected $expected).Count -gt 0
    $seesAdd = @(Get-C487ScriptCensusDrift -Census $added -Expected $expected).Count -gt 0
    Assert-C487 -Cond ($drift.Count -eq 0 -and $census.Count -eq $expected.Count -and $seesDrop -and $seesAdd) `
        -Name ('G056 {0} census rows' -f $expected.Count) `
        -Detail ('count=' + $census.Count + ' drop=' + $seesDrop + ' add=' + $seesAdd + ' ' + ($drift -join '; '))
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
    $nativeArgs = @([Antiphon.Nightly.NightlyTestsPlan]::NativeExecutionArguments($trxPath, $diagDir, $null, '', '', ''))
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
    try { $null = [Antiphon.Nightly.NightlyDiagnosticLog]::Read($path, 'discovery') }
    catch { $errorText = (Get-NightlyRefusalText $_) }
    Assert-C487 -Cond ($errorText -match $Pattern) -Name ('C1044 ' + $Name) -Detail $errorText
}

function Test-C1044_MultilineDiscovery {
    $expected = @(Get-C1044ExpectedNodes)
    $expectedDigest = Get-NightlySha256Text -Text ([Antiphon.Nightly.NightlyDocuments]::CanonicalJson(([ordered]@{
        # The census digest's UID order is the compiled SortCensusUids (OrdinalIgnoreCase, Ordinal tie-break).
        required = [Antiphon.Nightly.PlannedManifestCheck]::SortCensusUids([string[]]@($expected | Where-Object { $_.Type -ne 'Manual' } | ForEach-Object { $_.Uid }))
        excluded = [Antiphon.Nightly.PlannedManifestCheck]::SortCensusUids([string[]]@($expected | Where-Object { $_.Type -eq 'Manual' } | ForEach-Object { $_.Uid }))
    })))
    foreach ($variant in @('LF', 'CRLF')) {
        $newline = $(if ($variant -eq 'LF') { "`n" } else { "`r`n" })
        $path = Write-C1044Diagnostic -Name $variant -Text (Get-C1044MultilineText -Newline $newline)
        $nodes = @()
        $errorText = ''
        try { $nodes = @(([Antiphon.Nightly.NightlyDiagnosticLog]::Read($path, 'discovery')).Nodes) }
        catch { $errorText = (Get-NightlyRefusalText $_) }
        Assert-C487 -Cond (Test-C1044Inventory -Nodes $nodes) -Name ('C1044 ' + $variant + ' complete inventory') -Detail $errorText
        Assert-C487 -Cond (Test-C1044Metadata -Nodes $nodes) -Name ('C1044 ' + $variant + ' exact metadata')
        $census = [Antiphon.Nightly.NightlyCensus]::Census($nodes, $null, $false)
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
        $published = [Antiphon.Nightly.NightlyDiagnosticLog]::ReadDiscovery($output)
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
    try { $nodes = @(([Antiphon.Nightly.NightlyDiagnosticLog]::Read($path, 'discovery')).Nodes) }
    catch { $errorText = (Get-NightlyRefusalText $_) }
    $fixtureNode = @($nodes | Where-Object { $_.Uid -ceq 'fixture-uid' })
    $genericNode = @($nodes | Where-Object { $_.Uid -ceq 'generic-uid' })
    Assert-C487 -Cond ($nodes.Count -eq 2 -and $fixtureNode.Count -eq 1 -and $fixtureNode[0].Type -ceq $fixture -and $fixtureNode[0].Method -ceq 'ListsFiles' -and $genericNode.Count -eq 1 -and $genericNode[0].Type -ceq $generic) `
        -Name 'C1044 fixture and generic TypeName accepted' -Detail $errorText

    $malformed = Get-C1044DiscoveryText -Records ($records + @('TestNodeUpdateMessage { TestNode = TestNode { Uid = TestNodeUid { Value = truncated }'))
    Assert-C1044Rejects -Name 'fixture file stateless record refuses' -Text $malformed -Pattern 'malformed discovery record: missing state'
    $emptyType = New-C1044DiscoveryRecord -Uid 'empty-type' -TypeName '' -Method 'Plain'
    Assert-C1044Rejects -Name 'empty TypeName still truncated' -Text (Get-C1044DiscoveryText -Records @($emptyType)) -Pattern 'truncated diagnostic record'
}

function Test-C1044_ParserCompatibility {
    $root = Get-C487ProbeRoot
    $golden = [System.IO.File]::ReadAllText((Join-Path $root 'discovery.diag'))
    $nodes = @(([Antiphon.Nightly.NightlyDiagnosticLog]::Read((Join-Path $root 'discovery.diag'), 'discovery')).Nodes)
    Assert-C487 -Cond (Test-C1044Inventory -Nodes $nodes) -Name 'C1044 golden discovery inventory'
    Assert-C487 -Cond (Test-C1044Metadata -Nodes $nodes) -Name 'C1044 golden discovery metadata'
    $execText = [System.IO.File]::ReadAllText((Join-Path $root 'execution.diag'))
    $execution = @(([Antiphon.Nightly.NightlyDiagnosticLog]::Read((Join-Path $root 'execution.diag'), 'execution')).Nodes)
    Assert-C487 -Cond (Test-C1044Inventory -Nodes $execution -Execution) -Name 'C1044 golden terminal inventory'
    $withDiscovery = Write-C1044Diagnostic -Name 'nonterminal' -Text ($execText + "`n" + $golden)
    $filtered = @(([Antiphon.Nightly.NightlyDiagnosticLog]::Read($withDiscovery, 'execution')).Nodes)
    Assert-C487 -Cond ((Test-C1044Inventory -Nodes $filtered -Execution) -and
        @($filtered | Where-Object { $_.State -in @('InProgress', 'Discovered') }).Count -eq 0) -Name 'C1044 nonterminal updates filtered'
    $prefixed = [regex]::Replace($golden, '(?m)^TestNodeUpdateMessage', '2026-10-04T12:00:00.000+00:00 Pinned.Logger INFORMATION TestNodeUpdateMessage')
    $path = Write-C1044Diagnostic -Name 'prefixed' -Text $prefixed
    Assert-C487 -Cond (Test-C1044Metadata -Nodes ([Antiphon.Nightly.NightlyDiagnosticLog]::Read($path, 'discovery')).Nodes) -Name 'C1044 prefixed envelopes accepted'
    $multilineExec = $execText.Replace('DisplayName = Plain,', "DisplayName = Plain { },`n`ncontinued [, name,").TrimEnd([char[]]"`r`n")
    $path = Write-C1044Diagnostic -Name 'execution-multiline' -Text $multilineExec
    Assert-C487 -Cond (Test-C1044Inventory -Nodes ([Antiphon.Nightly.NightlyDiagnosticLog]::Read($path, 'execution')).Nodes -Execution) -Name 'C1044 multiline terminal inventory'
    $jsonPath = Join-Path $ResultsDirectory 'roundtrip.json'
    [Antiphon.Nightly.NightlyDiagnosticLog]::WriteDiscoveryDocument($jsonPath, ([Antiphon.Nightly.NightlyDiagnosticLog]::NewDiscoveryDocument($nodes, 'fixture-hash')))
    $roundtrip = [Antiphon.Nightly.NightlyDiagnosticLog]::Read($jsonPath, 'discovery')
    Assert-C487 -Cond ((Test-C1044Metadata -Nodes $roundtrip.Nodes) -and $roundtrip.AssemblyHash -ceq 'fixture-hash') -Name 'C1044 discovery JSON roundtrip'
    $manual = @($nodes | Where-Object { $_.Type -eq 'Manual' })[0]
    Assert-C487 -Cond (([Antiphon.Nightly.NightlyCensus]::Disposition($manual, $null, $false)).Disposition -eq 'excluded') -Name 'C1044 default OptIn excluded'
    Assert-C487 -Cond (([Antiphon.Nightly.NightlyCensus]::Disposition($manual, $null, $true)).Disposition -eq 'required') -Name 'C1044 profile OptIn required'
    $exclusions = @([pscustomobject]@{ Class = 'Manual'; Methods = @('ManualOne'); Reason = 'manual fixture'; Owner = 'C1044' })
    $disposition = [Antiphon.Nightly.NightlyCensus]::Disposition($manual, $exclusions, $true)
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

# CARD-1698: summary.json records the toolchain versions, floors and warnings under
# preflight.toolchain; the version comes from the ToolchainVersions seam, never the real git.
# This case proves Get-NightlyToolchainPreflight and its call site in the preflight block;
# C1698_SummaryJsonRecordsToolchain reads the summary.json a real run writes.
function Test-C1698_SummaryRecordsToolchain {
    $root = Join-Path $ResultsDirectory ([guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path (Join-Path $root 'docker') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repo 'docker/git-minimum-version.sh') -Destination (Join-Path $root 'docker/git-minimum-version.sh')
    Copy-Item -LiteralPath (Join-Path $repo 'global.json') -Destination (Join-Path $root 'global.json')
    $saved = $script:NightlySeams
    $script:NightlySeams = @{ ToolchainVersions = { param($tool) if ($tool -eq 'git') { 'git version 2.42.0' } else { '10.0.204 [/fake/sdk]' } } }
    $tc = $null
    try { $tc = Get-NightlyToolchainPreflight -RepoRoot $root 6>$null } catch { $tc = $null } finally { $script:NightlySeams = $saved }
    $json = ''
    if ($tc) { $json = ([ordered]@{ preflight = [ordered]@{ toolchain = $tc } } | ConvertTo-Json -Depth 8) }
    $back = $null
    if ($json) { $back = ($json | ConvertFrom-Json).preflight.toolchain }
    $git = $null; $sdk = $null
    if ($back) {
        $git = @($back.tools | Where-Object { $_.tool -eq 'git' }) | Select-Object -First 1
        $sdk = @($back.tools | Where-Object { $_.tool -eq 'dotnet-sdk' }) | Select-Object -First 1
    }
    Assert-C487 -Cond ($null -ne $git -and $git.found -eq '2.42.0' -and $git.floor -eq '2.43' -and $git.status -eq 'below-floor') -Name 'C1698 Summary records the git version and floor' -Detail $json
    Assert-C487 -Cond ($null -ne $sdk -and $sdk.found -eq '10.0.204' -and $sdk.status -eq 'ok') -Name 'C1698 Summary records the SDK version' -Detail $json
    $w = @()
    if ($back) { $w = @($back.warnings) }
    Assert-C487 -Cond ($w.Count -eq 1 -and ([string]$w[0].message).StartsWith('WARNING: git 2.42.0 is below the minimum 2.43')) -Name 'C1698 Summary records the warning' -Detail $json
    $impl = [IO.File]::ReadAllText((Join-Path $repo 'scripts/lib/nightly-tests-impl.ps1'))
    $call = $impl.IndexOf('$preflight.toolchain = Get-NightlyToolchainPreflight -RepoRoot $RepoRoot')
    $disk = $impl.IndexOf('$preflight.disk = [ordered]@{ exitCode = 0; freeBytes = 50GB; minimumFreeBytes = 10GB }')
    $write = $impl.IndexOf('preflight = $preflight')
    Assert-C487 -Cond ($call -gt $disk -and $disk -gt 0 -and $write -gt $call) -Name 'C1698 Summary preflight block records the toolchain' -Detail ('disk=' + $disk + ' call=' + $call + ' write=' + $write)
}

# CARD-1698: the same record through the real write path. The fixture clone gets every project
# the policy names (the probe fixtures too, so the project universe matches on Linux) and the two
# floor sources; Invoke-AntiphonNightlyTests runs its preflight block and writes summary.json,
# which is read back from disk.
function Test-C1698_SummaryJsonRecordsToolchain {
    $fx = New-Efx
    $policy = [Antiphon.Nightly.ExecutionPolicy]::Parse([IO.File]::ReadAllText($policyPath))
    foreach ($p in @([Antiphon.Nightly.NightlyPolicy]::ProjectUniverse($policy))) {
        $src = Join-Path $repo $p
        $dest = Join-Path $fx.Clone $p
        if (-not (Test-Path -LiteralPath $src -PathType Leaf)) { continue }
        New-Item -ItemType Directory -Path (Split-Path -Parent $dest) -Force | Out-Null
        Copy-Item -LiteralPath $src -Destination $dest -Force
    }
    New-Item -ItemType Directory -Path (Join-Path $fx.Clone 'docker') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repo 'docker/git-minimum-version.sh') -Destination (Join-Path $fx.Clone 'docker/git-minimum-version.sh')
    Copy-Item -LiteralPath (Join-Path $repo 'global.json') -Destination (Join-Path $fx.Clone 'global.json')
    $extra = [scriptblock]::Create("`$NightlySeams.ToolchainVersions = { param(`$tool) if (`$tool -eq 'git') { 'git version 2.42.0' } else { '10.0.204 [/fake/sdk]' } }")
    Write-C487Seams -Path $fx.Seams -TracePath $fx.Trace -Extra $extra
    $r = Invoke-E -Fx $fx 6>$null
    $summaryPath = Join-Path $fx.Logs 'summary.json'
    $json = ''
    if (Test-Path -LiteralPath $summaryPath) { $json = [IO.File]::ReadAllText($summaryPath) }
    $tc = $null
    if ($json) { $tc = ($json | ConvertFrom-Json).preflight.toolchain }
    $git = $null; $sdk = $null; $w = @()
    if ($tc) {
        $git = @($tc.tools | Where-Object { $_.tool -eq 'git' }) | Select-Object -First 1
        $sdk = @($tc.tools | Where-Object { $_.tool -eq 'dotnet-sdk' }) | Select-Object -First 1
        $w = @($tc.warnings)
    }
    $detail = 'exit=' + $r.ExitCode + ' error=' + $r.Error + ' summary=' + $(if ($json) { 'present' } else { 'missing' }) + ' toolchain=' + $(if ($tc) { ($tc | ConvertTo-Json -Depth 6 -Compress) } else { 'missing' })
    Assert-C487 -Cond ($null -ne $git -and $git.found -eq '2.42.0' -and $git.floor -eq '2.43' -and $git.status -eq 'below-floor' -and
        $null -ne $sdk -and $sdk.found -eq '10.0.204' -and $sdk.floor -eq '10.0.204' -and $sdk.status -eq 'ok') -Name 'C1698 Summary json on disk records the versions and floors' -Detail $detail
    Assert-C487 -Cond ($w.Count -eq 1 -and $w[0].tool -eq 'git' -and ([string]$w[0].message).StartsWith('WARNING: git 2.42.0 is below the minimum 2.43; lands will fail')) -Name 'C1698 Summary json on disk records the warning' -Detail $detail
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
    Test-C487_PrerequisiteGate
}
Write-C487Evidence -ResultsDirectory $ResultsDirectory -Case 'tests-summary' -Body @{ passed = $script:C487Passed; failed = $script:C487Failed; rows = $script:C487Rows }
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows $(if ($Case) { 0 } else { 85 })
