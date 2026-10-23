#requires -Version 5.1
# CARD-1313 S6-A. Intent only: every admitted attempt executes Full.
# Keep this bridge ASCII and 5.1 parseable. The selector is loaded only in pwsh 7.
. (Join-Path $PSScriptRoot 'nightly-common.ps1')

function Assert-ReleaseSelectionCapability {
    param([string]$SelectionMode)
    $command = Get-Command pwsh -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($SelectionMode -cne 'Full' -and -not $command) { throw 'selection-pwsh-required' }
    return $command
}

function Assert-ReleaseSelectionPath {
    param([string]$Path, [string]$Root, [string]$Checkout = '')
    $full = [IO.Path]::GetFullPath($Path)
    $parent = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $comparison = [StringComparison]::Ordinal
    if ([IO.Path]::DirectorySeparatorChar -eq '\') { $comparison = [StringComparison]::OrdinalIgnoreCase }
    if (-not $full.StartsWith($parent, $comparison) -or (Test-NightlyAncestorReparse $full)) { throw 'selection-path-outside-state' }
    if ($Checkout) {
        $disposable = [IO.Path]::GetFullPath($Checkout).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if ($full.StartsWith($disposable, $comparison)) { throw 'selection-path-in-checkout' }
    }
    return $full
}

function Write-ReleaseSelectionOnce {
    param([string]$Path, $Value)
    $text = ConvertTo-NightlyCanonicalJson $Value
    if (Test-Path -LiteralPath $Path) {
        if ([IO.File]::ReadAllText($Path) -cne $text) { throw 'selection-attempt-conflict' }
        return
    }
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $Path))
    # Flush before atomic create-only publication. An interrupted temp never becomes intent.
    $temp = $Path + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes($text)
        $stream = [IO.File]::Open($temp, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
        try { [IO.File]::Move($temp, $Path) }
        catch { if (-not (Test-Path -LiteralPath $Path) -or [IO.File]::ReadAllText($Path) -cne $text) { throw 'selection-attempt-conflict' } }
    } finally { if (Test-Path -LiteralPath $temp) { [IO.File]::Delete($temp) } }
}

