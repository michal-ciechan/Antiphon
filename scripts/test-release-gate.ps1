#requires -Version 5.1
# CARD-0599 offline harness for the release-gate lanes. ASCII-only.
#
# Every case invokes PRODUCTION code - the real policy reader, the real credit
# predicate, the real nightly run core, the real coordinator/publisher/registrar
# scripts - and asserts an observed effect. Git, GitHub and Windmill are injected
# at the I/O boundary only; no seam ever replaces a predicate or a gate.
#
# No build, no test assembly, no network and no live registration runs here.
#
# Unit 12 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: scripts/lib/release-gate.ps1,
# release-status.ps1 and register-release-gates.ps1 are C# (Antiphon.Nightly ReleaseGateRemote, ReleaseGateJournal,
# ReleaseGateTags, ReleaseStatus, ReleaseGateRegistration). Their cases run in process (ReleaseGateLibraryTests,
# ReleaseStatusTests, ReleaseGateRegistrationTests). Unit 14: publish-release.ps1 and promote-release.ps1 are C#
# (ReleasePublish, ReleasePromote: antiphon-scripts publish-release and promote-release); their cases run in process over
# ReleaseGateFixture and ReleaseGateGitFixture (ReleasePublishTests, ReleaseGateStatusTests,
# ReleaseGateAuthorityContractTests, ReleaseGatePublicationAuthorityTests, ReleasePromoteTests, ReleaseTagAllocatorTests).
# This harness keeps the still-PowerShell candidate coordinator's cases (its seams files hand $ReleaseGateSeams.Git to
# the C#), the policy and credit cases, the C1731 evidence-path cases and the C1339/C1733 real-git fixture's coordinator
# and adapter rows; the C1339 seed still builds a publication fixture (New-C599PublishFx). Fixture set-up calls the C#
# journal directly.
param(
    [string]$Case = '',
    [string]$ResultsDirectory = ''
)
$ErrorActionPreference = 'Continue'
$here = $PSScriptRoot
$lib = Join-Path $here 'lib'
. (Join-Path $lib 'nightly-common.ps1')
. (Join-Path $lib 'nightly-core.ps1'); Assert-NightlyCoreAvailable 'test-release-gate'
. (Join-Path $lib 'c487-harness.ps1')
# Fixture set-up writes the publication journal through the production C# (reservedAt and publishedAt on the wall clock).
$script:C599Journals = [Antiphon.Nightly.ReleaseGateJournal]::Production()

$ResultsDirectory = New-C487Root -ResultsDirectory $ResultsDirectory
# CARD-0599: every seam this harness hands the coordinator is admitted only in
# explicit test mode (the publisher's admission is ReleasePublishTests' and
# ReleaseGatePublicationAuthorityTests.C599A_PublisherTestMode's, in process).
$env:ANTIPHON_RELEASE_GATE_TEST_MODE = '1'
$repoRoot = Split-Path -Parent $here
$ShaA = ('a' * 40)
$ShaB = ('b' * 40)
$ShaC = ('c' * 40)
$CandidateRef = 'release/rc-20260923T083000Z'
$CandidateId = 'rc-20260923T083000Z'
$script:C1731Timing = [ordered]@{
    fixtureSeconds = 0.0; saveSeconds = 0.0; restoreSeconds = 0.0
    scanSeconds = 0.0; parentSeconds = 0.0; compareSeconds = 0.0; writeSeconds = 0.0
    gateSeconds = 0.0; restores = 0; reads = 0; writes = 0; unchanged = 0; parentOperations = 0
}

function New-C599Root {
    $root = Join-Path $ResultsDirectory ([guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    return $root
}

function New-C599Policy {
    <#
      Writes a real policy file (correct canonical hash) with the requested
      schema/profile shape, so the production reader is what accepts or refuses.
    #>
    param([string]$Root, [int]$SchemaVersion = 2, [switch]$OmitProfiles, [switch]$OmitE2EFromRc, [switch]$StaleHash)
    $suites = [ordered]@{}
    foreach ($id in @('antiphon', 'client', 'scripts')) {
        $suites[$id] = [ordered]@{ name = $id; mode = 'unattended' }
    }
    $suites['antiphon'] = [ordered]@{ name = 'Antiphon.Tests'; project = 'tests/Antiphon.Tests/Antiphon.Tests.csproj'; assembly = 'Antiphon.Tests'; mode = 'unattended' }
    $suites['e2e'] = [ordered]@{ name = 'Antiphon.E2E'; project = 'tests/Antiphon.E2E/Antiphon.E2E.csproj'; assembly = 'Antiphon.E2E'; mode = 'manual' }
    $obj = [ordered]@{
        schemaVersion = $SchemaVersion
        policyHash = ''
        defaultSuites = @('antiphon', 'client', 'scripts')
        projects = @('tests/Antiphon.Tests/Antiphon.Tests.csproj', 'tests/Antiphon.E2E/Antiphon.E2E.csproj')
        suites = $suites
    }
    if (-not $OmitProfiles) {
        $rcSuites = @('antiphon', 'client', 'scripts', 'e2e')
        if ($OmitE2EFromRc) { $rcSuites = @('antiphon', 'client', 'scripts') }
        $obj['profiles'] = [ordered]@{
            nightly = [ordered]@{ requiredSuites = @('antiphon', 'client', 'scripts'); exclusions = @() }
            rc = [ordered]@{
                requiredSuites = $rcSuites
                exclusions = @(
                    [ordered]@{ suite = 'e2e'; class = 'LiveCanaryTests'; reason = 'real headed provider'; owner = 'CARD-0599' },
                    [ordered]@{ suite = 'antiphon'; class = 'MixedTests'; methods = @('Live_row'); reason = 'live row'; owner = 'CARD-0599' }
                )
            }
        }
    }
    $hash = [Antiphon.Nightly.NightlyPolicy]::HashText(($obj | ConvertTo-Json -Depth 10))
    if ($StaleHash) { $obj['policyHash'] = ('0' * 64) } else { $obj['policyHash'] = $hash }
    New-Item -ItemType Directory -Path $Root -Force | Out-Null
    $path = Join-Path $Root 'policy.json'
    Set-Content -LiteralPath $path -Value ($obj | ConvertTo-Json -Depth 10) -Encoding UTF8
    return $path
}

function New-C599State {
    param(
        [string]$Profile = 'nightly',
        [string]$Trigger = 'scheduled',
        [string]$Ref = 'master',
        $Coverage = $true, $Tests = $true, $Report = $true,
        $ExitCode = 0,
        [string]$Sha = '',
        [string]$ExpectedSha = '',
        [string]$Candidate = '',
        [string]$RunId = 'run-1',
        [string]$PolicyHash = 'hash-1'
    )
    return [pscustomobject]@{
        runId = $RunId
        policyHash = $PolicyHash
        profile = $Profile
        trigger = $Trigger
        ref = $Ref
        coverageComplete = $Coverage
        testsPassed = $Tests
        reportDelivered = $Report
        exitCode = $ExitCode
        sha = $Sha
        expectedSha = $ExpectedSha
        candidateId = $Candidate
    }
}

function New-C599GitSeams {
    <#
      A real file-backed fake remote: ls-remote reads it, push writes it, and a
      separate observer read proves the recipient state rather than the request.
    #>
    param([string]$Root, [string]$HeadSha = '', [hashtable]$Refs = @{}, [hashtable]$Fail = @{}, [string]$TracePath = '')
    if ([string]::IsNullOrWhiteSpace($HeadSha)) { $HeadSha = ('b' * 40) }
    if ([string]::IsNullOrWhiteSpace($TracePath)) { $TracePath = Join-Path $Root 'git-trace.log' }
    $remote = Join-Path $Root 'remote.json'
    $fullRefs = Join-Path $Root 'remote-full-refs.json'
    Write-NightlyAtomicJson -Path $fullRefs -Object @{}
    $state = [ordered]@{}
    foreach ($k in $Refs.Keys) { $state[$k] = $Refs[$k] }
    Write-NightlyAtomicJson -Path $remote -Object $state
    $failPath = Join-Path $Root 'git-fail.json'
    $failState = [ordered]@{}
    foreach ($k in $Fail.Keys) { $failState[$k] = $Fail[$k] }
    Write-NightlyAtomicJson -Path $failPath -Object $failState
    $seams = Join-Path $Root 'seams.ps1'
    $text = @"
`$ReleaseGateSeams = @{
    Git = {
        param(`$WorkingDirectory, `$Arguments)
        `$trace = '$TracePath'
        `$remote = '$remote'
        `$failPath = '$failPath'
        Add-Content -LiteralPath `$trace -Value ('GIT ' + (`$Arguments -join ' ')) -Encoding ASCII
        `$fail = Get-Content -LiteralPath `$failPath -Raw | ConvertFrom-Json
        `$state = Get-Content -LiteralPath `$remote -Raw | ConvertFrom-Json
        `$sub = [string]`$Arguments[0]
        function Get-RefKey([string]`$r) { return ((`$r -replace '^refs/heads/', '') -replace '\^\{\}$', '' -replace '[^A-Za-z0-9]', '_') }
        function Get-PushSpec([string]`$spec) {
            if (`$spec -like 'refs/antiphon-release-push/*') {
                `$private = Get-Content -LiteralPath '$Root/private-ref.json' -Raw | ConvertFrom-Json
                return ([string]`$private.ref -replace '^refs/antiphon-release-push/[^/]+/', 'refs/tags/')
            }
            return `$spec
        }
        function Save-FullRef([string]`$ref, [string]`$sha) {
            if (-not `$ref.StartsWith('refs/')) { return }
            `$refs = Get-Content -LiteralPath '$fullRefs' -Raw | ConvertFrom-Json
            `$refs | Add-Member -NotePropertyName `$ref -NotePropertyValue `$sha -Force
            `$refs | ConvertTo-Json | Set-Content -LiteralPath '$fullRefs'
        }
        if (`$fail.PSObject.Properties.Name -contains `$sub -and [bool]`$fail.`$sub) {
            if (`$fail.PSObject.Properties.Name -contains (`$sub + '_commits') -and [bool]`$fail.(`$sub + '_commits')) {
                # commit-then-lose-response: the remote accepted the write, the caller did not hear.
                if (`$sub -eq 'push') {
                    `$spec = Get-PushSpec ([string]`$Arguments[2])
                    `$parts = `$spec -split ':'
                    `$key = Get-RefKey `$parts[`$parts.Count - 1]
                    `$val = `$parts[0]
                    if (`$val -like 'refs/tags/*') { `$val = '$HeadSha' }
                    `$state | Add-Member -NotePropertyName `$key -NotePropertyValue `$val -Force
                    Save-FullRef `$parts[`$parts.Count - 1] `$val
                    `$state | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath `$remote -Encoding UTF8
                }
            }
            return @{ ExitCode = 1; Output = 'injected failure' }
        }
        if (`$sub -eq 'fetch' -or `$sub -eq 'clone' -or `$sub -eq 'tag') { return @{ ExitCode = 0; Output = '' } }
        if (`$sub -eq 'rev-parse' -or `$sub -eq 'rev-list' -or `$sub -eq 'show-ref') { return @{ ExitCode = 0; Output = '$HeadSha' } }
        if (`$sub -eq 'update-ref') {
            if (`$Arguments[1] -eq '-d') { Remove-Item -LiteralPath '$Root/private-ref.json' -ErrorAction SilentlyContinue }
            else { @{ ref = [string]`$Arguments[1] } | ConvertTo-Json | Set-Content -LiteralPath '$Root/private-ref.json' }
            return @{ ExitCode = 0; Output = '' }
        }
        if (`$sub -eq 'log') { return @{ ExitCode = 0; Output = 'abc1234 feat: something' } }
        if (`$sub -eq 'ls-remote') {
            if (`$Arguments -contains '--refs') {
                `$refs = Get-Content -LiteralPath '$fullRefs' -Raw | ConvertFrom-Json
                `$lines = @(`$refs.PSObject.Properties | Where-Object { `$_.Name.StartsWith('refs/tags/') } | ForEach-Object { [string]`$_.Value + "``t" + `$_.Name })
                return @{ ExitCode = 0; Output = (`$lines -join "``n") }
            }
            `$ref = [string]`$Arguments[`$Arguments.Count - 1]
            `$key = Get-RefKey `$ref
            `$val = ''
            if (`$state.PSObject.Properties.Name -contains `$key) { `$val = [string]`$state.`$key }
            if ([string]::IsNullOrWhiteSpace(`$val)) { return @{ ExitCode = 0; Output = '' } }
            return @{ ExitCode = 0; Output = (`$val + "``t" + `$ref) }
        }
        if (`$sub -eq 'push') {
            `$spec = Get-PushSpec ([string]`$Arguments[2])
            `$parts = `$spec -split ':'
            `$key = Get-RefKey `$parts[`$parts.Count - 1]
            `$val = `$parts[0]
            if (`$val -like 'refs/tags/*') { `$val = '$HeadSha' }
            `$state | Add-Member -NotePropertyName `$key -NotePropertyValue `$val -Force
            Save-FullRef `$parts[`$parts.Count - 1] `$val
            `$state | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath `$remote -Encoding UTF8
            return @{ ExitCode = 0; Output = '' }
        }
        return @{ ExitCode = 0; Output = '' }
    }
}
"@
    Set-Content -LiteralPath $seams -Value $text -Encoding ASCII
    return [pscustomobject]@{ Path = $seams; RemotePath = $remote; FailPath = $failPath; Trace = $TracePath; Head = $HeadSha }
}

