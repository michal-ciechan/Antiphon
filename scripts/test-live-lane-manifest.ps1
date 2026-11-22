# CARD-1545 S1a live-lane manifest driver. ASCII-only.
# Diagnostic inventory only. No provider call, no live Claude, no policy edit.
# The gate scan is compiled (W2-nightly S23, scripts/lib/Antiphon.Nightly/LiveLaneGateScan.cs): its cases left
# this driver. LiveLaneInventoryTests.EveryLiveGateHasDisposition runs the gate matrix in-process, and
# "scripts/antiphon live-lane-scan-gates" rewrites the gates of tests/live-lanes.json (the old ScanGates case).
# The discovery, rosters, exports, receipts and scratch are compiled too (W2-nightly S24): ExactCoreRoster and
# ExactPtyCoreRoster run in-process (LiveLaneInventoryTests, LiveClaudeInventoryTests, or "scripts/antiphon
# live-lane-roster" for a named assembly); ExtraArgumentDecoyFirst, ExportPending, ProducerReceipt, ScratchResidue,
# BindManifest and WriteFixture are LiveLaneDiscoveryTests, and the last two are also "scripts/antiphon
# live-lane-bind-manifest" and "live-lane-write-fixture". What is left is the census (S25): CensusAdditiveIdentity,
# CensusTargetScope, FullCensusUnchanged, EmitPolicyHash (its policy-hash child) and Group (the S1a commission).
param(
    [string]$Case = '',
    [string]$RepoRoot = '',
    [string]$Group = '',
    [string]$ToolDll = '',
    [string]$CheckpointManifest = '',
    [string]$ExpectedSourceSha = '',
    [string]$CensusBaseSha = '',
    [string]$PolicyRoot = '',
    [string]$PolicyPath = ''
)

$ErrorActionPreference = 'Stop'

function Get-LiveLaneScriptRepo {
    param([string]$Start)
    if (-not [string]::IsNullOrWhiteSpace($Start)) {
        $candidate = $Start
        if (Test-Path -LiteralPath (Join-Path $candidate 'Antiphon.sln')) { return (Resolve-Path -LiteralPath $candidate).Path }
    }
    $dir = Split-Path -Parent $PSCommandPath
    $dir = Split-Path -Parent $dir
    if (Test-Path -LiteralPath (Join-Path $dir 'Antiphon.sln')) { return $dir }
    throw 'repo-root-missing'
}

