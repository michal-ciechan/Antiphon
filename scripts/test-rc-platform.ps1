#requires -Version 7.0
# CARD-1323: offline policy and frozen Windows authority witnesses. No publication.
param([string]$Case, [string]$ResultsDirectory)
$ErrorActionPreference = 'Stop'
foreach ($name in @('nightly-common', 'nightly-policy', 'nightly-coverage', 'release-gate', 'release-authority', 'test-selection', 'c487-harness')) {
    . "$PSScriptRoot/lib/$name.ps1"
}
$repoRoot = Split-Path -Parent $PSScriptRoot
$ResultsDirectory = New-C487Root -ResultsDirectory $ResultsDirectory

function Assert-C1323($Condition, $Name, $Detail = '') {
    Assert-C487 -Cond $Condition -Name "C1323 $Name" -Detail $Detail
}
function Copy-C1323Object($Object) { return ($Object | ConvertTo-Json -Depth 40 | ConvertFrom-Json) }
function Same-C1323Set($Actual, $Expected) {
    return ((@($Actual | Sort-Object -CaseSensitive) -join "`n") -ceq (@($Expected | Sort-Object -CaseSensitive) -join "`n"))
}
function Write-C1323Policy($Object, $Path) {
    $Object.policyHash = Get-NightlyPolicyHash $Object
    [IO.File]::WriteAllText($Path, ($Object | ConvertTo-Json -Depth 40))
}
function Invoke-C1323Git($Root, $Arguments) { return (Invoke-TestSelectionGit $Root $Arguments).Stdout.Trim() }
function Write-C1323File($Root, $Path, $Text) {
    $file = Join-Path $Root $Path
    [void](New-Item -ItemType Directory (Split-Path $file) -Force)
    [IO.File]::WriteAllText($file, $Text)
}
function New-C1323Nodes {
    $nodes = @()
    foreach ($pair in @(@('AgentTaskLandDeliveryE2ETests', 52), @('DispatchBaseWarningDeliveryE2ETests', 12), @('WorktreeRetirementDeliveryE2ETests', 5))) {
        for ($i = 1; $i -le $pair[1]; $i++) {
            $nodes += [pscustomobject]@{ uid = "$($pair[0])-$i"; class = "Antiphon.E2E.$($pair[0])"; method = 'Expanded'; categories = @('OptIn') }
        }
    }
    $nodes += [pscustomobject]@{ uid = 'unclassified'; class = 'Antiphon.E2E.NewOptInTests'; method = 'Runs'; categories = @('OptIn') }
    $nodes += [pscustomobject]@{ uid = 'legacy-live'; class = 'Antiphon.E2E.MixedTests'; method = 'Live'; categories = @() }
    $nodes += [pscustomobject]@{ uid = 'legacy-sibling'; class = 'Antiphon.E2E.MixedTests'; method = 'Offline'; categories = @() }
    return $nodes
}
function Get-C1323Required($Nodes, $Exclusions) {
    return @($Nodes | Where-Object {
        $n = [pscustomobject]@{ ClassName = $_.class; Type = $_.class; Method = $_.method; Categories = $_.categories }
        (Get-NightlyDiscoveryDisposition $n $Exclusions -ProfileAware).Disposition -eq 'required'
    } | ForEach-Object uid)
}

