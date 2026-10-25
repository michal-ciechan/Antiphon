#requires -Version 7.0
# CARD-1336: evaluate the production Bash body with an inert, readonly ssh function.
param(
    [Parameter(Mandatory)][ValidateSet('C1336_WrapperRef', 'C1336_NightlyCandidateIdentity')][string]$Case,
    [Parameter(Mandatory)][string]$ResultsDirectory
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $repoRoot 'scripts/lib/c487-harness.ps1')
$utf8 = [System.Text.UTF8Encoding]::new($false)
New-Item -ItemType Directory -Path $ResultsDirectory -Force | Out-Null

function Write-C1336File($Root, $Relative, $Text) {
    $path = Join-Path $Root $Relative
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $path))
    [IO.File]::WriteAllText($path, $Text, $utf8)
}

function Git-C1336($Root, $Arguments) {
    return (Invoke-TestSelectionGit $Root $Arguments).Stdout.Trim()
}

function Add-C1336Observer($Path, $Kind) {
    $source = [IO.File]::ReadAllText($Path)
    $tokens = $null; $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseInput($source, [ref]$tokens, [ref]$errors)
    if ($errors.Count -or -not $ast.ParamBlock) { throw "Cannot instrument wrapper $Path" }
    # Record received parameters before the unmodified production admission/body.
    $observer = @'

$observerPath = Join-Path (Split-Path -Parent $SeamsPath) ('OBSERVATION-' + $RunId + '.json')
[IO.File]::WriteAllText($observerPath, (ConvertTo-Json $PSBoundParameters -Depth 8))

'@
    $source = $source.Insert($ast.ParamBlock.Extent.EndOffset, $observer.Replace('OBSERVATION', $Kind))
    if ($Kind -ceq 'workload') {
        # Observe the production result and provide a deliberately optimistic
        # synthetic report field. No identity or admission function is substituted.
        $tail = @'
[IO.File]::WriteAllText((Join-Path (Split-Path -Parent $SeamsPath) ('workload-result-' + $RunId + '.json')), (ConvertTo-Json $result -Depth 12))
if ($result.SummaryPath -and (Test-Path -LiteralPath $result.SummaryPath)) {
    $synthetic = Get-Content -LiteralPath $result.SummaryPath -Raw | ConvertFrom-Json
    $synthetic | Add-Member -NotePropertyName reportDelivered -NotePropertyValue $true -Force
    $synthetic | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $result.SummaryPath
}

'@
        $anchor = 'if ($PassThru) { return $result }'
        if (-not $source.Contains($anchor)) { throw 'Workload observation anchor missing' }
        $source = $source.Replace($anchor, $tail + $anchor)
    }
    [IO.File]::WriteAllText($Path, $source, $utf8)
}

