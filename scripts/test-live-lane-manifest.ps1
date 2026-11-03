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

function Test-LiveLaneCrlfDisplayArguments {
    $problems = @()
    $lfPath = '/tmp/c1545-f0ad975a-lf.diag'
    $crlfPath = '/tmp/c1545-f0ad975a-crlf.diag'
    $line = 'TestNodeUid { Value = uid-submit }, DisplayName = Submitting_via_two_writes_completes_a_turn(claude), Properties = [ ParameterTypeFullNames = [System.String] ]'
    try {
        [System.IO.File]::WriteAllText($lfPath, $line + "`n")
        [System.IO.File]::WriteAllText($crlfPath, ($line + "`n").Replace("`n", "`r`n"))
        $lfMap = Get-LiveLaneDisplayMap -DiagPath $lfPath
        $crlfMap = Get-LiveLaneDisplayMap -DiagPath $crlfPath
        $lfArgs = @()
        $crlfArgs = @()
        if ($lfMap.ContainsKey('uid-submit')) { $lfArgs = @(Get-LiveLaneParsedArguments -Method 'Submitting_via_two_writes_completes_a_turn' -Display ([string]$lfMap['uid-submit'].Display)) }
        if ($crlfMap.ContainsKey('uid-submit')) { $crlfArgs = @(Get-LiveLaneParsedArguments -Method 'Submitting_via_two_writes_completes_a_turn' -Display ([string]$crlfMap['uid-submit'].Display)) }
        if (($lfArgs -join '|') -ne 'claude' -or ($crlfArgs -join '|') -ne 'claude') {
            $problems += ('C1545 V-2/display lf=' + ($lfArgs -join '|') + ' crlf=' + ($crlfArgs -join '|'))
        }
        $bare = $line.Replace('(claude)', '()')
        [System.IO.File]::WriteAllText($crlfPath, ($bare + "`n").Replace("`n", "`r`n"))
        $bareMap = Get-LiveLaneDisplayMap -DiagPath $crlfPath
        $bareArgs = @()
        if ($bareMap.ContainsKey('uid-submit')) { $bareArgs = @(Get-LiveLaneParsedArguments -Method 'Submitting_via_two_writes_completes_a_turn' -Display ([string]$bareMap['uid-submit'].Display)) }
        if (@($bareArgs).Count -ne 0) { $problems += 'C1545 V-2/display removed argument still parsed' }
    } finally {
        Remove-Item -LiteralPath '/tmp/c1545-f0ad975a-lf.diag' -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath '/tmp/c1545-f0ad975a-crlf.diag' -ErrorAction SilentlyContinue
    }
    return @($problems)
}

function New-LiveLaneDecoyFirstSubmitNodes {
    $decoy = [pscustomobject]@{
        project = 'Antiphon.Agents.Pty.Tests'
        uid = 'decoy-submit'
        namespace = 'Antiphon.Agents.Pty.Tests'
        type = 'ClaudeSubmitContractTests'
        method = 'Submitting_via_two_writes_completes_a_turn'
        displayName = 'Submitting_via_two_writes_completes_a_turn(fakeclaude)'
        arguments = @('fakeclaude')
        signature = ''
        categories = @('Pty')
    }
    $live = [pscustomobject]@{
        project = 'Antiphon.Agents.Pty.Tests'
        uid = 'live-submit'
        namespace = 'Antiphon.Agents.Pty.Tests'
        type = 'ClaudeSubmitContractLiveTests'
        method = 'Submitting_via_two_writes_completes_a_turn'
        displayName = 'Submitting_via_two_writes_completes_a_turn(claude)'
        arguments = @('claude')
        signature = ''
        categories = @('Pty')
    }
    return @($decoy, $live)
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
        $target = 'refs/remotes/origin/master'
        $cases = [ordered]@{
            honest = @{ Pre = $pre; Seed = $seed; Head = $head; Target = $target; Want = @(); Forbid = @('pre-not-on-target', 'later-tree', 'target-unresolved') }
            offTarget = @{ Pre = $offPre; Seed = $offSeed; Head = $offHead; Target = $target; Want = @('pre-not-on-target'); Forbid = @() }
            laterTree = @{ Pre = $pre; Seed = $seed; Head = $merged; Target = $target; Want = @('later-tree'); Forbid = @('pre-not-on-target') }
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
    'ExactCoreRoster' { Invoke-LiveLaneRosterCase -ProjectName 'Antiphon.Tests' }
    'ExactPtyCoreRoster' { Invoke-LiveLaneRosterCase -ProjectName 'Antiphon.Agents.Pty.Tests' }
    'ExtraArgumentDecoyFirst' {
        $projectName = 'Antiphon.Agents.Pty.Tests'
        $manifestObject = Read-LiveLaneManifestObject -RepoRoot $RepoRoot
        $entries = @(Copy-LiveLaneEntries (Get-LiveLaneProp $manifestObject 'core') | Where-Object { $_.project -eq $projectName })
        $nodes = @(New-LiveLaneDecoyFirstSubmitNodes)
        $expanded = Test-LiveLaneSubmitExtraExpansion -Project $projectName -Nodes $nodes -Entries $entries
        $sameArgument = Test-LiveLaneSubmitExtraExpansion -Project $projectName -Nodes $nodes -Entries $entries -CloneArguments @('claude')
        $decoyFailures = @()
        if (-not $expanded) { $decoyFailures += 'C1545 V-2/extra-argument expandedUidSet has no extra argument' }
        if ($sameArgument) { $decoyFailures += 'C1545 V-2/extra-argument same argument still expanded' }
        foreach ($item in @(Test-LiveLaneCrlfDisplayArguments)) {
            if (-not [string]::IsNullOrWhiteSpace([string]$item)) { $decoyFailures += [string]$item }
        }
        Write-LiveLaneCaseResult -DiscoveryOk $true -ErrorText '' -Failures $decoyFailures -LaunchCount 0
        if ($decoyFailures.Count -ne 0) { exit 1 }
        exit 0
    }
    'EveryLiveGateHasDisposition' {
        $manifest = Read-LiveLaneManifestObject -RepoRoot $RepoRoot
        $failures = @(Invoke-LiveLaneGateMatrix -RepoRoot $RepoRoot -Manifest $manifest)
        Write-LiveLaneCaseResult -DiscoveryOk $true -ErrorText '' -Failures $failures -LaunchCount 0
        exit 0
    }
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