function Get-ReleaseSelectionContract {
    param([string]$RepositoryRoot, [string]$Sha, [string]$Profile)
    $policy = Get-TestSelectionBlob $RepositoryRoot $Sha 'tests/test-execution-policy.json' | ConvertFrom-Json
    if ($policy.policyHash -cne (Get-NightlyPolicyHash $policy)) { throw 'policy-hash-stale' }
    [void](Get-NightlyPolicyProfile $policy $Profile)
    $catalog = Get-TestSelectionBlob $RepositoryRoot $Sha 'tests/test-selection-policy.json' | ConvertFrom-Json -AsHashtable
    [void](Assert-TestSelectionCatalog $catalog)
    if ($policy.selectionCatalog.hash -cne $catalog.catalogHash) { throw 'catalog-pin-mismatch' }
    $selectors = @{}
    foreach ($path in @('scripts/select-test-rosters.ps1', 'scripts/lib/test-selection.ps1')) {
        $selectors[$path] = Get-TestSelectionDigest (Get-TestSelectionBlob $RepositoryRoot $Sha $path)
    }
    return [ordered]@{
        policyHash = $policy.policyHash; catalogHash = $catalog.catalogHash
        selectorHash = (Get-TestSelectionDigest $selectors)
        runtimeHash = (Get-TestSelectionDigest (Get-TestSelectionBlob $RepositoryRoot $Sha 'global.json'))
        profile = $Profile
        platform = $(if ([IO.Path]::DirectorySeparatorChar -eq '\') { 'windows' } else { 'linux' })
    }
}

function Read-ReleaseSelectionBaseline {
    # An archived publication, not today's card revision or remote tip. The archive
    # receipt is an index into immutable publisher artifacts in configured state.
    # S6-B owns producing it; this reader never manufactures publication authority.
    param($Request, $CandidateContract)
    $empty = @{ sha = [string]$Request.BaselineSha; digest = ''; compatible = $false; reason = 'baseline-absent' }
    $path = [string]$Request.BaselineReceiptPath
    if (-not $path) {
        $archive = Join-Path $Request.ReleaseRoot 'baselines'
        if (Test-Path -LiteralPath $archive) {
            # Validate every candidate before choosing; a corrupt claimed record is
            # never quietly skipped in favour of an older convenient green.
            $valid = @()
            foreach ($file in @(Get-ChildItem -LiteralPath $archive -File -Filter '*.json' | Sort-Object Name)) {
                $copy = @{}; foreach ($key in $Request.Keys) { $copy[$key] = $Request[$key] }
                $copy.BaselineReceiptPath = $file.FullName
                # The directory may contain several valid releases. Validate each
                # on its own identity, then apply the caller's explicit B filter.
                $copy.BaselineSha = ''
                $candidate = Read-ReleaseSelectionBaseline $copy $CandidateContract
                if ($candidate.compatible -and (-not $Request.BaselineSha -or $candidate.sha -ceq $Request.BaselineSha)) { $valid += $candidate }
            }
            if ($valid.Count) { return $valid[-1] }
        }
        return $empty
    }
    $path = Assert-ReleaseSelectionPath $path $Request.ReleaseRoot $Request.RepositoryRoot
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $empty }
    $receipt = Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($receipt.format -cne 'antiphon-full-release-baseline-v1') { throw 'baseline-unqualified' }
    if ($receipt.coverageScope -cne 'Full' -or $receipt.profile -cne 'rc') { throw 'baseline-full-only' }
    if ($receipt.repository -cne $Request.Repository) { throw 'baseline-repository' }
    if ($receipt.platform -cne $CandidateContract.platform) { throw 'baseline-platform' }
    if ($Request.BaselineSha -and $receipt.sha -cne $Request.BaselineSha) { throw 'baseline-sha' }
    Assert-ReleaseSelectionCommit $Request.RepositoryRoot $receipt.sha
    $root = Assert-ReleaseSelectionPath (Join-Path $Request.ReleaseRoot $receipt.candidateRoot) $Request.ReleaseRoot $Request.RepositoryRoot
    $documents = @{}
    $required = @('authority/authority.json', 'authority/execution-plan.json', 'authority/discovery.json',
        'authority/ledger.json', 'authority/publication-authority.json', 'release-manifest.json', 'publish.json', 'complete-green.json')
    foreach ($name in $required) {
        $file = Assert-ReleaseSelectionPath (Join-Path $root $name) $root
        $expected = $receipt.files.$name
        if ($expected -isnot [string] -or $expected -cnotmatch '^[0-9a-f]{64}$' -or
            -not (Test-Path -LiteralPath $file -PathType Leaf) -or (Get-NightlyFileSha256 $file) -cne $expected) {
            throw ('baseline-digest:' + $name)
        }
        $documents[$name] = Get-Content -LiteralPath $file -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    $authority = $documents['authority/authority.json']; $ledger = $documents['authority/ledger.json']
    $publication = $documents['authority/publication-authority.json']; $manifest = $documents['release-manifest.json']
    $published = $documents['publish.json']; $green = $documents['complete-green.json']
    if ($authority.schemaVersion -ne 1 -or $authority.kind -cne 'release-authority' -or
        $authority.profile -cne 'rc' -or $authority.repository -cne $Request.Repository -or $authority.sha -cne $receipt.sha -or
        $ledger.selection -cne 'full' -or $ledger.sha -cne $receipt.sha -or
        $ledger.seamed -isnot [bool] -or $ledger.seamed -or $ledger.diagnostic -isnot [bool] -or $ledger.diagnostic -or
        $ledger.noReport -isnot [bool] -or $ledger.noReport -or -not (Test-ReleaseGateRealBoolean $ledger.teardownSucceeded) -or
        -not (Get-ReleaseGateCreditVerdict $green).Ok -or $green.profile -cne 'rc' -or $green.sha -cne $receipt.sha) { throw 'baseline-full-only' }
    foreach ($name in @('authority', 'executionPlan', 'discovery', 'ledger')) {
        $file = @{authority='authority';executionPlan='execution-plan';discovery='discovery';ledger='ledger'}[$name]
        if ($publication.inputs.($name + 'Digest') -cne $receipt.files.('authority/' + $file + '.json')) { throw 'baseline-publication-binding' }
    }
    $digest = Get-NightlySha256Text (ConvertTo-NightlyCanonicalJson $publication.inputs)
    if ($publication.digest -cne $digest -or $manifest.publicationAuthorityDigest -cne $digest -or
        $published.publicationAuthorityDigest -cne $digest -or $manifest.sha -cne $receipt.sha -or
        $published.sha -cne $receipt.sha -or $published.manifestDigest -cne (Get-ReleaseGateManifestDigest $manifest)) { throw 'baseline-publication-binding' }
    if (@($published.steps) -cnotcontains 'published' -or -not $published.releaseId -or -not $published.releaseUrl) { throw 'baseline-publication-required' }
    if ($ledger.prepublicationReceipt.kind -cne 'prepublication' -or
        $ledger.prepublicationReceipt.correlation.sha -cne $receipt.sha -or
        $ledger.prepublicationReceipt.correlation.runId -cne $ledger.runId -or
        $ledger.prepublicationReceipt.recipient.cardId -cne $authority.releaseCardId -or
        $ledger.prepublicationReceipt.recipient.revision -lt 1) { throw 'baseline-receipt' }
    $historical = Get-ReleaseSelectionContract $Request.RepositoryRoot $receipt.sha 'rc'
    if ((ConvertTo-NightlyCanonicalJson $historical) -cne (ConvertTo-NightlyCanonicalJson $receipt.contract)) { throw 'baseline-contract' }
    if ($authority.policyHash -cne $historical.policyHash) { throw 'baseline-policy' }
    $blob = Read-ReleaseAuthorityPinnedBlob $Request.RepositoryRoot $receipt.sha
    $policy = ConvertFrom-ReleaseAuthorityPolicyBytes $blob.Bytes
    if ($authority.policyBlobId -cne $blob.BlobId -or $authority.policyRawSha256 -cne (Get-ReleaseAuthorityBytesSha256 $blob.Bytes) -or
        (ConvertTo-NightlyCanonicalJson @($authority.requiredSuites)) -cne (ConvertTo-NightlyCanonicalJson @($policy.RequiredSuites))) { throw 'baseline-policy' }
    $plan = $documents['authority/execution-plan.json']; $discovery = $documents['authority/discovery.json']
    if ($ledger.authorityDigest -cne $receipt.files.'authority/authority.json' -or
        $ledger.executionPlanDigest -cne $receipt.files.'authority/execution-plan.json' -or
        $plan.authorityDigest -cne $receipt.files.'authority/authority.json' -or $plan.discoveryDigest -cne $receipt.files.'authority/discovery.json' -or
        $plan.sha -cne $receipt.sha -or $plan.intentId -cne $authority.intentId -or
        $ledger.intentId -cne $authority.intentId -or $ledger.runId -cne $green.runId -or
        $ledger.candidateId -cne $authority.candidateId -or $manifest.candidateId -cne $authority.candidateId) { throw 'baseline-ledger-binding' }
    foreach ($suite in $policy.RequiredSuites) {
        $planned = @($plan.suites | Where-Object id -CEQ $suite)
        if ($planned.Count -ne 1) { throw 'baseline-suite-universe' }
        if ($suite -cin @('client','scripts')) {
            $rows = @($ledger.rosters | Where-Object suite -CEQ $suite)
            if ($rows.Count -ne 1 -or $rows[0].exitCode -ne 0 -or @($planned[0].entries).Count -eq 0 -or
                @($rows[0].entries | Where-Object outcome -CNE 'passed').Count -ne 0 -or
                (@($rows[0].entries.id | Sort-Object) -join ',') -cne (@($planned[0].entries | Sort-Object) -join ',')) { throw 'baseline-roster-incomplete' }
            continue
        }
        $requiredUids = @(Get-ReleaseAuthorityRequiredUids @($discovery.suites.$suite) $policy.Exclusions $suite)
        $executed = @()
        foreach ($chunk in @($planned[0].chunks)) {
            $rows = @($ledger.chunks | Where-Object chunk -CEQ $chunk.id)
            if ($rows.Count -ne 1 -or $rows[0].exitCode -ne 0) { throw 'baseline-chunk-incomplete' }
            $trx = Assert-ReleaseSelectionPath (Join-Path $root $rows[0].trx) $root
            if ((Get-NightlyFileSha256 $trx) -cne $rows[0].trxSha256 -or $publication.inputs.evidence.($chunk.id) -cne $rows[0].trxSha256) { throw 'baseline-trx-digest' }
            $terminals = @(Read-NightlyTrxIdentities $trx)
            if (@($terminals | Where-Object Outcome -CNE 'Passed').Count -ne 0 -or
                (@($terminals.TestId | Sort-Object) -join ',') -cne (@($chunk.uids | Sort-Object) -join ',')) { throw 'baseline-chunk-incomplete' }
            $executed += @($terminals.TestId)
        }
        if ($requiredUids.Count -eq 0 -or (@($executed | Sort-Object) -join ',') -cne (@($requiredUids | Sort-Object) -join ',')) { throw 'baseline-uids-incomplete' }
    }
    $compatible = (ConvertTo-NightlyCanonicalJson $historical) -ceq (ConvertTo-NightlyCanonicalJson $CandidateContract)
    return @{ sha = $receipt.sha; digest = (Get-NightlyFileSha256 $path); compatible = $compatible
        reason = $(if ($compatible) { '' } else { 'baseline-incompatible' }); path = $path }
}

function Assert-ReleaseSelectionCommit {
    param([string]$Root, [string]$Sha)
    if ($Sha -cnotmatch '^[0-9a-f]{40}$') { throw 'selection-commit-required' }
    $read = Invoke-TestSelectionGit $Root @('cat-file', '-t', $Sha) @(0, 128)
    if ($read.Exit -ne 0 -or $read.Stdout.Trim() -cne 'commit') { throw 'selection-commit-unavailable' }
}

function New-ReleaseSelectionAttemptCore {
    param([hashtable]$Request)
    if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'selection-pwsh-required' }
    . (Join-Path $PSScriptRoot 'nightly-policy.ps1')
    . (Join-Path $PSScriptRoot 'release-gate.ps1')
    . (Join-Path $PSScriptRoot 'release-authority.ps1')
    . (Join-Path $PSScriptRoot 'nightly-coverage.ps1')
    . (Join-Path $PSScriptRoot 'test-selection.ps1')
    Assert-ReleaseSelectionCommit $Request.RepositoryRoot $Request.CandidateSha
    if ($Request.BaselineSha) { Assert-ReleaseSelectionCommit $Request.RepositoryRoot $Request.BaselineSha }
    $contract = Get-ReleaseSelectionContract $Request.RepositoryRoot $Request.CandidateSha $Request.Profile
    $baseline = Read-ReleaseSelectionBaseline $Request $contract
    $b = [string]$baseline.sha
    if ($b) {
        Assert-ReleaseSelectionCommit $Request.RepositoryRoot $b
        $ancestor = Invoke-TestSelectionGit $Request.RepositoryRoot @('merge-base', '--is-ancestor', $b, $Request.CandidateSha) @(0, 1)
        if ($ancestor.Exit -ne 0) { throw 'baseline-not-ancestor' }
    }
    $proposal = $null
    $projectProposal = $null
    $reasons = @()
    if ($baseline.reason) { $reasons += $baseline.reason }
    if ($b) {
        $proposal = Get-TestSelection $Request.RepositoryRoot $b $Request.CandidateSha $Request.Profile
        $reasons += @($proposal.fallbackReasons)
        if ($Request.SelectionMode -cne 'Full') {
            . (Join-Path $PSScriptRoot 'rc-project-selection.ps1')
            $projectProposal = Get-RcProjectSelection $Request.RepositoryRoot $b $Request.CandidateSha
        }
    }
    if ($Request.SelectionMode -ceq 'ChangeAware') { $reasons += 'change-aware-unqualified' }
    $qualificationDigest = ''
    if ($Request.QualificationPath -and (Test-Path -LiteralPath $Request.QualificationPath)) {
        [void](Assert-ReleaseSelectionPath $Request.QualificationPath $Request.ReleaseRoot $Request.RepositoryRoot)
        # Reading a claim never activates it. Invalid JSON is a corrupt claim, not absence.
        $qualification = Get-Content -LiteralPath $Request.QualificationPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($qualification -isnot [pscustomobject]) { throw 'qualification-invalid' }
        $qualificationDigest = Get-NightlyFileSha256 $Request.QualificationPath
    }
    $selection = [ordered]@{ format='antiphon-release-selection-v1'; candidateSha=$Request.CandidateSha; baselineSha=$b
        requestedMode=$Request.SelectionMode; effectiveMode='Full'; coverageScope='Full'; proposal=$proposal; projectProposal=$projectProposal
        fallbackReasons=@($reasons | Sort-Object -Unique); executionEvidence=$false }
    return Save-ReleaseSelectionAttempt $Request $selection $contract $baseline.digest $qualificationDigest
}

function Get-ReleaseSelectionTrainDigest {
    # S6-A binds opaque input bytes only. Membership validation belongs to S6-D.
    param($Request)
    if (-not $Request.TrainManifestPath) { return '' }
    $path = Assert-ReleaseSelectionPath $Request.TrainManifestPath $Request.ReleaseRoot $Request.RepositoryRoot
    if (-not (Test-Path -LiteralPath $path)) { return '' }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'selection-train-input' }
    return Get-NightlyFileSha256 $path
}

