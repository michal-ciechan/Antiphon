#requires -Version 5.1
# CARD-1514: opt-in phase timing sidecars. ASCII-only. Unset phase root is a no-op.
# TRX stays authoritative. This file never edits TRX, LogRoot summary.json, receipts,
# selection, the cost cache, or the release manifest.

# Visibility, not the script flag. A function-scoped dot-source used to set the
# flag and drop Resolve-QualificationTimingRoot when that call returned, and the
# next lock decision then failed with command-not-found. The probe is a function
# only this file defines: the path helpers (test-phase-paths.ps1) load on their
# own in processes that need nothing else from here.
if ($script:TestPhaseEvidenceLoading) { return }
$script:TestPhaseEvidenceResolver = $null
try { $script:TestPhaseEvidenceResolver = Get-Command Invoke-TestPhaseRunSummary -ErrorAction SilentlyContinue } catch { $script:TestPhaseEvidenceResolver = $null }
if ($script:TestPhaseEvidenceResolver) {
    $script:TestPhaseEvidenceLoaded = $true
    return
}
$script:TestPhaseEvidenceLoading = $true

if (-not (Get-Command Write-NightlyAtomicJson -ErrorAction SilentlyContinue)) {
    . (Join-Path $PSScriptRoot 'nightly-common.ps1')
}
. (Join-Path $PSScriptRoot 'nightly-core.ps1')

$script:TestPhaseVariableNames = @(
    'ANTIPHON_TEST_PHASE_ROOT',
    'ANTIPHON_TEST_PHASE_RUN_ID',
    'ANTIPHON_TEST_PHASE_GROUP_ID',
    'ANTIPHON_TEST_PHASE_ATTEMPT',
    'ANTIPHON_TEST_PHASE_SUITE'
)

function Remove-TestPhaseVariables {
    param($Environment)
    if ($null -eq $Environment) { return }
    foreach ($name in $script:TestPhaseVariableNames) {
        if ($Environment -is [System.Collections.IDictionary]) {
            [void]$Environment.Remove($name)
        }
    }
}

function Enable-TestPhaseExecutionEnvironment {
    param(
        [hashtable]$Environment,
        [string]$LogRoot,
        [string]$RunId,
        [string]$GroupId,
        [string]$Attempt,
        [string]$Suite,
        [string]$Profile,
        [string]$TimingRoot = ''
    )
    if ($null -eq $Environment) { $Environment = @{} }
    $explicit = [Environment]::GetEnvironmentVariable('ANTIPHON_TEST_PHASE_ROOT')
    $qualification = ($Profile -eq 'rc') -or ($Profile -eq 'nightly')
    $enable = $qualification -or (-not [string]::IsNullOrWhiteSpace($explicit)) -or (-not [string]::IsNullOrWhiteSpace($TimingRoot))
    if (-not $enable) {
        Remove-TestPhaseVariables -Environment $Environment
        return $Environment
    }
    $root = $TimingRoot
    if ([string]::IsNullOrWhiteSpace($root)) { $root = $explicit }
    if ([string]::IsNullOrWhiteSpace($root)) {
        $root = Join-Path $LogRoot 'timing'
    }
    $Environment['ANTIPHON_TEST_PHASE_ROOT'] = [string]$root
    $Environment['ANTIPHON_TEST_PHASE_RUN_ID'] = [string]$RunId
    $Environment['ANTIPHON_TEST_PHASE_GROUP_ID'] = [string]$GroupId
    $Environment['ANTIPHON_TEST_PHASE_ATTEMPT'] = [string]$Attempt
    $Environment['ANTIPHON_TEST_PHASE_SUITE'] = [string]$Suite
    return $Environment
}

function Get-TestPhaseClassBasisMap {
    $map = @{}
    $path = [string]$script:NightlyClassCostPath
    if ([string]::IsNullOrWhiteSpace($path)) {
        $path = Join-Path $PSScriptRoot 'nightly-class-costs.json'
    }
    if (-not (Test-Path -LiteralPath $path)) { return $map }
    # Parsed once per process and file content (Antiphon.Nightly/ClassCostCache.cs, shared with
    # Get-NightlyClassCostTable); every call returns its own copy.
    $entry = $null
    $full = ''
    if ($script:NightlyCoreAvailable -or (Test-NightlyCoreAvailable)) {
        $full = [System.IO.Path]::GetFullPath($path)
        $entry = [Antiphon.Nightly.ClassCostCache]::Find($full)
    }
    if ($null -ne $entry -and $null -ne $entry.Basis) { return $entry.Basis.Clone() }
    if ($null -ne $entry) { $doc = $entry.Document }
    elseif ($full) {
        $before = [Antiphon.Nightly.ClassCostCache]::Digest($full)
        $doc = [System.IO.File]::ReadAllText($path) | ConvertFrom-Json
        $entry = [Antiphon.Nightly.ClassCostCache]::Store($full, $before, $doc)
    }
    else { $doc = [System.IO.File]::ReadAllText($path) | ConvertFrom-Json }
    if ($null -ne $doc.classes) {
        foreach ($row in @($doc.classes.PSObject.Properties)) {
            $map[[string]$row.Name] = [string]$row.Value.basis
        }
    }
    if ($null -ne $entry) { $entry.Basis = $map.Clone() }
    return $map
}

function Get-TestPhaseTimingDirectory {
    param([string]$RunDirectory)
    if (-not [string]::IsNullOrWhiteSpace([string]$script:QualificationTimingRoot)) {
        return [string]$script:QualificationTimingRoot
    }
    return (Join-Path $RunDirectory 'timing')
}

