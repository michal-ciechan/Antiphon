#requires -Version 7.0
# Antiphon.Scripts select-release against the PowerShell selection worker it stands in front of: from the same
# state, each attempt must leave byte-identical selection files and return the same value or refusal whichever
# path answers, and the compiled command must answer exactly the cases it claims (no baseline). ASCII-only.
param([string]$Case = '', [string]$ResultsDirectory = '')
$ErrorActionPreference = 'Stop'
foreach ($name in 'nightly-common', 'release-selection', 'test-selection', 'c487-harness') {
    . (Join-Path $PSScriptRoot ('lib/' + $name + '.ps1'))
}
$repo = Split-Path -Parent $PSScriptRoot
$root = New-C487Root $ResultsDirectory

function Assert-RS([bool]$Ok, [string]$Name, [string]$Detail = '') { Assert-C487 $Ok ('RS ' + $Case + '.' + $Name) $Detail }

function Invoke-RSGit([string]$Root, [string[]]$Arguments) { return (Invoke-C487FixtureGit $Root $Arguments) }

function Write-RSFile([string]$Root, [string]$Relative, [string]$Text) {
    $path = Join-Path $Root $Relative
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $path))
    [IO.File]::WriteAllText($path, $Text)
}

# A repository holding the selection inputs at three commits: B, the candidate A, and S with a stale policy hash.
function New-RSRepository {
    $git = Join-Path $root 'repo'
    [void][IO.Directory]::CreateDirectory($git)
    [void](Invoke-RSGit $git @('init', '-q'))
    [void](Invoke-RSGit $git @('config', 'user.name', 'RS fixture'))
    [void](Invoke-RSGit $git @('config', 'user.email', 'fixture@example.invalid'))
    [void](Invoke-RSGit $git @('config', 'core.autocrlf', 'false'))
    foreach ($file in @('tests/test-execution-policy.json', 'tests/test-selection-policy.json', 'global.json', 'scripts/select-test-rosters.ps1', 'scripts/lib/test-selection.ps1')) {
        Write-RSFile $git $file ([IO.File]::ReadAllText((Join-Path $repo $file)))
    }
    [void](Invoke-RSGit $git @('add', '--all')); [void](Invoke-RSGit $git @('commit', '-qm', 'baseline'))
    $b = Invoke-RSGit $git @('rev-parse', 'HEAD')
    Write-RSFile $git 'src/changed.txt' 'candidate'
    [void](Invoke-RSGit $git @('add', '--all')); [void](Invoke-RSGit $git @('commit', '-qm', 'candidate'))
    $a = Invoke-RSGit $git @('rev-parse', 'HEAD')
    $policy = [IO.File]::ReadAllText((Join-Path $git 'tests/test-execution-policy.json'))
    Write-RSFile $git 'tests/test-execution-policy.json' ($policy -replace '"policyHash"\s*:\s*"[0-9a-f]{64}"', ('"policyHash": "' + ('0' * 64) + '"'))
    [void](Invoke-RSGit $git @('add', '--all')); [void](Invoke-RSGit $git @('commit', '-qm', 'stale policy'))
    $stale = Invoke-RSGit $git @('rev-parse', 'HEAD')
    return [pscustomobject]@{ Root = $git; B = $b; A = $a; Stale = $stale }
}

function New-RSRequest($Fixture, [string]$Release, [hashtable]$Change = @{}) {
    $request = @{RepositoryRoot = $Fixture.Root; Repository = 'fixture/repository'; ReleaseRoot = $Release; CandidateId = 'rc-2026-11-08-fixture'
        CandidateRef = 'rc/2026-11-08'; CandidateSha = $Fixture.A; BaselineSha = ''; SelectionMode = 'Full'; Profile = 'rc'; RunId = 'run-1'; Attempt = 1
        BaselineReceiptPath = ''; QualificationPath = ''; TimingRoot = (Join-Path $Release ('timing root ' + [char]0x3bb)); ReleaseIntentPath = ''; TrainManifestPath = ''}
    foreach ($key in $Change.Keys) { $request[$key] = $Change[$key] }
    return $request
}

# Every file under the release root: relative path and SHA-256, so two runs compare byte for byte.
function Get-RSTree([string]$Release) {
    $files = [ordered]@{}
    if (-not (Test-Path -LiteralPath $Release)) { return '' }
    foreach ($file in @(Get-ChildItem -LiteralPath $Release -Recurse -File -Force | Sort-Object FullName)) {
        $files[$file.FullName.Substring($Release.Length)] = Get-NightlyFileSha256 $file.FullName
    }
    return ConvertTo-NightlyCanonicalJson $files
}

function Invoke-RSAttempt($Request, [bool]$WorkerOnly) {
    $saved = $script:AntiphonScriptsCli
    if ($WorkerOnly) { $script:AntiphonScriptsCli = '' }
    try {
        $value = New-ReleaseSelectionAttempt $Request
        $answer = 'value ' + (ConvertTo-NightlyCanonicalJson $value)
    } catch { $answer = 'error ' + $_.Exception.Message }
    finally { $script:AntiphonScriptsCli = $saved }
    return [pscustomobject]@{ Answer = $answer; Route = $script:ReleaseSelectionRoute; Tree = (Get-RSTree $Request.ReleaseRoot) }
}

