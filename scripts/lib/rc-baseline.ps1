#requires -Version 7.0
# CARD-1836 S7a. Read-only baseline proposals, never execution/carry authority.
. (Join-Path $PSScriptRoot 'rc-admission.ps1')

function Resolve-RcBaselineCommit {
    param([string]$RepositoryRoot,[string]$RefName)
    (Invoke-TestSelectionGit $RepositoryRoot @('rev-parse','--verify',($RefName+'^{commit}'))).Stdout.Trim()
}

function Read-RcBaselinePointer {
    param([string]$RepositoryRoot,$Pointer,$CurrentContext,[DateTimeOffset]$UtcNow)
    $envelope=Read-RcAdmissionJson $Pointer.path $Pointer.digest
    if ($envelope.platform -cne $CurrentContext.platform) { throw 'baseline-platform-mismatch' }
    # Admission verifies its own historical inputs. New-candidate input differences
    # belong to later dirty marking, not to baseline discovery.
    $inventory=Test-RcHistoricalEvidence $Pointer.evidenceRoot $envelope.inventorySha256
    if (-not $inventory.valid) { throw 'inventory-unproven' }
    $object=Get-RcAdmissionObject $inventory $Pointer.evidenceRoot $envelope.proofPath
    $proof=Read-RcAdmissionJson $object.path $object.digest
    $context=@{repository=$CurrentContext.repository;platform=$CurrentContext.platform;
        rcLine=$envelope.rcLine;contract=$envelope.contract}
    $readArgs=@{Path=$Pointer.path;ExpectedDigest=$Pointer.digest;EvidenceRoot=$Pointer.evidenceRoot;
        RepositoryRoot=$RepositoryRoot;ExpectedSha=$Pointer.sourceSha;ExpectedRunId=$envelope.runId;
        CurrentSha=$Pointer.sourceSha;CurrentManifest=$proof.manifest;Chunk=$proof.chunk;
        CurrentContext=$context;UtcNow=$UtcNow}
    $admission=Read-RcAdmission @readArgs
    if (-not $admission.eligible) { throw ('baseline-admission-unproven:'+($admission.reasons -join ',')) }
    return @{envelope=$envelope;admission=$admission}
}

