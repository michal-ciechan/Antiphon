#requires -Version 7.0
# CARD-1545 S2b: live-lane execution receipts, lane state and use records. Diagnostic and
# shadow-only: no receipt is trusted (D-9 is S2c), no state carries gate credit, and a reuse is a
# use record, never an execution. Every verdict is recomputed from the receipt's own facts.
. "$PSScriptRoot/live-lane-fingerprint.ps1"

$script:LiveLaneReceiptFields=@('account','artifacts','authority','classifierVerdict','durationMs','endedUtc','executed','external','failed',
    'finalExit','fingerprint','host','inputAfter','inputBefore','kind','laneId','missing','passed','planned','ref','repository','rows','runId',
    'schemaVersion','sequence','skipped','sourceClean','sourceSha','startedUtc')
$script:LiveLaneUseFields=@('candidateSha','createdUtc','executed','executedNow','failed','fingerprintDigest','gateCredit','kind','laneId',
    'passed','receiptDigest','reused','runId','schemaVersion','skipped')
$script:LiveLaneRevocationFields=@('kind','reason','receiptDigest','schemaVersion')
$script:LiveLaneRowOutcomes=@('Passed','Failed','Skipped')
$script:LiveLaneVerdicts=@(0,1,2,20)
$script:LiveLaneFingerprintNames=@('environment','instructions','laneContract','liveRoots','repositoryClosure','runtimeInputs','testSources')

function Get-LiveLaneRosterDigest {
    param([string[]]$Roster)
    return Get-TestSelectionDigest @(Get-TestSelectionSorted @($Roster))
}

function Read-LiveLaneRosterFile {
    # A manifest copy on disk -> its expanded core roster.
    param([string]$Path)
    try { $manifest=[IO.File]::ReadAllText($Path) | ConvertFrom-Json -AsHashtable -Depth 64 } catch { throw 'manifest-invalid' }
    return ConvertTo-LiveLaneRoster $manifest
}

function ConvertTo-LiveLaneUtc {
    # A recorded time: a UTC DateTime from the JSON reader or an ISO-8601 'Z' string; else $null.
    param($Value)
    if ($Value -is [datetime]) { if ($Value.Kind -eq [DateTimeKind]::Utc) { return [DateTimeOffset]::new($Value) }; return $null }
    if ($Value -is [string] -and $Value -cmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?Z$') {
        $parsed=[DateTimeOffset]::MinValue
        if ([DateTimeOffset]::TryParse($Value,[Globalization.CultureInfo]::InvariantCulture,[Globalization.DateTimeStyles]::AssumeUniversal,[ref]$parsed)) { return $parsed }
    }
    return $null
}

function Test-LiveLaneField {
    # One typed field: a null is a missing requirement, a wrong type is a type fault.
    param($Reasons, $Map, [string]$Name, [string]$Type, [string]$Label = $Name)
    $value=$Map[$Name]
    if ($null -eq $value) { $Reasons.Add("required:$Label"); return $false }
    $ok=switch ($Type) {
        'string' { $value -is [string] -and $value.Length -gt 0 -and $value.Length -le 256 }
        'int' { $value -is [long] -or $value -is [int] }
        'bool' { $value -is [bool] }
        'sha1' { $value -is [string] -and $value -cmatch '^[0-9a-f]{40}$' }
        'sha256' { $value -is [string] -and $value -cmatch '^[0-9a-f]{64}$' }
        'time' { $null -ne (ConvertTo-LiveLaneUtc $value) }
        'object' { $value -is [Collections.IDictionary] }
        'list' { $value -is [Collections.IList] }
    }
    if (-not $ok) { $Reasons.Add("type:$Label"); return $false }
    if ($Type -ceq 'int' -and $value -lt 0) { $Reasons.Add("negative:$Label"); return $false }
    return $true
}

function Test-LiveLaneExactKeys {
    # Unknown fields are named only when the name is a plain identifier; a missing one is required.
    param($Reasons, $Map, [string[]]$Keys, [string]$Prefix = '')
    foreach ($key in $Map.Keys) {
        if (-not ($Keys -ccontains $key)) { $Reasons.Add($(if ($key -cmatch '^[A-Za-z][A-Za-z0-9]{0,40}$') { "field:$Prefix$key" } else { 'field:unnamed' })) }
    }
    foreach ($key in $Keys) { if (-not $Map.Contains($key)) { $Reasons.Add("required:$Prefix$key") } }
}

