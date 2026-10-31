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
    param([bool]$DiscoveryOk, [string]$ErrorText, [string[]]$Failures, [int]$LaunchCount)
    $items = @()
    foreach ($item in @($Failures)) {
        if ($null -eq $item) { continue }
        $text = [string]$item
        if ([string]::IsNullOrWhiteSpace($text)) { continue }
        $items += (ConvertTo-LiveLaneJsonString $text)
    }
    $failJson = '[' + ($items -join ',') + ']'
    $flag = $(if ($DiscoveryOk) { 'true' } else { 'false' })
    $line = '{"discoveryOk":' + $flag + ',"error":' + (ConvertTo-LiveLaneJsonString $ErrorText) + ',"failures":' + $failJson + ',"launchCount":' + $LaunchCount + '}'
    [Console]::Out.WriteLine($line)
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
    $discovery = $null
    try {
        $discovery = Invoke-LiveLaneDiscovery -RepoRoot $RepoRoot -Project $ProjectName -AssemblyPath $AssemblyPath -CaptureDir $capture
    } catch {
        Write-LiveLaneCaseResult -DiscoveryOk $false -ErrorText $_.Exception.Message -Failures @() -LaunchCount 0
        exit 1
    }
    if ($null -eq $discovery -or [string]$discovery.Error -eq 'assembly-missing') {
        $message = $(if ($discovery) { [string]$discovery.Error } else { 'assembly-missing' })
        Write-LiveLaneCaseResult -DiscoveryOk $false -ErrorText $message -Failures @() -LaunchCount 0
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
            Write-LiveLaneExportFile -Path $exportPath -Project $ProjectName -Cp $cp -SourceSha ([string](Get-LiveLaneProp $context 'sourceSha')) -RunId ([string](Get-LiveLaneProp $context 'runId')) -AssemblyHash ([string]$discovery.AssemblyHash) -AssemblyPath ([string]$discovery.AssemblyPath) -Nodes @($discovery.Nodes)
        }
    }
    $failures = @(Invoke-LiveLaneRosterMatrix -Project $ProjectName -Nodes @($discovery.Nodes) -Manifest $manifest -Context $context -AssemblyHash ([string]$discovery.AssemblyHash) -AssemblyPath ([string]$discovery.AssemblyPath))
    Write-LiveLaneCaseResult -DiscoveryOk ([bool]$discovery.Ok) -ErrorText ([string]$discovery.Error) -Failures $failures -LaunchCount ([int]$discovery.LaunchCount)
    exit 0
}

function Write-LiveLaneGroupContext {
    param([string]$Head)
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
        $copy = Join-Path ([System.IO.Path]::GetTempPath()) ('antiphon-live-lane-fixture-' + $runId + '.json')
        Copy-LiveLaneGitBlob -RepoRoot $RepoRoot -Spec $spec -Dest $copy
        $fixture = [System.IO.File]::ReadAllText($copy) | ConvertFrom-Json
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
    }
    $json = $doc | ConvertTo-Json -Depth 8
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($contextPath, $json, $utf8)
    return (Resolve-Path -LiteralPath $contextPath).Path
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
    'FullCensusUnchanged' {
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
        $contextPath = Write-LiveLaneGroupContext -Head $head
        $env:ANTIPHON_LIVE_PORTABLE_CONTEXT = $contextPath
        $env:TEMP = '/tmp'
        $env:TMP = '/tmp'
        $env:MSBUILDDISABLENODEREUSE = '1'
        $env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER = '1'
        $results = Join-Path $RepoRoot ('.antiphon/live-lane-runs/' + [guid]::NewGuid().ToString('N'))
        & dotnet exec $ToolDll run $manifestPath --after $Group --expected-source-sha $expected --serial --total-timeout 26m --hard-wall-timeout 29m --results-root $results
        exit $LASTEXITCODE
    }
    default {
        [Console]::Error.WriteLine('unknown-case')
        exit 2
    }
}
