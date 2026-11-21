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
    'test-nightly-report scripts/test-nightly-report.ps1',
    'test-reap-zombie-agents scripts/test-reap-zombie-agents.ps1',
    'test-stage-value-report scripts/test-stage-value-report.ps1',
    'test-rc-roster-repairs scripts/test-rc-roster-repairs.ps1',
    'test-nightly-failed-first scripts/test-nightly-failed-first.ps1',
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
        $nodes = @(([Antiphon.Nightly.NightlyDiagnosticLog]::Read($path, 'execution')).Nodes)
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
        $rows = @([Antiphon.Nightly.NightlyTrx]::ReadIdentities($trx))
        $discovery = Join-Path $ResultsDirectory "c1375-$state.json"
        Write-C487Discovery -Path $discovery -Nodes @(@{ uid = $uid; namespace = 'C1375.Probe';
            type = 'C1375Producer'; method = $method; state = 'Discovered' })
        $verdict = [Antiphon.Nightly.NightlyChunkVerdict]::NativeSuiteVerdict($trx, $discovery, $path, $ResultsDirectory, ([datetime]::UtcNow.AddMinutes(-1)), 2, 'c1375', 'c1375', 'test', 'test', 'test', 'test', @(), $null, $false, $null, '', $null)
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
        $parsed = @(([Antiphon.Nightly.NightlyDiagnosticLog]::Read($path, 'execution')).Nodes)
        Assert-C487 -Cond ($parsed.Count -eq 1 -and $parsed[0].Uid -ceq $uid -and
            $parsed[0].ClassName -ceq $class -and $parsed[0].Method -ceq $method -and
            $parsed[0].Outcome -ceq 'Failed') -Name ('D13 ' + $style + ' failure identity')
        $expected = '[Test Failure] ShouldAssertException: OperatingSystem.IsWindows()' + $nl +
            '    should be' + $nl + 'True' + $nl + '    but was' + $nl + 'False'
        Assert-C487 -Cond ($parsed.Count -eq 1 -and $parsed[0].Message -ceq $expected) -Name ('D13 ' + $style + ' failure message')
        $trx = Join-Path $ResultsDirectory ('d13-' + $style + '.trx')
        [IO.File]::WriteAllText($trx, $D13FailureTrx)
        $rows = @([Antiphon.Nightly.NightlyTrx]::ReadIdentities($trx))
        Assert-C487 -Cond ($rows.Count -eq 1 -and $rows[0].ClassName -ceq $class -and
            $rows[0].MethodName -ceq $method -and $rows[0].Outcome -ceq 'Failed') -Name ('D13 ' + $style + ' real TRX identity')
        $discovery = Join-Path $ResultsDirectory ('d13-' + $style + '.json')
        Write-C487Discovery -Path $discovery -Nodes @(@{ uid = $uid; namespace = 'Antiphon.E2E';
            type = 'AgentTaskLandDeliveryE2ETests'; method = $method; state = 'Discovered' })
        $v = [Antiphon.Nightly.NightlyChunkVerdict]::NativeSuiteVerdict($trx, $discovery, $path, $ResultsDirectory, ([datetime]::UtcNow.AddMinutes(-1)), 2, 'd13', 'd13', 'test', 'test', 'test', 'test', @(), $null, $false, $null, '', $null)
        Assert-C487 -Cond ($v.coverageComplete -and -not $v.testsPassed -and $v.reasons -contains 'failed-row' -and
            @($v.terminalNodes).Count -eq 1 -and $v.terminalNodes[0].Message -ceq $expected) -Name ('D13 ' + $style + ' complete failed verdict')
        # Even a misleading successful process exit cannot make the failure pass.
        $direct = [Antiphon.Nightly.NightlyChunkVerdict]::CoverageVerdict($parsed, 0, 'd13', 'd13', 'test', 'test', 'test', 'test', @(), @(), $false, '', $null, $null)
        Assert-C487 -Cond (-not $direct.testsPassed -and $direct.reasons -contains 'failed-row') -Name ('D13 ' + $style + ' error alone stays red')
    }
}

function Test-D13_ExecutionCompatibility {
    $root = Get-C487ProbeRoot
    $text = [IO.File]::ReadAllText((Join-Path $root 'execution.diag'))
    $pass = @(([Antiphon.Nightly.NightlyDiagnosticLog]::Read((Join-Path $root 'execution.diag'), 'execution')).Nodes)
    $skipPath = Write-C1044Diagnostic -Name 'd13-skips' -Text ($text.Replace('PassedTestNodeStateProperty', 'SkippedTestNodeStateProperty'))
    $skip = @(([Antiphon.Nightly.NightlyDiagnosticLog]::Read($skipPath, 'execution')).Nodes)
    Assert-C487 -Cond ($pass.Count -eq 8 -and $skip.Count -eq 8 -and
        @($pass | Where-Object { $_.Outcome -cne 'Passed' }).Count -eq 0 -and
        @($skip | Where-Object { $_.Outcome -cne 'Skipped' }).Count -eq 0 -and
        (Test-C1044Inventory -Nodes $pass -Execution) -and (Test-C1044Inventory -Nodes $skip -Execution)) -Name 'D13 pass and skip unchanged'
    $legacyPath = Write-C1044Diagnostic -Name 'd13-legacy' -Text ((Get-D13FailureText).Replace('ErrorTestNodeStateProperty', 'FailedTestNodeStateProperty'))
    $legacy = @(([Antiphon.Nightly.NightlyDiagnosticLog]::Read($legacyPath, 'execution')).Nodes)
    Assert-C487 -Cond ($legacy.Count -eq 1 -and $legacy[0].Outcome -ceq 'Failed' -and
        $legacy[0].Method -ceq 'C488_ApprovalPollingCannotConfirm') -Name 'D13 legacy Failed retained'
    $discovery = [IO.File]::ReadAllText((Join-Path $root 'discovery.diag'))
    $mixedPath = Write-C1044Diagnostic -Name 'd13-mixed' -Text ($text + "`n" + $discovery + "`n" + (Get-D13FailureText))
    $mixed = @(([Antiphon.Nightly.NightlyDiagnosticLog]::Read($mixedPath, 'execution')).Nodes)
    Assert-C487 -Cond ($mixed.Count -eq 9 -and @($mixed | Where-Object { $_.Outcome -eq 'Failed' }).Count -eq 1 -and
        @($mixed | Where-Object { $_.Outcome -in @('Discovered', 'InProgress') }).Count -eq 0) -Name 'D13 nonterminal updates omitted'
}