function Save-ReleaseSelectionAttempt {
    param($Request, $Selection, $Contract, [string]$BaselineDigest, [string]$QualificationDigest)
    if ($Request.RunId -cnotmatch '^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$' -or $Request.Attempt -lt 1) { throw 'selection-run-identity' }
    if ($Request.CandidateId -cnotmatch '^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$') { throw 'selection-candidate-identity' }
    $candidateRoot = Join-Path (Join-Path $Request.ReleaseRoot 'candidates') $Request.CandidateId
    $dir = Join-Path (Join-Path $candidateRoot 'attempts') $Request.RunId
    $path = Assert-ReleaseSelectionPath (Join-Path $dir 'selection-attempt.json') $Request.ReleaseRoot $Request.RepositoryRoot
    $selectionPath = Assert-ReleaseSelectionPath (Join-Path $dir 'selection.json') $Request.ReleaseRoot $Request.RepositoryRoot
    $selectionDigest = Get-NightlySha256Text (ConvertTo-NightlyCanonicalJson $Selection)
    $attempt = [ordered]@{
        format='antiphon-release-selection-attempt-v1'; repository=$Request.Repository; candidateId=$Request.CandidateId
        candidateRef=$Request.CandidateRef; candidateSha=$Request.CandidateSha; baselineSha=$Selection.baselineSha
        requestedMode=$Request.SelectionMode; effectiveMode='Full'; coverageScope='Full'; profile=$Request.Profile
        runId=$Request.RunId; attempt=[int]$Request.Attempt; selectionDigest=$selectionDigest; contract=$Contract
        baselineDigest=$BaselineDigest; qualificationDigest=$QualificationDigest; trainDigest=(Get-ReleaseSelectionTrainDigest $Request)
        fallbackReasons=$Selection.fallbackReasons
        baselineReceiptPath=$Request.BaselineReceiptPath; qualificationPath=$Request.QualificationPath
        timingRoot=$Request.TimingRoot; releaseIntentPath=$Request.ReleaseIntentPath; trainManifestPath=$Request.TrainManifestPath
    }
    # Check both immutable files before either write, including an interrupted first write.
    foreach ($pair in @(@($path, $attempt), @($selectionPath, $Selection))) {
        if ((Test-Path -LiteralPath $pair[0]) -and [IO.File]::ReadAllText($pair[0]) -cne (ConvertTo-NightlyCanonicalJson $pair[1])) { throw 'selection-attempt-conflict' }
    }
    Write-ReleaseSelectionOnce $selectionPath $Selection
    Write-ReleaseSelectionOnce $path $attempt
    return @{ SelectionAttemptPath=$path; SelectionAttemptDigest=(Get-NightlyFileSha256 $path)
        SelectionPath=$selectionPath; SelectionDigest=$selectionDigest; BaselineSha=$Selection.baselineSha; Selection=$attempt }
}