function New-C1336Candidate {
    foreach ($name in @('git', 'pwsh')) { [void](Get-Command $name -CommandType Application -ErrorAction Stop) }
    $root = Join-Path $ResultsDirectory 'candidate fixture'
    $seed = Join-Path $root 'seed'
    [void][IO.Directory]::CreateDirectory($seed)
    [void](Git-C1336 $seed @('init', '-q', '-b', 'rc/2026-10-18'))
    [void](Git-C1336 $seed @('config', 'user.name', 'C1336 fixture'))
    [void](Git-C1336 $seed @('config', 'user.email', 'fixture@example.invalid'))
    [void](Git-C1336 $seed @('config', 'core.autocrlf', 'false'))
    [void][IO.Directory]::CreateDirectory((Join-Path $seed 'scripts'))
    Copy-Item (Join-Path $repoRoot 'scripts/lib') (Join-Path $seed 'scripts/lib') -Recurse
    foreach ($name in @('nightly-run.ps1', 'nightly-tests.ps1', 'select-test-rosters.ps1')) {
        Copy-Item (Join-Path $repoRoot "scripts/$name") (Join-Path $seed "scripts/$name")
    }
    Add-C1336Observer (Join-Path $seed 'scripts/nightly-run.ps1') 'hop'
    Add-C1336Observer (Join-Path $seed 'scripts/nightly-tests.ps1') 'workload'
    foreach ($name in @('tests/test-execution-policy.json', 'tests/test-selection-policy.json', 'global.json')) {
        Write-C1336File $seed $name ([IO.File]::ReadAllText((Join-Path $repoRoot $name)))
    }
    $catalog = Get-Content (Join-Path $seed 'tests/test-selection-policy.json') -Raw | ConvertFrom-Json
    foreach ($path in @($catalog.sets.sourceInputs + $catalog.sets.members.source | Sort-Object -Unique)) {
        if ($path -and -not (Test-Path (Join-Path $seed $path))) { Write-C1336File $seed $path 'fixture source' }
    }
    # Keep the fixture's project census current with policy, including probes outside tests/.
    foreach ($path in (Git-C1336 $repoRoot @('ls-files', '*.csproj')).Split("`n")) {
        Write-C1336File $seed $path ([IO.File]::ReadAllText((Join-Path $repoRoot $path)))
    }
    Add-Content (Join-Path $seed 'scripts/nightly-run.ps1') '# C1336 owned clone self-hop'
    Write-C1336File $seed '.antiphon-nightly-owned' '{"kind":"nightly-clone"}'
    Write-C1336File $seed 'fixture.txt' 'A'
    [void](Git-C1336 $seed @('add', '--all'))
    [void](Git-C1336 $seed @('commit', '-qm', 'candidate A'))
    $a = Git-C1336 $seed @('rev-parse', 'HEAD')
    Write-C1336File $seed 'fixture.txt' 'B'
    [void](Git-C1336 $seed @('add', '--all'))
    [void](Git-C1336 $seed @('commit', '-qm', 'candidate B'))
    $b = Git-C1336 $seed @('rev-parse', 'HEAD')
    if ($a.Substring(0, 12) -ceq $b.Substring(0, 12)) { throw 'Fixture pins must differ' }
    [void](Git-C1336 $seed @('branch', 'fixture-b', $b))
    [void](Git-C1336 $seed @('branch', 'release/rc-20260923T083000Z', $a))
    [void](Git-C1336 $seed @('reset', '--hard', $a))
    $origin = Join-Path $root 'origin.git'
    [void](Git-C1336 $root @('clone', '--bare', $seed, $origin))
    $clone = Join-Path $root 'checkout'
    [void](Git-C1336 $root @('clone', $origin, $clone))
    # Fail fixture prerequisites before the behavioral red witness.
    Assert-ReleaseSelectionCommit $clone $a
    $contract = Get-ReleaseSelectionContract $clone $a 'rc'
    if (-not $contract.policyHash -or -not $contract.catalogHash) { throw 'Fixture contract missing hashes' }
    $master = Join-Path $root 'private-master'
    foreach ($name in @('last-run.json', 'last-complete-green.json', 'last-monitor.json', 'interim-qualification-receipt.json')) {
        Write-C1336File $master $name ('{"sentinel":"' + $name + '"}')
    }
    $seams = Join-Path $root 'seams.ps1'
    $trace = Join-Path $root 'trace.txt'
    Write-C487NativePassSeams $seams $trace @(@{Id='fixture-one';ClassName='Fixture.Fixture';MethodName='One';Outcome='Passed'}) @(@{uid='fixture-one';type='Fixture';method='One';namespace='Fixture'})
    Add-Content $seams @"
`$NightlySeams.Git = `$null
# Synthetic workload records model the full Windows RC profile on either host.
`$NightlySeams.HostPlatform = { return 'windows' }
`$NightlySeams.E2EPrerequisites = { return @{Ok=`$true;Rows=@()} }
Set-Item Function:script:Get-NightlyDefaultStateRoot { return '$($master.Replace("'", "''"))' }
"@
    # The shared synthetic native I/O helper is wrapped in a fail-closed target
    # allowlist; no unexpected executable can fall through to a real process.
    Add-Content $seams @'
$script:C1336NativeIo = $NightlySeams.StartProcess
$NightlySeams.StartProcess = {
    param($FilePath, $ArgumentList, $WorkingDirectory, $TimeoutMilliseconds, $Environment)
    $leaf = [IO.Path]::GetFileName(($FilePath -replace '\\', '/'))
    $args = @($ArgumentList)
    $fileIndex = [array]::IndexOf($args, '-File')
    $allowed = $leaf -in @('dotnet', 'npm', 'npm.cmd')
    # The native profile packages npm through ComSpec. Linux has no ComSpec;
    # accept only these three synthetic workload requests, without launching it.
    if ($FilePath -ceq [string]$env:ComSpec -and $args.Count -ge 3 -and $args[0] -ceq '/c' -and
        [IO.Path]::IsPathRooted($args[1]) -and [IO.Path]::GetFileName($args[1]) -in @('npm', 'npm.cmd', 'npm.exe')) {
        $allowed = ($args.Count -eq 3 -and $args[2] -ceq 'ci') -or
            ($args.Count -eq 4 -and $args[2] -ceq 'run' -and $args[3] -in @('build', 'lint'))
    }
    if ($leaf -match '^Antiphon\.(Tests|SessionRunner\.Tests|PtyHost\.Tests|Agents\.Pty\.Tests|Messaging\.Tests|Observability\.Tests|E2E|MarkdownPdf\.Tests)\.exe$') { $allowed = $true }
    if ($leaf -in @('pwsh', 'pwsh.exe') -and $fileIndex -ge 0) {
        $target = [IO.Path]::GetFileName(([string]$args[$fileIndex + 1] -replace '\\', '/'))
        $policy = Get-Content (Join-Path $WorkingDirectory 'tests/test-execution-policy.json') -Raw | ConvertFrom-Json
        $allowedScripts = @($policy.scriptCensus | Where-Object disposition -CEQ 'unattended' | ForEach-Object { [IO.Path]::GetFileName($_.path) })
        $allowed = $target -in ($allowedScripts + @('test-client.ps1', 'nightly-report.ps1'))
        if ($target -ceq 'nightly-report.ps1') {
            $summaryIndex = [array]::IndexOf($args, '-Summary')
            [IO.File]::WriteAllText(($args[$summaryIndex + 1] + '.report-receipt'), 'inert local report I/O')
        }
    }
    if (-not $allowed) { throw ('Unexpected fixture process: ' + $FilePath + ' ' + ($args -join ' ')) }
    return $script:C1336NativeIo.Invoke($FilePath, $args, $WorkingDirectory, $TimeoutMilliseconds, $Environment)
}
'@
    return @{Root=$root;Seed=$seed;Clone=$clone;Origin=$origin;A=$a;B=$b;Master=$master;Seams=$seams;Trace=$trace}
}

function Invoke-C1336Candidate($Fx, $Name, [hashtable]$Overrides = @{}) {
    $root = Join-Path $Fx.Root $Name
    $invoke = @{
        Profile='rc';Trigger='rc';Ref='rc/2026-10-18';ExpectedSha=$Fx.A
        CheckoutRoot=$Fx.Clone;RemoteUrl=$Fx.Origin;Repository='fixture/repository'
        StateRoot=(Join-Path $root 'state');LogRoot=(Join-Path $root 'logs')
        ReleaseRoot=(Join-Path $root 'release');CoordinationRoot=(Join-Path $root 'coord')
        SeamsPath=$Fx.Seams;RunId=$Name;NoSync=$true;WhatIf=$true;PassThru=$true
    }
    foreach ($key in $Overrides.Keys) { $invoke[$key] = $Overrides[$key] }
    $before = if (Test-Path $Fx.Trace) { [IO.File]::ReadAllText($Fx.Trace) } else { '' }
    $items = @(& (Join-Path $repoRoot 'scripts/nightly-run.ps1') @invoke 6>&1)
    $result = @($items | Where-Object { $_ -isnot [Management.Automation.InformationRecord] })[-1]
    $lines = @($items | Where-Object { $_ -is [Management.Automation.InformationRecord] } | ForEach-Object { [string]$_.MessageData })
    $json = @($lines | Where-Object { $_.StartsWith('{"nativeRunId":') })[-1]
    if (-not $result -or -not $json) { throw "Missing returned result/terminal JSON: $Name" }
    $after = if (Test-Path $Fx.Trace) { [IO.File]::ReadAllText($Fx.Trace) } else { '' }
    return @{Result=$result;Terminal=($json | ConvertFrom-Json);Args=$invoke;Root=$root;Trace=$after.Substring($before.Length)}
}

function Read-C1336Json($Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}

function Test-C1336EarlyRefusal($Run, $Reason) {
    $r = $Run.Result
    return ($r.ExitCode -eq 3 -and $r.Phase -ceq 'profile' -and $r.Refusal -ceq $Reason -and
        -not $Run.Trace -and -not (Test-Path (Join-Path $Run.Args.StateRoot 'last-run.json')) -and
        -not (Test-Path (Join-Path $Run.Args.StateRoot 'run.lock')) -and
        -not (Test-Path $Run.Args.ReleaseRoot) -and -not (Test-Path $Run.Args.LogRoot) -and
        -not (Test-Path $Run.Args.CoordinationRoot))
}

function Get-C1336Selection($Run, $Id, $Sha) {
    $root = Join-Path $Run.Args.ReleaseRoot "candidates/$Id/attempts/$($Run.Args.RunId)"
    $attemptPath = Join-Path $root 'selection-attempt.json'
    $selectionPath = Join-Path $root 'selection.json'
    $attempt = Read-C1336Json $attemptPath
    $selection = Read-C1336Json $selectionPath
    $valid = $attempt -and $selection -and $attempt.candidateId -ceq $Id -and
        $attempt.candidateSha -ceq $Sha -and $attempt.runId -ceq $Run.Args.RunId -and
        $attempt.attempt -eq 1 -and $selection.candidateSha -ceq $Sha -and
        $attempt.selectionDigest -ceq (Get-NightlyFileSha256 $selectionPath) -and
        -not (Test-Path (Join-Path $Run.Args.ReleaseRoot 'candidates/foreign'))
    return @{Ok=[bool]$valid;Attempt=$attempt;Selection=$selection;AttemptPath=$attemptPath;SelectionPath=$selectionPath}
}

if ($Case -ceq 'C1336_NightlyCandidateIdentity') {
    foreach ($name in @('nightly-common', 'nightly-policy', 'nightly-coverage', 'release-gate', 'release-selection', 'test-selection')) {
        . (Join-Path $repoRoot "scripts/lib/$name.ps1")
    }
    $fx = New-C1336Candidate
    $masterBefore = @{}
    foreach ($file in Get-ChildItem $fx.Master -File) { $masterBefore[$file.Name] = Get-NightlyFileSha256 $file.FullName }
    $expected = 'rc-2026-10-18-' + $fx.A.Substring(0, 12)
    $run = Invoke-C1336Candidate $fx 'omitted'
    # Keep this identity assertion first: an unrelated downstream refusal cannot
    # impersonate the historical missing-id defect.
    Assert-C487 ($run.Result.CandidateId -ceq $expected) 'C1336 N omitted' ("CandidateId expected=$expected actual=$($run.Result.CandidateId); refusal=$($run.Result.Refusal)")
    if ($run.Result.ExitCode -ne 0) { throw ('Omitted downstream admission failed: ' + ($run.Result | ConvertTo-Json -Compress -Depth 8)) }
    foreach ($variant in @(
        @{Name='whitespace';Args=@{CandidateId=" `t "}},
        @{Name='full-ref';Args=@{Ref='refs/heads/rc/2026-10-18'}},
        @{Name='repeat';Args=@{}},
        @{Name='explicit';Args=@{CandidateId=$expected}}
    )) {
        $run = Invoke-C1336Candidate $fx $variant.Name $variant.Args
        Assert-C487 ($run.Result.CandidateId -ceq $expected -and $run.Result.ExitCode -eq 0) ('C1336 N ' + $variant.Name) ($run.Result | ConvertTo-Json -Compress -Depth 8)
    }
    [void](Git-C1336 $fx.Clone @('reset', '--hard', $fx.B))
    [void](Git-C1336 $fx.Origin @('update-ref', 'refs/heads/rc/2026-10-18', $fx.B))
    $run = Invoke-C1336Candidate $fx 'different-pin' @{ExpectedSha=$fx.B}
    $other = 'rc-2026-10-18-' + $fx.B.Substring(0, 12)
    Assert-C487 ($run.Result.CandidateId -ceq $other -and $other -cne $expected -and $run.Result.ExitCode -eq 0) 'C1336 N different-pin'
    [void](Git-C1336 $fx.Clone @('reset', '--hard', $fx.A))
    [void](Git-C1336 $fx.Origin @('update-ref', 'refs/heads/rc/2026-10-18', $fx.A))
    foreach ($variant in @(
        @{Name='foreign';Args=@{CandidateId='foreign'};Reason='rc-candidate-id-mismatch'},
        @{Name='missing-sha';Args=@{ExpectedSha='';CandidateId='foreign'};Reason='rc-expected-sha'},
        @{Name='short-sha';Args=@{ExpectedSha=$fx.A.Substring(0,7);CandidateId='foreign'};Reason='rc-expected-sha'},
        @{Name='invalid-sha';Args=@{ExpectedSha=('z'*40);CandidateId='foreign'};Reason='rc-expected-sha'},
        @{Name='bad-ref';Args=@{Ref='rc/2026-02-30';ExpectedSha='';CandidateId='foreign'};Reason='rc-ref'}
    )) {
        $run = Invoke-C1336Candidate $fx $variant.Name $variant.Args
        Assert-C487 (Test-C1336EarlyRefusal $run $variant.Reason) ('C1336 N ' + $variant.Name) ($run.Result | ConvertTo-Json -Compress -Depth 8)
    }
    $legacy = Invoke-C1336Candidate $fx 'legacy-omitted' @{Ref='release/rc-20260923T083000Z'}
    $legacyExplicit = Invoke-C1336Candidate $fx 'legacy-explicit' @{Ref='release/rc-20260923T083000Z';CandidateId='rc-20260923T083000Z'}
    Assert-C487 ($legacy.Result.CandidateId -ceq 'rc-20260923T083000Z' -and $legacy.Result.ExitCode -eq 0 -and
        $legacyExplicit.Result.CandidateId -ceq 'rc-20260923T083000Z' -and $legacyExplicit.Result.ExitCode -eq 0) 'C1336 N legacy'

    $sync = Invoke-C1336Candidate $fx 'sync' @{NoSync=$false;WhatIf=$false}
    $selected = Get-C1336Selection $sync $expected $fx.A
    Assert-C487 ($selected.Ok -and $sync.Result.ExitCode -eq 0 -and $sync.Trace -match 'git fetch origin rc/2026-10-18') 'C1336 N sync-selection' ($sync.Result | ConvertTo-Json -Compress -Depth 8)
    $hop = Read-C1336Json (Join-Path $fx.Root 'hop-sync.json')
    $workload = Read-C1336Json (Join-Path $fx.Root 'workload-sync.json')
    $state = Read-C1336Json (Join-Path $sync.Args.StateRoot 'last-run.json')
    $summary = if ($state.summaryPath) { Read-C1336Json $state.summaryPath } else { $null }
    $hopOk = $hop -and $hop.CandidateId -ceq $expected -and $hop.ExpectedSha -ceq $fx.A -and
        $hop.Ref -ceq 'rc/2026-10-18' -and $hop.Profile -ceq 'rc' -and $hop.ContinueRunId -ceq 'sync' -and
        $hop.NoSync -and $hop.ContinueParentPid -gt 0 -and $hop.ContinueParentStartedAt -and
        $selected.Ok -and $summary -and $sync.Result.ExitCode -eq 0
    foreach ($key in @('StateRoot', 'LogRoot', 'ReleaseRoot', 'CoordinationRoot', 'CheckoutRoot')) {
        $hopOk = $hopOk -and $hop.$key -ceq $sync.Args[$key]
    }
    Assert-C487 ([bool]$hopOk) 'C1336 N hop-child'
    $workloadOk = $workload -and $workload.CandidateId -ceq $expected -and $workload.ExpectedSha -ceq $fx.A -and
        $workload.RunId -ceq 'sync' -and $workload.Attempt -eq 1 -and $selected.Ok -and
        $workload.SelectionAttemptPath -ceq $selected.AttemptPath -and
        $workload.SelectionPath -ceq $selected.SelectionPath -and
        $workload.SelectionAttemptDigest -ceq (Get-NightlyFileSha256 $selected.AttemptPath) -and
        $workload.SelectionDigest -ceq (Get-NightlyFileSha256 $selected.SelectionPath) -and
        $summary.selection.candidateId -ceq $expected -and $summary.selection.runId -ceq 'sync' -and
        $summary.selection.attempt -eq 1 -and $summary.sha -ceq $fx.A -and
        $summary.testsPassed -and $summary.coverageComplete -and $sync.Result.ExitCode -eq 0
    Assert-C487 ([bool]$workloadOk) 'C1336 N workload-child'
    Assert-C487 ($sync.Result.ExitCode -eq 0 -and $sync.Terminal.exitCode -eq 0 -and
        $sync.Terminal.nativeRunId -ceq 'sync' -and $sync.Terminal.candidateId -ceq $expected -and
        $sync.Terminal.sha -ceq $fx.A -and $state.runId -ceq 'sync' -and $state.candidateId -ceq $expected -and
        $state.sha -ceq $fx.A -and $summary.runId -ceq 'sync' -and $summary.sha -ceq $fx.A -and
        $sync.Terminal.coverageComplete -and $sync.Terminal.testsPassed -and $sync.Terminal.reportDelivered) 'C1336 N completion'
    $greenPath = Join-Path $sync.Args.ReleaseRoot "candidates/$expected/complete-green.json"
    $green = Read-C1336Json $greenPath
    $greenFiles = @(Get-ChildItem $sync.Args.ReleaseRoot -Recurse -Filter '*green.json' -ErrorAction SilentlyContinue)
    Assert-C487 ($green -and $green.creditKind -ceq 'rc-release' -and $green.candidateId -ceq $expected -and
        $green.sha -ceq $fx.A -and $green.runId -ceq 'sync' -and $green.selection.attempt -eq 1 -and
        $greenFiles.Count -eq 1 -and $greenFiles[0].FullName -ceq $greenPath -and
        -not (Test-Path (Join-Path $sync.Args.StateRoot 'last-complete-green.json'))) 'C1336 N rc-green'

    $noSync = Invoke-C1336Candidate $fx 'nosync' @{WhatIf=$false}
    $noSyncSelection = Get-C1336Selection $noSync $expected $fx.A
    $noSyncWorkload = Read-C1336Json (Join-Path $fx.Root 'workload-nosync.json')
    Assert-C487 ($noSyncSelection.Ok -and $noSync.Result.ExitCode -eq 0 -and $noSync.Terminal.testsPassed -and
        $noSyncWorkload.SelectionAttemptDigest -ceq (Get-NightlyFileSha256 $noSyncSelection.AttemptPath) -and
        $noSync.Trace -notmatch 'git (fetch|reset)' -and -not (Test-Path (Join-Path $fx.Root 'hop-nosync.json'))) 'C1336 N nosync-selection'

    $masterRun = Invoke-C1336Candidate $fx 'master-root' @{StateRoot=$fx.Master}
    # The pre-existing sentinel is intentional; absence checks use a fresh root
    # for every other early-refusal row.
    Assert-C487 ($masterRun.Result.ExitCode -eq 3 -and $masterRun.Result.Phase -ceq 'profile' -and
        $masterRun.Result.Refusal -ceq 'rc-master-state-root' -and -not $masterRun.Trace -and
        -not (Test-Path $masterRun.Args.ReleaseRoot) -and -not (Test-Path $masterRun.Args.CoordinationRoot)) 'C1336 N master-root'
    $noReport = Invoke-C1336Candidate $fx 'noreport' @{WhatIf=$false;NoReport=$true}
    $noReportState = Read-C1336Json (Join-Path $noReport.Args.StateRoot 'last-run.json')
    $noReportSummary = if ($noReportState.summaryPath) { Read-C1336Json $noReportState.summaryPath } else { $null }
    Assert-C487 ($noReport.Result.ExitCode -eq 0 -and $noReportSummary.reportDelivered -eq $true -and
        $noReport.Result.reportDelivered -eq $false -and $noReportState.reportDelivered -eq $false -and
        $noReport.Terminal.reportDelivered -eq $false -and -not $noReport.Result.CreditKind -and
        -not (Test-Path ($noReportState.summaryPath + '.report-receipt')) -and
        @(Get-ChildItem $noReport.Args.ReleaseRoot -Recurse -Filter '*green.json').Count -eq 0) 'C1336 N noreport'

    [void](Git-C1336 $fx.Origin @('update-ref', 'refs/heads/rc/2026-10-18', $fx.B))
    $moved = Invoke-C1336Candidate $fx 'moved-sync' @{NoSync=$false;WhatIf=$false}
    Assert-C487 ($moved.Result.ExitCode -eq 3 -and $moved.Result.Refusal -ceq 'candidate-moved' -and
        $moved.Result.Sha -ceq $fx.B -and -not (Test-Path (Join-Path $fx.Root 'workload-moved-sync.json')) -and
        -not (Test-Path $moved.Args.ReleaseRoot)) 'C1336 N moved-sync'
    $movedNoSync = Invoke-C1336Candidate $fx 'moved-nosync' @{WhatIf=$false}
    $movedResult = Read-C1336Json (Join-Path $fx.Root 'workload-result-moved-nosync.json')
    $movedSelection = Get-C1336Selection $movedNoSync $expected $fx.B
    Assert-C487 ($movedNoSync.Result.ExitCode -ne 0 -and $movedResult.ExitCode -eq 2 -and
        $movedResult.Error -like 'expected-sha-mismatch*' -and $movedSelection.Ok -and
        $movedNoSync.Trace -notmatch 'EXEC .*--report-trx' -and
        @(Get-ChildItem $movedNoSync.Args.ReleaseRoot -Recurse -Filter '*green.json').Count -eq 0) 'C1336 N moved-nosync'
    $masterOk = $true
    foreach ($name in $masterBefore.Keys) {
        if ((Get-NightlyFileSha256 (Join-Path $fx.Master $name)) -cne $masterBefore[$name]) { $masterOk = $false }
    }
    Assert-C487 $masterOk 'C1336 N master-unchanged'
    $help = [IO.File]::ReadAllText((Join-Path $repoRoot 'scripts/nightly-run.ps1')).Split('#>')[0]
    $helpOk = $help.Contains('rc-YYYY-MM-DD-<first 12 lowercase SHA hex>') -and
        $help.Contains('rc-YYYYMMDDTHHMMSSZ') -and $help.Contains('pinned full SHA') -and
        $help.Contains('Trigger rc requires Profile rc') -and $help.Contains('-Profile rc -Trigger rc') -and
        $help.Contains('does not create a candidate') -and $help.Contains('not master readiness')
    foreach ($name in @('StateRoot','LogRoot','ReleaseRoot','CoordinationRoot')) { $helpOk = $helpOk -and $help.Contains("-$name C:\scratch\") }
    Assert-C487 $helpOk 'C1336 N help'
    $authority = @(Get-ChildItem $fx.Root -Recurse -File | Where-Object {
        $_.Name -in @('candidate.json','publish.json','authority.json','publication-authority.json','release-manifest.json')
    })
    Assert-C487 ($authority.Count -eq 0) 'C1336 N no-authority' ($authority.FullName -join ',')
    Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 25
    return
}

function Invoke-C1336Wrapper {
    param([string]$Name, [string]$Schedule, [int]$SshExit)
    $root = Join-Path $ResultsDirectory $Name
    New-Item -ItemType Directory -Path $root | Out-Null
    $emptyPath = Join-Path $root 'empty-path'
    New-Item -ItemType Directory -Path $emptyPath | Out-Null
    $definition = Get-Content -LiteralPath (Join-Path $repoRoot 'scripts/windmill/antiphon-release-candidates.json') -Raw | ConvertFrom-Json
    $bodyPath = Join-Path $root 'body.sh'
    [System.IO.File]::WriteAllText($bodyPath, [string]$definition.content, $utf8)
    $launcherPath = Join-Path $root 'capture.sh'
    # Only Bash builtins are reachable. No external ssh is on PATH, even if function setup fails.
    $launcher = @'
set -euo pipefail
ssh() {
    printf 'call\n' >> "$C1336_CALLS"
    printf '%s\0' "$@" >> "$C1336_ARGS"
    return "$C1336_SSH_EXIT"
}
readonly -f ssh
[[ "$(type -t ssh)" == function ]] || exit 97
set +e
source "$1"
'@
    [System.IO.File]::WriteAllText($launcherPath, $launcher.Replace("`r`n", "`n") + "`n", $utf8)
    $bash = (Get-Command bash -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    $start = [System.Diagnostics.ProcessStartInfo]::new($bash)
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.WorkingDirectory = $root
    foreach ($arg in @('--noprofile', '--norc', $launcherPath.Replace('\', '/'), $bodyPath.Replace('\', '/'))) {
        $start.ArgumentList.Add($arg)
    }
    foreach ($key in @($start.Environment.Keys)) {
        if ($key -in @('BASH_ENV', 'ENV', 'SHELLOPTS', 'BASHOPTS', 'WM_SCHEDULE_PATH', 'WM_JOB_ID') -or
            $key.StartsWith('BASH_FUNC_')) { [void]$start.Environment.Remove($key) }
    }
    $start.Environment['PATH'] = $emptyPath.Replace('\', '/')
    $start.Environment['WM_JOB_ID'] = 'c1336-job'
    if ($Schedule) { $start.Environment['WM_SCHEDULE_PATH'] = $Schedule }
    $argsPath = Join-Path $root 'argv.bin'
    $callsPath = Join-Path $root 'calls.txt'
    $start.Environment['C1336_ARGS'] = $argsPath.Replace('\', '/')
    $start.Environment['C1336_CALLS'] = $callsPath.Replace('\', '/')
    $start.Environment['C1336_SSH_EXIT'] = [string]$SshExit
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $start
    $started = $false
    try {
        if (-not $process.Start()) { throw 'Bash capture did not start' }
        $started = $true
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        # The operation owns its deadline; outer host scheduling is not a performance assertion.
        if (-not $process.WaitForExit(30000)) { throw 'Bash capture exceeded its 30-second operation budget' }
        $outText = $stdout.GetAwaiter().GetResult()
        $errText = $stderr.GetAwaiter().GetResult()
        if (-not (Test-Path -LiteralPath $argsPath) -or -not (Test-Path -LiteralPath $callsPath)) {
            throw "Bash capture missing: exit=$($process.ExitCode) stdout=$outText stderr=$errText"
        }
        $captured = [System.IO.File]::ReadAllText($argsPath).Split([char]0)
        if ($captured[-1] -cne '') { throw 'Bash argument capture is not NUL terminated' }
        $remoteCommand = $captured[-2]
        $tokens = @($remoteCommand -split '\s+')
        $refIndexes = @(for ($i = 0; $i -lt $tokens.Count; $i++) { if ($tokens[$i] -ceq '-Ref') { $i } })
        $hasRef = $refIndexes.Count -eq 1 -and $tokens[$refIndexes[0] + 1] -ceq 'rc/2026-10-18'
        $slot = if ($Schedule -ceq 'u/lndcobra/antiphon_release_candidates') { $Schedule } else { 'manual' }
        $valid = $hasRef -and @([System.IO.File]::ReadAllLines($callsPath)).Count -eq 1 -and
            $remoteCommand -cmatch ' -File C:\\src\\Antiphon\\scripts\\release-cut\.ps1 ' -and
            @($tokens | Where-Object { $_ -ceq '-ScheduleSlot' }).Count -eq 1 -and
            $tokens[[array]::IndexOf($tokens, '-ScheduleSlot') + 1] -ceq $slot -and
            @($tokens | Where-Object { $_ -ceq '-JobId' }).Count -eq 1 -and
            $tokens[[array]::IndexOf($tokens, '-JobId') + 1] -ceq 'c1336-job' -and
            $process.ExitCode -eq $SshExit
        Assert-C487 -Cond $valid -Name ('C1336 W ' + $Name) -Detail (
            "Ref count=$($refIndexes.Count); expected Ref=rc/2026-10-18 slot=$slot exit=$SshExit; actual exit=$($process.ExitCode); command=$remoteCommand; stderr=$errText")
    } finally {
        if ($started -and -not $process.HasExited) {
            $process.Kill($true)
            if (-not $process.WaitForExit(10000)) { throw 'Bash capture cleanup failed' }
        }
        $process.Dispose()
    }
}

Invoke-C1336Wrapper -Name 'own-0' -Schedule 'u/lndcobra/antiphon_release_candidates' -SshExit 0
Invoke-C1336Wrapper -Name 'own-23' -Schedule 'u/lndcobra/antiphon_release_candidates' -SshExit 23
Invoke-C1336Wrapper -Name 'missing-0' -Schedule '' -SshExit 0
Invoke-C1336Wrapper -Name 'missing-23' -Schedule '' -SshExit 23
Invoke-C1336Wrapper -Name 'foreign-0' -Schedule 'u/lndcobra/antiphon_nightly_tests' -SshExit 0
Invoke-C1336Wrapper -Name 'foreign-23' -Schedule 'u/lndcobra/antiphon_nightly_tests' -SshExit 23

$definition = Get-Content -LiteralPath (Join-Path $repoRoot 'scripts/windmill/antiphon-release-candidates.json') -Raw | ConvertFrom-Json
$schedule = Get-Content -LiteralPath (Join-Path $repoRoot 'scripts/windmill/antiphon-release-candidates.schedule.json') -Raw | ConvertFrom-Json
$scheduleOk = $schedule.enabled -is [bool] -and -not $schedule.enabled -and
    $schedule.schedule -ceq '0 30 8,16 * * *' -and $schedule.timezone -ceq 'Europe/London' -and
    $null -ne $schedule.args -and @($schedule.args.PSObject.Properties).Count -eq 0 -and
    $null -ne $definition.schema.properties -and @($definition.schema.properties.PSObject.Properties).Count -eq 0 -and
    $definition.schema.PSObject.Properties.Name -contains 'required' -and @($definition.schema.required).Count -eq 0
Assert-C487 -Cond $scheduleOk -Name 'C1336 W schedule' -Detail ($schedule | ConvertTo-Json -Compress)

# Frozen independent Git blob IDs, including Git's checkout line-ending normalization.
$git = (Get-Command git -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$masterBlobs = @(& $git -C $repoRoot hash-object -- scripts/windmill/antiphon-nightly-tests.json scripts/windmill/antiphon-nightly-tests.schedule.json)
if ($LASTEXITCODE -ne 0) { throw 'Cannot hash the master wrapper/schedule' }
$masterOk = $masterBlobs.Count -eq 2 -and
    $masterBlobs[0] -ceq '4073816bbfb057c4dcf12e051862d32441d68dc4' -and
    $masterBlobs[1] -ceq '781f8559ea2598750eb130da49bb979f09e079c0'
Assert-C487 -Cond $masterOk -Name 'C1336 W master-unchanged' -Detail ($masterBlobs -join ',')

$readme = Get-Content -LiteralPath (Join-Path $repoRoot 'scripts/windmill/README.md') -Raw
$description = [string]$definition.description
$documentationOk = $description.Contains('rc/2026-10-18') -and $readme.Contains('-Ref rc/2026-10-18') -and
    $description.Contains('rollover') -and $readme.Contains('rollover') -and
    $description.Contains('NOT REGISTERED') -and $readme.Contains('NOT REGISTERED') -and
    $description.Contains('inert') -and $description.Contains('schedule stays disabled') -and
    $readme.Contains('enabled: false') -and $description.Contains('not qualified') -and
    $readme.Contains('not qualified') -and $description -notmatch '[\x00-\x1f]' -and
    [string]$definition.summary -notmatch '[\x00-\x1f]' -and $readme -notmatch '[\x00-\x08\x0b\x0c\x0e-\x1f]'
Assert-C487 -Cond $documentationOk -Name 'C1336 W documentation'
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 9