function Test-C1323_PlatformPolicy {
    $policy = Get-Content "$repoRoot/tests/test-execution-policy.json" -Raw | ConvertFrom-Json
    # Supply the new contract even at the pre-fix commit: this must expose the
    # global exclusion defect before the shipped schema is updated.
    $policy.schemaVersion = 4
    $policy.profiles.rc.exclusions = @($policy.profiles.rc.exclusions | Where-Object owner -ne 'CARD-1323')
    foreach ($class in @('AgentTaskLandDeliveryE2ETests', 'DispatchBaseWarningDeliveryE2ETests', 'WorktreeRetirementDeliveryE2ETests')) {
        $policy.profiles.rc.exclusions += [pscustomobject]@{ suite = 'e2e'; class = $class; platform = 'linux'; reason = 'Requires Windows ConPTY/native FakeGrok'; owner = 'CARD-1323' }
    }
    $policy.profiles.rc.exclusions += [pscustomobject]@{ suite = 'e2e'; class = 'MixedTests'; methods = @('Live'); reason = 'legacy live method'; owner = 'fixture' }
    $nodes = @(New-C1323Nodes)
    $expectedWindows = @($nodes.uid | Where-Object { $_ -ne 'legacy-live' })
    $windows = @(Get-NightlyProfileExclusions $policy 'rc' 'e2e' -Platform windows)
    $actualWindows = @(Get-C1323Required $nodes $windows)
    $retained = Same-C1323Set $actualWindows $expectedWindows
    Assert-C1323 $retained 'windows-retains-69-and-siblings' ($actualWindows -join ',')
    if (-not $retained) { return }
    $linux = @(Get-NightlyProfileExclusions $policy 'rc' 'e2e' -Platform linux)
    Assert-C1323 (Same-C1323Set (Get-C1323Required $nodes $linux) @('unclassified', 'legacy-sibling')) 'linux-excludes-exactly-69'
    $scoped = @($linux | Where-Object Owner -eq 'CARD-1323')
    Assert-C1323 ($scoped.Count -eq 3 -and @($scoped | Where-Object { -not $_.Reason -or -not $_.Owner }).Count -eq 0) 'exclusion-reasons-and-owners'
    Assert-C1323 (Same-C1323Set (Get-C1323Required $nodes @(Get-NightlyProfileExclusions $policy 'rc' 'e2e')) $expectedWindows) 'default-is-windows'
    $path = Join-Path $ResultsDirectory 'policy.json'
    Write-C1323Policy $policy $path
    $loaded = Read-NightlyExecutionPolicy -RepoRoot $repoRoot -PolicyPath $path
    Assert-C1323 ($loaded.SchemaVersion -eq 4) 'schema4-loads'
    $bytes = [IO.File]::ReadAllBytes($path)
    $authorityPolicy = ConvertFrom-ReleaseAuthorityPolicyBytes $bytes
    $expectedSuites = @('antiphon', 'session-runner', 'pty-host', 'agents-pty', 'messaging', 'client', 'scripts', 'e2e', 'markdown-pdf')
    Assert-C1323 (Same-C1323Set $authorityPolicy.RequiredSuites $expectedSuites) 'nine-suite-authority'
    Assert-C1323 (Same-C1323Set (Get-ReleaseAuthorityRequiredUids $nodes $authorityPolicy.Exclusions 'e2e') $expectedWindows) 'authority-resolves-windows'

    $invalid = @('macos', '', 'Windows', 1, $true, $null, @('linux'), @{ os = 'linux' })
    $missed = @()
    for ($i = 0; $i -lt $invalid.Count; $i++) {
        $bad = Copy-C1323Object $policy
        $row = @($bad.profiles.rc.exclusions | Where-Object owner -eq 'CARD-1323')[0]
        $row.platform = $invalid[$i]
        Write-C1323Policy $bad $path
        $read = Read-NightlyExecutionPolicy -RepoRoot $repoRoot -PolicyPath $path
        $message = ''
        try { [void](Get-NightlyProfileExclusions $read.Object 'rc' 'antiphon' -Platform windows) } catch { $message = $_.Exception.Message }
        if ($message -notmatch 'exclusion.*platform') { $missed += "$i=$message" }
    }
    Assert-C1323 ($missed.Count -eq 0) 'invalid-platforms-refuse-before-filtering' ($missed -join ';')
    $missed = @()
    foreach ($field in @('reason', 'owner')) {
        $bad = Copy-C1323Object $policy
        @($bad.profiles.rc.exclusions | Where-Object owner -eq 'CARD-1323')[0].$field = ''
        $message = ''
        try { [void](Get-NightlyProfileExclusions $bad 'rc' 'antiphon' -Platform windows) } catch { $message = $_.Exception.Message }
        if ($message -notmatch 'reason and owner') { $missed += $field }
    }
    Assert-C1323 ($missed.Count -eq 0) 'invalid-attribution-refuses-before-filtering' ($missed -join ',')
    $changed = Copy-C1323Object $policy
    @($changed.profiles.rc.exclusions | Where-Object owner -eq 'CARD-1323')[0].platform = 'windows'
    Assert-C1323 ((Get-NightlyPolicyHash $changed) -cne (Get-NightlyPolicyHash $policy)) 'platform-only-hash-changes'
    [IO.File]::WriteAllText($path, ($changed | ConvertTo-Json -Depth 40))
    $message = ''; try { [void](Read-NightlyExecutionPolicy -RepoRoot $repoRoot -PolicyPath $path) } catch { $message = $_.Exception.Message }
    Assert-C1323 ($message -match 'stale policy hash') 'stale-platform-hash-refuses'
    $message = ''; try { [void](ConvertFrom-ReleaseAuthorityPolicyBytes ([IO.File]::ReadAllBytes($path))) } catch { $message = $_.Exception.Message }
    Assert-C1323 ($message -eq 'policy-hash-stale') 'authority-stale-hash-refuses'
    $historical = Copy-C1323Object $policy
    $historical.schemaVersion = 3
    $historical.profiles.rc.exclusions = @($historical.profiles.rc.exclusions | Where-Object owner -ne 'CARD-1323')
    Write-C1323Policy $historical $path
    $old = Read-NightlyExecutionPolicy -RepoRoot $repoRoot -PolicyPath $path
    $oldAuthority = ConvertFrom-ReleaseAuthorityPolicyBytes ([IO.File]::ReadAllBytes($path))
    Assert-C1323 ($old.SchemaVersion -eq 3 -and (Same-C1323Set $oldAuthority.RequiredSuites $expectedSuites) -and (Same-C1323Set (Get-C1323Required $nodes @(Get-NightlyProfileExclusions $old.Object 'rc' 'e2e' -Platform linux)) $expectedWindows)) 'schema3-compatibility'
    $shipped = Read-NightlyExecutionPolicy -RepoRoot $repoRoot
    $shipRows = @($shipped.Object.profiles.rc.exclusions | Where-Object owner -eq 'CARD-1323')
    Assert-C1323 ($shipped.SchemaVersion -eq 4 -and $shipRows.Count -eq 3 -and @($shipRows | Where-Object { $_.platform -cne 'linux' -or $_.suite -cne 'e2e' }).Count -eq 0 -and (Same-C1323Set $shipRows.class $scoped.Class)) 'shipped-three-linux-rows'
    Assert-C1323 (@($shipped.Object.projectDispositions).Count -eq 12 -and $shipped.Object.selectionCatalog.path -ceq 'tests/test-selection-policy.json' -and @($shipped.Object.profiles.rc.exclusions | Where-Object class -eq 'ChannelOutboundIsolatedTests').Count -eq 1 -and @($shipped.Object.profiles.rc.exclusions | Where-Object owner -eq 'CARD-1313').Count -gt 0) 'master-inventory-preserved'

    $root = Join-Path $ResultsDirectory 'git-policy'
    [void](New-Item -ItemType Directory $root)
    [void](Invoke-C1323Git $root @('init', '--quiet'))
    [void](Invoke-C1323Git $root @('config', 'user.name', 'C1323 fixture'))
    [void](Invoke-C1323Git $root @('config', 'user.email', 'c1323@example.invalid'))
    [void](Invoke-C1323Git $root @('config', 'core.autocrlf', 'false'))
    [void](Invoke-C1323Git $root @('remote', 'add', 'origin', 'https://github.com/fixture/c1323'))
    foreach ($file in @('tests/test-selection-policy.json', 'scripts/select-test-rosters.ps1', 'scripts/lib/test-selection.ps1')) {
        Write-C1323File $root $file ([IO.File]::ReadAllText((Join-Path $repoRoot $file)))
    }
    Write-C1323File $root 'tests/test-execution-policy.json' ([Text.Encoding]::UTF8.GetString($bytes))
    $catalog = Read-TestSelectionCatalog "$root/tests/test-selection-policy.json" $policy.selectionCatalog.hash
    foreach ($input in @(Get-TestSelectionSorted @($catalog.sets.sourceInputs + $catalog.sets.members.source))) {
        if ($input) { Write-C1323File $root $input 'fixture source' }
    }
    [void](Invoke-C1323Git $root @('add', '--all'))
    [void](Invoke-C1323Git $root @('commit', '--quiet', '-m', 'fixture'))
    $sha = Invoke-C1323Git $root @('rev-parse', 'HEAD')
    $selection = Get-TestSelection $root $sha $sha 'rc'
    Assert-C1323 ($null -ne $selection -and $selection.candidateSha -ceq $sha) 'schema4-selector-admitted'
    Test-C1323FrozenLedger $root $sha $nodes $expectedWindows
}