function ConvertTo-LiveLaneJsonString {
    param([string]$Text)
    if ($null -eq $Text) { $Text = '' }
    $escaped = $Text.Replace('\', '\\').Replace('"', '\"').Replace("`r", '\r').Replace("`n", '\n').Replace("`t", '\t')
    return '"' + $escaped + '"'
}

function Write-LiveLaneCaseResult {
    param([bool]$DiscoveryOk, [string]$ErrorText, [string[]]$Failures, [int]$LaunchCount, [int]$DiscoveryMs = -1, [int]$ExportMs = -1, [int]$MatrixMs = -1)
    $items = @()
    foreach ($item in @($Failures)) {
        if ($null -eq $item) { continue }
        $text = [string]$item
        if ([string]::IsNullOrWhiteSpace($text)) { continue }
        $items += (ConvertTo-LiveLaneJsonString $text)
    }
    $failJson = '[' + ($items -join ',') + ']'
    $flag = $(if ($DiscoveryOk) { 'true' } else { 'false' })
    $line = '{"discoveryOk":' + $flag + ',"error":' + (ConvertTo-LiveLaneJsonString $ErrorText) + ',"failures":' + $failJson + ',"launchCount":' + $LaunchCount + ',"discoveryMs":' + $DiscoveryMs + ',"exportMs":' + $ExportMs + ',"matrixMs":' + $MatrixMs + '}'
    [Console]::Out.WriteLine($line)
}

if ($Case -eq 'EmitPolicyHash') {
    if ([string]::IsNullOrWhiteSpace($PolicyRoot)) { exit 2 }
    $policyLib = Join-Path $PolicyRoot 'scripts/lib'
    . (Join-Path $policyLib 'nightly-common.ps1')
    . (Join-Path $policyLib 'nightly-core.ps1'); $null = Test-NightlyCoreAvailable
    $hashPath = $PolicyPath
    if ([string]::IsNullOrWhiteSpace($hashPath)) { $hashPath = Join-Path $PolicyRoot 'tests/test-execution-policy.json' }
    [Console]::Out.WriteLine(([Antiphon.Nightly.NightlyPolicy]::HashText([System.IO.File]::ReadAllText($hashPath))))
    exit 0
}

$RepoRoot = Get-LiveLaneScriptRepo -Start $RepoRoot
. (Join-Path $RepoRoot 'scripts/lib/live-lane-manifest.ps1')

function Write-LiveLaneGroupContext {
    param([string]$Head, [string]$CheckpointResults)
    $captureRoot = Join-Path $RepoRoot '.antiphon/live-lane-capture'
    New-Item -ItemType Directory -Path $captureRoot -Force | Out-Null
    $runId = [guid]::NewGuid().ToString('N')
    $mode = 'baseline'
    $exportRequired = $false
    $reason = 'baseline-no-producer'
    $census = ''
    $fixtureBlob = ''
    $provenance = $null
    if ($Group -eq 'S1a') {
        if ($CensusBaseSha -notmatch '^[0-9a-f]{40}$') { throw 'census-base-required' }
        $mode = 'candidate'
        $exportRequired = $true
        $reason = 'candidate'
        $census = $CensusBaseSha.ToLowerInvariant()
        $spec = $census + ':' + $script:LiveLaneFixtureRel
        $blob = Get-LiveLaneGit -RepoRoot $RepoRoot -GitArgs @('rev-parse', $spec)
        if ($blob.Exit -ne 0 -or [string]::IsNullOrWhiteSpace($blob.Text)) { throw 'fixture-binding' }
        $fixtureBlob = $blob.Text.Trim()
        $fixtureScratch = New-LiveLaneOwnedScratch -RepoRoot $RepoRoot -Leaf ('fixture' + $runId)
        $copy = Join-Path $fixtureScratch 'fixture.json'
        try {
            Copy-LiveLaneGitBlob -RepoRoot $RepoRoot -Spec $spec -Dest $copy
            $fixture = [System.IO.File]::ReadAllText($copy) | ConvertFrom-Json
        } finally {
            Remove-LiveLaneOwnedScratch -Entry @{ Path = $fixtureScratch; Kind = 'dir'; Sha = '' }
        }
        $recorded = [string](Get-LiveLaneProp (Get-LiveLaneProp $fixture 'producing') 'sourceSha')
        $provenance = [ordered]@{}
        foreach ($projectRow in @(ConvertTo-LiveLaneList (Get-LiveLaneProp (Get-LiveLaneProp $fixture 'producing') 'projects'))) {
            $name = [string](Get-LiveLaneProp $projectRow 'project')
            $provenance[$name] = [ordered]@{
                sourceSha = $recorded
                assemblyHash = [string](Get-LiveLaneProp $projectRow 'assemblyHash')
            }
        }
    } elseif ($Group -ne 'S1a-baseline') {
        throw 'unknown-group'
    }
    $contextPath = Join-Path $captureRoot 'context.json'
    $doc = [ordered]@{
        mode = $mode
        exportRequired = $exportRequired
        reason = $reason
        censusBaseSha = $census
        sourceSha = $Head
        preS1BaseSha = $script:LiveLanePreS1BaseSha
        runId = $runId
        candidateRoot = $RepoRoot
        cp25Export = (Join-Path $captureRoot 'cp25-export.json')
        cp1Export = (Join-Path $captureRoot 'cp1-export.json')
        fixturePath = $script:LiveLaneFixtureRel
        fixtureBlob = $fixtureBlob
        landingTarget = $script:LiveLaneLandingTargetRef
        producerProvenance = $provenance
        protectedPaths = @($script:LiveLaneProtectedPaths)
        allowedSeedPaths = @($script:LiveLaneAllowedSeedPaths)
        checkpointResults = $CheckpointResults
    }
    $json = $doc | ConvertTo-Json -Depth 8
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($contextPath, $json, $utf8)
    return (Resolve-Path -LiteralPath $contextPath).Path
}

# CARD-1645 item 2. Every declared additive identity must appear in the after set, and an
# after-only UID is declared only on project, namespace, class and method together.
function New-LiveLaneCensusNode {
    param([string]$Project, [string]$Namespace, [string]$Type, [string]$Method)
    return [ordered]@{
        project = $Project
        uid = ($Namespace + '.' + $Type + '.' + $Method + '.' + $Project)
        namespace = $Namespace
        type = $Type
        method = $Method
        arguments = @()
        categories = @('Unit')
    }
}

function Write-LiveLaneCensusNodes {
    param([string]$Path, $Nodes)
    $json = [ordered]@{ nodes = @($Nodes) } | ConvertTo-Json -Depth 6
    [System.IO.File]::WriteAllText($Path, $json, (New-Object System.Text.UTF8Encoding $false))
}

function Invoke-LiveLaneAdditiveIdentityWitness {
    param([string]$RepoRoot)
    $problems = New-Object System.Collections.Generic.List[string]
    $dir = New-LiveLaneOwnedScratch -RepoRoot $RepoRoot -Leaf ('additive' + [guid]::NewGuid().ToString('N'))
    try {
        $old = New-LiveLaneCensusNode 'Antiphon.Tests' 'Antiphon.Tests.Scripts' 'C1645OldTests' 'Kept'
        $declared = [pscustomobject]@{ project = 'Antiphon.Tests'; namespace = 'Antiphon.Tests.Scripts'; class = 'C1645NewTests'; method = 'Added'; arguments = @() }
        $shapes = [ordered]@{
            present = @($old, (New-LiveLaneCensusNode 'Antiphon.Tests' 'Antiphon.Tests.Scripts' 'C1645NewTests' 'Added'))
            removed = @($old)
            otherNamespace = @($old, (New-LiveLaneCensusNode 'Antiphon.Tests' 'Antiphon.Tests.Elsewhere' 'C1645NewTests' 'Added'))
            otherProject = @($old, (New-LiveLaneCensusNode 'Antiphon.Agents.Pty.Tests' 'Antiphon.Tests.Scripts' 'C1645NewTests' 'Added'))
        }
        $beforePath = Join-Path $dir 'before.json'
        Write-LiveLaneCensusNodes -Path $beforePath -Nodes @($old)
        $additive = ConvertTo-LiveLaneIdentityJson @($declared)
        $contract = ConvertTo-LiveLaneIdentityJson (Get-LiveLaneContract)
        Initialize-LiveLaneCensusEngine
        $seen = @{}
        foreach ($name in @($shapes.Keys)) {
            $path = Join-Path $dir ($name + '.json')
            Write-LiveLaneCensusNodes -Path $path -Nodes $shapes[$name]
            $text = [string][LiveLaneCensusEngine]::Compare($beforePath, $path, '', '[]', '[]', $additive, $contract, '')
            $seen[$name] = @(($text -split "`n") | ForEach-Object { $_.Trim() } | Where-Object { $_ })
        }
        if ($seen.present -contains 'declared-additive-missing' -or $seen.present -contains 'undeclared-new-uid') {
            $problems.Add('C1645 D-2/present declared addition diagnosed: ' + ($seen.present -join ','))
        }
        if ($seen.removed -notcontains 'declared-additive-missing') {
            $problems.Add('C1645 D-2/removed declared addition not diagnosed: ' + ($seen.removed -join ','))
        }
        foreach ($name in @('otherNamespace', 'otherProject')) {
            if ($seen[$name] -notcontains 'undeclared-new-uid') { $problems.Add('C1645 D-2/' + $name + ' class+method match counted as declared: ' + ($seen[$name] -join ',')) }
            if ($seen[$name] -notcontains 'declared-additive-missing') { $problems.Add('C1645 D-2/' + $name + ' declared identity counted present: ' + ($seen[$name] -join ',')) }
        }
    } finally {
        Remove-LiveLaneOwnedScratch -Entry @{ Path = $dir; Kind = 'dir'; Sha = '' }
    }
    return @($problems)
}