function New-ReleaseSelectionAttempt {
    param([hashtable]$Request)
    $pwsh = Assert-ReleaseSelectionCapability $Request.SelectionMode
    if (-not $pwsh) {
        # Default Full remains usable by an old 5.1 bootstrap without installing a
        # selector runtime. No baseline, proposal or qualified claim is invented.
        if ($Request.BaselineSha -or $Request.BaselineReceiptPath -or $Request.QualificationPath) { throw 'selection-pwsh-required' }
        $selection = @{format='antiphon-release-selection-v1';candidateSha=$Request.CandidateSha;baselineSha='';requestedMode='Full'
            effectiveMode='Full';coverageScope='Full';proposal=$null;fallbackReasons=@('selector-runtime-unavailable');executionEvidence=$false}
        return Save-ReleaseSelectionAttempt $Request $selection $null '' ''
    }
    $requestPath = [IO.Path]::GetTempFileName(); $responsePath = [IO.Path]::GetTempFileName()
    try {
        [IO.File]::WriteAllText($requestPath, (ConvertTo-Json $Request -Depth 20), [Text.Encoding]::UTF8)
        $worker = Join-Path $PSScriptRoot 'release-selection-worker.ps1'
        $command = "& '" + $worker.Replace("'", "''") + "' -RequestPath '" + $requestPath.Replace("'", "''") + "' -ResponsePath '" + $responsePath.Replace("'", "''") + "'"
        $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
        if ($PSVersionTable.PSVersion.Major -ge 7) {
            . (Join-Path $PSScriptRoot 'checkpoint-process-runner.ps1')
            $start = New-Object Diagnostics.ProcessStartInfo($pwsh.Source)
            foreach ($arg in @('-NoProfile', '-NonInteractive', '-EncodedCommand', $encoded)) { $start.ArgumentList.Add($arg) }
            $run = [Antiphon.Checkpoints.CheckpointProcessRunner]::Run($start, 60000, $false)
            if ($run.TimedOut) { throw 'selection-worker-timeout' }
        } else {
            # 5.1's Arguments has no ArgumentList. Only a fixed switch list and
            # base64 are transported here; paths/Unicode are decoded by the child.
            if (-not ('Antiphon.ReleaseSelectionBridge' -as [type])) {
                Add-Type -TypeDefinition @'
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
namespace Antiphon {
    public static class ReleaseSelectionBridge {
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr a, string n);
        [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr j, IntPtr p);
        [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr j, int c, ref ExtendedLimits v, uint s);
        [DllImport("kernel32.dll")] static extern bool TerminateJobObject(IntPtr j, uint c);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [StructLayout(LayoutKind.Sequential)] struct BasicLimits {
            public long ProcessTime, JobTime; public uint Flags;
            public UIntPtr Minimum, Maximum; public uint Active; public UIntPtr Affinity; public uint Priority, Scheduling;
        }
        [StructLayout(LayoutKind.Sequential)] struct IoCounters { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
        [StructLayout(LayoutKind.Sequential)] struct ExtendedLimits {
            public BasicLimits Basic; public IoCounters Io; public UIntPtr ProcessMemory, JobMemory, PeakProcess, PeakJob;
        }
        public static void Run(string exe, string encoded) {
            IntPtr job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero) throw new InvalidOperationException("selection-worker-custody");
            Process process = null;
            try {
                var limits = new ExtendedLimits(); limits.Basic.Flags = 0x2000;
                if (!SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf(typeof(ExtendedLimits)))) throw new InvalidOperationException("selection-worker-custody");
                process = Process.Start(new ProcessStartInfo(exe, "-NoProfile -NonInteractive -EncodedCommand " + encoded) { UseShellExecute=false, CreateNoWindow=true });
                if (!AssignProcessToJobObject(job, process.Handle)) throw new InvalidOperationException("selection-worker-custody");
                if (!process.WaitForExit(60000)) throw new TimeoutException("selection-worker-timeout");
            } finally {
                TerminateJobObject(job, 1); CloseHandle(job);
                if (process != null) { if (!process.HasExited) process.Kill(); process.WaitForExit(1000); process.Dispose(); }
            }
        }
    }
}
'@
            }
            [Antiphon.ReleaseSelectionBridge]::Run($pwsh.Source, $encoded)
        }
        $response = Get-Content -LiteralPath $responsePath -Raw -Encoding UTF8 | ConvertFrom-Json
        if (-not $response -or -not $response.ok) { throw ('selection-refused:' + $response.error) }
        return $response.value
    } finally { [IO.File]::Delete($requestPath); [IO.File]::Delete($responsePath) }
}