function Test-C1323FrozenLedger($Root, $Sha, $Nodes, $ExpectedWindows) {
    $candidate = Join-Path $ResultsDirectory 'candidate'
    $identity = @{ RepositoryRoot = $Root; CandidateRoot = $candidate; Sha = $Sha; Repository = 'fixture/c1323'; CandidateId = 'fixture'; CandidateRef = 'fixture-ref' }
    $authority = New-ReleaseGateAuthority @identity -IntentId 'fixture-intent' -ReleaseCardId '13231323-1323-1323-1323-132313231323' -CreatedUtc ([datetime]'2026-01-01T00:00:00Z')
    $pinned = Test-ReleaseGatePinnedAuthority @identity
    Assert-C1323 ($pinned.Ok -and (Same-C1323Set (Get-ReleaseAuthorityRequiredUids $Nodes $authority.exclusions 'e2e') $ExpectedWindows)) 'pinned-authority-validates-windows' ($pinned.Reasons -join ',')
    $discovery = @{}
    foreach ($suite in $authority.requiredSuites) {
        if ($suite -in @('scripts', 'client')) { continue }
        $discovery[$suite] = @([pscustomobject]@{ uid = "$suite-ordinary"; class = 'OfflineTests'; method = 'Runs' })
    }
    $discovery['e2e'] = $Nodes
    $plan = New-ReleaseGateExecutionPlan $candidate $discovery @{ scripts = @('script'); client = @('client') }
    $dir = Get-ReleaseAuthorityDir $candidate
    $planPath = Join-Path $dir 'execution-plan.json'
    $ledgerPath = Join-Path $dir 'ledger.json'
    $start = '2026-01-01T00:01:00Z'; $end = '2026-01-01T00:02:00Z'
    $ledger = [ordered]@{
        intentId = $authority.intentId; candidateId = $authority.candidateId; candidateRef = $authority.candidateRef; sha = $Sha; runId = 'fixture-run'
        authorityDigest = $pinned.Digest; executionPlanDigest = Get-NightlyFileSha256 $planPath
        teardownSucceeded = $true; seamed = $false; noReport = $false; diagnostic = $false; selection = 'full'
        startedAt = $start; completedAt = $end; chunks = @(); rosters = @()
        prerequisites = @(@{ id = 'build'; exitCode = 0; succeeded = $true }, @{ id = 'client-bundle'; exitCode = 0; succeeded = $true }, @{ id = 'script-census'; exitCode = 0; succeeded = $true })
        prepublicationReceipt = @{
            kind = $script:ReleaseAuthorityReceiptKind; recipient = @{ kind = $script:ReleaseAuthorityRecipientKind; cardId = $authority.releaseCardId; revision = 1 }
            correlation = @{ intentId = $authority.intentId; candidateId = $authority.candidateId; sha = $Sha; runId = 'fixture-run' }; acceptedAt = $end
        }
    }
    foreach ($suite in $plan.suites) {
        $common = @{ intentId = $authority.intentId; candidateId = $authority.candidateId; sha = $Sha; runId = 'fixture-run'; startedAt = $start; completedAt = $end; exitCode = 0; suite = $suite.id }
        if ($suite.kind -eq 'roster') {
            $common.entries = @($suite.entries | ForEach-Object { @{ id = $_; outcome = 'passed' } })
            $ledger.rosters += $common
            continue
        }
        foreach ($chunk in $suite.chunks) {
            $row = $common.Clone(); $row.chunk = $chunk.id; $row.trx = "$($chunk.id).trx"
            $trx = Join-Path $candidate $row.trx
            Write-C487Trx $trx @($chunk.uids | ForEach-Object { @{ Id = $_; ClassName = $chunk.classes[0]; MethodName = 'Expanded'; Outcome = 'Passed' } })
            $row.trxSha256 = Get-NightlyFileSha256 $trx
            $row.executed = $chunk.uids.Count; $row.passed = $chunk.uids.Count; $row.failed = 0; $row.skipped = 0
            $ledger.chunks += $row
        }
    }
    # Only card I/O is supplied here; every authority, membership, digest and
    # credit decision remains production code. No HTTP or publisher is invoked.
    function Read-ReleaseGateReleaseCard {
        param($BaseUrl, $CardId)
        $description = "$script:ReleaseAuthorityCorrelationPrefix intentId=$($authority.intentId) candidateId=$($authority.candidateId) sha=$Sha runId=fixture-run"
        return @{ Ok = $true; Card = [pscustomobject]@{ id = $CardId; revisionCount = 1; description = $description } }
    }
    Write-NightlyAtomicJson $ledgerPath $ledger
    $green = [pscustomobject]@{ runId = 'fixture-run' }
    $now = [datetime]'2026-01-01T00:03:00Z'
    $valid = Test-ReleaseGateExecutionLedger $candidate $pinned $green $now
    Assert-C1323 $valid.Ok 'complete-synthetic-ledger-validates' ($valid.Reasons -join ',')
    $missing = 'AgentTaskLandDeliveryE2ETests-1'
    $chunk = @($plan.suites | Where-Object id -eq 'e2e')[0].chunks[0]
    $chunk.uids = @($chunk.uids | Where-Object { $_ -cne $missing })
    Write-NightlyAtomicJson $planPath $plan
    $ledger.executionPlanDigest = Get-NightlyFileSha256 $planPath
    $row = @($ledger.chunks | Where-Object chunk -eq $chunk.id)[0]
    $trx = Join-Path $candidate $row.trx
    Write-C487Trx $trx @($chunk.uids | ForEach-Object { @{ Id = $_; ClassName = $chunk.classes[0]; MethodName = 'Expanded'; Outcome = 'Passed' } })
    $row.trxSha256 = Get-NightlyFileSha256 $trx; $row.executed--; $row.passed--
    Write-NightlyAtomicJson $ledgerPath $ledger
    $invalid = Test-ReleaseGateExecutionLedger $candidate $pinned $green $now
    Assert-C1323 (-not $invalid.Ok -and (Same-C1323Set $invalid.Reasons @("plan-uid-missing:$missing", "uid-missing:$missing"))) 'missing-windows-uid-refuses' ($invalid.Reasons -join ',')
}

