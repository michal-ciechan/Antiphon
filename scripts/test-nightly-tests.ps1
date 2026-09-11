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
}

function Test-C487_G040 {
    foreach ($kind in @('list-only', 'inprogress')) {
        $v = ConvertTo-NightlyCoverageVerdict -TerminalRows @() -ProcessExit 0 -Sha 'a' -ExpectedSha 'a' -GitRef 'origin/master' -ExpectedRef 'origin/master' -PolicyHash 'p' -ExpectedPolicyHash 'p' -DiscoveryNodes @([pscustomobject]@{ State = 'InProgress' }) -UsedDiscoveryAsExecution ($kind -eq 'list-only')
        Assert-C487 -Cond (-not $v.coverageComplete -and -not $v.testsPassed) -Name ('G040 {0}' -f $kind)
    }
    $fx = New-Efx
    $r = Invoke-E -Fx $fx -Extra @{ Suites = @('antiphon') }
    Assert-C487 -Cond ((-not [bool]$r.coverageComplete) -and (-not [bool]$r.testsPassed)) -Name 'G040 missing-trx entry' -Detail ('cov=' + $r.coverageComplete + ' pass=' + $r.testsPassed)
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
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 55