function Read-TestPhaseTrxRows {
    param(
        [string]$Path,
        $Watch = $null,
        [int]$DeadlineMilliseconds = 200
    )
    if (-not $script:TestPhaseReadWarmed) { Initialize-TestPhaseReadWarmup }
    $none = [pscustomobject]@{ Rows = @(); Complete = $false; Refused = $true }
    if (-not $script:NightlyCoreAvailable) { Assert-NightlyCoreAvailable 'Read-TestPhaseTrxRows' }
    if ([string]::IsNullOrWhiteSpace($Path)) { return $none }
    if ($null -eq $Watch -and $null -ne $script:TestPhaseTrxReadWatch) { $Watch = $script:TestPhaseTrxReadWatch }
    if ($null -eq $Watch) { $Watch = [Diagnostics.Stopwatch]::StartNew() }
    if ($DeadlineMilliseconds -lt 1) { $DeadlineMilliseconds = 1 }
    $identity = Get-TestPhasePathIdentity -Path $Path
    if ($identity.Kind -ne 'regular') { return $none }
    if ($Watch.ElapsedMilliseconds -ge $DeadlineMilliseconds) { return $none }
    $opened = [Antiphon.Nightly.TestPhaseFiles]::OpenReadStream($Path)
    if ($opened.Refused -or $null -eq $opened.Stream) { return $none }
    try {
        $read = [Antiphon.TestPhase.TrxTimingScanner]::Read($opened.Stream, $Watch, $DeadlineMilliseconds, 262144, 16384)
        if ($null -eq $read -or -not $read.Complete) { return $none }
        return [pscustomobject]@{ Rows = [Antiphon.Nightly.NightlyTrx]::TimingRows($read, $Path); Complete = $true; Refused = $false }
    } catch {
        return $none
    } finally {
        try { $opened.Stream.Dispose() } catch { }
    }
}

. (Join-Path $PSScriptRoot 'test-phase-paths.ps1')
Initialize-TestPhaseLinuxIdentity

function Initialize-TestPhaseStreamReaders {
    if ('Antiphon.TestPhase.TrxTimingScanner' -as [type]) { return }
    # Antiphon.Nightly carries these types (Antiphon.Nightly/TestPhaseStreamReaders.cs); without it (Windows PowerShell 5.1,
    # ANTIPHON_NIGHTLY_CORE=off) the same source is compiled here.
    if (Test-NightlyCoreAvailable) { return }
    Add-Type -Path (Join-Path $PSScriptRoot 'Antiphon.Nightly/TestPhaseStreamReaders.cs')
}

Initialize-TestPhaseStreamReaders

# The bounded byte and JSONL readers are compiled (Antiphon.Nightly/TestPhaseFiles.cs: TestPhaseFiles.ReadBoundedBytes,
# OpenReadStream; TestPhaseReads.JsonlBounded, JsonlForSummary, Records). The whole-summary re-read budget is
# TestPhaseFiles.SummaryBudgetMilliseconds (A6).

function Write-TestPhasePlannedManifest {
    param(
        [string]$LogRoot,
        [string]$RunId,
        [string]$Attempt,
        [string]$Suite,
        [string]$SourceDigest,
        [string]$AssemblyDigest,
        [string]$PolicyDigest,
        [string]$CostFileDigest,
        [string]$AssemblyId,
        $Groups,
        $DiscoveryNodes,
        $ProfileExclusions,
        $DiscoveryIndex = $null,
        [string]$DiscoveryPath = ''
    )
    Assert-NightlyCoreAvailable 'Write-TestPhasePlannedManifest'
    $allPlanned = New-Object System.Collections.Generic.List[string]
    $entry = [pscustomobject]@{
        Document = [pscustomobject]@{ Nodes = @($DiscoveryNodes) }
        InProgress = @()
        Views = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    }
    if ($null -ne $DiscoveryIndex -and -not [string]::IsNullOrWhiteSpace($DiscoveryPath)) {
        $entry = [Antiphon.Nightly.NightlyDiscoveryIndex]::EntryOf($DiscoveryIndex, $DiscoveryPath)
    }
    $view = [Antiphon.Nightly.NightlyDiscoveryIndex]::View($entry, $ProfileExclusions, $true)
    $basisMap = Get-TestPhaseClassBasisMap
    $groupRows = @()
    foreach ($group in @($Groups)) {
        $ids = New-Object System.Collections.Generic.List[string]
        $carried = @()
        if ($group.PSObject.Properties.Name -contains 'nodes' -and $null -ne $group.nodes) {
            $carried = @($group.nodes)
        }
        if ($carried.Count -gt 0) {
            foreach ($node in $carried) {
                $uid = [string]$node.Uid
                if (-not [string]::IsNullOrWhiteSpace($uid)) { $ids.Add($uid) }
            }
        } else {
            $classes = @()
            foreach ($c in @($group.classes)) { $classes += [string]$c }
            [Antiphon.Nightly.PlannedManifestCheck]::AddStrings($ids, @([Antiphon.Nightly.NightlyDiscoveryIndex]::RequiredUids($view, $classes)))
        }
        $allPlanned.AddRange($ids)
        $bases = @{}
        foreach ($className in @($group.classes)) {
            $key = [string]$className
            $basis = 'model'
            if ($basisMap.ContainsKey($key)) { $basis = [string]$basisMap[$key] }
            elseif ($null -ne $script:NightlyClassCostTable -and $script:NightlyClassCostTable.ContainsKey($key)) { $basis = 'measured' }
            $bases[$basis] = $true
        }
        $pricing = 'model'
        $basisNames = @($bases.Keys)
        if ($basisNames.Count -eq 1) { $pricing = [string]$basisNames[0] }
        elseif ($basisNames.Count -gt 1) { $pricing = 'mixed' }
        $filter = ''
        if ($group.PSObject.Properties.Name -contains 'exactFilter' -and -not [string]::IsNullOrWhiteSpace([string]$group.exactFilter)) {
            $filter = [string]$group.exactFilter
        } elseif ($group.PSObject.Properties.Name -contains 'groupFilter') {
            $filter = [string]$group.groupFilter
        }
        $seconds = 0
        if ($group.PSObject.Properties.Name -contains 'seconds' -and $null -ne $group.seconds) {
            $seconds = [double]$group.seconds
        }
        $groupRows += [ordered]@{
            chunk = [string]$group.id
            classes = @($group.classes | ForEach-Object { [string]$_ })
            filter = $filter
            plannedTestIds = @($ids.ToArray())
            plannedSeconds = $seconds
            pricingBasis = $pricing
            sharedManifestOrder = $true
        }
    }
    # Union of planned UIDs (OrdinalIgnoreCase, as the former @{}), missing in RequiredUids order, extra
    # against an InvariantCultureIgnoreCase set, extra in first-planned order (compiled).
    $partition = [Antiphon.Nightly.PlannedManifestCheck]::Check($allPlanned, @($view.RequiredUids))
    $duplicate = [bool]$partition.Duplicate
    $missing = $partition.Missing
    $extra = $partition.Extra
    $manifest = [ordered]@{
        version = 1
        runId = [string]$RunId
        attempt = [string]$Attempt
        suite = [string]$Suite
        sourceDigest = [string]$SourceDigest
        assemblyDigest = [string]$AssemblyDigest
        policyDigest = [string]$PolicyDigest
        costFileDigest = [string]$CostFileDigest
        assembly = [string]$AssemblyId
        requiredPartitionExact = ((-not $duplicate) -and $missing.Count -eq 0 -and $extra.Count -eq 0)
        missingIds = @($missing)
        extraIds = @($extra)
        groups = @($groupRows)
    }
    $dir = Get-TestPhaseTimingDirectory -RunDirectory $LogRoot
    $text = [Antiphon.Nightly.TestPhaseJson]::Text($manifest, 0)
    $path = Join-Path $dir 'groups-planned.json'
    [Antiphon.Nightly.TestPhaseFiles]::WriteTextAtomic($path, $text)
    # Keep the singleton for existing readers. Also retain every allowlisted suite.
    if ($Suite -match '^[a-z0-9][a-z0-9-]{0,40}$') {
        $suiteFile = 'groups-planned-' + $Suite + '.json'
        [Antiphon.Nightly.TestPhaseFiles]::WriteTextAtomic((Join-Path $dir $suiteFile), $text)
        $filters = New-Object System.Collections.Generic.List[string]
        foreach ($plannedRow in @($groupRows)) {
            if ($null -eq $plannedRow) { continue }
            $filterText = ''
            if ($plannedRow.PSObject.Properties.Name -contains 'filter' -and $null -ne $plannedRow.filter) { $filterText = [string]$plannedRow.filter }
            $filters.Add($filterText)
        }
        Update-TestPhasePlannedIndex -Directory $dir -Suite $Suite -FileName $suiteFile -RunId $RunId -Attempt $Attempt -AssemblyId $AssemblyId -SourceDigest $SourceDigest -PolicyDigest $PolicyDigest -CostFileDigest $CostFileDigest -AssemblyDigest $AssemblyDigest -Filters @($filters.ToArray())
    }
    return $path
}