function New-C1323ExecutionFixture {
    $root = Join-Path $ResultsDirectory 'execution'
    [void][IO.Directory]::CreateDirectory($root)
    $clone = New-C487OwnedClone $root 'https://github.com/fixture/c1323'
    $policy = Get-Content "$repoRoot/tests/test-execution-policy.json" -Raw | ConvertFrom-Json
    # A one-suite offline policy keeps the executor bounded; V-1 pins the real nine.
    $policy.suites = [pscustomobject]@{ e2e = $policy.suites.e2e }
    $policy.profiles.rc.requiredSuites = @('e2e')
    $policy.projects = @('tests/Antiphon.E2E/Antiphon.E2E.csproj')
    $policy.projectDispositions = @([pscustomobject]@{ project = $policy.projects[0]; role = 'primary'; reason = 'offline fixture' })
    $policy.profiles.rc.exclusions += [pscustomobject]@{ suite = 'e2e'; class = 'MixedTests'; methods = @('Live'); reason = 'legacy live method'; owner = 'fixture' }
    Write-C1323File $clone 'tests/Antiphon.E2E/Antiphon.E2E.csproj' '<Project><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>'
    foreach ($file in @('global.json', 'tests/test-selection-policy.json', 'scripts/select-test-rosters.ps1')) {
        Write-C1323File $clone $file ([IO.File]::ReadAllText((Join-Path $repoRoot $file)))
    }
    Write-C1323Policy $policy "$clone/tests/test-execution-policy.json"
    [void](Invoke-C1323Git $clone @('init', '--quiet'))
    [void](Invoke-C1323Git $clone @('config', 'user.name', 'C1323 fixture'))
    [void](Invoke-C1323Git $clone @('config', 'user.email', 'c1323@example.invalid'))
    [void](Invoke-C1323Git $clone @('config', 'core.autocrlf', 'false'))
    [void](Invoke-C1323Git $clone @('add', '--all'))
    [void](Invoke-C1323Git $clone @('commit', '--quiet', '-m', 'offline execution fixture'))
    $sha = Invoke-C1323Git $clone @('rev-parse', 'HEAD')
    $nodes = @(New-C1323Nodes | ForEach-Object {
        @{ uid = $_.uid; type = ($_.class -replace '^Antiphon.E2E.', ''); className = $_.class; namespace = 'Antiphon.E2E'; method = $_.method; categories = $_.categories; state = 'Discovered' }
    })
    Write-NightlyAtomicJson "$root/nodes.json" $nodes
    Write-C1323File $root 'commands/npm.cmd' 'fixture launcher, never executed'
    return @{ Root = $root; Clone = $clone; Sha = $sha; Nodes = $nodes }
}