function Read-ReleaseSelectionAttempt {
    param([hashtable]$Expected)
    foreach ($pair in @(@('SelectionAttemptPath','SelectionAttemptDigest'), @('SelectionPath','SelectionDigest'))) {
        $path = [string]$Expected[$pair[0]]; $digest = [string]$Expected[$pair[1]]
        if (-not $path -or $digest -cnotmatch '^[0-9a-f]{64}$' -or
            -not (Test-Path -LiteralPath $path -PathType Leaf) -or (Test-NightlyAncestorReparse $path) -or
            (Get-NightlyFileSha256 $path) -cne $digest) { throw 'selection-hop-digest' }
    }
    $attempt = Get-Content -LiteralPath $Expected.SelectionAttemptPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $selection = Get-Content -LiteralPath $Expected.SelectionPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($attempt.format -cne 'antiphon-release-selection-attempt-v1' -or $selection.format -cne 'antiphon-release-selection-v1' -or
        $attempt.selectionDigest -cne $Expected.SelectionDigest -or $attempt.effectiveMode -cne 'Full' -or
        $attempt.coverageScope -cne 'Full' -or $selection.effectiveMode -cne 'Full' -or $selection.coverageScope -cne 'Full') { throw 'selection-hop-scope' }
    foreach ($pair in @(@('candidateSha','CandidateSha'), @('baselineSha','BaselineSha'), @('requestedMode','SelectionMode'),
        @('runId','RunId'), @('attempt','Attempt'), @('profile','Profile'), @('timingRoot','TimingRoot'),
        @('releaseIntentPath','ReleaseIntentPath'), @('trainManifestPath','TrainManifestPath'),
        @('baselineReceiptPath','BaselineReceiptPath'), @('qualificationPath','QualificationPath'))) {
        if ([string]$attempt.($pair[0]) -cne [string]$Expected[$pair[1]]) { throw ('selection-hop-' + $pair[0]) }
    }
    if ($selection.candidateSha -cne $Expected.CandidateSha -or $selection.baselineSha -cne $Expected.BaselineSha) { throw 'selection-hop-candidateSha' }
    if ([string]$attempt.trainDigest -cne (Get-ReleaseSelectionTrainDigest $Expected)) { throw 'selection-hop-trainDigest' }
    if ($attempt.repository -cne $Expected.Repository -or $attempt.candidateId -cne $Expected.CandidateId -or
        $attempt.candidateRef -cne ($Expected.CandidateRef -replace '^origin/', '')) { throw 'selection-hop-candidate' }
    $root = Join-Path (Join-Path (Join-Path (Join-Path $Expected.ReleaseRoot 'candidates') $Expected.CandidateId) 'attempts') $Expected.RunId
    foreach ($pair in @(@('SelectionAttemptPath','selection-attempt.json'),@('SelectionPath','selection.json'))) {
        $expectedPath = Assert-ReleaseSelectionPath (Join-Path $root $pair[1]) $Expected.ReleaseRoot $Expected.RepositoryRoot
        if ($expectedPath -cne [IO.Path]::GetFullPath($Expected[$pair[0]])) { throw 'selection-hop-path' }
    }
    return $attempt
}