function Write-TestPhaseFailureMarker {
    param([string]$RunDirectory, [string]$Code)
    $dir = Get-TestPhaseTimingDirectory -RunDirectory $RunDirectory
    try {
        if (-not (Test-Path -LiteralPath $dir)) {
            New-Item -ItemType Directory -Path $dir -Force | Out-Null
        }
        $path = Join-Path $dir 'phase-summary-failed'
        Write-NightlyEvidenceText $path $Code
    } catch {
        # An unwritable timing directory must not change the caller's verdict.
    }
}

# The executed chunk (TestPhaseJournal.FreshChunk / SettledChunk) and the journal head (time zones, the writer
# environment, the unsafe-directory check, the commit seams, contexts, roots, seed rows and cache, envelopes, the
# generation prefix and metadata, empty and seed generations, the projection, gap memory and existing-generation
# import) are compiled: Antiphon.Nightly/TestPhaseJournal.cs. So is the publication on its context (open and close,
# the commit file, envelopes, the prefix rescan, gap records, the executed-chunk append, the publication state, the
# projection, the live prefix reader and recovery): Antiphon.Nightly/TestPhasePublication.cs. A caller that opens a
# writer context binds it ($script:TestPhaseExecutedContext) and unbinds it once it is closed.

# What the compiled publication reads from this script scope: the process timing root, this host's edition and
# version (the writer environment) and the commit seams ($script:TestPhaseCommitBarrier, $script:TestPhaseCommitFault).
function Get-TestPhasePublicationScope {
    return [Antiphon.Nightly.TestPhasePublicationScope]::new([string]$script:QualificationTimingRoot, [string]$PSVersionTable.PSEdition, [string]$PSVersionTable.PSVersion, $script:TestPhaseCommitBarrier, $script:TestPhaseCommitFault)
}

function Write-TestPhaseExecutedManifest {
    param(
        $Verdict,
        [string]$LogRoot,
        [string]$TrxPath,
        [string]$Suite,
        [string]$Chunk,
        [string]$RunId,
        [string]$Attempt,
        [double]$HostWallSeconds = 0
    )
    $rows = @()
    $trxIdentity = Get-TestPhasePathIdentity -Path $TrxPath
    $trxMissing = ($trxIdentity.Kind -ne 'regular')
    if ($trxIdentity.Kind -eq 'regular') {
        $trxRead = Read-TestPhaseTrxRows -Path $TrxPath
        if ($null -ne $trxRead -and $trxRead.Complete -and -not $trxRead.Refused) {
            $rows = @($trxRead.Rows)
        }
    }
    Assert-NightlyCoreAvailable 'Write-TestPhaseExecutedManifest'
    # One row per test (about 19,000 in the main suite): NightlyCore.cs TestPhaseSummary.OutcomeRows.
    $outcomeRows = [Antiphon.Nightly.TestPhaseSummary]::OutcomeRows([object[]]$rows, 'TestId', 'Outcome', $false)
    $passed = [Antiphon.Nightly.TestPhaseSummary]::PassedCount($outcomeRows)
    $digest = ''
    if ($trxIdentity.Kind -eq 'regular') {
        try { $digest = Get-NightlyFileSha256 -Path $TrxPath } catch { $digest = '' }
    }
    Write-TestPhaseOwnerObservation -Verdict $Verdict -LogRoot $LogRoot -Suite $Suite -Chunk $Chunk -RunId $RunId -Attempt $Attempt
    $exitCode = $null
    $coverageComplete = $null
    $testsPassed = $null
    if ($null -ne $Verdict) {
        if ($Verdict.PSObject.Properties.Name -contains 'ExitCode') { $exitCode = $Verdict.ExitCode }
        if ($Verdict.PSObject.Properties.Name -contains 'coverageComplete') { $coverageComplete = [bool]$Verdict.coverageComplete }
        if ($Verdict.PSObject.Properties.Name -contains 'testsPassed') { $testsPassed = [bool]$Verdict.testsPassed }
    }
    $fresh = [Antiphon.Nightly.TestPhaseJournal]::FreshChunk($Chunk, $RunId, $Attempt, $Suite, $HostWallSeconds, $trxMissing, [int]$passed, @($outcomeRows), ([Antiphon.Nightly.TestPhaseText]::RelativePath($LogRoot, $TrxPath)), $digest, $exitCode, $coverageComplete, $testsPassed)
    # Into the bound context, else one opened, published, projected and closed (compiled: TestPhasePublication.PublishChunk).
    [void][Antiphon.Nightly.TestPhasePublication]::PublishChunk($script:TestPhaseExecutedContext, $LogRoot, $fresh, $Suite, $Chunk, $RunId, $Attempt, (Get-TestPhasePublicationScope))
}

