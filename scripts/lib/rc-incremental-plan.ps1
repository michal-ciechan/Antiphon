#requires -Version 7.0
# CARD-1836 S5c. Diagnostic only. No scheduler, activation or closure caller.
. (Join-Path $PSScriptRoot 'rc-admission.ps1')

function ConvertTo-RcPlanCanonicalValue {
    param($Value)
    if ($Value -is [Collections.IDictionary]) {
        $ordered=[ordered]@{}
        foreach ($key in (Get-TestSelectionSorted @($Value.Keys))) { $ordered[$key]=ConvertTo-RcPlanCanonicalValue $Value[$key] }
        return $ordered
    }
    if ($Value -is [array] -or $Value -is [Collections.IList]) {
        $items=@(foreach ($item in $Value) { ConvertTo-RcPlanCanonicalValue $item })
        return ,$items
    }
    return $Value
}
function Get-RcPlanValueDigest {
    param($Value)
    $text=ConvertTo-Json -InputObject (ConvertTo-RcPlanCanonicalValue $Value) -Depth 100 -Compress
    $bytes=[Text.UTF8Encoding]::new($false,$true).GetBytes(('antiphon-rc-plan-value-v1'+"`n"+$text))
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}
function Get-RcObligationKey {
    param($Row)
    foreach ($field in @('suite','className','method','caseKey')) { Assert-RcDigestText $Row[$field] ('obligation-'+$field) }
    # JSON tuples preserve boundaries even when identity components contain separators.
    return (ConvertTo-Json -InputObject @($Row.suite,$Row.className,$Row.method,$Row.caseKey) -Compress)
}
function Copy-RcPlanOrigin {
    param($Origin,[string]$Suite)
    Assert-TestSelectionKeys $Origin @('suite','envelopeDigest','runId','sourceSha','chunkId','wallSeconds')
    foreach ($field in @('suite','runId','chunkId')) { Assert-RcDigestText $Origin[$field] ('origin-'+$field) }
    if ($Origin.suite -cne $Suite -or -not (Test-RcFullDigest $Origin.envelopeDigest) -or
        $Origin.sourceSha -cnotmatch '\A[0-9a-f]{40}\z' -or
        $Origin.wallSeconds -isnot [ValueType] -or $Origin.wallSeconds -is [bool] -or
        -not [double]::IsFinite([double]$Origin.wallSeconds) -or [double]$Origin.wallSeconds -lt 0) { throw 'invalid-origin' }
    return @{suite=$Suite;envelopeDigest=$Origin.envelopeDigest;runId=$Origin.runId;sourceSha=$Origin.sourceSha;
        chunkId=$Origin.chunkId;wallSeconds=[double]$Origin.wallSeconds}
}
function New-RcExactPartition {
    [CmdletBinding()]
    param([object[]]$Suites,[AllowEmptyCollection()][object[]]$Decisions=@(),[switch]$ForceExecution)
    $universe=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    $suiteNames=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $chunks=[Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    foreach ($suite in $Suites) {
        Assert-TestSelectionKeys $suite @('suite','adapter','cases')
        Assert-RcDigestText $suite.suite 'suite'
        if (-not $suiteNames.Add($suite.suite)) { throw 'duplicate-suite' }
        if ($suite.cases -isnot [array]) { throw 'missing-suite-discovery' }
        $fresh=$suite.adapter -cne 'tunit'
        $cases=@($suite.cases)
        if ($fresh) {
            if ($cases.Count) { throw 'fresh-suite-must-be-whole' }
            # No trustworthy result adapter: require fresh execution of the WHOLE suite.
            $cases=@(@{suite=$suite.suite;className='<suite>';method='Execute';caseKey='entire-suite';
                stable=$true;boundary='suite';chunkId=('suite-'+$suite.suite)})
        } elseif (-not $cases.Count) { throw 'empty-tunit-discovery' }
        foreach ($case in $cases) {
            if ($universe.Count -ge 1000000) { throw 'obligation-limit' }
            Assert-TestSelectionKeys $case @('suite','className','method','caseKey','stable','boundary','chunkId')
            $key=Get-RcObligationKey $case
            if ($case.suite -cne $suite.suite -or $case.stable -isnot [bool]) { throw 'invalid-suite-case' }
            foreach ($field in @('boundary','chunkId')) { Assert-RcDigestText $case[$field] $field }
            $chunkKey=ConvertTo-Json -InputObject @($case.suite,$case.chunkId) -Compress
            if ($chunks.ContainsKey($chunkKey) -and $chunks[$chunkKey] -cne $case.boundary) { throw 'mixed-boundary-chunk' }
            $chunks[$chunkKey]=$case.boundary
            $reasons=@()
            if ($fresh) { $reasons+= 'fresh-suite-adapter-required' }
            if (-not $case.stable) { $reasons+= 'ambiguous-case' }
            if ($case.boundary -ceq 'unassigned') { $reasons+= 'unassigned-boundary' }
            $row=@{key=$key;suite=$case.suite;className=$case.className;method=$case.method;caseKey=$case.caseKey;
                boundary=$case.boundary;chunkId=$case.chunkId;stable=$case.stable;fresh=$fresh;
                disposition='Dirty';reasons=$reasons;origin=$null}
            if (-not $universe.TryAdd($key,$row)) { throw 'duplicate-obligation' }
        }
    }
    if (-not $suiteNames.Count) { throw 'empty-suite-universe' }
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    if (-not $ForceExecution) {
        foreach ($decision in $Decisions) {
            Assert-TestSelectionKeys $decision @('suite','className','method','caseKey','disposition','reasons','origin')
            $key=Get-RcObligationKey $decision
            if (-not $seen.Add($key)) { throw 'duplicate-decision' }
            if (-not $universe.ContainsKey($key)) { throw 'extra-decision' }
            if ($decision.disposition -cnotin @('Dirty','Sample','Carry') -or $decision.reasons -isnot [array]) { throw 'invalid-decision' }
            foreach ($r in $decision.reasons) { Assert-RcDigestText $r 'reason' }
            $row=$universe[$key]
            if ($decision.disposition -cne 'Dirty') { $row.origin=Copy-RcPlanOrigin $decision.origin $row.suite }
            if (-not $row.reasons.Count) { $row.disposition=$decision.disposition }
            $row.reasons=@(Get-TestSelectionSorted @(@($row.reasons)+@($decision.reasons) | Select-Object -Unique))
        }
        foreach ($row in $universe.Values) {
            if (-not $row.fresh -and -not $seen.Contains($row.key)) { throw 'decision-universe-mismatch' }
        }
    } else {
        foreach ($row in $universe.Values) { $row.reasons=@(Get-TestSelectionSorted @($row.reasons+'forced-full')) }
    }
    # Fixed point: whole methods and packed chunks launch together. A carry that
    # will actually run is removed from savings, including transitive expansion.
    do {
        $changed=$false
        foreach ($groupBy in @('method','chunk')) {
            $groups=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
            foreach ($row in $universe.Values) {
                $tuple=if ($groupBy -ceq 'method') { @($row.suite,$row.className,$row.method) } else { @($row.suite,$row.chunkId) }
                $group=ConvertTo-Json -InputObject $tuple -Compress
                if (-not $groups.ContainsKey($group)) { $groups.Add($group,[Collections.Generic.List[object]]::new()) }
                $groups[$group].Add($row)
            }
            foreach ($group in $groups.Values) {
                $kind=if (@($group | Where-Object disposition -CEQ 'Dirty').Count) {'Dirty'}
                    elseif (@($group | Where-Object disposition -CEQ 'Sample').Count) {'Sample'} else {'Carry'}
                foreach ($row in $group) {
                    if ($row.disposition -cne $kind) {
                        $row.disposition=$kind; $row.reasons=@(Get-TestSelectionSorted @($row.reasons+('whole-'+$groupBy+'-executes')))
                        $changed=$true
                    }
                }
            }
        }
    } while ($changed)
    $required=@(foreach ($key in (Get-TestSelectionSorted @($universe.Keys))) { $universe[$key] })
    $identityRows=@(foreach ($row in $required) { @($row.suite,$row.className,$row.method,$row.caseKey,$row.boundary,$row.chunkId,[string]$row.stable,[string]$row.fresh) })
    $partition=@{required=$required;dirty=@($required | Where-Object disposition -CEQ 'Dirty');
        sampled=@($required | Where-Object disposition -CEQ 'Sample');carried=@($required | Where-Object disposition -CEQ 'Carry');
        launch=@($required | Where-Object disposition -CNE 'Carry');universeDigest=(Get-RcPlanValueDigest $identityRows)}
    if ($partition.dirty.Count+$partition.sampled.Count+$partition.carried.Count -ne $required.Count) { throw 'partition-not-exact' }
    $partition.digest=Get-RcPlanValueDigest $partition
    return $partition
}
function Get-RcPartitionStatus {
    param($Partition,[AllowEmptyCollection()][object[]]$Results=@())
    $launch=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($row in $Partition.launch) { [void]$launch.Add($row.key) }
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $outcomes=@{Passed=0;Failed=0;Interrupted=0;Unproven=0;NotRun=0;ExpectedSkip=0;UnexpectedSkip=0}
    foreach ($result in $Results) {
        Assert-TestSelectionKeys $result @('key','outcome')
        if (-not $launch.Contains($result.key)) { throw 'result-outside-launch' }
        if (-not $seen.Add($result.key)) { throw 'duplicate-result' }
        if ($result.outcome -cnotin @('Passed','Failed','Interrupted','Unproven','NotRun','ExpectedSkip','UnexpectedSkip')) { throw 'invalid-result-outcome' }
        $outcomes[$result.outcome]++
    }
    $origins=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach ($row in $Partition.carried) {
        $origin=Copy-RcPlanOrigin $row.origin $row.suite
        $key=ConvertTo-Json -InputObject @($origin.envelopeDigest,$origin.runId,$origin.sourceSha,$origin.suite,$origin.chunkId) -Compress
        if ($origins.ContainsKey($key) -and $origins[$key].wallSeconds -ne $origin.wallSeconds) { throw 'origin-wall-conflict' }
        $origins[$key]=$origin
    }
    $wall=0.0
    foreach ($origin in $origins.Values) { $wall+=$origin.wallSeconds }
    return @{planned=@($Partition.required).Count;dirty=@($Partition.dirty).Count;sampled=@($Partition.sampled).Count;
        carried=@($Partition.carried).Count;executed=($outcomes.Passed+$outcomes.Failed+$outcomes.ExpectedSkip+$outcomes.UnexpectedSkip);
        passed=$outcomes.Passed;failed=($outcomes.Failed+$outcomes.UnexpectedSkip);interrupted=$outcomes.Interrupted;
        unproven=$outcomes.Unproven;notRun=($launch.Count-$seen.Count+$outcomes.NotRun);expectedSkip=$outcomes.ExpectedSkip;
        uniqueOriginChunks=$origins.Count;origins=@(foreach ($key in (Get-TestSelectionSorted @($origins.Keys))) { $origins[$key] });
        historicalPotentialWallSeconds=$wall}
}
function New-RcIncrementalPlan {
    [CmdletBinding()]
    param($Request,[string]$RepositoryRoot,[string]$ExpectedSha)
    Assert-TestSelectionKeys $Request @('format','candidateSha','treeId','platform','rcLine','suites','decisions','bindings','admissions',
        'exclusionDigest','contractDigest','boundaryMapDigest','historyDigest','sampleSeed','sampleBudget')
    if ($Request.format -cne 'antiphon-rc-plan-request-v1' -or $ExpectedSha -cnotmatch '\A[0-9a-f]{40}\z' -or
        $Request.candidateSha -cne $ExpectedSha -or
        (Invoke-TestSelectionGit $RepositoryRoot @('rev-parse','--verify',($ExpectedSha+'^{commit}'))).Stdout.Trim() -cne $ExpectedSha -or
        (Invoke-TestSelectionGit $RepositoryRoot @('rev-parse','HEAD')).Stdout.Trim() -cne $ExpectedSha -or
        (Invoke-TestSelectionGit $RepositoryRoot @('rev-parse',($ExpectedSha+'^{tree}'))).Stdout.Trim() -cne $Request.treeId) { throw 'candidate-binding-mismatch' }
    if ($Request.platform -cnotin @('windows','linux') -or $Request.rcLine -cnotmatch '\Arc/\d{4}-\d{2}-\d{2}\z') { throw 'invalid-plan-context' }
    foreach ($field in @('exclusionDigest','contractDigest','boundaryMapDigest','historyDigest')) {
        if (-not (Test-RcFullDigest $Request[$field])) { throw ('missing-plan-binding:'+ $field) }
    }
    Assert-RcDigestText $Request.sampleSeed 'sample-seed'
    if (($Request.sampleBudget -isnot [int] -and $Request.sampleBudget -isnot [long]) -or $Request.sampleBudget -lt 0) { throw 'invalid-sample-budget' }
    $reasons=[Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
    # This is deliberately not an activation switch. S5b's complete graph is still
    # unavailable and S6/S7 have not connected launch/closure authority.
    [void]$reasons.Add('activation-disabled')
    foreach ($r in (Get-RcAdmissionAlgorithmCoverage).reasons) { [void]$reasons.Add($r) }
    try { $proposal=New-RcExactPartition $Request.suites $Request.decisions }
    catch {
        [void]$reasons.Add('proposal-invalid:'+ $_.Exception.Message)
        $proposal=New-RcExactPartition $Request.suites -ForceExecution
    }
    Assert-TestSelectionKeys $Request.bindings @('input','output')
    foreach ($kind in @('input','output')) {
        try {
            $binding=$Request.bindings[$kind]
            Assert-TestSelectionKeys $binding @('path','digest')
            $receipt=Read-RcAdmissionJson $binding.path $binding.digest
            Assert-TestSelectionKeys $receipt @('kind','candidateSha','treeId','universeDigest')
            if ($receipt.kind -cne $kind -or $receipt.candidateSha -cne $ExpectedSha -or $receipt.treeId -cne $Request.treeId -or
                $receipt.universeDigest -cne $proposal.universeDigest) { throw 'receipt-binding-mismatch' }
        } catch { [void]$reasons.Add('receipt-unproven:'+ $kind) }
    }
    if ($Request.admissions -isnot [array] -or -not $Request.admissions.Count) { [void]$reasons.Add('baseline-absent') }
    foreach ($entry in $Request.admissions) {
        try {
            Assert-TestSelectionKeys $entry @('path','digest','evidenceRoot','sourceSha','runId','manifest','chunk','context')
            $admission=Read-RcAdmission -Path $entry.path -ExpectedDigest $entry.digest -EvidenceRoot $entry.evidenceRoot -RepositoryRoot $RepositoryRoot -ExpectedSha $entry.sourceSha -ExpectedRunId $entry.runId -CurrentSha $ExpectedSha -CurrentManifest $entry.manifest -Chunk $entry.chunk -CurrentContext $entry.context
            foreach ($r in $admission.reasons) { [void]$reasons.Add($r) }
            if (-not $admission.admitted) { [void]$reasons.Add('baseline-unproven') }
        } catch { [void]$reasons.Add('baseline-unproven') }
    }
    $effective=New-RcExactPartition $Request.suites -ForceExecution
    $plan=@{format='antiphon-rc-incremental-plan-v1';candidateSha=$ExpectedSha;treeId=$Request.treeId;platform=$Request.platform;
        rcLine=$Request.rcLine;request=$Request;proposalPartition=$proposal;partition=$effective;forcedReasons=@($reasons);
        effectiveMode='Full';coverageScope=$(if ($Request.platform -ceq 'linux') {'PlatformDiagnostic'} else {'IncrementalDiagnostic'});
        coverageComplete=$false;gateCredit=$false;bindingState='diagnostic-only';sampleSeed=$Request.sampleSeed;sampleBudget=$Request.sampleBudget}
    $plan.digest=Get-RcPlanValueDigest $plan
    return $plan
}
function Assert-RcPlanPath {
    param([string]$Path,[switch]$Output)
    $full=[IO.Path]::GetFullPath($Path)
    if ($Output) {
        $checkout=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')).TrimEnd([IO.Path]::DirectorySeparatorChar)
        if ($full.Equals($checkout,[StringComparison]::OrdinalIgnoreCase) -or $full.StartsWith(($checkout+[IO.Path]::DirectorySeparatorChar),[StringComparison]::OrdinalIgnoreCase)) { throw 'plan-output-inside-checkout' }
    }
    $current=$full
    for ($depth=0; $current; $depth++) {
        if ($depth -ge 128) { throw 'plan-path-depth' }
        if (([IO.File]::Exists($current) -or [IO.Directory]::Exists($current)) -and
            ([IO.File]::GetAttributes($current) -band [IO.FileAttributes]::ReparsePoint)) { throw 'plan-linked-path' }
        $current=[IO.Path]::GetDirectoryName($current)
    }
    return $full
}
function Write-RcPlanFileOnce {
    param([string]$Path,$Value)
    $full=Assert-RcPlanPath $Path -Output
    if ([IO.File]::Exists($full)) { throw 'write-once' }
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($full))
    $bytes=[Text.UTF8Encoding]::new($false,$true).GetBytes((ConvertTo-Json -InputObject (ConvertTo-RcPlanCanonicalValue $Value) -Depth 100 -Compress))
    if ($bytes.Length -gt 16777216) { throw 'plan-file-limit' }
    $file=[IO.File]::Open($full,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try { $file.Write($bytes); $file.Flush($true) } finally { $file.Dispose() }
    $digest=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
    [void](Read-RcAdmissionJson $full $digest)
    return $digest
}
function Assert-RcIncrementalPlan {
    param($Plan,[string]$ExpectedDigest)
    if ($Plan.format -cne 'antiphon-rc-incremental-plan-v1' -or -not (Test-RcFullDigest $ExpectedDigest) -or $Plan.digest -cne $ExpectedDigest) { throw 'plan-digest-mismatch' }
    $body=@{}; foreach ($key in $Plan.Keys) { if ($key -cne 'digest') { $body[$key]=$Plan[$key] } }
    if ((Get-RcPlanValueDigest $body) -cne $ExpectedDigest) { throw 'plan-digest-mismatch' }
    $effective=New-RcExactPartition $Plan.request.suites -ForceExecution
    if ($Plan.effectiveMode -cne 'Full' -or $Plan.coverageComplete -isnot [bool] -or $Plan.coverageComplete -or
        $Plan.gateCredit -isnot [bool] -or $Plan.gateCredit -or (Get-RcPlanValueDigest $Plan.partition) -cne (Get-RcPlanValueDigest $effective)) { throw 'plan-authority-invalid' }
}
function Write-RcIncrementalPlan {
    param([string]$Path,$Plan)
    Assert-RcIncrementalPlan $Plan $Plan.digest
    [void](Write-RcPlanFileOnce $Path $Plan)
}
function Read-RcStoredPlan {
    param([string]$Path,[string]$ExpectedDigest)
    $full=Assert-RcPlanPath $Path
    $plan=Read-RcAdmissionJson $full (Get-RcAdmissionFileDigest $full)
    Assert-RcIncrementalPlan $plan $ExpectedDigest
    return $plan
}
function Read-RcPlanPointer {
    param([string]$Root,[string]$ExpectedDigest)
    $path=Assert-RcPlanPath (Join-Path $Root 'index.json')
    $pointer=Read-RcAdmissionJson $path (Get-RcAdmissionFileDigest $path)
    Assert-TestSelectionKeys $pointer @('format','planDigest','sequence','path','sha256')
    if ($pointer.format -cne 'antiphon-rc-plan-index-v1' -or $pointer.planDigest -cne $ExpectedDigest -or
        ($pointer.sequence -isnot [int] -and $pointer.sequence -isnot [long]) -or $pointer.sequence -le 0 -or
        $pointer.path -cne ('revisions/'+$pointer.sequence+'/revision.json') -or -not (Test-RcFullDigest $pointer.sha256)) { throw 'invalid-revision-pointer' }
    return $pointer
}
function Write-RcPlanRevision {
    [CmdletBinding()]
    param([string]$EvidenceRoot,[string]$PlanPath,[string]$ExpectedPlanDigest,[AllowEmptyCollection()][object[]]$Results=@())
    $root=Assert-RcPlanPath $EvidenceRoot -Output
    [void][IO.Directory]::CreateDirectory($root)
    $lockPath=Assert-RcPlanPath (Join-Path $root '.writer.lock') -Output
    try { $lock=[IO.File]::Open($lockPath,[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None) }
    catch { throw 'writer-busy' }
    try {
        $plan=Read-RcStoredPlan $PlanPath $ExpectedPlanDigest
        [void](Get-RcPartitionStatus $plan.partition $Results)
        $storedPlan=Join-Path $root 'plan.json'
        if ([IO.File]::Exists($storedPlan)) { [void](Read-RcStoredPlan $storedPlan $ExpectedPlanDigest) }
        else { Write-RcIncrementalPlan $storedPlan $plan }
        $sequence=1L
        if ([IO.File]::Exists((Join-Path $root 'index.json'))) {
            # Validate the previous revision, not only the index, before extending it.
            $previous=Read-RcPlanStatus $root $ExpectedPlanDigest
            $sequence=$previous.sequence+1L
        }
        $relative='revisions/'+$sequence+'/revision.json'
        if ([IO.Directory]::Exists((Join-Path $root ('revisions/'+$sequence)))) { throw 'revision-already-exists' }
        $revision=@{format='antiphon-rc-plan-revision-v1';planDigest=$ExpectedPlanDigest;sequence=$sequence;state='open';results=$Results}
        $digest=Write-RcPlanFileOnce (Join-Path $root $relative) $revision
        $pointer=@{format='antiphon-rc-plan-index-v1';planDigest=$ExpectedPlanDigest;sequence=$sequence;path=$relative;sha256=$digest}
        $temporary=Join-Path $root ('index-'+[guid]::NewGuid().ToString('N')+'.tmp')
        [void](Write-RcPlanFileOnce $temporary $pointer)
        [void](Assert-RcPlanPath (Join-Path $root 'index.json') -Output)
        [IO.File]::Move($temporary,(Join-Path $root 'index.json'),$true)
        [void](Read-RcPlanStatus $root $ExpectedPlanDigest)
        return $pointer
    } finally { $lock.Dispose() }
}
function Read-RcPlanStatus {
    [CmdletBinding()]
    param([string]$EvidenceRoot,[string]$ExpectedPlanDigest)
    # Deliberately file-only: no Git/process/HTTP/build/discovery calls.
    $root=Assert-RcPlanPath $EvidenceRoot
    $plan=Read-RcStoredPlan (Join-Path $root 'plan.json') $ExpectedPlanDigest
    $pointer=Read-RcPlanPointer $root $ExpectedPlanDigest
    $path=Assert-RcPlanPath (Join-Path $root $pointer.path)
    $revision=Read-RcAdmissionJson $path $pointer.sha256
    Assert-TestSelectionKeys $revision @('format','planDigest','sequence','state','results')
    if ($revision.format -cne 'antiphon-rc-plan-revision-v1' -or $revision.planDigest -cne $ExpectedPlanDigest -or
        $revision.sequence -ne $pointer.sequence -or $revision.state -cne 'open' -or $revision.results -isnot [array]) { throw 'revision-not-open-or-bound' }
    $status=Get-RcPartitionStatus $plan.partition $revision.results
    $status.format='antiphon-rc-plan-status-v1'; $status.planDigest=$ExpectedPlanDigest; $status.sequence=$pointer.sequence
    $status.candidateSha=$plan.candidateSha; $status.platform=$plan.platform; $status.effectiveMode=$plan.effectiveMode
    $status.coverageScope=$plan.coverageScope; $status.coverageComplete=$false; $status.gateCredit=$false
    $status.forcedReasons=$plan.forcedReasons; $status.proposed=Get-RcPartitionStatus $plan.proposalPartition @()
    $reasonCounts=@{}
    foreach ($row in $plan.partition.required) { foreach ($reason in $row.reasons) { if (-not $reasonCounts.ContainsKey($reason)) { $reasonCounts[$reason]=0 }; $reasonCounts[$reason]++ } }
    $status.reasonCounts=$reasonCounts
    return $status
}