function Write-C1323ExecutionSeams($Fixture, $Platform) {
    $path = Join-Path $Fixture.Root "$Platform.ps1"
    $template = @'
$env:PATH = '__ROOT__/commands' + [IO.Path]::PathSeparator + $env:PATH
$NightlySeams = @{
    HostPlatform = { return '__PLATFORM__' }
    E2EPrerequisites = { return @{ Ok = $true; Rows = @(@{ name = 'offline-browser'; exitCode = 0 }) } }
    StartProcess = {
        param($FilePath, $ArgumentList, $WorkingDirectory, $TimeoutMilliseconds, $Environment)
        __CHILD_WRITERS__
        Add-Content -LiteralPath '__ROOT__/__PLATFORM__-commands.log' -Value ($FilePath + ' ' + ($ArgumentList -join ' '))
        $argv = @($ArgumentList)
        $nodes = @(Get-Content '__ROOT__/nodes.json' -Raw | ConvertFrom-Json)
        $diagIndex = [array]::IndexOf($argv, '--diagnostic-output-directory')
        if ($argv -contains '--list-tests') {
            Write-C487Discovery (Join-Path $argv[$diagIndex + 1] 'log.diag') $nodes
        } elseif ($argv -contains '--report-trx-filename') {
            $filterIndex = [array]::IndexOf($argv, '--treenode-filter')
            if ($filterIndex -ge 0) {
                $parts = $argv[$filterIndex + 1].Split('/')
                $nodes = @($nodes | Where-Object { $_.namespace -like $parts[2] -and $_.type -like $parts[3] -and $_.method -like $parts[4] })
            }
            foreach ($node in $nodes) { Add-Content '__ROOT__/__PLATFORM__-launched.txt' $node.uid }
            $trx = Join-Path $argv[[array]::IndexOf($argv, '--results-directory') + 1] $argv[[array]::IndexOf($argv, '--report-trx-filename') + 1]
            Write-C487Trx $trx @($nodes | ForEach-Object { @{ Id = $_.uid; ClassName = $_.className; MethodName = $_.method; Outcome = 'Passed' } })
            Write-C487Execution (Join-Path $argv[$diagIndex + 1] 'log.diag') @($nodes | ForEach-Object { @{ uid = $_.uid; type = $_.type; className = $_.className; method = $_.method; state = 'Passed' } })
            # This is child discovery output, never a precomputed census or verdict.
            Write-C487Discovery ([IO.Path]::ChangeExtension($trx, '.discovery.json')) @(Get-Content '__ROOT__/nodes.json' -Raw | ConvertFrom-Json)
        }
        return @{ ExitCode = 0; TimedOut = $false; ChildrenExited = $true; LogText = 'offline child completed' }
    }
}
'@
    # Import-NightlySeams has a local scope. Keep only the child artifact writers
    # inside the callback so a real process hop can resolve them on every call.
    $writers = @('Write-C487Trx', 'Write-C487Execution', 'Write-C487Discovery') | ForEach-Object {
        'function ' + $_ + " {`n" + (Get-Command $_ -CommandType Function).Definition + "`n}"
    }
    $text = $template.Replace('__CHILD_WRITERS__', ($writers -join "`n")).Replace('__ROOT__', $Fixture.Root.Replace("'", "''")).Replace('__PLATFORM__', $Platform)
    [IO.File]::WriteAllText($path, $text)
    return $path
}