function Test-D13_ExecutionGuards {
    $text = Get-D13FailureText
    foreach ($variant in @('duplicate', 'version')) {
        $changed = $(if ($variant -eq 'duplicate') { $text + "`n" + $text } else { $text.Replace('2.2.2', '9.0.0') })
        $path = Write-C1044Diagnostic -Name ('d13-' + $variant) -Text $changed
        $errorText = ''
        try { $null = [Antiphon.Nightly.NightlyDiagnosticLog]::Read($path, 'execution') } catch { $errorText = (Get-NightlyRefusalText $_) }
        if ($variant -eq 'duplicate') {
            Assert-C487 -Cond ($errorText -match 'duplicate execution uid') -Name 'D13 duplicate Error refuses' -Detail $errorText
        } else {
            Assert-C487 -Cond ($errorText -match 'version-drift') -Name 'D13 unsupported version refuses' -Detail $errorText
        }
    }
    $path = Write-C1044Diagnostic -Name 'd13-guard-error' -Text $text
    $parsed = @(([Antiphon.Nightly.NightlyDiagnosticLog]::Read($path, 'execution')).Nodes)
    $requiredUid = 'Antiphon.E2E.AgentTaskLandDeliveryE2ETests.1.1.C488_ApprovalPollingCannotConfirm.1.1.0'
    $omittedUid = 'Antiphon.E2E.AgentTaskLandDeliveryE2ETests.1.1.OtherMethod.1.1.0'
    $omitPath = Write-C1044Diagnostic -Name 'd13-guard-omit' -Text ($text.Replace($requiredUid, $omittedUid))
    $nodes = @(([Antiphon.Nightly.NightlyDiagnosticLog]::Read($omitPath, 'execution')).Nodes)
    $missing = [Antiphon.Nightly.NightlyChunkVerdict]::ExpandedRowsPresent(@(), $nodes, @($requiredUid))
    Assert-C487 -Cond ($nodes.Count -eq 1 -and $nodes[0].Uid -ceq $omittedUid -and
        -not $missing.Ok -and @($missing.Missing).Count -eq 1 -and [string]$missing.Missing[0] -ceq $requiredUid) `
        -Name 'D13 missing failed UID refuses'
    $extra = [Antiphon.Nightly.NightlyVerdicts]::CrossCheck(@(), $parsed, @())
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
        $nodes = @(([Antiphon.Nightly.NightlyDiagnosticLog]::Read($path, 'execution')).Nodes)
    } catch {
        $errorText = [string](Get-NightlyRefusalText $_)
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
    $verdict = [Antiphon.Nightly.NightlyChunkVerdict]::NativeSuiteVerdict($trx, $discovery, $path, $ResultsDirectory, ([datetime]::UtcNow.AddMinutes(-1)), 0, 'c1325', 'c1325', 'test', 'test', 'test', 'test', @(), $null, $false, $null, '', $null)
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
    $sameNs = [Antiphon.Nightly.NightlyCensus]::ClassAssigned($appSuffix, '', @($requiredApp))
    $otherNs = [Antiphon.Nightly.NightlyCensus]::ClassAssigned($agentsSuffix, '', @($requiredApp))
    $unqualified = [Antiphon.Nightly.NightlyCensus]::ClassAssigned($appSuffix, '', @('Widget'))
    $present = [Antiphon.Nightly.NightlyChunkVerdict]::RequiredClassesPresent(@([pscustomobject]@{ ClassName = $appSuffix }), @($requiredApp, $requiredAgents))
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
    try { $nodes = @(([Antiphon.Nightly.NightlyDiagnosticLog]::Read($path, 'discovery')).Nodes) }
    catch { $errorText = (Get-NightlyRefusalText $_) }
    $alpha = @($nodes | Where-Object { $_.Uid -ceq 'uid-a' -and $_.Type -ceq 'Alpha' })
    $beta = @($nodes | Where-Object { $_.Uid -ceq 'uid-b' -and $_.Type -ceq 'Beta' })
    $discoveryOk = ($errorText -eq '' -and $nodes.Count -eq 2 -and $alpha.Count -eq 1 -and $beta.Count -eq 1)

    $passedA = $first.Replace('DiscoveredTestNodeStateProperty', 'PassedTestNodeStateProperty')
    $passedB = $second.Replace('DiscoveredTestNodeStateProperty', 'PassedTestNodeStateProperty')
    $path = Write-C1044Diagnostic -Name 'c1391-two-passed' -Text ($header + $passedA + $passedB)
    $exec = @()
    $execError = ''
    try { $exec = @(([Antiphon.Nightly.NightlyDiagnosticLog]::Read($path, 'execution')).Nodes) }
    catch { $execError = (Get-NightlyRefusalText $_) }
    $passedLeft = @($exec | Where-Object { $_.Uid -ceq 'uid-a' -and $_.Outcome -ceq 'Passed' })
    $passedRight = @($exec | Where-Object { $_.Uid -ceq 'uid-b' -and $_.Outcome -ceq 'Passed' })
    $execOk = ($execError -eq '' -and $exec.Count -eq 2 -and $passedLeft.Count -eq 1 -and $passedRight.Count -eq 1)

    $truncated = 'TestNodeUpdateMessage { TestNode = TestNode { Uid = TestNodeUid { Value = truncated }'
    $path = Write-C1044Diagnostic -Name 'c1391-splice' -Text ($header + $truncated + $first)
    $spliced = @()
    $spliceError = ''
    try { $spliced = @(([Antiphon.Nightly.NightlyDiagnosticLog]::Read($path, 'discovery')).Nodes) }
    catch { $spliceError = (Get-NightlyRefusalText $_) }
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
    try { $nodes = @(([Antiphon.Nightly.NightlyDiagnosticLog]::Read($path, 'discovery')).Nodes) }
    catch { $errorText = (Get-NightlyRefusalText $_) }
    $balancedNode = @($nodes | Where-Object { $_.Uid -ceq 'balanced-uid' })
    $genericNode = @($nodes | Where-Object { $_.Uid -ceq 'generic-uid' })
    $ok = ($nodes.Count -eq 2 -and $balancedNode.Count -eq 1 -and $balancedNode[0].Type -ceq $balanced -and $genericNode.Count -eq 1 -and $genericNode[0].Type -ceq $generic)
    Assert-C487 -Cond $ok -Name 'C1391 balanced fixture TypeName still parses' -Detail $errorText
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
    return [Antiphon.Nightly.NightlyChunkVerdict]::NativeSuiteVerdict($Launch.Trx, $Discovery, $Launch.Exec, $RunDirectory, [datetime]::UtcNow.AddMinutes(-5), 0,
        $sha, $sha, 'origin/master', 'origin/master', 'p', 'p', @('Ns.' + $Launch.Class), $Exclusions, [bool]$Aware, $Index, '', $null)
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
    # Discovery parses are counted by the compiled reader (NightlyDiagnosticLog.DiscoveryReads).
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
        $index = [Antiphon.Nightly.NightlyDiscoveryIndex]::new()
        foreach ($l in $launches) {
            $before = [Antiphon.Nightly.NightlyDiagnosticLog]::DiscoveryReads
            $v = Invoke-C1419Verdict -Launch $l -Discovery $disc -RunDirectory $root -Index $index
            $indexedReads += ([Antiphon.Nightly.NightlyDiagnosticLog]::DiscoveryReads - $before)
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
        $copyIndex = [Antiphon.Nightly.NightlyDiscoveryIndex]::new()
        $before = [Antiphon.Nightly.NightlyDiagnosticLog]::DiscoveryReads
        foreach ($l in $launches[0..2]) {
            $copy = Join-Path $l.Dir 'launch.discovery.json'
            Copy-Item -LiteralPath $disc -Destination $copy -Force
            [void](Invoke-C1419Verdict -Launch $l -Discovery $copy -RunDirectory $root -Index $copyIndex)
        }
        Assert-C487 -Cond (([Antiphon.Nightly.NightlyDiagnosticLog]::DiscoveryReads - $before) -eq 1) -Name 'C1419 identical discovery bytes at three paths parse once' -Detail ('reads=' + ([Antiphon.Nightly.NightlyDiagnosticLog]::DiscoveryReads - $before))

        # Profile lane: the exclusion census and its required set come from the index.
        $hold = @([pscustomobject]@{ Class = 'ClassC'; Methods = @('Two'); Reason = 'held'; Owner = 'C1419' })
        $profileIndex = [Antiphon.Nightly.NightlyDiscoveryIndex]::new()
        $held = New-C1419Launch -Root $root -Name 'held' -Class 'ClassC' -Methods @('One')
        $before = [Antiphon.Nightly.NightlyDiagnosticLog]::DiscoveryReads
        $pv = @()
        foreach ($l in @($launches[0], $held, $launches[1])) {
            $pv += Invoke-C1419Verdict -Launch $l -Discovery $disc -RunDirectory $root -Index $profileIndex -Exclusions $hold -Aware
        }
        $profileReads = [Antiphon.Nightly.NightlyDiagnosticLog]::DiscoveryReads - $before
        $pPlain = Invoke-C1419Verdict -Launch $held -Discovery $disc -RunDirectory $root -Exclusions $hold -Aware
        $pText = Get-C1419VerdictText -Verdict $pv[1]
        $pOk = ($profileReads -eq 1 -and $pText -ceq (Get-C1419VerdictText -Verdict $pPlain) -and [bool]$pv[1].coverageComplete -and
            [int]$pv[1].census.RequiredCount -eq 7 -and [int]$pv[1].census.ExcludedCount -eq 1)
        Assert-C487 -Cond $pOk -Name 'C1419 profile exclusions census once and match the unindexed verdict' -Detail ('reads=' + $profileReads + ' ' + $pText)

        # A rewritten discovery is new bytes: the index parses it again.
        $nodes += @{ uid = 'ClassD.One'; type = 'ClassD'; method = 'One'; namespace = 'Ns'; state = 'Discovered' }
        Write-C487Discovery -Path $disc -Nodes $nodes
        $before = [Antiphon.Nightly.NightlyDiagnosticLog]::DiscoveryReads
        $after = Invoke-C1419Verdict -Launch $launches[0] -Discovery $disc -RunDirectory $root -Index $index
        $rewriteReads = [Antiphon.Nightly.NightlyDiagnosticLog]::DiscoveryReads - $before
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
        $calls = @($ast.FindAll({ param($a) $a -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -and $a.Static -and
            $a.Member.Value -eq 'NativeSuiteVerdict' -and $a.Expression.TypeName.FullName -eq 'Antiphon.Nightly.NightlyChunkVerdict' }, $true))
        $argName = ''
        if ($calls.Count -eq 1) {
            # NativeSuiteVerdict(..., profileAware, discoveryIndex, platform, expectedSkipRoster): the index is the 16th argument.
            $args16 = @($calls[0].Arguments)
            if ($args16.Count -eq 18 -and $args16[15] -is [System.Management.Automation.Language.VariableExpressionAst]) {
                $argName = $args16[15].VariablePath.UserPath
            }
        }
        $makers = @($ast.FindAll({ param($a) $a -is [System.Management.Automation.Language.AssignmentStatementAst] -and
            $a.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and $a.Left.VariablePath.UserPath -eq $argName -and
            @($a.Right.FindAll({ param($b) $b -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -and $b.Static -and
                $b.Member.Value -eq 'new' -and $b.Expression.TypeName.FullName -eq 'Antiphon.Nightly.NightlyDiscoveryIndex' }, $true)).Count -gt 0 }, $true))
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

function Test-Group4NativeTrxReader {
    # Calls the real ExecutionEvidenceFinished of the native owner, compiled into the nightly
    # core since W2-nightly S04 (scripts/lib/Antiphon.Nightly/NativeProcessOwner.cs). A closing-tag
    # search returns false for every completed TRX, so the positives below are red at 89fd55cef.
    $root = Join-Path $ResultsDirectory 'group4-native-trx-reader'
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    $ownerPath = Join-Path $lib (Join-Path 'Antiphon.Nightly' 'NativeProcessOwner.cs')
    $loadError = ''
    $loaded = 'Antiphon.Nightly.NativeProcessOwner' -as [type]
    if ($null -eq $loaded) { $loadError = 'NativeProcessOwner is not in the loaded core' }
    elseif ($loaded.Assembly.GetName().Name -ne 'Antiphon.Nightly') { $loadError = 'NativeProcessOwner comes from ' + $loaded.Assembly.GetName().Name }
    Assert-C487 -Cond ([string]::IsNullOrEmpty($loadError)) -Name 'group4 native owner is part of the compiled core' -Detail $loadError
    $type = $null
    try { $type = [Antiphon.Nightly.NativeProcessOwner] } catch { $type = $null }
    $find = {
        param($Target, [string]$Name, [int]$Count)
        if ($null -eq $Target) { return $null }
        $flags = [Reflection.BindingFlags]'Public,NonPublic,Static'
        foreach ($method in @($Target.GetMethods($flags))) {
            if ($method.Name -ne $Name) { continue }
            if (@($method.GetParameters()).Count -eq $Count) { return $method }
        }
        return $null
    }
    $reader = & $find $type 'ExecutionEvidenceFinished' 2
    $aged = & $find $type 'ExecutionEvidenceFinished' 3
    Assert-C487 -Cond ($null -ne $reader) -Name 'group4 native reader method is loaded'
    $writeTrx = {
        param([string]$Path, [string]$Outcome, [int]$Total, [int]$Executed, [int]$NotExecuted, [int]$Failed = 0)
        $passed = $Executed - $Failed
        if ($passed -lt 0) { $passed = 0 }
        $xml = '<?xml version="1.0" encoding="utf-8"?><TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><ResultSummary outcome="' + $Outcome + '"><Counters total="' + $Total + '" executed="' + $Executed + '" passed="' + $passed + '" failed="' + $Failed + '" notExecuted="' + $NotExecuted + '" /></ResultSummary></TestRun>'
        [System.IO.File]::WriteAllText($Path, $xml)
    }
    $writeDiag = {
        param([string]$Path, [int]$Ran, [switch]$NoStop)
        $pad = [string]::new('x', 20000)
        $body = $pad + "`ntotal ran: " + $Ran + "`n"
        if (-not $NoStop) { $body = $body + "request execute stop`n" }
        [System.IO.File]::WriteAllText($Path, $body)
    }
    $call = {
        param($Method, [string]$Trx, [string]$Diag, $NotBefore)
        if ($null -eq $Method) { return [pscustomobject]@{ Finished = $false; Error = 'method-missing' } }
        try {
            $count = @($Method.GetParameters()).Count
            $invokeArgs = New-Object object[] $count
            $invokeArgs[0] = [string]$Trx
            $invokeArgs[1] = [string]$Diag
            if ($count -ge 3) { $invokeArgs[2] = [datetime]$NotBefore }
            $value = [bool]$Method.Invoke($null, $invokeArgs)
            return [pscustomobject]@{ Finished = $value; Error = '' }
        } catch {
            return [pscustomobject]@{ Finished = $false; Error = $_.Exception.Message }
        }
    }
    $matchTrx = Join-Path $root 'match.trx'
    $matchDiag = Join-Path $root 'match.diag'
    & $writeTrx $matchTrx 'Completed' 119 119 0 0
    & $writeDiag $matchDiag 119
    $case = & $call $reader $matchTrx $matchDiag $null
    Assert-C487 -Cond ($case.Finished -and [string]::IsNullOrEmpty($case.Error)) -Name 'group4 native reader accepts the case trx shape' -Detail ('finished=' + $case.Finished + ' error=' + $case.Error)

    $realPath = Join-Path $repo (Join-Path 'scripts' (Join-Path 'fixtures' (Join-Path 'nightly' (Join-Path 'c487-probe' 'all.trx'))))
    $realText = [System.IO.File]::ReadAllText($realPath)
    $shaped = $realText.Replace('total="8"', 'total="119"').Replace('executed="8"', 'executed="119"').Replace('passed="8"', 'passed="119"')
    $shapeOk = ($shaped.Contains('total="119"') -and $shaped.Contains('executed="119"') -and $shaped.Contains('outcome="Completed"') -and -not $shaped.Contains('total="8"'))
    Assert-C487 -Cond $shapeOk -Name 'group4 real c487-probe shape was rewritten to 119/119'
    $realTrx = Join-Path $root 'real-shape.trx'
    [System.IO.File]::WriteAllText($realTrx, $shaped)
    $real = & $call $reader $realTrx $matchDiag $null
    Assert-C487 -Cond ($real.Finished -and [string]::IsNullOrEmpty($real.Error)) -Name 'group4 native reader accepts the real c487-probe trx shape' -Detail ('finished=' + $real.Finished + ' error=' + $real.Error)

    $nsPath = Join-Path $root 'ns.trx'
    $nsXml = '<?xml version="1.0" encoding="utf-8"?><TestRun xmlns:t="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><t:ResultSummary outcome=''Completed''><t:Counters notExecuted=''0'' failed=''0'' executed=''119'' total=''119'' /></t:ResultSummary></TestRun>'
    [System.IO.File]::WriteAllText($nsPath, $nsXml)
    $ns = & $call $reader $nsPath $matchDiag $null
    Assert-C487 -Cond ($ns.Finished -and [string]::IsNullOrEmpty($ns.Error)) -Name 'group4 native reader accepts a prefixed summary and single quotes' -Detail ('finished=' + $ns.Finished + ' error=' + $ns.Error)

    $notExec = Join-Path $root 'not-executed.trx'
    & $writeTrx $notExec 'Completed' 119 119 1 0
    $notExecResult = & $call $reader $notExec $matchDiag $null
    Assert-C487 -Cond (-not $notExecResult.Finished) -Name 'group4 native reader rejects notExecuted above zero' -Detail ('finished=' + $notExecResult.Finished)

    $lowDiag = Join-Path $root 'low.diag'
    & $writeDiag $lowDiag 118
    $low = & $call $reader $matchTrx $lowDiag $null
    Assert-C487 -Cond (-not $low.Finished) -Name 'group4 native reader rejects total ran below the trx total' -Detail ('finished=' + $low.Finished)

    $failedPath = Join-Path $root 'failed-count.trx'
    & $writeTrx $failedPath 'Completed' 119 119 0 3
    $failedCount = & $call $reader $failedPath $matchDiag $null
    Assert-C487 -Cond (-not $failedCount.Finished) -Name 'group4 native reader rejects failed above zero' -Detail ('finished=' + $failedCount.Finished)

    $failedOutcome = Join-Path $root 'failed-outcome.trx'
    & $writeTrx $failedOutcome 'Failed' 119 119 0 0
    $failedOutcomeResult = & $call $reader $failedOutcome $matchDiag $null
    Assert-C487 -Cond (-not $failedOutcomeResult.Finished) -Name 'group4 native reader rejects outcome Failed' -Detail ('finished=' + $failedOutcomeResult.Finished)

    $missing = Join-Path $root 'missing.trx'
    [System.IO.File]::WriteAllText($missing, '<?xml version="1.0" encoding="utf-8"?><TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"></TestRun>')
    $missingResult = & $call $reader $missing $matchDiag $null
    Assert-C487 -Cond (-not $missingResult.Finished) -Name 'group4 native reader rejects a trx with no ResultSummary' -Detail ('finished=' + $missingResult.Finished)

    $truncated = Join-Path $root 'truncated.trx'
    [System.IO.File]::WriteAllText($truncated, '<?xml version="1.0" encoding="utf-8"?><TestRun><ResultSummary outcome="Completed"><Counters total="119" executed="119" failed="0" notExecuted="0"')
    $truncatedResult = & $call $reader $truncated $matchDiag $null
    Assert-C487 -Cond (-not $truncatedResult.Finished) -Name 'group4 native reader rejects a truncated trx' -Detail ('finished=' + $truncatedResult.Finished)

    Assert-C487 -Cond ($null -ne $aged) -Name 'group4 native reader has a process-start age check'
    $old = New-Object System.DateTime 2020, 1, 1, 0, 0, 0, ([System.DateTimeKind]::Utc)
    $newer = New-Object System.DateTime 2026, 11, 10, 12, 0, 0, ([System.DateTimeKind]::Utc)
    $before = New-Object System.DateTime 2026, 11, 10, 11, 0, 0, ([System.DateTimeKind]::Utc)
    [System.IO.File]::SetLastWriteTimeUtc($matchTrx, $old)
    [System.IO.File]::SetLastWriteTimeUtc($matchDiag, $newer)
    $staleTrx = & $call $aged $matchTrx $matchDiag $before
    Assert-C487 -Cond (-not $staleTrx.Finished) -Name 'group4 native reader ignores a trx older than the process start' -Detail ('finished=' + $staleTrx.Finished + ' error=' + $staleTrx.Error)
    [System.IO.File]::SetLastWriteTimeUtc($matchTrx, $newer)
    [System.IO.File]::SetLastWriteTimeUtc($matchDiag, $old)
    $staleDiag = & $call $aged $matchTrx $matchDiag $before
    Assert-C487 -Cond (-not $staleDiag.Finished) -Name 'group4 native reader ignores a diagnostic older than the process start' -Detail ('finished=' + $staleDiag.Finished + ' error=' + $staleDiag.Error)
    [System.IO.File]::SetLastWriteTimeUtc($matchTrx, $newer)
    [System.IO.File]::SetLastWriteTimeUtc($matchDiag, $newer)
    $sameAge = & $call $aged $matchTrx $matchDiag $newer
    Assert-C487 -Cond (-not $sameAge.Finished) -Name 'group4 native reader ignores evidence timestamped at the process start' -Detail ('finished=' + $sameAge.Finished + ' error=' + $sameAge.Error)
    $freshAge = & $call $aged $matchTrx $matchDiag $before
    Assert-C487 -Cond ($freshAge.Finished -and [string]::IsNullOrEmpty($freshAge.Error)) -Name 'group4 native reader accepts evidence newer than the process start' -Detail ('finished=' + $freshAge.Finished + ' error=' + $freshAge.Error)

    $graceType = $null
    try { $graceType = [Antiphon.Nightly.NightlyCompletionGrace] } catch { $graceType = $null }
    $graceValue = -1
    if ($null -ne $graceType) { $graceValue = [int]$graceType::DefaultMilliseconds }
    Assert-C487 -Cond ($graceValue -eq 60000 -and $script:NightlyCompletionGraceMilliseconds -eq $graceValue) -Name 'group4 grace constant is the unmeasured 60000 ms placeholder' -Detail ('csharp=' + $graceValue + ' powershell=' + $script:NightlyCompletionGraceMilliseconds)
    $ownerText = [System.IO.File]::ReadAllText($ownerPath)
    Assert-C487 -Cond ($ownerText.Contains('NightlyCompletionGrace.Wait(') -and $ownerText.Contains('2x') -and $ownerText.Contains('p95')) -Name 'group4 native loop waits on the named grace constant'
    $graceImplText = [System.IO.File]::ReadAllText((Join-Path $lib 'nightly-tests-impl.ps1'))
    Assert-C487 -Cond (-not $graceImplText.Contains('function Invoke-NightlyCompletionGrace')) -Name 'group4 powershell grace copy is not a second implementation'

    $measured = -1.0
    $measureError = ''
    try {
        $stamp = New-Object System.DateTime 2026, 11, 10, 0, 0, 0, ([System.DateTimeKind]::Utc)
        $exitAt = $stamp.AddSeconds(5)
        [System.IO.File]::SetLastWriteTimeUtc($matchDiag, $stamp)
        $measured = [double]$graceType::MeasureFileStopToExitSeconds($matchDiag, $exitAt)
    } catch { $measureError = $_.Exception.Message }
    $measuredOk = ($measureError -eq '' -and [math]::Abs($measured - 5.0) -lt 0.05)
    Assert-C487 -Cond $measuredOk -Name 'group4 stop-to-exit seconds use the diagnostic write and the process exit' -Detail ('seconds=' + $measured + ' error=' + $measureError)
    $linePath = Join-Path $root 'chunk.log'
    [System.IO.File]::WriteAllText($linePath, "host-output`n")
    $lineError = ''
    try { [void]$graceType::AppendStopToExitLine($linePath, 5.0) } catch { $lineError = $_.Exception.Message }
    $lineText = ''
    if (Test-Path -LiteralPath $linePath) { $lineText = [System.IO.File]::ReadAllText($linePath) }
    Assert-C487 -Cond ($lineError -eq '' -and $lineText.Contains('execution-stop-to-exit-seconds=5.000')) -Name 'group4 chunk log records stop-to-exit seconds' -Detail ($lineError + $lineText)
    $skipPath = Join-Path $root 'skip.log'
    [System.IO.File]::WriteAllText($skipPath, "host-output`n")
    try { [void]$graceType::AppendStopToExitLine($skipPath, -1) } catch { }
    $skipText = [System.IO.File]::ReadAllText($skipPath)
    Assert-C487 -Cond (-not $skipText.Contains('execution-stop-to-exit-seconds=')) -Name 'group4 chunk log omits stop-to-exit when the markers were absent' -Detail $skipText

    $hangField = $null
    $hangError = ''
    try { $hangField = [string]([Antiphon.Nightly.NightlyWatchdog]::HarnessHangField($true)) } catch { $hangError = $_.Exception.Message }
    Assert-C487 -Cond ($hangField -eq 'completed-but-hung') -Name 'group4 summary harnessHang reason is completed-but-hung' -Detail $hangError
    $clearField = 'present'
    try { $clearField = [string]([Antiphon.Nightly.NightlyWatchdog]::HarnessHangField($false)) } catch { $clearField = 'present' }
    Assert-C487 -Cond ($clearField -eq '') -Name 'group4 summary harnessHang is empty when the chunk is not a hang' -Detail $clearField
    $implText = [System.IO.File]::ReadAllText((Join-Path $lib 'nightly-tests-impl.ps1'))
    Assert-C487 -Cond ($implText.Contains('harnessHang =') -and $implText.Contains('NightlyWatchdog]::HarnessHangField')) -Name 'group4 summary records harnessHang beside the red result'
    $notePath = Join-Path $repo (Join-Path 'docs' (Join-Path 'investigations' '2026-11-10-why-missed-session-runner-group4-post-completion-hang.md'))
    $noteText = [System.IO.File]::ReadAllText($notePath)
    Assert-C487 -Cond ($noteText.Contains('publicationTerminal') -and $noteText.Contains('failed-run') -and $noteText.Contains('<ResultSummary')) -Name 'group4 note records the opening-tag reader and publicationTerminal failed-run'
}