function Test-LiveLaneReceipt {
    # Validates one live-lane-execution receipt against the lane roster (-Manifest path or -Roster).
    # Returns valid, sorted reasons, the recomputed outcome and exit code, and the file digest.
    param([string]$Receipt, [string]$Manifest, [string[]]$Roster, [string]$RosterDigest)
    $reasons=[Collections.Generic.List[string]]::new()
    $invalid={ param([string[]]$why, $digest, $document)
        return [ordered]@{valid=$false;reasons=@(Get-TestSelectionSorted @($why));outcome='INVALID';code=2;digest=$digest;receipt=$document} }
    try { $read=Read-LiveLaneJsonDocument $Receipt 'receipt-unreadable' } catch { return & $invalid @($_.Exception.Message) $null $null }
    $document=$read.document
    if ($document -isnot [Collections.IDictionary]) { return & $invalid @('receipt-unreadable') $read.sha256 $null }
    try { Assert-LiveLaneCredentialFree $document } catch { return & $invalid @('credential-field-present') $read.sha256 $null }
    if ($Manifest) { $Roster=Read-LiveLaneRosterFile $Manifest }
    $rosterSet=[Collections.Generic.HashSet[string]]::new([string[]]@($Roster | Where-Object { $_ -is [string] -and $_ }),[StringComparer]::Ordinal)
    if (-not $RosterDigest) { $RosterDigest=Get-LiveLaneRosterDigest @($rosterSet) }

    Test-LiveLaneExactKeys $reasons $document $script:LiveLaneReceiptFields
    if ($document['kind'] -cne 'live-lane-execution') { $reasons.Add('kind') }
    if ($document['schemaVersion'] -isnot [long] -or $document['schemaVersion'] -ne 1) { $reasons.Add('schema') }
    foreach ($name in 'laneId','runId','repository','ref') { [void](Test-LiveLaneField $reasons $document $name 'string') }
    [void](Test-LiveLaneField $reasons $document 'sourceSha' 'sha1')
    [void](Test-LiveLaneField $reasons $document 'sourceClean' 'bool')
    foreach ($name in 'inputBefore','inputAfter') { [void](Test-LiveLaneField $reasons $document $name 'sha256') }
    # D-9 is out of scope: only a diagnostic receipt exists in S2, and it earns nothing.
    if ($document['authority'] -cne 'diagnostic') { $reasons.Add('issuer-unqualified') }
    $counts=@{}
    foreach ($name in 'sequence','durationMs','planned','executed','missing','passed','failed','skipped','classifierVerdict','finalExit') {
        if (Test-LiveLaneField $reasons $document $name 'int') { $counts[$name]=[long]$document[$name] }
    }
    foreach ($side in 'host','account') {
        if (Test-LiveLaneField $reasons $document $side 'object') {
            Test-LiveLaneExactKeys $reasons $document[$side] @('enrollmentId') "$side."
            [void](Test-LiveLaneField $reasons $document[$side] 'enrollmentId' 'string' "$side.enrollmentId")
        }
    }
    if (Test-LiveLaneField $reasons $document 'external' 'object') {
        Test-LiveLaneExactKeys $reasons $document['external'] @('epoch') 'external.'
        if ($null -ne $document['external']['epoch'] -and $document['external']['epoch'] -isnot [string]) { $reasons.Add('type:external.epoch') }
    }
    $started=$null; $ended=$null
    if (Test-LiveLaneField $reasons $document 'startedUtc' 'time') { $started=ConvertTo-LiveLaneUtc $document['startedUtc'] }
    if (Test-LiveLaneField $reasons $document 'endedUtc' 'time') { $ended=ConvertTo-LiveLaneUtc $document['endedUtc'] }
    if ($started -and $ended -and $ended -lt $started) { $reasons.Add('time-order') }
    if ($document['inputBefore'] -is [string] -and $document['inputBefore'] -cne $document['inputAfter']) { $reasons.Add('input-drift') }

    # Fingerprint: the stored digest must recompute from its components; nothing is trusted.
    $fingerprint=$document['fingerprint']
    if (Test-LiveLaneField $reasons $document 'fingerprint' 'object') {
        Test-LiveLaneExactKeys $reasons $fingerprint @('components','digest','rosterDigest') 'fingerprint.'
        $digestOk=Test-LiveLaneField $reasons $fingerprint 'digest' 'sha256' 'fingerprint.digest'
        [void](Test-LiveLaneField $reasons $fingerprint 'rosterDigest' 'sha256' 'fingerprint.rosterDigest')
        if (Test-LiveLaneField $reasons $fingerprint 'components' 'object' 'fingerprint.components') {
            $componentsOk=$true
            foreach ($name in $fingerprint['components'].Keys) {
                if (-not ($script:LiveLaneFingerprintNames -ccontains $name) -or $fingerprint['components'][$name] -isnot [string] -or
                        $fingerprint['components'][$name] -cnotmatch '^[0-9a-f]{64}$') { $componentsOk=$false }
            }
            if (-not $componentsOk) { $reasons.Add('type:fingerprint.components') }
            elseif ($digestOk -and $document['laneId'] -is [string] -and
                    (Get-LiveLaneFingerprintDigest $document['laneId'] $fingerprint['components']) -cne $fingerprint['digest']) { $reasons.Add('fingerprint-tamper') }
        }
        if ($fingerprint['rosterDigest'] -is [string] -and $fingerprint['rosterDigest'] -cne $RosterDigest) { $reasons.Add('roster-changed') }
    }

    # Rows: an exact multiset equal to the roster, closed outcomes, counts equal to the rows.
    $tally=@{Passed=0;Failed=0;Skipped=0}
    $rows=@()
    if (Test-LiveLaneField $reasons $document 'rows' 'list') {
        $rows=@($document['rows'])
        $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        if (-not $rosterSet.Count -or -not $rows.Count) { $reasons.Add('roster-empty') }
        foreach ($row in $rows) {
            if ($row -isnot [Collections.IDictionary]) { $reasons.Add('type:rows'); continue }
            Test-LiveLaneExactKeys $reasons $row @('outcome','uid') 'rows.'
            if ($row['uid'] -isnot [string] -or -not $row['uid']) { $reasons.Add('type:rows.uid'); continue }
            if (-not $seen.Add($row['uid'])) { $reasons.Add('roster-duplicate') }
            if (-not $rosterSet.Contains($row['uid'])) { $reasons.Add('roster-unexpected') }
            if ($row['outcome'] -isnot [string] -or -not ($script:LiveLaneRowOutcomes -ccontains $row['outcome'])) { $reasons.Add('outcome-enum') }
            else { $tally[$row['outcome']]++ }
        }
        foreach ($uid in $rosterSet) { if (-not $seen.Contains($uid)) { $reasons.Add('roster-incomplete') } }
    }
    if ($counts.Count -eq 10) {
        if ($counts.passed -ne $tally.Passed -or $counts.failed -ne $tally.Failed -or $counts.skipped -ne $tally.Skipped -or
            $counts.executed -ne $rows.Count -or $counts.planned -ne ($counts.executed+$counts.missing)) { $reasons.Add('counts-mismatch') }
        if ($counts.missing -ne 0 -or $counts.planned -ne $rosterSet.Count -or $counts.executed -lt $counts.planned) { $reasons.Add('roster-incomplete') }
        if (-not ($script:LiveLaneVerdicts -contains $counts.classifierVerdict)) { $reasons.Add('classifier-enum') }
        elseif ($counts.finalExit -ne $counts.classifierVerdict) { $reasons.Add('classifier-mismatch') }
    }

    # Artifacts: each named file beside the receipt must hash to its recorded digest.
    if (Test-LiveLaneField $reasons $document 'artifacts' 'list') {
        $directory=[IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Receipt))
        foreach ($artifact in $document['artifacts']) {
            if ($artifact -isnot [Collections.IDictionary] -or $artifact['name'] -isnot [string] -or $artifact['name'] -cnotmatch '^[A-Za-z0-9_.-]{1,64}$' -or
                    $artifact['sha256'] -isnot [string] -or $artifact['sha256'] -cnotmatch '^[0-9a-f]{64}$' -or @($artifact.Keys).Count -ne 2) { $reasons.Add('type:artifacts'); continue }
            $path=Join-Path $directory $artifact['name']
            try {
                if (([IO.File]::GetAttributes($path)).HasFlag([IO.FileAttributes]::ReparsePoint)) { $reasons.Add("artifact-digest:$($artifact['name'])"); continue }
                $actual=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($path))).ToLowerInvariant()
            } catch { $reasons.Add("artifact-missing:$($artifact['name'])"); continue }
            if ($actual -cne $artifact['sha256']) { $reasons.Add("artifact-digest:$($artifact['name'])") }
        }
    }

    $sorted=@(Get-TestSelectionSorted @($reasons))
    if ($sorted.Count) { return & $invalid $sorted $read.sha256 $document }
    # Outcome: the worse of the rows and the classifier; a skip is never green.
    $rowsCode=if ($tally.Failed) { 1 } elseif ($tally.Skipped) { 20 } else { 0 }
    $code=if ($counts.classifierVerdict -eq 2) { 2 } elseif ($rowsCode -eq 1 -or $counts.classifierVerdict -eq 1) { 1 }
        elseif ($rowsCode -eq 20 -or $counts.classifierVerdict -eq 20) { 20 } else { 0 }
    return [ordered]@{valid=$true;reasons=@();outcome=(Get-LiveLaneReceiptOutcome $code);code=$code;digest=$read.sha256;receipt=$document
        endedUtc=$ended;sequence=$counts.sequence}
}