# CARD-1645 items 1 and 4. The pre-S1 base must be on the fetched landing target, and a
# candidate that already carries target commits beyond the seed is a later tree.
function Invoke-LiveLaneScratchGit {
    param([string]$Root, [string[]]$GitArgs)
    $out = & git -C $Root -c user.name=c1645 -c user.email=c1645@example.invalid -c commit.gpgsign=false @GitArgs 2>&1
    if ($LASTEXITCODE -ne 0) { throw ('scratch-git ' + ($GitArgs -join ' ') + ': ' + ($out | Out-String)) }
    return (($out | Out-String).Trim())
}

function New-LiveLaneScratchCommit {
    param([string]$Root, [string]$File, [string]$Message)
    [System.IO.File]::WriteAllText((Join-Path $Root $File), $Message + "`n")
    Invoke-LiveLaneScratchGit $Root @('add', '--', $File) | Out-Null
    Invoke-LiveLaneScratchGit $Root @('commit', '-q', '-m', $Message) | Out-Null
    return (Invoke-LiveLaneScratchGit $Root @('rev-parse', 'HEAD'))
}

function Invoke-LiveLaneTargetScopeWitness {
    param([string]$RepoRoot)
    $problems = New-Object System.Collections.Generic.List[string]
    $dir = New-LiveLaneOwnedScratch -RepoRoot $RepoRoot -Leaf ('target' + [guid]::NewGuid().ToString('N'))
    try {
        Invoke-LiveLaneScratchGit $dir @('init', '-q', '--template=', '-b', 'master') | Out-Null
        New-LiveLaneScratchCommit $dir 'a.txt' 'base' | Out-Null
        $pre = New-LiveLaneScratchCommit $dir 'a.txt' 'pre'
        Invoke-LiveLaneScratchGit $dir @('checkout', '-q', '-b', 'subject') | Out-Null
        $seed = New-LiveLaneScratchCommit $dir 'b.txt' 'seed'
        $head = New-LiveLaneScratchCommit $dir 'c.txt' 'subject'
        Invoke-LiveLaneScratchGit $dir @('checkout', '-q', '-b', 'offtarget', $pre) | Out-Null
        $offPre = New-LiveLaneScratchCommit $dir 'd.txt' 'caller-chosen pre'
        $offSeed = New-LiveLaneScratchCommit $dir 'e.txt' 'caller-chosen seed'
        $offHead = New-LiveLaneScratchCommit $dir 'f.txt' 'caller-chosen head'
        Invoke-LiveLaneScratchGit $dir @('checkout', '-q', 'master') | Out-Null
        $later = New-LiveLaneScratchCommit $dir 'g.txt' 'later master'
        Invoke-LiveLaneScratchGit $dir @('update-ref', 'refs/remotes/origin/master', $later) | Out-Null
        Invoke-LiveLaneScratchGit $dir @('checkout', '-q', 'subject') | Out-Null
        Invoke-LiveLaneScratchGit $dir @('merge', '-q', '--no-edit', '--no-ff', 'master') | Out-Null
        $merged = Invoke-LiveLaneScratchGit $dir @('rev-parse', 'HEAD')
        # The reviewed subject after it lands is not a later tree: a no-ff merge keeps its
        # commits off the target first-parent line, a fast-forward keeps them on its own.
        Invoke-LiveLaneScratchGit $dir @('checkout', '-q', '-b', 'landedmerge', $later) | Out-Null
        Invoke-LiveLaneScratchGit $dir @('merge', '-q', '--no-edit', '--no-ff', $head) | Out-Null
        Invoke-LiveLaneScratchGit $dir @('update-ref', 'refs/remotes/origin/landedmerge', 'HEAD') | Out-Null
        Invoke-LiveLaneScratchGit $dir @('update-ref', 'refs/remotes/origin/landedff', $head) | Out-Null
        # The D-4 tree (subject merged with later master) stays a later tree after a no-ff land.
        Invoke-LiveLaneScratchGit $dir @('checkout', '-q', '-b', 'landedlater', $later) | Out-Null
        Invoke-LiveLaneScratchGit $dir @('merge', '-q', '--no-edit', '--no-ff', $merged) | Out-Null
        Invoke-LiveLaneScratchGit $dir @('update-ref', 'refs/remotes/origin/landedlater', 'HEAD') | Out-Null
        # A candidate cut from the target after the seed carries the seed only through a merge.
        Invoke-LiveLaneScratchGit $dir @('checkout', '-q', '-b', 'forkedlater', $later) | Out-Null
        Invoke-LiveLaneScratchGit $dir @('merge', '-q', '--no-edit', '--no-ff', $seed) | Out-Null
        $forked = New-LiveLaneScratchCommit $dir 'h.txt' 'forked candidate'
        $target = 'refs/remotes/origin/master'
        $cases = [ordered]@{
            honest = @{ Pre = $pre; Seed = $seed; Head = $head; Target = $target; Want = @(); Forbid = @('pre-not-on-target', 'later-tree', 'target-unresolved') }
            offTarget = @{ Pre = $offPre; Seed = $offSeed; Head = $offHead; Target = $target; Want = @('pre-not-on-target'); Forbid = @() }
            laterTree = @{ Pre = $pre; Seed = $seed; Head = $merged; Target = $target; Want = @('later-tree'); Forbid = @('pre-not-on-target') }
            landedMerge = @{ Pre = $pre; Seed = $seed; Head = $head; Target = 'refs/remotes/origin/landedmerge'; Want = @(); Forbid = @('later-tree', 'pre-not-on-target') }
            landedFastForward = @{ Pre = $pre; Seed = $seed; Head = $head; Target = 'refs/remotes/origin/landedff'; Want = @(); Forbid = @('later-tree', 'pre-not-on-target') }
            forkedLater = @{ Pre = $pre; Seed = $seed; Head = $forked; Target = $target; Want = @('later-tree'); Forbid = @('pre-not-on-target') }
            landedLaterMerge = @{ Pre = $pre; Seed = $seed; Head = $merged; Target = 'refs/remotes/origin/landedlater'; Want = @('later-tree'); Forbid = @('pre-not-on-target') }
            noTarget = @{ Pre = $pre; Seed = $seed; Head = $head; Target = 'refs/remotes/origin/c1645-absent'; Want = @('target-unresolved'); Forbid = @() }
        }
        foreach ($name in @($cases.Keys)) {
            $c = $cases[$name]
            $got = @(Get-LiveLaneTargetDiagnostics -RepoRoot $dir -Pre $c.Pre -Seed $c.Seed -Head $c.Head -TargetRef $c.Target)
            foreach ($want in @($c.Want)) { if ($got -notcontains $want) { $problems.Add('C1645 ' + $name + ' missing ' + $want + ': ' + ($got -join ',')) } }
            foreach ($bad in @($c.Forbid)) { if ($got -contains $bad) { $problems.Add('C1645 ' + $name + ' unexpected ' + $bad + ': ' + ($got -join ',')) } }
        }
        $context = [pscustomobject]@{ censusBaseSha = $seed; preS1BaseSha = $pre; sourceSha = $head; fixtureBlob = ('ab' * 20); landingTarget = $target }
        $line = [string](Get-LiveLaneCensusScopeLine -Context $context -TargetSha $later)
        foreach ($needle in @('scope=commission', 'not-run-on-later-trees', ('seed=' + $seed), ('pre=' + $pre), ('head=' + $head), ('fixtureBlob=' + ('ab' * 20)), 'fixture=unauthenticated', ('target=' + $target + '@' + $later))) {
            if ($line.IndexOf($needle, [StringComparison]::Ordinal) -lt 0) { $problems.Add('C1645 D-1/D-4 scope line lacks ' + $needle + ': ' + $line) }
        }
    } finally {
        Remove-LiveLaneOwnedScratch -Entry @{ Path = $dir; Kind = 'dir'; Sha = '' }
    }
    return @($problems)
}