function Read-C599Remote {
    # Independent observer read: never the coordinator's own return value.
    param($Seams, [string]$Ref)
    $key = (($Ref -replace '^refs/heads/', '') -replace '\^\{\}$', '' -replace '[^A-Za-z0-9]', '_')
    $state = Get-Content -LiteralPath $Seams.RemotePath -Raw | ConvertFrom-Json
    if ($state.PSObject.Properties.Name -contains $key) { return [string]$state.$key }
    return ''
}

function Set-C599GitFailure {
    param($Seams, [string]$Subcommand, [switch]$Clear, [switch]$CommitsAnyway)
    $state = Get-Content -LiteralPath $Seams.FailPath -Raw | ConvertFrom-Json
    $obj = [ordered]@{}
    foreach ($p in @($state.PSObject.Properties)) { $obj[$p.Name] = $p.Value }
    if ($Clear) { $obj.Remove($Subcommand); $obj.Remove($Subcommand + '_commits') }
    else {
        $obj[$Subcommand] = $true
        if ($CommitsAnyway) { $obj[$Subcommand + '_commits'] = $true }
    }
    Write-NightlyAtomicJson -Path $Seams.FailPath -Object $obj
}

function New-C599OwnedReleaseClone {
    param([string]$Root)
    $clone = Join-Path $Root 'checkout'
    New-Item -ItemType Directory -Path (Join-Path $clone '.git') -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $clone (Join-Path '.git' 'config')) -Value "[remote `"origin`"]`n`turl = https://github.com/michal-ciechan/Antiphon`n" -Encoding ASCII
    Write-NightlyAtomicJson -Path (Join-Path $clone '.antiphon-release-owned') -Object ([ordered]@{ kind = 'release-clone' })
    return $clone
}

function New-C599RcGreen {
    <#
      Lay down a complete, honest RC evidence set: candidate journal, summary and
      complete-green, all agreeing on the same candidate/SHA/policy hash.
    #>
    param([string]$ReleaseRoot, [string]$Sha, [string]$CandidateId, [string]$Ref, [string]$PolicyHash = 'policy-1')
    $root = [Antiphon.Nightly.ReleaseGateCredit]::CandidateRoot($ReleaseRoot, $CandidateId)
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    Write-NightlyAtomicJson -Path (Join-Path $root 'candidate.json') -Object ([ordered]@{
        schemaVersion = 1; candidateId = $CandidateId; ref = $Ref; sha = $Sha
        cutUtc = '2026-09-23T08:30:00Z'; scheduleSlot = 'u/lndcobra/antiphon_release_candidates'; pushed = $true
    })
    $summaryPath = Join-Path $root 'summary.json'
    Write-NightlyAtomicJson -Path $summaryPath -Object ([ordered]@{
        profile = 'rc'
        policyHash = $PolicyHash
        requiredSuites = @('antiphon', 'e2e')
        suites = @(
            [ordered]@{ id = 'antiphon'; chunk = 'all'; result = 'pass'; exitCode = 0; skipped = $false; requiredUidCount = 12; excludedUidCount = 1; censusDigest = 'd1'; exclusions = @() },
            [ordered]@{ id = 'e2e'; chunk = 'all'; result = 'pass'; exitCode = 0; skipped = $false; requiredUidCount = 8; excludedUidCount = 3; censusDigest = 'd2'; exclusions = @(
                [ordered]@{ uid = 'u1'; class = 'LiveCanaryTests'; reason = 'real headed provider'; owner = 'CARD-0599' }) }
        )
    })
    Write-NightlyAtomicJson -Path (Join-Path $root 'complete-green.json') -Object ([ordered]@{
        runId = 'rc-run-1'; policyHash = $PolicyHash; profile = 'rc'; trigger = 'rc'; ref = $Ref
        coverageComplete = $true; testsPassed = $true; reportDelivered = $true; exitCode = 0
        sha = $Sha; expectedSha = $Sha; candidateId = $CandidateId
        startedAt = '2026-09-23T08:31:00Z'; completedAt = '2026-09-23T09:50:00Z'
        summaryPath = $summaryPath; capabilities = @('land-v2')
    })
    return $root
}

# ============================================ CARD-0599 D-15 authority fixture ===
# A real scratch git repository holds the policy at a real commit; the authority is
# frozen by the PRODUCTION ReleaseAuthority.Freeze / ReleaseAuthority.Plan,
# and the chunk evidence is real TRX read back by the production TRX reader. Every
# negative below starts from this valid control and changes exactly one thing.

$script:C599RepoPolicyPath = Join-Path $repoRoot (Join-Path 'tests' 'test-execution-policy.json')
$script:C599IntentId = 'intent-20260923T083000Z'
$script:C599RunId = 'rc-run-1'
$script:C599NativeSuites = @('antiphon', 'session-runner', 'pty-host', 'agents-pty', 'messaging', 'e2e', 'markdown-pdf', 'observability', 'server-git', 'resilience', 'git-in-process')
$script:C599Rosters = @{ client = @('lint', 'build', 'vitest'); scripts = @('test-release-gate.ps1', 'test-nightly.ps1') }
$script:C599ABasePolicy = $null
$script:C599PolicyClones = [System.Collections.Generic.List[string]]::new()
# The release card bound to the intent (the prepublication receipt's recipient) and
# the injected publisher clock: after every fixture instant, never the wall clock.
$script:C599ReleaseCardId = '5b0e6c1e-8f1c-4c3e-9a55-0c5990c5990a'
$script:C599Now = [datetime]::Parse('2026-09-23T10:00:00Z', [System.Globalization.CultureInfo]::InvariantCulture,
    ([System.Globalization.DateTimeStyles]::AdjustToUniversal -bor [System.Globalization.DateTimeStyles]::AssumeUniversal))

function Invoke-C599RealGit {
    param([string]$Repo, [string[]]$Arguments)
    $r = [Antiphon.Nightly.ReleaseAuthority]::Git($Repo, [string[]]$Arguments)
    if ([int]$r.ExitCode -ne 0) { throw ('fixture git {0} failed: {1}' -f ($Arguments -join ' '), $r.Error) }
    return $r.Text.Trim()
}

function New-C599PolicyCheckout {
    <#
      The owned release clone as a REAL git repository: the policy is committed and
      its commit SHA is the candidate SHA. PolicyBytes defaults to the live policy.
    #>
    param([string]$Root, [byte[]]$PolicyBytes = $null, [string]$Repository = 'michal-ciechan/Antiphon')
    $clone = Join-Path $Root 'checkout'
    New-Item -ItemType Directory -Path (Join-Path $clone 'tests') -Force | Out-Null
    $script:C599PolicyClones.Add($clone)
    if ($null -eq $PolicyBytes) { $PolicyBytes = [System.IO.File]::ReadAllBytes($script:C599RepoPolicyPath) }
    [System.IO.File]::WriteAllBytes((Join-Path $clone (Join-Path 'tests' 'test-execution-policy.json')), $PolicyBytes)
    [void](Invoke-C599RealGit -Repo $clone -Arguments @('init', '-q'))
    [void](Invoke-C599RealGit -Repo $clone -Arguments @('config', 'core.autocrlf', 'false'))
    [void](Invoke-C599RealGit -Repo $clone -Arguments @('remote', 'add', 'origin', ('https://github.com/' + $Repository)))
    [void](Invoke-C599RealGit -Repo $clone -Arguments @('add', '--', 'tests/test-execution-policy.json'))
    [void](Invoke-C599RealGit -Repo $clone -Arguments @('-c', 'user.name=c599', '-c', 'user.email=c599@example.invalid', 'commit', '-q', '-m', 'policy'))
    $sha = Invoke-C599RealGit -Repo $clone -Arguments @('rev-parse', 'HEAD')
    Write-NightlyAtomicJson -Path (Join-Path $clone '.antiphon-release-owned') -Object ([ordered]@{ kind = 'release-clone' })
    return [pscustomobject]@{ Path = $clone; Sha = $sha }
}

function Get-C599Discovery {
    # Two classes per native suite (antiphon three chunks) plus one pinned-excluded node.
    $d = @{}
    foreach ($suite in $script:C599NativeSuites) {
        $tag = ($suite -replace '[^a-z]', '')
        $nodes = @(
            @{ uid = ('{0}-u1' -f $suite); class = ('{0}AlphaTests' -f $tag); method = 'One' },
            @{ uid = ('{0}-u2' -f $suite); class = ('{0}AlphaTests' -f $tag); method = 'Two' },
            @{ uid = ('{0}-u3' -f $suite); class = ('{0}BetaTests' -f $tag); method = 'Three(1)' }
        )
        if ($suite -eq 'antiphon') {
            $nodes += @{ uid = 'antiphon-u4'; class = 'antiphonGammaTests'; method = 'Four' }
            $nodes += @{ uid = 'antiphon-x1'; class = 'ClaudeAdapterIntegrationTests'; method = 'Live' }
        }
        if ($suite -eq 'e2e') { $nodes += @{ uid = 'e2e-x1'; class = 'DelegationPipelineE2ETests'; method = 'Live' } }
        $d[$suite] = $nodes
    }
    return $d
}