function Get-RcReleaseBaseline {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$CandidateSha,
        [Parameter(Mandatory)]$CurrentContext,
        [AllowEmptyCollection()][object[]]$Pointers=@(),$RecordedBaseline=$null,
        [string]$HistoryPath,[string]$HistoryDigest,
        [DateTimeOffset]$UtcNow=[DateTimeOffset]::UtcNow)
    $result=@{format='antiphon-rc-baseline-proposal-v1';candidateSha=$CandidateSha;selected=$null;
        reason='baseline-absent';reasons=@();fullAnchor=$null;failures=@();historyDigest=$HistoryDigest;
        forceFull=$true;executionEvidence=$false;gateCredit=$false}
    # Like Get-TestSelection: a proposal only reads objects and refs, none of which can change the filter/textconv
    # driver settings the runner asks git for before each command, so that answer is reused for this proposal
    # (60 s per directory) and the caller's setting is restored after it. On Windows the second process per git
    # call cost about 15 of 58 s of one proposal case (CARD-1947).
    if (-not ('Antiphon.Checkpoints.CheckpointProcessRunner' -as [type])) { . (Join-Path $PSScriptRoot 'checkpoint-process-runner.ps1') }
    $savedDriverCache=[Antiphon.Checkpoints.CheckpointProcessRunner]::DriverSettingsCacheMilliseconds
    [Antiphon.Checkpoints.CheckpointProcessRunner]::DriverSettingsCacheMilliseconds=60000
    try {
        if ($CandidateSha -cnotmatch '\A[0-9a-f]{40}\z' -or
            (Resolve-RcBaselineCommit $RepositoryRoot $CandidateSha) -cne $CandidateSha) { throw 'baseline-candidate-invalid' }
        if ($CurrentContext.platform -cnotin @('windows','linux') -or
            $CurrentContext.rcLine -cnotmatch '\Arc/\d{4}-\d{2}-\d{2}\z') { throw 'baseline-context-invalid' }
        $seen=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
        foreach ($pointer in $Pointers) {
            Assert-TestSelectionKeys $pointer @('refName','sourceSha','path','digest','evidenceRoot')
            foreach ($field in @('refName','sourceSha','path','digest','evidenceRoot')) {
                if ($pointer[$field] -isnot [string] -or -not $pointer[$field]) { throw 'baseline-pointer-invalid' }
            }
            if ($pointer.sourceSha -cnotmatch '\A[0-9a-f]{40}\z' -or -not (Test-RcFullDigest $pointer.digest) -or
                ($pointer.refName -cnotmatch '\Arefs/heads/release/\d+\.\d+\.\d+\z' -and $pointer.refName -cne $pointer.sourceSha)) { throw 'baseline-pointer-invalid' }
            if ($seen.ContainsKey($pointer.refName)) { throw 'baseline-pointer-conflict' }
            $seen.Add($pointer.refName,$pointer)
        }
        if ($null -ne $RecordedBaseline) {
            Assert-TestSelectionKeys $RecordedBaseline @('refName','sourceSha','path','digest','evidenceRoot')
            if ($RecordedBaseline.refName -cnotmatch '\Arefs/heads/release/\d+\.\d+\.\d+\z' -or
                -not $seen.ContainsKey($RecordedBaseline.refName)) { throw 'baseline-recorded-pointer-missing' }
            foreach ($field in @('refName','sourceSha','path','digest','evidenceRoot')) {
                if ($RecordedBaseline[$field] -cne $seen[$RecordedBaseline.refName][$field]) { throw 'baseline-recorded-pointer-conflict' }
            }
        }
        if ($Pointers.Count -eq 0) { throw 'baseline-absent' }
        $history=Read-RcAdmissionJson $HistoryPath $HistoryDigest
        Assert-TestSelectionKeys $history @('format','candidateSha','complete','failures')
        if ($history.format -cne 'antiphon-rc-baseline-history-v1' -or $history.candidateSha -cne $CandidateSha -or
            $history.complete -isnot [bool] -or -not $history.complete -or $history.failures -isnot [array]) { throw 'baseline-history-unproven' }
        foreach ($failure in $history.failures) {
            Assert-TestSelectionKeys $failure @('sourceSha','identity','outcome')
            if ($failure.sourceSha -cnotmatch '\A[0-9a-f]{40}\z' -or $failure.identity -isnot [string] -or -not $failure.identity -or
                $failure.outcome -cnotin @('Failed','Interrupted','NotRun','Unproven') -or
                (Invoke-TestSelectionGit $RepositoryRoot @('merge-base','--is-ancestor',$failure.sourceSha,$CandidateSha) @(0,1)).Exit -ne 0) { throw 'baseline-history-unproven' }
        }
        $eligible=[Collections.Generic.List[object]]::new()
        foreach ($pointer in $Pointers) {
            if ((Resolve-RcBaselineCommit $RepositoryRoot $pointer.refName) -cne $pointer.sourceSha) { throw 'baseline-ref-moved' }
            if ($pointer.sourceSha -ceq $CandidateSha -or
                (Invoke-TestSelectionGit $RepositoryRoot @('merge-base','--is-ancestor',$pointer.sourceSha,$CandidateSha) @(0,1)).Exit -ne 0) { continue }
            $read=Read-RcBaselinePointer $RepositoryRoot $pointer $CurrentContext $UtcNow
            $release=$pointer.refName.StartsWith('refs/heads/release/',[StringComparison]::Ordinal)
            if (-not $release -and $read.envelope.rcLine -cne $CurrentContext.rcLine) { continue }
            $date=(Invoke-TestSelectionGit $RepositoryRoot @('show','-s','--format=%ct',$pointer.sourceSha)).Stdout.Trim()
            $timestamp=0L
            if (-not [long]::TryParse($date,[ref]$timestamp)) { throw 'baseline-commit-date-invalid' }
            $eligible.Add(@{pointer=$pointer;read=$read;release=$release;timestamp=$timestamp})
        }
        # Explicit release, then newest release, then newest same-line candidate.
        # Ordinal comparison avoids dependence on host culture or caller ordering.
        $chosen=$null
        foreach ($entry in $eligible) {
            if ($RecordedBaseline -and $entry.pointer.refName -ceq $RecordedBaseline.refName) { $chosen=$entry; break }
            if ($null -eq $chosen -or ($entry.release -and -not $chosen.release) -or
                ($entry.release -eq $chosen.release -and ($entry.timestamp -gt $chosen.timestamp -or
                    ($entry.timestamp -eq $chosen.timestamp -and [string]::CompareOrdinal($entry.pointer.refName,$chosen.pointer.refName) -lt 0)))) { $chosen=$entry }
        }
        if ($null -eq $chosen) { throw 'baseline-absent' }
        # Bind bytes again, then the ref immediately before returning; no writes.
        $read=Read-RcBaselinePointer $RepositoryRoot $chosen.pointer $CurrentContext $UtcNow
        [void](Read-RcAdmissionJson $HistoryPath $HistoryDigest)
        if ((Resolve-RcBaselineCommit $RepositoryRoot $chosen.pointer.refName) -cne $chosen.pointer.sourceSha -or
            (Invoke-TestSelectionGit $RepositoryRoot @('merge-base','--is-ancestor',$chosen.pointer.sourceSha,$CandidateSha) @(0,1)).Exit -ne 0) { throw 'baseline-ref-moved' }
        $forced=Get-RcForcedFullDecision $CurrentContext.contract $read.envelope.contract $read.envelope.history $read.envelope.fullAnchor -UtcNow $UtcNow
        $reasons=[Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
        foreach ($reason in (@($read.admission.reasons)+@($forced.reasons))) { [void]$reasons.Add($reason) }
        if ($history.failures.Count) { [void]$reasons.Add('post-baseline-failures') }
        $result.selected=@{}; foreach ($field in @('refName','sourceSha','path','digest','evidenceRoot')) { $result.selected[$field]=$chosen.pointer[$field] }
        $result.fullAnchor=$read.envelope.fullAnchor
        $result.failures=$history.failures
        $result.reason=if ($RecordedBaseline -and $chosen.pointer.refName -ceq $RecordedBaseline.refName) { 'recorded-release' }
            elseif ($chosen.release) { 'ancestor-release' } else { 'same-line-candidate' }
        $result.reasons=@($reasons)
        $result.forceFull=$read.admission.forceFull -or $forced.forceFull -or $history.failures.Count -gt 0
    } catch {
        $result.selected=$null; $result.fullAnchor=$null; $result.forceFull=$true
        $result.reason=$_.Exception.Message; $result.reasons=@($_.Exception.Message)
    } finally { [Antiphon.Checkpoints.CheckpointProcessRunner]::DriverSettingsCacheMilliseconds=$savedDriverCache }
    return $result
}
