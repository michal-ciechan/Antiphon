#requires -Version 7.0
# Antiphon.Scripts select-release against the PowerShell selection worker it stands in front of: from the same
# state, each attempt must leave byte-identical selection files and return the same value or refusal whichever
# path answers, and the compiled command must answer exactly the cases it claims (every admitted attempt, with or
# without a baseline; each refusal is the worker's). ASCII-only.
param([string]$Case = '', [string]$ResultsDirectory = '')
$ErrorActionPreference = 'Stop'
foreach ($name in 'nightly-common', 'nightly-policy', 'nightly-coverage', 'release-gate', 'release-authority', 'release-selection', 'test-selection', 'c487-harness') {
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
    Test-RSScenario $fx 'baseline' 'cli' { param($r) New-RSRequest $fx $r @{ BaselineSha = $fx.B } }
    Test-RSScenario $fx 'baselineArchive' 'cli' { param($r) [void][IO.Directory]::CreateDirectory((Join-Path $r 'baselines')); New-RSRequest $fx $r }
    # Each of these the compiled command must leave to the worker, which refuses.
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

# Commit the given change on top of $Parent (or as a root commit) without moving any branch.
function New-RSCommit([string]$Root, [string]$Parent, [string]$Message, [scriptblock]$Change) {
    if ($Parent) { [void](Invoke-RSGit $Root @('read-tree', '--reset', '-u', $Parent)) }
    else { [void](Invoke-RSGit $Root @('rm', '-rq', '--cached', '--ignore-unmatch', '.')); Get-ChildItem -LiteralPath $Root -Force | Where-Object Name -cne '.git' | Remove-Item -Recurse -Force }
    & $Change
    [void](Invoke-RSGit $Root @('add', '--all'))
    $tree = Invoke-RSGit $Root @('write-tree')
    if ($Parent) { return Invoke-RSGit $Root @('commit-tree', $tree, '-p', $Parent, '-m', $Message) }
    return Invoke-RSGit $Root @('commit-tree', $tree, '-m', $Message)
}

# Rewrites the policy (and the catalog) so their hashes and the pin still agree: the selector accepts the commit.
function Write-RSPolicy([string]$Root, [scriptblock]$EditPolicy, [scriptblock]$EditCatalog) {
    $catalogPath = Join-Path $Root 'tests/test-selection-policy.json'
    $policyPath = Join-Path $Root 'tests/test-execution-policy.json'
    $policy = Get-Content -LiteralPath $policyPath -Raw | ConvertFrom-Json
    if ($EditCatalog) {
        $catalog = Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json -AsHashtable
        & $EditCatalog $catalog
        $catalog.catalogHash = Get-TestSelectionCatalogHash $catalog
        [IO.File]::WriteAllText($catalogPath, (ConvertTo-Json $catalog -Depth 30))
        $policy.selectionCatalog.hash = $catalog.catalogHash
    }
    if ($EditPolicy) { & $EditPolicy $policy }
    $policy.policyHash = Get-NightlyPolicyHash $policy
    [IO.File]::WriteAllText($policyPath, (ConvertTo-Json $policy -Depth 30))
}

# The selection inputs plus a small project graph for the planner: an ancestor Directory.Build import, a project
# reference, test packages, and the include shapes it reads (a glob across directories, ? and a literal bracket,
# this-file-directory, a NuGet package property, an item inside a Target).
function New-RSProjectRepository {
    $git = Join-Path $root 'projects'
    [void][IO.Directory]::CreateDirectory($git)
    [void](Invoke-RSGit $git @('init', '-q'))
    [void](Invoke-RSGit $git @('config', 'user.name', 'RS fixture'))
    [void](Invoke-RSGit $git @('config', 'user.email', 'fixture@example.invalid'))
    [void](Invoke-RSGit $git @('config', 'core.autocrlf', 'false'))
    $p0 = New-RSCommit $git '' 'projects' {
        foreach ($file in @('tests/test-execution-policy.json', 'tests/test-selection-policy.json', 'global.json', 'scripts/select-test-rosters.ps1', 'scripts/lib/test-selection.ps1')) {
            Write-RSFile $git $file ([IO.File]::ReadAllText((Join-Path $repo $file)))
        }
        Write-RSFile $git 'tests/rc-project-categories.json' ('{"schemaVersion":1,"projects":{"src/A/A.csproj":["app"],"src/B/B.csproj":["lib","core"]},' +
            '"batches":["src/A/A.csproj"],"nonQualificationTests":{}}')
        Write-RSFile $git 'build/common.props' '<Project><ItemGroup><AdditionalFiles Include="$(MSBuildThisFileDirectory)stylecop.json" /></ItemGroup></Project>'
        Write-RSFile $git 'build/stylecop.json' '{}'
        Write-RSFile $git 'Directory.Build.props' '<Project><Import Project="build/common.props" /></Project>'
        Write-RSFile $git 'src/A/A.csproj' (@(
            '<Project Sdk="Microsoft.NET.Sdk">',
            '  <ItemGroup>',
            '    <ProjectReference Include="..\B\B.csproj" />',
            '    <PackageReference Include="tunit" Version="1.0.0" />',
            '    <Compile Include="../shared/**/*.cs" />',
            '    <Compile Include="gen/?.cs" />',
            '    <None Include="$(MSBuildThisFileDirectory)assets/*.txt" />',
            '    <Content Include="$(PkgFixture_Tools)\tools\x.dll" />',
            '    <Content Include="[lit]/*.md" />',
            '  </ItemGroup>',
            '  <Target Name="Late"><ItemGroup><Compile Include="late/*.cs" /></ItemGroup></Target>',
            '</Project>') -join "`n")
        Write-RSFile $git 'src/A/Program.cs' 'class P {}'
        Write-RSFile $git 'src/A/gen/1.cs' 'class G1 {}'
        Write-RSFile $git 'src/A/gen/12.cs' 'class G12 {}'
        Write-RSFile $git 'src/A/assets/a.txt' 'asset'
        Write-RSFile $git 'src/A/[lit]/n.md' 'literal bracket'
        Write-RSFile $git 'src/shared/deep/S.cs' 'class S {}'
        Write-RSFile $git 'src/B/B.csproj' '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><Compile Include="*.cs" /></ItemGroup></Project>'
        Write-RSFile $git 'src/B/B.cs' 'class B {}'
    }
    $p1 = New-RSCommit $git $p0 'b' { Write-RSFile $git 'src/B/B.cs' 'class B { int x; }' }
    $add = New-RSCommit $git $p1 'add' {
        Write-RSFile $git 'src/C/C.csproj' '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><ProjectReference Include="../A/A.csproj" /><PackageReference Include="TUnit" /></ItemGroup></Project>'
        Write-RSFile $git 'src/A/Program.cs' 'class P { }'
    }
    $remove = New-RSCommit $git $p1 'remove' { Remove-Item -LiteralPath (Join-Path $git 'src/B') -Recurse -Force }
    $rename = New-RSCommit $git $p1 'rename' { Move-Item -LiteralPath (Join-Path $git 'src/A') (Join-Path $git 'src/A2') }
    $glob = New-RSCommit $git $p1 'glob' {
        Write-RSFile $git 'src/shared/deep/er/T.cs' 'class T {}'
        Write-RSFile $git 'src/A/gen/2.cs' 'class G2 {}'
        Write-RSFile $git 'src/A/late/L.cs' 'class L {}'
        Write-RSFile $git 'src/A/assets/b.txt' 'asset b'
    }
    $policy = New-RSCommit $git $p1 'policy' { Write-RSPolicy $git { param($p) $p.profiles.rc.description += ' changed contract' } $null }
    $catalog = New-RSCommit $git $p1 'catalog' { Write-RSPolicy $git $null { param($c) $c.sets[0].reason = [string]$c.sets[0].reason + ' (edited)' } }
    $staleBase = New-RSCommit $git $p0 'stale base' {
        $text = [IO.File]::ReadAllText((Join-Path $git 'tests/test-execution-policy.json'))
        Write-RSFile $git 'tests/test-execution-policy.json' ($text -replace '"policyHash"\s*:\s*"[0-9a-f]{64}"', ('"policyHash": "' + ('0' * 64) + '"'))
    }
    $staleChild = New-RSCommit $git $staleBase 'restored' {
        Write-RSFile $git 'tests/test-execution-policy.json' ([IO.File]::ReadAllText((Join-Path $repo 'tests/test-execution-policy.json')))
    }
    $unrelated = New-RSCommit $git '' 'unrelated' { Write-RSFile $git 'other.txt' 'no common history' }
    [void](Invoke-RSGit $git @('read-tree', '--reset', '-u', $p1))
    return [pscustomobject]@{ Root = $git; P0 = $p0; P1 = $p1; Add = $add; Remove = $remove; Rename = $rename; Glob = $glob
        Policy = $policy; Catalog = $catalog; StaleBase = $staleBase; StaleChild = $staleChild; Unrelated = $unrelated }
}

# A full-release baseline receipt for $Sha (as the change-aware suite archives one): real pinned git bytes and
# synthetic passing TRX. It proves the reader only; it is not a production supplier fixture.
function Write-RSReceipt($Fixture, [string]$Release, [string]$Sha, [string]$Name, [string]$Directory = 'baselines') {
    $candidateRoot = 'archive/' + $Name
    $croot = Join-Path $Release $candidateRoot; $dir = Join-Path $croot 'authority'
    $contract = Get-ReleaseSelectionContract $Fixture.Root $Sha 'rc'
    $blob = Read-ReleaseAuthorityPinnedBlob $Fixture.Root $Sha
    $policy = ConvertFrom-ReleaseAuthorityPolicyBytes $blob.Bytes
    $authority = @{schemaVersion=1;kind='release-authority';repository='fixture/repository';profile='rc';sha=$Sha;candidateId=('historical-' + $Name);candidateRef='rc/2026-11-08'
        intentId='historical-intent';releaseCardId='e83e4f3f-7b8c-45e1-8d93-5b337e343f53';requiredSuites=@($policy.RequiredSuites);exclusions=@($policy.Exclusions)
        policyHash=$policy.PolicyHash;policyBlobId=$blob.BlobId;policyRawSha256=(Get-ReleaseAuthorityBytesSha256 $blob.Bytes)}
    Write-NightlyAtomicJson (Join-Path $dir 'authority.json') $authority
    $discovery = @{}; foreach ($suite in $policy.RequiredSuites) { if ($suite -cnotin @('client', 'scripts')) { $discovery[$suite] = @(@{uid=($suite + '-one');class='Fixture';method='One'}) } }
    $plan = New-ReleaseGateExecutionPlan $croot $discovery @{client=@('client-tests');scripts=@('script-tests')}
    $chunks = @(); foreach ($suite in $plan.suites) { foreach ($chunk in $suite.chunks) {
        $trx = Join-Path $croot ('evidence/' + $chunk.id + '.trx'); Write-C487Trx $trx @(@{Id=$chunk.uids[0];ClassName='Fixture';MethodName='One';Outcome='Passed'})
        $chunks += @{suite=$suite.id;chunk=$chunk.id;exitCode=0;trx=('evidence/' + $chunk.id + '.trx');trxSha256=(Get-NightlyFileSha256 $trx)}
    } }
    $ledger = @{sha=$Sha;candidateId=$authority.candidateId;intentId='historical-intent';runId='baseline-run';selection='full';seamed=$false;diagnostic=$false;noReport=$false;teardownSucceeded=$true
        authorityDigest=(Get-NightlyFileSha256 (Join-Path $dir 'authority.json'));executionPlanDigest=(Get-NightlyFileSha256 (Join-Path $dir 'execution-plan.json'))
        prepublicationReceipt=@{kind='prepublication';correlation=@{sha=$Sha;runId='baseline-run'};recipient=@{cardId=$authority.releaseCardId;revision=4}}
        chunks=$chunks;rosters=@(@{suite='client';exitCode=0;entries=@(@{id='client-tests';outcome='passed'})},@{suite='scripts';exitCode=0;entries=@(@{id='script-tests';outcome='passed'})})}
    Write-NightlyAtomicJson (Join-Path $dir 'ledger.json') $ledger
    Write-NightlyAtomicJson (Join-Path $croot 'complete-green.json') @{profile='rc';trigger='rc';ref='rc/2026-11-08';sha=$Sha;expectedSha=$Sha;candidateId=$authority.candidateId
        runId='baseline-run';policyHash=$policy.PolicyHash;exitCode=0;coverageComplete=$true;testsPassed=$true;reportDelivered=$true;coverageScope='Full'}
    $pub = New-ReleaseGatePublicationAuthority $croot @{Authority=$authority;Digest=$ledger.authorityDigest} ('a' * 64)
    $manifest = New-ReleaseGateManifest @{repository='fixture/repository';candidateId=$authority.candidateId;sha=$Sha;publicationAuthorityDigest=$pub.Digest}
    Write-NightlyAtomicJson (Join-Path $croot 'release-manifest.json') $manifest
    Write-NightlyAtomicJson (Join-Path $croot 'publish.json') @{steps=@('published');sha=$Sha;publicationAuthorityDigest=$pub.Digest;manifestDigest=(Get-ReleaseGateManifestDigest $manifest)
        releaseId='historical-release';releaseUrl='https://example.invalid/release'}
    $files = @{}
    foreach ($file in @('authority/authority.json', 'authority/execution-plan.json', 'authority/discovery.json', 'authority/ledger.json',
        'authority/publication-authority.json', 'release-manifest.json', 'publish.json', 'complete-green.json')) { $files[$file] = Get-NightlyFileSha256 (Join-Path $croot $file) }
    $path = Join-Path $Release ($Directory + '/' + $Name + '.json')
    Write-NightlyAtomicJson $path @{format='antiphon-full-release-baseline-v1';coverageScope='Full';profile='rc';repository='fixture/repository';sha=$Sha
        platform=$contract.platform;contract=$contract;candidateRoot=$candidateRoot;files=$files}
    return $path
}

function New-RSBaselineRequest($Fixture, [string]$Release, [string]$Candidate, [string]$Baseline, [string]$Mode, [hashtable]$More = @{}) {
    $change = @{ CandidateSha = $Candidate; BaselineSha = $Baseline; SelectionMode = $Mode }
    foreach ($key in $More.Keys) { $change[$key] = $More[$key] }
    return New-RSRequest ([pscustomobject]@{ Root = $Fixture.Root; A = $Candidate }) $Release $change
}

function Test-RSBaselineMatchesWorker {
    $fx = New-RSProjectRepository
    Test-RSScenario $fx 'projectAdd' 'cli' { param($r) New-RSBaselineRequest $fx $r $fx.Add $fx.P1 'Shadow' }
    Test-RSScenario $fx 'projectRemove' 'cli' { param($r) New-RSBaselineRequest $fx $r $fx.Remove $fx.P1 'ChangeAware' }
    Test-RSScenario $fx 'projectRename' 'cli' { param($r) New-RSBaselineRequest $fx $r $fx.Rename $fx.P1 'Shadow' }
    Test-RSScenario $fx 'globEdges' 'cli' { param($r) New-RSBaselineRequest $fx $r $fx.Glob $fx.P0 'Shadow' }
    Test-RSScenario $fx 'fullMode' 'cli' { param($r) New-RSBaselineRequest $fx $r $fx.Glob $fx.P1 'Full' }
    Test-RSScenario $fx 'policyChange' 'cli' { param($r) New-RSBaselineRequest $fx $r $fx.Policy $fx.P1 'ChangeAware' }
    Test-RSScenario $fx 'catalogChange' 'cli' { param($r) New-RSBaselineRequest $fx $r $fx.Catalog $fx.P1 'Shadow' }
    Test-RSScenario $fx 'sameCommit' 'cli' { param($r) New-RSBaselineRequest $fx $r $fx.P1 $fx.P1 'Shadow' }
    Test-RSScenario $fx 'receipt' 'cli' {
        param($r)
        $path = Write-RSReceipt $fx $r $fx.P1 'full-p1' 'receipts'
        New-RSBaselineRequest $fx $r $fx.Add '' 'ChangeAware' @{ BaselineReceiptPath = $path }
    }
    Test-RSScenario $fx 'receiptIncompatible' 'cli' {
        param($r)
        $path = Write-RSReceipt $fx $r $fx.P1 'full-p1' 'receipts'
        New-RSBaselineRequest $fx $r $fx.Policy '' 'Shadow' @{ BaselineReceiptPath = $path }
    }
    Test-RSScenario $fx 'receiptMissing' 'cli' { param($r) New-RSBaselineRequest $fx $r $fx.Add $fx.P1 'Shadow' @{ BaselineReceiptPath = (Join-Path $r 'absent receipt.json') } }
    Test-RSScenario $fx 'archiveChoice' 'cli' {
        param($r)
        [void](Write-RSReceipt $fx $r $fx.P0 'full-a'); [void](Write-RSReceipt $fx $r $fx.P1 'full-b')
        New-RSBaselineRequest $fx $r $fx.Add '' 'Shadow'
    }
    Test-RSScenario $fx 'archiveFiltered' 'cli' {
        param($r)
        [void](Write-RSReceipt $fx $r $fx.P0 'full-a'); [void](Write-RSReceipt $fx $r $fx.P1 'full-b')
        New-RSBaselineRequest $fx $r $fx.Add $fx.P0 'Shadow'
    }
    Test-RSScenario $fx 'archiveIncompatible' 'cli' { param($r) [void](Write-RSReceipt $fx $r $fx.P1 'full-b'); New-RSBaselineRequest $fx $r $fx.Policy $fx.P1 'Shadow' }
    # Each of these the compiled command must leave to the worker, which refuses.
    Test-RSScenario $fx 'receiptCorrupt' 'worker' {
        param($r)
        $path = Write-RSReceipt $fx $r $fx.P1 'full-p1' 'receipts'
        $receipt = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable
        $receipt.files['authority/ledger.json'] = '0' * 64
        Write-NightlyAtomicJson $path $receipt
        New-RSBaselineRequest $fx $r $fx.Add '' 'Shadow' @{ BaselineReceiptPath = $path }
    }
    Test-RSScenario $fx 'notAncestor' 'worker' { param($r) New-RSBaselineRequest $fx $r $fx.Add $fx.Unrelated 'Shadow' }
    Test-RSScenario $fx 'staleBaseline' 'worker' { param($r) New-RSBaselineRequest $fx $r $fx.StaleChild $fx.StaleBase 'Shadow' }
}

$expected = 0
switch -Exact ($Case) {
    'CliMatchesWorker' { Test-RSCliMatchesWorker; $expected = 16 }
    'BaselineMatchesWorker' { Test-RSBaselineMatchesWorker; $expected = 17 }
    default { throw ('unknown case: ' + $Case) }
}
Complete-C487Harness -ResultsDirectory $root -ExpectedRows $expected