# One scenario: $Prepare builds the starting state in the release root and returns the request; it runs again
# before the worker-only attempt so both start from the same bytes.
function Test-RSScenario($Fixture, [string]$Name, [string]$ExpectedRoute, [scriptblock]$Prepare) {
    $release = Join-Path $root ('release ' + $Name)
    if (Test-Path -LiteralPath $release) { Remove-Item -LiteralPath $release -Recurse -Force }
    [void][IO.Directory]::CreateDirectory($release)
    $requests = @(& $Prepare $release)
    $default = $null
    foreach ($request in $requests) { $default = Invoke-RSAttempt $request $false }
    Remove-Item -LiteralPath $release -Recurse -Force
    [void][IO.Directory]::CreateDirectory($release)
    $requests = @(& $Prepare $release)
    $worker = $null
    foreach ($request in $requests) { $worker = Invoke-RSAttempt $request $true }
    $same = $default.Answer -ceq $worker.Answer -and $default.Tree -ceq $worker.Tree
    Assert-RS ($same -and $default.Route -ceq $ExpectedRoute) $Name ('route=' + $default.Route + ' expected=' + $ExpectedRoute +
        "`n  default: " + $default.Answer + "`n  worker:  " + $worker.Answer + "`n  trees equal: " + ($default.Tree -ceq $worker.Tree))
}

function Test-RSCliMatchesWorker {
    $cli = Get-AntiphonScriptsCli
    Assert-RS ([bool]$cli) 'cliBuilt' ('cli=' + $cli)
    $fx = New-RSRepository
    Test-RSScenario $fx 'full' 'cli' { param($r) New-RSRequest $fx $r }
    Test-RSScenario $fx 'changeAware' 'cli' { param($r) New-RSRequest $fx $r @{ SelectionMode = 'ChangeAware' } }
    Test-RSScenario $fx 'inputs' 'cli' {
        param($r)
        Write-RSFile $r 'claims/qualification.json' '{"claim":"unqualified","rows":[1,2]}'
        Write-RSFile $r 'train/manifest.json' 'opaque train bytes'
        New-RSRequest $fx $r @{ QualificationPath = (Join-Path $r 'claims/qualification.json'); TrainManifestPath = (Join-Path $r 'train/manifest.json'); ReleaseIntentPath = (Join-Path $r 'intent.json') }
    }
    Test-RSScenario $fx 'absentInputs' 'cli' { param($r) New-RSRequest $fx $r @{ QualificationPath = (Join-Path $r 'none.json'); TrainManifestPath = (Join-Path $r 'none-train.json') } }
    Test-RSScenario $fx 'resume' 'cli' { param($r) @((New-RSRequest $fx $r), (New-RSRequest $fx $r)) }
    Test-RSScenario $fx 'secondAttempt' 'cli' { param($r) @((New-RSRequest $fx $r), (New-RSRequest $fx $r @{ RunId = 'run-2'; Attempt = 2 })) }
    # Each of these the compiled command must leave to the worker, which answers or refuses.
    Test-RSScenario $fx 'baseline' 'worker' { param($r) New-RSRequest $fx $r @{ BaselineSha = $fx.B } }
    Test-RSScenario $fx 'baselineArchive' 'worker' { param($r) [void][IO.Directory]::CreateDirectory((Join-Path $r 'baselines')); New-RSRequest $fx $r }
    Test-RSScenario $fx 'conflict' 'worker' {
        param($r)
        Write-RSFile $r 'candidates/rc-2026-11-08-fixture/attempts/run-1/selection.json' '{"tampered":true}'
        New-RSRequest $fx $r
    }
    Test-RSScenario $fx 'notCommit' 'worker' { param($r) New-RSRequest $fx $r @{ CandidateSha = ('0' * 40) } }
    Test-RSScenario $fx 'stalePolicy' 'worker' { param($r) New-RSRequest $fx $r @{ CandidateSha = $fx.Stale } }
    Test-RSScenario $fx 'qualificationArray' 'worker' {
        param($r)
        Write-RSFile $r 'q.json' '[1]'
        New-RSRequest $fx $r @{ QualificationPath = (Join-Path $r 'q.json') }
    }
    Test-RSScenario $fx 'trainDirectory' 'worker' {
        param($r)
        [void][IO.Directory]::CreateDirectory((Join-Path $r 'train-dir'))
        New-RSRequest $fx $r @{ TrainManifestPath = (Join-Path $r 'train-dir') }
    }
    Test-RSScenario $fx 'outsideState' 'worker' {
        param($r)
        $outside = Join-Path $root ('outside-' + [guid]::NewGuid().ToString('N') + '.json')
        [IO.File]::WriteAllText($outside, '{}')
        New-RSRequest $fx $r @{ QualificationPath = $outside }
    }
    Test-RSScenario $fx 'badRunId' 'worker' { param($r) New-RSRequest $fx $r @{ RunId = '-bad' } }
}

$expected = 0
switch -Exact ($Case) {
    'CliMatchesWorker' { Test-RSCliMatchesWorker; $expected = 16 }
    default { throw ('unknown case: ' + $Case) }
}
Complete-C487Harness -ResultsDirectory $root -ExpectedRows $expected
