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
# import) are compiled: Antiphon.Nightly/TestPhaseJournal.cs. The publication below works on its context.

function Open-TestPhaseExecutedContext {
    param(
        [string]$LogRoot,
        [string]$TimingRoot,
        [string]$RunId,
        [string]$Attempt,
        [string]$Suite,
        [string]$Source,
        [string]$InvocationId,
        [switch]$ProjectOnly,
        [switch]$Recover
    )
    if ([string]::IsNullOrWhiteSpace($Source)) { $Source = 'unknown' }
    $timing = [Antiphon.Nightly.TestPhaseJournal]::ExecutedTimingRoot($LogRoot, $TimingRoot, [string]$script:QualificationTimingRoot)
    if ([string]::IsNullOrWhiteSpace($timing)) { return ([Antiphon.Nightly.TestPhaseJournal]::RefusedContext('timing-root-missing')) }
    $probe = $timing
    if (-not [string]::IsNullOrWhiteSpace($TimingRoot)) { $probe = $TimingRoot }
    elseif (-not [string]::IsNullOrWhiteSpace([string]$script:QualificationTimingRoot)) { $probe = [string]$script:QualificationTimingRoot }
    elseif (-not [string]::IsNullOrWhiteSpace($LogRoot)) { $probe = (Join-Path $LogRoot 'timing') }
    if ([Antiphon.Nightly.TestPhaseJournal]::UnsafeDirectory($probe)) { return ([Antiphon.Nightly.TestPhaseJournal]::RefusedContext('unsafe-root')) }
    if (Test-NightlyEvidencePath $timing 'Leaf') { return ([Antiphon.Nightly.TestPhaseJournal]::RefusedContext('timing-root-is-file')) }
    [Antiphon.Nightly.TestPhaseJournal]::WriteDirectory($timing)
    $journal = [Antiphon.Nightly.TestPhaseJournal]::JournalRoot($timing)
    [Antiphon.Nightly.TestPhaseJournal]::WriteDirectory($journal)
    $lockPath = Join-Path $journal 'writer.lock'
    $lock = $null
    try {
        $lock = [System.IO.File]::Open($lockPath, [System.IO.FileMode]::OpenOrCreate, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
    } catch {
        return ([Antiphon.Nightly.TestPhaseJournal]::RefusedContext('writer-lock-held'))
    }
    $writer = [Antiphon.Nightly.TestPhaseJournal]::WriterEnvironment([string]$PSVersionTable.PSEdition, [string]$PSVersionTable.PSVersion)
    $ctx = [Antiphon.Nightly.TestPhaseJournal]::OpenContext($timing, $LogRoot, $RunId, $Attempt, $Suite, $Source, $InvocationId, $lock, $writer)
    $ctx.ProjectOnly = [bool]$ProjectOnly
    $currentPath = Join-Path $journal 'current.json'
    $current = [Antiphon.Nightly.TestPhaseJournal]::ReadJsonFile($currentPath)
    if ($null -eq $current) {
        $flat = Join-Path $timing 'groups-executed.json'
        if ((Get-TestPhasePathIdentity -Path $flat).Kind -eq 'regular') {
            $parsed = [Antiphon.Nightly.TestPhaseJournal]::ReadJsonFile($flat)
            $valid = $false
            if ($null -ne $parsed -and $parsed.PSObject.Properties.Name -contains 'version' -and [int]$parsed.version -eq 1) {
                if (($parsed.PSObject.Properties.Name -contains 'chunks') -or ($parsed.PSObject.Properties.Name -contains 'chunk')) { $valid = $true }
            }
            [Antiphon.Nightly.TestPhaseJournal]::InitializeSeedGeneration($ctx, $flat, '', (-not $valid))
        } else {
            [Antiphon.Nightly.TestPhaseJournal]::InitializeEmptyGeneration($ctx, '')
        }
    } else {
        $bound = ([Antiphon.Nightly.TestPhaseText]::OrdinalEqual(([string]$current.timingRoot), ([string]$timing))) -and (-not [string]::IsNullOrWhiteSpace([string]$current.generation))
        if (-not $bound) {
            $ctx.Unresolved = $true
            $ctx.CorruptionReason = 'pointer-binding'
            [Antiphon.Nightly.TestPhaseJournal]::InitializeEmptyGeneration($ctx, [string]$current.generation)
        } else {
            [Antiphon.Nightly.TestPhaseJournal]::ImportExistingGeneration($ctx, [string]$current.generation, [bool]$ProjectOnly, [bool]$Recover)
        }
    }
    if (-not $ProjectOnly) { $script:TestPhaseExecutedContext = $ctx }
    return $ctx
}

function Test-TestPhaseJournalWriteFailure {
    param($Context, [string[]]$Failures)
    $journal = ''
    try { $journal = [IO.Path]::GetFullPath(([Antiphon.Nightly.TestPhaseJournal]::JournalRoot([string]$Context.TimingRoot))) } catch { return $true }
    if ([string]::IsNullOrWhiteSpace($journal)) { return $true }
    $prefix = $journal.TrimEnd([char]92, [char]47) + [IO.Path]::DirectorySeparatorChar
    foreach ($item in @($Failures)) {
        $text = [string]$item
        if ([string]::IsNullOrWhiteSpace($text)) { continue }
        $path = $text
        $split = $text.IndexOf(': ')
        if ($split -gt 0) { $path = $text.Substring(0, $split) }
        try { $path = [IO.Path]::GetFullPath($path) } catch { }
        if ($path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

function Close-TestPhaseExecutedContext {
    param($Context)
    if ($null -eq $Context) { return }
    if ($script:NightlyDeferEvidenceWrites -and $null -ne $Context.Lock) {
        $failed = @([Antiphon.Nightly.EvidenceFiles]::Flush())
        $named = @($failed | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
        if ($named.Count -gt 0) {
            $Context.Reason = 'commit-unpublished'
            $Context.Unresolved = $true
            if (-not $Context.Gaps.Contains('deferred-evidence-write-failed')) {
                [void]$Context.Gaps.Add('deferred-evidence-write-failed')
            }
            # A failed journal write keeps writer.lock. Any other path is already retained for the
            # verdict, so release the lock and let a later chunk in this process open.
            if (Test-TestPhaseJournalWriteFailure -Context $Context -Failures $named) { return }
        }
    }
    if ($null -ne $Context.Lock) {
        try { $Context.Lock.Dispose() } catch { }
        $Context.Lock = $null
    }
    $Context.Closed = $true
    if ($null -ne $script:TestPhaseExecutedContext -and [string]$script:TestPhaseExecutedContext.Id -eq [string]$Context.Id) {
        $script:TestPhaseExecutedContext = $null
    }
}

function Get-TestPhaseNextHeader {
    param($Context, [string]$RunId, [string]$Attempt, [string]$Suite)
    $run = [string]$RunId
    $attempt = [string]$Attempt
    $suite = [string]$Suite
    if ($Context.HeaderReady) {
        if (-not [string]::IsNullOrWhiteSpace([string]$Context.HeaderRun)) { $run = [string]$Context.HeaderRun }
        if ($Context.HeaderAttemptSeen) { $attempt = [string]$Context.HeaderAttempt }
        if (-not [string]::IsNullOrWhiteSpace([string]$Context.HeaderSuite)) { $suite = [string]$Context.HeaderSuite }
    }
    return [pscustomobject]@{ Run = $run; Attempt = $attempt; Suite = $suite }
}

function Write-TestPhaseCommitFile {
    param([string]$FinalPath, [string]$Text)
    $dir = Split-Path -Parent $FinalPath
    [Antiphon.Nightly.TestPhaseJournal]::WriteDirectory($dir)
    # During a nightly run with no commit seam set, the record is queued on the evidence file system
    # (nightly-core.ps1): the same refusal when it exists and the same read-back, without a temporary file
    # and rename per record. The seams drive the on-disk steps below.
    if ($script:NightlyDeferEvidenceWrites -and $null -eq $script:TestPhaseCommitBarrier -and $null -eq $script:TestPhaseCommitFault) {
        if (Test-NightlyEvidencePath $FinalPath 'Leaf') { throw 'commit-exists' }
        Write-NightlyEvidenceText $FinalPath $Text
        $unpublished = @([Antiphon.Nightly.EvidenceFiles]::Flush($FinalPath))
        if ($unpublished.Count -gt 0) { throw ('commit-unpublished ' + ($unpublished -join '; ')) }
        $onDisk = [System.IO.File]::ReadAllText($FinalPath)
        if ($onDisk -ne $Text) { throw 'commit-unreadable' }
        return
    }
    if (-not [Antiphon.Nightly.TestPhaseJournal]::WaitCommitBarrier($script:TestPhaseCommitBarrier, 'BeforeTempComplete')) { throw 'commit-barrier-timeout' }
    if ([Antiphon.Nightly.TestPhaseJournal]::CommitFault($script:TestPhaseCommitFault, 'CreateTemp')) { throw 'commit-fault-temp' }
    $tmp = Join-Path $dir ('.' + [guid]::NewGuid().ToString('n') + '.tmp')
    $stream = $null
    try {
        $stream = [System.IO.File]::Open($tmp, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
        $bytes = (New-Object System.Text.UTF8Encoding $false).GetBytes($Text)
        $stream.Write($bytes, 0, $bytes.Length)
        if ([Antiphon.Nightly.TestPhaseJournal]::CommitFault($script:TestPhaseCommitFault, 'Flush')) { throw 'commit-fault-flush' }
        # Flushed to the operating system, not forced to the disk: after a crash the latest record may be
        # missing or empty, which the prefix reader reports as a shorter prefix or as corruption.
        $stream.Flush()
        $stream.Dispose()
        $stream = $null
        if (-not [Antiphon.Nightly.TestPhaseJournal]::WaitCommitBarrier($script:TestPhaseCommitBarrier, 'AfterFlushBeforeRename')) { throw 'commit-barrier-timeout' }
        if ([Antiphon.Nightly.TestPhaseJournal]::CommitFault($script:TestPhaseCommitFault, 'Rename')) { throw 'commit-fault-rename' }
        if ([System.IO.File]::Exists($FinalPath)) { throw 'commit-exists' }
        [System.IO.File]::Move($tmp, $FinalPath)
        $tmp = ''
        if (-not [Antiphon.Nightly.TestPhaseJournal]::WaitCommitBarrier($script:TestPhaseCommitBarrier, 'AfterRenameBeforeAck')) { throw 'commit-barrier-timeout' }
        if ([Antiphon.Nightly.TestPhaseJournal]::CommitFault($script:TestPhaseCommitFault, 'Acknowledge')) { throw 'commit-fault-ack' }
        $readBack = [System.IO.File]::ReadAllText($FinalPath)
        if ($readBack -ne $Text) { throw 'commit-unreadable' }
    } catch {
        if ($null -ne $stream) { try { $stream.Dispose() } catch { } }
        if (-not [string]::IsNullOrWhiteSpace($tmp) -and [System.IO.File]::Exists($tmp)) {
            try { [System.IO.File]::Delete($tmp) } catch { }
        }
        throw
    }
}

function Add-TestPhaseJournalEnvelope {
    param($Context, [string]$Text, [string]$PublicationId, [int]$Sequence)
    $genDir = Join-Path ([Antiphon.Nightly.TestPhaseJournal]::JournalRoot($Context.TimingRoot)) $Context.Generation
    $final = Join-Path $genDir ($Sequence.ToString('00000000') + '.json')
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $attempt = 0
    $lastError = ''
    while ($attempt -lt 2) {
        if ($attempt -gt 0 -and $watch.Elapsed.TotalSeconds -gt 2) { break }
        $attempt++
        try {
            if (Test-NightlyEvidencePath $final 'Leaf') {
                $existing = Read-NightlyEvidenceText $final
                if (([Antiphon.Nightly.TestPhaseText]::RecordDigest($existing)) -and $existing.Contains($PublicationId)) { return $true }
                throw 'commit-conflict'
            }
            Write-TestPhaseCommitFile -FinalPath $final -Text $Text
            return $true
        } catch {
            $lastError = [string]$_.Exception.Message
        }
    }
    $Context.Reason = $lastError
    return $false
}

function Invoke-TestPhasePrefixRescan {
    param($Context)
    if (-not $Context.NeedsRecovery) { return }
    if ($Context.RescanAttempted) { return }
    $Context.RescanAttempted = $true
    $genDir = Join-Path ([Antiphon.Nightly.TestPhaseJournal]::JournalRoot($Context.TimingRoot)) $Context.Generation
    $prefix = [Antiphon.Nightly.TestPhaseJournal]::ReadGenerationPrefix($genDir)
    $Context.Records = $prefix.Records
    $Context.LastCommitted = $prefix.LastSequence
    $Context.NextSequence = $prefix.NextSequence
    [Antiphon.Nightly.TestPhaseJournal]::ImportGapMemory($Context, $prefix.Records)
    if ($prefix.Corrupt) {
        $Context.Unresolved = $true
        if ([string]::IsNullOrWhiteSpace([string]$Context.CorruptionReason)) { $Context.CorruptionReason = [string]$prefix.Reason }
    }
}

function Add-TestPhaseGapRecord {
    param(
        $Context,
        [string]$Reason,
        [string]$Suite = '',
        [string]$Chunk = '',
        [string]$RunId = '',
        [string]$Attempt = '',
        [string]$HeaderRun = '',
        [string]$HeaderAttempt = '',
        [string]$HeaderSuite = '',
        [string]$PublicationId = ''
    )
    $Context.NeedsRecovery = $true
    if (-not $Context.Gaps.Contains('publication-gap')) { [void]$Context.Gaps.Add('publication-gap') }
    if ([string]::IsNullOrWhiteSpace($HeaderRun)) { $HeaderRun = [string]$RunId }
    if ([string]::IsNullOrWhiteSpace($HeaderAttempt)) { $HeaderAttempt = [string]$Attempt }
    if ([string]::IsNullOrWhiteSpace($HeaderSuite)) { $HeaderSuite = [string]$Suite }
    if ([string]::IsNullOrWhiteSpace($PublicationId)) { $PublicationId = [guid]::NewGuid().ToString('n') }
    $sequence = [int]$Context.NextSequence
    $genDir = Join-Path ([Antiphon.Nightly.TestPhaseJournal]::JournalRoot($Context.TimingRoot)) $Context.Generation
    $final = Join-Path $genDir ($sequence.ToString('00000000') + '.json')
    $envelope = [Antiphon.Nightly.TestPhaseJournal]::EnvelopeText('gap', $sequence, $PublicationId, $Context, $Reason, $Suite, $Chunk, $Suite, $Chunk, $HeaderRun, $HeaderAttempt, $HeaderSuite, '', '')
    $ok = Add-TestPhaseJournalEnvelope -Context $Context -Text $envelope -PublicationId $PublicationId -Sequence $sequence
    if (-not $ok) {
        $already = $false
        if ($null -eq $Context.PendingGaps) { $Context.PendingGaps = New-Object System.Collections.Generic.List[object] }
        foreach ($pending in $Context.PendingGaps) {
            if ([string]$pending.PublicationId -eq $PublicationId) { $already = $true; break }
        }
        if (-not $already) {
            [void]$Context.PendingGaps.Add([pscustomobject]@{
                Reason = [string]$Reason
                Suite = [string]$Suite
                Chunk = [string]$Chunk
                RunId = [string]$RunId
                Attempt = [string]$Attempt
                HeaderRun = [string]$HeaderRun
                HeaderAttempt = [string]$HeaderAttempt
                HeaderSuite = [string]$HeaderSuite
                PublicationId = [string]$PublicationId
            })
        }
        return $false
    }
    $Context.Records.Add([pscustomobject]@{
        Kind = 'gap'
        Sequence = $sequence
        FreshJson = ''
        SettledJson = ''
        FreshSuite = [string]$Suite
        FreshChunk = [string]$Chunk
        SettledSuite = [string]$Suite
        SettledChunk = [string]$Chunk
        HeaderRun = [string]$HeaderRun
        HeaderAttempt = [string]$HeaderAttempt
        HeaderSuite = [string]$HeaderSuite
        PublicationId = [string]$PublicationId
    })
    $Context.LastCommitted = $sequence
    $Context.NextSequence = $sequence + 1
    return $true
}

function Submit-TestPhasePendingGaps {
    param($Context)
    if ($null -eq $Context.PendingGaps) { return $true }
    while ($Context.PendingGaps.Count -gt 0) {
        $pending = $Context.PendingGaps[0]
        $ok = Add-TestPhaseGapRecord -Context $Context -Reason ([string]$pending.Reason) -Suite ([string]$pending.Suite) -Chunk ([string]$pending.Chunk) -RunId ([string]$pending.RunId) -Attempt ([string]$pending.Attempt) -HeaderRun ([string]$pending.HeaderRun) -HeaderAttempt ([string]$pending.HeaderAttempt) -HeaderSuite ([string]$pending.HeaderSuite) -PublicationId ([string]$pending.PublicationId)
        if (-not $ok) { return $false }
        if ($Context.PendingGaps.Count -gt 0 -and [string]$Context.PendingGaps[0].PublicationId -eq [string]$pending.PublicationId) {
            $Context.PendingGaps.RemoveAt(0)
        }
    }
    return $true
}

function Add-TestPhaseExecutedChunk {
    param($Context, $Fresh, [string]$Suite, [string]$Chunk, [string]$RunId, [string]$Attempt)
    if ($Context.Closed -or $Context.Refused) { throw 'context-closed' }
    Invoke-TestPhasePrefixRescan -Context $Context
    $current = [Antiphon.Nightly.TestPhaseJournal]::ReadJsonFile((Join-Path ([Antiphon.Nightly.TestPhaseJournal]::JournalRoot($Context.TimingRoot)) 'current.json'))
    if ($null -eq $current -or -not ([Antiphon.Nightly.TestPhaseText]::OrdinalEqual(([string]$current.generation), ([string]$Context.Generation)))) { throw 'stale-context' }
    if ($null -ne $Context.PendingGaps -and $Context.PendingGaps.Count -gt 0) {
        $drained = Submit-TestPhasePendingGaps -Context $Context
        if (-not $drained) {
            Write-TestPhaseFailureMarker -RunDirectory $Context.LogRoot -Code 'phase-summary-failed'
            return $false
        }
    }
    $header = Get-TestPhaseNextHeader -Context $Context -RunId $RunId -Attempt $Attempt -Suite $Suite
    $freshJson = [Antiphon.Nightly.TestPhaseJson]::Text($Fresh, 0)
    $parsed = $freshJson | ConvertFrom-Json
    $settled = [Antiphon.Nightly.TestPhaseJournal]::SettledChunk($parsed)
    $settledJson = [Antiphon.Nightly.TestPhaseJson]::Text($settled, 0)
    $publicationId = [guid]::NewGuid().ToString('n')
    $sequence = [int]$Context.NextSequence
    $envelope = [Antiphon.Nightly.TestPhaseJournal]::EnvelopeText('chunk', $sequence, $publicationId, $Context, '', $Suite, $Chunk, [string]$settled.suite, [string]$settled.chunk, [string]$header.Run, [string]$header.Attempt, [string]$header.Suite, $freshJson, $settledJson)
    $ok = Add-TestPhaseJournalEnvelope -Context $Context -Text $envelope -PublicationId $publicationId -Sequence $sequence
    if (-not $ok) {
        Write-TestPhaseFailureMarker -RunDirectory $Context.LogRoot -Code 'phase-summary-failed'
        Add-TestPhaseGapRecord -Context $Context -Reason ([string]$Context.Reason) -Suite $Suite -Chunk $Chunk -RunId $RunId -Attempt $Attempt -HeaderRun ([string]$header.Run) -HeaderAttempt ([string]$header.Attempt) -HeaderSuite ([string]$header.Suite) | Out-Null
        return $false
    }
    if (-not [Antiphon.Nightly.TestPhaseJournal]::WaitCommitBarrier($script:TestPhaseCommitBarrier, 'AfterAckBeforeProjection')) { throw 'commit-barrier-timeout' }
    $Context.Records.Add([pscustomobject]@{
        Kind = 'chunk'
        Sequence = $sequence
        FreshJson = $freshJson
        SettledJson = $settledJson
        FreshSuite = [string]$Suite
        FreshChunk = [string]$Chunk
        SettledSuite = [string]$settled.suite
        SettledChunk = [string]$settled.chunk
        HeaderRun = [string]$header.Run
        HeaderAttempt = [string]$header.Attempt
        HeaderSuite = [string]$header.Suite
        PublicationId = $publicationId
    })
    $Context.LastCommitted = $sequence
    $Context.NextSequence = $sequence + 1
    $Context.NeedsRecovery = $false
    $Context.RescanAttempted = $false
    [Antiphon.Nightly.TestPhaseJournal]::UpdateContextHeader($Context, [string]$header.Run, [string]$header.Attempt, [string]$header.Suite)
    return $true
}

function Get-TestPhasePublicationStateText {
    param($Context, $ProjectionText)
    $quality = New-Object System.Collections.Generic.List[string]
    if ($Context.Unresolved) {
        if (-not $quality.Contains('publication-corruption')) { $quality.Add('publication-corruption') }
    }
    $pending = 0
    if ($null -ne $Context.PendingGaps) { $pending = $Context.PendingGaps.Count }
    if ($Context.Gaps.Count -gt 0 -or $pending -gt 0) {
        if (-not $quality.Contains('publication-gap')) { $quality.Add('publication-gap') }
    }
    $complete = ($quality.Count -eq 0)
    $digest = ''
    if ($null -ne $ProjectionText) { $digest = [Antiphon.Nightly.TestPhaseRecords]::Sha256Text($ProjectionText) }
    $state = [ordered]@{
        version = 1
        generation = [string]$Context.Generation
        sequence = [int]$Context.LastCommitted
        root = [string]$Context.TimingRoot
        publicationComplete = [bool]$complete
        unresolved = [bool]$Context.Unresolved
        reason = [string]$Context.CorruptionReason
        projectionDigest = [string]$digest
        quality = @($quality.ToArray())
    }
    return ([Antiphon.Nightly.TestPhaseJson]::Text($state, 0))
}

function Write-TestPhasePublicationState {
    param($Context, [string]$ProjectionText)
    $text = Get-TestPhasePublicationStateText -Context $Context -ProjectionText $ProjectionText
    $path = Join-Path $Context.TimingRoot 'groups-executed.state.json'
    [Antiphon.Nightly.TestPhaseFiles]::WriteTextAtomic($path, $text)
    return ([Antiphon.Nightly.TestPhaseText]::Sha256File($path))
}

function Complete-TestPhaseExecutedProjection {
    param($Context, [string]$LogRoot, [string]$TimingRoot, [string]$Terminal)
    $owned = $false
    if ($null -eq $Context) {
        $Context = Open-TestPhaseExecutedContext -LogRoot $LogRoot -TimingRoot $TimingRoot -RunId '' -Attempt '' -Suite '' -Source 'unknown' -ProjectOnly
        $owned = $true
    }
    if ($null -eq $Context -or $Context.Refused) { return $Context }
    try {
        if ($null -ne $Context.PendingGaps -and $Context.PendingGaps.Count -gt 0) {
            Submit-TestPhasePendingGaps -Context $Context | Out-Null
        }
        $text = [Antiphon.Nightly.TestPhaseJournal]::ProjectedDocument($Context)
        $terminalSet = -not [string]::IsNullOrWhiteSpace($Terminal)
        if ($null -eq $text -and -not $Context.Unresolved -and $Context.Gaps.Count -eq 0 -and -not $terminalSet) { return $Context }
        if ($null -ne $text) {
            $flat = Join-Path $Context.TimingRoot 'groups-executed.json'
            [Antiphon.Nightly.TestPhaseFiles]::WriteTextAtomic($flat, $text)
        }
        $stateDigest = Write-TestPhasePublicationState -Context $Context -ProjectionText $text
        if ($terminalSet) {
            $projectionDigest = ''
            if ($null -ne $text) { $projectionDigest = [Antiphon.Nightly.TestPhaseRecords]::Sha256Text($text) }
            $seal = [ordered]@{
                version = 1
                generation = [string]$Context.Generation
                sequence = [int]$Context.LastCommitted
                terminal = [string]$Terminal
                projectionDigest = [string]$projectionDigest
                stateDigest = [string]$stateDigest
            }
            $sealPath = Join-Path (Join-Path ([Antiphon.Nightly.TestPhaseJournal]::JournalRoot($Context.TimingRoot)) $Context.Generation) 'seal.json'
            [Antiphon.Nightly.TestPhaseFiles]::WriteTextAtomic($sealPath, ([Antiphon.Nightly.TestPhaseJson]::Text($seal, 0)))
        }
        return $Context
    } finally {
        if ($owned) { Close-TestPhaseExecutedContext -Context $Context }
    }
}

function Read-TestPhaseExecutedPrefix {
    param([string]$LogRoot, [string]$TimingRoot)
    $timing = [Antiphon.Nightly.TestPhaseJournal]::ExecutedTimingRoot($LogRoot, $TimingRoot, [string]$script:QualificationTimingRoot)
    $empty = [pscustomobject]@{
        Generation = ''
        LastSequence = 0
        RecordKind = 'none'
        Quality = @()
        Complete = $false
        PublicationComplete = $false
        Chunks = @()
        Refused = $false
        Reason = ''
        Unresolved = $false
        Terminal = ''
    }
    if ([string]::IsNullOrWhiteSpace($timing)) { $empty.Refused = $true; $empty.Reason = 'timing-root-missing'; return $empty }
    $current = [Antiphon.Nightly.TestPhaseJournal]::ReadJsonFile((Join-Path ([Antiphon.Nightly.TestPhaseJournal]::JournalRoot($timing)) 'current.json'))
    if ($null -eq $current) { return $empty }
    $genDir = Join-Path ([Antiphon.Nightly.TestPhaseJournal]::JournalRoot($timing)) ([string]$current.generation)
    $prefix = [Antiphon.Nightly.TestPhaseJournal]::ReadGenerationPrefix($genDir)
    $kind = 'none'
    if ($prefix.Records.Count -gt 0) { $kind = [string]$prefix.Records[$prefix.Records.Count - 1].Kind }
    $ids = New-Object System.Collections.Generic.List[string]
    foreach ($rec in $prefix.Records) {
        if ($rec.Kind -eq 'chunk') { $ids.Add([string]$rec.FreshChunk) }
    }
    $quality = New-Object System.Collections.Generic.List[string]
    if ($prefix.Corrupt) { $quality.Add('publication-corruption') }
    $terminal = ''
    $seal = [Antiphon.Nightly.TestPhaseJournal]::ReadJsonFile((Join-Path $genDir 'seal.json'))
    if ($null -ne $seal -and $seal.PSObject.Properties.Name -contains 'terminal') { $terminal = [string]$seal.terminal }
    return [pscustomobject]@{
        Generation = [string]$current.generation
        LastSequence = [int]$prefix.LastSequence
        RecordKind = $kind
        Quality = @($quality.ToArray())
        Complete = (-not $prefix.Corrupt -and $prefix.Records.Count -gt 0 -and -not [string]::IsNullOrWhiteSpace($terminal))
        PublicationComplete = (-not $prefix.Corrupt)
        Chunks = @($ids.ToArray())
        Refused = $false
        Reason = [string]$prefix.Reason
        Unresolved = [bool]$prefix.Corrupt
        Terminal = $terminal
    }
}

function Invoke-TestPhaseExecutedRecovery {
    param([string]$LogRoot, [string]$TimingRoot, [string]$RunId, [string]$Attempt, [string]$Suite, [string]$Source)
    $timing = [Antiphon.Nightly.TestPhaseJournal]::ExecutedTimingRoot($LogRoot, $TimingRoot, [string]$script:QualificationTimingRoot)
    $run = [string]$RunId
    $att = [string]$Attempt
    $src = [string]$Source
    if (-not [string]::IsNullOrWhiteSpace($timing)) {
        $current = [Antiphon.Nightly.TestPhaseJournal]::ReadJsonFile((Join-Path ([Antiphon.Nightly.TestPhaseJournal]::JournalRoot($timing)) 'current.json'))
        if ($null -ne $current -and -not [string]::IsNullOrWhiteSpace([string]$current.generation)) {
            $metaPath = Join-Path (Join-Path ([Antiphon.Nightly.TestPhaseJournal]::JournalRoot($timing)) ([string]$current.generation)) 'metadata.json'
            $meta = [Antiphon.Nightly.TestPhaseJournal]::ReadJsonFile($metaPath)
            if ($null -ne $meta) {
                if ([string]::IsNullOrWhiteSpace($run)) { $run = [string]$meta.runId }
                if ([string]::IsNullOrWhiteSpace($att)) { $att = [string]$meta.attempt }
                if ([string]::IsNullOrWhiteSpace($src)) { $src = [string]$meta.source }
            }
        }
    }
    if ([string]::IsNullOrWhiteSpace($src)) { $src = 'unknown' }
    $ctx = Open-TestPhaseExecutedContext -LogRoot $LogRoot -TimingRoot $TimingRoot -RunId $run -Attempt $att -Suite $Suite -Source $src -Recover
    if ($null -eq $ctx -or $ctx.Refused) { return $ctx }
    try {
        $genDir = Join-Path ([Antiphon.Nightly.TestPhaseJournal]::JournalRoot($ctx.TimingRoot)) $ctx.Generation
        $seal = [Antiphon.Nightly.TestPhaseJournal]::ReadJsonFile((Join-Path $genDir 'seal.json'))
        if ($null -ne $seal -and -not $ctx.Unresolved) {
            $preview = [Antiphon.Nightly.TestPhaseJournal]::ProjectedDocument($ctx)
            $projectionDigest = ''
            if ($null -ne $preview) { $projectionDigest = [Antiphon.Nightly.TestPhaseRecords]::Sha256Text($preview) }
            $stateText = Get-TestPhasePublicationStateText -Context $ctx -ProjectionText $preview
            $stateDigest = [Antiphon.Nightly.TestPhaseRecords]::Sha256Text($stateText)
            $projectionMatch = [Antiphon.Nightly.TestPhaseText]::OrdinalEqual($projectionDigest, ([string]$seal.projectionDigest))
            $stateMatch = [Antiphon.Nightly.TestPhaseText]::OrdinalEqual($stateDigest, ([string]$seal.stateDigest))
            if (-not $projectionMatch -or -not $stateMatch) {
                $ctx.Reason = 'seal-digest-mismatch'
                return $ctx
            }
        }
        Complete-TestPhaseExecutedProjection -Context $ctx | Out-Null
        return $ctx
    } finally {
        Close-TestPhaseExecutedContext -Context $ctx
    }
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
    $persistent = $false
    $bound = $null
    if ($null -ne $script:TestPhaseExecutedContext) {
        $existingCtx = $script:TestPhaseExecutedContext
        if (-not $existingCtx.Closed -and -not $existingCtx.Refused) {
            $bound = $existingCtx
            $persistent = $true
        }
    }
    $savedContext = $script:TestPhaseExecutedContext
    $savedTiming = $script:QualificationTimingRoot
    $opened = $null
    try {
        if (-not $persistent) {
            $opened = Open-TestPhaseExecutedContext -LogRoot $LogRoot -RunId $RunId -Attempt $Attempt -Suite $Suite -Source 'unknown'
            if ($null -eq $opened -or $opened.Refused) {
                Write-TestPhaseFailureMarker -RunDirectory $LogRoot -Code 'phase-summary-failed'
                return
            }
            $bound = $opened
        }
        $committed = $false
        try {
            $committed = Add-TestPhaseExecutedChunk -Context $bound -Fresh $fresh -Suite $Suite -Chunk $Chunk -RunId $RunId -Attempt $Attempt
        } catch {
            Write-TestPhaseFailureMarker -RunDirectory $LogRoot -Code 'phase-summary-failed'
            $committed = $false
        }
        if (-not $persistent) {
            $project = $committed -or $bound.HasSeed -or $bound.Unresolved -or ($bound.Gaps.Count -gt 0) -or ($bound.Records.Count -gt 0)
            if ($project) {
                Complete-TestPhaseExecutedProjection -Context $bound | Out-Null
            }
        }
    } finally {
        if ($null -ne $opened) {
            Close-TestPhaseExecutedContext -Context $opened
            $script:TestPhaseExecutedContext = $savedContext
            $script:QualificationTimingRoot = $savedTiming
        }
    }
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
            Complete-TestPhaseExecutedProjection -Context $live | Out-Null
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