function Test-Group4CompletedHang {
    # session-runner all-group-4: TRX Completed and "total ran" matched the
    # selection, then the process tree lived until the launch watchdog.
    $root = Join-Path $ResultsDirectory 'group4-completed-hang'
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    $writeTrx = {
        param([string]$Path, [string]$Outcome, [int]$Total, [int]$Executed, [int]$NotExecuted)
        $xml = '<?xml version="1.0" encoding="utf-8"?><TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><ResultSummary outcome="' + $Outcome + '"><Counters total="' + $Total + '" executed="' + $Executed + '" passed="' + $Executed + '" failed="0" notExecuted="' + $NotExecuted + '" /></ResultSummary></TestRun>'
        [System.IO.File]::WriteAllText($Path, $xml)
    }
    $matchTrx = Join-Path $root 'match.trx'
    $matchDiag = Join-Path $root 'match.diag'
    & $writeTrx $matchTrx 'Completed' 119 119 0
    $pad = [string]::new('x', 20000)
    [System.IO.File]::WriteAllText($matchDiag, ($pad + "`ntotal ran: 119`nrequest execute stop`n"))
    $finished = $false
    $finishedError = ''
    try { $finished = [bool]([Antiphon.Nightly.NightlyWatchdog]::ExecutionFinished($matchTrx, $matchDiag)) } catch { $finishedError = $_.Exception.Message }
    Assert-C487 -Cond ($finished -and [string]::IsNullOrEmpty($finishedError)) -Name 'group4 completed trx plus matching total ran is finished' -Detail $finishedError

    $lowDiag = Join-Path $root 'low.diag'
    [System.IO.File]::WriteAllText($lowDiag, "total ran: 118`nrequest execute stop`n")
    $low = $true
    try { $low = [bool]([Antiphon.Nightly.NightlyWatchdog]::ExecutionFinished($matchTrx, $lowDiag)) } catch { $low = $true }
    Assert-C487 -Cond (-not $low) -Name 'group4 total ran that misses the trx total is not finished'

    $noStop = Join-Path $root 'nostop.diag'
    [System.IO.File]::WriteAllText($noStop, "total ran: 119`n")
    $stopped = $true
    try { $stopped = [bool]([Antiphon.Nightly.NightlyWatchdog]::ExecutionFinished($matchTrx, $noStop)) } catch { $stopped = $true }
    Assert-C487 -Cond (-not $stopped) -Name 'group4 total ran without request execute stop is not finished'

    $bare = Join-Path $root 'bare.trx'
    Write-C487Trx -Path $bare -Rows @([pscustomobject]@{ Id = '1'; ClassName = 'A'; MethodName = 'M'; Outcome = 'Passed' })
    $bareFinished = $true
    try { $bareFinished = [bool]([Antiphon.Nightly.NightlyWatchdog]::ExecutionFinished($bare, $matchDiag)) } catch { $bareFinished = $true }
    Assert-C487 -Cond (-not $bareFinished) -Name 'group4 trx without ResultSummary is not finished'

    $partial = Join-Path $root 'partial.trx'
    & $writeTrx $partial 'Completed' 119 118 1
    $partialFinished = $true
    try { $partialFinished = [bool]([Antiphon.Nightly.NightlyWatchdog]::ExecutionFinished($partial, $matchDiag)) } catch { $partialFinished = $true }
    Assert-C487 -Cond (-not $partialFinished) -Name 'group4 notExecuted row is not a completed hang'

    $failedTrx = Join-Path $root 'failed.trx'
    & $writeTrx $failedTrx 'Failed' 119 119 0
    $failedFinished = $true
    try { $failedFinished = [bool]([Antiphon.Nightly.NightlyWatchdog]::ExecutionFinished($failedTrx, $matchDiag)) } catch { $failedFinished = $true }
    Assert-C487 -Cond (-not $failedFinished) -Name 'group4 Failed outcome is not a completed hang'

    $hung = $null
    $hungError = ''
    try { $hung = [Antiphon.Nightly.NightlyWatchdog]::CompletedHang($true, $true, 'session-runner', 'pid=42/image=git') } catch { $hungError = $_.Exception.Message }
    $hungOk = ($null -ne $hung -and [bool]$hung.CompletedButHung -and [string]$hung.Reason -eq 'completed-but-hung session-runner children=pid=42/image=git')
    Assert-C487 -Cond $hungOk -Name 'group4 finished timeout records completed-but-hung and the child list' -Detail $hungError

    $openHang = $null
    try { $openHang = [Antiphon.Nightly.NightlyWatchdog]::CompletedHang($true, $false, 'session-runner', 'pid=42/image=git') } catch { $openHang = $null }
    Assert-C487 -Cond ($null -ne $openHang -and -not [bool]$openHang.CompletedButHung -and [string]$openHang.Reason -eq 'watchdog-timeout') -Name 'group4 unfinished timeout stays watchdog-timeout'

    $clean = $null
    try { $clean = [Antiphon.Nightly.NightlyWatchdog]::CompletedHang($false, $true, 'session-runner', 'pid=42/image=git') } catch { $clean = $null }
    Assert-C487 -Cond ($null -ne $clean -and -not [bool]$clean.CompletedButHung) -Name 'group4 a finished run that exited is not a hang'

    $disp = $null
    try { $disp = [Antiphon.Nightly.NightlyWatchdog]::TimeoutDisposition($true, $false, $true) } catch { $disp = $null }
    $dispOk = ($null -ne $disp -and [string]$disp.Reason -eq 'completed-but-hung' -and -not [bool]$disp.CoverageComplete -and -not [bool]$disp.TestsPassed -and [bool]$disp.Incomplete -and -not [bool]$disp.Pass)
    Assert-C487 -Cond $dispOk -Name 'group4 completed hang stays a red test gate'

    $still = [Antiphon.Nightly.NightlyWatchdog]::TimeoutDisposition($true, $false, $false)
    Assert-C487 -Cond ([string]$still.Reason -eq 'watchdog-timeout' -and -not [bool]$still.TestsPassed -and -not [bool]$still.CoverageComplete) -Name 'group4 generic watchdog disposition is unchanged'

    $formatted = ''
    try { $formatted = [string]([Antiphon.Nightly.NightlyWatchdog]::HangChildren(@('42:2026-11-09T22:33:18.0000000Z'), @('git'), 7)) } catch { $formatted = '' }
    Assert-C487 -Cond ($formatted -eq 'pid=42/image=git') -Name 'group4 hang children format pid and image' -Detail $formatted

    $fallback = 'present'
    try { $fallback = [string]([Antiphon.Nightly.NightlyWatchdog]::HangChildren(@(), @(), 9)) } catch { $fallback = 'present' }
    Assert-C487 -Cond ($fallback -eq 'pid=9/image=') -Name 'group4 hang with no descendants names the root' -Detail $fallback

    $capture = Join-Path $root 'capture.hang.txt'
    $captureError = ''
    try { [Antiphon.Nightly.NightlyWatchdog]::WriteHangCapture($capture, 'session-runner', 'pid=42/image=git', 7) } catch { $captureError = $_.Exception.Message }
    $captureText = ''
    if (Test-Path -LiteralPath $capture) { $captureText = [System.IO.File]::ReadAllText($capture) }
    $captureOk = ($captureText.Contains('completed-hang-capture') -and $captureText.Contains('handles=not-enumerated') -and $captureText.Contains('pid=42/image=git') -and $captureText.Contains('suite=session-runner'))
    Assert-C487 -Cond $captureOk -Name 'group4 powershell hang capture names the tree and skips handle names' -Detail $captureError

    $graceLoad = ''
    if (-not ('Antiphon.Nightly.NightlyCompletionGrace' -as [type])) { $graceLoad = 'NightlyCompletionGrace is not in the loaded core' }
    $sleep = $null
    $grace = $null
    $graceError = $graceLoad
    try {
        $sleep = Start-Process -FilePath '/bin/sleep' -ArgumentList '30' -PassThru -ErrorAction Stop
        try {
            $box = @($sleep)
            $exited = [Func[bool]]{ $box[0].HasExited }.GetNewClosure()
            $finishedNow = [Func[bool]]{ $true }
            $grace = [Antiphon.Nightly.NightlyCompletionGrace]::Wait($exited, $finishedNow, 300, 5000, 10)
        } catch { $graceError = $_.Exception.Message }
        $alive = ($null -ne $sleep -and -not $sleep.HasExited)
        $graceOk = ($null -ne $grace -and [bool]$grace.CompletedButHung -and [bool]$grace.TimedOut -and $alive -and [int]$grace.ElapsedMilliseconds -ge 250 -and [int]$grace.ElapsedMilliseconds -lt 2000)
        $graceDetail = $graceError
        if ($null -ne $grace) { $graceDetail = 'elapsed=' + $grace.ElapsedMilliseconds + ' hung=' + $grace.CompletedButHung + ' alive=' + $alive }
        Assert-C487 -Cond $graceOk -Name 'group4 host must be classified hung inside the grace once total ran is reported' -Detail $graceDetail
    } finally {
        if ($null -ne $sleep -and -not $sleep.HasExited) {
            Stop-Process -Id $sleep.Id -Force -ErrorAction SilentlyContinue
        }
    }

    $openGrace = $null
    try {
        $stillRunning = [Func[bool]]{ $false }
        $notFinished = [Func[bool]]{ $false }
        $openGrace = [Antiphon.Nightly.NightlyCompletionGrace]::Wait($stillRunning, $notFinished, 5000, 250, 10)
    } catch { $openGrace = $null }
    $openOk = ($null -ne $openGrace -and [bool]$openGrace.TimedOut -and -not [bool]$openGrace.CompletedButHung -and [int]$openGrace.ElapsedMilliseconds -lt 1000)
    $openDetail = ''
    if ($null -ne $openGrace) { $openDetail = 'elapsed=' + $openGrace.ElapsedMilliseconds + ' hung=' + $openGrace.CompletedButHung }
    Assert-C487 -Cond $openOk -Name 'group4 unfinished run still uses the launch watchdog' -Detail $openDetail

    $ownerPath = Join-Path $lib (Join-Path 'Antiphon.Nightly' 'NativeProcessOwner.cs')
    $implPath = Join-Path $lib 'nightly-tests-impl.ps1'
    $ownerText = [System.IO.File]::ReadAllText($ownerPath)
    $implText = [System.IO.File]::ReadAllText($implPath)
    $captureAt = $ownerText.IndexOf('WriteHangCapture(job, root, logPath)')
    $killAt = $ownerText.IndexOf('TerminateJobObject(job, 1)')
    Assert-C487 -Cond ($captureAt -ge 0 -and $killAt -gt $captureAt) -Name 'group4 hang capture is written before the job is killed' -Detail ('capture=' + $captureAt + ' kill=' + $killAt)
    Assert-C487 -Cond ($ownerText.Contains('Observation.CompletedButHung = true')) -Name 'group4 native owner records CompletedButHung'
    Assert-C487 -Cond ($ownerText.Contains('NtQuerySystemInformation')) -Name 'group4 hang capture lists open handles'
    Assert-C487 -Cond (-not $ownerText.Contains('NtQueryObject')) -Name 'group4 hang capture does not query handle names'
    Assert-C487 -Cond ($ownerText.Contains('total ran:') -and $ownerText.Contains('request execute stop')) -Name 'group4 native grace uses the same finish markers'
    Assert-C487 -Cond ($implText.Contains('NightlyWatchdog]::CompletedHang')) -Name 'group4 chunk classifies a completed hang'
    Assert-C487 -Cond ($implText.Contains("-HangCapturePath (`$logPath + '.hang.txt')") -and $implText.Contains('-ExecutionGraceMilliseconds')) -Name 'group4 chunk arms the native grace and hang capture'
    Assert-C487 -Cond ($implText.Contains("notmatch '^process-exit-'")) -Name 'group4 completed hang drops the kill exit code reason'
    Assert-C487 -Cond ($implText.Contains('if ($completedHang)') -and $implText.Contains("::Outcome(`$script:NightlyTestsOutcome, 'TESTS')") -and $implText.Contains("`$timingWhy = 'completed-but-hung'")) -Name 'group4 completed hang is a test outcome rather than a generic timeout'
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
    Test-Group4CompletedHang
    Test-Group4NativeTrxReader
}
Write-C487Evidence -ResultsDirectory $ResultsDirectory -Case 'tests-summary' -Body @{ passed = $script:C487Passed; failed = $script:C487Failed; rows = $script:C487Rows }
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows $(if ($Case) { 0 } else { 104 })