function Complete-TestPhaseLaunch {
    param(
        $Verdict,
        [string]$LogRoot,
        [string]$TrxPath,
        [string]$Suite,
        [string]$Chunk,
        [string]$RunId,
        [string]$Attempt,
        [double]$HostWallSeconds = 0
    )
    try {
        if ($script:TestPhaseSeams -and $script:TestPhaseSeams.Summary) {
            & $script:TestPhaseSeams.Summary
        }
        Write-TestPhaseExecutedManifest -Verdict $Verdict -LogRoot $LogRoot -TrxPath $TrxPath -Suite $Suite -Chunk $Chunk -RunId $RunId -Attempt $Attempt -HostWallSeconds $HostWallSeconds
        $live = $script:TestPhaseExecutedContext
        if ($null -ne $live -and -not $live.Closed -and -not $live.Refused) {
            [void][Antiphon.Nightly.TestPhasePublication]::CompleteProjection($live, '', (Get-TestPhasePublicationScope))
        }
        $planned = Join-Path (Get-TestPhaseTimingDirectory -RunDirectory $LogRoot) 'groups-planned.json'
        if (Test-NightlyEvidencePath $planned) {
            Invoke-TestPhaseRunSummary -RunDirectory $LogRoot -Top 50
        }
    } catch {
        Write-TestPhaseFailureMarker -RunDirectory $LogRoot -Code 'phase-summary-failed'
    }
    return $Verdict
}