function Get-LiveLaneReceiptOutcome([int]$Code) {
    switch ($Code) { 0 { return 'GREEN' }; 1 { return 'RED' }; 20 { return 'INCONCLUSIVE' }; default { return 'INVALID' } }
}

function Test-LiveLaneUseRecord {
    # A use record says a receipt was reused: it executed nothing and earns nothing.
    param([string]$Path)
    $reasons=[Collections.Generic.List[string]]::new()
    try { $read=Read-LiveLaneJsonDocument $Path 'use-unreadable' } catch { return [ordered]@{valid=$false;reasons=@($_.Exception.Message);digest=$null;record=$null} }
    $record=$read.document
    if ($record -isnot [Collections.IDictionary]) { return [ordered]@{valid=$false;reasons=@('use-unreadable');digest=$read.sha256;record=$null} }
    Test-LiveLaneExactKeys $reasons $record $script:LiveLaneUseFields
    if ($record['kind'] -cne 'live-lane-use') { $reasons.Add('kind') }
    if ($record['schemaVersion'] -isnot [long] -or $record['schemaVersion'] -ne 1) { $reasons.Add('schema') }
    foreach ($name in 'laneId','runId') { [void](Test-LiveLaneField $reasons $record $name 'string') }
    [void](Test-LiveLaneField $reasons $record 'candidateSha' 'sha1')
    foreach ($name in 'receiptDigest','fingerprintDigest') { [void](Test-LiveLaneField $reasons $record $name 'sha256') }
    [void](Test-LiveLaneField $reasons $record 'createdUtc' 'time')
    foreach ($name in 'executedNow','executed','passed','failed','skipped') {
        if ((Test-LiveLaneField $reasons $record $name 'int') -and $record[$name] -ne 0) { $reasons.Add('use-executed') }
    }
    if ($record['reused'] -isnot [bool] -or -not $record['reused']) { $reasons.Add('type:reused') }
    if ($record['gateCredit'] -isnot [bool] -or $record['gateCredit']) { $reasons.Add('use-gate-credit') }
    return [ordered]@{valid=($reasons.Count -eq 0);reasons=@(Get-TestSelectionSorted @($reasons));digest=$read.sha256;record=$record}
}