function Resolve-ReleaseSelectionMembers {
    param($Attempt, [object[]]$FullMembers, [object[]]$ProposedMembers)
    if ($Attempt.effectiveMode -ceq 'Full' -and $Attempt.coverageScope -ceq 'Full') { return $FullMembers }
    throw 'selected-execution-unqualified'
}

function Invoke-ReleaseSelectionHop {
    # JSON splatting preserves spaces/Unicode through a 5.1 parent as well as 7.
    # The existing orchestration owns/waits this child; its workload still uses
    # the native owner (or the diagnostic workload I/O seam).
    param([string[]]$ArgumentList, [string]$WorkingDirectory)
    $fileIndex = [array]::IndexOf($ArgumentList, '-File')
    if ($fileIndex -lt 0) { throw 'selection-hop-script' }
    $parameters = @{}
    for ($i = $fileIndex + 2; $i -lt $ArgumentList.Count; $i++) {
        $key = $ArgumentList[$i].TrimStart('-')
        if ($key -in @('NoSync', 'NoReport', 'WhatIf')) { $parameters[$key] = $true }
        else {
            $i++
            # ConvertFrom-Json on newer pwsh converts ISO strings to DateTime.
            # Encode string values so even lock timestamps retain their exact bytes.
            $parameters[$key] = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($ArgumentList[$i]))
        }
    }
    $request = [IO.Path]::GetTempFileName()
    try {
        [IO.File]::WriteAllText($request, (ConvertTo-Json @{script=$ArgumentList[$fileIndex + 1];parameters=$parameters} -Depth 8), [Text.Encoding]::UTF8)
        $command = '$ErrorActionPreference="Stop";$r=Get-Content -LiteralPath ''' + $request.Replace("'", "''") + ''' -Raw -Encoding UTF8|ConvertFrom-Json;$p=@{};foreach($v in $r.parameters.PSObject.Properties){if($v.Value -is [bool]){$p[$v.Name]=$v.Value}else{$p[$v.Name]=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($v.Value))}};& $r.script @p; exit $LASTEXITCODE'
        $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
        $pwsh = Assert-ReleaseSelectionCapability 'Shadow'
        $proc = Start-NightlyProcess -StartParams @{FilePath=$pwsh.Source;ArgumentList=@('-NoProfile','-NonInteractive','-EncodedCommand',$encoded)
            WorkingDirectory=$WorkingDirectory;PassThru=$true;NoNewWindow=$true;Wait=$true}
        try { return [pscustomobject]@{ExitCode=$proc.ExitCode;TimedOut=$false;Pid=$proc.Id} } finally { $proc.Dispose() }
    } finally { [IO.File]::Delete($request) }
}