function ConvertTo-TestPhaseSummaryRows {
    param($Planned, [string]$RunDirectory)
    # The row loops are compiled (NightlyCore.cs TestPhaseSummary, TestPhaseJournalRead, TestPhaseSummaryFast).
    Assert-NightlyCoreAvailable 'ConvertTo-TestPhaseSummaryRows'
    $quality = New-Object System.Collections.Generic.List[string]
    $torn = 0
    $malformed = 0
    $watch = New-Object System.Diagnostics.Stopwatch
    if ($null -ne $script:TestPhaseSummaryReReadWatch) { $watch = $script:TestPhaseSummaryReReadWatch }
    $budget = [Antiphon.Nightly.TestPhaseReadBudget]::For($watch, [Antiphon.Nightly.TestPhaseFiles]::SummaryBudgetMilliseconds, 8388608)
    $plannedDocs = @(Read-TestPhasePlannedDocuments -RunDirectory $RunDirectory)
    $shards = @()
    $timing = Get-TestPhaseTimingDirectory -RunDirectory $RunDirectory
    Sync-NightlyDeferredEvidenceWrites
    if (Test-Path -LiteralPath $timing) {
        $shards = @(Get-ChildItem -LiteralPath $timing -Filter 'cases-*.jsonl' -File -ErrorAction SilentlyContinue)
        foreach ($chunkDir in @(Get-ChildItem -LiteralPath $timing -Force -Filter 'chunk-*' -ErrorAction SilentlyContinue)) {
            $kind = [Antiphon.Nightly.QualificationTiming]::DirectoryKind($chunkDir.FullName)
            if ($kind -eq 'symlink') {
                if (-not $quality.Contains('chunk-link-refused')) { $quality.Add('chunk-link-refused') }
                continue
            }
            if ($kind -ne 'directory') { continue }
            $shards += @(Get-ChildItem -LiteralPath $chunkDir.FullName -Filter 'cases-*.jsonl' -File -ErrorAction SilentlyContinue)
        }
    }
    $shards = @($shards | Sort-Object { [string]$_.Name })
    $completes = @{}
    $starts = @{}
    $fixtures = @()
    $seenComplete = @{}
    foreach ($shard in $shards) {
        $parsed = [Antiphon.Nightly.TestPhaseReads]::JsonlForSummary($shard.FullName, $budget)
        $torn += [int]$parsed.Torn
        $malformed += [int]$parsed.Malformed
        $header = $null
        foreach ($rec in @($parsed.Records)) {
            if ([string]$rec.recordKind -eq 'header') { $header = $rec; break }
        }
        $planForShard = $Planned
        $headerSuite = ''
        if ($null -ne $header -and $header.PSObject.Properties.Name -contains 'suite') { $headerSuite = [string]$header.suite }
        if (-not [string]::IsNullOrWhiteSpace($headerSuite)) {
            foreach ($plannedDoc in @($plannedDocs)) {
                if ($null -eq $plannedDoc) { continue }
                $docSuite = ''
                if ($plannedDoc.PSObject.Properties.Name -contains 'suite') { $docSuite = [string]$plannedDoc.suite }
                if ($docSuite -eq $headerSuite) { $planForShard = $plannedDoc; break }
            }
        }
        $stale = $false
        if ($null -ne $header -and $null -ne $planForShard) {
            if (-not [string]::IsNullOrWhiteSpace([string]$planForShard.runId) -and [string]$header.runId -ne [string]$planForShard.runId) { $stale = $true }
            if (-not [string]::IsNullOrWhiteSpace([string]$planForShard.assembly) -and [string]$header.assembly -ne [string]$planForShard.assembly) { $stale = $true }
        }
        if ($stale) {
            if (-not $quality.Contains('stale-input-not-joined')) { $quality.Add('stale-input-not-joined') }
            continue
        }
        foreach ($rec in @($parsed.Records)) {
            $kind = [string]$rec.recordKind
            if ($kind -eq 'fixture') { $fixtures += $rec; continue }
            $id = [string]$rec.testId
            if ([string]::IsNullOrWhiteSpace($id)) { continue }
            $evidenceKey = [Antiphon.Nightly.TestPhaseText]::EvidenceKey($header, $id)
            if ($kind -eq 'start') { $starts[$evidenceKey] = $rec; continue }
            if ($kind -ne 'complete') { continue }
            if ($seenComplete.ContainsKey($evidenceKey)) {
                $prior = $completes[$evidenceKey]
                if ([string]$prior.Record.outcome -ne [string]$rec.outcome) {
                    $prior.Conflict = $true
                    if (-not $quality.Contains('conflicting-duplicate')) { $quality.Add('conflicting-duplicate') }
                }
            } else {
                $seenComplete[$evidenceKey] = $true
                $completes[$evidenceKey] = [pscustomobject]@{ Record = $rec; Header = $header; Conflict = $false }
            }
        }
    }
    if ($torn -gt 0 -and -not $quality.Contains('torn-tail')) { $quality.Add('torn-tail') }
    if ($malformed -gt 0 -and -not $quality.Contains('malformed-line')) { $quality.Add('malformed-line') }

    $trxGroups = @{}
    $trxSuites = @{}
    $journalPrefix = $null
    $currentPath = Join-Path ([Antiphon.Nightly.TestPhaseJournal]::JournalRoot($timing)) 'current.json'
    $hasJournal = (Get-TestPhasePathIdentity -Path $currentPath).Kind -eq 'regular'
    if ($hasJournal) {
        $current = [Antiphon.Nightly.TestPhaseJournal]::ReadJsonFile($currentPath)
        if ($null -ne $current -and -not [string]::IsNullOrWhiteSpace([string]$current.generation)) {
            $journalPrefix = [Antiphon.Nightly.TestPhaseJournal]::ReadGenerationPrefix((Join-Path ([Antiphon.Nightly.TestPhaseJournal]::JournalRoot($timing)) ([string]$current.generation)))
            # Use the validated seed and retained records, including predecessor rows.
            # This read-only context never acquires writer ownership or projects files.
            $snapshot = [Antiphon.Nightly.TestPhaseJournal]::RefusedContext('')
            $snapshot.Refused = $false
            $snapshot.TimingRoot = $timing
            $snapshot.Generation = [string]$current.generation
            $snapshot.Records = $journalPrefix.Records
            [void][Antiphon.Nightly.TestPhaseJournal]::ReadSeedCache($snapshot, [string]$current.generation)
            $reduced = [Antiphon.Nightly.TestPhaseJournalRead]::Reduce($snapshot)
            for ($entryIndex = 0; $entryIndex -lt $reduced.Entries.Count; $entryIndex++) {
                $entry = $reduced.Entries[$entryIndex]
                $rowJson = [string]$entry.Json
                if ($entryIndex -eq ($reduced.Entries.Count - 1)) { $rowJson = [string]$entry.Fresh }
                if ([string]::IsNullOrWhiteSpace($rowJson)) { continue }
                # Only trx, chunk and suite are read, from a strict parse of the document; a document the core
                # cannot read (not one strict JSON object, a non-string value) is skipped like an unparseable one.
                $fields = [Antiphon.Nightly.TestPhaseJournalRead]::ChunkFields($rowJson)
                if ($null -eq $fields) { continue }
                $trxRel = [string]$fields[0]
                if ([string]::IsNullOrWhiteSpace($trxRel)) { continue }
                $fullTrx = [System.IO.Path]::GetFullPath((Join-Path $RunDirectory $trxRel))
                $trxGroups[$fullTrx] = [string]$fields[1]
                $trxSuites[$fullTrx] = [string]$fields[2]
            }
        }
    }
    $executedPath = Join-Path $timing 'groups-executed.json'
    if ($null -eq $journalPrefix -and (Get-TestPhasePathIdentity -Path $executedPath).Kind -eq 'regular') {
        $executed = (Read-NightlyEvidenceText $executedPath) | ConvertFrom-Json
        foreach ($chunk in @($executed.chunks)) {
            if (-not [string]::IsNullOrWhiteSpace([string]$chunk.trx)) {
                $fullTrx = [System.IO.Path]::GetFullPath((Join-Path $RunDirectory ([string]$chunk.trx)))
                $trxGroups[$fullTrx] = [string]$chunk.chunk
                $suiteText = ''
                if ($chunk.PSObject.Properties.Name -contains 'suite') { $suiteText = [string]$chunk.suite }
                $trxSuites[$fullTrx] = $suiteText
            }
        }
    }
    $trxRows = New-Object System.Collections.Generic.List[object]
    $trxFiles = @(Get-ChildItem -LiteralPath $RunDirectory -Filter '*.trx' -File -ErrorAction SilentlyContinue)
    foreach ($trx in $trxFiles) {
        if ((Get-TestPhasePathIdentity -Path $trx.FullName).Kind -ne 'regular') { continue }
        $trxRead = Read-TestPhaseTrxRows -Path $trx.FullName
        if ($null -eq $trxRead -or -not $trxRead.Complete -or $trxRead.Refused) { continue }
        $group = ''
        if ($trxGroups.ContainsKey($trx.FullName)) { $group = [string]$trxGroups[$trx.FullName] }
        $suiteText = ''
        if ($trxSuites.ContainsKey($trx.FullName)) { $suiteText = [string]$trxSuites[$trx.FullName] }
        [Antiphon.Nightly.TestPhaseSummary]::TagRows(@($trxRead.Rows), $group, $suiteText)
        $trxRows.AddRange([object[]]@($trxRead.Rows))
    }
    $nativeMap = [Antiphon.Nightly.TestPhaseSummary]::MapNative($trxRows.ToArray())

    $tests = New-Object System.Collections.Generic.List[object]
    $joinedTrx = @{}
    $completeTestIds = @{}
    foreach ($completeKey in @($completes.Keys)) {
        $completeTestIds[[string]$completes[$completeKey].Record.testId] = $true
    }
    foreach ($completeKey in @($completes.Keys)) {
        $bag = $completes[$completeKey]
        $id = [string]$bag.Record.testId
        $headerSuite = ''
        if ($null -ne $bag.Header -and $bag.Header.PSObject.Properties.Name -contains 'suite') {
            $headerSuite = [string]$bag.Header.suite
        }
        $matches = @()
        if ($nativeMap.ContainsKey([string]$id)) { $matches = @($nativeMap[[string]$id].ToArray()) }
        $filtered = New-Object System.Collections.Generic.List[object]
        foreach ($match in $matches) {
            $trxSuite = ''
            if ($match.PSObject.Properties.Name -contains 'Suite') { $trxSuite = [string]$match.Suite }
            $bothSuites = (-not [string]::IsNullOrWhiteSpace($headerSuite)) -and (-not [string]::IsNullOrWhiteSpace($trxSuite))
            if ($bothSuites -and ($headerSuite -ne $trxSuite)) { continue }
            $filtered.Add($match)
        }
        $matches = @($filtered.ToArray())
        $distinct = @{}
        foreach ($match in $matches) { $distinct[[string]$match.TestId + '|' + [string]$match.Path] = $match }
        $matchRows = @($distinct.Values)
        $joined = $matchRows.Count -eq 1 -and -not $bag.Conflict
        $trxOutcome = ''
        $phaseOutcome = ''
        $duration = $null
        if ($bag.Conflict) {
            $phaseOutcome = 'conflict'
            if (-not $quality.Contains('conflicting-duplicate')) { $quality.Add('conflicting-duplicate') }
        } else {
            $phaseOutcome = [string]$bag.Record.outcome
        }
        if ($matchRows.Count -gt 1) {
            $joined = $false
            if (-not $quality.Contains('ambiguous-id')) { $quality.Add('ambiguous-id') }
        } elseif ($matchRows.Count -eq 1) {
            $trxOutcome = [string]$matchRows[0].Outcome
            $duration = [string]$matchRows[0].Duration
            $joinedTrx[[string]$matchRows[0].TestId + '|' + [string]$matchRows[0].Path] = $true
        }
        $outcome = 'incomplete'
        $incomplete = $true
        if ($joined) {
            if ($trxOutcome -eq $phaseOutcome -and -not [string]::IsNullOrWhiteSpace($trxOutcome)) {
                $outcome = $trxOutcome
                $incomplete = $false
            } else {
                $outcome = 'incomplete'
                if (-not $quality.Contains('outcome-disagreement')) { $quality.Add('outcome-disagreement') }
            }
        } else {
            if (-not $quality.Contains('missing-terminal-remains-incomplete')) { $quality.Add('missing-terminal-remains-incomplete') }
        }
        $src = $bag.Record
        $ex = $src.exclusive
        $tests.Add([ordered]@{
            testId = [string]$id
            suite = [string]$headerSuite
            className = [string]$src.className
            method = [string]$src.method
            duration = $duration
            groupId = [string]$bag.Header.groupId
            outcome = $outcome
            trxOutcome = $trxOutcome
            phaseOutcome = $phaseOutcome
            joined = [bool]$joined
            incomplete = [bool]$incomplete
            countsKnown = $true
            db = $(if ($ex) { [long]$ex.db } else { 0 })
            host = $(if ($ex) { [long]$ex.host } else { 0 })
            process = $(if ($ex) { [long]$ex.process } else { 0 })
            fs = $(if ($ex) { [long]$ex.fs } else { 0 })
            network = $(if ($ex) { [long]$ex.network } else { 0 })
            fixture = $(if ($ex) { [long]$ex.fixture } else { 0 })
            overlapMs = $(if ($src.overlapMs) { [long]$src.overlapMs } else { 0 })
            remainderMs = $(if ($src.remainderMs) { [long]$src.remainderMs } else { 0 })
            droppedPhaseCount = $(if ($src.droppedPhaseCount) { [int]$src.droppedPhaseCount } else { 0 })
        })
    }
    foreach ($startKey in @($starts.Keys)) {
        if ($completes.ContainsKey([string]$startKey)) { continue }
        $startRec = $starts[$startKey]
        $startId = [string]$startRec.testId
        $startSuite = ''
        if (-not $quality.Contains('missing-terminal-remains-incomplete')) { $quality.Add('missing-terminal-remains-incomplete') }
        $tests.Add([ordered]@{
            testId = [string]$startId
            suite = [string]$startSuite
            className = ''
            method = ''
            duration = $null
            groupId = $null
            outcome = 'incomplete'
            trxOutcome = ''
            phaseOutcome = ''
            joined = $false
            incomplete = $true
            countsKnown = $false
            db = 0; host = 0; process = 0; fs = 0; network = 0; fixture = 0
            overlapMs = 0; remainderMs = 0; droppedPhaseCount = 0
        })
    }
    $unjoined = [Antiphon.Nightly.TestPhaseSummary]::UnjoinedTrxRows($trxRows.ToArray(), $joinedTrx, $completeTestIds)
    $tests.AddRange([object[]]@($unjoined.Rows))
    if ($unjoined.PassedWithoutPhase -and -not $quality.Contains('missing-phase-unknown')) { $quality.Add('missing-phase-unknown') }
    $shared = @()
    foreach ($fix in $fixtures) {
        $ex = $fix.exclusive
        $shared += [ordered]@{
            fixtureId = [string]$fix.fixtureId
            db = $(if ($ex) { [long]$ex.db } else { 0 })
            host = $(if ($ex) { [long]$ex.host } else { 0 })
            process = $(if ($ex) { [long]$ex.process } else { 0 })
            fs = $(if ($ex) { [long]$ex.fs } else { 0 })
            network = $(if ($ex) { [long]$ex.network } else { 0 })
            fixture = $(if ($ex) { [long]$ex.fixture } else { 0 })
        }
    }
    foreach ($token in @([Antiphon.Nightly.TestPhaseJournal]::PublicationQuality($RunDirectory, [string]$script:QualificationTimingRoot, $journalPrefix))) {
        if (-not $quality.Contains([string]$token)) { $quality.Add([string]$token) }
    }
    $complete = $quality.Count -eq 0
    if ([Antiphon.Nightly.TestPhaseSummaryFast]::AnyIncomplete($tests)) { $complete = $false }
    $spent = 0
    if ($null -ne $budget.Watch) { $spent = [int]$budget.Watch.ElapsedMilliseconds }
    return [pscustomobject]@{
        Quality = @($quality.ToArray())
        Torn = $torn
        Malformed = $malformed
        Tests = $tests.ToArray()
        Shared = @($shared)
        Complete = [bool]$complete
        SummaryAggregationBudgetMilliseconds = [int]$budget.Milliseconds
        SummaryAggregationMilliseconds = [int]$spent
    }
}