function Get-LiveLaneState {
    # Shadow-only lane state for one candidate. Unknown input decides before any receipt; then the
    # newest attempt for this fingerprint decides; then identity, lineage and D-6 freshness.
    param($Candidate, [string[]]$History = @(), [string[]]$Roster, [string]$RepositoryRoot, [string]$ReusePolicy = 'AlwaysRerun',
        $NowUtc, $Probes = @(), $Revocations = @())
    $reasons=[Collections.Generic.List[string]]::new()
    $result=[ordered]@{format='antiphon-live-lane-state-v1';laneId=$Candidate['laneId'];candidateSha=$Candidate['sha'];state='DUE';reasons=@()
        localCompatibility='DUE';reuseEligible=$false;policy=$ReusePolicy;selected=$null;lastGreen=$null;freshness=$null
        effectiveMode='Full';activation='shadow-only';gateCredit=$false;executionEvidence=$false}
    $finish={ $result.reasons=@(Get-TestSelectionSorted @($reasons)); return $result }

    $fingerprint=$Candidate['fingerprint']
    if ($fingerprint -isnot [Collections.IDictionary] -or $fingerprint['status'] -cne 'complete') { $reasons.Add('unknown-input') }
    $environment=if ($fingerprint -is [Collections.IDictionary]) { $fingerprint['environment'] }
    if ($fingerprint -isnot [Collections.IDictionary] -or $fingerprint['components'] -isnot [Collections.IDictionary] -or
        -not $fingerprint['components'].Contains('environment') -or $environment -isnot [Collections.IDictionary] -or $environment['status'] -cne 'complete') { $reasons.Add('environment-unknown') }
    if ($reasons.Count) { return & $finish }
    $rosterDigest=Get-LiveLaneRosterDigest $Roster

    # History: every record validated; one run id has one receipt.
    $executions=[Collections.Generic.List[object]]::new()
    $uses=[Collections.Generic.List[object]]::new()
    $revoked=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $runs=[Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    foreach ($path in @($History)) {
        try { $kind=(Read-LiveLaneJsonDocument $path 'history-unreadable').document['kind'] } catch { $reasons.Add('history-invalid'); continue }
        if ($kind -ceq 'live-lane-execution') {
            $check=Test-LiveLaneReceipt -Receipt $path -Roster $Roster -RosterDigest $rosterDigest
            if ($check.receipt -is [Collections.IDictionary] -and $check.receipt['runId'] -is [string]) {
                $run=$check.receipt['runId']
                if ($runs.ContainsKey($run) -and $runs[$run] -cne $check.digest) { throw 'contradictory-receipts' }
                $runs[$run]=$check.digest
            }
            if ($check.valid) { $executions.Add($check) }
            elseif (@($check.reasons) -ccontains 'roster-changed') {
                if ($check.receipt['fingerprint'] -is [Collections.IDictionary] -and $check.receipt['fingerprint']['digest'] -ceq $fingerprint['digest']) { $reasons.Add('roster-changed') }
            } else { $reasons.Add('history-invalid') }
        } elseif ($kind -ceq 'live-lane-use') {
            $use=Test-LiveLaneUseRecord $path
            if ($use.valid) { $uses.Add($use) } else { $reasons.Add('history-invalid') }
        } elseif ($kind -ceq 'live-lane-revocation') {
            $revocation=(Read-LiveLaneJsonDocument $path 'history-unreadable').document
            $keys=@(Get-TestSelectionSorted @($revocation.Keys))
            if (($keys -join ',') -cne ($script:LiveLaneRevocationFields -join ',') -or $revocation['schemaVersion'] -isnot [long] -or $revocation['schemaVersion'] -ne 1 -or
                $revocation['receiptDigest'] -isnot [string] -or $revocation['receiptDigest'] -cnotmatch '^[0-9a-f]{64}$') { $reasons.Add('history-invalid') }
            else { [void]$revoked.Add($revocation['receiptDigest']) }
        } else { $reasons.Add('history-invalid') }
    }
    # Another lane's receipt never stands for this lane.
    if (@($executions | Where-Object { $_.receipt['laneId'] -cne $Candidate['laneId'] }).Count) { $reasons.Add('foreign-lane') }
    $executions=@($executions | Where-Object { $_.receipt['laneId'] -ceq $Candidate['laneId'] })
    # Recorded time, then sequence, then digest: never the enumeration order.
    $ordered=@($executions | Sort-Object -Property @{Expression={ $_.endedUtc.UtcTicks }},@{Expression={ $_.sequence }},@{Expression={ $_.digest }})
    $greens=@($ordered | Where-Object { $_.outcome -ceq 'GREEN' })
    if ($greens.Count) { $result.lastGreen=$greens[-1].digest }

    $same=@($ordered | Where-Object { $_.receipt['fingerprint']['digest'] -ceq $fingerprint['digest'] })
    if (@($same | Where-Object { -not $_.receipt['sourceClean'] }).Count) { $reasons.Add('source-dirty') }
    $same=@($same | Where-Object { $_.receipt['sourceClean'] })
    if (-not $same.Count) {
        $reasons.Add('no-execution-receipt')
        if ($ordered.Count) {
            $other=$ordered[-1].receipt['fingerprint']['components']
            foreach ($name in (Get-TestSelectionSorted (@($other.Keys)+@($fingerprint['components'].Keys)))) {
                if ($other[$name] -cne $fingerprint['components'][$name]) { $reasons.Add("fingerprint-changed:$name") }
            }
        }
        return & $finish
    }
    $latest=$same[-1]
    switch ($latest.outcome) { 'RED' { $reasons.Add('newer-red') }; 'INCONCLUSIVE' { $reasons.Add('newer-inconclusive') }; 'INVALID' { $reasons.Add('newer-invalid') } }
    if ($reasons.Count) { return & $finish }

    # Identity and lineage of the selected green, all before any freshness question.
    $receipt=$latest.receipt
    if ($revoked.Contains($latest.digest)) { $reasons.Add('revoked') }
    if ($receipt['repository'] -cne $Candidate['repository']) { $reasons.Add('foreign-repository') }
    if ($receipt['ref'] -cne $Candidate['ref']) { $reasons.Add('foreign-ref') }
    if ($receipt['host']['enrollmentId'] -cne $Candidate['host']) { $reasons.Add('host-mismatch') }
    if ($receipt['account']['enrollmentId'] -cne $Candidate['account']) { $reasons.Add('account-mismatch') }
    $present=(Invoke-TestSelectionGit $RepositoryRoot @('cat-file','-e',"$($receipt['sourceSha'])^{commit}") -AllowedExit @(0,1,128)).Exit -eq 0
    if (-not $present) { $reasons.Add('source-missing') }
    else {
        $ancestry=(Invoke-TestSelectionGit $RepositoryRoot @('merge-base','--is-ancestor',$receipt['sourceSha'],$Candidate['sha']) -AllowedExit @(0,1,128)).Exit
        if ($ancestry -eq 1) { $reasons.Add('not-ancestor') } elseif ($ancestry -ne 0) { $reasons.Add('source-missing') }
    }
    if ($reasons.Count) { return & $finish }
    $result.localCompatibility='SATISFIED'
    $result.selected=[ordered]@{receiptDigest=$latest.digest;runId=$receipt['runId'];fingerprintDigest=$fingerprint['digest']}

    # D-6 with the receipt's own end time; AlwaysRerun refuses here.
    $freshness=Test-LiveLaneExternalFreshness -Policy $ReusePolicy -ExecutedAtUtc $latest.endedUtc -NowUtc $NowUtc `
        -Candidate @{nonce=$Candidate['nonce'];host=$Candidate['host'];account=$Candidate['account']} `
        -Executed @{epoch=$receipt['external']['epoch'];host=$receipt['host']['enrollmentId'];account=$receipt['account']['enrollmentId'];receiptId=$latest.digest} `
        -Probes $Probes -Revocations $Revocations
    $result.freshness=$freshness
    $result.reuseEligible=[bool]$freshness.reuseEligible
    if ($freshness.reuseEligible) { $result.state='SATISFIED' } else { foreach ($why in $freshness.reasons) { $reasons.Add($why) } }
    return & $finish
}

function New-LiveLaneUseRecord {
    # Records a reuse of the selected receipt. Writes a new file once; never touches the receipt.
    param($State, [string]$Path, $NowUtc)
    if ($State -isnot [Collections.IDictionary] -or $State['state'] -cne 'SATISFIED' -or $State['selected'] -isnot [Collections.IDictionary]) { throw 'state-not-satisfied' }
    if ($null -eq $NowUtc) { throw 'time-invalid' }
    $now=ConvertTo-LiveLaneUtc ([DateTimeOffset]$NowUtc).UtcDateTime
    $record=[ordered]@{kind='live-lane-use';schemaVersion=1;laneId=$State['laneId'];candidateSha=$State['candidateSha']
        receiptDigest=$State['selected']['receiptDigest'];runId=$State['selected']['runId'];fingerprintDigest=$State['selected']['fingerprintDigest']
        reused=$true;executedNow=0;executed=0;passed=0;failed=0;skipped=0;gateCredit=$false
        createdUtc=$now.UtcDateTime.ToString('yyyy-MM-ddTHH:mm:ssZ',[Globalization.CultureInfo]::InvariantCulture)}
    $bytes=[Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-TestSelectionCanonicalJson $record))
    try { $stream=[IO.FileStream]::new($Path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None) }
    catch [IO.IOException] { if ([IO.File]::Exists($Path) -or [IO.Directory]::Exists($Path)) { throw 'use-record-exists' }; throw 'use-record-unwritable' }
    catch { throw 'use-record-unwritable' }
    try { $stream.Write($bytes,0,$bytes.Length) } finally { $stream.Dispose() }
    return $record
}

function Get-LiveLaneHistoryStatus {
    # Status with receipts: the candidate fingerprint (with its attestation), the lane roster at
    # the candidate, and the binding context (repository, rc ref, host, account, nonce, clock,
    # probes, revocations). A use record is written only for a SATISFIED state and only on request.
    param([string]$RepositoryRoot, [string]$Lane, [string]$CandidateSha, [string]$Attestation, [string]$History, [string]$Context,
        [string]$ReusePolicy = 'AlwaysRerun', [string]$UseRecord)
    if (-not (@('AlwaysRerun','Bounded24h') -ccontains $ReusePolicy)) { throw 'policy-invalid' }
    if (-not $Context) { throw 'context-required' }
    $binding=(Read-LiveLaneJsonDocument $Context 'context-invalid').document
    $keys=@(if ($binding -is [Collections.IDictionary]) { Get-TestSelectionSorted @($binding.Keys) })
    if (($keys -join ',') -cne 'account,host,nonce,nowUtc,probes,ref,repository,revocations' -or $binding['probes'] -isnot [Collections.IList] -or
        $binding['revocations'] -isnot [Collections.IList] -or $null -eq (ConvertTo-LiveLaneUtc $binding['nowUtc'])) { throw 'context-invalid' }
    if (-not [IO.Directory]::Exists($History) -or ([IO.File]::GetAttributes($History)).HasFlag([IO.FileAttributes]::ReparsePoint)) { throw 'history-invalid' }
    $paths=@(Get-TestSelectionSorted @([IO.Directory]::GetFiles($History,'*.json')))
    if ($Attestation) { [void](Get-LiveLaneEnvironmentAttestation -Path $Attestation -Lane $Lane) }
    $fingerprint=Get-LiveLaneFingerprint -RepositoryRoot $RepositoryRoot -Sha $CandidateSha -Lane $Lane -Attestation $Attestation
    $tree=Read-LiveLaneTree $RepositoryRoot $CandidateSha
    $declaration=Read-LiveLaneDeclaration $RepositoryRoot $CandidateSha $tree $Lane
    $roster=Get-LiveLaneManifestRoster $RepositoryRoot $CandidateSha $tree $declaration['manifest']
    $candidate=@{laneId=$Lane;sha=$CandidateSha;repository=$binding['repository'];ref=$binding['ref'];host=$binding['host'];account=$binding['account']
        nonce=$binding['nonce'];fingerprint=$fingerprint}
    $state=Get-LiveLaneState -Candidate $candidate -History $paths -Roster $roster -RepositoryRoot $RepositoryRoot -ReusePolicy $ReusePolicy `
        -NowUtc (ConvertTo-LiveLaneUtc $binding['nowUtc']) -Probes @($binding['probes']) -Revocations @($binding['revocations'])
    $use=$null
    if ($UseRecord -and $state.state -ceq 'SATISFIED') { $use=New-LiveLaneUseRecord -State $state -Path $UseRecord -NowUtc (ConvertTo-LiveLaneUtc $binding['nowUtc']) }
    return [ordered]@{format='antiphon-live-lane-status-v1';laneId=$Lane;candidateSha=$CandidateSha;state=$state.state;reasons=$state.reasons
        effectiveMode='Full';activation='shadow-only';gateCredit=$false;executionEvidence=$false
        localCompatibility=$state.localCompatibility;reuseEligible=$state.reuseEligible;policy=$ReusePolicy;selected=$state.selected;lastGreen=$state.lastGreen
        useRecord=$(if ($use) { [ordered]@{receiptDigest=$use.receiptDigest;executedNow=$use.executedNow} })
        fingerprint=[ordered]@{status=$fingerprint.status;digest=$fingerprint.digest;components=$fingerprint.components
            unknownReasons=$fingerprint.unknownReasons;observations=$fingerprint.observations;environment=$fingerprint['environment']}}
}