function Update-C599Json {
    param([string]$Path, [scriptblock]$Mutate)
    $obj = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    & $Mutate $obj
    Write-NightlyAtomicJson -Path $Path -Object $obj
}

function New-C599Authority {
    <#
      Freeze a complete valid authority + plan + ledger for the candidate through
      the production functions, then lay down one real TRX per frozen chunk.
    #>
    param([string]$ReleaseRoot, [string]$Checkout, [string]$Sha, [string]$Repository = 'michal-ciechan/Antiphon')
    $root = [Antiphon.Nightly.ReleaseGateCredit]::CandidateRoot($ReleaseRoot, $CandidateId)
    $auth = [Antiphon.Nightly.ReleaseAuthority]::Freeze($Checkout, $root, $Sha, $Repository, $CandidateId, $CandidateRef, $script:C599IntentId, $script:C599ReleaseCardId,
        ([datetime]'2026-09-23T08:30:30Z'), $here)
    $discovery = Get-C599Discovery
    $plan = [Antiphon.Nightly.ReleaseAuthority]::Plan($root, $discovery, $script:C599Rosters)
    $dir = [Antiphon.Nightly.ReleaseAuthority]::Dir($root)
    $byUid = @{}
    foreach ($suite in $discovery.Keys) { foreach ($n in $discovery[$suite]) { $byUid[[string]$n.uid] = $n } }
    $chunks = @()
    $minute = 0
    foreach ($suite in @($plan.suites)) {
        if ([string]$suite.kind -ne 'native') { continue }
        foreach ($c in @($suite.chunks)) {
            $minute++
            $rel = ('evidence/{0}.trx' -f [string]$c.id)
            $trx = Join-Path $root $rel
            $rows = @($c.uids | ForEach-Object { $n = $byUid[[string]$_]; [pscustomobject]@{ Id = [string]$_; ClassName = [string]$n.class; MethodName = [string]$n.method; Outcome = 'Passed' } })
            Write-C487Trx -Path $trx -Rows $rows
            $start = ([datetime]'2026-09-23T08:31:00Z').AddMinutes($minute)
            $chunks += [ordered]@{
                suite = [string]$suite.id; chunk = [string]$c.id
                intentId = $script:C599IntentId; candidateId = $CandidateId; sha = $Sha; runId = $script:C599RunId
                exitCode = 0; startedAt = $start.ToString('o'); completedAt = $start.AddSeconds(30).ToString('o')
                trx = $rel; trxSha256 = (Get-NightlyFileSha256 -Path $trx)
                executed = $rows.Count; passed = $rows.Count; failed = 0; skipped = 0
            }
        }
    }
    $rosters = @()
    foreach ($id in @('client', 'scripts')) {
        $rosters += [ordered]@{
            suite = $id; intentId = $script:C599IntentId; candidateId = $CandidateId; sha = $Sha; runId = $script:C599RunId
            exitCode = 0; startedAt = '2026-09-23T09:30:00Z'; completedAt = '2026-09-23T09:35:00Z'
            entries = @($script:C599Rosters[$id] | ForEach-Object { [ordered]@{ id = $_; outcome = 'passed' } })
        }
    }
    Write-NightlyAtomicJson -Path (Join-Path $dir 'ledger.json') -Object ([ordered]@{
        schemaVersion = 1
        intentId = $script:C599IntentId; candidateId = $CandidateId; candidateRef = $CandidateRef; sha = $Sha; runId = $script:C599RunId
        authorityDigest = (Get-NightlyFileSha256 -Path (Join-Path $dir 'authority.json'))
        executionPlanDigest = (Get-NightlyFileSha256 -Path (Join-Path $dir 'execution-plan.json'))
        startedAt = '2026-09-23T08:31:00Z'; completedAt = '2026-09-23T09:50:00Z'
        teardownSucceeded = $true; seamed = $false; noReport = $false; diagnostic = $false; selection = 'full'
        prepublicationReceipt = [ordered]@{
            kind = 'prepublication'
            recipient = [ordered]@{ kind = 'release-card'; cardId = $script:C599ReleaseCardId; revision = 4 }
            correlation = [ordered]@{ intentId = $script:C599IntentId; candidateId = $CandidateId; sha = $Sha; runId = $script:C599RunId }
            acceptedAt = '2026-09-23T09:52:00Z'
        }
        buildHash = 'build-1'; bundleHash = 'bundle-1'
        prerequisites = @(
            [ordered]@{ id = 'build'; exitCode = 0; succeeded = $true },
            [ordered]@{ id = 'client-bundle'; exitCode = 0; succeeded = $true },
            [ordered]@{ id = 'script-census'; exitCode = 0; succeeded = $true })
        chunks = $chunks
        rosters = $rosters
    })
    return $auth
}