function Write-TestPhaseSummaryFiles {
    param($Planned, $Report, [string]$RunDirectory, [int]$Top)
    # The sorts, summary.json and the tests.csv rows are compiled (TestPhaseSummaryWrite.cs, TestPhaseSummaryFast).
    Assert-NightlyCoreAvailable 'Write-TestPhaseSummaryFiles'
    $timing = Get-TestPhaseTimingDirectory -RunDirectory $RunDirectory
    if (-not (Test-Path -LiteralPath $timing)) {
        New-Item -ItemType Directory -Path $timing -Force | Out-Null
    }
    # @($Report.Tests | Sort-Object { [string]$_.testId })
    $orderedTests = [Antiphon.Nightly.TestPhaseSummaryWrite]::SortByTestId($Report.Tests)
    $aggregationBudget = [Antiphon.Nightly.TestPhaseFiles]::SummaryBudgetMilliseconds
    $aggregationSpent = 0
    if ($null -ne $Report -and $Report.PSObject.Properties.Name -contains 'SummaryAggregationBudgetMilliseconds' -and $null -ne $Report.SummaryAggregationBudgetMilliseconds) {
        $aggregationBudget = [int]$Report.SummaryAggregationBudgetMilliseconds
    }
    if ($null -ne $Report -and $Report.PSObject.Properties.Name -contains 'SummaryAggregationMilliseconds' -and $null -ne $Report.SummaryAggregationMilliseconds) {
        $aggregationSpent = [int]$Report.SummaryAggregationMilliseconds
    }
    $summary = [ordered]@{
        version = 1
        complete = [bool]$Report.Complete
        quality = @($Report.Quality)
        tornLines = [int]$Report.Torn
        malformedLines = [int]$Report.Malformed
        summaryAggregationBudgetMilliseconds = [int]$aggregationBudget
        summaryAggregationMilliseconds = [int]$aggregationSpent
        tests = @($orderedTests)
        sharedFixtures = @($Report.Shared)
    }
    # Diagnostic summary only: avoid hundreds of thousands of PowerShell
    # recursive serializer calls for a complete native roster. Authority writers
    # retain their original canonical serializer and exact bytes.
    # ConvertTo-Json -InputObject $summary -Depth 50 -Compress
    [Antiphon.Nightly.TestPhaseFiles]::WriteTextAtomic((Join-Path $timing 'summary.json'), ([Antiphon.Nightly.TestPhaseSummaryWrite]::SummaryJson($summary)))

    $headers = @('testId','duration','groupId','outcome','trxOutcome','phaseOutcome','joined','incomplete','db','host','process','fs','network','fixture','overlapMs','remainderMs','droppedPhaseCount')
    $lines = New-Object System.Collections.Generic.List[string]
    $headCells = @()
    foreach ($h in $headers) { $headCells += ([Antiphon.Nightly.TestPhaseSummary]::Cell($h)) }
    $lines.Add(($headCells -join ','))
    $lines.AddRange([Antiphon.Nightly.TestPhaseSummaryFast]::Rows($orderedTests, [string[]]$headers))
    Write-NightlyEvidenceLines (Join-Path $timing 'tests.csv') $lines.ToArray()

    $limit = $Top
    if ($limit -lt 1) { $limit = 1 }
    # @($orderedTests | Sort-Object { -1 * ([long]$_.db + ... + [long]$_.remainderMs) })
    $ranked = [Antiphon.Nightly.TestPhaseSummaryWrite]::RankByTotal($orderedTests)
    $md = New-Object System.Collections.Generic.List[string]
    $md.Add('# Phase timing')
    $md.Add('')
    $md.Add('TRX remains authoritative. Sidecars are private and are not gate inputs.')
    $md.Add('')
    $md.Add('| test | outcome | exclusive+overlap+remainder |')
    $md.Add('| --- | --- | --- |')
    $shown = 0
    foreach ($test in $ranked) {
        if ($shown -ge $limit) { break }
        $shown++
        $total = [long]$test.db + [long]$test.host + [long]$test.process + [long]$test.fs + [long]$test.network + [long]$test.fixture + [long]$test.overlapMs + [long]$test.remainderMs
        $md.Add('| ' + ([Antiphon.Nightly.TestPhaseText]::MarkdownCell($test.testId)) + ' | ' + ([Antiphon.Nightly.TestPhaseText]::MarkdownCell($test.outcome)) + ' | ' + ([Antiphon.Nightly.TestPhaseText]::MarkdownCell($total)) + ' |')
    }
    $md.Add('')
    $md.Add('Shared fixture rows are separate from per-test totals.')
    Write-NightlyEvidenceLines (Join-Path $timing 'summary.md') $md.ToArray()
}