function Test-C1323_PlatformExecution {
    foreach ($name in @('nightly-tests-impl', 'nightly-run-impl')) { . "$PSScriptRoot/lib/$name.ps1" }
    $fx = New-C1323ExecutionFixture
    $linuxSeams = Write-C1323ExecutionSeams $fx 'linux'
    $linux = Invoke-AntiphonNightlyTests -RepoRoot $fx.Clone -LogRoot "$($fx.Root)/linux-direct" -Profile rc -Sha $fx.Sha -SeamsPath $linuxSeams
    $launched = @(Get-Content "$($fx.Root)/linux-launched.txt")
    $portable = Same-C1323Set $launched @('unclassified', 'legacy-sibling')
    Assert-C1323 $portable 'linux-launches-only-portable-cases' ($launched -join ',')
    if (-not $portable) { return } # red witness: no dependent assertions obscure it
    $summary = Get-Content $linux.SummaryPath -Raw | ConvertFrom-Json
    Assert-C1323 ($summary.testsPassed -and -not $summary.coverageComplete -and $summary.coverageScope -ceq 'PlatformDiagnostic' -and $summary.platform -ceq 'linux' -and $summary.reasons -contains 'windows-rc-obligations-outstanding') 'linux-summary-is-diagnostic'
    $census = @($summary.suites | Where-Object { $_.requiredUidCount -eq 2 -and $_.excludedUidCount -eq 70 })
    Assert-C1323 ($census.Count -gt 0 -and @($census[0].exclusions | Where-Object owner -eq 'CARD-1323').Count -eq 69) 'linux-census-retains-exclusion-attribution'
    Assert-C1323 ($launched -contains 'unclassified' -and $launched -contains 'legacy-sibling' -and $launched -notcontains 'legacy-live') 'optin-and-legacy-method-semantics'

    $windowsSeams = Write-C1323ExecutionSeams $fx 'windows'
    $windows = Invoke-AntiphonNightlyTests -RepoRoot $fx.Clone -LogRoot "$($fx.Root)/windows-direct" -Profile rc -Sha $fx.Sha -SeamsPath $windowsSeams
    $launched = @(Get-Content "$($fx.Root)/windows-launched.txt")
    Assert-C1323 (Same-C1323Set $launched @($fx.Nodes.uid | Where-Object { $_ -ne 'legacy-live' })) 'windows-retains-delivery-cases' ($launched -join ',')
    $summary = Get-Content $windows.SummaryPath -Raw | ConvertFrom-Json
    Assert-C1323 ($windows.ExitCode -eq 0 -and $summary.testsPassed -and $summary.coverageComplete -and $summary.coverageScope -ceq 'Full' -and $summary.platform -ceq 'windows' -and $summary.reasons.Count -eq 0) 'windows-summary-remains-full' ($summary | ConvertTo-Json -Depth 8 -Compress)

    $releaseRoot = "$($fx.Root)/releases"
    $green = "$releaseRoot/candidates/fixture/complete-green.json"
    $masterGreen = "$($fx.Root)/master/last-complete-green.json"
    Write-C1323File $fx.Root 'releases/candidates/fixture/complete-green.json' 'candidate sentinel'
    Write-C1323File $fx.Root 'master/last-complete-green.json' 'master sentinel'
    foreach ($platform in @('linux', 'windows')) {
        $runId = "offline-$platform"
        $stateRoot = "$($fx.Root)/state-$platform"
        $request = @{ RepositoryRoot = $fx.Clone; CandidateSha = $fx.Sha; CandidateRef = 'rc/2026-10-20'; Repository = 'fixture/c1323'; Profile = 'rc'; ReleaseRoot = $releaseRoot; CandidateId = 'fixture'; RunId = $runId; SelectionMode = 'Full'; Attempt = 1 }
        $bound = New-ReleaseSelectionAttemptCore $request
        $run = Invoke-AntiphonNightlyRunCore -NoSync -CheckoutRoot $fx.Clone -StateRoot $stateRoot -LogRoot "$($fx.Root)/run-$platform" -RemoteUrl 'https://github.com/fixture/c1323' -Ref 'rc/2026-10-20' -Profile rc -Trigger rc -ExpectedSha $fx.Sha -CandidateId fixture -Repository 'fixture/c1323' -ReleaseRoot $releaseRoot -CoordinationRoot "$($fx.Root)/coordination" -RunId $runId -SeamsPath "$($fx.Root)/$platform.ps1" -SelectionAttemptPath $bound.SelectionAttemptPath -SelectionAttemptDigest $bound.SelectionAttemptDigest -SelectionPath $bound.SelectionPath -SelectionDigest $bound.SelectionDigest
        $state = Get-Content "$stateRoot/last-run.json" -Raw | ConvertFrom-Json
        if ($platform -eq 'linux') {
            $record = ConvertTo-NightlyResultRecord $run
            Assert-C1323 ($state.testsPassed -and -not $state.coverageComplete -and $state.coverageScope -ceq 'PlatformDiagnostic' -and $state.platform -ceq 'linux' -and $state.reasons -contains 'windows-rc-obligations-outstanding' -and $record.coverageScope -ceq 'PlatformDiagnostic' -and $record.reasons -contains 'windows-rc-obligations-outstanding') 'linux-state-is-diagnostic' ($state | ConvertTo-Json -Depth 4 -Compress)
            Assert-C1323 ($run.CreditKind -eq '' -and (Get-ReleaseGateCreditVerdict $state).Reasons -contains 'full-credit-scope-required') 'linux-credit-refuses'
            Assert-C1323 ([IO.File]::ReadAllText($green) -ceq 'candidate sentinel' -and [IO.File]::ReadAllText($masterGreen) -ceq 'master sentinel') 'linux-leaves-green-files-identical'
        } else {
            Assert-C1323 ($run.ExitCode -eq 0 -and $state.coverageComplete -and $state.coverageScope -ceq 'Full' -and $state.platform -ceq 'windows' -and $run.CreditKind -ceq 'rc-release' -and [IO.File]::ReadAllText($green) -cne 'candidate sentinel') 'windows-state-preserves-full-behavior' ($state | ConvertTo-Json -Depth 4 -Compress)
        }
    }
    $unknownSeams = Write-C1323ExecutionSeams $fx 'unknown'
    $unknown = Invoke-AntiphonNightlyTests -RepoRoot $fx.Clone -LogRoot "$($fx.Root)/unknown" -Profile rc -Sha $fx.Sha -SeamsPath $unknownSeams
    $refusalRecord = ConvertTo-Json (ConvertTo-NightlyResultRecord $unknown) -Compress
    $legacyRefusal = '{"nativeRunId":"","sha":"","ref":"","trigger":"","profile":"","candidateId":"","creditKind":"","localDueDate":"","policyHash":"","coverageComplete":false,"testsPassed":false,"reportDelivered":false,"exitCode":3,"summaryPath":""}'
    Assert-C1323 ($unknown.ExitCode -eq 3 -and $unknown.Refusal -ceq 'unsupported-host-platform' -and -not (Test-Path "$($fx.Root)/unknown-commands.log") -and $refusalRecord -ceq $legacyRefusal) 'unknown-platform-launches-nothing'
    Assert-C1323 ($linux.coverageScope -ceq 'PlatformDiagnostic' -and $linux.reasons -contains 'windows-rc-obligations-outstanding') 'diagnostic-return-value'
}