function New-C599GitHubSeams {
    <#
      Stateful GitHub fixture: write acceptance is separate from stored state, and
      a GET/readback is the only thing that proves publication.
    #>
    param([string]$Root, [string]$GitSeamsText, [hashtable]$Fail = @{})
    $store = Join-Path $Root 'github.json'
    Write-NightlyAtomicJson -Path $store -Object ([ordered]@{ releases = @() })
    $failPath = Join-Path $Root 'github-fail.json'
    $failState = [ordered]@{}
    foreach ($k in $Fail.Keys) { $failState[$k] = $Fail[$k] }
    Write-NightlyAtomicJson -Path $failPath -Object $failState
    $seams = Join-Path $Root 'seams-gh.ps1'
    $text = $GitSeamsText + @"

`$ReleaseGateSeams.GitHub = {
    param(`$Verb, `$Arguments, `$WorkingDirectory)
    `$store = '$store'
    `$failPath = '$failPath'
    `$preFlag = ''
    if (`$Arguments -and `$Arguments.ContainsKey('prerelease') -and [bool]`$Arguments['prerelease']) { `$preFlag = ' --prerelease' }
    Add-Content -LiteralPath (Join-Path (Split-Path -Parent `$store) 'gh-trace.log') -Value ('GH ' + `$Verb + ' ' + [string]`$Arguments.tag + `$preFlag) -Encoding ASCII
    `$fail = Get-Content -LiteralPath `$failPath -Raw | ConvertFrom-Json
    `$db = Get-Content -LiteralPath `$store -Raw | ConvertFrom-Json
    `$rows = @()
    foreach (`$r in @(`$db.releases)) { `$rows += `$r }
    `$tag = [string]`$Arguments.tag
    `$key = `$Verb
    if (`$Verb -eq 'release-upload') { `$key = 'release-upload:' + [System.IO.Path]::GetFileName([string]`$Arguments.asset) }
    `$shouldFail = (`$fail.PSObject.Properties.Name -contains `$key -and [bool]`$fail.`$key)
    `$commits = (`$fail.PSObject.Properties.Name -contains (`$key + '_commits') -and [bool]`$fail.(`$key + '_commits'))
    function Save(`$rows) {
        @{ releases = `$rows } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath `$store -Encoding UTF8
    }
    if (`$Verb -eq 'release-view') {
        if (`$shouldFail) { return @{ ExitCode = 1; Body = `$null } }
        foreach (`$r in `$rows) {
            if ([string]`$r.tagName -eq `$tag) {
                return @{ ExitCode = 0; Body = `$r }
            }
        }
        return @{ ExitCode = 1; Body = `$null }
    }
    if (`$Verb -eq 'release-create') {
        if (`$shouldFail -and -not `$commits) { return @{ ExitCode = 1; Body = `$null } }
        `$exists = `$false
        foreach (`$r in `$rows) { if ([string]`$r.tagName -eq `$tag) { `$exists = `$true } }
        if (-not `$exists) {
            `$rows += [pscustomobject]@{ id = ('R-' + `$tag); name = `$tag; tagName = `$tag; url = ('https://example/releases/tag/' + `$tag); isDraft = `$true; assets = @() }
            Save `$rows
        }
        if (`$shouldFail) { return @{ ExitCode = 1; Body = `$null } }
        return @{ ExitCode = 0; Body = `$null }
    }
    if (`$Verb -eq 'release-upload') {
        if (`$shouldFail -and -not `$commits) { return @{ ExitCode = 1; Body = `$null } }
        foreach (`$r in `$rows) {
            if ([string]`$r.tagName -eq `$tag) {
                `$assets = @()
                foreach (`$a in @(`$r.assets)) { `$assets += `$a }
                `$assets += [System.IO.Path]::GetFileName([string]`$Arguments.asset)
                `$r | Add-Member -NotePropertyName assets -NotePropertyValue `$assets -Force
            }
        }
        Save `$rows
        if (`$shouldFail) { return @{ ExitCode = 1; Body = `$null } }
        return @{ ExitCode = 0; Body = `$null }
    }
    if (`$Verb -eq 'release-publish') {
        if (`$shouldFail -and -not `$commits) { return @{ ExitCode = 1; Body = `$null } }
        foreach (`$r in `$rows) {
            if ([string]`$r.tagName -eq `$tag) { `$r | Add-Member -NotePropertyName isDraft -NotePropertyValue `$false -Force }
        }
        Save `$rows
        if (`$shouldFail) { return @{ ExitCode = 1; Body = `$null } }
        return @{ ExitCode = 0; Body = `$null }
    }
    throw ('unknown verb ' + `$Verb)
}
"@
    Set-Content -LiteralPath $seams -Value $text -Encoding ASCII
    return [pscustomobject]@{ Path = $seams; StorePath = $store; FailPath = $failPath }
}

# ------------------------------------------------------------ fake card API ---
# D-14 readback: the publisher GETs the release card through the Antiphon API over
# real loopback HTTP. One owned stub per harness child answers
# GET /fx/<fixture>/api/cards/<id> from that fixture's card-api.json, so each
# fixture's card is its own recipient state; every request line is logged for the
# read-only proof. The store is { status; raw; cards = { <requested id> = card } }:
# a non-200 status or a raw body replaces the card answer.

$script:C599CardStub = $null

function Get-C599CardStub {
    if ($null -ne $script:C599CardStub) { return $script:C599CardStub }
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $port = ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
    $ps = [powershell]::Create()
    [void]$ps.AddScript({
        param($listener, $root)
        while ($true) {
            try { $client = $listener.AcceptTcpClient() } catch { break }
            try {
                $stream = $client.GetStream()
                $reader = [System.IO.StreamReader]::new($stream, [System.Text.Encoding]::ASCII)
                $lines = @()
                while ($true) { $line = $reader.ReadLine(); if ([string]::IsNullOrEmpty($line)) { break }; $lines += $line }
                if ($lines.Count -eq 0) { continue }
                $parts = $lines[0] -split ' '
                $status = 404
                $body = '{}'
                $m = [regex]::Match([string]$parts[1], '^/fx/(?<fx>[0-9a-f]{32})/api/cards/(?<id>[^/?]+)$')
                if ($m.Success) {
                    $dir = Join-Path $root $m.Groups['fx'].Value
                    [System.IO.File]::AppendAllText((Join-Path $dir 'card-api.requests'), ($lines[0] + "`n"))
                    $store = Get-Content -LiteralPath (Join-Path $dir 'card-api.json') -Raw | ConvertFrom-Json
                    $id = [uri]::UnescapeDataString($m.Groups['id'].Value)
                    if ($parts[0] -cne 'GET') { $status = 405 }
                    elseif ([int]$store.status -ne 200) { $status = [int]$store.status }
                    elseif ($null -ne $store.raw) { $status = 200; $body = [string]$store.raw }
                    elseif ($store.cards.PSObject.Properties.Name -contains $id) { $status = 200; $body = ($store.cards.$id | ConvertTo-Json -Depth 8 -Compress) }
                }
                $reason = $(if ($status -eq 200) { 'OK' } elseif ($status -eq 404) { 'Not Found' } else { 'Error' })
                $bytes = [System.Text.Encoding]::UTF8.GetBytes($body)
                $head = "HTTP/1.1 $status $reason`r`nContent-Type: application/json; charset=utf-8`r`nContent-Length: $($bytes.Length)`r`nConnection: close`r`n`r`n"
                $h = [System.Text.Encoding]::ASCII.GetBytes($head)
                $stream.Write($h, 0, $h.Length)
                $stream.Write($bytes, 0, $bytes.Length)
                $stream.Flush()
            } catch {
            } finally { $client.Close() }
        }
    }).AddArgument($listener).AddArgument($ResultsDirectory)
    $handle = $ps.BeginInvoke()
    $script:C599CardStub = [pscustomobject]@{ Port = $port; Shell = $ps; Handle = $handle; Listener = $listener }
    return $script:C599CardStub
}

function Stop-C599CardStub {
    if ($null -eq $script:C599CardStub) { return }
    $stub = $script:C599CardStub
    $script:C599CardStub = $null
    $stub.Listener.Stop()
    try { [void]$stub.Shell.EndInvoke($stub.Handle) } catch { }
    $stub.Shell.Dispose()
}

function Get-C599CardBody {
    # The release card body carrying this run's correlation line (D-14 readback).
    param([string]$IntentId = $script:C599IntentId, [string]$Candidate = $CandidateId, [string]$Sha, [string]$RunId = $script:C599RunId)
    return ("Release candidate {0}`n`nrelease-correlation: intentId={1} candidateId={0} sha={2} runId={3}`n`nTests: all eight suites passed." -f $Candidate, $IntentId, $Sha, $RunId)
}

function New-C599CardApi {
    # The fixture's release card as the API serves it: revision 4 (the receipt's) and
    # a body correlated to this intent, candidate, SHA and native run.
    param([string]$Root, [string]$Sha, [string]$Candidate = '')
    if ([string]::IsNullOrWhiteSpace($Candidate)) { $Candidate = $script:CandidateId }
    $store = Join-Path $Root 'card-api.json'
    $card = [ordered]@{
        id = $script:C599ReleaseCardId; identifier = 'CARD-0601'; title = ('Release {0}' -f $Candidate)
        description = (Get-C599CardBody -Candidate $Candidate -Sha $Sha); revisionCount = 4; status = 'Review'
        concurrencyToken = '0b0e6c1e-8f1c-4c3e-9a55-0c5990c59900'
    }
    Write-NightlyAtomicJson -Path $store -Object ([ordered]@{ status = 200; raw = $null; cards = [ordered]@{ ($script:C599ReleaseCardId) = $card } })
    [System.IO.File]::WriteAllText((Join-Path $Root 'card-api.requests'), '')
    $stub = Get-C599CardStub
    return [pscustomobject]@{
        Url = ('http://127.0.0.1:{0}/fx/{1}' -f $stub.Port, (Split-Path -Leaf $Root))
        Store = $store
        Requests = (Join-Path $Root 'card-api.requests')
    }
}

# =============================================================== V-1 policy ===

function New-C599Nodes {
    param([switch]$WithUnclassified)
    $nodes = @(
        [pscustomobject]@{ Uid = 'u-live-1'; ClassName = 'E2E.LiveCanaryTests'; Type = 'LiveCanaryTests'; Method = 'Runs_live'; Categories = @('OptIn') },
        [pscustomobject]@{ Uid = 'u-optin-1'; ClassName = 'E2E.BoardE2ETests'; Type = 'BoardE2ETests'; Method = 'Board_loads'; Categories = @('OptIn') },
        [pscustomobject]@{ Uid = 'u-optin-2'; ClassName = 'E2E.BoardE2ETests'; Type = 'BoardE2ETests'; Method = 'Board_filters'; Categories = @('OptIn') },
        [pscustomobject]@{ Uid = 'u-plain-1'; ClassName = 'E2E.SmokeE2ETests'; Type = 'SmokeE2ETests'; Method = 'Home_renders'; Categories = @() },
        [pscustomobject]@{ Uid = 'u-mixed-live'; ClassName = 'A.MixedTests'; Type = 'MixedTests'; Method = 'Live_row'; Categories = @() },
        [pscustomobject]@{ Uid = 'u-mixed-offline'; ClassName = 'A.MixedTests'; Type = 'MixedTests'; Method = 'Offline_row'; Categories = @() }
    )
    if ($WithUnclassified) {
        $nodes += [pscustomobject]@{ Uid = 'u-brand-new'; ClassName = 'E2E.BrandNewTests'; Type = 'BrandNewTests'; Method = 'Never_seen'; Categories = @('OptIn') }
    }
    return $nodes
}

function Test-C599_MetadataParsers {
    $root = New-C599Root
    $policy = [Antiphon.Nightly.NightlyPolicy]::Read($repoRoot, (New-C599Policy -Root $root))
    $exclusions = @([Antiphon.Nightly.NightlyPolicy]::ProfileExclusions($policy.Object, 'rc', '', 'windows'))

    # JSON discovery document and MTP diagnostic records must agree on UIDs and
    # must both PRESERVE the raw category rather than consuming it.
    $jsonNodes = @(
        @{ uid = 'u1'; type = 'BoardE2ETests'; method = 'Board_loads'; namespace = 'E2E'; categories = @('OptIn'); state = 'Discovered' },
        @{ uid = 'u2'; type = 'SmokeE2ETests'; method = 'Home_renders'; namespace = 'E2E'; categories = @(); state = 'Discovered' }
    )
    $discPath = Join-Path $root 'discovery.json'
    Write-C487Discovery -Path $discPath -Nodes $jsonNodes
    $doc = [Antiphon.Nightly.NightlyDiagnosticLog]::ReadDiscovery($discPath)
    Assert-C487 -Cond (@($doc.Nodes).Count -eq 2) -Name 'C599 MetadataParsers json parser reads both nodes' -Detail ([string]@($doc.Nodes).Count)
    $optin = @($doc.Nodes | Where-Object { $_.Uid -eq 'u1' })[0]
    Assert-C487 -Cond ($optin.Categories -contains 'OptIn') -Name 'C599 MetadataParsers json parser preserves the raw OptIn category'
    Assert-C487 -Cond ([bool]$optin.Excluded) -Name 'C599 MetadataParsers json OptIn still excluded in the default lane'

    $rec = 'TestNodeUid { Value = u1 }, TypeName = BoardE2ETests, MethodName = Board_loads, Namespace = E2E, TestMetadataProperty { Key = OptIn }'
    $diag = [Antiphon.Nightly.NightlyDiagnosticLog]::Record($rec, 'Discovered', $null)
    Assert-C487 -Cond ([string]$diag.Uid -eq 'u1') -Name 'C599 MetadataParsers diagnostic parser reads the same uid' -Detail $diag.Uid
    Assert-C487 -Cond ($diag.Categories -contains 'OptIn') -Name 'C599 MetadataParsers diagnostic parser preserves the raw category'
    Assert-C487 -Cond ([string]$diag.ClassName -eq [string]$optin.ClassName) -Name 'C599 MetadataParsers both parsers agree on the class identity' -Detail ($diag.ClassName + ' vs ' + $optin.ClassName)

    # Profile-aware: the SAME nodes now resolve by declared rows, not by category.
    foreach ($n in @($doc.Nodes)) {
        $defaultExcluded = [Antiphon.Nightly.NightlyCensus]::Excluded($n, $null, $false)
        $profileExcluded = [Antiphon.Nightly.NightlyCensus]::Excluded($n, $exclusions, $true)
        if ([string]$n.Uid -eq 'u1') {
            Assert-C487 -Cond ($defaultExcluded -and (-not $profileExcluded)) -Name 'C599 MetadataParsers OptIn excluded by default but required in a profile' -Detail ('default=' + $defaultExcluded + ' profile=' + $profileExcluded)
        } else {
            Assert-C487 -Cond ((-not $defaultExcluded) -and (-not $profileExcluded)) -Name 'C599 MetadataParsers plain node required in both lanes'
        }
    }

    $jsonUids = @([Antiphon.Nightly.NightlyCensus]::RequiredUids(@($doc.Nodes), @(), $exclusions, $true))
    $diagUids = @([Antiphon.Nightly.NightlyCensus]::RequiredUids(@($diag, @($doc.Nodes)[1]), @(), $exclusions, $true))
    Assert-C487 -Cond ((($jsonUids | Sort-Object) -join ',') -eq (($diagUids | Sort-Object) -join ',')) -Name 'C599 MetadataParsers json and diagnostic required uid sets are identical' -Detail (($jsonUids -join ',') + ' vs ' + ($diagUids -join ','))

    # A truncated record is an error, never a silently dropped row.
    $truncated = ''
    try { [void]([Antiphon.Nightly.NightlyDiagnosticLog]::Record('TestNodeUid { Value = u9 }', 'Discovered', $null)) } catch { $truncated = (Get-NightlyRefusalText $_) }
    Assert-C487 -Cond ($truncated -match 'truncated') -Name 'C599 MetadataParsers truncated diagnostic record throws' -Detail $truncated

    $dupPath = Join-Path $root 'dup.json'
    Write-C487Discovery -Path $dupPath -Nodes @($jsonNodes[0], $jsonNodes[0])
    $dupMsg = ''
    try { [void]([Antiphon.Nightly.NightlyDiagnosticLog]::ReadDiscovery($dupPath)) } catch { $dupMsg = (Get-NightlyRefusalText $_) }
    Assert-C487 -Cond ($dupMsg -match 'duplicate discovery uid') -Name 'C599 MetadataParsers duplicate uid throws' -Detail $dupMsg
}

function Test-C599_EligibilityCensus {
    $root = New-C599Root
    $policy = [Antiphon.Nightly.NightlyPolicy]::Read($repoRoot, (New-C599Policy -Root $root))
    $exclusions = @([Antiphon.Nightly.NightlyPolicy]::ProfileExclusions($policy.Object, 'rc', '', 'windows'))
    $nodes = New-C599Nodes

    $census = [Antiphon.Nightly.NightlyCensus]::Census($nodes, $exclusions, $true)
    Assert-C487 -Cond ($census.Ok) -Name 'C599 EligibilityCensus profile census is well formed' -Detail ($census.Duplicates -join ',')
    Assert-C487 -Cond (($census.RequiredCount + $census.ExcludedCount) -eq $nodes.Count) -Name 'C599 EligibilityCensus every uid has exactly one disposition' -Detail ('req=' + $census.RequiredCount + ' exc=' + $census.ExcludedCount + ' total=' + $nodes.Count)

    $required = @($census.Required | ForEach-Object { [string]$_.uid })
    $excluded = @($census.Excluded | ForEach-Object { [string]$_.uid })
    Assert-C487 -Cond ($excluded -contains 'u-live-1') -Name 'C599 EligibilityCensus named live class is excluded' -Detail ($excluded -join ',')
    Assert-C487 -Cond ($excluded -contains 'u-mixed-live') -Name 'C599 EligibilityCensus named live METHOD is excluded' -Detail ($excluded -join ',')
    Assert-C487 -Cond ($required -contains 'u-mixed-offline') -Name 'C599 EligibilityCensus sibling method of a mixed class stays required' -Detail ($required -join ',')
    Assert-C487 -Cond (($required -contains 'u-optin-1') -and ($required -contains 'u-optin-2')) -Name 'C599 EligibilityCensus OptIn alone does not exclude in the rc profile' -Detail ($required -join ',')

    foreach ($row in @($census.Excluded)) {
        Assert-C487 -Cond ((-not [string]::IsNullOrWhiteSpace([string]$row.reason)) -and (-not [string]::IsNullOrWhiteSpace([string]$row.owner))) -Name ('C599 EligibilityCensus exclusion {0} records reason and owner' -f $row.uid) -Detail ([string]$row.reason + '/' + [string]$row.owner)
    }

    # A brand new unclassified case must stay required, not inherit manual status.
    $withNew = New-C599Nodes -WithUnclassified
    $census2 = [Antiphon.Nightly.NightlyCensus]::Census($withNew, $exclusions, $true)
    $required2 = @($census2.Required | ForEach-Object { [string]$_.uid })
    Assert-C487 -Cond ($required2 -contains 'u-brand-new') -Name 'C599 EligibilityCensus a new unclassified case stays required' -Detail ($required2 -join ',')
    Assert-C487 -Cond ($census2.Digest -ne $census.Digest) -Name 'C599 EligibilityCensus a new case changes the census digest' -Detail $census2.Digest

    # The default nightly lane is unchanged by any of this.
    $defaultCensus = [Antiphon.Nightly.NightlyCensus]::Census($nodes, $null, $false)
    $defaultRequired = @($defaultCensus.Required | ForEach-Object { [string]$_.uid })
    Assert-C487 -Cond ($defaultRequired -notcontains 'u-optin-1') -Name 'C599 EligibilityCensus default lane still excludes OptIn' -Detail ($defaultRequired -join ',')
    Assert-C487 -Cond ($defaultRequired -contains 'u-mixed-live') -Name 'C599 EligibilityCensus default lane ignores profile method rows' -Detail ($defaultRequired -join ',')

    $dupCensus = [Antiphon.Nightly.NightlyCensus]::Census(@($nodes[0], $nodes[0]), $exclusions, $true)
    Assert-C487 -Cond ($dupCensus.Duplicates.Count -eq 1) -Name 'C599 EligibilityCensus duplicate uid is reported' -Detail ($dupCensus.Duplicates -join ',')

    $allExcluded = [Antiphon.Nightly.NightlyCensus]::Census(@($nodes[0]), $exclusions, $true)
    Assert-C487 -Cond ((-not $allExcluded.Ok) -and $allExcluded.RequiredCount -eq 0) -Name 'C599 EligibilityCensus zero required uids is not ok' -Detail ([string]$allExcluded.RequiredCount)
    Assert-C487 -Cond ($census.Digest -eq ([Antiphon.Nightly.NightlyCensus]::Census($nodes, $exclusions, $true)).Digest) -Name 'C599 EligibilityCensus digest is stable for the same roster' -Detail $census.Digest
}

function Test-C599_ExpandedCoverage {
    $root = New-C599Root
    $policy = [Antiphon.Nightly.NightlyPolicy]::Read($repoRoot, (New-C599Policy -Root $root))
    $exclusions = @([Antiphon.Nightly.NightlyPolicy]::ProfileExclusions($policy.Object, 'rc', '', 'windows'))

    $discNodes = @(
        @{ uid = 'e1'; type = 'BoardE2ETests'; method = 'Board_loads'; namespace = 'E2E'; categories = @('OptIn'); state = 'Discovered' },
        @{ uid = 'e2'; type = 'BoardE2ETests'; method = 'Board_filters'; namespace = 'E2E'; categories = @('OptIn'); state = 'Discovered' },
        @{ uid = 'e3'; type = 'LiveCanaryTests'; method = 'Runs_live'; namespace = 'E2E'; categories = @('OptIn'); state = 'Discovered' }
    )
    $trxRows = @(
        @{ Id = 'e1'; ClassName = 'E2E.BoardE2ETests'; MethodName = 'Board_loads'; Outcome = 'Passed' },
        @{ Id = 'e2'; ClassName = 'E2E.BoardE2ETests'; MethodName = 'Board_filters'; Outcome = 'Passed' }
    )
    $run = Join-Path $root 'run'
    New-Item -ItemType Directory -Path $run -Force | Out-Null
    $trx = Join-Path $run 'e2e.trx'
    $disc = Join-Path $run 'e2e.discovery.json'
    $diagDir = Join-Path $run 'exec'
    New-Item -ItemType Directory -Path $diagDir -Force | Out-Null
    Write-C487Trx -Path $trx -Rows $trxRows
    Write-C487Discovery -Path $disc -Nodes $discNodes
    $execNodes = @(
        @{ uid = 'e1'; state = 'Passed'; type = 'BoardE2ETests'; method = 'Board_loads'; className = 'E2E.BoardE2ETests' },
        @{ uid = 'e2'; state = 'Passed'; type = 'BoardE2ETests'; method = 'Board_filters'; className = 'E2E.BoardE2ETests' }
    )
    Write-C487Execution -Path (Join-Path $diagDir 'log.diag') -Nodes $execNodes
    $before = (Get-NightlyUtcNow).AddMinutes(-5)

    $verdict = [Antiphon.Nightly.NightlyChunkVerdict]::NativeSuiteVerdict($trx, $disc, $diagDir, $run, $before, 0, $ShaA, $ShaA, 'r', 'r', 'h', 'h', @(), $exclusions, $true, $null, '', $null)
    Assert-C487 -Cond ([bool]$verdict.coverageComplete) -Name 'C599 ExpandedCoverage control: both required rows executed is complete' -Detail (@($verdict.reasons) -join ';')
    Assert-C487 -Cond (($null -ne $verdict.census) -and [int]$verdict.census.RequiredCount -eq 2) -Name 'C599 ExpandedCoverage census reports 2 required uids' -Detail ([string]$verdict.census.RequiredCount)
    Assert-C487 -Cond ([int]$verdict.census.ExcludedCount -eq 1) -Name 'C599 ExpandedCoverage census reports the 1 named exclusion' -Detail ([string]$verdict.census.ExcludedCount)

    # Remove one expanded required row: incomplete.
    $run2 = Join-Path $root 'run2'
    New-Item -ItemType Directory -Path (Join-Path $run2 'exec') -Force | Out-Null
    Write-C487Trx -Path (Join-Path $run2 'e2e.trx') -Rows @($trxRows[0])
    Write-C487Discovery -Path (Join-Path $run2 'e2e.discovery.json') -Nodes $discNodes
    Write-C487Execution -Path (Join-Path $run2 (Join-Path 'exec' 'log.diag')) -Nodes @($execNodes[0])
    $missing = [Antiphon.Nightly.NightlyChunkVerdict]::NativeSuiteVerdict((Join-Path $run2 'e2e.trx'), (Join-Path $run2 'e2e.discovery.json'), (Join-Path $run2 'exec'), $run2, $before, 0, $ShaA, $ShaA, 'r', 'r', 'h', 'h', @(), $exclusions, $true, $null, '', $null)
    Assert-C487 -Cond (-not [bool]$missing.coverageComplete) -Name 'C599 ExpandedCoverage a removed expanded row is incomplete' -Detail (@($missing.reasons) -join ';')
    Assert-C487 -Cond ((@($missing.reasons) -join ';') -match 'missing-expanded-row') -Name 'C599 ExpandedCoverage names the missing expanded row' -Detail (@($missing.reasons) -join ';')

    # In the DEFAULT lane the same evidence would have zero required rows, which
    # is exactly the silent-green the profile lane must refuse.
    $defaultVerdict = [Antiphon.Nightly.NightlyChunkVerdict]::NativeSuiteVerdict($trx, $disc, $diagDir, $run, $before, 0, $ShaA, $ShaA, 'r', 'r', 'h', 'h', @(), $null, $false, $null, '', $null)
    Assert-C487 -Cond (($null -ne $defaultVerdict.census) -and [int]$defaultVerdict.census.RequiredCount -eq 0) -Name 'C599 ExpandedCoverage default lane would require zero uids here' -Detail ([string]$defaultVerdict.census.RequiredCount)

    # A profile lane whose every node is excluded is never green.
    $onlyLive = @(@{ uid = 'e3'; type = 'LiveCanaryTests'; method = 'Runs_live'; namespace = 'E2E'; categories = @('OptIn'); state = 'Discovered' })
    $run3 = Join-Path $root 'run3'
    New-Item -ItemType Directory -Path (Join-Path $run3 'exec') -Force | Out-Null
    Write-C487Trx -Path (Join-Path $run3 'e2e.trx') -Rows @(@{ Id = 'e3'; ClassName = 'E2E.LiveCanaryTests'; MethodName = 'Runs_live'; Outcome = 'Passed' })
    Write-C487Discovery -Path (Join-Path $run3 'e2e.discovery.json') -Nodes $onlyLive
    Write-C487Execution -Path (Join-Path $run3 (Join-Path 'exec' 'log.diag')) -Nodes @(@{ uid = 'e3'; state = 'Passed'; type = 'LiveCanaryTests'; method = 'Runs_live'; className = 'E2E.LiveCanaryTests' })
    $zero = [Antiphon.Nightly.NightlyChunkVerdict]::NativeSuiteVerdict((Join-Path $run3 'e2e.trx'), (Join-Path $run3 'e2e.discovery.json'), (Join-Path $run3 'exec'), $run3, $before, 0, $ShaA, $ShaA, 'r', 'r', 'h', 'h', @(), $exclusions, $true, $null, '', $null)
    Assert-C487 -Cond ((-not [bool]$zero.coverageComplete) -and (-not [bool]$zero.testsPassed)) -Name 'C599 ExpandedCoverage zero required uids is never green' -Detail (@($zero.reasons) -join ';')
    Assert-C487 -Cond ((@($zero.reasons) -join ';') -match 'zero-required-uid') -Name 'C599 ExpandedCoverage names zero-required-uid' -Detail (@($zero.reasons) -join ';')

    # Missing execution diagnostics is incomplete even with a green TRX.
    $noDiag = [Antiphon.Nightly.NightlyChunkVerdict]::NativeSuiteVerdict($trx, $disc, '', $run, $before, 0, $ShaA, $ShaA, 'r', 'r', 'h', 'h', @(), $exclusions, $true, $null, '', $null)
    Assert-C487 -Cond (-not [bool]$noDiag.coverageComplete) -Name 'C599 ExpandedCoverage missing execution diagnostics is incomplete' -Detail (@($noDiag.reasons) -join ';')
}

# ========================================================== V-2/V-3 run lane ===

function Test-C599_CreditIdentity {
    # credit kind x profile x trigger x ref, plus malformed/foreign identity rows.
    $profiles = @('nightly', 'rc')
    $triggers = @('scheduled', 'manual', 'rc')
    $refs = @('master', 'origin/master', 'feat/x', $CandidateRef)
    foreach ($p in $profiles) {
        foreach ($t in $triggers) {
            foreach ($r in $refs) {
                $kind = [Antiphon.Nightly.ReleaseGateCredit]::CreditKind($p, $t, $r)
                $expected = ''
                if ($p -eq 'nightly' -and $t -eq 'scheduled' -and ($r -eq 'master' -or $r -eq 'origin/master')) { $expected = 'master-scheduled' }
                if ($p -eq 'rc' -and $t -eq 'rc' -and $r -eq $CandidateRef) { $expected = 'rc-release' }
                Assert-C487 -Cond ([string]$kind -eq $expected) -Name ('C599 CreditIdentity {0}/{1}/{2} earns [{3}]' -f $p, $t, $r, $expected) -Detail ('got=' + $kind)
            }
        }
    }
    # Malformed and foreign identity rows earn nothing.
    foreach ($row in @(
        @('turbo', 'scheduled', 'master'),
        @('nightly', 'scheduled', 'refs/heads/master'),
        @('rc', 'rc', 'release/current'),
        @('rc', 'rc', 'release/rc-2026-09-23T083000Z'),
        @('rc', 'rc', 'release/rc-20261332T083000Z'),
        @('', 'rc', $CandidateRef),
        @('rc', '', $CandidateRef),
        @('nightly', 'feature', 'master')
    )) {
        $kind = [Antiphon.Nightly.ReleaseGateCredit]::CreditKind($row[0], $row[1], $row[2])
        Assert-C487 -Cond ([string]::IsNullOrWhiteSpace([string]$kind)) -Name ('C599 CreditIdentity malformed [{0}/{1}/{2}] earns nothing' -f $row[0], $row[1], $row[2]) -Detail ('got=' + $kind)
    }
    # An absent profile is the pre-CARD-0599 master lane, unchanged.
    $legacy = [Antiphon.Nightly.ReleaseGateCredit]::CreditKind('', 'scheduled', 'master')
    Assert-C487 -Cond ([string]$legacy -eq 'master-scheduled') -Name 'C599 CreditIdentity absent profile is still the master lane' -Detail $legacy
    # The refs/heads/ prefix is accepted for a candidate but not for master.
    $prefixed = [Antiphon.Nightly.ReleaseGateCredit]::CreditKind('rc', 'rc', ('refs/heads/' + $CandidateRef))
    Assert-C487 -Cond ([string]$prefixed -eq 'rc-release') -Name 'C599 CreditIdentity refs/heads candidate ref is accepted' -Detail $prefixed
}

function Test-C599_CreditVerdicts {
    # One invalid field at a time against a valid control, per credit kind.
    $master = New-C599State
    $v = [Antiphon.Nightly.ReleaseGateCredit]::CreditVerdict($master)
    Assert-C487 -Cond ([string]$v.CreditKind -eq 'master-scheduled') -Name 'C599 CreditVerdicts control master green earns master credit' -Detail (@($v.Reasons) -join ';')
    Assert-C487 -Cond ([Antiphon.Nightly.NightlyRunParts]::CompleteGreenPredicate($master, 'master-scheduled')) -Name 'C599 CreditVerdicts predicate default asks the master question'
    Assert-C487 -Cond (-not ([Antiphon.Nightly.NightlyRunParts]::CompleteGreenPredicate($master, 'rc-release'))) -Name 'C599 CreditVerdicts master green never earns release credit'

    $rc = New-C599State -Profile 'rc' -Trigger 'rc' -Ref $CandidateRef -Sha $ShaB -ExpectedSha $ShaB -Candidate $CandidateId
    $vr = [Antiphon.Nightly.ReleaseGateCredit]::CreditVerdict($rc)
    Assert-C487 -Cond ([string]$vr.CreditKind -eq 'rc-release') -Name 'C599 CreditVerdicts control rc green earns release credit' -Detail (@($vr.Reasons) -join ';')
    Assert-C487 -Cond (-not ([Antiphon.Nightly.NightlyRunParts]::CompleteGreenPredicate($rc, 'master-scheduled'))) -Name 'C599 CreditVerdicts rc green never earns master credit'

    foreach ($field in @('coverageComplete', 'testsPassed', 'reportDelivered')) {
        $bad = New-C599State
        $bad.$field = $false
        Assert-C487 -Cond ([string]([Antiphon.Nightly.ReleaseGateCredit]::CreditVerdict($bad)).CreditKind -eq '') -Name ('C599 CreditVerdicts {0} false earns nothing' -f $field)
        $str = New-C599State
        $str.$field = 'true'
        $sv = [Antiphon.Nightly.ReleaseGateCredit]::CreditVerdict($str)
        Assert-C487 -Cond ([string]$sv.CreditKind -eq '') -Name ('C599 CreditVerdicts {0} as the STRING "true" earns nothing' -f $field) -Detail (@($sv.Reasons) -join ';')
        Assert-C487 -Cond ((@($sv.Reasons) -join ';') -match ('not-true-boolean:' + $field)) -Name ('C599 CreditVerdicts {0} string is named as not-true-boolean' -f $field) -Detail (@($sv.Reasons) -join ';')
    }

    $nonZero = New-C599State -ExitCode 1
    Assert-C487 -Cond ([string]([Antiphon.Nightly.ReleaseGateCredit]::CreditVerdict($nonZero)).CreditKind -eq '') -Name 'C599 CreditVerdicts non-zero exit earns nothing'
    $noExit = New-C599State
    $noExit.exitCode = $null
    Assert-C487 -Cond ([string]([Antiphon.Nightly.ReleaseGateCredit]::CreditVerdict($noExit)).CreditKind -eq '') -Name 'C599 CreditVerdicts missing exitCode earns nothing'
    $noRun = New-C599State -RunId ''
    Assert-C487 -Cond ([string]([Antiphon.Nightly.ReleaseGateCredit]::CreditVerdict($noRun)).CreditKind -eq '') -Name 'C599 CreditVerdicts missing runId earns nothing'
    $noPolicy = New-C599State -PolicyHash ''
    Assert-C487 -Cond ([string]([Antiphon.Nightly.ReleaseGateCredit]::CreditVerdict($noPolicy)).CreditKind -eq '') -Name 'C599 CreditVerdicts missing policyHash earns nothing'

    $shaMismatch = New-C599State -Profile 'rc' -Trigger 'rc' -Ref $CandidateRef -Sha $ShaB -ExpectedSha $ShaC -Candidate $CandidateId
    $sm = [Antiphon.Nightly.ReleaseGateCredit]::CreditVerdict($shaMismatch)
    Assert-C487 -Cond (([string]$sm.CreditKind -eq '') -and ((@($sm.Reasons) -join ';') -match 'rc-sha-mismatch')) -Name 'C599 CreditVerdicts rc sha mismatch earns nothing' -Detail (@($sm.Reasons) -join ';')
    $shortSha = New-C599State -Profile 'rc' -Trigger 'rc' -Ref $CandidateRef -Sha 'bbbbbbb' -ExpectedSha 'bbbbbbb' -Candidate $CandidateId
    Assert-C487 -Cond ([string]([Antiphon.Nightly.ReleaseGateCredit]::CreditVerdict($shortSha)).CreditKind -eq '') -Name 'C599 CreditVerdicts rc short sha earns nothing'
    $noCandidate = New-C599State -Profile 'rc' -Trigger 'rc' -Ref $CandidateRef -Sha $ShaB -ExpectedSha $ShaB -Candidate ''
    Assert-C487 -Cond ([string]([Antiphon.Nightly.ReleaseGateCredit]::CreditVerdict($noCandidate)).CreditKind -eq '') -Name 'C599 CreditVerdicts rc without a candidate id earns nothing'
    $noExpected = New-C599State -Profile 'rc' -Trigger 'rc' -Ref $CandidateRef -Sha $ShaB -ExpectedSha '' -Candidate $CandidateId
    Assert-C487 -Cond ([string]([Antiphon.Nightly.ReleaseGateCredit]::CreditVerdict($noExpected)).CreditKind -eq '') -Name 'C599 CreditVerdicts rc without a pinned expectedSha earns nothing'

    Assert-C487 -Cond (-not ([Antiphon.Nightly.NightlyRunParts]::CompleteGreenPredicate($master, 'invented-kind'))) -Name 'C599 CreditVerdicts an invented credit kind earns nothing'
    Assert-C487 -Cond (-not ([Antiphon.Nightly.NightlyRunParts]::CompleteGreenPredicate($null, 'master-scheduled'))) -Name 'C599 CreditVerdicts a null state earns nothing'
}

function Test-C599RunSummaryRecord {
    param([string]$Json)
    $record = $Json | ConvertFrom-Json -ErrorAction Stop
    return ($null -ne $record.PSObject.Properties['runId'])
}

function Test-C599_RunSummaryIdentity {
    # Pure strings: force Windows-shaped input without filesystem/native admission.
    $windows = '{"runId":"windows-fixture","clone":"C:\\Antiphon\\nightly\\checkout","logDir":"C:\\Antiphon\\nightly\\logs\\run"}'
    $timing = '{"version":1,"complete":true,"tests":[],"sharedFixtures":[]}'
    Assert-C487 -Cond (Test-C599RunSummaryRecord $windows) -Name 'C599 RunSummaryIdentity Windows path record is a run summary'
    Assert-C487 -Cond (-not (Test-C599RunSummaryRecord $timing)) -Name 'C599 RunSummaryIdentity timing record is not a run summary'
    $saved = $script:NightlySeams
    try {
        $script:NightlySeams = @{ HostPlatform = { return 'windows' } }
        $observed = [string]$script:NightlySeams.HostPlatform.Invoke()
        Assert-C487 -Cond (([Antiphon.Nightly.NightlyTestsPlan]::ExecutionPlatform($observed)) -ceq 'windows') -Name 'C599 RunSummaryIdentity Windows observation needs no native work'
    } finally { $script:NightlySeams = $saved }
}

function Test-C599_ReportLane {
    # R-4: an RC incident identity can never be the master nightly incident.
    # CARD-1340: private roots. The production Windows defaults stay in the producer scripts.
    $rcGreen = New-C599State -Profile 'rc' -Trigger 'rc' -Ref $CandidateRef -Sha $ShaB -ExpectedSha $ShaB -Candidate $CandidateId
    $masterGreen = New-C599State
    $root = New-C599Root
    $state = Join-Path $root 'state'
    $release = Join-Path $root 'releases'
    $rcPaths = [Antiphon.Nightly.ReleaseGateCredit]::StatePaths('rc-release', $state, $release, $CandidateId)
    $masterPaths = [Antiphon.Nightly.ReleaseGateCredit]::StatePaths('master-scheduled', $state, '', '')
    Assert-C487 -Cond ($rcPaths.GreenPath -ne $masterPaths.GreenPath) -Name 'C599 ReportLane the two lanes write different green files' -Detail ($rcPaths.GreenPath + ' vs ' + $masterPaths.GreenPath)
    Assert-C487 -Cond ($masterPaths.GreenPath -eq (Join-Path $state 'last-complete-green.json')) -Name 'C599 ReportLane master green path is unchanged' -Detail $masterPaths.GreenPath
    Assert-C487 -Cond ($rcPaths.GreenPath -eq (Join-Path (Join-Path (Join-Path $release 'candidates') $CandidateId) 'complete-green.json')) -Name 'C599 ReportLane rc green lands under its candidate root' -Detail $rcPaths.GreenPath
    $missing = ''
    try { [void]([Antiphon.Nightly.ReleaseGateCredit]::StatePaths('rc-release', 'x', '', '')) } catch { $missing = $_.Exception.Message }
    Assert-C487 -Cond ($missing -match 'candidate id') -Name 'C599 ReportLane rc credit without a candidate id throws' -Detail $missing
    Assert-C487 -Cond ([string]([Antiphon.Nightly.ReleaseGateCredit]::CreditVerdict($rcGreen)).CreditKind -ne [string]([Antiphon.Nightly.ReleaseGateCredit]::CreditVerdict($masterGreen)).CreditKind) -Name 'C599 ReportLane the two lanes never share a credit kind'
}

# ======================================================= V-6 publication lane ===

function New-C599PublishFx {
    <#
      CARD-0599 D-15: the release clone is a real git repository whose commit holds
      the policy, so the candidate SHA is real and the publisher re-reads the pinned
      blob itself. The remote/GitHub stay file-backed recipients behind the seams.
    #>
    param([byte[]]$PolicyBytes = $null, [switch]$NoAuthority, [switch]$LongLivedCandidate, [string]$Repository = 'michal-ciechan/Antiphon')
    $fixtureWatch = [Diagnostics.Stopwatch]::StartNew()
    $root = New-C599Root
    $co = New-C599PolicyCheckout -Root $root -PolicyBytes $PolicyBytes -Repository $Repository
    $Head = $co.Sha
    # Any assignment makes these locals for the whole function. Start from the
    # script stamp identity, then override for a long-lived fixture.
    $CandidateId = $script:CandidateId
    $CandidateRef = $script:CandidateRef
    if ($LongLivedCandidate) {
        # Fixture date only. Production scripts must not hard-code a line date.
        $CandidateRef = 'rc/2026-10-15'
        $CandidateId = 'rc-2026-10-15-' + $Head.Substring(0, 12)
    }
    $git = New-C599GitSeams -Root $root -HeadSha $Head
    $gitText = Get-Content -LiteralPath $git.Path -Raw
    $gh = New-C599GitHubSeams -Root $root -GitSeamsText $gitText
    $gh | Add-Member -NotePropertyName RemotePath -NotePropertyValue $git.RemotePath -Force
    $gh | Add-Member -NotePropertyName FailGitPath -NotePropertyValue $git.FailPath -Force
    $releaseRoot = Join-Path $root 'releases'
    $checkout = $co.Path
    $policyHash = ''
    if (-not $NoAuthority) {
        $auth = New-C599Authority -ReleaseRoot $releaseRoot -Checkout $checkout -Sha $Head -Repository $Repository
        $policyHash = [string]$auth.policyHash
    }
    if ($NoAuthority) { $policyHash = 'policy-1' }
    [void](New-C599RcGreen -ReleaseRoot $releaseRoot -Sha $Head -CandidateId $CandidateId -Ref $CandidateRef -PolicyHash $policyHash)
    # The publisher's clock is injected at the seam boundary, never the wall clock.
    Add-Content -LiteralPath $gh.Path -Encoding ASCII -Value ("`n`$ReleaseGateSeams.UtcNow = {{ return [datetime]::Parse('{0}', [System.Globalization.CultureInfo]::InvariantCulture, ([System.Globalization.DateTimeStyles]::AdjustToUniversal -bor [System.Globalization.DateTimeStyles]::AssumeUniversal)) }}" -f $script:C599Now.ToString('o'))
    # The remote holds the candidate at the tested sha.
    $remoteKey = (($CandidateRef -replace '^refs/heads/', '') -replace '[^A-Za-z0-9]', '_')
    $state = [ordered]@{ $remoteKey = $Head }
    Write-NightlyAtomicJson -Path $git.RemotePath -Object $state
    # The release card the receipt names, served by the fake Antiphon API.
    $cardApi = New-C599CardApi -Root $root -Sha $Head -Candidate $CandidateId
    $script:C1731Timing.fixtureSeconds += $fixtureWatch.Elapsed.TotalSeconds
    return [pscustomobject]@{
        Root = $root; Git = $git; Gh = $gh; ReleaseRoot = $releaseRoot; Checkout = $checkout; Head = $Head; CardApi = $cardApi
        CandidateId = $CandidateId; CandidateRef = $CandidateRef
    }
}

# ======================================================= V-4 e2e evidence ===

function Test-C599_Prerequisites {
    # W2-nightly S11: the prerequisite probe, the native classification and the generated playwright.ps1 install are
    # NightlyTestsPlan; their rows are ReleaseGateE2EEvidenceTests.C599_PrerequisiteProbe, in-process.
    $root = New-C599Root
    # The production selection path must treat a failed prerequisite as red, never as a skip.
    $policyPath = New-C599Policy -Root $root
    $policy = [Antiphon.Nightly.NightlyPolicy]::Read($repoRoot, $policyPath)
    $required = @([Antiphon.Nightly.NightlyPolicy]::ProfileRequiredSuites($policy.Object, 'rc'))
    Assert-C487 -Cond ($required -contains 'e2e') -Name 'C599 Prerequisites the rc profile requires e2e before any prerequisite check'
    # W2-nightly S13: the suite preparation that records it is Antiphon.Nightly/NightlySuiteRunner.cs.
    $runner = Get-Content -LiteralPath (Join-Path $lib 'Antiphon.Nightly/NightlySuiteRunner.cs') -Raw
    Assert-C487 -Cond ($runner -match 'e2e-prerequisite-failed') -Name 'C599 Prerequisites a failed prerequisite is a named red reason'
}

function Test-C599_EnvironmentIsolation {
    $policy = [Antiphon.Nightly.NightlyPolicy]::Read($repoRoot, '')
    $envMap = [Antiphon.Nightly.NightlyPolicy]::SafeChildEnvironment($policy.Object, 'e2e', @{
        ANTIPHON_HEADED_TESTS = '1'
        ANTIPHON_DISTILLER_APPLY_CANARY = '1'
        ANTIPHON_TG_TEST_TOKEN = 'live-token'
        KEEP_ME = 'yes'
    })
    foreach ($name in @('ANTIPHON_HEADED_TESTS', 'ANTIPHON_DISTILLER_APPLY_CANARY', 'ANTIPHON_TG_TEST_TOKEN')) {
        Assert-C487 -Cond ([string]$envMap[$name] -eq '') -Name ('C599 EnvironmentIsolation {0} is cleared for an e2e child' -f $name) -Detail ([string]$envMap[$name])
    }
    Assert-C487 -Cond ([string]$envMap['KEEP_ME'] -eq 'yes') -Name 'C599 EnvironmentIsolation an unrelated variable survives'
    Assert-C487 -Cond (-not $envMap.ContainsKey('ANTIPHON_BROKER_TESTS') -or [string]$envMap['ANTIPHON_BROKER_TESTS'] -ne '1') -Name 'C599 EnvironmentIsolation the broker opt-in is not set for e2e'
    $messagingEnv = [Antiphon.Nightly.NightlyPolicy]::SafeChildEnvironment($policy.Object, 'messaging', @{})
    Assert-C487 -Cond ([string]$messagingEnv['ANTIPHON_BROKER_TESTS'] -eq '1') -Name 'C599 EnvironmentIsolation the broker opt-in IS set for messaging'
}

# ========================== the C1339 real-git fixture's shared helpers (C599A, B1) ===
# The C599A matrices and the C599B contract run in process since unit 14
# (ReleaseGatePublicationAuthorityTests, ReleaseGateAuthorityContractTests). These
# helpers stay for the C1339 seed, the coordinator's C1339 rows and the C1733 cases.

function Assert-C599AMatrix {
    param([string]$Name, [object[]]$Results)
    $bad = @($Results | Where-Object { -not $_.Pass })
    Assert-C487 -Cond (@($Results).Count -gt 0 -and $bad.Count -eq 0) -Name $Name -Detail ((@($bad | ForEach-Object { $_.Detail })) -join ' | ')
}

function Get-C599ABasePolicyBytes {
    <#
      Explicit historical schema-2 policy for the eight-suite authority controls.
      Current-policy publication controls continue to read C599RepoPolicyPath.
      The fixture retains the two exclusions that name these fixture classes.
    #>
    if ($null -eq $script:C599ABasePolicy) {
        $script:C599ABasePolicy = [System.IO.File]::ReadAllBytes((Join-Path $here 'fixtures/c599-policy-v2.json'))
    }
    return $script:C599ABasePolicy
}

function New-C599AEvidenceLink {
    # A directory link inside the candidate root pointing outside it: a junction on
    # Windows (no privilege needed), a symbolic link elsewhere.
    param([string]$Link, [string]$Target)
    if ($IsWindows -or $env:OS -eq 'Windows_NT') { New-Item -ItemType Junction -Path $Link -Target $Target | Out-Null }
    else { New-Item -ItemType SymbolicLink -Path $Link -Target $Target | Out-Null }
}

function Test-C599_E2EWatchdog {
    # CARD-1345: the shipped policy resolves the rc e2e suite to 120 minutes.
    # The rc session-runner suite gets 30 minutes; antiphon and the fallback stay put.
    $ship = [Antiphon.Nightly.NightlyPolicy]::Read($repoRoot, '')
    $e2e = [Antiphon.Nightly.NightlyWatchdog]::PolicyWatchdogMilliseconds($ship.Object, 'e2e', 3600000)
    $antiphon = [Antiphon.Nightly.NightlyWatchdog]::PolicyWatchdogMilliseconds($ship.Object, 'antiphon', 3600000)
    $session = [Antiphon.Nightly.NightlyWatchdog]::PolicyWatchdogMilliseconds($ship.Object, 'session-runner', 3600000)
    $missing = [Antiphon.Nightly.NightlyWatchdog]::PolicyWatchdogMilliseconds($ship.Object, 'not-a-suite', 3600000)
    Assert-C487 -Cond ($e2e -eq 7200000) -Name 'C599 resolved e2e watchdog is 7200000' -Detail ([string]$e2e)
    Assert-C487 -Cond ($antiphon -eq 3600000) -Name 'C599 antiphon watchdog stays 3600000' -Detail ([string]$antiphon)
    Assert-C487 -Cond ($session -eq 1800000) -Name 'C599 resolved session-runner watchdog is 1800000' -Detail ([string]$session)
    Assert-C487 -Cond ($missing -eq 3600000) -Name 'C599 missing watchdog key keeps the 3600000 fallback' -Detail ([string]$missing)
}

# CARD-1339: real local recipients, with a confined transport adapter.
. (Join-Path $here 'fixtures/c1339-release-git.ps1')
. (Join-Path $here 'fixtures/c1339-fixture-state.ps1')

function Invoke-C1339LocalGit {
    param($Fx, [string[]]$Arguments, [switch]$Remote, [switch]$AllowAbsent)
    $wd = $Fx.Checkout
    if ($Remote) { $wd = $Fx.Bare }
    $r = Invoke-C1339GitProcess -Root $Fx.Root -WorkingDirectory $wd -Arguments $Arguments
    if ($r.ExitCode -ne 0 -and -not $AllowAbsent) { throw ('C1339 fixture git: ' + $r.Error) }
    return [string]$r.Output
}

function New-C1339FixtureSeed {
    param([switch]$Stamp, [switch]$NoCandidateRemote)
    # All fixture setup children inherit no task/API/provider credentials, hooks,
    # signing configuration or network protocols. Restore the harness environment.
    $saved = @{}
    foreach ($item in @(Get-ChildItem Env:)) {
        if ($item.Name -match '(?i)TOKEN|SECRET|PASSWORD|CREDENTIAL|ANTIPHON_API|CODEX|CLAUDE|OPENAI|GH_|GITHUB|GIT_|ASKPASS') {
            $saved[$item.Name] = $item.Value
            [Environment]::SetEnvironmentVariable($item.Name, $null)
        }
    }
    $env:GIT_CONFIG_NOSYSTEM = '1'; $env:GIT_ALLOW_PROTOCOL = 'file'
    $env:GIT_CONFIG_GLOBAL = Join-Path $ResultsDirectory 'empty-git-config'
    try { $fx = New-C599PublishFx -PolicyBytes (Get-C599ABasePolicyBytes) -LongLivedCandidate:(-not $Stamp) -Repository 'fixture/release' }
    finally {
        foreach ($name in @('GIT_CONFIG_NOSYSTEM', 'GIT_ALLOW_PROTOCOL', 'GIT_CONFIG_GLOBAL')) { [Environment]::SetEnvironmentVariable($name, $null) }
        foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
    }
    $bare = Join-Path $fx.Root 'remote.git'
    New-Item -ItemType Directory -Path $bare | Out-Null
    $fx | Add-Member Bare $bare
    $fx | Add-Member FaultPath (Join-Path $fx.Root 'real-git-fault.json')
    $fx | Add-Member PreTag 'v2026.10.15.1-rc.1'
    $fx | Add-Member FinalTag 'v2026.10.15.1'
    Add-Content -LiteralPath $fx.Gh.Path -Encoding ASCII -Value "`n`$ReleaseGateSeams.UtcNow = { return [datetime]'2026-10-15T12:00:00Z' }"
    [void](Invoke-C1339LocalGit $fx @('init', '--bare', '-q') -Remote)
    if (-not $NoCandidateRemote) { [void](Invoke-C1339LocalGit $fx @('push', $bare, ($fx.Head + ':refs/heads/' + $fx.CandidateRef))) }
    [void](Invoke-C1339LocalGit $fx @('commit', '--allow-empty', '-q', '-m', 'distinct B'))
    $fx | Add-Member B (Invoke-C1339LocalGit $fx @('rev-parse', 'HEAD'))
    [void](Invoke-C1339LocalGit $fx @('commit', '--allow-empty', '-q', '-m', 'distinct C'))
    $fx | Add-Member C (Invoke-C1339LocalGit $fx @('rev-parse', 'HEAD'))
    [void](Invoke-C1339LocalGit $fx @('push', $bare, ($fx.C + ':refs/heads/fixture-objects')))
    # Keep checkout at A for publication; other commits remain real and addressable.
    [void](Invoke-C1339LocalGit $fx @('checkout', '--detach', '-q', $fx.Head))
    if (-not $Stamp) {
        Update-C599Json -Path (Join-Path ([Antiphon.Nightly.ReleaseGateCredit]::CandidateRoot($fx.ReleaseRoot, $fx.CandidateId)) 'candidate.json') -Mutate { param($c) $c.cutUtc = '2026-10-15T00:00:00Z' }
    }
    $adapter = Join-Path $here 'fixtures/c1339-release-git.ps1'
    Add-Content -LiteralPath $fx.Gh.Path -Encoding ASCII -Value @"
. '$adapter'
`$ReleaseGateSeams.Git = {
    param(`$WorkingDirectory, `$Arguments)
    Invoke-C1339FixtureGit -Root '$($fx.Root)' -Checkout '$($fx.Checkout)' -Bare '$bare' -WorkingDirectory `$WorkingDirectory -Arguments `$Arguments -FaultPath '$($fx.FaultPath)'
}
"@
    return $fx
}

function Add-C1339Tag {
    param($Fx, [string]$Tag, [string]$Sha)
    # Seed the recipient independently. A nested local tag also makes Git's push
    # source DWIM ambiguous, which is unrelated to remote-read resolution.
    [void](Invoke-C1339LocalGit $Fx @('tag', '-a', $Tag, $Sha, '-m', 'fixture') -Remote)
}

function Set-C1339Read {
    param($Fx, [string]$Ref, [string]$Output = '', [string]$Mode = 'prepend', [int]$Ordinal = 0, [int]$Exit = 0, [string]$Push = '')
    Write-NightlyAtomicJson -Path $Fx.FaultPath -Object @{ push = $Push; reads = @(@{ ref = $Ref; output = $Output; mode = $Mode; ordinal = $Ordinal; exit = $Exit }) }
}

function Test-C1339NoWrites {
    param($Fx)
    $trace = Join-Path $Fx.Root 'real-git-trace.log'
    $ghTrace = Join-Path $Fx.Root 'gh-trace.log'
    return (-not (Test-Path -LiteralPath $trace) -or (Get-Content -LiteralPath $trace -Raw) -notmatch '(?m)^(tag|push) ') -and
        (-not (Test-Path -LiteralPath $ghTrace) -or (Get-Content -LiteralPath $ghTrace -Raw) -notmatch '(?m)^GH release-(create|upload|publish)')
}

function New-C1339Check {
    param([bool]$Pass, [string]$Detail)
    [pscustomobject]@{ Pass = $Pass; Detail = $Detail }
}

function Test-C1339_ExactRemoteRefs {
    # Only the fixture adapter's own guards (G-64, G-65) stay here. G-1..G-6 (the exact remote-ref parser) run in process:
    # ReleaseGateLibraryTests.C1339_ExactRemoteRefs_parser; the candidate's reads (G-7..G-9) since unit 13: ReleaseCandidateGitTests;
    # the publisher's and promoter's reads (G-10..G-17) since unit 14: ReleaseTagAllocatorTests.C1339_ExactRemoteRefs_publisher_and_promoter.
    $fx = New-C1339Fixture

    # These controls can never execute Git, including under deliberate mutation.
    $launches = [Collections.Generic.List[string]]::new()
    $inert = { param($Root, $WorkingDirectory, $Arguments) $launches.Add(($Arguments -join ' ')); return @{ ExitCode = 0; Output = '' } }
    $outside = New-C599Root
    $link = Join-Path $fx.Root 'escape'
    New-Item -ItemType SymbolicLink -Path $link -Target $outside | Out-Null
    $checks = @()
    foreach ($bad in @($outside, $link)) {
        $refused = $false
        try { [void](Invoke-C1339FixtureGit -Root $fx.Root -Checkout $fx.Checkout -Bare $bad -WorkingDirectory $fx.Checkout -Arguments @('ls-remote', '--tags', 'origin') -Launcher $inert) }
        catch { $refused = $_.Exception.Message -match '^fixture-(path-outside-root|linked-path)' }
        $checks += New-C1339Check ($refused -and $launches.Count -eq 0) $bad
    }
    Assert-C599AMatrix 'C1339 G-64 fixture-path-contained' $checks
    $checks = @()
    foreach ($requestArgs in @(@('ls-remote', '--tags', 'https://example.invalid/repo'), @('unknown'), @('push', '--mirror', 'origin'))) {
        $refused = $false
        try { [void](Invoke-C1339FixtureGit -Root $fx.Root -Checkout $fx.Checkout -Bare $fx.Bare -WorkingDirectory $fx.Checkout -Arguments $requestArgs -Launcher $inert) }
        catch { $refused = $_.Exception.Message -match '^fixture-(transport-refused|command-refused|push-form)' }
        $checks += New-C1339Check ($refused -and $launches.Count -eq 0) ($requestArgs -join ' ')
    }
    Assert-C599AMatrix 'C1339 G-65 fixture-transport-allowlist' $checks
}

# ====================================================================== run ===

function Clear-C599FixtureReadOnly {
    # Only our disposable clones: Git marks loose objects read-only on Windows.
    # Leave deletion and failure retention to the owning ScriptHarness process.
    foreach ($clone in $script:C599PolicyClones) {
        $objects = Join-Path $clone '.git/objects'
        if (-not (Test-Path -LiteralPath $objects)) { $objects = Join-Path $clone 'objects' }
        if (Test-Path -LiteralPath $objects) {
            Get-ChildItem -LiteralPath $objects -Recurse -Force -File | ForEach-Object { $_.IsReadOnly = $false }
        }
    }
}

. (Join-Path $here 'fixtures/c1731-evidence-path.ps1')
. (Join-Path $here 'fixtures/c1733-allocator.ps1')
$cases = Get-C487CaseFunctions -Prefix 'C599_'
$caseWatch = [Diagnostics.Stopwatch]::StartNew()
$caseStartedUtc = [datetime]::UtcNow
if ($Case) {
    $fn = Get-Command -Name ('Test-{0}' -f $Case) -CommandType Function -ErrorAction SilentlyContinue
    if (-not $fn) { Write-Error ('unknown case {0}' -f $Case); exit 2 }
    try { & $fn } finally {
        $bodySeconds = $caseWatch.Elapsed.TotalSeconds
        $teardownWatch = [Diagnostics.Stopwatch]::StartNew()
        try { Complete-C1733FixtureRun }
        finally {
            if ($env:C1731_TIMING_ROOT) {
                $timingRoot = $env:C1731_TIMING_ROOT
                if (-not [IO.Path]::IsPathRooted($timingRoot)) { $timingRoot = Join-Path $repoRoot $timingRoot }
                [void][IO.Directory]::CreateDirectory($timingRoot)
                $record = [ordered]@{
                    case = $Case; source = [string](& git -C $repoRoot rev-parse HEAD)
                    host = [Environment]::MachineName; platform = [Environment]::OSVersion.Platform.ToString()
                    startedUtc = $caseStartedUtc.ToString('o'); bodySeconds = $bodySeconds
                    teardownSeconds = $teardownWatch.Elapsed.TotalSeconds; phases = $script:C1731Timing
                    externalCalls = 'Filesystem scan/parent/compare/write measured; gate includes fresh Git/HTTP/IO checks. Their individual spans and process startup remain unknown.'
                }
                $path = Join-Path $timingRoot ($Case + '-' + [guid]::NewGuid().ToString('N') + '.json')
                [IO.File]::WriteAllText($path, ($record | ConvertTo-Json -Depth 8))
            }
        }
    }
} else {
    try { foreach ($fn in $cases) { & $fn } } finally { Complete-C1733FixtureRun }
}
Write-C487Evidence -ResultsDirectory $ResultsDirectory -Case 'release-gate-summary' -Body @{
    passed = $script:C487Passed; failed = $script:C487Failed; rows = $script:C487Rows
}
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 0