function Invoke-TestPhaseRunSummary {
    param([string]$RunDirectory, [int]$Top = 50)
    $plannedPath = Join-Path (Get-TestPhaseTimingDirectory -RunDirectory $RunDirectory) 'groups-planned.json'
    $docs = @(Read-TestPhasePlannedDocuments -RunDirectory $RunDirectory)
    if ($docs.Count -lt 1) { return }
    try {
        # A historical singleton is one suite. It stays the stale-check document
        # and is not treated as the membership of every other suite.
        $planned = $docs[0]
        if ((Get-TestPhasePathIdentity -Path $plannedPath).Kind -eq 'regular') {
            $planned = (Read-NightlyEvidenceText $plannedPath) | ConvertFrom-Json
        }
        $report = ConvertTo-TestPhaseSummaryRows -Planned $planned -RunDirectory $RunDirectory
        Write-TestPhaseSummaryFiles -Planned $planned -Report $report -RunDirectory $RunDirectory -Top $Top
    } catch {
        Write-TestPhaseFailureMarker -RunDirectory $RunDirectory -Code 'phase-summary-failed'
    }
}

function Write-TestPhaseOwnerObservation {
    param($Verdict, [string]$LogRoot, [string]$Suite, [string]$Chunk, [string]$RunId = '', [string]$Attempt = '')
    try {
        Assert-NightlyCoreAvailable 'Write-TestPhaseOwnerObservation'
        # The document (or null: nothing to write) from TestPhaseRecords.OwnerDocument; naming, the
        # existing-file refusal and the write stay here.
        $doc = [Antiphon.Nightly.TestPhaseRecords]::OwnerDocument($Verdict, $Suite, $Chunk, $RunId, $Attempt)
        if ($null -eq $doc) { return }
        $dir = Get-TestPhaseTimingDirectory -RunDirectory $LogRoot
        $path = Join-Path $dir ('owner-' + $Suite + '-' + $Chunk + '-' + [string]$doc['runId'] + '-' + [string]$doc['attempt'] + '.json')
        if ((Get-TestPhasePathIdentity -Path $path).Kind -eq 'regular') { return }
        [Antiphon.Nightly.TestPhaseFiles]::WriteTextAtomic($path, ([Antiphon.Nightly.TestPhaseJson]::Text($doc, 0)))
    } catch {
        # Owner stamps are diagnostic. A write failure must not change the verdict.
    }
}