function Test-C1415_SkipExclusions {
    # CARD-1415: the shipped rc policy excludes exactly the by-design skipped identities, so a
    # Windows rc launch never runs them and its census stays complete; siblings stay required.
    # CARD-1425: the CARD-1281 producer-pack cases run from an in-test fixture, so they are
    # required on every platform and only the two Windows-only rows remain.
    . "$PSScriptRoot/lib/nightly-tests-impl.ps1"
    $shipped = Read-NightlyExecutionPolicy -RepoRoot $repoRoot
    $ns = 'Antiphon.Tests.Application'
    $methods = [ordered]@{
        AdvisoryChangePolicyTests = @('C1303_R12_LandRefusesUnavailableCorrectedEvidence', 'C1281_ReceiptCorrection', 'C1281_IncompleteNeverClean', 'C1281_AdvisoryDoesNotRerun')
        PipelineSpeedIntegrationTests = @('C1281_B1Authorization', 'C1281_B1ReceiptImport', 'C1281_B1CorrectionTransaction', 'C1281_WiredRoundtrip', 'C1281_WiredRoundtripDelivery')
        AgentTaskDispatchBaseGuardTests = @('C1213_RemoteSourceLandingKeepsLegacyIdentity', 'C1213_SourceLandingKeepsLegacyIdentity')
        AgentTaskDispatchFailureTests = @('a_git_timeout_fails_one_task_not_the_tick', 'A_failing_dispatch_does_not_block_the_queue')
    }
    $nodes = @()
    foreach ($class in $methods.Keys) {
        foreach ($m in $methods[$class]) {
            # C1303 R12 is argument-expanded: three UIDs, one method segment.
            $cases = if ($m -ceq 'C1303_R12_LandRefusesUnavailableCorrectedEvidence') { @('missing-blob', 'missing-bundle', 'changed-blob') } else { @('') }
            foreach ($c in $cases) {
                $nodes += [pscustomobject]@{ Uid = "$class.$m($c)"; ClassName = "$ns.$class"; Type = $class; Method = $m; Categories = @('Integration'); State = 'Discovered' }
            }
        }
    }
    $pack = @($nodes | Where-Object { $_.Method -cin @('C1303_R12_LandRefusesUnavailableCorrectedEvidence', 'C1281_ReceiptCorrection', 'C1281_IncompleteNeverClean', 'C1281_B1Authorization', 'C1281_B1ReceiptImport', 'C1281_B1CorrectionTransaction', 'C1281_WiredRoundtrip') } | ForEach-Object Uid)
    $windowsOnly = @($nodes | Where-Object { $_.Method -cin @('C1213_RemoteSourceLandingKeepsLegacyIdentity', 'a_git_timeout_fails_one_task_not_the_tick') } | ForEach-Object Uid)
    $rows = @($shipped.Object.profiles.rc.exclusions | Where-Object owner -ceq 'CARD-1415')
    Assert-C1323 ($rows.Count -eq 2 -and @($rows | Where-Object platform -cne 'windows').Count -eq 0 -and @($rows | Where-Object { [string]::IsNullOrWhiteSpace($_.reason) -or $_.suite -cne 'antiphon' -or @($_.methods).Count -eq 0 }).Count -eq 0) 'c1415-two-method-scoped-rows' ($rows | ConvertTo-Json -Depth 4 -Compress)
    $missing = @()
    foreach ($row in $rows) {
        foreach ($m in @($row.methods)) {
            $hit = @(Get-ChildItem -Path "$repoRoot/tests/Antiphon.Tests/Application" -Filter "$($row.class)*.cs" -File | Select-String -SimpleMatch "Task $m(")
            if ($hit.Count -eq 0) { $missing += "$($row.class).$m" }
        }
    }
    Assert-C1323 ($missing.Count -eq 0) 'c1415-excluded-methods-exist' ($missing -join ',')
    $packLaunched = @()
    foreach ($platform in @('windows', 'linux')) {
        $exclusions = @(Get-NightlyProfileExclusions $shipped.Object 'rc' 'antiphon' -Platform $platform)
        $expected = if ($platform -eq 'windows') { $windowsOnly } else { @() }
        $census = Get-NightlyDiscoveryCensus -DiscoveryNodes $nodes -ProfileExclusions $exclusions -ProfileAware
        $excluded = @($census.Excluded | ForEach-Object { $_.uid })
        Assert-C1323 ((Same-C1323Set $excluded $expected) -and @($census.Excluded | Where-Object owner -cne 'CARD-1415').Count -eq 0) "c1415-$platform-excludes-exactly" ($excluded -join ',')
        $plan = @(Resolve-NightlySuiteChunks -DiscoveryNodes $nodes -Chunks @([pscustomobject]@{ id = 'all'; classes = @() }) -Exclusions $exclusions -ProfileAware $true -Native $true)
        $launched = @()
        foreach ($group in $plan) {
            if ([string]::IsNullOrWhiteSpace([string]$group.exactFilter)) {
                $launched += @($nodes | Where-Object { @($group.classes) -ccontains $_.ClassName } | ForEach-Object Uid)
            } else { $launched += @($group.nodes | ForEach-Object Uid) }
        }
        $required = @($nodes.Uid | Where-Object { $expected -cnotcontains $_ })
        Assert-C1323 (Same-C1323Set $launched $required) "c1415-$platform-launches-only-required" ($launched -join ',')
        $packLaunched += @($pack | Where-Object { $launched -ccontains $_ } | ForEach-Object { "$platform/$_" })
        # The executor reports only launched cases; all passing leaves no skipped row.
        $terminal = @($launched | ForEach-Object { [pscustomobject]@{ Identity = $_; Outcome = 'Passed' } })
        $verdict = ConvertTo-NightlyCoverageVerdict -TerminalRows $terminal -ProcessExit 0 -Sha 'a' -ExpectedSha 'a' -GitRef 'r' -ExpectedRef 'r' -PolicyHash $shipped.Hash -ExpectedPolicyHash $shipped.Hash
        Assert-C1323 ($verdict.coverageComplete -and $verdict.testsPassed -and $verdict.skipped -eq 0) "c1415-$platform-census-complete" ($verdict | ConvertTo-Json -Compress)
    }
    # CARD-1425: all nine producer-pack cases are launched on both platforms.
    Assert-C1323 ($pack.Count -eq 9 -and $packLaunched.Count -eq 18) 'c1425-pack-methods-required' ($packLaunched -join ',')
    # Control: the same identities run and skipped (no exclusion) leave the census incomplete.
    $skipped = @($nodes | ForEach-Object { [pscustomobject]@{ Identity = $_.Uid; Outcome = $(if ($windowsOnly -ccontains $_.Uid) { 'Skipped' } else { 'Passed' }) } })
    $control = ConvertTo-NightlyCoverageVerdict -TerminalRows $skipped -ProcessExit 0 -Sha 'a' -ExpectedSha 'a' -GitRef 'r' -ExpectedRef 'r' -PolicyHash $shipped.Hash -ExpectedPolicyHash $shipped.Hash
    Assert-C1323 (-not $control.coverageComplete -and $control.skipped -eq 2) 'c1415-launched-skips-stay-incomplete' ($control | ConvertTo-Json -Compress)
}

try {
    switch ($Case) {
        'C1323_PlatformPolicy' { Test-C1323_PlatformPolicy }
        'C1323_PlatformExecution' { Test-C1323_PlatformExecution }
        'C1415_SkipExclusions' { Test-C1415_SkipExclusions }
        default { throw "unknown case: $Case" }
    }
} catch {
    Write-C487Fail "C1323 unexpected exception" $_.ToString()
}
finally {
    foreach ($relative in @('git-policy/.git/objects', 'execution/checkout/.git/objects')) {
        $objects = Join-Path $ResultsDirectory $relative
        if (Test-Path -LiteralPath $objects) {
            Get-ChildItem -LiteralPath $objects -Recurse -Force -File | ForEach-Object { $_.IsReadOnly = $false }
        }
    }
}
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 0