switch ($Case) {
    'CensusAdditiveIdentity' {
        $failures = @(Invoke-LiveLaneAdditiveIdentityWitness -RepoRoot $RepoRoot)
        Write-LiveLaneCaseResult -DiscoveryOk $true -ErrorText '' -Failures $failures -LaunchCount 0
        exit 0
    }
    'CensusTargetScope' {
        $failures = @(Invoke-LiveLaneTargetScopeWitness -RepoRoot $RepoRoot)
        Write-LiveLaneCaseResult -DiscoveryOk $true -ErrorText '' -Failures $failures -LaunchCount 0
        exit 0
    }
    'FullCensusUnchanged' {
        # The witnesses are compiled (W2-nightly S24). The producer receipt's census check (census-skipped-receipt)
        # runs here, over the PowerShell census, until the census is compiled too (S25).
        $receiptProbe = [Func[string, string, string, bool]]{
            param($Repo, $Sha, $Red)
            $wired = Invoke-LiveLaneCensus -Context ([pscustomobject]@{ candidateRoot = $Repo; sourceSha = $Sha; checkpointResults = $Red; censusBaseSha = '' })
            return [bool](Test-LiveLaneCensusHas $wired @('producer-row-not-green'))
        }
        $witness = New-Object System.Collections.Generic.List[string]
        foreach ($item in [Antiphon.Nightly.LiveLaneExports]::ExportPendingWitness($RepoRoot)) { $witness.Add([string]$item) }
        if ($witness.Count -eq 0) { foreach ($item in [Antiphon.Nightly.LiveLaneReceipt]::ProducerReceiptWitness($RepoRoot, $receiptProbe)) { $witness.Add([string]$item) } }
        if ($witness.Count -eq 0) {
            foreach ($item in [Antiphon.Nightly.LiveLaneScratch]::ScratchResidueWitness($RepoRoot, $script:LiveLaneFixtureRel, [string[]]@($script:LiveLaneProtectedPaths))) { $witness.Add([string]$item) }
        }
        if ($witness.Count -gt 0) {
            foreach ($item in $witness) { [Console]::Out.WriteLine($item) }
            exit 1
        }
        $beforeResidue = @(Get-LiveLaneTempResidue)
        $collected = @(Invoke-LiveLaneFullCensus -RepoRoot $RepoRoot)
        $exitCode = 1
        $lines = @()
        if ($collected.Count -gt 0) {
            $last = $collected[-1]
            $parsed = 0
            if ($last -is [int] -or [int]::TryParse([string]$last, [ref]$parsed)) {
                $exitCode = [int]$last
                if ($collected.Count -gt 1) { $lines = @($collected[0..($collected.Count - 2)]) }
            } else {
                $lines = @($collected)
            }
        }
        foreach ($line in @($lines)) { [Console]::Out.WriteLine([string]$line) }
        foreach ($name in @(Get-LiveLaneTempResidue)) {
            $seen = $false
            foreach ($old in @($beforeResidue)) { if ($old -eq $name) { $seen = $true } }
            if (-not $seen) {
                [Console]::Out.WriteLine('tmp-residue')
                exit 1
            }
        }
        exit $exitCode
    }
    'Group' {
        if ([string]::IsNullOrWhiteSpace($ToolDll) -or -not (Test-Path -LiteralPath $ToolDll)) { throw 'tool-dll-missing' }
        $manifestPath = $CheckpointManifest
        if ([string]::IsNullOrWhiteSpace($manifestPath)) { $manifestPath = Join-Path $RepoRoot '.antiphon/s1a-sanitization-checkpoints.yaml' }
        $head = (Get-LiveLaneGit -RepoRoot $RepoRoot -GitArgs @('rev-parse', 'HEAD')).Text.Trim()
        $expected = $ExpectedSourceSha
        if ([string]::IsNullOrWhiteSpace($expected)) { $expected = $head }
        # CARD-1645: the census checks the pre-S1 base against the fetched landing target.
        if ($Group -eq 'S1a') { & git -C $RepoRoot fetch -q origin master }
        $results = Join-Path $RepoRoot ('.antiphon/live-lane-runs/' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $results -Force | Out-Null
        $contextPath = Write-LiveLaneGroupContext -Head $head -CheckpointResults $results
        $env:ANTIPHON_LIVE_PORTABLE_CONTEXT = $contextPath
        $env:TEMP = '/tmp'
        $env:TMP = '/tmp'
        $env:MSBUILDDISABLENODEREUSE = '1'
        $env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER = '1'
        # A command row is ready before its builds. One --after run picks CP-30 first and
        # the census then refuses with producer-incomplete. Run the roster rows, then the census.
        # 90m is the admission budget on a saturated host. The roster child wall is separate.
        if ($Group -eq 'S1a') {
            foreach ($row in @('CP-25', 'CP-1', 'CP-30')) {
                & dotnet exec $ToolDll run $manifestPath --rows $row --expected-source-sha $expected --serial --total-timeout 90m --hard-wall-timeout 100m --results-root $results
                if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
            }
            exit 0
        }
        & dotnet exec $ToolDll run $manifestPath --after $Group --expected-source-sha $expected --serial --total-timeout 90m --hard-wall-timeout 100m --results-root $results
        exit $LASTEXITCODE
    }
    default {
        [Console]::Error.WriteLine('unknown-case')
        exit 2
    }
}
