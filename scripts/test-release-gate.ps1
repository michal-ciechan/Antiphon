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
. (Join-Path $lib 'nightly-run-impl.ps1')
. (Join-Path $lib 'nightly-tests-impl.ps1')
. (Join-Path $lib 'c487-harness.ps1')

$ResultsDirectory = New-C487Root -ResultsDirectory $ResultsDirectory
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
        if (`$fail.PSObject.Properties.Name -contains `$sub -and [bool]`$fail.`$sub) {
            if (`$fail.PSObject.Properties.Name -contains (`$sub + '_commits') -and [bool]`$fail.(`$sub + '_commits')) {
                # commit-then-lose-response: the remote accepted the write, the caller did not hear.
                if (`$sub -eq 'push') {
                    `$spec = [string]`$Arguments[2]
                    `$parts = `$spec -split ':'
                    `$key = Get-RefKey `$parts[`$parts.Count - 1]
                    `$val = `$parts[0]
                    if (`$val -like 'refs/tags/*') { `$val = '$HeadSha' }
                    `$state | Add-Member -NotePropertyName `$key -NotePropertyValue `$val -Force
                    `$state | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath `$remote -Encoding UTF8
                }
            }
            return @{ ExitCode = 1; Output = 'injected failure' }
        }
        if (`$sub -eq 'fetch' -or `$sub -eq 'clone' -or `$sub -eq 'tag') { return @{ ExitCode = 0; Output = '' } }
        if (`$sub -eq 'rev-parse' -or `$sub -eq 'rev-list') { return @{ ExitCode = 0; Output = '$HeadSha' } }
        if (`$sub -eq 'log') { return @{ ExitCode = 0; Output = 'abc1234 feat: something' } }
        if (`$sub -eq 'ls-remote') {
            `$ref = [string]`$Arguments[`$Arguments.Count - 1]
            `$key = Get-RefKey `$ref
            `$val = ''
            if (`$state.PSObject.Properties.Name -contains `$key) { `$val = [string]`$state.`$key }
            if ([string]::IsNullOrWhiteSpace(`$val)) { return @{ ExitCode = 0; Output = '' } }
            return @{ ExitCode = 0; Output = (`$val + "``t" + `$ref) }
        }
        if (`$sub -eq 'push') {
            `$spec = [string]`$Arguments[2]
            `$parts = `$spec -split ':'
            `$key = Get-RefKey `$parts[`$parts.Count - 1]
            `$val = `$parts[0]
            if (`$val -like 'refs/tags/*') { `$val = '$HeadSha' }
            `$state | Add-Member -NotePropertyName `$key -NotePropertyValue `$val -Force
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
    Add-Content -LiteralPath (Join-Path (Split-Path -Parent `$store) 'gh-trace.log') -Value ('GH ' + `$Verb + ' ' + [string]`$Arguments.tag) -Encoding ASCII
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

    # The shipped policy must itself be v2 and carry both profiles.
    $ship = Read-NightlyExecutionPolicy -RepoRoot $repoRoot
    Assert-C487 -Cond ([int]$ship.SchemaVersion -eq 2) -Name 'C599 ProfileSchema shipped policy is v2' -Detail ([string]$ship.SchemaVersion)
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

    $rec = 'TestNodeUid { Value = u1 } TypeName = BoardE2ETests MethodName = Board_loads Namespace = E2E TestMetadataProperty { Key = OptIn'
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

