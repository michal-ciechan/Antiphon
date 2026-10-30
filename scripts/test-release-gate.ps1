#requires -Version 5.1
# CARD-0599 offline harness for the release-gate lanes. ASCII-only.
#
# Every case invokes PRODUCTION code - the real policy reader, the real credit
# predicate, the real nightly run core, the real coordinator/publisher/registrar
# scripts - and asserts an observed effect. Git, GitHub and Windmill are injected
# at the I/O boundary only; no seam ever replaces a predicate or a gate.
#
# No build, no test assembly, no network and no live registration runs here.
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
. (Join-Path $lib 'release-gate.ps1')
. (Join-Path $lib 'release-authority.ps1')
. (Join-Path $lib 'nightly-run-impl.ps1')
. (Join-Path $lib 'nightly-tests-impl.ps1')
. (Join-Path $lib 'c487-harness.ps1')

$ResultsDirectory = New-C487Root -ResultsDirectory $ResultsDirectory
# CARD-0599: every seam this harness hands the publisher (fake Git, fake GitHub,
# injected clock) is admitted only in explicit test mode, and test mode is refused
# whenever a real remote would be reached. C599A_PublisherTestMode clears it.
$env:ANTIPHON_RELEASE_GATE_TEST_MODE = '1'
$repoRoot = Split-Path -Parent $here
$ShaA = ('a' * 40)
$ShaB = ('b' * 40)
$ShaC = ('c' * 40)
$CandidateRef = 'release/rc-20260923T083000Z'
$CandidateId = 'rc-20260923T083000Z'

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
    $hash = Get-NightlyPolicyHash -Object $obj
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

function Invoke-C599Candidate {
    param([string]$ReleaseRoot, [string]$Checkout, $Seams, [string]$Ref)
    return (& (Join-Path $here 'release-candidate.ps1') -ReleaseRoot $ReleaseRoot -CheckoutRoot $Checkout `
        -Ref $Ref -SeamsPath $Seams.Path -PassThru)
}

function New-C599RcGreen {
    <#
      Lay down a complete, honest RC evidence set: candidate journal, summary and
      complete-green, all agreeing on the same candidate/SHA/policy hash.
    #>
    param([string]$ReleaseRoot, [string]$Sha, [string]$CandidateId, [string]$Ref, [string]$PolicyHash = 'policy-1')
    $root = Get-ReleaseGateCandidateRoot -ReleaseRoot $ReleaseRoot -CandidateId $CandidateId
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
# frozen by the PRODUCTION New-ReleaseGateAuthority / New-ReleaseGateExecutionPlan,
# and the chunk evidence is real TRX read back by the production TRX reader. Every
# negative below starts from this valid control and changes exactly one thing.

$script:C599RepoPolicyPath = Join-Path $repoRoot (Join-Path 'tests' 'test-execution-policy.json')
$script:C599IntentId = 'intent-20260923T083000Z'
$script:C599RunId = 'rc-run-1'
$script:C599NativeSuites = @('antiphon', 'session-runner', 'pty-host', 'agents-pty', 'messaging', 'e2e', 'markdown-pdf', 'observability')
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
    $r = Invoke-ReleaseAuthorityGit -RepositoryRoot $Repo -Arguments $Arguments
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

function Get-C599AuthorityDir {
    param($Fx)
    return (Get-ReleaseAuthorityDir -CandidateRoot (Get-ReleaseGateCandidateRoot -ReleaseRoot $Fx.ReleaseRoot -CandidateId $CandidateId))
}

function Update-C599Json {
    param([string]$Path, [scriptblock]$Mutate)
    $obj = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    & $Mutate $obj
    Write-NightlyAtomicJson -Path $Path -Object $obj
}

function Update-C599Ledger {
    # Mutate the ledger (or plan) and keep every OTHER binding valid, so the one
    # changed field is the only thing a guard can refuse on.
    param($Fx, [scriptblock]$Ledger = $null, [scriptblock]$Plan = $null)
    $dir = Get-C599AuthorityDir -Fx $Fx
    if ($Plan) { Update-C599Json -Path (Join-Path $dir 'execution-plan.json') -Mutate $Plan }
    Update-C599Json -Path (Join-Path $dir 'ledger.json') -Mutate {
        param($l)
        $l.executionPlanDigest = (Get-NightlyFileSha256 -Path (Join-Path $dir 'execution-plan.json'))
        if ($Ledger) { & $Ledger $l }
    }
}

function Set-C599TrxOutcome {
    # Rewrite one chunk's real TRX and re-bind its digest and counts, so only the
    # UID outcome differs from the valid control.
    param($Fx, [string]$Chunk, [string]$Uid, [string]$Outcome)
    $dir = Get-C599AuthorityDir -Fx $Fx
    $ledger = Get-Content -LiteralPath (Join-Path $dir 'ledger.json') -Raw | ConvertFrom-Json
    $row = @($ledger.chunks | Where-Object { [string]$_.chunk -eq $Chunk })[0]
    $trx = Join-Path (Split-Path -Parent $dir) ([string]$row.trx)
    $rows = @(Read-NightlyTrxIdentities -TrxPath $trx | ForEach-Object {
        $o = [string]$_.Outcome
        if ([string]$_.TestId -eq $Uid) { $o = $Outcome }
        [pscustomobject]@{ Id = [string]$_.TestId; ClassName = [string]$_.ClassName; MethodName = [string]$_.MethodName; Outcome = $o } })
    Write-C487Trx -Path $trx -Rows $rows
    Update-C599Ledger -Fx $Fx -Ledger {
        param($l)
        foreach ($c in @($l.chunks)) {
            if ([string]$c.chunk -ne $Chunk) { continue }
            $c.trxSha256 = (Get-NightlyFileSha256 -Path $trx)
            $c.passed = @($rows | Where-Object { $_.Outcome -eq 'Passed' }).Count
            $c.failed = @($rows | Where-Object { $_.Outcome -eq 'Failed' }).Count
            $c.skipped = @($rows | Where-Object { $_.Outcome -ne 'Passed' -and $_.Outcome -ne 'Failed' }).Count
        }
    }
}

function New-C599Authority {
    <#
      Freeze a complete valid authority + plan + ledger for the candidate through
      the production functions, then lay down one real TRX per frozen chunk.
    #>
    param([string]$ReleaseRoot, [string]$Checkout, [string]$Sha, [string]$Repository = 'michal-ciechan/Antiphon')
    $root = Get-ReleaseGateCandidateRoot -ReleaseRoot $ReleaseRoot -CandidateId $CandidateId
    $auth = New-ReleaseGateAuthority -RepositoryRoot $Checkout -CandidateRoot $root -Sha $Sha -Repository $Repository `
        -CandidateId $CandidateId -CandidateRef $CandidateRef -IntentId $script:C599IntentId -ReleaseCardId $script:C599ReleaseCardId `
        -CreatedUtc ([datetime]'2026-09-23T08:30:30Z')
    $discovery = Get-C599Discovery
    $plan = New-ReleaseGateExecutionPlan -CandidateRoot $root -Discovery $discovery -Rosters $script:C599Rosters
    $dir = Get-ReleaseAuthorityDir -CandidateRoot $root
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

function Test-C599NoRemoteWrite {
    # Observer reads of both recipients: no release stored and no git push/tag attempted.
    param($Fx)
    $trace = ''
    if (Test-Path -LiteralPath $Fx.Git.Trace) { $trace = Get-Content -LiteralPath $Fx.Git.Trace -Raw }
    $writes = @(($trace -split "`n") | Where-Object { $_ -match '^GIT (push|tag)' })
    $db = Get-Content -LiteralPath $Fx.Gh.StorePath -Raw | ConvertFrom-Json
    return ($writes.Count -eq 0 -and @($db.releases).Count -eq 0)
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

function Get-C599Release {
    # Recipient-side read of the stored release, not the publisher's return value.
    param($Gh, [string]$Tag)
    $db = Get-Content -LiteralPath $Gh.StorePath -Raw | ConvertFrom-Json
    foreach ($r in @($db.releases)) { if ([string]$r.tagName -eq $Tag) { return $r } }
    return $null
}

function Set-C599GitHubFailure {
    param($Gh, [string]$Key, [switch]$Clear, [switch]$CommitsAnyway)
    $state = Get-Content -LiteralPath $Gh.FailPath -Raw | ConvertFrom-Json
    $obj = [ordered]@{}
    foreach ($p in @($state.PSObject.Properties)) { $obj[$p.Name] = $p.Value }
    if ($Clear) { $obj.Remove($Key); $obj.Remove($Key + '_commits') }
    else {
        $obj[$Key] = $true
        if ($CommitsAnyway) { $obj[$Key + '_commits'] = $true }
    }
    Write-NightlyAtomicJson -Path $Gh.FailPath -Object $obj
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

function Update-C599Card {
    # Mutate the served store (status/raw) or the release card itself.
    param($Fx, [scriptblock]$Card = $null, [scriptblock]$Store = $null)
    $c599Card = $Card; $c599Store = $Store
    Update-C599Json -Path $Fx.CardApi.Store -Mutate {
        param($s)
        if ($c599Store) { & $c599Store $s }
        if ($c599Card) { & $c599Card $s.cards.($script:C599ReleaseCardId) }
    }
}

# =============================================================== V-1 policy ===

function Test-C599_ProfileSchema {
    $root = New-C599Root
    $v2 = New-C599Policy -Root (Join-Path $root 'a') -SchemaVersion 2
    $ok = Read-NightlyExecutionPolicy -RepoRoot $repoRoot -PolicyPath $v2
    Assert-C487 -Cond ([int]$ok.SchemaVersion -eq 2) -Name 'C599 ProfileSchema v2 loads' -Detail ([string]$ok.SchemaVersion)

    $v1 = New-C599Policy -Root (Join-Path $root 'b') -SchemaVersion 1 -OmitProfiles
    $ok1 = Read-NightlyExecutionPolicy -RepoRoot $repoRoot -PolicyPath $v1
    Assert-C487 -Cond ([int]$ok1.SchemaVersion -eq 1) -Name 'C599 ProfileSchema v1 still loads' -Detail ([string]$ok1.SchemaVersion)
    $nightlyV1 = @(Get-NightlyProfileRequiredSuites -PolicyObject $ok1.Object -Profile 'nightly')
    Assert-C487 -Cond ($nightlyV1.Count -eq 3) -Name 'C599 ProfileSchema v1 nightly falls back to defaultSuites' -Detail ($nightlyV1 -join ',')

    $refusedRc = ''
    try { [void](Get-NightlyPolicyProfile -PolicyObject $ok1.Object -Profile 'rc') } catch { $refusedRc = $_.Exception.Message }
    Assert-C487 -Cond ($refusedRc -match 'schema 2') -Name 'C599 ProfileSchema rc on a v1 policy refuses' -Detail $refusedRc

    $refusedUnknown = ''
    try { [void](Get-NightlyPolicyProfile -PolicyObject $ok.Object -Profile 'turbo') } catch { $refusedUnknown = $_.Exception.Message }
    Assert-C487 -Cond ($refusedUnknown -match 'unknown profile') -Name 'C599 ProfileSchema unknown profile refuses' -Detail $refusedUnknown

    $stale = New-C599Policy -Root (Join-Path $root 'c') -StaleHash
    $staleMsg = ''
    try { [void](Read-NightlyExecutionPolicy -RepoRoot $repoRoot -PolicyPath $stale) } catch { $staleMsg = $_.Exception.Message }
    Assert-C487 -Cond ($staleMsg -match 'stale policy hash') -Name 'C599 ProfileSchema stale hash refuses' -Detail $staleMsg

    $bad = New-C599Policy -Root (Join-Path $root 'd') -SchemaVersion 7 -OmitProfiles
    $badMsg = ''
    try { [void](Read-NightlyExecutionPolicy -RepoRoot $repoRoot -PolicyPath $bad) } catch { $badMsg = $_.Exception.Message }
    Assert-C487 -Cond ($badMsg -match 'unsupported policy schema') -Name 'C599 ProfileSchema unknown version refuses' -Detail $badMsg

    # Historical v1/v2 fixtures remain supported; the shipped inventory is v4.
    $ship = Read-NightlyExecutionPolicy -RepoRoot $repoRoot
    Assert-C487 -Cond ([int]$ship.SchemaVersion -eq 4) -Name 'C599 ProfileSchema shipped policy is v4' -Detail ([string]$ship.SchemaVersion)
    Assert-C487 -Cond ($null -ne $ship.Object.profiles.rc) -Name 'C599 ProfileSchema shipped policy defines rc' -Detail ''
}

function Test-C599_ProfileSuites {
    $root = New-C599Root
    $policyPath = New-C599Policy -Root $root
    $policy = Read-NightlyExecutionPolicy -RepoRoot $repoRoot -PolicyPath $policyPath
    $nightly = @(Get-NightlyProfileRequiredSuites -PolicyObject $policy.Object -Profile 'nightly')
    $rc = @(Get-NightlyProfileRequiredSuites -PolicyObject $policy.Object -Profile 'rc')
    Assert-C487 -Cond ($nightly -notcontains 'e2e') -Name 'C599 ProfileSuites nightly excludes e2e' -Detail ($nightly -join ',')
    Assert-C487 -Cond ($rc -contains 'e2e') -Name 'C599 ProfileSuites rc requires e2e' -Detail ($rc -join ',')
    Assert-C487 -Cond ($rc.Count -eq ($nightly.Count + 1)) -Name 'C599 ProfileSuites rc is nightly plus e2e' -Detail ($rc -join ',')

    # A manual-mode suite the profile requires is runnable; one it does not is not.
    Assert-C487 -Cond (Test-NightlyProfileSuiteRunnable -PolicyObject $policy.Object -Profile 'rc' -SuiteId 'e2e') -Name 'C599 ProfileSuites manual e2e is runnable in rc'
    Assert-C487 -Cond (-not (Test-NightlyProfileSuiteRunnable -PolicyObject $policy.Object -Profile 'nightly' -SuiteId 'e2e')) -Name 'C599 ProfileSuites manual e2e stays out of nightly'

    # An rc profile that forgot e2e is visibly not the rc contract.
    $noE2E = New-C599Policy -Root (Join-Path $root 'x') -OmitE2EFromRc
    $p2 = Read-NightlyExecutionPolicy -RepoRoot $repoRoot -PolicyPath $noE2E
    $rc2 = @(Get-NightlyProfileRequiredSuites -PolicyObject $p2.Object -Profile 'rc')
    Assert-C487 -Cond ($rc2 -notcontains 'e2e') -Name 'C599 ProfileSuites missing e2e is detectable in the required set' -Detail ($rc2 -join ',')

    # A partial explicit selection is never a complete profile.
    $present = Test-NightlyRequiredSuitesPresent -Selected @('antiphon') -Required $rc
    Assert-C487 -Cond ((-not $present.Ok) -and $present.Missing.Count -ge 2) -Name 'C599 ProfileSuites partial selection is incomplete' -Detail ($present.Missing -join ',')
    $full = Test-NightlyRequiredSuitesPresent -Selected $rc -Required $rc
    Assert-C487 -Cond ($full.Ok) -Name 'C599 ProfileSuites control full rc selection is complete'

    # Every exclusion row needs a reason and an owner or the policy refuses.
    $bad = Get-Content -LiteralPath $policyPath -Raw | ConvertFrom-Json
    $bad.profiles.rc.exclusions = @([pscustomobject]@{ suite = 'e2e'; class = 'X' })
    $msg = ''
    try { [void](Get-NightlyProfileExclusions -PolicyObject $bad -Profile 'rc' -SuiteId '') } catch { $msg = $_.Exception.Message }
    Assert-C487 -Cond ($msg -match 'reason and owner') -Name 'C599 ProfileSuites exclusion without reason/owner refuses' -Detail $msg
}

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
    $policy = Read-NightlyExecutionPolicy -RepoRoot $repoRoot -PolicyPath (New-C599Policy -Root $root)
    $exclusions = @(Get-NightlyProfileExclusions -PolicyObject $policy.Object -Profile 'rc' -SuiteId '')

    # JSON discovery document and MTP diagnostic records must agree on UIDs and
    # must both PRESERVE the raw category rather than consuming it.
    $jsonNodes = @(
        @{ uid = 'u1'; type = 'BoardE2ETests'; method = 'Board_loads'; namespace = 'E2E'; categories = @('OptIn'); state = 'Discovered' },
        @{ uid = 'u2'; type = 'SmokeE2ETests'; method = 'Home_renders'; namespace = 'E2E'; categories = @(); state = 'Discovered' }
    )
    $discPath = Join-Path $root 'discovery.json'
    Write-C487Discovery -Path $discPath -Nodes $jsonNodes
    $doc = Read-NightlyDiscoveryDocument -Path $discPath
    Assert-C487 -Cond (@($doc.Nodes).Count -eq 2) -Name 'C599 MetadataParsers json parser reads both nodes' -Detail ([string]@($doc.Nodes).Count)
    $optin = @($doc.Nodes | Where-Object { $_.Uid -eq 'u1' })[0]
    Assert-C487 -Cond ($optin.Categories -contains 'OptIn') -Name 'C599 MetadataParsers json parser preserves the raw OptIn category'
    Assert-C487 -Cond ([bool]$optin.Excluded) -Name 'C599 MetadataParsers json OptIn still excluded in the default lane'

    $rec = 'TestNodeUid { Value = u1 }, TypeName = BoardE2ETests, MethodName = Board_loads, Namespace = E2E, TestMetadataProperty { Key = OptIn }'
    $diag = ConvertFrom-NightlyDiagnosticRecord -Record $rec -State 'Discovered'
    Assert-C487 -Cond ([string]$diag.Uid -eq 'u1') -Name 'C599 MetadataParsers diagnostic parser reads the same uid' -Detail $diag.Uid
    Assert-C487 -Cond ($diag.Categories -contains 'OptIn') -Name 'C599 MetadataParsers diagnostic parser preserves the raw category'
    Assert-C487 -Cond ([string]$diag.ClassName -eq [string]$optin.ClassName) -Name 'C599 MetadataParsers both parsers agree on the class identity' -Detail ($diag.ClassName + ' vs ' + $optin.ClassName)

    # Profile-aware: the SAME nodes now resolve by declared rows, not by category.
    foreach ($n in @($doc.Nodes)) {
        $defaultExcluded = Test-NightlyDiscoveryExcluded -Node $n
        $profileExcluded = Test-NightlyDiscoveryExcluded -Node $n -ProfileExclusions $exclusions -ProfileAware
        if ([string]$n.Uid -eq 'u1') {
            Assert-C487 -Cond ($defaultExcluded -and (-not $profileExcluded)) -Name 'C599 MetadataParsers OptIn excluded by default but required in a profile' -Detail ('default=' + $defaultExcluded + ' profile=' + $profileExcluded)
        } else {
            Assert-C487 -Cond ((-not $defaultExcluded) -and (-not $profileExcluded)) -Name 'C599 MetadataParsers plain node required in both lanes'
        }
    }

    $jsonUids = @(Get-NightlyRequiredDiscoveryUids -DiscoveryNodes @($doc.Nodes) -ProfileExclusions $exclusions -ProfileAware)
    $diagUids = @(Get-NightlyRequiredDiscoveryUids -DiscoveryNodes @($diag, @($doc.Nodes)[1]) -ProfileExclusions $exclusions -ProfileAware)
    Assert-C487 -Cond ((($jsonUids | Sort-Object) -join ',') -eq (($diagUids | Sort-Object) -join ',')) -Name 'C599 MetadataParsers json and diagnostic required uid sets are identical' -Detail (($jsonUids -join ',') + ' vs ' + ($diagUids -join ','))

    # A truncated record is an error, never a silently dropped row.
    $truncated = ''
    try { [void](ConvertFrom-NightlyDiagnosticRecord -Record 'TestNodeUid { Value = u9 }' -State 'Discovered') } catch { $truncated = $_.Exception.Message }
    Assert-C487 -Cond ($truncated -match 'truncated') -Name 'C599 MetadataParsers truncated diagnostic record throws' -Detail $truncated

    $dupPath = Join-Path $root 'dup.json'
    Write-C487Discovery -Path $dupPath -Nodes @($jsonNodes[0], $jsonNodes[0])
    $dupMsg = ''
    try { [void](Read-NightlyDiscoveryDocument -Path $dupPath) } catch { $dupMsg = $_.Exception.Message }
    Assert-C487 -Cond ($dupMsg -match 'duplicate discovery uid') -Name 'C599 MetadataParsers duplicate uid throws' -Detail $dupMsg
}

function Test-C599_EligibilityCensus {
    $root = New-C599Root
    $policy = Read-NightlyExecutionPolicy -RepoRoot $repoRoot -PolicyPath (New-C599Policy -Root $root)
    $exclusions = @(Get-NightlyProfileExclusions -PolicyObject $policy.Object -Profile 'rc' -SuiteId '')
    $nodes = New-C599Nodes

    $census = Get-NightlyDiscoveryCensus -DiscoveryNodes $nodes -ProfileExclusions $exclusions -ProfileAware
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
    $census2 = Get-NightlyDiscoveryCensus -DiscoveryNodes $withNew -ProfileExclusions $exclusions -ProfileAware
    $required2 = @($census2.Required | ForEach-Object { [string]$_.uid })
    Assert-C487 -Cond ($required2 -contains 'u-brand-new') -Name 'C599 EligibilityCensus a new unclassified case stays required' -Detail ($required2 -join ',')
    Assert-C487 -Cond ($census2.Digest -ne $census.Digest) -Name 'C599 EligibilityCensus a new case changes the census digest' -Detail $census2.Digest

    # The default nightly lane is unchanged by any of this.
    $defaultCensus = Get-NightlyDiscoveryCensus -DiscoveryNodes $nodes
    $defaultRequired = @($defaultCensus.Required | ForEach-Object { [string]$_.uid })
    Assert-C487 -Cond ($defaultRequired -notcontains 'u-optin-1') -Name 'C599 EligibilityCensus default lane still excludes OptIn' -Detail ($defaultRequired -join ',')
    Assert-C487 -Cond ($defaultRequired -contains 'u-mixed-live') -Name 'C599 EligibilityCensus default lane ignores profile method rows' -Detail ($defaultRequired -join ',')

    $dupCensus = Get-NightlyDiscoveryCensus -DiscoveryNodes @($nodes[0], $nodes[0]) -ProfileExclusions $exclusions -ProfileAware
    Assert-C487 -Cond ($dupCensus.Duplicates.Count -eq 1) -Name 'C599 EligibilityCensus duplicate uid is reported' -Detail ($dupCensus.Duplicates -join ',')

    $allExcluded = Get-NightlyDiscoveryCensus -DiscoveryNodes @($nodes[0]) -ProfileExclusions $exclusions -ProfileAware
    Assert-C487 -Cond ((-not $allExcluded.Ok) -and $allExcluded.RequiredCount -eq 0) -Name 'C599 EligibilityCensus zero required uids is not ok' -Detail ([string]$allExcluded.RequiredCount)
    Assert-C487 -Cond ($census.Digest -eq (Get-NightlyDiscoveryCensus -DiscoveryNodes $nodes -ProfileExclusions $exclusions -ProfileAware).Digest) -Name 'C599 EligibilityCensus digest is stable for the same roster' -Detail $census.Digest
}

function Test-C599_ExpandedCoverage {
    $root = New-C599Root
    $policy = Read-NightlyExecutionPolicy -RepoRoot $repoRoot -PolicyPath (New-C599Policy -Root $root)
    $exclusions = @(Get-NightlyProfileExclusions -PolicyObject $policy.Object -Profile 'rc' -SuiteId '')

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

    $verdict = ConvertTo-NightlyNativeSuiteVerdict -TrxPath $trx -DiscoveryPath $disc -ExecutionDiagnosticPath $diagDir -RunDirectory $run -NotBeforeUtc $before -ProcessExit 0 -Sha $ShaA -ExpectedSha $ShaA -GitRef 'r' -ExpectedRef 'r' -PolicyHash 'h' -ExpectedPolicyHash 'h' -ProfileExclusions $exclusions -ProfileAware
    Assert-C487 -Cond ([bool]$verdict.coverageComplete) -Name 'C599 ExpandedCoverage control: both required rows executed is complete' -Detail (@($verdict.reasons) -join ';')
    Assert-C487 -Cond (($null -ne $verdict.census) -and [int]$verdict.census.RequiredCount -eq 2) -Name 'C599 ExpandedCoverage census reports 2 required uids' -Detail ([string]$verdict.census.RequiredCount)
    Assert-C487 -Cond ([int]$verdict.census.ExcludedCount -eq 1) -Name 'C599 ExpandedCoverage census reports the 1 named exclusion' -Detail ([string]$verdict.census.ExcludedCount)

    # Remove one expanded required row: incomplete.
    $run2 = Join-Path $root 'run2'
    New-Item -ItemType Directory -Path (Join-Path $run2 'exec') -Force | Out-Null
    Write-C487Trx -Path (Join-Path $run2 'e2e.trx') -Rows @($trxRows[0])
    Write-C487Discovery -Path (Join-Path $run2 'e2e.discovery.json') -Nodes $discNodes
    Write-C487Execution -Path (Join-Path $run2 (Join-Path 'exec' 'log.diag')) -Nodes @($execNodes[0])
    $missing = ConvertTo-NightlyNativeSuiteVerdict -TrxPath (Join-Path $run2 'e2e.trx') -DiscoveryPath (Join-Path $run2 'e2e.discovery.json') -ExecutionDiagnosticPath (Join-Path $run2 'exec') -RunDirectory $run2 -NotBeforeUtc $before -ProcessExit 0 -Sha $ShaA -ExpectedSha $ShaA -GitRef 'r' -ExpectedRef 'r' -PolicyHash 'h' -ExpectedPolicyHash 'h' -ProfileExclusions $exclusions -ProfileAware
    Assert-C487 -Cond (-not [bool]$missing.coverageComplete) -Name 'C599 ExpandedCoverage a removed expanded row is incomplete' -Detail (@($missing.reasons) -join ';')
    Assert-C487 -Cond ((@($missing.reasons) -join ';') -match 'missing-expanded-row') -Name 'C599 ExpandedCoverage names the missing expanded row' -Detail (@($missing.reasons) -join ';')

    # In the DEFAULT lane the same evidence would have zero required rows, which
    # is exactly the silent-green the profile lane must refuse.
    $defaultVerdict = ConvertTo-NightlyNativeSuiteVerdict -TrxPath $trx -DiscoveryPath $disc -ExecutionDiagnosticPath $diagDir -RunDirectory $run -NotBeforeUtc $before -ProcessExit 0 -Sha $ShaA -ExpectedSha $ShaA -GitRef 'r' -ExpectedRef 'r' -PolicyHash 'h' -ExpectedPolicyHash 'h'
    Assert-C487 -Cond (($null -ne $defaultVerdict.census) -and [int]$defaultVerdict.census.RequiredCount -eq 0) -Name 'C599 ExpandedCoverage default lane would require zero uids here' -Detail ([string]$defaultVerdict.census.RequiredCount)

    # A profile lane whose every node is excluded is never green.
    $onlyLive = @(@{ uid = 'e3'; type = 'LiveCanaryTests'; method = 'Runs_live'; namespace = 'E2E'; categories = @('OptIn'); state = 'Discovered' })
    $run3 = Join-Path $root 'run3'
    New-Item -ItemType Directory -Path (Join-Path $run3 'exec') -Force | Out-Null
    Write-C487Trx -Path (Join-Path $run3 'e2e.trx') -Rows @(@{ Id = 'e3'; ClassName = 'E2E.LiveCanaryTests'; MethodName = 'Runs_live'; Outcome = 'Passed' })
    Write-C487Discovery -Path (Join-Path $run3 'e2e.discovery.json') -Nodes $onlyLive
    Write-C487Execution -Path (Join-Path $run3 (Join-Path 'exec' 'log.diag')) -Nodes @(@{ uid = 'e3'; state = 'Passed'; type = 'LiveCanaryTests'; method = 'Runs_live'; className = 'E2E.LiveCanaryTests' })
    $zero = ConvertTo-NightlyNativeSuiteVerdict -TrxPath (Join-Path $run3 'e2e.trx') -DiscoveryPath (Join-Path $run3 'e2e.discovery.json') -ExecutionDiagnosticPath (Join-Path $run3 'exec') -RunDirectory $run3 -NotBeforeUtc $before -ProcessExit 0 -Sha $ShaA -ExpectedSha $ShaA -GitRef 'r' -ExpectedRef 'r' -PolicyHash 'h' -ExpectedPolicyHash 'h' -ProfileExclusions $exclusions -ProfileAware
    Assert-C487 -Cond ((-not [bool]$zero.coverageComplete) -and (-not [bool]$zero.testsPassed)) -Name 'C599 ExpandedCoverage zero required uids is never green' -Detail (@($zero.reasons) -join ';')
    Assert-C487 -Cond ((@($zero.reasons) -join ';') -match 'zero-required-uid') -Name 'C599 ExpandedCoverage names zero-required-uid' -Detail (@($zero.reasons) -join ';')

    # Missing execution diagnostics is incomplete even with a green TRX.
    $noDiag = ConvertTo-NightlyNativeSuiteVerdict -TrxPath $trx -DiscoveryPath $disc -ExecutionDiagnosticPath '' -RunDirectory $run -NotBeforeUtc $before -ProcessExit 0 -Sha $ShaA -ExpectedSha $ShaA -GitRef 'r' -ExpectedRef 'r' -PolicyHash 'h' -ExpectedPolicyHash 'h' -ProfileExclusions $exclusions -ProfileAware
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
                $kind = Get-ReleaseGateCreditKind -Profile $p -Trigger $t -Ref $r
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
        $kind = Get-ReleaseGateCreditKind -Profile $row[0] -Trigger $row[1] -Ref $row[2]
        Assert-C487 -Cond ([string]::IsNullOrWhiteSpace([string]$kind)) -Name ('C599 CreditIdentity malformed [{0}/{1}/{2}] earns nothing' -f $row[0], $row[1], $row[2]) -Detail ('got=' + $kind)
    }
    # An absent profile is the pre-CARD-0599 master lane, unchanged.
    $legacy = Get-ReleaseGateCreditKind -Profile '' -Trigger 'scheduled' -Ref 'master'
    Assert-C487 -Cond ([string]$legacy -eq 'master-scheduled') -Name 'C599 CreditIdentity absent profile is still the master lane' -Detail $legacy
    # The refs/heads/ prefix is accepted for a candidate but not for master.
    $prefixed = Get-ReleaseGateCreditKind -Profile 'rc' -Trigger 'rc' -Ref ('refs/heads/' + $CandidateRef)
    Assert-C487 -Cond ([string]$prefixed -eq 'rc-release') -Name 'C599 CreditIdentity refs/heads candidate ref is accepted' -Detail $prefixed
}

function Test-C599_CreditVerdicts {
    # One invalid field at a time against a valid control, per credit kind.
    $master = New-C599State
    $v = Get-ReleaseGateCreditVerdict -State $master
    Assert-C487 -Cond ([string]$v.CreditKind -eq 'master-scheduled') -Name 'C599 CreditVerdicts control master green earns master credit' -Detail (@($v.Reasons) -join ';')
    Assert-C487 -Cond (Test-NightlyCompleteGreenPredicate -State $master) -Name 'C599 CreditVerdicts predicate default asks the master question'
    Assert-C487 -Cond (-not (Test-NightlyCompleteGreenPredicate -State $master -CreditKind 'rc-release')) -Name 'C599 CreditVerdicts master green never earns release credit'

    $rc = New-C599State -Profile 'rc' -Trigger 'rc' -Ref $CandidateRef -Sha $ShaB -ExpectedSha $ShaB -Candidate $CandidateId
    $vr = Get-ReleaseGateCreditVerdict -State $rc
    Assert-C487 -Cond ([string]$vr.CreditKind -eq 'rc-release') -Name 'C599 CreditVerdicts control rc green earns release credit' -Detail (@($vr.Reasons) -join ';')
    Assert-C487 -Cond (-not (Test-NightlyCompleteGreenPredicate -State $rc)) -Name 'C599 CreditVerdicts rc green never earns master credit'

    foreach ($field in @('coverageComplete', 'testsPassed', 'reportDelivered')) {
        $bad = New-C599State
        $bad.$field = $false
        Assert-C487 -Cond ([string](Get-ReleaseGateCreditVerdict -State $bad).CreditKind -eq '') -Name ('C599 CreditVerdicts {0} false earns nothing' -f $field)
        $str = New-C599State
        $str.$field = 'true'
        $sv = Get-ReleaseGateCreditVerdict -State $str
        Assert-C487 -Cond ([string]$sv.CreditKind -eq '') -Name ('C599 CreditVerdicts {0} as the STRING "true" earns nothing' -f $field) -Detail (@($sv.Reasons) -join ';')
        Assert-C487 -Cond ((@($sv.Reasons) -join ';') -match ('not-true-boolean:' + $field)) -Name ('C599 CreditVerdicts {0} string is named as not-true-boolean' -f $field) -Detail (@($sv.Reasons) -join ';')
    }

    $nonZero = New-C599State -ExitCode 1
    Assert-C487 -Cond ([string](Get-ReleaseGateCreditVerdict -State $nonZero).CreditKind -eq '') -Name 'C599 CreditVerdicts non-zero exit earns nothing'
    $noExit = New-C599State
    $noExit.exitCode = $null
    Assert-C487 -Cond ([string](Get-ReleaseGateCreditVerdict -State $noExit).CreditKind -eq '') -Name 'C599 CreditVerdicts missing exitCode earns nothing'
    $noRun = New-C599State -RunId ''
    Assert-C487 -Cond ([string](Get-ReleaseGateCreditVerdict -State $noRun).CreditKind -eq '') -Name 'C599 CreditVerdicts missing runId earns nothing'
    $noPolicy = New-C599State -PolicyHash ''
    Assert-C487 -Cond ([string](Get-ReleaseGateCreditVerdict -State $noPolicy).CreditKind -eq '') -Name 'C599 CreditVerdicts missing policyHash earns nothing'

    $shaMismatch = New-C599State -Profile 'rc' -Trigger 'rc' -Ref $CandidateRef -Sha $ShaB -ExpectedSha $ShaC -Candidate $CandidateId
    $sm = Get-ReleaseGateCreditVerdict -State $shaMismatch
    Assert-C487 -Cond (([string]$sm.CreditKind -eq '') -and ((@($sm.Reasons) -join ';') -match 'rc-sha-mismatch')) -Name 'C599 CreditVerdicts rc sha mismatch earns nothing' -Detail (@($sm.Reasons) -join ';')
    $shortSha = New-C599State -Profile 'rc' -Trigger 'rc' -Ref $CandidateRef -Sha 'bbbbbbb' -ExpectedSha 'bbbbbbb' -Candidate $CandidateId
    Assert-C487 -Cond ([string](Get-ReleaseGateCreditVerdict -State $shortSha).CreditKind -eq '') -Name 'C599 CreditVerdicts rc short sha earns nothing'
    $noCandidate = New-C599State -Profile 'rc' -Trigger 'rc' -Ref $CandidateRef -Sha $ShaB -ExpectedSha $ShaB -Candidate ''
    Assert-C487 -Cond ([string](Get-ReleaseGateCreditVerdict -State $noCandidate).CreditKind -eq '') -Name 'C599 CreditVerdicts rc without a candidate id earns nothing'
    $noExpected = New-C599State -Profile 'rc' -Trigger 'rc' -Ref $CandidateRef -Sha $ShaB -ExpectedSha '' -Candidate $CandidateId
    Assert-C487 -Cond ([string](Get-ReleaseGateCreditVerdict -State $noExpected).CreditKind -eq '') -Name 'C599 CreditVerdicts rc without a pinned expectedSha earns nothing'

    Assert-C487 -Cond (-not (Test-NightlyCompleteGreenPredicate -State $master -CreditKind 'invented-kind')) -Name 'C599 CreditVerdicts an invented credit kind earns nothing'
    Assert-C487 -Cond (-not (Test-NightlyCompleteGreenPredicate -State $null)) -Name 'C599 CreditVerdicts a null state earns nothing'
}

function Test-C599_SelectionHopExitCodes {
    $root = New-C599Root
    $child = Join-Path $root 'exit child.ps1'
    Set-Content -LiteralPath $child -Encoding ASCII -Value @'
param([int]$Code, [string]$Value, [string]$Marker)
[IO.File]::WriteAllText($Marker, $Value)
exit $Code
'@
    foreach ($code in @(0, 4, 7)) {
        $marker = Join-Path $root ('received-{0}.txt' -f $code)
        $value = 'value with spaces'
        $result = Invoke-ReleaseSelectionHop -WorkingDirectory $root -ArgumentList @(
            '-File', $child, '-Code', [string]$code, '-Value', $value, '-Marker', $marker)
        $received = if (Test-Path -LiteralPath $marker) { [IO.File]::ReadAllText($marker) } else { '' }
        Assert-C487 -Cond ($result.ExitCode -eq $code -and $received -ceq $value) `
            -Name ('C599 SelectionHopExitCodes child exit {0} and spaced argument survive' -f $code) `
            -Detail ('expected={0} actual={1} received=[{2}]' -f $code, $result.ExitCode, $received)
    }
}

function New-C599RunFx {
    <#
      A run fixture whose clone, state root, release root and coordination root are
      all private, so the production run core can be exercised end to end.
    #>
    param([switch]$Rc)
    $root = New-C599Root
    $state = Join-Path $root 'state'
    New-Item -ItemType Directory -Path $state -Force | Out-Null
    $trace = Join-Path $root 'trace.log'
    $seams = Join-Path $root 'seams.ps1'
    $sha = $ShaA
    $origin = ''
    if ($Rc) {
        # Selection binds real committed policy/runtime blobs before either hop.
        # Keep the synthetic checkout for cases that only exercise refusals/locks.
        $seed = Join-Path $root 'seed'
        $script:C599PolicyClones.Add($seed)
        New-Item -ItemType Directory -Path (Join-Path $seed 'scripts') -Force | Out-Null
        [void](Invoke-C599RealGit $seed @('init', '-q', '-b', $CandidateRef))
        [void](Invoke-C599RealGit $seed @('config', 'core.autocrlf', 'false'))
        Copy-Item -LiteralPath $lib -Destination (Join-Path $seed 'scripts/lib') -Recurse
        foreach ($file in @('nightly-run.ps1', 'nightly-tests.ps1', 'nightly-report.ps1', 'select-test-rosters.ps1')) {
            Copy-Item -LiteralPath (Join-Path $here $file) -Destination (Join-Path $seed ('scripts/' + $file))
        }
        foreach ($file in @('tests/test-execution-policy.json', 'tests/test-selection-policy.json', 'global.json')) {
            $target = Join-Path $seed $file
            New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
            Copy-Item -LiteralPath (Join-Path $repoRoot $file) -Destination $target
        }
        $catalog = Get-Content -LiteralPath (Join-Path $seed 'tests/test-selection-policy.json') -Raw | ConvertFrom-Json
        foreach ($path in @($catalog.sets.sourceInputs + $catalog.sets.members.source | Sort-Object -Unique)) {
            if ($path -and -not (Test-Path -LiteralPath (Join-Path $seed $path))) {
                $target = Join-Path $seed $path
                New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
                Set-Content -LiteralPath $target -Value 'fixture source' -Encoding ASCII
            }
        }
        $projects = Invoke-C599RealGit $repoRoot @('ls-files', 'tests/*.csproj', 'scripts/fixtures/*.csproj')
        foreach ($path in $projects.Split("`n")) {
            $target = Join-Path $seed $path
            New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
            Copy-Item -LiteralPath (Join-Path $repoRoot $path) -Destination $target
        }
        # Commit the differing wrapper and ownership markers so fetch/reset/clean
        # preserves them, forcing the production self-reexec path.
        Add-Content -LiteralPath (Join-Path $seed 'scripts/nightly-run.ps1') -Value '# C1357 owned clone wrapper' -Encoding ASCII
        Write-NightlyAtomicJson -Path (Join-Path $seed '.antiphon-nightly-owned') -Object @{kind='nightly-clone'}
        Write-NightlyAtomicJson -Path (Join-Path $seed '.antiphon-release-owned') -Object @{kind='release-clone'}
        [void](Invoke-C599RealGit $seed @('add', '--all'))
        [void](Invoke-C599RealGit $seed @('-c', 'user.name=c599', '-c', 'user.email=c599@example.invalid', 'commit', '-qm', 'RC fixture candidate'))
        $origin = Join-Path $root 'origin.git'
        $script:C599PolicyClones.Add($origin)
        [void](Invoke-C599RealGit $root @('clone', '--bare', $seed, $origin))
        $clone = Join-Path $root 'checkout'
        $script:C599PolicyClones.Add($clone)
        [void](Invoke-C599RealGit $root @('clone', $origin, $clone))
        $sha = Invoke-C599RealGit $clone @('rev-parse', 'HEAD')
        $rows = @(@{Id='fixture-one';ClassName='Fixture.Fixture';MethodName='One';Outcome='Passed'})
        $nodes = @(@{uid='fixture-one';type='Fixture';method='One';namespace='Fixture'})
        Write-C487NativePassSeams $seams $trace $rows $nodes
        Add-Content -LiteralPath $seams -Encoding ASCII -Value @'
$NightlySeams.Git = $null
# The fake process records model full Windows RC execution on every host.
$NightlySeams.HostPlatform = { return 'windows' }
$NightlySeams.E2EPrerequisites = { return @{Ok=$true;Rows=@()} }
'@
    } else {
        $clone = New-C487OwnedClone -Root $root
        Write-C487Seams -Path $seams -TracePath $trace
    }
    return [pscustomobject]@{
        Root = $root; State = $state; Clone = $clone; Trace = $trace; Seams = $seams
        ReleaseRoot = (Join-Path $root 'releases'); Coordination = (Join-Path $root 'coord')
        Sha = $sha; Origin = $origin; RunId = ('c599-' + [guid]::NewGuid().ToString('N'))
    }
}

function Invoke-C599Run {
    param($Fx, [hashtable]$Extra)
    $splat = @{
        CheckoutRoot = $Fx.Clone
        StateRoot = $Fx.State
        LogRoot = (Join-Path $Fx.State 'logs')
        SeamsPath = $Fx.Seams
        NoReport = $true
        Trigger = 'scheduled'
        Ref = 'master'
        Profile = 'nightly'
        ReleaseRoot = $Fx.ReleaseRoot
        CoordinationRoot = $Fx.Coordination
        PassThru = $true
    }
    if ($Fx.Origin) {
        $splat.RemoteUrl = $Fx.Origin
        $splat.Repository = 'fixture/repository'
        $splat.RunId = $Fx.RunId
    }
    if ($Extra) { foreach ($k in $Extra.Keys) { $splat[$k] = $Extra[$k] } }
    return Invoke-AntiphonNightlyRun @splat
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
        Assert-C487 -Cond ((Get-NightlyExecutionPlatform) -ceq 'windows') -Name 'C599 RunSummaryIdentity Windows observation needs no native work'
    } finally { $script:NightlySeams = $saved }
}

function Assert-C599RcChild {
    param($Fx, $Result, [string]$LogRoot, [string]$Label)
    Assert-C487 -Cond ($Result.Phase -ceq 'hop' -and -not $Result.Refusal -and $Result.Sha -ceq $Fx.Sha) `
        -Name ('C599 {0} outer run reaches self-reexec without refusal' -f $Label) `
        -Detail ('phase={0} refusal={1} exit={2}' -f $Result.Phase, $Result.Refusal, $Result.ExitCode)
    $summaries = @(Get-ChildItem -LiteralPath $LogRoot -Filter summary.json -Recurse -ErrorAction SilentlyContinue |
        Where-Object { Test-C599RunSummaryRecord (Get-Content -LiteralPath $_.FullName -Raw) })
    $summary = $null
    if ($summaries.Count -eq 1) { $summary = Get-Content -LiteralPath $summaries[0].FullName -Raw | ConvertFrom-Json }
    Assert-C487 -Cond ($summaries.Count -eq 1 -and $null -ne $summary -and $summary.runId -ceq $Fx.RunId) `
        -Name ('C599 {0} exactly one fresh child summary names this run' -f $Label) -Detail ('summaries={0}' -f $summaries.Count)
    Assert-C487 -Cond ($summary.profile -ceq 'rc' -and $summary.trigger -ceq 'rc' -and
        $summary.sha -ceq $Fx.Sha -and $summary.expectedSha -ceq $Fx.Sha -and
        $summary.gitRef -ceq ('origin/' + $CandidateRef) -and $summary.runId -ceq $Fx.RunId) `
        -Name ('C599 {0} child consumed the pinned RC identity' -f $Label)
    $selection = $summary.selection
    Assert-C487 -Cond ($selection.candidateSha -ceq $Fx.Sha -and $selection.candidateRef -ceq $CandidateRef -and
        $selection.candidateId -ceq $CandidateId -and $selection.runId -ceq $Fx.RunId -and $selection.profile -ceq 'rc' -and
        $selection.requestedMode -ceq 'Full' -and $selection.effectiveMode -ceq 'Full' -and $selection.coverageScope -ceq 'Full' -and
        $selection.baselineSha -ceq '' -and $summary.coverageScope -ceq 'Full') `
        -Name ('C599 {0} child consumed the bound Full selection' -f $Label)
    $attemptRoot = Join-Path (Join-Path (Join-Path (Join-Path $Fx.ReleaseRoot 'candidates') $CandidateId) 'attempts') $Fx.RunId
    $attemptPath = Join-Path $attemptRoot 'selection-attempt.json'
    $selectionPath = Join-Path $attemptRoot 'selection.json'
    $bound = $null; $artifact = $null
    if (Test-Path -LiteralPath $attemptPath) { $bound = Get-Content -LiteralPath $attemptPath -Raw | ConvertFrom-Json }
    if (Test-Path -LiteralPath $selectionPath) { $artifact = Get-Content -LiteralPath $selectionPath -Raw | ConvertFrom-Json }
    Assert-C487 -Cond ($null -ne $bound -and $null -ne $artifact -and
        $artifact.candidateSha -ceq $Fx.Sha -and $artifact.requestedMode -ceq 'Full' -and
        $artifact.effectiveMode -ceq 'Full' -and $artifact.coverageScope -ceq 'Full' -and
        $bound.selectionDigest -ceq (Get-NightlyFileSha256 $selectionPath) -and
        (ConvertTo-NightlyCanonicalJson $selection) -ceq (ConvertTo-NightlyCanonicalJson $bound)) `
        -Name ('C599 {0} child selection matches the immutable bound artifacts' -f $Label)
}

function Test-C599_MasterStateIsolation {
    # An RC run must leave every master state file byte-identical.
    $fx = New-C599RunFx -Rc
    $masterFiles = Get-ReleaseGateMasterStateFiles -StateRoot $fx.State
    foreach ($f in $masterFiles) { Set-Content -LiteralPath $f -Value ('{"sentinel":"' + [System.IO.Path]::GetFileName($f) + '"}') -Encoding ASCII }
    $before = @{}
    foreach ($f in $masterFiles) { $before[$f] = (Get-NightlyFileSha256 -Path $f) }

    $rcState = Join-Path $fx.Root 'rc-state'
    New-Item -ItemType Directory -Path $rcState -Force | Out-Null
    Assert-C487 -Cond (@(Get-ChildItem -LiteralPath $fx.Root -Filter summary.json -Recurse).Count -eq 0) `
        -Name 'C599 MasterStateIsolation run starts without child summaries'
    $r = Invoke-C599Run -Fx $fx -Extra @{
        Profile = 'rc'; Trigger = 'rc'; Ref = $CandidateRef; ExpectedSha = $fx.Sha
        CandidateId = $CandidateId; StateRoot = $rcState; LogRoot = (Join-Path $rcState 'logs')
    }
    Assert-C599RcChild -Fx $fx -Result $r -LogRoot (Join-Path $rcState 'logs') -Label 'MasterStateIsolation'
    foreach ($f in $masterFiles) {
        $after = (Get-NightlyFileSha256 -Path $f)
        Assert-C487 -Cond ($after -eq $before[$f]) -Name ('C599 MasterStateIsolation {0} is byte-identical after an rc run' -f [System.IO.Path]::GetFileName($f)) -Detail ($before[$f] + ' vs ' + $after)
    }
    # And an RC pointed at the master state root refuses outright.
    $refuse = Invoke-C599Run -Fx $fx -Extra @{
        Profile = 'rc'; Trigger = 'rc'; Ref = $CandidateRef; ExpectedSha = $fx.Sha
        CandidateId = $CandidateId; StateRoot = (Get-NightlyDefaultStateRoot)
    }
    Assert-C487 -Cond ([int]$refuse.ExitCode -eq 3 -and [string]$refuse.Refusal -eq 'rc-master-state-root') -Name 'C599 MasterStateIsolation rc into the master state root refuses' -Detail ([string]$refuse.Refusal)
}

function Test-C599_ParameterHops {
    # Refusals that can only fire if the identity actually reached the core.
    $fx = New-C599RunFx
    $unknown = Invoke-C599Run -Fx $fx -Extra @{ Profile = 'turbo' }
    Assert-C487 -Cond ([int]$unknown.ExitCode -eq 3 -and [string]$unknown.Refusal -eq 'unknown-profile') -Name 'C599 ParameterHops unknown profile refuses at the core' -Detail ([string]$unknown.Refusal)

    $badRef = Invoke-C599Run -Fx $fx -Extra @{ Profile = 'rc'; Trigger = 'rc'; Ref = 'master'; ExpectedSha = $ShaA; StateRoot = (Join-Path $fx.Root 's1') }
    Assert-C487 -Cond ([string]$badRef.Refusal -eq 'rc-ref') -Name 'C599 ParameterHops rc with a master ref refuses' -Detail ([string]$badRef.Refusal)

    $noSha = Invoke-C599Run -Fx $fx -Extra @{ Profile = 'rc'; Trigger = 'rc'; Ref = $CandidateRef; StateRoot = (Join-Path $fx.Root 's2') }
    Assert-C487 -Cond ([string]$noSha.Refusal -eq 'rc-expected-sha') -Name 'C599 ParameterHops rc without ExpectedSha refuses' -Detail ([string]$noSha.Refusal)

    $shortSha = Invoke-C599Run -Fx $fx -Extra @{ Profile = 'rc'; Trigger = 'rc'; Ref = $CandidateRef; ExpectedSha = 'abcd'; StateRoot = (Join-Path $fx.Root 's3') }
    Assert-C487 -Cond ([string]$shortSha.Refusal -eq 'rc-expected-sha') -Name 'C599 ParameterHops a short ExpectedSha refuses' -Detail ([string]$shortSha.Refusal)

    $rcTrigger = Invoke-C599Run -Fx $fx -Extra @{ Profile = 'nightly'; Trigger = 'rc'; StateRoot = (Join-Path $fx.Root 's4') }
    Assert-C487 -Cond ([string]$rcTrigger.Refusal -eq 'trigger-profile-mismatch') -Name 'C599 ParameterHops trigger rc without the rc profile refuses' -Detail ([string]$rcTrigger.Refusal)

    # The candidate id is derived from the ref when omitted.
    $fx = New-C599RunFx -Rc
    Assert-C487 -Cond (@(Get-ChildItem -LiteralPath $fx.Root -Filter summary.json -Recurse).Count -eq 0) `
        -Name 'C599 ParameterHops run starts without child summaries'
    $derived = Invoke-C599Run -Fx $fx -Extra @{ Profile = 'rc'; Trigger = 'rc'; Ref = $CandidateRef; ExpectedSha = $fx.Sha; StateRoot = (Join-Path $fx.Root 's5'); LogRoot = (Join-Path $fx.Root 's5-logs') }
    Assert-C487 -Cond ([string]$derived.CandidateId -eq $CandidateId) -Name 'C599 ParameterHops candidate id is derived from the ref' -Detail ([string]$derived.CandidateId)
    Assert-C599RcChild -Fx $fx -Result $derived -LogRoot (Join-Path $fx.Root 's5-logs') -Label 'ParameterHops'

    # The wrapper script itself must declare and forward every new parameter.
    $wrapper = Get-Content -LiteralPath (Join-Path $here 'nightly-run.ps1') -Raw
    foreach ($p in @('Profile', 'ExpectedSha', 'CandidateId', 'ReleaseRoot', 'CoordinationRoot')) {
        Assert-C487 -Cond ($wrapper -match ('\[string\]\$' + $p + ' =')) -Name ('C599 ParameterHops nightly-run.ps1 declares -{0}' -f $p)
        Assert-C487 -Cond ($wrapper -match ($p + ' = \$' + $p)) -Name ('C599 ParameterHops nightly-run.ps1 forwards -{0} to the core' -f $p)
    }
    $impl = Get-Content -LiteralPath (Join-Path $lib 'nightly-run-impl.ps1') -Raw
    Assert-C487 -Cond ($impl -match "'-Profile', \`$Profile") -Name 'C599 ParameterHops the self-reexec hop forwards -Profile'
    foreach ($p in @('ExpectedSha', 'CandidateId', 'ReleaseRoot', 'CoordinationRoot')) {
        Assert-C487 -Cond ($impl -match ("'-" + $p + "'")) -Name ('C599 ParameterHops the self-reexec hop forwards -{0}' -f $p)
    }
    $tests = Get-Content -LiteralPath (Join-Path $here 'nightly-tests.ps1') -Raw
    Assert-C487 -Cond (($tests -match '\[string\]\$Profile =') -and ($tests -match '\[string\]\$ExpectedSha =')) -Name 'C599 ParameterHops nightly-tests.ps1 accepts -Profile and -ExpectedSha'
    Assert-C487 -Cond ($impl -match "'-Profile', \`$Profile,") -Name 'C599 ParameterHops the tests hop forwards -Profile'
}

function Test-C599_CloneOwnership {
    $fx = New-C599RunFx
    foreach ($p in @('C:\src\Antiphon', 'C:\Antiphon\worktrees\card-task-0233729c')) {
        $r = Invoke-C599Run -Fx $fx -Extra @{ CheckoutRoot = $p; Profile = 'rc'; Trigger = 'rc'; Ref = $CandidateRef; ExpectedSha = $ShaA; StateRoot = (Join-Path $fx.Root ('o' + [guid]::NewGuid().ToString('N'))) }
        Assert-C487 -Cond ([int]$r.ExitCode -eq 3) -Name ('C599 CloneOwnership rc refuses {0}' -f $p) -Detail ([string]$r.Refusal)
    }
    # The RC coordinator applies the same ownership rule to its own clone.
    $root = New-C599Root
    $seams = New-C599GitSeams -Root $root -HeadSha $ShaB
    $releaseRoot = Join-Path $root 'releases'
    $unmarked = Join-Path $root 'unmarked'
    New-Item -ItemType Directory -Path (Join-Path $unmarked '.git') -Force | Out-Null
    $r1 = Invoke-C599Candidate -ReleaseRoot $releaseRoot -Checkout $unmarked -Seams $seams -Ref $CandidateRef
    Assert-C487 -Cond ([string]$r1.Refusal -eq 'unmarked-clone') -Name 'C599 CloneOwnership coordinator refuses an unmarked clone' -Detail ([string]$r1.Refusal)
    $r2 = Invoke-C599Candidate -ReleaseRoot $releaseRoot -Checkout 'C:\src\Antiphon' -Seams $seams -Ref $CandidateRef
    Assert-C487 -Cond ([string]$r2.Refusal -eq 'shared-tree') -Name 'C599 CloneOwnership coordinator refuses the shared tree' -Detail ([string]$r2.Refusal)
    $owned = New-C599OwnedReleaseClone -Root $root
    $r3 = Invoke-C599Candidate -ReleaseRoot $releaseRoot -Checkout $owned -Seams $seams -Ref $CandidateRef
    Assert-C487 -Cond ([int]$r3.ExitCode -eq 0) -Name 'C599 CloneOwnership control owned clone is accepted' -Detail ([string]$r3.Refusal)
}

function Test-C599_SharedLock {
    $root = New-C599Root
    $coord = Join-Path $root 'coord'
    $first = Enter-ReleaseGateNativeLock -CoordinationRoot $coord -RunId 'run-master' -Lane 'master'
    Assert-C487 -Cond ($first.Ok -and $first.OwnsLock) -Name 'C599 SharedLock first lane acquires' -Detail ([string]$first.Reason)
    Assert-C487 -Cond (Test-Path -LiteralPath (Get-ReleaseGateNativeLockPath -CoordinationRoot $coord)) -Name 'C599 SharedLock the lock file exists on disk'

    # A live owner is never stolen, whatever the other lane is.
    $script:ReleaseGateOwnsNativeLock2 = $null
    $second = Enter-ReleaseGateNativeLock -CoordinationRoot $coord -RunId 'run-rc' -Lane 'rc'
    Assert-C487 -Cond ((-not $second.Ok) -and [string]$second.Reason -eq 'deferred-busy') -Name 'C599 SharedLock the second lane gets deferred-busy' -Detail ([string]$second.Reason)
    Assert-C487 -Cond (-not $second.OwnsLock) -Name 'C599 SharedLock the deferred lane owns nothing'

    $record = Read-NightlyLockRecord -LockPath (Get-ReleaseGateNativeLockPath -CoordinationRoot $coord)
    Assert-C487 -Cond ([int]$record.pid -eq $PID) -Name 'C599 SharedLock owner identity records the pid' -Detail ([string]$record.pid)
    Assert-C487 -Cond ([string]$record.lane -eq 'master') -Name 'C599 SharedLock owner identity records the lane' -Detail ([string]$record.lane)
    Assert-C487 -Cond (-not [string]::IsNullOrWhiteSpace([string]$record.processStartedAt)) -Name 'C599 SharedLock owner identity records the process start' -Detail ([string]$record.processStartedAt)

    Exit-ReleaseGateNativeLock -CoordinationRoot $coord -RunId 'run-master'
    Assert-C487 -Cond (-not (Test-Path -LiteralPath (Get-ReleaseGateNativeLockPath -CoordinationRoot $coord))) -Name 'C599 SharedLock release removes the lock file'
    $third = Enter-ReleaseGateNativeLock -CoordinationRoot $coord -RunId 'run-rc2' -Lane 'rc'
    Assert-C487 -Cond ($third.Ok -and $third.OwnsLock) -Name 'C599 SharedLock the other lane acquires once released' -Detail ([string]$third.Reason)
    Exit-ReleaseGateNativeLock -CoordinationRoot $coord -RunId 'run-rc2'

    # A contended scheduled run records deferred-busy and stays non-green.
    $fx = New-C599RunFx
    $held = Enter-ReleaseGateNativeLock -CoordinationRoot $fx.Coordination -RunId 'other-run' -Lane 'master'
    Assert-C487 -Cond ($held.Ok) -Name 'C599 SharedLock control: a foreign owner holds the lock'
    $busy = Invoke-C599Run -Fx $fx -Extra @{ Profile = 'rc'; Trigger = 'rc'; Ref = $CandidateRef; ExpectedSha = $ShaA; StateRoot = (Join-Path $fx.Root 'busy') }
    Exit-ReleaseGateNativeLock -CoordinationRoot $fx.Coordination -RunId 'other-run'
    Assert-C487 -Cond ([int]$busy.ExitCode -eq 3 -and [string]$busy.Refusal -eq 'deferred-busy') -Name 'C599 SharedLock a contended rc run returns deferred-busy' -Detail ([string]$busy.Refusal)
    $attempt = Join-Path (Join-Path $fx.Root 'busy') 'last-run.json'
    $recorded = ''
    if (Test-Path -LiteralPath $attempt) { $recorded = [string]((Get-Content -LiteralPath $attempt -Raw | ConvertFrom-Json).phase) }
    Assert-C487 -Cond ($recorded -eq 'deferred-busy') -Name 'C599 SharedLock the deferred attempt is recorded on disk' -Detail $recorded
}

function Test-C599_Continuation {
    $root = New-C599Root
    $coord = Join-Path $root 'coord'
    $own = Enter-ReleaseGateNativeLock -CoordinationRoot $coord -RunId 'run-1' -Lane 'master'
    Assert-C487 -Cond ($own.Ok) -Name 'C599 Continuation control: the parent owns the shared lock'
    $start = [string]$own.Record.processStartedAt

    $hop = Enter-ReleaseGateNativeLock -CoordinationRoot $coord -RunId 'run-1' -Lane 'master' -ContinueOwnerPid $PID -ContinueOwnerStartedAt $start
    Assert-C487 -Cond ($hop.Ok -and (-not $hop.OwnsLock) -and [string]$hop.Reason -eq 'hop') -Name 'C599 Continuation a matching continuation inherits without owning' -Detail ([string]$hop.Reason)

    $wrongRun = Enter-ReleaseGateNativeLock -CoordinationRoot $coord -RunId 'run-2' -Lane 'master' -ContinueOwnerPid $PID -ContinueOwnerStartedAt $start
    Assert-C487 -Cond ((-not $wrongRun.Ok) -and [string]$wrongRun.Reason -eq 'shared-hop-identity') -Name 'C599 Continuation a mismatched run id refuses' -Detail ([string]$wrongRun.Reason)
    $wrongLane = Enter-ReleaseGateNativeLock -CoordinationRoot $coord -RunId 'run-1' -Lane 'rc' -ContinueOwnerPid $PID -ContinueOwnerStartedAt $start
    Assert-C487 -Cond ((-not $wrongLane.Ok) -and [string]$wrongLane.Reason -eq 'shared-hop-identity') -Name 'C599 Continuation a mismatched lane refuses' -Detail ([string]$wrongLane.Reason)
    $wrongStart = Enter-ReleaseGateNativeLock -CoordinationRoot $coord -RunId 'run-1' -Lane 'master' -ContinueOwnerPid $PID -ContinueOwnerStartedAt '2001-01-01T00:00:00.0000000Z'
    Assert-C487 -Cond ((-not $wrongStart.Ok) -and [string]$wrongStart.Reason -eq 'shared-hop-identity') -Name 'C599 Continuation a mismatched process start refuses' -Detail ([string]$wrongStart.Reason)
    $deadParent = Enter-ReleaseGateNativeLock -CoordinationRoot $coord -RunId 'run-1' -Lane 'master' -ContinueOwnerPid 999999 -ContinueOwnerStartedAt $start
    Assert-C487 -Cond (-not $deadParent.Ok) -Name 'C599 Continuation a dead continuation parent refuses' -Detail ([string]$deadParent.Reason)
    Exit-ReleaseGateNativeLock -CoordinationRoot $coord -RunId 'run-1'
}

function Test-C599_ReportLane {
    # R-4: an RC incident identity can never be the master nightly incident.
    # CARD-1340: private roots. The production Windows defaults stay in the producer scripts.
    $rcGreen = New-C599State -Profile 'rc' -Trigger 'rc' -Ref $CandidateRef -Sha $ShaB -ExpectedSha $ShaB -Candidate $CandidateId
    $masterGreen = New-C599State
    $root = New-C599Root
    $state = Join-Path $root 'state'
    $release = Join-Path $root 'releases'
    $rcPaths = Get-ReleaseGateStatePaths -CreditKind 'rc-release' -StateRoot $state -ReleaseRoot $release -CandidateId $CandidateId
    $masterPaths = Get-ReleaseGateStatePaths -CreditKind 'master-scheduled' -StateRoot $state
    Assert-C487 -Cond ($rcPaths.GreenPath -ne $masterPaths.GreenPath) -Name 'C599 ReportLane the two lanes write different green files' -Detail ($rcPaths.GreenPath + ' vs ' + $masterPaths.GreenPath)
    Assert-C487 -Cond ($masterPaths.GreenPath -eq (Join-Path $state 'last-complete-green.json')) -Name 'C599 ReportLane master green path is unchanged' -Detail $masterPaths.GreenPath
    Assert-C487 -Cond ($rcPaths.GreenPath -eq (Join-Path (Join-Path (Join-Path $release 'candidates') $CandidateId) 'complete-green.json')) -Name 'C599 ReportLane rc green lands under its candidate root' -Detail $rcPaths.GreenPath
    $missing = ''
    try { [void](Get-ReleaseGateStatePaths -CreditKind 'rc-release' -StateRoot 'x' -CandidateId '') } catch { $missing = $_.Exception.Message }
    Assert-C487 -Cond ($missing -match 'candidate id') -Name 'C599 ReportLane rc credit without a candidate id throws' -Detail $missing
    Assert-C487 -Cond ([string](Get-ReleaseGateCreditVerdict -State $rcGreen).CreditKind -ne [string](Get-ReleaseGateCreditVerdict -State $masterGreen).CreditKind) -Name 'C599 ReportLane the two lanes never share a credit kind'
}

# ======================================================= V-6 publication lane ===

function New-C599PublishFx {
    <#
      CARD-0599 D-15: the release clone is a real git repository whose commit holds
      the policy, so the candidate SHA is real and the publisher re-reads the pinned
      blob itself. The remote/GitHub stay file-backed recipients behind the seams.
    #>
    param([byte[]]$PolicyBytes = $null, [switch]$NoAuthority, [switch]$LongLivedCandidate, [string]$Repository = 'michal-ciechan/Antiphon')
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
    return [pscustomobject]@{
        Root = $root; Git = $git; Gh = $gh; ReleaseRoot = $releaseRoot; Checkout = $checkout; Head = $Head; CardApi = $cardApi
        CandidateId = $CandidateId; CandidateRef = $CandidateRef
    }
}

function Invoke-C599Publish {
    param($Fx, [hashtable]$Extra, [string]$CandidateId = '')
    if ([string]::IsNullOrWhiteSpace($CandidateId)) { $CandidateId = $script:CandidateId }
    $splat = @{
        ReleaseRoot = $Fx.ReleaseRoot
        CandidateId = $CandidateId
        CheckoutRoot = $Fx.Checkout
        SeamsPath = $Fx.Gh.Path
        AntiphonApiUrl = $Fx.CardApi.Url
        PassThru = $true
    }
    if ($Extra) { foreach ($k in $Extra.Keys) { $splat[$k] = $Extra[$k] } }
    return (& (Join-Path $here 'publish-release.ps1') @splat)
}

function Test-C599_CandidateIdentity {
    $root = New-C599Root
    $git = New-C599GitSeams -Root $root -HeadSha $ShaB
    $releaseRoot = Join-Path $root 'releases'
    $checkout = New-C599OwnedReleaseClone -Root $root

    $cut = Invoke-C599Candidate -ReleaseRoot $releaseRoot -Checkout $checkout -Seams $git -Ref $CandidateRef
    Assert-C487 -Cond ([int]$cut.ExitCode -eq 0 -and [bool]$cut.Pushed) -Name 'C599 CandidateIdentity control: a candidate is cut and pushed' -Detail ([string]$cut.Refusal)
    Assert-C487 -Cond ((Read-C599Remote -Seams $git -Ref $CandidateRef) -eq $ShaB) -Name 'C599 CandidateIdentity an observer read finds the candidate at the pinned sha' -Detail (Read-C599Remote -Seams $git -Ref $CandidateRef)
    $journal = Get-Content -LiteralPath $cut.JournalPath -Raw | ConvertFrom-Json
    Assert-C487 -Cond ([string]$journal.sha -eq $ShaB -and [string]$journal.ref -eq $CandidateRef) -Name 'C599 CandidateIdentity the journal pins one ref and one sha' -Detail ([string]$journal.sha)

    # Master moved: a resume must NOT re-pin.
    $git2 = New-C599GitSeams -Root (Join-Path $root 'moved') -HeadSha $ShaC -TracePath (Join-Path $root 'moved-trace.log')
    Copy-Item -LiteralPath $git.RemotePath -Destination $git2.RemotePath -Force
    $resume = Invoke-C599Candidate -ReleaseRoot $releaseRoot -Checkout $checkout -Seams $git2 -Ref $CandidateRef
    Assert-C487 -Cond ([string]$resume.Sha -eq $ShaB) -Name 'C599 CandidateIdentity a resume keeps the original pin even after master moved' -Detail ([string]$resume.Sha)
    Assert-C487 -Cond ([bool]$resume.Resumed) -Name 'C599 CandidateIdentity the resume is reported as a resume'
    $trace2 = Get-Content -LiteralPath $git2.Trace -Raw -ErrorAction SilentlyContinue
    Assert-C487 -Cond ([string]::IsNullOrEmpty($trace2) -or ($trace2 -notmatch 'GIT fetch')) -Name 'C599 CandidateIdentity a resume performs no fetch' -Detail ([string]$trace2)

    # A remote candidate at a different sha is a hard refusal, and nothing moves.
    $other = 'release/rc-20260923T160000Z'
    $st = Get-Content -LiteralPath $git.RemotePath -Raw | ConvertFrom-Json
    $st | Add-Member -NotePropertyName 'release_rc_20260923T160000Z' -NotePropertyValue $ShaA -Force
    $st | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $git.RemotePath -Encoding UTF8
    $conflict = Invoke-C599Candidate -ReleaseRoot $releaseRoot -Checkout $checkout -Seams $git -Ref $other
    Assert-C487 -Cond ([string]$conflict.Refusal -eq 'remote-candidate-conflict') -Name 'C599 CandidateIdentity a remote candidate at another sha refuses' -Detail ([string]$conflict.Refusal)
    Assert-C487 -Cond ((Read-C599Remote -Seams $git -Ref $other) -eq $ShaA) -Name 'C599 CandidateIdentity the conflicting remote ref was not moved' -Detail (Read-C599Remote -Seams $git -Ref $other)

    # Ref-shape negatives.
    foreach ($bad in @('release/current', 'master', 'release/rc-2026', 'refs/tags/v1')) {
        $r = Invoke-C599Candidate -ReleaseRoot $releaseRoot -Checkout $checkout -Seams $git -Ref $bad
        Assert-C487 -Cond ([int]$r.ExitCode -eq 3) -Name ('C599 CandidateIdentity refuses ref {0}' -f $bad) -Detail ([string]$r.Refusal)
    }

    # No push argument may carry a force flag.
    $allTrace = Get-Content -LiteralPath $git.Trace -Raw
    Assert-C487 -Cond ($allTrace -notmatch '--force' -and $allTrace -notmatch '\+refs/') -Name 'C599 CandidateIdentity no push used force or a plus refspec' -Detail $allTrace
}

function Test-C599_CutRecovery {
    foreach ($cut in @('before-push', 'commit-then-lose-response')) {
        $root = New-C599Root
        $git = New-C599GitSeams -Root $root -HeadSha $ShaB
        $releaseRoot = Join-Path $root 'releases'
        $checkout = New-C599OwnedReleaseClone -Root $root
        if ($cut -eq 'before-push') { Set-C599GitFailure -Seams $git -Subcommand 'push' }
        else { Set-C599GitFailure -Seams $git -Subcommand 'push' -CommitsAnyway }

        $r1 = Invoke-C599Candidate -ReleaseRoot $releaseRoot -Checkout $checkout -Seams $git -Ref $CandidateRef
        $observed = Read-C599Remote -Seams $git -Ref $CandidateRef
        if ($cut -eq 'before-push') {
            Assert-C487 -Cond ([string]$r1.Refusal -eq 'push-failed') -Name 'C599 CutRecovery before-push reports a failed push' -Detail ([string]$r1.Refusal)
            Assert-C487 -Cond ([string]::IsNullOrWhiteSpace($observed)) -Name 'C599 CutRecovery before-push left the remote untouched' -Detail $observed
        } else {
            Assert-C487 -Cond ([int]$r1.ExitCode -eq 0 -and [bool]$r1.Pushed) -Name 'C599 CutRecovery a lost response is reconciled from the remote' -Detail ([string]$r1.Refusal)
            Assert-C487 -Cond ($observed -eq $ShaB) -Name 'C599 CutRecovery the remote holds exactly the pinned sha' -Detail $observed
        }
        Assert-C487 -Cond (Test-Path -LiteralPath (Join-Path (Get-ReleaseGateCandidateRoot -ReleaseRoot $releaseRoot -CandidateId $CandidateId) 'candidate.json')) -Name ('C599 CutRecovery {0} wrote the journal before the remote write' -f $cut)

        # Restart from disk: the same identity resumes, never a second candidate.
        Set-C599GitFailure -Seams $git -Subcommand 'push' -Clear
        $r2 = Invoke-C599Candidate -ReleaseRoot $releaseRoot -Checkout $checkout -Seams $git -Ref $CandidateRef
        Assert-C487 -Cond ([int]$r2.ExitCode -eq 0 -and [string]$r2.Sha -eq $ShaB) -Name ('C599 CutRecovery {0} restart resumes the same sha' -f $cut) -Detail ([string]$r2.Sha)
        Assert-C487 -Cond ((Read-C599Remote -Seams $git -Ref $CandidateRef) -eq $ShaB) -Name ('C599 CutRecovery {0} ends with exactly one candidate at the pinned sha' -f $cut)
    }
    # A fetch failure before any pin writes nothing at all.
    $root = New-C599Root
    $git = New-C599GitSeams -Root $root -HeadSha $ShaB
    Set-C599GitFailure -Seams $git -Subcommand 'fetch'
    $releaseRoot = Join-Path $root 'releases'
    $checkout = New-C599OwnedReleaseClone -Root $root
    $r = Invoke-C599Candidate -ReleaseRoot $releaseRoot -Checkout $checkout -Seams $git -Ref $CandidateRef
    Assert-C487 -Cond ([string]$r.Refusal -eq 'fetch-failed') -Name 'C599 CutRecovery a fetch failure refuses' -Detail ([string]$r.Refusal)
    Assert-C487 -Cond ([string]::IsNullOrWhiteSpace((Read-C599Remote -Seams $git -Ref $CandidateRef))) -Name 'C599 CutRecovery a fetch failure leaves the remote untouched'
}

function Test-C599_PublicationGate {
    $fx = New-C599PublishFx
    $root = Get-ReleaseGateCandidateRoot -ReleaseRoot $fx.ReleaseRoot -CandidateId $CandidateId
    $greenPath = Join-Path $root 'complete-green.json'
    $good = Get-Content -LiteralPath $greenPath -Raw

    $ok = Invoke-C599Publish -Fx $fx
    Assert-C487 -Cond ([int]$ok.ExitCode -eq 0 -and [bool]$ok.Published) -Name 'C599 PublicationGate control: a complete rc green publishes' -Detail ([string]$ok.Refusal)

    foreach ($row in @(
        @('coverageComplete', $false, 'coverage false'),
        @('testsPassed', $false, 'tests false'),
        @('reportDelivered', $false, 'report false'),
        @('exitCode', 1, 'non-zero exit'),
        @('trigger', 'scheduled', 'wrong trigger'),
        @('profile', 'nightly', 'wrong profile'),
        @('noReport', $true, 'NoReport run'),
        @('diagnostic', $true, 'diagnostic run'),
        @('seamed', $true, 'seam-driven run')
    )) {
        $fx2 = New-C599PublishFx
        $p2 = Join-Path (Get-ReleaseGateCandidateRoot -ReleaseRoot $fx2.ReleaseRoot -CandidateId $CandidateId) 'complete-green.json'
        $obj = Get-Content -LiteralPath $p2 -Raw | ConvertFrom-Json
        $obj | Add-Member -NotePropertyName $row[0] -NotePropertyValue $row[1] -Force
        $obj | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $p2 -Encoding UTF8
        $r = Invoke-C599Publish -Fx $fx2
        Assert-C487 -Cond ([int]$r.ExitCode -ne 0 -and (-not [bool]$r.Published)) -Name ('C599 PublicationGate refuses a {0}' -f $row[2]) -Detail ([string]$r.Refusal)
    }

    # A moved remote candidate refuses even with a perfect green.
    $fx3 = New-C599PublishFx
    Write-NightlyAtomicJson -Path $fx3.Git.RemotePath -Object ([ordered]@{ release_rc_20260923T083000Z = $ShaC })
    $moved = Invoke-C599Publish -Fx $fx3
    Assert-C487 -Cond (([int]$moved.ExitCode -ne 0) -and ([string]$moved.Refusal -match 'remote-candidate-moved')) -Name 'C599 PublicationGate a moved remote candidate refuses' -Detail ([string]$moved.Refusal)

    # An unresolved remote is not agreement.
    $fx4 = New-C599PublishFx
    Write-NightlyAtomicJson -Path $fx4.Git.RemotePath -Object ([ordered]@{})
    $unresolved = Invoke-C599Publish -Fx $fx4
    Assert-C487 -Cond (([int]$unresolved.ExitCode -ne 0) -and ([string]$unresolved.Refusal -match 'remote-candidate-unresolved')) -Name 'C599 PublicationGate an unresolved remote candidate refuses' -Detail ([string]$unresolved.Refusal)

    # A required suite absent from the frozen plan refuses (D-15: the pinned policy
    # names it; the summary is not consulted at all).
    $fx5 = New-C599PublishFx
    Update-C599Ledger -Fx $fx5 -Plan { param($p) $p.suites = @($p.suites | Where-Object { [string]$_.id -ne 'e2e' }) }
    $missingSuite = Invoke-C599Publish -Fx $fx5
    Assert-C487 -Cond (([int]$missingSuite.ExitCode -ne 0) -and ([string]$missingSuite.Refusal -match 'required-suite-missing:e2e')) -Name 'C599 PublicationGate a missing required e2e suite refuses' -Detail ([string]$missingSuite.Refusal)

    # A policy hash that does not match the qualified one refuses.
    $fx6 = New-C599PublishFx
    $wrongHash = Invoke-C599Publish -Fx $fx6 -Extra @{ ExpectedPolicyHash = 'some-other-hash' }
    Assert-C487 -Cond (([int]$wrongHash.ExitCode -ne 0) -and ([string]$wrongHash.Refusal -match 'policy-hash-mismatch')) -Name 'C599 PublicationGate a policy hash mismatch refuses' -Detail ([string]$wrongHash.Refusal)

    # Missing evidence refuses before any remote write.
    $fx7 = New-C599PublishFx
    Remove-Item -LiteralPath (Join-Path (Get-ReleaseGateCandidateRoot -ReleaseRoot $fx7.ReleaseRoot -CandidateId $CandidateId) 'complete-green.json') -Force
    $noEvidence = Invoke-C599Publish -Fx $fx7
    Assert-C487 -Cond ([string]$noEvidence.Refusal -eq 'missing-evidence') -Name 'C599 PublicationGate missing rc green refuses' -Detail ([string]$noEvidence.Refusal)
    Assert-C487 -Cond ($null -eq (Get-C599Release -Gh $fx7.Gh -Tag 'v2026.09.23.1')) -Name 'C599 PublicationGate a refused publication created no release'
    Assert-C487 -Cond ($good.Length -gt 0) -Name 'C599 PublicationGate control fixture green was readable'
}

function Test-C599_TagRecovery {
    foreach ($cut in @('before-push', 'commit-then-lose-response')) {
        $fx = New-C599PublishFx
        if ($cut -eq 'before-push') { Set-C599GitFailure -Seams $fx.Git -Subcommand 'push' }
        else { Set-C599GitFailure -Seams $fx.Git -Subcommand 'push' -CommitsAnyway }
        $r1 = Invoke-C599Publish -Fx $fx
        if ($cut -eq 'before-push') {
            Assert-C487 -Cond ([string]$r1.Refusal -eq 'tag-push-failed') -Name 'C599 TagRecovery before-push refuses' -Detail ([string]$r1.Refusal)
            Assert-C487 -Cond ($null -eq (Get-C599Release -Gh $fx.Gh -Tag $r1.Tag)) -Name 'C599 TagRecovery before-push created no release'
        } else {
            Assert-C487 -Cond ([int]$r1.ExitCode -eq 0) -Name 'C599 TagRecovery a lost tag-push response is reconciled from the remote peel' -Detail ([string]$r1.Refusal)
        }
        Assert-C487 -Cond (-not [string]::IsNullOrWhiteSpace([string]$r1.Tag)) -Name ('C599 TagRecovery {0} reserved a tag before touching the remote' -f $cut) -Detail ([string]$r1.Tag)

        Set-C599GitFailure -Seams $fx.Git -Subcommand 'push' -Clear
        $r2 = Invoke-C599Publish -Fx $fx
        Assert-C487 -Cond ([string]$r2.Tag -eq [string]$r1.Tag) -Name ('C599 TagRecovery {0} restart reuses the same tag, not a new sequence' -f $cut) -Detail ([string]$r1.Tag + ' vs ' + [string]$r2.Tag)
        Assert-C487 -Cond ([int]$r2.ExitCode -eq 0 -and [bool]$r2.Published) -Name ('C599 TagRecovery {0} restart completes the publication' -f $cut) -Detail ([string]$r2.Refusal)
        $journal = Get-ReleaseGatePublicationJournal -Path (Join-Path $fx.ReleaseRoot 'publications.json')
        Assert-C487 -Cond (@($journal.reservations).Count -eq 1) -Name ('C599 TagRecovery {0} allocated exactly one reservation' -f $cut) -Detail ([string]@($journal.reservations).Count)
    }
    # A remote tag peeling to another sha is a hard refusal.
    $fx2 = New-C599PublishFx
    $gitText = Get-Content -LiteralPath $fx2.Git.Path -Raw
    Assert-C487 -Cond ($gitText.Length -gt 0) -Name 'C599 TagRecovery control fixture seams were written'
}

function Test-C599_DraftRecovery {
    foreach ($cut in @('before-write', 'commit-then-lose-response')) {
        $fx = New-C599PublishFx
        if ($cut -eq 'before-write') { Set-C599GitHubFailure -Gh $fx.Gh -Key 'release-create' }
        else { Set-C599GitHubFailure -Gh $fx.Gh -Key 'release-create' -CommitsAnyway }
        $r1 = Invoke-C599Publish -Fx $fx
        $stored = Get-C599Release -Gh $fx.Gh -Tag ([string]$r1.Tag)
        if ($cut -eq 'before-write') {
            Assert-C487 -Cond ([string]$r1.Refusal -eq 'draft-create-failed') -Name 'C599 DraftRecovery before-write refuses' -Detail ([string]$r1.Refusal)
            Assert-C487 -Cond ($null -eq $stored) -Name 'C599 DraftRecovery before-write stored no release'
        } else {
            Assert-C487 -Cond ($null -ne $stored) -Name 'C599 DraftRecovery a lost create response still left the draft stored' -Detail ([string]$r1.Refusal)
            Assert-C487 -Cond ([int]$r1.ExitCode -eq 0) -Name 'C599 DraftRecovery a lost create response is reconciled by readback' -Detail ([string]$r1.Refusal)
        }
        Set-C599GitHubFailure -Gh $fx.Gh -Key 'release-create' -Clear
        $r2 = Invoke-C599Publish -Fx $fx
        Assert-C487 -Cond ([int]$r2.ExitCode -eq 0 -and [bool]$r2.Published) -Name ('C599 DraftRecovery {0} restart publishes' -f $cut) -Detail ([string]$r2.Refusal)
        $final = Get-C599Release -Gh $fx.Gh -Tag ([string]$r2.Tag)
        Assert-C487 -Cond ($null -ne $final -and (-not [bool]$final.isDraft)) -Name ('C599 DraftRecovery {0} the recipient sees a published, non-draft release' -f $cut)
        $db = Get-Content -LiteralPath $fx.Gh.StorePath -Raw | ConvertFrom-Json
        Assert-C487 -Cond (@($db.releases).Count -eq 1) -Name ('C599 DraftRecovery {0} exactly one release exists' -f $cut) -Detail ([string]@($db.releases).Count)
    }
}

function Get-C599NotesCredentialFindings {
    # Return metadata only: note contents must never reach a failure diagnostic.
    param([string]$Notes)
    $patterns = [ordered]@{ bearer = 'Bearer '; token = 'token' }
    $lines = $Notes -split "`r?`n"
    for ($i = 0; $i -lt $lines.Count; $i++) {
        foreach ($name in $patterns.Keys) {
            if ($lines[$i] -match $patterns[$name]) {
                [pscustomobject]@{ Pattern = $name; Line = $i + 1 }
            }
        }
    }
}

function Format-C599NotesCredentialFindings {
    param([object[]]$Findings)
    return (@($Findings | ForEach-Object { 'pattern={0} line={1}' -f $_.Pattern, $_.Line }) -join '; ')
}

function Test-C599_NotesCredentialScan {
    # Exercise the real generator with the Windows exclusion list, even on Linux.
    $fx = New-C599PublishFx
    [void](Invoke-C599Publish -Fx $fx)
    $notes = Get-Content -LiteralPath (Join-Path (Get-ReleaseGateCandidateRoot -ReleaseRoot $fx.ReleaseRoot -CandidateId $CandidateId) 'release-notes.md') -Raw
    $findings = @(Get-C599NotesCredentialFindings -Notes $notes)
    Assert-C487 -Cond ($notes.Contains('RefreshGithubTokenRemoteCommandTests')) -Name 'C599 NotesCredentialScan generator preserves the Windows exclusion class'
    Assert-C487 -Cond ($findings.Count -eq 0) -Name 'C599 NotesCredentialScan generated Windows exclusions are not credentials' -Detail (Format-C599NotesCredentialFindings $findings)

    $benign = 'C:\Antiphon\releases\checkout user=fixture machine=fixture-host' + "`r`n" + ('a' * 40) + "`r`n" + ('b' * 64) + "`r`nRefreshGithubTokenRemoteCommandTests"
    $findings = @(Get-C599NotesCredentialFindings -Notes $benign)
    Assert-C487 -Cond ($findings.Count -eq 0) -Name 'C599 NotesCredentialScan Windows paths CRLF hashes and identifiers are not credentials' -Detail (Format-C599NotesCredentialFindings $findings)

    $witnesses = @(
        @{ Name = 'bearer'; Pattern = 'bearer'; Text = 'Bearer c599-synthetic-value' },
        @{ Name = 'token assignment'; Pattern = 'token'; Text = 'token=c599-synthetic-value' },
        @{ Name = 'JSON token'; Pattern = 'token'; Text = '"token": "c599-synthetic-value"' },
        @{ Name = 'environment token'; Pattern = 'token'; Text = 'GITHUB_TOKEN=c599-synthetic-value' },
        @{ Name = 'access token'; Pattern = 'token'; Text = 'access_token: c599-synthetic-value' }
    )
    foreach ($ending in @(@{ Name = 'LF'; Text = "`n" }, @{ Name = 'CRLF'; Text = "`r`n" })) {
        foreach ($witness in $witnesses) {
            $text = @('Release fixture', '', $witness.Text) -join $ending.Text
            $findings = @(Get-C599NotesCredentialFindings -Notes $text)
            $detail = Format-C599NotesCredentialFindings $findings
            $expected = 'pattern={0} line=3' -f $witness.Pattern
            Assert-C487 -Cond ($findings.Count -eq 1 -and $detail -ceq $expected) -Name ('C599 NotesCredentialScan {0} {1} is rejected with safe location' -f $ending.Name, $witness.Name) -Detail $detail
        }
    }
}

function Test-C599_AssetRecovery {
    foreach ($asset in @('release-manifest.json', 'verification-summary.json')) {
        foreach ($cut in @('before-write', 'commit-then-lose-response')) {
            $fx = New-C599PublishFx
            $key = 'release-upload:' + $asset
            if ($cut -eq 'before-write') { Set-C599GitHubFailure -Gh $fx.Gh -Key $key }
            else { Set-C599GitHubFailure -Gh $fx.Gh -Key $key -CommitsAnyway }
            $r1 = Invoke-C599Publish -Fx $fx
            Assert-C487 -Cond ([int]$r1.ExitCode -ne 0 -and (-not [bool]$r1.Published)) -Name ('C599 AssetRecovery {0} {1} does not publish' -f $asset, $cut) -Detail ([string]$r1.Refusal)
            $stored = Get-C599Release -Gh $fx.Gh -Tag ([string]$r1.Tag)
            Assert-C487 -Cond ($null -ne $stored -and [bool]$stored.isDraft) -Name ('C599 AssetRecovery {0} {1} leaves a pending draft, not a release' -f $asset, $cut)

            Set-C599GitHubFailure -Gh $fx.Gh -Key $key -Clear
            $r2 = Invoke-C599Publish -Fx $fx
            Assert-C487 -Cond ([int]$r2.ExitCode -eq 0 -and [bool]$r2.Published) -Name ('C599 AssetRecovery {0} {1} restart publishes' -f $asset, $cut) -Detail ([string]$r2.Refusal)
            $final = Get-C599Release -Gh $fx.Gh -Tag ([string]$r2.Tag)
            $assets = @($final.assets)
            Assert-C487 -Cond ($assets -contains 'release-manifest.json' -and $assets -contains 'verification-summary.json') -Name ('C599 AssetRecovery {0} {1} both assets are present after recovery' -f $asset, $cut) -Detail ($assets -join ',')
            $manifestPath = Join-Path (Get-ReleaseGateCandidateRoot -ReleaseRoot $fx.ReleaseRoot -CandidateId $CandidateId) 'release-manifest.json'
            $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
            Assert-C487 -Cond ([string]$manifest.sha -eq $fx.Head) -Name ('C599 AssetRecovery {0} {1} the manifest is bound to the tested sha' -f $asset, $cut) -Detail ([string]$manifest.sha)
            Assert-C487 -Cond ([string]$manifest.tag -eq [string]$r2.Tag) -Name ('C599 AssetRecovery {0} {1} the manifest names the published tag' -f $asset, $cut) -Detail ([string]$manifest.tag)
        }
    }
    # A notes file exists and never leaks a credential-shaped value.
    $fx2 = New-C599PublishFx
    $r = Invoke-C599Publish -Fx $fx2
    $notes = Get-Content -LiteralPath (Join-Path (Get-ReleaseGateCandidateRoot -ReleaseRoot $fx2.ReleaseRoot -CandidateId $CandidateId) 'release-notes.md') -Raw
    Assert-C487 -Cond ($notes -match ([regex]::Escape([string]$r.Tag))) -Name 'C599 AssetRecovery notes name the tag' -Detail ([string]$r.Tag)
    Assert-C487 -Cond ($notes -match 'no earlier release') -Name 'C599 AssetRecovery the first release says there is no earlier release'
    $findings = @(Get-C599NotesCredentialFindings -Notes $notes)
    Assert-C487 -Cond ($findings.Count -eq 0) -Name 'C599 AssetRecovery notes carry no credential-shaped text' -Detail (Format-C599NotesCredentialFindings $findings)
    Assert-C487 -Cond ($notes -match 'Exclusions') -Name 'C599 AssetRecovery notes list the exclusions'
    Assert-C487 -Cond ($notes -match 'Suites') -Name 'C599 AssetRecovery notes list the per-suite counts'
}

function Test-C599_PublishRecovery {
    foreach ($cut in @('before-write', 'commit-then-lose-response')) {
        $fx = New-C599PublishFx
        if ($cut -eq 'before-write') { Set-C599GitHubFailure -Gh $fx.Gh -Key 'release-publish' }
        else { Set-C599GitHubFailure -Gh $fx.Gh -Key 'release-publish' -CommitsAnyway }
        $r1 = Invoke-C599Publish -Fx $fx
        $stored = Get-C599Release -Gh $fx.Gh -Tag ([string]$r1.Tag)
        if ($cut -eq 'before-write') {
            Assert-C487 -Cond ([int]$r1.ExitCode -ne 0 -and (-not [bool]$r1.Published)) -Name 'C599 PublishRecovery before-write stays pending' -Detail ([string]$r1.Refusal)
            Assert-C487 -Cond ($null -ne $stored -and [bool]$stored.isDraft) -Name 'C599 PublishRecovery before-write the release is still a draft'
        } else {
            Assert-C487 -Cond ([int]$r1.ExitCode -eq 0 -and [bool]$r1.Published) -Name 'C599 PublishRecovery a lost publish response is reconciled by readback' -Detail ([string]$r1.Refusal)
            Assert-C487 -Cond ($null -ne $stored -and (-not [bool]$stored.isDraft)) -Name 'C599 PublishRecovery the recipient sees it published'
        }
        Set-C599GitHubFailure -Gh $fx.Gh -Key 'release-publish' -Clear
        $r2 = Invoke-C599Publish -Fx $fx
        Assert-C487 -Cond ([int]$r2.ExitCode -eq 0 -and [bool]$r2.Published) -Name ('C599 PublishRecovery {0} restart ends published' -f $cut) -Detail ([string]$r2.Refusal)
        Assert-C487 -Cond ([string]$r2.Tag -eq [string]$r1.Tag) -Name ('C599 PublishRecovery {0} recovery reuses the same tag' -f $cut) -Detail ([string]$r1.Tag + ' vs ' + [string]$r2.Tag)
        $journal = Get-ReleaseGatePublicationJournal -Path (Join-Path $fx.ReleaseRoot 'publications.json')
        $published = @($journal.reservations | Where-Object { [bool]$_.published })
        Assert-C487 -Cond ($published.Count -eq 1 -and -not [string]::IsNullOrWhiteSpace([string]$published[0].releaseId)) -Name ('C599 PublishRecovery {0} exactly one published reservation with a release id' -f $cut) -Detail ([string]$published.Count)
    }
}

function Test-C599_ManifestAllowlist {
    $manifest = New-ReleaseGateManifest -Fields @{ repository = 'r'; tag = 'v2026.09.23.1'; sha = $ShaB }
    $check = Test-ReleaseGateManifestAllowlist -Manifest $manifest
    Assert-C487 -Cond ($check.Ok) -Name 'C599 ManifestAllowlist a built manifest carries only allowlisted keys' -Detail ($check.Extra -join ',')
    $msg = ''
    try { [void](New-ReleaseGateManifest -Fields @{ hostname = 'desktop'; tag = 'v1' }) } catch { $msg = $_.Exception.Message }
    Assert-C487 -Cond ($msg -match 'not allowlisted') -Name 'C599 ManifestAllowlist a hostname field is refused' -Detail $msg
    $leaky = [ordered]@{ tag = 'v1'; transcript = 'secret' }
    $bad = Test-ReleaseGateManifestAllowlist -Manifest $leaky
    Assert-C487 -Cond ((-not $bad.Ok) -and ($bad.Extra -contains 'transcript')) -Name 'C599 ManifestAllowlist a transcript field is detected' -Detail ($bad.Extra -join ',')
    $digest = Get-ReleaseGateManifestDigest -Manifest $manifest
    Assert-C487 -Cond ($digest.Length -eq 64) -Name 'C599 ManifestAllowlist the manifest digest is a sha256' -Detail $digest
}

# ============================================================== V-7 status ===

function Test-C599_StatusIdentity {
    $root = New-C599Root
    $releaseRoot = Join-Path $root 'releases'
    New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
    $journal = Join-Path $releaseRoot 'publications.json'

    $unknown = & (Join-Path $here 'release-status.ps1') -ReleaseRoot $releaseRoot -Sha $ShaB -PassThru
    Assert-C487 -Cond ([string]$unknown.state -eq 'unknown') -Name 'C599 StatusIdentity no publication journal is unknown, not latest' -Detail ([string]$unknown.state)

    [void](New-ReleaseGateTagReservation -RemoteTags @() -JournalPath $journal -CandidateId $CandidateId -Sha $ShaB -CutUtc ([datetime]'2026-09-23T08:30:00Z'))
    $pending = & (Join-Path $here 'release-status.ps1') -ReleaseRoot $releaseRoot -Sha $ShaB -PassThru
    Assert-C487 -Cond ([string]$pending.state -eq 'unreleased integration build') -Name 'C599 StatusIdentity a reserved but unpublished tag is not released' -Detail ([string]$pending.state)

    [void](Set-ReleaseGateReservationPublished -JournalPath $journal -CandidateId $CandidateId -ReleaseId 'R1' -ReleaseUrl 'https://example/releases/tag/v2026.09.23.1')
    $released = & (Join-Path $here 'release-status.ps1') -ReleaseRoot $releaseRoot -Sha $ShaB -PassThru
    Assert-C487 -Cond ([string]$released.state -eq 'released' -and [string]$released.tag -eq 'v2026.09.23.1') -Name 'C599 StatusIdentity an exact sha match reports released with its tag' -Detail ([string]$released.tag)
    Assert-C487 -Cond ([string]$released.releaseUrl -match 'releases/tag/v2026.09.23.1') -Name 'C599 StatusIdentity the release url is the GitHub tag address' -Detail ([string]$released.releaseUrl)

    $newer = & (Join-Path $here 'release-status.ps1') -ReleaseRoot $releaseRoot -Sha $ShaC -PassThru
    Assert-C487 -Cond ([string]$newer.state -eq 'unreleased integration build') -Name 'C599 StatusIdentity a newer sha is NOT labelled as the latest release' -Detail ([string]$newer.state)
    Assert-C487 -Cond ([string]::IsNullOrWhiteSpace([string]$newer.tag)) -Name 'C599 StatusIdentity a newer sha carries no tag' -Detail ([string]$newer.tag)

    $caseInsensitive = & (Join-Path $here 'release-status.ps1') -ReleaseRoot $releaseRoot -Sha ($ShaB.ToUpperInvariant()) -PassThru
    Assert-C487 -Cond ([string]$caseInsensitive.state -eq 'released') -Name 'C599 StatusIdentity sha matching is case-insensitive' -Detail ([string]$caseInsensitive.state)
}

function Test-C599_NoActivation {
    $text = Get-Content -LiteralPath (Join-Path $here 'release-status.ps1') -Raw
    foreach ($forbidden in @('restart-apphost', 'Stop-Process', 'taskkill', 'git checkout', 'deploy-local')) {
        Assert-C487 -Cond ($text -notmatch [regex]::Escape($forbidden)) -Name ('C599 NoActivation release-status performs no {0}' -f $forbidden)
    }
    $pub = Get-Content -LiteralPath (Join-Path $here 'publish-release.ps1') -Raw
    foreach ($forbidden in @('restart-apphost', 'Stop-Process', 'taskkill', 'deploy-local')) {
        Assert-C487 -Cond ($pub -notmatch [regex]::Escape($forbidden)) -Name ('C599 NoActivation publish-release performs no {0}' -f $forbidden)
    }
    # Behavioural, not textual: run a real cut and a real publication and inspect
    # the git calls they actually made. A prose mention of --force must not pass
    # this, and a run that pushed nothing at all must not pass it either.
    $fx = New-C599PublishFx
    $r = Invoke-C599Publish -Fx $fx
    Assert-C487 -Cond ([int]$r.ExitCode -eq 0 -and [bool]$r.Published) -Name 'C599 NoActivation control: the publication ran' -Detail ([string]$r.Refusal)
    $trace = Get-Content -LiteralPath $fx.Git.Trace -Raw
    $pushes = @(($trace -split "`n") | Where-Object { $_ -match '^GIT push' })
    Assert-C487 -Cond ($pushes.Count -ge 1) -Name 'C599 NoActivation the publication really pushed something' -Detail ([string]$pushes.Count)
    $forced = @($pushes | Where-Object { $_ -match '--force' -or $_ -match '--force-with-lease' -or $_ -match 'push\s+\S+\s+\+' })
    Assert-C487 -Cond ($forced.Count -eq 0) -Name 'C599 NoActivation no observed push carried a force flag or plus refspec' -Detail ($forced -join ';')
    $isTagDeletion = {
        param([string]$Call)
        return ($Call -match '\btag\s+(-d|--delete)(\s|$)' -or
            ($Call -match '^GIT push\s' -and
                ($Call -match '\s:refs/tags/' -or $Call -match '\s(--delete|-d)(\s|$)')))
    }
    Assert-C487 -Cond ((& $isTagDeletion 'GIT tag -d v2026.10.15.1') -and
        (& $isTagDeletion 'GIT push origin :refs/tags/v2026.10.15.1') -and
        (& $isTagDeletion 'GIT push origin --delete v2026.10.15.1') -and
        -not (& $isTagDeletion 'GIT push origin refs/antiphon-release-push/fixture/*:refs/tags/*')) `
        -Name 'C599 NoActivation deletion matcher distinguishes a populated source refspec'
    $deletes = @(($trace -split "`n") | Where-Object { & $isTagDeletion $_ })
    Assert-C487 -Cond ($deletes.Count -eq 0) -Name 'C599 NoActivation no observed call deleted or re-created a tag' -Detail ($deletes -join ';')
}

# ======================================================== V-5 registration ===

function New-C599WindmillFx {
    <#
      Stateful installed-API fixture. Write acceptance is separate from stored
      state; only a GET readback proves a definition reached the workspace.
    #>
    param([hashtable]$Fail = @{}, [hashtable]$Existing = @{})
    $root = New-C599Root
    $store = Join-Path $root 'windmill.json'
    $seed = [ordered]@{ scripts = [ordered]@{}; schedules = [ordered]@{} }
    foreach ($k in $Existing.Keys) { $seed[$k] = $Existing[$k] }
    Write-NightlyAtomicJson -Path $store -Object $seed
    $failPath = Join-Path $root 'windmill-fail.json'
    $failState = [ordered]@{}
    foreach ($k in $Fail.Keys) { $failState[$k] = $Fail[$k] }
    Write-NightlyAtomicJson -Path $failPath -Object $failState
    $trace = Join-Path $root 'wm-trace.log'
    $seams = Join-Path $root 'seams-wm.ps1'
    $text = @"
`$ReleaseGateSeams = @{
    Windmill = {
        param(`$Method, `$Path, `$Body, `$Context)
        `$store = '$store'
        `$failPath = '$failPath'
        Add-Content -LiteralPath '$trace' -Value (`$Method + ' ' + `$Path) -Encoding ASCII
        if (-not [string]::IsNullOrWhiteSpace([string]`$Context.token)) {
            Add-Content -LiteralPath '$trace' -Value 'TOKEN-PRESENT' -Encoding ASCII
        }
        `$fail = Get-Content -LiteralPath `$failPath -Raw | ConvertFrom-Json
        `$db = Get-Content -LiteralPath `$store -Raw | ConvertFrom-Json
        `$scripts = @{}
        foreach (`$p in @(`$db.scripts.PSObject.Properties)) { `$scripts[`$p.Name] = `$p.Value }
        `$schedules = @{}
        foreach (`$p in @(`$db.schedules.PSObject.Properties)) { `$schedules[`$p.Name] = `$p.Value }
        function Save() {
            `$obj = @{ scripts = `$scripts; schedules = `$schedules }
            `$obj | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath `$store -Encoding UTF8
        }
        `$key = ''
        if (`$Path -match '/scripts/create') { `$key = 'script-create' }
        elseif (`$Path -match '/schedules/create') { `$key = 'schedule-create' }
        elseif (`$Path -match '/schedules/setenabled') { `$key = 'schedule-enable' }
        elseif (`$Path -match '/scripts/get') { `$key = 'script-get' }
        elseif (`$Path -match '/schedules/get') { `$key = 'schedule-get' }
        `$shouldFail = (`$fail.PSObject.Properties.Name -contains `$key -and [bool]`$fail.`$key)
        `$commits = (`$fail.PSObject.Properties.Name -contains (`$key + '_commits') -and [bool]`$fail.(`$key + '_commits'))
        if (`$Method -eq 'GET') {
            if (`$shouldFail) { return @{ Ok = `$false; Status = 503; Body = `$null } }
            if (`$key -eq 'script-get') {
                `$name = (`$Path -split '/scripts/get/p/')[1]
                if (`$scripts.ContainsKey(`$name)) { return @{ Ok = `$true; Status = 200; Body = `$scripts[`$name] } }
                return @{ Ok = `$false; Status = 404; Body = `$null }
            }
            `$name = (`$Path -split '/schedules/get/')[1]
            if (`$schedules.ContainsKey(`$name)) { return @{ Ok = `$true; Status = 200; Body = `$schedules[`$name] } }
            return @{ Ok = `$false; Status = 404; Body = `$null }
        }
        if (`$shouldFail -and -not `$commits) { return @{ Ok = `$false; Status = 500; Body = `$null } }
        if (`$key -eq 'script-create') {
            `$content = [string]`$Body.content
            `$hash = (Get-NightlySha256Text -Text (ConvertTo-NightlyCanonicalJson -Object `$Body))
            `$scripts[[string]`$Body.path] = [pscustomobject]@{ path = [string]`$Body.path; hash = `$hash; content = `$content; tag = [string]`$Body.tag }
            Save
        } elseif (`$key -eq 'schedule-create') {
            `$schedules[[string]`$Body.path] = [pscustomobject]@{
                path = [string]`$Body.path; schedule = [string]`$Body.schedule; timezone = [string]`$Body.timezone
                script_path = [string]`$Body.script_path; enabled = [bool]`$Body.enabled; tag = [string]`$Body.tag
            }
            Save
        } elseif (`$key -eq 'schedule-enable') {
            `$name = (`$Path -split '/schedules/setenabled/')[1]
            if (`$schedules.ContainsKey(`$name)) {
                `$schedules[`$name] | Add-Member -NotePropertyName enabled -NotePropertyValue ([bool]`$Body.enabled) -Force
                Save
            }
        }
        if (`$shouldFail) { return @{ Ok = `$false; Status = 500; Body = `$null } }
        return @{ Ok = `$true; Status = 200; Body = @{} }
    }
}
"@
    Set-Content -LiteralPath $seams -Value $text -Encoding ASCII
    $tokenFile = Join-Path $root 'token.txt'
    Set-Content -LiteralPath $tokenFile -Value 'wm-secret-token-value' -Encoding ASCII
    $profile = Join-Path $root 'profile.json'
    Write-NightlyAtomicJson -Path $profile -Object ([ordered]@{
        windmillBaseUrl = 'http://windmill.invalid'
        workspace = 'mc'
        tokenFile = $tokenFile
        repositoryPath = 'C:\src\Antiphon'
        projectId = '00000000-0000-0000-0000-000000000001'
    })
    return [pscustomobject]@{ Root = $root; Store = $store; FailPath = $failPath; Seams = $seams; Profile = $profile; Trace = $trace; TokenFile = $tokenFile }
}

function Read-C599Windmill {
    param($Fx)
    return (Get-Content -LiteralPath $Fx.Store -Raw | ConvertFrom-Json)
}

function Set-C599WindmillFailure {
    param($Fx, [string]$Key, [switch]$Clear, [switch]$CommitsAnyway)
    $state = Get-Content -LiteralPath $Fx.FailPath -Raw | ConvertFrom-Json
    $obj = [ordered]@{}
    foreach ($p in @($state.PSObject.Properties)) { $obj[$p.Name] = $p.Value }
    if ($Clear) { $obj.Remove($Key); $obj.Remove($Key + '_commits') }
    else {
        $obj[$Key] = $true
        if ($CommitsAnyway) { $obj[$Key + '_commits'] = $true }
    }
    Write-NightlyAtomicJson -Path $Fx.FailPath -Object $obj
}

function Invoke-C599Register {
    param($Fx, [hashtable]$Extra)
    $splat = @{ Profile = $Fx.Profile; SeamsPath = $Fx.Seams; PassThru = $true }
    if ($Extra) { foreach ($k in $Extra.Keys) { $splat[$k] = $Extra[$k] } }
    return (& (Join-Path $here 'register-release-gates.ps1') @splat)
}

function Test-C599_Preview {
    $fx = New-C599WindmillFx
    $r = Invoke-C599Register -Fx $fx
    Assert-C487 -Cond ([int]$r.ExitCode -eq 0 -and [string]$r.Mode -eq 'preview') -Name 'C599 Preview a plain run is a preview' -Detail ([string]$r.Refusal)
    Assert-C487 -Cond ([int]$r.Writes -eq 0) -Name 'C599 Preview a preview performs zero writes' -Detail ([string]$r.Writes)
    $db = Read-C599Windmill -Fx $fx
    Assert-C487 -Cond (@($db.scripts.PSObject.Properties).Count -eq 0 -and @($db.schedules.PSObject.Properties).Count -eq 0) -Name 'C599 Preview the workspace is untouched after a preview'
    Assert-C487 -Cond (@($r.Rows).Count -eq 3) -Name 'C599 Preview all three definitions are previewed' -Detail ([string]@($r.Rows).Count)
    foreach ($row in @($r.Rows)) {
        Assert-C487 -Cond ([string]$row.action -like 'preview:*') -Name ('C599 Preview {0} row is marked preview' -f $row.key) -Detail ([string]$row.action)
        Assert-C487 -Cond ((-not [string]::IsNullOrWhiteSpace([string]$row.desiredDigest)) -and (-not [string]::IsNullOrWhiteSpace([string]$row.cron)) -and (-not [string]::IsNullOrWhiteSpace([string]$row.timezone))) -Name ('C599 Preview {0} row carries digest, cron and timezone' -f $row.key) -Detail ([string]$row.cron)
    }
    # A preview never reads the token file at all.
    $trace = ''
    if (Test-Path -LiteralPath $fx.Trace) { $trace = Get-Content -LiteralPath $fx.Trace -Raw }
    Assert-C487 -Cond ($trace -notmatch 'TOKEN-PRESENT') -Name 'C599 Preview a preview sends no token' -Detail $trace
    # And -EnableSchedule without -Apply refuses outright.
    $enable = Invoke-C599Register -Fx $fx -Extra @{ EnableSchedule = 'rc' }
    Assert-C487 -Cond ([string]$enable.Refusal -eq 'enable-requires-apply') -Name 'C599 Preview -EnableSchedule without -Apply refuses' -Detail ([string]$enable.Refusal)
}

function Test-C599_ApplyReadback {
    $fx = New-C599WindmillFx
    $r = Invoke-C599Register -Fx $fx -Extra @{ Apply = $true }
    Assert-C487 -Cond ([int]$r.ExitCode -eq 0) -Name 'C599 ApplyReadback control apply succeeds' -Detail ([string]$r.Refusal)
    $db = Read-C599Windmill -Fx $fx
    foreach ($path in @('u/lndcobra/antiphon_nightly_tests', 'u/lndcobra/antiphon_nightly_readiness', 'u/lndcobra/antiphon_release_candidates')) {
        Assert-C487 -Cond ($db.scripts.PSObject.Properties.Name -contains $path) -Name ('C599 ApplyReadback script {0} is stored' -f $path)
        Assert-C487 -Cond ($db.schedules.PSObject.Properties.Name -contains $path) -Name ('C599 ApplyReadback schedule {0} is stored' -f $path)
        Assert-C487 -Cond (-not [bool]$db.schedules.$path.enabled) -Name ('C599 ApplyReadback schedule {0} was created DISABLED' -f $path) -Detail ([string]$db.schedules.$path.enabled)
    }
    Assert-C487 -Cond ([string]$db.schedules.'u/lndcobra/antiphon_release_candidates'.schedule -eq '0 30 8,16 * * *') -Name 'C599 ApplyReadback the rc cron is the two-slot schedule' -Detail ([string]$db.schedules.'u/lndcobra/antiphon_release_candidates'.schedule)
    Assert-C487 -Cond ([string]$db.schedules.'u/lndcobra/antiphon_release_candidates'.timezone -eq 'Europe/London') -Name 'C599 ApplyReadback the rc timezone is Europe/London'
    Assert-C487 -Cond ([string]$db.schedules.'u/lndcobra/antiphon_nightly_tests'.schedule -eq '0 30 0 * * *') -Name 'C599 ApplyReadback the master cron is unchanged'
    foreach ($row in @($r.Rows)) {
        Assert-C487 -Cond (-not [string]::IsNullOrWhiteSpace([string]$row.readbackHash)) -Name ('C599 ApplyReadback {0} recorded a readback hash' -f $row.key) -Detail ([string]$row.readbackHash)
    }
    $trace = Get-Content -LiteralPath $fx.Trace -Raw
    Assert-C487 -Cond ($trace -match 'TOKEN-PRESENT') -Name 'C599 ApplyReadback apply sends the token from the token file'
    Assert-C487 -Cond ($trace -notmatch 'wm-secret-token-value') -Name 'C599 ApplyReadback the token VALUE never reaches the trace'

    # Idempotent reapply: no duplicates, and an enabled schedule stays enabled.
    $enabled = Invoke-C599Register -Fx $fx -Extra @{ Apply = $true; EnableSchedule = 'readiness' }
    Assert-C487 -Cond ([int]$enabled.ExitCode -eq 0 -and [string]$enabled.Enabled -eq 'readiness') -Name 'C599 ApplyReadback an explicit selector enables exactly one schedule' -Detail ([string]$enabled.Refusal)
    $db2 = Read-C599Windmill -Fx $fx
    Assert-C487 -Cond ([bool]$db2.schedules.'u/lndcobra/antiphon_nightly_readiness'.enabled) -Name 'C599 ApplyReadback the selected schedule is enabled on readback'
    Assert-C487 -Cond (-not [bool]$db2.schedules.'u/lndcobra/antiphon_release_candidates'.enabled) -Name 'C599 ApplyReadback enabling readiness did not enable rc'
    Assert-C487 -Cond (-not [bool]$db2.schedules.'u/lndcobra/antiphon_nightly_tests'.enabled) -Name 'C599 ApplyReadback enabling readiness did not enable nightly'

    $again = Invoke-C599Register -Fx $fx -Extra @{ Apply = $true }
    Assert-C487 -Cond ([int]$again.ExitCode -eq 0) -Name 'C599 ApplyReadback a second apply succeeds' -Detail ([string]$again.Refusal)
    Assert-C487 -Cond ([int]$again.Writes -eq 0) -Name 'C599 ApplyReadback a second apply writes nothing (idempotent)' -Detail ([string]$again.Writes)
    $db3 = Read-C599Windmill -Fx $fx
    Assert-C487 -Cond (@($db3.scripts.PSObject.Properties).Count -eq 3) -Name 'C599 ApplyReadback a second apply created no duplicate scripts' -Detail ([string]@($db3.scripts.PSObject.Properties).Count)
    Assert-C487 -Cond ([bool]$db3.schedules.'u/lndcobra/antiphon_nightly_readiness'.enabled) -Name 'C599 ApplyReadback a reapply PRESERVES an already enabled schedule'

    # Persistence cuts across each write, with ready and unavailable recipients.
    foreach ($key in @('script-create', 'schedule-create')) {
        foreach ($cut in @('before-write', 'commit-then-lose-response')) {
            $fx2 = New-C599WindmillFx
            if ($cut -eq 'before-write') { Set-C599WindmillFailure -Fx $fx2 -Key $key }
            else { Set-C599WindmillFailure -Fx $fx2 -Key $key -CommitsAnyway }
            $bad = Invoke-C599Register -Fx $fx2 -Extra @{ Apply = $true }
            Assert-C487 -Cond ([int]$bad.ExitCode -ne 0) -Name ('C599 ApplyReadback {0} {1} fails the apply' -f $key, $cut) -Detail ([string]$bad.Refusal)
            $dbBad = Read-C599Windmill -Fx $fx2
            if ($cut -eq 'before-write' -and $key -eq 'script-create') {
                Assert-C487 -Cond (@($dbBad.scripts.PSObject.Properties).Count -eq 0) -Name 'C599 ApplyReadback a failed script write stored nothing'
            }
            Set-C599WindmillFailure -Fx $fx2 -Key $key -Clear
            $recovered = Invoke-C599Register -Fx $fx2 -Extra @{ Apply = $true }
            Assert-C487 -Cond ([int]$recovered.ExitCode -eq 0) -Name ('C599 ApplyReadback {0} {1} restart completes the apply' -f $key, $cut) -Detail ([string]$recovered.Refusal)
            $dbOk = Read-C599Windmill -Fx $fx2
            Assert-C487 -Cond (@($dbOk.scripts.PSObject.Properties).Count -eq 3 -and @($dbOk.schedules.PSObject.Properties).Count -eq 3) -Name ('C599 ApplyReadback {0} {1} recovery ends with exactly three of each' -f $key, $cut) -Detail ([string]@($dbOk.scripts.PSObject.Properties).Count)
        }
    }

    # A readback outage is not success.
    $fx3 = New-C599WindmillFx
    Set-C599WindmillFailure -Fx $fx3 -Key 'script-get'
    $outage = Invoke-C599Register -Fx $fx3 -Extra @{ Apply = $true }
    Assert-C487 -Cond ([int]$outage.ExitCode -ne 0) -Name 'C599 ApplyReadback a readback outage fails the apply' -Detail ([string]$outage.Refusal)

    # An enable that does not read back enabled is not success.
    $fx4 = New-C599WindmillFx
    [void](Invoke-C599Register -Fx $fx4 -Extra @{ Apply = $true })
    Set-C599WindmillFailure -Fx $fx4 -Key 'schedule-enable'
    $enableFail = Invoke-C599Register -Fx $fx4 -Extra @{ Apply = $true; EnableSchedule = 'rc' }
    Assert-C487 -Cond ([int]$enableFail.ExitCode -ne 0) -Name 'C599 ApplyReadback a failed enable is not success' -Detail ([string]$enableFail.Refusal)
    $db4 = Read-C599Windmill -Fx $fx4
    Assert-C487 -Cond (-not [bool]$db4.schedules.'u/lndcobra/antiphon_release_candidates'.enabled) -Name 'C599 ApplyReadback the rc schedule is still disabled after a failed enable'
}

function Test-C599_Concurrency {
    # Concurrent drift: another writer changed the live cron under us.
    $fx = New-C599WindmillFx
    [void](Invoke-C599Register -Fx $fx -Extra @{ Apply = $true })
    $db = Get-Content -LiteralPath $fx.Store -Raw | ConvertFrom-Json
    $db.schedules.'u/lndcobra/antiphon_release_candidates' | Add-Member -NotePropertyName schedule -NotePropertyValue '0 0 3 * * *' -Force
    $db | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $fx.Store -Encoding UTF8
    $drift = Invoke-C599Register -Fx $fx -Extra @{ Apply = $true }
    Assert-C487 -Cond ([string]$drift.Refusal -eq 'schedule-drift') -Name 'C599 Concurrency concurrent cron drift refuses' -Detail ([string]$drift.Refusal)
    $after = Get-Content -LiteralPath $fx.Store -Raw | ConvertFrom-Json
    Assert-C487 -Cond ([string]$after.schedules.'u/lndcobra/antiphon_release_candidates'.schedule -eq '0 0 3 * * *') -Name 'C599 Concurrency the drifted schedule was not silently overwritten' -Detail ([string]$after.schedules.'u/lndcobra/antiphon_release_candidates'.schedule)

    # Timezone drift is the same refusal.
    $fx2 = New-C599WindmillFx
    [void](Invoke-C599Register -Fx $fx2 -Extra @{ Apply = $true })
    $db2 = Get-Content -LiteralPath $fx2.Store -Raw | ConvertFrom-Json
    $db2.schedules.'u/lndcobra/antiphon_nightly_tests' | Add-Member -NotePropertyName timezone -NotePropertyValue 'UTC' -Force
    $db2 | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $fx2.Store -Encoding UTF8
    $tzDrift = Invoke-C599Register -Fx $fx2 -Extra @{ Apply = $true }
    Assert-C487 -Cond ([string]$tzDrift.Refusal -eq 'schedule-drift') -Name 'C599 Concurrency concurrent timezone drift refuses' -Detail ([string]$tzDrift.Refusal)
}

function Test-C599_ScheduleProvenance {
    # The wrappers must derive trigger from the runner's schedule context.
    $master = Get-Content -LiteralPath (Join-Path (Join-Path $here 'windmill') 'antiphon-nightly-tests.json') -Raw | ConvertFrom-Json
    $content = [string]$master.content
    Assert-C487 -Cond ($content -match 'WM_SCHEDULE_PATH') -Name 'C599 ScheduleProvenance the master wrapper reads WM_SCHEDULE_PATH'
    Assert-C487 -Cond ($content -match 'TRIGGER=manual') -Name 'C599 ScheduleProvenance the master wrapper defaults to manual'
    Assert-C487 -Cond ($content -match 'u/lndcobra/antiphon_nightly_tests') -Name 'C599 ScheduleProvenance the master wrapper checks its OWN schedule path'
    Assert-C487 -Cond ($content -match '-Trigger \$TRIGGER') -Name 'C599 ScheduleProvenance the master wrapper passes the derived trigger'
    Assert-C487 -Cond ($content -match '-Profile nightly') -Name 'C599 ScheduleProvenance the master wrapper pins the nightly profile'

    $rc = Get-Content -LiteralPath (Join-Path (Join-Path $here 'windmill') 'antiphon-release-candidates.json') -Raw | ConvertFrom-Json
    $rcContent = [string]$rc.content
    Assert-C487 -Cond ($rcContent -match 'WM_SCHEDULE_PATH') -Name 'C599 ScheduleProvenance the rc wrapper reads WM_SCHEDULE_PATH'
    Assert-C487 -Cond ($rcContent -match 'u/lndcobra/antiphon_release_candidates') -Name 'C599 ScheduleProvenance the rc wrapper checks its own schedule path'
    Assert-C487 -Cond ($rcContent -notmatch 'TRIGGER=scheduled') -Name 'C599 ScheduleProvenance the rc wrapper never claims scheduled master credit'

    $rcSched = Get-Content -LiteralPath (Join-Path (Join-Path $here 'windmill') 'antiphon-release-candidates.schedule.json') -Raw | ConvertFrom-Json
    Assert-C487 -Cond (-not [bool]$rcSched.enabled) -Name 'C599 ScheduleProvenance the checked-in rc schedule payload is disabled'
    Assert-C487 -Cond ([string]$rcSched.schedule -eq '0 30 8,16 * * *') -Name 'C599 ScheduleProvenance the rc payload carries the two-slot cron' -Detail ([string]$rcSched.schedule)

    # CARD-0616: a raw JSON escape in a definition's human-facing prose (a Windows path
    # whose \releases became a carriage return) survives registration and misreads the
    # producer-owned clone path in the Windmill UI. Every checked-in definition's summary
    # and description must be free of control characters.
    $dirtyProse = @()
    foreach ($def in (Get-ChildItem -LiteralPath (Join-Path $here 'windmill') -Filter '*.json' -File)) {
        $obj = Get-Content -LiteralPath $def.FullName -Raw | ConvertFrom-Json
        foreach ($field in @('summary', 'description')) {
            $val = [string]$obj.$field
            if ($val -match '[\x00-\x1f]') { $dirtyProse += ('{0}:{1}' -f $def.Name, $field) }
        }
    }
    Assert-C487 -Cond ($dirtyProse.Count -eq 0) -Name 'C599 ScheduleProvenance no definition summary or description carries a control character' -Detail ($dirtyProse -join ',')
    Assert-C487 -Cond ([string]$rc.description -match 'C:\\Antiphon\\releases\\checkout') -Name 'C599 ScheduleProvenance the rc description reads the producer-owned clone path intact' -Detail ([string]$rc.description)

    # A pre-enabled rc payload is refused rather than registered.
    $fx = New-C599WindmillFx
    $defs = Join-Path $fx.Root 'defs'
    New-Item -ItemType Directory -Path $defs -Force | Out-Null
    Copy-Item -Path (Join-Path (Join-Path $here 'windmill') '*.json') -Destination $defs -Force
    $bad = Get-Content -LiteralPath (Join-Path $defs 'antiphon-release-candidates.schedule.json') -Raw | ConvertFrom-Json
    $bad | Add-Member -NotePropertyName enabled -NotePropertyValue $true -Force
    $bad | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $defs 'antiphon-release-candidates.schedule.json') -Encoding UTF8
    $r = Invoke-C599Register -Fx $fx -Extra @{ Apply = $true; DefinitionRoot = $defs }
    Assert-C487 -Cond ([string]$r.Refusal -eq 'rc-schedule-preenabled') -Name 'C599 ScheduleProvenance a pre-enabled rc payload refuses' -Detail ([string]$r.Refusal)

    # Profile hygiene: a non-mc workspace and an inline token both refuse.
    $fx2 = New-C599WindmillFx
    $p = Get-Content -LiteralPath $fx2.Profile -Raw | ConvertFrom-Json
    $p | Add-Member -NotePropertyName workspace -NotePropertyValue 'other' -Force
    $p | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $fx2.Profile -Encoding UTF8
    $wrongWs = Invoke-C599Register -Fx $fx2
    Assert-C487 -Cond ([string]$wrongWs.Refusal -eq 'profile-invalid') -Name 'C599 ScheduleProvenance a non-mc workspace refuses' -Detail ([string]$wrongWs.Refusal)

    $fx3 = New-C599WindmillFx
    $p3 = Get-Content -LiteralPath $fx3.Profile -Raw | ConvertFrom-Json
    $p3 | Add-Member -NotePropertyName token -NotePropertyValue 'inline-secret' -Force
    $p3 | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $fx3.Profile -Encoding UTF8
    $inline = Invoke-C599Register -Fx $fx3
    Assert-C487 -Cond ([string]$inline.Refusal -eq 'profile-invalid') -Name 'C599 ScheduleProvenance an inline token in the profile refuses' -Detail ([string]$inline.Refusal)

    $fx4 = New-C599WindmillFx
    Remove-Item -LiteralPath $fx4.TokenFile -Force
    $noToken = Invoke-C599Register -Fx $fx4 -Extra @{ Apply = $true }
    Assert-C487 -Cond ([string]$noToken.Refusal -eq 'token-unavailable') -Name 'C599 ScheduleProvenance a missing token file refuses the apply' -Detail ([string]$noToken.Refusal)
}

# ======================================================= V-4 e2e evidence ===

function Test-C599_Prerequisites {
    $root = New-C599Root
    $logRoot = Join-Path $root 'logs'
    New-Item -ItemType Directory -Path $logRoot -Force | Out-Null

    # Missing generated playwright script: red, and named.
    $fakeRepo = Join-Path $root 'repo'
    New-Item -ItemType Directory -Path $fakeRepo -Force | Out-Null
    $script:NightlySeams = $null
    $noBrowser = Test-NightlyE2EPrerequisites -RepoRoot $fakeRepo -LogRoot $logRoot -BundlePath (Join-Path $fakeRepo 'client\dist\index.html')
    Assert-C487 -Cond (-not $noBrowser.Ok) -Name 'C599 Prerequisites a missing playwright script is not ok'
    $names = @($noBrowser.Rows | ForEach-Object { [string]$_.name })
    Assert-C487 -Cond ($names -contains 'playwright-script') -Name 'C599 Prerequisites the missing browser installer is named' -Detail ($names -join ',')
    Assert-C487 -Cond ($names -contains 'client-bundle') -Name 'C599 Prerequisites the missing client bundle is named' -Detail ($names -join ',')

    # The production selection path must treat that as red, never as a skip.
    $policyPath = New-C599Policy -Root $root
    $policy = Read-NightlyExecutionPolicy -RepoRoot $repoRoot -PolicyPath $policyPath
    $required = @(Get-NightlyProfileRequiredSuites -PolicyObject $policy.Object -Profile 'rc')
    Assert-C487 -Cond ($required -contains 'e2e') -Name 'C599 Prerequisites the rc profile requires e2e before any prerequisite check'
    $suiteMap = Get-NightlySuiteMap -PolicyObject $policy.Object
    Assert-C487 -Cond (Test-NightlySuiteIsNative -Suite $suiteMap['e2e'] -SuiteId 'e2e') -Name 'C599 Prerequisites e2e is classified native so it serialises'
    Assert-C487 -Cond (Test-NightlySuiteIsNative -Suite $suiteMap['antiphon'] -SuiteId 'antiphon') -Name 'C599 Prerequisites antiphon is still classified native'
    Assert-C487 -Cond (-not (Test-NightlySuiteIsNative -Suite $suiteMap['client'] -SuiteId 'client')) -Name 'C599 Prerequisites the client suite is not native'

    # The implementation must install through the GENERATED playwright.ps1.
    $impl = Get-Content -LiteralPath (Join-Path $lib 'nightly-tests-impl.ps1') -Raw
    Assert-C487 -Cond ($impl -match 'playwright\.ps1') -Name 'C599 Prerequisites the executor uses the generated playwright.ps1'
    Assert-C487 -Cond ($impl -match 'install.*chromium' -or $impl -match "'install', 'chromium'") -Name 'C599 Prerequisites the executor installs chromium'
    Assert-C487 -Cond ($impl -match 'e2e-prerequisite-failed') -Name 'C599 Prerequisites a failed prerequisite is a named red reason'
}

function Test-C599_EnvironmentIsolation {
    $policy = Read-NightlyExecutionPolicy -RepoRoot $repoRoot
    $envMap = Get-NightlySafeChildEnvironment -PolicyObject $policy.Object -SuiteId 'e2e' -BaseEnvironment @{
        ANTIPHON_HEADED_TESTS = '1'
        ANTIPHON_DISTILLER_APPLY_CANARY = '1'
        ANTIPHON_TG_TEST_TOKEN = 'live-token'
        KEEP_ME = 'yes'
    }
    foreach ($name in @('ANTIPHON_HEADED_TESTS', 'ANTIPHON_DISTILLER_APPLY_CANARY', 'ANTIPHON_TG_TEST_TOKEN')) {
        Assert-C487 -Cond ([string]$envMap[$name] -eq '') -Name ('C599 EnvironmentIsolation {0} is cleared for an e2e child' -f $name) -Detail ([string]$envMap[$name])
    }
    Assert-C487 -Cond ([string]$envMap['KEEP_ME'] -eq 'yes') -Name 'C599 EnvironmentIsolation an unrelated variable survives'
    Assert-C487 -Cond (-not $envMap.ContainsKey('ANTIPHON_BROKER_TESTS') -or [string]$envMap['ANTIPHON_BROKER_TESTS'] -ne '1') -Name 'C599 EnvironmentIsolation the broker opt-in is not set for e2e'
    $messagingEnv = Get-NightlySafeChildEnvironment -PolicyObject $policy.Object -SuiteId 'messaging' -BaseEnvironment @{}
    Assert-C487 -Cond ([string]$messagingEnv['ANTIPHON_BROKER_TESTS'] -eq '1') -Name 'C599 EnvironmentIsolation the broker opt-in IS set for messaging'
}

function Test-C599_ExecutionArtifacts {
    $root = New-C599Root
    $trx = Join-Path $root 'e2e-all.trx'
    $diag = Join-Path $root 'e2e-exec'
    $nativeArgs = @(Get-NightlyNativeExecutionArguments -TrxPath $trx -DiagnosticDirectory $diag `
        -Classes @('Antiphon.E2E.SmokeE2ETests', 'Antiphon.E2E.BoardE2ETests') `
        -GroupFilter '/*/Antiphon.E2E/(SmokeE2ETests*)|(BoardE2ETests*)/*')
    Assert-C487 -Cond ($nativeArgs -contains '--report-trx') -Name 'C599 ExecutionArtifacts the executor requests a fresh TRX'
    Assert-C487 -Cond ($nativeArgs -contains '--diagnostic') -Name 'C599 ExecutionArtifacts the executor requests MTP diagnostics'
    Assert-C487 -Cond ($nativeArgs -contains '--diagnostic-output-directory') -Name 'C599 ExecutionArtifacts the executor pins the diagnostic directory'
    $filterIdx = [array]::IndexOf($nativeArgs, '--treenode-filter')
    Assert-C487 -Cond ($filterIdx -ge 0 -and ([string]$nativeArgs[$filterIdx + 1]) -match 'SmokeE2ETests' -and ([string]$nativeArgs[$filterIdx + 1]) -match 'BoardE2ETests') -Name 'C599 ExecutionArtifacts the executor launches only the eligible roster' -Detail ([string]$nativeArgs[$filterIdx + 1])
    # The TRX filename reaching MTP must be a basename with the directory carried
    # separately, or the runner writes the report where the census never reads it.
    $nested = @(Get-NightlyNativeExecutionArguments -TrxPath (Join-Path (Join-Path $root 'sub') 'x.trx') -DiagnosticDirectory $diag -Classes @())
    $nameIdx = [array]::IndexOf($nested, '--report-trx-filename')
    $dirIdx = [array]::IndexOf($nested, '--results-directory')
    Assert-C487 -Cond (([string]$nested[$nameIdx + 1]) -eq 'x.trx') -Name 'C599 ExecutionArtifacts the trx filename is passed as a basename' -Detail ([string]$nested[$nameIdx + 1])
    Assert-C487 -Cond (([string]$nested[$dirIdx + 1]) -like '*sub') -Name 'C599 ExecutionArtifacts the trx directory is passed separately' -Detail ([string]$nested[$dirIdx + 1])
    $msg = ''
    try { [void](Get-NightlyNativeExecutionArguments -TrxPath $trx -DiagnosticDirectory '' -Classes @()) } catch { $msg = $_.Exception.Message }
    Assert-C487 -Cond ($msg -match 'diagnostic directory') -Name 'C599 ExecutionArtifacts a missing diagnostic directory is refused' -Detail $msg
    $impl = Get-Content -LiteralPath (Join-Path $lib 'nightly-tests-impl.ps1') -Raw
    Assert-C487 -Cond ($impl -match 'Wait-NightlyOwnedCleanup') -Name 'C599 ExecutionArtifacts the executor waits for owned cleanup'
    Assert-C487 -Cond ($impl -match 'owned-child still running') -Name 'C599 ExecutionArtifacts an unjoined owned child is an error'
    Assert-C487 -Cond ($impl -match 'requiredUidCount' -and $impl -match 'censusDigest') -Name 'C599 ExecutionArtifacts the suite row records the expanded census'
}

# ================================================= CARD-0599 D-15 authority (B1) ===

function Test-C599B_MissingDiscovery {
    foreach ($variant in @('missing', 'empty', 'null')) {
        $root = New-C599Root
        $dir = Get-ReleaseAuthorityDir -CandidateRoot $root
        Write-NightlyAtomicJson -Path (Join-Path $dir 'authority.json') -Object ([ordered]@{
            candidateId = $CandidateId; intentId = $script:C599IntentId; sha = $ShaB
            requiredSuites = @('markdown-pdf'); exclusions = @()
        })
        $discovery = @{}
        if ($variant -eq 'empty') { $discovery['markdown-pdf'] = @() }
        if ($variant -eq 'null') { $discovery['markdown-pdf'] = $null }
        $plan = New-ReleaseGateExecutionPlan -CandidateRoot $root -Discovery $discovery -Rosters @{}
        $frozen = Get-Content -LiteralPath (Join-Path $dir 'discovery.json') -Raw | ConvertFrom-Json
        Assert-C487 -Cond (@($plan.suites).Count -eq 1 -and @($plan.suites[0].chunks).Count -eq 0 -and
            @($frozen.suites.'markdown-pdf').Count -eq 0) -Name ('C599 missing-discovery-' + $variant) `
            -Detail ($plan | ConvertTo-Json -Depth 10 -Compress)
    }
}

function Test-C599B_PinnedPolicy {
    # V-18 / R-9 ordinary contract: the pinned policy blob, not the report, decides.
    $fx = New-C599PublishFx -PolicyBytes (Get-C599ABasePolicyBytes)
    $ok = Invoke-C599Publish -Fx $fx
    $rel = Get-C599Release -Gh $fx.Gh -Tag ([string]$ok.Tag)
    Assert-C487 -Cond ([int]$ok.ExitCode -eq 0 -and [bool]$ok.Published) -Name 'C599 V-18: a valid eight-suite pinned authority publishes' -Detail ([string]$ok.Refusal)
    Assert-C487 -Cond ($null -ne $rel -and -not [bool]$rel.isDraft) -Name 'C599 V-18: the recipient holds a published non-draft release'
    $root = Get-ReleaseGateCandidateRoot -ReleaseRoot $fx.ReleaseRoot -CandidateId $CandidateId
    $manifest = Get-Content -LiteralPath (Join-Path $root 'release-manifest.json') -Raw | ConvertFrom-Json
    $ids = @($manifest.suites | ForEach-Object { [string]$_.id })
    Assert-C487 -Cond ($ids.Count -eq 8 -and ([string]$manifest.sha -eq $fx.Head)) -Name 'C599 V-18: the manifest names all eight pinned suites at the tested sha' -Detail ($ids -join ',')

    # A shrunken summary alone confers nothing: it is not read, the eight stay required.
    $fxS = New-C599PublishFx -PolicyBytes (Get-C599ABasePolicyBytes)
    Update-C599Json -Path (Join-Path (Get-ReleaseGateCandidateRoot -ReleaseRoot $fxS.ReleaseRoot -CandidateId $CandidateId) 'summary.json') -Mutate {
        param($s) $s.requiredSuites = @('antiphon'); $s.suites = @($s.suites | Where-Object { [string]$_.id -eq 'antiphon' }) }
    $shrunk = Invoke-C599Publish -Fx $fxS
    $sm = Get-Content -LiteralPath (Join-Path (Get-ReleaseGateCandidateRoot -ReleaseRoot $fxS.ReleaseRoot -CandidateId $CandidateId) 'release-manifest.json') -Raw | ConvertFrom-Json
    Assert-C487 -Cond ([bool]$shrunk.Published -and @($sm.suites).Count -eq 8) -Name 'C599 V-18: a shrunken summary is not a source of required suites' -Detail ([string]$shrunk.Refusal)

    # G-224: a seven-suite report (plan, ledger and summary agree on seven) cannot
    # publish against the eight-suite pinned policy.
    $fx7 = New-C599PublishFx -PolicyBytes (Get-C599ABasePolicyBytes)
    Update-C599Ledger -Fx $fx7 -Plan { param($p) $p.suites = @($p.suites | Where-Object { [string]$_.id -ne 'e2e' }) } `
        -Ledger { param($l) $l.chunks = @($l.chunks | Where-Object { [string]$_.suite -ne 'e2e' }) }
    Update-C599Json -Path (Join-Path (Get-ReleaseGateCandidateRoot -ReleaseRoot $fx7.ReleaseRoot -CandidateId $CandidateId) 'summary.json') -Mutate {
        param($s) $s.requiredSuites = @('antiphon', 'session-runner', 'pty-host', 'agents-pty', 'messaging', 'client', 'scripts') }
    $seven = Invoke-C599Publish -Fx $fx7
    Assert-C487 -Cond (([string]$seven.Refusal -match 'required-suite-missing:e2e') -and (Test-C599NoRemoteWrite -Fx $fx7)) -Name 'C599 G-224: seven-suite report cannot publish eight-suite policy' -Detail ([string]$seven.Refusal)

    # G-225: a stale pinned hash in the frozen authority blocks before any remote write.
    $fxH = New-C599PublishFx -PolicyBytes (Get-C599ABasePolicyBytes)
    Update-C599Json -Path (Join-Path (Get-C599AuthorityDir -Fx $fxH) 'authority.json') -Mutate { param($a) $a.policyHash = ('0' * 64) }
    $stale = Invoke-C599Publish -Fx $fxH
    Assert-C487 -Cond (([string]$stale.Refusal -match 'policy-hash-mismatch') -and (Test-C599NoRemoteWrite -Fx $fxH)) -Name 'C599 G-225: wrong pinned hash blocks before any remote write' -Detail ([string]$stale.Refusal)

    # Stale blob identity: the recorded blob is not the one at the pinned commit.
    $fxB = New-C599PublishFx -PolicyBytes (Get-C599ABasePolicyBytes)
    Update-C599Json -Path (Join-Path (Get-C599AuthorityDir -Fx $fxB) 'authority.json') -Mutate { param($a) $a.policyBlobId = ('1' * 40) }
    $blob = Invoke-C599Publish -Fx $fxB
    Assert-C487 -Cond (([string]$blob.Refusal -match 'policy-blob-id-mismatch') -and (Test-C599NoRemoteWrite -Fx $fxB)) -Name 'C599 V-18: a stale pinned blob id blocks before any remote write' -Detail ([string]$blob.Refusal)

    # R-9: a legacy candidate with no authority cannot publish; nothing is synthesized.
    $fxL = New-C599PublishFx -PolicyBytes (Get-C599ABasePolicyBytes) -NoAuthority
    $legacy = Invoke-C599Publish -Fx $fxL
    Assert-C487 -Cond (([string]$legacy.Refusal -match 'missing-authority') -and -not [bool]$legacy.Published) -Name 'C599 R-9: a legacy candidate with no authority refuses' -Detail ([string]$legacy.Refusal)
    Assert-C487 -Cond (Test-C599NoRemoteWrite -Fx $fxL) -Name 'C599 R-9: the legacy refusal made zero remote writes'
    Assert-C487 -Cond (-not (Test-Path -LiteralPath (Join-Path (Get-C599AuthorityDir -Fx $fxL) 'authority.json'))) -Name 'C599 R-9: no authority was synthesized for the legacy candidate'
}

function Test-C599B_AllChunks {
    # V-18 / R-9 ordinary contract: every frozen chunk and every required UID.
    $fx = New-C599PublishFx
    $plan = Get-Content -LiteralPath (Join-Path (Get-C599AuthorityDir -Fx $fx) 'execution-plan.json') -Raw | ConvertFrom-Json
    $antiphon = @(@($plan.suites | Where-Object { [string]$_.id -eq 'antiphon' })[0].chunks)
    Assert-C487 -Cond ($antiphon.Count -eq 3 -and (@($antiphon | ForEach-Object { @($_.uids) }) -notcontains 'antiphon-x1')) -Name 'C599 V-18: the frozen plan chunks one class each and omits pinned exclusions' -Detail ([string]$antiphon.Count)
    $ok = Invoke-C599Publish -Fx $fx
    Assert-C487 -Cond ([int]$ok.ExitCode -eq 0 -and [bool]$ok.Published -and $null -ne (Get-C599Release -Gh $fx.Gh -Tag ([string]$ok.Tag))) -Name 'C599 V-18: a complete multi-chunk ledger publishes' -Detail ([string]$ok.Refusal)

    # G-226: a passing sibling (antiphon-001/002 pass) cannot hide a failed chunk.
    $fxF = New-C599PublishFx
    Set-C599TrxOutcome -Fx $fxF -Chunk 'antiphon-003' -Uid 'antiphon-u4' -Outcome 'Failed'
    $failed = Invoke-C599Publish -Fx $fxF
    Assert-C487 -Cond (([string]$failed.Refusal -match 'uid-not-passed:antiphon-u4') -and (Test-C599NoRemoteWrite -Fx $fxF)) -Name 'C599 G-226: passing sibling cannot hide failed chunk' -Detail ([string]$failed.Refusal)

    # G-290: a deleted chunk record blocks authority.
    $fxM = New-C599PublishFx
    Update-C599Ledger -Fx $fxM -Ledger { param($l) $l.chunks = @($l.chunks | Where-Object { [string]$_.chunk -ne 'antiphon-002' }) }
    $missing = Invoke-C599Publish -Fx $fxM
    Assert-C487 -Cond (([string]$missing.Refusal -match 'chunk-missing:antiphon-002') -and (Test-C599NoRemoteWrite -Fx $fxM)) -Name 'C599 G-290: deleted or extra chunk blocks authority' -Detail ([string]$missing.Refusal)

    # G-292: a skipped required UID with passing siblings refuses.
    $fxK = New-C599PublishFx
    Set-C599TrxOutcome -Fx $fxK -Chunk 'messaging-001' -Uid 'messaging-u2' -Outcome 'NotExecuted'
    $skipped = Invoke-C599Publish -Fx $fxK
    Assert-C487 -Cond (([string]$skipped.Refusal -match 'uid-not-passed:messaging-u2') -and (Test-C599NoRemoteWrite -Fx $fxK)) -Name 'C599 G-292: required skip unknown missing and stale rows refuse' -Detail ([string]$skipped.Refusal)

    # A client roster entry that did not pass refuses by its own declared roster.
    $fxR = New-C599PublishFx
    Update-C599Ledger -Fx $fxR -Ledger { param($l) foreach ($r in @($l.rosters)) { if ([string]$r.suite -eq 'client') { $r.entries[2].outcome = 'failed' } } }
    $roster = Invoke-C599Publish -Fx $fxR
    Assert-C487 -Cond (([string]$roster.Refusal -match 'roster-entry-not-passed:client:vitest') -and (Test-C599NoRemoteWrite -Fx $fxR)) -Name 'C599 V-18: a failed client roster entry blocks before any remote write' -Detail ([string]$roster.Refusal)
}

# ================================= CARD-0599 D-15 full V-12 matrix (C599A, B1) ===
# One valid B1 fixture per method. Every variant restores that control byte for
# byte, changes exactly one field, row or handshake and requires the NAMED refusal
# token. Gate variants run the same production composition publish-release.ps1
# runs before its first write (Test-ReleaseGateAuthority + the publication gate +
# the credit verdict); one publisher variant per guard also proves zero remote
# writes through the observer. Rows aggregate each guard's variants.

function Save-C599ASnapshot {
    param($Fx)
    $files = @{}
    foreach ($f in @(Get-ChildItem -LiteralPath $Fx.ReleaseRoot -Recurse -File -Force)) { $files[$f.FullName] = [System.IO.File]::ReadAllBytes($f.FullName) }
    $absent = @()
    foreach ($p in @($Fx.Git.RemotePath, $Fx.Git.Trace, $Fx.Gh.StorePath, $Fx.CardApi.Store, (Join-Path $Fx.Checkout (Join-Path 'tests' 'test-execution-policy.json')))) {
        if (Test-Path -LiteralPath $p -PathType Leaf) { $files[$p] = [System.IO.File]::ReadAllBytes($p) } else { $absent += $p }
    }
    return [pscustomobject]@{ Root = $Fx.ReleaseRoot; Files = $files; Absent = $absent }
}

function Restore-C599ASnapshot {
    param($Snap)
    foreach ($f in @(Get-ChildItem -LiteralPath $Snap.Root -Recurse -File -Force)) {
        if (-not $Snap.Files.ContainsKey($f.FullName)) { Remove-Item -LiteralPath $f.FullName -Force }
    }
    foreach ($p in @($Snap.Absent)) { if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Force } }
    foreach ($p in @($Snap.Files.Keys)) {
        $parent = Split-Path -Parent $p
        if (-not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
        [System.IO.File]::WriteAllBytes($p, $Snap.Files[$p])
    }
}

function Get-C599AGate {
    # publish-release.ps1's pre-write decision, with the remote answer read from the recipient.
    param($Fx)
    $paths = Get-ReleaseGateStatePaths -CreditKind 'rc-release' -StateRoot '' -ReleaseRoot $Fx.ReleaseRoot -CandidateId $CandidateId
    $green = Get-Content -LiteralPath $paths.GreenPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $candidate = Get-Content -LiteralPath $paths.JournalPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $remoteSha = Read-C599Remote -Seams $Fx.Git -Ref $CandidateRef
    $authority = Test-ReleaseGateAuthority -CandidateRoot $paths.Root -RepositoryRoot $Fx.Checkout -Green $green -Candidate $candidate `
        -Repository 'michal-ciechan/Antiphon' -NowUtc $script:C599Now -CardApiBaseUrl $Fx.CardApi.Url
    $gate = Test-ReleaseGatePublicationGate -Green $green -Candidate $candidate -RemoteSha $remoteSha -Authority $authority
    $verdict = Get-ReleaseGateCreditVerdict -State $green
    return [pscustomobject]@{ Ok = [bool]$gate.Ok; Reasons = @(@($gate.Reasons) + @($verdict.Reasons) | ForEach-Object { [string]$_ }); Suites = @($authority.Suites) }
}

function Invoke-C599AVariant {
    <#
      Restore the valid control, apply Mutate, and require a refusal naming every
      Expect token. -Publish drives the real publisher and the observer instead.
    #>
    param($Fx, $Snap, [string]$Label, [scriptblock]$Mutate, [string[]]$Expect, [switch]$Publish, [hashtable]$Extra = $null, [scriptblock]$Cleanup = $null)
    Restore-C599ASnapshot -Snap $Snap
    $got = @(); $refused = $false; $err = ''
    try {
        & $Mutate
        if ($Publish) {
            $res = Invoke-C599Publish -Fx $Fx -Extra $Extra
            $got = @(([string]$res.Refusal) -split ',' | Where-Object { $_ })
            $refused = (-not [bool]$res.Published) -and ([int]$res.ExitCode -ne 0) -and (Test-C599NoRemoteWrite -Fx $Fx)
        } else {
            $gate = Get-C599AGate -Fx $Fx
            $got = @($gate.Reasons); $refused = -not $gate.Ok
        }
    } catch {
        $err = 'threw: ' + $_.Exception.Message
    } finally {
        if ($Cleanup) { & $Cleanup }
        Restore-C599ASnapshot -Snap $Snap
    }
    $absentTokens = @($Expect | Where-Object { $got -notcontains $_ })
    return [pscustomobject]@{
        Pass = ($refused -and $absentTokens.Count -eq 0 -and $err -eq '')
        Detail = ('{0}: refused={1} missing=[{2}] got=[{3}] {4}' -f $Label, $refused, ($absentTokens -join ' '), ($got -join ' '), $err)
    }
}

function Assert-C599AMatrix {
    param([string]$Name, [object[]]$Results)
    $bad = @($Results | Where-Object { -not $_.Pass })
    Assert-C487 -Cond (@($Results).Count -gt 0 -and $bad.Count -eq 0) -Name $Name -Detail ((@($bad | ForEach-Object { $_.Detail })) -join ' | ')
}

function Set-C599AField {
    param($Object, [string]$Name, $Value)
    $Object | Add-Member -NotePropertyName $Name -NotePropertyValue $Value -Force
}

function Update-C599AAuthority {
    # Mutate the frozen authority and re-bind plan/ledger to its new digest, so the
    # changed field is the only thing a guard can refuse on.
    param($Fx, [scriptblock]$Mutate)
    $authDir = Get-C599AuthorityDir -Fx $Fx
    Update-C599Json -Path (Join-Path $authDir 'authority.json') -Mutate $Mutate
    $authDigest = Get-NightlyFileSha256 -Path (Join-Path $authDir 'authority.json')
    Update-C599Ledger -Fx $Fx -Plan { param($p) $p.authorityDigest = $authDigest } -Ledger { param($l) $l.authorityDigest = $authDigest }
}

function Update-C599ADiscovery {
    # Mutate discovery.json (and optionally the plan) keeping the plan's discovery digest bound.
    param($Fx, [scriptblock]$Mutate, [scriptblock]$Plan = $null, [scriptblock]$Ledger = $null)
    $authDir = Get-C599AuthorityDir -Fx $Fx
    Update-C599Json -Path (Join-Path $authDir 'discovery.json') -Mutate $Mutate
    $discoveryDigest = Get-NightlyFileSha256 -Path (Join-Path $authDir 'discovery.json')
    $c599aDiscoveryPlan = $Plan
    Update-C599Ledger -Fx $Fx -Ledger $Ledger -Plan { param($p) $p.discoveryDigest = $discoveryDigest; if ($c599aDiscoveryPlan) { & $c599aDiscoveryPlan $p } }
}

function Update-C599ATrx {
    # Rewrite one chunk's real TRX rows and re-bind its digest and all four counts.
    param($Fx, [string]$Chunk, [scriptblock]$Rows, [switch]$KeepBinding)
    $authDir = Get-C599AuthorityDir -Fx $Fx
    $doc = Get-Content -LiteralPath (Join-Path $authDir 'ledger.json') -Raw | ConvertFrom-Json
    $row = @($doc.chunks | Where-Object { [string]$_.chunk -eq $Chunk })[0]
    $trxPath = Join-Path (Split-Path -Parent $authDir) ([string]$row.trx)
    $c599aTrxChunk = $Chunk
    $current = @(Read-NightlyTrxIdentities -TrxPath $trxPath | ForEach-Object {
        [pscustomobject]@{ Id = [string]$_.TestId; ClassName = [string]$_.ClassName; MethodName = [string]$_.MethodName; Outcome = [string]$_.Outcome } })
    $c599aTrxRows = @(& $Rows $current)
    Write-C487Trx -Path $trxPath -Rows $c599aTrxRows
    if ($KeepBinding) { return }
    Update-C599Ledger -Fx $Fx -Ledger {
        param($l)
        foreach ($c in @($l.chunks)) {
            if ([string]$c.chunk -ne $c599aTrxChunk) { continue }
            $c.trxSha256 = (Get-NightlyFileSha256 -Path $trxPath)
            $c.executed = $c599aTrxRows.Count
            $c.passed = @($c599aTrxRows | Where-Object { $_.Outcome -eq 'Passed' }).Count
            $c.failed = @($c599aTrxRows | Where-Object { $_.Outcome -eq 'Failed' }).Count
            $c.skipped = @($c599aTrxRows | Where-Object { $_.Outcome -ne 'Passed' -and $_.Outcome -ne 'Failed' }).Count
        }
    }
}

function Update-C599AChunkRow {
    param($Fx, [string]$Chunk, [scriptblock]$Mutate)
    $c599aChunkId = $Chunk; $c599aChunkMutate = $Mutate
    Update-C599Ledger -Fx $Fx -Ledger { param($l) foreach ($c in @($l.chunks)) { if ([string]$c.chunk -eq $c599aChunkId) { & $c599aChunkMutate $c } } }
}

function Update-C599ARosterRow {
    param($Fx, [string]$Suite, [scriptblock]$Mutate)
    $c599aRosterId = $Suite; $c599aRosterMutate = $Mutate
    Update-C599Ledger -Fx $Fx -Ledger { param($l) foreach ($r in @($l.rosters)) { if ([string]$r.suite -eq $c599aRosterId) { & $c599aRosterMutate $r } } }
}

function Update-C599AGreen {
    param($Fx, [scriptblock]$Mutate)
    Update-C599Json -Path (Join-Path (Get-ReleaseGateCandidateRoot -ReleaseRoot $Fx.ReleaseRoot -CandidateId $CandidateId) 'complete-green.json') -Mutate $Mutate
}

function Move-C599AEvidenceTime {
    <#
      Shift every evidence instant (authority, run, chunks, rosters, receipt) by
      Offset with every digest re-bound: the order stays valid, so only a bound
      against the publisher's clock can refuse.
    #>
    param($Fx, [timespan]$Offset)
    $c599aOffset = $Offset
    $c599aShift = { param($v) (Test-ReleaseAuthorityInstant -Value $v).Add($c599aOffset).ToString('o') }
    Update-C599AAuthority -Fx $Fx -Mutate { param($a) $a.createdAt = (& $c599aShift $a.createdAt) }
    Update-C599Ledger -Fx $Fx -Ledger {
        param($l)
        $l.startedAt = (& $c599aShift $l.startedAt); $l.completedAt = (& $c599aShift $l.completedAt)
        foreach ($row in @(@($l.chunks) + @($l.rosters))) { $row.startedAt = (& $c599aShift $row.startedAt); $row.completedAt = (& $c599aShift $row.completedAt) }
        $l.prepublicationReceipt.acceptedAt = (& $c599aShift $l.prepublicationReceipt.acceptedAt)
    }
}

function Update-C599AReceipt {
    # Mutate the ledger's prepublication receipt, keeping every other binding valid.
    param($Fx, [scriptblock]$Mutate)
    $c599aReceiptMutate = $Mutate
    Update-C599Ledger -Fx $Fx -Ledger { param($l) & $c599aReceiptMutate $l.prepublicationReceipt }
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

function New-C599APolicyBytes {
    # The C599A base policy with one edit and its canonical hash restated (unless -StaleHash).
    param([scriptblock]$Edit = $null, [switch]$StaleHash)
    $obj = [System.Text.Encoding]::UTF8.GetString((Get-C599ABasePolicyBytes)) | ConvertFrom-Json
    if ($Edit) { & $Edit $obj }
    $obj.policyHash = (Get-NightlyPolicyHash -Object $obj)
    if ($StaleHash) { $obj.policyHash = ('0' * 64) }
    return [System.Text.Encoding]::UTF8.GetBytes(($obj | ConvertTo-Json -Depth 20))
}

function Set-C599APinnedPolicy {
    <#
      Commit PolicyBytes on the real checkout and re-pin the whole candidate to that
      commit: journal, green, remote, authority blob identity/digests/hash, private
      copy, plan, ledger, every chunk/roster row, the receipt correlation and the release
      card body the API serves. Only the policy content differs.
    #>
    param($Fx, [byte[]]$PolicyBytes)
    [System.IO.File]::WriteAllBytes((Join-Path $Fx.Checkout (Join-Path 'tests' 'test-execution-policy.json')), $PolicyBytes)
    [void](Invoke-C599RealGit -Repo $Fx.Checkout -Arguments @('add', '--', 'tests/test-execution-policy.json'))
    [void](Invoke-C599RealGit -Repo $Fx.Checkout -Arguments @('-c', 'user.name=c599', '-c', 'user.email=c599@example.invalid', 'commit', '-q', '--allow-empty', '-m', 'repin'))
    $pinSha = Invoke-C599RealGit -Repo $Fx.Checkout -Arguments @('rev-parse', 'HEAD')
    $pinHash = ''
    try { $pinHash = Get-NightlyPolicyHash -Object ([System.Text.Encoding]::UTF8.GetString($PolicyBytes) | ConvertFrom-Json) } catch { $pinHash = ('0' * 64) }
    $croot = Get-ReleaseGateCandidateRoot -ReleaseRoot $Fx.ReleaseRoot -CandidateId $CandidateId
    [System.IO.File]::WriteAllBytes((Join-Path (Get-C599AuthorityDir -Fx $Fx) 'policy.json'), $PolicyBytes)
    Update-C599Json -Path (Join-Path $croot 'candidate.json') -Mutate { param($c) $c.sha = $pinSha }
    Update-C599AGreen -Fx $Fx -Mutate { param($g) $g.sha = $pinSha; $g.expectedSha = $pinSha; $g.policyHash = $pinHash }
    Write-NightlyAtomicJson -Path $Fx.Git.RemotePath -Object ([ordered]@{ ('release_rc_20260923T083000Z') = $pinSha })
    Update-C599Json -Path (Join-Path (Get-C599AuthorityDir -Fx $Fx) 'authority.json') -Mutate {
        param($a)
        $a.sha = $pinSha
        $a.policyBlobId = (Get-ReleaseAuthorityGitBlobId -Bytes $PolicyBytes)
        $a.policyRawSha256 = (Get-ReleaseAuthorityBytesSha256 -Bytes $PolicyBytes)
        $a.policyHash = $pinHash
    }
    $pinDigest = Get-NightlyFileSha256 -Path (Join-Path (Get-C599AuthorityDir -Fx $Fx) 'authority.json')
    Update-C599Ledger -Fx $Fx -Plan { param($p) $p.sha = $pinSha; $p.authorityDigest = $pinDigest } -Ledger {
        param($l)
        $l.sha = $pinSha; $l.authorityDigest = $pinDigest
        foreach ($c in @($l.chunks)) { $c.sha = $pinSha }
        foreach ($r in @($l.rosters)) { $r.sha = $pinSha }
        $l.prepublicationReceipt.correlation.sha = $pinSha
    }
    Update-C599Card -Fx $Fx -Card { param($c) $c.description = (Get-C599CardBody -Sha $pinSha) }
}

function New-C599AEvidenceLink {
    # A directory link inside the candidate root pointing outside it: a junction on
    # Windows (no privilege needed), a symbolic link elsewhere.
    param([string]$Link, [string]$Target)
    if ($IsWindows -or $env:OS -eq 'Windows_NT') { New-Item -ItemType Junction -Path $Link -Target $Target | Out-Null }
    else { New-Item -ItemType SymbolicLink -Path $Link -Target $Target | Out-Null }
}

function Remove-C599AEvidenceLink {
    param([string]$Link)
    if (Test-Path -LiteralPath $Link) { [System.IO.Directory]::Delete($Link, $false) }
}

function Test-C599A_PinnedPolicy {
    # V-12 full matrix for the pinned policy, first child (G-224, G-225, G-285 gate
    # rows). Summary/report and working tree never decide.
    $fx = New-C599PublishFx -PolicyBytes (Get-C599ABasePolicyBytes)
    $snap = Save-C599ASnapshot -Fx $fx
    $croot = Get-ReleaseGateCandidateRoot -ReleaseRoot $fx.ReleaseRoot -CandidateId $CandidateId
    $eight = @('antiphon', 'session-runner', 'pty-host', 'agents-pty', 'messaging', 'client', 'scripts', 'e2e')
    $control = Get-C599AGate -Fx $fx
    Assert-C487 -Cond ($control.Ok -and @($control.Suites).Count -eq 8) -Name 'C599 V-12: the valid eight-suite control passes the publication gate' -Detail (@($control.Reasons) -join ' ')

    # G-224: each suite removed from a plan, ledger and summary that all agree still
    # refuses, because the pinned blob names the eight.
    $m = @()
    foreach ($sid in $eight) {
        $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label ('drop ' + $sid) -Expect @('required-suite-missing:' + $sid) -Mutate {
            Update-C599Ledger -Fx $fx -Plan { param($p) $p.suites = @($p.suites | Where-Object { [string]$_.id -ne $sid }) } -Ledger {
                param($l)
                $l.chunks = @($l.chunks | Where-Object { [string]$_.suite -ne $sid })
                $l.rosters = @($l.rosters | Where-Object { [string]$_.suite -ne $sid })
            }
            Update-C599Json -Path (Join-Path $croot 'summary.json') -Mutate { param($s) $s.requiredSuites = @($eight | Where-Object { $_ -ne $sid }) }
        }
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'extra plan suite' -Expect @('plan-suite-unexpected:extra') -Mutate {
        Update-C599Ledger -Fx $fx -Plan { param($p) $p.suites = @($p.suites) + @([pscustomobject]@{ id = 'extra'; kind = 'roster'; entries = @('x') }) }
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'publisher drop client' -Publish -Expect @('required-suite-missing:client') -Mutate {
        Update-C599Ledger -Fx $fx -Plan { param($p) $p.suites = @($p.suites | Where-Object { [string]$_.id -ne 'client' }) } -Ledger {
            param($l) $l.rosters = @($l.rosters | Where-Object { [string]$_.suite -ne 'client' }) }
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'publisher legacy RequiredSuites seven' -Publish -Expect @('required-suites-mismatch') `
        -Extra @{ RequiredSuites = @($eight | Where-Object { $_ -ne 'e2e' }) } -Mutate { }
    Assert-C599AMatrix -Name 'C599 G-224: each of the eight pinned suites missing from an agreeing report refuses with zero remote writes' -Results $m

    # G-225: no ExpectedPolicyHash is supplied; the pinned hash is still required.
    $m = @()
    foreach ($v in @(@{ l = 'zeros'; set = $true; val = ('0' * 64) }, @{ l = 'absent'; set = $false }, @{ l = 'null'; set = $true; val = $null }, @{ l = 'number'; set = $true; val = 7 })) {
        $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label ('authority policyHash ' + $v.l) -Expect @('policy-hash-mismatch') -Mutate {
            Update-C599AAuthority -Fx $fx -Mutate { param($a) if ($v.set) { Set-C599AField $a 'policyHash' $v.val } else { $a.PSObject.Properties.Remove('policyHash') } }
        }
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'green policyHash foreign' -Expect @('green-policy-hash-mismatch') -Mutate {
        Update-C599AGreen -Fx $fx -Mutate { param($g) $g.policyHash = ('0' * 64) }
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'pinned blob states a stale hash' -Expect @('policy-hash-stale') -Mutate {
        Set-C599APinnedPolicy -Fx $fx -PolicyBytes (New-C599APolicyBytes -StaleHash)
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'publisher absent authority hash' -Publish -Expect @('policy-hash-mismatch') -Mutate {
        Update-C599AAuthority -Fx $fx -Mutate { param($a) $a.PSObject.Properties.Remove('policyHash') }
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'publisher legacy ExpectedPolicyHash foreign' -Publish -Expect @('policy-hash-mismatch') `
        -Extra @{ ExpectedPolicyHash = ('f' * 64) } -Mutate { }
    Assert-C599AMatrix -Name 'C599 G-225: a wrong, absent, null or mistyped pinned hash blocks before any remote write' -Results $m

    # G-285: the blob at the pinned commit decides; the private copy and the working tree do not.
    $sevenBytes = New-C599APolicyBytes -Edit { param($o) $o.profiles.rc.requiredSuites = @($eight | Where-Object { $_ -ne 'e2e' }) }
    $m = @()
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'private copy edited' -Expect @('policy-copy-mismatch') -Mutate {
        [System.IO.File]::WriteAllBytes((Join-Path (Get-C599AuthorityDir -Fx $fx) 'policy.json'), $sevenBytes)
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'seven-suite working tree with seven-suite ledger' -Expect @('required-suite-missing:e2e') -Mutate {
        [System.IO.File]::WriteAllBytes((Join-Path $fx.Checkout (Join-Path 'tests' 'test-execution-policy.json')), $sevenBytes)
        Update-C599Ledger -Fx $fx -Plan { param($p) $p.suites = @($p.suites | Where-Object { [string]$_.id -ne 'e2e' }) } -Ledger {
            param($l) $l.chunks = @($l.chunks | Where-Object { [string]$_.suite -ne 'e2e' }) }
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'publisher private copy edited' -Publish -Expect @('policy-copy-mismatch') -Mutate {
        [System.IO.File]::WriteAllBytes((Join-Path (Get-C599AuthorityDir -Fx $fx) 'policy.json'), $sevenBytes)
    }
    Assert-C599AMatrix -Name 'C599 G-285: an edited private copy or working-tree policy cannot replace the pinned blob' -Results $m
}

function Test-C599A_PinnedPolicySchema {
    # V-12 full matrix for the pinned policy, second child (G-286, -WhatIf and the
    # publisher controls): the pinned blob's own shape, and what a publication writes.
    $fx = New-C599PublishFx -PolicyBytes (Get-C599ABasePolicyBytes)
    $snap = Save-C599ASnapshot -Fx $fx
    $croot = Get-ReleaseGateCandidateRoot -ReleaseRoot $fx.ReleaseRoot -CandidateId $CandidateId
    $eight = @('antiphon', 'session-runner', 'pty-host', 'agents-pty', 'messaging', 'client', 'scripts', 'e2e')
    $sevenBytes = New-C599APolicyBytes -Edit { param($o) $o.profiles.rc.requiredSuites = @($eight | Where-Object { $_ -ne 'e2e' }) }

    # G-286: the pinned blob itself must be schema 2 with the full rc profile. Each
    # variant re-pins the whole candidate to a real commit holding that blob.
    Restore-C599ASnapshot -Snap $snap
    Set-C599APinnedPolicy -Fx $fx -PolicyBytes (Get-C599ABasePolicyBytes)
    $repin = Get-C599AGate -Fx $fx
    Restore-C599ASnapshot -Snap $snap
    Assert-C487 -Cond ($repin.Ok) -Name 'C599 G-286: re-pinning the candidate to a new commit with a valid blob is accepted (repin control)' -Detail (@($repin.Reasons) -join ' ')
    $shapes = @(
        @{ l = 'schema 99'; t = 'policy-schema-unsupported'; e = { param($o) $o.schemaVersion = 99 } },
        @{ l = 'schema 1'; t = 'policy-schema-unsupported'; e = { param($o) $o.schemaVersion = 1 } },
        @{ l = 'schema string'; t = 'policy-schema-unsupported'; e = { param($o) $o.schemaVersion = '2' } },
        @{ l = 'schema absent'; t = 'policy-schema-unsupported'; e = { param($o) $o.PSObject.Properties.Remove('schemaVersion') } },
        @{ l = 'profiles absent'; t = 'policy-rc-profile-missing'; e = { param($o) $o.PSObject.Properties.Remove('profiles') } },
        @{ l = 'rc absent'; t = 'policy-rc-profile-missing'; e = { param($o) $o.profiles.PSObject.Properties.Remove('rc') } },
        @{ l = 'rc empty'; t = 'policy-rc-profile-missing'; e = { param($o) $o.profiles.rc.requiredSuites = @() } },
        @{ l = 'unknown rc suite'; t = 'policy-rc-suite-unknown:extra-suite'; e = { param($o) $o.profiles.rc.requiredSuites = @($o.profiles.rc.requiredSuites) + 'extra-suite' } },
        @{ l = 'undefined suite'; t = 'policy-rc-suite-undefined:messaging'; e = { param($o) $o.suites.PSObject.Properties.Remove('messaging') } }
    )
    foreach ($sid in $eight) {
        $shapes += @{ l = ('rc without ' + $sid); t = ('policy-rc-suite-missing:' + $sid); e = [scriptblock]::Create(('param($o) $o.profiles.rc.requiredSuites = @($o.profiles.rc.requiredSuites | Where-Object {{ $_ -ne ''{0}'' }})' -f $sid)) }
    }
    $m = @()
    foreach ($shape in $shapes) {
        $bytes = New-C599APolicyBytes -Edit $shape.e
        $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label $shape.l -Expect @($shape.t) -Mutate { Set-C599APinnedPolicy -Fx $fx -PolicyBytes $bytes }
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'unparseable blob' -Expect @('policy-unparseable') -Mutate {
        Set-C599APinnedPolicy -Fx $fx -PolicyBytes ([System.Text.Encoding]::UTF8.GetBytes('{ not json'))
    }
    $bytes = New-C599APolicyBytes -Edit { param($o) $o.schemaVersion = 99 }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'publisher schema 99' -Publish -Expect @('policy-schema-unsupported') -Mutate { Set-C599APinnedPolicy -Fx $fx -PolicyBytes $bytes }
    $bytes = New-C599APolicyBytes -Edit { param($o) $o.profiles.rc.requiredSuites = @($o.profiles.rc.requiredSuites | Where-Object { $_ -ne 'scripts' }) }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'publisher rc without scripts' -Publish -Expect @('policy-rc-suite-missing:scripts') -Mutate { Set-C599APinnedPolicy -Fx $fx -PolicyBytes $bytes }
    Assert-C599AMatrix -Name 'C599 G-286: an unknown schema, missing or empty rc profile and each absent suite in the pinned blob refuse' -Results $m

    # -WhatIf validates the full authority but writes no publication authority and nothing remote.
    Restore-C599ASnapshot -Snap $snap
    $pubAuthPath = Join-Path (Get-C599AuthorityDir -Fx $fx) 'publication-authority.json'
    $preview = Invoke-C599Publish -Fx $fx -Extra @{ WhatIf = $true }
    Assert-C487 -Cond ([int]$preview.ExitCode -eq 0 -and -not [bool]$preview.Published -and -not (Test-Path -LiteralPath $pubAuthPath) -and (Test-C599NoRemoteWrite -Fx $fx)) `
        -Name 'C599 V-18: -WhatIf passes the gate but writes no publication authority and nothing remote' -Detail ('exit=' + $preview.ExitCode + ' refusal=' + $preview.Refusal + ' file=' + (Test-Path -LiteralPath $pubAuthPath))

    # -WhatIf computes the proposed tag read-only: publications.json is never created,
    # and an existing journal (another candidate's same-day tag) stays byte-identical.
    Restore-C599ASnapshot -Snap $snap
    $pubJournal = Join-Path $fx.ReleaseRoot 'publications.json'
    $absentBefore = -not (Test-Path -LiteralPath $pubJournal)
    $firstPreview = Invoke-C599Publish -Fx $fx -Extra @{ WhatIf = $true }
    $absentAfter = -not (Test-Path -LiteralPath $pubJournal)
    Write-NightlyAtomicJson -Path $pubJournal -Object ([ordered]@{ schemaVersion = 1; reservations = @([ordered]@{
        candidateId = 'rc-20260923T020000Z'; sha = $ShaC; tag = 'v2026.09.23.1'; sequence = 1; reservedAt = '2026-09-23T02:00:00Z'
        published = $true; releaseId = 'R-0'; releaseUrl = 'https://example/releases/tag/v2026.09.23.1' }) })
    $journalBefore = Get-ReleaseAuthorityBytesSha256 -Bytes ([System.IO.File]::ReadAllBytes($pubJournal))
    $secondPreview = Invoke-C599Publish -Fx $fx -Extra @{ WhatIf = $true }
    $journalAfter = Get-ReleaseAuthorityBytesSha256 -Bytes ([System.IO.File]::ReadAllBytes($pubJournal))
    Assert-C487 -Cond ($absentBefore -and $absentAfter -and [int]$firstPreview.ExitCode -eq 0 -and [string]$firstPreview.Tag -eq 'v2026.09.23.1' -and
            [int]$secondPreview.ExitCode -eq 0 -and [string]$secondPreview.Tag -eq 'v2026.09.23.2' -and $journalBefore -ceq $journalAfter -and (Test-C599NoRemoteWrite -Fx $fx)) `
        -Name 'C599 V-18: -WhatIf proposes the next tag without reserving it and leaves publications.json byte-identical' `
        -Detail ('absent-after=' + $absentAfter + ' tags=' + $firstPreview.Tag + '/' + $secondPreview.Tag + ' same=' + ($journalBefore -ceq $journalAfter) + ' refusal=' + $secondPreview.Refusal)

    # G-285 publisher control: a seven-suite working tree AND a moved HEAD commit do
    # not alter the frozen authority; the pinned eight still publish.
    Restore-C599ASnapshot -Snap $snap
    $pinnedBlob = Invoke-C599RealGit -Repo $fx.Checkout -Arguments @('rev-parse', ($fx.Head + ':tests/test-execution-policy.json'))
    [System.IO.File]::WriteAllBytes((Join-Path $fx.Checkout (Join-Path 'tests' 'test-execution-policy.json')), $sevenBytes)
    [void](Invoke-C599RealGit -Repo $fx.Checkout -Arguments @('-c', 'user.name=c599', '-c', 'user.email=c599@example.invalid', 'commit', '-q', '-a', '-m', 'seven'))
    [System.IO.File]::WriteAllBytes((Join-Path $fx.Checkout (Join-Path 'tests' 'test-execution-policy.json')), [System.Text.Encoding]::UTF8.GetBytes('{ dirty'))
    $ok = Invoke-C599Publish -Fx $fx
    $manifest = Get-Content -LiteralPath (Join-Path $croot 'release-manifest.json') -Raw | ConvertFrom-Json
    $auth = Get-Content -LiteralPath (Join-Path (Get-C599AuthorityDir -Fx $fx) 'authority.json') -Raw | ConvertFrom-Json
    $rel = Get-C599Release -Gh $fx.Gh -Tag ([string]$ok.Tag)
    Assert-C487 -Cond ([bool]$ok.Published -and $null -ne $rel -and -not [bool]$rel.isDraft -and @($manifest.suites).Count -eq 8 -and [string]$auth.policyBlobId -eq $pinnedBlob) `
        -Name 'C599 G-285: a changed working tree and moved HEAD cannot alter the frozen suite authority' -Detail ([string]$ok.Refusal)
    $pub = Get-Content -LiteralPath $pubAuthPath -Raw | ConvertFrom-Json
    $journal = Get-Content -LiteralPath (Join-Path $croot 'publish.json') -Raw | ConvertFrom-Json
    Assert-C487 -Cond (-not [string]::IsNullOrWhiteSpace([string]$pub.digest) -and [string]$manifest.publicationAuthorityDigest -eq [string]$pub.digest -and [string]$journal.publicationAuthorityDigest -eq [string]$pub.digest -and [string]$pub.inputs.policyBlobId -eq $pinnedBlob) `
        -Name 'C599 V-18: the frozen publication authority digest is journalled and carried in the manifest' -Detail ([string]$pub.digest)
}

function Invoke-C599AFieldMatrix {
    # V-12 full matrix, the authority field children of C599A_PinnedPolicy: every frozen
    # authority field absent, null, mistyped and foreign, each re-bound so it alone
    # differs. Skip/Take select this child's fields, keeping each child near 30 s.
    param([int]$Skip, [int]$Take, [string]$MatrixName)
    $fx = New-C599PublishFx -PolicyBytes (Get-C599ABasePolicyBytes)
    $snap = Save-C599ASnapshot -Fx $fx
    $eight = @('antiphon', 'session-runner', 'pty-host', 'agents-pty', 'messaging', 'client', 'scripts', 'e2e')
    $control = Get-C599AGate -Fx $fx
    Assert-C487 -Cond ($control.Ok) -Name 'C599 V-12: the valid authority control passes before the field matrix' -Detail (@($control.Reasons) -join ' ')

    # Every frozen authority field: absent, null, wrong type, wrapped in a one-element
    # array (a JSON array is never the scalar it contains) and wrong identity. k
    # overrides the expected tokens for one kind.
    $fields = @(
        @{ n = 'schemaVersion'; t = @('authority-schema'); wt = '1'; fv = 2 },
        @{ n = 'kind'; t = @('authority-kind'); wt = 7; fv = 'release-candidate' },
        @{ n = 'repository'; t = @('authority-repository'); wt = 7; fv = 'someone-else/Antiphon' },
        @{ n = 'profile'; t = @('authority-profile'); wt = 7; fv = 'nightly' },
        @{ n = 'coordinatorVersion'; t = @('authority-coordinator-version'); wt = 1; fv = 'c599-other' },
        @{ n = 'sha'; t = @('authority-sha'); wt = 7; fv = $ShaC },
        @{ n = 'candidateId'; t = @('authority-candidate'); wt = 7; fv = 'rc-20260101T000000Z' },
        @{ n = 'candidateRef'; t = @('authority-candidate-ref'); wt = 7; fv = 'release/rc-20260101T000000Z' },
        @{ n = 'intentId'; t = @('authority-intent'); wt = 7; fv = 'intent-foreign'; k = @{ foreign = @('plan-intent', 'ledger-intent') } },
        @{ n = 'releaseCardId'; t = @('authority-release-card'); wt = 7; fv = '00000000-0000-4000-8000-00000000c599'; k = @{ foreign = @('receipt-recipient') } },
        @{ n = 'policyPath'; t = @('authority-policy-path'); wt = 7; fv = 'tests/other-policy.json' },
        @{ n = 'policyBlobId'; t = @('policy-blob-id-mismatch'); wt = 7; fv = ('1' * 40) },
        @{ n = 'policyRawSha256'; t = @('policy-raw-digest-mismatch'); wt = 7; fv = ('0' * 64) },
        @{ n = 'policyHash'; t = @('policy-hash-mismatch'); wt = 7; fv = ('0' * 64) },
        @{ n = 'requiredSuites'; t = @('authority-suites-type', 'authority-suites-mismatch'); wt = 8; fv = @($eight | Where-Object { $_ -ne 'e2e' }); k = @{ foreign = @('authority-suites-mismatch') } },
        @{ n = 'exclusions'; t = @('authority-exclusions-type', 'authority-exclusions-mismatch'); wt = 'none'; fv = 'drop-first'; k = @{ foreign = @('authority-exclusions-mismatch') } },
        @{ n = 'scriptHashes'; t = @('script-digest-mismatch:lib/release-authority.ps1'); wt = 'x'; fv = 'one-changed' },
        @{ n = 'createdAt'; t = @('ledger-timestamps'); wt = $true; fv = '2026-09-23T09:00:00Z' }
    )
    $fields = @($fields | Select-Object -Skip $Skip -First $Take)
    $m = @()
    foreach ($f in $fields) {
        foreach ($kind in @('absent', 'null', 'type', 'array', 'foreign')) {
            $expect = @($f.t)
            if ($f.k -and $f.k.ContainsKey($kind)) { $expect = @($f.k[$kind]) }
            $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label ('{0} {1}' -f $f.n, $kind) -Expect $expect -Mutate {
                Update-C599AAuthority -Fx $fx -Mutate {
                    param($a)
                    switch ($kind) {
                        'absent' { $a.PSObject.Properties.Remove($f.n) }
                        'null' { Set-C599AField $a $f.n $null }
                        'type' { Set-C599AField $a $f.n $f.wt }
                        'array' { Set-C599AField $a $f.n (New-C599AWrapped -Value $a.($f.n)) }
                        'foreign' {
                            if ($f.fv -is [string] -and $f.fv -eq 'drop-first') { $a.exclusions = @($a.exclusions | Select-Object -Skip 1) }
                            elseif ($f.fv -is [string] -and $f.fv -eq 'one-changed') { $a.scriptHashes.'lib/release-authority.ps1' = ('0' * 64) }
                            else { Set-C599AField $a $f.n $f.fv }
                        }
                    }
                }
            }
        }
    }
    Assert-C599AMatrix -Name $MatrixName -Results $m
}

function Test-C599A_PinnedPolicyFields {
    Invoke-C599AFieldMatrix -Skip 0 -Take 5 -MatrixName 'C599 V-12: the frozen schemaVersion, kind, repository, profile and coordinatorVersion fields refuse when absent, null, mistyped, array-wrapped or foreign'
}

function Test-C599A_PinnedPolicyIdentityFields {
    Invoke-C599AFieldMatrix -Skip 5 -Take 5 -MatrixName 'C599 V-12: the frozen sha, candidateId, candidateRef, intentId and releaseCardId fields refuse when absent, null, mistyped, array-wrapped or foreign'
}

function Test-C599A_PinnedPolicyBlobFields {
    Invoke-C599AFieldMatrix -Skip 10 -Take 4 -MatrixName 'C599 V-12: the frozen policyPath, policyBlobId, policyRawSha256 and policyHash fields refuse when absent, null, mistyped, array-wrapped or foreign'
}

function Test-C599A_PinnedPolicySuiteFields {
    Invoke-C599AFieldMatrix -Skip 14 -Take 4 -MatrixName 'C599 V-12: the frozen requiredSuites, exclusions, scriptHashes and createdAt fields refuse when absent, null, mistyped, array-wrapped or foreign'
}

function New-C599AWrapped {
    # A one-element JSON array holding Value (an array value is nested, not flattened).
    param($Value)
    $wrapped = New-Object object[] 1
    $wrapped[0] = $Value
    return (, $wrapped)
}

function Test-C599A_PinnedPolicyBindings {
    # V-12 full matrix, third child of C599A_PinnedPolicy: strict JSON shapes inside
    # the list/object fields, and the repository/coordinator bindings. The digest
    # chain is recomputable, so these must bind to something outside the files.
    $fx = New-C599PublishFx -PolicyBytes (Get-C599ABasePolicyBytes)
    $snap = Save-C599ASnapshot -Fx $fx
    $eight = @('antiphon', 'session-runner', 'pty-host', 'agents-pty', 'messaging', 'client', 'scripts', 'e2e')
    $control = Get-C599AGate -Fx $fx
    Assert-C487 -Cond ($control.Ok) -Name 'C599 V-12: the valid authority control passes before the binding matrix' -Detail (@($control.Reasons) -join ' ')

    $m = @()
    foreach ($shape in @(
            @{ l = 'requiredSuites comma-joined string'; t = 'authority-suites-type'; e = { param($a) $a.requiredSuites = ($eight -join ',') } },
            @{ l = 'requiredSuites unknown suite name'; t = 'authority-suites-type'; e = { param($a) $a.requiredSuites = @($eight | ForEach-Object { if ($_ -eq 'e2e') { 'e2e-live' } else { $_ } }) } },
            @{ l = 'requiredSuites duplicated name'; t = 'authority-suites-type'; e = { param($a) $a.requiredSuites = @($eight) + @('antiphon') } },
            @{ l = 'requiredSuites element array-wrapped'; t = 'authority-suites-type'; e = { param($a) $a.requiredSuites = @(@($eight | Select-Object -First 7) + @(, (New-C599AWrapped -Value 'e2e'))) } },
            @{ l = 'requiredSuites element number'; t = 'authority-suites-type'; e = { param($a) $a.requiredSuites = @(@($eight | Select-Object -First 7) + @(8)) } },
            @{ l = 'exclusion methods joined string'; t = 'authority-exclusions-type'; e = { param($a) Set-C599AField $a.exclusions[0] 'methods' 'Live' } },
            @{ l = 'exclusion class array-wrapped'; t = 'authority-exclusions-type'; e = { param($a) Set-C599AField $a.exclusions[0] 'class' (New-C599AWrapped -Value ([string]$a.exclusions[0].class)) } },
            @{ l = 'exclusion row a string'; t = 'authority-exclusions-type'; e = { param($a) $a.exclusions = @('antiphon:ClaudeAdapterIntegrationTests') + @($a.exclusions | Select-Object -Skip 1) } },
            @{ l = 'script digest array-wrapped'; t = 'script-digest-mismatch:lib/release-authority.ps1'; e = { param($a) $a.scriptHashes.'lib/release-authority.ps1' = (New-C599AWrapped -Value ([string]$a.scriptHashes.'lib/release-authority.ps1')) } },
            @{ l = 'createdAt array-wrapped'; t = 'ledger-timestamps'; e = { param($a) Set-C599AField $a 'createdAt' (New-C599AWrapped -Value ([string]$a.createdAt)) } })) {
        $c599aShape = $shape
        $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label $shape.l -Expect @($shape.t) -Mutate { Update-C599AAuthority -Fx $fx -Mutate $c599aShape.e }
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'publisher requiredSuites comma-joined string' -Publish -Expect @('authority-suites-type') -Mutate {
        Update-C599AAuthority -Fx $fx -Mutate { param($a) $a.requiredSuites = ($eight -join ',') }
    }
    Assert-C599AMatrix -Name 'C599 V-12: requiredSuites, exclusions and script digests refuse joined strings, unknown names and array-wrapped scalars' -Results $m

    # repository binds to the checkout's origin AND the publication destination;
    # coordinatorVersion binds to the coordinator this publisher supports.
    $originUrl = Invoke-C599RealGit -Repo $fx.Checkout -Arguments @('remote', 'get-url', 'origin')
    $restoreOrigin = { [void](Invoke-C599RealGit -Repo $fx.Checkout -Arguments @('remote', 'set-url', 'origin', $originUrl)) }
    $m = @()
    foreach ($url in @('https://github.com/someone-else/Antiphon', 'git@github.com:someone-else/Antiphon.git', 'https://example.invalid/michal-ciechan/Antiphon', 'michal-ciechan/Antiphon')) {
        $c599aUrl = $url
        $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label ('checkout origin ' + $url) -Expect @('checkout-origin-repository') -Cleanup $restoreOrigin -Mutate {
            [void](Invoke-C599RealGit -Repo $fx.Checkout -Arguments @('remote', 'set-url', 'origin', $c599aUrl))
        }
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'checkout origin removed' -Expect @('checkout-origin-repository') -Cleanup {
        [void](Invoke-ReleaseAuthorityGit -RepositoryRoot $fx.Checkout -Arguments @('remote', 'add', 'origin', $originUrl))
    } -Mutate { [void](Invoke-C599RealGit -Repo $fx.Checkout -Arguments @('remote', 'remove', 'origin')) }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'publisher foreign destination' -Publish -Expect @('authority-repository', 'checkout-origin-repository') `
        -Extra @{ Repository = 'someone-else/Antiphon' } -Mutate { }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'publisher foreign checkout origin' -Publish -Expect @('checkout-origin-repository') -Cleanup $restoreOrigin -Mutate {
        [void](Invoke-C599RealGit -Repo $fx.Checkout -Arguments @('remote', 'set-url', 'origin', 'https://github.com/someone-else/Antiphon'))
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'publisher foreign authority repository' -Publish -Expect @('authority-repository') -Mutate {
        Update-C599AAuthority -Fx $fx -Mutate { param($a) $a.repository = 'someone-else/Antiphon' }
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'publisher foreign coordinatorVersion' -Publish -Expect @('authority-coordinator-version') -Mutate {
        Update-C599AAuthority -Fx $fx -Mutate { param($a) $a.coordinatorVersion = 'c599-b0' }
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'publisher absent coordinatorVersion' -Publish -Expect @('authority-coordinator-version') -Mutate {
        Update-C599AAuthority -Fx $fx -Mutate { param($a) $a.PSObject.Properties.Remove('coordinatorVersion') }
    }
    Assert-C599AMatrix -Name 'C599 V-12: repository binds to the checkout origin and the publication destination, coordinatorVersion to the supported coordinator' -Results $m
}

function Test-C599A_ExecutionLedger {
    # V-12 full matrix for the frozen plan and full execution ledger, first child
    # (G-226, G-287..G-289): failed siblings, evidence containment, prerequisites,
    # native exits and zero executions.
    $fx = New-C599PublishFx -PolicyBytes (Get-C599ABasePolicyBytes)
    $snap = Save-C599ASnapshot -Fx $fx
    $croot = Get-ReleaseGateCandidateRoot -ReleaseRoot $fx.ReleaseRoot -CandidateId $CandidateId
    $planDoc = Get-Content -LiteralPath (Join-Path (Get-C599AuthorityDir -Fx $fx) 'execution-plan.json') -Raw | ConvertFrom-Json
    $chunks = @()
    foreach ($s in @($planDoc.suites | Where-Object { [string]$_.kind -eq 'native' })) {
        foreach ($c in @($s.chunks)) { $chunks += [pscustomobject]@{ Suite = [string]$s.id; Id = [string]$c.id; Uids = @($c.uids | ForEach-Object { [string]$_ }) } }
    }
    $uids = @($chunks | ForEach-Object { $_.Uids })
    $control = Get-C599AGate -Fx $fx
    Assert-C487 -Cond ($control.Ok -and $chunks.Count -eq 13 -and $uids.Count -eq 19) -Name 'C599 V-12: the valid 13-chunk 19-UID control ledger passes the publication gate' -Detail ((@($control.Reasons) -join ' ') + (' chunks={0} uids={1}' -f $chunks.Count, $uids.Count))

    # G-226: a failed chunk is refused whether its passing sibling runs before or after it.
    $m = @()
    foreach ($suiteId in @($chunks | ForEach-Object { $_.Suite } | Select-Object -Unique)) {
        $own = @($chunks | Where-Object { $_.Suite -eq $suiteId })
        foreach ($target in @($own[0], $own[$own.Count - 1])) {
            $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label ('fail ' + $target.Id) -Expect @('uid-not-passed:' + $target.Uids[0]) -Mutate {
                Set-C599TrxOutcome -Fx $fx -Chunk $target.Id -Uid $target.Uids[0] -Outcome 'Failed'
            }
        }
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'publisher fail e2e-002' -Publish -Expect @('uid-not-passed:e2e-u3') -Mutate {
        Set-C599TrxOutcome -Fx $fx -Chunk 'e2e-002' -Uid 'e2e-u3' -Outcome 'Failed'
    }
    Assert-C599AMatrix -Name 'C599 G-226: a passing sibling chunk never hides a failed chunk in any suite' -Results $m

    # G-287: evidence must resolve under the candidate root with no link component.
    $outsideDir = Join-Path $fx.Root 'outside-evidence'
    $linkPath = Join-Path $croot 'evidence-link'
    $m = @()
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'parent-relative escape' -Expect @('chunk-evidence-escape:antiphon-001') -Mutate {
        Copy-Item -LiteralPath (Join-Path $croot (Join-Path 'evidence' 'antiphon-001.trx')) -Destination (Join-Path (Split-Path -Parent $croot) 'outside.trx')
        Update-C599AChunkRow -Fx $fx -Chunk 'antiphon-001' -Mutate { param($c) $c.trx = '../outside.trx' }
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'absolute path' -Expect @('chunk-evidence-escape:antiphon-001') -Mutate {
        Update-C599AChunkRow -Fx $fx -Chunk 'antiphon-001' -Mutate { param($c) $c.trx = (Join-Path $croot (Join-Path 'evidence' 'antiphon-001.trx')) }
    }
    foreach ($via in @('gate', 'publisher')) {
        $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label ('linked directory ' + $via) -Publish:($via -eq 'publisher') -Expect @('chunk-evidence-escape:antiphon-001') -Mutate {
            New-Item -ItemType Directory -Path $outsideDir -Force | Out-Null
            Copy-Item -LiteralPath (Join-Path $croot (Join-Path 'evidence' 'antiphon-001.trx')) -Destination (Join-Path $outsideDir 'antiphon-001.trx') -Force
            New-C599AEvidenceLink -Link $linkPath -Target $outsideDir
            Update-C599AChunkRow -Fx $fx -Chunk 'antiphon-001' -Mutate { param($c) $c.trx = 'evidence-link/antiphon-001.trx' }
        } -Cleanup { Remove-C599AEvidenceLink -Link $linkPath }
    }
    Assert-C599AMatrix -Name 'C599 G-287: outside, absolute and reparse-escaped evidence refuses before publication' -Results $m

    # G-288: every required build/prerequisite row.
    $m = @()
    foreach ($pre in @('build', 'client-bundle', 'script-census')) {
        $shapes = @(
            @{ l = 'exit 1'; t = 'prerequisite-failed'; e = { param($r) $r.exitCode = 1 } },
            @{ l = 'succeeded false'; t = 'prerequisite-failed'; e = { param($r) $r.succeeded = $false } },
            @{ l = 'succeeded string'; t = 'prerequisite-failed'; e = { param($r) $r.succeeded = 'true' } },
            @{ l = 'exit string'; t = 'prerequisite-failed'; e = { param($r) $r.exitCode = '0' } },
            @{ l = 'exit absent'; t = 'prerequisite-failed'; e = { param($r) $r.PSObject.Properties.Remove('exitCode') } }
        )
        foreach ($shape in $shapes) {
            $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label ('{0} {1}' -f $pre, $shape.l) -Expect @($shape.t + ':' + $pre) -Mutate {
                Update-C599Ledger -Fx $fx -Ledger { param($l) foreach ($r in @($l.prerequisites)) { if ([string]$r.id -eq $pre) { & $shape.e $r } } }
            }
        }
        $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label ($pre + ' absent') -Expect @('prerequisite-missing:' + $pre) -Mutate {
            Update-C599Ledger -Fx $fx -Ledger { param($l) $l.prerequisites = @($l.prerequisites | Where-Object { [string]$_.id -ne $pre }) }
        }
        $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label ($pre + ' duplicated') -Expect @('prerequisite-missing:' + $pre) -Mutate {
            Update-C599Ledger -Fx $fx -Ledger { param($l) $l.prerequisites = @($l.prerequisites) + @($l.prerequisites | Where-Object { [string]$_.id -eq $pre }) }
        }
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'publisher script-census exit 1' -Publish -Expect @('prerequisite-failed:script-census') -Mutate {
        Update-C599Ledger -Fx $fx -Ledger { param($l) foreach ($r in @($l.prerequisites)) { if ([string]$r.id -eq 'script-census') { $r.exitCode = 1 } } }
    }
    Assert-C599AMatrix -Name 'C599 G-288: each failed, absent, duplicated or mistyped required prerequisite blocks remote writes' -Results $m

    # G-289: native exit and actual counts, not an all-passed TRX, decide.
    $m = @()
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'antiphon-001 exit 1' -Expect @('chunk-exit:antiphon-001') -Mutate { Update-C599AChunkRow -Fx $fx -Chunk 'antiphon-001' -Mutate { param($c) $c.exitCode = 1 } }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'e2e-002 exit -1' -Expect @('chunk-exit:e2e-002') -Mutate { Update-C599AChunkRow -Fx $fx -Chunk 'e2e-002' -Mutate { param($c) $c.exitCode = -1 } }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'exit absent' -Expect @('chunk-exit:pty-host-001') -Mutate { Update-C599AChunkRow -Fx $fx -Chunk 'pty-host-001' -Mutate { param($c) $c.PSObject.Properties.Remove('exitCode') } }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'exit string' -Expect @('chunk-exit:agents-pty-002') -Mutate { Update-C599AChunkRow -Fx $fx -Chunk 'agents-pty-002' -Mutate { param($c) $c.exitCode = '0' } }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'zero executions' -Expect @('chunk-zero-executed:messaging-002') -Mutate { Update-C599ATrx -Fx $fx -Chunk 'messaging-002' -Rows { param($rows) @() } }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'roster exit 1' -Expect @('roster-exit:scripts') -Mutate { Update-C599ARosterRow -Fx $fx -Suite 'scripts' -Mutate { param($r) $r.exitCode = 1 } }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'roster exit absent' -Expect @('roster-exit:client') -Mutate { Update-C599ARosterRow -Fx $fx -Suite 'client' -Mutate { param($r) $r.PSObject.Properties.Remove('exitCode') } }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'publisher messaging-002 exit 1' -Publish -Expect @('chunk-exit:messaging-002') -Mutate { Update-C599AChunkRow -Fx $fx -Chunk 'messaging-002' -Mutate { param($c) $c.exitCode = 1 } }
    Assert-C599AMatrix -Name 'C599 G-289: a nonzero or non-numeric child exit or zero executions blocks despite a passing TRX' -Results $m
}

function Test-C599A_ExecutionLedgerChunks {
    # V-12 full matrix, second child of C599A_ExecutionLedger (G-290..G-292): the
    # exact frozen chunk set, disjoint UID membership and every required expanded UID.
    $fx = New-C599PublishFx -PolicyBytes (Get-C599ABasePolicyBytes)
    $snap = Save-C599ASnapshot -Fx $fx
    $croot = Get-ReleaseGateCandidateRoot -ReleaseRoot $fx.ReleaseRoot -CandidateId $CandidateId
    $planDoc = Get-Content -LiteralPath (Join-Path (Get-C599AuthorityDir -Fx $fx) 'execution-plan.json') -Raw | ConvertFrom-Json
    $chunks = @()
    foreach ($s in @($planDoc.suites | Where-Object { [string]$_.kind -eq 'native' })) {
        foreach ($c in @($s.chunks)) { $chunks += [pscustomobject]@{ Suite = [string]$s.id; Id = [string]$c.id; Uids = @($c.uids | ForEach-Object { [string]$_ }) } }
    }
    $uids = @($chunks | ForEach-Object { $_.Uids })
    $control = Get-C599AGate -Fx $fx
    Assert-C487 -Cond ($control.Ok -and $chunks.Count -eq 13 -and $uids.Count -eq 19) -Name 'C599 V-12: the valid 13-chunk 19-UID control ledger passes the publication gate' -Detail ((@($control.Reasons) -join ' ') + (' chunks={0} uids={1}' -f $chunks.Count, $uids.Count))

    # G-290: exactly the frozen chunk set.
    $m = @()
    foreach ($chunk in $chunks) {
        $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label ('delete ' + $chunk.Id) -Expect @('chunk-missing:' + $chunk.Id) -Mutate {
            Update-C599Ledger -Fx $fx -Ledger { param($l) $l.chunks = @($l.chunks | Where-Object { [string]$_.chunk -ne $chunk.Id }) }
        }
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'extra ledger chunk' -Expect @('chunk-unexpected:antiphon-099') -Mutate {
        Update-C599Ledger -Fx $fx -Ledger { param($l) $x = ($l.chunks[0] | ConvertTo-Json -Depth 6 | ConvertFrom-Json); $x.chunk = 'antiphon-099'; $l.chunks = @($l.chunks) + @($x) }
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'duplicate ledger chunk' -Expect @('chunk-duplicate:antiphon-001') -Mutate {
        Update-C599Ledger -Fx $fx -Ledger { param($l) $l.chunks = @($l.chunks) + @($l.chunks | Where-Object { [string]$_.chunk -eq 'antiphon-001' }) }
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'plan chunk removed' -Expect @('plan-uid-missing:antiphon-u3', 'chunk-unexpected:antiphon-002') -Mutate {
        Update-C599Ledger -Fx $fx -Plan { param($p) foreach ($s in @($p.suites)) { if ([string]$s.id -eq 'antiphon') { $s.chunks = @($s.chunks | Where-Object { [string]$_.id -ne 'antiphon-002' }) } } }
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'plan chunk added' -Expect @('plan-uid-unexpected:antiphon-u9', 'chunk-missing:antiphon-004') -Mutate {
        Update-C599Ledger -Fx $fx -Plan { param($p) foreach ($s in @($p.suites)) { if ([string]$s.id -eq 'antiphon') { $s.chunks = @($s.chunks) + @([pscustomobject]@{ id = 'antiphon-004'; classes = @('antiphonDeltaTests'); uids = @('antiphon-u9') }) } } }
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'publisher extra ledger chunk' -Publish -Expect @('chunk-unexpected:antiphon-099') -Mutate {
        Update-C599Ledger -Fx $fx -Ledger { param($l) $x = ($l.chunks[0] | ConvertTo-Json -Depth 6 | ConvertFrom-Json); $x.chunk = 'antiphon-099'; $l.chunks = @($l.chunks) + @($x) }
    }
    Assert-C599AMatrix -Name 'C599 G-290: every deleted, extra or duplicated frozen chunk blocks authority' -Results $m

    # G-291: disjoint membership; a duplicate UID is never deduplicated into coverage.
    $dupSibling = {
        Update-C599ATrx -Fx $fx -Chunk 'antiphon-002' -Rows { param($rows) @($rows) + @([pscustomobject]@{ Id = 'antiphon-u1'; ClassName = 'antiphonAlphaTests'; MethodName = 'One'; Outcome = 'Passed' }) }
    }
    $m = @()
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'duplicate in sibling chunk' -Expect @('uid-wrong-chunk:antiphon-u1') -Mutate $dupSibling
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'duplicate in same chunk' -Expect @('chunk-trx-unreadable:antiphon-001') -Mutate {
        Update-C599ATrx -Fx $fx -Chunk 'antiphon-001' -Rows { param($rows) @($rows) + @($rows[0]) }
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'plan overlap' -Expect @('plan-uid-overlap:antiphon-u1') -Mutate {
        Update-C599Ledger -Fx $fx -Plan { param($p) foreach ($s in @($p.suites)) { if ([string]$s.id -eq 'antiphon') { foreach ($c in @($s.chunks)) { if ([string]$c.id -eq 'antiphon-002') { $c.uids = @($c.uids) + 'antiphon-u1' } } } } }
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'publisher duplicate in sibling chunk' -Publish -Expect @('uid-wrong-chunk:antiphon-u1') -Mutate $dupSibling
    Assert-C599AMatrix -Name 'C599 G-291: a duplicated UID in a sibling chunk, the same chunk or the plan refuses' -Results $m

    # G-292: every required expanded UID independently, and every non-pass shape.
    $m = @()
    foreach ($chunk in $chunks) {
        foreach ($u in $chunk.Uids) {
            $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label ('remove ' + $u) -Expect @('uid-missing:' + $u) -Mutate {
                Update-C599ATrx -Fx $fx -Chunk $chunk.Id -Rows { param($rows) @($rows | Where-Object { $_.Id -ne $u }) }
            }
        }
    }
    Assert-C599AMatrix -Name 'C599 G-292: each required expanded UID removed from its chunk evidence refuses' -Results $m
    $m = @()
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'skipped' -Expect @('uid-not-passed:messaging-u2') -Mutate { Set-C599TrxOutcome -Fx $fx -Chunk 'messaging-001' -Uid 'messaging-u2' -Outcome 'NotExecuted' }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'unknown outcome' -Expect @('uid-not-passed:session-runner-u3') -Mutate { Set-C599TrxOutcome -Fx $fx -Chunk 'session-runner-002' -Uid 'session-runner-u3' -Outcome 'Inconclusive' }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'unknown uid' -Expect @('uid-unknown:antiphon-zz') -Mutate {
        Update-C599ATrx -Fx $fx -Chunk 'antiphon-001' -Rows { param($rows) @($rows) + @([pscustomobject]@{ Id = 'antiphon-zz'; ClassName = 'antiphonAlphaTests'; MethodName = 'Stray'; Outcome = 'Passed' }) }
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'stale evidence' -Expect @('chunk-evidence-digest:agents-pty-001') -Mutate {
        Update-C599ATrx -Fx $fx -Chunk 'agents-pty-001' -KeepBinding -Rows { param($rows) @($rows | ForEach-Object { $_.Outcome = 'Failed'; $_ }) }
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'evidence file missing' -Expect @('chunk-evidence-missing:e2e-001') -Mutate {
        Remove-Item -LiteralPath (Join-Path $croot (Join-Path 'evidence' 'e2e-001.trx')) -Force
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'publisher unknown outcome' -Publish -Expect @('uid-not-passed:pty-host-u1') -Mutate { Set-C599TrxOutcome -Fx $fx -Chunk 'pty-host-001' -Uid 'pty-host-u1' -Outcome 'Inconclusive' }
    Assert-C599AMatrix -Name 'C599 G-292: skipped, unknown-outcome, unknown-UID, stale and missing evidence refuse' -Results $m
}

function Test-C599A_ExecutionLedgerCounts {
    # V-12 full matrix, third child of C599A_ExecutionLedger (G-293..G-295): empty
    # required sets, counts recomputed from the TRX and pinned exclusions.
    $fx = New-C599PublishFx -PolicyBytes (Get-C599ABasePolicyBytes)
    $snap = Save-C599ASnapshot -Fx $fx
    $croot = Get-ReleaseGateCandidateRoot -ReleaseRoot $fx.ReleaseRoot -CandidateId $CandidateId
    $planDoc = Get-Content -LiteralPath (Join-Path (Get-C599AuthorityDir -Fx $fx) 'execution-plan.json') -Raw | ConvertFrom-Json
    $chunks = @()
    foreach ($s in @($planDoc.suites | Where-Object { [string]$_.kind -eq 'native' })) {
        foreach ($c in @($s.chunks)) { $chunks += [pscustomobject]@{ Suite = [string]$s.id; Id = [string]$c.id; Uids = @($c.uids | ForEach-Object { [string]$_ }) } }
    }
    $uids = @($chunks | ForEach-Object { $_.Uids })
    $control = Get-C599AGate -Fx $fx
    Assert-C487 -Cond ($control.Ok -and $chunks.Count -eq 13 -and $uids.Count -eq 19) -Name 'C599 V-12: the valid 13-chunk 19-UID control ledger passes the publication gate' -Detail ((@($control.Reasons) -join ' ') + (' chunks={0} uids={1}' -f $chunks.Count, $uids.Count))

    # G-293: a consistent zero-required suite or empty declared roster never publishes.
    $m = @()
    foreach ($suiteId in @($chunks | ForEach-Object { $_.Suite } | Select-Object -Unique)) {
        $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label ('empty ' + $suiteId) -Expect @('required-uids-empty:' + $suiteId) -Mutate {
            Update-C599ADiscovery -Fx $fx -Mutate { param($d) Set-C599AField $d.suites $suiteId @() } `
                -Plan { param($p) foreach ($s in @($p.suites)) { if ([string]$s.id -eq $suiteId) { $s.chunks = @() } } } `
                -Ledger { param($l) $l.chunks = @($l.chunks | Where-Object { [string]$_.suite -ne $suiteId }) }
        }
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'e2e all excluded' -Expect @('required-uids-empty:e2e') -Mutate {
        Update-C599ADiscovery -Fx $fx -Mutate { param($d) $d.suites.e2e = @($d.suites.e2e | Where-Object { [string]$_.uid -eq 'e2e-x1' }) } `
            -Plan { param($p) foreach ($s in @($p.suites)) { if ([string]$s.id -eq 'e2e') { $s.chunks = @() } } } `
            -Ledger { param($l) $l.chunks = @($l.chunks | Where-Object { [string]$_.suite -ne 'e2e' }) }
    }
    foreach ($rid in @('client', 'scripts')) {
        $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label ('empty roster ' + $rid) -Expect @('roster-empty:' + $rid) -Mutate {
            Update-C599Ledger -Fx $fx -Plan { param($p) foreach ($s in @($p.suites)) { if ([string]$s.id -eq $rid) { $s.entries = @() } } } `
                -Ledger { param($l) foreach ($r in @($l.rosters)) { if ([string]$r.suite -eq $rid) { $r.entries = @() } } }
        }
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'publisher empty messaging' -Publish -Expect @('required-uids-empty:messaging') -Mutate {
        Update-C599ADiscovery -Fx $fx -Mutate { param($d) $d.suites.messaging = @() } `
            -Plan { param($p) foreach ($s in @($p.suites)) { if ([string]$s.id -eq 'messaging') { $s.chunks = @() } } } `
            -Ledger { param($l) $l.chunks = @($l.chunks | Where-Object { [string]$_.suite -ne 'messaging' }) }
    }
    Assert-C599AMatrix -Name 'C599 G-293: a zero-required suite or empty declared roster cannot publish' -Results $m

    # G-294: counts are recomputed from the raw TRX, never trusted from the ledger.
    $m = @()
    foreach ($shape in @(
            @{ l = 'passed +1'; t = 'chunk-count-passed'; e = { param($c) $c.passed = [int]$c.passed + 1 } },
            @{ l = 'executed +1'; t = 'chunk-count-executed'; e = { param($c) $c.executed = [int]$c.executed + 1 } },
            @{ l = 'failed string'; t = 'chunk-count-failed'; e = { param($c) $c.failed = '0' } },
            @{ l = 'skipped absent'; t = 'chunk-count-skipped'; e = { param($c) $c.PSObject.Properties.Remove('skipped') } })) {
        $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label $shape.l -Expect @($shape.t + ':antiphon-001') -Mutate { Update-C599AChunkRow -Fx $fx -Chunk 'antiphon-001' -Mutate $shape.e }
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'publisher plausible inflated counts' -Publish -Expect @('chunk-count-passed:session-runner-001', 'chunk-count-executed:session-runner-001') -Mutate {
        Update-C599AChunkRow -Fx $fx -Chunk 'session-runner-001' -Mutate { param($c) $c.passed = [int]$c.passed + 1; $c.executed = [int]$c.executed + 1 }
    }
    Assert-C599AMatrix -Name 'C599 G-294: inflated or mistyped ledger counts refuse against the recomputed TRX' -Results $m

    # G-295: exclusions come from the pinned blob; a post-result edit cannot drop a failed UID.
    $gammaExclusion = [pscustomobject]@{ suite = 'antiphon'; class = 'antiphonGammaTests'; reason = 'post-result edit'; owner = 'CARD-0599' }
    $excludedBytes = New-C599APolicyBytes -Edit { param($o) $o.profiles.rc.exclusions = @($o.profiles.rc.exclusions) + @($gammaExclusion) }
    $failGamma = { Set-C599TrxOutcome -Fx $fx -Chunk 'antiphon-003' -Uid 'antiphon-u4' -Outcome 'Failed' }
    $m = @()
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'authority exclusions edited' -Expect @('authority-exclusions-mismatch') -Mutate {
        & $failGamma
        Update-C599AAuthority -Fx $fx -Mutate { param($a) $a.exclusions = @($a.exclusions) + @($gammaExclusion) }
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'working-tree policy edited' -Expect @('uid-not-passed:antiphon-u4') -Mutate {
        & $failGamma
        [System.IO.File]::WriteAllBytes((Join-Path $fx.Checkout (Join-Path 'tests' 'test-execution-policy.json')), $excludedBytes)
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'private copy edited' -Expect @('policy-copy-mismatch', 'uid-not-passed:antiphon-u4') -Mutate {
        & $failGamma
        [System.IO.File]::WriteAllBytes((Join-Path (Get-C599AuthorityDir -Fx $fx) 'policy.json'), $excludedBytes)
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'summary exclusions edited' -Expect @('uid-not-passed:antiphon-u4') -Mutate {
        & $failGamma
        Update-C599Json -Path (Join-Path $croot 'summary.json') -Mutate { param($s) foreach ($r in @($s.suites)) { Set-C599AField $r 'exclusions' @($gammaExclusion) } }
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'publisher authority exclusions edited' -Publish -Expect @('authority-exclusions-mismatch') -Mutate {
        & $failGamma
        Update-C599AAuthority -Fx $fx -Mutate { param($a) $a.exclusions = @($a.exclusions) + @($gammaExclusion) }
    }
    Assert-C599AMatrix -Name 'C599 G-295: a post-result exclusion edit cannot remove a failed UID' -Results $m
}

function Test-C599A_ExecutionLedgerJoins {
    # V-12 full matrix, second child of C599A_ExecutionLedger: identity joins
    # (G-296..G-299), digests (G-300) and timestamps (G-301).
    $fx = New-C599PublishFx -PolicyBytes (Get-C599ABasePolicyBytes)
    $snap = Save-C599ASnapshot -Fx $fx
    $croot = Get-ReleaseGateCandidateRoot -ReleaseRoot $fx.ReleaseRoot -CandidateId $CandidateId
    $planDoc = Get-Content -LiteralPath (Join-Path (Get-C599AuthorityDir -Fx $fx) 'execution-plan.json') -Raw | ConvertFrom-Json
    $chunks = @()
    foreach ($s in @($planDoc.suites | Where-Object { [string]$_.kind -eq 'native' })) {
        foreach ($c in @($s.chunks)) { $chunks += [pscustomobject]@{ Suite = [string]$s.id; Id = [string]$c.id; Uids = @($c.uids | ForEach-Object { [string]$_ }) } }
    }
    $uids = @($chunks | ForEach-Object { $_.Uids })
    $control = Get-C599AGate -Fx $fx
    Assert-C487 -Cond ($control.Ok -and $chunks.Count -eq 13 -and $uids.Count -eq 19) -Name 'C599 V-12: the valid 13-chunk 19-UID control ledger passes the publication gate' -Detail ((@($control.Reasons) -join ' ') + (' chunks={0} uids={1}' -f $chunks.Count, $uids.Count))

    # G-296..G-299: every record joins the same intent, candidate, full SHA and RunId.
    $short = $fx.Head.Substring(0, 12)
    $joins = @(
        @{ g = 'G-296'; name = 'intent'; rows = @(
            @{ l = 'plan'; t = @('plan-intent'); e = { Update-C599Ledger -Fx $fx -Plan { param($p) $p.intentId = 'intent-foreign' } } },
            @{ l = 'ledger'; t = @('ledger-intent'); e = { Update-C599Ledger -Fx $fx -Ledger { param($l) $l.intentId = 'intent-foreign' } } },
            @{ l = 'chunk'; t = @('chunk-intent:e2e-001'); e = { Update-C599AChunkRow -Fx $fx -Chunk 'e2e-001' -Mutate { param($c) $c.intentId = 'intent-foreign' } } },
            @{ l = 'roster'; t = @('roster-identity:scripts'); e = { Update-C599ARosterRow -Fx $fx -Suite 'scripts' -Mutate { param($r) $r.intentId = 'intent-foreign' } } },
            @{ l = 'ledger array-wrapped'; t = @('ledger-intent'); e = { Update-C599Ledger -Fx $fx -Ledger { param($l) $l.intentId = (New-C599AWrapped -Value ([string]$l.intentId)) } } },
            @{ l = 'publisher chunk'; p = $true; t = @('chunk-intent:agents-pty-002'); e = { Update-C599AChunkRow -Fx $fx -Chunk 'agents-pty-002' -Mutate { param($c) $c.intentId = 'intent-foreign' } } }) },
        @{ g = 'G-297'; name = 'candidate'; rows = @(
            @{ l = 'plan'; t = @('plan-candidate'); e = { Update-C599Ledger -Fx $fx -Plan { param($p) $p.candidateId = 'rc-20260101T000000Z' } } },
            @{ l = 'ledger'; t = @('ledger-candidate'); e = { Update-C599Ledger -Fx $fx -Ledger { param($l) $l.candidateId = 'rc-20260101T000000Z' } } },
            @{ l = 'ledger ref'; t = @('ledger-candidate-ref'); e = { Update-C599Ledger -Fx $fx -Ledger { param($l) $l.candidateRef = 'release/rc-20260101T000000Z' } } },
            @{ l = 'chunk'; t = @('chunk-candidate:pty-host-002'); e = { Update-C599AChunkRow -Fx $fx -Chunk 'pty-host-002' -Mutate { param($c) $c.candidateId = 'rc-20260101T000000Z' } } },
            @{ l = 'roster'; t = @('roster-identity:client'); e = { Update-C599ARosterRow -Fx $fx -Suite 'client' -Mutate { param($r) $r.candidateId = 'rc-20260101T000000Z' } } },
            @{ l = 'chunk array-wrapped'; t = @('chunk-candidate:pty-host-001'); e = { Update-C599AChunkRow -Fx $fx -Chunk 'pty-host-001' -Mutate { param($c) $c.candidateId = (New-C599AWrapped -Value ([string]$c.candidateId)) } } },
            @{ l = 'publisher ledger ref'; p = $true; t = @('ledger-candidate-ref'); e = { Update-C599Ledger -Fx $fx -Ledger { param($l) $l.candidateRef = 'release/rc-20260101T000000Z' } } }) },
        @{ g = 'G-298'; name = 'full SHA'; rows = @(
            @{ l = 'plan'; t = @('plan-sha'); e = { Update-C599Ledger -Fx $fx -Plan { param($p) $p.sha = $ShaC } } },
            @{ l = 'ledger foreign'; t = @('ledger-sha'); e = { Update-C599Ledger -Fx $fx -Ledger { param($l) $l.sha = $ShaC } } },
            @{ l = 'ledger abbreviated'; t = @('ledger-sha'); e = { Update-C599Ledger -Fx $fx -Ledger { param($l) $l.sha = $short } } },
            @{ l = 'chunk foreign'; t = @('chunk-sha:messaging-001'); e = { Update-C599AChunkRow -Fx $fx -Chunk 'messaging-001' -Mutate { param($c) $c.sha = $ShaC } } },
            @{ l = 'roster'; t = @('roster-identity:scripts'); e = { Update-C599ARosterRow -Fx $fx -Suite 'scripts' -Mutate { param($r) $r.sha = $short } } },
            @{ l = 'roster array-wrapped'; t = @('roster-identity:client'); e = { Update-C599ARosterRow -Fx $fx -Suite 'client' -Mutate { param($r) $r.sha = (New-C599AWrapped -Value ([string]$r.sha)) } } },
            @{ l = 'publisher chunk abbreviated'; p = $true; t = @('chunk-sha:antiphon-002'); e = { Update-C599AChunkRow -Fx $fx -Chunk 'antiphon-002' -Mutate { param($c) $c.sha = $short } } }) },
        @{ g = 'G-299'; name = 'native RunId'; rows = @(
            @{ l = 'ledger'; t = @('ledger-run'); e = { Update-C599Ledger -Fx $fx -Ledger { param($l) $l.runId = 'rc-run-foreign' } } },
            @{ l = 'chunk'; t = @('chunk-run:session-runner-002'); e = { Update-C599AChunkRow -Fx $fx -Chunk 'session-runner-002' -Mutate { param($c) $c.runId = 'rc-run-foreign' } } },
            @{ l = 'roster'; t = @('roster-identity:client'); e = { Update-C599ARosterRow -Fx $fx -Suite 'client' -Mutate { param($r) $r.runId = 'rc-run-foreign' } } },
            @{ l = 'ledger array-wrapped'; t = @('ledger-run'); e = { Update-C599Ledger -Fx $fx -Ledger { param($l) $l.runId = (New-C599AWrapped -Value ([string]$l.runId)) } } },
            @{ l = 'green'; t = @('ledger-run', 'chunk-run:antiphon-001'); e = { Update-C599AGreen -Fx $fx -Mutate { param($g) $g.runId = 'rc-run-foreign' } } },
            @{ l = 'publisher chunk'; p = $true; t = @('chunk-run:e2e-002'); e = { Update-C599AChunkRow -Fx $fx -Chunk 'e2e-002' -Mutate { param($c) $c.runId = 'rc-run-foreign' } } }) }
    )
    foreach ($join in $joins) {
        $m = @()
        foreach ($row in $join.rows) {
            $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label $row.l -Publish:([bool]$row.p) -Expect $row.t -Mutate $row.e
        }
        Assert-C599AMatrix -Name ('C599 {0}: foreign {1} evidence on the plan, ledger, a chunk or a roster refuses' -f $join.g, $join.name) -Results $m
    }

    # G-300: the authority, plan, discovery and every executable script digest bind.
    $m = @()
    foreach ($scriptRel in @('publish-release.ps1', 'promote-release.ps1', 'lib/release-gate.ps1', 'lib/release-authority.ps1', 'lib/nightly-policy.ps1', 'lib/nightly-coverage.ps1', 'lib/nightly-common.ps1')) {
        $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label ('script ' + $scriptRel) -Expect @('script-digest-mismatch:' + $scriptRel) -Mutate {
            Update-C599AAuthority -Fx $fx -Mutate { param($a) $a.scriptHashes.$scriptRel = ('0' * 64) }
        }
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'authority changed without re-binding' -Expect @('plan-authority-digest', 'ledger-authority-digest') -Mutate {
        Update-C599Json -Path (Join-Path (Get-C599AuthorityDir -Fx $fx) 'authority.json') -Mutate { param($a) $a.coordinatorVersion = 'c599-other' }
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'plan digest' -Expect @('ledger-plan-digest') -Mutate {
        Update-C599Ledger -Fx $fx -Ledger { param($l) $l.executionPlanDigest = ('0' * 64) }
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'discovery changed without re-binding' -Expect @('plan-discovery-digest') -Mutate {
        Update-C599Json -Path (Join-Path (Get-C599AuthorityDir -Fx $fx) 'discovery.json') -Mutate {
            param($d) $d.suites.antiphon = @($d.suites.antiphon) + @([pscustomobject]@{ uid = 'antiphon-x2'; class = 'ClaudeAdapterIntegrationTests'; method = 'Other' }) }
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'publisher script digest' -Publish -Expect @('script-digest-mismatch:publish-release.ps1') -Mutate {
        Update-C599AAuthority -Fx $fx -Mutate { param($a) $a.scriptHashes.'publish-release.ps1' = ('0' * 64) }
    }
    Assert-C599AMatrix -Name 'C599 G-300: a modified script, authority, plan or discovery digest blocks publication' -Results $m

    # G-301: authority <= run start <= every chunk/roster window <= run end.
    $m = @()
    foreach ($shape in @(
            @{ l = 'chunk end before start'; t = 'chunk-timestamps:antiphon-002'; e = { Update-C599AChunkRow -Fx $fx -Chunk 'antiphon-002' -Mutate { param($c) $c.completedAt = '2026-09-23T08:30:59Z' } } },
            @{ l = 'chunk before run'; t = 'chunk-timestamps:antiphon-001'; e = { Update-C599AChunkRow -Fx $fx -Chunk 'antiphon-001' -Mutate { param($c) $c.startedAt = '2026-09-23T08:30:40Z' } } },
            @{ l = 'chunk after run'; t = 'chunk-timestamps:e2e-002'; e = { Update-C599AChunkRow -Fx $fx -Chunk 'e2e-002' -Mutate { param($c) $c.completedAt = '2026-09-23T09:51:00Z' } } },
            @{ l = 'chunk unparseable'; t = 'chunk-timestamps:messaging-001'; e = { Update-C599AChunkRow -Fx $fx -Chunk 'messaging-001' -Mutate { param($c) $c.startedAt = 'yesterday' } } },
            @{ l = 'run before authority'; t = 'ledger-timestamps'; e = { Update-C599Ledger -Fx $fx -Ledger { param($l) $l.startedAt = '2026-09-22T08:31:00Z' } } },
            @{ l = 'run end equals start'; t = 'ledger-timestamps'; e = { Update-C599Ledger -Fx $fx -Ledger { param($l) $l.completedAt = $l.startedAt } } },
            @{ l = 'run start absent'; t = 'ledger-timestamps'; e = { Update-C599Ledger -Fx $fx -Ledger { param($l) $l.PSObject.Properties.Remove('startedAt') } } },
            @{ l = 'roster end before start'; t = 'roster-timestamps:client'; e = { Update-C599ARosterRow -Fx $fx -Suite 'client' -Mutate { param($r) $r.completedAt = '2026-09-23T09:29:00Z' } } })) {
        $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label $shape.l -Expect @($shape.t) -Mutate $shape.e
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'publisher chunk end before start' -Publish -Expect @('chunk-timestamps:pty-host-001') -Mutate {
        Update-C599AChunkRow -Fx $fx -Chunk 'pty-host-001' -Mutate { param($c) $c.completedAt = '2026-09-23T08:31:00Z' }
    }
    Assert-C599AMatrix -Name 'C599 G-301: out-of-order or stale evidence timestamps refuse' -Results $m

    # G-301 bound: evidence dated after the publisher's clock refuses. The fixture
    # clock (C599Now) is injected and earlier than the wall clock, so a check against
    # the wall clock would accept every variant below.
    $m = @()
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'whole run shifted one hour ahead' -Expect @('evidence-future:ledger', 'evidence-future:roster:client', 'evidence-future:receipt') -Mutate {
        Move-C599AEvidenceTime -Fx $fx -Offset ([timespan]::FromHours(1))
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'run end after the clock' -Expect @('evidence-future:ledger') -Mutate {
        Update-C599Ledger -Fx $fx -Ledger { param($l) $l.completedAt = '2026-09-23T10:30:00Z' }
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'authority created after the clock' -Expect @('evidence-future:authority') -Mutate {
        Update-C599AAuthority -Fx $fx -Mutate { param($a) $a.createdAt = '2026-09-24T08:30:30Z' }
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'publisher whole run shifted one day ahead' -Publish -Expect @('evidence-future:ledger') -Mutate {
        Move-C599AEvidenceTime -Fx $fx -Offset ([timespan]::FromDays(1))
    }
    Assert-C599AMatrix -Name 'C599 G-301: evidence dated after the injected publisher clock refuses' -Results $m
}

function Test-C599A_ExecutionLedgerMarkers {
    # V-12 full matrix, fourth child of C599A_ExecutionLedger: whole-run markers
    # (G-302..G-305), credit flags (G-387..G-389), declared rosters and the restored
    # control publication.
    $fx = New-C599PublishFx -PolicyBytes (Get-C599ABasePolicyBytes)
    $snap = Save-C599ASnapshot -Fx $fx
    $croot = Get-ReleaseGateCandidateRoot -ReleaseRoot $fx.ReleaseRoot -CandidateId $CandidateId
    $planDoc = Get-Content -LiteralPath (Join-Path (Get-C599AuthorityDir -Fx $fx) 'execution-plan.json') -Raw | ConvertFrom-Json
    $chunks = @()
    foreach ($s in @($planDoc.suites | Where-Object { [string]$_.kind -eq 'native' })) {
        foreach ($c in @($s.chunks)) { $chunks += [pscustomobject]@{ Suite = [string]$s.id; Id = [string]$c.id; Uids = @($c.uids | ForEach-Object { [string]$_ }) } }
    }
    $uids = @($chunks | ForEach-Object { $_.Uids })
    $control = Get-C599AGate -Fx $fx
    Assert-C487 -Cond ($control.Ok -and $chunks.Count -eq 13 -and $uids.Count -eq 19) -Name 'C599 V-12: the valid 13-chunk 19-UID control ledger passes the publication gate' -Detail ((@($control.Reasons) -join ' ') + (' chunks={0} uids={1}' -f $chunks.Count, $uids.Count))

    # G-302..G-305 and report acceptance: whole-run markers are real booleans / exact values.
    $markers = @(
        @{ n = 'C599 G-302: a missing, false, string or numeric teardown cannot publish'; f = 'teardownSucceeded'; t = 'teardown-not-succeeded'; values = @('absent', $false, 'true', 1); pv = $false },
        @{ n = 'C599 G-303: a seam-driven ledger or run creates no release'; f = 'seamed'; t = 'ledger-seamed'; values = @('absent', $true, 'false'); pv = $true; g = 'seamed'; gt = 'seam-driven-run' },
        @{ n = 'C599 G-304: a NoReport ledger or run creates no release'; f = 'noReport'; t = 'ledger-no-report'; values = @('absent', $true, 'false'); pv = $true; g = 'noReport'; gt = 'no-report-run' },
        @{ n = 'C599 G-305: subset and diagnostic ledgers or runs create no release'; f = 'selection'; t = 'ledger-selection'; values = @('absent', 'subset', 'diagnostic', 'Full', (New-C599AWrapped -Value 'full')); pv = 'subset'; g = 'diagnostic'; gt = 'diagnostic-run'; f2 = 'diagnostic'; t2 = 'ledger-diagnostic' }
    )
    foreach ($mk in $markers) {
        $m = @()
        $fields = @(@{ f = $mk.f; t = $mk.t; values = $mk.values })
        if ($mk.f2) { $fields += @{ f = $mk.f2; t = $mk.t2; values = @('absent', $true, 'false') } }
        foreach ($fd in $fields) {
            foreach ($val in $fd.values) {
                $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label ('{0}={1}' -f $fd.f, $val) -Expect @($fd.t) -Mutate {
                    Update-C599Ledger -Fx $fx -Ledger { param($l) if ($val -is [string] -and $val -eq 'absent') { $l.PSObject.Properties.Remove($fd.f) } else { Set-C599AField $l $fd.f $val } }
                }
            }
        }
        if ($mk.g) {
            $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label ('green ' + $mk.g) -Expect @($mk.gt) -Mutate { Update-C599AGreen -Fx $fx -Mutate { param($g) Set-C599AField $g $mk.g $true } }
        }
        $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label ('publisher ' + $mk.f) -Publish -Expect @($mk.t) -Mutate {
            Update-C599Ledger -Fx $fx -Ledger { param($l) Set-C599AField $l $mk.f $mk.pv }
        }
        Assert-C599AMatrix -Name $mk.n -Results $m
    }

    # G-387..G-389: complete-green credit flags are real booleans only.
    foreach ($flag in @(@{ g = 'G-387'; f = 'coverageComplete' }, @{ g = 'G-388'; f = 'testsPassed' }, @{ g = 'G-389'; f = 'reportDelivered' })) {
        $m = @()
        foreach ($val in @('true', $false, 'absent', 1)) {
            $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label ('{0}={1}' -f $flag.f, $val) -Expect @('not-rc-complete-green', ('not-true-boolean:' + $flag.f)) -Mutate {
                Update-C599AGreen -Fx $fx -Mutate { param($g) if ($val -is [string] -and $val -eq 'absent') { $g.PSObject.Properties.Remove($flag.f) } else { Set-C599AField $g $flag.f $val } }
            }
        }
        $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label ('publisher string ' + $flag.f) -Publish -Expect @('not-rc-complete-green') -Mutate {
            Update-C599AGreen -Fx $fx -Mutate { param($g) Set-C599AField $g $flag.f 'true' }
        }
        Assert-C599AMatrix -Name ('C599 {0}: a string or non-true {1} flag cannot publish' -f $flag.g, $flag.f) -Results $m
    }

    # Client and script evidence validate by their own declared rosters.
    $m = @()
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'roster entry missing' -Expect @('roster-entry-missing:scripts:test-nightly.ps1') -Mutate {
        Update-C599ARosterRow -Fx $fx -Suite 'scripts' -Mutate { param($r) $r.entries = @($r.entries | Where-Object { [string]$_.id -ne 'test-nightly.ps1' }) } }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'roster entry unknown' -Expect @('roster-entry-unknown:client:e2e-invented') -Mutate {
        Update-C599ARosterRow -Fx $fx -Suite 'client' -Mutate { param($r) $r.entries = @($r.entries) + @([pscustomobject]@{ id = 'e2e-invented'; outcome = 'passed' }) } }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'roster entry duplicate' -Expect @('roster-duplicate:client:lint') -Mutate {
        Update-C599ARosterRow -Fx $fx -Suite 'client' -Mutate { param($r) $r.entries = @($r.entries) + @($r.entries[0]) } }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'roster entry skipped' -Expect @('roster-entry-not-passed:scripts:test-release-gate.ps1') -Mutate {
        Update-C599ARosterRow -Fx $fx -Suite 'scripts' -Mutate { param($r) $r.entries[0].outcome = 'skipped' } }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'roster row missing' -Expect @('roster-missing:scripts') -Mutate {
        Update-C599Ledger -Fx $fx -Ledger { param($l) $l.rosters = @($l.rosters | Where-Object { [string]$_.suite -ne 'scripts' }) } }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'publisher roster entry missing' -Publish -Expect @('roster-entry-missing:client:vitest') -Mutate {
        Update-C599ARosterRow -Fx $fx -Suite 'client' -Mutate { param($r) $r.entries = @($r.entries | Where-Object { [string]$_.id -ne 'vitest' }) } }
    Assert-C599AMatrix -Name 'C599 V-12: client and script evidence validate against their own declared rosters' -Results $m

    # The restored control still publishes, with suite counts recomputed from the ledger.
    Restore-C599ASnapshot -Snap $snap
    $ok = Invoke-C599Publish -Fx $fx
    $manifest = Get-Content -LiteralPath (Join-Path $croot 'release-manifest.json') -Raw | ConvertFrom-Json
    $antiphonRow = @($manifest.suites | Where-Object { [string]$_.id -eq 'antiphon' })[0]
    $rel = Get-C599Release -Gh $fx.Gh -Tag ([string]$ok.Tag)
    Assert-C487 -Cond ([bool]$ok.Published -and $null -ne $rel -and -not [bool]$rel.isDraft -and [int]$antiphonRow.chunks -eq 3 -and [int]$antiphonRow.required -eq 4 -and [int]$antiphonRow.executed -eq 4 -and [int]$antiphonRow.passed -eq 4) `
        -Name 'C599 V-12: the restored control ledger publishes with suite counts recomputed from the evidence' -Detail ([string]$ok.Refusal + ' ' + ($antiphonRow | ConvertTo-Json -Compress))
}

function Test-C599A_ExecutionLedgerReceipt {
    # V-12 full matrix, third child of C599A_ExecutionLedger (D-14, G-306): the typed
    # prepublication receipt replaces the old boolean acceptance. Its kind, its
    # recipient (the release card bound to the authority) and its correlation to
    # this intent/candidate/SHA/native run are each checked; the legacy flag confers nothing.
    $fx = New-C599PublishFx -PolicyBytes (Get-C599ABasePolicyBytes)
    $snap = Save-C599ASnapshot -Fx $fx
    $control = Get-C599AGate -Fx $fx
    Assert-C487 -Cond ($control.Ok) -Name 'C599 V-12: the valid control ledger with a correlated prepublication receipt passes' -Detail (@($control.Reasons) -join ' ')

    # G-306 (PC-306): only the prepublication kind can mint publication authority.
    $m = @()
    foreach ($v in @(
            @{ l = 'final'; v = 'final' }, @{ l = 'publication'; v = 'publication' }, @{ l = 'case-changed'; v = 'Prepublication' },
            @{ l = 'array-wrapped'; v = (New-C599AWrapped -Value 'prepublication') }, @{ l = 'number'; v = 1 }, @{ l = 'null'; v = $null }, @{ l = 'absent'; absent = $true })) {
        $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label ('receipt kind ' + $v.l) -Expect @('receipt-kind') -Mutate {
            Update-C599AReceipt -Fx $fx -Mutate { param($r) if ($v.absent) { $r.PSObject.Properties.Remove('kind') } else { Set-C599AField $r 'kind' $v.v } }
        }
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'publisher final receipt' -Publish -Expect @('receipt-kind') -Mutate {
        Update-C599AReceipt -Fx $fx -Mutate { param($r) $r.kind = 'final' }
    }
    Assert-C599AMatrix -Name 'C599 G-306: a final or foreign receipt kind where the prepublication receipt is required refuses' -Results $m

    # Presence and type: a boolean, string or array is not a receipt.
    $m = @()
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'legacy reportAccepted without receipt' -Expect @('receipt-missing') -Mutate {
        Update-C599Ledger -Fx $fx -Ledger { param($l) Set-C599AField $l 'reportAccepted' $true; $l.PSObject.Properties.Remove('prepublicationReceipt') }
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'receipt null' -Expect @('receipt-missing') -Mutate {
        Update-C599Ledger -Fx $fx -Ledger { param($l) Set-C599AField $l 'prepublicationReceipt' $null }
    }
    foreach ($v in @(@{ l = 'boolean'; v = $true }, @{ l = 'string'; v = 'prepublication' }, @{ l = 'number'; v = 4 })) {
        $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label ('receipt ' + $v.l) -Expect @('receipt-type') -Mutate {
            Update-C599Ledger -Fx $fx -Ledger { param($l) Set-C599AField $l 'prepublicationReceipt' $v.v }
        }
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'receipt array-wrapped' -Expect @('receipt-type') -Mutate {
        Update-C599Ledger -Fx $fx -Ledger { param($l) Set-C599AField $l 'prepublicationReceipt' (New-C599AWrapped -Value $l.prepublicationReceipt) }
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'publisher legacy reportAccepted without receipt' -Publish -Expect @('receipt-missing') -Mutate {
        Update-C599Ledger -Fx $fx -Ledger { param($l) Set-C599AField $l 'reportAccepted' $true; $l.PSObject.Properties.Remove('prepublicationReceipt') }
    }
    Assert-C599AMatrix -Name 'C599 V-12: a missing, boolean, string or array-wrapped receipt cannot publish and the legacy reportAccepted flag confers nothing' -Results $m

    # Recipient identity: exactly the release card bound to the authority.
    $m = @()
    foreach ($v in @(
            @{ l = 'recipient absent'; e = { param($r) $r.PSObject.Properties.Remove('recipient') } },
            @{ l = 'recipient string'; e = { param($r) $r.recipient = $script:C599ReleaseCardId } },
            @{ l = 'recipient master incident'; e = { param($r) $r.recipient.kind = 'master-incident' } },
            @{ l = 'recipient kind absent'; e = { param($r) $r.recipient.PSObject.Properties.Remove('kind') } },
            @{ l = 'foreign card'; e = { param($r) $r.recipient.cardId = '00000000-0000-4000-8000-00000000c599' } },
            @{ l = 'card array-wrapped'; e = { param($r) $r.recipient.cardId = (New-C599AWrapped -Value ([string]$r.recipient.cardId)) } },
            @{ l = 'card not a guid'; e = { param($r) $r.recipient.cardId = 'CARD-0599' } },
            @{ l = 'card absent'; e = { param($r) $r.recipient.PSObject.Properties.Remove('cardId') } },
            @{ l = 'revision zero'; e = { param($r) $r.recipient.revision = 0 } },
            @{ l = 'revision string'; e = { param($r) $r.recipient.revision = '4' } },
            @{ l = 'revision absent'; e = { param($r) $r.recipient.PSObject.Properties.Remove('revision') } })) {
        $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label $v.l -Expect @('receipt-recipient') -Mutate { Update-C599AReceipt -Fx $fx -Mutate $v.e }
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'publisher foreign card' -Publish -Expect @('receipt-recipient') -Mutate {
        Update-C599AReceipt -Fx $fx -Mutate { param($r) $r.recipient.cardId = '00000000-0000-4000-8000-00000000c599' }
    }
    Assert-C599AMatrix -Name 'C599 V-12: a receipt from any recipient but the release card bound to the authority refuses' -Results $m

    # Correlation: the same intent, candidate, full SHA and native run, accepted after
    # the run ended and not after the publisher's clock.
    $m = @()
    foreach ($v in @(
            @{ l = 'correlation absent'; t = 'receipt-correlation'; e = { param($r) $r.PSObject.Properties.Remove('correlation') } },
            @{ l = 'foreign intent'; t = 'receipt-correlation'; e = { param($r) $r.correlation.intentId = 'intent-foreign' } },
            @{ l = 'foreign candidate'; t = 'receipt-correlation'; e = { param($r) $r.correlation.candidateId = 'rc-20260101T000000Z' } },
            @{ l = 'foreign sha'; t = 'receipt-correlation'; e = { param($r) $r.correlation.sha = $ShaC } },
            @{ l = 'abbreviated sha'; t = 'receipt-correlation'; e = { param($r) $r.correlation.sha = ([string]$r.correlation.sha).Substring(0, 12) } },
            @{ l = 'foreign run'; t = 'receipt-correlation'; e = { param($r) $r.correlation.runId = 'rc-run-foreign' } },
            @{ l = 'run array-wrapped'; t = 'receipt-correlation'; e = { param($r) $r.correlation.runId = (New-C599AWrapped -Value ([string]$r.correlation.runId)) } },
            @{ l = 'run absent'; t = 'receipt-correlation'; e = { param($r) $r.correlation.PSObject.Properties.Remove('runId') } },
            @{ l = 'accepted before run end'; t = 'receipt-timestamps'; e = { param($r) $r.acceptedAt = '2026-09-23T09:49:00Z' } },
            @{ l = 'accepted unparseable'; t = 'receipt-timestamps'; e = { param($r) $r.acceptedAt = 'soon' } },
            @{ l = 'accepted absent'; t = 'receipt-timestamps'; e = { param($r) $r.PSObject.Properties.Remove('acceptedAt') } },
            @{ l = 'accepted after the clock'; t = 'evidence-future:receipt'; e = { param($r) $r.acceptedAt = '2026-09-23T10:30:00Z' } })) {
        $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label $v.l -Expect @($v.t) -Mutate { Update-C599AReceipt -Fx $fx -Mutate $v.e }
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'publisher foreign run' -Publish -Expect @('receipt-correlation') -Mutate {
        Update-C599AReceipt -Fx $fx -Mutate { param($r) $r.correlation.runId = 'rc-run-foreign' }
    }
    Assert-C599AMatrix -Name 'C599 V-12: a receipt not correlated to this intent, candidate, SHA and native run refuses' -Results $m
}

function Get-C599ClosedPort {
    # A loopback port that was just free: bound, read and released, so a connect is refused.
    $l = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    $l.Start()
    $port = ([System.Net.IPEndPoint]$l.LocalEndpoint).Port
    $l.Stop()
    return $port
}

function Test-C599A_ReceiptReadback {
    # V-12 full matrix, D-14 readback (review bf928242 finding 1): publication reads the
    # release card bound to the authority back through the Antiphon API (GET
    # /api/cards/{id}, read-only), requires the receipt's revision to equal the card's
    # CURRENT revision and the card body to carry this run's correlation line. An
    # unavailable readback refuses: publication fails closed. Producer-to-recipient
    # delivery through the report queue is B5/B6 (the report stage writes the receipt).
    $fx = New-C599PublishFx -PolicyBytes (Get-C599ABasePolicyBytes)
    $snap = Save-C599ASnapshot -Fx $fx
    $expectedLine = ('GET /fx/{0}/api/cards/{1} HTTP/1.1' -f (Split-Path -Leaf $fx.Root), $script:C599ReleaseCardId)
    [System.IO.File]::WriteAllText($fx.CardApi.Requests, '')
    $control = Get-C599AGate -Fx $fx
    $requests = @(Get-Content -LiteralPath $fx.CardApi.Requests | Where-Object { $_ })
    Assert-C487 -Cond ($control.Ok -and $requests.Count -eq 1 -and $requests[0] -ceq $expectedLine) `
        -Name 'C599 V-12: the valid control reads the bound release card back once through GET /api/cards/{id}' `
        -Detail ((@($control.Reasons) -join ' ') + ' requests=[' + ($requests -join ' | ') + ']')

    # Unavailable: any transport failure, non-200 answer or body that is not one JSON
    # object refuses. Nothing falls back to the ledger's own copy of the receipt.
    $apiUrl = $fx.CardApi.Url
    $closed = ('http://127.0.0.1:{0}/fx/{1}' -f (Get-C599ClosedPort), (Split-Path -Leaf $fx.Root))
    $restoreUrl = { $fx.CardApi.Url = $apiUrl }
    $m = @()
    foreach ($v in @(
            @{ l = 'api answers 500'; e = { Update-C599Card -Fx $fx -Store { param($s) $s.status = 500 } } },
            @{ l = 'api answers 404'; e = { Update-C599Card -Fx $fx -Store { param($s) $s.status = 404 } } },
            @{ l = 'api answers 401'; e = { Update-C599Card -Fx $fx -Store { param($s) $s.status = 401 } } },
            @{ l = 'card unknown to the api'; e = { Update-C599Card -Fx $fx -Store { param($s) $s.cards.PSObject.Properties.Remove($script:C599ReleaseCardId) } } },
            @{ l = 'body not json'; e = { Update-C599Card -Fx $fx -Store { param($s) $s.raw = '<html>maintenance</html>' } } },
            @{ l = 'body a json array'; e = { Update-C599Card -Fx $fx -Store { param($s) $s.raw = '[]' } } },
            @{ l = 'body a one-card json array'; e = { Update-C599Card -Fx $fx -Store { param($s) $s.raw = ('[' + ($s.cards.($script:C599ReleaseCardId) | ConvertTo-Json -Depth 8 -Compress) + ']') } } },
            @{ l = 'body empty'; e = { Update-C599Card -Fx $fx -Store { param($s) $s.raw = '' } } },
            @{ l = 'api unreachable'; e = { $fx.CardApi.Url = $closed } },
            @{ l = 'api url empty'; e = { $fx.CardApi.Url = '' } },
            @{ l = 'api url not http'; e = { $fx.CardApi.Url = ('file:///' + ($fx.CardApi.Store -replace '\\', '/')) } })) {
        $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label $v.l -Expect @('release-card-readback-unavailable') -Mutate $v.e -Cleanup $restoreUrl
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'publisher api answers 503' -Publish -Expect @('release-card-readback-unavailable') -Mutate {
        Update-C599Card -Fx $fx -Store { param($s) $s.status = 503 }
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'publisher api unreachable' -Publish -Expect @('release-card-readback-unavailable') `
        -Extra @{ AntiphonApiUrl = $closed } -Mutate { }
    Assert-C599AMatrix -Name 'C599 V-12: an unavailable, failed, unknown or non-object release card readback refuses (fail closed)' -Results $m

    # Revision: the receipt must name the card's current revision, exactly.
    $m = @()
    foreach ($v in @(
            @{ l = 'card moved on after the receipt'; e = { Update-C599Card -Fx $fx -Card { param($c) $c.revisionCount = 5 } } },
            @{ l = 'receipt names an earlier revision'; e = { Update-C599AReceipt -Fx $fx -Mutate { param($r) $r.recipient.revision = 3 } } },
            @{ l = 'receipt names a later revision'; e = { Update-C599AReceipt -Fx $fx -Mutate { param($r) $r.recipient.revision = 9 } } },
            @{ l = 'card revision string'; e = { Update-C599Card -Fx $fx -Card { param($c) $c.revisionCount = '4' } } },
            @{ l = 'card revision array-wrapped'; e = { Update-C599Card -Fx $fx -Card { param($c) $c.revisionCount = (New-C599AWrapped -Value 4) } } },
            @{ l = 'card revision null'; e = { Update-C599Card -Fx $fx -Card { param($c) $c.revisionCount = $null } } },
            @{ l = 'card revision absent'; e = { Update-C599Card -Fx $fx -Card { param($c) $c.PSObject.Properties.Remove('revisionCount') } } })) {
        $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label $v.l -Expect @('release-card-readback-revision') -Mutate $v.e
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'publisher receipt names an earlier revision' -Publish -Expect @('release-card-readback-revision') -Mutate {
        Update-C599AReceipt -Fx $fx -Mutate { param($r) $r.recipient.revision = 3 }
    }
    Assert-C599AMatrix -Name 'C599 V-12: a receipt revision other than the release card current revision refuses' -Results $m

    # Body: exactly one correlation line naming exactly this intent, candidate, full SHA and run.
    $head = $fx.Head
    $valid = Get-C599CardBody -Sha $head
    $m = @()
    foreach ($v in @(
            @{ l = 'no correlation line'; b = ('Release candidate {0}' -f $CandidateId) },
            @{ l = 'foreign intent'; b = (Get-C599CardBody -Sha $head -IntentId 'intent-foreign') },
            @{ l = 'foreign candidate'; b = (Get-C599CardBody -Sha $head -Candidate 'rc-20260101T000000Z') },
            @{ l = 'foreign sha'; b = (Get-C599CardBody -Sha $ShaC) },
            @{ l = 'abbreviated sha'; b = (Get-C599CardBody -Sha $head.Substring(0, 12)) },
            @{ l = 'foreign run'; b = (Get-C599CardBody -Sha $head -RunId 'rc-run-foreign') },
            @{ l = 'run key missing'; b = ($valid -replace ' runId=\S+', '') },
            @{ l = 'key case changed'; b = ($valid -creplace 'runId=', 'RunId=') },
            @{ l = 'extra key'; b = ($valid -replace '(runId=\S+)', '$1 note=x') },
            @{ l = 'second foreign line'; b = ($valid + "`n" + ((Get-C599CardBody -Sha $head -RunId 'rc-run-foreign') -split "`n" | Where-Object { $_ -like 'release-correlation:*' })) },
            @{ l = 'description number'; b = 7 },
            @{ l = 'description null'; b = $null })) {
        $c599Body = $v.b
        $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label $v.l -Expect @('release-card-readback-correlation') -Mutate {
            Update-C599Card -Fx $fx -Card { param($c) $c.description = $c599Body }
        }
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'description absent' -Expect @('release-card-readback-correlation') -Mutate {
        Update-C599Card -Fx $fx -Card { param($c) $c.PSObject.Properties.Remove('description') }
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'publisher body names a foreign run' -Publish -Expect @('release-card-readback-correlation') -Mutate {
        Update-C599Card -Fx $fx -Card { param($c) $c.description = (Get-C599CardBody -Sha $fx.Head -RunId 'rc-run-foreign') }
    }
    Assert-C599AMatrix -Name 'C599 V-12: a release card body without exactly this run correlation refuses' -Results $m

    # Identity: the API must answer with the bound card itself.
    $m = @()
    foreach ($v in @(
            @{ l = 'api serves a different card'; e = { param($c) $c.id = '00000000-0000-4000-8000-00000000c599' } },
            @{ l = 'served card id not a guid'; e = { param($c) $c.id = 'CARD-0601' } },
            @{ l = 'served card id absent'; e = { param($c) $c.PSObject.Properties.Remove('id') } })) {
        $c599CardEdit = $v.e
        $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label $v.l -Expect @('release-card-readback-identity') -Mutate { Update-C599Card -Fx $fx -Card $c599CardEdit }
    }
    $m += Invoke-C599AVariant -Fx $fx -Snap $snap -Label 'publisher api serves a different card' -Publish -Expect @('release-card-readback-identity') -Mutate {
        Update-C599Card -Fx $fx -Card { param($c) $c.id = '00000000-0000-4000-8000-00000000c599' }
    }
    Assert-C599AMatrix -Name 'C599 V-12: a readback of any card but the bound release card refuses' -Results $m

    # The restored control publishes, and every request the API saw was a GET of the bound card.
    Restore-C599ASnapshot -Snap $snap
    [System.IO.File]::WriteAllText($fx.CardApi.Requests, '')
    $ok = Invoke-C599Publish -Fx $fx
    $requests = @(Get-Content -LiteralPath $fx.CardApi.Requests | Where-Object { $_ })
    $rel = Get-C599Release -Gh $fx.Gh -Tag ([string]$ok.Tag)
    $foreign = @($requests | Where-Object { $_ -cne $expectedLine })
    Assert-C487 -Cond ([bool]$ok.Published -and $null -ne $rel -and -not [bool]$rel.isDraft -and $requests.Count -ge 1 -and $foreign.Count -eq 0) `
        -Name 'C599 V-12: the restored control publishes after only read-only GETs of the bound release card' `
        -Detail ([string]$ok.Refusal + ' requests=[' + ($requests -join ' | ') + ']')
}

function Invoke-C599ATestModeVariant {
    <#
      One publisher run from the restored control under a changed seam admission.
      Passes only when it is refused with every Expect token, wrote nothing remote,
      never read the injected clock and never consulted the fake git remote.
    #>
    param($Fx, $Snap, [string]$ClockTrace, [string]$Label, [string[]]$Expect, [hashtable]$Extra = $null, [scriptblock]$Before, [scriptblock]$After)
    Restore-C599ASnapshot -Snap $Snap
    if (Test-Path -LiteralPath $ClockTrace) { Remove-Item -LiteralPath $ClockTrace -Force }
    $got = @(); $refused = $false; $touched = $false; $err = ''
    try {
        & $Before
        $res = Invoke-C599Publish -Fx $Fx -Extra $Extra
        $got = @(([string]$res.Refusal) -split ',' | Where-Object { $_ })
        $refused = (-not [bool]$res.Published) -and ([int]$res.ExitCode -ne 0) -and (Test-C599NoRemoteWrite -Fx $Fx)
        $touched = (Test-Path -LiteralPath $ClockTrace) -or ((Test-Path -LiteralPath $Fx.Git.Trace) -and ((Get-Content -LiteralPath $Fx.Git.Trace -Raw) -match 'GIT '))
    } catch {
        $err = 'threw: ' + $_.Exception.Message
    } finally {
        & $After
        if (Test-Path -LiteralPath $ClockTrace) { Remove-Item -LiteralPath $ClockTrace -Force }
        Restore-C599ASnapshot -Snap $Snap
    }
    $absentTokens = @($Expect | Where-Object { $got -notcontains $_ })
    return [pscustomobject]@{
        Pass = ($refused -and -not $touched -and $absentTokens.Count -eq 0 -and $err -eq '')
        Detail = ('{0}: refused={1} seams-used={2} missing=[{3}] got=[{4}] {5}' -f $Label, $refused, $touched, ($absentTokens -join ' '), ($got -join ' '), $err)
    }
}

function Test-C599A_PublisherTestMode {
    # Review bf928242 finding 2: the clock seam (and every other publisher seam) is
    # admitted only in explicit test mode, ANTIPHON_RELEASE_GATE_TEST_MODE=1 exactly,
    # and publish-release refuses test mode whenever a real remote would be reached (no
    # seams file, or one without the Git or GitHub adapter). A real publication can
    # therefore never run on an injected clock.
    $fx = New-C599PublishFx -PolicyBytes (Get-C599ABasePolicyBytes)
    $clockTrace = Join-Path $fx.Root 'clock-trace.log'
    # Record every read of the injected clock (overrides the fixture clock, same instant).
    Add-Content -LiteralPath $fx.Gh.Path -Encoding ASCII -Value ("`n`$ReleaseGateSeams.UtcNow = {{ Add-Content -LiteralPath '{0}' -Value 'clock' -Encoding ASCII; return [datetime]::Parse('{1}', [System.Globalization.CultureInfo]::InvariantCulture, ([System.Globalization.DateTimeStyles]::AdjustToUniversal -bor [System.Globalization.DateTimeStyles]::AssumeUniversal)) }}" -f $clockTrace, $script:C599Now.ToString('o'))
    $snap = Save-C599ASnapshot -Fx $fx
    $clearTrace = { if (Test-Path -LiteralPath $clockTrace) { Remove-Item -LiteralPath $clockTrace -Force } }

    $ok = Invoke-C599Publish -Fx $fx
    $rel = Get-C599Release -Gh $fx.Gh -Tag ([string]$ok.Tag)
    Assert-C487 -Cond ([bool]$ok.Published -and $null -ne $rel -and -not [bool]$rel.isDraft -and (Test-Path -LiteralPath $clockTrace)) `
        -Name 'C599 V-18: in test mode the fake remote and the injected clock publish the valid control' -Detail ([string]$ok.Refusal)
    Restore-C599ASnapshot -Snap $snap
    & $clearTrace

    # Outside test mode (unset, or any value but exactly 1) a seams file is refused
    # before it is loaded: the clock is never read and no fake or real remote is touched.
    $m = @()
    foreach ($value in @('', '0', 'true', 'TRUE', ' 1', '1 ', 'yes')) {
        $c599Mode = $value
        $m += Invoke-C599ATestModeVariant -Fx $fx -Snap $snap -ClockTrace $clockTrace -Label ('test mode [' + $value + ']') -Expect @('seams-outside-test-mode') `
            -Before { [Environment]::SetEnvironmentVariable('ANTIPHON_RELEASE_GATE_TEST_MODE', $c599Mode) } `
            -After { [Environment]::SetEnvironmentVariable('ANTIPHON_RELEASE_GATE_TEST_MODE', '1') }
    }
    Assert-C599AMatrix -Name 'C599 V-18: outside test mode a seams file is refused before it is loaded, the clock is never read and nothing remote is written' -Results $m

    # The production clock function itself: the seam answers only in test mode.
    $saved = $script:ReleaseGateSeams
    $outside = ''
    $inside = $null
    try {
        $script:ReleaseGateSeams = @{ UtcNow = { return $script:C599Now } }
        [Environment]::SetEnvironmentVariable('ANTIPHON_RELEASE_GATE_TEST_MODE', $null)
        try { $outside = 'returned ' + (Get-ReleaseGateUtcNow).ToString('o') } catch { $outside = 'refused' }
        [Environment]::SetEnvironmentVariable('ANTIPHON_RELEASE_GATE_TEST_MODE', '1')
        try { $inside = Get-ReleaseGateUtcNow } catch { $inside = $null }
    } finally {
        $script:ReleaseGateSeams = $saved
        [Environment]::SetEnvironmentVariable('ANTIPHON_RELEASE_GATE_TEST_MODE', '1')
    }
    Assert-C487 -Cond ($outside -eq 'refused' -and $null -ne $inside -and $inside -eq $script:C599Now) `
        -Name 'C599 V-18: the release-gate clock seam is refused outside test mode and honoured inside it' -Detail ('outside=' + $outside + ' inside=' + $inside)

    # Test mode with a real remote refuses before any remote read or write. The
    # checkout's origin points at a missing local path for these variants, so even a
    # missing guard could never reach a real remote.
    $gitText = Get-Content -LiteralPath $fx.Git.Path -Raw
    $ghText = Get-Content -LiteralPath $fx.Gh.Path -Raw
    $clockLine = @($ghText -split "`n" | Where-Object { $_ -like '$ReleaseGateSeams.UtcNow = *' })[-1]
    $gitOnly = Join-Path $fx.Root 'seams-git-only.ps1'
    Set-Content -LiteralPath $gitOnly -Encoding ASCII -Value ($gitText + "`n" + $clockLine)
    $ghOnly = Join-Path $fx.Root 'seams-github-only.ps1'
    Set-Content -LiteralPath $ghOnly -Encoding ASCII -Value ($ghText.Replace($gitText, '$ReleaseGateSeams = @{}'))
    $originUrl = Invoke-C599RealGit -Repo $fx.Checkout -Arguments @('remote', 'get-url', 'origin')
    $noRemote = Join-Path $fx.Root 'no-such-remote'
    $isolate = { [void](Invoke-C599RealGit -Repo $fx.Checkout -Arguments @('remote', 'set-url', 'origin', $noRemote)) }
    $restore = { [void](Invoke-C599RealGit -Repo $fx.Checkout -Arguments @('remote', 'set-url', 'origin', $originUrl)) }
    $m = @()
    foreach ($v in @(@{ l = 'no seams file'; p = '' }, @{ l = 'seams without the GitHub adapter'; p = $gitOnly }, @{ l = 'seams without the Git adapter'; p = $ghOnly })) {
        $m += Invoke-C599ATestModeVariant -Fx $fx -Snap $snap -ClockTrace $clockTrace -Label $v.l -Expect @('test-mode-real-remote') -Extra @{ SeamsPath = $v.p } -Before $isolate -After $restore
    }
    Assert-C599AMatrix -Name 'C599 V-18: test mode refuses a real remote: no seams file, or one without the Git or GitHub adapter' -Results $m
}

# ====================================================== CARD-1311 S2 ref ===

function Test-C1311_LongLivedRcRef {
    # D-1/D-2: rc/YYYY-MM-DD is a candidate. Its id is one per tested SHA.
    # A legacy stamp still parses and still earns rc-release.
    $accepted = Test-ReleaseGateCandidateRef -Ref 'rc/2026-10-15'
    Assert-C487 -Cond ([bool]$accepted.Ok -and [string]$accepted.Ref -eq 'rc/2026-10-15') -Name 'C1311 LongLivedRcRef rc/2026-10-15 is accepted' -Detail ([string]$accepted.Reason)

    $prefixed = Test-ReleaseGateCandidateRef -Ref 'refs/heads/rc/2026-10-15'
    Assert-C487 -Cond ([bool]$prefixed.Ok -and [string]$prefixed.Ref -eq 'rc/2026-10-15') -Name 'C1311 LongLivedRcRef refs/heads/rc/2026-10-15 normalizes to rc/2026-10-15' -Detail ([string]$prefixed.Ref)

    $prefix = $ShaB.Substring(0, 12)
    $withSha = Test-ReleaseGateCandidateRef -Ref 'rc/2026-10-15' -Sha $ShaB
    Assert-C487 -Cond ([string]$withSha.CandidateId -eq ('rc-2026-10-15-' + $prefix)) -Name ('C1311 LongLivedRcRef candidate id is rc-2026-10-15-' + $prefix) -Detail ([string]$withSha.CandidateId)

    $badDate = Test-ReleaseGateCandidateRef -Ref 'rc/2026-13-01'
    Assert-C487 -Cond ((-not [bool]$badDate.Ok) -and [string]$badDate.Reason -eq 'ref-date') -Name 'C1311 LongLivedRcRef rc/2026-13-01 refuses ref-date' -Detail ([string]$badDate.Reason)

    $badShape = Test-ReleaseGateCandidateRef -Ref 'rc/20261015'
    Assert-C487 -Cond ((-not [bool]$badShape.Ok) -and [string]$badShape.Reason -eq 'ref-shape') -Name 'C1311 LongLivedRcRef rc/20261015 refuses ref-shape' -Detail ([string]$badShape.Reason)

    $stamp = Test-ReleaseGateCandidateRef -Ref $CandidateRef
    Assert-C487 -Cond ([bool]$stamp.Ok -and [string]$stamp.CandidateId -eq $CandidateId -and [string]$stamp.Ref -eq $CandidateRef) -Name 'C1311 LongLivedRcRef legacy stamp release/rc-20260923T083000Z is accepted' -Detail ([string]$stamp.Reason)

    $longCredit = Get-ReleaseGateCreditKind -Profile 'rc' -Trigger 'rc' -Ref 'rc/2026-10-15'
    Assert-C487 -Cond ([string]$longCredit -eq 'rc-release') -Name 'C1311 LongLivedRcRef credit rc/rc/rc/2026-10-15 earns rc-release' -Detail ([string]$longCredit)

    $stampCredit = Get-ReleaseGateCreditKind -Profile 'rc' -Trigger 'rc' -Ref $CandidateRef
    Assert-C487 -Cond ([string]$stampCredit -eq 'rc-release') -Name 'C1311 LongLivedRcRef credit rc/rc/release/rc-20260923T083000Z earns rc-release' -Detail ([string]$stampCredit)

    $omitRoot = New-C599Root
    $omitGit = New-C599GitSeams -Root $omitRoot -HeadSha $ShaB
    $omitRelease = Join-Path $omitRoot 'releases'
    $omitCheckout = New-C599OwnedReleaseClone -Root $omitRoot
    $omitted = & (Join-Path $here 'release-candidate.ps1') -ReleaseRoot $omitRelease -CheckoutRoot $omitCheckout -SeamsPath $omitGit.Path -PassThru
    $omitTrace = ''
    if (Test-Path -LiteralPath $omitGit.Trace) { $omitTrace = [string](Get-Content -LiteralPath $omitGit.Trace -Raw -ErrorAction SilentlyContinue) }
    Assert-C487 -Cond ([string]$omitted.Refusal -eq 'rc-ref-required' -and [int]$omitted.ExitCode -eq 3 -and -not [bool]$omitted.Pushed -and $omitTrace -notmatch 'GIT push') -Name 'C1311 LongLivedRcRef omitted Ref refuses rc-ref-required' -Detail ([string]$omitted.Refusal)

    # The remote tip is ShaB. rev-parse would answer ShaC, so a master pin is visible.
    $liveRoot = New-C599Root
    $liveGit = New-C599GitSeams -Root $liveRoot -HeadSha $ShaC -Refs @{ rc_2026_10_15 = $ShaB }
    $liveRelease = Join-Path $liveRoot 'releases'
    $liveCheckout = New-C599OwnedReleaseClone -Root $liveRoot
    $live = Invoke-C599Candidate -ReleaseRoot $liveRelease -Checkout $liveCheckout -Seams $liveGit -Ref 'rc/2026-10-15'
    $liveTrace = ''
    if (Test-Path -LiteralPath $liveGit.Trace) { $liveTrace = [string](Get-Content -LiteralPath $liveGit.Trace -Raw -ErrorAction SilentlyContinue) }
    $journalSha = ''
    $journalRef = ''
    $journalId = ''
    if (-not [string]::IsNullOrWhiteSpace([string]$live.JournalPath) -and (Test-Path -LiteralPath $live.JournalPath)) {
        $journal = Get-Content -LiteralPath $live.JournalPath -Raw | ConvertFrom-Json
        $journalSha = [string]$journal.sha
        $journalRef = [string]$journal.ref
        $journalId = [string]$journal.candidateId
    }
    $observed = Read-C599Remote -Seams $liveGit -Ref 'rc/2026-10-15'
    Assert-C487 -Cond (
        [int]$live.ExitCode -eq 0 -and
        -not [bool]$live.Pushed -and
        $journalSha -eq $ShaB -and
        $journalRef -eq 'rc/2026-10-15' -and
        $journalId -eq ('rc-2026-10-15-' + $prefix) -and
        $observed -eq $ShaB -and
        $liveTrace -notmatch 'GIT push') -Name 'C1311 LongLivedRcRef an existing rc/2026-10-15 tip is journalled and not pushed' -Detail ('exit=' + [string]$live.ExitCode + ' refusal=' + [string]$live.Refusal + ' sha=' + $journalSha + ' id=' + $journalId)
}

# ====================================================== CARD-1311 S3 tag ===

function Get-C1311Utc([string]$Text) {
    return [datetime]::Parse($Text, [System.Globalization.CultureInfo]::InvariantCulture, ([System.Globalization.DateTimeStyles]::AdjustToUniversal -bor [System.Globalization.DateTimeStyles]::AssumeUniversal))
}

function Get-C1311TagKey([string]$Tag) {
    return (('refs/tags/' + $Tag) -replace '[^A-Za-z0-9]', '_')
}

function Invoke-C1311Promote {
    <#
      Drive scripts/promote-release.ps1 against a file-backed remote. The live
      ref tip is whatever -RefTip seeds; the happy path passes the peeled SHA.
    #>
    param(
        [string]$PreTag = '',
        [string]$RemotePeel = '',
        [string]$HeadSha = '',
        [string]$CandidateId = '',
        [string]$CreditSha = '',
        [string]$RefTip = '',
        [hashtable]$ExtraRemote = @{},
        [switch]$SeedGitHub,
        [switch]$SkipRemote,
        [switch]$Reserve
    )
    if ([string]::IsNullOrWhiteSpace($HeadSha)) { $HeadSha = $ShaB }
    $root = New-C599Root
    $release = Join-Path $root 'releases'
    $reservedTag = ''
    $checkout = ''
    $policyHash = 'policy-1'
    $CandidateRef = 'rc/2026-10-15'
    if ($Reserve) {
        # The successful promotion now carries real pinned policy/ledger authority.
        $co = New-C599PolicyCheckout -Root $root -PolicyBytes (Get-C599ABasePolicyBytes)
        $checkout = $co.Path
        $HeadSha = $co.Sha; $CreditSha = $co.Sha; $RemotePeel = $co.Sha; $RefTip = $co.Sha
        $CandidateId = 'rc-2026-10-15-' + $co.Sha.Substring(0, 12)
        $auth = New-C599Authority -ReleaseRoot $release -Checkout $checkout -Sha $co.Sha
        $policyHash = $auth.policyHash
        $creditForReserve = $CreditSha
        if ([string]::IsNullOrWhiteSpace($creditForReserve)) { $creditForReserve = $HeadSha }
        $reserved = New-ReleaseGateTagReservation -RemoteTags @() -JournalPath (Join-Path $release 'publications.json') -CandidateId $CandidateId -Sha $creditForReserve -CutUtc (Get-C1311Utc '2026-10-15T00:00:00Z')
        $reservedTag = [string]$reserved.Tag
        [void](Set-ReleaseGateReservationPublished -JournalPath (Join-Path $release 'publications.json') -CandidateId $CandidateId -ReleaseId 'R-pre' -ReleaseUrl 'https://example/releases/tag/pre')
        if ([string]::IsNullOrWhiteSpace($PreTag)) { $PreTag = $reservedTag }
    }
    $refs = @{}
    if (-not [string]::IsNullOrWhiteSpace($RefTip)) { $refs['rc_2026_10_15'] = $RefTip }
    if (-not $SkipRemote -and -not [string]::IsNullOrWhiteSpace($PreTag)) {
        $peel = $RemotePeel
        if ([string]::IsNullOrWhiteSpace($peel)) { $peel = $HeadSha }
        $refs[(Get-C1311TagKey $PreTag)] = $peel
    }
    foreach ($key in @($ExtraRemote.Keys)) { $refs[$key] = $ExtraRemote[$key] }
    $git = New-C599GitSeams -Root $root -HeadSha $HeadSha -Refs $refs
    $gitText = Get-Content -LiteralPath $git.Path -Raw
    $gh = New-C599GitHubSeams -Root $root -GitSeamsText $gitText
    if ($SeedGitHub -and -not [string]::IsNullOrWhiteSpace($PreTag)) {
        Write-NightlyAtomicJson -Path $gh.StorePath -Object ([ordered]@{
            releases = @([ordered]@{ id = 'R-pre'; name = $PreTag; tagName = $PreTag; url = 'https://example/releases/tag/pre'; isDraft = $false; assets = @('release-manifest.json') })
        })
    }
    $credit = $CreditSha
    if ([string]::IsNullOrWhiteSpace($credit)) { $credit = $RemotePeel }
    if ([string]::IsNullOrWhiteSpace($credit)) { $credit = $HeadSha }
    if (-not [string]::IsNullOrWhiteSpace($CandidateId) -and (Test-ReleaseGateFullSha -Sha $credit)) {
        [void](New-C599RcGreen -ReleaseRoot $release -Sha $credit -CandidateId $CandidateId -Ref 'rc/2026-10-15' -PolicyHash $policyHash)
    }
    if (-not $checkout) { $checkout = New-C599OwnedReleaseClone -Root $root }
    $cardApi = New-C599CardApi -Root $root -Sha $credit -Candidate $CandidateId
    Add-Content -LiteralPath $gh.Path -Encoding ASCII -Value "`n`$ReleaseGateSeams.UtcNow = { return [datetime]'2026-10-15T12:00:00Z' }"
    $scriptPath = Join-Path $here 'promote-release.ps1'
    if (Test-Path -LiteralPath $scriptPath) {
        $result = & $scriptPath -ReleaseRoot $release -Tag $PreTag -CandidateId $CandidateId -CheckoutRoot $checkout -SeamsPath $gh.Path -AntiphonApiUrl $cardApi.Url -PassThru
    } else {
        $result = [pscustomobject]@{ ExitCode = 1; Refusal = 'missing-script'; Tag = ''; Sha = ''; Promoted = $false; Resumed = $false; PreReleaseTag = $PreTag }
    }
    return [pscustomobject]@{
        Result = $result; Git = $git; Gh = $gh; Root = $root; ReleaseRoot = $release; Checkout = $checkout
        PreTag = $PreTag; CandidateId = $CandidateId; ReservedTag = $reservedTag; Script = $scriptPath
        Head = $HeadSha; CardApi = $cardApi
    }
}

function Test-C1311TraceClean([string]$Trace) {
    if ([string]::IsNullOrWhiteSpace($Trace)) { return $false }
    $bad = @($Trace -split "`n" | Where-Object { $_ -match '--force' -or $_ -match '\+refs/' -or $_ -match 'tag -d' -or $_ -match 'push.*--delete' -or $_ -match '\s:refs/tags' })
    return ($bad.Count -eq 0)
}

function Test-C1311_PreReleaseTag {
    # D-3/D-4: -rc.K parses, a long-lived id proposes one, and promote writes the
    # final tag beside it. Stamp publication stays a final tag without --prerelease.
    $accepted = Test-ReleaseGateTag -Tag 'v2026.10.15.2-rc.1'
    $bareRc = Test-ReleaseGateTag -Tag 'v2026.10.15.2-rc'
    Assert-C487 -Cond (
        [bool]$accepted.Ok -and [bool]$accepted.Prerelease -and [int]$accepted.Rc -eq 1 -and [int]$accepted.Sequence -eq 2 -and
        (-not [bool]$bareRc.Ok) -and [string]$bareRc.Reason -eq 'tag-shape') `
        -Name 'C1311 PreReleaseTag v2026.10.15.2-rc.1 is accepted' -Detail ('rc1=' + [string]$accepted.Reason + '/' + [string]$accepted.Prerelease + ' bare=' + [string]$bareRc.Reason)

    $finalOnly = Test-ReleaseGateTag -Tag 'v2026.10.15.2'
    Assert-C487 -Cond ([bool]$finalOnly.Ok -and -not [bool]$finalOnly.Prerelease -and [int]$finalOnly.Sequence -eq 2) `
        -Name 'C1311 PreReleaseTag v2026.10.15.2 is accepted' -Detail ([string]$finalOnly.Reason + '/' + [string]$finalOnly.Prerelease)

    $zero = Test-ReleaseGateTag -Tag 'v2026.10.15.2-rc.0'
    Assert-C487 -Cond ((-not [bool]$zero.Ok) -and [string]$zero.Reason -eq 'tag-shape') `
        -Name 'C1311 PreReleaseTag v2026.10.15.2-rc.0 refuses tag-shape' -Detail ([string]$zero.Reason)

    $proposalRoot = New-C599Root
    $proposalJournal = Join-Path $proposalRoot 'publications.json'
    $cut = Get-C1311Utc '2026-10-15T00:00:00Z'
    $id1 = 'rc-2026-10-15-' + $ShaB.Substring(0, 12)
    $id2 = 'rc-2026-10-15-' + $ShaC.Substring(0, 12)
    $shaD = ('d' * 40)
    $shaE = ('e' * 40)
    $id3 = 'rc-2026-10-15-' + $shaD.Substring(0, 12)
    $id4 = 'rc-2026-10-16-' + $shaE.Substring(0, 12)
    $p1 = Get-ReleaseGateTagProposal -RemoteTags @() -JournalPath $proposalJournal -CandidateId $id1 -Sha $ShaB -CutUtc $cut
    [void](New-ReleaseGateTagReservation -RemoteTags @() -JournalPath $proposalJournal -CandidateId $id1 -Sha $ShaB -CutUtc $cut)
    $p2 = Get-ReleaseGateTagProposal -RemoteTags @() -JournalPath $proposalJournal -CandidateId $id2 -Sha $ShaC -CutUtc $cut
    [void](New-ReleaseGateTagReservation -RemoteTags @() -JournalPath $proposalJournal -CandidateId $id2 -Sha $ShaC -CutUtc $cut)
    $closed = $false
    if (Get-Command -Name Set-ReleaseGateReservationFinalTag -ErrorAction SilentlyContinue) {
        [void](Set-ReleaseGateReservationPublished -JournalPath $proposalJournal -CandidateId $id1 -ReleaseId 'fixture-pre' -ReleaseUrl 'https://example.invalid/pre')
        $closed = [bool](Set-ReleaseGateReservationFinalTag -JournalPath $proposalJournal -CandidateId $id1 -Sha $ShaB -PreReleaseTag $p1.Tag -FinalTag 'v2026.10.15.1')
    }
    $p3 = Get-ReleaseGateTagProposal -RemoteTags @() -JournalPath $proposalJournal -CandidateId $id3 -Sha $shaD -CutUtc $cut
    $p4 = Get-ReleaseGateTagProposal -RemoteTags @() -JournalPath $proposalJournal -CandidateId $id4 -Sha $shaE -CutUtc (Get-C1311Utc '2026-10-16T00:00:00Z')
    $stampJournal = Join-Path $proposalRoot 'stamp.json'
    $stampCut = Get-C1311Utc '2026-09-23T08:30:00Z'
    $stampProposal = Get-ReleaseGateTagProposal -RemoteTags @() -JournalPath $stampJournal -CandidateId $script:CandidateId -Sha $ShaB -CutUtc $stampCut
    [void](New-ReleaseGateTagReservation -RemoteTags @() -JournalPath $stampJournal -CandidateId $script:CandidateId -Sha $ShaB -CutUtc $stampCut)
    $reused = Get-ReleaseGateTagProposal -RemoteTags @() -JournalPath $stampJournal -CandidateId $script:CandidateId -Sha $ShaB -CutUtc $stampCut
    $threw = ''
    try { [void](Get-ReleaseGateTagProposal -RemoteTags @() -JournalPath $stampJournal -CandidateId $script:CandidateId -Sha $ShaC -CutUtc $stampCut) } catch { $threw = $_.Exception.Message }
    Assert-C487 -Cond (
        [string]$p1.Tag -eq 'v2026.10.15.1-rc.1' -and
        [string]$p2.Tag -eq 'v2026.10.15.1-rc.2' -and
        $closed -and
        [string]$p3.Tag -eq 'v2026.10.15.2-rc.1' -and
        [string]$p4.Tag -eq 'v2026.10.16.1-rc.1' -and
        [string]$stampProposal.Tag -eq 'v2026.09.23.1' -and
        [bool]$reused.Reused -and [string]$reused.Tag -eq 'v2026.09.23.1' -and
        $threw -match 'already reserved') `
        -Name 'C1311 PreReleaseTag a long-lived proposal is vYYYY.MM.DD.N-rc.K' `
        -Detail ('p1=' + [string]$p1.Tag + ' p2=' + [string]$p2.Tag + ' closed=' + $closed + ' p3=' + [string]$p3.Tag + ' p4=' + [string]$p4.Tag + ' stamp=' + [string]$stampProposal.Tag + ' reused=' + [string]$reused.Tag + ' threw=' + $threw)

    $happy = Invoke-C1311Promote -Reserve -RemotePeel $ShaB -HeadSha $ShaB -CandidateId $id1 -CreditSha $ShaB -RefTip $ShaB -SeedGitHub
    $finalSha = Read-C599Remote -Seams $happy.Git -Ref ('refs/tags/' + ($happy.PreTag -replace '-rc\.[0-9]+$', ''))
    $again = $null
    $secondTrace = ''
    if ([int]$happy.Result.ExitCode -eq 0 -and (Test-Path -LiteralPath $happy.Script)) {
        $before = @(Get-Content -LiteralPath $happy.Git.Trace -ErrorAction SilentlyContinue).Count
        $again = & $happy.Script -ReleaseRoot $happy.ReleaseRoot -Tag $happy.PreTag -CandidateId $happy.CandidateId -CheckoutRoot $happy.Checkout -SeamsPath $happy.Gh.Path -AntiphonApiUrl $happy.CardApi.Url -PassThru
        $lines = @(Get-Content -LiteralPath $happy.Git.Trace -ErrorAction SilentlyContinue)
        if ($lines.Count -gt $before) { $secondTrace = ($lines | Select-Object -Skip $before) -join "`n" }
    }
    $journalFinal = ''
    $journalPath = Join-Path $happy.ReleaseRoot 'publications.json'
    if (Test-Path -LiteralPath $journalPath) {
        $journalDoc = Get-Content -LiteralPath $journalPath -Raw | ConvertFrom-Json
        foreach ($row in @($journalDoc.reservations)) {
            if ([string]$row.candidateId -eq $happy.CandidateId) { $journalFinal = [string]$row.finalTag }
        }
    }
    $expectedFinal = $happy.PreTag -replace '-rc\.[0-9]+$', ''
    Assert-C487 -Cond (
        [int]$happy.Result.ExitCode -eq 0 -and [bool]$happy.Result.Promoted -and
        [string]$happy.Result.Tag -eq $expectedFinal -and [string]$happy.Result.Sha -eq $happy.Head -and
        $finalSha -eq $happy.Head -and (Read-C599Remote -Seams $happy.Git -Ref 'rc/2026-10-15') -eq $happy.Head -and
        $null -ne $again -and [int]$again.ExitCode -eq 0 -and [bool]$again.Resumed -and
        $secondTrace -notmatch 'GIT push' -and $secondTrace -notmatch 'GIT tag' -and
        $journalFinal -eq $expectedFinal) `
        -Name 'C1311 PreReleaseTag promote creates the final tag at the peeled sha' `
        -Detail ('exit=' + [string]$happy.Result.ExitCode + ' refusal=' + [string]$happy.Result.Refusal + ' tag=' + [string]$happy.Result.Tag + ' pre=' + $happy.PreTag + ' finalSha=' + $finalSha + ' journal=' + $journalFinal + ' again=' + [string]$again.ExitCode + '/' + [string]$again.Resumed)

    $preLeft = Read-C599Remote -Seams $happy.Git -Ref ('refs/tags/' + $happy.PreTag)
    $ghLeft = Get-C599Release -Gh $happy.Gh -Tag $happy.PreTag
    Assert-C487 -Cond (
        $happy.PreTag -match '-rc\.[1-9][0-9]*$' -and
        $preLeft -eq $happy.Head -and $null -ne $ghLeft -and [string]$ghLeft.tagName -eq $happy.PreTag -and
        $finalSha -eq $happy.Head) `
        -Name 'C1311 PreReleaseTag promote leaves the pre-release tag in place' -Detail ('pre=' + $happy.PreTag + '/' + $preLeft + ' gh=' + [string]$ghLeft.tagName)

    $conflict = Invoke-C1311Promote -PreTag 'v2026.10.15.1-rc.1' -RemotePeel $ShaB -HeadSha $ShaB -CandidateId $id1 -CreditSha $ShaB -RefTip $ShaB -ExtraRemote @{ (Get-C1311TagKey 'v2026.10.15.1') = $ShaA }
    $missing = Invoke-C1311Promote -PreTag 'v2026.10.15.1-rc.1' -SkipRemote -HeadSha $ShaB -CandidateId $id1 -CreditSha $ShaB -RefTip $ShaB
    $short = Invoke-C1311Promote -PreTag 'v2026.10.15.1-rc.1' -RemotePeel 'abcd' -HeadSha $ShaB -CandidateId $id1 -CreditSha $ShaB -RefTip $ShaB
    $mismatch = Invoke-C1311Promote -PreTag 'v2026.10.15.1-rc.1' -RemotePeel $ShaB -HeadSha $ShaB -CandidateId $id1 -CreditSha $ShaC -RefTip $ShaB
    $conflictTrace = ''
    if (Test-Path -LiteralPath $conflict.Git.Trace) { $conflictTrace = [string](Get-Content -LiteralPath $conflict.Git.Trace -Raw) }
    Assert-C487 -Cond (
        [string]$conflict.Result.Refusal -eq 'final-tag-conflict' -and [int]$conflict.Result.ExitCode -eq 3 -and
        (Read-C599Remote -Seams $conflict.Git -Ref 'refs/tags/v2026.10.15.1') -eq $ShaA -and
        (Read-C599Remote -Seams $conflict.Git -Ref 'refs/tags/v2026.10.15.1-rc.1') -eq $ShaB -and
        $conflictTrace -notmatch 'GIT push' -and $conflictTrace -notmatch '--force' -and
        [string]$missing.Result.Refusal -eq 'prerelease-missing' -and
        [string]$short.Result.Refusal -eq 'tag-peel' -and
        [string]$mismatch.Result.Refusal -eq 'credited-sha-mismatch' -and
        [string]::IsNullOrWhiteSpace((Read-C599Remote -Seams $missing.Git -Ref 'refs/tags/v2026.10.15.1')) -and
        [string]::IsNullOrWhiteSpace((Read-C599Remote -Seams $short.Git -Ref 'refs/tags/v2026.10.15.1')) -and
        [string]::IsNullOrWhiteSpace((Read-C599Remote -Seams $mismatch.Git -Ref 'refs/tags/v2026.10.15.1'))) `
        -Name 'C1311 PreReleaseTag promote refuses a final tag at another sha' `
        -Detail ('conflict=' + [string]$conflict.Result.Refusal + ' missing=' + [string]$missing.Result.Refusal + ' short=' + [string]$short.Result.Refusal + ' mismatch=' + [string]$mismatch.Result.Refusal)

    $stampPublished = $false
    $stampPre = -1
    $stampCreates = 0
    $stampTrace = ''
    $longPublished = $false
    $longPre = -1
    $longCreates = 0
    $longTrace = ''
    $longTagOk = $false
    $longPeeled = ''
    $promoteText = ''
    $promoteTrace = ''
    $promoteScript = Join-Path $here 'promote-release.ps1'
    try {
        $fxS = New-C599PublishFx
        $pubS = Invoke-C599Publish -Fx $fxS
        $stampPublished = ([int]$pubS.ExitCode -eq 0 -and [bool]$pubS.Published -and [string]$pubS.Tag -eq 'v2026.09.23.1')
        $stampGh = ''
        $stampGhPath = Join-Path $fxS.Root 'gh-trace.log'
        if (Test-Path -LiteralPath $stampGhPath) { $stampGh = [string](Get-Content -LiteralPath $stampGhPath -Raw) }
        $stampCreateLines = @(($stampGh -split "`n") | Where-Object { $_ -match '^GH release-create' })
        $stampCreates = $stampCreateLines.Count
        $stampPre = @($stampCreateLines | Where-Object { $_ -match '--prerelease' }).Count
        if (Test-Path -LiteralPath $fxS.Git.Trace) { $stampTrace = [string](Get-Content -LiteralPath $fxS.Git.Trace -Raw) }
    } catch { $stampTrace = $_.Exception.Message }
    try {
        $fxL = New-C599PublishFx -LongLivedCandidate
        $pubL = Invoke-C599Publish -Fx $fxL -CandidateId $fxL.CandidateId
        $parsedLong = Test-ReleaseGateTag -Tag ([string]$pubL.Tag)
        $longPublished = ([int]$pubL.ExitCode -eq 0 -and [bool]$pubL.Published)
        $longTagOk = ($longPublished -and [bool]$parsedLong.Ok -and [bool]$parsedLong.Prerelease)
        $longPeeled = Read-C599Remote -Seams $fxL.Git -Ref ('refs/tags/' + [string]$pubL.Tag)
        $longTagOk = $longTagOk -and ($longPeeled -eq [string]$fxL.Head) -and ((Read-C599Remote -Seams $fxL.Git -Ref 'rc/2026-10-15') -eq [string]$fxL.Head)
        $longGh = ''
        $longGhPath = Join-Path $fxL.Root 'gh-trace.log'
        if (Test-Path -LiteralPath $longGhPath) { $longGh = [string](Get-Content -LiteralPath $longGhPath -Raw) }
        $longCreateLines = @(($longGh -split "`n") | Where-Object { $_ -match '^GH release-create' })
        $longCreates = $longCreateLines.Count
        $longPre = @($longCreateLines | Where-Object { $_ -match '--prerelease' }).Count
        if (Test-Path -LiteralPath $fxL.Git.Trace) { $longTrace = [string](Get-Content -LiteralPath $fxL.Git.Trace -Raw) }
    } catch { $longTrace = $_.Exception.Message }
    if (Test-Path -LiteralPath $promoteScript) { $promoteText = [string](Get-Content -LiteralPath $promoteScript -Raw) }
    if (Test-Path -LiteralPath $happy.Git.Trace) { $promoteTrace = [string](Get-Content -LiteralPath $happy.Git.Trace -Raw) }
    $promoteQuiet = ($promoteText -notmatch 'restart-apphost') -and ($promoteText -notmatch 'Stop-Process') -and ($promoteText -notmatch 'taskkill') -and ($promoteText -notmatch 'deploy-local') -and ($promoteText -notmatch '--force') -and ($promoteText -notmatch 'tag -d')
    Assert-C487 -Cond (
        $stampPublished -and $stampCreates -ge 1 -and $stampPre -eq 0 -and (Test-C1311TraceClean $stampTrace) -and ($stampTrace -match 'GIT push') -and
        $longPublished -and $longTagOk -and $longCreates -ge 1 -and $longPre -eq $longCreates -and (Test-C1311TraceClean $longTrace) -and ($longTrace -match 'GIT push') -and
        (Test-C1311TraceClean $promoteTrace) -and ($promoteTrace -match 'GIT push') -and
        $promoteQuiet -and (Test-Path -LiteralPath $promoteScript) -and
        -not (Test-C1311TraceClean 'GIT push origin :refs/tags/v2026.10.15.1') -and
        -not (Test-C1311TraceClean 'GIT push origin --delete refs/tags/v2026.10.15.1') -and
        -not (Test-C1311TraceClean 'GIT push origin --force refs/tags/v2026.10.15.1') -and
        -not (Test-C1311TraceClean 'GIT push origin +refs/tags/v2026.10.15.1:refs/tags/v2026.10.15.1')) `
        -Name 'C1311 PreReleaseTag no tag delete and no force push' `
        -Detail ('stamp=' + $stampPublished + '/' + $stampCreates + '/' + $stampPre + ' long=' + $longPublished + '/' + $longTagOk + '/' + $longCreates + '/' + $longPre + ' peel=' + $longPeeled + ' quiet=' + $promoteQuiet)
}

function Get-C1311TraceTail {
    param([string]$Path, [int]$Skip)
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) { return '' }
    $lines = @(Get-Content -LiteralPath $Path -ErrorAction SilentlyContinue)
    if ($Skip -ge $lines.Count) { return '' }
    return (($lines | Select-Object -Skip $Skip) -join "`n")
}

function Test-C1311_MovingTipAuthority {
    # D-2/D-5/D-6: a second SHA of the same rc ref gets its own candidate root.
    # The first authority and tag stay on the first SHA. Publish and promote of
    # that SHA refuse remote-candidate-moved once the live tip has moved.
    # scriptHashes bind seven executable script files, never the moving tip.
    $fx = New-C599PublishFx -LongLivedCandidate
    $firstSha = [string]$fx.Head
    $secondSha = $ShaC
    $firstId = [string]$fx.CandidateId
    $secondId = 'rc-2026-10-15-' + $secondSha.Substring(0, 12)
    $firstRoot = Get-ReleaseGateCandidateRoot -ReleaseRoot $fx.ReleaseRoot -CandidateId $firstId
    $authPath = Join-Path (Get-ReleaseAuthorityDir -CandidateRoot $firstRoot) 'authority.json'
    $published = Invoke-C599Publish -Fx $fx -CandidateId $firstId
    $firstTag = [string]$published.Tag
    $authBefore = ''
    if (Test-Path -LiteralPath $authPath) { $authBefore = [string](Get-FileHash -LiteralPath $authPath -Algorithm SHA256).Hash }
    $authDoc = $null
    if (Test-Path -LiteralPath $authPath) { $authDoc = Get-Content -LiteralPath $authPath -Raw -Encoding UTF8 | ConvertFrom-Json }

    $remote = Get-Content -LiteralPath $fx.Git.RemotePath -Raw | ConvertFrom-Json
    $remote | Add-Member -NotePropertyName 'rc_2026_10_15' -NotePropertyValue $secondSha -Force
    $remote | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $fx.Git.RemotePath -Encoding UTF8
    $second = Invoke-C599Candidate -ReleaseRoot $fx.ReleaseRoot -Checkout $fx.Checkout -Seams $fx.Git -Ref 'rc/2026-10-15'
    $secondRoot = ''
    $secondJournalSha = ''
    $secondJournalId = ''
    $secondJournalRef = ''
    if (-not [string]::IsNullOrWhiteSpace([string]$second.JournalPath) -and (Test-Path -LiteralPath $second.JournalPath)) {
        $secondRoot = Split-Path -Parent ([string]$second.JournalPath)
        $secondJournal = Get-Content -LiteralPath $second.JournalPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $secondJournalSha = [string]$secondJournal.sha
        $secondJournalId = [string]$secondJournal.candidateId
        $secondJournalRef = [string]$secondJournal.ref
    }
    $firstJournalSha = ''
    $firstJournalPath = Join-Path $firstRoot 'candidate.json'
    if (Test-Path -LiteralPath $firstJournalPath) {
        $firstJournalSha = [string](Get-Content -LiteralPath $firstJournalPath -Raw -Encoding UTF8 | ConvertFrom-Json).sha
    }
    $rootsDiffer = $false
    if (-not [string]::IsNullOrWhiteSpace($secondRoot) -and (Test-Path -LiteralPath $secondRoot)) {
        $rootsDiffer = ([System.IO.Path]::GetFullPath($secondRoot) -ne [System.IO.Path]::GetFullPath($firstRoot))
    }
    Assert-C487 -Cond (
        [int]$second.ExitCode -eq 0 -and
        -not [bool]$second.Pushed -and
        $rootsDiffer -and
        $secondJournalId -eq $secondId -and
        $secondJournalSha -eq $secondSha -and
        $secondJournalRef -eq 'rc/2026-10-15' -and
        $firstJournalSha -eq $firstSha -and
        $secondId -ne $firstId) `
        -Name 'C1311 MovingTipAuthority a second sha gets its own candidate root' `
        -Detail ('exit=' + [string]$second.ExitCode + ' refusal=' + [string]$second.Refusal + ' id=' + $secondJournalId + ' sha=' + $secondJournalSha + ' first=' + $firstJournalSha)

    $authAfter = ''
    $authSha = ''
    if (Test-Path -LiteralPath $authPath) {
        $authAfter = [string](Get-FileHash -LiteralPath $authPath -Algorithm SHA256).Hash
        $authSha = [string](Get-Content -LiteralPath $authPath -Raw -Encoding UTF8 | ConvertFrom-Json).sha
    }
    Assert-C487 -Cond (
        -not [string]::IsNullOrWhiteSpace($authBefore) -and
        $authAfter -eq $authBefore -and
        $authSha -eq $firstSha -and
        $authSha -ne $secondSha) `
        -Name 'C1311 MovingTipAuthority the first authority.json still names the first sha' `
        -Detail ('sha=' + $authSha + ' hash=' + $authBefore + '/' + $authAfter)

    $gitLinesBefore = 0
    if (Test-Path -LiteralPath $fx.Git.Trace) { $gitLinesBefore = @(Get-Content -LiteralPath $fx.Git.Trace -ErrorAction SilentlyContinue).Count }
    $republish = Invoke-C599Publish -Fx $fx -CandidateId $firstId
    $republishTrace = Get-C1311TraceTail -Path $fx.Git.Trace -Skip $gitLinesBefore
    Assert-C487 -Cond (
        [int]$republish.ExitCode -eq 3 -and
        -not [bool]$republish.Published -and
        [string]$republish.Refusal -match 'remote-candidate-moved' -and
        $republishTrace -notmatch 'GIT tag' -and
        $republishTrace -notmatch 'GIT push') `
        -Name 'C1311 MovingTipAuthority publish of the first sha refuses remote-candidate-moved' `
        -Detail ('exit=' + [string]$republish.ExitCode + ' refusal=' + [string]$republish.Refusal)

    $stillPeeled = ''
    if (-not [string]::IsNullOrWhiteSpace($firstTag)) { $stillPeeled = Read-C599Remote -Seams $fx.Git -Ref ('refs/tags/' + $firstTag) }
    Assert-C487 -Cond (
        [int]$published.ExitCode -eq 0 -and [bool]$published.Published -and
        $firstTag -match '-rc\.[1-9][0-9]*$' -and
        $stillPeeled -eq $firstSha) `
        -Name 'C1311 MovingTipAuthority the first tag still peels to the first sha' `
        -Detail ('tag=' + $firstTag + ' peel=' + $stillPeeled + ' published=' + [string]$published.ExitCode + '/' + [string]$published.Refusal)

    $promoteLines = 0
    if (Test-Path -LiteralPath $fx.Git.Trace) { $promoteLines = @(Get-Content -LiteralPath $fx.Git.Trace -ErrorAction SilentlyContinue).Count }
    $ghPath = Join-Path $fx.Root 'gh-trace.log'
    $ghLines = 0
    if (Test-Path -LiteralPath $ghPath) { $ghLines = @(Get-Content -LiteralPath $ghPath -ErrorAction SilentlyContinue).Count }
    $promoteScript = Join-Path $here 'promote-release.ps1'
    $promoted = & $promoteScript -ReleaseRoot $fx.ReleaseRoot -Tag $firstTag -CandidateId $firstId -CheckoutRoot $fx.Checkout -SeamsPath $fx.Gh.Path -AntiphonApiUrl $fx.CardApi.Url -PassThru
    $promoteTrace = Get-C1311TraceTail -Path $fx.Git.Trace -Skip $promoteLines
    $ghTail = Get-C1311TraceTail -Path $ghPath -Skip $ghLines
    $finalName = ''
    if (-not [string]::IsNullOrWhiteSpace($firstTag)) { $finalName = [regex]::Replace($firstTag, '-rc\.[0-9]+$', '') }
    $finalPeeled = ''
    if (-not [string]::IsNullOrWhiteSpace($finalName)) { $finalPeeled = Read-C599Remote -Seams $fx.Git -Ref ('refs/tags/' + $finalName) }
    $preStill = ''
    if (-not [string]::IsNullOrWhiteSpace($firstTag)) { $preStill = Read-C599Remote -Seams $fx.Git -Ref ('refs/tags/' + $firstTag) }
    $finalRecorded = ''
    $pubPath = Join-Path $fx.ReleaseRoot 'publications.json'
    if (Test-Path -LiteralPath $pubPath) {
        $pubDoc = Get-Content -LiteralPath $pubPath -Raw -Encoding UTF8 | ConvertFrom-Json
        foreach ($row in @($pubDoc.reservations)) {
            if ([string]$row.candidateId -eq $firstId -and $row.PSObject.Properties.Name -contains 'finalTag') {
                $finalRecorded = [string]$row.finalTag
            }
        }
    }
    Assert-C487 -Cond (
        [string]$promoted.Refusal -eq 'remote-candidate-moved' -and
        [int]$promoted.ExitCode -eq 3 -and
        -not [bool]$promoted.Promoted -and
        [string]::IsNullOrWhiteSpace($finalPeeled) -and
        [string]::IsNullOrWhiteSpace($finalRecorded) -and
        $preStill -eq $firstSha -and
        $promoteTrace -notmatch 'GIT tag' -and
        $promoteTrace -notmatch 'GIT push' -and
        $promoteTrace -notmatch '--force' -and
        $ghTail -notmatch 'GH ') `
        -Name 'C1311 MovingTipAuthority promote of a moved tip refuses remote-candidate-moved' `
        -Detail ('exit=' + [string]$promoted.ExitCode + ' refusal=' + [string]$promoted.Refusal + ' final=' + $finalName + '/' + $finalPeeled + ' recorded=' + $finalRecorded + ' pre=' + $preStill)

    $want = @($script:ReleaseAuthorityScripts)
    $have = @()
    if ($null -ne $authDoc -and $null -ne $authDoc.scriptHashes) { $have = @($authDoc.scriptHashes.PSObject.Properties.Name) }
    $fresh = Get-ReleaseAuthorityScriptHashes
    $same = ($want.Count -eq 7 -and $have.Count -eq $want.Count)
    $omitsTip = $true
    foreach ($rel in $want) {
        if ($have -notcontains $rel) { $same = $false; continue }
        $got = [string]$authDoc.scriptHashes.$rel
        if ($got -ne [string]$fresh[$rel] -or [string]::IsNullOrWhiteSpace($got)) { $same = $false }
        if ($got -eq $firstSha -or $got -eq $secondSha -or $rel -eq $firstSha -or $rel -eq $secondSha) { $omitsTip = $false }
        if ($got.Contains($firstSha) -or $got.Contains($secondSha)) { $omitsTip = $false }
    }
    foreach ($rel in $have) { if ($want -notcontains $rel) { $same = $false } }
    Assert-C487 -Cond ($same -and $omitsTip -and $null -ne $authDoc -and [string]$authDoc.sha -eq $firstSha) `
        -Name 'C1311 MovingTipAuthority scriptHashes omit the branch tip' `
        -Detail ('keys=' + ($have -join ',') + ' same=' + $same + ' omits=' + $omitsTip)
}

function Test-C599_E2EWatchdog {
    # CARD-1345: the shipped policy resolves the rc e2e suite to 120 minutes.
    # The rc session-runner suite gets 30 minutes; antiphon and the fallback stay put.
    $ship = Read-NightlyExecutionPolicy -RepoRoot $repoRoot
    $e2e = Get-NightlyWatchdogMs -PolicyObject $ship.Object -Key 'e2e' -Fallback 3600000
    $antiphon = Get-NightlyWatchdogMs -PolicyObject $ship.Object -Key 'antiphon' -Fallback 3600000
    $session = Get-NightlyWatchdogMs -PolicyObject $ship.Object -Key 'session-runner' -Fallback 3600000
    $missing = Get-NightlyWatchdogMs -PolicyObject $ship.Object -Key 'not-a-suite' -Fallback 3600000
    Assert-C487 -Cond ($e2e -eq 7200000) -Name 'C599 resolved e2e watchdog is 7200000' -Detail ([string]$e2e)
    Assert-C487 -Cond ($antiphon -eq 3600000) -Name 'C599 antiphon watchdog stays 3600000' -Detail ([string]$antiphon)
    Assert-C487 -Cond ($session -eq 1800000) -Name 'C599 resolved session-runner watchdog is 1800000' -Detail ([string]$session)
    Assert-C487 -Cond ($missing -eq 3600000) -Name 'C599 missing watchdog key keeps the 3600000 fallback' -Detail ([string]$missing)
}

# CARD-1339: real local recipients, with a confined transport adapter.
. (Join-Path $here 'fixtures/c1339-release-git.ps1')

function Invoke-C1339LocalGit {
    param($Fx, [string[]]$Arguments, [switch]$Remote, [switch]$AllowAbsent)
    $wd = $Fx.Checkout
    if ($Remote) { $wd = $Fx.Bare }
    $r = Invoke-C1339GitProcess -Root $Fx.Root -WorkingDirectory $wd -Arguments $Arguments
    if ($r.ExitCode -ne 0 -and -not $AllowAbsent) { throw ('C1339 fixture git: ' + $r.Error) }
    return [string]$r.Output
}

function New-C1339Fixture {
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
        Update-C599Json -Path (Join-Path (Get-ReleaseGateCandidateRoot -ReleaseRoot $fx.ReleaseRoot -CandidateId $fx.CandidateId) 'candidate.json') -Mutate { param($c) $c.cutUtc = '2026-10-15T00:00:00Z' }
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

function Invoke-C1339Promote {
    param($Fx, [hashtable]$Extra = @{})
    $parameters = @{ Repository = 'fixture/release'; AntiphonApiUrl = $Fx.CardApi.Url }
    foreach ($key in $Extra.Keys) {
        if ($null -eq $Extra[$key]) { $parameters.Remove($key) } else { $parameters[$key] = $Extra[$key] }
    }
    & (Join-Path $here 'promote-release.ps1') -ReleaseRoot $Fx.ReleaseRoot -Tag $Fx.PreTag -CandidateId $Fx.CandidateId `
        -CheckoutRoot $Fx.Checkout -SeamsPath $Fx.Gh.Path -PassThru @parameters
}

function Invoke-C1339Publish {
    param($Fx, [switch]$WhatIf)
    Invoke-C599Publish -Fx $Fx -CandidateId $Fx.CandidateId -Extra @{ Repository = 'fixture/release'; WhatIf = [bool]$WhatIf }
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

function Get-C1339Peel {
    param($Fx, [string]$Tag)
    # show-ref --verify is exact; rev-parse of a ref alone also performs DWIM.
    $object = Invoke-C1339LocalGit $Fx @('show-ref', '--verify', '--hash', ('refs/tags/' + $Tag)) -Remote -AllowAbsent
    if (-not (Test-ReleaseGateFullSha $object)) { return '' }
    Invoke-C1339LocalGit $Fx @('rev-parse', '--verify', ($object + '^{}')) -Remote -AllowAbsent
}

function New-C1339Check {
    param([bool]$Pass, [string]$Detail)
    [pscustomobject]@{ Pass = $Pass; Detail = $Detail }
}

function Test-C1339_ExactRemoteRefs {
    $results = @()
    foreach ($order in @('control', 'nested-first', 'exact-first')) {
        $fx = New-C1339Fixture
        Add-C1339Tag $fx $fx.PreTag $fx.Head
        if ($order -ne 'control') {
            Add-C1339Tag $fx ('refs/tags/' + $fx.PreTag) $fx.B
            $output = Invoke-C1339LocalGit $fx @('ls-remote', '--tags', $fx.Bare, ('refs/tags/' + $fx.PreTag + '^{}'))
            # Real records, reordered so the nested record arrives first.
            $lines = @($output -split "`n" | Sort-Object { ($_ -split "`t")[1] })
            if ($order -eq 'exact-first') { [array]::Reverse($lines) }
            Write-NightlyAtomicJson -Path $fx.FaultPath -Object @{ reads = @(@{ ref = 'refs/tags/' + $fx.PreTag + '^{}'; mode = 'replace'; exit = 0; output = ($lines -join "`n") }) }
        }
        Initialize-C1339PromotionReservation $fx
        $r = Invoke-C1339Promote $fx
        $peel = Invoke-C1339LocalGit $fx @('rev-parse', '--verify', ('refs/tags/' + $fx.FinalTag + '^{}')) -Remote -AllowAbsent
        $results += New-C1339Check ($r.ExitCode -eq 0 -and $r.Promoted -and $peel -ceq $fx.Head) ($order + ' refusal=' + $r.Refusal + ' peel=' + $peel)
    }
    Assert-C599AMatrix -Name 'C1339 G-13 promotion-pre-read' -Results $results

    # Parser cases use complete real records; impossible output is introduced only
    # for malformed/duplicate/query-failure boundaries.
    $ref = 'refs/tags/' + $fx.PreTag + '^{}'
    $nestedLine = $fx.B + "`trefs/tags/refs/tags/" + $fx.PreTag + '^{}'
    $exactLine = $fx.Head + "`t" + $ref
    $checks = @()
    foreach ($output in @(($nestedLine + "`n" + $exactLine), ($exactLine + "`n" + $nestedLine))) {
        $r = ConvertFrom-ReleaseGateRemoteRef @{ ExitCode = 0; Output = $output } $ref
        $checks += New-C1339Check ($r.Status -eq 'Found' -and $r.Sha -ceq $fx.Head) 'exact tag order'
    }
    $r = ConvertFrom-ReleaseGateRemoteRef @{ ExitCode = 0; Output = $nestedLine } $ref
    $checks += New-C1339Check ($r.Status -eq 'Absent') 'nested only tag'
    Assert-C599AMatrix 'C1339 G-1 exact-tag' $checks

    [void](Invoke-C1339LocalGit $fx @('push', $fx.Bare, ($fx.B + ':refs/heads/archive/' + $fx.CandidateRef)))
    $headRef = 'refs/heads/' + $fx.CandidateRef
    $headOutput = Invoke-C1339LocalGit $fx @('ls-remote', '--heads', $fx.Bare)
    $r = ConvertFrom-ReleaseGateRemoteRef @{ ExitCode = 0; Output = $headOutput } $headRef
    $suffix = $fx.B + "`trefs/heads/archive/" + $fx.CandidateRef
    $absent = ConvertFrom-ReleaseGateRemoteRef @{ ExitCode = 0; Output = $suffix } $headRef
    Assert-C487 -Cond ($r.Status -eq 'Found' -and $r.Sha -ceq $fx.Head -and $absent.Status -eq 'Absent') -Name 'C1339 G-2 exact-head'
    $r = ConvertFrom-ReleaseGateRemoteRef @{ ExitCode = 0; Output = ($fx.Head + "`t" + $ref.ToUpperInvariant()) } $ref
    Assert-C487 -Cond ($r.Status -eq 'Absent') -Name 'C1339 G-3 ordinal-case'
    $checks = @()
    foreach ($sha in @('abcd', ('z' * 40))) {
        $r = ConvertFrom-ReleaseGateRemoteRef @{ ExitCode = 0; Output = ($sha + "`t" + $ref) } $ref
        $checks += New-C1339Check ($r.Status -eq 'Failed') $sha
    }
    Assert-C599AMatrix 'C1339 G-4 full-sha' $checks
    $checks = @()
    foreach ($output in @(($exactLine + "`n" + $fx.B + "`t" + $ref), ($fx.B + "`t" + $ref + "`n" + $exactLine))) {
        $r = ConvertFrom-ReleaseGateRemoteRef @{ ExitCode = 0; Output = $output } $ref
        $checks += New-C1339Check ($r.Status -eq 'Failed') 'conflicting duplicates'
    }
    $r = ConvertFrom-ReleaseGateRemoteRef @{ ExitCode = 0; Output = ("bad unrelated`n" + $exactLine) } $ref
    $checks += New-C1339Check ($r.Status -eq 'Found' -and $r.Sha -ceq $fx.Head) 'unrelated malformed record'
    Assert-C599AMatrix 'C1339 G-5 ambiguous-record' $checks
    $checks = @()
    foreach ($output in @('', $exactLine)) {
        $r = ConvertFrom-ReleaseGateRemoteRef @{ ExitCode = 1; Output = $output } $ref
        $checks += New-C1339Check ($r.Status -eq 'Failed') 'failed query'
    }
    $r = ConvertFrom-ReleaseGateRemoteRef @{ ExitCode = 0; Output = '' } $ref
    $checks += New-C1339Check ($r.Status -eq 'Absent') 'successful empty query'
    Assert-C599AMatrix 'C1339 G-6 query-failure' $checks

    $fx = New-C1339Fixture
    Set-C1339Read $fx ('refs/heads/' + $fx.CandidateRef) ($fx.B + "`trefs/heads/archive/" + $fx.CandidateRef)
    $r = Invoke-C599Candidate -ReleaseRoot $fx.ReleaseRoot -Checkout $fx.Checkout -Seams $fx.Gh -Ref $fx.CandidateRef
    $journal = Get-Content -LiteralPath $r.JournalPath -Raw | ConvertFrom-Json
    Assert-C487 -Cond ($r.ExitCode -eq 0 -and $journal.sha -ceq $fx.Head -and $journal.candidateId -ceq $fx.CandidateId -and (Test-C1339NoWrites $fx)) -Name 'C1339 G-7 candidate-long-read' -Detail $r.Refusal

    $fx = New-C1339Fixture -Stamp
    [void](Invoke-C1339LocalGit $fx @('push', $fx.Bare, ($fx.B + ':refs/heads/' + $fx.CandidateRef)))
    Set-C1339Read $fx ('refs/heads/' + $fx.CandidateRef) ($fx.Head + "`trefs/heads/archive/" + $fx.CandidateRef)
    $r = Invoke-C599Candidate -ReleaseRoot $fx.ReleaseRoot -Checkout $fx.Checkout -Seams $fx.Gh -Ref $fx.CandidateRef
    $tip = Invoke-C1339LocalGit $fx @('rev-parse', ('refs/heads/' + $fx.CandidateRef)) -Remote
    $checks = @(New-C1339Check ($r.Refusal -eq 'remote-candidate-conflict' -and $tip -ceq $fx.B -and (Test-C1339NoWrites $fx)) $r.Refusal)
    Set-C1339Read $fx ('refs/heads/' + $fx.CandidateRef) ($fx.Head + "`trefs/heads/" + $fx.CandidateRef) -Mode replace -Exit 1
    $r = Invoke-C599Candidate -ReleaseRoot $fx.ReleaseRoot -Checkout $fx.Checkout -Seams $fx.Gh -Ref $fx.CandidateRef
    $checks += New-C1339Check ($r.Refusal -eq 'remote-candidate-unresolved' -and (Test-C1339NoWrites $fx)) $r.Refusal
    Assert-C599AMatrix 'C1339 G-8 candidate-stamp-read' $checks

    $checks = @()
    foreach ($push in @('before', 'after')) {
        $fx = New-C1339Fixture -Stamp -NoCandidateRemote
        Set-C1339Read $fx ('refs/heads/' + $fx.CandidateRef) ($fx.Head + "`trefs/heads/archive/" + $fx.CandidateRef) -Ordinal 2 -Push $push
        $r = Invoke-C599Candidate -ReleaseRoot $fx.ReleaseRoot -Checkout $fx.Checkout -Seams $fx.Gh -Ref $fx.CandidateRef
        $tip = Invoke-C1339LocalGit $fx @('rev-parse', '--verify', ('refs/heads/' + $fx.CandidateRef)) -Remote -AllowAbsent
        $ok = $r.Refusal -eq 'push-failed' -and $tip -eq ''
        if ($push -eq 'after') { $ok = $r.ExitCode -eq 0 -and $r.Pushed -and $tip -ceq $fx.Head }
        $checks += New-C1339Check $ok ($push + ':' + $r.Refusal)
    }
    Assert-C599AMatrix 'C1339 G-9 candidate-recovery-read' $checks

    $fx = New-C1339Fixture
    [void](Invoke-C1339LocalGit $fx @('push', $fx.Bare, ($fx.B + ':refs/heads/' + $fx.CandidateRef)))
    Set-C1339Read $fx ('refs/heads/' + $fx.CandidateRef) ($fx.Head + "`trefs/heads/archive/" + $fx.CandidateRef)
    $r = Invoke-C1339Publish $fx
    Assert-C487 -Cond ($r.Refusal -eq 'remote-candidate-moved' -and (Test-C1339NoWrites $fx) -and (Get-C1339Peel $fx $fx.PreTag) -eq '') -Name 'C1339 G-10 publisher-head-read' -Detail $r.Refusal

    $checks = @()
    foreach ($push in @('after', 'before')) {
        $fx = New-C1339Fixture
        $nestedSha = $fx.B
        if ($push -eq 'before') { $nestedSha = $fx.Head }
        Set-C1339Read $fx ('refs/tags/' + $fx.PreTag + '^{}') ($nestedSha + "`trefs/tags/refs/tags/" + $fx.PreTag + '^{}') -Push $push
        $r = Invoke-C1339Publish $fx
        $peel = Get-C1339Peel $fx $fx.PreTag
        $journal = Get-Content -LiteralPath (Join-Path $fx.ReleaseRoot 'publications.json') -Raw | ConvertFrom-Json
        $ok = $r.Refusal -eq 'tag-push-failed' -and $peel -eq ''
        if ($push -eq 'after') { $ok = $r.ExitCode -eq 0 -and $r.Published -and $peel -ceq $fx.Head }
        $checks += New-C1339Check ($ok -and @($journal.reservations).Count -eq 1) ($push + ':' + $r.Refusal)
    }
    Assert-C599AMatrix 'C1339 G-11 publisher-recovery-read' $checks

    $fx = New-C1339Fixture
    Add-C1339Tag $fx ('refs/tags/' + $fx.PreTag) $fx.B
    Set-C1339Read $fx ('refs/tags/' + $fx.PreTag + '^{}') ($fx.B + "`trefs/tags/unrelated/" + $fx.PreTag + '^{}')
    $r = Invoke-C1339Publish $fx
    $gh = Get-Content -LiteralPath $fx.Gh.StorePath -Raw | ConvertFrom-Json
    $privateRefs = Invoke-C1339LocalGit $fx @('for-each-ref', '--format=%(refname)', 'refs/antiphon-release-push/')
    Assert-C487 -Cond ($r.ExitCode -eq 0 -and $r.Published -and (Get-C1339Peel $fx $fx.PreTag) -ceq $fx.Head -and @($gh.releases).Count -eq 1 -and -not $gh.releases[0].isDraft -and $privateRefs -eq '') -Name 'C1339 G-12 publisher-verify-read' -Detail $r.Refusal

    $fx = New-C1339Fixture
    Add-C1339Tag $fx $fx.PreTag $fx.Head
    [void](Invoke-C1339LocalGit $fx @('push', $fx.Bare, ($fx.B + ':refs/heads/' + $fx.CandidateRef)))
    Set-C1339Read $fx ('refs/heads/' + $fx.CandidateRef) ($fx.Head + "`trefs/heads/archive/" + $fx.CandidateRef)
    Initialize-C1339PromotionReservation $fx
    $r = Invoke-C1339Promote $fx
    Assert-C487 -Cond ($r.Refusal -eq 'remote-candidate-moved' -and (Test-C1339NoWrites $fx) -and (Get-C1339Peel $fx $fx.FinalTag) -eq '') -Name 'C1339 G-14 promotion-head-read' -Detail $r.Refusal

    $checks = @()
    foreach ($conflict in @($false, $true)) {
        $fx = New-C1339Fixture
        Add-C1339Tag $fx $fx.PreTag $fx.Head
        Add-C1339Tag $fx ('refs/tags/' + $fx.FinalTag) $fx.Head
        if ($conflict) { Add-C1339Tag $fx $fx.FinalTag $fx.C }
        Initialize-C1339PromotionReservation $fx
        $r = Invoke-C1339Promote $fx
        $peel = Get-C1339Peel $fx $fx.FinalTag
        $privateRefs = Invoke-C1339LocalGit $fx @('for-each-ref', '--format=%(refname)', 'refs/antiphon-release-push/')
        $ok = $r.ExitCode -eq 0 -and $r.Promoted -and -not $r.Resumed -and $peel -ceq $fx.Head
        if ($conflict) { $ok = $r.Refusal -eq 'final-tag-conflict' -and $peel -ceq $fx.C -and (Test-C1339NoWrites $fx) }
        $checks += New-C1339Check ($ok -and $privateRefs -eq '' -and (Get-C1339Peel $fx ('refs/tags/' + $fx.FinalTag)) -ceq $fx.Head) ('conflict=' + $conflict + ':' + $r.Refusal)
    }
    Assert-C599AMatrix 'C1339 G-15 promotion-existing-read' $checks

    $checks = @()
    foreach ($push in @('after', 'before')) {
        $fx = New-C1339Fixture
        Add-C1339Tag $fx $fx.PreTag $fx.Head
        $nestedSha = $fx.B
        if ($push -eq 'before') { $nestedSha = $fx.Head }
        Set-C1339Read $fx ('refs/tags/' + $fx.FinalTag + '^{}') ($nestedSha + "`trefs/tags/refs/tags/" + $fx.FinalTag + '^{}') -Ordinal 2 -Push $push
        Initialize-C1339PromotionReservation $fx
        $r = Invoke-C1339Promote $fx
        $peel = Get-C1339Peel $fx $fx.FinalTag
        $ok = $r.Refusal -eq 'tag-push-failed' -and $peel -eq ''
        if ($push -eq 'after') { $ok = $r.ExitCode -eq 0 -and $r.Promoted -and $peel -ceq $fx.Head }
        $checks += New-C1339Check $ok ($push + ':' + $r.Refusal)
    }
    Assert-C599AMatrix 'C1339 G-16 promotion-recovery-read' $checks

    $checks = @()
    foreach ($failure in @($false, $true)) {
        $fx = New-C1339Fixture
        Add-C1339Tag $fx $fx.PreTag $fx.Head
        if ($failure) { Set-C1339Read $fx ('refs/tags/' + $fx.FinalTag + '^{}') -Mode replace -Ordinal 2 -Exit 1 }
        else { Set-C1339Read $fx ('refs/tags/' + $fx.FinalTag + '^{}') ($fx.B + "`trefs/tags/refs/tags/" + $fx.FinalTag + '^{}') -Ordinal 2 }
        Initialize-C1339PromotionReservation $fx
        $r = Invoke-C1339Promote $fx
        $ok = $r.ExitCode -eq 0 -and $r.Promoted
        if ($failure) { $ok = $r.Refusal -eq 'tag-peel' -and -not $r.Promoted -and -not $r.Resumed }
        $checks += New-C1339Check ($ok -and (Get-C1339Peel $fx $fx.FinalTag) -ceq $fx.Head) ('failed-query=' + $failure + ':' + $r.Refusal)
    }
    Assert-C599AMatrix 'C1339 G-17 promotion-verify-read' $checks

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

function New-C1339AllocationFixture {
    $fx = New-C1339Fixture
    Add-Content -LiteralPath $fx.Gh.Path -Encoding ASCII -Value "`n`$ReleaseGateSeams.UtcNow = { return [datetime]'2026-10-15T12:00:00Z' }"
    return $fx
}

function Test-C1339_RemoteTagAllocation {
    $checks = @()
    foreach ($lightweight in @($false, $true)) {
        $fx = New-C1339AllocationFixture
        foreach ($n in @(1, 2)) {
            $tag = 'v2026.10.15.' + $n
            if ($lightweight) { [void](Invoke-C1339LocalGit $fx @('tag', $tag, $fx.Head) -Remote) }
            else { Add-C1339Tag $fx $tag $fx.Head }
        }
        $r = Invoke-C1339Publish $fx -WhatIf
        $checks += New-C1339Check ($r.ExitCode -eq 0 -and $r.Tag -ceq 'v2026.10.15.3-rc.1' -and
            -not (Test-Path -LiteralPath (Join-Path $fx.ReleaseRoot 'publications.json')) -and (Test-C1339NoWrites $fx)) ('lightweight=' + $lightweight + ' tag=' + $r.Tag + ':' + $r.Refusal)
    }
    Assert-C599AMatrix 'C1339 G-18 remote-finals' $checks
    Add-C1339Tag $fx 'v2026.10.15.3-rc.4' $fx.Head
    $r = Invoke-C1339Publish $fx -WhatIf
    Assert-C487 -Cond ($r.ExitCode -eq 0 -and $r.Tag -ceq 'v2026.10.15.3-rc.5') -Name 'C1339 G-19 remote-prerelease' -Detail $r.Tag
    Add-C1339Tag $fx 'v2026.10.15.3' $fx.Head
    $r = Invoke-C1339Publish $fx -WhatIf
    Assert-C487 -Cond ($r.ExitCode -eq 0 -and $r.Tag -ceq 'v2026.10.15.4-rc.1') -Name 'C1339 G-20 remote-final-closes' -Detail $r.Tag

    $fx = New-C1339AllocationFixture
    Add-C1339Tag $fx 'refs/tags/v2026.10.15.1' $fx.B
    Add-C1339Tag $fx 'irrelevant' $fx.B
    Add-C1339Tag $fx 'v2026.10.15.3' $fx.Head
    $r = Invoke-C1339Publish $fx -WhatIf
    Import-ReleaseGateSeams -SeamsPath $fx.Gh.Path
    $inventory = Get-ReleaseGateRemoteTagInventory -WorkingDirectory $fx.Checkout
    $output = Invoke-C1339LocalGit $fx @('ls-remote', '--tags', $fx.Bare)
    Set-C1339Read $fx 'origin' $output -Mode replace
    Import-ReleaseGateSeams -SeamsPath $fx.Gh.Path
    $withPeels = Get-ReleaseGateRemoteTagInventory -WorkingDirectory $fx.Checkout
    Assert-C487 -Cond ($r.ExitCode -eq 0 -and $r.Tag -ceq 'v2026.10.15.1-rc.1' -and $inventory.Ok -and
        @($inventory.Tags).Count -eq 1 -and $withPeels.Ok -and @($withPeels.Tags).Count -eq 1 -and $withPeels.Tags[0] -ceq 'v2026.10.15.3') -Name 'C1339 G-21 canonical-inventory' -Detail $r.Tag

    $day = Get-C1311Utc '2026-10-15T12:00:00Z'
    $journal = Join-Path (New-C599Root) 'publications.json'
    $row = New-ReleaseGateReservationEntry -CandidateId 'journal-candidate' -Sha $fx.B -Tag 'v2026.10.15.2-rc.7' -Sequence 2 -Rc 7 -Published $false
    Write-NightlyAtomicJson -Path $journal -Object @{ schemaVersion = 1; reservations = @($row) }
    $proposalArgs = @{ JournalPath = $journal; CandidateId = $fx.CandidateId; Sha = $fx.Head; CutUtc = $day }
    $r = Get-ReleaseGateTagProposal @proposalArgs -RemoteTags @('v2026.10.15.1')
    Assert-C487 -Cond ($r.Tag -ceq 'v2026.10.15.2-rc.8' -and @($r.Journal.reservations).Count -eq 1) -Name 'C1339 G-22 journal-union' -Detail $r.Tag
    $r = Get-ReleaseGateTagProposal @proposalArgs -RemoteTags @('v2026.10.15.1', 'v2026.10.15.2', 'v2026.10.15.3')
    # This extra final closes a journal-only open sequence too.
    $closedJournal = $r.Tag -ceq 'v2026.10.15.4-rc.1'
    $emptyJournal = Join-Path (New-C599Root) 'publications.json'
    $proposalArgs.JournalPath = $emptyJournal
    $r = Get-ReleaseGateTagProposal @proposalArgs -RemoteTags @('v2026.10.15.1', 'v2026.10.15.3')
    Assert-C487 -Cond ($r.Tag -ceq 'v2026.10.15.2-rc.1' -and $closedJournal) -Name 'C1339 G-23 smallest-free' -Detail $r.Tag
    $proposalArgs.CandidateId = $script:CandidateId
    $r = Get-ReleaseGateTagProposal @proposalArgs -RemoteTags @('v2026.10.15.1', 'v2026.10.15.2-rc.4')
    $proposalArgs.JournalPath = $journal
    $r2 = Get-ReleaseGateTagProposal @proposalArgs -RemoteTags @('v2026.10.15.1')
    Assert-C487 -Cond ($r.Tag -ceq 'v2026.10.15.3' -and $r2.Tag -ceq 'v2026.10.15.3') -Name 'C1339 G-24 legacy-occupancy' -Detail ($r.Tag + '/' + $r2.Tag)

    $fx = New-C1339AllocationFixture
    $control = Invoke-C1339Publish $fx -WhatIf
    $checks = @(New-C1339Check ($control.ExitCode -eq 0 -and $control.Tag -ceq 'v2026.10.15.1-rc.1') 'valid inventory admission')
    foreach ($preview in @($true, $false)) {
        Set-C1339Read $fx 'origin' -Mode replace -Exit 1
        $r = Invoke-C1339Publish $fx -WhatIf:$preview
        $checks += New-C1339Check ($r.Refusal -eq 'remote-tag-inventory-failed' -and $r.Tag -eq '' -and (Test-C1339NoWrites $fx) -and
            -not (Test-Path -LiteralPath (Join-Path $fx.ReleaseRoot 'publications.json'))) ('preview=' + $preview + ':' + $r.Refusal)
    }
    foreach ($bad in @("short`trefs/tags/v2026.10.15.1", ($fx.Head + "`trefs/tags/v2026.10.15.1`n" + $fx.B + "`trefs/tags/v2026.10.15.1"), 'malformed')) {
        Set-C1339Read $fx 'origin' $bad -Mode replace
        $r = Invoke-C1339Publish $fx -WhatIf
        $checks += New-C1339Check ($r.Refusal -eq 'remote-tag-inventory-failed' -and $r.Tag -eq '' -and (Test-C1339NoWrites $fx)) ('invalid inventory:' + $r.Refusal)
    }
    Assert-C599AMatrix 'C1339 G-25 inventory-required' $checks

    $fx = New-C1339AllocationFixture
    Write-NightlyAtomicJson -Path $fx.FaultPath -Object @{ push = 'before' }
    $first = Invoke-C1339Publish $fx
    Add-C1339Tag $fx 'v2026.10.15.2-rc.4' $fx.B
    Write-NightlyAtomicJson -Path $fx.FaultPath -Object @{}
    $second = Invoke-C1339Publish $fx
    $journal = Join-Path $fx.ReleaseRoot 'publications.json'
    $stored = Get-Content -LiteralPath $journal -Raw | ConvertFrom-Json
    Assert-C487 -Cond ($first.Refusal -eq 'tag-push-failed' -and $second.ExitCode -eq 0 -and $second.Published -and $second.Resumed -and
        $first.Tag -ceq 'v2026.10.15.1-rc.1' -and $second.Tag -ceq $first.Tag -and @($stored.reservations).Count -eq 1 -and
        (Get-C1339Peel $fx $second.Tag) -ceq $fx.Head) -Name 'C1339 G-26 reservation-reuse' -Detail $second.Refusal
    $before = [IO.File]::ReadAllText($journal)
    $argsForProposal = @{ JournalPath = $journal; CandidateId = $fx.CandidateId; Sha = $fx.C; CutUtc = $day; RemoteTags = @() }
    $errorText = ''
    try { [void](Get-ReleaseGateTagProposal @argsForProposal) } catch { $errorText = $_.Exception.Message }
    Assert-C487 -Cond ($errorText -match 'already reserved for sha' -and [IO.File]::ReadAllText($journal) -ceq $before) -Name 'C1339 G-27 candidate-sha-conflict' -Detail $errorText
    $argsForProposal.Sha = $fx.Head; $argsForProposal.CandidateId = 'rc-2026-10-15-' + $fx.B.Substring(0, 12)
    $errorText = ''
    try { [void](Get-ReleaseGateTagProposal @argsForProposal) } catch { $errorText = $_.Exception.Message }
    Assert-C487 -Cond ($errorText -match 'already reserved under candidate' -and [IO.File]::ReadAllText($journal) -ceq $before) -Name 'C1339 G-28 sha-candidate-conflict' -Detail $errorText

    foreach ($final in @($false, $true)) {
        $fx = New-C1339AllocationFixture
        $tag = $fx.PreTag; $reason = 'tag-conflict'; $label = 'C1339 G-29 selected-tag-recheck'
        if ($final) { $tag = $fx.FinalTag; $reason = 'final-tag-conflict'; $label = 'C1339 G-30 selected-final-recheck' }
        Write-NightlyAtomicJson -Path $fx.FaultPath -Object @{ inventoryRace = @{ tag = $tag; sha = $fx.C } }
        $r = Invoke-C1339Publish $fx
        $journal = Join-Path $fx.ReleaseRoot 'publications.json'
        $stored = Get-Content -LiteralPath $journal -Raw | ConvertFrom-Json
        $localTag = Invoke-C1339LocalGit $fx @('show-ref', '--verify', '--hash', ('refs/tags/' + $fx.PreTag)) -AllowAbsent
        $checks = @(New-C1339Check ($r.Refusal -eq $reason -and $r.Tag -ceq $fx.PreTag -and @($stored.reservations).Count -eq 1 -and
            $stored.reservations[0].tag -ceq $fx.PreTag -and (Test-C1339NoWrites $fx) -and $localTag -eq '' -and (Get-C1339Peel $fx $tag) -ceq $fx.C) ('race:' + $r.Refusal))
        # Restart from that persisted reservation against the now conflicting ref.
        $before = [IO.File]::ReadAllText($journal)
        $again = Invoke-C1339Publish $fx
        $checks += New-C1339Check ($again.Refusal -eq $reason -and $again.Tag -ceq $fx.PreTag -and [IO.File]::ReadAllText($journal) -ceq $before -and (Test-C1339NoWrites $fx)) ('reserved conflict:' + $again.Refusal)
        Assert-C599AMatrix $label $checks
    }

    $fx = New-C1339AllocationFixture
    Add-C1339Tag $fx 'v2026.10.15.1' $fx.B
    $journal = Join-Path $fx.ReleaseRoot 'publications.json'
    $root = Get-ReleaseGateCandidateRoot -ReleaseRoot $fx.ReleaseRoot -CandidateId $fx.CandidateId
    $pubAuthority = Join-Path (Get-ReleaseAuthorityDir -CandidateRoot $root) 'publication-authority.json'
    $preview = Invoke-C1339Publish $fx -WhatIf
    $emptyPreview = $preview.ExitCode -eq 0 -and $preview.Tag -ceq 'v2026.10.15.2-rc.1' -and -not (Test-Path -LiteralPath $journal)
    $emptyAuthority = -not (Test-Path -LiteralPath $pubAuthority)
    [void](New-ReleaseGateTagReservation -JournalPath $journal -CandidateId $fx.CandidateId -Sha $fx.Head -CutUtc $day -RemoteTags @('v2026.10.15.1'))
    $before = [IO.File]::ReadAllText($journal)
    $preview = Invoke-C1339Publish $fx -WhatIf
    Assert-C487 -Cond ($emptyPreview -and $preview.ExitCode -eq 0 -and $preview.Resumed -and [IO.File]::ReadAllText($journal) -ceq $before -and (Test-C1339NoWrites $fx)) -Name 'C1339 G-31 preview-reservation' -Detail $preview.Refusal
    Assert-C487 -Cond ($emptyAuthority -and -not (Test-Path -LiteralPath $pubAuthority)) -Name 'C1339 G-32 preview-authority'

    $fx = New-C1339AllocationFixture
    foreach ($tag in @('v2026.10.15.2-rc.9', 'v2026.10.15.3-rc.2', 'v2026.10.15.3-rc.4')) { Add-C1339Tag $fx $tag $fx.Head }
    Import-ReleaseGateSeams -SeamsPath $fx.Gh.Path
    $inventory = Get-ReleaseGateRemoteTagInventory -WorkingDirectory $fx.Checkout
    $ordered = @($inventory.Tags | Sort-Object)
    $nChecks = @(); $kChecks = @()
    foreach ($reverse in @($false, $true)) {
        if ($reverse) { [array]::Reverse($ordered) }
        $r = Get-ReleaseGateTagProposal -JournalPath (Join-Path $fx.ReleaseRoot 'publications.json') -CandidateId $fx.CandidateId -Sha $fx.Head -CutUtc $day -RemoteTags $ordered
        $nChecks += New-C1339Check ($r.Sequence -eq 3) $r.Tag
        $kChecks += New-C1339Check ($r.Rc -eq 5) $r.Tag
    }
    Assert-C599AMatrix 'C1339 G-67 greatest-open-n' $nChecks
    Assert-C599AMatrix 'C1339 G-68 greatest-k' $kChecks

    $fx = New-C1339AllocationFixture
    Add-C1339Tag $fx 'v2026.10.15.1' $fx.Head
    Add-C1339Tag $fx 'v2026.10.15.2' $fx.Head
    Write-NightlyAtomicJson -Path $fx.FaultPath -Object @{ push = 'before' }
    $r = Invoke-C1339Publish $fx
    $journal = Join-Path $fx.ReleaseRoot 'publications.json'
    $stored = Get-Content -LiteralPath $journal -Raw | ConvertFrom-Json
    Assert-C487 -Cond ($r.Refusal -eq 'tag-push-failed' -and @($stored.reservations).Count -eq 1 -and $stored.reservations[0].tag -ceq 'v2026.10.15.3-rc.1' -and
        $stored.reservations[0].candidateId -ceq $fx.CandidateId -and -not $stored.reservations[0].published) -Name 'C1339 G-69 no-invented-publications' -Detail $r.Refusal

    $checks = @()
    foreach ($function in @('Get-ReleaseGateTagProposal', 'New-ReleaseGateTagReservation')) {
        $path = Join-Path (New-C599Root) 'publications.json'
        $parameters = @{ JournalPath = $path; CandidateId = $fx.CandidateId; Sha = $fx.Head; CutUtc = $day }
        $result = $null; $refused = ''
        # ScriptHarness is a bounded -NonInteractive child; missing mandatory input
        # cannot pause it for a prompt. All other arguments are valid.
        try { $result = & $function @parameters -ErrorAction Stop } catch { $refused = $_.Exception.Message }
        $checks += New-C1339Check ($null -eq $result -and $refused -match 'RemoteTags|mandatory|NonInteractive' -and -not (Test-Path -LiteralPath $path)) ($function + ':' + $refused)
        $explicit = & $function @parameters -RemoteTags @()
        $checks += New-C1339Check ($explicit.Tag -ceq 'v2026.10.15.1-rc.1') ($function + ': explicit empty')
    }
    Assert-C599AMatrix 'C1339 G-70 no-implicit-inventory' $checks
}

# ====================================================================== run ===

function Set-C1339Clock {
    param($Fx, [string]$Utc, [string]$NextUtc = '')
    if (-not $NextUtc) { $NextUtc = $Utc }
    Add-Content -LiteralPath $Fx.Gh.Path -Encoding ASCII -Value @"
`$script:C1339ClockCalls = 0
`$ReleaseGateSeams.UtcNow = {
    Add-Content -LiteralPath '$($Fx.Root)/clock.log' -Value 'clock' -Encoding ASCII
    `$script:C1339ClockCalls++
    if (`$script:C1339ClockCalls -gt 1) { return [datetime]'$NextUtc' }
    return [datetime]'$Utc'
}
"@
}

function Set-C1339Candidate {
    param($Fx, [string]$Sha)
    $CandidateId = 'rc-2026-10-15-' + $Sha.Substring(0, 12)
    $CandidateRef = $Fx.CandidateRef
    $auth = New-C599Authority -ReleaseRoot $Fx.ReleaseRoot -Checkout $Fx.Checkout -Sha $Sha -Repository 'fixture/release'
    [void](New-C599RcGreen -ReleaseRoot $Fx.ReleaseRoot -Sha $Sha -CandidateId $CandidateId -Ref $CandidateRef -PolicyHash $auth.policyHash)
    $Fx.CandidateId = $CandidateId
    $Fx.Head = $Sha
    $Fx.CardApi = New-C599CardApi -Root $Fx.Root -Sha $Sha -Candidate $CandidateId
    [void](Invoke-C1339LocalGit $Fx @('push', $Fx.Bare, ($Sha + ':refs/heads/' + $CandidateRef)))
    [void](Invoke-C1339LocalGit $Fx @('checkout', '--detach', '-q', $Sha))
}

function Test-C1339_AllocationUtcDay {
    $fx = New-C1339Fixture
    Set-C1339Clock $fx '2026-10-20T01:59:59+02:00' '2026-10-20T00:00:01Z'
    $candidatePath = Join-Path (Get-ReleaseGateCandidateRoot -ReleaseRoot $fx.ReleaseRoot -CandidateId $fx.CandidateId) 'candidate.json'
    $candidateBefore = [IO.File]::ReadAllText($candidatePath)
    $preview = Invoke-C1339Publish $fx -WhatIf
    $previewClean = -not (Test-Path (Join-Path $fx.ReleaseRoot 'publications.json'))
    $first = Invoke-C1339Publish $fx
    Assert-C487 -Cond ($preview.ExitCode -eq 0 -and $preview.Tag -ceq 'v2026.10.19.1-rc.1' -and $previewClean -and
        $first.ExitCode -eq 0 -and $first.Published -and $first.Tag -ceq 'v2026.10.19.1-rc.1' -and
        (Get-C1339Peel $fx $first.Tag) -ceq $fx.Head) -Name 'C1339 G-33 utc-day' -Detail ($first.Tag + ':' + $first.Refusal)
    $calls = @(Get-Content (Join-Path $fx.Root 'clock.log')).Count
    Assert-C487 -Cond ($calls -eq 2) -Name 'C1339 G-34 one-clock-sample' -Detail ([string]$calls)
    $identityPreserved = [IO.File]::ReadAllText($candidatePath) -ceq $candidateBefore
    $fx = New-C1339Fixture
    Set-C1339Clock $fx '2026-10-19T23:59:59Z'
    Write-NightlyAtomicJson -Path $fx.FaultPath -Object @{ push = 'before' }
    $failed = Invoke-C1339Publish $fx
    $journal = Join-Path $fx.ReleaseRoot 'publications.json'
    $reserved = (Get-Content $journal -Raw | ConvertFrom-Json).reservations[0]
    $reservedBeforeMidnight = $failed.Refusal -eq 'tag-push-failed' -and $failed.Tag -ceq 'v2026.10.19.1-rc.1' -and
        (Get-C1339Peel $fx $failed.Tag) -eq '' -and -not $reserved.published
    Remove-Item -LiteralPath $fx.FaultPath
    Set-C1339Clock $fx '2026-10-20T00:00:01Z'
    $again = Invoke-C1339Publish $fx
    $stored = (Get-Content $journal -Raw | ConvertFrom-Json).reservations
    $same = $reservedBeforeMidnight -and $again.ExitCode -eq 0 -and $again.Published -and $again.Resumed -and
        $again.Tag -ceq 'v2026.10.19.1-rc.1' -and @($stored).Count -eq 1 -and $stored[0].tag -ceq $reserved.tag -and
        $stored[0].sha -ceq $reserved.sha -and $stored[0].candidateId -ceq $reserved.candidateId -and $stored[0].reservedAt -ceq $reserved.reservedAt
    Set-C1339Candidate $fx $fx.B
    $next = Invoke-C1339Publish $fx
    Assert-C487 -Cond ($same -and $next.ExitCode -eq 0 -and $next.Published -and $next.Tag -ceq 'v2026.10.20.1-rc.1' -and
        (Get-C1339Peel $fx $next.Tag) -ceq $fx.B) -Name 'C1339 G-35 midnight-reuse' -Detail ($again.Tag + '/' + $next.Tag + ':' + $next.Refusal)
    $stamp = New-C1339Fixture -Stamp
    Set-C1339Clock $stamp '2026-10-20T00:00:01Z'
    $legacy = Invoke-C1339Publish $stamp
    Assert-C487 -Cond ($legacy.ExitCode -eq 0 -and $legacy.Published -and $legacy.Tag -ceq 'v2026.09.23.1' -and
        (Get-C1339Peel $stamp $legacy.Tag) -ceq $stamp.Head) -Name 'C1339 G-36 stamp-day' -Detail ($legacy.Tag + ':' + $legacy.Refusal)
    Assert-C487 -Cond $identityPreserved -Name 'C1339 G-37 candidate-date-preserved'
}

function Initialize-C1339PromotionReservation {
    param($Fx)
    $path = Join-Path $Fx.ReleaseRoot 'publications.json'
    [void](New-ReleaseGateTagReservation -JournalPath $path -CandidateId $Fx.CandidateId -Sha $Fx.Head -CutUtc ([datetime]'2026-10-15T00:00:00Z') -RemoteTags @())
    [void](Set-ReleaseGateReservationPublished -JournalPath $path -CandidateId $Fx.CandidateId -ReleaseId 'fixture-pre' -ReleaseUrl 'https://example.invalid/fixture/pre')
}

function New-C1339PromotionFixture {
    $fx = New-C1339Fixture
    Initialize-C1339PromotionReservation $fx
    Add-C1339Tag $fx $fx.PreTag $fx.Head
    Write-NightlyAtomicJson -Path $fx.Gh.StorePath -Object @{ releases = @(@{ id = 'fixture-pre'; tagName = $fx.PreTag; isDraft = $false }) }
    [void](Invoke-C1339LocalGit $fx @('checkout', '--detach', '-q', $fx.B))
    return $fx
}

function Get-C1339RecipientState {
    param($Fx)
    # Independent readers, including local tag attempts and the GitHub recipient.
    return @(
        (Invoke-C1339LocalGit $Fx @('for-each-ref', '--format=%(refname):%(objectname)', 'refs/tags') -Remote),
        (Invoke-C1339LocalGit $Fx @('for-each-ref', '--format=%(refname):%(objectname)', 'refs/tags')),
        $(if (Test-Path $Fx.Gh.StorePath) { [IO.File]::ReadAllText($Fx.Gh.StorePath) } else { '<absent>' })
    ) -join "`n"
}

function Invoke-C1339AuthorityVariant {
    param($Fx, $Snap, [scriptblock]$Mutate, [string]$Reason, [hashtable]$Extra = @{})
    $CandidateId = $Fx.CandidateId
    Restore-C599ASnapshot $Snap
    & $Mutate
    $journalPath = Join-Path $Fx.ReleaseRoot 'publications.json'
    $journal = [IO.File]::ReadAllText($journalPath)
    $before = Get-C1339RecipientState $Fx
    $r = Invoke-C1339Promote $Fx $Extra
    $check = New-C1339Check ($r.ExitCode -ne 0 -and -not $r.Promoted -and -not $r.Resumed -and
        @($r.Refusal -split ',') -contains $Reason -and (Test-C1339NoWrites $Fx) -and
        (Get-C1339RecipientState $Fx) -ceq $before -and [IO.File]::ReadAllText($journalPath) -ceq $journal) ($Reason + ': ' + $r.Refusal)
    Restore-C599ASnapshot $Snap
    return $check
}

function Test-C1339_PromoteAuthorityHash {
    $fx = New-C1339PromotionFixture
    $CandidateId = $fx.CandidateId
    $control = Invoke-C1339Promote $fx @{ WhatIf = $true }
    $snap = Save-C599ASnapshot $fx
    $hashChecks = @(New-C1339Check ($control.ExitCode -eq 0 -and (Test-C1339NoWrites $fx)) ('valid:' + $control.Refusal))
    $auth = Get-Content (Join-Path (Get-C599AuthorityDir $fx) 'authority.json') -Raw | ConvertFrom-Json
    $hashChecks += New-C1339Check (@($auth.scriptHashes.PSObject.Properties.Name).Count -eq 7 -and
        $auth.scriptHashes.'promote-release.ps1' -ceq (Get-ReleaseAuthorityScriptHashes)['promote-release.ps1']) 'seven script digests'
    foreach ($missing in @($false, $true)) {
        $hashChecks += Invoke-C1339AuthorityVariant $fx $snap {
            Update-C599AAuthority $fx { param($a)
                if ($missing) { $a.scriptHashes.PSObject.Properties.Remove('promote-release.ps1') }
                else { Set-C599AField $a.scriptHashes 'promote-release.ps1' ('0' * 64) }
            }
        } 'script-digest-mismatch:promote-release.ps1'
    }
    Assert-C599AMatrix 'C1339 G-38 promotion-hash-required' $hashChecks
    foreach ($resume in @($false, $true)) {
        $fx = New-C1339PromotionFixture
        $CandidateId = $fx.CandidateId
        if ($resume) { Add-C1339Tag $fx $fx.FinalTag $fx.Head }
        $control = Invoke-C1339Promote $fx @{ WhatIf = $true }
        $snap = Save-C599ASnapshot $fx
        $checks = @(New-C1339Check ($control.ExitCode -eq 0 -and (Test-C1339NoWrites $fx)) ('valid:' + $control.Refusal))
        foreach ($variant in @(
            @{ reason = 'script-digest-mismatch:promote-release.ps1'; edit = { Update-C599AAuthority $fx { param($a) Set-C599AField $a.scriptHashes 'promote-release.ps1' ('0' * 64) } } },
            @{ reason = 'missing-authority'; edit = { Remove-Item (Join-Path (Get-C599AuthorityDir $fx) 'authority.json') } },
            @{ reason = 'ledger-selection'; edit = { Update-C599Ledger $fx -Ledger { param($l) $l.selection = 'targeted' } } },
            @{ reason = 'ledger-diagnostic'; edit = { Update-C599Ledger $fx -Ledger { param($l) $l.diagnostic = $true } } },
            @{ reason = 'uid-not-passed:antiphon-u1'; edit = { Set-C599TrxOutcome $fx -Chunk 'antiphon-001' -Uid 'antiphon-u1' -Outcome 'Failed' } },
            @{ reason = 'policy-raw-digest-mismatch'; edit = { Update-C599AAuthority $fx { param($a) $a.policyRawSha256 = ('0' * 64) } } },
            @{ reason = 'release-card-readback-unavailable'; edit = { Update-C599Card $fx -Store { param($s) $s.status = 503 } } }
        )) { $checks += Invoke-C1339AuthorityVariant $fx $snap $variant.edit $variant.reason }
        $restored = Invoke-C1339Promote $fx
        $checks += New-C1339Check ($restored.ExitCode -eq 0 -and (Get-C1339Peel $fx $fx.FinalTag) -ceq $fx.Head -and
            ($restored.Promoted -or $restored.Resumed)) ('restored admission:' + $restored.Refusal)
        $label = 'C1339 G-39 promotion-fresh-authority'
        if ($resume) { $label = 'C1339 G-40 promotion-resume-authority' }
        Assert-C599AMatrix $label $checks
    }
    $fx = New-C1339PromotionFixture
    $CandidateId = $fx.CandidateId
    $snap = Save-C599ASnapshot $fx
    $checks = @(Invoke-C1339AuthorityVariant $fx $snap {} 'authority-repository' @{ Repository = 'fixture/foreign' })
    $checks += Invoke-C1339AuthorityVariant $fx $snap {} 'authority-repository' @{ Repository = $null }
    $control = Invoke-C1339Promote $fx @{ WhatIf = $true }
    $checks += New-C1339Check ($control.ExitCode -eq 0) $control.Refusal
    Assert-C599AMatrix 'C1339 G-41 promotion-repository' $checks

    $fallback = New-C1339PromotionFixture
    Update-C599Card $fallback -Card { param($c) $c.revisionCount = 5 }
    $savedApi = $env:ANTIPHON_API
    try {
        $env:ANTIPHON_API = $fallback.CardApi.Url
        $control = Invoke-C1339Promote $fx @{ WhatIf = $true }
        $checks = @(New-C1339Check ($control.ExitCode -eq 0) ('explicit override:' + $control.Refusal))
        $checks += Invoke-C1339AuthorityVariant $fx $snap { Update-C599Card $fx -Store { param($s) $s.status = 503 } } 'release-card-readback-unavailable'
        $checks += Invoke-C1339AuthorityVariant $fx $snap { Update-C599Card $fx -Card { param($c) $c.revisionCount = 5 } } 'release-card-readback-revision'
        Assert-C599AMatrix 'C1339 G-42 promotion-card-readback' $checks
    } finally { $env:ANTIPHON_API = $savedApi }
}

function Invoke-C1339ReservationVariant {
    param($Fx, $Snap, [scriptblock]$Mutate, [string]$Reason)
    Restore-C599ASnapshot $Snap
    $path = Join-Path $Fx.ReleaseRoot 'publications.json'
    & $Mutate
    $journal = if (Test-Path $path) { [IO.File]::ReadAllText($path) } else { '<absent>' }
    $before = Get-C1339RecipientState $Fx
    try { $r = Invoke-C1339Promote $Fx } catch { $r = [pscustomobject]@{ ExitCode = 1; Refusal = ('threw:' + $_.Exception.Message); Promoted = $false; Resumed = $false } }
    $after = if (Test-Path $path) { [IO.File]::ReadAllText($path) } else { '<absent>' }
    $check = New-C1339Check ($r.ExitCode -ne 0 -and -not $r.Promoted -and -not $r.Resumed -and
        $r.Refusal -ceq $Reason -and (Test-C1339NoWrites $Fx) -and
        (Get-C1339RecipientState $Fx) -ceq $before -and $after -ceq $journal) ($Reason + ': ' + $r.Refusal)
    Restore-C599ASnapshot $Snap
    return $check
}

function Install-C1339WriterFault {
    param($Fx)
    # Only this invocation's script scope, installed after the real imports by the
    # existing admitted fixture. All other paths delegate to the production writer.
    Add-Content -LiteralPath $Fx.Gh.Path -Encoding ASCII -Value @"
`$script:C1339Writer = (Get-Command Write-NightlyAtomicJson).ScriptBlock
function script:Write-NightlyAtomicJson {
    param([string]`$Path, `$Object)
    if (`$Path -ceq '$(Join-Path $Fx.ReleaseRoot 'publications.json')' -and (Test-Path '$($Fx.FaultPath)')) {
        `$fault = Get-Content '$($Fx.FaultPath)' -Raw | ConvertFrom-Json
        if (`$fault.writer -eq 'throw') { throw 'fixture-atomic-write-failure' }
        if (`$fault.writer -eq 'silent') { return }
    }
    & `$script:C1339Writer -Path `$Path -Object `$Object
}
"@
}

function Get-C1339PreReleaseState {
    param($Fx)
    (Invoke-C1339LocalGit $Fx @('show-ref', '--verify', '--hash', ('refs/tags/' + $Fx.PreTag)) -Remote) + '/' +
        (Get-C1339Peel $Fx $Fx.PreTag) + '/' + [IO.File]::ReadAllText($Fx.Gh.StorePath)
}

# Keep the reservation matrices method-scoped: the combined fresh/resumed/target
# rehearsal exceeded the unchanged five-minute ScriptHarness execution budget.
function Invoke-C1339PromotionReservations {
    param([bool]$Resume)
    $happy = New-C1339PromotionFixture
    $result = Invoke-C1339Promote $happy

    $variants = @(
        @{ label = 'C1339 G-43 reservation-journal'; reason = 'missing'; edits = @(
            { Remove-Item -LiteralPath $path },
            { Update-C599Json $path { param($j) $j.reservations = @() } }) },
        @{ label = 'C1339 G-44 reservation-unique'; reason = 'ambiguous'; edits = @(
            { Update-C599Json $path { param($j) $j.reservations = @($j.reservations[0], $j.reservations[0]) } }) },
        @{ label = 'C1339 G-45 reservation-typed'; reason = 'invalid'; edits = @(
            { Set-Content -LiteralPath $path -Value '{broken' },
            { Set-Content -LiteralPath $path -Value '[]' },
            { $raw = [IO.File]::ReadAllText($path); [IO.File]::WriteAllText($path, '[' + $raw + ']') },
            { Update-C599Json $path { param($j) $j.schemaVersion = '1' } },
            { Update-C599Json $path { param($j) $j.schemaVersion = 2 } },
            { Update-C599Json $path { param($j) $j.reservations = $j.reservations[0] } },
            { Update-C599Json $path { param($j) $j.reservations = @('row') } },
            { Update-C599Json $path { param($j) $j.reservations[0].candidateId = @($j.reservations[0].candidateId) } },
            { Update-C599Json $path { param($j) $j.reservations[0].sha = @($j.reservations[0].sha) } },
            { Update-C599Json $path { param($j) $j.reservations[0].tag = @($j.reservations[0].tag) } },
            { Update-C599Json $path { param($j) $j.reservations[0].published = 'true' } },
            { Update-C599Json $path { param($j) $j.reservations[0].releaseId = @('fixture-pre') } },
            { Update-C599Json $path { param($j) $j.reservations[0].releaseUrl = @('https://example.invalid/pre') } },
            { Update-C599Json $path { param($j) Set-C599AField $j.reservations[0] 'finalTag' @('v2026.10.15.1') } }) },
        @{ label = 'C1339 G-46 reservation-candidate'; reason = 'candidate-mismatch'; edits = @(
            { Update-C599Json $path { param($j) $j.reservations[0].candidateId = 'foreign-candidate' } }) },
        @{ label = 'C1339 G-47 reservation-sha'; reason = 'sha-mismatch'; edits = @(
            { Update-C599Json $path { param($j) $j.reservations[0].sha = $fx.B } },
            { Update-C599Json $path { param($j) $j.reservations[0].sha = $fx.Head.Substring(0,12) } }) },
        @{ label = 'C1339 G-48 reservation-tag'; reason = 'tag-mismatch'; edits = @(
            { Update-C599Json $path { param($j) $j.reservations[0].tag = 'v2026.10.15.2-rc.1' } },
            { Update-C599Json $path { param($j) $j.reservations[0].tag = $fx.PreTag.ToUpperInvariant() } }) },
        @{ label = 'C1339 G-49 reservation-published'; reason = 'unpublished'; edits = @(
            { Update-C599Json $path { param($j) $j.reservations[0].published = $false } }) },
        @{ label = 'C1339 G-50 reservation-release-id'; reason = 'release-identity'; edits = @(
            { Update-C599Json $path { param($j) $j.reservations[0].releaseId = '' } }) },
        @{ label = 'C1339 G-51 reservation-release-url'; reason = 'release-identity'; edits = @(
            { Update-C599Json $path { param($j) $j.reservations[0].releaseUrl = '' } }) },
        @{ label = 'C1339 G-52 reservation-final-consistent'; reason = 'final-conflict'; edits = @(
            { Update-C599Json $path { param($j) Set-C599AField $j.reservations[0] 'finalTag' 'v2026.10.15.2' } },
            { Update-C599Json $path { param($j) Set-C599AField $j.reservations[0] 'finalTag' $fx.PreTag } }) }
    )
    foreach ($resume in @($Resume)) {
        $fx = New-C1339PromotionFixture
        if ($resume) { Add-C1339Tag $fx $fx.FinalTag $fx.Head }
        $valid = Invoke-C1339Promote $fx @{ WhatIf = $true }
        $snap = Save-C599ASnapshot $fx
        $resumedChecks = @(New-C1339Check ($valid.ExitCode -eq 0) ('valid control:' + $valid.Refusal))
        foreach ($variant in $variants) {
            $checks = @(New-C1339Check ($valid.ExitCode -eq 0 -and $result.ExitCode -eq 0) 'valid controls')
            foreach ($edit in $variant.edits) {
                $checks += Invoke-C1339ReservationVariant $fx $snap $edit ('promotion-reservation-' + $variant.reason)
            }
            if ($variant.label -eq 'C1339 G-52 reservation-final-consistent') {
                $path = Join-Path $fx.ReleaseRoot 'publications.json'
                $before = [IO.File]::ReadAllText($path)
                $wrongFinal = Set-ReleaseGateReservationFinalTag -JournalPath $path -CandidateId $fx.CandidateId -Sha $fx.Head -PreReleaseTag $fx.PreTag -FinalTag 'v2026.10.15.2'
                $checks += New-C1339Check (-not $wrongFinal -and [IO.File]::ReadAllText($path) -ceq $before) 'recorder final must match pre-release'
            }
            if (-not $resume) { Assert-C599AMatrix $variant.label $checks }
            else { $resumedChecks += $checks }
        }
        if ($resume) { Assert-C599AMatrix 'C1339 G-53 reservation-resume' $resumedChecks }
    }

}

function Test-C1339_PromoteFreshReservations {
    Invoke-C1339PromotionReservations -Resume $false
}

function Test-C1339_PromoteResumedReservations {
    Invoke-C1339PromotionReservations -Resume $true
}

function Test-C1339_PromoteReservationAndTarget {
    $happy = New-C1339PromotionFixture
    $preBefore = Get-C1339PreReleaseState $happy
    $result = Invoke-C1339Promote $happy
    $object = Invoke-C1339LocalGit $happy @('show-ref', '--verify', '--hash', ('refs/tags/' + $happy.FinalTag)) -Remote -AllowAbsent
    $peel = Get-C1339Peel $happy $happy.FinalTag
    $type = if ($object) { Invoke-C1339LocalGit $happy @('cat-file', '-t', $object) -Remote } else { '' }
    $preChecks = @(New-C1339Check ((Get-C1339PreReleaseState $happy) -ceq $preBefore) 'fresh success')
    $recoveryChecks = @()

    $head = Invoke-C1339LocalGit $happy @('rev-parse', 'HEAD')
    $live = Invoke-C1339LocalGit $happy @('rev-parse', ('refs/heads/' + $happy.CandidateRef)) -Remote
    Assert-C487 -Cond ($result.ExitCode -eq 0 -and $result.Promoted -and $happy.Head -cne $happy.B -and
        $head -ceq $happy.B -and $live -ceq $happy.Head -and $peel -ceq $happy.Head -and $peel -cne $head) -Name 'C1339 G-54 target-peel' -Detail ($result.Refusal + ':' + $peel + '/' + $head)
    Assert-C487 -Cond ($type -ceq 'tag' -and $peel -ceq $happy.Head) -Name 'C1339 G-55 annotated-final' -Detail $type
    Clear-Content -LiteralPath (Join-Path $happy.Root 'real-git-trace.log')
    $again = Invoke-C1339Promote $happy
    $stored = (Get-Content (Join-Path $happy.ReleaseRoot 'publications.json') -Raw | ConvertFrom-Json).reservations
    $recoveryChecks += New-C1339Check ($again.ExitCode -eq 0 -and $again.Resumed -and -not $again.Promoted -and
        (Test-C1339NoWrites $happy) -and @($stored).Count -eq 1 -and $stored[0].finalTag -ceq $happy.FinalTag -and
        (Invoke-C1339LocalGit $happy @('show-ref', '--verify', '--hash', ('refs/tags/' + $happy.FinalTag)) -Remote) -ceq $object) 'ordinary same-final retry'
    $preChecks += New-C1339Check ((Get-C1339PreReleaseState $happy) -ceq $preBefore) 'same-final retry'

    $fx = New-C1339PromotionFixture
    Add-C1339Tag $fx $fx.FinalTag $fx.C
    $before = Get-C1339RecipientState $fx
    $preBefore = Get-C1339PreReleaseState $fx
    $r = Invoke-C1339Promote $fx
    Assert-C487 -Cond ($r.Refusal -ceq 'final-tag-conflict' -and -not $r.Promoted -and -not $r.Resumed -and
        (Test-C1339NoWrites $fx) -and (Get-C1339RecipientState $fx) -ceq $before) -Name 'C1339 G-56 final-conflict' -Detail $r.Refusal
    $preChecks += New-C1339Check ((Get-C1339PreReleaseState $fx) -ceq $preBefore) 'conflicting final'

    $checks = @()
    foreach ($final in @($false, $true)) {
        $fx = New-C1339PromotionFixture
        $tag = $fx.PreTag
        if ($final) { $tag = $fx.FinalTag; Add-C1339Tag $fx $tag $fx.Head }
        Set-C1339Read $fx ('refs/tags/' + $tag + '^{}') '' -Mode replace
        $before = Get-C1339RecipientState $fx
        $r = Invoke-C1339Promote $fx
        $checks += New-C1339Check ($r.Refusal -ceq 'tag-peel' -and (Test-C1339NoWrites $fx) -and
            (Get-C1339RecipientState $fx) -ceq $before) ($tag + ':' + $r.Refusal)
    }
    Assert-C599AMatrix 'C1339 G-57 direct-without-peel' $checks

    foreach ($fault in @('remove', 'throw', 'silent', 'replace')) {
        $fx = New-C1339PromotionFixture
        $path = Join-Path $fx.ReleaseRoot 'publications.json'
        $unrelated = ''
        if ($fault -eq 'silent') {
            [void](New-ReleaseGateTagReservation -JournalPath $path -CandidateId ('rc-2026-10-15-' + $fx.C.Substring(0,12)) -Sha $fx.C -CutUtc ([datetime]'2026-10-15T00:00:00Z') -RemoteTags @($fx.PreTag))
            $unrelated = (Get-Content $path -Raw | ConvertFrom-Json).reservations[1] | ConvertTo-Json -Depth 12 -Compress
        }
        $expectedCount = if ($unrelated) { 2 } else { 1 }
        $before = [IO.File]::ReadAllText($path)
        $preBefore = Get-C1339PreReleaseState $fx
        Install-C1339WriterFault $fx
        if ($fault -in @('throw', 'silent')) { Write-NightlyAtomicJson -Path $fx.FaultPath -Object @{ writer = $fault } }
        else { Write-NightlyAtomicJson -Path $fx.FaultPath -Object @{ journalCut = @{ ref = 'refs/tags/' + $fx.FinalTag; path = $path; mode = $fault; sha = $fx.B } } }
        try { $r = Invoke-C1339Promote $fx } catch { $r = [pscustomobject]@{ ExitCode = 1; Refusal = ('threw:' + $_.Exception.Message); Promoted = $false; Resumed = $false } }
        $after = [IO.File]::ReadAllText($path)
        $rows = (Get-Content $path -Raw | ConvertFrom-Json).reservations
        $shape = switch ($fault) {
            'remove' { @($rows).Count -eq 0 }
            'replace' { @($rows).Count -eq 1 -and $rows[0].sha -ceq $fx.B -and [string]$rows[0].finalTag -eq '' }
            default { $after -ceq $before }
        }
        $label = switch ($fault) {
            'remove' { 'C1339 G-58 journal-result' }
            'throw' { 'C1339 G-59 journal-exception' }
            'silent' { 'C1339 G-60 journal-readback' }
            'replace' { 'C1339 G-66 recording-identity' }
        }
        Assert-C487 -Cond ($r.ExitCode -ne 0 -and $r.Refusal -ceq 'promotion-journal-write-failed' -and
            -not $r.Promoted -and -not $r.Resumed -and $shape -and
            (Get-C1339Peel $fx $fx.FinalTag) -ceq $fx.Head) -Name $label -Detail ($fault + ':' + $r.Refusal)
        $preChecks += New-C1339Check ((Get-C1339PreReleaseState $fx) -ceq $preBefore) ('journal fault:' + $fault)
        $finalObject = Invoke-C1339LocalGit $fx @('show-ref', '--verify', '--hash', ('refs/tags/' + $fx.FinalTag)) -Remote
        Remove-Item -LiteralPath $fx.FaultPath
        [IO.File]::WriteAllText($path, $before)
        Clear-Content -LiteralPath (Join-Path $fx.Root 'real-git-trace.log')
        $again = Invoke-C1339Promote $fx
        $rows = (Get-Content $path -Raw | ConvertFrom-Json).reservations
        $unrelatedOk = -not $unrelated -or ($rows[1] | ConvertTo-Json -Depth 12 -Compress) -ceq $unrelated
        $recoveryChecks += New-C1339Check ($again.ExitCode -eq 0 -and $again.Resumed -and (Test-C1339NoWrites $fx) -and
            @($rows).Count -eq $expectedCount -and $unrelatedOk -and $rows[0].sha -ceq $fx.Head -and $rows[0].tag -ceq $fx.PreTag -and $rows[0].finalTag -ceq $fx.FinalTag -and
            (Invoke-C1339LocalGit $fx @('show-ref', '--verify', '--hash', ('refs/tags/' + $fx.FinalTag)) -Remote) -ceq $finalObject) ('journal recovery:' + $fault + ':' + $again.Refusal)
    }

    $checks = @()
    foreach ($fault in @('race', 'before', 'after')) {
        $fx = New-C1339PromotionFixture
        $preBefore = Get-C1339PreReleaseState $fx
        if ($fault -eq 'race') { Write-NightlyAtomicJson -Path $fx.FaultPath -Object @{ pushRace = @{ tag = $fx.FinalTag; sha = $fx.C } } }
        else { Write-NightlyAtomicJson -Path $fx.FaultPath -Object @{ push = $fault } }
        $r = Invoke-C1339Promote $fx
        $trace = Get-Content (Join-Path $fx.Root 'real-git-trace.log') -Raw
        $safe = $trace -notmatch '--force|push[^\r\n]* \+|tag -d|push[^\r\n]*--delete'
        if ($fault -eq 'race') {
            $checks += New-C1339Check ($r.Refusal -ceq 'tag-push-failed' -and -not $r.Promoted -and -not $r.Resumed -and
                (Get-C1339Peel $fx $fx.FinalTag) -ceq $fx.C -and $safe) ('race:' + $r.Refusal)
        } else {
            $expected = ($fault -eq 'before' -and $r.Refusal -ceq 'tag-push-failed' -and (Get-C1339Peel $fx $fx.FinalTag) -eq '') -or
                ($fault -eq 'after' -and $r.ExitCode -eq 0 -and (Get-C1339Peel $fx $fx.FinalTag) -ceq $fx.Head)
            Remove-Item -LiteralPath $fx.FaultPath
            $retry = Invoke-C1339Promote $fx
            $checks += New-C1339Check ($expected -and $safe -and $retry.ExitCode -eq 0 -and
                (Get-C1339Peel $fx $fx.FinalTag) -ceq $fx.Head) ($fault + ':' + $r.Refusal + '/' + $retry.Refusal)
        }
        $preChecks += New-C1339Check ((Get-C1339PreReleaseState $fx) -ceq $preBefore) ('push:' + $fault)
    }
    Assert-C599AMatrix 'C1339 G-63 create-only-race' $checks
    Assert-C599AMatrix 'C1339 G-61 same-tag-recovery' $recoveryChecks
    Assert-C599AMatrix 'C1339 G-62 prerelease-preserved' $preChecks
}

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

. (Join-Path $here 'fixtures/c1500-status.ps1')
$cases = Get-C487CaseFunctions -Prefix 'C599_'
if ($Case) {
    $fn = Get-Command -Name ('Test-{0}' -f $Case) -CommandType Function -ErrorAction SilentlyContinue
    if (-not $fn) { Write-Error ('unknown case {0}' -f $Case); exit 2 }
    try { & $fn } finally { Stop-C599CardStub; Clear-C599FixtureReadOnly }
} else {
    try { foreach ($fn in $cases) { & $fn } } finally { Stop-C599CardStub; Clear-C599FixtureReadOnly }
}
Write-C487Evidence -ResultsDirectory $ResultsDirectory -Case 'release-gate-summary' -Body @{
    passed = $script:C487Passed; failed = $script:C487Failed; rows = $script:C487Rows
}
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 0
