# CARD-1545 S1a live-lane manifest driver. ASCII-only.
# Diagnostic inventory only. No provider call, no live Claude, no policy edit.
param(
    [string]$Case = '',
    [string]$AssemblyPath = '',
    [string]$Project = '',
    [string]$RepoRoot = '',
    [string]$Group = '',
    [string]$ToolDll = '',
    [string]$CheckpointManifest = '',
    [string]$ExpectedSourceSha = '',
    [string]$CensusBaseSha = '',
    [string]$PolicyRoot = '',
    [string]$PolicyPath = '',
    [string]$CaptureDir = ''
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

function Write-LiveLanePhase {
    param([string]$Capture, [string]$Name, [int]$ElapsedMs)
    $line = 'phase=' + $Name + ' elapsedMs=' + $ElapsedMs
    [Console]::Error.WriteLine($line)
    try { [Console]::Error.Flush() } catch { }
    if ([string]::IsNullOrWhiteSpace($Capture)) { return }
    New-Item -ItemType Directory -Path $Capture -Force | Out-Null
    $path = Join-Path $Capture 'phase.log'
    [System.IO.File]::AppendAllText($path, $line + "`n")
}

if ($Case -eq 'EmitPolicyHash') {
    if ([string]::IsNullOrWhiteSpace($PolicyRoot)) { exit 2 }
    $policyLib = Join-Path $PolicyRoot 'scripts/lib'
    . (Join-Path $policyLib 'nightly-common.ps1')
    . (Join-Path $policyLib 'nightly-policy.ps1')
    $hashPath = $PolicyPath
    if ([string]::IsNullOrWhiteSpace($hashPath)) { $hashPath = Join-Path $PolicyRoot 'tests/test-execution-policy.json' }
    $policyObject = [System.IO.File]::ReadAllText($hashPath) | ConvertFrom-Json
    [Console]::Out.WriteLine((Get-NightlyPolicyHash -Object $policyObject))
    exit 0
}

$RepoRoot = Get-LiveLaneScriptRepo -Start $RepoRoot
. (Join-Path $RepoRoot 'scripts/lib/live-lane-manifest.ps1')

function Invoke-LiveLaneRosterCase {
    param([string]$ProjectName)
    $capture = $CaptureDir
    if ([string]::IsNullOrWhiteSpace($capture)) { $capture = Join-Path $RepoRoot '.antiphon/live-lane-capture' }
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    Write-LiveLanePhase -Capture $capture -Name 'start' -ElapsedMs 0
    $discovery = $null
    try {
        $discovery = Invoke-LiveLaneDiscovery -RepoRoot $RepoRoot -Project $ProjectName -AssemblyPath $AssemblyPath -CaptureDir $capture
    } catch {
        Write-LiveLanePhase -Capture $capture -Name 'discovery-failed' -ElapsedMs ([int]$clock.ElapsedMilliseconds)
        Write-LiveLaneCaseResult -DiscoveryOk $false -ErrorText $_.Exception.Message -Failures @() -LaunchCount 0 -DiscoveryMs ([int]$clock.ElapsedMilliseconds)
        exit 1
    }
    $discoveryMs = [int]$clock.ElapsedMilliseconds
    Write-LiveLanePhase -Capture $capture -Name 'discovery' -ElapsedMs $discoveryMs
    if ($null -eq $discovery -or [string]$discovery.Error -eq 'assembly-missing') {
        $message = $(if ($discovery) { [string]$discovery.Error } else { 'assembly-missing' })
        Write-LiveLaneCaseResult -DiscoveryOk $false -ErrorText $message -Failures @() -LaunchCount 0 -DiscoveryMs $discoveryMs
        exit 1
    }
    $manifest = Read-LiveLaneManifestObject -RepoRoot $RepoRoot
    $context = Read-LiveLaneContextObject
    $mode = ''
    $exportRequired = $false
    if ($context) {
        $mode = [string](Get-LiveLaneProp $context 'mode')
        $flag = Get-LiveLaneProp $context 'exportRequired'
        if ($null -ne $flag) { $exportRequired = [bool]$flag }
    }
    $exportPathWritten = ''
    if ($mode -eq 'candidate' -and $exportRequired) {
        $exportPath = ''
        $cp = ''
        if ($ProjectName -eq 'Antiphon.Agents.Pty.Tests') {
            $exportPath = [string](Get-LiveLaneProp $context 'cp25Export')
            $cp = 'CP-25'
        } else {
            $exportPath = [string](Get-LiveLaneProp $context 'cp1Export')
            $cp = 'CP-1'
        }
        if (-not [string]::IsNullOrWhiteSpace($exportPath)) {
            # Pending until the roster checks pass. A killed or red row must not look final.
            Write-LiveLaneExportFile -Path $exportPath -Project $ProjectName -Cp $cp -SourceSha ([string](Get-LiveLaneProp $context 'sourceSha')) -RunId ([string](Get-LiveLaneProp $context 'runId')) -AssemblyHash ([string]$discovery.AssemblyHash) -AssemblyPath ([string]$discovery.AssemblyPath) -Nodes @($discovery.Nodes)
            $exportPathWritten = $exportPath
        }
    }
    $exportMs = [int]$clock.ElapsedMilliseconds
    Write-LiveLanePhase -Capture $capture -Name 'export' -ElapsedMs $exportMs
    $failures = @(Invoke-LiveLaneRosterMatrix -Project $ProjectName -Nodes @($discovery.Nodes) -Manifest $manifest -Context $context -AssemblyHash ([string]$discovery.AssemblyHash) -AssemblyPath ([string]$discovery.AssemblyPath))
    $matrixMs = [int]$clock.ElapsedMilliseconds
    Write-LiveLanePhase -Capture $capture -Name 'matrix' -ElapsedMs $matrixMs
    $checksPassed = ($failures.Count -eq 0 -and [bool]$discovery.Ok)
    if (-not [string]::IsNullOrWhiteSpace($exportPathWritten)) {
        Complete-LiveLaneExport -Path $exportPathWritten -ChecksPassed $checksPassed
    }
    Write-LiveLanePhase -Capture $capture -Name 'final' -ElapsedMs ([int]$clock.ElapsedMilliseconds)
    Write-LiveLaneCaseResult -DiscoveryOk ([bool]$discovery.Ok) -ErrorText ([string]$discovery.Error) -Failures $failures -LaunchCount ([int]$discovery.LaunchCount) -DiscoveryMs $discoveryMs -ExportMs $exportMs -MatrixMs $matrixMs
    exit 0
}

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

function Write-LiveLaneWitness {
    param([string[]]$Problems)
    $items = @($Problems | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
    if ($items.Count -eq 0) {
        [Console]::Out.WriteLine('accepted')
        exit 0
    }
    foreach ($item in $items) { [Console]::Out.WriteLine([string]$item) }
    exit 1
}

function Invoke-LiveLaneExportPendingWitness {
    param([string]$RepoRoot)
    $problems = New-Object System.Collections.Generic.List[string]
    $root = New-LiveLaneOwnedScratch -RepoRoot $RepoRoot -Leaf ('exportpending' + [guid]::NewGuid().ToString('N'))
    try {
        $path = Join-Path $root 'export.json'
        Write-LiveLaneExportFile -Path $path -Project 'Antiphon.Tests' -Cp 'CP-1' -SourceSha ('a' * 40) -RunId 'run' -AssemblyHash ('ab' * 32) -AssemblyPath '' -Nodes @()
        $pending = [System.IO.File]::ReadAllText($path) | ConvertFrom-Json
        if ([bool](Get-LiveLaneProp $pending 'terminal')) { $problems.Add('finalized-early') }
        Complete-LiveLaneExport -Path $path -ChecksPassed $false
        $held = [System.IO.File]::ReadAllText($path) | ConvertFrom-Json
        if ([bool](Get-LiveLaneProp $held 'terminal')) { $problems.Add('finalized-on-failure') }
        Complete-LiveLaneExport -Path $path -ChecksPassed $true
        $done = [System.IO.File]::ReadAllText($path) | ConvertFrom-Json
        if (-not [bool](Get-LiveLaneProp $done 'terminal')) { $problems.Add('not-finalized') }
    } finally {
        Clear-LiveLaneOwnedScratch
    }
    if (Test-Path -LiteralPath $root) { $problems.Add('export-scratch-residue') }
    return @($problems)
}

function Invoke-LiveLaneProducerReceiptWitness {
    param([string]$RepoRoot)
    $problems = New-Object System.Collections.Generic.List[string]
    $sha = 'a' * 40
    $root = New-LiveLaneOwnedScratch -RepoRoot $RepoRoot -Leaf ('receiptwitness' + [guid]::NewGuid().ToString('N'))
    try {
        $missing = Test-LiveLaneProducerReceipt -Context ([pscustomobject]@{ sourceSha = $sha; checkpointResults = (Join-Path $root 'absent') })
        if ($missing.Reason -ne 'producer-receipt-missing') { $problems.Add('missing-shape') }
        $red = Join-Path $root 'red'
        New-Item -ItemType Directory -Path $red -Force | Out-Null
        Write-LiveLaneReceiptFixture -Root $red -Commit $sha -RowState 'red'
        $redResult = Test-LiveLaneProducerReceipt -Context ([pscustomobject]@{ sourceSha = $sha; checkpointResults = $red })
        if ($redResult.Reason -ne 'producer-row-not-green') { $problems.Add('red-shape') }
        $timed = Join-Path $root 'timed'
        New-Item -ItemType Directory -Path $timed -Force | Out-Null
        Write-LiveLaneReceiptFixture -Root $timed -Commit $sha -RowState 'running'
        $timedResult = Test-LiveLaneProducerReceipt -Context ([pscustomobject]@{ sourceSha = $sha; checkpointResults = $timed })
        if ($timedResult.Reason -ne 'producer-row-not-green') { $problems.Add('timeout-shape') }
        $foreign = Join-Path $root 'foreign'
        New-Item -ItemType Directory -Path $foreign -Force | Out-Null
        Write-LiveLaneReceiptFixture -Root $foreign -Commit ('b' * 40) -RowState 'green' -BuildSource 'unknown' -LineCommit ('b' * 40)
        $foreignResult = Test-LiveLaneProducerReceipt -Context ([pscustomobject]@{ sourceSha = $sha; checkpointResults = $foreign })
        if ($foreignResult.Reason -ne 'producer-receipt-foreign') { $problems.Add('foreign-shape') }
        $green = Join-Path $root 'green'
        New-Item -ItemType Directory -Path $green -Force | Out-Null
        Write-LiveLaneReceiptFixture -Root $green -Commit $sha -RowState 'green'
        $greenResult = Test-LiveLaneProducerReceipt -Context ([pscustomobject]@{ sourceSha = $sha; checkpointResults = $green })
        if (-not $greenResult.Accepted) { $problems.Add('green-shape') }
        $wired = Invoke-LiveLaneCensus -Context ([pscustomobject]@{ candidateRoot = $RepoRoot; sourceSha = $sha; checkpointResults = $red; censusBaseSha = '' })
        if (-not (Test-LiveLaneCensusHas $wired @('producer-row-not-green'))) { $problems.Add('census-skipped-receipt') }
    } finally {
        Clear-LiveLaneOwnedScratch
    }
    return @($problems)
}

function Invoke-LiveLaneScratchWitness {
    param([string]$RepoRoot)
    $problems = New-Object System.Collections.Generic.List[string]
    foreach ($label in @('fail', 'green')) {
        $before = @(Get-LiveLaneTempResidue)
        $created = New-Object System.Collections.Generic.List[string]
        $sha = ''
        try {
            $owned = New-LiveLaneOwnedScratch -RepoRoot $RepoRoot -Leaf ('residue' + $label + [guid]::NewGuid().ToString('N'))
            $created.Add($owned)
            $head = (Get-LiveLaneGit -RepoRoot $RepoRoot -GitArgs @('rev-parse', 'HEAD')).Text
            $material = Get-LiveLaneCensusSeedMaterial -RepoRoot $RepoRoot -Seed $head -FixtureRel $script:LiveLaneFixtureRel -Pre $script:LiveLanePreS1BaseSha
            if ($null -eq $material) { $problems.Add('seed-material-missing') }
            else { $created.Add([string]$material.BeforeDir) }
            $policy = New-LiveLanePolicyMutant -RepoRoot $RepoRoot -Edit { param($obj) $obj.policyHash = [string]$obj.policyHash }
            $created.Add([string]$policy.Root)
            $sha = New-LiveLaneDanglingSeed -RepoRoot $RepoRoot
            if ($label -eq 'fail') { throw 'forced-scratch-failure' }
        } catch {
            if ($label -eq 'green') { $problems.Add([string]$_.Exception.Message) }
        } finally {
            Clear-LiveLaneOwnedScratch
        }
        foreach ($path in @($created)) {
            if (-not [string]::IsNullOrWhiteSpace($path) -and (Test-Path -LiteralPath $path)) { $problems.Add('residue-' + $label) }
        }
        if (-not [string]::IsNullOrWhiteSpace($sha)) {
            $still = Get-LiveLaneGit -RepoRoot $RepoRoot -GitArgs @('cat-file', '-t', $sha)
            if ($still.Exit -eq 0) { $problems.Add('dangling-commit-' + $label) }
        }
        foreach ($name in @(Get-LiveLaneTempResidue)) {
            $seen = $false
            foreach ($old in @($before)) { if ($old -eq $name) { $seen = $true } }
            if (-not $seen) { $problems.Add('tmp-residue') }
        }
    }
    return @($problems)
}

switch ($Case) {
    'ExactCoreRoster' { Invoke-LiveLaneRosterCase -ProjectName 'Antiphon.Tests' }
    'ExactPtyCoreRoster' { Invoke-LiveLaneRosterCase -ProjectName 'Antiphon.Agents.Pty.Tests' }
    'EveryLiveGateHasDisposition' {
        $manifest = Read-LiveLaneManifestObject -RepoRoot $RepoRoot
        $failures = @(Invoke-LiveLaneGateMatrix -RepoRoot $RepoRoot -Manifest $manifest)
        Write-LiveLaneCaseResult -DiscoveryOk $true -ErrorText '' -Failures $failures -LaunchCount 0
        exit 0
    }
    'ExportPending' { Write-LiveLaneWitness -Problems @(Invoke-LiveLaneExportPendingWitness -RepoRoot $RepoRoot) }
    'ProducerReceipt' { Write-LiveLaneWitness -Problems @(Invoke-LiveLaneProducerReceiptWitness -RepoRoot $RepoRoot) }
    'ScratchResidue' { Write-LiveLaneWitness -Problems @(Invoke-LiveLaneScratchWitness -RepoRoot $RepoRoot) }
    'FullCensusUnchanged' {
        $pending = @(Invoke-LiveLaneExportPendingWitness -RepoRoot $RepoRoot)
        if (@($pending | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) }).Count -gt 0) {
            foreach ($item in @($pending)) { [Console]::Out.WriteLine([string]$item) }
            exit 1
        }
        $residue = @(Invoke-LiveLaneScratchWitness -RepoRoot $RepoRoot)
        if (@($residue | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) }).Count -gt 0) {
            foreach ($item in @($residue)) { [Console]::Out.WriteLine([string]$item) }
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
    'ScanGates' {
        $written = Write-LiveLaneGateCatalog -RepoRoot $RepoRoot
        [Console]::Out.WriteLine(('gates=' + $written.Count + ' unresolved=' + $written.Unresolved))
        exit 0
    }
    'BindManifest' {
        $capture = $CaptureDir
        if ([string]::IsNullOrWhiteSpace($capture)) { $capture = Join-Path $RepoRoot '.antiphon/live-lane-capture' }
        Update-LiveLaneManifestBindings -RepoRoot $RepoRoot -CaptureDir $capture
        [Console]::Out.WriteLine('bound')
        exit 0
    }
    'WriteFixture' {
        $capture = $CaptureDir
        if ([string]::IsNullOrWhiteSpace($capture)) { $capture = Join-Path $RepoRoot '.antiphon/live-lane-capture' }
        $sourceSha = $ExpectedSourceSha
        if ([string]::IsNullOrWhiteSpace($sourceSha)) {
            $sourceSha = (Get-LiveLaneGit -RepoRoot $RepoRoot -GitArgs @('rev-parse', 'HEAD')).Text.Trim()
        }
        $dest = Write-LiveLaneDiscoveryFixture -RepoRoot $RepoRoot -CaptureDir $capture -SourceSha $sourceSha
        [Console]::Out.WriteLine($dest)
        exit 0
    }
    'Group' {
        if ([string]::IsNullOrWhiteSpace($ToolDll) -or -not (Test-Path -LiteralPath $ToolDll)) { throw 'tool-dll-missing' }
        $manifestPath = $CheckpointManifest
        if ([string]::IsNullOrWhiteSpace($manifestPath)) { $manifestPath = Join-Path $RepoRoot '.antiphon/s1a-sanitization-checkpoints.yaml' }
        $head = (Get-LiveLaneGit -RepoRoot $RepoRoot -GitArgs @('rev-parse', 'HEAD')).Text.Trim()
        $expected = $ExpectedSourceSha
        if ([string]::IsNullOrWhiteSpace($expected)) { $expected = $head }
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