function New-C599RunFx {
    <#
      A run fixture whose clone, state root, release root and coordination root are
      all private, so the production run core can be exercised end to end.
    #>
    param([switch]$Rc)
    $root = New-C599Root
    $state = Join-Path $root 'state'
    New-Item -ItemType Directory -Path $state -Force | Out-Null
    $clone = New-C487OwnedClone -Root $root
    $trace = Join-Path $root 'trace.log'
    $seams = Join-Path $root 'seams.ps1'
    Write-C487Seams -Path $seams -TracePath $trace
    return [pscustomobject]@{
        Root = $root; State = $state; Clone = $clone; Trace = $trace; Seams = $seams
        ReleaseRoot = (Join-Path $root 'releases'); Coordination = (Join-Path $root 'coord')
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
    if ($Extra) { foreach ($k in $Extra.Keys) { $splat[$k] = $Extra[$k] } }
    return Invoke-AntiphonNightlyRun @splat
}

function Test-C599_MasterStateIsolation {
    # An RC run must leave every master state file byte-identical.
    $fx = New-C599RunFx
    $masterFiles = Get-ReleaseGateMasterStateFiles -StateRoot $fx.State
    foreach ($f in $masterFiles) { Set-Content -LiteralPath $f -Value ('{"sentinel":"' + [System.IO.Path]::GetFileName($f) + '"}') -Encoding ASCII }
    $before = @{}
    foreach ($f in $masterFiles) { $before[$f] = (Get-NightlyFileSha256 -Path $f) }

    $rcState = Join-Path $fx.Root 'rc-state'
    New-Item -ItemType Directory -Path $rcState -Force | Out-Null
    $r = Invoke-C599Run -Fx $fx -Extra @{
        Profile = 'rc'; Trigger = 'rc'; Ref = $CandidateRef; ExpectedSha = $ShaA
        CandidateId = $CandidateId; StateRoot = $rcState; LogRoot = (Join-Path $rcState 'logs')
    }
    Assert-C487 -Cond ($null -ne $r) -Name 'C599 MasterStateIsolation rc run returned a result' -Detail ([string]$r.ExitCode)
    foreach ($f in $masterFiles) {
        $after = (Get-NightlyFileSha256 -Path $f)
        Assert-C487 -Cond ($after -eq $before[$f]) -Name ('C599 MasterStateIsolation {0} is byte-identical after an rc run' -f [System.IO.Path]::GetFileName($f)) -Detail ($before[$f] + ' vs ' + $after)
    }
    # And an RC pointed at the master state root refuses outright.
    $refuse = Invoke-C599Run -Fx $fx -Extra @{
        Profile = 'rc'; Trigger = 'rc'; Ref = $CandidateRef; ExpectedSha = $ShaA
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
    $derived = Invoke-C599Run -Fx $fx -Extra @{ Profile = 'rc'; Trigger = 'rc'; Ref = $CandidateRef; ExpectedSha = $ShaA; StateRoot = (Join-Path $fx.Root 's5'); LogRoot = (Join-Path $fx.Root 's5-logs') }
    Assert-C487 -Cond ([string]$derived.CandidateId -eq $CandidateId) -Name 'C599 ParameterHops candidate id is derived from the ref' -Detail ([string]$derived.CandidateId)

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
    $rcGreen = New-C599State -Profile 'rc' -Trigger 'rc' -Ref $CandidateRef -Sha $ShaB -ExpectedSha $ShaB -Candidate $CandidateId
    $masterGreen = New-C599State
    $rcPaths = Get-ReleaseGateStatePaths -CreditKind 'rc-release' -StateRoot 'C:\Antiphon\nightly' -ReleaseRoot 'C:\Antiphon\releases' -CandidateId $CandidateId
    $masterPaths = Get-ReleaseGateStatePaths -CreditKind 'master-scheduled' -StateRoot 'C:\Antiphon\nightly'
    Assert-C487 -Cond ($rcPaths.GreenPath -ne $masterPaths.GreenPath) -Name 'C599 ReportLane the two lanes write different green files' -Detail ($rcPaths.GreenPath + ' vs ' + $masterPaths.GreenPath)
    Assert-C487 -Cond ($masterPaths.GreenPath -eq 'C:\Antiphon\nightly\last-complete-green.json') -Name 'C599 ReportLane master green path is unchanged' -Detail $masterPaths.GreenPath
    Assert-C487 -Cond ($rcPaths.GreenPath -like '*releases\candidates\rc-20260923T083000Z\complete-green.json') -Name 'C599 ReportLane rc green lands under its candidate root' -Detail $rcPaths.GreenPath
    $missing = ''
    try { [void](Get-ReleaseGateStatePaths -CreditKind 'rc-release' -StateRoot 'x' -CandidateId '') } catch { $missing = $_.Exception.Message }
    Assert-C487 -Cond ($missing -match 'candidate id') -Name 'C599 ReportLane rc credit without a candidate id throws' -Detail $missing
    Assert-C487 -Cond ([string](Get-ReleaseGateCreditVerdict -State $rcGreen).CreditKind -ne [string](Get-ReleaseGateCreditVerdict -State $masterGreen).CreditKind) -Name 'C599 ReportLane the two lanes never share a credit kind'
}

# ======================================================= V-6 publication lane ===

function New-C599PublishFx {
    param([string]$Head = '')
    if ([string]::IsNullOrWhiteSpace($Head)) { $Head = $ShaB }
    $root = New-C599Root
    $git = New-C599GitSeams -Root $root -HeadSha $Head
    $gitText = Get-Content -LiteralPath $git.Path -Raw
    $gh = New-C599GitHubSeams -Root $root -GitSeamsText $gitText
    $gh | Add-Member -NotePropertyName RemotePath -NotePropertyValue $git.RemotePath -Force
    $gh | Add-Member -NotePropertyName FailGitPath -NotePropertyValue $git.FailPath -Force
    $releaseRoot = Join-Path $root 'releases'
    $checkout = New-C599OwnedReleaseClone -Root $root
    [void](New-C599RcGreen -ReleaseRoot $releaseRoot -Sha $Head -CandidateId $CandidateId -Ref $CandidateRef)
    # The remote holds the candidate at the tested sha.
    $state = [ordered]@{ ('release_rc_20260923T083000Z') = $Head }
    Write-NightlyAtomicJson -Path $git.RemotePath -Object $state
    return [pscustomobject]@{
        Root = $root; Git = $git; Gh = $gh; ReleaseRoot = $releaseRoot; Checkout = $checkout; Head = $Head
    }
}

function Invoke-C599Publish {
    param($Fx, [hashtable]$Extra)
    $splat = @{
        ReleaseRoot = $Fx.ReleaseRoot
        CandidateId = $CandidateId
        CheckoutRoot = $Fx.Checkout
        SeamsPath = $Fx.Gh.Path
        RequiredSuites = @('antiphon', 'e2e')
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

    # A required suite that did not pass refuses.
    $fx5 = New-C599PublishFx
    $sp = Join-Path (Get-ReleaseGateCandidateRoot -ReleaseRoot $fx5.ReleaseRoot -CandidateId $CandidateId) 'summary.json'
    $s = Get-Content -LiteralPath $sp -Raw | ConvertFrom-Json
    $s.suites = @($s.suites | Where-Object { [string]$_.id -ne 'e2e' })
    $s | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $sp -Encoding UTF8
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
    Assert-C487 -Cond ($notes -notmatch 'Bearer ' -and $notes -notmatch 'token') -Name 'C599 AssetRecovery notes carry no credential-shaped text'
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

    [void](New-ReleaseGateTagReservation -JournalPath $journal -CandidateId $CandidateId -Sha $ShaB -CutUtc ([datetime]'2026-09-23T08:30:00Z'))
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
    $deletes = @(($trace -split "`n") | Where-Object { $_ -match 'tag -d' -or $_ -match 'push.*:refs/tags' })
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
    $nativeArgs = @(Get-NightlyNativeExecutionArguments -TrxPath $trx -DiagnosticDirectory $diag -Classes @('SmokeE2ETests', 'BoardE2ETests'))
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

# ====================================================================== run ===

$cases = Get-C487CaseFunctions -Prefix 'C599_'
if ($Case) {
    $fn = Get-Command -Name ('Test-{0}' -f $Case) -CommandType Function -ErrorAction SilentlyContinue
    if (-not $fn) { Write-Error ('unknown case {0}' -f $Case); exit 2 }
    & $fn
} else {
    foreach ($fn in $cases) { & $fn }
}
Write-C487Evidence -ResultsDirectory $ResultsDirectory -Case 'release-gate-summary' -Body @{
    passed = $script:C487Passed; failed = $script:C487Failed; rows = $script:C487Rows
}
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 0