function Update-TestPhasePlannedIndex {
    param(
        [string]$Directory,
        [string]$Suite,
        [string]$FileName,
        [string]$RunId,
        [string]$Attempt,
        [string]$AssemblyId,
        [string]$SourceDigest = '',
        [string]$PolicyDigest = '',
        [string]$CostFileDigest = '',
        [string]$AssemblyDigest = '',
        $Filters = $null
    )
    if ($Suite -notmatch '^[a-z0-9][a-z0-9-]{0,40}$') { return }
    if ($FileName -notmatch '^groups-planned-[a-z0-9][a-z0-9-]{0,40}\.json$') { return }
    $path = Join-Path $Directory 'groups-planned-index.json'
    $kept = New-Object System.Collections.Generic.List[object]
    if ((Get-TestPhasePathIdentity -Path $path).Kind -eq 'regular') {
        $existing = (Read-NightlyEvidenceText $path) | ConvertFrom-Json
        if ($existing.PSObject.Properties.Name -contains 'suites' -and $null -ne $existing.suites) {
            foreach ($row in @($existing.suites)) {
                if ($null -eq $row) { continue }
                if ([string]$row.suite -eq $Suite) { continue }
                $keptFilters = New-Object System.Collections.Generic.List[string]
                if ($row.PSObject.Properties.Name -contains 'filters' -and $null -ne $row.filters) {
                    foreach ($item in @($row.filters)) { $keptFilters.Add([string]$item) }
                }
                $keptSource = ''
                $keptPolicy = ''
                $keptCost = ''
                $keptAssemblyDigest = ''
                if ($row.PSObject.Properties.Name -contains 'sourceDigest' -and $null -ne $row.sourceDigest) { $keptSource = [string]$row.sourceDigest }
                if ($row.PSObject.Properties.Name -contains 'policyDigest' -and $null -ne $row.policyDigest) { $keptPolicy = [string]$row.policyDigest }
                if ($row.PSObject.Properties.Name -contains 'costFileDigest' -and $null -ne $row.costFileDigest) { $keptCost = [string]$row.costFileDigest }
                if ($row.PSObject.Properties.Name -contains 'assemblyDigest' -and $null -ne $row.assemblyDigest) { $keptAssemblyDigest = [string]$row.assemblyDigest }
                $kept.Add([ordered]@{
                    suite = [string]$row.suite
                    file = [string]$row.file
                    runId = [string]$row.runId
                    attempt = [string]$row.attempt
                    assembly = [string]$row.assembly
                    sourceDigest = $keptSource
                    policyDigest = $keptPolicy
                    costFileDigest = $keptCost
                    assemblyDigest = $keptAssemblyDigest
                    filters = @($keptFilters.ToArray())
                })
            }
        }
    }
    $freshFilters = New-Object System.Collections.Generic.List[string]
    if ($null -ne $Filters) {
        foreach ($item in @($Filters)) { $freshFilters.Add([string]$item) }
    }
    $kept.Add([ordered]@{
        suite = [string]$Suite
        file = [string]$FileName
        runId = [string]$RunId
        attempt = [string]$Attempt
        assembly = [string]$AssemblyId
        sourceDigest = [string]$SourceDigest
        policyDigest = [string]$PolicyDigest
        costFileDigest = [string]$CostFileDigest
        assemblyDigest = [string]$AssemblyDigest
        filters = @($freshFilters.ToArray())
    })
    $doc = [ordered]@{
        version = 1
        runId = [string]$RunId
        attempt = [string]$Attempt
        suites = @($kept.ToArray())
    }
    [Antiphon.Nightly.TestPhaseFiles]::WriteTextAtomic($path, ([Antiphon.Nightly.TestPhaseJson]::Text($doc, 0)))
}

function Read-TestPhasePlannedDocuments {
    param([string]$RunDirectory)
    $dir = Get-TestPhaseTimingDirectory -RunDirectory $RunDirectory
    $docs = New-Object System.Collections.Generic.List[object]
    $indexPath = Join-Path $dir 'groups-planned-index.json'
    if ((Get-TestPhasePathIdentity -Path $indexPath).Kind -eq 'regular') {
        $index = (Read-NightlyEvidenceText $indexPath) | ConvertFrom-Json
        if ($index.PSObject.Properties.Name -contains 'suites' -and $null -ne $index.suites) {
            foreach ($row in @($index.suites)) {
                if ($null -eq $row) { continue }
                $file = ''
                if ($row.PSObject.Properties.Name -contains 'file') { $file = [string]$row.file }
                if ($file -notmatch '^groups-planned-[a-z0-9][a-z0-9-]{0,40}\.json$') { continue }
                $docPath = Join-Path $dir $file
                if ((Get-TestPhasePathIdentity -Path $docPath).Kind -ne 'regular') { continue }
                $docs.Add(((Read-NightlyEvidenceText $docPath) | ConvertFrom-Json))
            }
        }
    }
    if ($docs.Count -eq 0) {
        $singleton = Join-Path $dir 'groups-planned.json'
        if ((Get-TestPhasePathIdentity -Path $singleton).Kind -eq 'regular') {
            $docs.Add(((Read-NightlyEvidenceText $singleton) | ConvertFrom-Json))
        }
    }
    return @($docs.ToArray())
}

function Initialize-TestPhaseReadWarmup {
    # Compile the bounded readers before a measured call. A fresh process
    # otherwise spends the read budget on the first invocation. The chunk
    # timing readers (Antiphon.Nightly.QualificationTiming) warm themselves.
    if ($script:TestPhaseReadWarmed) { return }
    $script:TestPhaseReadWarmed = $true
    $dir = $null
    try {
        $dir = Join-Path ([IO.Path]::GetTempPath()) ('antiphon-timing-warm-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        $path = Join-Path $dir 'cases-0-warm.jsonl'
        $bytes = New-Object byte[] 262145
        [IO.File]::WriteAllBytes($path, $bytes)
        $null = [Antiphon.Nightly.TestPhaseReads]::JsonlBounded($path, 262144, 4000, 200, [Diagnostics.Stopwatch]::StartNew())
        $trx = Join-Path $dir 'warm.trx'
        [System.IO.File]::WriteAllText($trx, '<TestRun><Results><UnitTestResult testName="warm" duration="00:00:01" outcome="Passed" /></Results></TestRun>')
        $null = Read-TestPhaseTrxRows -Path $trx
    } catch {
    } finally {
        if (-not [string]::IsNullOrWhiteSpace($dir) -and (Test-Path -LiteralPath $dir)) {
            Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

# The warm-up runs on first use, not at load: each reader that starts a read deadline calls
# Initialize-TestPhaseReadWarmup before its clock starts. A process that loads this file and never reads evidence
# (the heartbeat, most nightly helpers) does not spend time on it.
if (-not (Get-Variable -Name TestPhaseReadWarmed -Scope Script -ErrorAction SilentlyContinue)) { $script:TestPhaseReadWarmed = $false }

$script:TestPhaseEvidenceLoading = $false
$script:TestPhaseEvidenceLoaded = $true
